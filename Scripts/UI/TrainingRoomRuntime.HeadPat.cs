using Godot;
using System.Collections.Generic;

/// <summary>
/// 摸頭（設計同胸部）：滑鼠停在前髮上半部 → 游標變成半透明手；點一下 → 出現一隻手在頭頂左右撫摸，
/// 頭（臉／表情／眼睛）以脖子為軸跟著小幅旋轉，頭髮跟頭綁在一起轉，另外再以頭頂為錨點多一點擺動＋跟手位移。
/// 按住左鍵往上拖加速、往下拖減速；右鍵點頭取消。手跟胸／下方共用「只有兩隻手」的規則。
/// 做法：在所有圖層更新完之後（HeadPatLate）才疊加位移／旋轉，並記住上一幀疊了多少，
/// 沒被其他系統重算的圖層會先扣掉上一幀的量，不會累積（之前「頭飛走」就是累積造成的）。
/// </summary>
public partial class TrainingRoomRuntime
{
    [ExportGroup("摸頭")]
    [Export] public bool    HeadPatEnabled      = true;
    [Export] public float   HeadPatSpeed        = 5f;      // 撫摸基本速度
    [Export] public float   HeadPatSpeedLvRate  = 0.005f;  // 拖曳每 1 螢幕 px 改變多少等級
    [Export] public float   HeadPatSpeedLvMax   = 5f;
    [Export] public float   HeadPatSpeedPerLv   = 0.25f;   // 每級速度加成
    [Export] public float   HeadPatHandSize     = 108f;    // 手的大小（場景 px）
    [Export] public float   HeadPatHandSwing    = 29f;     // 手左右擺動幅度（畫布 px）
    [Export] public Vector2 HeadPatHandLocal    = new Vector2(958f, 330f);   // 手在頭頂的位置（前髮畫布座標）
    [Export] public float   HeadPatHeadRotDeg   = 0.53f;    // 頭（含頭髮）繞脖子旋轉的最大角度
    [Export] public float   HeadPatHairRotDeg   = 0.53f;    // 頭髮額外以頭頂為錨點的擺動角度
    [Export] public float   HeadPatHairMovePx   = 3.5f;    // 頭髮額外跟手位移（畫布 px）
    [Export] public Vector2 HeadPatHandNudge    = new Vector2(0f, 40f);    // 手的位置微調（場景 px，Y 正＝往下）
    [Export] public Vector2 HeadPatHandPivot    = new Vector2(0.5f, 0.5f);   // 手的旋轉錨點（手圖的比例 0~1）；這個點會貼在頭上，旋轉時它不動
    [Export] public float   HeadPatHandWobbleDeg = 1.5f;   // 撫摸時手的左右擺動角度
    [Export] public Vector2 HeadPatHandNudgeLeft   = new Vector2(10f, -12f);   // 左手（鏡像那隻）的位置微調，跟右手分開調
    [Export] public float   HeadPatHandBaseRotLeft = 30f;                    // 左手的基本角度（正＝順時針，右手 -30 的鏡像）
    [Export] public float   HeadPatHandBaseRot  = -30f;    // 手的基本角度（負＝逆時針；左手自動鏡像）
    [Export] public float   HeadPatLagPx        = 40f;     // 重力／慣性：髮尾最大延遲位移（貼圖 px，後髮最多）
    [Export] public float   HeadPatLagStiffness = 60f;     // 慣性彈簧硬度（越小越晃）
    [Export] public float   HeadPatLagDamping   = 0.35f;   // 慣性阻尼比（越小彈越久）
    [Export] public float   HeadPatWavePx       = 6f;      // 頭髮波浪幅度（貼圖 px）
    [Export] public float   HairMoveLagGain      = 1.5f;   // 呼吸／上下移動時髮尾慣性延遲的放大倍率（0＝關閉）
    [Export] public float   HairMoveLagStiffness = 45f;    // 越小髮尾越慢跟上
    [Export] public float   HairMoveLagDamping   = 0.3f;   // 越小晃越久
    [Export] public float   HairMoveLagMaxPx     = 40f;    // 延遲上限（貼圖 px）
    [Export] public Vector2 HeadPatHairAnchor   = new Vector2(958f, 285f);   // 頭髮錨點＝前髮最上緣中央
    [Export] public Vector2 HeadPatNeckPivot    = new Vector2(990f, 760f);   // 頭部旋轉軸：越靠近頭中心（臉 331~877）位移越小；越往下（脖子 900）頭頂甩得越多
    [Export] public Rect2   HeadPatZone         = new Rect2(696f, 285f, 523f, 332f);   // 前髮內容框上半部

    static readonly string[] _patHairNames = { "前髮", "後髮1", "後髮2", "後髮3", "後發辮子1", "後發辮子2" };
    static readonly string[] _patHeadExtra =
    {
        "表情/眼白","表情/眼白2","表情/眼球1","表情/眼球2","表情/眼皮","表情/眼皮2",
        "表情/睫毛1","表情/睫毛2","表情/下睫毛1","表情/下睫毛2","眼角1","眼角2",
    };

    // 各頭髮層的彎曲設定（V＝貼圖高度比例；依 PIL 量的內容框）：髮根結束、髮尾、髮尾跟頭比例、慣性權重、波浪權重
    static readonly Dictionary<string, (float root, float tip, float follow, float lag, float wave)> _patHairBend = new()
    {
        { "後髮1", (0.19f,  0.51f,  0.25f, 1.0f, 1.0f) },   // 274~1631：最長，影響最大
        { "後髮2", (0.17f,  0.248f, 0.35f, 0.8f, 0.7f) },   // 472~793
        { "後髮3", (0.15f,  0.236f, 0.35f, 0.8f, 0.7f) },   // 378~756
        { "前髮",  (0.14f,  0.297f, 0.70f, 0.3f, 0.3f) },   // 285~950：短，影響小
    };
    readonly Dictionary<string, ShaderMaterial> _patHairMats = new();
    readonly Dictionary<string, (Vector2 pos, Vector2 vel)> _hairMoveSpring = new();
    float _patLag, _patLagVel, _patWavePhase, _patActivity, _patPrevCur;

    bool        _patOn, _patDragOwner, _patHover, _patSlotLeft;
    int         _ordH;
    float       _patT, _patCur, _patSpeedLv, _patLastMouseY;
    TextureRect _patHand;
    readonly Dictionary<string, (Vector2 pos, float rot, Vector2 off, float deg)> _patApplied = new();

    void SetupHeadPat()
    {
        AddChild(new HeadPatLate { Name = "HeadPatLate", Runtime = this, ProcessPriority = 1000 });
        // 頭髮彎曲用的 shader：沿用頭髮呼吸擴張那份（已掛的直接用；沒掛的補一份，擴張量維持 0）
        foreach (var name in _patHairBend.Keys)
        {
            if (!Layers.TryGetValue(name, out var c) || c == null) continue;
            if (c.Material is ShaderMaterial sm && sm.Shader == _hairFlareShader) { _patHairMats[name] = sm; continue; }
            if (c.Material == null && _hairFlareShader != null)
            {
                var m = new ShaderMaterial { Shader = _hairFlareShader };
                m.SetShaderParameter("expand_amount", 0f);
                c.Material = m;
                _patHairMats[name] = m;
            }
        }
    }

    bool InPatZoneWorld(Vector2 world)
    {
        if (!Layers.TryGetValue("前髮", out var hair) || hair == null || !hair.IsVisibleInTree()) return false;
        return HeadPatZone.HasPoint(hair.GetGlobalTransform().AffineInverse() * world);
    }
    bool InPatZone(Vector2 screenPos) => InPatZoneWorld(ScreenToWorld(screenPos));

    /// <summary>由 _Input 呼叫（不吃掉事件）。</summary>
    void HeadPatInput(InputEvent e)
    {
        if (!HeadPatEnabled) return;
        if (e is InputEventMouseMotion mm)
        {
            bool inZone = !IsPointerOverUi() && InPatZone(mm.Position) && !Input.IsMouseButtonPressed(MouseButton.Left);
            if (inZone && !_patHover) { _patHover = true; ApplyBreastHoverCursor(false); }
            else if (!inZone && _patHover && !Input.IsMouseButtonPressed(MouseButton.Left)) { _patHover = false; ApplyHandCursor(); }
        }
        else if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
        {
            if (mb.Pressed)
            {
                _patDragOwner = false;
                if (_jijiPhase == 3 || IsPointerOverUi() || !InPatZone(mb.Position)) return;
                _patDragOwner = true;
                _patLastMouseY = mb.Position.Y;
                if (!_patOn) StartPat();
            }
            else _patDragOwner = false;
        }
    }

    void StartPat()
    {
        bool leftBusy  = SlotBusy(true, "H");
        bool rightBusy = SlotBusy(false, "H");
        bool bWasLeft = _bSlotLeft;
        if (_handMode == HandMode.Left || _handMode == HandMode.Right)
        {
            // 左下角指定了手（跟下方同一套對應：「左手」鈕＝右手那隻）：用那隻，那隻手在忙就取代
            _patSlotLeft = _handMode == HandMode.Right;
            if (_patSlotLeft && _touchAnimRunningLeft)   CancelHandTouchAnim(true);
            if (!_patSlotLeft && _touchAnimRunningRight) CancelHandTouchAnim(false);
            if (_bTouchRunning && _bSlotLeft == _patSlotLeft) CancelBTouch();
            CancelNipplesInSlot(_patSlotLeft);
            MakeRoomForHand("H");
        }
        else
        {
            string replaced = MakeRoomForHandPeek("H", out bool? oldSlotH);
            if (replaced != null) _patSlotLeft = oldSlotH ?? false;
            else _patSlotLeft = rightBusy && !leftBusy;   // 右手在忙就用左手，否則用右手
            CancelSlot(_patSlotLeft, "H");
        }
        _ordH = ++_handSeq;

        var tex = ResourceHelper.LoadTexAny(_patSlotLeft ? "Sprites/左手" : "Sprites/右手");
        if (tex == null) return;
        if (_touchHandLayer == null)
        {
            _touchHandLayer = new Control { Name = "TouchHandLayer", MouseFilter = Control.MouseFilterEnum.Ignore };
            _touchHandLayer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _canvas.AddChild(_touchHandLayer);
        }
        _patHand = new TextureRect
        {
            Name = "TouchHand_Head", Texture = tex,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Size = new Vector2(HeadPatHandSize, HeadPatHandSize * HandYSquash),
        };
        _patHand.PivotOffset = _patHand.Size / 2f;
        ApplyHandTouchVisualMaterial(_patHand);   // 跟摸胸的手一樣的描邊＋濾鏡
        _touchHandLayer.AddChild(_patHand);
        _patOn = true;
        _patT = 0f;
        PlayerDataManagerTouch();
    }

    void CancelPat()
    {
        _patOn = false;
        if (_patHand != null && IsInstanceValid(_patHand)) _patHand.QueueFree();
        _patHand = null;
        _patSpeedLv = 0f;
    }

    /// <summary>HeadPatLate 每幀（所有圖層更新完之後）呼叫。</summary>
    internal void ApplyHeadPat(float dt)
    {
        // 速度：從頭上按下的這次拖曳才調整（往上加速、往下減速）
        if (_patOn && _patDragOwner && Input.IsMouseButtonPressed(MouseButton.Left))
        {
            float y = GetViewport().GetMousePosition().Y;
            _patSpeedLv = Mathf.Clamp(_patSpeedLv + (_patLastMouseY - y) * HeadPatSpeedLvRate, 0f, HeadPatSpeedLvMax);
            _patLastMouseY = y;
        }

        float target = 0f;
        if (_patOn && HeadPatEnabled)
        {
            _patT += dt * HeadPatSpeed * (1f + _patSpeedLv * HeadPatSpeedPerLv);
            target = Mathf.Sin(_patT);
        }
        _patCur = _patOn ? target : Mathf.Lerp(_patCur, 0f, 1f - Mathf.Exp(-8f * dt));
        if (!_patOn && Mathf.Abs(_patCur) < 0.001f) _patCur = 0f;

        // 手：貼在頭頂左右撫摸（跟著前髮圖層走）
        if (_patOn && _patHand != null && IsInstanceValid(_patHand) && Layers.TryGetValue("前髮", out var hairL) && hairL != null)
        {
            Vector2 p = hairL.GetGlobalTransform() * (HeadPatHandLocal + new Vector2(_patCur * HeadPatHandSwing, 0f));
            // 左手是右手的鏡像圖：錨點 X 也鏡像，位置／角度用左手自己的參數
            Vector2 pr = _patSlotLeft ? new Vector2(1f - HeadPatHandPivot.X, HeadPatHandPivot.Y) : HeadPatHandPivot;
            Vector2 piv = _patHand.Size * pr;
            _patHand.PivotOffset = piv;
            _patHand.GlobalPosition = p - piv + (_patSlotLeft ? HeadPatHandNudgeLeft : HeadPatHandNudge);
            _patHand.RotationDegrees = _patSlotLeft
                ? HeadPatHandBaseRotLeft - _patCur * HeadPatHandWobbleDeg
                : HeadPatHandBaseRot + _patCur * HeadPatHandWobbleDeg;
        }

        float headDeg = _patCur * HeadPatHeadRotDeg;
        float hairDeg = _patCur * HeadPatHairRotDeg;
        Vector2 hairMove = new Vector2(_patCur * HeadPatHairMovePx, 0f);

        foreach (var name in _patHairNames) PatLayer(name, headDeg, hairDeg, hairMove);
        UpdatePatHairBend(dt, headDeg + hairDeg);
        UpdatePatTalk(dt);   // 摸頭台詞氣泡（PatTalk.cs）
        UpdateLick(dt);      // 舔（Tongue.cs）
        UpdateNipples(dt);   // 摸乳頭（Nipple.cs）
        UpdateTongue(dt);    // 舌頭（Tongue.cs）：放在摸頭之後，才能跟著臉的最終位置
        foreach (var name in _headLayerSet)  PatLayer(name, headDeg, 0f, Vector2.Zero);
        foreach (var name in _patHeadExtra)  PatLayer(name, headDeg, 0f, Vector2.Zero);
        UpdateInsertMouth(dt);   // 插入時嘴巴放大、越舒服越小（InsertMouth.cs）
        if (_patCur == 0f) _patApplied.Clear();
    }

    /// <summary>頭髮彎曲：髮根跟頭、髮尾少跟＋重力慣性（彈簧延遲，放開後還會晃一下）＋波浪。</summary>
    void UpdatePatHairBend(float dt, float totalDeg)
    {
        // 慣性彈簧：髮尾追著手的位置（_patCur），落後的量＝延遲
        float k = Mathf.Max(1f, HeadPatLagStiffness);
        float c = 2f * Mathf.Sqrt(k) * HeadPatLagDamping;
        float sdt = Mathf.Min(dt, 1f / 30f);
        _patLagVel += (k * (_patCur - _patLag) - c * _patLagVel) * sdt;
        _patLag    += _patLagVel * sdt;
        float lag = _patLag - _patCur;

        float vel = Mathf.Abs(_patCur - _patPrevCur) / Mathf.Max(dt, 0.0001f);
        _patPrevCur = _patCur;
        _patActivity = Mathf.Lerp(_patActivity, Mathf.Clamp(vel / Mathf.Max(0.1f, HeadPatSpeed), 0f, 1f), 1f - Mathf.Exp(-4f * dt));
        _patWavePhase += dt * 6f;

        float theta = Mathf.DegToRad(totalDeg);
        foreach (var kv in _patHairMats)
        {
            if (!_patHairBend.TryGetValue(kv.Key, out var b) || kv.Value == null) continue;
            var m = kv.Value;
            m.SetShaderParameter("pat_theta", theta);
            m.SetShaderParameter("pat_neck_v", HeadPatNeckPivot.Y / 3200f);
            m.SetShaderParameter("pat_root_v", b.root);
            m.SetShaderParameter("pat_tip_v", b.tip);
            m.SetShaderParameter("pat_tip_follow", b.follow);
            m.SetShaderParameter("pat_lag_px", lag * HeadPatLagPx * b.lag);
            m.SetShaderParameter("pat_wave_px", HeadPatWavePx * b.wave * _patActivity);
            m.SetShaderParameter("pat_wave_phase", _patWavePhase);

            // 呼吸／上提／被推動等整體移動：髮尾用彈簧追圖層位置（不含摸頭疊加的量），落後的部分＝延遲
            Vector2 lagMove = Vector2.Zero;
            if (Layers.TryGetValue(kv.Key, out var ctrl) && ctrl != null && HairMoveLagGain > 0f)
            {
                Vector2 basePos = ctrl.Position;
                if (_patApplied.TryGetValue(kv.Key, out var ap)) basePos -= ap.off;
                if (!_hairMoveSpring.TryGetValue(kv.Key, out var sp)) sp = (basePos, Vector2.Zero);
                float hk = Mathf.Max(1f, HairMoveLagStiffness);
                float hc = 2f * Mathf.Sqrt(hk) * HairMoveLagDamping;
                sp.vel += (hk * (basePos - sp.pos) - hc * sp.vel) * sdt2(dt);
                sp.pos += sp.vel * sdt2(dt);
                _hairMoveSpring[kv.Key] = sp;
                lagMove = ((sp.pos - basePos) * HairMoveLagGain * b.lag).LimitLength(HairMoveLagMaxPx);
            }
            m.SetShaderParameter("move_lag_px", lagMove);
        }
    }

    static float sdt2(float dt) => Mathf.Min(dt, 1f / 30f);

    /// <summary>圖層點 x（畫布座標）的新位置 f(x) = N + Rh·(A + Rr·(x−A) + move − N)：
    /// 先以頭頂 A 擺動＋位移（只有頭髮），再整顆頭繞脖子 N 轉。換算成 Control 的 Position 偏移＝f(pivot)−pivot。</summary>
    void PatLayer(string name, float headDeg, float hairDeg, Vector2 move)
    {
        if (!Layers.TryGetValue(name, out var c) || c == null) return;

        // 先扣掉上一幀疊加的量（如果這一幀沒有別的系統重算過它的位置／角度）
        Vector2 basePos = c.Position; float baseRot = c.RotationDegrees;
        if (_patApplied.TryGetValue(name, out var prev))
        {
            if (basePos.IsEqualApprox(prev.pos)) basePos -= prev.off;
            if (Mathf.IsEqualApprox(baseRot, prev.rot)) baseRot -= prev.deg;
        }

        var rh = new Transform2D(Mathf.DegToRad(headDeg), Vector2.Zero);
        var rr = new Transform2D(Mathf.DegToRad(hairDeg), Vector2.Zero);
        Vector2 p = c.PivotOffset, A = HeadPatHairAnchor, N = HeadPatNeckPivot;
        Vector2 fp = N + rh.BasisXform(A + rr.BasisXform(p - A) + move - N);
        Vector2 off = fp - p;
        float deg = headDeg + hairDeg;

        c.Position = basePos + off;
        c.RotationDegrees = baseRot + deg;
        _patApplied[name] = (c.Position, c.RotationDegrees, off, deg);
    }
}
