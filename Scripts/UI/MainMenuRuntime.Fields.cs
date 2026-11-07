using Godot;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 主選單執行時建構器（Unity MainMenuRuntime 的 Godot 移植版）
///
/// 【移植對照】
///   MonoBehaviour        → Control（本節點即畫布根，靠 project.godot 的 canvas_items 縮放）
///   [SerializeField]     → [Export]
///   UnityEngine.UI.Text  → Label
///   UnityEngine.UI.Image → TextureRect / ColorRect
///   AudioSource          → AudioStreamPlayer
///   StartCoroutine       → async/await + CancellationToken
///   Resources.Load       → ResourceLoader.Load（需完整副檔名路徑）
///
/// 【重要】Unity UI 的 Y 軸朝上，Godot 的 Y 軸朝下。
///   所有 Inspector 座標常數保持與 Unity 相同，
///   由 SetRT / Img / Txt 等輔助方法統一翻轉 Y，避免逐一改數值。
/// </summary>
public partial class MainMenuRuntime : Control
{
    // ═══════════════════════════════════════════════════════════
    //  設計解析度（對應 Unity CanvasScaler referenceResolution）
    // ═══════════════════════════════════════════════════════════
    public const float RefWidth  = 1920f;
    public const float RefHeight = 1080f;

    // ═══════════════════════════════════════════════════════════
    //  面板節點
    // ═══════════════════════════════════════════════════════════
    private Control _mainPanel;
    private Control _settingsPanel;
    private Control _skipPanel;
    private Control _menuBtnContainer;   // 主選單按鈕容器（統一隱藏/顯示）
    /// <summary>為 true 時，下次進入主選單直接顯示開始畫面（由遊戲中的「返回主畫面」設定）。</summary>
    public static bool SkipIntroOnce;

    private Control _logoPanel;
    private Control _dialoguePanel;

    // ═══════════════════════════════════════════════════════════
    //  對話相關節點
    // ═══════════════════════════════════════════════════════════
    private Label       _speakerText;
    private Label       _contentText;
    private TextureRect _bgImage;
    private TextureRect _bgImage2;   // 第二背景圖層（用於交叉淡入）

    // CG 圖層（0~10）
    private TextureRect[] _cgImages = new TextureRect[DialogueSystem.CG_LAYER_COUNT];
    private TextureRect   _nameBarImage;

    // 角色圖層
    private TextureRect _charBodyImage;
    private TextureRect _charFaceImage;
    private TextureRect _char2Image;      // 第二角色圖層（如 yellow mao，身體表情合一）

    // ═══════════════════════════════════════════════════════════
    //  標題圖片位置（TitleName）
    // ═══════════════════════════════════════════════════════════
    [ExportGroup("標題圖片位置")]
    [Export] public Vector2 TitlePos  = new Vector2(540, 320);
    [Export] public Vector2 TitleSize = new Vector2(650, 350);
    /// <summary>標題圖片不受父物件裁切</summary>
    [Export] public bool TitleOverflow = false;

    // ═══════════════════════════════════════════════════════════
    //  開場動畫場景設定
    // ═══════════════════════════════════════════════════════════

    // ── 場景1: bg_PlayerRoom — 放大偏左，往右平移 ──
    [ExportGroup("開場動畫 場景1")]
    [Export] public float   Scene1Scale    = 1.5f;
    [Export] public Vector2 Scene1Start    = new Vector2(-200f, 0f);
    [Export] public Vector2 Scene1End      = new Vector2(200f, 0f);
    [Export] public float   Scene1Duration = 4f;

    // ── 場景2: bg_LinaiRoom(day) — 放大偏右，往左平移 ──
    [ExportGroup("開場動畫 場景2")]
    [Export] public float   Scene2Scale    = 1.5f;
    [Export] public Vector2 Scene2Start    = new Vector2(200f, 0f);
    [Export] public Vector2 Scene2End      = new Vector2(-200f, 0f);
    [Export] public float   Scene2Duration = 4f;

    // ── 場景3: bg_LivingRoom — 中央拉遠，再往右平移 ──
    [ExportGroup("開場動畫 場景3")]
    [Export] public float   Scene3ScaleStart = 1.8f;                    // 起始放大
    [Export] public float   Scene3ScaleEnd   = 1.0f;                    // 拉遠到的倍率
    [Export] public float   Scene3ZoomDur    = 2f;                      // 拉遠秒數
    [Export] public Vector2 Scene3PanStart   = new Vector2(0f, 0f);     // 拉遠時的位置
    [Export] public Vector2 Scene3PanEnd     = new Vector2(-150f, 0f);   // 往右平移終點
    [Export] public float   Scene3PanDur     = 2f;                      // 平移秒數（同時交叉淡入 ApartmentDoor）

    // ── 場景4: bg_ApartmentDoor — 走路靠近 + 鏡頭拉近 + CG從下到上 ──
    [ExportGroup("開場動畫 場景4")]
    [Export] public float   Scene4StartScale = 1f;                          // 場景4初始放大倍率
    [Export] public float   Scene4WalkScale  = 1.5f;                        // 走路目標放大倍率
    [Export] public Vector2 Scene4WalkTarget = new Vector2(350f, -100f);    // 走路目標位移
    [Export] public float   Scene4WalkDur    = 2f;                          // 走路秒數
    [Export] public int     Scene4WalkSway   = 5;                           // 搖擺強度(1~5)
    [Export] public float   Scene4ZoomScale  = 3f;                          // 鏡頭拉近倍率
    [Export] public Vector2 Scene4ZoomTarget = new Vector2(350f, -100f);    // 鏡頭拉近位移
    [Export] public float   Scene4ZoomDur    = 2f;                          // 鏡頭拉近秒數
    [Export] public string  Scene4CGName     = "linai knock 1";             // CG 圖片名稱
    [Export] public float   Scene4CGScale    = 1.755f;                          // CG 放大倍率
    [Export] public Vector2 Scene4CGStart    = new Vector2(0f, 250f);      // CG 起始位移（底下）
    [Export] public Vector2 Scene4CGEnd      = new Vector2(0f, -50f);         // CG 結束位移（往上）
    [Export] public float   Scene4CGPanDur   = 2f;                          // CG 往上秒數
    [Export] public string  Scene4CGBG       = "linai knock background";    // CG 背景圖（CG 下層）

    // ── 場景5: CG節奏切換 — 在scene4 CG停頓後，瞬間切換scale共4次 ──
    [ExportGroup("開場動畫 場景5")]
    [Export] public float   Scene5Wait     = 1f;                    // scene4結束後等待多久才開始scene5
    [Export] public float   Scene5Scale1   = 1.872f;                  // 第1幀 scale
    [Export] public string  Scene5CG1      = "linai knock 2";       // 第1幀圖片
    [Export] public Vector2 Scene5Offset1  = Vector2.Zero;          // 第1幀位移
    [Export] public float   Scene5Scale2   = 1.989f;                  // 第2幀 scale
    [Export] public string  Scene5CG2      = "linai knock 3";       // 第2幀圖片
    [Export] public Vector2 Scene5Offset2  = Vector2.Zero;          // 第2幀位移
    [Export] public float   Scene5Scale3   = 2.106f;                  // 第3幀 scale
    [Export] public string  Scene5CG3      = "linai knock 4";       // 第3幀圖片
    [Export] public Vector2 Scene5Offset3  = Vector2.Zero;          // 第3幀位移
    [Export] public float   Scene5Scale4   = 2.34f;                  // 第4幀 scale
    [Export] public string  Scene5CG4      = "linai knock 5";       // 第4幀圖片
    [Export] public Vector2 Scene5Offset4  = new Vector2(100f, 0f); // 第4幀位移
    [Export] public float   Scene5Interval = 0.9f;                  // 每幀之間的等待秒數

    // ── 場景6: 黑屏 + TitleName 放大縮小 → 主選單 ──
    [ExportGroup("開場動畫 場景6")]
    [Export] public float   Scene6Wait            = 0.2f;                          // scene5結束後等待多久黑屏
    [Export] public Vector2 Scene6TitlePos        = new Vector2(-100f, -40f);         // TitleName 位置(X,Y)
    [Export] public Vector2 Scene6TitleScaleStart = new Vector2(2.5f, 2.5f);     // TitleName 初始放大(X,Y)
    [Export] public Vector2 Scene6TitleScaleEnd   = new Vector2(2f, 2f);         // TitleName 縮小到(X,Y)
    [Export] public float   Scene6ShrinkDelay     = 0f;                          // TitleName 出現後等多久才開始縮小
    [Export] public float   Scene6ShrinkDur       = 2.5f;                          // 縮小動畫秒數
    [Export] public float   Scene6HoldDur         = 0.8f;                          // 縮小完成後等多久再閃光

    // ═══════════════════════════════════════════════════════════
    //  開場BGM設定
    // ═══════════════════════════════════════════════════════════
    [ExportGroup("開場BGM設定")]
    [Export] public float BgmStartTime     = 3f;    // 音樂從第幾秒開始播放
    [Export] public float BgmFadeInDur     = 4f;    // 淡入秒數
    [Export] public float BgmVolume        = 1f;    // 最終音量(0~1)
    /// <summary>在指定秒數時開始淡出（0=不淡出）</summary>
    [Export] public float BgmFadeOutAt     = 0f;    // 音樂播到第幾秒開始淡出（0=不淡出）
    [Export] public float BgmFadeOutDur    = 2f;    // 淡出秒數
    /// <summary>淡出結束後的音量（0=靜音，可設小音量保留背景）</summary>
    [Export] public float BgmFadeOutTarget = 0f;    // 淡出目標音量

    // ═══════════════════════════════════════════════════════════
    //  跳過開場設定
    // ═══════════════════════════════════════════════════════════
    [ExportGroup("跳過開場設定")]
    [Export] public float SkipMusicTime = 30.5f;      // 跳過後音樂跳到的秒數

    // ═══════════════════════════════════════════════════════════
    //  設定面板 - 文字描邊
    // ═══════════════════════════════════════════════════════════
    [ExportGroup("設定面板 文字描邊")]
    [Export] public float SetTextOutline      = 0f;
    [Export] public Color SetTextOutlineColor = new Color(0f, 0f, 0f, 1f);

    // ═══════════════════════════════════════════════════════════
    //  設定面板 - 佈局
    // ═══════════════════════════════════════════════════════════
    [ExportGroup("設定面板 佈局")]
    [Export] public Vector2 SetPanelPos     = new Vector2(0, 0);        // 背景框位置
    [Export] public Vector2 SetPanelSize    = new Vector2(780, 860);    // 背景框大小
    [Export] public Vector2 SetHeadPos      = new Vector2(0, 350);      // 標題欄位置
    [Export] public Vector2 SetHeadSize     = new Vector2(420, 65);     // 標題欄大小
    [Export] public float   SetRowW         = 560f;                     // 列寬度
    [Export] public float   SetRowH         = 46f;                      // 列高度
    [Export] public float   SetRowStartY    = 240f;                     // 第一列 Y
    [Export] public float   SetRowGap       = 64f;                      // 列間距
    [Export] public Vector2 SetBtnApplyPos  = new Vector2(-130, -350);  // 套用按鈕位置
    [Export] public Vector2 SetBtnApplySize = new Vector2(200, 72);     // 套用按鈕大小
    [Export] public Vector2 SetBtnExitPos   = new Vector2(130, -350);   // 退出按鈕位置
    [Export] public Vector2 SetBtnExitSize  = new Vector2(200, 72);     // 退出按鈕大小

    // ═══════════════════════════════════════════════════════════
    //  閃光中圖片移動設定
    // ═══════════════════════════════════════════════════════════
    [ExportGroup("閃光中圖片移動")]
    /// <summary>閃光期間背景是否繼續移動到指定位置</summary>
    [Export] public bool    FlashMoveEnabled = true;
    [Export] public Vector2 FlashMoveTarget  = new Vector2(100f, 0f);   // 閃光期間移動目標
    [Export] public float   FlashMoveDur     = 1.5f;                    // 移動秒數

    // ═══════════════════════════════════════════════════════════
    //  隨機標題圖片滑動（跳過後）
    // ═══════════════════════════════════════════════════════════
    [ExportGroup("隨機標題圖片滑動")]
    [Export] public Vector2 SlideStart      = new Vector2(-1800f, 0f);   // 圖片起始位置
    [Export] public Vector2 SlideEnd        = new Vector2(-300f, -100f);    // 圖片結束位置
    [Export] public float   SlideFadeIn     = 1.5f;                     // 淡入持續秒數
    [Export] public float   SlideDuration   = 1.5f;                      // 滑動持續秒數
    /// <summary>開啟：點擊畫面可切換圖片（淡出淡入，不滑動）</summary>
    [Export] public bool    SlideClickSwitch = false;
    [Export] public float   SlideSwitchFade  = 0.5f;                    // 點擊切換的淡出淡入秒數

    // ═══════════════════════════════════════════════════════════
    //  按鈕位置（X, Y座標）
    // ═══════════════════════════════════════════════════════════
    [ExportGroup("主選單按鈕位置")]
    [Export] public Vector2 BtnStartPos   = new Vector2(580, 50f);   // 左
    [Export] public Vector2 BtnLoadPos    = new Vector2(580, -70f);   // 右
    [Export] public Vector2 BtnSettPos    = new Vector2(580, -190f);  // 左
    [Export] public Vector2 BtnQuitPos    = new Vector2(580, -310f);  // 右
    [Export] public Vector2 BtnGalleryPos = new Vector2(580, -430f);  // 左

    // ═══════════════════════════════════════════════════════════
    //  標題與滑動圖節點
    // ═══════════════════════════════════════════════════════════
    private Control     _titleGO;
    private TextureRect _titleRT;

    private TextureRect _slideRT;
    private TextureRect _slideImg;
    private CancellationTokenSource _slideCts;      // 取代 Unity 的 Coroutine 控制代碼
    private Vector2 _prevSlideStart, _prevSlideEnd;
    private float   _prevSlideDuration;
    private bool    _slideParamsDirty;
    private Texture2D[] _randomTextures;            // 快取所有隨機圖片
    private bool    _slideSwitching;                // 正在切換中，防止重複點擊
    private Control _introCG;                       // 開場動畫 CG 物件引用
    private Control _cgBgGO;                        // 開場動畫 CG 背景物件引用

    // 主選單五顆按鈕
    private Control _btnStartRT, _btnLoadRT, _btnSettRT, _btnQuitRT;

    // 圖鑒房
    private GalleryScreen _galleryScreen;
    private Control       _btnGalleryRT;

    // ═══════════════════════════════════════════════════════════
    //  設定面板節點
    // ═══════════════════════════════════════════════════════════
    private Control _setBgRT, _setHeadRT;
    private Control _setLangBtnRT, _setLangBtn2RT, _setLangBtn3RT;
    private Control _setSnd1RT, _setSnd2RT, _setSnd3RT;
    private Control _setBtn7RT, _setBtn8RT;
    private Label   _setHeadTxt, _setLangBtnTxt, _setLangBtn2Txt, _setLangBtn3Txt;
    private Label   _setBtn1Txt, _setBtn2Txt, _setBtn3Txt;
    private Label   _setBtn7Txt, _setBtn8Txt;

    // 設定面板的滑桿與選項索引
    private HSlider  _settBgmSlider, _settSeSlider, _settVoiceSlider;
    private int[]    _settResIdx, _settLangIdx, _settSpdIdx;
    private string[] _settResOpts, _settLangOpts, _settSpdOpts;

    // ═══════════════════════════════════════════════════════════
    //  音訊播放器（Unity AudioSource → Godot AudioStreamPlayer）
    // ═══════════════════════════════════════════════════════════
    private AudioStreamPlayer _bgmSource;
    private bool _bgmFading = false;  // BGM 正在淡入/淡出時為 true，避免 ApplyVolumes 覆蓋進行中的淡出
    private AudioStreamPlayer _seSource;
    private AudioStreamPlayer _meSource;

    // 背景動畫控制
    private CancellationTokenSource _bgAnimCts;

    // ═══════════════════════════════════════════════════════════
    //  淡入淡出遮罩與對話框
    // ═══════════════════════════════════════════════════════════
    private ColorRect _fadeImage;
    private bool      _screenBlack;                              // 目前是否黑屏中
    private Color     _screenFadeColor = Colors.Black;           // fadeout 使用的顏色（fadein 自動對應）
    private Control   _dialogueBox;                              // 對話框底板（控制顯隱用）
    private Control   _nameBarGO;                                // 名字框 GO
    private Control   _btnBarGO;                                 // 按鈕列 GO

    // ═══════════════════════════════════════════════════════════
    //  對話播放狀態
    // ═══════════════════════════════════════════════════════════
    private bool _isTyping;
    private bool _skipTyping;
    private bool _waitingClick;
    private bool _lockClick;      // autoNext 鎖定點擊（必須等計時結束）
    private bool _fastForward;    // 快速播放模式
    private bool _skipAll;        // 跳過全部
    private int  _autoPlaySpeed;  // 0=關 1=X1(2s) 2=X2(1s) 3=X3(0.5s) 4=X4(0.15s)
    private bool _autoAdvance;    // 自動播放觸發旗標

    // 滑鼠長按偵測
    private float _mouseHoldTime;
    private bool  _mouseHoldActive;

    // 快轉／自動播放按鈕文字
    private Label _fastFwdBtnTxt;
    private Label _autoPlayBtnTxt;

    // ═══════════════════════════════════════════════════════════
    //  對話頂部按鈕列
    // ═══════════════════════════════════════════════════════════
    private DialogueTopBtnConfig _topBtnCfg;
    private Control _tbAuto, _tbFF, _tbSkip, _tbSave, _tbLoad;
    private Label   _tbAutoTxt, _tbFFTxt, _tbSkipTxt, _tbSaveTxt, _tbLoadTxt;

    // 選擇題結果（-1 = 等待中）
    private int _choiceResult = -1;

    // 主要協程的取消權杖（場景離開時中止所有 async 流程）
    private CancellationTokenSource _lifeCts = new CancellationTokenSource();

    // ═══════════════════════════════════════════════════════════
    //  生命週期
    // ═══════════════════════════════════════════════════════════

    public override void _Ready()
    {
        // 本節點作為全螢幕畫布根
        // Unity 的 Canvas + CanvasScaler 由 project.godot 的
        // stretch/mode=canvas_items + aspect=keep 取代，不需程式設定
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Pass;

        // Unity 需要 EventSystem / GraphicRaycaster / AudioListener，
        // Godot 內建輸入與音訊系統，不需額外建立

        // 初始化音樂播放器
        _bgmSource = new AudioStreamPlayer { Name = "BgmSource", Bus = "Master" };
        AddChild(_bgmSource);

        _seSource = new AudioStreamPlayer { Name = "SeSource", Bus = "Master" };
        AddChild(_seSource);

        _meSource = new AudioStreamPlayer { Name = "MeSource", Bus = "Master" };
        AddChild(_meSource);

        // 訂閱音量變更事件（Unity 的 OnEnable）
        SettingsManager.OnVolumeChanged += ApplyVolumes;

        // 套用 SettingsManager 儲存的音量
        ApplyVolumes();

        Build();

        // 開發者模式（密技召喚 + 解鎖後 F9 開關）現在由 autoload 的
        // DevModeManager 全域常駐處理，不用在每個場景各自建立監聽節點。

        // 從遊戲中「返回主畫面」→ 直接顯示開始畫面（不重播 LOGO 與開場動畫）
        if (SkipIntroOnce || DemoMode.Enabled)   // 試玩版：OP 直接跳到主畫面
        {
            SkipIntroOnce = false;
            if (DemoMode.Enabled)
            {   // 省略 LogoIntro 就沒有背景預載，這裡補上，之後進調教室的黑屏才不會太長
                foreach (var relPath in TrainingRoomRuntime.GetPreloadSpritePaths())
                    ResourceHelper.PreloadTexAny(relPath);
                ResourceLoader.LoadThreadedRequest("res://Scenes/TrainingRoom.tscn");
            }
            TitleDirect(_lifeCts.Token);
        }
        else
        {
            // 從 logo 開場（Unity: StartCoroutine(LogoIntro())）
            TaskLogger.Run(LogoIntro(_lifeCts.Token), "MainMenu.LogoIntro");
        }
    }

    public override void _ExitTree()
    {
        CleanupOpBgmBus();
        // 取消訂閱（Unity 的 OnDisable）
        SettingsManager.OnVolumeChanged -= ApplyVolumes;

        // 中止所有進行中的 async 流程
        _lifeCts?.Cancel();
        _slideCts?.Cancel();
        _bgAnimCts?.Cancel();
    }

    /// <summary>根據 SettingsManager 的值即時更新各播放器音量</summary>
    private void ApplyVolumes()
    {
        var sm = SettingsManager.Instance;
        if (sm == null) return;

        // Godot 音量以分貝計，需從線性值轉換
        if (IsInstanceValid(_bgmSource) && !_bgmFading) _bgmSource.VolumeDb = LinearToDb(sm.MusicVolume * BgmVolume);  // 使用者音量 × 場景音量，淡入/淡出期間不覆蓋
        if (IsInstanceValid(_seSource))  _seSource.VolumeDb  = LinearToDb(sm.SFXVolume);
        if (IsInstanceValid(_meSource))  _meSource.VolumeDb  = LinearToDb(sm.VoiceVolume);
    }

    /// <summary>線性音量(0~1) 轉分貝，0 視為靜音(-80dB)</summary>
    private static float LinearToDb(float linear)
    {
        if (linear <= 0.0001f) return -80f;
        return Mathf.LinearToDb(linear);
    }
}
