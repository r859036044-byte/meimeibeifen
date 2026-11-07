using Godot;
using System;
using System.Threading.Tasks;

/// <summary>
/// 「什麼都不做 2 天」小遊戲（Unity LivingRoomRuntime.MiniGameNight3 的 Godot 移植版）
/// 獨立 CanvasLayer：MiniGameNight3.Play(owner) → 回傳結果（Won / Orgasms）。
/// 劇情銜接：Play 結束後依 Result 自行接（主選單入口在 MainMenuRuntime.MiniGame.cs 的 StartMiniGameFromMenu）。
/// 台詞/文字全部在下面 MgText 區塊，自己改。
/// </summary>
public partial class MiniGameNight3 : CanvasLayer
{
    // ══════════════════════════════════════════════════════════
    //  ★ 文字區（全部是佔位，自行編輯）
    // ══════════════════════════════════════════════════════════
    public static class MgText
    {
        public static string Title      = "小遊戲標題";
        public static string IntroBody  = "（開場文字：自行編輯 MgText.IntroBody）";
        public static string IntroBtn   = "開始";
        public static string Speaker    = "角色名";
        public static string WinText    = "（成功文字：自行編輯 MgText.WinText）";
        public static string FailText   = "（失敗文字：自行編輯 MgText.FailText）";
        // 反應台詞：每組可放多句，隨機抽。分類見 GetReaction。
        public static string[] Orgasm        = { "（反應：高潮 1）", "（反應：高潮 2）", "（反應：高潮 3）" };
        public static string[] StoppedAt100  = { "（反應：滿值沒對準 1）", "（反應：滿值沒對準 2）" };
        public static string[] GHitHard      = { "（反應：G點 用力 1）", "（反應：G點 用力 2）" };
        public static string[] UterusHard    = { "（反應：子宮口 用力 1）", "（反應：子宮口 用力 2）" };
        public static string[] UterusAny     = { "（反應：子宮口 1）", "（反應：子宮口 2）" };
        public static string[] EarlyGentle   = { "（反應：前期 小力 1）", "（反應：前期 小力 2）" };
        public static string[] LateGentle    = { "（反應：後期 小力 1）", "（反應：後期 小力 2）" };
        public static string[] FarHard       = { "（反應：中段 用力 1）", "（反應：中段 用力 2）" };
        public static string[] NearGentle    = { "（反應：G點附近 小力 1）", "（反應：G點附近 小力 2）" };
        public static string EarlyHard   = "（反應：前期 用力）";
        public static string EarlyMid    = "（反應：前期 小幅）";
        public static string MidMid      = "（反應：過渡 小幅）";
        public static string MidGentle   = "（反應：過渡 小力）";
        public static string MidHard     = "（反應：過渡 用力）";
        public static string LateHard    = "（反應：後期 用力）";
        public static string LateMid     = "（反應：後期 小幅）";
        public static string Default     = "（反應：預設）";
    }

    // ══════════════════════════════════════════════════════════
    //  結果
    // ══════════════════════════════════════════════════════════
    public struct MgResult { public bool Won; public int Orgasms; public int Rounds; }

    readonly TaskCompletionSource<MgResult> _done = new();
    readonly Random _rng = new();

    // 設定（若 res://Resources/MG/MiniGameNight3Config.tres 存在就用它，否則用 Unity 內的數值）
    MiniGameNight3Config _cfg;
    Control _root;
    TextureRect _imgBottom, _imgBottom2, _imgFrame;
    ShaderMaterial _bottom2Mat, _bottomMat;
    float _tipUvY, _jRotDeg, _expK, _upFollow; Vector2 _jiDisp;
    Control _panel, _marker, _fill, _track;
    Label _round, _pct, _org, _posLbl, _gain, _hint, _spk, _react, _cont;
    Button[] _btns = new Button[3];
    ColorRect _flash;

    // 輸入（每幀由 _Input 收集）
    bool _clickEdge, _spaceEdge;
    int  _chosen = -1;
    float _markerX = 300f;

    const float TrackW = 600f;
    const float MarkMax = 580f;

    public static async Task<MgResult> Play(Node owner)
    {
        var g = new MiniGameNight3 { Layer = 200, Name = "MiniGameNight3" };
        owner.AddChild(g);
        try { return await g._done.Task; }
        finally { if (IsInstanceValid(g)) g.QueueFree(); }
    }

    public static float SkipScale = 8f;   // 按住 Ctrl（或滑鼠右鍵）時全部動畫的倍速；要選擇的時候（待機中）不會加速
    public override void _ExitTree() { Engine.TimeScale = 1.0; _done.TrySetCanceled(); }

    public override void _Ready()
    {
        _cfg = ResourceLoader.Exists("res://Resources/MG/MiniGameNight3Config.tres")
            ? ResourceLoader.Load<MiniGameNight3Config>("res://Resources/MG/MiniGameNight3Config.tres")
            : null;
        if (_cfg == null)
        {
            _cfg = new MiniGameNight3Config
            {
                BottomPos = new Vector2(495, 200), BottomSize = new Vector2(550, 800),
                UterusPos = new Vector2(480, 360), UterusSize = new Vector2(320, 320),
                FramePos  = new Vector2(500, 160), FrameSize  = new Vector2(520, 770),
                JijiSize  = new Vector2(500, 700),
            };
        }
        BuildUI();
        TaskLogger.Run(RunAsync(), "MiniGameNight3");
    }

    // 每幀固定更新雞雞/子宮動畫（不依賴遊戲流程目前在哪個等待狀態，雞雞往上子宮就一定會跟著動）
    public override void _Process(double delta)
    {
        if (_cumMat != null && _cumRect != null && IsInstanceValid(_cumRect)) UpdateSlosh((float)delta);
        bool ff = (Input.IsKeyPressed(Key.Ctrl) || Input.IsMouseButtonPressed(MouseButton.Right)) && !_idling;
        Engine.TimeScale = ff ? SkipScale : 1.0;   // 任何動畫（開場後的淡入、掃動、射精、白色流動…）按住都能快轉
        if (_osc)   // 待機與選完掃動共用同一個動作：標記以 SweepSpeed 等速在 20~580 來回
        {
            float step = (_idling ? IdleSpeed : _sweepSpeed) * (float)delta;
            float lo = _idling ? IdleMinX : 20f, hi = _idling ? IdleMaxX : MarkMax;   // 待機只在小範圍慢慢動，不會頂到變形
            _markerX += _dir * step; _traveled += step;
            if (_markerX >= hi) { _markerX = hi; _dir = -1f; }
            if (_markerX <= lo) { _markerX = lo; _dir = 1f; }
        }
        if (_stage != null && _cfg != null && _uterusPoly != null && _jijiPoly != null) SyncJiji();
    }

    public override void _Input(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) _clickEdge = true;
        else if (e is InputEventKey { Keycode: Key.Space, Pressed: true, Echo: false }) _spaceEdge = true;
    }

    // ══════════════════════════════════════════════════════════
    //  UI
    // ══════════════════════════════════════════════════════════
    // ── 版面（四張圖同為 2050x1250 的整張畫布，疊在同一原點，貼齊畫面左側）──
    public static float StageScale = 0.828f;          // 畫布縮放
    public static Vector2 CanvasPx = new(2050f, 1250f);
    public static Vector2 JijiPivotPx = new(393f, 664f);    // 雞雞旋轉支點（畫布像素）：雞雞圖左側中間
    public static Vector2 StageOffsetPx = new(996f, 0f);   // 整體偏移（畫布像素）：負=往左，讓內容貼齊畫面左邊
    public static Vector2 GroupBOffsetPx = new(-684f, 0f);  // 子宮+外框 相對 底+雞雞 的偏移（畫布像素）
    public static Vector2 UterusFinePx = new(25f, -20f);     // 子宮單獨微調
    public static Vector2 FrameFinePx = new(0f, -24f);       // 外框單獨微調
    public static Vector2 BottomFinePx = new(-52f, -148f);      // 底單獨微調
    public static float JijiRotSign = 1f;                   // 旋轉方向：1=逆時針（頂端往左），-1=順時針
    public static float JijiMoveScale = 1f;                 // 上下位移倍率（0=只繞錨點旋轉不位移）
    public static float JijiLeftCompPx = 40f;               // 往上旋轉（靠近子宮）時的向左位移補償（畫布像素）；0=不補償
    public static bool MaskOn = false;                      // 遮色片：雞雞、子宮 只顯示在「底」的形狀內（外框不受影響）
    public static float PanelY = 60f;                       // 面板上緣位置（畫面像素）
    public static float PanelX = 750f;                     // 右側面板左邊位置（畫面像素）
    public static Color BgColor = new(0.02f, 0.01f, 0.03f);   // 背景底色（沒有背景圖時）
    public static bool BgUseImage = true;                   // true=改回用 mg_bg.png 背景圖
    public static Vector2 BgOffsetPx = new(-100f, 0f);       // 背景圖位移（畫面像素，負=往左）
    public static float UiScale = 1.15f;                     // 面板與氣泡的放大倍率
    public static int SweepCycles = 2;                      // 選完後完整上下掃幾次才開始隨機停止
    public static float UterusLiftSmooth = 3.5f;            // 子宮被頂上移的平滑速度（越小越慢）
    public static float UterusRate = 0.7f;                  // 子宮變形最快速度（變形量/秒，越小越慢）
    public static float UterusIdleMul = 0.5f;               // 子宮待機自己的起伏幅度（相對 IdleAmp）
    public static float FrameStretchPx = 50f;               // 外框向下拉長（畫面像素）
    public static float FrameScale = 1.01f;                 // 外框放大倍率
    public static Vector2 FrameCenterPx = new(1636f, 511f);   // 外框放大的中心（畫布像素）
    public static float FrameClipErodePx = 12f;             // 外框裁切再往內縮的像素（右上角超出外框就加大，0=不縮）
    public static bool FrameClipOn = true;                  // 底/子宮/雞雞 只顯示在外框內部形狀裡（背景除外）
    public static float BottomExpandMax = 0.28f;            // 底左右最大擴張比例（0.12＝寬 12%）
    public static float BottomExpandSigma = 0.2f;           // 擴張範圍的高度（佔底圖高度的比例）
    public static float BottomCenterU = 0.492f;             // 擴張的左右中心（底圖寬度比例）
    public static float BottomExpandYPx = 650f;             // 擴張固定發生的高度（畫布像素；底圖明顯歪斜的位置約 600~700）
    public static bool BottomExpandFollowTip = false;       // true=擴張位置改成跟著雞雞前端
    public static float BottomExpandTiltDeg = 30f;          // 擴張方向逆時針傾斜角（跟九宮格一樣歪）
    public static bool BottomWhiteRemove = false;            // 底圖周邊的白色全部移除，只留粉色
    public static float BottomTopCutPx = 0f;               // 底圖最上面的球狀部分切掉（畫布像素；0=不切）
    public static float BottomGridFollow = 0.5f;            // 九宮格變形時，底額外跟著九宮格位移的比例（0=不跟）
    public static float BottomRotOffsetDeg = 2f;            // 底額外順時針旋轉角度（負=逆時針）
    public static float UterusFollowMul = 0.6f;             // 子宮跟著底上下的比例（1=跟底完全一樣，0=不動）
    public static float UterusExpandMul = 0.5f;             // 子宮左右擴張占底擴張的比例（0=不擴張）
    public static float BottomRotMul = 0.3f;                // 擴張那層底跟著雞雞旋轉的比例（0=不轉）
    public static float BottomFollow = 0.1f;                // 擴張的那層底跟著雞雞往上的比例（0=不動）
    public static bool BottomExpandOn = true;
    public static float JijiTipPx = 79f;                    // 雞雞前端在畫布上的 Y（畫布像素）
    public static int UterusStartRow = 2;                   // 雞雞碰到九宮格第幾排錨點才開始變形（由上數，0~3；2＝由下數第二排）
    public static float UterusRampRows = 1f;                // 從開始到變形最大，雞雞要再往上走幾排的距離
    public static float JijiRotMul = 0.05f;                  // 旋轉擺動幅度倍率（1=原本，越小轉越少）
    public static float PushRiseTime = 0.7f, PushRiseExtra = 0.12f, PushJitter = 0.012f;   // 頂到底後慢慢再往上的時間／再往上多少（占雞雞行程比例）／抖動幅度
    float _pushExtra, _pushJit, _thrustPx, _preUp, _preUpS; int _thrustPhase;
    public static float ThrustLastPx = 10f;   // 最後一次（第 3 次）往下多少
    public static float ThrustPx = 5f;   // 「用力插」：前面幾次再往上多 5px，最後一次（頂到底）往下 5px
    public static float JijiUpExtraPx = 50f;               // 雞雞向上的最大位移再多加這麼多（畫布像素）
    public static float DefSmooth = 8f;                     // 變形/位置平滑速度（越小越慢越柔）
    public static int MaxRounds = 10, NeedOrgasms = 2;       // 最多回合、需要的高潮次數
    public static int PhaseEarlyEnd = 3, PhaseMidEnd = 7;    // 前戲期/過渡期結束的回合
    public static float EnergyMax = 100f, EnergyRecover = 3f; // 精力上限、每回合回復
    public static float[] EnergyCost = { 5f, 12f, 20f };     // 三個按鈕的精力消耗（全選最強約第 6 回合沒力）
    // 射精動畫（第 10 回合結尾）
    public static bool CumTestButton = true;                // 顯示測試按鈕（在「用力插」下面）
    public static Vector2 CumTestOffsetPx = new(0f, 100f); // 測試按鈕相對第三顆氣泡的位置（畫面像素）
    public static float CumMarkerX = 40f;                   // 雞雞頂到最深時的標記位置（越小越深）
    public static float CumPushTime = 0.45f;                 // 雞雞頂到最深的時間（秒）
    public static int CumSpurts = 6;                        // 一共射幾團
    public static float CumRiseTime = 0.22f, CumSlideTime = 1.3f;   // 往上射（越小越用力）／沿內圈邊溜下來
    public static float CumSizeFirst = 1.4f, CumSizeSecond = 0.8f, CumSizeLast = 0.6f;   // 第一顆／第二顆／最後一顆的大小倍率
    public static int CumWaveSize = 2;                      // 一波同時射幾發（像調教房：每波 2~3 發連在一起）
    public static float CumWaveGap = 0.5f, CumInWaveGap = 0.05f;   // 波與波的間隔／同一波內每發的間隔（秒）
    public static float CumBeadScale = 0f;                // 尾端小圓珠大小（相對頭，0=沒有）
    public static float CumBeadAlpha = 0.4f;               // 尾端小圓珠不透明度（越小越不明顯）
    public static float CumCurvePx = 25f;                   // 射出時身體彎曲的程度（子宮座標像素，0=直的）
    public static Vector2 CumFirstTargetPx = Vector2.Zero;  // 第一波統一射向的點（子宮座標像素）；(0,0)＝自動取九宮格最高的頂點
    public static Vector2 CumFirstOffsetPx = new(-220f, -5f);   // 第一波落點的微調（子宮座標像素；負 X＝往左、負 Y＝往上）
    public static float CumFirstSpreadPx = 12f;             // 第一波各發在那個點附近的左右散開
    public static float CumMistPx = 70f, CumMistAlpha = 0.45f;   // 子宮底隨機模糊白斑的大小／最濃的程度（0=沒有）
    public static bool CumWhiteOnBottom = true;             // 白色流進「底」圖的深紅通道（黑線不蓋）
    public static float CumJijiY0 = 480f, CumJijiY1 = 1120f;   // 白色在雞雞上流動的範圍（雞雞圖畫布 y，從前端往下）
    public static float CumPoolAlpha = 0.95f;                 // 灌滿的液面的不透明度（1=不透明，不淡化）
    public static float CumSloshGain = 1f, CumSloshSpring = 28f, CumSloshDamp = 3.2f;   // 液面晃動：力道／彈性（越大越快回正）／阻尼（越小晃越久）
    float _slX, _slV, _lastTipY, _lastDefPx, _slB, _slBV;
    public static float CumBulgeGain = 5f, CumBulgeMaxPx = 30f, CumWaveCalm = 0.7f, CumWaveCalmAmp = 0.8f, CumWaveBigAmp = 3f;   // 凸起力道／最大高度；沒動作後波慢慢平靜的速度；平靜時／最大動作時的波大小倍率
    float _slE, _slEImp, _lastPool; bool _slReset; ShaderMaterial _cumMat; Rect2 _cumA;
    public static float _unusedSl;   // 往上頂時液面中間凸起、往下退時中間凹進去的力道／最大高度（畫布像素）
    public static float CumSloshDef = 2.5f;   // 液面受子宮格子變形影響的程度（變形的速度推動晃動；格子左右高低差直接讓液面傾斜）
    public static float CumHazeTop = 0.15f;                 // 白底漸層最上面的濃度（0=最上面沒有白；1=整片一樣濃）
    public static float CumHazeAlpha = 0.3f;                // 被射完後子宮內圈下半部半透明白底的濃度（0=沒有）
    public static float CumJijiEdgePx = 16f;                 // 白色只蓋紅色與黑線的交界處，這是交界兩側各算多寬（畫布像素，越大蓋的範圍越寬）
    public static float CumJijiStrokeTime = 3.6f;           // 白色從頂點沿著左右兩邊的線刷到底花多久（秒）
    public static float CumJijiBlobTime = 1.0f;             // 起點那團不規則橢圓浮現、長大的時間（秒），之後才開始往下流
    public static Vector2 CumJijiBlobCenterPx = new(689f, 187f);   // 那團的中心（雞雞圖畫布像素）
    public static float CumJijiBlobRotDeg = 35f;            // 起點那團橢圓的旋轉角度（度，正＝逆時針歪，跟九宮格同方向；負＝順時針）
    public static Vector2 CumJijiBlobSizePx = new(62f, 26f);       // 那團的半徑 x／y
    public static float CumJijiBrushSoft = 0.1f;            // 筆刷前緣的柔和程度（越大尾巴越長越淡）
    public static float CumJijiCenterX = 845f, CumJijiSlope = 0.03f;   // 雞雞中線：在 CumJijiY0 高度的 x 位置（畫布像素）、每往下 1px 往右偏多少
    public static float CumJijiHalfW = 200f;                // 白色範圍的半寬（畫布像素），左右對稱
    public static float CumJijiMid = 0.75f;                 // 中線附近的白色濃度（相對兩側；1=一樣濃）
    public static float CumJijiLineCover = 1f;              // 黑線本身被白色蓋住的程度（1=完全蓋住，0=線不蓋、只有旁邊有白暈）
    public static float CumJijiWhiteStr = 0.95f;             // 白色蓋過雞雞紅色與黑線的程度
    public static float CumWhiteX0 = 895f, CumWhiteX1 = 1185f;   // 白色流入的橫向範圍（底圖畫布像素，包含兩側黑線）
    public static float CumBrushArc = 60f;                  // 白色前緣像筆刷刷過的弧線彎多少（畫布像素；0=一條直線，兩側比中間更晚到）
    public static float CumBrushSlant = 20f;                // 前緣整體斜多少（畫布像素；負=往另一邊斜）
    public static float CumWhiteMid = 0.3f;                 // 通道中間的白色濃度（相對兩側；1=一樣濃）
    public static float CumWhiteTiltDeg = 15f;              // 中間淡的那條線歪的角度（度，正＝由上往右下歪）
    public static float CumWhiteCenterOfs = 0.1f;           // 中間淡的那條線偏離正中間多少（-1~1，相對半寬）
    public static float CumWhiteStrength = 0.95f;            // 白色蓋過去的程度（1=完全蓋過黑線）
    public static float CumWhiteFlowTime = 4.5f;            // 白色流入底圖深紅通道花多久（秒）
    public static bool CumTroomStyle = true;                // 照調教房的射精：直的一串圓依序飛出，頭落地形成一團再被後面吸收變大（false=蝌蚪風格）
    public static float CumTailDelay = 0.35f;               // 調教房風格：尾巴比頭晚多久到（相對往上射的時間，越大越長）
    public static float CumSagPx = 60f;                     // 射出時中段被重力往下拉的程度（0=直線）
    public static float CumWobble = 0.15f;                  // 身體粗細的波動（0=平滑，越大越像流動的液體）
    public static float CumHeadPx = 22f;                    // 蝌蚪頭的大小（半徑，子宮座標像素）
    public static float CumTailPx = 3f;                     // 尾巴最細處的粗細（半徑）
    public static float CumOutlinePx = 2.5f;                // 頭的深棕色輪廓粗細
    public static float CumHeadCenterAlpha = 0.85f;         // 頭中心的不透明度（0=全透明）
    public static float CumJetLenPx = 420f;                 // 液柱長度（子宮座標像素）
    public static float CumTiltDeg = 8f;                   // 液柱歪的角度（跟九宮格一樣逆時針 30 度）
    public static float CumWallScale = 0.88f;               // 溜滑梯沿著內圈邊的位置（1=貼著邊緣，越小越靠內）
    public static float CumTrail = 0.45f;                   // 滑的時候尾巴的長度（0~1）
    public static Rect2 CumAreaPx = new(1428f, 108f, 357f, 255f);   // 子宮內圈（深色圓）的範圍（畫布像素），液體只在這裡面
    public static float CumPeakFromTopPx = 30f;             // 最高點：離子宮內圈頂端往下多少（子宮座標像素，越小射得越高）
    public static float CumShootSpreadPx = 150f;             // 每團射到頂時，左右散開的隨機範圍（子宮座標像素）
    public static float CumRestUpPx = 20f;                  // 液面底離內圈底端往上多少（子宮座標像素）
    public static float CumPoolPx = 22f;                    // 每團滴到底後，積在子宮底部的液面升高多少（畫布像素）
    public static float CumBlobPx = 44f;                    // 每團的大小（半徑，畫布像素）
    public static float CumAlpha = 0.95f;                   // 不透明度（1=完全不透明）
    public static bool CumClipUterus = true;                // 只顯示在子宮形狀裡面
    public static Vector2 CumTipOffsetPx = new(-130f, 0f);     // 出口相對雞雞前端的微調（畫布像素）
    public static float IdleSpeed = 60f;                    // 待機時的移動速度（慢）
    public static float IdleMinX = 430f, IdleMaxX = 580f;   // 待機只在這個範圍動（越靠近 580 越不會頂到子宮變形）
    public static float[] ActionSpeeds = { 520f, 850f, 1250f };   // 三個按鈕選完後的掃動速度（慢／中／快）
    public static float SweepSpeed = 450f;                  // 掃動速度（邏輯座標 px/秒，全程 560）
    public static bool BottomPink = false;                   // 底圖整個改成單一粉色（保留原本的形狀/透明度）
    public static Color BottomColor = new(0.976f, 0.514f, 0.502f);
    public static float BottomStretchPx = 60f;              // 底圖向下拉長（畫面像素）
    public static float JijiStartRotDeg = 10f;               // 初始位置（G點端）額外旋轉角，正=順時針；往子宮端逐漸減為 0
    // ── 子宮變形（3x3 九宮格錨點，頂排固定、底排中間變形最多）──
    public static Rect2 UterusBoxPx = new(1334f, 0f, 534f, 484f);   // 子宮在畫布上的範圍（畫布像素）
    public static float UterusDeformPx = 123f;              // 變形量最大值（畫布像素，往上）
    public static float[] UterusGridW = { 0f, 0f, 0f, 0f,   0.1f, 0.25f, 0.25f, 0.1f,   0.3f, 1.108f, 1.308f, 0.8f,   0.5f, 1f, 1f, 0.1f };   // 4x4 錨點（=3x3 九宮格）權重，由上到下、左到右
    public static float UterusGridRotDeg = 30f;             // 九宮格逆時針旋轉角（度）
    public static float UterusScale = 1.2f;                 // 子宮放大倍率（以子宮中心放大）
    public static float UterusTouchLiftPx = 40f;            // 雞雞頂到子宮時，子宮額外上移量（畫布像素）
    public static float UterusTouchFrom = 0.8f;             // 變形量超過多少才開始被頂（0~1）
    public static bool UterusOnTop = true;                  // 子宮是否畫在雞雞上面
    public static bool ShowUterusGrid = true;               // 畫出九宮格
    public static float IdlePeriod = 4f;                    // 待機時上下一個來回的秒數（與G點高潮掃動速度相近）
    public static float IdleAmp = 0.8f;                     // 待機時變形幅度（0~1）
    const int GN = 10;
    // ── 雞雞變形（頂端＝前端移動最多、底部固定；再繞錨點旋轉）──
    public static Rect2 JijiBoxPx = new(380f, 70f, 910f, 1190f);    // 雞雞在畫布上的範圍（畫布像素）
    public static float[] JijiGridW = { 1f, 1f, 1f, 1f,  1f, 1f, 1f, 1f,  1f, 1f, 1f, 1f,  1f, 1f, 1f, 1f };   // 全 1＝整體上下平移（不做九宮格）
    public static float JijiIdleMul = 0.3f;                 // 雞雞待機上下幅度（相對子宮待機幅度）
    public static bool ShowJijiGrid = false;
    public static float BgFollow = 0.3f;                    // 背景跟隨雞雞上下位移的比例（0=不動，1=完全跟隨）
    public static bool ShowPivot = false;                    // 顯示雞雞旋轉錨點（紅點）
    public static Vector2 JijiFinePx = new(315f, 375f);        // 雞雞單獨微調
    const float PanelW = 400f, PanelH = 250f, TK = 352f / TrackW;   // TK：軌道顯示比例

    Control _stage;
    ColorRect _pivotDot;
    TextureRect _bgImg;
    Polygon2D _uterusPoly, _jijiPoly;
    readonly Line2D[] _jGridLines = new Line2D[8];
    readonly ColorRect[] _jGridDots = new ColorRect[16];
    readonly Line2D[] _gridLines = new Line2D[8];
    readonly ColorRect[] _gridDots = new ColorRect[16];
    float _idleT, _idleMix, _nxS = -1f, _uDef, _liftS, _dir = 1f, _traveled; bool _osc; bool _idling; float _sweepSpeed = 450f;
    ShaderMaterial _maskJiji, _maskUterus;
    // 外框內部形狀裁切（共用）：底、子宮、雞雞都不會跑出外框；背景不受影響
    const string FClipDecl = @"
uniform sampler2D fmask : filter_linear;
uniform vec2 f_pos;
uniform vec2 f_size;
uniform float f_on = 1.0;
uniform vec2 f_er = vec2(0.0, 0.0);
varying vec2 wpos;
void vertex() { wpos = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy; }
uniform float rot_a = 0.0;
uniform vec2 rot_p = vec2(0.19, 0.53);
uniform float asp = 1.6;
vec2 unrot(vec2 q) {
    vec2 d = (q - rot_p) * vec2(asp, 1.0);
    float c = cos(rot_a); float s = sin(rot_a);
    d = vec2(c * d.x + s * d.y, -s * d.x + c * d.y);
    return rot_p + d / vec2(asp, 1.0);
}
float fclip() {
    vec2 q = (wpos - f_pos) / f_size;
    if (q.x < 0.0 || q.y < 0.0 || q.x > 1.0 || q.y > 1.0) return mix(1.0, 0.0, f_on);
    vec2 e = f_er;
    float a = texture(fmask, q).a;
    a = min(a, texture(fmask, q + vec2(e.x, 0.0)).a);
    a = min(a, texture(fmask, q - vec2(e.x, 0.0)).a);
    a = min(a, texture(fmask, q + vec2(0.0, e.y)).a);
    a = min(a, texture(fmask, q - vec2(0.0, e.y)).a);
    a = min(a, texture(fmask, q + e).a);
    a = min(a, texture(fmask, q - e).a);
    return mix(1.0, a, f_on);
}
";
    // 歪斜的左右擴張：沿著逆時針傾斜 ex_tilt 的方向擴張，位置在 (ex_cx, ex_tip) 附近（與九宮格同角度）
    const string ExpandDecl = @"
uniform float ex_amt = 0.0;
uniform float ex_tip = 0.0;
uniform float ex_sig = 0.12;
uniform float ex_cx = 0.46;
uniform float ex_tilt = 0.0;
uniform float top_cut = 0.0;
uniform float wh_on = 1.0;
float wh(vec3 c) { return smoothstep(0.78, 0.93, min(c.r, min(c.g, c.b))); }
vec2 expandq(vec2 q) {
    vec2 o = vec2(ex_cx, ex_tip);
    vec2 p = (q - o) * vec2(asp, 1.0);
    float c = cos(ex_tilt); float s = sin(ex_tilt);
    float al = c * p.x - s * p.y;
    float ac = s * p.x + c * p.y;
    float bb = exp(-pow(ac / ex_sig, 2.0));
    al /= (1.0 + ex_amt * bb);
    p = al * vec2(c, -s) + ac * vec2(s, c);
    return o + p / vec2(asp, 1.0);
}
";
    const string BottomExpandShader = @"
shader_type canvas_item;" + FClipDecl + ExpandDecl + @"
uniform vec4 tint : source_color = vec4(0.976, 0.514, 0.502, 1.0);
uniform float on = 0.0;
uniform float fill_on = 1.0;
uniform float wf_amt = 0.0;
uniform float wf_t0 = 0.29;
uniform float wf_t1 = 0.78;
uniform float wf_str = 0.9;
uniform float wf_mid = 0.3;
uniform float wf_brush_a = 0.05;
uniform float wf_brush_s = 0.02;
uniform float wf_cofs = 0.1;
uniform float wf_slant = 0.2;
uniform float wf_x0 = 0.44;
uniform float wf_x1 = 0.57;
uniform sampler2D wf_tex : filter_linear;
uniform float wf_soft = 0.12;
void fragment() {
    vec2 uv0 = unrot(UV);
    vec2 qq = expandq(uv0);
    vec4 c = texture(TEXTURE, qq);
    if (qq.x < 0.0 || qq.x > 1.0 || qq.y < 0.0 || qq.y > 1.0) c.a = 0.0;
    c.a *= mix(1.0, 1.0 - wh(c.rgb), wh_on) * smoothstep(top_cut, top_cut + 0.006, qq.y);
    {   // 白色流入底圖裡顏色較深的紅色通道（從上往下慢慢流下來，之後留著）
        float deep = smoothstep(0.50, 0.45, c.g) * smoothstep(0.30, 0.37, c.g) * step(0.75, c.r) * smoothstep(wf_x0 - 0.03, wf_x0 + 0.03, qq.x) * smoothstep(wf_x1 + 0.03, wf_x1 - 0.03, qq.x);   // 只有深紅色（黑線不蓋）；兩側邊緣柔化
        float bu = (qq.x - ((wf_x0 + wf_x1) * 0.5)) / max((wf_x1 - wf_x0) * 0.5, 0.001);
        float front = mix(wf_t0, wf_t1 + 0.16, wf_amt) + bu * wf_brush_s + bu * bu * wf_brush_a;   // 筆刷刷過去：前緣是一道圓滑的弧線，微微斜，沒有抖動／顆粒
        float wm = smoothstep(front, front - 0.14, qq.y) * smoothstep(wf_t0 - 0.03, wf_t0 + 0.03, qq.y) * step(0.001, wf_amt);   // 前緣柔化
        float cxm = (wf_x0 + wf_x1) * 0.5 + wf_cofs * (wf_x1 - wf_x0);
        float u = (qq.x - cxm - (qq.y - wf_t0) * wf_slant) / max((wf_x1 - wf_x0) * 0.5, 0.001);   // 中線有點歪、而且不在正中間
        float prof = mix(wf_mid, 1.0, smoothstep(0.05, 0.8, abs(u)));   // 左右兩邊比較濃、中間比較淡
        c.rgb = mix(c.rgb, vec3(0.97, 0.95, 0.92), deep * wm * wf_str * prof);
        // 另外新增：
        // 跟雞雞一樣用預先算好的筆刷遮罩 mg_bottom_stroke.png：R＝深紅通道的範圍（邊緣濃、中間淡、邊緣不規則）、G＝由上往下的進度
        vec4 sm = texture(wf_tex, qq);
        float frontp = wf_amt * (1.0 + wf_soft);
        float wm2 = smoothstep(frontp, frontp - wf_soft, sm.g) * step(0.001, wf_amt);
        c.rgb = mix(c.rgb, vec3(0.97, 0.95, 0.92), sm.r * wm2 * wf_str);
    }
    float fc = fclip();
    c.a *= fc;
    c.rgb = mix(tint.rgb, c.rgb, c.a);   // 外框以內沒有圖的地方：墊粉色
    c.a = max(c.a, fc * fill_on);
    COLOR = mix(c, vec4(tint.rgb, c.a * tint.a), on);
}";
    const string MaskShader = @"
shader_type canvas_item;" + FClipDecl + ExpandDecl + @"
uniform sampler2D mask_tex : filter_linear;
uniform vec2 m_pos;
uniform vec2 m_size;
uniform float m_on = 1.0;
uniform float jf_amt = 0.0;
uniform sampler2D jf_tex : filter_linear;
uniform float jf_soft = 0.1;
uniform float jb_amt = 0.0;
uniform float jb_cs = 1.0;
uniform float jb_sn = 0.0;
uniform vec2 jb_c = vec2(782.0, 130.0);
uniform vec2 jb_r = vec2(60.0, 40.0);
uniform float jf_y0 = 85.0;
uniform float jf_y1 = 900.0;
uniform float jf_str = 0.9;
uniform float jf_edge = 5.0;
uniform float jf_line = 0.3;
uniform float jf_c0 = 740.0;
uniform float jf_cs = 0.15;
uniform float jf_w = 170.0;
uniform float jf_mid = 0.35;
uniform float jf_brush_a = 60.0;
uniform float jf_brush_s = 20.0;
uniform vec2 jf_canvas = vec2(2050.0, 1250.0);
uniform vec2 dp_c = vec2(680.0, 130.0);
uniform vec2 dp_r = vec2(75.0, 45.0);
uniform float dp_amp = 1.0;
uniform float dp_a = 0.9;
uniform float dp_in = 0.0;
uniform float dp_sl = 0.0;
uniform float dp_dbg = 0.0;
uniform float dp_under = 1.0;
uniform float dp_rot = 0.0;
void fragment() {
    vec2 q = unrot((wpos - m_pos) / m_size);
    q = expandq(q);
    float a = 0.0;
    if (q.x >= 0.0 && q.y >= 0.0 && q.x <= 1.0 && q.y <= 1.0) { vec4 mt = texture(mask_tex, q); a = mt.a * mix(1.0, 1.0 - wh(mt.rgb), wh_on) * smoothstep(top_cut, top_cut + 0.006, q.y); }
    float blobA = 0.0;
    {   // 白色像 G 筆刷：從雞雞頂點的黑線出發，沿著左右兩邊「連在一起」的線一路刷到底；
        // 只往外側和往下，不往內；沒有連到這條線的黑線完全不碰。
        // 遮罩圖 mg_jiji_stroke.png：R＝這條線（含往外的一圈柔和的暈）、G＝從頂點沿線走的進度 0→1
        vec4 sm = texture(jf_tex, UV);
        float frontp = jf_amt * (1.0 + jf_soft);
        float wm = smoothstep(frontp, frontp - jf_soft, sm.g) * step(0.001, jf_amt);   // 進度還沒到的地方不刷；前緣是柔和的筆刷尾
        COLOR.rgb = mix(COLOR.rgb, vec3(0.97, 0.95, 0.92), sm.r * wm * COLOR.a * jf_str);
        // 起點先積出一團不規則的橢圓（有點歪、邊緣有幾個小凹凸），慢慢浮現、長大，之後才從這裡開始流出
        vec2 bd = (UV * jf_canvas - jb_c) / (jb_r * mix(0.45, 1.0, jb_amt));
        bd = vec2(jb_cs * bd.x - jb_sn * bd.y, jb_sn * bd.x + jb_cs * bd.y);   // 整團跟你的圖一樣歪一點（逆時針旋轉）
        float ba = atan(bd.y, bd.x);
        float brad = length(bd) / (1.0 + 0.16 * sin(2.0 * ba + 0.8) + 0.11 * sin(3.0 * ba + 2.1) + 0.06 * sin(5.0 * ba + 0.3));
        float bl = smoothstep(1.0, 0.55, brad) * 0.9;   // 邊緣柔和一點、整體稍微透明
        // 像精液一樣，從這團往下垂出幾條不規則的流：長短、粗細、彎曲都不一樣，越往下越細越淡
        vec2 dq0 = UV * jf_canvas - jb_c;
        vec2 dq = vec2(dot(dq0, vec2(0.91, -0.42)), dot(dq0, vec2(0.42, 0.91)));   // 跟雞雞圖一樣歪：沿著歪的中軸往右下流
        const float FanW = 95.0;   // 流水最上面那段的橫向放大量（像素，沿歪的軸）
        float gr = smoothstep(0.5, 1.0, jb_amt) * clamp((jf_amt - 0.12) / 0.88, 0.0, 1.0);   // 先長出來，再隨著白色往下流的進度一路流長
        float dr = 0.0;
        float xs[4] = float[4](-18.0, -6.0, 6.0, 18.0);      // 全部從原點出發
        float sg[4] = float[4](-1.0, -1.0, 1.0, 1.0);          // 左邊兩條往左彎、右邊兩條往右彎（像雞雞兩側主線的弧線）
        float mg[4] = float[4](210.0, 105.0, 15.0, 105.0);  // 弧線向外的幅度（左右分得更開）
        float xe[4] = float[4](-120.0, -120.0, 25.0, 25.0); // 流到後來靠攏併入的位置
        float ts[4] = float[4](150.0, 220.0, 120.0, 220.0);
        float te[4] = float[4](360.0, 460.0, 400.0, 430.0);
        float ln[4] = float[4](370.0, 550.0, 470.0, 310.0); // 從原點出發，最長流到下方約 600 像素再淡掉
        for (int i = 0; i < 4; i++) {
            float fy = ln[i] * gr;                     // 這一條目前流到的位置（流頭）
            // 沿著流的中心線：yh 位置的左右偏移（流身和流頭共用）
            float cy = clamp(dq.y, 0.0, 2000.0);
            float cx = mix(xs[i] + sg[i] * mg[i] * (1.0 - exp(-cy / 95.0)), xe[i], smoothstep(ts[i], te[i], cy))
                     + 22.0 * (0.35 + 0.65 * smoothstep(0.0, 300.0, cy)) * sin(cy * 0.017 + float(i) * 2.3) + 9.0 * sin(cy * 0.043 + float(i) * 4.1);
            float hy = fy;
            float hx = mix(xs[i] + sg[i] * mg[i] * (1.0 - exp(-hy / 95.0)), xe[i], smoothstep(ts[i], te[i], hy))
                     + 22.0 * (0.35 + 0.65 * smoothstep(0.0, 300.0, hy)) * sin(hy * 0.017 + float(i) * 2.3) + 9.0 * sin(hy * 0.043 + float(i) * 4.1);
            float prog = clamp(dq.y / ln[i], 0.0, 1.0);
            float rnd = 1.0 + 0.12 * sin(dq.y * 0.05 + float(i) * 1.7) + 0.08 * sin(dq.y * 0.13 + float(i) * 3.1);   // 粗細有一點隨機變化（範圍不大）
            float near = 1.0 - smoothstep(0.0, 110.0, dq.y);   // 靠近原點那一段：更粗、更濃，跟原點那團接在一起
            float w = (27.0 * (1.0 - 0.62 * prog) + 14.0 * near + 16.0 * (1.0 - smoothstep(0.0, 55.0, dq.y))) * rnd + (FanW + (sg[i] < 0.0 ? 50.0 : -75.0)) * (1.0 - smoothstep(30.0, 100.0, dq.y));   // 最上面約 30px 橫向放大，四條合起來蓋住整個雞雞的頂   // 越上面越粗
            float hr = 27.0 * (1.0 - 0.62 * clamp(fy / ln[i], 0.0, 1.0)) + 9.0;   // 水滴頭的半徑
            float sw = pow(smoothstep(fy - 130.0, fy, dq.y), 1.7);   // 流身在接近流頭的 130 像素內，平順地鼓起來變成水滴（不是另外貼一顆圓）
            w = mix(w, hr, sw);
            float body = smoothstep(w, w * 0.2, abs(dq.x - cx)) * smoothstep(sg[i] < 0.0 ? -115.0 : -60.0, 6.0, dq.y) * smoothstep(fy + 6.0, fy - 4.0, dq.y) * clamp(1.0 - 0.6 * prog + 0.35 * near, 0.0, 1.0);   // 流身一路連到流頭，不再先淡掉
            vec2 hv = vec2(dq.x - hx, dq.y - hy);   // 流頭只負責圓圓的底，上面由流身鼓起來接上
            float head = smoothstep(hr, hr * 0.55, length(hv)) * (0.95 - 0.5 * clamp(fy / ln[i], 0.0, 1.0)) * step(0.01, gr) * (1.0 - smoothstep(0.8, 1.0, gr));
            dr = max(dr, max(body, head));
        }
        {   // 流水不能超過兩側主線：用雞雞輪廓左右邊緣（每 75px 一個點）內縮一點，超出的地方就收掉
            float py = UV.y * jf_canvas.y; float fi = clamp((py - 150.0) / 75.0, 0.0, 12.99); int i0 = int(fi); float ft = fi - float(i0);
            float eL[14] = float[14](545.0,481.0,452.0,419.0,400.0,395.0,413.0,434.0,462.0,500.0,526.0,539.0,566.0,659.0);
            float eR[14] = float[14](908.0,1018.0,1086.0,1129.0,1146.0,1163.0,1172.0,1172.0,1155.0,1167.0,1183.0,1184.0,1161.0,1059.0);
            float xl = mix(eL[i0], eL[i0 + 1], ft) + 30.0, xr = mix(eR[i0], eR[i0 + 1], ft) - 30.0;
            float px = UV.x * jf_canvas.x;
            dr *= smoothstep(xl - 12.0, xl + 14.0, px) * smoothstep(xr + 12.0, xr - 14.0, px);
        }
        // 一團大的柔和橢圓：帶著流一起往下走，越往下越淡
        float ey = gr * 400.0;
        vec2 ed = vec2(dq.x / 95.0, (dq.y - ey) / 60.0);
        float el_ = smoothstep(1.0, 0.0, length(ed)) * 0.35 * (1.0 - clamp(ey / 520.0, 0.0, 1.0)) * step(0.01, gr);
        dr = max(dr, el_);
        blobA = max(bl, dr) * jb_amt * jf_str;
        float lum_ = dot(COLOR.rgb, vec3(0.299, 0.587, 0.114));   // 黑色的線不要被白霧蓋住：很暗的地方只留一點點白
        blobA *= mix(1.0, mix(0.15, 1.0, smoothstep(0.12, 0.42, lum_)), step(0.5, COLOR.a));
    }
    float dropA = 0.0;
    {   // 雞雞前端旁邊那一小灘會晃的水：直接畫在雞雞這一層（跟原點、流水同一個畫法），所以一定跟著雞雞走、不會被別的圖層切到
        vec2 pd = UV * jf_canvas - dp_c;
        pd = vec2(cos(dp_rot) * pd.x + sin(dp_rot) * pd.y, -sin(dp_rot) * pd.x + cos(dp_rot) * pd.y);   // 整個水灘（包含水面的波）一起旋轉
        vec2 dq2 = pd / dp_r;
        float body = smoothstep(1.0, 0.78, length(dq2));
        float tw = -0.42 + (sin(UV.x * jf_canvas.x * 0.05 + TIME * 2.4) * 0.11 + sin(UV.x * jf_canvas.x * 0.022 - TIME * 1.5 + 1.3) * 0.07) * dp_amp + dp_sl * dq2.x * 0.12;
        tw = clamp(tw, -0.80, -0.12);
        float fillm = smoothstep(tw - 0.05, tw + 0.05, dq2.y);
        float depth = mix(0.92, 1.0, smoothstep(tw, tw + 0.5, dq2.y));
        dropA = body * fillm * depth * dp_a * dp_in;
    }
    COLOR.a *= mix(1.0, a, m_on) * fclip();
    dropA *= 1.0 - COLOR.a * dp_under;
    dropA *= mix(1.0, mix(0.23, 1.0, smoothstep(0.12, 0.42, dot(COLOR.rgb, vec3(0.299, 0.587, 0.114)))), step(0.5, COLOR.a));   // 黑線不被水灘蓋住（只留 23%）   // 水灘在雞雞後面：雞雞身體有畫到的地方蓋住它，只有雞雞外面（雞雞和底之間）看得到
    blobA = max(blobA, dropA);
    COLOR = vec4(mix(COLOR.rgb, vec3(0.97, 0.95, 0.92), blobA), max(COLOR.a, blobA));
    COLOR.rgb = mix(COLOR.rgb, vec3(1.0, 1.0, 1.0), clamp(dropA * 1.2, 0.0, 1.0) * (1.0 - dp_dbg));   // 水灘的部分再白一點
    COLOR.rgb = mix(COLOR.rgb, vec3(1.0, 0.1, 0.1), dropA * dp_dbg);   // 起點那團不受雞雞輪廓、遮色片、外框裁切限制
}";
    const string DropShader = @"
shader_type canvas_item;" + FClipDecl + @"
uniform vec2 canvas_px = vec2(2050.0, 1250.0);
uniform vec2 d_c = vec2(1000.0, 700.0);
uniform vec2 d_r = vec2(75.0, 30.0);
uniform float d_amp = 1.0;
uniform float d_a = 0.7;
uniform float d_in = 0.0;
uniform float d_sl = 0.0;
uniform float d_dbg = 0.0;
uniform float d_rot = 0.0;
void fragment() {
    vec2 p = UV * canvas_px;
    vec2 pd = p - d_c;
    pd = vec2(cos(d_rot) * pd.x + sin(d_rot) * pd.y, -sin(d_rot) * pd.x + cos(d_rot) * pd.y);
    vec2 q = pd / d_r;
    float body = smoothstep(1.0, 0.78, length(q));
    float tw = -0.42 + (sin(p.x * 0.05 + TIME * 2.4) * 0.11 + sin(p.x * 0.022 - TIME * 1.5 + 1.3) * 0.07) * d_amp + d_sl * q.x * 0.12;
    tw = clamp(tw, -0.80, -0.12);   // 水面再怎麼晃都不會升到水灘中間以上，不會把水灘吃掉一塊   // 液面：跟子宮一樣的水波，量少
    float fillm = smoothstep(tw - 0.05, tw + 0.05, q.y);
    float depth = mix(0.7, 1.0, smoothstep(tw, tw + 0.5, q.y));
    COLOR = vec4(mix(vec3(0.96, 0.94, 0.89), vec3(0.2, 0.45, 1.0), d_dbg), mix(body * fillm * depth * d_a * d_in, 0.35 * step(abs(p.x - d_c.x), 400.0) * step(abs(p.y - d_c.y), 300.0), d_dbg * 0.0) + d_dbg * 0.0);   // 不再套外框裁切，才不會被切掉一塊
    vec4 ut_ = texture(TEXTURE, UV);   // 子宮自己的圖：黑線不被水滴蓋住，很暗的地方只留 23%
    COLOR.a *= mix(1.0, mix(0.23, 1.0, smoothstep(0.12, 0.42, dot(ut_.rgb, vec3(0.299, 0.587, 0.114)))), step(0.5, ut_.a));
}";
    const string CumShader = @"
shader_type canvas_item;" + FClipDecl + @"
uniform float amt = 0.95;
uniform vec3 col : source_color = vec3(0.98, 0.97, 0.94);
uniform vec3 ol_col : source_color = vec3(0.32, 0.18, 0.10);
uniform float ol_w = 3.5;
uniform float ctr_a = 0.25;
uniform float wob = 0.15;
uniform vec4 ms0; uniform vec4 ms1; uniform vec4 ms2; uniform vec4 ms3; uniform vec4 ms4; uniform vec4 ms5;   // 子宮底的模糊白斑（x,y,半徑,濃度）
uniform float pool_a = 0.8;
uniform float haze = 0.0;
uniform vec2 haze_y = vec2(100.0, 360.0);
uniform float haze_a = 0.3;
uniform float haze_top = 0.55;
uniform float tro = 0.0;   // 1＝調教房風格：直的一串圓（頭大尾小、越後越淡）
uniform float bead_k = 0.5;
uniform float bead_a = 0.55;
uniform sampler2D ut_tex : filter_linear;
uniform vec2 canvas_px;
uniform float ut_on = 1.0;
uniform vec4 tl0; uniform vec4 tl1; uniform vec4 tl2; uniform vec4 tl3;
uniform vec4 tl4; uniform vec4 tl5; uniform vec4 tl6; uniform vec4 tl7;
uniform vec4 hd0; uniform vec4 hd1; uniform vec4 hd2; uniform vec4 hd3;
uniform vec4 hd4; uniform vec4 hd5; uniform vec4 hd6; uniform vec4 hd7;
uniform float pool_top = 100000.0;
uniform float sl_x = 0.0;
uniform float sl_cx = 0.0;
uniform float sl_hw = 150.0;
uniform float sl_b = 0.0;
uniform float sl_amp = 1.0;
uniform float iso = 0.17;
// 蝌蚪：頭（圓、有深棕色輪廓、中心半透明）＋沿著軌跡彎曲、越來越細且越來越透明的尾巴
void tad(vec2 p, vec4 tl, vec4 hd, inout vec3 c, inout float a) {
    float rh = hd.z; float rt = hd.w;
    if (rh <= 0.0) return;
    vec2 T = tl.xy; vec2 M = tl.zw; vec2 H = hd.xy;
    float best = 1e9; float bs = 1.0;
    vec2 pq = T; float pr = rt; float ps = 0.0;
    for (int k = 1; k < 16; k++) {   // 把曲線切成 15 小段，每段算「到線段的距離」＋粗細漸變，才會是連續的一條，不會變成一串圓球
        float s = float(k) / 15.0;
        vec2 q = (1.0 - s) * (1.0 - s) * T + 2.0 * s * (1.0 - s) * M + s * s * H;
        float r = mix(rt, rh, mix(pow(s, 1.6), s, tro)) * (1.0 + wob * sin(s * 22.0 - TIME * 26.0) * (1.0 - s * 0.6));   // 沿著身體的波動：像液體在流動、粗細不平均
        vec2 pa = p - pq; vec2 ba = q - pq;
        float h = clamp(dot(pa, ba) / max(dot(ba, ba), 0.0001), 0.0, 1.0);
        float d = length(pa - ba * h) - mix(pr, r, h);
        if (d < best) { best = d; bs = mix(ps, s, h); }
        pq = q; pr = r; ps = s;
    }
    float body = smoothstep(1.2, -1.2, best);
    float bm = bead_k <= 0.0 ? 0.0 : bead_a * smoothstep(rh * bead_k * 1.1, rh * bead_k * 0.1, length(p - T));   // 尾端的淡淡一團：邊緣完全柔化、沒有圓邊，只是尾端稍微濃一點
    if (body <= 0.0 && bm <= 0.0) return;
    float dh = length(p - H) / rh;
    float hm = smoothstep(1.3, 0.85, dh);                            // 頭的範圍
    float al = body * mix(smoothstep(0.0, 0.7, bs), mix(0.5, 0.92, bs), tro);   // 尾端越來越透明（調教房：0.92→0.5）
    al = max(al, bm);
    al *= mix(1.0, mix(ctr_a, 1.0, smoothstep(0.15, 0.75, dh)), hm); // 頭中心透明
    float ring = smoothstep(-ol_w - 1.0, -ol_w, best) * smoothstep(0.45, 0.9, bs) * (1.0 - tro);   // 深棕色輪廓（主要在頭）
    vec3 cc = mix(col, ol_col, ring * 0.9);   // 平塗，不加陰影／高光
    float aa = mix(al, max(al, body * 0.95 * smoothstep(0.45, 0.9, bs)), ring);   // 輪廓不被中心透明吃掉
    c = mix(c, cc, aa / max(aa + a * (1.0 - aa), 0.001));
    a = aa + a * (1.0 - aa);
}
void fragment() {
    vec2 p = UV * canvas_px;   // 子宮自己的座標（沒變形）；液體畫在跟子宮一樣的網格上，所以會跟著子宮一起變形、移動
    // 積在底部的液面
    float top = pool_top + (sin(p.x * 0.022 + TIME * 2.2) * 5.5 + sin(p.x * 0.011 - TIME * 1.3 + 1.7) * 3.0) * sl_amp + sl_x * (p.x - sl_cx) - sl_b * (1.0 - pow(clamp((p.x - sl_cx) / sl_hw, -1.0, 1.0), 2.0));   // 多個不同速度的波疊起來＋被頂/雞雞下降時液面整個傾斜晃動
    float f = clamp(iso + (p.y - top) * 0.03, 0.0, 0.9) * step(top - 6.0, p.y);
    float a = smoothstep(iso - 0.03, iso + 0.03, f) * pool_a;
    vec3 c = col;
    a *= mix(0.72, 1.0, smoothstep(iso, iso + 0.2, f));
    {   // 被射完之後，子宮內圈下半部一層半透明的白底（越靠下越濃）
        float hz = haze * haze_a * mix(haze_top, 1.0, smoothstep(haze_y.x, haze_y.y, p.y));
        c = mix(c, col, hz / max(hz + a * (1.0 - hz), 0.001));
        a = hz + a * (1.0 - hz);
    }
    tad(p, tl0, hd0, c, a); tad(p, tl1, hd1, c, a); tad(p, tl2, hd2, c, a); tad(p, tl3, hd3, c, a);
    tad(p, tl4, hd4, c, a); tad(p, tl5, hd5, c, a); tad(p, tl6, hd6, c, a); tad(p, tl7, hd7, c, a);
    vec4 mss[6] = vec4[6](ms0, ms1, ms2, ms3, ms4, ms5);
    for (int k = 0; k < 6; k++) {
        float mm = mss[k].w * pow(smoothstep(mss[k].z, 0.0, length(p - mss[k].xy)), 1.5);   // 很糊的一團白
        c = mix(c, col, mm / max(mm + a * (1.0 - mm), 0.001));
        a = mm + a * (1.0 - mm);
    }
    float inside = mix(1.0, smoothstep(0.35, 0.65, texture(ut_tex, UV).a), ut_on);   // 只顯示在子宮內圈裡（圓滑邊緣）
    vec4 ut_ = texture(TEXTURE, UV);   // 子宮自己的圖：黑線不被精液蓋住，很暗的地方只留 23%
    float lk_ = mix(1.0, mix(0.23, 1.0, smoothstep(0.12, 0.42, dot(ut_.rgb, vec3(0.299, 0.587, 0.114)))), step(0.5, ut_.a));
    COLOR = vec4(c, amt * a * inside * fclip() * lk_);
}";

    Vector2 _tipScr;   // 雞雞前端在 _stage 內的位置（SyncJiji 每幀更新）
    Texture2D _uterusTex;
    Node _cumRect; Polygon2D _dropPoly; ShaderMaterial _dropMat; float _dropIn;
    public static bool CumDropOn = true;
    public static bool CumDrop2On = true, CumDrop2Blue = false;   // 子宮底部的那個小水灘（藍色方便辨認；確認位置後把 CumDrop2Blue 改成 false 變白）
    public static Vector2 CumDrop2OffsetPx = new(50f, 90f), CumDrop2RadiusPx = new(42.5f, 23f);
    public static float CumDrop2Alpha = 0.9f, CumDrop2RotDeg = 165f;   // 水滴旋轉角度（負＝右邊往上）
    public static float CumDropRotDeg = -15f;   // 水灘旋轉角度（正＝右邊往下，負＝右邊往上）
    public static bool CumDropBehindJiji = true;   // true＝水灘在雞雞後面（雞雞蓋住它）、底的前面
    public static bool CumDropDebug = false;  // true＝水灘塗成紅色，用來看清楚哪一塊是它、哪一塊是別的東西被切；確認後改成 false
    public static bool CumDropAboveFrame = false;   // true＝畫在最上面（外框也蓋不到它，完全不會被切）
    public static bool CumDropBelowJiji = false;   // true＝水灘在雞雞下面（會被雞雞的輪廓切掉一部分）
    public static Vector2 CumDropOffsetPx = new(-100f, 87f), CumDropRadiusPx = new(75f, 45f);   // 子宮底部小水滴：位置微調／大小
    public static float CumDropAlpha = 1f;
    public static float CumDropFollow = 1f;   // 水灘跟著雞雞位移的比例（1=完全一起動，0=只跟著子宮）

    void UpdateSlosh(float dts)
    {
        dts = Mathf.Max(dts, 0.0001f);
        var A = _cumA; var mat = _cumMat;
        float vel = (_tipScr.Y - _lastTipY) / dts; _lastTipY = _tipScr.Y;
        float defPx = UterusDeformPx * _uDefCur, dvel = (defPx - _lastDefPx) / dts; _lastDefPx = defPx;
        if (_slReset) { _slX = _slV = _slB = _slBV = _slE = 0f; vel = dvel = 0f; _slReset = false; }
        _slV += (-CumSloshSpring * _slX - CumSloshDamp * _slV + vel * CumSloshGain * 0.00035f + dvel * CumSloshDef * 0.0008f) * dts;
        _slX = Mathf.Clamp(_slX + _slV * dts, -0.3f, 0.3f);
        _slBV += (-CumSloshSpring * _slB - CumSloshDamp * _slBV - vel * CumBulgeGain * 0.6f + _slEImp * 40f) * dts;   // 雞雞往上(vel<0)→中間凸起；往下→中間凹；落下／水位上升也推一下
        _slB = Mathf.Clamp(_slB + _slBV * dts, -CumBulgeMaxPx, CumBulgeMaxPx);
        float inE = Mathf.Max(Mathf.Max(Mathf.Abs(vel) / 120f, Mathf.Abs(dvel) / 120f), _slEImp); _slEImp = 0f;
        _slE = Mathf.Max(_slE * Mathf.Exp(-dts * CumWaveCalm), Mathf.Clamp(inE, 0f, 1f));   // 動作越大波越大；停下來後慢慢平靜
        mat.SetShaderParameter("sl_amp", Mathf.Lerp(CumWaveCalmAmp, CumWaveBigAmp, _slE));
        mat.SetShaderParameter("sl_b", _slB); mat.SetShaderParameter("sl_hw", A.Size.X * 0.5f * CumWallScale);
        float yL = A.Position.Y + A.Size.Y * 0.6f;   // 格子變形在液面左右兩端的高低差＝液面跟著格子傾斜
        float gradDef = (UterusDisp(new Vector2(A.End.X, yL)).Y - UterusDisp(new Vector2(A.Position.X, yL)).Y) / A.Size.X;
        mat.SetShaderParameter("sl_x", Mathf.Clamp(_slX + gradDef * CumSloshDef, -0.5f, 0.5f)); mat.SetShaderParameter("sl_cx", A.GetCenter().X);
    }

    /// 子宮變形後，某個「畫布座標」的點實際被推到哪（位移量，畫布像素）
    Vector2 UterusDisp(Vector2 canvasPt)
    {
        var box = UterusBoxPx;
        float gr = Mathf.DegToRad(UterusGridRotDeg);
        var ctr = box.Size / 2f;
        var q = ctr + (canvasPt - box.Position - ctr).Rotated(gr);
        float w = GridW(UterusGridW, Mathf.Clamp(q.X / box.Size.X, 0f, 1f), Mathf.Clamp(q.Y / box.Size.Y, 0f, 1f));
        return new Vector2(0f, -UterusDeformPx).Rotated(-gr) * (w * _uDefCur);
    }

    /// 第 10 回合結尾：細長、歪歪的白色液柱用力射出，頂到內圈上緣後像溜滑梯一樣沿著內圈邊往下滑，
    /// 滑到底融進液面，最後積在子宮底部不消失。液體畫在跟子宮同一個變形網格上，會跟著子宮一起動。
    public static int CumPreLight = 2;                       // 射精前先有幾次比較小力的衝撞
    public static float CumPreBaseX = 800f;                  // 衝撞之間退回的位置（標記位置，越小越深）
    public static float CumPreDepthX = 230f;                 // 小力衝撞頂到的位置
    public static float CumPreDefMul = 0.5f;                 // 射精前三次衝撞時子宮變形量的倍率（1＝跟平常一樣，越小變形越少）
    public static float CumPreUp12Px = 90f;                 // 前兩次小力衝撞，頂進去時雞雞往上多的量
    public static float CumPreRotGain = 2f;                  // 射精前往上多頂的量換算成旋轉的倍率（越大轉得越多，1＝跟位置一樣）
    public static float CumTinyHold = 0.5f;                  // 第三次先射一點點之後停留的時間（秒）
    public static float CumPreUpPx = 80f;                    // 射精前三次衝撞，每次頂進去時雞雞再往上多的量（畫布像素）
    public static float CumPre3DepthX = 160f;                 // 第三次（先射一點點）頂到的位置，比較深一點
    public static float CumPreInTime = 0.35f, CumPreOutTime = 0.4f, CumPreRetreatTime = 0.55f;   // 頂進去／退出來／射完一點點之後後退的時間（秒）
    public static int CumTinyCount = 3;                      // 第三次先射幾顆
    public static float CumTinyMul = 0.4f;                  // 第三次那一點點的大小倍率
    public static float CumTinyReach = 0.6f;                // 第三次那一點點射多遠（1＝跟正式的一樣遠，越小越近）

    async Task MoveMarker(float to, float time)
    {
        float a = _markerX;
        for (float s = 0; s < 1f && Alive; s += Dt / Mathf.Max(time, 0.01f)) { _markerX = Mathf.Lerp(a, to, Mathf.SmoothStep(0f, 1f, s)); await Frame(); }
        _markerX = to;
    }

    /// 正式射精前：先 2 次小力衝撞，第三次頂深一點並先射一點點，接著後退，然後才是原本那一下（頂到最深＋射出）
    async Task PreThrusts()
    {
        _osc = false; _preUp = 0f;
        await MoveMarker(CumPreBaseX, CumPreOutTime);
        for (int k = 0; k < CumPreLight && Alive; k++)
        {
            _preUp = CumPreUp12Px; await MoveMarker(CumPreDepthX, CumPreInTime);
            if (!Alive) { _preUp = 0f; return; }
            _preUp = 0f; await MoveMarker(CumPreBaseX, CumPreOutTime);
        }
        if (!Alive) return;
        _preUp = CumPreUpPx; await MoveMarker(CumPre3DepthX, CumPreInTime);
        if (!Alive) { _preUp = 0f; return; }
        await PlayCum(true);
        _preUp = 0f;
        if (!Alive) return;
        await MoveMarker(CumPreBaseX, CumPreRetreatTime);
    }

    async Task PlayCum(bool tiny = false)
    {
        if (!tiny) await PreThrusts();
        if (!Alive) return;
        _uterusTex ??= ResourceLoader.Load<Texture2D>("res://Resources/MG/mg_uterus_inner.png");   // 子宮內圈的範圍
        if (_cumRect != null && IsInstanceValid(_cumRect)) _cumRect.QueueFree();
        var poly = new Polygon2D { Name = "cum", Texture = _uterusPoly.Texture, Polygons = _uterusPoly.Polygons, UV = _uterusPoly.UV };
        _cumRect = poly;
        var mat = new ShaderMaterial { Shader = new Shader { Code = CumShader } };
        poly.Material = mat;
        _stage.AddChild(poly); _stage.MoveChild(poly, _imgFrame.GetIndex());   // 子宮上面、外框下面
        SetFClip(mat);
        if (_dropPoly != null && IsInstanceValid(_dropPoly)) _dropPoly.QueueFree();
        _dropPoly = null; _dropIn = 0f;
        if (CumDrop2On && !tiny)
        {   // 子宮底部（九宮格由下數第二排正中間，雞雞上面凹進去的地方）一小灘會晃的水；圖層在雞雞下面
            _dropMat = new ShaderMaterial { Shader = new Shader { Code = DropShader } };
            _dropPoly = new Polygon2D { Name = "cumdrop", Texture = _uterusPoly.Texture, Polygons = _uterusPoly.Polygons, UV = _uterusPoly.UV, Material = _dropMat };
            _stage.AddChild(_dropPoly); _stage.MoveChild(_dropPoly, _uterusPoly.GetIndex() + 1);   // 掛在子宮上面：跟子宮同一個網格，一起變形、移動
            SetFClip(_dropMat);
        }
        _osc = false;
        float m0 = _markerX;
        for (float t = 0; t < 1f && Alive && !tiny; t += Dt / CumPushTime) { _markerX = Mathf.Lerp(m0, CumMarkerX, t * t * (3f - 2f * t)); await Frame(); }
        int n = tiny ? Mathf.Clamp(CumTinyCount, 1, 8) : Mathf.Clamp(CumSpurts, 1, 8);
        var side = new float[n]; var jit = new float[n]; var sz = new float[n];
        for (int i = 0; i < n; i++)
        {
            side[i] = i % 2 == 0 ? -1f : 1f;
            jit[i] = ((float)_rng.NextDouble() - 0.5f) * 2f;
            sz[i] = (i == 0 ? CumSizeFirst : Mathf.Lerp(CumSizeSecond, CumSizeLast, n > 2 ? (i - 1) / (float)(n - 2) : 0f)) * (0.92f + (float)_rng.NextDouble() * 0.16f);   // 第一顆最大，第二顆起明顯小一點
        }
        if (tiny) for (int i = 0; i < n; i++) sz[i] *= CumTinyMul;
        float one = CumRiseTime + CumSlideTime;
        var t0 = new float[n];
        for (int i = 0; i < n; i++) t0[i] = (i / Mathf.Max(CumWaveSize, 1)) * CumWaveGap + (i % Mathf.Max(CumWaveSize, 1)) * CumInWaveGap;
        float dur = t0[n - 1] + one + 0.3f;
        float firstLand0 = CumRiseTime * (1f + (CumTroomStyle ? CumTailDelay : 0f));
        dur = Mathf.Max(dur, Mathf.Max(firstLand0 + CumJijiBlobTime * 0.8f + CumJijiStrokeTime, firstLand0 + CumWhiteFlowTime) + 0.2f);   // 等白色流完才結束
        if (tiny) dur = t0[n - 1] + CumRiseTime + CumTinyHold;   // 只射一點點：很快就結束
        var tl = new Vector4[8]; var hd = new Vector4[8];
        var A = CumAreaPx; var C = A.GetCenter();
        _cumWf = 0f; _cumJf = 0f; _cumJb = 0f;
        // 第一波統一射向九宮格最高的那個頂點（左右限制在內圈裡），後面幾波才散開
        Vector2 firstP;
        {
            var bxx = UterusBoxPx; float gr0 = Mathf.DegToRad(UterusGridRotDeg); var ctr0 = bxx.Size / 2f; var best0 = new Vector2(ctr0.X, ctr0.Y);
            float bestY = 1e9f;
            for (int rr0 = 0; rr0 < 4; rr0++) for (int cc0 = 0; cc0 < 4; cc0++)
            {
                var L0 = new Vector2(cc0 / 3f * bxx.Size.X, rr0 / 3f * bxx.Size.Y);
                var q0 = bxx.Position + ctr0 + (L0 - ctr0).Rotated(-gr0);
                if (q0.Y < bestY) { bestY = q0.Y; best0 = q0; }
            }
            firstP = CumFirstTargetPx != Vector2.Zero ? CumFirstTargetPx : new Vector2(Mathf.Clamp(best0.X, A.GetCenter().X - A.Size.X * 0.5f * CumWallScale * 0.75f, A.GetCenter().X + A.Size.X * 0.5f * CumWallScale * 0.75f), A.Position.Y + CumPeakFromTopPx);
        }
        firstP += CumFirstOffsetPx;   // 整體微調（預設往左 100）
        var mist = new Vector4[6];
        for (int k = 0; k < 6; k++)   // 子宮的模糊白斑：隨機位置、大小、濃度；前 3 個落在上半部，其餘在底部
            mist[k] = new Vector4(Mathf.Lerp(A.Position.X + A.Size.X * 0.18f, A.End.X - A.Size.X * 0.18f, (float)_rng.NextDouble()),
                                  k < 3 ? Mathf.Lerp(A.Position.Y + A.Size.Y * 0.08f, A.Position.Y + A.Size.Y * 0.42f, (float)_rng.NextDouble())
                                        : Mathf.Lerp(A.End.Y - A.Size.Y * 0.38f, A.End.Y - A.Size.Y * 0.05f, (float)_rng.NextDouble()),
                                  Rf2(CumMistPx * 0.6f, CumMistPx * 1.3f), Rf2(CumMistAlpha * 0.5f, CumMistAlpha));
        float rx = A.Size.X / 2f * CumWallScale, ry = A.Size.Y / 2f * CumWallScale;
        float tilt = Mathf.DegToRad(CumTiltDeg);
        Vector2 Ell(float th) => C + new Vector2(Mathf.Cos(th) * rx, Mathf.Sin(th) * ry);
        for (float el = 0; el < dur && Alive; el += Dt)
        {
            var up = _uterusPoly.Position; var sc = _uterusPoly.Scale; var bx = UterusBoxPx;
            poly.Polygon = _uterusPoly.Polygon; poly.Position = up; poly.Scale = sc;   // 跟子宮同一個變形網格
            // 雞雞前端（畫面座標）換回子宮座標（反推變形）
            var scr = _tipScr + CumTipOffsetPx * StageScale;
            var startC = (scr - up) / sc + bx.Position;
            for (int k = 0; k < 3; k++) startC = (scr - up) / sc + bx.Position - UterusDisp(startC);
            float yPeak = A.Position.Y + CumPeakFromTopPx;
            float yBot = A.End.Y - CumRestUpPx;
            float pool = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = el - t0[i];
                if (t < 0f) { tl[i] = Vector4.Zero; hd[i] = Vector4.Zero; continue; }
                float tr = Mathf.Clamp(t / CumRiseTime, 0f, 1f);
                float trU = t / CumRiseTime;                                   // 沒夾住的進度（調教房模式：頭落地後尾巴還要追上來）
                float riseEnd = CumTroomStyle ? 1f + CumTailDelay : 1f;
                float ts = Mathf.Clamp((t - CumRiseTime * riseEnd) / CumSlideTime, 0f, 1f);
                // 歪歪的細長液柱：沿著九宮格那個傾斜方向往上射（左上），落點在內圈上緣
                float xHit = startC.X + (yPeak - startC.Y) * Mathf.Tan(tilt) + jit[i] * CumShootSpreadPx;
                var P = i < Mathf.Max(CumWaveSize, 1) ? new Vector2(firstP.X + jit[i] * CumFirstSpreadPx, firstP.Y) : new Vector2(Mathf.Clamp(xHit, C.X - rx * 0.75f, C.X + rx * 0.75f), yPeak);
                if (tiny) P = startC.Lerp(P, CumTinyReach);
                float rc = CumBlobPx * sz[i];
                Vector2 head, tail, mid; float rad;
                if (trU < riseEnd || ts <= 0f)
                {
                    float e = 1f - (1f - tr) * (1f - tr);                  // 照調教房：直線＋ease-out，出發最快
                    head = startC.Lerp(P, e);
                    tail = startC.Lerp(P, Mathf.Max(0f, e - CumJetLenPx / Mathf.Max(startC.DistanceTo(P), 1f)));   // 拖著細長的尾巴
                    if (CumTroomStyle)
                    {
                        // 調教房：一串圓從起點依序出發（頭先、尾後），直線 ease-out；頭落地後以 40% 大小形成一團，後面的段抵達「吸收」讓它長大
                        float et = 1f - (1f - Mathf.Clamp(trU - CumTailDelay, 0f, 1f)) * (1f - Mathf.Clamp(trU - CumTailDelay, 0f, 1f));
                        tail = startC.Lerp(P, et); mid = (head + tail) / 2f;
                        rad = trU < 1f ? CumHeadPx * sz[i] : Mathf.Lerp(rc * 0.4f, rc, Mathf.Clamp((trU - 1f) / Mathf.Max(CumTailDelay, 0.01f), 0f, 1f));
                        float land0 = Mathf.Clamp((ts - 0.85f) / 0.15f, 0f, 1f); pool += CumPoolPx * land0;
                        tl[i] = new Vector4(tail.X, tail.Y, mid.X, mid.Y); hd[i] = new Vector4(head.X, head.Y, rad, CumTailPx);
                        continue;
                    }
                    var dv = head - tail; var perp = dv.Length() > 1f ? new Vector2(-dv.Y, dv.X).Normalized() : Vector2.Zero;
                    mid = (head + tail) / 2f + perp * CumCurvePx * side[i] * (1f - tr * 0.5f) + new Vector2(0f, CumSagPx) * (1f - tr);   // 射出時身體彎曲、中段被重力往下拉（像拋出去的液體）
                    rad = CumHeadPx * sz[i];
                }
                else
                {
                    // 溜滑梯：從撞到的位置沿著內圈邊往下滑，越滑越快
                    float th0 = Mathf.Atan2((P.Y - C.Y) / ry, (P.X - C.X) / rx);
                    float thEnd = side[i] > 0f ? Mathf.Pi / 2f : -3f * Mathf.Pi / 2f;
                    if (side[i] > 0f && th0 > thEnd) th0 -= Mathf.Tau;
                    if (side[i] < 0f && th0 < thEnd) th0 += Mathf.Tau;
                    float pr = Mathf.Pow(ts, 1.6f);
                    float prT = Mathf.Max(0f, pr - CumTrail);
                    var off = (P - Ell(th0)) * (1f - pr);
                    head = Ell(Mathf.Lerp(th0, thEnd, pr)) + off;
                    tail = Ell(Mathf.Lerp(th0, thEnd, prT)) + (P - Ell(th0)) * (1f - prT);
                    float prM = (pr + prT) / 2f;
                    mid = Ell(Mathf.Lerp(th0, thEnd, prM)) + (P - Ell(th0)) * (1f - prM);   // 身體沿著滑的軌跡彎，跟著頭
                    rad = CumTroomStyle ? rc : CumHeadPx * sz[i];
                }
                float land = Mathf.Clamp((ts - 0.85f) / 0.15f, 0f, 1f);   // 滑到底就融進液面
                pool += CumPoolPx * land;
                tl[i] = new Vector4(tail.X, tail.Y, mid.X, mid.Y); hd[i] = new Vector4(head.X, head.Y, rad * (1f - land), CumTailPx * (1f - land));
            }
            for (int i = n; i < 8; i++) { tl[i] = Vector4.Zero; hd[i] = Vector4.Zero; }
            float firstLand = CumRiseTime * (1f + (CumTroomStyle ? CumTailDelay : 0f));   // 第一波落地的時間
            float mk = Mathf.Clamp((el - t0[Mathf.Max(CumWaveSize, 1) - 1 < n ? Mathf.Max(CumWaveSize, 1) - 1 : 0] - firstLand) / 0.8f, 0f, 1f);
            for (int k = 0; k < 6; k++) mat.SetShaderParameter("ms" + k, new Vector4(mist[k].X, mist[k].Y, mist[k].Z, mist[k].W * mk * (tiny ? 0f : 1f)));
            mat.SetShaderParameter("haze", Mathf.Clamp((el - t0[0] - firstLand) / 2.5f, 0f, 1f));
            mat.SetShaderParameter("haze_a", CumHazeAlpha); mat.SetShaderParameter("haze_top", CumHazeTop);
            mat.SetShaderParameter("pool_a", CumPoolAlpha);
            mat.SetShaderParameter("haze_y", new Vector2(A.Position.Y + A.Size.Y * 0.25f, A.End.Y));
            _cumJb = Mathf.Clamp((el - t0[0] - firstLand) / CumJijiBlobTime, 0f, 1f);
            _dropIn = Mathf.Clamp((el - t0[0] - firstLand - 0.3f) / 1.2f, 0f, 1f);
            _cumJf = Mathf.Clamp((el - t0[0] - firstLand - CumJijiBlobTime * 0.8f) / CumJijiStrokeTime, 0f, 1f);
            _cumWf = Mathf.Clamp((el - t0[0] - firstLand) / CumWhiteFlowTime, 0f, 1f);
            if (tiny) { _cumJb = _cumJf = _cumWf = 0f; _dropIn = 0f; mat.SetShaderParameter("haze", 0f); }   // 先射一點點：不出現雞雞上的白色流水、也沒有白底
            for (int i = 0; i < 8; i++) { mat.SetShaderParameter("tl" + i, tl[i]); mat.SetShaderParameter("hd" + i, hd[i]); }
            mat.SetShaderParameter("bead_k", CumBeadScale); mat.SetShaderParameter("bead_a", CumBeadAlpha); mat.SetShaderParameter("wob", CumTroomStyle ? 0f : CumWobble); mat.SetShaderParameter("tro", CumTroomStyle ? 1f : 0f); mat.SetShaderParameter("col", CumTroomStyle ? new Color(0.96f, 0.94f, 0.89f) : new Color(0.98f, 0.97f, 0.94f)); mat.SetShaderParameter("ol_w", CumOutlinePx); mat.SetShaderParameter("ctr_a", CumTroomStyle ? 1f : CumHeadCenterAlpha);
            {   // 液面晃動改在 _Process 每幀更新（射完之後也會繼續動）；這裡只送「液面變化」造成的波（落下去、水位上升）
                float dts2 = Mathf.Max(Dt, 0.0001f);
                if (el < 0.05f) { _slReset = true; _lastPool = 0f; }
                _slEImp = Mathf.Max(_slEImp, Mathf.Clamp(Mathf.Abs(pool - _lastPool) / dts2 / 120f, 0f, 1f)); _lastPool = pool;
                _cumMat = mat; _cumA = A;
            }
            mat.SetShaderParameter("pool_top", pool > 0.5f ? yBot - pool : 100000f);
            mat.SetShaderParameter("amt", CumAlpha * (tiny ? Mathf.Clamp((dur - el) / 0.35f, 0f, 1f) : 1f));
            mat.SetShaderParameter("ut_tex", _uterusTex);
            mat.SetShaderParameter("canvas_px", CanvasPx);
            mat.SetShaderParameter("ut_on", CumClipUterus ? 1f : 0f);
            await Frame();
        }
        if (tiny) { if (IsInstanceValid(poly)) poly.QueueFree(); _cumRect = null; }   // 那一點點淡掉後就清掉，之後正式射精才重新開始
        // 液面留在子宮裡不消失（遊戲結束離開時才一起清掉）；再按測試按鈕會先清掉上一批
    }

    Vector2 View => GetViewport().GetVisibleRect().Size;

    TextureRect MakeLayer(string name)
    {
        var tr = new TextureRect
        {
            Name = name, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = Control.MouseFilterEnum.Ignore,
            Texture = ResourceLoader.Load<Texture2D>("res://Resources/MG/" + name + ".png"),
            Size = CanvasPx * StageScale, Position = Vector2.Zero,
        };
        _stage.AddChild(tr);
        return tr;
    }

    Label Lbl(Control p, string t, int fs, Color col, float x, float top, float w, float h,
              HorizontalAlignment ha = HorizontalAlignment.Center)
    {
        var l = new Label { Text = t, HorizontalAlignment = ha, VerticalAlignment = VerticalAlignment.Center,
                            MouseFilter = Control.MouseFilterEnum.Ignore, Position = new Vector2(x, top), Size = new Vector2(w, h),
                            AutowrapMode = TextServer.AutowrapMode.WordSmart };
        l.AddThemeFontSizeOverride("font_size", fs);
        l.AddThemeColorOverride("font_color", col);
        p.AddChild(l);
        return l;
    }

    ColorRect Box(Control p, Color c, float x, float y, float w, float h)
    {
        var r = new ColorRect { Color = c, Position = new Vector2(x, y), Size = new Vector2(w, h), MouseFilter = Control.MouseFilterEnum.Ignore };
        p.AddChild(r);
        return r;
    }

    void BuildUI()
    {
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        AddChild(_root);
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var bg = new ColorRect { Color = BgColor, MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.AddChild(bg); bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var bgTex = ResourceLoader.Load<Texture2D>("res://Resources/MG/mg_bg.png");   // 背景圖（可換檔）
        if (bgTex != null && BgUseImage)
        {
            var bgImg = new TextureRect { Texture = bgTex, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, MouseFilter = Control.MouseFilterEnum.Ignore };
            _bgImg = bgImg;
            _root.AddChild(bgImg); bgImg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            bgImg.OffsetLeft = bgImg.OffsetRight = BgOffsetPx.X; bgImg.OffsetTop = bgImg.OffsetBottom = BgOffsetPx.Y;
        }

        // 舞台：貼齊畫面最左邊、垂直置中；四層同原點
        var stageSize = CanvasPx * StageScale;
        _stage = new Control { Name = "Stage", MouseFilter = Control.MouseFilterEnum.Ignore, ClipContents = false,
                               Size = stageSize, Position = new Vector2(0f, (View.Y - stageSize.Y) / 2f) };
        _root.AddChild(_stage);
        _imgBottom2 = MakeLayer("mg_bottom");   // 底下面再一層一樣的底：左右會隨雞雞抵達的位置擴張，同時當遮色片
        _bottom2Mat = new ShaderMaterial { Shader = new Shader { Code = BottomExpandShader } };
        _imgBottom2.Material = _bottom2Mat;
        _imgBottom = MakeLayer("mg_bottom");
        _bottomMat = new ShaderMaterial { Shader = new Shader { Code = BottomExpandShader } };
        _imgBottom.Material = _bottomMat;
        BuildUterusMesh();
        BuildJijiMesh();
        if (UterusOnTop) _stage.MoveChild(_uterusPoly, _jijiPoly.GetIndex());   // 子宮畫在雞雞上面
        var sh = new Shader { Code = MaskShader };
        _maskJiji = new ShaderMaterial { Shader = sh };
        _maskUterus = new ShaderMaterial { Shader = sh };
        _jijiPoly.Material = _maskJiji; _uterusPoly.Material = _maskUterus;
        _pivotDot = new ColorRect { Color = new Color(1f, 0.1f, 0.1f, 0.95f), Size = new Vector2(14, 14), MouseFilter = Control.MouseFilterEnum.Ignore };
        _jijiPoly.AddChild(_pivotDot);
        _imgFrame  = MakeLayer("mg_frame");
        BuildUterusGrid();

        _flash = new ColorRect { Color = new Color(1, 1, 1, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.AddChild(_flash); _flash.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        // 面板：只留進度條區（標題 / 回合 / 高潮值 / 高潮次數 / 位置軌道），圓角粉色風格
        _panel = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.AddChild(_panel);
        _panel.Size = new Vector2(PanelW, PanelH);
        _panel.Position = new Vector2(PanelX, PanelY);
        _panel.Scale = new Vector2(UiScale, UiScale);
        RoundBox(_panel, 0, 0, PanelW, PanelH, new Color(1f, 0.96f, 0.98f, 0.95f), new Color(0.93f, 0.55f, 0.72f), 36);

        var txt  = new Color(0.55f, 0.20f, 0.38f);
        var pink = new Color(0.93f, 0.40f, 0.65f);
        Lbl(_panel, MgText.Title, 24, txt, 0, 14, PanelW, 36);
        _round = Lbl(_panel, "", 17, new Color(0.65f, 0.35f, 0.50f), 0, 50, PanelW, 28);
        Lbl(_panel, "高潮值", 17, txt, 24, 84, 100, 26, HorizontalAlignment.Left);
        _pct = Lbl(_panel, "0/100", 17, txt, PanelW - 124, 84, 100, 26, HorizontalAlignment.Right);
        RoundBox(_panel, 24, 112, 352, 22, new Color(0.96f, 0.86f, 0.91f), new Color(0.93f, 0.70f, 0.82f), 11, 2);
        _fill = RoundBox(_panel, 26, 114, 0, 18, pink, pink, 9, 0);
        _org = Lbl(_panel, "", 19, pink, 0, 142, PanelW, 28);

        // 軌道（邏輯 600 → 顯示 352）：子宮口8% | 遠端36% | G點10% | 近端13% | 淺部33%
        _track = Box(_panel, new Color(0.97f, 0.90f, 0.94f), 24, 190, 352, 20);
        Zone(0f, 0.08f, new Color(0.90f, 0.40f, 0.62f));
        Zone(0.08f, 0.44f, new Color(0.98f, 0.90f, 0.94f));
        Zone(0.44f, 0.54f, new Color(0.95f, 0.55f, 0.75f));
        Zone(0.54f, 0.67f, new Color(0.97f, 0.78f, 0.87f));
        Zone(0.67f, 1f, new Color(0.98f, 0.90f, 0.94f));
        _marker = new ColorRect { Color = new Color(1f, 0.90f, 0.40f), Size = new Vector2(22, 26), MouseFilter = Control.MouseFilterEnum.Ignore };
        _track.AddChild(_marker);

        // 下方對話條：只在顯示反應台詞 / 得分時出現（底圖由 RoundBox 繪製）
        _dlgBg = RoundBox(_panel, 0, PanelH + 12, PanelW, 230, new Color(1f, 0.96f, 0.98f, 0.95f), new Color(0.93f, 0.55f, 0.72f), 30);
        _dlgBg.Visible = false;
        _gain  = Lbl(_panel, "", 20, txt, 20, PanelH + 20, 360, 34);
        _spk   = Lbl(_panel, MgText.Speaker, 18, pink, 20, PanelH + 56, 360, 26, HorizontalAlignment.Left);
        _react = Lbl(_panel, "", 19, txt, 20, PanelH + 84, 360, 120, HorizontalAlignment.Left);
        _react.VerticalAlignment = VerticalAlignment.Top;
        _cont  = Lbl(_panel, "— 點擊 / 空白鍵 繼續 —", 14, new Color(0.65f, 0.35f, 0.50f, 0.8f), 0, PanelH + 206, PanelW, 24);
        // 已移除的舊欄位（保留不顯示，避免其他程式碼寫入時出錯）
        _posLbl = new Label { Visible = false }; _hint = new Label { Visible = false };
        _panel.AddChild(_posLbl); _panel.AddChild(_hint);
        _spk.Visible = _dlgBg.Visible = false; _cont.Visible = false;

        // 氣泡選項（取代三顆按鈕）：位置/大小見 BubblePos / BubbleSize（畫面像素）
        string[] labels = { "小力摩擦", "小幅用力", "用力插" };
        for (int i = 0; i < 3; i++)
        {
            var b = MakeBubble(labels[i]);
            _root.AddChild(b);
            b.Position = BubblePos[i]; b.Size = BubbleSize; b.Scale = new Vector2(UiScale, UiScale);
            int idx = i; b.Pressed += () => _chosen = idx;
            _btns[i] = b;
        }
        if (CumTestButton)   // 測試用：隨時播射精動畫（做完特效後把 CumTestButton 設 false）
        {
            var tb = MakeBubble("射精（測試）");
            _root.AddChild(tb);
            tb.Position = BubblePos[2] + CumTestOffsetPx; tb.Size = BubbleSize; tb.Scale = new Vector2(UiScale, UiScale);
            tb.Pressed += async () =>
            {
                if (_cumBusy) return;
                _cumBusy = true; bool wasOsc = _osc;
                await PlayCum();
                _osc = wasOsc; _cumBusy = false;
            };
        }
        _panel.Modulate = new Color(1, 1, 1, 0);
    }
    bool _cumBusy;

    /// 圓角方塊（border=0 不畫邊）；回傳 Panel 供調大小
    Panel RoundBox(Control p, float x, float y, float w, float h, Color fill, Color border, int radius, int bw = 3)
    {
        var pn = new Panel { Position = new Vector2(x, y), Size = new Vector2(w, h), MouseFilter = Control.MouseFilterEnum.Ignore };
        pn.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = fill, BorderColor = border,
            BorderWidthLeft = bw, BorderWidthRight = bw, BorderWidthTop = bw, BorderWidthBottom = bw,
            CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
            ShadowColor = new Color(0.9f, 0.4f, 0.6f, bw > 0 ? 0.25f : 0f), ShadowSize = bw > 0 ? 8 : 0,
        });
        p.AddChild(pn);
        return pn;
    }
    Panel _dlgBg;

    public static Vector2[] BubblePos = { new(1010f, 590f), new(1090f, 700f), new(1010f, 810f) };   // 氣泡左上角（畫面像素）
    public static Vector2 BubbleSize = new(260f, 80f);
    public static int BubbleFont = 24;

    Button MakeBubble(string text)
    {
        StyleBoxFlat Sb(Color fill, Color border) => new StyleBoxFlat
        {
            BgColor = fill, BorderColor = border,
            BorderWidthLeft = 4, BorderWidthRight = 4, BorderWidthTop = 4, BorderWidthBottom = 4,
            CornerRadiusTopLeft = 40, CornerRadiusTopRight = 40, CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 40,
            ShadowColor = new Color(0.9f, 0.4f, 0.6f, 0.30f), ShadowSize = 10, ShadowOffset = new Vector2(0, 4),
        };
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        b.AddThemeStyleboxOverride("normal",  Sb(new Color(1f, 0.97f, 0.98f, 0.96f), new Color(0.93f, 0.55f, 0.72f)));
        b.AddThemeStyleboxOverride("hover",   Sb(new Color(1f, 0.86f, 0.92f, 1f),    new Color(0.85f, 0.30f, 0.55f)));
        b.AddThemeStyleboxOverride("pressed", Sb(new Color(0.96f, 0.72f, 0.84f, 1f), new Color(0.75f, 0.2f, 0.45f)));
        b.AddThemeColorOverride("font_color", new Color(0.45f, 0.18f, 0.32f));
        b.AddThemeColorOverride("font_hover_color", new Color(0.6f, 0.1f, 0.3f));
        b.AddThemeFontSizeOverride("font_size", BubbleFont);
        // 氣泡尾巴（左下角）
        var tail = new Polygon2D { Color = new Color(0.93f, 0.55f, 0.72f) };
        b.AddChild(tail);
        tail.Polygon = new[] { new Vector2(50, BubbleSize.Y - 4), new Vector2(14, BubbleSize.Y - 4), new Vector2(-6, BubbleSize.Y + 26) };
        var tailIn = new Polygon2D { Color = new Color(1f, 0.97f, 0.98f) };
        b.AddChild(tailIn);
        tailIn.Polygon = new[] { new Vector2(44, BubbleSize.Y - 6), new Vector2(18, BubbleSize.Y - 6), new Vector2(2, BubbleSize.Y + 18) };
        return b;
    }

    void Zone(float a, float b, Color c)
        => Box(_track, c, a * 352f, 0, (b - a) * 352f, 20);

    // ══════════════════════════════════════════════════════════
    //  同步圖層（標記 X → 雞雞上下 / 旋轉）。對齊位置 = G點深度，往子宮方向向上移
    // ══════════════════════════════════════════════════════════
    void SyncJiji()
    {
        _marker.Position = new Vector2(_markerX * TK - 11f, -3f);
        float S = StageScale;
        _imgBottom.Position = (StageOffsetPx + BottomFinePx) * S;
        foreach (var bm in new[] { _bottomMat, _bottom2Mat }) { bm.SetShaderParameter("tint", BottomColor); bm.SetShaderParameter("on", BottomPink ? 1f : 0f); }
        _imgBottom.Size = new Vector2(CanvasPx.X * S, CanvasPx.Y * S + BottomStretchPx);
        _imgBottom2.Position = _imgBottom.Position; _imgBottom2.Size = _imgBottom.Size;
        _imgFrame.Position  = (StageOffsetPx + GroupBOffsetPx + FrameFinePx) * S - (FrameScale - 1f) * FrameCenterPx * S;
        _imgFrame.Size = CanvasPx * S * FrameScale + new Vector2(0f, FrameStretchPx);
        float nxRaw = Mathf.Clamp(_markerX / MarkMax, 0f, 1f);
        if (_nxS < 0f) _nxS = nxRaw;
        _nxS = Mathf.Lerp(_nxS, nxRaw, 1f - Mathf.Exp(-DefSmooth * Dt));   // 平滑：位置/變形慢慢跟上，不會突然跳
        float nx = _nxS;
        float jUp = (_cfg.JijiY_Uterus - _cfg.JijiY_G) * JijiMoveScale + JijiUpExtraPx;   // 雞雞前端最大上移量（畫布像素）
        _preUpS = Mathf.Lerp(_preUpS, _preUp, 1f - Mathf.Exp(-14f * Dt));
        float nxr = Mathf.Clamp(nx - _preUpS / Mathf.Max(jUp, 1f) * CumPreRotGain, 0f, 1f);   // 射精前的衝撞：往上多頂的量也算進旋轉，轉得跟最後那一下一樣
        float sr = Mathf.Lerp(_cfg.JijiRot_Uterus, _cfg.JijiRot_G, nxr);
        float jit = _pushJit * PushJitter * (Mathf.Sin(Time.GetTicksMsec() * 0.045f) * 0.6f + Mathf.Sin(Time.GetTicksMsec() * 0.113f) * 0.4f);   // 頂到底時的輕微抖動
        _thrustPx = Mathf.Lerp(_thrustPx, (_thrustPhase == 1 ? ThrustPx : _thrustPhase == 2 ? -ThrustLastPx : 0f) + _preUp, 1f - Mathf.Exp(-14f * Dt));
        float dj = Mathf.Clamp(1f - nx + _pushExtra * PushRiseExtra + jit + _thrustPx / Mathf.Max(jUp, 1f), 0f, 1.3f);   // 雞雞與子宮用同一個值：待機、選完掃動都是標記位置驅動
        _tipUvY = 0f;
        _jiDisp = new Vector2(-JijiLeftCompPx, -jUp) * dj;   // 雞雞這一層現在整個移動了多少（原點要扣掉，才會停在子宮上不跟著雞雞跑）
        // 子宮變形：雞雞前端碰到九宮格第 UterusStartRow 排錨點（由上數，0=最頂；預設 2＝由下數第二排）才開始，
        // 再往上走 UterusRampRows 排的距離變形量才到最大
        float tipY = StageOffsetPx.Y + JijiFinePx.Y - _cfg.JijiOffsetY + JijiTipPx - jUp * dj;
        var ub = UterusBoxPx; float uk = UterusScale;
        _imgBottom.Position = _imgBottom2.Position = (StageOffsetPx + BottomFinePx) * S + new Vector2(0f, -dj * jUp * S * BottomFollow);   // 看得到的底與遮色片的底一起跟著
        _tipUvY = (tipY - (StageOffsetPx.Y + BottomFinePx.Y)) / (CanvasPx.Y + BottomStretchPx / S);
        float rowSp = ub.Size.Y * uk / 3f;
        float rowY0 = StageOffsetPx.Y + GroupBOffsetPx.Y + UterusFinePx.Y + ub.Position.Y + ub.Size.Y * (1f - uk) / 2f;
        float d = Mathf.Clamp((rowY0 + UterusStartRow * rowSp - tipY) / (rowSp * UterusRampRows), 0f, 1f);
        d *= Mathf.Lerp(1f, CumPreDefMul, Mathf.Clamp(_preUpS / Mathf.Max(Mathf.Max(CumPreUp12Px, CumPreUpPx), 1f), 0f, 1f));   // 射精前的小力衝撞：子宮變形縮小
        _upFollow = dj * jUp * BottomFollow * UterusFollowMul;
        UpdateUterus(d, d);
        var gf = new Vector2(0f, -UterusDeformPx).Rotated(-Mathf.DegToRad(UterusGridRotDeg)) * (d * BottomGridFollow * S);   // 九宮格變形的位移方向
        _imgBottom.Position += gf; _imgBottom2.Position += gf;
        _expK = dj;   // 擴張強度：雞雞越往上越寬
        _jRotDeg = JijiRotSign * sr * JijiRotMul + JijiStartRotDeg * nxr;
        UpdateJiji(dj, jUp, _jRotDeg);
        {   // 雞雞前端（畫布 782,79）在 _stage 內的位置：變形後再繞轉軸旋轉
            var jb = JijiBoxPx; var p0 = new Vector2(782f, 79f) - jb.Position + new Vector2(-JijiLeftCompPx, -jUp) * dj;
            var pv = JijiPivotPx - jb.Position;
            p0 = pv + (p0 - pv).Rotated(Mathf.DegToRad(_jRotDeg));
            _tipScr = (StageOffsetPx + JijiFinePx + new Vector2(_cfg.JijiOffX, -_cfg.JijiOffsetY) + jb.Position + p0) * S;
        }
        if (_bgImg != null)   // 背景跟著往上一點（只跟上下）
        {
            float by = BgOffsetPx.Y - Mathf.Min(d, 1f) * jUp * S * BgFollow;
            _bgImg.OffsetTop = by; _bgImg.OffsetBottom = by;
        }
        if (_cumRect is Polygon2D cp && IsInstanceValid(cp))   // 精液跟著子宮的變形／收縮（射完以後也一直同步）
        {
            cp.Polygon = _uterusPoly.Polygon; cp.Position = _uterusPoly.Position; cp.Scale = _uterusPoly.Scale;
        }
        if (_dropPoly != null && IsInstanceValid(_dropPoly) && _dropMat != null)
        {
            _dropPoly.Polygon = _uterusPoly.Polygon; _dropPoly.Position = _uterusPoly.Position; _dropPoly.Scale = _uterusPoly.Scale;
            _dropMat.SetShaderParameter("canvas_px", CanvasPx);
            {   // 九宮格正中間（子宮框的中心）；網格一起動，所以不用再算上抬
                var ub3 = UterusBoxPx;
                _dropMat.SetShaderParameter("d_c", ub3.Position + ub3.Size / 2f + CumDrop2OffsetPx);
            }
            _dropMat.SetShaderParameter("d_r", CumDrop2RadiusPx);
            _dropMat.SetShaderParameter("d_amp", Mathf.Lerp(0.9f, 1.8f, _slE)); _dropMat.SetShaderParameter("d_rot", Mathf.DegToRad(CumDrop2RotDeg));   // 水面的波（動作越大越大）   // 動作越大晃越大，平常也輕輕晃
            _dropMat.SetShaderParameter("d_sl", Mathf.Clamp(_slX * 3f, -0.3f, 0.3f));
            _dropMat.SetShaderParameter("d_dbg", CumDrop2Blue ? 1f : 0f); _dropMat.SetShaderParameter("d_a", CumDrop2Alpha); _dropMat.SetShaderParameter("d_in", _dropIn);
        }
        UpdateMask();
    }

    void BuildUterusMesh()
    {
        _uterusPoly = new Polygon2D { Name = "mg_uterus", Texture = ResourceLoader.Load<Texture2D>("res://Resources/MG/mg_uterus.png") };
        _stage.AddChild(_uterusPoly);
        var polys = new Godot.Collections.Array();
        for (int j = 0; j < GN; j++)
            for (int i = 0; i < GN; i++)
            {
                int k = j * (GN + 1) + i;
                polys.Add(new int[] { k, k + 1, k + GN + 2, k + GN + 1 });
            }
        _uterusPoly.Polygons = polys;
        UpdateUterus(0f, 0f);
    }

    void BuildUterusGrid()
    {
        for (int i = 0; i < 8; i++)
        {
            _jGridLines[i] = new Line2D { Width = 2f, DefaultColor = new Color(1f, 0.4f, 0.6f, 0.9f) };
            _stage.AddChild(_jGridLines[i]);
        }
        for (int i = 0; i < 16; i++)
        {
            _jGridDots[i] = new ColorRect { Size = new Vector2(10, 10), Color = new Color(0.4f, 0.8f, 1f), MouseFilter = Control.MouseFilterEnum.Ignore };
            _stage.AddChild(_jGridDots[i]);
        }
        for (int i = 0; i < 8; i++)
        {
            _gridLines[i] = new Line2D { Width = 2f, DefaultColor = new Color(0.2f, 1f, 0.5f, 0.9f) };
            _stage.AddChild(_gridLines[i]);
        }
        for (int i = 0; i < 16; i++)
        {
            _gridDots[i] = new ColorRect { Size = new Vector2(10, 10), Color = new Color(1f, 0.9f, 0.2f), MouseFilter = Control.MouseFilterEnum.Ignore };
            _stage.AddChild(_gridDots[i]);
        }
    }

    void BuildJijiMesh()
    {
        _jijiPoly = new Polygon2D { Name = "mg_jiji", Texture = ResourceLoader.Load<Texture2D>("res://Resources/MG/mg_jiji.png") };
        _stage.AddChild(_jijiPoly);
        var polys = new Godot.Collections.Array();
        for (int j = 0; j < GN; j++)
            for (int i = 0; i < GN; i++)
            {
                int k = j * (GN + 1) + i;
                polys.Add(new int[] { k, k + 1, k + GN + 2, k + GN + 1 });
            }
        _jijiPoly.Polygons = polys;
    }

    float _uDefCur, _cumWf, _cumJf, _cumJb; Texture2D _jfTex, _wfTex;
    void UpdateUterus(float def, float touch)
    {
        _uDefCur = def;
        UpdateMesh(_uterusPoly, UterusBoxPx,
                      StageOffsetPx + GroupBOffsetPx + UterusFinePx
                        + new Vector2(0f, -LiftStep(touch) - _upFollow),
                      UterusGridW, new Vector2(0f, -UterusDeformPx), def, 0f, Vector2.Zero, _gridLines, _gridDots, ShowUterusGrid, UterusScale, UterusGridRotDeg);
        if (_uterusPoly != null)   // 左右擴張（以子宮中心為準，比底少一點）
        {
            float g = BottomExpandOn ? BottomExpandMax * Mathf.Clamp(_expK, 0f, 1f) * UterusExpandMul : 0f;
            float S = StageScale, k = UterusScale;
            _uterusPoly.Scale = new Vector2(S * k * (1f + g), S * k);
            _uterusPoly.Position += new Vector2(-UterusBoxPx.Size.X * S * k * g / 2f, 0f);
        }
    }

    float LiftStep(float touch)
    {
        float target = TouchEase(touch) * UterusTouchLiftPx;
        _liftS = Mathf.Lerp(_liftS, target, 1f - Mathf.Exp(-UterusLiftSmooth * Dt));   // 慢慢上去、慢慢回來
        return _liftS;
    }

    /// 0（未碰到）→1（頂到），平滑漸進。注意：Godot 的 Mathf.SmoothStep 不是 GLSL 的 smoothstep，這裡自己算。
    static float TouchEase(float def)
    {
        float t = Mathf.Clamp((def - UterusTouchFrom) / Mathf.Max(1f - UterusTouchFrom, 0.001f), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    void UpdateJiji(float def, float upPx, float rotDeg)
    {
        UpdateMesh(_jijiPoly, JijiBoxPx, StageOffsetPx + JijiFinePx + new Vector2(_cfg.JijiOffX, -_cfg.JijiOffsetY), JijiGridW,
                   new Vector2(-JijiLeftCompPx, -upPx), def, rotDeg, JijiPivotPx, _jGridLines, _jGridDots, ShowJijiGrid, 1f, 0f);
        if (_pivotDot != null)
        {
            _pivotDot.Visible = ShowPivot;
            _pivotDot.Position = JijiPivotPx - JijiBoxPx.Position - _pivotDot.Size / 2f;
        }
    }

    /// 通用 4x4 錨點（3x3 九宮格）網格變形。amp＝權重 1、def＝1 時的位移（畫布像素，y 負=往上）。
    /// gridRot：九宮格逆時針旋轉角（度），位移方向也一起旋轉。
    void UpdateMesh(Polygon2D poly, Rect2 box, Vector2 posCanvas, float[] W, Vector2 amp, float def,
                    float rotDeg, Vector2 pivotCanvas, Line2D[] lines, ColorRect[] dots, bool showGrid, float k, float gridRot)
    {
        if (poly == null) return;
        float S = StageScale;
        poly.Scale = new Vector2(S * k, S * k);
        poly.Position = (posCanvas + box.Position + box.Size * (1f - k) / 2f) * S;
        Vector2 pv = pivotCanvas - box.Position;
        float rad = Mathf.DegToRad(rotDeg);
        float gr = Mathf.DegToRad(gridRot);
        Vector2 ctr = box.Size / 2f;
        Vector2 ampR = amp.Rotated(-gr);          // 逆時針
        int n = GN + 1;
        var pts = new Vector2[n * n]; var uvs = new Vector2[n * n];
        for (int j = 0; j < n; j++)
        {
            float v = (float)j / GN;
            for (int i = 0; i < n; i++)
            {
                float u = (float)i / GN;
                var p0 = new Vector2(u * box.Size.X, v * box.Size.Y);
                var q = ctr + (p0 - ctr).Rotated(gr);                 // 轉到九宮格自己的座標
                float uu = Mathf.Clamp(q.X / box.Size.X, 0f, 1f), vv = Mathf.Clamp(q.Y / box.Size.Y, 0f, 1f);
                float w = GridW(W, uu, vv);
                var p = p0 + ampR * (w * def);
                if (rotDeg != 0f) p = pv + (p - pv).Rotated(rad);
                pts[j * n + i] = p;
                uvs[j * n + i] = box.Position + p0;
            }
        }
        poly.Polygon = pts; poly.UV = uvs;

        // 九宮格錨點（畫出來；4x4 點 = 3x3 格）
        var cp = new Vector2[16];
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 4; c++)
            {
                var L = new Vector2(c / 3f * box.Size.X, r / 3f * box.Size.Y);
                var p = ctr + (L - ctr).Rotated(-gr) + ampR * (W[r * 4 + c] * def);
                if (rotDeg != 0f) p = pv + (p - pv).Rotated(rad);
                cp[r * 4 + c] = poly.Position + p * S * k;
            }
        for (int r = 0; r < 4; r++) if (lines[r] != null) { lines[r].Points = new[] { cp[r * 4], cp[r * 4 + 1], cp[r * 4 + 2], cp[r * 4 + 3] }; lines[r].Visible = showGrid; }
        for (int c = 0; c < 4; c++) if (lines[4 + c] != null) { lines[4 + c].Points = new[] { cp[c], cp[4 + c], cp[8 + c], cp[12 + c] }; lines[4 + c].Visible = showGrid; }
        for (int i = 0; i < 16; i++) if (dots[i] != null) { dots[i].Position = cp[i] - new Vector2(5, 5); dots[i].Visible = showGrid; }
    }

    static float GridW(float[] W, float u, float v)
    {
        float[] bu = { (1 - u) * (1 - u) * (1 - u), 3 * u * (1 - u) * (1 - u), 3 * u * u * (1 - u), u * u * u };
        float[] bv = { (1 - v) * (1 - v) * (1 - v), 3 * v * (1 - v) * (1 - v), 3 * v * v * (1 - v), v * v * v };
        float w = 0f;
        for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) w += W[r * 4 + c] * bv[r] * bu[c];
        return w;
    }

    Texture2D _frameMaskTex;
    void SetFClip(ShaderMaterial m)
    {
        if (m == null) return;
        _frameMaskTex ??= ResourceLoader.Load<Texture2D>("res://Resources/MG/mg_framemask.png");
        m.SetShaderParameter("fmask", _frameMaskTex);
        m.SetShaderParameter("f_pos", _imgFrame.GlobalPosition);
        m.SetShaderParameter("f_size", _imgFrame.Size);
        m.SetShaderParameter("f_er", new Vector2(FrameClipErodePx / CanvasPx.X, FrameClipErodePx / CanvasPx.Y));
        m.SetShaderParameter("f_on", FrameClipOn && _frameMaskTex != null ? 1f : 0f);
    }

    static float Rf2(float a, float b) => a + (float)GD.Randf() * (b - a);

    float ExTip() => BottomExpandFollowTip ? _tipUvY : BottomExpandYPx / (CanvasPx.Y + BottomStretchPx / StageScale);

    void SetBRot(ShaderMaterial m)
    {
        if (m == null) return;
        m.SetShaderParameter("rot_a", Mathf.DegToRad(_jRotDeg * BottomRotMul + BottomRotOffsetDeg));
        m.SetShaderParameter("rot_p", new Vector2((JijiPivotPx.X + JijiFinePx.X - BottomFinePx.X) / CanvasPx.X, (JijiPivotPx.Y + JijiFinePx.Y - BottomFinePx.Y) / (CanvasPx.Y + BottomStretchPx / StageScale)));
        m.SetShaderParameter("asp", _imgBottom2.Size.X / _imgBottom2.Size.Y);
    }

    void UpdateMask()
    {
        SetBRot(_bottom2Mat); SetBRot(_bottomMat); SetBRot(_maskJiji); SetBRot(_maskUterus);
        SetFClip(_bottom2Mat); SetFClip(_bottomMat); SetFClip(_maskJiji); SetFClip(_maskUterus);
        if (_maskJiji != null)
        {
            _jfTex ??= ResourceLoader.Load<Texture2D>("res://Resources/MG/mg_jiji_stroke.png");
            _maskJiji.SetShaderParameter("jf_tex", _jfTex); _maskJiji.SetShaderParameter("jf_soft", CumJijiBrushSoft);
            _maskJiji.SetShaderParameter("jf_amt", _cumJf); _maskJiji.SetShaderParameter("jb_amt", _cumJb); _maskJiji.SetShaderParameter("jb_cs", Mathf.Cos(Mathf.DegToRad(CumJijiBlobRotDeg))); _maskJiji.SetShaderParameter("jb_sn", Mathf.Sin(Mathf.DegToRad(CumJijiBlobRotDeg))); _maskJiji.SetShaderParameter("jb_c", CumJijiBlobCenterPx); _maskJiji.SetShaderParameter("jb_r", CumJijiBlobSizePx);
            if (_maskUterus != null) { _maskUterus.SetShaderParameter("jb_amt", _cumJb); _maskUterus.SetShaderParameter("jb_cs", Mathf.Cos(Mathf.DegToRad(CumJijiBlobRotDeg))); _maskUterus.SetShaderParameter("jb_sn", Mathf.Sin(Mathf.DegToRad(CumJijiBlobRotDeg))); _maskUterus.SetShaderParameter("jb_c", CumJijiBlobCenterPx + _jiDisp); _maskUterus.SetShaderParameter("jb_r", CumJijiBlobSizePx); _maskUterus.SetShaderParameter("jf_str", CumJijiWhiteStr); _maskUterus.SetShaderParameter("jf_canvas", CanvasPx); }   // 起點那團也畫在子宮這層上（子宮蓋在雞雞上面，不然會被擋住）
            _maskJiji.SetShaderParameter("dp_c", new Vector2(782f, 79f) + CumDropOffsetPx); _maskJiji.SetShaderParameter("dp_r", CumDropRadiusPx);
            _maskJiji.SetShaderParameter("dp_amp", Mathf.Lerp(0.35f, 0.8f, _slE)); _maskJiji.SetShaderParameter("dp_sl", Mathf.Clamp(_slX * 1.5f, -0.15f, 0.15f));   // 雞雞旁邊那灘白水的波：幅度縮小約一半
            _maskJiji.SetShaderParameter("dp_a", CumDropAlpha); _maskJiji.SetShaderParameter("dp_in", CumDropOn ? _dropIn : 0f); _maskJiji.SetShaderParameter("dp_dbg", CumDropDebug ? 1f : 0f); _maskJiji.SetShaderParameter("dp_rot", Mathf.DegToRad(CumDropRotDeg)); _maskJiji.SetShaderParameter("dp_under", CumDropBehindJiji ? 1f : 0f);
            _maskJiji.SetShaderParameter("jf_y0", CumJijiY0); _maskJiji.SetShaderParameter("jf_y1", CumJijiY1);
            _maskJiji.SetShaderParameter("jf_str", CumJijiWhiteStr); _maskJiji.SetShaderParameter("jf_edge", CumJijiEdgePx); _maskJiji.SetShaderParameter("jf_line", CumJijiLineCover); _maskJiji.SetShaderParameter("jf_c0", CumJijiCenterX); _maskJiji.SetShaderParameter("jf_cs", CumJijiSlope); _maskJiji.SetShaderParameter("jf_w", CumJijiHalfW); _maskJiji.SetShaderParameter("jf_mid", CumJijiMid); _maskJiji.SetShaderParameter("jf_brush_a", CumBrushArc); _maskJiji.SetShaderParameter("jf_brush_s", CumBrushSlant); _maskJiji.SetShaderParameter("jf_canvas", CanvasPx);
        }
        foreach (var bmat in new[] { _bottom2Mat, _bottomMat })
        if (bmat != null)
        {
            bmat.SetShaderParameter("ex_amt", BottomExpandOn ? BottomExpandMax * Mathf.Clamp(_expK, 0f, 1f) : 0f);
            bmat.SetShaderParameter("ex_tip", ExTip());
            bmat.SetShaderParameter("ex_tilt", Mathf.DegToRad(BottomExpandTiltDeg));
            bmat.SetShaderParameter("top_cut", BottomTopCutPx / (CanvasPx.Y + BottomStretchPx / StageScale));
            bmat.SetShaderParameter("wh_on", BottomWhiteRemove ? 1f : 0f);
            _wfTex ??= ResourceLoader.Load<Texture2D>("res://Resources/MG/mg_bottom_stroke.png");
            bmat.SetShaderParameter("wf_tex", _wfTex);
            bmat.SetShaderParameter("wf_amt", CumWhiteOnBottom ? _cumWf : 0f);
            bmat.SetShaderParameter("wf_str", CumWhiteStrength);
            bmat.SetShaderParameter("wf_mid", CumWhiteMid);
            bmat.SetShaderParameter("wf_brush_a", CumBrushArc / (CanvasPx.Y + BottomStretchPx / StageScale));
            bmat.SetShaderParameter("wf_brush_s", CumBrushSlant / (CanvasPx.Y + BottomStretchPx / StageScale));
            bmat.SetShaderParameter("wf_cofs", CumWhiteCenterOfs);
            bmat.SetShaderParameter("wf_slant", Mathf.Tan(Mathf.DegToRad(CumWhiteTiltDeg)) * (CanvasPx.Y + BottomStretchPx / StageScale) / CanvasPx.X);
            bmat.SetShaderParameter("wf_x0", CumWhiteX0 / CanvasPx.X);
            bmat.SetShaderParameter("wf_x1", CumWhiteX1 / CanvasPx.X);
            bmat.SetShaderParameter("wf_t0", 363f / (CanvasPx.Y + BottomStretchPx / StageScale));
            bmat.SetShaderParameter("wf_t1", 969f / (CanvasPx.Y + BottomStretchPx / StageScale));
            bmat.SetShaderParameter("ex_sig", BottomExpandSigma);
            bmat.SetShaderParameter("ex_cx", BottomCenterU);
        }
        foreach (var m in new[] { _maskJiji, _maskUterus })
        {
            if (m == null) continue;
            m.SetShaderParameter("mask_tex", _imgBottom2.Texture);
            m.SetShaderParameter("m_pos", _imgBottom2.GlobalPosition);
            m.SetShaderParameter("m_size", _imgBottom2.Size);
            m.SetShaderParameter("ex_amt", BottomExpandOn ? BottomExpandMax * Mathf.Clamp(_expK, 0f, 1f) : 0f);
            m.SetShaderParameter("ex_tip", ExTip());
            m.SetShaderParameter("ex_tilt", Mathf.DegToRad(BottomExpandTiltDeg));
            m.SetShaderParameter("top_cut", BottomTopCutPx / (CanvasPx.Y + BottomStretchPx / StageScale));
            m.SetShaderParameter("wh_on", BottomWhiteRemove ? 1f : 0f);
            m.SetShaderParameter("ex_sig", BottomExpandSigma);
            m.SetShaderParameter("ex_cx", BottomCenterU);
            m.SetShaderParameter("m_on", MaskOn ? 1f : 0f);
        }
    }

    async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    bool Alive => IsInstanceValid(this) && IsInsideTree();
    float Dt => (float)GetProcessDeltaTime();
    bool ConsumeClick() { bool r = _clickEdge || _spaceEdge; _clickEdge = _spaceEdge = false; return r; }

    async Task WaitClick()
    {
        _clickEdge = _spaceEdge = false;
        while (Alive && !ConsumeClick()) await Frame();
    }

    // ══════════════════════════════════════════════════════════
    //  遊戲邏輯
    // ══════════════════════════════════════════════════════════
    static int CalcGain(int action, int round, int posCat)
    {
        int g;
        if (round <= PhaseEarlyEnd) g = action == 0 ? 30 : action == 1 ? 15 : 5;
        else if (round <= PhaseMidEnd) g = action == 0 ? 10 : action == 1 ? 20 : 10;
        else                  g = action == 0 ? 5  : action == 1 ? 15 : 35;
        if (posCat == 0)                     g += 20;
        else if (posCat == 3)                g += 20;
        else if (posCat == 1 && action == 0) g += 15;
        else if (posCat == 2 && action == 2) g += 15;
        return g;
    }

    string Pick(string[] a) => a[_rng.Next(a.Length)];

    string GetReaction(int action, int round, int posCat, int pleasureBefore, bool orgasmed)
    {
        if (orgasmed) return Pick(MgText.Orgasm);
        if (pleasureBefore >= 100) return Pick(MgText.StoppedAt100);
        if (posCat == 0 && action == 2) return Pick(MgText.GHitHard);
        if (posCat == 3 && action == 2) return Pick(MgText.UterusHard);
        if (posCat == 3) return Pick(MgText.UterusAny);
        if (round <= PhaseEarlyEnd && action == 0) return Pick(MgText.EarlyGentle);
        if (round >= 11 && action == 0) return Pick(MgText.LateGentle);
        if (posCat == 2 && action == 2) return Pick(MgText.FarHard);
        if (posCat == 1 && action == 0) return Pick(MgText.NearGentle);
        if (round <= PhaseEarlyEnd && action == 2) return MgText.EarlyHard;
        if (round <= PhaseEarlyEnd && action == 1) return MgText.EarlyMid;
        if (round <= PhaseMidEnd && action == 1) return MgText.MidMid;
        if (round <= PhaseMidEnd && action == 0) return MgText.MidGentle;
        if (round <= PhaseMidEnd && action == 2) return MgText.MidHard;
        if (action == 2) return MgText.LateHard;
        if (action == 1) return MgText.LateMid;
        return MgText.Default;
    }

    void SetPleasure(float v)
    {
        v = Mathf.Clamp(v, 0f, 1f);
        _fill.Size = new Vector2(348f * v, 18f);
        _pct.Text = $"{Mathf.RoundToInt(v * 100f)}/100";
    }

    static bool InG(float x) => x >= 255f && x <= 313f;
    static bool InU(float x) => x <= 46f;

    void SetMarkerColor(bool hot)
        => ((ColorRect)_marker).Color = hot ? new Color(1f, 0.30f, 0.50f) : new Color(1f, 0.90f, 0.40f);

    void UpdateOrg(int n)
    {
        string h = "";
        for (int i = 0; i < NeedOrgasms; i++) h += i < n ? "♥ " : "♡ ";
        _org.Text = $"高潮次數：{h}({n}/{NeedOrgasms})";
    }

    async Task RunAsync()
    {
        // ── 開場 ──
        var intro = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        _root.AddChild(intro); intro.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var ib = new ColorRect { Color = new Color(0, 0, 0, 0.92f) }; intro.AddChild(ib); ib.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var il = new Label { Text = MgText.Title + "\n\n" + MgText.IntroBody + "\n\n— " + MgText.IntroBtn + "（點擊 / 空白鍵）—",
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        il.AddThemeFontSizeOverride("font_size", 28);
        intro.AddChild(il); il.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        il.OffsetLeft = -500; il.OffsetRight = 500; il.OffsetTop = -250; il.OffsetBottom = 250;
        await WaitClick();
        if (!Alive) return;
        intro.QueueFree();

        // 面板淡入
        for (float t = 0; t < 1f && Alive; t += Dt * 3f) { _panel.Modulate = new Color(1, 1, 1, t); await Frame(); }
        _panel.Modulate = Colors.White;

        string[] posNames = { "精準命中 G點！", "極近 — G點周圍", "極遠 — 中段", "頂到子宮口！" };
        int round = 0, orgasmCount = 0;
        int pl = 0;
        float energy = EnergyMax;
        bool exhausted = false;
        var gold = new Color(1f, 0.85f, 0.40f);

        while (Alive && round < MaxRounds && orgasmCount < NeedOrgasms)
        {
            round++;
            if (round > 1) energy = Mathf.Min(EnergyMax, energy + EnergyRecover);
            string phase = round <= PhaseEarlyEnd ? "【前戲期】" : round <= PhaseMidEnd ? "【過渡期】" : "【衝刺期】";
            _round.Text = $"第 {round} / {MaxRounds} 回合　精力 {Mathf.RoundToInt(energy)}/{Mathf.RoundToInt(EnergyMax)}　{phase}";
            _hint.Text = round <= PhaseEarlyEnd ? "提示：前戲期適合溫柔的觸碰" : round <= PhaseMidEnd ? "提示：過渡期適合穩定的節奏" : "提示：衝刺期適合猛烈的衝擊";

            SetMarkerColor(false);

            SetPleasure(pl / 100f); UpdateOrg(orgasmCount);
            foreach (var b in _btns) b.Visible = true;
            _cont.Visible = false; _gain.Text = ""; _spk.Visible = _dlgBg.Visible = false; _react.Text = "";

            _chosen = -1;
            _idling = true; _osc = true; _traveled = 0f; _markerX = Mathf.Clamp(_markerX, IdleMinX, IdleMaxX);
            while (Alive && _chosen < 0) await Frame();
            _idling = false;
            if (!Alive) return;
            foreach (var b in _btns) b.Visible = false;

            int action = _chosen;
            energy -= EnergyCost[Mathf.Clamp(action, 0, 2)];
            if (energy < 0f) { exhausted = true; break; }   // 精力不夠了：沒力氣繼續，遊戲結束
            _sweepSpeed = ActionSpeeds[Mathf.Clamp(action, 0, 2)]; _traveled = 0f;
            _thrustPhase = action == 2 ? 1 : 0;
            // 選完後繼續同一個上下動作，再完整掃 SweepCycles 個來回（一個來回 = 2×560），才開始隨機停止
            SetMarkerColor(false);
            float sweepEnd = _traveled + SweepCycles * 1120f;
            while (Alive && _traveled < sweepEnd) await Frame();
            _thrustPhase = action == 2 ? 2 : 0;   // 前面的幾次過了，最後一次往下 5px
            while (Alive && _markerX > 21f) await Frame();   // 不管選哪個，最後一定會再繼續到最深處把子宮頂一下，然後才隨機落點
            _osc = false;
            _markerX = Mathf.Min(_markerX, 20f);
            for (float s = 0; s < 1f && Alive; s += Dt / PushRiseTime)   // 頂到底之後，子宮慢慢往上，雞雞也跟著再往上一點，並且輕微抖動
            { _pushExtra = Mathf.Sin(s * Mathf.Pi * 0.5f); _pushJit = 1f; await Frame(); }
            _pushExtra = 1f;
            if (!Alive) return;
            int roll = _rng.Next(0, 20);
            int posCat = roll < 3 ? 0 : roll < 8 ? 1 : roll < 18 ? 2 : 3;
            _posLbl.Text = $"當前位置：{posNames[posCat]}";
            _posLbl.AddThemeColorOverride("font_color", (posCat == 0 || posCat == 3) ? new Color(1f, 0.85f, 0.40f) : gold);

            float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);
            float targetX = posCat == 3 ? R(20f, 70f) : posCat == 2 ? R(110f, 200f) : posCat == 1 ? R(400f, 490f) : R(258f, 310f);

            float oldX = _markerX;
            for (float s = 0; s < 1f && Alive; s += Dt * 4.5f)
            {
                _pushExtra = 1f - Mathf.SmoothStep(0f, 1f, s); _pushJit = 1f - s;
                _markerX = Mathf.Lerp(oldX, targetX, Mathf.SmoothStep(0f, 1f, s));
                await Frame();
            }
            _markerX = targetX; _pushExtra = _pushJit = 0f; _thrustPhase = 0;
            SetMarkerColor(posCat == 0 || posCat == 3);
            int gain = CalcGain(action, round, posCat);
            int oldPl = pl; pl = Math.Min(pl + gain, 100);
            _gain.Text = $"+{gain} 高潮值"; _dlgBg.Visible = true;
            _gain.AddThemeColorOverride("font_color", gain >= 30 ? new Color(0.40f, 1f, 0.60f) : gain >= 15 ? new Color(0.80f, 0.80f, 0.40f) : new Color(0.60f, 0.55f, 0.50f));

            for (float t = 0; t < 1f && Alive; t += Dt * 4f) { SetPleasure(Mathf.Lerp(oldPl, pl, t) / 100f); await Frame(); }
            SetPleasure(pl / 100f);

            bool orgasmed = false;
            if (pl >= 100)
            {
                _gain.Text = "★ 高潮值滿！對準敏感點！ ★"; _gain.AddThemeColorOverride("font_color", new Color(1f, 0.40f, 0.60f));
                _posLbl.Text = "點擊 / 按空白鍵 停下標記！";
                _hint.Text = "對準兩端粉紅色區域（G點 或 子宮口）就能讓她高潮！";
                float speed = 280f + orgasmCount * 60f, dir = 1f;
                _clickEdge = _spaceEdge = false;
                await Frame();               // 吃掉按下按鈕的那一下點擊
                _clickEdge = _spaceEdge = false;
                while (Alive)
                {
                    _markerX += dir * speed * Dt;
                    if (_markerX >= MarkMax) { _markerX = MarkMax; dir = -1f; }
                    if (_markerX <= 20f)     { _markerX = 20f;     dir = 1f; }
                    SetMarkerColor(InG(_markerX) || InU(_markerX));
                    if (ConsumeClick()) break;
                    await Frame();
                }
                if (!Alive) return;
                bool hitG = InG(_markerX), hitU = InU(_markerX);
                if (hitG || hitU)
                {
                    orgasmed = true; orgasmCount++; pl = 0;
                    _gain.Text = $"★ 精準命中{(hitG ? "G點" : "子宮口")}！高潮觸發！ ★";
                    _markerX = hitG ? Mathf.Clamp(_markerX, 258f, 310f) : _markerX;
                }
                else { pl = 70; _gain.Text = "沒有對準敏感點……快感回落到 70"; }
                for (float t = 0; t < 1f && Alive; t += Dt * 3f) { SetPleasure(Mathf.Lerp(1f, pl / 100f, t)); await Frame(); }
                SetPleasure(pl / 100f);
            }

            if (orgasmed)
            {
                for (float t = 0; t < 1f && Alive; t += Dt * 2f) { _flash.Color = new Color(1, 1, 1, Mathf.Lerp(0.8f, 0f, t)); await Frame(); }
                _flash.Color = new Color(1, 1, 1, 0);
            }
            UpdateOrg(orgasmCount);

            string reaction = GetReaction(action, round, posCat, oldPl, orgasmed);
            _spk.Visible = _dlgBg.Visible = true; _react.Text = "";
            _clickEdge = _spaceEdge = false;
            for (int i = 0; i < reaction.Length && Alive; i++)
            {
                _react.Text += reaction[i];
                if (_clickEdge) { _react.Text = reaction; break; }
                await ToSignal(GetTree().CreateTimer(0.02), SceneTreeTimer.SignalName.Timeout);
            }
            _cont.Visible = true;
            await ToSignal(GetTree().CreateTimer(0.4), SceneTreeTimer.SignalName.Timeout);
            await WaitClick();
        }
        if (!Alive) return;

        if (Alive && !exhausted && round >= MaxRounds) await PlayCum();   // 第 10 回合必定射精

        // ── 結果 ──
        bool won = orgasmCount >= NeedOrgasms;
        foreach (var b in _btns) b.Visible = false;
        _cont.Visible = false; _gain.Text = ""; _hint.Text = ""; _spk.Visible = _dlgBg.Visible = false; _react.Text = ""; _posLbl.Text = ""; _round.Text = "";
        var res = new Label { Text = (won ? MgText.WinText : MgText.FailText) + $"\n\n{(exhausted ? "精力耗盡\n" : "")}高潮 {orgasmCount}/{NeedOrgasms}　回合 {round}/{MaxRounds}\n\n— 點擊 / 空白鍵 繼續 —",
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        res.AddThemeFontSizeOverride("font_size", 28);
        res.AddThemeColorOverride("font_color", won ? gold : new Color(0.70f, 0.55f, 0.55f));
        var rb = new ColorRect { Color = new Color(0, 0, 0, 0.88f), MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.AddChild(rb); rb.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(res); res.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        res.OffsetLeft = -450; res.OffsetRight = 450; res.OffsetTop = -200; res.OffsetBottom = 200;
        await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
        await WaitClick();
        for (float t = 0; t < 1f && Alive; t += Dt * 1.5f) { _root.Modulate = new Color(1, 1, 1, 1f - t); await Frame(); }
        _done.TrySetResult(new MgResult { Won = won, Orgasms = orgasmCount, Rounds = round });
    }
}
