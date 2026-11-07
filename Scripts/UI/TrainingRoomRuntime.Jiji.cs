using Godot;

/// <summary>
/// TrainingRoomRuntime — 雞雞插入互動（對照 Unity 版 Phase 0~3 + 突破分數系統）。
///
/// 操作流程（跟 Unity 一樣）：
///   Phase 0 → 1：點底部「🍆」按鈕 → 雞雞＋男人肚子在「入口下方」淡入出現
///                （也可以直接點 B 熱點，會先淡入）
///   Phase 1 → 2：點 B 熱點 → 雞雞＋男人肚子一起滑到 B（小穴入口）
///   Phase 2 → 3：在 B 上「按下左鍵」→ 開始插入，雞雞自動往上推（Stage 0）
///   Phase 3    ：到達該階段上限 → 卡住（阻力）→「按住左鍵」累積突破分數
///                  · 按住 +25/秒，按住同時滑鼠往上 +35/秒，濕度越高累積越快、目標越低
///                  · 放開或往下拉 → 分數 -60/秒；Stage 0 分數歸零 → 失敗退回 Phase 2
///                  · 分數滿 → 突破！（表情 插入1 → 1 秒後 插入2）
///                按住左鍵時滑鼠往上拖＝推進、往下拖＝退出一點
///   Stage 1（突破後，對照 Unity 深插振盪）：雞雞自動來回抽插，
///                按住左鍵往上拖＝加速（幅度/頻率變大），往下拖＝減速；速度夠快肚子會鼓起
///   右鍵：Phase 3 → 2（拔出回入口）、Phase 2 → 1（退回）、Phase 1 → 收起
///
/// 位置控制：雞雞/男人肚子/精液 三層每幀由這裡直接寫 Position（基準 _layerBasePos + 偏移），
/// 覆蓋掉呼吸系統對這三層的位移（對照 Unity：jiji 啟用時呼吸迴圈跳過這兩層）。
///
/// 超出入口的部分用 shader 裁掉（對照 Unity UpdateJijiClip）：
/// 裁切線固定在「進入 Phase 2 時雞雞尖端所在的高度」，之後往上推進去的部分就看不到。
///
/// 開合疊層：開1＝xiaoxuekai、洞＝xiaoxuekaidi、插入＝xiaoxuekai2、肚子＝charuduzi（見 UpdateOpenLayers）。
/// 【尚未移植】Stage 1 的自動深插振盪＋肚子＋G 點、m 腿推擠、插入狀態的內射。
/// </summary>
public partial class TrainingRoomRuntime
{
    // ── Export：出現 / 靠近 ─────────────────────────────────────────────
    [Export] public float   JijiAppearDuration   = 0.35f;              // 淡入 / 滑動時間（秒）
    [Export] public float   JijiDefaultOffsetY   = 300f;               // Phase 1：出現在入口下方多少（畫布 px，正＝往下）
    /// <summary>Phase 2：點 B 衝到的位置微調，單位是「螢幕 px」（正＝往下）。
    /// 之前是用角色畫布 px（角色整體縮小到 31.5%），使用者說往下 10px 實際畫面上只動了 3px 左右，
    /// 看起來像沒動；改成螢幕 px，執行時自動換算成畫布 px。使用者累計要求 10+15+20 ＝ 45。</summary>
    [Export] public float JijiInsertOverlapPx = 6f;   // 抽插時雞雞裁切線再往上讓幾 px（螢幕 px），雞雞多伸進插入層底下，避免兩者之間露出縫（0＝不補）
    [Export] public float JijiInsertOffsetX = -1f;   // 插入時雞雞＋精液額外左右微調（畫布 px，負＝向左）
    [Export] public float JijiBellyOffsetX = -1f;   // 男人肚子（雞雞的肚子）單獨左右微調（畫布 px，負＝向左）
    [Export] public Vector2 JijiPhase2Offset     = new Vector2(7f, 51f);   // X：男人肚子＋雞雞往右 3 → 5；Y：45 → 47 → 49 → 51
    /// <summary>雞雞（連同精液）以底部為錨點的旋轉角度（度，Godot 正＝順時針；-1.5 ＝ 逆時針 1.5 度）。</summary>
    [Export] public float   JijiRotationDeg      = -1.5f;
                                                                          // 雞雞/男人肚子/精液/裁切線都以這個位置為基準，會一起往下
    [Export] public float   JijiPhase2SpeedMult  = 1f;                 // Phase 2 滑動速度倍率
    // ── Export：Phase 3 操作手感 ────────────────────────────────────────
    [Export] public float   JijiMouseAccelMult   = 2.5f;               // 按住拖曳滑鼠時的推進倍率
    [Export] public float   JijiDownSpeed        = 60f;
    [Export] public float   JijiRetractSpeed     = 320f;
    [Export] public float   JijiBreakSurgePx     = 55f;                // 突破瞬間（插入層出現）雞雞往上衝多少（螢幕 px）：35 → 再高 20 ＝ 55
    [Export] public float   JijiBreakSurgeDropPx = 5f;                 // 衝到頂之後只回落多少（螢幕 px），之後就停在「衝刺 − 回落」的深度繼續抽插
    [Export] public float   JijiBreakSurgeTime   = 0.35f;              // 衝上去＋回落的總時間（秒）
    [Export] public float   JijiStage1LiftPx     = 20f;                // 突破後雞雞整體再往上（螢幕 px）
    [Export] public float   JijiStage1ClipLiftPx = 20f;                // 突破後黃色裁切線整體再往上（螢幕 px）               // 突破前放開左鍵時往回退的速度（畫布 px/秒）
    [Export] public float   ResistScoreRateUpBonus  = 35f;             // 按住＋往上時額外每秒加分
    [Export] public float   ResistWetnessScoreBonus = 0.8f;            // 濕度 100% 時累積速度 ×(1+0.8)
    [Export] public float   ResistWetnessWeight     = 0.3f;            // 濕度 100% 時突破目標 ×(1-0.3)
    [Export] public float   ResistJijiSlowSpeed     = 3f;              // 阻力中仍緩慢撐開的速度
    [Export] public float   ResistJijiSlowRange     = 25f;             // 阻力中最多撐過上限多少
    [Export] public float   ResistBodyPushMaxY      = 6f;              // 阻力中全身被往上推的幅度
    [Export] public float   ResistBodyTrembleAmp    = 2f;              // 阻力中全身發抖幅度
    [Export] public float   ResistBodyTrembleFreq   = 4f;              // 阻力中全身發抖頻率（Hz）
    [Export] public bool    JijiClipEnabled         = true;            // 裁掉插進去的部分
    [Export] public float   JijiClipOffsetY         = -28f;            // 裁切線「中間」的位置（螢幕 px，正＝往下）：-20 → -24 → -28（中間再收）
    /// <summary>傘形（∩）裁切：中間最高、往左右兩側越來越低，兩側比中間多裁掉這麼多（螢幕 px）。0＝平的直線裁切。</summary>
    [Export] public float   JijiClipArchPx          = 0f;              // 舊的拋物線傘形（改用下面的雙圓形裁切，預設關閉）
    // ── 雙圓形裁切（使用者描述：像兩把圓形切割刀，切掉雞雞左上、右上）──────────────
    // 中間照 JijiClipOffsetY 的水平線裁；另外在左右各放一個圓，圓內的部分也裁掉，
    // 圓的最低點比中間裁切線低 JijiClipCircleDip，所以兩側會被切得比中間深。單位都是螢幕 px。
    [Export] public float   JijiClipCircleOffsetX   = 35f;   // 圓心離雞雞中線左右各多遠（JijiClipCircleOverlap < 0 時才用）
    /// <summary>兩個裁切圓的重疊比例（重疊寬度 ÷ 直徑）。≥0 時圓心距離自動算成 直徑 ×(1−重疊)，
    /// 也就是每個圓心離中線 半徑 ×(1−重疊)；改圓的大小時重疊比例會維持。設成負數改回用 JijiClipCircleOffsetX。</summary>
    [Export] public float   JijiClipCircleOverlap   = 0.1f;   // 使用者要求：兩顆往內收到重疊約 10%
    float ClipCircleOffsetEff => JijiClipCircleOverlap >= 0f
        ? JijiClipCircleRadius * (1f - JijiClipCircleOverlap)
        : JijiClipCircleOffsetX;
    [Export] public float   JijiClipCircleRadius    = 22f;   // 圓的半徑
    [Export] public float   JijiClipCircleDip       = 17f;   // 圓的最低點比中間裁切線低多少（14 → 往下 3 ＝ 17）
    [Export] public float   JijiClipFeatherPx       = 1.5f;            // 裁切邊緣柔化（螢幕 px），避免鋸齒
    [Export] public float   JijiOpen1BottomFade     = 1f;              // （舊）開1 剛張開時底部淡出，已改成下面的「推越深、底部淡出越多」
    // ── 雞雞碰到開1 之後的連動（使用者要求）──────────────────────────────
    // 碰到＝推進到 Stage 0 的 JijiOpenLayerStartT（開1 開始出現的位置）。之後：
    //   • 兩個裁切圓跟著雞雞往上移，但比較慢（JijiClipCircleFollow 比例）
    //   • 開1 從底部開始淡出，推越深淡出越多，推到頂（卡住、開始突破）時大約剩上半部（JijiOpen1KeepAtMax）
    [Export] public float   JijiClipCircleFollow    = 0.5f;            // 裁切圓跟著雞雞往上的比例（0＝不動，1＝完全跟著）
    [Export] public float   JijiOpen1KeepAtMax      = 0.4f;            // 推到頂時開1 還留下多少（從上往下算）：0.5 → 0.4，淡出加強
    [Export] public float   JijiOpen1FadeBandPx     = 18f;             // 開1 淡出邊緣的柔和寬度（螢幕 px）：12 → 18，漸層更明顯
    // ── Export：開合疊層（開1／洞／插入，對照 Unity JijiOpenLayer*）──────
    [Export] public float   JijiOpenLayerStartT     = 0.3f;            // Stage 0 推到該段 30% 才開始張開
    [Export] public float   JijiOpenLayerMaxScaleX  = 0.8f;            // 完全張開時的 X 縮放（1 → 0.8：使用者要求開的擴張幅度小一點）
    /// <summary>開1／洞／插入／肚子 這幾張插入疊層整體的位置微調（畫布 px，正＝往下）。使用者要求先往下 10px。</summary>
    // 插入疊層各自的上下位移，單位都是「螢幕 px」（正＝往下，執行時換算成畫布 px）。
    // 原本三層共用一個值，改成分開調：
    // 使用者要求：原本的「開1」（xiaoxuekai）裡面就有跟「開上」一樣的 Λ 形線條（兩張圖的 Λ 在原圖上位置完全一樣，
    // PIL 量紅線範圍都是 y1429 起），改成用開1 取代開上，開1 和洞一起放到「開上」原本所在的位置：
    // 開上 ＝ 洞(15) ＋ 開上額外(-17) ＝ -2 → 開1、洞都設 -2（兩者維持原圖的相對對齊）。
    [Export] public float   JijiOverlayOffsetY      = 0f;    // 開1（原本 58 → -2 → 往下 2 ＝ 0）
    [Export] public float   JijiOpen1OffsetX        = -1f;   // 開1 左右（螢幕 px，負＝往左）：使用者要求往左 1
    [Export] public float   JijiHoleOffsetX         = -1f;   // 洞 左右（螢幕 px，負＝往左）：使用者要求往左 1（開上跟著洞）
    [Export] public float   JijiHoleOffsetY         = -2f;   // 洞（原本 15）
    /// <summary>摸 B 時「洞」圖層的顯示縮放（BTouchHoleScale × JijiOpenLayerMaxScaleX）。
    /// 0 = 完全不顯示（若後面覺得影響太大可以直接設 0 關掉），預設 0.3 ≈ 開了 30% 的樣子。</summary>
    [Export] public float   BTouchHoleScale          = 0.3f;
    [Export] public float   BTouchHolePulse          = 0.4f;    // 蹭到最低時洞／開1 額外放大的比例（小幅度脈動）
    [Export] public bool    JijiUseHoleTopLayer     = false; // 開上：已由開1 取代，預設不顯示（改 true 可恢復）
    [Export] public float   JijiInsertLayerOffsetY  = 25f;   // 插入：55 → 30 → 28 → 25（往上 3）→ 使用者要求再往下 80（25+80＝105）；開1 位置不受影響（突破時用差值鎖定）
    /// <summary>肚子整體上下位置（螢幕 px，正＝往下）。
    /// 【修正 v2】_s2OscY 現在跟 _jijiInsertY 完全一致（含 JijiStage1LiftPx + 突破衝刺殘留），
    /// 肚子跟雞雞三者完全同步。原本 132 已補償了 20+50=70px 的差距，現在差距消除，
    /// 預設調回 62（132 − 70），讓肚子維持在視覺上相同的位置。</summary>
    [Export] public float   S2TummyExtraOffsetY     = 185f;   // 132 → 62（_s2OscY 改為跟雞雞同步後，移除 70px 舊補償）
    /// <summary>肚子出現的判定點往下移多少（螢幕 px）：雞雞頂的幅度只要到「原本門檻 − 這個值」就出現，
    /// 等於判定點跟著肚子一起往下移。</summary>
    [Export] public float   S2TummyShowLowerPx      = 10f;
    /// <summary>肚子跟著雞雞升到最高點時，額外往下壓多少（螢幕 px）；越高壓越多，最低點為 0。</summary>
    [Export] public float   S2TummyTopDropPx        = 20f;
                                                                    // 再加 32：Y 縮放從 0.6 改回 1 之後，圖會往上跑約 32 螢幕 px，補回來讓位置跟之前看到的一致
    [Export] public float   JijiHoleTopShowAt       = 0.95f; // 開上：洞張開到這個程度（0~1）才出現
    [Export] public float   JijiHoleTopExtraY       = -17f;  // 開上：跟著洞之外的位移（螢幕 px，正＝往下）：+3 → 往上 20 ＝ -17
    [Export] public float   JijiHoleTopFadeTime     = 0.25f; // 開上：淡入秒數
    // ── Export：突破後的深插振盪（對照 Unity「Stage 2：深插振盪模式」，數值照抄）──
    // 按住左鍵上下拖曳＝調整速度等級（往上拖加速、往下拖減速），雞雞依等級自動來回抽插。
    [Export] public float   S2IdleAmp          = 12f;     // 等級 0 的振盪幅度（畫布 px）
    [Export] public float   S2IdleFreq         = 0.8f;    // 等級 0 的頻率（次/秒）
    [Export] public float   S2AmpPerLevel      = 28f;     // 每級增加幅度
    [Export] public float   S2FreqPerLevel     = 0.25f;   // 每級增加頻率
    [Export] public float   S2SpeedLvMax       = 5f;      // 等級上限
    [Export] public float   S2SpeedLvRate      = 0.005f;  // 拖曳每 1 螢幕 px 改變多少等級（拖 200px ＝ 1 級）
    [Export] public float   S2InsertAmpMult    = 0.4f;    // 「插入」層跟著動的比例：0.2 → 0.4，跟 M 腿被推上去的幅度一樣（MLegThrustFollow）
    [Export] public float   S2InsertMaxY       = 999f;    // 「插入」層最多跟著移動多少：35 → 不設限，才能跟腿一樣高
    [Export] public float   S2InsertMaxScaleX  = 1.05f;   // 往上頂時「插入」層最多撐寬多少（1.35 → 1.2 → 1.1 → 1.05）
    [Export] public float   S2OpenExpandAdd    = 0.2f;    // 往上頂時開1／洞額外張開（0.35 → 0.2，擴張幅度小一點）
    [Export] public float   S2BodyFollowMult   = 0.25f;   // 全身被頂上去的比例
    [Export] public float   S2TummyThreshLv    = 0.5f;    // 等級 ≥ 這個值…
    [Export] public float   S2TummyThreshAmp   = 0.33f;   // …且幅度 ≥ 範圍 × 這個比例 → 肚子鼓起
    [Export] public float   S2TummyYScale      = 1f;      // 肚子 Y 縮放：改回 1＝原圖形狀（Unity 原本壓成 0.5，看起來扁扁變形）
    [Export] public Vector2 S2TummyOffset      = new Vector2(0f, 80f);   // 肚子相對雞雞（Unity 座標，Y 正＝往上）
    [Export] public float   S2UpperZoneT       = 0.33f;   // 肚子出現後振盪下限（範圍下 1/3）

    // ── 狀態 ────────────────────────────────────────────────────────────
    Vector2 _jijiOffset;            // 目前相對基準位置的偏移（畫布 px）
    Vector2 _jijiPhase2Pos;         // Phase 2 到達的偏移（插入的基準點）
    float   _jijiAlpha = 1f;
    System.Threading.CancellationTokenSource _jijiAnimCts;
    bool    _jijiAnimating;
    Vector2 _jijiLastMouse;
    float   _resistBodyPushY, _resistBodyPushCur, _resistBodyPushTotal;
    ShaderMaterial _jijiClipMat;

    static readonly string[] _jijiLayers = { "雞雞", "男人肚子", "精液" };
    ShaderMaterial _jijiSemMat;
    string[] _jijiUniformNames;
    float _glansLen, _glansRamp;   // 龜頭白色暈染：長度（貼圖 px）、淡入進度
    [Export] public float GlansWhiteOuterPx = 130f;   // 外射後：白色暈染從龜頭往下多長（貼圖 px）
    [Export] public float GlansWhiteInnerPx = 270f;   // 內射後：白色暈染的長度（約半個雞雞）
    public float _semAlpha = 1f;   // 雞雞上精液的殘留程度（用衛生紙擦會降低）
    float _semReveal = 1f, _semPool, _semSpread, _semFlow, _semT = -1f;   // 內射後精液動畫（_semT<0＝沒在播）
    [Export] public float SemRevealSec = 7f;    // 內射後精液從尖端到根部出現的秒數
    [Export] public float SemFlowSec   = 16f;   // 銜接處白色聚集→暈開→流開到消失的總秒數
    bool _jijiSemenShown;   // 雞雞上的「精液」圖層：射精後才顯示，收起雞雞或清精液時隱藏

    /// <summary>射精時呼叫：雞雞有出現的話，雞雞上的精液圖層跟著出現。</summary>
    /// <summary>內射後呼叫：精液從尖端到根部慢慢出現，銜接處聚集白色後暈開、流開。</summary>
    public void StartSemenFlow() { if (_jijiPhase > 0) { _glansLen = GlansWhiteInnerPx; _jijiSemenShown = true; _semT = 0f; _semReveal = 0f; _semPool = 0f; _semSpread = 0f; _semFlow = 0f; } }
    void UpdateSemenFlow(float dt)
    {
        if (_semT < 0f) { _semReveal = 1f; _semPool = 0f; return; }
        _semT += dt;
        _semReveal = Mathf.Clamp(_semT / Mathf.Max(0.1f, SemRevealSec), 0f, 1f);
        float tt = _semT / Mathf.Max(1f, SemFlowSec);            // 0~1
        _semPool   = Mathf.SmoothStep(0f, 0.12f, tt) * (1f - Mathf.SmoothStep(0.35f, 1f, tt));   // 先聚集變濃，之後慢慢淡掉
        _semSpread = Mathf.SmoothStep(0.1f, 0.9f, tt);           // 越來越暈開
        _semFlow   = Mathf.SmoothStep(0.05f, 1f, tt);            // 往下流
        if (_semT > SemFlowSec) { _semT = -1f; _semReveal = 1f; _semPool = 0f; }
    }
    public void ShowJijiSemen() { if (_jijiPhase > 0) { if (!_jijiSemenShown) _glansLen = GlansWhiteOuterPx; _jijiSemenShown = true; _semT = -1f; _semReveal = 1f; } }
    public void HideJijiSemen() { _jijiSemenShown = false; _semAlpha = 1f; _glansLen = 0f; _glansRamp = 0f; _semT = -1f; _semReveal = 1f; _semPool = 0f; SetLayerQuiet("精液", false); }

    /// <summary>開1／洞 跟著插入深度的額外位移，由呼吸迴圈疊加在基準位置上（見 Effects.cs 一般圖層迴圈）。</summary>
    public readonly System.Collections.Generic.Dictionary<string, Vector2> JijiLayerExtraOffset = new();
    float _holeTopAlpha;      // 開上 目前透明度（出現後維持，直到拔出/收起）
    bool _openLayersFrozen;   // 突破（插入層出現）後，開1 改成跟著「插入」層走
    float _insOffYPrev;
    float _insFollowT;
    float _s2CenterCur = float.NaN;    // 抽插中心位置（平滑後）
    float _insShownY;                  // 插入層此刻實際套用的 Y 位移（畫布 px，負＝往上）
    float _junctionSmooth; bool _junctionInit;
    float _open1InsertDiffY;  // 開1 相對 插入 的 Y 差（突破當下記錄）
    float _s2SwayT, _s2SpeedLv, _s2Sin, _s2Amp;
    float _s2OscY;              // 純抽插深度（肚子用）
    float _breakSurgeT = -1f;   // 突破衝刺計時（<0 ＝ 沒在衝）
    bool  _s2TummyOn;
    /// <summary>開1／洞 貼圖內容的中心（2000x3200 畫布像素），X 縮放要以這裡為軸心，不然會往畫布中線縮。</summary>
    static readonly System.Collections.Generic.Dictionary<string, Vector2> _openLayerPivot = new()
    {
        { "開1", new Vector2(985f,   1461.5f) },
        { "洞",  new Vector2(987.5f, 1488.5f) },
        { "插入", new Vector2(975.5f, 1299f) },
    };

    /// <summary>所有角色圖層共用的整體抖動/位移：高潮痙攣 ＋ 插入阻力的往上推擠與發抖。</summary>
    Vector2 BodyShake => _spasmsBodyShake + new Vector2(0f, -_resistBodyPushTotal);

    // 給 DEV 面板（ExpressionTester）顯示
    public bool  IsResisting        => _jijiPhase == 3 && _resistBlocking;
    public float ResistProgressNorm => _resistTarget > 0f ? Mathf.Clamp(_resistProgress / _resistTarget, 0f, 1f) : 0f;
    public int   JijiPhasePublic    => _jijiPhase;

    // ════════════════════════════════════════════════════════════════════
    //  Phase 切換
    // ════════════════════════════════════════════════════════════════════

    // ── 肛門目標：套用跟 B 一樣的「靠近→按住突破→抽插」，但不開肚子／洞／開1／插入那些疊層 ──
    bool _jijiAnus;   // 這次雞雞的目標是肛門（m腿/肛門）而不是 B
    [Export] public Vector2 AnusTargetOffsetPx = new Vector2(-3.5f, -23f);   // 雞雞對準肛門時的位置微調（螢幕 px，往下＝+Y；Play 中可調）
    [Export(PropertyHint.Range, "0,60,1")] public float AnusClickMarginPx = 8f;   // 點肛門圖層判定往外擴多少 px
    Rect2 _anusBox; bool _anusBoxSet;

    // ── 肛門被撐開：插入時肛門圖層左右擴張、往上拱成弧形；雞雞的裁切跟著變形後的肛門 ──
    [Export] public float AnusOpenFullPx  = 60f;    // 雞雞碰到後再往裡推多少（畫布 px）肛門撐到最大
    [Export] public float AnusRightPx     = 2f;    // 肛門擴張時整體向右多偏移幾 px
    [Export] public float AnusBottomCutPx = 1.5f;  // 肛門底部弧形裁切（兩側比中間多切幾 px）
    [Export] public float AnusWidenMax    = 1.7f;  // 最大左右擴張比例（0.35＝寬 35%）
    [Export] public float AnusRisePx      = 6f;     // 最大整體往上（畫布 px）
    [Export] public float AnusArchPx      = 8f;     // 最大弧形：中間比兩側再多往上多少（畫布 px）
    [Export] public float AnusOpenSpeed   = 6f;     // 撐開／收回的跟隨速度
    [Export] public float AnusClipOffsetPx = 10f;     // 肛門目標：裁切線相對「肛門圖層上緣」的位置（螢幕 px，正＝往下、負＝往上）
    [Export(PropertyHint.Range, "0,0.8,0.01")] public float AnusTaperMax = 0.12f;     // 突破後肛門下方雞雞變細的最大比例（0.3＝窄 30%）
    [Export(PropertyHint.Range, "0,0.8,0.01")] public float PussyTaperMax = 0.08f;   // 插入小穴後，雞雞靠近交接處變細的最大比例（0＝不變細）
    [Export] public float AnusTaperArchPx  = 10f;   // 變細起點的弧形：兩側比中間再往下多少 px（0＝水平）
    [Export] public float AnusTaperStartPx = 5f;    // 變細從裁切線（肛門）往下多少 px 開始
    [Export] public float AnusTaperLenPx   = 40f;   // 變細往下漸層到消失的長度（螢幕 px）；越上面越細
    float _anusOpen;   // 目前撐開程度 0~1
    ShaderMaterial _anusMat;

    /// <summary>肛門被插時，肛門圖層要畫在雞雞／精液上面（不然撐開的樣子被雞雞蓋住）；不是肛門目標時放回前髮前面。</summary>
    void UpdateAnusLayerOrder()
    {
        if (_charRoot == null || !Layers.TryGetValue("m腿/肛門", out var an) || an == null || an.GetParent() != _charRoot) return;
        bool above = _jijiAnus && _jijiPhase >= 1;
        if (above)
        {
            int target = -1;
            foreach (var n in new[] { "雞雞", "精液" })
                if (Layers.TryGetValue(n, out var l) && l != null && l.GetParent() == _charRoot) target = Mathf.Max(target, l.GetIndex());
            if (target >= 0 && an.GetIndex() < target) _charRoot.MoveChild(an, target);
        }
        else
        {   // 不是肛門目標：肛門要放回「雞雞／洞／開1／插入…這一組的最下面」，不能留在雞雞上面。
            // （以前放回的是「前髮前面」＝雞雞上面，而且只在肛門排在前髮後面時才會動，所以肛門插過一次之後，肛門會一直跑到雞雞上面）
            int min = int.MaxValue;
            foreach (var n in new[] { "洞", "雞雞", "精液", "開上", "開1", "插入", "男人肚子" })
                if (Layers.TryGetValue(n, out var l) && l != null && l.GetParent() == _charRoot) min = Mathf.Min(min, l.GetIndex());
            if (min != int.MaxValue && an.GetIndex() > min) _charRoot.MoveChild(an, min);
        }
    }

    void UpdateAnusDeform(float dt)
    {
        UpdateAnusLayerOrder();
        float tgt = (_jijiAnus && _jijiPhase == 3) ? Mathf.Clamp(JijiTouchDepth() / Mathf.Max(1f, AnusOpenFullPx), 0f, 1f) : 0f;
        _anusOpen = Mathf.Lerp(_anusOpen, tgt, 1f - Mathf.Exp(-AnusOpenSpeed * dt));
        if (_anusOpen < 0.001f && tgt <= 0f) _anusOpen = 0f;
        if (!Layers.TryGetValue("m腿/肛門", out var c) || c is not TextureRect tr || tr.Texture == null) return;
        if (_anusMat == null)
        {
            if (tr.Material != null) return;
            _anusMat = new ShaderMaterial { Shader = new Shader { Code = @"shader_type canvas_item;
uniform float cx_uv = 0.5;
uniform float hw_uv = 0.04;
uniform float widen = 0.0;
uniform float rise_uv = 0.0;
uniform float arch_uv = 0.0;
uniform float shift_uv = 0.0;
uniform float bot_uv = 1.0;
uniform float cut_uv = 0.0;
varying vec4 vcol;
void vertex() { vcol = COLOR; }
void fragment() {
    float u = clamp((UV.x - cx_uv) / hw_uv, -1.0, 1.0);
    float dy = rise_uv + arch_uv * (1.0 - u * u);                // 中間往上多、兩側少 → 弧形
    vec2 uv = vec2(cx_uv + (UV.x - cx_uv) / (1.0 + widen) - shift_uv, UV.y + dy);
    float lim = bot_uv - cut_uv * u * u;                          // 底部弧形裁切：中間留、兩側多切
    vec4 ac = texture(TEXTURE, uv) * vcol;
    ac.a *= 1.0 - smoothstep(lim, lim + 0.0004, uv.y) * step(0.00001, cut_uv);
    COLOR = ac;
}" } };
            tr.Material = _anusMat;
        }
        if (!_anusBoxSet) { _anusBox = PinchScanBox(tr); _anusBoxSet = true; }
        Vector2 sz = tr.Size;
        if (_anusBox.Size.X > 0f && sz.X > 0f && sz.Y > 0f)
        {
            _anusMat.SetShaderParameter("cx_uv", (_anusBox.Position.X + _anusBox.Size.X * 0.5f) / sz.X);
            _anusMat.SetShaderParameter("hw_uv", Mathf.Max(0.001f, _anusBox.Size.X * 0.5f / sz.X));
            _anusMat.SetShaderParameter("widen", AnusWidenMax * _anusOpen);
            _anusMat.SetShaderParameter("rise_uv", AnusRisePx * _anusOpen / sz.Y);
            _anusMat.SetShaderParameter("arch_uv", AnusArchPx * _anusOpen / sz.Y);
            _anusMat.SetShaderParameter("shift_uv", AnusRightPx * _anusOpen / sz.X);
            _anusMat.SetShaderParameter("bot_uv", (_anusBox.Position.Y + _anusBox.Size.Y) / sz.Y);
            _anusMat.SetShaderParameter("cut_uv", AnusBottomCutPx * _anusOpen / sz.Y);
        }
    }

    bool AnusCenterScreen(out Vector2 screen)
    {
        screen = Vector2.Zero;
        if (!Layers.TryGetValue("m腿/肛門", out var c) || c is not TextureRect tr || tr.Texture == null) return false;
        if (!_anusBoxSet) { _anusBox = PinchScanBox(tr); _anusBoxSet = true; }
        if (_anusBox.Size.X <= 0f) return false;
        screen = tr.GetGlobalTransform() * (_anusBox.Position + _anusBox.Size * 0.5f);
        return true;
    }

    bool IsMouseOverMLegAnus(Vector2 screenPos)
    {
        if (!_mLegOpen || !Layers.TryGetValue("m腿/肛門", out var c) || c == null || !c.IsVisibleInTree()) return false;
        float m = AnusClickMarginPx * Mathf.Max(0.01f, _camZoom);
        return PinchOnLayer("m腿/肛門", screenPos) || PinchOnLayer("m腿/肛門", screenPos + new Vector2(m, 0f)) || PinchOnLayer("m腿/肛門", screenPos - new Vector2(m, 0f))
            || PinchOnLayer("m腿/肛門", screenPos + new Vector2(0f, m)) || PinchOnLayer("m腿/肛門", screenPos - new Vector2(0f, m));
    }

    /// <summary>點肛門圖層（M 腿打開時）：跟點 B 一樣的流程——靠近 → 入口按下 → 按住突破。</summary>
    void OnAnusClicked()
    {
        if (_jijiPhase >= 2 && !_jijiAnus) return;   // 已經在對 B 插入／靠近：不搶
        if (_jijiPhase == 3) return;
        CancelBTouch();
        _jijiAnus = true;
        SayMaleEv(MaleAnusLines);
        if (_jijiPhase == 0) ShowJiji();
        else OnJijiHotspotDown();
    }

    void HideAnusBlockedOverlays()
    {   // 肛門不需要肚子／洞／開1／插入／開上
        foreach (var n in new[] { "開1", "洞", "開上", "插入", "肚子" })
            if (Layers.TryGetValue(n, out var c) && c != null && c.Visible) { c.Visible = false; c.Scale = Vector2.One; }
        _s2TummyOn = false;
    }

    /// <summary>B 熱點中心 → 雞雞偏移：讓雞雞尖端對準 B（對照 Unity MoveJijiToHotspot）。肛門模式改對準肛門圖層。</summary>
    Vector2 CalcPhase2Offset()
    {
        Vector2 off = Vector2.Zero;
        var h4 = (_hotspotRTs != null && _hotspotRTs.Length > 4) ? _hotspotRTs[4] : null;
        Vector2 anusScr = Vector2.Zero;
        bool useAnus = _jijiAnus && AnusCenterScreen(out anusScr);
        if (useAnus && h4 != null && IsInstanceValid(h4) && _charRoot != null && _layerBasePos.TryGetValue("雞雞", out var basePosA))
        {   // 肛門 = 對準 B 的算法 + 「肛門中心 − B 中心」的差（同一套座標）
            Vector2 hC = ScreenToCanvas(h4.GetGlobalRect().GetCenter());
            off = hC - (basePosA + _jijiTipLocal) + (ScreenToCanvas(anusScr) - hC);
            var res = off + (JijiPhase2Offset + AnusTargetOffsetPx) / CharScreenScale();
            GD.Print($"[AnusDbg] 肛門screen={anusScr} B中心screen={h4.GetGlobalRect().GetCenter()} 偏移={res}");
            if (float.IsNaN(res.X) || float.IsNaN(res.Y) || res.Length() > 1200f) { GD.Print("[AnusDbg] 偏移異常，改用 B 的位置"); _jijiAnus = false; }
            else return res;
        }
        if (h4 != null && IsInstanceValid(h4) && _charRoot != null
            && _layerBasePos.TryGetValue("雞雞", out var basePos))
        {
            Vector2 hCanvas = ScreenToCanvas(h4.GetGlobalRect().GetCenter());   // _charRoot 本地
            // 雞雞尖端（圖層本地像素）在 _charRoot 本地的位置 = basePos + _jijiTipLocal
            off = hCanvas - (basePos + _jijiTipLocal);
        }
        return off + JijiPhase2Offset / CharScreenScale();
    }

    void ShowJiji()
    {
        if (!_erectOn) { SayMale(MaleNotHardLines); GD.Print("[Male] 勃起度不夠，雞雞不出現"); return; }
        SayMaleEv(MaleShowLines);
        CancelBTouch();   // 雞雞出現後 B 改當雞雞入口，摸 B 的手收起來
        _jijiPhase = 1; _jijiStage = 0; _jijiInsertY = 0f;
        _jijiMovingUp = false; _resistBlocking = false; _resistProgress = 0f; _jijiSurgeVel = 0f;
        _jijiPhase2Pos = CalcPhase2Offset();
        _jijiOffset = _jijiPhase2Pos + new Vector2(0f, JijiDefaultOffsetY);
        foreach (var n in _jijiLayers) SetLayerQuiet(n, n != "精液");   // 精液射精後才出現
        EnsureJijiClip();
        // 原地淡入（不滑動，對照 Unity JijiAppearCoroutine）
        _ = AnimateJijiAsync(null, fadeIn: true, JijiAppearDuration);
        GD.Print("[Jiji] → Phase 1（淡入）");
    }

    void HideOpenAndInsertLayers(bool keepHole = false)
    {
        _holeTopAlpha = 0f;
        foreach (var n in new[] { "開1", "洞", "開上", "插入", "肚子" })
        {
            if (keepHole && (n == "洞" || n == "開1")) continue;   // 拔出後洞和頂部（開1 的 Λ）一起留著、一起縮小
            if (!Layers.TryGetValue(n, out var c) || c == null) continue;
            c.Visible = false;
            c.Scale = Vector2.One;
            JijiLayerExtraOffset.Remove(n);
        }
        _openLayersFrozen = false;
        _s2SwayT = 0f; _s2CenterCur = float.NaN; _s2SpeedLv = 0f; _s2Sin = 0f; _s2Amp = 0f; _s2TummyOn = false;
        _breakSurgeT = -1f;
        _s2Paused = false; _pullActive = false; _pullProgress = 0f;
        if (!keepHole) _holeLeftOpen = false;   // 拔出/收起：衝刺維持的深度也一起清掉
    }

    /// <summary>摸 B（_bTouchRunning）期間：「洞」稍微出現；M 腿時再加上「開1」，兩者都跟著手蹭的節奏
    /// 小幅度脈動變形（_bTouchPulse，手蹭到最低時最大）。其他插入疊層（開上／插入／肚子）保持隱藏。</summary>
    void ShowBTouchHoleEffect()
    {
        foreach (var n in new[] { "開上", "插入", "肚子" })
        {
            if (!Layers.TryGetValue(n, out var c) || c == null) continue;
            c.Visible = false;
        }

        float px2c = 1f / CharScreenScale();
        // 手指在：保底 HoleTouchMin（10%），濕度越高開越大（最多 HoleWetOpenMax 30%）
        float baseScale = JijiOpenLayerMaxScaleX * Mathf.Max(HoleTouchMin, HoleWetOpenMax * Mathf.Clamp(Wetness / 100f, 0f, 1f));
        float pulse = 1f + BTouchHolePulse * _bTouchPulse;
        float sx = baseScale * pulse;

        // 開1：只有 M 腿時才出現
        if (Layers.TryGetValue("開1", out var o1) && o1 != null)
        {
            if (_mLegOpen && baseScale > 0f)
            {
                if (_openLayerPivot.TryGetValue("開1", out var pv1)) o1.PivotOffset = pv1;
                var fm = EnsureOpen1Fade(o1);
                fm.SetShaderParameter("erode", 0f);
                fm.SetShaderParameter("keep", Mathf.Clamp(JijiOpen1KeepAtMax, 0f, 1f));
                fm.SetShaderParameter("band_px", JijiOpen1FadeBandPx / CharScreenScale());
                SetOverlayOffset("開1", o1, new Vector2(JijiOpen1OffsetX * px2c, JijiOverlayOffsetY * px2c));
                o1.Visible = true;
                o1.Scale = new Vector2(sx, 1f);
            }
            else o1.Visible = false;
        }

        if (!Layers.TryGetValue("洞", out var hole) || hole == null) return;
        SetOverlayOffset("洞", hole, new Vector2(JijiHoleOffsetX * px2c, JijiHoleOffsetY * px2c));
        if (_openLayerPivot.TryGetValue("洞", out var hpv)) hole.PivotOffset = hpv;
        hole.Visible = baseScale > 0f;
        hole.Scale = new Vector2(sx, 1f);
    }

    void HideJiji()
    {
        _creampieAmount = 0;
        _firstTimeBleeding = false;   // 雞雞上的血和精液不隨收起消失，要用衛生紙擦掉（Tissue.cs）
        HideOpenAndInsertLayers();
        _jijiAnimCts?.Cancel();
        _jijiAnimating = false;
        _jijiPhase = 0; _jijiStage = 0; _jijiInsertY = 0f;
        _jijiMovingUp = false; _resistBlocking = false; _resistProgress = 0f; _jijiSurgeVel = 0f;
        _resistBodyPushY = 0f;
        foreach (var n in _jijiLayers)
        {
            if (!Layers.TryGetValue(n, out var c) || c == null) continue;
            c.Visible = false;
            c.Modulate = Colors.White;
            c.RotationDegrees = 0f;
            if (_layerBasePos.TryGetValue(n, out var bp)) c.Position = bp;
        }
        _jijiAlpha = 1f;
        _jijiAnus = false;   // 下次重新選目標（B／肛門）
        GD.Print("[Jiji] → Phase 0（收起）");
    }

    /// <summary>B 熱點按下（ButtonDown）。</summary>
    void OnJijiHotspotDown()
    {
        ExitHandMode();
        switch (_jijiPhase)
        {
            case 0:
                ShowJiji();
                break;
            case 1:
                // Phase 1 → 2：滑到 B（跟 Unity 一樣雞雞＋男人肚子一起動）
                _jijiPhase2Pos = CalcPhase2Offset();
                _jijiPhase = 2;
                _ = AnimateJijiAsync(_jijiPhase2Pos, fadeIn: false,
                                     JijiAppearDuration / Mathf.Max(JijiPhase2SpeedMult, 0.1f));
                GD.Print("[Jiji] → Phase 2（靠近）");
                break;
            case 2:
                if (_jijiAnimating) break;   // 還在滑動中不接受
                if (_jijiAnus && AnusCenterScreen(out var dbgA) && Layers.TryGetValue("雞雞", out var dbgJ) && dbgJ != null)
                    GD.Print($"[AnusDbg] 肛門中心 screen={dbgA}  雞雞尖端 screen={dbgJ.GetGlobalTransform() * _jijiTipLocal}  差(尖端-肛門)={dbgJ.GetGlobalTransform() * _jijiTipLocal - dbgA}");
                StartInsertion();
                break;
            // Phase 3：左鍵是否按住由 UpdateJiji 每幀直接讀，這裡不用處理
        }
    }

    void StartInsertion()
    {
        if (!_mLegOpen) { GD.Print("[Jiji] 沒開 M 腿：不能插入／突破"); return; }
        if (!_jijiAnus && MPantyBlocksInsert) { GD.Print("[Jiji] 內褲還穿著：要先把內褲向右撥開才能插入"); return; }
        SayMaleEv(MaleInsertLines);   // 一般腿只能靠近，不能向上突破
        _holeLeftOpen = false;   // 重新插入：洞回到一般的張開邏輯
        _jijiPhase = 3; _jijiStage = 0; _jijiInsertY = 0f;
        _jijiMovingUp = true;
        _resistBlocking = false; _resistProgress = 0f; _jijiSurgeVel = 0f;
        _resistBodyPushY = 0f; _resistBodyPushCur = 0f;
        _jijiLastMouse = GetViewport().GetMousePosition();
        var pdm = PlayerDataManager.Instance;
        if (pdm != null) { pdm.TodayDidNothing = false; pdm.TodayInserted = true; }
        GD.Print("[Jiji] → Phase 3（開始插入 Stage 0）");
    }

    void OnJijiRightClick()
    {
        switch (_jijiPhase)
        {
            case 3:
                if (_pullOutAnim || _insertEjacActive) break;          // 正在拔出／內射動畫中
                if (_jijiStage >= 1)
                {
                    // 突破後：第一次右鍵先停下抽插；停下後再右鍵（按住）才開始拔出（有阻力，見 UpdatePullOut）
                    if (_playForced) { }   // 強制加速中：不能停下
                    else if (!_s2Paused) { _s2Paused = true; _s2SpeedLv = 0f; GD.Print("[Jiji] 右鍵 → 停止抽插（速度重置）"); }
                    else if (!_pullActive) { _pullActive = true; _pullProgress = 0f; _pullTarget = PullOutTarget(); GD.Print($"[Jiji] 右鍵按住 → 開始拔出（目標 {_pullTarget:F0}）"); }
                    break;
                }
                BackToPhase2("右鍵");
                break;
            case 2:
                _jijiPhase = 1;
                _ = AnimateJijiAsync(_jijiPhase2Pos + new Vector2(0f, JijiDefaultOffsetY), false, JijiAppearDuration);
                GD.Print("[Jiji] 右鍵 Phase 2 → 1");
                break;
            case 1:
                HideJiji();
                break;
        }
    }

    void BackToPhase2(string why)
    {
        HideOpenAndInsertLayers();
        _jijiPhase = 2; _jijiStage = 0; _jijiInsertY = 0f;
        _jijiMovingUp = false; _resistBlocking = false; _resistProgress = 0f; _jijiSurgeVel = 0f;
        _resistBodyPushY = 0f;
        _ = AnimateJijiAsync(_jijiPhase2Pos, false, JijiAppearDuration);
        GD.Print($"[Jiji] {why} → Phase 2");
    }

    /// <summary>滑動到目標偏移（ease-out cubic）或原地淡入。</summary>
    async System.Threading.Tasks.Task AnimateJijiAsync(Vector2? target, bool fadeIn, float dur)
    {
        _jijiAnimCts?.Cancel();
        var cts = new System.Threading.CancellationTokenSource();
        _jijiAnimCts = cts;
        _jijiAnimating = true;
        Vector2 start = _jijiOffset;
        dur = Mathf.Max(0.01f, dur);
        float t = 0f;
        if (fadeIn) _jijiAlpha = 0f;
        try
        {
            while (t < dur)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (cts.IsCancellationRequested || !IsInstanceValid(this)) return;
                t += (float)GetProcessDeltaTime();
                float p = Mathf.Clamp(t / dur, 0f, 1f);
                if (target.HasValue)
                {
                    float ease = 1f - Mathf.Pow(1f - p, 3f);
                    _jijiOffset = start.Lerp(target.Value, ease);
                }
                if (fadeIn) _jijiAlpha = p;
            }
            if (target.HasValue) _jijiOffset = target.Value;
            _jijiAlpha = 1f;
        }
        finally
        {
            if (_jijiAnimCts == cts) _jijiAnimating = false;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  每幀更新（_Process 最後呼叫，覆蓋呼吸系統對雞雞三層的位置）
    // ════════════════════════════════════════════════════════════════════

    void UpdateJiji(float dt)
    {
        UpdateResistBodyPush(dt);
        UpdatePussyBreath();   // 小穴跟著呼吸左右微擴（Controls.cs）
        UpdateMLegThrust(dt);  // 插入往上頂時 M 腿被推上去、往外張
        UpdateSweatAndWater(dt);   // 過程中流汗和小穴水（InsertEjac.cs）
        UpdateAnusDeform(dt);   // 肛門撐開變形（雞雞裁切也用 _anusOpen）
        // 雞雞沒出現（Phase 0）時也要讓摸 B 的洞／開1 能顯示與收起，不然下面這行 return 之後
        // UpdateOpenLayers() 根本不會跑（這就是 M 腿點小穴洞和開都沒出現的原因）。
        if (_jijiPhase == 0 && (_bTouchRunning || _openLayersFrozen || JijiLayerExtraOffset.Count > 0 || _holeLeftOpen))
            UpdateOpenLayers();
        if (_jijiPhase == 0) return;

        if (_jijiPhase == 3 && !_jijiAnimating && !_pullOutAnim && !_insertEjacActive)
            UpdateInsertion(dt);
        if (_jijiPhase == 3) UpdateMLegThrust(dt);   // 用這一幀剛算好的抽插值再算一次，M 腿才不會比插入慢一幀

        Vector2 off = _jijiPhase == 3 ? _jijiPhase2Pos + new Vector2(0f, -_jijiInsertY) : _jijiOffset;
        if (_jijiPhase == 3) _jijiOffset = off;

        foreach (var n in _jijiLayers)
        {
            if (!Layers.TryGetValue(n, out var c) || c == null) continue;
            if (_layerBasePos.TryGetValue(n, out var bp)) c.Position = bp + off + (n == "男人肚子" ? new Vector2(JijiBellyOffsetX, 0f) : (_jijiPhase == 3 && _jijiStage >= 1 ? new Vector2(JijiInsertOffsetX, 0f) : Vector2.Zero));
            c.Modulate = new Color(1f, 1f, 1f, _jijiAlpha * (n == "精液" ? _semAlpha : 1f));
            if (n == "精液") c.Visible = _jijiSemenShown;
            // 雞雞／精液：以雞雞底部中央為錨點微旋轉（男人肚子不轉）
            if (n != "男人肚子")
            {
                c.PivotOffset = _jijiBottomLocal;
                c.RotationDegrees = JijiRotationDeg;
            }
        }

        UpdateOpenLayers();

        // 裁切線：固定在 Phase 2 時雞雞尖端的高度（圖層本地像素 → UV）
        if (_jijiClipMat != null && Layers.TryGetValue("雞雞", out var jj) && jj != null && jj.Size.Y > 0f)
        {
            float clipOffScreen = JijiClipOffsetY - (_jijiPhase == 3 && _jijiStage >= 1 ? JijiStage1ClipLiftPx : 0f);
            float aw = _jijiAnus ? _anusOpen : 0f;   // 肛門撐開程度：裁切圓左右拉開、整條線往上、兩側相對更低（弧形）
            float clipLocalY = _jijiTipLocal.Y + clipOffScreen / CharScreenScale() + (_jijiPhase2Pos.Y - off.Y) - (AnusRisePx + AnusArchPx) * aw;
            // 抽插時「插入」層會往上動，裁切線也要跟著它往上，不然插入和雞雞之間會露出一段沒有雞雞的皮膚色
            float clipBaseY = clipLocalY;   // 世界固定的入口線（已抵銷雞雞自己的移動）
            if (_jijiPhase == 3 && _jijiStage >= 1 && Layers.TryGetValue("插入", out var insL) && insL != null && insL.Visible)
            {
                // 剛插入（突破）那一下插入層會大幅往上衝，這時不跟隨；插穩約 1.2 秒後才慢慢開始跟隨。
                // 跟隨量直接用「插入層此刻實際被移動的量」（和插入同一份數值，不再另外量座標），所以不會有基準點誤差。
                _insFollowT += (float)GetProcessDeltaTime();
                float fk = Mathf.Clamp((_insFollowT - 1.2f) / 1.5f, 0f, 1f);
                clipLocalY += Mathf.Min(0f, _insShownY) * fk;
            }
            else _insFollowT = 0f;
            // 肛門：裁切線直接放在肛門圖層（變形後）的上緣，雞雞一進到肛門上面就被裁掉
            if (_jijiAnus && _jijiPhase >= 2 && _anusBoxSet && _anusBox.Size.X > 0f && Layers.TryGetValue("m腿/肛門", out var anL) && anL != null)
                clipLocalY = _anusBox.Position.Y - (AnusRisePx + AnusArchPx) * aw + anL.Position.Y - jj.Position.Y + AnusClipOffsetPx / CharScreenScale();
            // 抽插時插入和雞雞之間可能露出縫：雞雞的裁切線再往上讓一點點，讓雞雞多伸進插入層底下（插入層蓋在雞雞上面，所以看不到多出來的部分）
            if (_jijiPhase == 3 && _jijiStage >= 1 && !_jijiAnus) clipLocalY -= JijiInsertOverlapPx / CharScreenScale();
            // 紅暈／血的定位線：不跟雞雞走，只跟「插入」層走（入口線＋插入層此刻實際的位移）
            _junctionSmooth = (_jijiPhase == 3 && _jijiStage >= 1) ? clipBaseY + _insShownY : clipBaseY;
            _jijiClipMat.SetShaderParameter("junction_px", _junctionSmooth);
            _jijiClipMat.SetShaderParameter("clip_v", JijiClipEnabled ? clipLocalY / jj.Size.Y : 0f);
            float px2uv = 1f / CharScreenScale() / jj.Size.Y;
            float s2c = 1f / CharScreenScale();   // 螢幕 px → 貼圖（畫布）px
            _jijiClipMat.SetShaderParameter("arch_v", JijiClipEnabled ? JijiClipArchPx * px2uv : 0f);
            _jijiClipMat.SetShaderParameter("feather_v", Mathf.Max(JijiClipFeatherPx * px2uv, 0.00001f));
            _jijiClipMat.SetShaderParameter("clip_px", clipLocalY);
            _jijiClipMat.SetShaderParameter("circle_on", JijiClipEnabled && JijiClipCircleRadius > 0f ? 1f : 0f);
            _jijiClipMat.SetShaderParameter("circle_off_px", ClipCircleOffsetEff * s2c * (1f + AnusWidenMax * aw));
            _jijiClipMat.SetShaderParameter("circle_r_px", JijiClipCircleRadius * s2c * (1f + AnusWidenMax * aw * 0.5f));
            _jijiClipMat.SetShaderParameter("circle_dip_px", JijiClipCircleDip * s2c + AnusArchPx * aw);
            _jijiClipMat.SetShaderParameter("feather_px", Mathf.Max(JijiClipFeatherPx * s2c, 0.01f));
            _jijiClipMat.SetShaderParameter("debug_on", ClipDebug ? 1f : 0f);
            _jijiClipMat.SetShaderParameter("blood_amt", _jijiBlood);
            _jijiClipMat.SetShaderParameter("blood_seed", _bloodSeed);
            _jijiClipMat.SetShaderParameter("blood_blur", _jijiBloodBlur);
            _jijiCutLocalY = clipLocalY;
            UpdateJijiBloodRoots();
            bool flushOn = _jijiPhase == 3 && _jijiStage >= 1 && !_pullOutAnim;
            _jijiFlush = Mathf.MoveToward(_jijiFlush, flushOn ? 1f : 0f, (float)GetProcessDeltaTime() * (flushOn ? 0.7f : 0.8f));
            _jijiClipMat.SetShaderParameter("flush_amt", _jijiFlush);
            _jijiClipMat.SetShaderParameter("circle_rise_px", JijiTouchDepth() * JijiClipCircleFollow);
            float taperK_ = (_jijiPhase == 3 && _jijiStage >= 1) ? (_jijiAnus ? AnusTaperMax * aw : PussyTaperMax) : 0f;
            _jijiClipMat.SetShaderParameter("taper_k", taperK_);   // 插入小穴時雞雞和插入物件交接處也往中間收，配合插入物件的寬度
            // 雞雞左緣（x≈892）離收縮中線（x≈998）約 106px，變細時左緣被往右縮 taperK_*106；
            // 插入物件的左下角也往內（右）縮同樣的量，跟著雞雞的左緣走（雞雞本身不動）
            if (_insertEdgeMat != null)
                _insertEdgeMat.SetShaderParameter("wid_lb", (InsertShrinkBottomLeftPx + taperK_ * 106f * InsertShrinkTaperFollow) / 210f);
            _jijiClipMat.SetShaderParameter("taper_start_px", clipLocalY + AnusTaperStartPx * s2c);
            _jijiClipMat.SetShaderParameter("taper_len_px", AnusTaperLenPx * s2c);
            _jijiClipMat.SetShaderParameter("taper_arch_px", AnusTaperArchPx * s2c);
            UpdateSemenFlow((float)GetProcessDeltaTime());
            _glansRamp = Mathf.MoveToward(_glansRamp, _jijiSemenShown ? 1f : 0f, (float)GetProcessDeltaTime() / 1.5f);   // 射完後慢慢染開
            _jijiClipMat.SetShaderParameter("gw_amt", _glansRamp * _semAlpha * 0.85f);
            _jijiClipMat.SetShaderParameter("gw_len", Mathf.Max(_glansLen, 1f));
            _jijiClipMat.SetShaderParameter("gw_from", (_jijiPhase == 3 && _jijiStage >= 1) ? clipLocalY : 1585f);
            _jijiClipMat.SetShaderParameter("sem_reveal", 1f);
            _jijiClipMat.SetShaderParameter("sem_pool", _semPool);
            _jijiClipMat.SetShaderParameter("sem_spread", _semSpread);
            _jijiClipMat.SetShaderParameter("sem_flow", _semFlow);
            if (_jijiSemMat != null)
            {
                if (_jijiUniformNames == null)
                {   // 只在第一次列出參數名稱，之後每幀不再配置記憶體（之前每幀重新列表造成很卡）
                    var names = new System.Collections.Generic.List<string>();
                    foreach (var u in _jijiClipMat.Shader.GetShaderUniformList())
                    {
                        string un = (string)((Godot.Collections.Dictionary)u)["name"];
                        if (!un.StartsWith("sem_") && !un.StartsWith("gw_")) names.Add(un);
                    }
                    _jijiUniformNames = names.ToArray();
                }
                foreach (var un in _jijiUniformNames) _jijiSemMat.SetShaderParameter(un, _jijiClipMat.GetShaderParameter(un));
                _jijiSemMat.SetShaderParameter("sem_on", 1f);
                _jijiSemMat.SetShaderParameter("sem_reveal", _semReveal);
            }
            if (ClipDebug) UpdateClipDebugOverlay(jj, clipLocalY, s2c);
            else if (_clipDebugOverlay != null) _clipDebugOverlay.Visible = false;
        }
    }

    /// <summary>開1／洞／插入／肚子 疊層（對照 Unity Phase 3 開合層區塊 + Stage 1 振盪區塊）。
    /// Stage 0：推進到該段 JijiOpenLayerStartT 以上，開1／洞 從窄張開，並跟著雞雞往上。
    /// Stage 1（突破後）：「插入」層出現並隨抽插微動／撐寬，開1 跟著插入層，洞 跟著雞雞，
    /// 往上頂時兩者額外張開；速度夠快時「肚子」鼓起跟著雞雞。
    /// 全部疊層都加上 JijiOverlayOffsetY 的整體微調。</summary>
    /// <summary>突破後把 開1 移到雞雞下面（洞本來就在雞雞下面）；突破前／拔出後放回雞雞上面。
    /// 直接調整 _charRoot 底下的子節點順序（Godot 越後面的子節點畫在越上面）。</summary>
    void UpdateOverlayDrawOrder()
    {
        if (_charRoot == null) return;
        UpdateFluidLayer();   // 奶油泡芙／流出的精液／血：放在雞雞正下面（InsertEjac.cs）
        if (!Layers.TryGetValue("雞雞", out var jj) || jj == null) return;
        if (!Layers.TryGetValue("開1", out var o1) || o1 == null) return;
        if (jj.GetParent() != _charRoot || o1.GetParent() != _charRoot) return;
        bool below = _jijiPhase == 3 && _jijiStage >= 1;
        int ji = jj.GetIndex(), oi = o1.GetIndex();
        if (below && oi > ji)
            _charRoot.MoveChild(o1, ji);                       // 放到雞雞正下面
        else if (!below && oi < ji)
        {
            // 放回原本位置：開上後面（沒有開上就放在雞雞/精液後面）
            int target = Layers.TryGetValue("開上", out var ks) && ks != null && ks.GetParent() == _charRoot
                ? ks.GetIndex()
                : (Layers.TryGetValue("精液", out var sm) && sm != null && sm.GetParent() == _charRoot ? sm.GetIndex() : jj.GetIndex());
            _charRoot.MoveChild(o1, target);                   // o1 在前面，移除後 target 往前一格 → 剛好落在 target 後面
        }
    }

    void UpdateOpenLayers()
    {
        if (_jijiAnus) { HideAnusBlockedOverlays(); return; }   // 肛門：沒有開肚子／洞／開1／插入的效果
        UpdateOverlayDrawOrder();
        if (_jijiPhase != 3 && _holeLeftOpen)
        {
            // 拔出後／被舔濕：洞最多開到 HoleWetOpenMax（30%），之後隨濕度下降慢慢合起來（Play.cs 的濕度）
            if (Layers.TryGetValue("洞", out var hl) && hl != null)
            {
                float full = JijiOpenLayerMaxScaleX;
                float wet01 = Mathf.Clamp(Wetness / 100f, 0f, 1f);
                float target = full * HoleWetOpenMax * wet01;
                bool lickingB = _lickOn && _lickTarget == "B";
                if (lickingB || _bTouchRunning) target = Mathf.Max(target, full * HoleTouchMin);   // 被舔／手指在：保底 10%
                float speed = _holeLeftScale > target ? HoleCloseSpeed : 3f;   // 張開快、合起來慢
                _holeLeftScale = Mathf.Lerp(_holeLeftScale, target, 1f - Mathf.Exp(-speed * (float)GetProcessDeltaTime()));
                if (!JijiLayerExtraOffset.ContainsKey("洞"))
                    SetOverlayOffset("洞", hl, new Vector2(JijiHoleOffsetX / CharScreenScale(), JijiHoleOffsetY / CharScreenScale()));
                if (_openLayerPivot.TryGetValue("洞", out var hpv)) hl.PivotOffset = hpv;
                hl.Visible = true;
                hl.Scale = new Vector2(_holeLeftScale, 1f);
                if (Layers.TryGetValue("開1", out var tp) && tp != null)
                {   // 頂部（開1）跟洞同一個縮放、同一個中心一起縮小，洞合起來頂部也一起消失
                    if (_openLayerPivot.TryGetValue("開1", out var tpv)) tp.PivotOffset = tpv;
                    SetOverlayOffset("開1", tp, new Vector2(JijiOpen1OffsetX / CharScreenScale(), JijiOverlayOffsetY / CharScreenScale()));
                    tp.Visible = true;
                    tp.Scale = new Vector2(_holeLeftScale, 1f);
                }
                if (!lickingB && _holeLeftScale < full * 0.01f) { hl.Visible = false; _holeLeftOpen = false; if (Layers.TryGetValue("開1", out var tp2) && tp2 != null) { tp2.Visible = false; tp2.Scale = Vector2.One; JijiLayerExtraOffset.Remove("開1"); } }   // 完全合起來
            }
            return;
        }
        if (_jijiPhase != 3)
        {
            // 使用者要求：點 B 摸弄時（_bTouchRunning）要讓「洞」圖層稍微出現，作為視覺回饋。
            if (_bTouchRunning && _mLegOpen) { ShowBTouchHoleEffect(); return; }   // 一般腿不顯示洞／開1，只有 M 腿才顯示
            if (_openLayersFrozen || JijiLayerExtraOffset.Count > 0) HideOpenAndInsertLayers();
            return;
        }
        float px2c = 1f / CharScreenScale();                 // 螢幕 px → 畫布 px
        float oy     = JijiOverlayOffsetY     * px2c;       // 開1
        float oyHole = JijiHoleOffsetY        * px2c;       // 洞
        float oyIns  = JijiInsertLayerOffsetY * px2c;       // 插入

        float minY = _jijiStage == 0 ? JijiStage1MinY : JijiStage2MinY;
        float maxY = _jijiStage == 0 ? JijiStage1MaxY : JijiStage2MaxY;
        float lerpT = Mathf.Clamp((_jijiInsertY - minY) / Mathf.Max(maxY - minY, 0.01f), 0f, 1f);
        float openT = Mathf.Clamp((lerpT - JijiOpenLayerStartT) / Mathf.Max(1f - JijiOpenLayerStartT, 0.001f), 0f, 1f);
        if (_jijiStage >= 1) openT = Mathf.Clamp(openT + _s2Sin * S2OpenExpandAdd, 0f, 1f);

        // ── 插入層（Stage 1+）──
        float insOffY = 0f;
        if (Layers.TryGetValue("插入", out var ins) && ins != null)
        {
            if (_jijiStage >= 1)
            {
                insOffY = -Mathf.Min(_s2Sin * _s2Amp * S2InsertAmpMult, S2InsertMaxY);
                if (_openLayerPivot.TryGetValue("插入", out var pv)) ins.PivotOffset = pv;
                EnsureInsertEdgeBlur(ins);
                ins.Visible = true;
                ins.Modulate = new Color(1f, 1f, 1f, _insertFadeAlpha);   // 拔出時淡出
                float sx = Mathf.Min(1f + _s2Sin * (S2InsertMaxScaleX - 1f), S2InsertMaxScaleX);
                ins.Scale = new Vector2(sx, 1f);
                // M 腿的位移是在呼吸迴圈裡套用（晚一幀），插入這裡是即時套用 → 插入固定延後一幀，兩者才完全同步
                float insShown = _insOffYPrev; _insOffYPrev = insOffY;
                _insShownY = insShown;
                SetOverlayOffset("插入", ins, new Vector2(0f, oyIns + insShown));
            }
            else { ins.Visible = false; ins.Scale = Vector2.One; JijiLayerExtraOffset.Remove("插入"); _insOffYPrev = 0f; _insShownY = 0f; }
        }

        // ── 開1／洞 ──
        foreach (var n in new[] { "開1", "洞" })
        {
            if (!Layers.TryGetValue(n, out var c) || c == null) continue;
            if (openT <= 0f) { c.Visible = false; continue; }
            if (_openLayerPivot.TryGetValue(n, out var pv)) c.PivotOffset = pv;
            c.Visible = true;
            c.Scale = new Vector2(openT * JijiOpenLayerMaxScaleX, 1f);
            if (n == "開1")
            {
                // 推越深底部淡出越多：Stage 0 用張開程度 openT，突破後維持最多（剩 JijiOpen1KeepAtMax）
                float erode = _jijiStage >= 1 ? 1f : openT;
                var fm = EnsureOpen1Fade(c);
                fm.SetShaderParameter("erode", erode);
                fm.SetShaderParameter("keep", Mathf.Clamp(JijiOpen1KeepAtMax, 0f, 1f));
                fm.SetShaderParameter("band_px", JijiOpen1FadeBandPx / CharScreenScale());
            }

            float baseY = n == "洞" ? oyHole : oy;
            // 使用者要求：洞／開 不要跟著雞雞一起往上（往上會跑進「插入」底下被蓋住），固定在原位
            Vector2 off = new Vector2(0f, baseY);
            if (n == "開1" && _jijiStage >= 1)
            {
                // 突破當下記錄 開1 與 插入 的差，之後 開1 跟著插入層走（對照 Unity _open1TrackInsert）
                if (!_openLayersFrozen)
                {
                    float cur = JijiLayerExtraOffset.TryGetValue("開1", out var o1) ? o1.Y : oy;
                    _open1InsertDiffY = cur;   // 開1 固定在突破時的位置，上下起伏只靠 MLegThrustOffset（和M腿完全同步）
                    _openLayersFrozen = true;
                }
                off = new Vector2(0f, _open1InsertDiffY);
            }
            if (n == "開1") off.X += JijiOpen1OffsetX * px2c;
            else if (n == "洞") off.X += JijiHoleOffsetX * px2c;
            SetOverlayOffset(n, c, off);
        }

        // ── 開上（洞完全張開後淡入，位置跟洞一樣；畫在雞雞和洞上面）──
        if (!JijiUseHoleTopLayer)
        {
            if (Layers.TryGetValue("開上", out var htOff) && htOff != null) htOff.Visible = false;
        }
        else if (Layers.TryGetValue("開上", out var ht) && ht != null)
        {
            if (openT >= JijiHoleTopShowAt || _jijiStage >= 1 || _holeTopAlpha > 0f)
                _holeTopAlpha = Mathf.Min(1f, _holeTopAlpha + (float)GetProcessDeltaTime() / Mathf.Max(JijiHoleTopFadeTime, 0.01f));
            ht.Visible = _holeTopAlpha > 0f;
            ht.Modulate = new Color(1f, 1f, 1f, _holeTopAlpha);
            Vector2 holeOff = JijiLayerExtraOffset.TryGetValue("洞", out var ho) ? ho : new Vector2(0f, oyHole);
            SetOverlayOffset("開上", ht, holeOff + new Vector2(0f, JijiHoleTopExtraY * px2c));
        }

        // ── 肚子（Stage 1 速度夠快才鼓起，跟著雞雞）──
        if (Layers.TryGetValue("肚子", out var tm) && tm != null)
        {
            if (_jijiStage >= 1 && _s2TummyOn && !_s2Paused && !_pullOutAnim)   // 停下／拔出時肚子消失
            {
                tm.Visible = true;
                tm.Scale = new Vector2(1f, S2TummyYScale);
                float anim = _s2Sin * _s2Amp * S2InsertAmpMult;
                // Unity：肚子 = 雞雞位置 + S2TummyOffset（Y 正＝往上）+ 動畫 → Godot Y 取負
                SetOverlayOffset("肚子", tm, _jijiPhase2Pos + new Vector2(0f, -_s2OscY)
                    + new Vector2(S2TummyOffset.X, -S2TummyOffset.Y - anim + S2TummyExtraOffsetY * px2c
                    + S2TummyTopDropPx * px2c * Mathf.Clamp((_s2OscY - JijiStage2MinY) / Mathf.Max(JijiStage2MaxY - JijiStage2MinY, 1f), 0f, 1f)));   // 跟著雞雞走＋額外往下
            }
            else { tm.Visible = false; tm.Scale = Vector2.One; JijiLayerExtraOffset.Remove("肚子"); }
        }
    }

    /// <summary>突破後的深插振盪（對照 Unity Stage 1 區塊）：
    /// 按住左鍵上下拖曳改速度等級 → 幅度／頻率跟著變，雞雞以 |sin| 從下限往上頂再回來。</summary>
    void UpdateStage1Osc(float dt)
    {
        Vector2 mouseNow = GetViewport().GetMousePosition();
        float upPx = -(mouseNow.Y - _jijiLastMouse.Y);   // 螢幕往上為正
        _jijiLastMouse = mouseNow;

        // 停下狀態：左鍵按住（且沒在拔）＝繼續抽插
        PlayEndTick(dt);   // 遊玩時間／精液增加／強制加速（PlayEnd.cs）
        if (_s2Paused && !_pullActive && _jijiHeld && !_playTimeUp && _endureT <= 0f) { _s2Paused = false; GD.Print("[Jiji] 左鍵 → 繼續抽插"); }

        if (_jijiHeld && !_s2Paused && !_speedOwnerBreast)   // 這次拖曳歸胸管時，不動抽插速度
            _s2SpeedLv = Mathf.Clamp(_s2SpeedLv + upPx * S2SpeedLvRate, 0f, S2SpeedLvMax);

        float minY = JijiStage2MinY, maxY = JijiStage2MaxY, range = Mathf.Max(maxY - minY, 1f);
        _s2Amp = S2IdleAmp + _s2SpeedLv * S2AmpPerLevel;
        float freq = S2IdleFreq + _s2SpeedLv * S2FreqPerLevel;

        _s2TummyOn = _s2SpeedLv >= S2TummyThreshLv
                  && _s2Amp >= range * S2TummyThreshAmp - S2TummyShowLowerPx / CharScreenScale();
        // 抽插中肚子鼓起時中心位置在上面 1/3；右鍵停下後要回到最淺的位置（和剛插入時一樣），平滑過渡
        float centerTarget = (_s2TummyOn && !_s2Paused) ? minY + range * S2UpperZoneT : minY;
        _s2CenterCur = float.IsNaN(_s2CenterCur) ? centerTarget : Mathf.Lerp(_s2CenterCur, centerTarget, 1f - Mathf.Exp(-6f * dt));
        float center = _s2CenterCur;

        if (_s2Paused)
        {
            // 停下：振盪幅度平滑收到 0（停在最淺的位置）
            _s2Sin = Mathf.Lerp(_s2Sin, 0f, 1f - Mathf.Exp(-40f * dt));   // 右鍵幾乎立刻停下
        }
        else
        {
            _s2SwayT += dt * freq;
            _s2Sin = Mathf.Abs(Mathf.Sin(_s2SwayT * Mathf.Pi));
        }
        _jijiInsertY = Mathf.Clamp(center + _s2Sin * _s2Amp, minY, maxY);
        // 突破衝刺：插入層出現時雞雞先往上衝 JijiBreakSurgePx 再回到抽插位置（sin 半波：快上、回落）
        _jijiInsertY += JijiStage1LiftPx / CharScreenScale();   // 突破後整體再往上

        // 前 40% 時間快速衝到 JijiBreakSurgePx，後 60% 只回落 JijiBreakSurgeDropPx，之後維持在這個深度。
        if (_breakSurgeT >= 0f)
        {
            _breakSurgeT += dt;
            float p = Mathf.Clamp(_breakSurgeT / Mathf.Max(JijiBreakSurgeTime, 0.01f), 0f, 1f);
            float up = JijiBreakSurgePx, hold = JijiBreakSurgePx - JijiBreakSurgeDropPx;
            float surgePx = p < 0.4f
                ? up * Mathf.Sin(p / 0.4f * Mathf.Pi * 0.5f)                            // 衝上去（ease-out）
                : Mathf.Lerp(up, hold, Mathf.SmoothStep(0f, 1f, (p - 0.4f) / 0.6f));   // 小幅回落
            _jijiInsertY += surgePx / CharScreenScale();
        }
        _s2OscY = _jijiInsertY;   // 肚子跟著雞雞整體位置（含 Stage1Lift＋衝刺），保持三者同步
        _resistBodyPushY = _s2Sin * _s2Amp * S2BodyFollowMult;   // 頂到上面時全身被推上去

        if (_s2Paused) UpdatePullOut(dt);
    }

    // ════════════════════════════════════════════════════════════════════
    //  拔出（突破後：右鍵先停下，再右鍵按住拔出，有阻力但比插入小）
    // ════════════════════════════════════════════════════════════════════
    [Export] public float PullOutScoreMult  = 0.6f;   // 拔出所需分數 ＝ 突破分數 × 這個比例（力比插入小）
    [Export] public float PullOutRateMult   = 1.2f;   // 按住時累積速度倍率
    [Export] public float PullOutCreepPx    = 10f;    // 按住拔的時候雞雞被慢慢往外拉多少（螢幕 px）
    [Export] public float PullOutBodyDownPx = 4f;     // 按住拔的時候全身被往下拉多少（螢幕 px）
    [Export] public float PullOutFadeTime   = 0.3f;   // 拔出瞬間「插入」淡出秒數
    // 拔出動作（使用者要求：不要退太快太多）：先比較快地退到「入口上面一點」停一下，再慢慢滑到入口位置。
    [Export] public float PullOutFirstStopPx = 10f;   // 第一段先退到入口上方多少（螢幕 px）
    [Export] public float PullOutFirstTime   = 0.3f;  // 第一段時間（秒）
    [Export] public float PullOutSlowTime    = 1.2f;  // 第二段慢慢滑到入口的時間（秒）
    [Export] public float HoleWetOpenMax = 0.3f;   // 拔出後／被舔濕時洞最多開多少（相對全開；濕度 100 時）
    [Export] public float HoleCloseSpeed = 0.6f;
    [Export] public float HoleTouchMin   = 0.1f;   // 被舔或手指在的時候，洞至少開多少（相對全開）   // 洞合起來的速度（越小越慢）
    [Export] public float PullOutHoleShrinkMax = 0.3f; // 拔出後洞不會完全關，最多縮 30%；高潮值越高縮越少（100 時不縮）

    bool  _s2Paused, _pullActive, _pullOutAnim, _holeLeftOpen;
    float _pullProgress, _pullTarget = 60f, _insertFadeAlpha = 1f, _holeLeftScale;

    float PullOutTarget() =>
        Mathf.Max(1f, ResistBreakScoreTarget * PullOutScoreMult * (1f - Mathf.Clamp(WetnessRatio() * ResistWetnessWeight, 0f, 1f)));

    void UpdatePullOut(float dt)
    {
        if (!_pullActive) return;
        bool rightHeld = Input.IsMouseButtonPressed(MouseButton.Right);
        if (rightHeld)
        {
            _pullProgress += ResistScoreRateHold * PullOutRateMult * (1f + WetnessRatio() * ResistWetnessScoreBonus) * dt;
        }
        else
        {
            _pullProgress -= ResistScoreDecayRate * dt;
            if (_pullProgress <= 0f) { _pullProgress = 0f; _pullActive = false; GD.Print("[Jiji] 放開 → 取消拔出（維持停下）"); }
        }
        float ratio = Mathf.Clamp(_pullProgress / Mathf.Max(_pullTarget, 0.01f), 0f, 1f);
        _jijiInsertY -= ratio * PullOutCreepPx / CharScreenScale();          // 慢慢被往外拉
        _resistBodyPushY = -ratio * PullOutBodyDownPx / CharScreenScale();   // 全身被往下拉（負＝往下）
        if (_pullProgress >= _pullTarget) _ = PullOutAsync();
    }

    /// <summary>拔出成功：「插入」淡出，雞雞往外衝過頭一點再回到入口（突破衝刺的反向），洞留著不完全關閉。</summary>
    async System.Threading.Tasks.Task PullOutAsync()
    {
        if (_pullOutAnim) return;
        SayMaleEv(MalePullOutLines, true);
        _ = PullOutExprSeqAsync();   // 拔出後表情：抖動嘴 2 秒 → 小嘴巴 → 越來越平淡（InsertMouth.cs）
        _pullOutAnim = true;
        _pullActive = false;
        _resistBodyPushY = 0f;
        float startY = _jijiInsertY;
        float stopY  = PullOutFirstStopPx / CharScreenScale();
        float t1 = Mathf.Max(0.05f, PullOutFirstTime), t2 = Mathf.Max(0.05f, PullOutSlowTime);
        float t = 0f;
        GD.Print("[Jiji] 拔出！");
        while (t < t1 + t2 && IsInstanceValid(this) && _jijiPhase == 3)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            t += (float)GetProcessDeltaTime();
            _jijiInsertY = t < t1
                ? Mathf.Lerp(startY, stopY, 1f - Mathf.Pow(1f - t / t1, 2f))                  // 第一段：退到入口上方一點（ease-out）
                : Mathf.Lerp(stopY, 0f, Mathf.SmoothStep(0f, 1f, (t - t1) / t2));             // 第二段：慢慢滑到入口
            _insertFadeAlpha = 1f - Mathf.Clamp(t / Mathf.Max(PullOutFadeTime, 0.01f), 0f, 1f);
        }
        if (!IsInstanceValid(this)) return;
        // 洞保留：從目前張開的大小開始，之後依高潮值縮到目標
        if (Layers.TryGetValue("洞", out var hole) && hole != null) _holeLeftScale = hole.Scale.X;
        HideOpenAndInsertLayers(keepHole: true);
        _holeLeftOpen = true;
        _jijiPhase = 2; _jijiStage = 0; _jijiInsertY = 0f; _jijiOffset = _jijiPhase2Pos;
        _s2Paused = false; _pullOutAnim = false; _insertFadeAlpha = 1f;
        GD.Print("[Jiji] 拔出完成 → Phase 2（洞維持張開）");
        OnPulledOutWithCreampie();   // 有內射的話：洞口鼓出奶油泡芙、往下流（InsertEjac.cs）
    }

    void UpdateInsertion(float dt)
    {
        _jijiHeld = Input.IsMouseButtonPressed(MouseButton.Left);
        if (_jijiStage >= 1) { UpdateStage1Osc(dt); return; }   // 突破後：深插振盪（Unity 的做法）

        // 還沒突破（Stage 0）時放開左鍵 → 立刻往回退，退到底就回到入口（Phase 2）。
        // 原本照 Unity：放開後雞雞還會自己繼續往上推到頂，卡住後要等突破分數慢慢扣到 0 才退回，
        // 使用者覺得「取消之後要過一段時間才回彈」像延遲。改成放開就馬上回彈；再按住就繼續往上推。
        if (!_jijiHeld)
        {
            _resistBlocking = false; _resistProgress = 0f; _resistBodyPushY = 0f;
            _jijiMovingUp = false; _jijiSurgeVel = 0f;
            _jijiInsertY -= JijiRetractSpeed * dt;
            if (_jijiInsertY <= 0f)
            {
                _jijiInsertY = 0f;
                _jijiPhase = 2;
                _jijiOffset = _jijiPhase2Pos;
                HideOpenAndInsertLayers();
                GD.Print("[Jiji] 放開 → 退回 Phase 2");
            }
            return;
        }
        if (!_resistBlocking && !_jijiMovingUp) _jijiMovingUp = true;   // 退到一半又按住 → 繼續往上推

        float stageSpeed, stageMaxY, stageMinY;
        switch (_jijiStage)
        {
            case 0:  stageSpeed = JijiStage1Speed; stageMaxY = JijiStage1MaxY; stageMinY = JijiStage1MinY; break;
            case 1:  stageSpeed = JijiStage2Speed; stageMaxY = JijiStage2MaxY; stageMinY = JijiStage2MinY; break;
            default: stageSpeed = JijiStage3Speed; stageMaxY = JijiStage3MaxY; stageMinY = JijiStage3MinY; break;
        }

        // 滑鼠拖曳（只在按住左鍵時算，避免移去點選單時誤推）：螢幕往上＝推進
        Vector2 mouseNow = GetViewport().GetMousePosition();
        float mouseContrib = 0f;
        if (_jijiHeld && _charRoot != null)
        {
            Vector2 dLocal = _charRoot.GetGlobalTransform().AffineInverse().BasisXform(mouseNow - _jijiLastMouse);
            mouseContrib = -dLocal.Y * JijiMouseAccelMult;
        }
        _jijiLastMouse = mouseNow;

        float delta = (_jijiMovingUp ? stageSpeed * dt : 0f) + mouseContrib;

        // 突破後的衝刺
        if (!_resistBlocking && _jijiSurgeVel > 0f)
        {
            delta += _jijiSurgeVel * dt;
            _jijiSurgeVel = Mathf.Lerp(_jijiSurgeVel, 0f, dt * BreakthroughSurgeDamping);
            if (_jijiSurgeVel < 1f) _jijiSurgeVel = 0f;
        }

        if (_resistBlocking)
        {
            bool movingDown = delta < -0.5f;
            if (_jijiHeld && !movingDown)
            {
                float rate = ResistScoreRateHold + (delta > 0.5f ? ResistScoreRateUpBonus : 0f);
                rate *= 1f + WetnessRatio() * ResistWetnessScoreBonus;
                _resistProgress += rate * dt;
                // 緩慢撐開前進
                _jijiInsertY = Mathf.Min(_jijiInsertY + ResistJijiSlowSpeed * dt, stageMaxY + ResistJijiSlowRange);
                _resistBodyPushY = ResistProgressNorm * ResistBodyPushMaxY;

                if (_resistProgress >= _resistTarget) Breakthrough();
            }
            else
            {
                _resistProgress -= ResistScoreDecayRate * dt;
                _resistBodyPushY = ResistProgressNorm * ResistBodyPushMaxY;
                if (_resistProgress <= 0f)
                {
                    _resistProgress = 0f; _resistBlocking = false; _resistBodyPushY = 0f; _jijiSurgeVel = 0f;
                    if (FaceController != null && FaceController.CurrentExpression == "插入1" && !string.IsNullOrEmpty(_preResistExpr)) FaceController.SetExpression(_preResistExpr);   // 突破失敗：表情還原
                    if (_jijiStage == 0) { BackToPhase2("突破失敗（Stage 0）"); return; }
                    _jijiMovingUp = false;           // Stage 1+：停在原地，按住往上拖可再挑戰
                    _jijiInsertY = Mathf.Min(_jijiInsertY, stageMaxY);
                    GD.Print($"[Jiji] 突破分數歸 0（Stage {_jijiStage}）→ 停止上推");
                }
            }
            return;
        }

        if (delta == 0f) return;
        _jijiInsertY += delta;

        if (_jijiInsertY >= stageMaxY)
        {
            _jijiInsertY = stageMaxY;
            if (_jijiStage < 2)
            {
                float target = ResistBreakScoreTarget * (1f - Mathf.Clamp(WetnessRatio() * ResistWetnessWeight, 0f, 1f));
                _resistTarget = Mathf.Max(target, 1f);
                _resistProgress = 0f;
                _resistBlocking = true;
                _preResistExpr = FaceController?.CurrentExpression;
                FaceController?.SetExpression("插入1");   // 按住突破中：抖動嘴
                GD.Print($"[Jiji] Stage {_jijiStage} 到上限 → 阻力（目標 {_resistTarget:F0}，按住左鍵累積）");
            }
            else
            {
                _jijiMovingUp = false;
            }
        }
        if (_jijiInsertY < stageMinY) _jijiInsertY = stageMinY;
    }

    void Breakthrough()
    {
        _resistBlocking = false; _resistProgress = 0f; _resistBodyPushY = 0f;
        if (_jijiStage >= 2) { _jijiMovingUp = false; return; }
        SayMaleEv(MaleBreakLines, true);
        _jijiStage++;
        _jijiMovingUp = false;
        _jijiSurgeVel = 0f;
        // 進入深插振盪：速度等級歸 0，從 Stage 2 下限開始（對照 Unity 突破 → Stage 1 初始化）
        _s2SwayT = 0f; _s2CenterCur = float.NaN; _s2SpeedLv = 0f; _s2Sin = 0f; _s2TummyOn = false;
        _breakSurgeT = 0f;   // 突破衝刺開始
        _jijiLastMouse = GetViewport().GetMousePosition();
        GD.Print($"[Jiji] 突破！→ Stage {_jijiStage}");
        if (!_jijiAnus) OnBreakthroughBleed();   // 第一次（處女）→ 破處流血（InsertEjac.cs）；肛門不套用
        // 玩法串接（Play.cs）：濕度不夠就硬突破 → 穴高潮值下降（越乾扣越多）
        OnBreakthroughWetCheck();
        FaceController?.SetExpression("插入2");   // 完全突破：小嘴巴
    }

    string _preResistExpr;   // 開始按住突破之前的表情（突破失敗時還原）

    float WetnessRatio()
    {
        var pdm = PlayerDataManager.Instance;
        return pdm != null && pdm.MaxVaginaWetness > 0f ? pdm.CurrentVaginaWetness / pdm.MaxVaginaWetness : 0f;
    }

    /// <summary>阻力中全身往上推擠＋發抖（對照 Unity ApplyResistBodyPush），結果透過 BodyShake 套到所有圖層。</summary>
    void UpdateResistBodyPush(float dt)
    {
        _resistBodyPushCur = Mathf.Lerp(_resistBodyPushCur, _resistBodyPushY, dt * 6f);
        if (Mathf.Abs(_resistBodyPushY) < 0.01f && Mathf.Abs(_resistBodyPushCur) < 0.3f) _resistBodyPushCur = 0f;
        float ratio = ResistBodyPushMaxY > 0f ? Mathf.Abs(_resistBodyPushCur) / ResistBodyPushMaxY : 0f;
        float tremble = (_resistBlocking || _pullActive)
            ? Mathf.Sin((float)Time.GetTicksMsec() / 1000f * ResistBodyTrembleFreq * Mathf.Pi * 2f) * ResistBodyTrembleAmp * ratio
            : 0f;
        _resistBodyPushTotal = _resistBodyPushCur + tremble;
    }

    /// <summary>雞雞碰到開1 之後又往裡推了多少（畫布 px）。碰到＝推到 Stage 0 開1 開始出現的位置；
    /// 突破後以雞雞目前深度計算（跟著抽插上下）。沒在插入時為 0。</summary>
    float JijiTouchDepth()
    {
        if (_jijiPhase != 3) return 0f;
        float touchY = JijiStage1MinY + JijiOpenLayerStartT * (JijiStage1MaxY - JijiStage1MinY);
        return Mathf.Max(0f, _jijiInsertY - touchY);
    }

    // ── F1「查看裁切範圍」────────────────────────────────────────────────
    /// <summary>開啟時：雞雞被裁掉的部分改成半透明紅色顯示，並畫出裁切線（黃）和兩個裁切圓（青）。</summary>
    public bool ClipDebug { get; set; }
    Control _clipDebugOverlay;
    float _dbgClipPx, _dbgOffPx, _dbgRPx, _dbgDipPx;

    void UpdateClipDebugOverlay(Control jiji, float clipPx, float s2c)
    {
        if (_clipDebugOverlay == null || !IsInstanceValid(_clipDebugOverlay))
        {
            // 掛在雞雞圖層底下：座標直接用貼圖像素，雞雞移動/旋轉時線也會跟著
            _clipDebugOverlay = new Control { Name = "ClipDebugOverlay", MouseFilter = Control.MouseFilterEnum.Ignore };
            _clipDebugOverlay.Size = jiji.Size;
            _clipDebugOverlay.Draw += DrawClipDebug;
            jiji.AddChild(_clipDebugOverlay);
        }
        _dbgClipPx = clipPx;
        _dbgOffPx = ClipCircleOffsetEff * s2c;
        _dbgRPx = JijiClipCircleRadius * s2c;
        _dbgDipPx = JijiClipCircleDip * s2c;
        _clipDebugOverlay.Visible = jiji.Visible;
        _clipDebugOverlay.QueueRedraw();
    }

    void DrawClipDebug()
    {
        var o = _clipDebugOverlay;
        if (o == null) return;
        float w = 4f;   // 線寬（貼圖像素，畫面上約 1px）
        // 中間的水平裁切線
        o.DrawLine(new Vector2(820f, _dbgClipPx), new Vector2(1180f, _dbgClipPx), new Color(1f, 0.9f, 0.1f, 0.95f), w);
        // 兩個裁切圓
        if (JijiClipCircleRadius > 0f)
        {
            float cy = _dbgClipPx + _dbgDipPx - _dbgRPx - JijiTouchDepth() * JijiClipCircleFollow;
            var cc = new Color(0.1f, 0.95f, 1f, 0.95f);
            o.DrawArc(new Vector2(998f - _dbgOffPx, cy), _dbgRPx, 0f, Mathf.Tau, 64, cc, w);
            o.DrawArc(new Vector2(998f + _dbgOffPx, cy), _dbgRPx, 0f, Mathf.Tau, 64, cc, w);
        }
        // 雞雞中線
        o.DrawLine(new Vector2(998f, _dbgClipPx - 120f), new Vector2(998f, _dbgClipPx + 120f), new Color(1f, 1f, 1f, 0.6f), 2f);
    }

    // ── M 腿插入推擠（對照 Unity ApplyResistMLegPush + Stage 1 振盪的 m腿跟隨）──────
    // 雞雞往上頂的時候，M 腿整組（腿、腳、足、腿底、小穴、肛門，以及黏在小穴上的洞/開1）被往上推，
    // 兩條腿同時以各自的大腿根部為軸往外張（右腿順時針、左腿逆時針，腳/足跟著同一條腿用同一個軸心轉，不會脫節）。
    //   • 突破前卡住時：跟突破分數成正比（按越久推越多）
    //   • 突破後抽插：跟每一下往上頂的幅度同步
    [Export] public float MLegThrustFollow   = 0.4f;   // 突破後：M 腿被往上推的量 ＝ 雞雞頂的幅度 × 這個比例（Unity S2MLegFollowMult）
    [Export] public float MLegThrustResistPx = 20f;    // 突破前卡住時：M 腿最多被往上推多少（畫布 px，Unity ResistMLegPushMaxY）
    [Export] public float MLegThrustRotDeg   = 3f;     // 兩條腿往外張的最大角度（度，Unity S2MLegRotDeg）

    public readonly System.Collections.Generic.Dictionary<string, Vector2> MLegThrustOffset = new();
    public readonly System.Collections.Generic.Dictionary<string, float>   MLegThrustRot    = new();
    float _mlegThrustPush, _mlegThrustRot;

    static readonly string[] _mlegRightSide = { "m腿/腿1", "m腿/腳1", "m腿/足1" };   // 畫面右腿
    static readonly string[] _mlegLeftSide  = { "m腿/腿2", "m腿/腳2", "m腿/足2" };   // 畫面左腿
    static readonly string[] _mlegPushOnly  = { "m腿/腿底", "m腿/小穴", "m腿/小穴細節", "m腿/肛門", "洞", "開1", "開上" };

    /// <summary>腿往外張的旋轉軸心（大腿根部附近，畫布像素）。同一條腿的腿/腳/足共用一個軸心，轉起來才不會分開。</summary>
    static bool MLegThrustPivot(string layer, out Vector2 pivot)
    {
        if (System.Array.IndexOf(_mlegRightSide, layer) >= 0) { pivot = new Vector2(1060f, 1500f); return true; }
        if (System.Array.IndexOf(_mlegLeftSide,  layer) >= 0) { pivot = new Vector2(940f,  1500f); return true; }
        pivot = Vector2.Zero; return false;
    }

    void UpdateMLegThrust(float dt)
    {
        float targetPush = 0f, targetRot = 0f;
        if (_mLegOpen && _jijiPhase == 3)
        {
            if (_jijiStage >= 1)
            {
                targetPush = _s2Sin * _s2Amp * MLegThrustFollow;
                targetRot  = _s2Sin * MLegThrustRotDeg;
            }
            else if (_resistBlocking)
            {
                targetPush = ResistProgressNorm * MLegThrustResistPx;
                targetRot  = ResistProgressNorm * MLegThrustRotDeg;
            }
        }
        // 突破後跟著抽插要即時；其他情況平滑過渡（放開時慢慢回原位）
        float k = (_jijiPhase == 3 && _jijiStage >= 1) ? 1f : 1f - Mathf.Exp(-8f * dt);
        _mlegThrustPush = Mathf.Lerp(_mlegThrustPush, targetPush, k);
        _mlegThrustRot  = Mathf.Lerp(_mlegThrustRot,  targetRot,  k);
        if (Mathf.Abs(_mlegThrustPush) < 0.05f && Mathf.Abs(targetPush) < 0.05f) _mlegThrustPush = 0f;
        if (Mathf.Abs(_mlegThrustRot)  < 0.01f && Mathf.Abs(targetRot)  < 0.01f) _mlegThrustRot  = 0f;

        Vector2 up = new Vector2(0f, -_mlegThrustPush);   // Godot Y 朝下，往上＝負
        foreach (var n in _mlegRightSide) { MLegThrustOffset[n] = up; MLegThrustRot[n] =  _mlegThrustRot; }   // 右腿順時針往外張
        foreach (var n in _mlegLeftSide)  { MLegThrustOffset[n] = up; MLegThrustRot[n] = -_mlegThrustRot; }   // 左腿逆時針往外張
        foreach (var n in _mlegPushOnly)  { MLegThrustOffset[n] = up; }
    }

    // ── 工具 ────────────────────────────────────────────────────────────

    /// <summary>角色畫布 1px 在螢幕上是幾 px（_charRoot 的整體縮放，約 0.315）。</summary>
    float CharScreenScale()
    {
        if (_charRoot == null) return 1f;
        float sc = _charRoot.GetGlobalTransform().Scale.Y;
        return sc > 0.001f ? sc : 1f;
    }

    /// <summary>雞雞.png 實際畫出內容的外框（2000x3200 畫布像素，PIL 量 alpha bbox）。</summary>
    /// <summary>雞雞底部中央（雞雞.png 內容下緣中點，畫布像素）＝旋轉錨點。</summary>
    static readonly Vector2 _jijiBottomLocal = new Vector2(984.5f, 2130f);

    static readonly Rect2 _jijiContentRect = new Rect2(850f, 1571f, 1119f - 850f, 2130f - 1571f);

    /// <summary>滑鼠（螢幕座標）是否點在雞雞本體上（含目前的位移）。</summary>
    bool IsMouseOverJiji(Vector2 screenPos)
    {
        if (!Layers.TryGetValue("雞雞", out var j) || j == null || !j.Visible) return false;
        Vector2 local = j.GetGlobalTransform().AffineInverse() * screenPos;
        return _jijiContentRect.HasPoint(local);
    }

    void SetLayerQuiet(string name, bool v)
    {
        if (Layers.TryGetValue(name, out var c) && c != null) c.Visible = v;
    }

    /// <summary>設定插入疊層的額外位移，並「立刻」把位移差套到目前位置上。
    /// 原因：位移是交給呼吸迴圈在下一幀才加進 Position，疊層剛出現的第一幀會先畫在舊位置
    /// （例如突破時「插入」先出現在原圖的上面位置，下一幀才瞬移到正確位置）。
    /// 這裡直接補上差值，第一幀就在正確位置；下一幀呼吸迴圈算出來的結果也一樣，不會跳。</summary>
    void SetOverlayOffset(string name, Control c, Vector2 off)
    {
        Vector2 old = JijiLayerExtraOffset.TryGetValue(name, out var o) ? o : Vector2.Zero;
        if (c != null && old != off) c.Position += off - old;
        JijiLayerExtraOffset[name] = off;
    }

    ShaderMaterial _open1FadeMat;

    /// <summary>開1（xiaoxuekai）底部淡出：fade=1 時內容底部漸層透明，fade=0 完全不淡出。
    /// 由 UpdateOpenLayers 依張開程度設定（剛張開 fade 大、張越開 fade 越小）。</summary>
    ShaderMaterial EnsureOpen1Fade(Control c)
    {
        if (_open1FadeMat != null) return _open1FadeMat;
        var sh = new Shader
        {
            Code = @"shader_type canvas_item;
uniform float erode = 0.0;    // 0＝完整，1＝只剩上面 keep 比例
uniform float keep = 0.5;
uniform float band_px = 40.0;
void fragment() {
    vec4 c = COLOR;
    // xiaoxuekai 內容 y 1324~1599（3200 高）。淡出邊界從底部 1599 往上移，
    // erode=1 時移到 1324 + 275×keep（剩上半部）；邊界以下透明、邊界附近柔和過渡。
    float py = UV.y / TEXTURE_PIXEL_SIZE.y;
    float edge = mix(1599.0 + band_px, 1324.0 + 275.0 * keep, erode);
    c.a *= 1.0 - smoothstep(edge - band_px, edge + band_px, py);
    COLOR = c;
}"
        };
        _open1FadeMat = new ShaderMaterial { Shader = sh };
        c.Material = _open1FadeMat;
        return _open1FadeMat;
    }

    [Export] public float InsertEdgeBlurPx = 4f;   // 「插入」底部左右兩邊的模糊半徑（貼圖像素），一點點就好
    ShaderMaterial _insertEdgeMat;

    /// <summary>「插入」層（xiaoxuekai2）底部左右兩邊做輕微模糊：只在內容的底部區域、
    /// 越靠近左右兩側越模糊，中間和上半部完全不受影響。</summary>
    void EnsureInsertEdgeBlur(Control ins)
    {
        if (_insertEdgeMat != null) { _insertEdgeMat.SetShaderParameter("radius", InsertEdgeBlurPx); return; }
        var sh = new Shader
        {
            Code = @"shader_type canvas_item;
uniform float radius = 4.0;
uniform float wid_l = 0.428;   // 插入內容左緣 U（向右擴張的基準）
uniform float wid_s = 0.0;     // 向右擴張比例
uniform float wid_b = 0.0;     // 只有底部額外的向右擴張比例：讓插入物件的右下角伸到雞雞的右緣（貼齊）
uniform float wid_r = 0.545;   // 插入內容右緣 U（左下角內縮的基準）
uniform float wid_lb = 0.0;    // 只有底部的左邊往內（往右）縮的比例：讓插入物件的左下角縮進來貼齊雞雞的左緣
varying vec4 vcol;
void vertex() { vcol = COLOR; }
void fragment() {
    vec2 texel = TEXTURE_PIXEL_SIZE;
    // xiaoxuekai2 內容範圍：x 856~1095、y 1166~1432（2000x3200 畫布）
    float cx    = 975.5 * texel.x;
    float halfw = 119.5 * texel.x;
    float side   = smoothstep(0.45, 1.0, abs(UV.x - cx) / halfw);          // 越靠左右兩側越強
    float bottom = smoothstep(1320.0 * texel.y, 1400.0 * texel.y, UV.y);   // 只有底部
    float w = side * bottom;
    float wb_ = smoothstep(1330.0 * texel.y, 1430.0 * texel.y, UV.y);   // 越靠底部越多
    float wid_ = wid_s + wid_b * wb_;
    float ux_ = UV.x < wid_r ? wid_r - (wid_r - UV.x) / (1.0 - min(wid_lb * wb_, 0.9)) : UV.x;   // 左邊往內縮（只有底部，越靠底越多）：內容往右壓，左下角離開左邊、靠向雞雞
    vec2 UVw = vec2(ux_ > wid_l ? wid_l + (ux_ - wid_l) / (1.0 + wid_) : ux_, UV.y);   // 向右擴張（底部額外多一些）
    vec4 c = texture(TEXTURE, UVw);
    if (w > 0.001) {
        vec4 acc = vec4(0.0);
        for (int i = -2; i <= 2; i++)
            for (int j = -2; j <= 2; j++)
                acc += texture(TEXTURE, UVw + vec2(float(i), float(j)) * texel * radius * 0.5);
        c = mix(c, acc / 25.0, w);
    }
    c *= vcol;
    COLOR = c;
}"
        };
        _insertEdgeMat = new ShaderMaterial { Shader = sh };
        _insertEdgeMat.SetShaderParameter("radius", InsertEdgeBlurPx);
        ins.Material = _insertEdgeMat;
        UpdateInsertMask();   // 設定插入向右擴張
    }

    /// <summary>雞雞圖層掛上裁切 shader：UV.y 小於 clip_v（入口線以上）的像素不畫。</summary>
    void EnsureJijiClip()
    {
        if (_jijiClipMat != null) return;
        if (!Layers.TryGetValue("雞雞", out var j) || j == null) return;
        var sh = new Shader
        {
            Code = @"shader_type canvas_item;
uniform float clip_v = 0.0;
uniform float arch_v = 0.0;      // 傘形：兩側比中間低多少（UV）
uniform float feather_v = 0.0005;
// 雙圓形裁切（貼圖像素座標）
uniform float clip_px = 0.0;
uniform float circle_on = 0.0;
uniform float circle_off_px = 110.0;
uniform float circle_r_px = 70.0;
uniform float circle_dip_px = 45.0;
uniform float feather_px = 3.0;
uniform float taper_k = 0.0;           // 肛門下方雞雞變細：最大比例
uniform float taper_start_px = 0.0;    // 變細起點（貼圖 px，雞雞本地 y）
uniform float taper_len_px = 40.0;     // 變細漸層長度（貼圖 px）
uniform float taper_arch_px = 0.0;     // 變細起點弧形：兩側往下多少（貼圖 px）
varying vec4 vcol;
void vertex() { vcol = COLOR; }
uniform float debug_on = 0.0;
uniform float circle_rise_px = 0.0;   // 雞雞碰到開1 之後，裁切圓跟著往上移的量（貼圖 px）
uniform float blood_amt = 0.0;        // 第一次（破處）時雞雞上沾的血（0~1）
uniform float junction_px = 0.0;     // 平滑後的入口線（雞雞本地像素），紅暈／血的定位用
uniform float flush_amt = 0.0;        // 0~1：插入時入口的淡淡紅暈（不論是不是處），平滑漸入漸出
uniform float gw_amt = 0.0;            // 射精後龜頭白色暈染的濃度 0~1
uniform float gw_len = 130.0;          // 白色暈染從龜頭往下的長度（貼圖 px）
uniform float gw_from = 1585.0;        // 暈染起點（貼圖 px；外射＝龜頭尖端、內射＝插入銜接處）
uniform float sem_on = 0.0;            // 1＝這是「精液」圖層用的材質（雞雞本體為 0）
uniform float sem_reveal = 1.0;        // 精液從尖端到根部慢慢出現的進度 0~1
uniform float sem_len = 520.0;         // 出現範圍的總長度（貼圖 px）
uniform float sem_pool = 0.0;          // 雞雞和插入銜接處聚集的白色濃度 0~1
uniform float sem_spread = 0.0;        // 白色暈開的程度 0~1
uniform float sem_flow = 0.0;          // 白色往下流開的進度 0~1
uniform float blood_blur = 0.0;       // 0~1：抽插久了血越來越模糊、變淡
uniform float blood_seed = 0.0;       // 每次破處隨機，血痕分布每次都不一樣
float bhash(float n) { return fract(sin(n * 12.9898 + blood_seed * 78.233) * 43758.5453); }
float bnoise(vec2 p) {
    vec2 i = floor(p); vec2 f = fract(p); f = f * f * (3.0 - 2.0 * f);
    float a = bhash(i.x + i.y * 57.0), b = bhash(i.x + 1.0 + i.y * 57.0);
    float c = bhash(i.x + (i.y + 1.0) * 57.0), d = bhash(i.x + 1.0 + (i.y + 1.0) * 57.0);
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}
// 雞雞.png 內容左右範圍 x 850~1119（2000 寬）→ 中心 984.5、半寬 134.5；尖端附近中線 x≈998
void fragment() {
    vec4 c = COLOR;   // Godot 4：COLOR 已經是「貼圖顏色 × modulate」，不能再乘一次 texture（會變紅變暗）
    if (taper_k > 0.0005) {   // 肛門突破後：靠近肛門的地方雞雞往中間收（越上面越細，往下漸層消失）
        float tdx_ = clamp(abs(UV.x - 998.0 / 2000.0) / (134.5 / 2000.0), 0.0, 1.0);
        float ddp_ = UV.y / TEXTURE_PIXEL_SIZE.y - taper_start_px - taper_arch_px * tdx_ * tdx_;   // 弧形：兩側晚一點才開始
        float tu_ = clamp((ddp_ + 5.0) / (max(taper_len_px, 1.0) + 5.0), 0.0, 1.0);   // 起點上方 5px 先緩緩收，不要突然歪進去
        float tk_ = taper_k * (1.0 - tu_ * tu_ * (3.0 - 2.0 * tu_));
        float cxu_ = 998.0 / 2000.0;
        c = texture(TEXTURE, vec2(cxu_ + (UV.x - cxu_) / (1.0 - tk_), UV.y)) * vcol;
    }
    float dx = clamp(abs(UV.x - 984.5 / 2000.0) / (134.5 / 2000.0), 0.0, 1.0);
    float cut_y = clip_v + arch_v * dx * dx;            // 水平（或拋物線傘形）裁切線
    float keep = smoothstep(cut_y - feather_v, cut_y + feather_v, UV.y);
    if (circle_on > 0.5) {
        vec2 p = UV / TEXTURE_PIXEL_SIZE;                                  // 貼圖像素座標
        float cy = clip_px + circle_dip_px - circle_r_px - circle_rise_px;  // 圓心高度：最低點＝裁切線＋dip，再往上跟著雞雞
        float dl = distance(p, vec2(998.0 - circle_off_px, cy));
        float dr = distance(p, vec2(998.0 + circle_off_px, cy));
        keep *= smoothstep(circle_r_px - feather_px, circle_r_px + feather_px, min(dl, dr));   // 圓內裁掉
    }
    if (blood_amt > 0.001) {
        // 破處的血（每條血痕隨機位置、粗細、長度、濃淡、彎曲；尾巴分叉、越往尾端越模糊淡掉）
        vec2 bp = UV / TEXTURE_PIXEL_SIZE;
        float along = clamp((bp.y - 1585.0) / 340.0, 0.0, 1.0);              // 0＝尖端，1＝往下 340px
        float m = 0.0;
        for (int i = 0; i < 11; i++) {
            float fi = float(i);
            float h1 = bhash(fi * 3.1 + 1.0), h2 = bhash(fi * 7.7 + 2.0);
            float h3 = bhash(fi * 11.3 + 3.0), h4 = bhash(fi * 5.3 + 4.0), h5 = bhash(fi * 2.3 + 5.0);
            float len = 0.2 + h2 * 0.8;                                         // 長度
            if (along > len + 0.2) continue;
            float x0  = 880.0 + h1 * 220.0;                                     // 起點左右位置
            float cx  = x0 + (6.0 + h5 * 8.0) * sin(bp.y * (0.02 + h3 * 0.03) + h1 * 6.28)
                           + 4.0 * sin(bp.y * 0.011 + h2 * 6.28);               // 彎曲
            float w   = 2.5 + h3 * 7.0;                                         // 粗細
            float fork = smoothstep(len * 0.5, len, along);                     // 尾段開始分叉
            float spread = fork * (6.0 + h4 * 12.0);
            float blur = 1.0 + fork * 4.0;                                      // 越往尾端邊緣越模糊
            float wT = w * (1.0 - 0.55 * fork);
            float dm = abs(bp.x - cx);
            float da = abs(bp.x - (cx - spread));
            float db = abs(bp.x - (cx + spread * (0.5 + h5 * 0.8)));
            float mainS = 1.0 - smoothstep(wT * 0.5, wT * 0.5 + blur * 2.0, dm);
            float brS   = max(1.0 - smoothstep(wT * 0.35, wT * 0.35 + blur * 3.0, da),
                              1.0 - smoothstep(wT * 0.35, wT * 0.35 + blur * 3.0, db));
            float sS = mix(mainS, max(mainS * (1.0 - fork), brS), fork);
            float fade = 1.0 - smoothstep(len - 0.12, len + 0.15, along);       // 尾端柔和淡出
            m = max(m, sS * fade * (0.45 + h4 * 0.55));
        }
        float tint = (1.0 - smoothstep(0.15, 0.85, along)) * (0.5 + 0.5 * bnoise(bp * 0.05));   // 尖端附近不均勻泛紅
        float blot = smoothstep(0.6, 0.82, bnoise(bp * 0.035 + 7.0)) * (1.0 - along);              // 隨機血斑
        // 雞雞與入口（裁切線）銜接處：血從這裡冒出來，往下漸淡
        float ypx = UV.y / TEXTURE_PIXEL_SIZE.y;
        float dcut = ypx - junction_px - arch_v * dx * dx / TEXTURE_PIXEL_SIZE.y;
        float mist = 0.55 + 0.45 * bnoise(vec2(bp.x, dcut) * 0.045);      // 霧化噪點：固定在入口位置、不隨時間或抽插閃動
        float band = (1.0 - smoothstep(0.0, 95.0, dcut)) * smoothstep(-20.0, -2.0, dcut);       // 銜接處冒血：邊緣模糊、往下漸淡
        band *= (1.0 - smoothstep(60.0, 125.0, abs(bp.x - 998.0))) * mist;          // 左右也柔化
        float cutpx = cut_y / TEXTURE_PIXEL_SIZE.y;
        float m2 = band * min(1.0, blood_amt * 2.0 + 0.3) * flush_amt;
        // 一圈圍繞插入的弧形血池：像滿水冒泡、上濃下淡；雞雞動久了（blood_blur）越來越模糊
        float dxn = clamp(abs(bp.x - 998.0) / 130.0, 0.0, 1.0);
        float dArc = dcut + 30.0 * dxn * dxn;
        float poolLen = 75.0 + 70.0 * blood_blur;
        float pool = (1.0 - smoothstep(0.0, poolLen, dArc)) * smoothstep(-22.0, -2.0, dArc);
        pool *= (1.0 - smoothstep(60.0, 140.0, abs(bp.x - 998.0)));
        pool *= 0.55 + 0.45 * bnoise(vec2(bp.x, dcut) * (0.05 - 0.03 * blood_blur) + 5.0);
        float poolK = min(1.0, blood_amt * 2.0 + 0.3) * (1.0 - 0.7 * blood_blur) * flush_amt;
        float bub = 0.0;
        if (dArc > -4.0 && dArc < 80.0) {
            vec2 cell = floor(bp / 26.0);
            float c1 = bhash(cell.x * 13.1 + cell.y * 71.7), c2 = bhash(cell.x * 5.3 + cell.y * 29.9);
            float life = fract(TIME * (0.10 + c2 * 0.12) + c1);
            vec2 ctr = (cell + vec2(0.25 + 0.5 * c1, 0.25 + 0.5 * c2)) * 26.0;
            float r = (3.0 + 6.0 * c2) * life;
            float dd = distance(bp, ctr);
            float ring = 1.0 - smoothstep(0.0, 1.8 + 2.5 * blood_blur, abs(dd - r));
            float fill = (1.0 - smoothstep(r - 1.5, r + 1.5, dd)) * 0.35;
            bub = (ring * 0.8 + fill) * (1.0 - life) * step(0.35, c1) * (1.0 - smoothstep(0.0, 80.0, dArc));
        }
        m2 = max(m2, max(pool * 0.85, bub * 0.45) * poolK);
        m2 *= 1.0 - 0.6 * blood_blur;
        // 剛插入時入口多一些明顯的紅色血量：形狀不規則、邊緣模糊，抽插久了（blood_blur）才變弱
        float pn = bnoise(vec2(bp.x, dcut) * 0.02 + 3.0) * 0.6 + bnoise(vec2(bp.x, dcut) * 0.06 + 9.0) * 0.4;
        float patch = (1.0 - smoothstep(0.0, 130.0, dcut)) * smoothstep(-20.0, -2.0, dcut);
        patch *= 1.0 - smoothstep(55.0, 150.0, abs(bp.x - 998.0));
        patch *= smoothstep(0.25, 0.85, pn);
        float fresh = clamp(1.0 - blood_blur * 1.4, 0.2, 1.0);
        m2 = max(m2, patch * 0.55 * fresh * flush_amt);
        m = clamp(max(m, blot * 0.75) + tint * 0.35, 0.0, 1.0) * blood_amt * (1.0 - 0.7 * blood_blur);   // 原本雞雞上的血慢慢淡掉
        m = max(m, m2);
        c.rgb = mix(c.rgb, vec3(0.80, 0.04, 0.07), clamp(m * 1.35, 0.0, 1.0));   // 血：不透明度加強
    }
    {
        // 插入時入口的淡淡紅暈（不論是不是處、持續維持、漸入漸出）
        vec2 hp = UV / TEXTURE_PIXEL_SIZE;
        float hd = hp.y - junction_px - arch_v * dx * dx / TEXTURE_PIXEL_SIZE.y;
        float halo = (1.0 - smoothstep(0.0, 110.0, hd)) * smoothstep(-20.0, -2.0, hd);
        halo *= 1.0 - smoothstep(50.0, 140.0, abs(hp.x - 998.0));
        halo *= 0.6 + 0.4 * bnoise(vec2(hp.x, hd) * 0.03);
        c.rgb = mix(c.rgb, vec3(0.95, 0.25, 0.30), halo * 0.22 * flush_amt);
    }
    {
        // 入口的接觸陰影：雞雞從入口往下一小段稍微變暗、漸層消失（像是從裡面冒出來的，不是貼上去的另一張圖）
        vec2 sp_ = UV / TEXTURE_PIXEL_SIZE;
        float sd_ = sp_.y - junction_px - arch_v * dx * dx / TEXTURE_PIXEL_SIZE.y;
        float sh_ = (1.0 - smoothstep(0.0, 80.0, sd_)) * smoothstep(-6.0, 6.0, sd_);
        sh_ *= 1.0 - smoothstep(70.0, 150.0, abs(sp_.x - 998.0));
        c.rgb *= 1.0 - 0.16 * sh_ * flush_amt;
    }
    if (gw_amt > 0.001 && sem_on < 0.5) {   // 射精後龜頭一帶白色暈染（只染在雞雞本體像素上，不改透明度）
        vec2 gp_ = UV / TEXTURE_PIXEL_SIZE;
        float gd_ = gp_.y - gw_from - arch_v * dx * dx / TEXTURE_PIXEL_SIZE.y;
        float gn_ = 0.75 + 0.25 * bnoise(vec2(gp_.x, gd_) * 0.04 + 21.0);
        float gm_ = (1.0 - smoothstep(0.0, gw_len, gd_)) * smoothstep(-10.0, 8.0, gd_) * gn_;
        gm_ *= 1.0 - smoothstep(70.0, 150.0, abs(gp_.x - 998.0)) * 0.6;
        c.rgb = mix(c.rgb, vec3(0.99, 0.98, 0.95) * vcol.rgb, clamp(gm_ * gw_amt, 0.0, 0.92));   // 白色也要乘上環境色（夜晚濾鏡），不然會比周圍亮
    }
    if (sem_on > 0.5) {   // 內射後雞雞上的精液圖層：從尖端到根部慢慢出現（只改這張圖自己的透明度，不會超出圖的範圍）
        vec2 sq_ = UV / TEXTURE_PIXEL_SIZE;
        float dj_ = sq_.y - junction_px - arch_v * dx * dx / TEXTURE_PIXEL_SIZE.y;   // 銜接線往下的距離
        float rvEnd_ = sem_reveal * (sem_len + 60.0);
        float rv_ = 1.0 - smoothstep(rvEnd_ - 60.0, rvEnd_, dj_ + 30.0);
        c.a *= mix(rv_, 1.0, step(0.999, sem_reveal));
    } else if (sem_pool > 0.001) {   // 銜接處聚集的白色：只染在雞雞本體已有的像素上（不改透明度），所以不會跑到雞雞外面
        vec2 sq_ = UV / TEXTURE_PIXEL_SIZE;
        float dj_ = sq_.y - junction_px - arch_v * dx * dx / TEXTURE_PIXEL_SIZE.y;
        float xw_ = abs(sq_.x - 998.0);
        float wide_ = 1.0 - smoothstep(40.0 + 80.0 * sem_spread, 95.0 + 130.0 * sem_spread, xw_);
        float yc_ = 10.0 + sem_flow * 110.0;
        float sig_ = 16.0 + 70.0 * sem_spread;
        float band_ = exp(-((dj_ - yc_) * (dj_ - yc_)) / (sig_ * sig_)) * smoothstep(-4.0, 3.0, dj_);
        float nz_ = 0.6 + 0.4 * bnoise(vec2(sq_.x, dj_) * (0.05 - 0.03 * sem_spread) + 11.0);
        float pm_ = clamp(band_ * wide_ * nz_ * sem_pool, 0.0, 0.9);
        c.rgb = mix(c.rgb, vec3(0.98, 0.97, 0.93), pm_);
    }
    if (debug_on > 0.5) {
        // F1「查看裁切範圍」：被裁掉的部分不隱藏，改成半透明紅色顯示
        c.rgb = mix(vec3(1.0, 0.15, 0.15), c.rgb, keep);
        c.a = mix(c.a * 0.55, c.a, keep);
    } else {
        c.a *= keep;
    }
    COLOR = c;
}"
        };
        _jijiClipMat = new ShaderMaterial { Shader = sh };
        _jijiClipMat.SetShaderParameter("clip_v", 0f);
        j.Material = _jijiClipMat;
        // 精液跟雞雞同一張畫布、同一個位移和旋轉 → 共用同一個裁切材質，一起被裁切
        if (Layers.TryGetValue("精液", out var sm) && sm != null)
        {
            _jijiSemMat = new ShaderMaterial { Shader = sh };   // 精液自己一份材質（同一支 shader、每幀複製裁切參數），才能單獨做出現／聚集／暈開
            _jijiSemMat.SetShaderParameter("sem_on", 1f);
            sm.Material = _jijiSemMat;
        }
    }
}
