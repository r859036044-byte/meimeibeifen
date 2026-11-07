using Godot;
using System;
using System.Threading.Tasks;

/// <summary>
/// EyeCloseController — Unity 版移植
/// 閉眼動畫控制器。
///
/// 移植說明：
///   RectTransform.anchoredPosition → Control.Position
///   RectTransform.sizeDelta.y      → Control.Size.Y
///   localScale                     → Control.Scale
///   localEulerAngles.z             → Control.RotationDegrees
///   Coroutine / WaitForSeconds     → async Task + await delay
/// </summary>
public partial class EyeCloseController : Node
{
    // ════════════════════════════════════════════════════════════
    //  資料結構
    // ════════════════════════════════════════════════════════════

    [Serializable]
    public partial class SlidePart : Resource
    {
        public Control RT;
        [Export] public float   CloseOffsetX = 0f;
        [Export] public float   CloseOffsetY = -30f;
        [Export] public float   CloseRotateZ = 0f;
        [Export] public int     EyeGroup     = 0;
        [Export] public Vector2 OpenPos;
        [Export] public float   OpenRotateZ;
    }

    [Serializable]
    public partial class MaskPart : Resource
    {
        public Control RT;
        [Export] public float   CloseHeight = 3f;
        [Export] public int     EyeGroup    = 0;
        [Export] public float   OpenHeight;
        [Export] public Vector2 OpenPos;   // 用來疊加 BreathOffsetY，不然遮罩容器呼吸時不會跟著動
    }

    [Serializable]
    public partial class TearPart : Resource
    {
        public Control RT;
        [Export] public float   ExpandX = 20f;
        [Export] public float   OffsetY = 0f;
        [Export] public int     EyeGroup = 0;
        [Export] public float   OpenWidth;
        [Export] public Vector2 OpenPos;
        /// <summary>固定疊加的垂直位移（Godot 座標，正值＝往下）。
        /// 用來微調眼淚圖層的基準高度，跟 OffsetY（張嘴/流淚動畫用的動態位移）分開，
        /// 不影響原本的擴張/位移動畫邏輯。</summary>
        [Export] public float   ExtraOffsetY = 0f;
        /// <summary>固定向左平移的量（px）：整個眼淚圖往左 ExtraLeft。</summary>
        [Export] public float   ExtraLeft = 0f;
    }

    [Serializable]
    public partial class FlipPart : Resource
    {
        public Control RT;
        [Export] public float   OffsetY  = 0f;
        /// <summary>翻轉到底（flipT=1）時額外疊加的水平位移（Godot 座標，正值＝往右移，
        /// 負值＝往左移——這裡跟 OffsetY 的正負號慣例不同，OffsetX 直接用直覺的方向，
        /// 不用像 OffsetY 那樣心算一次負負得正）。0 = 不位移（預設）。</summary>
        [Export] public float   OffsetX  = 0f;
        [Export] public int     EyeGroup = 0;
        [Export] public Vector2 OpenScale;
        [Export] public Vector2 OpenPos;
        /// <summary>翻轉到底（flipT=1）時額外疊加的旋轉角度（度）。0 = 不旋轉（預設，
        /// ClosedFlipParts／HalfFlipParts 等既有用途都維持 0，不受影響）。目前只有 ZZZ 的
        /// EyelidFlipParts（睫毛＋眼角）會設非 0 值，讓左右眼翻轉時鏡射旋轉。</summary>
        [Export] public float   ClosedRotateZ = 0f;
        /// <summary>true＝翻轉壓縮到接近一條線（flipT≈0.5，scaleY≈0）之後就直接隱藏
        /// （Visible=false），不再繼續播放到 flipT=1 的「鏡射後完全翻轉」畫面。
        /// false（預設）＝正常播放完整段翻轉，跟睫毛一樣。目前只有 ZZZ 的眼角1/眼角2
        /// 設成 true——使用者要求眼角壓縮成一條線之後直接消失，睫毛則維持原本翻轉效果。</summary>
        [Export] public bool    HideWhenFlipped = false;
        public float OpenRotateZ;
    }

    // ════════════════════════════════════════════════════════════
    //  Export
    // ════════════════════════════════════════════════════════════

    [ExportGroup("位移部位（眼皮、睫毛、眼角、下睫毛）")]
    [Export] public SlidePart[] SlideParts;

    [ExportGroup("眼白遮罩縮放")]
    [Export] public MaskPart[] MaskParts;

    [ExportGroup("眼淚左右擴張")]
    [Export] public TearPart[] TearParts;

    [ExportGroup("全閉2：睫毛翻轉")]
    [Export] public FlipPart[] ClosedFlipParts;

    [ExportGroup("半閉2：下睫毛翻轉")]
    [Export] public FlipPart[] HalfFlipParts;
    [Export] public FlipPart[] HalfFlipCounterParts;

    [ExportGroup("全閉3：眼皮翻轉（ZZZ 專用）")]
    /// <summary>跟全閉2（ClosedFlipParts）是同一種「半閉→全閉段 scaleY 翻轉」機制，但翻轉的
    /// 是眼皮而不是睫毛。獨立出一組全新的狀態/陣列，是為了不去動 ClosedFlipParts——
    /// 那組是「做愛4」在用、對照 Unity 場景真實資料的翻轉睫毛效果，不能因為 ZZZ 這個
    /// 全新表情的需求而被覆寫掉。
    /// （註：這裡原本誤用了 Unity 專屬的 [Tooltip(...)] 屬性，Godot C# 沒有這個屬性，
    /// 會導致編譯失敗、遊戲打不開，已經改成一般的文件註解。）</summary>
    [Export] public FlipPart[] EyelidFlipParts;

    [ExportGroup("動畫設定")]
    [Export] public float AnimSpeed      = 10f;
    [Export] public float HalfCloseAmount = 0.45f;

    /// <summary>由 FaceExpressionController 依目前表情動態設定：閉眼時 SlideParts（眼皮/睫毛/
    /// 眼角/下睫毛）額外再往下（Godot 座標正值）位移多少 px，讓整組閉眼圖案的視覺位置更低。
    /// 0 = 不額外位移（預設，維持原本的閉眼位置）。只在 t（閉合進度）內插時按比例套用，
    /// 睜眼狀態（t=0）完全不受影響。目前只有 ZZZ（睡覺）表情會設這個值，見
    /// FaceExpressionController.cs 的 _eyeExtraCloseOffsetMap。</summary>
    [Export] public float ClosedExtraOffsetY = 0f;

    /// <summary>閉眼時左右眼各自的高度校正（Godot 座標，正值＝往下，負值＝往上）。
    /// 實際遊戲截圖量測：沒校正時，閉眼後畫面右眼（群組1）比左眼（群組2）低約 16px（角色畫布座標）。
    /// 第一版用貼圖 PIL 模擬推算方向，結果推反了（右眼往下、左眼往上），讓差距變更大；
    /// 這版改以遊戲實際截圖為準。臉本身的歪斜是刻意的（畫面右側較高，睜眼時右眼就比左眼高約 12px），
    /// 閉眼要維持同樣的歪斜關係，不是拉成水平：16（原本右眼偏低）+ 12（應該偏高）≈ 28，兩眼各分 14。
    /// 只在閉合進度 t 內插時按比例套用，睜眼（t=0）完全不受影響。
    /// 如果還差一點：右眼太低 → Group1 再減；左眼太低 → Group2 再減。</summary>
    /// <summary>ZZZ（全閉3）時眼皮（眼皮/眼皮2）的閉眼位置（相對睜眼位置，畫布 px，正＝往下）。
    /// ZZZ 的睫毛是原地翻轉（不往下滑），原本眼皮還是照一般閉眼往下滑 30＋ZZZ 額外 20，
    /// 結果眼皮的摺痕跑到翻轉後的睫毛下面／疊在睫毛上。改成眼皮停在睫毛正上方：
    /// 群組1（畫面右眼）往下 10、往左 3（跟睫毛的 OffsetX -3 一致），群組2（畫面左眼）往下 20（跟睫毛的 OffsetY 一致）。
    /// 數值是用貼圖模擬合成挑出來的。</summary>
    [Export] public Vector2 ZzzEyelidOffset1 = new Vector2(-3f, 10f);
    [Export] public Vector2 ZzzEyelidOffset2 = new Vector2(0f, 20f);

    // 之前為了修「左右眼一高一低」一直調這兩個值，但真正原因是 ResetFlip 的 bug（見下方 ResetFlip 註解），
    // 左眼睫毛根本沒在閉眼。bug 修掉後歸零，閉眼高低差回到原圖本身刻意的臉部歪斜。
    [Export] public float ClosedOffsetYGroup1 = 0f;
    [Export] public float ClosedOffsetYGroup2 = 0f;

    /// <summary>由 FaceExpressionController 依目前表情動態設定：閉眼時額外再往「垂下」方向
    /// 旋轉多少度（正值 = 眼睛外側角往下垂的角度）。兩側眼睛會自動依 EyeGroup 鏡射方向
    /// （群組1 用正值、群組2 用負值），讓左右眼對稱地往外下垂，不用手動分別給正負號。
    /// 0 = 不額外旋轉（預設）。同樣只在 t（閉合進度）內插時按比例套用。</summary>
    [Export] public float ClosedExtraRotateZ = 0f;

    [ExportGroup("眨眼設定")]
    [Export] public bool  AutoBlink      = true;
    [Export] public float BlinkCloseTime = 0.12f;
    [Export] public float BlinkInterval1 = 3.0f;
    [Export] public float BlinkInterval2 = 3.5f;
    [Export] public float BlinkInterval3 = 4.0f;
    [Export] public float DoubleBlinkGap = 0.15f;

    // ════════════════════════════════════════════════════════════
    //  公開屬性
    // ════════════════════════════════════════════════════════════

    public bool  IsClosed => _targetClose;
    public float Progress => _t;

    /// <summary>由 TrainingRoomRuntime 每幀寫入的「頭部呼吸」Y 偏移量（Godot 座標）。
    /// 眼皮/睫毛/眼角/下睫毛/眼白/眼球/眼淚都已經完全交給這個控制器管位置，
    /// 不會再經過通用呼吸迴圈，呼吸時的自然起伏要靠這裡疊加。</summary>
    public float BreathOffsetY = 0f;
    /// <summary>高潮痙攣時整個身體的抖動位移（由 TrainingRoomRuntime 每幀傳入 _spasmsBodyShake），
    /// 眼睛圖層不經過通用呼吸迴圈，沒有這個的話臉在抖、眼睛不抖，看起來左右眼高度亂跳。</summary>
    public Vector2 ShakeOffset = Vector2.Zero;

    // ════════════════════════════════════════════════════════════
    //  私有狀態
    // ════════════════════════════════════════════════════════════

    private bool  _targetClose          = false;
    private bool  _expressionClosed     = false;
    private bool  _expressionClosed2    = false;
    private bool  _expressionHalfClosed  = false;
    private bool  _expressionHalfClosed2 = false;
    private bool  _expressionClosed3     = false;   // 全閉3（眼皮翻轉，ZZZ 專用）
    private float _t = 0f;
    private bool  _blinkRunning = false;

    private bool  _semenCloseL = false;
    private bool  _semenCloseR = false;
    private float _tL = 0f;
    private float _tR = 0f;

    private float _exprTargetL = -1f;
    private float _exprTargetR = -1f;
    private float _exprTL2     = 0f;
    private float _exprTR2     = 0f;

    // ════════════════════════════════════════════════════════════
    //  生命週期
    // ════════════════════════════════════════════════════════════

    public override void _Ready()
    {
        InitParts();
        if (AutoBlink) StartBlinking();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        float target;
        if (_expressionClosed || _expressionClosed2 || _expressionClosed3)
            target = 1f;
        else if (_expressionHalfClosed || _expressionHalfClosed2)
            target = HalfCloseAmount;
        else
            target = _targetClose ? 1f : 0f;

        _t  = AnimSpeed <= 0f ? target : Mathf.MoveToward(_t,  target, AnimSpeed * dt);

        float targetL = _semenCloseL ? 1f : 0f;
        float targetR = _semenCloseR ? 1f : 0f;
        _tL = AnimSpeed <= 0f ? targetL : Mathf.MoveToward(_tL, targetL, AnimSpeed * dt);
        _tR = AnimSpeed <= 0f ? targetR : Mathf.MoveToward(_tR, targetR, AnimSpeed * dt);

        if (_exprTargetL >= 0f)
            _exprTL2 = AnimSpeed <= 0f ? _exprTargetL : Mathf.MoveToward(_exprTL2, _exprTargetL, AnimSpeed * dt);
        if (_exprTargetR >= 0f)
            _exprTR2 = AnimSpeed <= 0f ? _exprTargetR : Mathf.MoveToward(_exprTR2, _exprTargetR, AnimSpeed * dt);

        Apply(_t);
    }

    private void InitParts()
    {
        foreach (var p in SlideParts ?? Array.Empty<SlidePart>())
            if (p?.RT != null)
            {
                p.OpenPos     = p.RT.Position;
                p.OpenRotateZ = p.RT.RotationDegrees;
                // 位移不受 PivotOffset 影響，但這裡也順便修正成貼圖內容中心，這樣之後
                // CloseRotateZ／ClosedExtraRotateZ 做旋轉時，才會繞著眼皮/睫毛/眼角自己
                // 的中心轉，而不是繞著整張透明畫布的中心轉（否則旋轉時一樣會被甩到很遠的地方，
                // 跟之前 ClosedFlipParts 樞紐點抓錯是同一種問題）。
                SetFlipPivotToCenter(p.RT);
                if (p.EyeGroup == 0) p.EyeGroup = GuessEyeGroup(p.RT);
            }
        foreach (var p in MaskParts ?? Array.Empty<MaskPart>())
            if (p?.RT != null)
            {
                p.OpenHeight = p.RT.Size.Y;
                p.OpenPos    = p.RT.Position;
                if (p.EyeGroup == 0) p.EyeGroup = GuessEyeGroup(p.RT);
            }
        foreach (var p in TearParts ?? Array.Empty<TearPart>())
            if (p?.RT != null)
            {
                p.OpenWidth = p.RT.Size.X;
                p.OpenPos   = p.RT.Position;
                if (p.EyeGroup == 0) p.EyeGroup = GuessEyeGroup(p.RT);
            }
        foreach (var p in ClosedFlipParts ?? Array.Empty<FlipPart>())
            if (p?.RT != null)
            {
                p.OpenScale    = p.RT.Scale;
                p.OpenPos      = p.RT.Position;
                p.OpenRotateZ  = p.RT.RotationDegrees;
                SetFlipPivotToCenter(p.RT);
                if (p.EyeGroup == 0) p.EyeGroup = GuessEyeGroup(p.RT);
            }
        foreach (var p in HalfFlipParts ?? Array.Empty<FlipPart>())
            if (p?.RT != null)
            {
                p.OpenScale    = p.RT.Scale;
                p.OpenPos      = p.RT.Position;
                p.OpenRotateZ  = p.RT.RotationDegrees;
                SetFlipPivotToCenter(p.RT);
                if (p.EyeGroup == 0) p.EyeGroup = GuessEyeGroup(p.RT);
            }
        foreach (var p in HalfFlipCounterParts ?? Array.Empty<FlipPart>())
            if (p?.RT != null)
            {
                p.OpenScale    = p.RT.Scale;
                p.OpenPos      = p.RT.Position;
                p.OpenRotateZ  = p.RT.RotationDegrees;
                SetFlipPivotToCenter(p.RT);
                if (p.EyeGroup == 0) p.EyeGroup = GuessEyeGroup(p.RT);
            }
        foreach (var p in EyelidFlipParts ?? Array.Empty<FlipPart>())
            if (p?.RT != null)
            {
                p.OpenScale    = p.RT.Scale;
                p.OpenPos      = p.RT.Position;
                p.OpenRotateZ  = p.RT.RotationDegrees;
                SetFlipPivotToCenter(p.RT);
                if (p.EyeGroup == 0) p.EyeGroup = GuessEyeGroup(p.RT);
            }
    }

    /// <summary>
    /// 把要做 scaleY 翻轉的 Control 樞紐點（PivotOffset）設到「貼圖實際有畫東西的那一小塊區域」
    /// 的正中央，而不是整張圖片檔案的正中央。
    ///
    /// 背景（第一次修這個問題時想錯了，這裡記錄正確的根本原因）：
    /// Unity 場景檔核對過，睫毛1/2 等要翻轉的 RectTransform 的 m_Pivot 都是 (0.5, 0.5)、
    /// sizeDelta 只有 146x68 這種小尺寸——因為 Unity 版的美術資源本來就是裁切好的小圖。
    /// 但 Godot 版改成整批貼圖直接讀取 PNG 檔案原始尺寸（TrainingRoomRuntime.BuildCharacter()：
    /// img.Size = new Vector2(tex.GetWidth(), tex.GetHeight())），而「睫毛1.png」等檔案
    /// 其實是整張 2000x3200 的「全身空白畫布」，實際畫出來的睫毛內容只佔中間偏上一小塊
    /// （經量測，睫毛1.png 的睫毛內容大約在 x:1004~1148, y:601~671，畫布其餘部分整片透明）。
    ///
    /// BuildCharacter() 已經對「每一層」都把 PivotOffset 設成 Size/2（整張畫布的正中央，
    /// 約 y=1600），這對「平移/旋轉」類的操作沒問題（因為那些不會放大鏡射範圍），
    /// 但如果拿這個「畫布正中央」當作 Scale.y=-1 的翻轉樞紐，等於是把整張 2000x3200
    /// 的透明畫布繞著 y=1600 上下鏡射——睫毛內容從 y≈636 的地方，鏡射後會出現在
    /// y≈2*1600-636=2564 的地方，也就是整張畫布很下面（腳的高度）！這才是使用者回報
    /// 「閉眼/翻轉之後圖層跑到腳那邊」的真正原因，跟樞紐點「有沒有置中」無關，
    /// 是置中在「錯的物件」（整張透明畫布 vs. 真正有畫面的那一小塊）。
    ///
    /// 修法：這裡改成用「貼圖實際內容的像素外框（bounding box）中心」當作翻轉樞紐，
    /// 而不是整張圖片的 Size/2。下面這幾個座標是直接用 Python/PIL 量測對應 PNG 檔案
    /// 非透明像素外框算出來的中心點（畫布左上角為原點，跟 Godot 的 Size/Position 同一套
    /// 像素座標系）；找不到對應名稱時退回原本的 Size/2（安全預設值，不會產生例外）。
    /// </summary>
    private static readonly System.Collections.Generic.Dictionary<string, Vector2> FlipPivotOverrides = new()
    {
        // 表情_眼皮／表情_眼皮2
        { "表情_眼皮",   new Vector2(1050f, 608f) },
        { "表情_眼皮2",  new Vector2(863f,  618f) },
        // 表情_睫毛1／表情_睫毛2：全閉2（ZZZ／做愛4）翻轉部位
        { "表情_睫毛1",  new Vector2(1076f, 636f) },
        { "表情_睫毛2",  new Vector2(835f,  636f) },
        // 眼角1／眼角2
        { "眼角1",      new Vector2(1112.5f, 647.5f) },
        { "眼角2",      new Vector2(799.5f,  660f) },
        // 表情_下睫毛1／表情_下睫毛2：半閉2翻轉部位
        { "表情_下睫毛1", new Vector2(1062f, 666f) },
        { "表情_下睫毛2", new Vector2(850f,  678f) },
    };

    private static void SetFlipPivotToCenter(Control rt)
    {
        rt.PivotOffset = FlipPivotOverrides.TryGetValue(rt.Name, out var p) ? p : rt.Size * 0.5f;
    }

    /// <summary>判斷某個 Control 是不是目前 EyelidFlipParts 裡的部位（眼皮/眼皮2）——
    /// 用來讓 Apply() 的 SlideParts 迴圈知道要跳過它，改交給 ApplyFlip(EyelidFlipParts, ...) 處理。</summary>
    private bool IsEyelidFlipRT(Control rt)
    {
        if (EyelidFlipParts == null) return false;
        foreach (var p in EyelidFlipParts)
            if (p?.RT == rt) return true;
        return false;
    }

    private static int GuessEyeGroup(Control ctrl)
    {
        string n = ctrl?.Name ?? "";
        return n.TrimEnd().EndsWith("2") ? 2 : 1;
    }

    // ════════════════════════════════════════════════════════════
    //  公開 API
    // ════════════════════════════════════════════════════════════

    public void CloseEyes()
    {
        ClearEyeTargets();
        _expressionClosed = true; _expressionClosed2 = false; _expressionClosed3 = false;
        _expressionHalfClosed = false; _expressionHalfClosed2 = false;
        _targetClose = true;
    }

    public void OpenEyes()
    {
        ClearEyeTargets();
        _expressionClosed = false; _expressionClosed2 = false; _expressionClosed3 = false;
        _expressionHalfClosed = false; _expressionHalfClosed2 = false;
        _targetClose = false; _semenCloseL = false; _semenCloseR = false;
    }

    public void SemenCloseLeft()  => _semenCloseL = true;
    public void SemenCloseRight() => _semenCloseR = true;
    public void SemenOpenLeft()   => _semenCloseL = false;
    public void SemenOpenRight()  => _semenCloseR = false;
    public void SemenOpenBoth()   { _semenCloseL = false; _semenCloseR = false; }

    /// <summary>進房間套用預設表情後呼叫：眼睛動畫直接跳到目標，不播放「睜眼→閉眼」的過渡。</summary>
    public void SnapToTarget()
    {
        float target;
        if (_expressionClosed || _expressionClosed2 || _expressionClosed3) target = 1f;
        else if (_expressionHalfClosed || _expressionHalfClosed2) target = HalfCloseAmount;
        else target = _targetClose ? 1f : 0f;
        _t = target;
        if (_exprTargetL >= 0f) _exprTL2 = _exprTargetL;
        if (_exprTargetR >= 0f) _exprTR2 = _exprTargetR;
        Apply(_t);
    }

    public void SetClosed(bool closed)
    {
        ClearEyeTargets();
        _expressionClosed = closed; _expressionClosed2 = false; _expressionClosed3 = false;
        _expressionHalfClosed = false; _expressionHalfClosed2 = false;
        _targetClose = closed;
    }

    public void SetClosed2(bool closed)
    {
        ClearEyeTargets();
        _expressionClosed2 = closed; _expressionClosed = false; _expressionClosed3 = false;
        _expressionHalfClosed = false; _expressionHalfClosed2 = false;
        _targetClose = closed;
    }

    /// <summary>全閉3：ZZZ 睡覺表情專用，半閉→全閉段翻轉的是眼皮（EyelidFlipParts），
    /// 不是全閉2的睫毛（ClosedFlipParts）。</summary>
    public void SetClosed3(bool closed)
    {
        ClearEyeTargets();
        _expressionClosed3 = closed; _expressionClosed = false; _expressionClosed2 = false;
        _expressionHalfClosed = false; _expressionHalfClosed2 = false;
        _targetClose = closed;
    }

    public void SetHalfClosed(bool half)
    {
        ClearEyeTargets();
        _expressionHalfClosed = half; _expressionClosed = false;
        _expressionClosed2 = false; _expressionClosed3 = false; _expressionHalfClosed2 = false;
    }

    public void SetHalfClosed2(bool half)
    {
        ClearEyeTargets();
        _expressionHalfClosed2 = half; _expressionHalfClosed = false;
        _expressionClosed = false; _expressionClosed2 = false; _expressionClosed3 = false;
    }

    public void SetEyeTargets(float leftTarget, float rightTarget)
    {
        _exprTargetL = Mathf.Clamp(leftTarget, 0f, 1f);
        _exprTargetR = Mathf.Clamp(rightTarget, 0f, 1f);
        _expressionClosed = false; _expressionClosed2 = false; _expressionClosed3 = false;
        _expressionHalfClosed = false; _expressionHalfClosed2 = false;
        _targetClose = false;
    }

    public void ClearEyeTargets() { _exprTargetL = -1f; _exprTargetR = -1f; }

    public void StartBlinking()
    {
        _blinkRunning = true;
        _ = BlinkLoop();
    }

    public void StopBlinking() => _blinkRunning = false;

    // ════════════════════════════════════════════════════════════
    //  眨眼協程
    // ════════════════════════════════════════════════════════════

    private async Task BlinkLoop()
    {
        while (_blinkRunning && IsInsideTree())
        {
            await DelaySeconds(BlinkInterval1);
            if (!_blinkRunning) break;
            await BlinkOnce();

            await DelaySeconds(BlinkInterval2);
            if (!_blinkRunning) break;
            await BlinkOnce();

            await DelaySeconds(BlinkInterval3);
            if (!_blinkRunning) break;
            await BlinkOnce();
            await DelaySeconds(DoubleBlinkGap);
            await BlinkOnce();
        }
    }

    private async Task BlinkOnce()
    {
        if (_expressionClosed || _expressionClosed2 || _expressionClosed3) return;
        _targetClose = true;
        await DelaySeconds(BlinkCloseTime);
        // 眨眼途中如果表情剛好換成閉眼（插入／拔出時常常這樣），不能無條件睜開，會把閉眼表情蓋掉、
        // 眼睛在不該睜開的時候睜開，閉眼和睜眼動畫還同時在跑（看起來像多一雙眼睛）。回到「目前表情要求的狀態」。
        _targetClose = _expressionClosed || _expressionClosed2 || _expressionClosed3;
        float openWait = AnimSpeed > 0f ? 1f / AnimSpeed : 0f;
        await DelaySeconds(openWait);
    }

    private async Task DelaySeconds(float sec)
    {
        if (sec <= 0f) return;
        await ToSignal(GetTree().CreateTimer(sec), SceneTreeTimer.SignalName.Timeout);
    }

    // ════════════════════════════════════════════════════════════
    //  Apply
    // ════════════════════════════════════════════════════════════

    private float GetEffectiveT(float globalT, int eyeGroup)
    {
        if (eyeGroup == 1) return Mathf.Max(_exprTargetL >= 0f ? _exprTL2 : globalT, _tL);
        if (eyeGroup == 2) return Mathf.Max(_exprTargetR >= 0f ? _exprTR2 : globalT, _tR);
        return Mathf.Max(globalT, Mathf.Max(_tL, _tR));
    }

    private void Apply(float t)
    {
        // 統一疊加的「頭部呼吸」Y 偏移，讓眼睛跟著頭一起有自然的起伏（見 BreathOffsetY 欄位說明）。
        Vector2 breathV = new Vector2(0f, BreathOffsetY) + ShakeOffset;

        foreach (var p in SlideParts ?? Array.Empty<SlidePart>())
        {
            if (p?.RT == null) continue;

            // 全閉3（ZZZ 眼皮翻轉）啟用時，凡是同時掛在 EyelidFlipParts 裡的部位（眼皮/眼皮2），
            // 這裡的位移／旋轉動畫要整個跳過，改成固定釘在 OpenPos，不能再讓它一邊滑動位置
            // 一邊做翻轉縮放——使用者明確要求「以物件中心為錨點，原地收縮成一條線再翻轉過來」，
            // 如果同時還套用 SlideParts 的位移，物件的錨點（中心）本身就會一直在移動，
            // 看起來就不會是「原地翻轉」，而是「一邊滑一邊變形」，跟要的效果不一樣。
            // 位移/旋轉這裡交給下面 ApplyFlip(EyelidFlipParts, ...) 全權處理縮放，
            // 該部位維持在原始位置不動即可。
            if (_expressionClosed3 && IsEyelidFlipRT(p.RT))
            {
                p.RT.Position = p.OpenPos + breathV;
                if (!Mathf.IsZeroApprox(p.OpenRotateZ - p.RT.RotationDegrees))
                    p.RT.RotationDegrees = p.OpenRotateZ;
                continue;
            }

            float et = GetEffectiveT(t, p.EyeGroup);

            // ZZZ（全閉3）：眼皮不照一般閉眼往下滑，改停在翻轉後的睫毛正上方（見 ZzzEyelidOffset 說明）
            if (_expressionClosed3 && p.RT.Name.ToString().Contains("眼皮"))
            {
                Vector2 zOff = p.EyeGroup == 2 ? ZzzEyelidOffset2 : ZzzEyelidOffset1;
                p.RT.Position = p.OpenPos.Lerp(p.OpenPos + zOff, et) + breathV;
                if (!Mathf.IsZeroApprox(p.OpenRotateZ - p.RT.RotationDegrees))
                    p.RT.RotationDegrees = p.OpenRotateZ;
                continue;
            }

            // Unity Y 朝上 → Godot Y 朝下，CloseOffsetY 在 Unity 是負值=下移，Godot 裡正值=下移
            // ClosedExtraOffsetY：額外的「閉眼時整體再往下」偏移量（目前只有 ZZZ 表情會用到）。
            float groupFixY = p.EyeGroup == 1 ? ClosedOffsetYGroup1 : p.EyeGroup == 2 ? ClosedOffsetYGroup2 : 0f;
            Vector2 closePos = p.OpenPos + new Vector2(p.CloseOffsetX, -p.CloseOffsetY + ClosedExtraOffsetY + groupFixY);
            p.RT.Position = p.OpenPos.Lerp(closePos, et) + breathV;

            // ClosedExtraRotateZ：額外的「閉眼時垂下」旋轉角度（目前只有 ZZZ 表情會用到）。
            // 群組1／群組2 用相反正負號鏡射，讓左右眼對稱地往外側下垂。
            float extraRotSign = p.EyeGroup == 2 ? -1f : 1f;
            float totalCloseRotate = p.CloseRotateZ + ClosedExtraRotateZ * extraRotSign;
            // 注意：Godot 的 Mathf.LerpAngle 是在「弧度」單位下做最短路徑角度內插，
            // 這裡的角度全部是「度」（RotationDegrees），如果直接把度數丟進 LerpAngle
            // 會被誤當成弧度計算，結果差非常多（例如 30 度丟進去，算出來的「度」只剩
            // 不到 2 度，幾乎看不出旋轉——這正是之前「怎麼調角度都看不出旋轉效果」的真正原因）。
            // 這裡的角度變化量很小、不會超過 ±180 度，不需要處理「跨越 360 度」的最短路徑問題，
            // 直接用一般的線性內插 Mathf.Lerp 即可，不要用 LerpAngle。
            if (!Mathf.IsZeroApprox(totalCloseRotate))
                p.RT.RotationDegrees = Mathf.Lerp(p.OpenRotateZ, p.OpenRotateZ + totalCloseRotate, et);
            else if (!Mathf.IsZeroApprox(p.OpenRotateZ - p.RT.RotationDegrees))
                p.RT.RotationDegrees = p.OpenRotateZ; // 還原（上一幀可能還有殘留的旋轉）
        }

        foreach (var p in MaskParts ?? Array.Empty<MaskPart>())
        {
            if (p?.RT == null) continue;
            float et = GetEffectiveT(t, p.EyeGroup);
            var sz = p.RT.Size;
            sz.Y = Mathf.Lerp(p.OpenHeight, p.CloseHeight, et);
            p.RT.Size = sz;
            // 遮罩容器本身原本沒有動 Position，這裡加上呼吸偏移，讓整個眼白+眼球裁切容器
            // 也跟著頭一起浮動（容器內的眼白/眼球是相對容器的本地座標，不用另外處理）。
            p.RT.Position = p.OpenPos + breathV;
        }

        foreach (var p in TearParts ?? Array.Empty<TearPart>())
        {
            if (p?.RT == null) continue;
            float et   = GetEffectiveT(t, p.EyeGroup);
            float tearT = Mathf.InverseLerp(HalfCloseAmount, 1f, et);
            var sz = p.RT.Size;
            sz.X = p.OpenWidth + p.ExpandX * tearT;
            p.RT.Size = sz;
            var pos = p.RT.Position;
            pos.X = p.OpenPos.X - p.ExtraLeft;
            pos.Y = p.OpenPos.Y - p.OffsetY * tearT + p.ExtraOffsetY; // Unity Y 朝上→負轉正；ExtraOffsetY 是固定微調
            p.RT.Position = pos + breathV;
        }

        if (_expressionClosed2)
            ApplyFlip(ClosedFlipParts, Mathf.InverseLerp(HalfCloseAmount, 1f, t), breathV);
        else
            ResetFlip(ClosedFlipParts);

        if (_expressionClosed3)
            ApplyFlip(EyelidFlipParts, Mathf.InverseLerp(HalfCloseAmount, 1f, t), breathV);
        else
            ResetFlip(EyelidFlipParts);

        if (_expressionHalfClosed2)
        {
            float flipT = Mathf.InverseLerp(0f, HalfCloseAmount, t);
            ApplyFlip(HalfFlipParts, flipT, breathV);
            ApplyFlip(HalfFlipCounterParts, flipT, breathV);
        }
        else
        {
            ResetFlip(HalfFlipParts);
            ResetFlip(HalfFlipCounterParts);
        }
    }

    private static void ApplyFlip(FlipPart[] parts, float flipT, Vector2 breathV)
    {
        if (parts == null) return;
        foreach (var p in parts)
        {
            if (p?.RT == null) continue;
            // 同樣的單位問題：ClosedRotateZ 是「度」，這裡改用 Mathf.Lerp 而不是
            // Mathf.LerpAngle（LerpAngle 吃的是弧度，誤用會導致實際旋轉角度只剩零頭，
            // 詳見上面 SlideParts 迴圈那段的說明）。
            if (!Mathf.IsZeroApprox(p.ClosedRotateZ))
                p.RT.RotationDegrees = Mathf.Lerp(p.OpenRotateZ, p.OpenRotateZ + p.ClosedRotateZ, flipT);
            var s = p.RT.Scale;
            s.Y = Mathf.Lerp(p.OpenScale.Y, -p.OpenScale.Y, flipT);
            p.RT.Scale = s;
            if (!Mathf.IsZeroApprox(p.OffsetY))
            {
                var pos = p.RT.Position;
                pos.Y = p.OpenPos.Y - p.OffsetY * flipT + breathV.Y;   // 要補上呼吸/抖動/上提位移，不然閉眼翻轉時會不跟著頭動
                p.RT.Position = pos;
            }
            if (!Mathf.IsZeroApprox(p.OffsetX))
            {
                var pos = p.RT.Position;
                pos.X = p.OpenPos.X + p.OffsetX * flipT + breathV.X; // 正值＝往右移，負值＝往左移；補上位移同上
                p.RT.Position = pos;
            }
            // 壓縮到接近一條線（flipT>=0.5，scaleY 已經過了 0 那個點）之後直接隱藏，
            // 不繼續播放鏡射後的翻轉畫面——只有 HideWhenFlipped=true 的部位（眼角）會這樣，
            // 睫毛等其他部位 HideWhenFlipped 預設 false，維持原本行為不受影響。
            if (p.HideWhenFlipped)
                p.RT.Visible = flipT < 0.5f;
        }
    }

    private static void ResetFlip(FlipPart[] parts)
    {
        if (parts == null) return;
        foreach (var p in parts)
        {
            if (p?.RT == null) continue;
            p.RT.Scale = p.OpenScale;
            // 【BUG 修正】原本這裡會把有 OffsetX/OffsetY 的部位每一幀強制拉回睜眼位置 OpenPos。
            // ResetFlip 在「沒有翻轉」時每一幀都會被呼叫，而 ZZZ 用的 EyelidFlipParts 裡
            // 睫毛2／眼角2 有 OffsetY=-20、睫毛1／眼角1 有 OffsetX=-3 →
            // 一般閉眼（高潮、做愛等）時，畫面左眼的睫毛和眼角永遠被釘在睜眼高度：
            // 閉不下去、呼吸和高潮/插入抖動也都不會動，看起來就是左右眼一高一低。
            // 這些部位同時都是 SlideParts，位置本來就由上面的 SlideParts 迴圈每幀計算，
            // 這裡不需要也不應該再改位置。
            if (!Mathf.IsZeroApprox(p.ClosedRotateZ))
                p.RT.RotationDegrees = p.OpenRotateZ;
            if (p.HideWhenFlipped)
                p.RT.Visible = true; // 還原成睜眼/非翻轉狀態時一定要重新顯示，避免卡在隱藏狀態
        }
    }
}
