using Godot;

/// <summary>
/// 男主狀態（Inspector「男主狀態」「遊玩時間」群組，Play 中可即時調）：
///  • 精力（左下精力條，Stamina）＝男主的「感覺」0~120：沒插入最多 40；≥90 才能射（射精按鈕）；抽插累積，
///    超過 100 會「破功」快速衝到 120 → 自動射精。小穴越舒服（高潮值）累積越快；又快又高會「痛」。
///  • 勃起度（左下原本的「精液」條，_semenLevel）：≥50 才勃起（雞雞才能出現），<30 軟掉（雞雞收起來）；痛會讓它掉。
///  • 精液量（SemenStock，右下角顯示）＝男主能射多少：越多每次射越多；射完減少，會慢慢回復。
///  • 忍耐按鈕：冷靜下來、停止抽插、精力慢慢下降；精力超過 100 時忍耐有失敗率（超過越多越高），失敗＝破功。
///    射的過程中再按忍耐＝少射一點（有機率忍不住全射）。
///  • 遊玩時間（PlayTimeLimitSec，預設 5 分鐘，進房間就倒數）：時間到男主停下；精力 ≥ ForcedFeelPct 時改成自動加速停不下來。
/// 台詞內容在 Inspector 的 Male*／Play*／Endure* Lines（目前是中性占位字，請自行改）。
/// </summary>
public partial class TrainingRoomRuntime
{
    [ExportGroup("遊玩時間")]
    [Export] public string[] PlaySleepLines = { "（時間到：準備結束1）", "（時間到：準備結束2）", "（時間到：準備結束3）" };      // 時間到、精力不夠高
    [Export] public string[] PlayHurryLines = { "（時間到：趕快做完1）", "（時間到：趕快做完2）", "（時間到：趕快做完3）" };      // 時間到、精力 ≥ ForcedFeelPct
    [Export] public string[] PlayCleanLines = { "（射精後：清理乾淨再離開1）", "（射精後：清理乾淨再離開2）", "（射精後：清理乾淨再離開3）" }; // 時間到且有射精
    [Export(PropertyHint.Range, "0,120,1")] public float ForcedFeelPct = 80f;     // 時間到時精力到這個值以上＝強制加速
    [Export(PropertyHint.Range, "0,120,1")] public float EjacBtnFeelPct = 90f;    // 精力到這個值才能按射精按鈕
    [Export(PropertyHint.Range, "0,20,0.1")] public float ForcedAccel = 3f;       // 強制加速：每秒增加的速度等級
    [Export(PropertyHint.Range, "0,10,0.1")] public float CleanLineDelay = 3.5f;  // 射精後多久說清理台詞

    [ExportGroup("男主狀態")]
    [Export] public string[] MaleNotHardLines = { "（還沒硬1）", "（還沒硬2）", "（還沒硬3）" };      // 勃起度不夠時想叫雞雞出來
    [Export] public string[] MaleSoftLines    = { "（軟掉了1）", "（軟掉了2）", "（軟掉了3）" };      // 勃起度掉到 <30
    [Export] public string[] EndureOkLines    = { "（忍住了1）", "（忍住了2）", "（忍住了3）" };
    [Export] public string[] EndureFailLines  = { "（忍不住了1）", "（忍不住了2）", "（忍不住了3）" };
    [Export(PropertyHint.Range, "0,100,1")] public float ErectOnPct  = 30f;   // 勃起度 ≥ 這個才勃起
    [Export(PropertyHint.Range, "0,100,1")] public float ErectOffPct = 15f;   // 勃起度 < 這個就軟掉
    [Export(PropertyHint.Range, "0,30,0.1")] public float ErectGain = 6f;       // 有任何互動時每秒增加的勃起度（黃條，基礎；再乘上連續動作的加速倍率）
    [Export(PropertyHint.Range, "0,30,0.1")] public float ErectDecay = 2f;      // 停手超過 ErectHoldSec 之後每秒下降（最低降到「自然上升的底」）
    [Export(PropertyHint.Range, "0,10,0.1")] public float ErectHoldSec = 4f;  // 停手後先撐這麼久才開始下降（操控內褲、動作之間短暫停頓不會掉）
    [Export(PropertyHint.Range, "0,10,0.1")] public float ErectSeeGain = 1.5f;   // 「看到」的加成：每 1 單位看到的程度（小穴／胸／衣服被拉開、掰腿、內褲撥開…）每秒多增加多少（沒蓋被子時）
    [Export(PropertyHint.Range, "0,40,1")] public float ErectSeeCap = 15f;      // 「看到」的程度每多 1，自然上升的底最高再多多少
    [Export(PropertyHint.Range, "0,1,0.05")] public float ErectPrepW = 0.6f;    // 只是準備插入（雞雞出現／靠近／停在入口）時，勃起度上升的權重（它也算在操控，不會軟掉）
    [Export(PropertyHint.Range, "0,2,0.05")] public float ErectWBreast = 0.5f;  // 只在摸胸／乳頭時的上升權重（加成太快就調小）
    [Export(PropertyHint.Range, "0,2,0.05")] public float ErectWMouthHead = 0.7f;   // 嘴／摸頭的上升權重
    [Export(PropertyHint.Range, "0,2,0.05")] public float ErectWB = 1f;         // 摸 B／舔 B 的上升權重
    [Export(PropertyHint.Range, "0,2,0.05")] public float ErectWThrust = 1.2f;  // 抽插的上升權重
    [Export(PropertyHint.Range, "0.02,0.3,0.01")] public float ErectSeeHemMax = 0.10f;   // 裙擺拉起到多少（V）算「完全看到」
    [Export(PropertyHint.Range, "0.02,0.5,0.01")] public float ErectSeeStretchMax = 0.15f;   // 上衣往下拉到多少（V）算「完全看到」
    [Export(PropertyHint.Range, "0,5,0.05")] public float ErectIdleGain = 0.5f; // 沒蓋被子時，不做任何事也會慢慢增加的底（每秒）
    [Export(PropertyHint.Range, "0,100,1")] public float ErectIdleMax = 45f;    // 自然增加的底最多到這
    [Export(PropertyHint.Range, "0,10,0.1")] public float ErectBoostMax = 3f;   // 連續動作的加速倍率上限（每秒增加量 ×(1+倍率)）
    [Export(PropertyHint.Range, "0,5,0.05")] public float ErectBoostRise = 0.6f; // 連續動作時加速倍率每秒增加多少（越做越快）
    [Export(PropertyHint.Range, "0,50,0.5")] public float ErectBoostFall = 10f; // 停手之後加速倍率每秒掉多少
    [Export(PropertyHint.Range, "0,30,0.5")] public float PainErectDrain = 12f; // 痛到滿時每秒下降的勃起度
    [Export(PropertyHint.Range, "0,100,1")] public float ErectDropOnEjac = 35f; // 射精後勃起度下降
    [Export(PropertyHint.Range, "0,120,1")] public float FeelCapNoInsert = 40f; // 沒插入時精力最多到這
    [Export(PropertyHint.Range, "0,20,0.1")] public float FeelRateTouch = 0.125f; // 沒插入、有互動時每秒增加（綠條，非雞雞功能很難升）
    [Export(PropertyHint.Range, "0,20,0.1")] public float FeelRateBase = 1.25f; // 抽插中每秒增加（最慢；雞雞功能才升得快）
    [Export(PropertyHint.Range, "0,30,0.1")] public float FeelRatePerSpeed = 3f; // 速度滿檔時額外每秒增加
    [Export(PropertyHint.Range, "0,3,0.05")] public float FeelHoleBoost = 0.8f; // 小穴高潮值 100 時累積速度額外 ×(1+這個)：小穴越舒服越容易射
    [Export(PropertyHint.Range, "0,5,0.05")] public float FeelDecay = 0.8f;     // 沒累積時每秒下降
    [Export(PropertyHint.Range, "0,200,1")] public float FeelBreakRate = 45f;   // 破功（超過 100）後每秒衝多少
    [Export(PropertyHint.Range, "0,120,1")] public float FeelAfterEjac = 20f;   // 射精後精力掉到
    [Export(PropertyHint.Range, "0,100,1")] public float PainStartFeel = 60f;   // 精力超過這個且又快才會痛
    [Export(PropertyHint.Range, "0,1,0.05")] public float PainFeelSlow = 0.3f;  // 痛時精力累積變慢多少
    [Export(PropertyHint.Range, "0,100,1")] public float SemenStock = 80f;      // 精液量（男主能射多少）
    [Export(PropertyHint.Range, "0,5,0.05")] public float SemenRegen = 0.15f;   // 每秒回復
    [Export(PropertyHint.Range, "0,100,1")] public float SemenUsePerEjac = 45f; // 滿量射一次消耗
    [Export(PropertyHint.Range, "0,1,0.05")] public float EjacMulMin = 0.35f;   // 精液量接近 0 時，射精量的倍率（量 100 時 ×1）

    [ExportGroup("忍耐")]
    [Export(PropertyHint.Range, "0.5,20,0.1")] public float EndureSec = 3f;          // 一次忍耐持續幾秒（停止抽插、冷靜）
    [Export(PropertyHint.Range, "0,30,0.5")] public float EndureFeelDrop = 6f;       // 忍耐中每秒下降的精力
    [Export(PropertyHint.Range, "0,40,0.5")] public float EndureFailOver = 20f;      // 精力超過 100 這麼多時，忍耐必定失敗
    [Export(PropertyHint.Range, "0,1,0.05")] public float EndureFailInEjac = 0.3f;   // 射精中再忍耐：忍不住全射的機率
    [Export(PropertyHint.Range, "0,1,0.05")] public float EndureCutMul = 0.5f;       // 射精中忍耐成功：剩下的量 ×

    [ExportGroup("男主台詞（事件）")]
    [Export] public string[] MaleHardLines     = { "（硬了1）", "（硬了2）", "（硬了3）" };           // 勃起度到 50，勃起
    [Export] public string[] MaleShowLines     = { "（雞雞出現1）", "（雞雞出現2）", "（雞雞出現3）" };       // 按 🍆 叫雞雞出來
    [Export] public string[] MaleInsertLines   = { "（開始插入1）", "（開始插入2）", "（開始插入3）" };       // 按住入口開始插入
    [Export] public string[] MaleBreakLines    = { "（突破1）", "（突破2）", "（突破3）" };           // 突破進去
    [Export] public string[] MaleAnusLines     = { "（肛門1）", "（肛門2）", "（肛門3）" };           // 點肛門準備插入
    [Export] public string[] MaleAnusLickLines = { "（舔肛門1）", "（舔肛門2）", "（舔肛門3）" };         // 舌頭舔肛門開始
    [Export] public string[] MaleThrustLines   = { "（抽插中1）", "（抽插中2）", "（抽插中3）" };         // 抽插中隨機間隔說
    [Export] public string[] MaleFastLines     = { "（太快了，痛1）", "（太快了，痛2）", "（太快了，痛3）" };     // 又快又高、痛
    [Export] public string[] MaleCanEjacLines  = { "（快到了1）", "（快到了2）", "（快到了3）" };         // 精力到 90，可以射
    [Export] public string[] MaleBreakFeelLines= { "（要破功了1）", "（要破功了2）", "（要破功了3）" };       // 精力超過 100
    [Export] public string[] MaleEjacLines     = { "（射精1）", "（射精2）", "（射精3）" };           // 射精開始
    [Export] public string[] MaleAfterEjacLines= { "（射完了1）", "（射完了2）", "（射完了3）" };         // 射精後
    [Export] public string[] MalePullOutLines  = { "（拔出1）", "（拔出2）", "（拔出3）" };           // 拔出
    [Export(PropertyHint.Range, "0,30,0.5")] public float MaleLineCooldown = 2.5f;   // 事件台詞最短間隔（秒）
    [Export(PropertyHint.Range, "1,60,0.5")] public float MaleThrustLineGap = 9f;    // 抽插中台詞平均間隔（秒）

    float _maleLineCd, _maleThrustLineT;
    bool _prevCanEjac, _prevErectOn;

    /// <summary>事件台詞（有冷卻，避免一直跳；force＝一定要說）。</summary>
    void SayMaleEv(string[] lines, bool force = false)
    {
        if (lines == null || lines.Length == 0) return;
        if (!force && _maleLineCd > 0f) return;
        _maleLineCd = MaleLineCooldown;
        SayMale(lines);
    }

    // ── 狀態 ──
    float _erectBase, _erectBoost, _erectIdleT;   // 勃起度：自然上升的底／連續動作的加速倍率
    bool _playForced, _ejacDone;
    bool _erectOn;               // 目前是否勃起（有遲滯）
    bool _breaking, _prevBreaking;   // 破功中（精力往 120 衝）
    float _pain;                 // 0~1
    float _endureT;              // 忍耐剩餘秒數
    float _ejacMul = 1f;         // 這次射精量倍率
    bool _ejacStop;              // 射精中忍耐成功：停止後面的波
    public bool Erect => _erectOn;
    public float ErectionPct => _semenLevel * 100f;

    Button _ejacBtn, _endureBtn;
    Label _timeLbl, _statLbl;

    void SayMale(string[] lines)
    {
        string t = Pick(lines);
        if (!string.IsNullOrEmpty(t)) ShowBubble(ref _bubblePlayer, ref _bubblePlayerLbl, t, true);
    }

    int EjacWaves(int n) => Mathf.Max(1, Mathf.RoundToInt(n * _ejacMul));

    /// <summary>「看到多少」0~約 4：裙擺拉起、肩帶拉開、上衣往下拉、掰腿、內褲撥開、內褲脫掉。越高，沒蓋被子時勃起度自己上升越快。</summary>
    float ExposureScore()
    {
        float e = 0f;
        e += Mathf.Clamp(Mathf.Max(_hemPullA[0], _hemPullA[1]) / Mathf.Max(0.001f, ErectSeeHemMax), 0f, 1f);   // 裙擺拉開 → 小穴看得到
        e += Mathf.Clamp(Mathf.Max(_rotA[0], _rotA[1]) / Mathf.Max(1f, ClothTopRotMax), 0f, 1f);               // 肩帶拉開 → 胸看得到
        e += Mathf.Clamp(Mathf.Max(_stA[0], _stA[1]) / Mathf.Max(0.001f, ErectSeeStretchMax), 0f, 1f);         // 上衣往下拉
        if (_mLegOpen) e += 0.6f;                                                                             // 掰腿
        if (_mLegOpen && !_pantyGone) e += Mathf.Clamp(_mpA / Mathf.Max(1f, MPantyAsideMaxPx), 0f, 1f);       // 內褲撥開
        if (_pantyGone) e += 1f;                                                                              // 內褲脫掉
        return e;
    }

    // ═══════════════ 每幀（Play.cs）：精力／勃起度／精液量 ═══════════════
    void StepMale(float dt, bool anything, bool thrust, float sThrust, float actW = 1f)
    {
        bool inserted = _jijiPhase == 3 && _jijiStage >= 1;
        bool thrusting = inserted && thrust && _endureT <= 0f;
        if (_endureT > 0f) _endureT = Mathf.Max(0f, _endureT - dt);

        // 痛：又快、精力又高
        float hi = Mathf.Clamp((Stamina - PainStartFeel) / Mathf.Max(1f, 100f - PainStartFeel), 0f, 1f);
        float painTgt = thrusting ? Mathf.Clamp(sThrust * hi, 0f, 1f) : 0f;
        _pain = Mathf.Lerp(_pain, painTgt, 1f - Mathf.Exp(-4f * dt));

        // 精力（感覺）
        float hole01 = Mathf.Clamp(ClimaxLevel / 100f, 0f, 1f);
        float rise = 0f;
        if (thrusting) rise = (FeelRateBase + FeelRatePerSpeed * sThrust) * (1f + FeelHoleBoost * hole01) * (1f - PainFeelSlow * _pain) * (_erectOn ? 1f : 0.5f);
        else if (anything && !inserted) rise = FeelRateTouch;
        if (!inserted)
        {
            if (Stamina > FeelCapNoInsert) Stamina = Mathf.Max(FeelCapNoInsert, Stamina - FeelDecay * 3f * dt);
            else Stamina = Mathf.Min(FeelCapNoInsert, Stamina + rise * dt);
        }
        else Stamina += rise * dt;
        if (rise <= 0f && !_breaking) Stamina -= FeelDecay * dt;
        if (_endureT > 0f) Stamina -= EndureFeelDrop * dt;

        if (Stamina >= 100f && _endureT <= 0f) _breaking = true;
        if (_breaking)
        {
            Stamina += FeelBreakRate * dt;
            if (Stamina >= 120f && !_ejacRunning && !_insertEjacActive) MaleEjaculate();
        }
        Stamina = Mathf.Clamp(Stamina, 0f, 120f);

        // 勃起度
        float er = _semenLevel * 100f;
        _erectBase = Mathf.Min(_erectBase, er);                                    // 底不會比現在的值高（射精／被痛扣掉之後底跟著降）
        float expo = ExposureScore();                                              // 看得到多少：小穴／胸／衣服被拉開、掰腿、內褲撥開…
        if (DuvetState == 1)
        {   // 沒蓋被子：自己慢慢增加；看到的越多（expo 越大）增加越快、能升到的底也越高
            float baseMax = Mathf.Min(90f, ErectIdleMax + ErectSeeCap * expo);
            _erectBase = Mathf.Min(baseMax, _erectBase + (ErectIdleGain + ErectSeeGain * expo) * dt);
        }
        bool prep = _jijiPhase > 0 && _jijiPhase < 3;   // 準備插入（雞雞出現、靠近、停在入口）：正在操控，不算「什麼都不做」
        bool act = anything || _mpDrag || _pantyDrag || prep;   // 操控內褲（撥開／往下拉）也算有在動作
        if (act)
        {
            float w = anything ? actW : ErectPrepW;   // 動作的權重：摸胸／乳頭比較輕、摸 B／抽插比較重；只是準備插入就用 ErectPrepW
            if (_mpDrag || _pantyDrag) w = Mathf.Max(w, 0.8f);
            _erectIdleT = 0f;
            _erectBoost = Mathf.Min(ErectBoostMax, _erectBoost + ErectBoostRise * Mathf.Min(1f, w) * dt);   // 越做越快（權重低的動作加成累積得慢）
            er += ErectGain * (1f + _erectBoost) * w * dt;
        }
        else
        {
            _erectIdleT += dt;
            _erectBoost = Mathf.Max(0f, _erectBoost - ErectBoostFall * dt);
            if (_erectIdleT > ErectHoldSec) er = Mathf.Max(_erectBase, er - ErectDecay * dt);   // 停手一下之後才下降，但不低於自然上升的底
        }
        if (DuvetState == 1 && er < _erectBase) er = _erectBase;
        er -= PainErectDrain * _pain * dt;
        SetSemenLevel(er / 100f);
        float ep = _semenLevel * 100f;
        if (!_erectOn && ep >= ErectOnPct) _erectOn = true;
        else if (_erectOn && ep < ErectOffPct && !prep)   // 準備插入時不軟（雞雞不會被收起來）
        {
            _erectOn = false;
            if (_jijiPhase > 0) { SayMale(MaleSoftLines); HideJiji(); }   // 軟掉：雞雞收起來
        }

        // ── 事件台詞 ──
        if (_maleLineCd > 0f) _maleLineCd -= dt;
        if (_erectOn && !_prevErectOn) SayMaleEv(MaleHardLines);
        _prevErectOn = _erectOn;
        bool canEj = Stamina >= EjacBtnFeelPct;
        if (canEj && !_prevCanEjac) SayMaleEv(MaleCanEjacLines);
        _prevCanEjac = canEj;
        if (_breaking && !_prevBreaking) SayMaleEv(MaleBreakFeelLines, true);
        _prevBreaking = _breaking;
        if (thrusting)
        {
            if (_pain > 0.6f) SayMaleEv(MaleFastLines);
            _maleThrustLineT -= dt;
            if (_maleThrustLineT <= 0f) { _maleThrustLineT = MaleThrustLineGap * Rf(0.7f, 1.3f); SayMaleEv(MaleThrustLines); }
        }
        else _maleThrustLineT = MaleThrustLineGap * 0.5f;

        // 精液量慢慢回復
        SemenStock = Mathf.Min(100f, SemenStock + SemenRegen * dt);
    }

    // ═══════════════ 射精（按鈕／破功／強制）═══════════════
    void MaleEjaculate()
    {
        if (_ejacRunning || _insertEjacActive) return;
        float s01 = Mathf.Clamp(SemenStock / 100f, 0f, 1f);
        _ejacMul = Mathf.Lerp(EjacMulMin, 1f, s01);
        _ejacStop = false;
        SemenStock = Mathf.Max(0f, SemenStock - SemenUsePerEjac * _ejacMul);
        SayMaleEv(MaleEjacLines, true);
        TriggerEjaculation();
        GetTree().CreateTimer(2.5f).Timeout += () => { if (IsInstanceValid(this)) SayMaleEv(MaleAfterEjacLines); };
        _ejacDone = true;
        _breaking = false;
        Stamina = Mathf.Min(Stamina, FeelAfterEjac);
        SetSemenLevel(_semenLevel - ErectDropOnEjac / 100f);
        bool wasEnd = _playTimeUp;
        _playForced = false;
        if (wasEnd) CleanLineLater();
    }

    void CleanLineLater()
    {
        GetTree().CreateTimer(CleanLineDelay).Timeout += () => { if (IsInstanceValid(this)) SayMale(PlayCleanLines); };
    }

    // ═══════════════ 忍耐 ═══════════════
    void OnEndurePressed()
    {
        if (_ejacRunning || _insertEjacActive)
        {   // 射到一半再忍耐：少射一點，也可能忍不住全射
            if (_ejacStop) return;
            if (GD.Randf() < EndureFailInEjac) { _ejacMul = Mathf.Max(_ejacMul, 1f); SayMale(EndureFailLines); GD.Print("[Male] 忍耐失敗：全射"); }
            else { _ejacStop = true; _ejacMul *= EndureCutMul; SayMale(EndureOkLines); GD.Print("[Male] 忍耐成功：少射"); }
            return;
        }
        float over = Mathf.Max(0f, Stamina - 100f);
        float fail = Stamina >= 100f ? Mathf.Clamp(over / Mathf.Max(1f, EndureFailOver), 0f, 1f) : 0f;
        if (_breaking) fail = Mathf.Max(fail, 0.5f);   // 已經在破功了，更難忍
        if (GD.Randf() < fail)
        {
            _breaking = true; SayMale(EndureFailLines); GD.Print($"[Male] 忍耐失敗（失敗率 {fail:P0}）");
            return;
        }
        _breaking = false; _endureT = EndureSec;
        if (_jijiPhase == 3 && _jijiStage >= 1) { _s2Paused = true; _s2SpeedLv = 0f; }
        SayMale(EndureOkLines);
    }

    /// <summary>在 UpdateStage1Osc（抽插中）每幀呼叫：時間到停下／強制加速／忍耐中停止抽插。</summary>
    void PlayEndTick(float dt)
    {
        if (!PlayTimeLimitOn)
        {
            if (_playTimeUp) { _playTimeUp = false; _playForced = false; }
        }
        if (_endureT > 0f) { _s2Paused = true; _s2SpeedLv = 0f; _pullActive = false; }
        if (_playTimeUp && !_playForced) _s2Paused = true;
        if (_playForced)
        {
            _s2Paused = false; _pullActive = false;
            _s2SpeedLv = Mathf.Min(S2SpeedLvMax, _s2SpeedLv + ForcedAccel * dt);
        }
    }

    // ═══════════════ HUD／倒數（每幀 _Process）═══════════════
    void UpdatePlayEnd(float dt)
    {
        UpdateHoverTip();   // 懸停提示氣泡（HoverTip.cs）
        UpdateBarNumbers(); // 條上的具體數值
        // 倒數：進房間就開始算
        if (PlayTimeLimitOn && !_playTimeUp)
        {
            _playTime += dt;
            if (_playTime >= PlayTimeLimitSec)
            {
                _playTimeUp = true;
                bool thrusting = _jijiPhase == 3 && _jijiStage >= 1;
                if (thrusting && Stamina >= ForcedFeelPct)
                {
                    _playForced = true; SayMale(PlayHurryLines);
                    GD.Print("[PlayEnd] 時間到＋精力夠 → 強制加速");
                }
                else
                {
                    if (thrusting) { _s2Paused = true; _s2SpeedLv = 0f; }
                    SayMale(PlaySleepLines);
                    if (_ejacDone) CleanLineLater();
                    GD.Print("[PlayEnd] 時間到 → 男主停下");
                }
            }
        }

        var parent = UiRoot;
        if (parent == null || _semenBar == null) return;
        if (_ejacBtn == null || !IsInstanceValid(_ejacBtn))
        {
            _ejacBtn = MakeCornerBtn(parent, "BtnPlayEjac", "💦 射精", -256f, -116f);
            _ejacBtn.Pressed += () => { if (Stamina >= EjacBtnFeelPct && SemenStock > 0f) MaleEjaculate(); };
        }
        if (_endureBtn == null || !IsInstanceValid(_endureBtn))
        {
            _endureBtn = MakeCornerBtn(parent, "BtnEndure", "🧘 忍耐", -406f, -266f);
            _endureBtn.Pressed += OnEndurePressed;
        }
        if (_timeLbl == null || !IsInstanceValid(_timeLbl))
        {
            _timeLbl = MakeCornerLbl(parent, "PlayTimeLbl", -160f, -112f, -146f, 24);
        }
        if (_statLbl == null || !IsInstanceValid(_statLbl))
        {
            _statLbl = MakeCornerLbl(parent, "MaleStatLbl", -230f, -150f, -240f, 16);
        }
        _timeLbl.Visible = PlayTimeLimitOn;
        float left = Mathf.Max(0f, PlayTimeLimitSec - _playTime);
        _timeLbl.Text = $"⏳ {(int)(left / 60f)}:{(int)(left % 60f):00}";
        _timeLbl.Modulate = left <= 30f ? new Color(1f, 0.45f, 0.45f) : Colors.White;
        _statLbl.Text = $"精力 {Stamina:F0}/120{(_pain > 0.25f ? "  痛" : "")}\n勃起 {_semenLevel * 100f:F0}{(_erectOn ? "（硬）" : "（軟）")}\n射量 {SemenStock:F0}";
        bool ok = Stamina >= EjacBtnFeelPct && SemenStock > 0f && !_ejacRunning && !_insertEjacActive;
        _ejacBtn.Disabled = !ok;
        _ejacBtn.Modulate = ok ? Colors.White : new Color(1, 1, 1, 0.45f);
        UpdateTissue(dt);
        _endureBtn.Modulate = (_endureT > 0f) ? new Color(0.7f, 1f, 0.7f) : Colors.White;
    }

    Button MakeCornerBtn(Node parent, string name, string text, float left, float right)
    {
        var b = new Button { Name = name, Text = text, CustomMinimumSize = new Vector2(140f, 44f), FocusMode = Control.FocusModeEnum.None };
        b.AnchorLeft = 1; b.AnchorRight = 1; b.AnchorTop = 1; b.AnchorBottom = 1;
        b.OffsetLeft = left; b.OffsetRight = right; b.OffsetBottom = -42f; b.OffsetTop = -86f;
        parent.AddChild(b);
        return b;
    }

    Label MakeCornerLbl(Node parent, string name, float left, float bottom, float top, int size)
    {
        var l = new Label { Name = name, HorizontalAlignment = HorizontalAlignment.Right, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AnchorLeft = 1; l.AnchorRight = 1; l.AnchorTop = 1; l.AnchorBottom = 1;
        l.OffsetLeft = left; l.OffsetRight = -20f; l.OffsetBottom = bottom; l.OffsetTop = top;
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeConstantOverride("outline_size", 6);
        l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.8f));
        parent.AddChild(l);
        return l;
    }
}
