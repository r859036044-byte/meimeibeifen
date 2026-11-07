using Godot;
using System.Collections.Generic;

/// <summary>
/// TrainingRoomRuntime — 互動熱點（綠圈）系統。
///
/// 【已知簡化，相對 Unity 原版】
/// Unity 原版的熱點互動極度精細：胸部熱點觸發持續手部動畫協程
/// （AnimateHand，每幀依 BreastAnimMoveLeft/Right 做胸部聯動位移）、
/// 腿部熱點（6/7）支援拖曳展開角度＋自動切換到「M腿模式」（含弧形網格
/// 裁切、M腿獨立呼吸/腳部展開曲線）、手肘熱點（9/10）直接驅動 ArmIK
/// 拖曳與吸附、雞雞熱點（4）驅動長達 3 階段（Phase 1/2/3）的插入位移與
/// 阻力突破分數系統。這些都是以 Update() 逐幀手動调校的視覺化"手感"
/// 系統，並非資料驅動，在此以合理工作量移植為：
///   • 熱點位置/大小/閃爍回饋（FlashHotspot）— 完整移植
///   • 點擊熱點 → 對應舒服度數值上升 + 閃爍視覺回饋 — 完整移植（功能等價）
///   • 熱點 9/10（手肘）→ 直接呼叫既有 ArmIK.BeginDrag()/EndDrag()（沿用
///     Godot 版 ArmIK 既有的拖曳/回彈邏輯，未做原版的吸附/凍結細節）
///   • 熱點 4（雞雞）→ 呼叫 OnJijiButtonPressed 的簡化顯示切換（見 BuildUI.cs）
///   • 腿部熱點（6/7，膝蓋）→ M 腿模式：原版是拖曳超過閾值才觸發、含弧形網格裁切、
///     腳部展開曲線等一整套程序化系統，這裡簡化成拖曳超過閾值（見 MLegKneeDragThreshold）
///     才把「腿」圖層群組交叉淡出、「m腿」圖層群組交叉淡入（ToggleMLegModeAsync）——
///     右膝往右拖、左膝往左拖才會「開」，已經打開時往反方向拖回去才會「關」（見
///     OnKneeDragStart/OnKneeDragEnd），不做拖曳角度/腳部展開曲線那些細節。M 腿打開後，
///     膝蓋熱點的綠點位置會即時黏著 M 腿實際的膝蓋位置（見 UpdateMLegKneeHotspotTracking，
///     跟胸部熱點 UpdateBreastHotspotTracking 是同一套「跟著圖層實際位置」思路）。
/// </summary>
public partial class TrainingRoomRuntime : Node
{
    /// <summary>F1 面板「隱藏綠點」：只隱藏熱點的綠圈和文字，熱點本身照樣可以點。</summary>
    [Export] public bool HotspotDotsHiddenAtStart = true;   // 一開始先把綠點（熱點的綠圈和文字）藏起來，熱點本身照樣可以點；F1 面板可以再打開
    public bool HotspotDotsHidden { get; private set; }
    public void SetHotspotDotsHidden(bool hidden)
    {
        HotspotDotsHidden = hidden;
        if (_hotspotImgs != null)
            foreach (var img in _hotspotImgs) if (img != null) img.Visible = !hidden;
        if (_hotspotTexts != null)
            foreach (var t in _hotspotTexts) if (t != null) t.Visible = !hidden;
    }

    void BuildHotspots()
    {
        int n = HotspotPos.Length;
        _hotspotImgs  = new TextureRect[n];
        _hotspotRTs   = new Control[n];
        _hotspotTexts = new Label[n];

        var layer = new Control { Name = "HotspotLayer", MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _canvas.AddChild(layer);

        for (int i = 0; i < n; i++)
        {
            int idx = i;

            // 9/10（手肘）不再用固定位置的綠圈熱點：改成每幀判定滑鼠是否按在「手肘的真實當前位置」
            // 附近（見 TryGrabElbow，隨手臂呼吸/擺動即時跟著跑，不是畫面上一個死點）。
            // 這裡只留 null 佔位，陣列長度/索引不變，OnHotspotClicked 的 case 9/10 邏輯原樣保留、
            // 由 TryGrabElbow 直接呼叫。
            if (idx == 9 || idx == 10 || idx == 0)   // 0＝鼻：綠點已移除（嘴巴綠點搬到鼻子的位置）
            {
                _hotspotImgs[i] = null;
                _hotspotRTs[i] = null;
                _hotspotTexts[i] = null;
                continue;
            }

            float sz = HotspotSize[i];

            var holder = new Control { Name = "Hotspot_" + (i + 1), MouseFilter = Control.MouseFilterEnum.Ignore };
            MainMenuRuntime.ApplyPointAnchor(holder, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), HotspotPlacePos(i), new Vector2(sz, sz));
            layer.AddChild(holder);

            var img = new TextureRect
            {
                Name = "Circle",
                Texture = MakeCircleTexture(64, new Color(0.15f, 0.85f, 0.25f)),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                Modulate = new Color(1f, 1f, 1f, HotspotAlpha),
            };
            img.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            holder.AddChild(img);

            var btn = new Button { Name = "Btn", Flat = true };
            btn.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            holder.AddChild(btn);
            if (idx == 6 || idx == 7)
            {
                // 使用者要求：膝蓋熱點（開 M 腿）從「點一下就切換」改成「拖曳超過閾值才觸發」，
                // 見下面 OnKneeDragStart/OnKneeDragEnd 與 MLegKneeDragThreshold 的說明。
                btn.ButtonDown += () => OnKneeDragStart(idx);
                btn.ButtonUp   += () => OnKneeDragEnd(idx);
            }
            else if (idx == 4)
            {
                // B（雞雞）：要能「按下去就開始、按住不放持續突破」，所以用 ButtonDown 而不是
                // Pressed（Pressed 要放開才觸發，沒辦法做按住）。見 Jiji.cs OnJijiHotspotDown。
                // 但正在摸 B／舔 B 時再按會取消 → 改成放開沒拖才觸發（ClickDrag.cs），按住上下拖可以調速度。
                btn.ButtonDown += HotspotBPress;
            }
            else
            {
                // 放開才觸發；按下後拖超過 ClickDragPx 就不算點擊（按住物件上下拖調速度不會被當成點物件）
                btn.ButtonDown += ClickDragBegin;
                btn.Pressed += () => { if (!ClickWasDrag()) OnHotspotClicked(idx); };
            }
            btn.MouseEntered += () => { img.Modulate = new Color(0.4f, 1f, 0.5f, 0.95f); HoverTipShowIdx(idx); };
            btn.MouseExited  += () => { img.Modulate = new Color(1f, 1f, 1f, HotspotAlpha); HoverTipHide(); };

            // 胸部熱點（2=胸1/左手，3=胸2/右手）額外提示：滑鼠移過去時，系統游標換成
            // 小一點、半透明的手（跟點下去之後才出現的「持續動畫手」是兩回事，這裡只是
            // hover 提示這裡摸得到），滑鼠移開恢復成進房間時套用的手形游標。
            if (idx == 2 || idx == 3)
            {
                bool isLeftBreast = (idx == 2);
                btn.MouseEntered += () => { if (_tongueTool) ApplyTongueCursor(); else ApplyBreastHoverCursor(isLeftBreast); };
                btn.MouseExited  += () => ApplyHandCursor();
            }
            // 嘴巴：只能用舌頭 → 一律顯示舌頭游標；B：選了 👅 才顯示舌頭游標
            if (idx == 1)
            {
                btn.MouseEntered += () => ApplyTongueCursor();
                btn.MouseExited  += () => ApplyHandCursor();
            }
            if (idx == 4)
            {
                btn.MouseEntered += () => { if (_tongueTool && _jijiPhase == 0) ApplyTongueCursor(); };
                btn.MouseExited  += () => { if (_hoverCursorKind == 3) ApplyHandCursor(); };
            }

            // 使用者要求：胸部熱點的 hover 手勢游標（ApplyBreastHoverCursor，已改回原本
            // 100% 大小）也套用在腿（膝蓋）熱點上，一樣依左右膝對應左右手圖。
            if (idx == 6 || idx == 7)
            {
                bool isLeftKnee = (idx == 6);
                btn.MouseEntered += () => ApplyBreastHoverCursor(isLeftKnee, false);
                btn.MouseExited  += () => ApplyHandCursor();
            }

            var lbl = new Label
            {
                Name = "T", Text = HotspotLabels[i],
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            lbl.AddThemeFontSizeOverride("font_size", 22);
            lbl.AddThemeColorOverride("font_color", Colors.White);
            lbl.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            holder.AddChild(lbl);

            _hotspotImgs[i]  = img;
            _hotspotRTs[i]   = holder;
            _hotspotTexts[i] = lbl;
        }
    }

    /// <summary>
    /// 使用者要求：把胸部熱點（2=胸1、3=胸2）的判定範圍改成「跟著胸部貼圖本身」走，
    /// 不要固定在畫面上一個死點——胸部圖層本來就會隨呼吸、摸胸擴張/上提、觸摸動畫聯動
    /// 位移，熱點釘死在 HotspotPos 的話，動畫進行到一半常常會對不準實際看到的胸部位置。
    ///
    /// 做法：跟熱點 9/10（手肘，見下面 TryGrabElbow）改成「即時抓真實位置」是同一種思路，
    /// 只是那邊抓的是 ArmIK 的骨架點，這裡抓的是圖層自己的軸心點——每幀直接用
    /// Control.GetGlobalTransform() 把「胸部圖層自己的 PivotOffset」（已經是貼圖實際內容的
    /// 中心，見 BuildUI.cs 的 ApplyBreastPivotOverrides／Fields.cs 的 BreastPivotOverrides）
    /// 轉成全域座標，這樣不管呼吸/摸胸動畫把胸部推到哪裡、放大縮小多少，都能拿到「這一幀
    /// 胸部視覺中心實際在螢幕上的位置」，讓熱點的中心點貼著跑。熱點本身的大小仍維持
    /// HotspotSize 設定值，不會因為胸部那張畫布本身很大（跟其他圖層一樣是整張 2000x3200
    /// 攤平畫布）就跟著放大成一個誇張大小的判定範圍。
    /// 從 _Process() 在 UpdateBreathing() 之後呼叫，確保拿到的是這一幀算好的最新位置。
    /// </summary>
    /// <summary>
    /// 簡化版 M 腿模式切換（見檔頭「已知簡化」）：點一下膝蓋熱點（6/7），把「腿」圖層群組
    /// 交叉淡出、「m腿」圖層群組交叉淡入（用 Modulate.A 從 1→0／0→1，時間長度是
    /// MLegFadeDuration），再點一次切回來。不做 Unity 原版拖曳角度/腳部展開曲線/弧形網格
    /// 裁切那些細節，純粹是「腿 ↔ m腿」兩組圖層的顯示切換，但至少讓膝蓋熱點真的能打開
    /// M 腿姿勢，不再只是印一句「尚未實作」。
    /// </summary>
    async System.Threading.Tasks.Task ToggleMLegModeAsync()
    {
        _mLegTransitioning = true;
        if (_bTouchRunning) CancelBTouch();   // 一般 ↔ M 腿切換時自動關掉摸 B 的動畫
        bool opening = !_mLegOpen;
        _mLegOpen = opening;
        MLegCameraFocus(opening);

        string fromPrefix = opening ? "腿" : "m腿";
        string toPrefix   = opening ? "m腿" : "腿";

        // 淡入的那組要先打開 Visible（Alpha=0），不然 Visible=false 時光改 Modulate.A
        // 完全看不到任何東西。
        SetGroupAlpha(toPrefix, 0f, forceVisible: true);

        float t = 0f;
        float dur = Mathf.Max(0.01f, MLegFadeDuration);
        while (t < dur)
        {
            t += (float)GetProcessDeltaTime();
            // Godot 的 Mathf 沒有 Unity 的 Clamp01，改用 Clamp(x, 0, 1)。
            float p = Mathf.Clamp(t / dur, 0f, 1f);
            SetGroupAlpha(fromPrefix, 1f - p);
            SetGroupAlpha(toPrefix, p);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        // 淡出的那組真的關掉 Visible（省繪製、也避免疊在畫面上擋到滑鼠判定），
        // 同時把 Alpha 復原成 1，這樣下次換它淡入時才不會一開始就是透明的。
        HideLayerGroup(fromPrefix);
        SetGroupAlpha(fromPrefix, 1f);
        _mLegTransitioning = false;
    }

    void UpdateBreastHotspotTracking()
    {
        // 使用者反應左胸/右胸的綠點位置裝反了：跟先前修正「摸胸動畫左右手對應哪個胸部圖層」
        // 是同一個根源——熱點2（畫面左邊、左手）原本追蹤的是「胸1」，但「胸1」實際上是
        // 畫面右邊那顆胸部的貼圖，所以熱點2的綠點才會跑到右邊去。這裡跟 CancelHandTouchAnim／
        // AnimateHandTouch 的對應關係保持一致（isLeft→胸2、!isLeft→胸1），對調過來。
        UpdateOneBreastHotspotTracking(2, "胸2", BreastHotspotNudgeLeft);
        UpdateOneBreastHotspotTracking(3, "胸1", BreastHotspotNudgeRight);
    }

    void UpdateOneBreastHotspotTracking(int idx, string layerName, Vector2 nudge)
    {
        if (_hotspotRTs == null || idx < 0 || idx >= _hotspotRTs.Length) return;
        var holder = _hotspotRTs[idx];
        if (holder == null) return;
        if (!Layers.TryGetValue(layerName, out var breastCtrl) || breastCtrl == null) return;

        Vector2 globalCenter = breastCtrl.GetGlobalTransform() * breastCtrl.PivotOffset;
        holder.GlobalPosition = globalCenter - holder.Size * 0.5f + nudge;
    }

    /// <summary>
    /// 使用者要求：M 腿膝蓋熱點（6/7）打開 M 腿之後，綠點位置要移動到、並黏著 M 腿
    /// 實際的膝蓋位置（見 Fields.cs 的 MLegKneeLocalPoint 說明），沒打開 M 腿時則
    /// 維持原本 HotspotPos 那個固定點——因為 OnKneeDragEnd 判斷拖曳方向/距離時，
    /// 是拿「這次放開滑鼠的位置」跟「按下時的位置」比較，熱點固定不動才能讓使用者
    /// 每次都从同一個地方開始拖，判定才穩定。
    /// 跟 UpdateBreastHotspotTracking 一樣，要在 UpdateBreathing() 把這一幀腿部呼吸/
    /// 展開位移算完之後才呼叫，才能抓到最新位置。
    /// </summary>
    void UpdateMLegKneeHotspotTracking()
    {
        // 傳入對應的「一般腿」圖層名稱，沒開 M 腿時改追蹤那個圖層（見 UpdateOneKneeHotspotTracking）
        UpdateOneKneeHotspotTracking(6, "m腿/腿2", MLegKneeHotspotNudgeLeft,  "腿/腿2", LegKneeHotspotNudgeLeft);  // 左膝
        UpdateOneKneeHotspotTracking(7, "m腿/腿1", MLegKneeHotspotNudgeRight, "腿/腿1", LegKneeHotspotNudgeRight); // 右膝
    }

    /// <summary>
    /// 使用者要求：M 腿打開後，B（雞雞/插入互動，熱點 4）的判定點要改成跟著「m腿/小穴」
    /// 這個圖層一起上下移動，並在跟隨到的位置上再往上 30px（見 Fields.cs 的
    /// MLegJijiHotspotNudge／MLegJijiLocalPoint 說明）。沒開 M 腿時維持原本 HotspotPos[4]
    /// 的固定點，跟 UpdateMLegKneeHotspotTracking 是同一套邏輯，只是只有一個點。
    /// </summary>
    /// <summary>小穴_M.png 實際畫出內容的外框（2000x3200 畫布像素，PIL 量 alpha bbox）。</summary>
    static readonly Rect2 _mLegPussyContentRect = new Rect2(860f, 1321f, 1098f - 860f, 1579f - 1321f);

    /// <summary>滑鼠（場景座標）是否點在 M 腿小穴本體上。</summary>
    bool IsMouseOverMLegPussy(Vector2 worldPos)
    {
        if (!Layers.TryGetValue("m腿/小穴", out var c) || c == null || !c.Visible) return false;
        Vector2 local = c.GetGlobalTransform().AffineInverse() * worldPos;
        return _mLegPussyContentRect.HasPoint(local);
    }

    void UpdateMLegJijiHotspotTracking()
    {
        if (_hotspotRTs == null || _hotspotRTs.Length <= 4) return;
        var holder = _hotspotRTs[4];
        if (holder == null) return;

        // 使用者要求：M 腿打開後 B 綠點整個隱藏（連按鈕一起），改成直接點「m腿/小穴」本體觸發同樣功能
        // （見 _Input 的 IsMouseOverMLegPussy）。位置照樣持續更新，摸 B 的手／雞雞靠近還是用這個點當基準。
        holder.Visible = !_mLegOpen;

        if (!_mLegOpen)
        {
            MainMenuRuntime.ApplyPointAnchor(holder, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                HotspotPlacePos(4), new Vector2(HotspotSize[4] + BHotspotSizeAddPx, HotspotSize[4] + BHotspotSizeAddPx));   // 每幀重設，所以 BHotspotUpPx 可即時調
            return;
        }

        if (!Layers.TryGetValue("m腿/小穴", out var jijiCtrl) || jijiCtrl == null) return;

        Vector2 globalCenter = jijiCtrl.GetGlobalTransform() * MLegJijiLocalPoint;
        holder.GlobalPosition = globalCenter - holder.Size * 0.5f + MLegJijiHotspotNudge;
    }

    float _kneeDragProgress = 0f;   // 目前拖曳進度 0~1，用來補償綠點被腿旋轉帶向內側的位移
    readonly Dictionary<int, Vector2> _kneeRestLocal = new();
    readonly HashSet<int> _kneePendingCapture = new();

    void UpdateOneKneeHotspotTracking(int idx, string mLegLayerName, Vector2 mLegNudge,
                                       string regularLegLayerName = "", Vector2 regularNudge = default)
    {
        if (_hotspotRTs == null || idx < 0 || idx >= _hotspotRTs.Length) return;
        var holder = _hotspotRTs[idx];
        if (holder == null) return;

        if (!_mLegOpen)
        {
            // 使用者要求：沒開 M 腿時，綠點也要跟著一般腿圖層走（呼吸/搖腿動畫時綠點隨著位移）。
            // 不改使用者調好的位置：先照 HotspotPos 放好，等這個位置生效後（下一幀），
            // 把「綠點中心相對腿圖層的本地座標」記下來，之後每幀用腿圖層的 GlobalTransform 還原，
            // 綠點就像黏在腿上一樣，平移/旋轉都跟著，但靜止時位置跟 HotspotPos 完全一致。
            MainMenuRuntime.ApplyPointAnchor(holder, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                HotspotPos[idx], new Vector2(HotspotSize[idx], HotspotSize[idx]));
            if (!string.IsNullOrEmpty(regularLegLayerName)
                && Layers.TryGetValue(regularLegLayerName, out var regCtrl) && regCtrl != null)
            {
                var xf = regCtrl.GetGlobalTransform();
                if (_kneeRestLocal.TryGetValue(idx, out var restLocal))
                {
                    float comp = _kneeDragProgress * LegDragKneeDotCompensation * (idx == 6 ? -1f : 1f);   // 往外補償
                    holder.GlobalPosition = xf * restLocal - holder.Size * 0.5f + regularNudge + new Vector2(comp, 0f);
                }
                else if (_kneePendingCapture.Contains(idx))
                {
                    _kneeRestLocal[idx] = xf.AffineInverse() * (holder.GlobalPosition + holder.Size * 0.5f);
                    _kneePendingCapture.Remove(idx);
                }
                else _kneePendingCapture.Add(idx);
            }
            return;
        }

        if (!Layers.TryGetValue(mLegLayerName, out var legCtrl) || legCtrl == null) return;
        if (!MLegKneeLocalPoint.TryGetValue(mLegLayerName, out var localPoint)) return;

        Vector2 mLegGlobalCenter = legCtrl.GetGlobalTransform() * localPoint;
        holder.GlobalPosition = mLegGlobalCenter - holder.Size * 0.5f + mLegNudge;
    }

    // ── 膝蓋熱點（6/7）拖曳判定：改成拖曳超過閾值才觸發 M 腿開／關 ─────────────
    // 使用者規則：右膝（7）向右拖 ≥ 閾值 → 開；左膝（6）向左拖 ≥ 閾值 → 開。
    // 已經打開時，往反方向拖回去 ≥ 閾值 → 關（對稱設計；使用者原話沒有明講關閉的判定，
    // 這裡先採用最直覺的「拖回去就關」，之後使用者測試覺得不順手再調整）。
    const float MLegKneeDragThreshold = 30f;
    bool _kneeDragActive = false;
    int _kneeDragIdx = -1;
    Vector2 _kneeDragStartPos;

    void OnKneeDragStart(int idx)
    {
        _kneeDragActive = true;
        _kneeDragIdx = idx;
        _kneeDragStartPos = GetViewport().GetMousePosition();
        // 除錯用：先確認 button_down 有沒有真的觸發、起點抓到哪裡，等確定拖曳判定
        // 正常運作了再把這行 GD.Print 拿掉。
        GD.Print($"[MLeg] 膝蓋 {idx} 按下，起點={_kneeDragStartPos}");

        // 每次重新開始拖曳前先歸零，避免上一次沒拖滿、旋轉/位移沒重置乾淨的殘留值。
        LegDragExtraRotation["腿/腿1"] = 0f;
        LegDragExtraRotation["腿/腿2"] = 0f;
        LegDragExtraRotation["腿/腳1"] = 0f; LegDragExtraRotation["腿/腳2"] = 0f;
        LegDragExtraRotation["腿/足1"] = 0f; LegDragExtraRotation["腿/足2"] = 0f;
        LegDragExtraOffsetX["腿/腿1"] = 0f;
        LegDragExtraOffsetX["腿/腿2"] = 0f;
        _kneeDragProgress = 0f;
        LegDragExtraOffsetX["腿/腳1"] = 0f;
        LegDragExtraOffsetX["腿/腳2"] = 0f;
        LegDragExtraOffsetX["腿/足1"] = 0f;
        LegDragExtraOffsetX["腿/足2"] = 0f;
    }

    /// <summary>
    /// 使用者要求：沒開 M 腿時，拖曳膝蓋熱點（開 M 腿手勢）的過程中，對應的腿要先做一點
    /// 輕微旋轉當視覺回饋——右膝（7）拖對應右腿「腿/腿1」逆時針、左膝（6）拖對應左腿
    /// 「腿/腿2」順時針，旋轉角度隨拖曳距離（0～MLegKneeDragThreshold）線性長到
    /// LegDragRotationAmount，拖錯方向或還沒動則是 0（見 Fields.cs 的 LegDragExtraRotation／
    /// LegDragPivotOverride、Effects.cs 的 UpdateLegLayerDetails() 怎麼套用這個值）。
    /// 從 _Input() 在滑鼠移動、且正在拖膝蓋熱點時逐幀呼叫。
    /// </summary>
    void UpdateKneeDragRotationFeedback(Vector2 mousePos)
    {
        bool isLeftKnee = (_kneeDragIdx == 6);
        float dx = mousePos.X - _kneeDragStartPos.X;

        // 只在往「開」的方向拖時才給視覺回饋，往反方向拖或還沒動則是 0。
        float outwardDist = isLeftKnee ? Mathf.Max(0f, -dx) : Mathf.Max(0f, dx);
        float progress = Mathf.Clamp(outwardDist / MLegKneeDragThreshold, 0f, 1f);
        if (!_mLegOpen && PantyBlocksMLeg) progress = Mathf.Min(progress, PantyBlockLegMaxFrac);   // 內褲拉過「縮」的位置：腿只能掰一點點

        // 使用者要求：左右膝蓋熱點聯動——不管拖哪一邊，兩條腿都要一起有旋轉回饋，
        // 不再只有被拖的那一邊動（腿1＝右腿逆時針、腿2＝左腿順時針，方向固定不受
        // 拖哪一邊影響）。
        LegDragExtraRotation["腿/腿1"] = progress * LegDragRotationAmount * -1f;
        LegDragExtraRotation["腿/腿2"] = progress * LegDragRotationAmount *  1f;
        // 腳/足跟同一條腿用同樣角度（同軸心）一起轉，接縫才不會裂開
        LegDragExtraRotation["腿/腳1"] = LegDragExtraRotation["腿/足1"] = progress * LegDragRotationAmount * -1f;
        LegDragExtraRotation["腿/腳2"] = LegDragExtraRotation["腿/足2"] = progress * LegDragRotationAmount *  1f;

        // 使用者要求：同時讓腿1/腿2（右腿/左腿）連同足1/足2（右腳掌/左腳掌）也往左右移動
        // 一點，拖滿時最多移動 LegDragOffsetXAmount（右側 +X、左側 −X）。
        // 最開時整條腿（腿＋腳＋足）一起往內收：三層用同樣的量平移，關節接縫才不會裂開
        float inward = progress * progress * LegDragFootInwardX;
        LegDragExtraOffsetX["腿/腿1"] = progress * LegDragOffsetXAmount - inward;
        LegDragExtraOffsetX["腿/腿2"] = progress * -LegDragOffsetXAmount + inward;
        _kneeDragProgress = progress;
        LegDragExtraOffsetX["腿/腳1"] = progress * (LegDragOffsetXAmount + LegDragFootExtraX) - inward;
        LegDragExtraOffsetX["腿/腳2"] = progress * -(LegDragOffsetXAmount + LegDragFootExtraX) + inward;
        LegDragExtraOffsetX["腿/足1"] = progress * LegDragOffsetXAmount - inward;
        LegDragExtraOffsetX["腿/足2"] = progress * -LegDragOffsetXAmount + inward;

        // 同一個拖曳進度也拿來驅動小穴／身體下緣的輕微拉伸（見 ApplyMLegDragStretch）。
        ApplyMLegDragStretch(progress);
    }

    /// <summary>使用者要求：拖曳膝蓋熱點過程中，小穴左右、身體下緣都要有一點點拉伸的視覺
    /// 回饋，幅度跟拖曳進度（0～1）成正比。實際的 shader/region 設定見 BuildUI.cs 的
    /// SetupMLegDragStretchShaders()、Fields.cs 的 MLegDragStretchRegions——這裡只是每幀
    /// 拿目前的拖曳進度去更新兩個 ShaderMaterial 的 expand_amount 參數。</summary>
    void ApplyMLegDragStretch(float progress)
    {
        if (_mLegDragStretchMaterials.TryGetValue("腿/小穴", out var jijiMat) && jijiMat != null)
            jijiMat.SetShaderParameter("expand_amount", progress * MLegDragJiXueStretchAmount);
        if (_mLegDragStretchMaterials.TryGetValue("身體", out var bodyMat) && bodyMat != null)
            bodyMat.SetShaderParameter("expand_amount", progress * MLegDragBodyStretchAmount);
    }

    void OnKneeDragEnd(int idx)
    {
        // 除錯用：不管有沒有判定成功都先印出來，確認 button_up 真的有觸發、拖曳距離算出多少。
        GD.Print($"[MLeg] 膝蓋 {idx} 放開，目前位置={GetViewport().GetMousePosition()}，_kneeDragActive={_kneeDragActive}，_kneeDragIdx={_kneeDragIdx}");

        bool wasActive = _kneeDragActive && _kneeDragIdx == idx;
        _kneeDragActive = false;

        // 拖曳過程中的旋轉/拉伸視覺回饋在放開的當下就歸零：拖到閾值的話，接下來
        // ToggleMLegModeAsync 的「腿」群組淡出會蓋掉這些效果；沒拖到閾值（取消）的話
        // 就直接讓腿/小穴/身體回正，不做額外的緩動。
        LegDragExtraRotation["腿/腿1"] = 0f;
        LegDragExtraRotation["腿/腿2"] = 0f;
        LegDragExtraRotation["腿/腳1"] = 0f; LegDragExtraRotation["腿/腳2"] = 0f;
        LegDragExtraRotation["腿/足1"] = 0f; LegDragExtraRotation["腿/足2"] = 0f;
        LegDragExtraOffsetX["腿/腿1"] = 0f;
        LegDragExtraOffsetX["腿/腿2"] = 0f;
        _kneeDragProgress = 0f;
        LegDragExtraOffsetX["腿/腳1"] = 0f;
        LegDragExtraOffsetX["腿/腳2"] = 0f;
        LegDragExtraOffsetX["腿/足1"] = 0f;
        LegDragExtraOffsetX["腿/足2"] = 0f;
        ApplyMLegDragStretch(0f);

        if (!wasActive || _mLegTransitioning) return;

        float dx = GetViewport().GetMousePosition().X - _kneeDragStartPos.X;
        bool isLeftKnee = (idx == 6);

        bool dragOutward = isLeftKnee ? (dx <= -MLegKneeDragThreshold) : (dx >= MLegKneeDragThreshold);
        bool dragInward  = isLeftKnee ? (dx >=  MLegKneeDragThreshold) : (dx <= -MLegKneeDragThreshold);

        GD.Print($"[MLeg] 膝蓋 {idx} dx={dx}，isLeftKnee={isLeftKnee}，dragOutward={dragOutward}，dragInward={dragInward}，_mLegOpen={_mLegOpen}");

        if (!_mLegOpen && dragOutward && PantyBlocksMLeg) { if (PantyDebugLog) GD.Print("[PantyDbg] 內褲還沒脫掉／穿回去：不能進 M 腿"); return; }
        if ((!_mLegOpen && dragOutward) || (_mLegOpen && dragInward))
        {
            FlashHotspot(idx);
            TaskLogger.Run(ToggleMLegModeAsync(), "TrainingRoom.ToggleMLegMode");
        }
    }

    // ── 手肘抓取判定：取代原本固定位置的綠圈熱點 9/10 ─────────────────
    // 直接用 ArmIK/RightArmIK 當前實際的手肘世界座標（跟 HandFollowWrist 用同一個點）
    // 做距離判定，滑鼠在附近按下就等同點了熱點 9/10，呼叫同一套 OnHotspotClicked 邏輯，
    // 拖動/回彈/凍結等既有行為完全不變。
    const float ElbowGrabRadius = 48f;

    public override void _Input(InputEvent @event)
    {
        if (_timingChallengeActive || _endingDay) return;
        ClickDragInput(@event);   // 點擊／拖曳判斷（ClickDrag.cs）
        if (TissueInput(@event)) { GetViewport().SetInputAsHandled(); return; }   // 衛生紙模式（Tissue.cs）
        if (PantyDebugKeys(@event)) return;   // Insert/Home/End：逐項關掉最近的內褲效果找出是哪個造成問題（Tissue.cs 旁的 PantyDebug）
        if (AllAgesLocked)
        {   // 全年齡：只剩 摸頭／蓋被子／捏臉／鏡頭
            if (PinchInput(@event)) { GetViewport().SetInputAsHandled(); return; }
            HeadPatInput(@event);
            if (DuvetInput(@event)) { GetViewport().SetInputAsHandled(); return; }
            if (HandleCameraInput(@event)) { GetViewport().SetInputAsHandled(); return; }
            if (@event is InputEventMouseButton arb && arb.Pressed && arb.ButtonIndex == MouseButton.Right && HandleRightClick(arb.Position)) GetViewport().SetInputAsHandled();
            return;
        }
        if (_dragTool)   // 拖曳模式：只處理被子／衣服／鏡頭，其他一律無視
        {
            if (!DemoMode.Enabled && MLegPantyAsideInput(@event)) { GetViewport().SetInputAsHandled(); return; }   // M 腿內褲撥開（MLegPantyAside.cs）：排最前面，不被衣擺／被子搶走（試玩版：衣服物件不可控制）
            if (!DemoMode.Enabled && PantyBedInput(@event)) { GetViewport().SetInputAsHandled(); return; }
            if (DuvetInput(@event)) { GetViewport().SetInputAsHandled(); return; }
            if (!DemoMode.Enabled && (ClothHemInput(@event) || PantyInput(@event))) { GetViewport().SetInputAsHandled(); return; }
            if (HandleCameraInput(@event)) { GetViewport().SetInputAsHandled(); return; }
            return;
        }
        if (!DemoMode.Enabled && MLegPantyAsideInput(@event)) { GetViewport().SetInputAsHandled(); return; }   // M 腿內褲撥開（MLegPantyAside.cs）：排最前面，不被衣擺／被子搶走（試玩版：衣服物件不可控制）
        if (PinchInput(@event)) { GetViewport().SetInputAsHandled(); return; }   // 捏臉（Pinch.cs）
        HeadPatInput(@event);
        TongueInput(@event);
        if (!DemoMode.Enabled && PantyBedInput(@event)) { GetViewport().SetInputAsHandled(); return; }   // 床上的內褲：拖動／往上拖穿回去（PantyPull.cs）
        if (DuvetInput(@event)) { GetViewport().SetInputAsHandled(); return; }   // 抓被子上緣往下拉（Bedding.cs）
        if (!DemoMode.Enabled && (ClothHemInput(@event) || PantyInput(@event))) { GetViewport().SetInputAsHandled(); return; }   // 抓衣擺往上拉、捲起來（Cloth.cs）
        NippleHoverInput(@event);   // 滑鼠停在奶頭上：游標換成碰奶頭用的食指手
        if (NippleInput(@event)) { GetViewport().SetInputAsHandled(); return; }   // 摸乳頭（Nipple.cs）    // 舌頭：從嘴巴開始的拖曳＝調速度   // 摸頭：前髮上半部按住左鍵（不吃掉事件）
        if (HandleCameraInput(@event)) { GetViewport().SetInputAsHandled(); return; }   // 滾輪縮放 / 中鍵平移（Camera.cs）
        if (@event is InputEventMouseButton rb && rb.Pressed && rb.ButtonIndex == MouseButton.Right)
        {
            // 右鍵：點在雞雞/小穴上才會讓雞雞退一階（或停止摸 B），點在胸部上停止摸胸，
            // 空白處在放大時拖動畫面（見 Controls.cs HandleRightClick）
            if (HandleRightClick(rb.Position)) GetViewport().SetInputAsHandled();
            return;
        }
        if (@event is InputEventMouseButton lb && lb.Pressed && lb.ButtonIndex == MouseButton.Left
            && _jijiPhase == 2 && IsMouseOverJiji(ScreenToWorld(lb.Position)))
        {
            // 對照 Unity：Phase 2 直接點雞雞本體也能開始插入（不一定要點 B 熱點）
            OnJijiHotspotDown();
            return;
        }
        if (@event is InputEventMouseButton pb && pb.Pressed && pb.ButtonIndex == MouseButton.Left
            && _mLegOpen && !IsPointerOverUi() && IsMouseOverMLegPussy(ScreenToWorld(pb.Position)))
        {
            // M 腿模式：點小穴本體＝原本點 B 熱點（摸 B／雞雞靠近／開始插入，按住可突破）
            HotspotBPress();   // 正在摸 B／舔 B 時延後到放開、沒拖才算點（ClickDrag.cs）
            GetViewport().SetInputAsHandled();
            return;
        }
        if (@event is InputEventMouseButton ab && ab.Pressed && ab.ButtonIndex == MouseButton.Left
            && _mLegOpen && _jijiPhase <= 2 && !IsPointerOverUi() && IsMouseOverMLegAnus(ab.Position))
        {
            // M 腿模式：點肛門圖層＝肛門的突破流程（OnAnusClicked，Jiji.cs）；選了 👅 且雞雞沒出現 → 舔肛門（增加肛門濕潤）
            if (_tongueTool && _jijiPhase == 0) ToggleLick("A"); else OnAnusClicked();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            TryGrabElbow(ScreenToWorld(mb.Position));   // 手肘位置是場景座標，縮放後要換算
        // 拖曳膝蓋熱點過程中的即時旋轉視覺回饋（見 UpdateKneeDragRotationFeedback 說明），
        // 只有沒開 M 腿、且正在拖膝蓋熱點時才需要追蹤滑鼠移動。
        else if (@event is InputEventMouseMotion mm && _kneeDragActive && !_mLegOpen)
            UpdateKneeDragRotationFeedback(mm.Position);
    }

    // ── 手肘/手腕連接點除錯圓點（紅=ArmElbow／橘=HandJoint，兩點該重疊）───
    TextureRect _dotElbow1, _dotJoint1, _dotElbow2, _dotJoint2;

    void BuildIKDebugDots()
    {
        if (!ShowIKDebugDots) return;
        var layer = new Control { Name = "IKDebugDots", MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.ZIndex = 100;
        _canvas.AddChild(layer);

        TextureRect MakeDot(Color c)
        {
            var d = new TextureRect
            {
                Texture = MakeCircleTexture(10, c),
                Size = new Vector2(20, 20),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            layer.AddChild(d);
            return d;
        }

        _dotElbow1 = MakeDot(new Color(1f, 0.15f, 0.15f, 0.9f)); // 紅：ArmIK.ArmElbow
        _dotJoint1 = MakeDot(new Color(1f, 0.55f, 0.05f, 0.9f)); // 橘：ArmIK.HandJoint
        _dotElbow2 = MakeDot(new Color(1f, 0.15f, 0.15f, 0.9f)); // 紅：RightArmIK.ArmElbow
        _dotJoint2 = MakeDot(new Color(1f, 0.55f, 0.05f, 0.9f)); // 橘：RightArmIK.HandJoint
    }

    void UpdateIKDebugDots()
    {
        if (!ShowIKDebugDots) return;

        void Place(TextureRect dot, Vector2? p)
        {
            if (dot == null) return;
            if (!p.HasValue) { dot.Visible = false; return; }
            dot.Visible = true;
            dot.GlobalPosition = p.Value - dot.Size / 2f;
        }

        if (ArmIK != null)
        {
            Place(_dotElbow1, ArmIK.GetElbowGlobalPos());
            Place(_dotJoint1, ArmIK.GetHandJointGlobalPos());
        }
        if (RightArmIK != null)
        {
            Place(_dotElbow2, RightArmIK.GetElbowGlobalPos());
            Place(_dotJoint2, RightArmIK.GetHandJointGlobalPos());
        }
    }

    void TryGrabElbow(Vector2 mouseGlobalPos)
    {
        if (ArmIK != null && !ArmIK.IsDragging)
        {
            var p = ArmIK.GetElbowGlobalPos();
            if (p.HasValue && p.Value.DistanceTo(mouseGlobalPos) <= ElbowGrabRadius)
            {
                OnHotspotClicked(9);
                return;
            }
        }
        if (RightArmIK != null && !RightArmIK.IsDragging)
        {
            var p = RightArmIK.GetElbowGlobalPos();
            if (p.HasValue && p.Value.DistanceTo(mouseGlobalPos) <= ElbowGrabRadius)
                OnHotspotClicked(10);
        }
    }

    // 之前這裡只有單一個快取欄位，不分半徑/顏色，導致後面呼叫 MakeCircleTexture 用不同
    // 半徑/顏色時，會直接拿到第一次呼叫產生的舊圖（顏色/大小都是錯的）。改成用
    // (radius,color) 當 key 快取，讓熱點的綠圈跟除錯用的紅/橘點可以同時正確存在。
    static readonly Dictionary<(int, Color), ImageTexture> _circleTexCache = new();
    static Texture2D MakeCircleTexture(int radius, Color color)
    {
        var key = (radius, color);
        if (_circleTexCache.TryGetValue(key, out var cached)) return cached;
        int d = radius * 2;
        var img = Image.CreateEmpty(d, d, false, Image.Format.Rgba8);
        for (int y = 0; y < d; y++)
        {
            for (int x = 0; x < d; x++)
            {
                float dx = x - radius + 0.5f, dy = y - radius + 0.5f;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp(radius - dist, 0f, 1.5f) / 1.5f;
                if (dist > radius) a = 0f;
                img.SetPixel(x, y, new Color(color.R, color.G, color.B, color.A * a));
            }
        }
        var tex = ImageTexture.CreateFromImage(img);
        _circleTexCache[key] = tex;
        return tex;
    }

    /// <summary>熱點編號 0-based：0鼻 1嘴 2胸1(左手) 3胸2(右手) 4B(雞雞) 5尻 6/7腿 8足 9/10手肘</summary>
    void OnHotspotClicked(int idx)
    {
        FlashHotspot(idx);
        if (idx == 4 && _jijiPhase == 1) _jijiAnus = false;   // 雞雞還在靠近時改點 B：目標換回 B

        switch (idx)
        {
            case 0: // 鼻
                Mood = Mathf.Min(100f, Mood + 1f);
                break;
            case 1: // 嘴
                if (TongueOnMouthClicked()) break;   // 嘴巴只能用舌頭（Tongue.cs）
                ComfortMouth = Mathf.Min(100f, ComfortMouth + 3f);
                PlayerDataManagerTouch();
                break;
            case 2: // 胸1（左手）→ 點一次觸發手部觸摸動畫，再點一次取消
                if (_tongueTool) { ToggleLick("L"); break; }   // 選了 👅：改成舔（Tongue.cs）
                ComfortChest = Mathf.Min(100f, ComfortChest + 3f);
                HadBreastTouch = true;
                PlayerDataManagerTouch();
                ToggleHandTouchAnim(true);
                break;
            case 3: // 胸2（右手）→ 點一次觸發手部觸摸動畫，再點一次取消
                if (_tongueTool) { ToggleLick("R"); break; }
                ComfortChest = Mathf.Min(100f, ComfortChest + 3f);
                HadBreastTouch = true;
                PlayerDataManagerTouch();
                ToggleHandTouchAnim(false);
                break;
            case 4: // B：雞雞沒出現 → 用手摸 B（再點一次取消）；按了🍆雞雞出現後 → 雞雞靠近/插入
                if (_jijiPhase == 0 && _tongueTool) ToggleLick("B");   // 選了 👅：舔 B
                else if (_jijiPhase == 0) ToggleBTouch();
                else OnJijiHotspotDown();
                break;
            case 5: // 尻
                ComfortButt = Mathf.Min(100f, ComfortButt + 3f);
                PlayerDataManagerTouch();
                break;
            // case 6/7（腿/膝蓋）：已改成拖曳觸發，不再走 Pressed → OnHotspotClicked 這條路，
            // 邏輯搬到 OnKneeDragStart/OnKneeDragEnd（見上面），這裡故意不留 case。
            case 8: // 足
                ComfortHole = Mathf.Min(200f, ComfortHole + 2f);
                PlayerDataManagerTouch();
                break;
            case 9: // 左手肘 → ArmIK
                if (ArmIKDragEnabled && ArmIK != null && !ArmIK.IsDragging) ArmIK.BeginDrag();
                break;
            case 10: // 右手肘 → RightArmIK
                if (ArmIKDragEnabled && RightArmIK != null && !RightArmIK.IsDragging) RightArmIK.BeginDrag();
                break;
        }

        // 高潮值隨舒服度累積緩慢上升（簡化版）
        float avgComfort = (ComfortMouth + ComfortChest + ComfortButt + ComfortHole * 0.5f) / 4f;
        AddClimax(avgComfort * 0.01f);
    }

    void PlayerDataManagerTouch()
    {
        var pdm = PlayerDataManager.Instance;
        if (pdm == null) return;
        pdm.TodayDidNothing = false;
    }

    // ── 右側快速按鈕：摸了 ────────────────────────────────────────
    /// <summary>
    /// 玩家按「摸了」：記錄本晚有互動（TodayDidNothing=false）、
    /// 累加胸部使用次數，並設定 HadBreastTouch 旗標以便後續對話分支判斷。
    /// </summary>
    void OnTouchedPressed()
    {
        var pdm = PlayerDataManager.Instance;
        if (pdm == null) return;
        pdm.TodayDidNothing = false;
        pdm.AddBreastUse();
        HadBreastTouch = true;
        // 輕微增加胸部舒服度，讓數值面板有視覺回饋
        ComfortChest = Mathf.Min(100f, ComfortChest + 10f);
        UpdateClimaxHud();
        GD.Print("[TrainingRoom] 摸了 → TodayDidNothing=false, BreastUse+1, HadBreastTouch=true");
    }

    // ── 右側快速按鈕：高潮 ────────────────────────────────────────
    /// <summary>
    /// 玩家按「高潮」：記錄本晚有互動且發生高潮（TodayInsertOrgasm=true）、
    /// 觸發立繪痙攣效果，讓流程在 EndToday() 走 InsertOrgasm 分支（最佳結局路線）。
    /// </summary>
    void OnOrgasmPressed()
    {
        var pdm = PlayerDataManager.Instance;
        if (pdm == null) return;
        pdm.TodayDidNothing  = false;
        pdm.TodayInsertOrgasm = true;
        ClimaxLevel = 100f;
        TriggerClimax();
        GD.Print("[TrainingRoom] 高潮 → TodayDidNothing=false, TodayInsertOrgasm=true");
    }

    /// <summary>側邊選單「🍑 插入」快速按鈕：只記錄今天有插入（劇情旗標用），
    /// 實際的插入操作改由滑鼠控制（🍆 → 點 B 靠近 → 在 B 按住突破），見 TrainingRoomRuntime.Jiji.cs。</summary>
    void OnInsertPressed()
    {
        var pdm = PlayerDataManager.Instance;
        if (pdm == null) return;
        pdm.TodayDidNothing = false;
        pdm.TodayInserted = true;
        GD.Print("[TrainingRoom] 插入 → TodayDidNothing=false, TodayInserted=true");
    }

    void FlashHotspot(int idx)
    {
        if (_hotspotImgs == null || idx < 0 || idx >= _hotspotImgs.Length) return;
        var img = _hotspotImgs[idx];
        if (img == null) return;
        img.Modulate = new Color(1f, 0.95f, 0.2f, 0.95f);
        _flashResetTimers[idx] = 0.25f;
    }

    readonly float[] _flashResetTimers = new float[HotspotPos.Length];

    void UpdateHotspotFlash(float dt)
    {
        if (_hotspotImgs == null) return;
        for (int i = 0; i < _flashResetTimers.Length; i++)
        {
            if (_flashResetTimers[i] <= 0f) continue;
            _flashResetTimers[i] -= dt;
            if (_flashResetTimers[i] <= 0f && _hotspotImgs[i] != null)
                _hotspotImgs[i].Modulate = new Color(1f, 1f, 1f, HotspotAlpha);
        }
    }

    // ════════════════════════════════════════════════════════════
    // 手部觸摸動畫（胸部熱點）
    // 移植自 Unity 原版 PlaceHandPersist() / AnimateHand() / CancelPersistHand()。
    // 點一次胸部熱點 → 冒出手部游標並開始持續擺動動畫（同時讓對應胸部圖層跟著
    // 小幅位移／旋轉），再點一次同一邊的熱點 → 取消動畫、手部游標消失、胸部歸位。
    // 右手（胸2）取消時會依累積的 _breastSpeedLevel 判定 BreastComfortRoute，
    // 供後續劇情分支使用，對照 Unity CancelPersistHand() 的邏輯。
    // ════════════════════════════════════════════════════════════

    [Export] public bool ArmIKDragEnabled = false;   // 手臂（熱點 9/10 手肘）拖動：這版不做，預設關；程式碼（ArmIK.BeginDrag 等）都保留，勾起來就恢復

    // ── 胸部熱點 hover 提示游標：小一點、半透明的手 ─────────────────────
    Texture2D _breastHoverCursorLeftTex, _breastHoverCursorRightTex;

    // 胸部／膝蓋綠點的半透明手游標：大小跟著鏡頭縮放（放大 2 倍游標也 2 倍），依大小快取。
    readonly Dictionary<(bool, int), ImageTexture> _hoverCursorCache = new();
    int  _hoverCursorKind;     // 0＝沒在用，1＝左手，2＝右手
    int  _hoverCursorSize;

    [Export] public bool GreenDotCursorEnabled = true;     // 綠點（胸／膝蓋／乳頭／嘴巴）滑過時游標會變；關掉＝游標都不會變（也在右上選單「設置」可切）
    [Export] public float HoverCursorBasePx = 56f;          // 綠點游標的基本大小（px）
    [Export] public bool HoverCursorFollowZoom = false;     // 綠點游標大小跟著鏡頭縮放（預設關：固定大小，滾輪縮放時不會卡頓）
    int HoverCursorSizeNow() => HoverCursorFollowZoom
        ? Mathf.Clamp(Mathf.RoundToInt(HoverCursorBasePx * 1.4f * _camZoom), 16, 250)   // 系統游標上限約 256
        : Mathf.Clamp(Mathf.RoundToInt(HoverCursorBasePx), 16, 250);

    bool _lastBreastHoverLeft;
    bool _hoverIsChest = true;   // 目前這個 hover 游標是胸部的（要套 BreastHoverCursorScale）還是膝蓋的

    void ApplyBreastHoverCursor(bool isLeft, bool chest = true)
    {
        _lastBreastHoverLeft = isLeft; _hoverIsChest = chest;
        if (!GreenDotCursorEnabled || ClothDragActive) return;   // 正在拖衣服（肩帶／上半部／下擺）：滑過綠點也不換游標
        // 抽插中按住左鍵拖曳（控制小穴速度）時，滑過綠點也不換游標
        if (_jijiPhase == 3 && _jijiStage >= 1 && Input.IsMouseButtonPressed(MouseButton.Left) && !_speedOwnerBreast) return;
        int size = chest ? Mathf.Max(8, Mathf.RoundToInt(HoverCursorSizeNow() * BreastHoverCursorScale)) : HoverCursorSizeNow();
        if (HoverTipEnabled && _hoverTipOn) HoverTipShow(chest ? HoverTipTexts[isLeft ? 2 : 3] : HoverTipTexts[isLeft ? 6 : 7]);   // 離開奶頭回到胸／腿的提示
        size = Mathf.Max(8, Mathf.RoundToInt(size * HandNormK(isLeft)));
        if (!_hoverCursorCache.TryGetValue((isLeft, size), out var cached) || cached == null)
        {
            var tex = ResourceHelper.LoadTexAny(isLeft ? "Sprites/左手" : "Sprites/右手");
            if (tex == null) return;
            var img = tex.GetImage();
            if (img.IsCompressed()) img.Decompress();
            float maxSide = Mathf.Max(img.GetWidth(), img.GetHeight());
            float scale = size / maxSide;
            int w = Mathf.Max(1, Mathf.RoundToInt(img.GetWidth() * scale));
            int h = Mathf.Max(1, Mathf.RoundToInt(img.GetHeight() * scale));
            img.Resize(w, h, Image.Interpolation.Lanczos);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = img.GetPixel(x, y);
                    c.A *= BreastHoverCursorAlpha;
                    img.SetPixel(x, y, c);
                }
            cached = ImageTexture.CreateFromImage(img);
            _hoverCursorCache[(isLeft, size)] = cached;
        }
        _hoverCursorKind = isLeft ? 1 : 2;
        _hoverCursorSize = HoverCursorSizeNow();   // 存未縮放的值，鏡頭縮放改變才重算
        var hotspot = new Vector2(cached.GetWidth() / 2f, cached.GetHeight() / 2f);
        Input.SetCustomMouseCursor(cached, Input.CursorShape.Arrow, hotspot);
    }

    /// <summary>每幀（Camera.cs）：鏡頭縮放改變時，如果正在顯示綠點的半透明手游標，換成對應大小。</summary>
    void RefreshHoverCursorForZoom()
    {
        if (_hoverCursorKind == 0 || _hoverCursorKind == 4 || !HoverCursorFollowZoom) return;   // 4＝奶頭 hover 游標（固定大小）；固定大小模式不用重算
        if (Mathf.Abs(HoverCursorSizeNow() - _hoverCursorSize) >= 3)
        {
            if (_hoverCursorKind == 3) ApplyTongueCursor();   // 舌頭游標（Tongue.cs）
            else ApplyBreastHoverCursor(_hoverCursorKind == 1, _hoverIsChest);
        }
    }

    void ToggleHandTouchAnim(bool isLeft)
    {
        if (!HandAnimEnabled) return;
        bool running = isLeft ? _touchAnimRunningLeft : _touchAnimRunningRight;
        if (running) CancelHandTouchAnim(isLeft);
        else StartHandTouchAnim(isLeft);
    }

    // UpdateClimaxTouchGrowth()：已改寫成速度×高潮值的連續曲線，移到 TrainingRoomRuntime.Arousal.cs。

    void StartHandTouchAnim(bool isLeft)
    {
        if (isLeft && _touchAnimRunningLeft) return;
        if (!isLeft && _touchAnimRunningRight) return;

        var tex = ResourceHelper.LoadTexAny(isLeft ? "Sprites/左手" : "Sprites/右手");
        if (tex == null) return;
        MakeRoomForHand(isLeft ? "L" : "R");   // 手只有 2 隻：第 3 個出現時取代最早的
        if (isLeft) _ordL = ++_handSeq; else _ordR = ++_handSeq;

        // 手部游標跟熱點掛在同一個 _canvas 座標系下（比照 BuildHotspots() 的 FullRect
        // 中介層寫法），Unity 原始的 HandLeftInitPos/HandRightInitPos/HandSize 數值
        // 因此可以直接照抄，不需要另外換算。
        if (_touchHandLayer == null)
        {
            _touchHandLayer = new Control { Name = "TouchHandLayer", MouseFilter = Control.MouseFilterEnum.Ignore };
            _touchHandLayer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _canvas.AddChild(_touchHandLayer);
        }

        var handCtrl = new TextureRect
        {
            Name = isLeft ? "TouchHand_Left" : "TouchHand_Right",
            Texture = tex,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var basePos = isLeft ? HandLeftInitPos : HandRightInitPos;
        // 使用者要求手部貼圖 Y 軸壓縮一點，用 HandYSquash（<1）縮小顯示高度，寬度不變；
        // 每一幀 AnimateHandTouch() 更新位置時也要用同一個尺寸，不能改回 (HandSize,HandSize)，
        // 否則壓縮效果下一幀就被蓋掉。
        var handSize = new Vector2(HandSize, HandSize * HandYSquash) * HandNormK(isLeft);
        MainMenuRuntime.ApplyPointAnchor(handCtrl, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), basePos, handSize);
        // 手部自己的旋轉樞紐設在自己貼圖中心，避免旋轉時位置跑掉（跟胸部/眼睛的樞紐點是同一種問題）。
        handCtrl.PivotOffset = handCtrl.Size / 2f;
        // 使用者要求手部貼圖右下角漸層半透明＋加一點黑邊，讓貼在胸口上時比較有立體感、
        // 輪廓也更清楚。
        ApplyHandTouchVisualMaterial(handCtrl);
        _touchHandLayer.AddChild(handCtrl);

        if (isLeft) { _touchHandLeft = handCtrl; _touchAnimRunningLeft = true; }
        else        { _touchHandRight = handCtrl; _touchAnimRunningRight = true; }

        TaskLogger.Run(AnimateHandTouch(handCtrl, basePos, isLeft),
            isLeft ? "TrainingRoom.AnimateHandTouch.Left" : "TrainingRoom.AnimateHandTouch.Right");
    }

    static Shader _handTouchShader;

    /// <summary>手部觸摸貼圖的視覺效果：右下角漸層變半透明＋邊緣加一點黑色描邊。
    /// 用 canvas_item fragment shader 統一處理，左手／右手（左手.png/右手.png）共用同一份邏輯，
    /// 不用另外修圖。左右手每次冒出來都是新的 TextureRect，所以材質也是即用即建，
    /// 只共用編譯好的 Shader 本體（_handTouchShader 是 static，只建一次）。</summary>
    void ApplyHandTouchVisualMaterial(TextureRect ctrl)
    {
        if (_handTouchShader == null)
        {
            _handTouchShader = new Shader
            {
                Code = @"
shader_type canvas_item;

uniform float corner_fade_alpha : hint_range(0.0, 1.0) = 0.35;
uniform vec4 outline_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
uniform float outline_width = 1.5;
uniform vec4 char_rect = vec4(0.0, 0.0, 1.0, 1.0);
uniform bool top_enabled = false;
uniform float top_height = 0.10;
uniform float top_softness = 0.01;
uniform float top_tilt = -0.12;
uniform float top_offset = 0.0;
uniform vec4 top_color : source_color = vec4(1.0);
uniform bool right_enabled = false;
uniform float right_start = 0.5;
uniform float right_softness = 0.45;
uniform vec4 right_color : source_color = vec4(1.0);

vec3 shade_mul(vec2 suv) {
    vec2 cuv = (suv - char_rect.xy) / max(char_rect.zw - char_rect.xy, vec2(0.0001));
    vec3 mul = vec3(1.0);
    if (top_enabled) {
        float edge = top_height + top_offset + top_tilt * (cuv.x - 0.5);
        float t = 1.0 - smoothstep(edge - top_softness, edge + top_softness, cuv.y);
        mul *= mix(vec3(1.0), top_color.rgb, t);
    }
    if (right_enabled) {
        float r = 1.0 - smoothstep(right_start - max(right_softness, 0.0001), right_start, cuv.x);
        mul *= mix(vec3(1.0), right_color.rgb, r);
    }
    return mul;
}

varying vec4 vc;
void vertex() { vc = COLOR; }   // 頂點色×Modulate（含父節點的夜晚濾鏡）；fragment 的 COLOR 已經乘過貼圖，不能拿來用

void fragment() {
    vec4 mod_color = vc;
    vec4 tex_color = texture(TEXTURE, UV);

    // 右下角漸層變半透明：越靠近右下角（UV.x+UV.y 越接近 2），alpha 越往
    // corner_fade_alpha 收斂；左上角、中間大致不受影響。
    float corner = clamp((UV.x + UV.y) - 1.0, 0.0, 1.0) * 2.0;
    tex_color.a *= mix(1.0, corner_fade_alpha, corner);

    // 黑邊：自己是透明像素，但緊鄰（上下左右＋四個對角）有不透明像素時，畫成黑色描邊，
    // 讓手的輪廓在胸口貼圖上更明顯。
    bool is_outline = false;
    if (outline_width > 0.0 && tex_color.a < 0.5) {
        vec2 texel = TEXTURE_PIXEL_SIZE * outline_width;
        float neighbor = 0.0;
        neighbor = max(neighbor, texture(TEXTURE, UV + vec2(texel.x, 0.0)).a);
        neighbor = max(neighbor, texture(TEXTURE, UV - vec2(texel.x, 0.0)).a);
        neighbor = max(neighbor, texture(TEXTURE, UV + vec2(0.0, texel.y)).a);
        neighbor = max(neighbor, texture(TEXTURE, UV - vec2(0.0, texel.y)).a);
        neighbor = max(neighbor, texture(TEXTURE, UV + texel).a);
        neighbor = max(neighbor, texture(TEXTURE, UV - texel).a);
        neighbor = max(neighbor, texture(TEXTURE, UV + vec2(texel.x, -texel.y)).a);
        neighbor = max(neighbor, texture(TEXTURE, UV + vec2(-texel.x, texel.y)).a);
        is_outline = neighbor > 0.5;
    }

    // Godot 不允許在 fragment() 裡用 return，改成 if/else
    if (is_outline) {
        COLOR = vec4(outline_color.rgb * shade_mul(SCREEN_UV) * mod_color.rgb, mod_color.a);
    } else {
        tex_color.rgb *= shade_mul(SCREEN_UV);
        COLOR = tex_color * mod_color;
    }
}
"
            };
        }

        var mat = new ShaderMaterial { Shader = _handTouchShader };
        mat.SetShaderParameter("corner_fade_alpha", HandTouchCornerFadeAlpha);
        mat.SetShaderParameter("outline_width", HandTouchOutlineWidthPx);
        ctrl.Material = mat;
        SyncHandShade();
    }

    [Export] public bool HandMirrorRight = true;   // 兩隻摸胸的手動作鏡像（右手擺動／旋轉方向相反，圖不翻轉）
    static readonly string[] _shadeParamNames = { "char_rect","top_enabled","top_height","top_softness","top_tilt","top_offset","top_color","right_enabled","right_start","right_softness","right_color" };

    /// <summary>把人物光影濾鏡（頭頂／右側）的參數複製到摸胸的手，讓手一起被濾鏡影響。</summary>
    void SyncHandShade()
    {
        if (_charShadeMat == null) return;
        foreach (var h in new Control[] { _touchHandLeft, _touchHandRight, _patHand })
        {
            if (h == null || !IsInstanceValid(h) || h.Material is not ShaderMaterial m) continue;
            foreach (var n in _shadeParamNames) m.SetShaderParameter(n, _charShadeMat.GetShaderParameter(n));
        }
    }

    void CancelHandTouchAnim(bool isLeft)
    {
        // 使用者反應裝反了：左手動的其實是畫面右邊那顆胸部，所以這裡的對應關係要對調
        // （原本 isLeft→胸1、!isLeft→胸2，現在改成 isLeft→胸2、!isLeft→胸1）。
        string breastLayer = isLeft ? "胸2" : "胸1";
        TouchAnimOffset.Remove(breastLayer);
        if (Layers.TryGetValue(breastLayer, out var breastCtrl) && breastCtrl != null)
            breastCtrl.RotationDegrees = 0f;
        string nippleLayerC = breastLayer.Replace("胸", "奶頭");
        TouchAnimOffset.Remove(nippleLayerC);
        if (Layers.TryGetValue(nippleLayerC, out var nipCtrlC) && nipCtrlC != null)
            nipCtrlC.RotationDegrees = 0f;

        if (isLeft)
        {
            _touchAnimRunningLeft = false;
            if (_touchHandLeft != null) { _touchHandLeft.QueueFree(); _touchHandLeft = null; }
        }
        else
        {
            _touchAnimRunningRight = false;
            if (_touchHandRight != null) { _touchHandRight.QueueFree(); _touchHandRight = null; }

            // 對應 Unity CancelPersistHand()：右手（胸2）放開時依累積的摸胸速度等級
            // 判定本次摸胸走「舒服」還是「不舒服」路線，供後續劇情分支判斷。
            HadBreastTouch = true;
            BreastComfortRoute = _breastSpeedLevel >= BreastSpeedLevelMax * 0.5f;
            _breastSpeedLevel = 0f;
        }
    }

    /// <summary>
    /// 持續手部觸摸動畫主迴圈，移植自 Unity IEnumerator AnimateHand()。
    /// 用 sin/cos 波形驅動手部位置＋旋轉，按住滑鼠左鍵時加速（HandAnimHoldUpMult），
    /// 放開時減速（HandAnimHoldDownMult）；右手（胸2）另外依滑鼠垂直移動速度累積
    /// _breastSpeedLevel，供 CancelHandTouchAnim() 判斷舒服/不舒服路線。
    /// 手部擺動的同時，把等比例的位移／旋轉透過 TouchAnimOffset／RotationDegrees
    /// 疊加到對應的胸部圖層上，做出「手推著胸部一起擺動」的聯動效果——不能直接寫
    /// 胸部的 Position，會跟 UpdateBreathing() 每幀寫入的呼吸位移互搶（見 Effects.cs）。
    /// </summary>
    async System.Threading.Tasks.Task AnimateHandTouch(Control handCtrl, Vector2 basePos, bool isLeft)
    {
        // 跟 CancelHandTouchAnim() 同一個對調（左手→胸2、右手→胸1），修正「裝反了」的問題。
        string breastLayer = isLeft ? "胸2" : "胸1";
        Vector2 breastMove = isLeft ? BreastAnimMoveLeft : BreastAnimMoveRight;
        float breastRot    = isLeft ? BreastAnimRotLeft  : BreastAnimRotRight;
        // 跟 StartHandTouchAnim() 建立時用同一個壓縮後尺寸，每幀更新位置時才不會把
        // Y 軸壓縮效果蓋回原本的正方形。
        var handSize = new Vector2(HandSize, HandSize * HandYSquash) * HandNormK(isLeft);

        float t = 0f;
        while (IsInstanceValid(handCtrl) && (isLeft ? _touchAnimRunningLeft : _touchAnimRunningRight))
        {
            float dt = (float)GetProcessDeltaTime();
            if (dt <= 0f) dt = 1f / 60f;

            // sin(t) > 0 代表手正處於「往上」那半段波形（cos(t) 正在往下降，
            // Godot Y 向下為正，所以 offset 減少＝畫面上往上移動）；沒按住滑鼠時
            // 維持原速（倍率 1），不做上下差異化。
            bool holding = Input.IsMouseButtonPressed(MouseButton.Left);
            float speedMult = 1f;
            if (holding)
            {
                bool movingUp = Mathf.Sin(t) > 0f;
                speedMult = movingUp ? HandAnimHoldUpMult : HandAnimHoldDownMult;
            }
            t += dt * HandAnimSpeed * speedMult * TouchLevelMult;   // 等級越高手動得越快（Unity levelMult）

            float wave = Mathf.Sin(t);
            float mir = (!isLeft && HandMirrorRight) ? -1f : 1f;   // 鏡像：右手 X 位移與旋轉方向相反
            float angle = Mathf.Lerp(HandAnimAngleMin, HandAnimAngleMax, (wave + 1f) * 0.5f) * mir;
            // 手要跟著人物／胸一起上下（抽插時全身被推動 BodyShake、M腿時的 ChestLift）：畫布單位 × 螢幕縮放，Y 反向（ApplyPointAnchor 正＝往上）
            Vector2 follow = (BodyShake + ChestLift) * CharScreenScale();
            var pos = basePos + new Vector2(wave * HandAnimMoveX * mir, Mathf.Cos(t) * HandAnimMoveY) + new Vector2(follow.X, -follow.Y);
            MainMenuRuntime.ApplyPointAnchor(handCtrl, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, handSize);
            handCtrl.RotationDegrees = angle;

            TouchAnimOffset[breastLayer] = new Vector2(wave * breastMove.X, Mathf.Cos(t) * breastMove.Y);
            if (Layers.TryGetValue(breastLayer, out var breastCtrl) && breastCtrl != null)
                breastCtrl.RotationDegrees = wave * breastRot;

            // 奶頭跟著同一側的胸一起位移、旋轉（軸心沿用胸的），停止時在 CancelHandTouchAnim 復位
            string nippleLayer = breastLayer.Replace("胸", "奶頭");
            TouchAnimOffset[nippleLayer] = TouchAnimOffset[breastLayer];
            if (Layers.TryGetValue(nippleLayer, out var nipCtrl) && nipCtrl != null)
            {
                if (breastCtrl != null) nipCtrl.PivotOffset = breastCtrl.PivotOffset;
                nipCtrl.RotationDegrees = wave * breastRot;
            }

            // 只有右手（胸2）累積摸胸速度等級，依滑鼠垂直方向移動速度疊加，
            // 對照 Unity 用滑鼠速度判斷「摸得快不快」來決定後續走舒服/不舒服路線。
            // 速度等級改由 Arousal.cs 的 UpdateClimaxTouchGrowth() 每幀統一更新
            // （按住左鍵往上拖加速、往下拖減速，兩隻手＋摸 B 共用同一個等級，對照 Unity）。

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
}
