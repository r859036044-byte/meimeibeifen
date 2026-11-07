using Godot;

/// <summary>
/// TrainingRoomRuntime — 玩法串接：高潮值（ClimaxLevel）的兩階段流程。
///
/// 第一階段（前戲）：用手摸胸（胸1/胸2 熱點）、摸 B（沒按🍆、雞雞沒出現時點 B 熱點）
///   → 依「撫摸速度」跟「目前高潮值」的契合度持續加減高潮值，滿 100 高潮。
/// 第二階段（插入）：高潮過一次之後再插入突破，才是正確流程；
///   還沒高潮過就硬突破 → 可以突破，但高潮值扣一大段（BreakthroughNoClimaxPenalty）；
///   如果扣下去會變成負的（高潮值 < 扣的量）→ ClimaxGainLocked，這次在調教室裡高潮值不會再增加。
///   突破後的抽插（深插振盪）也用同一條曲線加減高潮值。
///
/// 撫摸速度（對照 Unity AnimateHand 的等級系統）：
///   摸的時候「按住左鍵往上拖＝加速、往下拖＝減速」，速度等級 0~BreastSpeedLevelMax，
///   手的動畫速度也會跟著等級變快（1 + 等級 × BreastSpeedPerLevel）。
///   抽插時則用深插振盪自己的速度等級（_s2SpeedLv）。
///
/// 加減曲線（連續曲線，不是 if/else 分段）：
///   s  = 速度（0~1，等級 / 上限）
///   x  = 高潮值 / 100
///   偏好速度 p(x) = Lerp(ArousalPrefSpeedLow, ArousalPrefSpeedHigh, smoothstep(x))
///        → 高潮值低時喜歡輕柔（p 小），高潮值高時喜歡激烈（p 大），中間平滑過渡
///   契合度  m = exp(-((s - p) / ArousalMatchWidth)²)       （高斯鐘形，s 越接近 p 越接近 1）
///   每秒變化 = 基礎速率 × (m × (1 + 懲罰比例) − 懲罰比例)
///        → 完全契合時 +基礎速率；差很遠時 −基礎速率 × 懲罰比例；中間連續變化
///   所以低高潮值時：輕輕摸上升、摸太快反而下降；高潮值高時反過來。
/// </summary>
public partial class TrainingRoomRuntime
{
    // ── Export：加減曲線 ─────────────────────────────────────────────
    [Export] public float ArousalPrefSpeedLow   = 0.15f;  // 高潮值 0 時最舒服的速度（0~1）
    [Export] public float ArousalPrefSpeedHigh  = 0.85f;  // 高潮值 100 時最舒服的速度（0~1）
    [Export] public float ArousalMatchWidth     = 0.28f;  // 契合曲線寬度（越大越寬容）
    [Export] public float ArousalPenaltyRatio   = 0.6f;   // 速度完全不對時，扣的速率 = 基礎速率 × 這個比例
    // ── Export：各動作的基礎速率（每秒高潮值，完全契合時）──────────────
    [Export] public float ArousalRateBreast     = 1.2f;   // 每隻手摸胸
    [Export] public float ArousalRateB          = 2.0f;   // 摸 B
    [Export] public float ArousalRateInsert     = 2.5f;   // 突破後抽插
    // ── Export：流程 ─────────────────────────────────────────────────
    [Export] public float BreakthroughNoClimaxPenalty = 30f;  // 還沒高潮過就突破：扣多少高潮值
    [Export] public float ClimaxAfterLevel      = 0f;     // 高潮後穴高潮值回到多少（使用者設計：歸 0，濕度不變）
    // ── Export：摸 B 的手 ────────────────────────────────────────────
    [Export] public float BTouchMoveX           = 5f;
    [Export] public float BTouchMoveY           = 3f;
    [Export] public Vector2 BTouchHandOffset    = new Vector2(10f, 20f);   // 手相對 B 熱點中心（螢幕 px）

    /// <summary>這次進調教室是否已經高潮過（高潮過再突破才是正確流程）。</summary>
    public bool HadClimaxThisSession { get; private set; }

    /// <summary>還沒高潮就硬突破、而且扣完變成負的 → 這次在調教室裡高潮值不會再增加（只會維持或減少）。</summary>
    public bool ClimaxGainLocked { get; private set; }

    /// <summary>目前每秒高潮值變化（給 DEV 面板看）。</summary>
    public float ArousalRateNow { get; private set; }

    TextureRect _bTouchHand;
    bool _bTouchRunning;
    Vector2 _touchLastMouse;

    // ════════════════════════════════════════════════════════════════
    //  曲線
    // ════════════════════════════════════════════════════════════════

    /// <summary>速度 s（0~1）在目前高潮值下的契合係數：+1（最舒服）~ −ArousalPenaltyRatio（最不舒服）。</summary>
    public float ArousalCurve(float s)
    {
        float x = Mathf.Clamp(ClimaxLevel / 100f, 0f, 1f);
        float sm = x * x * (3f - 2f * x);                       // smoothstep
        float pref = Mathf.Lerp(ArousalPrefSpeedLow, ArousalPrefSpeedHigh, sm);
        float d = (Mathf.Clamp(s, 0f, 1f) - pref) / Mathf.Max(ArousalMatchWidth, 0.01f);
        float match = Mathf.Exp(-d * d);
        return match * (1f + ArousalPenaltyRatio) - ArousalPenaltyRatio;
    }

    /// <summary>加減高潮值；滿 100 且目前沒在高潮中 → 觸發高潮。</summary>
    public void AddClimax(float delta)
    {
        if (delta > 0f && ClimaxGainLocked) return;   // 被鎖住：不會再增加
        ClimaxLevel = Mathf.Clamp(ClimaxLevel + delta, 0f, 100f);
        if (ClimaxLevel >= 100f && !_spasmsRunning) TriggerClimax();
    }

    /// <summary>由 TriggerClimax() 呼叫：記錄已高潮、高潮值回到 ClimaxAfterLevel。</summary>
    void OnClimaxReached()
    {
        HadClimaxThisSession = true;
        ClimaxLevel = ClimaxAfterLevel;
        ReactClimax();   // 高潮反應（Play.cs）
        ShowEmote(EmoteBubble.Kind.Exclaim);
    }

    // ════════════════════════════════════════════════════════════════
    //  每幀：撫摸速度等級 + 高潮值增減（取代原本的「每幾秒 +1」）
    // ════════════════════════════════════════════════════════════════

    void UpdateClimaxTouchGrowth(float dt)
    {
        Vector2 mouseNow = GetViewport().GetMousePosition();
        float upPx = -(mouseNow.Y - _touchLastMouse.Y);
        _touchLastMouse = mouseNow;

        int breastHands = (_touchAnimRunningLeft ? 1 : 0) + (_touchAnimRunningRight ? 1 : 0);
        breastHands += NippleHandCount;   // 摸乳頭也算摸胸（Nipple.cs）
        bool anyTouch = breastHands > 0 || _bTouchRunning;

        // 撫摸速度等級：按住左鍵往上拖加速、往下拖減速（對照 Unity AnimateHand）
        // 速度各管各的：抽插中，左鍵按下時若游標在摸胸的手附近 → 這次拖曳只控制胸；否則只控制抽插（小穴）
        bool leftNow = Input.IsMouseButtonPressed(MouseButton.Left);
        bool thrusting = _jijiPhase == 3 && _jijiStage >= 1;
        if (leftNow && !_speedLeftPrev)
        {
            bool nearHand = false;
            foreach (var h in new[] { _touchHandLeft, _touchHandRight })
                if (h != null && IsInstanceValid(h) && h.GetGlobalRect().Grow(60f).HasPoint(mouseNow)) nearHand = true;
            _speedOwnerBreast = !thrusting || nearHand;
        }
        _speedLeftPrev = leftNow;
        if (thrusting && !leftNow) _speedOwnerBreast = false;   // 抽插中沒按著時預設歸抽插，避免一直卡在胸
        if (anyTouch && leftNow && !_patDragOwner && !_tongueDragOwner && (!thrusting || _speedOwnerBreast))   // 在頭上拖曳只調摸頭速度
            _breastSpeedLevel = Mathf.Clamp(_breastSpeedLevel + upPx * BreastSpeedLevelRate, 0f, BreastSpeedLevelMax);

        float rate = UpdatePlayValues(dt, breastHands);   // 濕度／各部位高潮值／穴高潮值（Play.cs）

        ArousalRateNow = rate;
        if (rate != 0f && !_spasmsRunning) AddClimax(rate * dt);
    }

    /// <summary>手部動畫的速度倍率（等級越高手動得越快，對照 Unity levelMult）。</summary>
    bool _speedLeftPrev = false;
    bool _speedOwnerBreast = true;   // 這次左鍵拖曳控制的是胸（true）還是抽插（false）
    float TouchLevelMult => 1f + _breastSpeedLevel * BreastSpeedPerLevel;

    // ════════════════════════════════════════════════════════════════
    //  摸 B（雞雞沒出現時點 B 熱點：出現一隻手持續撫摸，再點一次取消）
    // ════════════════════════════════════════════════════════════════

    void ToggleBTouch()
    {
        if (_bTouchRunning) CancelBTouch();
        else StartBTouch();
    }

    // ── 手只有 2 隻（左手／右手）：胸1、胸2、摸 B 三者最多同時 2 個，第 3 個出現時最早觸發的那個被取代 ──
    int _handSeq, _ordL, _ordR, _ordB;
    bool _bSlotLeft = false;   // 摸 B 用的是「左手」那一隻 → 游標手水平翻轉

    /// <summary>新手（kind: "L"／"R"／"B"）要出現前呼叫：已有 2 隻在用就取消最早的。回傳被取消的 kind（沒有則 null）。</summary>
    string MakeRoomForHand(string kind)
    {
        // 同一隻手不能同時做兩件事：摸頭的手跟新的摸胸手同一側 → 取消摸頭
        if ((kind == "L" || kind == "R") && _patOn && _patSlotLeft == (kind == "L")) CancelPat();
        if (kind == "L" || kind == "R") CancelNipplesInSlot(kind == "L");
        // 同一隻手不能同時做兩件事：摸 B 的手跟新出現的摸胸手同一側 → 摸 B 換到另一隻手（另一隻也被用就直接取消摸 B）
        if (kind != "B" && _bTouchRunning && _bSlotLeft == (kind == "L"))
        {
            bool otherBusy = kind == "L" ? _touchAnimRunningRight : _touchAnimRunningLeft;
            if (otherBusy || _handMode == HandMode.Left || _handMode == HandMode.Right) CancelBTouch();   // 玩家指定了手就不自動換手
            else
            {
                _bSlotLeft = !_bSlotLeft;
                if (_bTouchHand != null && IsInstanceValid(_bTouchHand))
                {
                    var th = (TextureRect)_bTouchHand;
                    th.FlipH = _bSlotLeft;
                    th.PivotOffset = th.Size * BTipRatio;
                }
            }
        }
        var act = new System.Collections.Generic.List<(int ord, string k)>();
        if (_touchAnimRunningLeft  && kind != "L") act.Add((_ordL, "L"));
        if (_touchAnimRunningRight && kind != "R") act.Add((_ordR, "R"));
        if (_bTouchRunning         && kind != "B") act.Add((_ordB, "B"));
        if (_patOn                 && kind != "H") act.Add((_ordH, "H"));
        if (_nipL.On               && kind != "NL") act.Add((_nipL.Ord, "NL"));
        if (_nipR.On               && kind != "NR") act.Add((_nipR.Ord, "NR"));
        if (act.Count < 2) return null;
        act.Sort((x, y) => x.ord.CompareTo(y.ord));
        string first = act[0].k;
        if (first == "L") CancelHandTouchAnim(true);
        else if (first == "R") CancelHandTouchAnim(false);
        else if (first == "H") CancelPat();
        else if (first == "NL" || first == "NR") CancelNipple(first);
        else CancelBTouch();
        return first;
    }

    Vector2 BTipRatio => _bSlotLeft ? new Vector2(1f - RoomCursorTipRatio.X, RoomCursorTipRatio.Y) : RoomCursorTipRatio;

    void StartBTouch()
    {
        if (_bTouchRunning) return;
        // 決定摸 B 用哪一隻手：取代最早的那隻；只有 1 隻在用就用空的那隻；都沒用就用右手
        if (_handMode == HandMode.Left || _handMode == HandMode.Right)
        {
            // 玩家用左下角按鈕指定了手：摸 B 就用那一隻；那隻手正在摸胸就被取代
            _bSlotLeft = _handMode == HandMode.Right;   // 按鈕與實際手相反（左下角「左手」鈕＝右手那隻）
            if (_bSlotLeft && _touchAnimRunningLeft)   CancelHandTouchAnim(true);
            if (!_bSlotLeft && _touchAnimRunningRight) CancelHandTouchAnim(false);
            if (_patOn && _patSlotLeft == _bSlotLeft)  CancelPat();
            CancelNipplesInSlot(_bSlotLeft);
        }
        else
        {
        bool leftBusy  = SlotBusy(true, "B");
        bool rightBusy = SlotBusy(false, "B");
        string replaced = MakeRoomForHandPeek("B", out bool? oldSlotB);
        if (replaced != null) _bSlotLeft = oldSlotB ?? false;
        else if (leftBusy && !rightBusy) _bSlotLeft = false;
        else if (rightBusy && !leftBusy) _bSlotLeft = true;
        else _bSlotLeft = false;
        }
        _ordB = ++_handSeq;
        // 使用者要求：摸 B 不要用摸胸那隻手，改用「游標」那隻手（食指），在洞口的位置蹭
        var tex = ResourceHelper.LoadTexAny("Sprites/游標");
        if (tex == null) return;
        if (_touchHandLayer == null)
        {
            _touchHandLayer = new Control { Name = "TouchHandLayer", MouseFilter = Control.MouseFilterEnum.Ignore };
            _touchHandLayer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _canvas.AddChild(_touchHandLayer);
        }
        float sz = BTouchCursorSize;
        var hand = new TextureRect
        {
            Name = "TouchHand_B",
            Texture = tex,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Size = new Vector2(sz, sz * tex.GetHeight() / Mathf.Max(1f, tex.GetWidth())),
            FlipH = _bSlotLeft,   // 用左手時游標手左右反轉
        };
        hand.PivotOffset = hand.Size * BTipRatio;   // 以指尖為軸心微微轉動
        _touchHandLayer.AddChild(hand);
        _bTouchHand = hand;
        _bTouchRunning = true;
        HadBreastTouch = true;
        PlayerDataManagerTouch();
        TaskLogger.Run(AnimateBTouch(hand), "TrainingRoom.AnimateBTouch");
    }

    [Export] public float BTouchCursorSize = 96f;   // 80 → 96（放大 20%）   // 摸 B 的游標手大小（場景 px，跟著鏡頭縮放）
    [Export] public float BTouchHandDownPx = 21f;       // 一般腿：點 B 出現的手再往下多少 px
    [Export] public float BTouchMLegHandDownPx = 8f;   // M 腿：點小穴出現的手再往下多少 px
    [Export] public float BTouchMLegPussyDeformAmount = 0.3f;   // M 腿：小穴中心變形幅度（比一般腿大）
    [Export] public float BTouchPussyDeformAmount = 0.12f;   // 蹭的時候小穴中心的小變形幅度（越小越不明顯）
    float _bTouchPulse = 0f;   // 0~1：手蹭到最低時 = 1，用來同步小穴／洞的小變形

    /// <summary>小穴中心的小變形：目前可見的那張小穴（一般腿 / M 腿）套 a，另一張歸零。</summary>
    void SetPussyDeform(float a)
    {
        string on  = _mLegOpen ? "m腿/小穴" : "腿/小穴";
        string off = _mLegOpen ? "腿/小穴" : "m腿/小穴";
        if (_mLegDragStretchMaterials.TryGetValue(on, out var m1) && m1 != null) m1.SetShaderParameter("expand_amount", a);
        if (_mLegDragStretchMaterials.TryGetValue(off, out var m2) && m2 != null) m2.SetShaderParameter("expand_amount", 0f);
    }

    /// <summary>蹭的中心點：M 腿時是 M 小穴的洞口，一般腿時是小穴中間（場景座標）。</summary>
    Vector2 PussyRubPoint()
    {
        if (Layers.TryGetValue("m腿/小穴", out var m) && m != null && m.IsVisibleInTree())
            return m.GetGlobalTransform() * new Vector2(979f, 1455f);
        if (Layers.TryGetValue("腿/小穴", out var n) && n != null)
            return n.GetGlobalTransform() * new Vector2(976f, 1497f);
        var h4 = (_hotspotRTs != null && _hotspotRTs.Length > 4) ? _hotspotRTs[4] : null;
        return h4 != null ? h4.GetGlobalRect().GetCenter() : Vector2.Zero;
    }

    void CancelBTouch()
    {
        _bTouchRunning = false;
        _bTouchPulse = 0f;
        SetPussyDeform(0f);
        if (_bTouchHand != null && IsInstanceValid(_bTouchHand)) _bTouchHand.QueueFree();
        _bTouchHand = null;
    }

    async System.Threading.Tasks.Task AnimateBTouch(TextureRect hand)
    {
        float t = 0f;
        while (_bTouchRunning && IsInstanceValid(hand))
        {
            float dt = (float)GetProcessDeltaTime();
            if (dt <= 0f) dt = 1f / 60f;
            bool holding = Input.IsMouseButtonPressed(MouseButton.Left);
            float speedMult = holding ? (Mathf.Sin(t) > 0f ? HandAnimHoldUpMult : HandAnimHoldDownMult) : 1f;
            t += dt * HandAnimSpeed * speedMult * TouchLevelMult;

            // 指尖在洞口上下左右小幅度蹭（主要上下、帶一點左右）
            float handDown = _mLegOpen ? BTouchMLegHandDownPx : BTouchHandDownPx;
            Vector2 tip = PussyRubPoint()
                        + new Vector2(Mathf.Sin(t * 0.5f) * BTouchMoveX * 0.5f, Mathf.Sin(t) * BTouchMoveY + handDown);
            // 手蹭到最低（sin(t)=1）時小穴中心變形最大，跟手同步
            _bTouchPulse = Mathf.Max(0f, Mathf.Sin(t));
            SetPussyDeform(_bTouchPulse * (_mLegOpen ? BTouchMLegPussyDeformAmount : BTouchPussyDeformAmount));
            Vector2 tipOffset = hand.Size * BTipRatio;
            hand.GlobalPosition = tip - tipOffset + new Vector2(_bSlotLeft ? 2f : 0f, 0f);   // 左右反轉的手再往右 2px
            hand.RotationDegrees = Mathf.Sin(t) * 4f * (_bSlotLeft ? -1f : 1f);

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
}
