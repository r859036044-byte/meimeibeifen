using Godot;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// TrainingRoomRuntime — 呼吸微動畫、痙攣抖動（TriggerClimax/Spasms）、噴水效果（簡化）。
/// 痙攣的節奏（6 波：小→小→最大→餘震 x3）忠實移植自 Unity 版 SpasmsCoroutine，
/// 但改用 _Process 逐幀狀態機（_spasmsWaveIdx/_spasmsWaveTimer）取代協程，
/// 且不驅動 Spine 尾巴速度（Unity 原版 BuildSpineTail 從未被呼叫，見檔案頂端說明）。
/// 噴水效果簡化為由程式產生的水滴 TextureRect 淡入淡出＋簡單拋物線位移，
/// 取代 Unity 原版逐滴計算之網格水花/水漬合併系統。
/// </summary>
public partial class TrainingRoomRuntime : Node
{
    void UpdateClimaxHud()
    {
        if (_climaxBar != null) _climaxBar.Value = ClimaxLevel;
        if (_climaxValueLabel != null)
            _climaxValueLabel.Text = $"{Mathf.RoundToInt(ClimaxLevel)} / 100" + (ClimaxGainLocked ? "（鎖定）" : "");
        if (_staminaBar != null) _staminaBar.Value = Stamina;

        if (_moodBar != null) { _moodBar.Value = Mood; _moodLabel.Text = $"{Mathf.RoundToInt(Mood)}"; }
        if (_comfortMouthBar != null) { _comfortMouthBar.Value = ComfortMouth; _comfortMouthLabel.Text = $"{Mathf.RoundToInt(ComfortMouth)}"; }
        if (_comfortChestBar != null) { _comfortChestBar.Value = ComfortChest; _comfortChestLabel.Text = $"{Mathf.RoundToInt(ComfortChest)}"; }
        if (_comfortButtBar != null) { _comfortButtBar.Value = ComfortButt; _comfortButtLabel.Text = $"{Mathf.RoundToInt(ComfortButt)}"; }
        if (_comfortHoleBar != null) { _comfortHoleBar.Value = ComfortHole; _comfortHoleLabel.Text = $"{Mathf.RoundToInt(ComfortHole)}"; }

        UpdateHotspotFlash((float)GetProcessDeltaTime());
    }

    // ── 呼吸效果：分成幾個獨立子系統，各自負責不同部位（對應 Unity 原本的
    //   ApplyHairLayerSettings / ApplyArmLayerSettings / ApplyLegLayerSettings / 胸部 X-Y 晃動 / 身體縮放脈動），
    //   剩下沒被這些系統接管的圖層才走最簡單的「整體上下位移」。────────
    static readonly HashSet<string> _breathLowerSet = new()
    {
        "腿/腿1","腿/腿2","腿/腳1","腿/腳2","腿/足1","腿/足2",
        "m腿/腿底","m腿/腿1","m腿/腿2","m腿/腳1","m腿/腳2","m腿/足1","m腿/足2",
        "男人腿1","男人腿2","腿/汗1","m腿/小穴","m腿/小穴細節","m腿/肛門","腿/小穴","m腿/內褲",
        "內褲",   // 內褲跟小穴同步（只微量呼吸），不然內褲會比小穴多晃
        "開1","洞","開上","插入",   // 插入疊層要跟小穴同步呼吸，不然會跟小穴錯開
    };
    static readonly HashSet<string> _breathChestSet = new() { "胸1", "胸2", "奶頭1", "奶頭2" };

    // 眼睛相關圖層：已經各自交給 EyeCloseController（眼皮/睫毛/眼角/下睫毛/眼白/眼球/眼淚）
    // 和 EyeFollowController（眼球1/2 滑鼠追蹤）逐幀直接控制 Position/Size。
    // 這裡如果不排除，下面「其餘一般圖層」的通用呼吸迴圈會每一幀都用舊的 _layerBasePos
    // 覆蓋掉這些控制器剛設好的 Position，等於兩套系統同時搶著改同一個節點的位置——
    // 這正是使用者回報「眨眼時眼球有一瞬間跑到頭上位置」的真正原因（位置被呼吸系統
    // 用另一組座標蓋掉，不是遮罩本身的問題）。
    static readonly HashSet<string> _eyeControlledSet = new()
    {
        "表情/眼白", "表情/眼白2",
        "表情/眼球1", "表情/眼球2",
        "表情/眼皮", "表情/眼皮2",
        "表情/睫毛1", "表情/睫毛2",
        "表情/下睫毛1", "表情/下睫毛2",
        "眼角1", "眼角2",
        "表情/眼淚1", "表情/眼淚2", "表情/眼淚3", "表情/眼淚4",
    };

    // 頭部相關圖層（臉本體＋眉毛/鼻子/嘴巴/腮紅/嘴形）：使用者要求呼吸時頭比身體多動一點點，
    // 這些圖層在下面的通用呼吸迴圈裡改用 HeadBreathRatio（>1）而不是身體的 1.0。
    // 頭髮已經有自己的一套呼吸/飄動系統（見 UpdateHairLayers），這裡不重複列入。
    static readonly HashSet<string> _headLayerSet = new()
    {
        "臉的陰影", "臉",
        "眉毛1", "眉毛2",
        "表情/鼻子", "表情/嘴巴", "表情/腮紅",
        "笑", "笑2", "小嘴", "吐舌", "張嘴1", "張嘴2", "斜動嘴", "抖動嘴", "小嘴吐舌", "小嘴巴",
    };

    // 使用者要求：M 腿出現時，身體以上的所有物件整體往上移約 3px（螢幕 px，跟著 M 腿淡入時間漸進）。
    [Export] public float MLegUpperLiftPx = 10f;
    /// <summary>身體底部（從肚臍往下漸增）在整體上提之外額外再上升的 px，最底部最多。</summary>
    [Export] public float MLegBodyBottomExtraLiftPx = 5f;
    float _mLegLiftCur = 0f;
    // _mLegLiftCur 是「螢幕 px」；圖層位置是畫布 px（角色整體縮到約 0.315），要除以 CharScreenScale() 才是真的上提這麼多螢幕 px
    // 頭部相關物件（臉/表情/眼睛/頭髮）只上提較小的量（使用者要求 1~2px），其餘身體以上用 MLegUpperLiftPx
    [Export] public float MLegHeadLiftPx = 1.5f;
    [Export] public float MLegChestLiftPx = 1.5f;   // 胸/奶頭（使用者要求胸部以上都是 0.8）
    [Export] public float MLegBodyNeckV = 0.25f;     // 身體上提漸層起點（占畫布高 0~1）：移到胸部下緣，脖子/肩膀/胸都維持 0.8，不被漸層拉扯
    [Export] public float MLegBodyNavelV = 0.40f;    // 漸層終點（約肚臍），從這裡往下上提達到最大
    Vector2 HeadLift => new Vector2(0f, -(MLegUpperLiftPx > 0.001f ? _mLegLiftCur / MLegUpperLiftPx * MLegHeadLiftPx : 0f) / Mathf.Max(0.01f, CharScreenScale()));
    Vector2 ChestLift => new Vector2(0f, -(MLegUpperLiftPx > 0.001f ? _mLegLiftCur / MLegUpperLiftPx * MLegChestLiftPx : 0f) / Mathf.Max(0.01f, CharScreenScale()));
    // 手臂/手（在胸部高度附近）跟胸一樣用 1.5
    Vector2 ArmLift => ChestLift;
    Vector2 UpperLift => new Vector2(0f, -_mLegLiftCur / Mathf.Max(0.01f, CharScreenScale()));
    // 不屬於「身體以上」的下半身／插入相關圖層（通用呼吸迴圈裡不套用上提）
    static readonly HashSet<string> _noLiftSet = new()
    {
        "雞雞", "精液", "男人肚子", "肚子", "表情/小穴水", "表情/高潮水",
    };

    void UpdateBreathing(float dt)
    {
        if (!BreathEnabled || _charRoot == null) return;
        _mLegLiftCur = Mathf.MoveToward(_mLegLiftCur, _mLegOpen ? MLegUpperLiftPx : 0f,
            dt * MLegUpperLiftPx / Mathf.Max(0.01f, MLegFadeDuration));
        if (_mLegDragStretchMaterials.TryGetValue("身體", out var bodyLiftMat) && bodyLiftMat != null)
        {
            float prog = MLegUpperLiftPx > 0.001f ? _mLegLiftCur / MLegUpperLiftPx : 0f;
            // 螢幕 px → 畫布 px → UV（畫布高 3200）
            float px2uv = 1f / Mathf.Max(0.01f, CharScreenScale()) / 3200f;
            // 身體圖層本體已經用頭部的量（MLegHeadLiftPx）整體上提；脖子→肚臍之間再漸增到 MLegUpperLiftPx，
            // 肚臍以下再多升 MLegBodyBottomExtraLiftPx。
            bodyLiftMat.SetShaderParameter("lift_top_v", prog * Mathf.Max(0f, MLegUpperLiftPx - MLegHeadLiftPx) * px2uv);
            bodyLiftMat.SetShaderParameter("lift_bottom_v", prog * MLegBodyBottomExtraLiftPx * px2uv);
            bodyLiftMat.SetShaderParameter("lift_neck_v", MLegBodyNeckV);
            bodyLiftMat.SetShaderParameter("lift_navel_v", MLegBodyNavelV);
        }
        _breathTime += dt * BreathSpeed;
        float sin = Mathf.Sin(_breathTime * Mathf.Pi * 2f);
        float cos = Mathf.Cos(_breathTime * Mathf.Pi * 2f);
        float globalY = sin * BreathAmplitude;
        _lowerBreathY = -globalY * BreathLowerRatio;
        if (_fluidLayer != null || _bloodLayer != null) UpdateFluidLayer();

        // 身體：吸氣時輕微膨脹/拉伸（只往外擴，不往內縮）
        if (Layers.TryGetValue("身體", out var bodyCtrl) && bodyCtrl != null)
        {
            bodyCtrl.Scale = new Vector2(
                1f + Mathf.Max(0f, sin) * BreathBodyExpand,
                1f + sin * BreathBodyStretch);
        }

        // 胸部：吸氣時往外擴＋放大一點，吐氣時完全收回原位——改用跟身體 BreathBodyExpand
        // 同一種「只吸氣才動」的 inhale=Max(0,sin) 邏輯（原本用 cos 做左右晃動，相位跟
        // 身體/頭髮的 sin 呼吸週期對不上，使用者反應「胸部呼吸跟其他物件聯動不太好」
        // 就是這個相位不同步造成的），這樣胸部會跟身體/頭在同一時間點一起脹一起收。
        float inhale = Mathf.Max(0f, sin);
        foreach (var name in _breathChestSet)
        {
            if (!Layers.TryGetValue(name, out var c) || c == null) continue;
            if (!_layerBasePos.TryGetValue(name, out var bp)) continue;
            float xSign = (name == "胸1" || name == "奶頭1") ? 1f : -1f;
            var p = bp;
            p.X += xSign * inhale * BreathChestX;
            p.Y -= globalY + sin * BreathChestY;
            p += BodyShake + ChestLift;
            // 摸胸觸摸動畫（AnimateHand 移植）疊加的額外位移——只有「胸1」「胸2」自己
            // 在跑動畫時才會出現在字典裡，奶頭1/2 沒有直接對應的動畫但仍是這個迴圈的一部分，
            // TryGetValue 查不到就是 0，不影響奶頭本身的呼吸位移。
            if (TouchAnimOffset.TryGetValue(name, out var touchOff)) p += touchOff;
            c.Position = p;

            // 只有「胸1」「胸2」本體做外擴縮放（BreastPivotOverrides 已經把它們的軸心設在
            // 貼圖實際內容中心，縮放才會看起來像「胸部變大」而不是整張畫布跟著跑位）。
            // 奶頭1/2 沒有專屬軸心，維持只跟位置、不做縮放。
            if (name == "胸1" || name == "胸2")
            {
                c.Scale = new Vector2(1f + inhale * BreastExpandScale, 1f + inhale * BreastExpandScale);
                // 使用者要求吸氣時胸部除了變大，也要整個再往上提一點點，用同一個 inhale
                // 權重疊加在剛剛算好的 p.Y 上面（inhale 只在吸氣時 >0，吐氣時完全歸零）。
                var pos2 = c.Position;
                pos2.Y -= inhale * BreastExpandMoveUp;
                c.Position = pos2;
            }
        }

        UpdateHairLayers(dt);
        UpdateArmLayerDetails(globalY);
        UpdateLegLayerDetails(globalY, cos);

        // 眼睛相關圖層（眼白/眼球/眼皮/睫毛/眼角/眼淚）已經完全交給 EyeCloseController／
        // EyeFollowController 逐幀控制位置，不會經過下面的通用迴圈（見 _eyeControlledSet 排除）。
        // 使用者要求表情也要跟著呼吸有一點自然的起伏，這裡改成把「頭部呼吸位移量」直接
        // 傳給這兩個控制器，讓它們在各自算完閉眼/追蹤位置後，自己疊加這一點點 Y 偏移，
        // 不會再跟通用呼吸迴圈搶位置（那正是之前「眨眼時眼球跳到頭上」的成因）。
        float headBreathOffsetY = -globalY * HeadBreathRatio;
        if (EyeCloseCtrl != null) { EyeCloseCtrl.BreathOffsetY = headBreathOffsetY; EyeCloseCtrl.ShakeOffset = BodyShake + HeadLift; }
        if (EyeFollowCtrl != null) { EyeFollowCtrl.BreathOffsetY = headBreathOffsetY; EyeFollowCtrl.ShakeOffset = BodyShake + HeadLift; }

        // 其餘一般圖層：跟著上半身整體上下移動；頭部圖層用 HeadBreathRatio（比身體多一點），
        // 下半身沒被 LegLayerSettings 接管的用 BreathLowerRatio 微量跟隨。
        foreach (var kv in Layers)
        {
            if (kv.Value == null || !_layerBasePos.TryGetValue(kv.Key, out var basePos)) continue;
            if (_breathChestSet.Contains(kv.Key)) continue;
            if (_hairLayerSet.Contains(kv.Key)) continue;
            if (_armLayerSet.Contains(kv.Key)) continue;
            if (_legLayerSet.Contains(kv.Key)) continue;
            if (_eyeControlledSet.Contains(kv.Key)) continue;
            float weight = _breathLowerSet.Contains(kv.Key) ? BreathLowerRatio
                         : _headLayerSet.Contains(kv.Key)  ? HeadBreathRatio
                         : 1f;
            var p = basePos;
            p.Y -= globalY * weight; // Godot Y 向下為正，呼吸上移用負號
            p += BodyShake;
            if (_headLayerSet.Contains(kv.Key) || kv.Key == "身體" || kv.Key == "衣服" || kv.Key == "衣服/左帶" || kv.Key == "衣服/右帶") p += HeadLift;   // 身體本體用小幅上提，其餘漸層由 shader 補
            else if (!_breathLowerSet.Contains(kv.Key) && !_noLiftSet.Contains(kv.Key)) p += UpperLift;   // M 腿時身體以上整體上提
            if (ExprLayerExtraOffset.TryGetValue(kv.Key, out var exprExtra)) p += exprExtra;
            if (JijiLayerExtraOffset.TryGetValue(kv.Key, out var jijiExtra)) p += jijiExtra;   // 開1/洞 跟著插入深度移動
            if (MLegThrustOffset.TryGetValue(kv.Key, out var thrustExtra)) p += thrustExtra;   // 插入推擠：小穴/肛門/洞/開1 往上
            kv.Value.Position = p;
        }
    }

    // ── 頭髮飄動：主驅動波 + 次要諧波 + 彈簧阻尼追蹤目標角度（忠實移植自 Unity ApplyHairLayerSettings）──
    void UpdateHairLayers(float dt)
    {
        float t = (float)Time.GetTicksMsec() / 1000f;
        for (int i = 0; i < HairLayerSettings.Length; i++)
        {
            var s = HairLayerSettings[i];
            if (!Layers.TryGetValue(s.LayerName, out var ctrl) || ctrl == null) continue;
            if (!_layerBasePos.TryGetValue(s.LayerName, out var basePos)) continue;
            if (!_layerBaseSizeDelta.TryGetValue(s.LayerName, out var baseSize)) continue;

            // 大部分髮層仍維持 BuildCharacter 預設的整張畫布正中心 pivot（Unity 原始 Pivot 數值是對著
            // 自動切割出的小裁切矩形算的，攤平圖套用會差幾十倍、直接飛出畫面，已實測confirmed）。
            // 但 _辮子1/_辮子2 是例外：已經在 BuildCharacter 用 _customAttachPivot（從實際裁切矩形反推的
            // 髮根附著點）建立時套好旋轉錨點並把補償量吃進 basePos，這裡同樣維持 Zero、不用再補償。
            Vector2 pivotComp = Vector2.Zero;
            float breathY = -(BreathAmplitude * Mathf.Sin(_breathTime * Mathf.Pi * 2f)) * s.BreathWeight; // Y 向下為正
            // 跟頭髮尾端擴張（下面 flareMat 那段）共用同一個「只吸氣才動」相位，
            // 兩條小辮子的左右位移要跟後髮1/前髮的擴張同步，看起來才像同一次呼吸動作。
            float inhale = Mathf.Max(0f, Mathf.Sin(_breathTime * Mathf.Pi * 2f));
            // 使用者要求：後髮擴散時，兩條小辮子各自往左右意思一下位移一點點就好
            // （後髮2 在左邊，往左＝負X；後髮3 在右邊，往右＝正X）。
            float braidExpandX = s.LayerName == "後髮2" ? -inhale * BraidExpandMoveX
                                : s.LayerName == "後髮3" ?  inhale * BraidExpandMoveX
                                : 0f;

            float animRot = 0f, animX = 0f;
            if (s.AnimEnabled)
            {
                float phaseRad = s.AnimPhase * Mathf.Pi * 2f;
                float primary  = Mathf.Sin(t * s.AnimSpeed * Mathf.Pi * 2f + phaseRad);
                float harmonic = Mathf.Sin(t * s.AnimSpeed * s.HarmonicFreq * Mathf.Pi * 2f + phaseRad + 1f) * s.HarmonicAmp;
                float target = Mathf.Lerp(s.AnimRotMin, s.AnimRotMax, (primary + 1f) * 0.5f)
                             + harmonic * (s.AnimRotMax - s.AnimRotMin) * 0.5f;

                float spring = (target - _hairRotCurrent[i]) * s.SpringForce;
                _hairRotVelocity[i] += spring * dt;
                _hairRotVelocity[i] *= Mathf.Pow(s.SpringDamp, dt * 60f);
                _hairRotCurrent[i]  += _hairRotVelocity[i] * dt;

                animRot = _hairRotCurrent[i];
                animX   = primary * s.AnimMoveX;
            }

            // 呼吸同步旋轉（給辮子等「呼吸時也要看得出在轉」的圖層用；跟身體吸/吐同一個相位，
            // 不是上面那條獨立飄動波，兩者疊加）。_辮子1/_辮子2 改用 Inspector 可調的
            // BraidBreathRotWeight1/2，方便跟錨點一起微調重影問題。
            float breathRotWeight = s.LayerName == "後髮3" ? BraidBreathRotWeight1
                                   : s.LayerName == "後髮2" ? BraidBreathRotWeight2
                                   : s.BreathRotWeight;
            if (breathRotWeight != 0f)
                animRot += Mathf.Sin(_breathTime * Mathf.Pi * 2f) * breathRotWeight;

            float scaleX = 1f, scaleY = 1f;
            if (s.AnimScaleX > 0f || s.AnimScaleY != 0f)
            {
                float phaseRad2 = s.AnimPhase * Mathf.Pi * 2f + Mathf.Pi * 0.5f;
                float wave = Mathf.Abs(Mathf.Sin(t * s.AnimSpeed * Mathf.Pi * 2f + phaseRad2));
                if (s.AnimScaleX > 0f) scaleX = 1f + wave * s.AnimScaleX;
                if (s.AnimScaleY != 0f) scaleY = 1f + wave * s.AnimScaleY;
            }

            // 左右頭髮微調（HairUserNudge，X 向右為正，跟 Unity 慣例一致，不用像 Y 一樣翻轉）
            ctrl.Position = basePos + pivotComp + new Vector2(animX + HairUserNudge(s.LayerName).X + braidExpandX, breathY) + BodyShake + HeadLift;
            float braidStatic = s.LayerName == "後髮3" ? BraidStaticRotDeg : s.LayerName == "後髮2" ? -BraidStaticRotDeg : 0f;   // 右邊辮子順時針、左邊逆時針，和後面的頭髮接起來，不要縮進去
            ctrl.RotationDegrees = -animRot + braidStatic; // Unity Z 正=逆時針，Godot 正=順時針 → 取負
            ctrl.Scale = new Vector2(scaleX, scaleY);

            // 頭髮尾端呼吸擴張：只在吸氣（sin>0）時往外推，跟身體膨脹（BreathBodyExpand）
            // 用同一個判斷方式，吐氣時完全歸零、不會往內縮。
            if (_hairFlareMaterials.TryGetValue(s.LayerName, out var flareMat) && flareMat != null)
            {
                flareMat.SetShaderParameter("expand_amount", inhale * HairBottomExpandAmount);
            }
        }
    }

    // ── 手臂/手：復刻 Unity ApplyArmLayerSettings ─────────────────────────
    //   • 每層以自己的 Pivot（換算到整張畫布）為軸心做 靜態旋轉 + 呼吸旋轉
    //   • 位移 = Offset + 呼吸 Y；手（FollowLayerName）再疊加手臂的累計位移與旋轉角
    //   • 同時計算「無呼吸靜止姿勢」寫給 ArmIK，拖動開始時從這個姿勢出發
    //   • ArmIK 拖動/回彈/凍結中的圖層交給 ArmIK.LateProcess
    //   globalY = sin(呼吸) * BreathAmplitude（Unity 慣例，Y 向上為正）
    void UpdateArmLayerDetails(float globalY)
    {
        static Vector2 ToU(Vector2 v) => new Vector2(v.X, -v.Y);
        float breathSin = Mathf.Sin(_breathTime * Mathf.Pi * 2f);

        if (ArmIK      != null) ArmIK.FrozenBreathY      = globalY * ArmBreathScale;
        if (RightArmIK != null) RightArmIK.FrozenBreathY = globalY * ArmBreathScale;

        _armLayerDeltaPos.Clear();     _armLayerDeltaRot.Clear();
        _armLayerRestDeltaPos.Clear(); _armLayerRestDeltaRot.Clear();

        foreach (var s in ArmLayerSettings)
        {
            if (!Layers.TryGetValue(s.LayerName, out var ctrl) || ctrl == null) continue;
            if (!_layerBasePos.TryGetValue(s.LayerName, out var basePos)) continue;

            Vector2 pivotPx = HasArmSpriteRect(s.LayerName) ? UnityPivotToLocal(s.LayerName, s.Pivot) : ctrl.Size * 0.5f;

            float globalMult = (s.LayerName == "手1" || s.LayerName == "手2") ? HandBreathScale
                             : (s.LayerName == "手臂1" || s.LayerName == "手臂2") ? ArmBreathScale : 1f;
            float breathY   = globalY * s.BreathWeight * globalMult;          // Unity Y 上正
            float flipSign  = s.FlipX ? -1f : 1f;
            float finalRot  = s.Rotation + breathSin * s.BreathRotation * flipSign;  // Unity 角度

            Vector2 followPos = Vector2.Zero, followPosR = Vector2.Zero;
            float   followRot = 0f,           followRotR = 0f;
            if (!string.IsNullOrEmpty(s.FollowLayerName))
            {
                _armLayerDeltaPos.TryGetValue(s.FollowLayerName, out followPos);
                _armLayerRestDeltaPos.TryGetValue(s.FollowLayerName, out followPosR);
                if (s.FollowRotation)
                {
                    _armLayerDeltaRot.TryGetValue(s.FollowLayerName, out followRot);
                    _armLayerRestDeltaRot.TryGetValue(s.FollowLayerName, out followRotR);
                }
            }

            Vector2 offset    = s.Offset + ArmUserNudge(s.LayerName);
            Vector2 delta     = ToU(offset + new Vector2(0f, breathY)) + followPos;   // Godot 座標
            Vector2 restDelta = ToU(offset) + followPosR;
            _armLayerRestDeltaPos[s.LayerName] = restDelta;
            _armLayerRestDeltaRot[s.LayerName] = s.Rotation + followRotR;

            // 這層屬於哪個 ArmIK、要加哪個 PosAdjust（Unity 慣例 → Godot）
            ArmIK owner = null; bool isArm = false; Vector2 adj = Vector2.Zero;
            foreach (var ik in new[] { ArmIK, RightArmIK })
            {
                if (ik == null) continue;
                if (s.LayerName == ik.ArmLayerName)  { owner = ik; isArm = true;  adj = ToU(ik.ArmPosAdjust);  break; }
                if (s.LayerName == ik.HandLayerName) { owner = ik; isArm = false; adj = ToU(ik.HandPosAdjust); break; }
            }

            // 靜止姿勢（無呼吸）→ ArmIK 拖動初始化用（Unity：DragArmRestRot = 本層 Rotation，不含 Follow）
            if (owner != null)
            {
                Vector2 restPos = basePos + restDelta + adj;
                float   restRot = -s.Rotation;
                if (isArm) { owner.DragArmRestPos  = restPos; owner.DragArmRestRot  = restRot; owner.DragArmRestPivot  = pivotPx; owner.DragArmRestValid  = true; }
                else       { owner.DragHandRestPos = restPos; owner.DragHandRestRot = restRot; owner.DragHandRestPivot = pivotPx; owner.DragHandRestValid = true; }
                if (owner.IsActive) continue;   // 拖動/回彈/凍結中：交給 ArmIK.LateProcess
            }

            Vector2 finalPos = basePos + delta + adj;
            ctrl.PivotOffset     = pivotPx;
            ctrl.Scale           = new Vector2(flipSign, 1f);
            ctrl.Position        = finalPos + BodyShake + ArmLift;   // 手臂/手：跟胸一樣的小幅上提
            ctrl.RotationDegrees = -(finalRot + followRot);

            _armLayerDeltaPos[s.LayerName] = finalPos - basePos;   // 含 PosAdjust（與 Unity 相同）
            _armLayerDeltaRot[s.LayerName] = finalRot + followRot;
        }
    }

    // ── 腿部：呼吸聯動（各自 Pivot + BreathWeightY/X/Phase）+ 原地循環搖擺（簡化：不含拖曳展開/M腿專屬位移）──
    void UpdateLegLayerDetails(float globalY, float breathCos)
    {
        float t = (float)Time.GetTicksMsec() / 1000f;
        foreach (var s in LegLayerSettings)
        {
            if (!Layers.TryGetValue(s.LayerName, out var ctrl) || ctrl == null) continue;
            if (!_layerBasePos.TryGetValue(s.LayerName, out var basePos)) continue;
            if (!_layerBaseSizeDelta.TryGetValue(s.LayerName, out var baseSize)) continue;

            // 同上：不套用 Unity 的 Pivot（那是對小裁切矩形算的，攤平圖用不了），維持預設中心 pivot。
            Vector2 pivotComp = Vector2.Zero;

            // 使用者要求：拖曳膝蓋熱點（開 M 腿手勢）過程中，對應的腿要有輕微旋轉當視覺回饋，
            // 旋轉軸心要在大腿根部（LegDragPivotOverride），不是預設整張畫布中心。這裡只有
            // 這條腿目前有非 0 的額外旋轉量時才切換 PivotOffset，其餘時間都是預設中心 pivot，
            // 不影響原本的呼吸/原地搖擺視覺（見 Fields.cs 的 LegDragExtraRotation 說明）。
            float dragExtraRot = LegDragExtraRotation.TryGetValue(s.LayerName, out var der) ? der : 0f;
            float thrustRot = MLegThrustRot.TryGetValue(s.LayerName, out var tr) ? tr : 0f;   // 插入推擠：M 腿往外張（Jiji.cs）
            if (Mathf.Abs(dragExtraRot) > 0.0001f && LegDragPivotOverride.TryGetValue(s.LayerName, out var pivotOverride))
                ctrl.PivotOffset = pivotOverride;
            else if (Mathf.Abs(thrustRot) > 0.0001f && MLegThrustPivot(s.LayerName, out var thrustPivot))
                ctrl.PivotOffset = thrustPivot;
            else
                ctrl.PivotOffset = ctrl.Size * 0.5f;

            float animRot = 0f;
            Vector2 animMove = Vector2.Zero;
            if (s.AnimEnabled)
            {
                float phaseRad = s.AnimPhase * Mathf.Pi * 2f;
                float aSin = Mathf.Sin(t * s.AnimSpeed * Mathf.Pi * 2f + phaseRad);
                float aCos = Mathf.Cos(t * s.AnimSpeed * Mathf.Pi * 2f + phaseRad);
                animRot  = Mathf.Lerp(s.AnimRotMin, s.AnimRotMax, (aSin + 1f) * 0.5f);
                animMove = new Vector2(aSin * s.AnimMove.X, aCos * s.AnimMove.Y);
            }

            float breathPhaseRad = s.BreathPhase * Mathf.Pi * 2f;
            float sinP = Mathf.Sin(_breathTime * Mathf.Pi * 2f + breathPhaseRad);
            float cosP = Mathf.Cos(_breathTime * Mathf.Pi * 2f + breathPhaseRad);
            float breathOffsetY = -(sinP * BreathAmplitude * s.BreathWeightY);
            float breathOffsetX = cosP * s.BreathWeightX;

            // 使用者要求：拖曳膝蓋熱點時，腿1/腿2/足1/足2 也要跟著往左右移動一點點
            // （見 Fields.cs 的 LegDragExtraOffsetX 說明），沒在拖曳時這裡查不到值/是 0，
            // 不影響原本的呼吸/原地搖擺位置。
            float dragExtraX = LegDragExtraOffsetX.TryGetValue(s.LayerName, out var dex) ? dex : 0f;

            Vector2 thrustOff = MLegThrustOffset.TryGetValue(s.LayerName, out var to) ? to : Vector2.Zero;
            ctrl.Position = basePos + pivotComp + s.Offset + animMove + new Vector2(breathOffsetX + dragExtraX, breathOffsetY) + BodyShake + thrustOff;
            ctrl.RotationDegrees = -(s.Rotation + animRot) + dragExtraRot + thrustRot;
        }
    }

    // ── 高潮觸發：顯示高潮水層 + 啟動痙攣 ───────────────────────────
    public void TriggerClimax()
    {
        OnClimaxReached();   // 記錄「已高潮過」＋高潮值回到 ClimaxAfterLevel（見 Arousal.cs）
        OnClimaxSweat();     // 高潮一次出很多汗（InsertEjac.cs）
        FaceController?.SetExpression("高潮");
        SetLayer("表情/高潮水", true);
        SetLayer("表情/小穴水", true);

        _spasmsRunning = true;
        _spasmsWaveIdx = -1; // -1 = 緩入收縮階段
        _spasmsWaveTimer = 0f;
        StartSquirt();

        var pdm = PlayerDataManager.Instance;
        if (pdm != null) { pdm.TodayTouchedOrgasm = true; }
    }

    void UpdateSpasms(float dt)
    {
        if (!_spasmsRunning) return;

        if (_spasmsWaveIdx < 0)
        {
            _spasmsWaveTimer += dt;
            float t = Mathf.Clamp(_spasmsWaveTimer / SpasmsContractEase, 0f, 1f);
            float amp = SpasmsShakeAmplitude * t;
            _spasmsBodyShake = new Vector2((float)GD.RandRange(-amp, amp), (float)GD.RandRange(-amp * 0.5f, amp * 0.5f));
            if (t >= 1f) { _spasmsWaveIdx = 0; _spasmsWaveTimer = 0f; }
            return;
        }

        if (_spasmsWaveIdx >= _spasmsWaves.Length)
        {
            // 緩出回正
            _spasmsWaveTimer += dt;
            float t = 1f - Mathf.Clamp(_spasmsWaveTimer / SpasmsContractEase, 0f, 1f);
            _spasmsBodyShake = _spasmsBodyShake.Lerp(Vector2.Zero, 1f - t);
            if (t <= 0f)
            {
                _spasmsRunning = false;
                _spasmsBodyShake = Vector2.Zero;
                StopSquirt();
            }
            return;
        }

        var (ampMult, dur, squirtMult) = _spasmsWaves[_spasmsWaveIdx];
        _spasmsWaveTimer += dt;
        float amp2 = SpasmsShakeAmplitude * ampMult;
        _spasmsBodyShake = new Vector2((float)GD.RandRange(-amp2, amp2), (float)GD.RandRange(-amp2 * 0.5f, amp2 * 0.5f));

        if (squirtMult > 0.01f && !_squirtActive) StartSquirt();
        else if (squirtMult <= 0.01f && _squirtActive) StopSquirt();

        if (_spasmsWaveTimer >= dur)
        {
            _spasmsWaveIdx++;
            _spasmsWaveTimer = 0f;
            if (_spasmsWaveIdx < _spasmsWaves.Length)
                StopSquirt();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 高潮噴水系統（SquirtWave port from Unity）
    // ─ 設計對應 Unity 版 9 波次 SquirtWaves 陣列：
    //   • 各波段控制 rate / speed / cone / distance 乘數
    //   • 水滴物理：Godot Y-down，落地後生成水漬 blob 並淡出
    //   • 座標：起點優先讀取小穴層 GlobalRect，fallback 用 SquirtOrigin
    // ═══════════════════════════════════════════════════════════════

    /// <summary>飛行中水滴物理狀態。</summary>
    sealed class SquirtDropState
    {
        public Panel   Node;
        public float   Vx, Vy;      // 速度（像素/秒）Godot Y-down
        public float   GroundY;     // 落地 Y 座標（螢幕空間）
        public float   Life;        // 剩餘存活時間（秒）
        public float   MaxLife;
        public float   DropW;       // 水滴寬（用於落地 blob 大小）
    }

    /// <summary>落地水漬 blob 視覺狀態。</summary>
    sealed class PuddleBlobState
    {
        public Control Node;
        public float   FadeDelay;   // 開始淡出前的延遲（秒）
        public float   FadeDur;     // 淡出持續時間（秒）
        public float   Timer;       // 計時器
    }

    // 9 波次定義：( dur, rate, spd, cone, dist )
    //   dur  = 波段持續秒數
    //   rate = 每秒生成倍率（0 = 停頓波）
    //   spd  = SquirtInitSpeed 乘數
    //   cone = 散射半角（度）
    //   dist = 落地距離上限（像素）
    static readonly (float dur, float rate, float spd, float cone, float dist)[] SquirtWaveDefs =
    {
        (0.8f, 0.55f, 0.70f, 35f, 240f),  // W1：近距中量
        (0.4f, 0f,    0f,    0f,  0f   ),  // 停頓
        (1.2f, 1.00f, 1.05f, 48f, 360f),  // W2：量多較大
        (0.5f, 0.35f, 0.68f, 32f, 240f),  // 溫和過渡
        (1.5f, 1.55f, 1.35f, 55f, 440f),  // W3：最高潮
        (1.0f, 0.65f, 0.80f, 42f, 320f),  // 漸弱
        (0.8f, 0.50f, 0.70f, 35f, 240f),  // W4 ≈ W1
        (0.3f, 0f,    0f,    0f,  0f   ),  // 停頓
        (0.8f, 0.50f, 0.70f, 35f, 240f),  // W5 ≈ W1
    };

    /// <summary>取得噴水起點（螢幕座標）：優先讀小穴層 GlobalRect，fallback SquirtOrigin。</summary>
    Vector2 GetSquirtOrigin()
    {
        // 嘗試找小穴層（Layers 是 Dictionary<string, Control>）
        Control layer = null;
        if (Layers != null)
        {
            if (Layers.TryGetValue("m腿/小穴", out var n1) && n1 != null) layer = n1;
            else if (Layers.TryGetValue("腿/小穴", out var n2) && n2 != null) layer = n2;
        }
        if (layer != null && layer.IsInsideTree())
        {
            // 取 Control 的全域矩形中心
            var rect = layer.GetGlobalRect();
            return rect.GetCenter();
        }
        // Fallback：用 SquirtOrigin 偏移（Unity Y-up → Godot screen）
        var vp = GetViewport().GetVisibleRect().Size;
        return new Vector2(vp.X * 0.5f + SquirtOrigin.X, vp.Y * 0.5f - SquirtOrigin.Y);
    }

    // ── 公開介面 ──────────────────────────────────────────────────

    // 改成 public：Unity 原版 DEV 面板（ExpressionTester.OnGUI）有獨立的「噴水 開始/停止」
    // 按鈕可以手動觸發，跟高潮時自動觸發是分開的兩件事，Godot 這邊也要讓 DEV 面板能直接呼叫。
    public void StartSquirt()
    {
        if (_squirtWaveRunning) return;
        _squirtActive = true;
        _squirtTimer = 0f;
        TaskLogger.Run(SquirtEmitterAsync(), "SquirtEmitter");
    }

    public void StopSquirt()
    {
        _squirtActive = false;
        _squirtWaveRunning = false;
    }

    /// <summary>供 DEV 面板顯示目前噴水開關狀態用。</summary>
    public bool IsSquirting => _squirtActive;

    // ════════════════════════════════════════════════════════════
    // 射精效果：以 async/await + ColorRect 動態節點實作。
    // 設計對應 Unity 版 EjaculationSequence（7 相位）的簡化移植——
    //   • 不使用粒子或 Spine；改用程序化生成的小圓彈頭串 + 圓角 blob。
    //   • 座標系統：HotspotPos 用 Unity Y-up，轉換時 Y 取負。
    //   • 起點   = 雞雞熱點中心（HotspotPos[4]）+ EjacTipOffset（Y 取負）。
    //   • 目標   = 各部位熱點中心 ± 隨機偏移，全部在螢幕座標系。
    //   • 飛行   = 拋物線弧（sin 曲線），segCount 個小方塊依次追頭。
    //   • 落點   = Panel + StyleBoxFlat 圓角 blob，先膨脹再穩定在 FinalScale。
    //   • 滴落   = 相位 4 的 addDrip：3 個水滴往下墜落淡出。
    // ════════════════════════════════════════════════════════════

    /// <summary>計算熱點在螢幕上的中心位置（Unity Y-up HotspotPos → Godot screen coords）。</summary>
    Vector2 HotspotScreenCenter(int idx)
    {
        var vSize = GetViewport().GetVisibleRect().Size;
        var h = HotspotPos[idx];
        return new Vector2(vSize.X * 0.5f + h.X, vSize.Y * 0.5f - h.Y);
    }

    /// <summary>射精起點：雞雞熱點中心 + EjacTipOffset（Y 取負轉到 Godot 螢幕座標）。</summary>
    Vector2 EjacSourcePos() =>
        HotspotScreenCenter(4) + new Vector2(EjacTipOffset.X, -EjacTipOffset.Y);

    /// <summary>觸發射精動畫。若上一次仍在播放則忽略本次呼叫。</summary>
    public void TriggerEjaculation()
    {
        if (_ejacRunning)
        {
            GD.Print("[TrainingRoom] TriggerEjaculation：動畫進行中，跳過。");
            return;
        }
        // 插入抽插中 → 內射；其他 → 外射（見 InsertEjac.cs）
        if (CanInsertEjac) TaskLogger.Run(InsertEjaculationAsync(), "TrainingRoom.InsertEjaculation");
        else               TaskLogger.Run(EjaculationSequenceAsync(), "TrainingRoom.EjaculationSequence");
    }

    /// <summary>清除所有射精殘留節點。</summary>
    public void ClearEjacDebris()
    {
        foreach (var d in _ejacDebris)
            if (d != null && IsInstanceValid(d)) d.QueueFree();
        _ejacDebris.Clear();
        HideJijiSemen();
    }

    // ─── 主序列（對照 Unity EjaculationSequence 標準版）──────────────────────
    // 落點全部改成跟 Unity 一樣以「角色畫布座標（2000x3200 像素）」計算：
    //   目標點 = 某圖層實際畫出內容的中心 + Unity 原本的偏移量，再換算到螢幕座標。
    // 之前用 HotspotPos（熱點固定座標）+ 螢幕中心推算，熱點位置跟圖層實際位置有落差，
    // 加上每一發各自亂數，落點（尤其 Y）很分散。
    [ExportGroup("射精節奏")]
    [Export(PropertyHint.Range, "1,4,1")] public int EjacLeadCount = 2;      // 起手「流出一點」幾次（力道不強）
    [Export(PropertyHint.Range, "2,6,1")] public int EjacTailCount = 3;      // 最後「回到近射、越來越弱」幾次
    [Export] public Vector2 EjacGapSlow   = new Vector2(0.30f, 0.50f);       // 起手流出之間的間隔（秒）
    [Export] public Vector2 EjacGapNormal = new Vector2(0.28f, 0.45f);       // 近射／中射之間的間隔（秒）
    [Export] public Vector2 EjacGapStrong = new Vector2(0.45f, 0.70f);       // 遠射之間的間隔（秒，蓄力久一點）

    async System.Threading.Tasks.Task EjaculationSequenceAsync()
    {
        _ejacRunning = true;
        ShowJijiSemen();   // 射精後雞雞上的精液圖層出現
        _sweat = Mathf.Min(1f, _sweat + SweatOnEjac);
        try
        {
            string pussy = ActivePussyLayer();

            // 射精分 4 段，像真的射：先流出一點（力道不強）→ ① 近射 → ② 中射 → ③ 遠射 → ④ 回到近射（越來越弱、慢慢流乾）。
            // 每一發的間隔、力道、距離都不一樣（以前每一發都接著射、力道一樣，太統一）。間隔／力道可在 Inspector 的「射精節奏」調整。

            // ── 起手：流出一點（力道不強、距離很短、慢、有滴落）──────────────────
            {
                int lead = Mathf.Max(1, EjacWaves(EjacLeadCount));
                for (int i = 0; i < lead; i++)
                {
                    Vector2 tipC = JijiTipCanvas();
                    Vector2 tgt = i == 0 ? LayerCanvasCenter(pussy) + new Vector2(Rf(-12, 12), Rf(-8, 8))
                                         : RandomUpTarget(tipC, EjacCloseMaxDist * 0.22f, EjacSpreadX * 0.3f);
                    await ShootSemenAsync(tipC, tgt, Mathf.Max(3, EjacCloseSegCount - 3), 0.45f, 0.75f, addDrip: true);
                    await Wait(Rf(EjacGapSlow.X, EjacGapSlow.Y));
                }
            }
            if (_ejacStop) goto EjacEnd;

            // ── ① 近射：2~3 次脈動，間隔不均、力道漸強（1 → 1.25）──────────────────
            for (int wave = 0, waveN = EjacWaves(3); wave < waveN; wave++)
            {
                float k = waveN <= 1 ? 1f : wave / (float)(waveN - 1);
                int simul = wave == 0 ? 1 : (int)GD.RandRange(1, 2);
                Vector2 pussyC = LayerCanvasCenter(pussy);
                for (int sh = 0; sh < simul; sh++)
                {
                    Vector2 tipC = JijiTipCanvas();
                    Vector2 tgtC = wave == 0
                        ? pussyC + new Vector2(Rf(-15, 15), Rf(-10, 10))
                        : RandomUpTarget(tipC, EjacCloseMaxDist * Mathf.Lerp(0.6f, 1f, k), EjacSpreadX);
                    var task = ShootSemenAsync(tipC, tgtC, EjacCloseSegCount, Mathf.Lerp(0.85f, 1.25f, k), 1f);
                    if (sh == simul - 1) await task;
                }
                await Wait(Rf(EjacGapNormal.X, EjacGapNormal.Y) * Mathf.Lerp(1.2f, 0.85f, k));   // 越射越密一點
            }
            if (_ejacStop) goto EjacEnd;

            // ── ② 中射：胸，2 次脈動，力道比近射強、間隔也不一樣 ────────────────────
            {
                Vector2 c1 = LayerCanvasCenter("胸1"), c2 = LayerCanvasCenter("胸2");
                var mids = new System.Collections.Generic.List<Vector2> { c1, c2, (c1 + c2) * 0.5f };
                for (int i = mids.Count - 1; i > 0; i--) { int j = (int)GD.RandRange(0, i); (mids[i], mids[j]) = (mids[j], mids[i]); }
                for (int m = 0; m < 2; m++)
                {
                    Vector2 tipC = JijiTipCanvas();
                    await ShootSemenAsync(tipC, mids[m] + new Vector2(Rf(-10, 10), Rf(-10, 10)), EjacMidSegCount, m == 0 ? 1.35f : 1.55f, 1.1f);
                    await Wait(Rf(EjacGapNormal.X, EjacGapNormal.Y) * (m == 0 ? 1f : 1.25f));
                }
            }
            if (_ejacStop || _ejacMul < 0.5f) goto EjacEnd;

            // ── ③ 遠射：3 次最有力的脈動，間隔最長（要蓄力），力道 2.0 ───────────────
            {
                Vector2 tipC = JijiTipCanvas();
                float dist = Mathf.Lerp(EjacFarMinDist, EjacFarMaxDist, Rf(0.5f, 0.9f)) - 80f;
                Vector2 t = PushOutOfNoShootZone(RandomUpTarget(tipC, dist, EjacSpreadX));
                await ShootSemenAsync(tipC, t, EjacFarSegCount, 2f, EjacFarBlobScale);
                await Wait(Rf(EjacGapStrong.X, EjacGapStrong.Y));
            }
            {
                Vector2 tipC = JijiTipCanvas();
                float baseX = Rf(60f, 100f);
                Vector2 bang = PushOutOfNoShootZone(LayerCanvasCenter("前髮") + new Vector2(baseX + Rf(-15, 15), -Rf(80f, 120f)));
                Vector2 face = PushOutOfNoShootZone(LayerCanvasCenter("臉")   + new Vector2(baseX + Rf(-15, 15),  Rf(100f, 140f)));
                _ = ShootSemenAsync(tipC, bang, EjacFarSegCount, 2.1f, EjacFarBlobScale);
                await ShootSemenAsync(tipC, face, EjacFarSegCount, 1.9f, EjacFarBlobScale);
                await Wait(Rf(EjacGapStrong.X, EjacGapStrong.Y) * 1.1f);
            }
            {
                Vector2 tipC = JijiTipCanvas();
                Vector2 chest = LayerCanvasCenter(GD.Randf() < 0.5f ? "胸1" : "胸2");
                await ShootSemenAsync(tipC, chest, EjacFarSegCount, 1.8f, EjacFarBlobScale);
                await Wait(Rf(EjacGapNormal.X, EjacGapNormal.Y));
            }
            if (_ejacStop || _ejacMul < 0.75f) goto EjacEnd;

            // ── ④ 回到近射：越來越弱、間隔越來越長，最後幾下只是流出來（帶滴落）──────
            {
                int tail = Mathf.Max(2, EjacWaves(EjacTailCount));
                for (int wave = 0; wave < tail; wave++)
                {
                    float k = tail <= 1 ? 1f : wave / (float)(tail - 1);   // 0 → 1：越來越弱
                    Vector2 tipC = JijiTipCanvas();
                    float dist = EjacCloseMaxDist * Mathf.Lerp(0.9f, 0.25f, k);
                    await ShootSemenAsync(tipC, RandomUpTarget(tipC, dist, EjacSpreadX * Mathf.Lerp(0.8f, 0.3f, k)),
                                          Mathf.Max(3, Mathf.RoundToInt(Mathf.Lerp(EjacCloseSegCount, EjacCloseSegCount - 4, k))),
                                          Mathf.Lerp(0.95f, 0.45f, k), Mathf.Lerp(1f, 0.75f, k), addDrip: true);
                    await Wait(Rf(EjacGapNormal.X, EjacGapNormal.Y) * Mathf.Lerp(1f, 2.2f, k));   // 越來越慢
                    if (_ejacStop) break;
                }
            }

        EjacEnd:
            // ── Phase 5：雞雞頂端精液團擴張 ──────────────────────────────────
            SpawnLandingBlob(CanvasToScreen(JijiTipCanvas()), large: true);
        }
        finally
        {
            _ejacRunning = false;
        }
    }

    // ─── 射精座標工具（角色畫布座標 2000x3200，Godot Y 朝下）────────────────

    static float Rf(float a, float b) => (float)GD.RandRange(a, b);
    SignalAwaiter Wait(float sec) => ToSignal(GetTree().CreateTimer(sec), SceneTreeTimer.SignalName.Timeout);

    /// <summary>各圖層貼圖實際畫出內容的中心（PIL 量 alpha bbox，畫布像素座標）。
    /// 圖層本身都是整張 2000x3200 透明畫布，不能拿節點中心當位置。</summary>
    static readonly System.Collections.Generic.Dictionary<string, Vector2> _ejacLayerContentCenter = new()
    {
        { "胸1",      new Vector2(977f,   998f) },
        { "胸2",      new Vector2(873f,   988f) },
        { "前髮",     new Vector2(957.5f, 617.5f) },
        { "臉",       new Vector2(990f,   604f) },
        { "腿/小穴",  new Vector2(976.5f, 1497.5f) },
        { "m腿/小穴", new Vector2(979f,   1450f) },
    };
    /// <summary>雞雞尖端（雞雞.png 內容最上緣中央，畫布像素座標）。</summary>
    static readonly Vector2 _jijiTipLocal = new Vector2(998f, 1598f);

    string ActivePussyLayer() =>
        _mLegOpen && Layers.ContainsKey("m腿/小穴") ? "m腿/小穴" : "腿/小穴";

    /// <summary>圖層內容中心 → _charRoot 本地座標（＝角色畫布像素尺度，含該圖層當下的呼吸/抖動位移）。</summary>
    Vector2 LayerCanvasCenter(string layer)
    {
        Vector2 local = _ejacLayerContentCenter.TryGetValue(layer, out var c) ? c : new Vector2(1000f, 1600f);
        if (Layers.TryGetValue(layer, out var node) && node != null && _charRoot != null)
            return node.GetTransform() * local;   // 圖層（_charRoot 子節點）本地像素 → _charRoot 本地座標（含位移/旋轉/樞紐）
        return local;
    }

    Vector2 JijiTipCanvas()
    {
        if (Layers.TryGetValue("雞雞", out var j) && j != null && j.Visible)
            return j.GetTransform() * _jijiTipLocal;
        // 雞雞沒顯示：退回用 B 熱點位置（螢幕 → 畫布）
        return ScreenToCanvas(HotspotScreenCenter(4) + new Vector2(EjacTipOffset.X, -EjacTipOffset.Y));
    }

    Vector2 CanvasToScreen(Vector2 canvas) =>
        _charRoot != null ? _charRoot.GetGlobalTransform() * canvas : canvas;
    Vector2 ScreenToCanvas(Vector2 screen) =>
        _charRoot != null ? _charRoot.GetGlobalTransform().AffineInverse() * screen : screen;

    /// <summary>Unity 預設落點：起點往上 maxDist×0.5~1、左右 ±spread（Unity Y 朝上 → Godot 減）。</summary>
    static Vector2 RandomUpTarget(Vector2 tip, float maxDist, float spread) =>
        tip + new Vector2(Rf(-spread, spread), -Rf(maxDist * 0.5f, maxDist));

    // ── 禁射區（眼睛/嘴巴附近，對照 Unity IsInNoShootZone / PushOutOfNoShootZone）──
    // NoShootZone*_Offset 沿用 Unity 數值（相對「臉」中心，Unity Y 朝上），這裡把 Y 取負。
    Vector2 PushOutOfNoShootZone(Vector2 pos)
    {
        Vector2 face = LayerCanvasCenter("臉");
        Vector2[] centers = {
            face + new Vector2(NoShootZoneA_Offset.X, -NoShootZoneA_Offset.Y),
            face + new Vector2(NoShootZoneB_Offset.X, -NoShootZoneB_Offset.Y) };
        Vector2[] sizes = { NoShootZoneA_Size, NoShootZoneB_Size };
        for (int z = 0; z < 2; z++)
        {
            Vector2 h = sizes[z] * 0.5f, c = centers[z];
            if (pos.X < c.X - h.X || pos.X > c.X + h.X || pos.Y < c.Y - h.Y || pos.Y > c.Y + h.Y) continue;
            float dL = pos.X - (c.X - h.X), dR = (c.X + h.X) - pos.X;
            float dT = pos.Y - (c.Y - h.Y), dB = (c.Y + h.Y) - pos.Y;
            float m = Mathf.Min(Mathf.Min(dL, dR), Mathf.Min(dT, dB));
            if      (m == dL) pos.X = c.X - h.X - 15f;
            else if (m == dR) pos.X = c.X + h.X + 15f;
            else if (m == dT) pos.Y = c.Y - h.Y - 15f;
            else              pos.Y = c.Y + h.Y + 15f;
        }
        return pos;
    }

    // ─── 單發飛行彈 ──────────────────────────────────────────────────────────
    // 由起點到終點：segCount 個小方塊（Panel + StyleBoxFlat 圓角）串成弧形，
    // 頭部較大（headR px 半徑）向尾部漸縮（tailR px），整體在 EjacFlightTime 秒飛到落點，
    // 落地後生成一個膨脹 blob。
    // ═══════════════════════════════════════════════════════════════
    // 射精飛行段 + 落點：使用程序化像素紋理（對應 Unity CreateCircleSprite / 
    // CreateProceduralSemenSprite / CreateSoftRectSprite）
    // ═══════════════════════════════════════════════════════════════

    // ── 靜態紋理快取 ─────────────────────────────────────────────
    static ImageTexture _ejacCircleTex;
    static ImageTexture _ejacSoftRectTex;

    static ImageTexture GetEjacCircleTex()
    {
        if (_ejacCircleTex != null) return _ejacCircleTex;
        const int size = 64;
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        float center = size * 0.5f, radius = center - 1f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dist = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
                float a = Mathf.Clamp((radius - dist) / 1.5f, 0f, 1f);
                img.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        _ejacCircleTex = ImageTexture.CreateFromImage(img);
        return _ejacCircleTex;
    }

    static ImageTexture GetEjacSoftRectTex()
    {
        if (_ejacSoftRectTex != null) return _ejacSoftRectTex;
        const int w = 64, h = 80;
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        float cx = w * 0.5f, cy = h * 0.5f;
        for (int py = 0; py < h; py++)
            for (int px = 0; px < w; px++)
            {
                float nx = (px - cx) / cx;
                float ny = (py - cy) / cy;
                // 左右大幅霧化，上下微霧（對應 Unity CreateSoftRectSprite）
                float edgeDist = nx * nx * 1.5f + ny * ny * 0.05f;
                float a = Mathf.Clamp((0.5f - edgeDist) / 0.5f, 0f, 1f);
                a = a * a * (3f - 2f * a);   // smoothstep × 2
                a = a * a * (3f - 2f * a);
                img.SetPixel(px, py, new Color(1f, 1f, 1f, a));
            }
        _ejacSoftRectTex = ImageTexture.CreateFromImage(img);
        return _ejacSoftRectTex;
    }

    /// <summary>生成一次性程序化精液紋理（每次不同形狀，對應 Unity CreateProceduralSemenSprite）</summary>
    static ImageTexture CreateSemenBlobTex()
    {
        const int w = 80, h = 80;
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        float cx = w * 0.5f, cy = h * 0.5f;

        int blobCount = (int)GD.RandRange(5, 9);
        var bx  = new float[blobCount]; var by  = new float[blobCount];
        var brx = new float[blobCount]; var bry = new float[blobCount];
        for (int i = 0; i < blobCount; i++)
        {
            bx[i]  = cx + (float)GD.RandRange(-18.0, 18.0);
            by[i]  = cy + (float)GD.RandRange(-18.0, 18.0);
            brx[i] = (float)GD.RandRange(6.0, 20.0);
            bry[i] = (float)GD.RandRange(6.0, 25.0);
        }

        for (int py = 0; py < h; py++)
            for (int px = 0; px < w; px++)
            {
                float minDist = float.MaxValue;
                for (int i = 0; i < blobCount; i++)
                {
                    float dx = (px - bx[i]) / brx[i];
                    float dy = (py - by[i]) / bry[i];
                    float d = dx * dx + dy * dy;
                    if (d < minDist) minDist = d;
                }
                if (minDist > 1.2f) { img.SetPixel(px, py, Colors.Transparent); continue; }

                float edge      = Mathf.Clamp((1.05f - minDist) / 0.25f, 0f, 1f);
                float brightness = Mathf.Lerp(0.85f, 0.98f, 1f - minDist);
                float hlx = (px - cx + 5f) / (w * 0.3f);
                float hly = (py - cy - 10f) / (h * 0.25f);
                float highlight = Mathf.Clamp(1f - (hlx * hlx + hly * hly), 0f, 1f) * 0.3f;

                float r = Mathf.Clamp(brightness + highlight, 0f, 1f);
                float g = Mathf.Clamp(brightness + highlight - 0.02f, 0f, 1f);
                float b = Mathf.Clamp(brightness + highlight - 0.04f, 0f, 1f);
                float a = edge * Mathf.Lerp(0.5f, 0.85f, 1f - minDist * 0.7f);

                // 邊緣混入皮膚粉色（避免灰色陰影，對應 Unity 的 edgeMix）
                float edgeMix = Mathf.Clamp(minDist * 1.2f, 0f, 1f);
                r = Mathf.Lerp(r, 0.82f, edgeMix * 0.7f);
                g = Mathf.Lerp(g, 0.52f, edgeMix * 0.7f);
                b = Mathf.Lerp(b, 0.48f, edgeMix * 0.7f);

                img.SetPixel(px, py, new Color(r, g, b, a));
            }
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>建立軟邊圓形 TextureRect（用於飛行段）</summary>
    TextureRect MkCircleTR(float diam, Color color)
    {
        var tr = new TextureRect
        {
            Texture           = GetEjacCircleTex(),
            ExpandMode        = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode       = TextureRect.StretchModeEnum.Scale,
            Size              = new Vector2(diam, diam),
            MouseFilter       = Control.MouseFilterEnum.Ignore,
            SelfModulate      = color,
        };
        return tr;
    }

    /// <summary>建立軟矩形 TextureRect（用於段間連接）</summary>
    TextureRect MkConnTR(Color color)
    {
        var tr = new TextureRect
        {
            Texture      = GetEjacSoftRectTex(),
            ExpandMode   = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode  = TextureRect.StretchModeEnum.Scale,
            MouseFilter  = Control.MouseFilterEnum.Ignore,
            SelfModulate = color,
        };
        return tr;
    }

    /// <summary>更新連接段的位置/大小/旋轉，使其延伸在 A、B 兩點之間。</summary>
    static void UpdateConnector(TextureRect conn, Vector2 posA, Vector2 posB, float width)
    {
        float dist  = posA.DistanceTo(posB);
        Vector2 mid = (posA + posB) * 0.5f;
        float angle = Mathf.Atan2(posB.Y - posA.Y, posB.X - posA.X) + Mathf.Pi * 0.5f;
        conn.Size        = new Vector2(width, dist + 2f);
        conn.PivotOffset = conn.Size * 0.5f;
        conn.Position    = mid - conn.PivotOffset;
        conn.Rotation    = angle;
    }

    // ── 射精飛行動畫（對照 Unity ShootSemen）────────────────────────────────
    // 跟之前版本的差別（使用者反應「一開始有奇怪變大的渲染、要偏向射出的感覺」）：
    //   • 之前所有段/連接條一開始就全部疊在起點、尺寸全開，還沒出發的段擠成一大團白色
    //     → 改成跟 Unity 一樣：每段輪到才出現（segDelay 依序出發），還沒出發的連接條不顯示。
    //   • 之前是等速 + 拋物線弧度（像丟出去）→ 改成 Unity 的直線 + ease-out（一開始最快，射出感）。
    //   • 之前落點 blob 是全部飛完才從 0 放大到滿再縮小（突然膨脹）→ 改成 Unity 的做法：
    //     頭一落地就以 40% 大小出現，後面每段抵達就「吸收」讓 blob 長大一點，最後停在最終大小。
    //   • 飛行時間依距離調整：EjacFlightTime × clamp(距離/600, 0.6, 2.2) / 速度倍率。
    // fromC / toC 都是角色畫布座標，內部每幀換算成螢幕座標（角色呼吸時起點/落點也會跟著走）。
    async System.Threading.Tasks.Task ShootSemenAsync(Vector2 fromC, Vector2 toC, int segCount,
        float flightSpeedMul, float blobScaleMul, bool addDrip = false)
    {
        if (_canvas == null) return;
        segCount = Mathf.Max(3, segCount);
        const float headDiam = 18f;
        const float tailDiam = 5f;

        // Unity 目標點再加 ±5 的極小偏移
        toC += new Vector2(Rf(-5, 5), Rf(-5, 5));

        float canvasDist = fromC.DistanceTo(toC);
        float flightT  = Mathf.Max(0.05f, EjacFlightTime) * Mathf.Clamp(canvasDist / 600f, 0.6f, 2.2f)
                       / Mathf.Max(0.1f, flightSpeedMul);
        float segDelay = 0.022f / Mathf.Max(0.1f, flightSpeedMul);

        float Diam(int i) => Mathf.Lerp(headDiam, tailDiam, i / (float)(segCount - 1));
        var col = new Color(0.96f, 0.94f, 0.89f, 1f);

        var segs = new TextureRect[segCount];
        var segPos = new Vector2[segCount];
        var segState = new int[segCount];   // 0=還沒出發 1=飛行中 2=已落地
        for (int i = 0; i < segCount; i++)
        {
            var tr = MkCircleTR(Diam(i), new Color(col.R, col.G, col.B, Mathf.Lerp(0.92f, 0.50f, i / (float)(segCount - 1))));
            tr.Visible = false;               // 輪到才出現
            _canvas.AddChild(tr);
            _ejacDebris.Add(tr);
            segs[i] = tr;
        }

        int connCount = segCount - 1;
        var conns = new TextureRect[connCount, 3];
        for (int i = 0; i < connCount; i++)
        {
            float a2 = Mathf.Lerp(0.85f, 0.40f, i / (float)Mathf.Max(1, connCount - 1));
            for (int k = 0; k < 3; k++)
            {
                var c = MkConnTR(new Color(col.R, col.G, col.B, a2));
                c.Visible = false;
                _canvas.AddChild(c);
                _ejacDebris.Add(c);
                conns[i, k] = c;
            }
        }

        TextureRect blob = null;
        Vector2 blobFull = Vector2.Zero;
        float finalS = Mathf.Clamp(EjacBlobFinalScale, 0.05f, 2f);
        int absorbed = 0;
        float elapsed = 0f;

        while (absorbed < segCount)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!IsInstanceValid(this)) return;
            elapsed += (float)GetProcessDeltaTime();

            Vector2 fromS = CanvasToScreen(fromC), toS = CanvasToScreen(toC);

            for (int i = 0; i < segCount; i++)
            {
                if (segState[i] == 2) continue;
                var sg = segs[i];
                if (sg == null || !IsInstanceValid(sg)) { segState[i] = 2; absorbed++; continue; }

                float t = (elapsed - segDelay * i) / flightT;
                if (t < 0f) continue;

                if (t >= 1f)
                {
                    // 抵達落點：頭段生成 blob，之後每段被「吸收」讓 blob 長大
                    if (blob == null)
                    {
                        blob = MakeLandingBlob(toS, blobScaleMul, out blobFull);
                    }
                    segState[i] = 2;
                    absorbed++;
                    _ejacDebris.Remove(sg);
                    sg.QueueFree();
                    if (blob != null && IsInstanceValid(blob))
                    {
                        float sc = Mathf.Lerp(0.4f, 1f, absorbed / (float)segCount) * finalS;
                        blob.Scale = new Vector2(sc, sc);
                    }
                    continue;
                }

                segState[i] = 1;
                float ease = 1f - (1f - t) * (1f - t);      // ease-out：出發最快
                segPos[i] = fromS.Lerp(toS, ease);
                float d = Diam(i);
                sg.Size = new Vector2(d, d);
                sg.Position = segPos[i] - new Vector2(d * 0.5f, d * 0.5f);
                sg.Visible = true;
            }

            // 連接條：兩端的段都在飛行中、而且拉開距離才顯示（避免擠在起點成一團）
            for (int i = 0; i < connCount; i++)
            {
                bool show = segState[i] == 1 && segState[i + 1] == 1
                            && segPos[i].DistanceSquaredTo(segPos[i + 1]) > 1f;
                float dA = Diam(i), dB = Diam(i + 1);
                float[] ws = { dA * 0.9f, (dA + dB) * 0.5f * 0.4f, dB * 0.9f };
                Vector2 pA = segPos[i], pB = segPos[i + 1];
                Vector2[] sub = { pA.Lerp(pB, 0.2f), (pA + pB) * 0.5f, pA.Lerp(pB, 0.8f) };
                for (int k = 0; k < 3; k++)
                {
                    var c = conns[i, k];
                    if (c == null || !IsInstanceValid(c)) continue;
                    c.Visible = show;
                    if (!show) continue;
                    Vector2 ca = k == 0 ? pA : sub[k - 1];
                    Vector2 cb = k == 2 ? pB : sub[k + 1];
                    UpdateConnector(c, ca, cb, ws[k]);
                }
            }
        }

        for (int i = 0; i < connCount; i++)
            for (int k = 0; k < 3; k++)
                if (conns[i, k] != null && IsInstanceValid(conns[i, k]))
                { _ejacDebris.Remove(conns[i, k]); conns[i, k].QueueFree(); }

        if (blob != null && IsInstanceValid(blob)) blob.Scale = new Vector2(finalS, finalS);
        if (addDrip)
            TaskLogger.Run(SpawnDripAsync(CanvasToScreen(toC)), "TrainingRoom.EjacDrip");
    }

    /// <summary>飛行彈落地時的精液團（不播放膨脹動畫，由 ShootSemenAsync 依吸收段數控制大小）。</summary>
    TextureRect MakeLandingBlob(Vector2 pos, float scaleMul, out Vector2 fullSize)
    {
        float w = 40f * EjacBlobMaxScale * scaleMul;
        float h = w * Rf(0.75f, 1.10f);
        fullSize = new Vector2(w, h);
        float s0 = 0.4f * Mathf.Clamp(EjacBlobFinalScale, 0.05f, 2f);
        var blob = new TextureRect
        {
            Texture     = CreateSemenBlobTex(),
            ExpandMode  = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Size        = fullSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            PivotOffset = fullSize * 0.5f,
            Position    = pos - fullSize * 0.5f,
            Scale       = new Vector2(s0, s0),
            SelfModulate = new Color(1f, 1f, 1f, 0.88f),
        };
        _canvas.AddChild(blob);
        _ejacDebris.Add(blob);
        return blob;
    }

    // ─── 落點 blob（程序化精液紋理，對應 Unity CreateProceduralSemenSprite）────
    void SpawnLandingBlob(Vector2 pos, bool large = false)
    {
        if (_canvas == null) return;
        float baseW = 40f * EjacBlobMaxScale * (large ? 1.6f : 1f);
        float baseH = baseW * (float)GD.RandRange(0.75, 1.10);  // 輕微橢圓

        var tex = CreateSemenBlobTex();
        var blob = new TextureRect
        {
            Texture      = tex,
            ExpandMode   = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode  = TextureRect.StretchModeEnum.Scale,
            Size         = new Vector2(baseW, baseH),
            MouseFilter  = Control.MouseFilterEnum.Ignore,
            Scale        = Vector2.Zero,
            PivotOffset  = new Vector2(baseW * 0.5f, baseH * 0.5f),
            Position     = pos - new Vector2(baseW * 0.5f, baseH * 0.5f),
        };
        _canvas.AddChild(blob);
        _ejacDebris.Add(blob);

        TaskLogger.Run(AnimateBlobAsync(blob), "TrainingRoom.EjacBlob");
    }

    async System.Threading.Tasks.Task AnimateBlobAsync(TextureRect blob)
    {
        if (blob == null) return;

        // 膨脹到最大
        float elapsed = 0f;
        float dur     = Mathf.Max(0.05f, EjacBlobExpandTime);
        while (elapsed < dur && IsInstanceValid(blob))
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            elapsed += (float)GetProcessDeltaTime();
            float t   = Mathf.Clamp(elapsed / dur, 0f, 1f);
            float ease = 1f - Mathf.Pow(1f - t, 3f);
            blob.Scale = new Vector2(ease, ease);
        }
        if (!IsInstanceValid(blob)) return;
        blob.Scale = Vector2.One;

        // 縮小到穩定大小
        float finalS = Mathf.Clamp(EjacBlobFinalScale, 0.01f, 2f);
        elapsed = 0f;
        dur     = 0.30f;
        while (elapsed < dur && IsInstanceValid(blob))
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            elapsed += (float)GetProcessDeltaTime();
            float t  = Mathf.Clamp(elapsed / dur, 0f, 1f);
            float s  = Mathf.Lerp(1f, finalS, t);
            blob.Scale = new Vector2(s, s);
        }
        if (IsInstanceValid(blob)) blob.Scale = new Vector2(finalS, finalS);
    }

    // ─── 滴落效果（TextureRect + 軟圓形）────────────────────────────────────
    async System.Threading.Tasks.Task SpawnDripAsync(Vector2 startPos)
    {
        for (int i = 0; i < 3; i++)
        {
            if (i > 0)
                await ToSignal(GetTree().CreateTimer(i * 0.14f), SceneTreeTimer.SignalName.Timeout);

            float diam = (float)GD.RandRange(5.0, 9.0);
            var drip = new TextureRect
            {
                Texture      = GetEjacCircleTex(),
                ExpandMode   = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode  = TextureRect.StretchModeEnum.Scale,
                Size         = new Vector2(diam, diam * (float)GD.RandRange(1.2, 2.0)),
                SelfModulate = new Color(0.95f, 0.92f, 0.86f, 0.80f),
                MouseFilter  = Control.MouseFilterEnum.Ignore,
                Position     = startPos + new Vector2((float)GD.RandRange(-8, 8), 0),
            };
            _canvas.AddChild(drip);
            _ejacDebris.Add(drip);

            TaskLogger.Run(AnimateDripAsync(drip), "TrainingRoom.EjacDripDrop");
        }
    }

    async System.Threading.Tasks.Task AnimateDripAsync(TextureRect drip)
    {
        if (drip == null) return;
        const float life = 3.5f;
        float vy      = (float)GD.RandRange(35f, 70f);
        float elapsed = 0f;

        while (elapsed < life && IsInstanceValid(drip))
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            float dt = (float)GetProcessDeltaTime();
            elapsed += dt;
            vy      += 55f * dt;
            drip.Position += new Vector2(0, vy * dt);
            drip.Modulate  = new Color(1f, 1f, 1f, Mathf.Clamp(1f - elapsed / life, 0f, 1f));
        }
        if (IsInstanceValid(drip))
        {
            _ejacDebris.Remove(drip);
            drip.QueueFree();
        }
    }



    /// <summary>【已移植】XiaoXueStatus：Unity 版「小穴狀態」文字。</summary>
    public string XiaoXueStatus => "正常";

    // ── 波次發射器（async）────────────────────────────────────────

    async Task SquirtEmitterAsync()
    {
        _squirtWaveRunning = true;
        try
        {
            foreach (var (dur, rate, spd, cone, dist) in SquirtWaveDefs)
            {
                if (!_squirtActive || !_squirtWaveRunning) break;

                float elapsed = 0f;
                float spawnAccum = 0f;

                while (elapsed < dur)
                {
                    if (!_squirtActive || !_squirtWaveRunning) goto Done;
                    float dt = (float)GetProcessDeltaTime();
                    elapsed += dt;

                    if (rate > 0f)
                    {
                        spawnAccum += dt * SquirtRate * rate;
                        while (spawnAccum >= 1f)
                        {
                            spawnAccum -= 1f;
                            SpawnSquirtDropPhysics(GetSquirtOrigin(), spd, cone, dist);
                        }
                    }
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
            }
            Done:;
        }
        finally
        {
            _squirtActive = false;
            _squirtWaveRunning = false;
        }
    }

    // ── 水滴生成（物理初始化）──────────────────────────────────────

    void SpawnSquirtDropPhysics(Vector2 origin, float spdMult, float coneHalf, float maxDist)
    {
        if (_canvas == null) return;

        // 射角：以向下為基準，coneHalf 度偏左右
        float angleDeg = (float)GD.RandRange(-coneHalf, coneHalf);
        float rad = Mathf.DegToRad(angleDeg);
        float sinA = Mathf.Sin(rad);
        float cosA = Mathf.Cos(rad);
        float lateralRatio = Mathf.Abs(sinA);

        float speed = SquirtInitSpeed * spdMult;
        // Godot Y-down：cosA > 0 → vy 正 = 向下；偏側時加些向下份量
        float vy = (cosA - lateralRatio * 0.95f) * speed;
        float vx = sinA * speed * 0.60f;

        // 落地 Y（螢幕座標，比起點更低）
        float distRange = maxDist > 0f ? (float)GD.RandRange(maxDist * 0.24f, maxDist * 0.80f) : 0f;
        float groundY = origin.Y + distRange;

        // 水滴外觀：細長的圓角矩形，寬度隨速度略有變化
        float dropW = (float)GD.RandRange(3.0, 6.0);
        float dropH = dropW * (float)GD.RandRange(2.0, 3.5);

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.44f, 0.43f, 0.55f, 0.30f),
            CornerRadiusTopLeft     = (int)(dropW * 0.5f),
            CornerRadiusTopRight    = (int)(dropW * 0.5f),
            CornerRadiusBottomLeft  = (int)(dropW * 0.5f),
            CornerRadiusBottomRight = (int)(dropW * 0.5f),
        };
        var node = new Panel
        {
            CustomMinimumSize = new Vector2(dropW, dropH),
            Size               = new Vector2(dropW, dropH),
            MouseFilter        = Control.MouseFilterEnum.Ignore,
        };
        node.AddThemeStyleboxOverride("panel", style);

        // 初始位置 ± 水平隨機擾動
        float jitter = (float)GD.RandRange(-8.0, 8.0);
        node.Position = origin + new Vector2(jitter - dropW * 0.5f, -dropH * 0.5f);
        _canvas.AddChild(node);

        float life = SquirtDropLife;
        _activeSquirtDrops.Add(new SquirtDropState
        {
            Node    = node,
            Vx      = vx,
            Vy      = vy,
            GroundY = groundY,
            Life    = life,
            MaxLife = life,
            DropW   = dropW,
        });
    }

    // ── 每幀物理更新（呼叫自 _Process）──────────────────────────────

    void UpdateSquirt(float dt)
    {
        // 更新飛行中的水滴
        for (int i = _activeSquirtDrops.Count - 1; i >= 0; i--)
        {
            var s = _activeSquirtDrops[i];
            if (s.Node == null || !IsInstanceValid(s.Node))
            {
                _activeSquirtDrops.RemoveAt(i);
                continue;
            }

            s.Life -= dt;
            if (s.Life <= 0f)
            {
                s.Node.QueueFree();
                _activeSquirtDrops.RemoveAt(i);
                continue;
            }

            // 物理積分（Godot Y-down）
            s.Vy += SquirtGravity * dt;
            var pos = s.Node.Position + new Vector2(s.Vx * dt, s.Vy * dt);
            s.Node.Position = pos;

            // 水滴按速度方向旋轉
            float angle = Mathf.Atan2(s.Vy, s.Vx);
            s.Node.Rotation = angle - Mathf.Pi * 0.5f;  // 頂部對準速度方向

            // 淡出（存活後半段才開始）
            float lifeRatio = s.Life / s.MaxLife;
            s.Node.Modulate = new Color(1, 1, 1, Mathf.Clamp(lifeRatio * 2f, 0f, 1f));

            // 落地判定
            if (pos.Y >= s.GroundY)
            {
                s.Node.QueueFree();
                _activeSquirtDrops.RemoveAt(i);
                SpawnPuddleBlob(new Vector2(pos.X, s.GroundY), s.DropW);
            }
        }
    }

    // ── 水漬 blob 生成 ───────────────────────────────────────────────

    void SpawnPuddleBlob(Vector2 pos, float dropW)
    {
        if (_canvas == null) return;

        // 中心橢圓
        float blobW = dropW * (float)GD.RandRange(3.5, 5.5);
        float blobH = blobW * (float)GD.RandRange(0.40f, 0.60f);
        AddPuddleEllipse(pos, blobW, blobH,
            new Color(0.44f, 0.43f, 0.55f, 0.30f),
            fadeDelay: (float)GD.RandRange(3.0f, 5.0f),
            fadeDur:   (float)GD.RandRange(4.0f, 7.0f));

        // 1–2 個衛星橢圓
        int extras = GD.Randi() % 2 == 0 ? 1 : 2;
        for (int i = 0; i < extras; i++)
        {
            float sW = blobW * (float)GD.RandRange(0.35f, 0.65f);
            float sH = sW * (float)GD.RandRange(0.40f, 0.65f);
            var offset = new Vector2(
                (float)GD.RandRange(-blobW * 0.9f, blobW * 0.9f),
                (float)GD.RandRange(-blobH * 0.8f, blobH * 0.8f));
            AddPuddleEllipse(pos + offset, sW, sH,
                new Color(0.44f, 0.43f, 0.55f, 0.30f),
                fadeDelay: (float)GD.RandRange(2.5f, 4.5f),
                fadeDur:   (float)GD.RandRange(3.5f, 6.0f));
        }
    }

    // 水漬改用「柔邊霧化」貼圖（使用者反應原本圓角方塊看起來像幾何圖形）：
    // 程序化產生幾張中心不透明、邊緣柔和漸淡、外形帶一點不規則的軟圓，隨機挑一張用。
    static ImageTexture[] _softPuddleTex;

    static ImageTexture GetSoftPuddleTex()
    {
        if (_softPuddleTex == null)
        {
            _softPuddleTex = new ImageTexture[4];
            var rng = new RandomNumberGenerator { Seed = 12345 };
            for (int k = 0; k < _softPuddleTex.Length; k++)
            {
                const int N = 64;
                var img = Image.CreateEmpty(N, N, false, Image.Format.Rgba8);
                // 邊緣不規則：用幾個不同頻率的正弦波擾動半徑
                float p1 = rng.RandfRange(0, Mathf.Tau), p2 = rng.RandfRange(0, Mathf.Tau), p3 = rng.RandfRange(0, Mathf.Tau);
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                        float ang = Mathf.Atan2(dy, dx);
                        float rr = 0.78f + 0.08f * Mathf.Sin(ang * 3f + p1) + 0.05f * Mathf.Sin(ang * 5f + p2) + 0.03f * Mathf.Sin(ang * 7f + p3);
                        float d = Mathf.Sqrt(dx * dx + dy * dy) / rr;
                        float a = Mathf.Clamp(1f - d, 0f, 1f);
                        a = a * a * (3f - 2f * a);           // 柔和的邊緣（霧化）
                        a = Mathf.Pow(a, 0.8f);
                        img.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
                _softPuddleTex[k] = ImageTexture.CreateFromImage(img);
            }
        }
        return _softPuddleTex[(int)GD.RandRange(0, _softPuddleTex.Length - 1)];
    }

    void AddPuddleEllipse(Vector2 pos, float w, float h, Color col, float fadeDelay, float fadeDur)
    {
        // 柔邊貼圖邊緣是漸淡的，畫大一點（×1.5）視覺大小才跟原本差不多
        w *= 1.5f; h *= 1.5f;
        var node = new TextureRect
        {
            Texture      = GetSoftPuddleTex(),
            ExpandMode   = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode  = TextureRect.StretchModeEnum.Scale,
            Size         = new Vector2(w, h),
            MouseFilter  = Control.MouseFilterEnum.Ignore,
            SelfModulate = col,
        };
        node.Position = pos - new Vector2(w * 0.5f, h * 0.5f);
        // 水漬圖層要在角色下面、但在背景上面：
        // 不能用 ZIndex = -1（背景也是 _canvas 的子節點、ZIndex = 0，-1 會連背景一起壓到下面看不到），
        // 改成調整子節點順序：插在 _charRoot 的正前面（背景 → 水漬 → 角色）。
        _canvas.AddChild(node);
        if (CharCanvasChild != null && IsInstanceValid(CharCanvasChild) && CharCanvasChild.GetParent() == _canvas)   // 人物外面包了光影 CanvasGroup，用最外層節點判斷
            _canvas.MoveChild(node, CharCanvasChild.GetIndex());

        _puddleBlobs.Add(new PuddleBlobState
        {
            Node      = node,
            FadeDelay = fadeDelay,
            FadeDur   = fadeDur,
            Timer     = 0f,
        });
    }

    // ── 水漬淡出（呼叫自 _Process）──────────────────────────────────

    void UpdatePuddleFade(float dt)
    {
        for (int i = _puddleBlobs.Count - 1; i >= 0; i--)
        {
            var b = _puddleBlobs[i];
            if (b.Node == null || !IsInstanceValid(b.Node))
            {
                _puddleBlobs.RemoveAt(i);
                continue;
            }

            b.Timer += dt;
            float fadeStart = b.FadeDelay;
            float fadeEnd   = b.FadeDelay + b.FadeDur;

            const float PuddleMinAlpha = 0.333f;  // 0.30(BgColor.A) × 0.333 ≈ 10% 有效透明度
            if (b.Timer < fadeStart)
            {
                // 等待期：保持初始透明度（BgColor.A=0.30 控制）
                b.Node.Modulate = Colors.White;
            }
            else if (b.Timer < fadeEnd)
            {
                // 緩慢淡化至 10%（最淡後停留，不消失）
                float t = (b.Timer - fadeStart) / b.FadeDur;
                float alpha = Mathf.Lerp(1f, PuddleMinAlpha, t);
                b.Node.Modulate = new Color(1, 1, 1, alpha);
            }
            // 達到最淡後停在 PuddleMinAlpha，不 QueueFree（由 ClearPuddles 統一清除）
        }
    }

    public void ClearPuddles()
    {
        foreach (var s in _activeSquirtDrops) if (s.Node != null && IsInstanceValid(s.Node)) s.Node.QueueFree();
        _activeSquirtDrops.Clear();
        foreach (var b in _puddleBlobs) if (b.Node != null && IsInstanceValid(b.Node)) b.Node.QueueFree();
        _puddleBlobs.Clear();
        // 舊的 _puddleDrops（已不使用）
        foreach (var d in _puddleDrops) if (IsInstanceValid(d)) d.QueueFree();
        _puddleDrops.Clear();
        StopSquirt();
    }
}
