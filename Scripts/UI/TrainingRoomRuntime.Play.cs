using Godot;
using System.Collections.Generic;

/// <summary>
/// 玩法數值（使用者設計）：
///   • 濕度（PlayerDataManager.CurrentVaginaWetness，0~100）：摸胸／乳頭／親嘴／摸頭／摸 B／舔都會提升濕度，
///     抽插時也會繼續提升；什麼都沒做時慢慢下降。高潮後濕度「不會」歸零。
///   • 各部位高潮值（胸／乳頭／嘴／頭，0~100）：被摸的部位依速度契合度上升，沒摸慢慢下降。
///     部位高潮值越高 → 這個部位提升濕度越快；抽插時，正在被弄的部位還會依它的高潮值加快穴高潮值上升。
///   • 穴高潮值（ClimaxLevel，0~100）：摸 B／舔 B／抽插時上升；滿了就高潮，高潮後歸 0。
///     抽插時濕度越低上升越慢；突破時濕度不夠（BreakWetNeed）→ 穴高潮值依乾的程度下降。
///   • 點擊反應：開始任何互動、互動進行中每隔幾秒、突破、高潮時，女主頭旁邊出現反應氣泡（台詞依穴高潮值分三段）。
/// 所有數值都在 Inspector「玩法數值」「點擊反應」群組，Play 中可即時調。
/// </summary>
public partial class TrainingRoomRuntime
{
    [ExportGroup("玩法數值")]
    [Export] public float WetGainBreast   = 0.8f;   // 摸胸／舔胸：每秒濕度（部位高潮值 0 時；100 時 ×1.5）
    [Export] public float WetGainNipple   = 1.2f;   // 摸乳頭
    [Export] public float WetGainMouth    = 0.6f;   // 親嘴
    [Export] public float WetGainHead     = 0f;     // 摸頭（0＝摸頭不會濕）
    [Export(PropertyHint.Range, "0,1,0.01")] public float HeadPatRelief = 0.6f;   // 摸頭時，「不舒服」（速度不對的扣分、濕度不夠就突破的扣分）減輕多少（0.6＝只剩 40%）
    [Export] public float WetGainB        = 3.0f;   // 摸 B
    [Export] public float WetGainLickB    = 4.0f;   // 舔 B
    [Export] public float WetGainThrust   = 0.5f;   // 抽插本身
    [Export] public float WetDecay        = 0.3f;   // 什麼都沒做時每秒下降
    [Export] public float PartArousalRate = 4f;     // 部位高潮值：速度完全契合時每秒上升
    [Export] public float PartArousalDecay = 1f;    // 部位沒被弄時每秒下降
    [Export] public float PartBoostAt100  = 0.5f;   // 抽插時，某部位高潮值 100 → 穴高潮值上升速度 +50%（各部位相加）
    [Export] public float InsertDryMult   = 0.4f;   // 濕度 0 時抽插的穴高潮值速度倍率（濕度 100 時 ×1）
    [Export] public float BreakWetNeed    = 60f;    // 突破需要的濕度
    [Export] public float BreakDryPenalty = 40f;    // 濕度 0 突破時穴高潮值扣多少（濕度越接近需求扣越少）

    public readonly Dictionary<string, float> PartArousal = new() { { "胸", 0f }, { "乳頭", 0f }, { "嘴", 0f }, { "頭", 0f } };

    float Wetness => PlayerDataManager.Instance?.CurrentVaginaWetness ?? 0f;
    void AddWetness(float d) { if (d != 0f) PlayerDataManager.Instance?.AddVaginaWetness(d); }

    /// <summary>部位的速度契合曲線（跟 ArousalCurve 一樣，但用部位自己的高潮值）。</summary>
    float PartCurve(float s, float partValue)
    {
        float x = Mathf.Clamp(partValue / 100f, 0f, 1f);
        float sm = x * x * (3f - 2f * x);
        float pref = Mathf.Lerp(ArousalPrefSpeedLow, ArousalPrefSpeedHigh, sm);
        float d = (Mathf.Clamp(s, 0f, 1f) - pref) / Mathf.Max(ArousalMatchWidth, 0.01f);
        return Mathf.Exp(-d * d) * (1f + ArousalPenaltyRatio) - ArousalPenaltyRatio;
    }

    void StepPart(string part, bool active, float s, float dt)
    {
        float v = PartArousal[part];
        v += active ? PartArousalRate * PartCurve(s, v) * dt : -PartArousalDecay * dt;
        PartArousal[part] = Mathf.Clamp(v, 0f, 100f);
    }

    /// <summary>每幀（UpdateClimaxTouchGrowth）呼叫：更新濕度與部位高潮值，回傳穴高潮值每秒變化。</summary>
    float UpdatePlayValues(float dt, int breastHandsInclNipple)
    {
        float sTouch  = BreastSpeedLevelMax > 0f ? _breastSpeedLevel / BreastSpeedLevelMax : 0f;
        float sTongue = TongueSpeedLvMax > 0f ? _tongueSpeedLv / TongueSpeedLvMax : 0f;
        float sThrust = S2SpeedLvMax > 0f ? _s2SpeedLv / S2SpeedLvMax : 0f;
        float sPat    = HeadPatSpeedLvMax > 0f ? _patSpeedLv / HeadPatSpeedLvMax : 0f;

        bool handBreast = _touchAnimRunningLeft || _touchAnimRunningRight;
        bool lickBreast = _lickOn && _lickTarget != "B";
        bool breast = handBreast || lickBreast;
        bool nipple = NippleHandCount > 0;
        bool mouth  = _tongueOn;
        bool head   = _patOn;
        bool bTouch = _bTouchRunning;
        bool lickB  = _lickOn && _lickTarget == "B";
        bool thrust = _jijiPhase == 3 && _jijiStage >= 1 && !_s2Paused;
        StepAnusWet(dt, thrust, sTongue);   // 肛門濕潤度（PlayAnus.cs）

        StepPart("胸",   breast, lickBreast && !handBreast ? sTongue : sTouch, dt);
        StepPart("乳頭", nipple, sTouch, dt);
        StepPart("嘴",   mouth,  sTongue, dt);
        StepPart("頭",   head,   sPat, dt);

        // ── 濕度 ──
        float wet = 0f;
        float Mul(string p) => 1f + 0.5f * PartArousal[p] / 100f;
        if (breast) wet += WetGainBreast * Mul("胸") * ((_touchAnimRunningLeft ? 1 : 0) + (_touchAnimRunningRight ? 1 : 0) + (lickBreast ? 1 : 0));
        if (nipple) wet += WetGainNipple * Mul("乳頭") * NippleHandCount;
        if (mouth)  wet += WetGainMouth * Mul("嘴");
        if (head)   wet += WetGainHead * Mul("頭");
        if (bTouch) wet += WetGainB;
        if (lickB)  wet += WetGainLickB;
        if (thrust) wet += _jijiAnus ? (_anusComfort ? WetGainThrust * AnusComfortWetMult : 0f) : WetGainThrust;   // 肛門舒服時小穴濕得比較快
        bool anything = breast || nipple || mouth || head || bTouch || lickB || thrust;
        AddWetness((anything ? wet : -WetDecay) * dt);
        // 勃起度上升的權重：只摸胸／乳頭最輕、嘴／摸頭次之、摸 B 正常、抽插最重（取目前在做的事裡最大的）
        float erectW = 0f;
        if (breast || nipple) erectW = Mathf.Max(erectW, ErectWBreast);
        if (mouth || head)    erectW = Mathf.Max(erectW, ErectWMouthHead);
        if (bTouch || lickB)  erectW = Mathf.Max(erectW, ErectWB);
        if (thrust)           erectW = Mathf.Max(erectW, ErectWThrust);
        StepMale(dt, anything, thrust, sThrust, erectW);   // 精力／勃起度／精液量（PlayEnd.cs）

        // ── 穴高潮值 ──
        float rate = 0f;
        float Rel(float x) => (x < 0f && head) ? x * (1f - HeadPatRelief) : x;   // 摸頭可以改善不舒服：負的（扣分）部分打折
        if (bTouch) rate += Rel(ArousalCurve(sTouch)) * ArousalRateB;
        if (lickB)  rate += Rel(ArousalCurve(sTongue)) * ArousalRateB * LickArousalMult;
        if (thrust)
        {
            float w01 = Mathf.Clamp(Wetness / 100f, 0f, 1f);
            float boost = 1f;
            if (breast) boost += PartBoostAt100 * PartArousal["胸"] / 100f;
            if (nipple) boost += PartBoostAt100 * PartArousal["乳頭"] / 100f;
            if (mouth)  boost += PartBoostAt100 * PartArousal["嘴"] / 100f;
            if (head)   boost += PartBoostAt100 * PartArousal["頭"] / 100f;
            float k = Rel(ArousalCurve(sThrust));
            rate += k * ArousalRateInsert * (_jijiAnus ? AnusWetFactor() : Mathf.Lerp(InsertDryMult, 1f, w01)) * (k > 0f ? boost : 1f);
        }

        UpdateReactions(dt, breast, nipple, mouth, head, bTouch || lickB, thrust);
        return rate;
    }

    /// <summary>突破時（Jiji.cs Breakthrough）：濕度不夠 → 穴高潮值依乾的程度下降。</summary>
    void OnBreakthroughWetCheck()
    {
        float w = _jijiAnus ? _anusWet : Wetness;   // 肛門突破看肛門濕潤度
        bool enough = w >= BreakWetNeed;
        if (!enough)
        {
            float lack = 1f - Mathf.Clamp(w / Mathf.Max(1f, BreakWetNeed), 0f, 1f);
            float pen = BreakDryPenalty * lack * (_patOn ? 1f - HeadPatRelief : 1f);   // 摸頭時扣分減輕
            ClimaxLevel = Mathf.Clamp(ClimaxLevel - pen, 0f, 100f);
            GD.Print($"[Play] 濕度不夠就突破（{w:F0}/{BreakWetNeed:F0}）→ 穴高潮值 -{pen:F0}");
        }
        ReactLine(enough ? ReactBreakWet : ReactBreakDry, true);
        ShowEmote(enough ? EmoteBubble.Kind.Exclaim : EmoteBubble.Kind.Tangle);   // 濕度足夠＝驚嘆號；不夠＝毛線團
    }

    // ════════════════════════════════════════════════════════════════
    //  點擊反應（女主頭旁的氣泡）
    // ════════════════════════════════════════════════════════════════
    [ExportGroup("點擊反應")]
    [Export] public bool     ReactEnabled     = true;
    [Export] public float    ReactCooldown    = 2.5f;   // 兩句反應最短間隔（秒）
    [Export] public Vector2  ReactInterval    = new Vector2(4f, 7f);   // 互動進行中，每隔幾秒（隨機）再來一句
    // 台詞：反應時把「穴高潮值分段」＋「正在被弄的部位」＋「目前狀態」的台詞混在一起隨機挑（不會連續重複同一句）。自行增減。
    [Export] public string[] ReactLow   = { "嗯…", "唔…", "……嗯", "嗯？", "呼…", "……", "嗯嗯…", "唔…嗯", "哼嗯…", "嗯…？" };
    [Export] public string[] ReactMid   = { "嗯…啊…", "哈啊…", "唔嗯…", "嗯…♡", "呼…哈…", "啊…", "嗯…哈啊…", "哈…嗯…", "唔…啊…", "啊嗯…" };
    [Export] public string[] ReactHigh  = { "啊…♡", "哈啊…♡", "嗯啊…♡", "啊、啊…♡", "嗯嗯…♡", "哈啊、哈啊…♡", "啊啊…♡", "嗯…啊啊…♡", "呀…♡", "哈…啊…♡" };
    // 部位（正在被弄的部位才會加入候選）
    [Export] public string[] ReactBreast = { "嗯…", "唔嗯…", "哈…" };
    [Export] public string[] ReactNipple = { "啊…！", "嗯…！", "呀…" };
    [Export] public string[] ReactHead   = { "嗯……", "呼嗯…", "嗯…♪" };
    [Export] public string[] ReactPussy  = { "啊…", "嗯啊…", "哈啊…" };
    [Export] public string[] ReactThrust = { "啊、啊…", "嗯、嗯…", "哈、哈啊…" };
    [Export] public string[] ReactKiss   = { "嗯…唔…", "唔嗯…", "嗯…♡", "唔…", "嗯嗯…" };   // 親嘴中（嘴巴被佔住，只用這組）
    // 狀態
    [Export] public string[] ReactSleep      = { "Zzz…", "嗯…（睡）", "呼…呼…" };   // 睡著（ZZZ 表情）時
    [Export] public string[] ReactWet        = { "哈啊…♡", "嗯…♡", "啊…♡" };       // 濕度高（≥ ReactWetAt）
    [Export] public string[] ReactDryThrust  = { "唔…", "嗚…", "……！" };          // 抽插但濕度低（< ReactDryAt）
    [Export] public string[] ReactNearClimax = { "啊、啊、啊…♡", "哈啊…♡！", "嗯啊啊…♡" };   // 穴高潮值 ≥ 90
    [Export] public float    ReactWetAt = 70f;
    [Export] public float    ReactDryAt = 30f;
    [Export] public string[] ReactBreakDry = { "唔…！", "嗚…！", "……！！" };       // 濕度不夠就突破
    [Export] public string[] ReactBreakWet = { "啊…！", "嗯啊…！", "哈啊…！" };     // 濕度足夠突破
    [Export] public string[] ReactClimaxLines = { "～～♡！", "啊啊…♡！", "嗯啊啊啊…♡！" }; // 高潮

    float _reactCd, _reactNext = -1f;
    int   _reactPrevCount;

    void UpdateReactions(float dt, bool breast, bool nipple, bool mouth, bool head, bool pussy, bool thrust)
    {
        _reactCd -= dt;
        int count = (breast ? 1 : 0) + (nipple ? 1 : 0) + (mouth ? 1 : 0) + (head ? 1 : 0) + (pussy ? 1 : 0) + (thrust ? 1 : 0);
        if (!ReactEnabled) { _reactPrevCount = count; return; }

        EmoteOnTouchStart(_reactFlags.nipple, nipple, _reactFlags.head, head);   // 氣泡表情（Emote.cs）
        _reactFlags = (breast, nipple, mouth, head, pussy, thrust);
        if (count > _reactPrevCount) ReactNow(mouth);   // 剛開始一個新的互動（點擊）
        _reactPrevCount = count;

        if (count == 0) { _reactNext = -1f; return; }
        if (_reactNext < 0f) _reactNext = (float)GD.RandRange(ReactInterval.X, ReactInterval.Y);
        _reactNext -= dt;
        if (_reactNext <= 0f) { ReactNow(mouth); _reactNext = (float)GD.RandRange(ReactInterval.X, ReactInterval.Y); }
    }

    (bool breast, bool nipple, bool mouth, bool head, bool pussy, bool thrust) _reactFlags;
    string _reactLast;

    /// <summary>依目前狀態組候選台詞，隨機挑一句。</summary>
    void ReactNow(bool kissing)
    {
        if (kissing) { ReactLine(ReactKiss, false); return; }
        var pool = new List<string>();
        void Add(string[] a, int weight = 1) { if (a == null) return; for (int i = 0; i < weight; i++) pool.AddRange(a); }

        float c = ClimaxLevel, w = Wetness;
        bool sleeping = FaceController != null && FaceController.CurrentExpression == "ZZZ";
        if (sleeping && c < 35f) Add(ReactSleep, 2);
        Add(c < 35f ? ReactLow : c < 75f ? ReactMid : ReactHigh, 2);
        var f = _reactFlags;
        if (f.breast) Add(ReactBreast);
        if (f.nipple) Add(ReactNipple);
        if (f.head)   Add(ReactHead);
        if (f.pussy)  Add(ReactPussy);
        if (f.thrust) Add(ReactThrust);
        if (w >= ReactWetAt) Add(ReactWet);
        if (f.thrust && w < ReactDryAt) Add(ReactDryThrust, 2);
        if (c >= 90f) Add(ReactNearClimax, 2);
        ReactLine(pool.ToArray(), false);
    }

    void ReactClimax() => ReactLine(ReactClimaxLines, true);

    void ReactLine(string[] lines, bool force)
    {
        if (!ReactEnabled || (!force && _reactCd > 0f)) return;
        string t = Pick(lines);
        for (int i = 0; i < 4 && t == _reactLast && lines.Length > 1; i++) t = Pick(lines);   // 避免連續同一句
        if (string.IsNullOrEmpty(t)) return;
        _reactLast = t;
        ShowBubble(ref _bubbleGirl, ref _bubbleGirlLbl, t, false);
        _reactCd = ReactCooldown;
    }
}
