using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// TrainingRoomRuntime 的欄位宣告、圖層順序、靜態旗標。
/// 靜態旗標名稱刻意與 Unity 原始碼、以及 Godot 專案中既有呼叫端
/// （ApartmentRuntime / BathroomRuntime / LivingRoomRuntime / MainMenuRuntime.Dialogue.cs）
/// 完全一致，取代 LivingRoomRuntime.cs 底部原本的 stub class。
/// </summary>
public partial class TrainingRoomRuntime : Node
{
    // ── 跨場景靜態旗標（與 Unity 版、與既有呼叫端一致）───────────────
    /// <summary>ApartmentRuntime 設定，進入調教室時自動觸發 Day2 初次對話</summary>
    public static bool PendingDay2First { get; set; }
    /// <summary>BathroomRuntime 設定，進入調教室時自動觸發 Day3 初次對話</summary>
    public static bool PendingDay3First { get; set; }
    /// <summary>本晚有發生右手摸胸互動</summary>
    public static bool HadBreastTouch = false;
    /// <summary>摸胸舒服路線（true=舒服, false=不舒服）</summary>
    public static bool BreastComfortRoute = true;
    /// <summary>從 LivingRoom（Day3_Start）進來 → 第三晚</summary>
    public static bool PendingNight3 { get; set; }
    /// <summary>直接進入小遊戲，跳過一切 TrainingRoom 設定</summary>
    public static bool PendingMiniGameOnly { get; set; }

    // ── 圖層順序（底→頂）───────────────────────────────────────────
    // 與 Unity 原始碼 LayerOrder 的差異（依使用者要求調整）：
    //   • 移除 "_尾巴2"、"耳朵1"、"耳朵2" —— 是從別的專案帶過來的殘留素材，這個角色不需要。
    //   • "手臂1"/"手臂2" 移到 "身體" 之前 —— 原順序手臂蓋在身體上面，畫面看起來是錯的；
    //     Unity 原始碼裡手臂在身體之後，可能是靠 ArmIK 在執行時動態調整 sibling 順序，
    //     這次沒有移植那段動態排序，所以先直接調整靜態順序讓手臂在身體下面。
    //   • 移除 "男人腿1"、"男人腿2" —— 使用者確認不再使用這兩個男性物件。
    //   • "男人肚子" 移到 "雞雞" 之後（原本在腿部那一段，比雞雞早很多）—— 使用者要求
    //     男人肚子要蓋在雞雞上面（插入視覺遮擋效果），且原本就只跟雞雞一起用
    //     OnJijiButtonPressed 顯示/隱藏，位置移動不影響顯示邏輯。
    static readonly string[] LayerOrder = new[]
    {
        "後髮-", "後髮1", "後髮2", "後髮3",
        "後發辮子1", "後發辮子2",
        // "_辮子1", "_辮子2",  ← 已移除：Unity 場景沒有這兩層（實際擺動的辮子是 後髮2/後髮3），
        //                        Godot 多顯示出來會變成兩層頭髮一起飄。

        "手臂1","手臂2",
        // 使用者反應畫面右邊那隻手（手臂1/手1 這組）目前疊在身體前面看起來很違和，
        // 應該要在身體後面。原本 手1/手2 是跟胸部/奶頭放在一起、統一畫在身體前面
        // （方便手貼在胸口的姿勢），但「手1」這側其實是要藏在身體後面的手，
        // 這裡把它單獨移到跟 手臂1/手臂2 同一批、身體之前繪製；手2 維持不動。
        "手1",

        "身體",
        "腿/腿1", "腿/腿2", "腿/腳1", "腿/腳2", "腿/足1", "腿/足2",
        // 使用者確認 Unity 原始碼裡 M 腿其實有獨立的一整套美術（Assets/Resources/Sprites/Meimei/m腿/
        // 資料夾），之前攤平匯出時因為跟一般腿撞到同樣的檔名（腿1.png/腳1.png/足1.png…）被蓋掉了，
        // 只留下一般腿那份，所以 M 腿模式打開後看起來完全沒變化。已經從 Unity 專案把這些圖重新複製
        // 過來、另存新檔名（腿1_M.png 等，避免再撞名），見下面 _extraSpritePaths 的對應。
        // 「m腿/腿底」是全新圖層（Unity 原始碼有、Godot 這邊 LayerOrder 之前沒收錄），M 腿獨有、
        // 一般腿沒有對應物，是大腿根部的底圖（避免張開時露出破綻），畫在其餘 m腿 肢體之下——
        // 整個 m腿 群組現在搬到「手2」之後畫（見下面），這裡只是先說明各圖層彼此的上下關係。

        "臉的陰影","臉",
        "表情/眼白","表情/眼白2",
        "表情/眼球1","表情/眼球2",
        "表情/眼皮","表情/眼皮2",
        "表情/睫毛1","表情/睫毛2",
        "表情/下睫毛1","表情/下睫毛2",
        "眼角1","眼角2",
        "表情/鼻子","表情/嘴巴","表情/腮紅",
        "笑","笑2","小嘴","吐舌","張嘴1","張嘴2","斜動嘴","抖動嘴","小嘴吐舌","小嘴巴",

        "胸1","胸2","奶頭1","奶頭2",
        "手2",

        // （M 腿整組圖層已搬到「衣服」之後，見下面——使用者要求 M 腿的物件都要在裙子上面。
        //   彼此的上下關係不變：腿底 → 小穴／肛門 → 腿1/腿2/腳/足。）

        // 小穴→內褲(衣服1)→衣服(衣服2)：內褲蓋在小穴上、衣服蓋在內褲上；三者都在前髮之下
        "腿/小穴",
        "內褲",
        "蝴蝶結",     // 內褲上的蝴蝶結（整張畫布對位，疊在內褲正上方）
        // 使用者要求：衣服要在 M 腿小穴與肚子（插入用疊層）上面 → 這兩層搬到衣服之前
        "m腿/腿底",   // 出血純色底：M 系列最底（在小穴下面）
        "m腿/小穴",
        "肚子",
        // 衣服：疊在所有身體/手/胸/腿之上，但在前髮之下（頭髮最上層），以身體頂端對齊
        // 衣服拆成 3 件：左右兩條帶子在本體「下面」（先畫），再畫本體
        "衣服/左帶","衣服/右帶",
        "衣服",
        "蝴蝶結2",    // 衣服上的蝴蝶結（疊在衣服正上方）
        // 使用者要求：M 腿的物件都要在裙子（衣服）上面 → 整組 m腿 從「手2」後面搬到「衣服」之後、前髮之前。
        // 這組本來就在手上面（使用者先前要求），搬到更後面後仍然在手上面。
        "m腿/腿1","m腿/腿2","m腿/腳1","m腿/腳2","m腿/足1","m腿/足2",
        "m腿/肛門",   // 肛門放在 M 腿最前面（腿／腳／足上面、前髮下面；原本小穴細節的位置）；mleg_extra 的「m腿/小穴細節」已停用（跟肛門.png 一樣）
        "m腿/內褲",   // M 腿專用內褲圖（內褲_M.png）：在 M 腿各層、衣服上面；M 腿打開且內褲沒脫才顯示（見 Cloth.cs UpdateClothBump）
        "表情/眼淚1","表情/眼淚2","表情/眼淚3","表情/眼淚4",   // 眼淚要在前髮下面
        "前髮",
        // 使用者要求眉毛要畫在前髮上面（之前眉毛排在前髮之前，會被前髮蓋住）。
        // Godot 的子節點繪製順序＝陣列順序，越後面加入的越晚畫、疊在越上層，
        // 所以只要把「眉毛1」「眉毛2」移到「前髮」後面即可，其餘臉部圖層順序不變。
        "眉毛1","眉毛2",
        "妹妹正面",

        "汗1","汗2","汗3","汗4","腿/汗1",
        // 原本寫成 "m腿_小穴"（底線）跟其他 m腿 圖層的 "/" 命名慣例不一致，也讓 basename
        // 規則抓不到對應檔案（見下面 _extraSpritePaths 說明），改成 "m腿/小穴" 統一慣例；
        // Effects.cs 的 _breathLowerSet 裡同一個字串也要一起改，否則兩邊對不上。
        "表情/小穴水","表情/高潮水",
        // 插入用疊層（Unity 場景物件名稱 → 實際圖檔）：肚子=charuduzi、洞=xiaoxuekaidi、
        // 開1=xiaoxuekai、插入=xiaoxuekai2。之前在 Unity 裡用中文名字找不到，是因為場景檔把
        // 名稱存成 \uXXXX 跳脫字元、圖檔本身又是拼音檔名。
        // 順序對照 Unity：洞 在雞雞下面，開1／插入 在雞雞上面。全部預設隱藏，由 Jiji.cs 控制。
        // 使用者最新要求：洞在雞雞下面、小穴上面（小穴在前面的圖層，已經比這裡早畫）；開1 在雞雞上面。
        "洞",
        "雞雞",
        "精液",   // 雞雞上的精液：畫在雞雞上面、插入下面；射精後才出現，跟雞雞一起被裁切（見 Jiji.cs）
        "開上","開1","插入",   // 開上＝xiaoxuekaishang（目前已由開1 取代，預設不顯示）
        "男人肚子",   // 男人肚子：預設隱藏，只跟雞雞一起顯示/隱藏
    };

    /// <summary>回傳 BuildCharacter()／BuildBackground() 實際會用到的所有貼圖資源路徑
    /// （不含副檔名），跟這兩個方法內部解析 relPath 的規則完全一致（_extraSpritePaths 優先，
    /// 查不到才落回 "Sprites/" + basename）。
    ///
    /// 用途：使用者反應從主選單點擊「開始」到進入調教室之間的黑畫面等太久——原因是
    /// LayerOrder 有 70 幾層，BuildCharacter() 逐層呼叫 ResourceHelper.LoadTexAny()
    /// 同步讀檔＋解壓縮，全部擠在 ChangeSceneToFile 那一瞬間，畫面完全卡住。
    /// MainMenuRuntime 在切換場景「之前」（例如淡出動畫、或更早的對話播放期間，
    /// 反正結局不管選哪個都會進 TrainingRoom）可以先拿這份清單呼叫
    /// ResourceHelper.PreloadTexAny() 在背景執行緒預先解壓縮，藏掉大部分等待時間，
    /// 之後 BuildCharacter() 真的呼叫 LoadTexAny 時大多會命中 Godot 資源快取。</summary>
    public static IEnumerable<string> GetPreloadSpritePaths()
    {
        yield return "Backgrounds/bg_LinaiBed_night_room";
        yield return "Backgrounds/bg_LinaiBed_night_bed";
        // 進房間後才同步載入、之前沒列進預載的貼圖（被子、M 腿內褲邊線／裁切腿、各種游標與小圖示）：一併預載，避免黑屏／進場瞬間卡頓
        foreach (var extra in new[] {
            "Backgrounds/bg_LinaiBed_night_duvet_covered", "Backgrounds/bg_LinaiBed_night_duvet_kicked",
            "Sprites/內褲_M_edge", "Sprites/腿1_M_cut", "Sprites/腿2_M_cut",
            "Sprites/游標", "Sprites/手圖標", "Sprites/蝴蝶結", "Sprites/pinch_hand", "Sprites/舌頭游標", "Sprites/舌頭2" })
            yield return extra;

        foreach (var layerName in LayerOrder)
        {
            if (_extraSpritePaths.TryGetValue(layerName, out var ep))
            {
                yield return ep;
            }
            else
            {
                string baseName = layerName.Contains("/") ? layerName[(layerName.LastIndexOf('/') + 1)..] : layerName;
                yield return "Sprites/" + baseName;
            }
        }
    }

    // 備援路徑表：basename 規則會撞名的例外圖層。
    // "手1"/"手2" 在 Unity 原始碼裡實際路徑是 Sprites/Meimei/手1、Sprites/Meimei/手2（女主角的手），
    // 但 Godot 專案裡 Resources/Sprites/ 攤平匯出時，這兩個檔名剛好被「另一個角色（男主）的手」
    // 同名檔案蓋掉了（Unity 裡兩者是不同資料夾、不同圖，攤平後只留下男主那張）。
    // 已從 Unity 專案把正確的女主角手圖複製過來，另存新檔名 手1_女.png / 手2_女.png 避免再撞名。
    static readonly Dictionary<string, string> _extraSpritePaths = new()
    {
        { "眼角1", "Sprites/眼角1" },
        { "眼角2", "Sprites/眼角2" },
        { "肚子",  "Sprites/charuduzi" },
        { "洞",    "Sprites/xiaoxuekaidi" },
        { "開1",   "Sprites/xiaoxuekai" },
        { "開上",  "Sprites/xiaoxuekaishang" },
        { "插入",  "Sprites/xiaoxuekai2" },
        { "手1", "Sprites/手1_女" },
        { "手2", "Sprites/手2_女" },
        { "衣服", "Sprites/衣服" },
        { "衣服/左帶", "Sprites/衣服_左帶" },
        { "衣服/右帶", "Sprites/衣服_右帶" },
        { "內褲", "Sprites/內褲" },
        { "m腿/內褲", "Sprites/內褲_M" },
        // M 腿獨立美術（Unity Assets/Resources/Sprites/Meimei/m腿/ 資料夾）跟一般腿同名撞檔
        // （腿1.png/腿2.png/腳1.png/腳2.png/足1.png/足2.png/小穴.png），basename 規則會抓到
        // 一般腿那張圖。已從 Unity 專案複製過來、另存新檔名（後綴 _M）避免撞名，這裡查表指過去。
        // 「m腿/肛門」「m腿/腿底」比較特別：「肛門」在 Unity 原始碼裡本來就只有 m腿 這一份、
        // 沒有跟一般腿撞名，「腿底」則是全新圖層，兩者都不需要查表，basename 規則本身就對。
        { "m腿/腿1", "Sprites/腿1_M" },
        { "m腿/腿2", "Sprites/腿2_M" },
        { "m腿/腳1", "Sprites/腳1_M" },
        { "m腿/腳2", "Sprites/腳2_M" },
        { "m腿/足1", "Sprites/足1_M" },
        { "m腿/足2", "Sprites/足2_M" },
        { "m腿/小穴", "Sprites/小穴_M" },
        { "m腿/腿底", "Sprites/腿底_M" },
        // { "m腿/小穴細節", "Sprites/mleg_extra" },
    };

    // ── Export：立繪設定 ───────────────────────────────────────────
    [Export] public float CharScale = 0.315f;
    [Export] public Vector2 CharPosition = new Vector2(-35f, 25f); // 225→145→95→35→25（再下修 10）

    // ── Export：衣物圖層偏移（畫布 px，正＝往下）──────────────────
    // 對齊計算（2000×3200 畫布）：
    //   衣服 content top=821，身體 content top=145 → 衣服往上移 676px，OffsetY=-676
    //   內褲 content bottom=1639，小穴 content bottom=1611 → 內褲往上移 28px，OffsetY=-28
    [Export] public float ClothOffsetY   = 0f;     // 衣服：0＝圖檔本身在畫布上的位置（圖是同畫布匯出，原位就是對的）；正＝往下，Play 中可即時調
    [Export] public float PantyOffsetY   = 0f;     // 內褲：0＝圖檔原位（可在 Inspector 即時微調，正＝往下）
    [Export(PropertyHint.Range, "0,1,0.01")] public float ClothAlpha = 1f;      // 衣服（衣服2）不透明度：100%
    [Export(PropertyHint.Range, "0,1,0.01")] public float PantyAlpha = 0.85f;   // 內褲（衣服1）不透明度：85%

    // ── Export：背景 ───────────────────────────────────────────────
    [Export] public Vector2 BackgroundSize = new Vector2(1862.4f, 1047.6f);
    [Export] public Vector2 BackgroundOffset = new Vector2(-65f, 80f);
    [Export] public Vector2 BackgroundBaseSize = new Vector2(1920f, 1080f);
    [Export] public Vector2 BackgroundBaseOffset = Vector2.Zero;

    // ── Export：痙攣效果 ───────────────────────────────────────────
    [Export] public float SpasmsShakeAmplitude = 10f;
    [Export] public float SpasmsBurstDuration  = 0.35f;
    [Export] public int   SpasmsCount          = 5;
    [Export] public float SpasmsPauseDuration  = 0.5f;
    [Export] public float SpasmsArmContract    = 2f;
    [Export] public float SpasmsFootContract   = 2f;
    [Export] public float SpasmsContractEase   = 0.12f;

    // ── Export：呼吸效果（數值取自 TrainingRoom.unity 實際 Inspector 調校值）────
    [Export] public bool  BreathEnabled     = true;
    [Export] public float BreathSpeed       = 0.6f;
    [Export] public float BreathAmplitude   = 1.5f;
    [Export] public float BreathLowerRatio  = 0.01f;
    // 使用者要求頭部呼吸時移動幅度比身體多一點點（1.0＝跟身體一樣，這裡給 1.15＝多 15%）。
    // 使用者要求吸氣時頭部要多往上一點，先誇張調到 2.2 方便看效果，之後再依回饋調小。
    [Export] public float HeadBreathRatio   = 2.2f;
    [Export] public float BreathChestX      = 1.5f;
    [Export] public float BreathChestY      = 1f;
    [Export] public float BreathBodyExpand  = 0.002f;
    [Export] public float BreathBodyStretch = 0.0005f;
    [Export(PropertyHint.Range, "0,2")] public float HandBreathScale = 0.2f;
    [Export(PropertyHint.Range, "0,2")] public float ArmBreathScale  = 1f;

    // ── 頭髮飄動設定（彈簧物理＋諧波，數值取自 Unity Inspector）────────
    public class HairLayerSetting
    {
        public string  LayerName;
        public bool    AnimEnabled  = true;
        public float   AnimSpeed    = 0.65f;
        public float   AnimPhase    = 0f;
        public float   AnimRotMin   = -8f;
        public float   AnimRotMax   =  8f;
        public Vector2 Pivot        = new Vector2(0.5f, 1f);
        public float   SpringForce  = 10f;
        public float   SpringDamp   = 0.80f;
        public float   HarmonicFreq = 2.3f;
        public float   HarmonicAmp  = 0.18f;
        public float   AnimMoveX    = 0f;
        public float   AnimScaleX   = 0f;
        public float   AnimScaleY   = 0f;
        public float   BreathWeight = 1f;
        public float   BreathRotWeight = 0f; // 呼吸週期額外附加的旋轉幅度（度），給辮子等需要「呼吸時也要轉」的圖層用
    }
    static readonly HairLayerSetting[] HairLayerSettings =
    {
        // 使用者反應搖擺幅度還是太大，第三輪再砍一半：
        // 後髮1 AnimRotMin/Max ±2.5→±1.2、HarmonicAmp 0.07→0.035、AnimMoveX 0.1→0.05；
        // 後髮2/3 AnimRotMin/Max ±1.5→±0.7、HarmonicAmp 0.08→0.04、AnimMoveX 0.9→0.4。
        // （第一輪從 ±5/0.24/3、第二輪從 ±3/0.15/1.8 砍下來，這是第三輪。）
        // 使用者要求吸氣時頭髮要跟著頭多往上一點，BreathWeight 先誇張調大方便看效果，之後再依回饋調小。
        // 使用者反應後髮的呼吸擺動（BreathWeight）可以再小一點點，三層都往下調一些
        // （後髮1 3→2.4、後髮2/3 2.2→1.8），其餘飄動參數不動。
        new HairLayerSetting { LayerName="後髮1", AnimSpeed=0.3f, AnimPhase=0f,    AnimRotMin=-1.2f, AnimRotMax=1.2f, Pivot=new Vector2(0.5f,0.5f), SpringForce=0f,  SpringDamp=0f,    HarmonicFreq=2f,   HarmonicAmp=0.035f, AnimMoveX=0.05f, AnimScaleX=0.0001f, AnimScaleY=0.0001f, BreathWeight=2.4f },
        new HairLayerSetting { LayerName="後髮2", AnimSpeed=0.6f, AnimPhase=0.62f, AnimRotMin=-0.7f, AnimRotMax=0.7f, Pivot=new Vector2(0.5f,1f),   SpringForce=6f,  SpringDamp=0.74f, HarmonicFreq=3.1f, HarmonicAmp=0.04f,  AnimMoveX=0.4f,  AnimScaleX=0f,      AnimScaleY=0f,      BreathWeight=1.8f, BreathRotWeight=0f }, // ← 沿用原 _辮子2 的動畫（與後髮2是同一張圖）
        new HairLayerSetting { LayerName="後髮3", AnimSpeed=0.6f, AnimPhase=0.12f, AnimRotMin=-0.7f, AnimRotMax=0.7f, Pivot=new Vector2(0.5f,1f),   SpringForce=6f,  SpringDamp=0.74f, HarmonicFreq=3.1f, HarmonicAmp=0.04f,  AnimMoveX=0.4f,  AnimScaleX=0f,      AnimScaleY=0f,      BreathWeight=1.8f, BreathRotWeight=0f }, // ← 沿用原 _辮子1 的動畫（與後髮3是同一張圖）
        new HairLayerSetting { LayerName="前髮",  AnimSpeed=0f,   AnimPhase=0f,    AnimRotMin=0f,   AnimRotMax=0f,  Pivot=new Vector2(0f,0.5f),   SpringForce=0f,  SpringDamp=0f,    HarmonicFreq=0f,   HarmonicAmp=0f,    AnimMoveX=0f,   AnimScaleX=0.001f,  AnimScaleY=0.015f,  BreathWeight=3f },
        // new HairLayerSetting { LayerName="_辮子1", AnimSpeed=0.6f, AnimPhase=0.12f, AnimRotMin=-5f, AnimRotMax=5f,  Pivot=new Vector2(0.5f,1f),   SpringForce=6f,  SpringDamp=0.74f, HarmonicFreq=3.1f, HarmonicAmp=0.24f, AnimMoveX=3f,   AnimScaleX=0f,      AnimScaleY=0f,      BreathWeight=1f, BreathRotWeight=0f },  // 已移除（重複圖層）
        // new HairLayerSetting { LayerName="_辮子2", AnimSpeed=0.6f, AnimPhase=0.62f, AnimRotMin=-5f, AnimRotMax=5f,  Pivot=new Vector2(0.5f,1f),   SpringForce=6f,  SpringDamp=0.74f, HarmonicFreq=3.1f, HarmonicAmp=0.24f, AnimMoveX=3f,   AnimScaleX=0f,      AnimScaleY=0f,      BreathWeight=1f, BreathRotWeight=0f },  // 已移除（重複圖層）
    };
    readonly HashSet<string> _hairLayerSet = new(HairLayerSettings.Select(h => h.LayerName));

    // 左右頭髮（後髮2＝左、後髮3＝右）位置微調：使用者要求兩邊都往中間靠一點點。
    // 慣例同 ArmUserNudge：X 向右為正，單位 = 畫布像素（角色縮放 0.315，約 1/3 螢幕像素）。
    // 後髮2 在左邊，往中間＝往右（+X）；後髮3 在右邊，往中間＝往左（−X）。
    // 如果實際看起來方向相反（例如貼圖本身有左右鏡射），把下面兩個值的正負號對調即可。
    // 第一次調 ±10px 使用者反應還要再靠近一點，這裡加到 ±18px。
    [Export] public Vector2 LeftHairNudge  = new Vector2(18f, 0f);  // 後髮2
    [Export] public Vector2 RightHairNudge = new Vector2(-18f, 0f); // 後髮3
    Vector2 HairUserNudge(string layer) => layer switch
    {
        "後髮2" => LeftHairNudge,
        "後髮3" => RightHairNudge,
        _       => Vector2.Zero,
    };

    // 前髮下移量（畫布像素，Godot Y 向下為正）：使用者反應瀏海位置偏高，原本 5px 不夠，調到 18px。
    [Export] public float FrontHairDropY = 18f;
    readonly float[] _hairRotCurrent  = new float[HairLayerSettings.Length];
    readonly float[] _hairRotVelocity = new float[HairLayerSettings.Length];

    // ── 手臂/手部圖層設定（復刻 Unity ApplyArmLayerSettings：軸心 + 呼吸旋轉 + Follow 跟隨鏈） ──
    // 數值 = TrainingRoom.unity 裡 TrainingRoomRuntime（fileID 903285447）序列化的 ArmLayerSettings。
    // 慣例同 Unity：Offset / Pivot 的 Y 向上為正、Rotation 逆時針為正；換算在 UpdateArmLayerDetails 做。
    public class ArmLayerSetting
    {
        public string  LayerName;
        public Vector2 Offset = Vector2.Zero;
        public float   Rotation = 0f;
        public float   BreathRotation = 0f;
        public Vector2 Pivot = new Vector2(0.5f, 0.5f);   // 相對 Unity 小切圖（可超出 0~1）
        public float   BreathWeight = 1f;
        public bool    FlipX = false;
        public string  FollowLayerName = "";              // 跟隨父圖層的位移（手跟手臂）
        public bool    FollowRotation  = true;            // 同時繼承父圖層旋轉角
    }
    static readonly ArmLayerSetting[] ArmLayerSettings =
    {
        new ArmLayerSetting { LayerName="手臂1", Offset=new Vector2(0f,0f),   Rotation=0f,   BreathRotation=1.5f, Pivot=new Vector2(0.4f,0.9f), BreathWeight=1f },
        new ArmLayerSetting { LayerName="手臂2", Offset=new Vector2(0f,-2f),  Rotation=0f,   BreathRotation=-2f,  Pivot=new Vector2(1f,0.9f),   BreathWeight=-1f },
        new ArmLayerSetting { LayerName="手1",   Offset=new Vector2(-60f,2f), Rotation=0f,   BreathRotation=0f,   Pivot=new Vector2(0.2f,0.4f), BreathWeight=1f,    FollowLayerName="手臂1" },
        new ArmLayerSetting { LayerName="手2",   Offset=new Vector2(-3f, 9f), Rotation=0.5f, BreathRotation=0f,   Pivot=new Vector2(2f,0.5f),   BreathWeight=-0.8f, FollowLayerName="手臂2" },
    };
    // 手臂位置微調（Unity 慣例：X 向右、Y 向上為正，單位 = 畫布像素；角色縮放 0.315，約 1/3 螢幕像素）
    // 手臂2 的位移會透過 Follow 帶著 手2 一起移動。
    //
    // 使用者更正：上一輪調錯邊了——以「畫面上看到的」左右為準（不是角色自己的解剖學左右），
    // 畫面右邊那隻手要再往中間靠一點，畫面左邊那隻手要退回原本的位置。
    // 「手臂2/手2」這組（LeftArmNudge）退回原本的 15；「手臂1/手1」這組（RightForearmNudge）
    // 從 15 調到 5，往中間（−X）收一點。
    [Export] public Vector2 RightForearmNudge = new Vector2(5f, 0f);  // 手1（右前臂）
    [Export] public Vector2 LeftArmNudge      = new Vector2(15f, 0f);  // 手臂2 + 手2（左後臂＋前臂整組）
    [Export] public Vector2 RightUpperArmNudge = new Vector2(-10f, 0f); // 手臂1（右後臂）；手1 會自動抵銷，右前臂不跟著動
    Vector2 ArmUserNudge(string layer) => layer switch
    {
        "手臂1" => RightUpperArmNudge,
        "手1"   => RightForearmNudge - RightUpperArmNudge,  // 抵銷 Follow 帶過來的後臂位移
        "手臂2" => LeftArmNudge,
        _       => Vector2.Zero,
    };

    readonly Dictionary<string, Vector2> _armLayerDeltaPos     = new();  // Godot 座標
    readonly Dictionary<string, float>   _armLayerDeltaRot     = new();  // Unity 角度
    readonly Dictionary<string, Vector2> _armLayerRestDeltaPos = new();
    readonly Dictionary<string, float>   _armLayerRestDeltaRot = new();
    readonly HashSet<string> _armLayerSet = new(ArmLayerSettings.Select(a => a.LayerName));

    // 手臂/手在 2000x3200 攤平畫布上的主要裁切矩形（Unity .meta，x/y 為 Unity 座標、y 從底部往上）。
    // Unity 的 Pivot / ShoulderPivot / HandPivot 都是相對這塊矩形的比例，靠它換算成整張畫布上的點。
    static readonly Dictionary<string, Rect2> ArmSpriteRects = new()
    {
        { "手臂1", new Rect2(1067f, 1953f, 210f, 419f) },
        { "手臂2", new Rect2( 694f, 1957f, 189f, 411f) },
        { "手1",   new Rect2(1160f, 1950f, 241f, 528f) },  // = 手1_女.png（與 Unity Meimei/手1 相同檔案）
        { "手2",   new Rect2( 557f, 1950f, 227f, 471f) },  // = 手2_女.png
    };
    const float ArmCanvasH = 3200f;
    public bool HasArmSpriteRect(string layer) => ArmSpriteRects.ContainsKey(layer);

    /// <summary>Unity 小切圖上的比例點 → Godot 圖層（整張畫布）本地像素座標（Y 向下）</summary>
    public static Vector2 UnityPivotToLocal(string layer, Vector2 unityPivot)
    {
        if (!ArmSpriteRects.TryGetValue(layer, out var r)) return new Vector2(1000f, 1600f);
        return new Vector2(r.Position.X + unityPivot.X * r.Size.X,
                           ArmCanvasH - (r.Position.Y + unityPivot.Y * r.Size.Y));
    }

    // ── 腿部圖層設定（簡化版：呼吸聯動＋原地循環動畫，不含拖動展開/M腿專屬位移） ──
    public class LegLayerSetting
    {
        public string  LayerName;
        public Vector2 Offset = Vector2.Zero;
        public float   Rotation = 0f;
        public Vector2 Pivot = new Vector2(0.5f, 0.5f);
        public float   BreathWeightY = 0.05f;
        public float   BreathWeightX = 0f;
        public float   BreathPhase   = 0f;
        public bool    AnimEnabled = false;
        public float   AnimSpeed   = 1f;
        public float   AnimPhase   = 0f;
        public float   AnimRotMin  = -10f;
        public float   AnimRotMax  =  10f;
        public Vector2 AnimMove    = Vector2.Zero;
    }
    static readonly LegLayerSetting[] LegLayerSettings =
    {
        new LegLayerSetting { LayerName="腿/腿1",  Offset=new Vector2(-1f,0f),  Pivot=new Vector2(-0.1f,0.85f), BreathWeightY=0.5f, AnimEnabled=true, AnimSpeed=0.4f, AnimRotMin=-0.3f, AnimRotMax=0.5f, AnimMove=new Vector2(0.5f,1f) },
        new LegLayerSetting { LayerName="腿/腿2",  Offset=new Vector2(-4f,2f), Pivot=new Vector2(1f,0.89f),    BreathWeightY=0.5f, AnimEnabled=true, AnimSpeed=0.4f, AnimRotMin=0.3f,  AnimRotMax=-0.5f, AnimMove=Vector2.Zero },
        new LegLayerSetting { LayerName="m腿/腿1", Offset=new Vector2(0f,0f),  Pivot=new Vector2(0.2f,0.3f),   BreathWeightY=0.5f, AnimEnabled=true, AnimSpeed=0.4f, AnimRotMin=1f,    AnimRotMax=-2f },
        new LegLayerSetting { LayerName="m腿/腿2", Offset=new Vector2(0f,0f),  Pivot=new Vector2(0.84f,0.32f), BreathWeightY=0.5f, AnimEnabled=true, AnimSpeed=0.4f, AnimRotMin=-1f,   AnimRotMax=2f },
        new LegLayerSetting { LayerName="m腿/足2", Offset=new Vector2(0f,0f),  Pivot=new Vector2(0.2f,0.3f),   BreathWeightY=0.5f, AnimEnabled=true, AnimSpeed=0.4f, AnimRotMin=-1f,   AnimRotMax=2f },
        // ── 腳/足的末端圖層：原本 AnimEnabled=false，只有呼吸位移、沒有原地擺動，
        //    導致大腿在搖擺、小腿/腳完全不動，兩截看起來是分開的（使用者回報「下半的腿沒動」）。
        //    這裡改成套用跟同一條腿（腿1/腿2）相同的擺動幅度與相位，讓上下兩截一起動。
        //    （場景樹是扁平結構，腳沒有真的掛在腿下面當子節點，這裡是用「複製同一組擺動參數」
        //    的方式讓兩層視覺上同步，不是真正的骨骼父子繼承，仍可能不是 100% 密合，
        //    如果角度看起來還是差一點，可以再微調 AnimRotMin/AnimRotMax 讓兩層更貼合。）
        // 使用者確認調錯物件了：之前調的「腳1」要還原成原本樣子，實際該調整的是「腳2」，
        // 往左移動 4px 試試。
        // 使用者要求「腳」相關的物件（腿/腳1、腿/腳2、m腿/腳1、m腿/腳2，不含「足」）
        // 集體往右移動 2px，在原本 Offset.X 上直接加 2。
        new LegLayerSetting { LayerName="腿/腳1",  Offset=new Vector2(4f,0f),  BreathWeightY=0.5f, AnimEnabled=true, AnimSpeed=0.4f, AnimRotMin=-0.3f, AnimRotMax=0.5f },  // 使用者要求再向右2px
        new LegLayerSetting { LayerName="腿/腳2",  Offset=new Vector2(0f,0f),  BreathWeightY=0.5f, AnimEnabled=true, AnimSpeed=0.4f, AnimRotMin=0.3f,  AnimRotMax=-0.5f },  // 使用者要求再向右2px
        new LegLayerSetting { LayerName="腿/足1",  Offset=new Vector2(0f,0f),  BreathWeightY=0.5f, AnimEnabled=true, AnimSpeed=0.4f, AnimRotMin=-0.3f, AnimRotMax=0.5f },
        new LegLayerSetting { LayerName="腿/足2",  Offset=new Vector2(0f,0f),  BreathWeightY=0.5f, AnimEnabled=true, AnimSpeed=0.4f, AnimRotMin=0.3f,  AnimRotMax=-0.5f },
        new LegLayerSetting { LayerName="m腿/腳1", Offset=new Vector2(4f,0f),  BreathWeightY=0.5f, AnimEnabled=true, AnimSpeed=0.4f, AnimRotMin=1f,    AnimRotMax=-2f },  // 使用者要求再向右2px
        new LegLayerSetting { LayerName="m腿/腳2", Offset=new Vector2(4f,0f),  BreathWeightY=0.5f, AnimEnabled=true, AnimSpeed=0.4f, AnimRotMin=-1f,   AnimRotMax=2f },  // 使用者要求再向右2px
        new LegLayerSetting { LayerName="m腿/足1", Offset=new Vector2(0f,0f),  BreathWeightY=0.5f, AnimEnabled=true, AnimSpeed=0.4f, AnimRotMin=1f,    AnimRotMax=-2f },
    };
    readonly HashSet<string> _legLayerSet = new(LegLayerSettings.Select(l => l.LayerName));

    // ── 特定圖層的「真實旋轉錨點」（肩膀 / 髮根附著點），取代預設的整張畫布正中心 pivot ──
    // 這 6 個是從 .meta 矩形反推的估算值，肉眼看還不夠準，所以改成 [Export]，可以直接在
    // Godot 編輯器的 Inspector 面板上（選取場景根節點 TrainingRoomRuntime）用滑鼠拖數字微調，
    // 不用改程式碼、也不用重新編譯，改完存檔重進場景就看得到效果。
    //
    // 【自己調的方法】數值是 (X, Y)，都是 0~1 的比例，對應整張 2000x3200 攤平畫布：
    //   X：0=圖片最左邊，1=圖片最右邊。錨點在旋轉時「不動」，錨點以外的部分會繞著它擺，
    //      錨點離畫面上看到的關節（肩膀/髮根）越近，轉起來就越像是從那個關節在擺動。
    //   Y：注意這裡刻意維持 Unity 習慣的「0=下 1=上」，不是 Godot 的上下！所以要把錨點往
    //      畫面「上方」（肩膀/髮根方向）移，就把 Y 調大（往 1 靠近）；往「下方」（手/髮尾方向）
    //      移，就把 Y 調小（往 0 靠近）。
    //   做法建議：先看歪的方向——如果整張圖是繞著一個看起來偏離關節的點在轉，把錨點往關節
    //   的方向挪一點（例如關節看起來在圖片偏上偏左，就把 X 調小、Y 調大），一次改 0.02~0.05，
    //   重進場景看一次，反覆逼近。
    //
    //   手臂1: 對應 手臂1 圖層本身（肩膀端）；手2/手1 同理是手掌圖層本身。
    //   估算來源（僅供參考，不影響你調的時候要不要照這個邏輯）：.meta 裡最大裁切矩形的
    //   上緣中點／整張畫布比例——手臂1 rect(1067,1953,210,419)、手臂2 rect(694,1957,189,411)、
    //   手1 rect(1160,1950,241,528)、手2 rect(557,1950,227,471)、
    //   _辮子1 rect(1099,2131,155,430)、_辮子2 rect(661,2134,158,431)，畫布都是 2000x3200。
    // 手臂1/手1（RightArmIK 那一側）目前實測會讓手飄到跟身體完全不連的位置（髖部附近），
    // 已經連續兩次估算都不準、原因還沒抓到，這次先把這一側的自訂錨點退回中心 (0.5,0.5)
    // ——等於暫時關閉這側的自訂旋轉軸心，退回最早「至少不會飄走」的狀態。手臂2/手2
    // 那一側你確認過是連著的，所以先保留、繼續開放給你微調。
    // 手臂/手兩側都已經連續多次估算都不準、還讓手飛走，先整個退回中心 (0.5,0.5)——
    // 也就是關閉自訂旋轉錨點，回到最一開始「手不會飛走」的版本。要重新開啟的話，
    // 把對應的值改回非 (0.5,0.5) 即可（Inspector 或這裡改都可以）。
    [Export] public Vector2 ArmAttachPivot1   = new Vector2(0.5f, 0.5f);
    [Export] public Vector2 ArmAttachPivot2   = new Vector2(0.5f, 0.5f);
    [Export] public Vector2 HandAttachPivot1  = new Vector2(0.5f, 0.5f);
    [Export] public Vector2 HandAttachPivot2  = new Vector2(0.5f, 0.5f);
    [Export] public float BraidStaticRotDeg = 4f;   // 兩條辮子的固定旋轉：右邊（後髮3）順時針、左邊（後髮2）逆時針各轉幾度，讓辮子和後面的頭髮接起來
    [Export] public Vector2 BraidAttachPivot1 = new Vector2(0.58825f, 0.800313f); // _辮子1（髮根）
    [Export] public Vector2 BraidAttachPivot2 = new Vector2(0.37000f, 0.801563f); // _辮子2（髮根）
    // 辮子「呼吸同步旋轉」的幅度（度），跟上面的錨點一起在 Inspector 微調：
    // 如果辮子看起來跟後面那條靜止不動的「後發辮子」重疊出現重影，通常是這個轉動幅度
    // 太大、或錨點還沒對準髮根，兩個都可以試著調小/調準看看。
    // 使用者反應整體搖擺幅度還是太大，呼吸同步旋轉第三輪再從 ±2 縮到 ±1。
    [Export] public float BraidBreathRotWeight1 = 1f;
    [Export] public float BraidBreathRotWeight2 = -1f;

    // 手肘/手腕連接點除錯用的小圓點（見 TrainingRoomRuntime.Interaction.cs 的
    // BuildIKDebugDots/UpdateIKDebugDots）。紅點＝ArmElbow（手臂認為的手腕位置），
    // 橘點＝HandJoint（手認為的手腕位置）。Unity 原版在 Scene View 用同一組顏色的
    // 拖拉手把，兩點完全重疊＝連接點正確；這裡沒有拖拉手把，只能先把點畫出來讓你
    // 肉眼比對兩點差多少、往哪個方向差，再回頭調 ArmIK/RightArmIK 的 ArmElbow/HandJoint
    // （目前這兩組還是寫死在程式碼裡，不是 Inspector 欄位，因為每次改都要知道實際偏差
    // 多少 px，只能先看點再調數字）。不需要時可以在這裡關掉。
    [Export] public bool ShowIKDebugDots = false; // 預設關掉，避免畫面上多出你沒要求的紅橘點；要除錯再自己開

    Dictionary<string, Vector2> _customAttachPivot => new()
    {
        { "手臂1",  ArmAttachPivot1 },
        { "手臂2",  ArmAttachPivot2 },
        { "手1",    HandAttachPivot1 },
        { "手2",    HandAttachPivot2 },
        { "後髮3",  BraidAttachPivot1 },   // 原 _辮子1 的旋轉錨點
        { "後髮2",  BraidAttachPivot2 },   // 原 _辮子2 的旋轉錨點
    };

    // ── Export：噴水效果（高潮噴水 SquirtWave 系統）─────────────────
    /// <summary>噴水起點相對螢幕中心的偏移（Unity Y-up 座標）。
    /// 預設 (0, -200) ≈ 小穴位置；執行時會優先改用層的 GlobalRect。</summary>
    [Export] public Vector2 SquirtOrigin      = new Vector2(0f, -200f);

    // ── Export：插入系統（4 階段）────────────────────────────────────────────────
    // Phase 3 三個子階段的速度（px/s，在 _charRoot 縮放下的座標）
    [Export] public float JijiStage1Speed   = 120f;  // Stage 0 → 1 的推進速度
    [Export] public float JijiStage2Speed   = 200f;  // Stage 1 → 2 的推進速度
    [Export] public float JijiStage3Speed   = 300f;  // Stage 2 的推進速度
    // 各階段上下限（相對 Phase2BaseY 的 Y 偏移，正值 = 往上）
    [Export] public float JijiStage1MaxY    =  80f;
    [Export] public float JijiStage1MinY    =   0f;
    [Export] public float JijiStage2MaxY    = 275f;   // 180 → 突破後最高再往上 30 螢幕 px（÷0.315 ≈ 95 畫布 px）
    [Export] public float JijiStage2MinY    =  60f;
    [Export] public float JijiStage3MaxY    = 300f;
    [Export] public float JijiStage3MinY    = 150f;
    // 阻力突破系統
    [Export] public float BreakthroughBoost        =  15f;   // 突破瞬間額外往上衝的幅度
    [Export] public float BreakthroughSurgeSpeed   = 180f;   // 突破後的衝刺速度
    [Export] public float BreakthroughSurgeDamping =   6f;   // 衝刺衰減（越大衰減越快）
    [Export] public float ResistBreakScoreTarget   = 100f;   // 突破所需分數
    [Export] public float ResistScoreRateHold      =  25f;   // 「按住」時每秒累積的分數
    [Export] public float ResistScoreDecayRate     =  60f;   // 放開時每秒衰減的分數

    // ── 插入系統執行期狀態 ────────────────────────────────────────────────────────
    // 插入階段：0=雞雞隱藏, 1=雞雞可見（未插入）, 2=在入口位置, 3=插入中
    int   _jijiPhase       = 0;
    int   _jijiStage       = 0;   // Phase 3 子階段 0/1/2
    float _jijiInsertY     = 0f;  // 插入深度（往上推的幅度，正值越大越深）
    bool  _jijiMovingUp    = false;
    bool  _resistBlocking  = false;
    float _resistProgress  = 0f;
    float _resistTarget    = 100f;
    float _jijiSurgeVel    = 0f;  // 突破後的衝刺速度殘餘
    bool  _jijiHeld        = false; // 這一幀滑鼠左鍵是否按住（Phase 3 由 UpdateJiji 每幀讀取）
    /// <summary>每秒生成水滴數的基準（Wave 乘數會再乘上這個值）。</summary>
    [Export] public int     SquirtRate        = 12;
    /// <summary>水滴存活時間（秒）。</summary>
    [Export] public float   SquirtDropLife    = 1.8f;
    /// <summary>各波段定義時會蓋掉 SquirtDuration；保留給 Inspector。</summary>
    [Export] public float   SquirtDuration    = 8.0f;
    /// <summary>基準噴射初速（螢幕像素/秒）。Wave 的 spd 乘數再乘上此值。</summary>
    [Export] public float   SquirtInitSpeed   = 600f;
    /// <summary>重力加速度（像素/秒²，正值 = Godot Y 向下）。</summary>
    [Export] public float   SquirtGravity     = 400f;

    // ── Export：精液除錯 / 禁射區（先從 Unity 版 ExpressionTester.OnGUI 對應的欄位
    //    照抄過來，讓 DEV 面板的版面可以先跟 Unity 對齊；實際的射精粒子/落點碰撞判定
    //    系統這邊還沒有實作（那套邏輯在 Unity 原始碼有數千行，屬於獨立大工程，
    //    見 TriggerEjaculation()/ClearEjacDebris() 的說明），這幾個欄位目前只是
    //    「先擺著、可以在 Inspector 或 DEV 面板調整」，還沒有任何程式碼真的讀取它們。────
    [Export] public bool    EjacDebugColors     = false;
    [Export] public bool    EjacShowNoShootZone = false;
    [Export] public bool    EjacShowEyeZone     = false;
    [Export] public Vector2 NoShootZoneA_Offset = new Vector2(-129f, -50f);
    [Export] public Vector2 NoShootZoneA_Size   = new Vector2(142f, 160f);
    [Export] public Vector2 NoShootZoneB_Offset = new Vector2(2f, -195f);
    [Export] public Vector2 NoShootZoneB_Size   = new Vector2(165f, 160f);

    // ── Export：射精效果（EjaculationSequence）參數 ────────────────────────────────────
    [Export] public float   EjacFlightTime     = 0.4f;   // 單發飛行時間（秒）
    [Export] public float   EjacWaveDelay      = 0.20f;  // 波次間隔（秒）
    [Export] public float   EjacBlobMaxScale   = 1.0f;   // 落點 blob 最大縮放（1.0 = 半徑 15px blob）
    [Export] public float   EjacBlobFinalScale = 0.65f;  // 落點 blob 最終大小（相對 MaxScale）
    [Export] public float   EjacBlobExpandTime = 0.70f;  // blob 膨脹時間（秒）
    /// <summary>射精起點偏移：相對雞雞熱點中心的螢幕像素偏移，Unity Y-up 座標系（正 Y = 往上）。
    /// 預設 (0, 25) = 雞雞熱點上方 25px，對應雞雞前端尖端位置。</summary>
    [Export] public Vector2 EjacTipOffset      = new Vector2(0f, 25f);

    // ── Export：射精落點距離（照抄 Unity 預設值，單位＝角色畫布像素 2000x3200，不是螢幕像素）──
    [Export] public float   EjacCloseMaxDist   = 400f;   // 近射：往上 dist×0.5~1
    [Export] public float   EjacFarMinDist     = 700f;   // 遠射距離下限
    [Export] public float   EjacFarMaxDist     = 1500f;  // 遠射距離上限
    [Export] public float   EjacSpreadX        = 140f;   // 左右散布 ±
    [Export] public int     EjacCloseSegCount  = 8;      // 近射飛行段數
    [Export] public int     EjacMidSegCount    = 12;     // 中射（胸）飛行段數
    [Export] public int     EjacFarSegCount    = 16;     // 遠射飛行段數
    [Export] public float   EjacFarBlobScale   = 1.2f;   // 遠射落點團塊大小倍率

    // ── Export：數值 ───────────────────────────────────────────────
    [Export(PropertyHint.Range, "0,100")] public float ClimaxLevel = 0f;
    [Export(PropertyHint.Range, "0,100")] public float Mood         = 50f;
    [Export(PropertyHint.Range, "0,100")] public float ComfortMouth = 0f;
    [Export(PropertyHint.Range, "0,100")] public float ComfortChest = 0f;
    [Export(PropertyHint.Range, "0,100")] public float ComfortButt  = 0f;
    [Export(PropertyHint.Range, "0,200")] public float ComfortHole  = 0f;
    [Export(PropertyHint.Range, "0,100")] public float Stamina      = 0f;

    // ── Export：熱點（綠圈）位置與大小 ─────────────────────────────
    [Export(PropertyHint.Range, "0,1")] public float HotspotAlpha = 0.14f; // Unity Inspector 實際值是 0（熱點預設完全透明，靠游標懸停才顯示，我們沒有移植那套游標系統，故保留低透明度讓玩家看得到可點擊範圍，但調低避免蓋住胸/手等較小的立繪部位
    public static readonly Vector2[] HotspotPos = {
        new Vector2( -42f,  270f),  // 0 = 鼻
        new Vector2( -44f,  285f),  // 1 = 嘴（移到原本鼻子綠點的位置）
        new Vector2(-202f,  100f),  // 2 = 胸1（左胸，左手）
        new Vector2( 118f,  100f),  // 3 = 胸2（右胸，右手）
        new Vector2( -42f,   10f),  // 4 = B（雞雞/插入互動）
        new Vector2( -42f,  -40f),  // 5 = 尻
        new Vector2( -80f, -122f),  // 6 = 腿（左）—— 向內靠攏 20px（-102→-82），再向內 2px、向下 2px
        new Vector2(  -4f, -122f),  // 7 = 腿（右）—— 向內靠攏（18→-2），再向內 2px、向下 2px
        new Vector2( -42f, -230f),  // 8 = 足
        new Vector2(-260f,   40f),  // 9 = 左手肘（ArmIK）
        new Vector2( 176f,   40f),  // 10 = 右手肘（ArmIK）
    };
    /// <summary>B 綠點（熱點 4）只往上移的量（螢幕 px，正＝往上）。只移綠點本身，雞雞/射精等用 HotspotPos[4] 的位置不受影響。</summary>
    [Export] public float BHotspotUpPx = 10f;
	[Export] public float BHotspotSizeAddPx = 10f;
    Vector2 HotspotPlacePos(int i) => i == 4 ? HotspotPos[i] + new Vector2(0f, BHotspotUpPx) : HotspotPos[i];
    static readonly string[] HotspotLabels = { "鼻", "嘴", "胸", "胸", "B", "尻", "腿", "腿", "足", "肘", "肘" };
    static readonly float[]  HotspotSize   = { 64f, 24f, 54.4f, 54.4f, 36f, 64f, 64f, 64f, 64f, 56f, 56f };  // 胸(2/3)使用者要求縮小15%

    // ── 公開圖層字典（供 ArmIK / FaceExpressionController / EyeFollowController 使用）──
    public readonly Dictionary<string, Control> Layers = new();

    /// <summary>表情系統（FaceExpressionController）要求「某個圖層額外疊加的固定位移」，
    /// 例如 ZZZ 表情要讓眉毛垂下去一點。
    ///
    /// 背景：像「眉毛1」「眉毛2」這種在 _headLayerSet 裡的圖層，每一幀都會被下面
    /// UpdateBreathing() 的通用呼吸迴圈用 basePos + 呼吸位移 直接覆蓋 Position，
    /// 如果 FaceExpressionController 只是單純設一次 ctrl.Position，下一幀就會被蓋掉、
    /// 完全看不出效果（跟之前「眨眼時眼球跳到頭上」是同一類「兩套系統搶著改同一個節點」的問題）。
    /// 所以改成表情系統把想要的額外位移量寫進這個字典，呼吸迴圈自己在算 basePos 的時候
    /// 一併加上去，兩邊就不會互搶。FaceExpressionController 切換表情時負責維護這個字典的內容
    /// （沒有覆寫值的圖層等於 0，不影響原本的呼吸位置）。</summary>
    public readonly Dictionary<string, Vector2> ExprLayerExtraOffset = new();

    // ── 內部節點引用 ──────────────────────────────────────────────
    CanvasLayer _canvas;
    Control     _charRoot;
    public Control CharRoot => _charRoot;

    Label   _climaxValueLabel;
    ProgressBar _climaxBar;
    Label   _moodLabel, _comfortMouthLabel, _comfortChestLabel, _comfortButtLabel, _comfortHoleLabel;
    ProgressBar _moodBar, _comfortMouthBar, _comfortChestBar, _comfortButtBar, _comfortHoleBar;
    ProgressBar _staminaBar, _semenBar;
    float _semenLevel = 0f;
    public float SemenLevel => _semenLevel;

    TextureRect[] _hotspotImgs;
    Control[]     _hotspotRTs;
    Label[]       _hotspotTexts;

    // ── ArmIK 連結（左手熱點9 / 右手熱點10）────────────────────────
    [Export] public NodePath ArmIKPath;
    [Export] public NodePath RightArmIKPath;
    public ArmIK ArmIK;
    public ArmIK RightArmIK;
    public FaceExpressionController FaceController;
    public EyeCloseController       EyeCloseCtrl;
    public EyeFollowController      EyeFollowCtrl;
    public IdleStateController      IdleStateCtrl;

    // ── 呼吸內部狀態 ──────────────────────────────────────────────
    float _breathTime;
    readonly Dictionary<string, Vector2> _layerBasePos = new();
    readonly Dictionary<string, Vector2> _layerBaseSizeDelta = new(); // 各圖層原始像素尺寸（等同 Unity SetNativeSize 後的 sizeDelta）
    const float LayerBasePivotNorm = 0.5f; // BuildCharacter 建立時所有圖層 pivot 都是 (0.5,0.5)，與 Unity 版一致

    // ── 痙攣內部狀態 ──────────────────────────────────────────────
    bool  _spasmsRunning;
    float _spasmsTimer;
    int   _spasmsWaveIdx;
    float _spasmsWaveTimer;
    Vector2 _spasmsBodyShake = Vector2.Zero;
    readonly (float amp, float dur, float squirt)[] _spasmsWaves =
    {
        (0.25f, 0.30f, 0.15f),
        (0.45f, 0.30f, 0.30f),
        (1.00f, 0.45f, 1.00f),
        (0.10f, 0.54f, 0.05f),
        (0.07f, 0.48f, 0.02f),
        (0.04f, 0.42f, 0.00f),
    };

    // ── 噴水/水漬內部狀態（SquirtWave 系統）────────────────────────
    bool  _squirtActive;
    float _squirtTimer;
    bool  _squirtWaveRunning;

    // 飛行中的水滴（物理狀態）
    readonly List<SquirtDropState> _activeSquirtDrops = new();
    // 落地後的水漬 blob（純視覺 + 淡出）
    readonly List<PuddleBlobState> _puddleBlobs = new();

    // 舊的 _puddleDrops 保留空欄位以防外部還有引用（不再使用）
    readonly List<Control> _puddleDrops = new();

    // ── 射精效果內部狀態 ─────────────────────────────────────────
    bool _ejacRunning;
    readonly List<Control> _ejacDebris = new();

    // ── 半晚/日流程狀態 ───────────────────────────────────────────
    bool _pendingDialogueDone = true;

    // ── 手部模式（簡化，供底部按鈕使用）─────────────────────────────
    enum HandMode { None, Left, Right, Both }
    HandMode _handMode = HandMode.None;
    Button _btnLeft, _btnRight, _btnDual;
    bool _handcuffOpen = false;

    // ── M 腿模式切換（膝蓋熱點 6/7，見 Interaction.cs ToggleMLegModeAsync）───────
    // Unity 原版是拖曳超過閾值才切換、含弧形網格裁切等一整套程序化系統（見 Interaction.cs
    // 檔頭「已知簡化」），這裡簡化成點一下膝蓋熱點就交叉淡出「腿」群組、淡入「m腿」群組，
    // 再點一次切回來，不做拖曳角度/腳部展開曲線那些細節。
    [Export] public float MLegFadeDuration = 0.3f; // 腿淡出/M腿淡入的時間（秒）
    bool _mLegOpen = false;          // 目前是不是在 M 腿模式
    bool _mLegTransitioning = false; // 淡出/淡入動畫進行中，避免連點打架

    // ════════════════════════════════════════════════════════════
    //  手部觸摸動畫（Unity AnimateHand() 移植，供胸部熱點 2/3 使用）
    //
    // 跟上面「手部模式」（_handMode/_btnLeft/_btnRight，底部按鈕用的簡化樁）是
    // 兩套獨立系統，這裡不動它們——這套是「點胸部熱點 → 冒出一隻手貼在胸口、
    // 手＋胸部一起左右擺動」的實際觸摸動畫，對照 Unity 原版 PlaceHandPersist()／
    // AnimateHand()／CancelPersistHand()。
    //
    // 座標系說明：手部游標（TouchHand_Left/Right）比照 BuildHotspots() 的做法，
    // 用 MainMenuRuntime.ApplyPointAnchor 掛在 _canvas（跟熱點同一個螢幕座標系），
    // Unity 原始的 HandLeftInitPos/HandRightInitPos/HandAnimMoveX/Y 等數值因此可以
    // 直接照抄，不用換算（ApplyPointAnchor 檔頭已註明 Unity 座標常數可以原封不動照抄）。
    // ════════════════════════════════════════════════════════════

    [ExportGroup("手部觸摸動畫（胸部熱點用）")]
    [Export] public bool  HandAnimEnabled     = true;
    // 幅度維持不變，速度再加快到 9。
    [Export] public float HandAnimSpeed       = 9f;
    [Export] public float HandAnimAngleMin    = -3f;
    [Export] public float HandAnimAngleMax    = 3f;
    [Export] public float HandAnimMoveX       = 4f;
    [Export] public float HandAnimMoveY       = 2f;
    [Export] public float HandAnimHoldUpMult  = 1.6f;
    [Export] public float HandAnimHoldDownMult = 0.5f;
    // 手部游標縮小、位置直接對齊熱點 2（胸1/左手）與熱點 3（胸2/右手）的 HotspotPos，
    // 原本照抄 Unity 數值（180 大小、Y=50）跟 Godot 這邊熱點的實際座標（Y=100）對不上，
    // 導致手看起來偏大又偏離綠色熱點圈。
    [Export] public float HandSize            = 108f;   // 90 → 108（放大 20%）
    // 右手 X 改成 10。
    [Export] public Vector2 HandLeftInitPos   = new Vector2(-100f, 200f);
    [Export] public Vector2 HandRightInitPos  = new Vector2(15f, 200f);
    // 使用者要求手部貼圖 Y 軸壓縮一點（看起來扁一點、比較貼合胸口，不會太圓潤凸出）。
    // 1 = 不壓縮，數值越小壓越扁；只影響顯示用的 Size，不影響位置/旋轉計算用的 basePos。
    [Export(PropertyHint.Range, "0.3,1")] public float HandYSquash = 0.8f;
    // 使用者要求手部貼圖右下角漸層變半透明、邊緣加一點黑邊，讓手貼上去時比較有立體感、
    // 輪廓也更清楚。0=右下角完全透明，1=跟其他地方一樣不透明。
    [Export(PropertyHint.Range, "0,1")] public float HandTouchCornerFadeAlpha = 0.35f;
    [Export] public float HandTouchOutlineWidthPx = 0f;   // 黑邊寬度（0＝不畫；原 1.5，shader 修好後黑邊才真的出現）

    [ExportGroup("胸部聯動（觸摸動畫用，左手→胸1，右手→胸2）")]
    // 抓到問題了：手部游標是直接掛在 _canvas（螢幕座標，1:1 像素）下面，胸部圖層卻是在
    // _charRoot 底下（_charRoot.Scale = CharScale = 0.315，整個立繪整體縮小到 31.5%）。
    // 之前把這裡的幅度「跟著手部動畫一起縮小」（30→6px 那輪），是把兩個不同座標系的數字
    // 當同一套在調，導致這裡從 5/8/5 被砍到只剩 0.8/2.5/1.5——換算到螢幕上等於只剩
    // 0.25~0.8px，幾乎看不出來，這才是「看不到胸部一起動」的真正原因，不是邏輯沒接上。
    // 改回接近 Unity 原始碼的預設值（5/8/5），這裡才是胸部自己的座標系，不用再乘 CharScale。
    [Export] public Vector2 BreastAnimMoveLeft  = new Vector2(5f, 8f);
    [Export] public float   BreastAnimRotLeft   = 2.5f;   // 被摸時的旋轉減弱一半（原 5）
    [Export] public Vector2 BreastAnimMoveRight = new Vector2(5f, 8f);
    [Export] public float   BreastAnimRotRight  = 2.5f;   // 被摸時的旋轉減弱一半（原 5）

    // 使用者要求呼吸吸氣時胸部要「向外擴」、吐氣時「回收」，像真的呼吸——這裡是額外的
    // 縮放量。原本 0.08（放大 8%）使用者反應幅度太大，先縮到 0.03。
    [Export] public float BreastExpandScale = 0.03f;
    // 吸氣時胸部額外往上提一點的位移量（跟身體/胸部原本的 Y 呼吸位移疊加），
    // 讓「擴張」看起來更像胸部整個往上鼓起，不是只有變大。
    [Export] public float BreastExpandMoveUp = 4f;

    // 滑鼠移到胸部熱點上時，系統游標換成的「小一點、半透明的手」——半透明程度（0~1）。
    [Export] public float BreastHoverCursorAlpha = 0.6f;
    [Export(PropertyHint.Range, "0.3,1.5,0.05")] public float BreastHoverCursorScale = 0.9f;   // 滑鼠停在胸部綠點時的半透明手游標大小倍率（縮小 10%）

    // 胸部熱點改成跟著貼圖軸心即時跑之後（見 Interaction.cs 的 UpdateBreastHotspotTracking），
    // 這裡是額外的手動微調位移（螢幕像素，X 向右、Y 向下為正），微調用，不影響追蹤邏輯本身。
    // 使用者要求右邊的綠點再往右移 30px。
    [Export] public Vector2 BreastHotspotNudgeLeft  = new Vector2(-2f, 0f);
    [Export] public Vector2 BreastHotspotNudgeRight = new Vector2(32f, 0f);

    // 使用者要求：M 腿膝蓋熱點（6/7）打開 M 腿後，綠點要黏著 M 腿實際的膝蓋位置走
    // （跟胸部熱點是同一套「跟著圖層實際位置」思路，見 Interaction.cs 的
    // UpdateMLegKneeHotspotTracking()）。這裡先給 0，之後使用者測試覺得偏一點再調。
    // 使用者要求：M 腿打開後的綠點位置再往上 50px，左膝再往左多移 30px、右膝再往右多移 30px
    // （這裡是 GlobalPosition 直接加總的螢幕像素位移，Y 向下為正，所以「往上」是負值，
    // 跟 HotspotPos 用的 Unity Y 朝上慣例不同，不要搞混）。
    [Export] public Vector2 MLegKneeHotspotNudgeLeft  = new Vector2(-75f, -185f);  // 使用者要求再向外開10px → 再往外 5
    [Export] public Vector2 MLegKneeHotspotNudgeRight = new Vector2(75f, -185f);  // 使用者要求再向外開10px → 再往外 5

    /// <summary>M 腿膝蓋熱點即時跟隨用：「m腿/腿1」（右腿）「m腿/腿2」（左腿，兩腿的
    /// 左右對應已用 Python 量測貼圖內容框驗證過，跟命名數字直覺相反，跟胸部熱點
    /// 之前修正「1/2 跟畫面左右是反的」是同一個坑）貼圖本身「大腿內容框下緣中點」，
    /// 近似當作膝蓋關節位置（大腿=腿、小腿=腳，膝蓋就是兩者交接處，用大腿內容框
    /// 下緣最接近）。數值來源：對 腿1_M.png/腿2_M.png 做 getbbox() 量出來的內容框，
    /// 取 (中心X, 下緣Y)，皆為 2000x3200 攤平畫布上的像素座標（跟 BreastPivotOverrides
    /// 同一套座標系，直接當本地座標用，不用翻轉）。
    /// 注意：跟胸部熱點不同，這裡刻意不去動 m腿/腿1、m腿/腿2 這兩層 Control 本身的
    /// PivotOffset（那個要留給呼吸/旋轉動畫用，見 Effects.cs「不套用 Unity 的 Pivot，
    /// 維持預設中心 pivot」的說明），改用獨立字典存這個點，只給
    /// UpdateMLegKneeHotspotTracking() 讀，不影響既有的腿部呼吸/旋轉邏輯。</summary>
    static readonly Dictionary<string, Vector2> MLegKneeLocalPoint = new()
    {
        { "m腿/腿1", new Vector2(1163.5f, 1724f) }, // 右腿
        { "m腿/腿2", new Vector2(840.5f,  1761f) }, // 左腿
    };

    // 使用者要求：一般腿（沒開 M 腿時）的膝蓋熱點也要跟著腿圖層走（呼吸/搖腿動畫時綠點會跟著位移）。
    // 同樣用「腿圖層 GetGlobalTransform() * localPoint」計算世界座標，localPoint 取大腿貼圖
    // 內容框下緣中點（2000x3200 畫布座標，與 MLegKneeLocalPoint 同一套，先暫用 M 腿的量測值，
    // 之後使用者實機測試再微調 LegKneeHotspotNudgeLeft/Right 即可）。
    static readonly Dictionary<string, Vector2> LegKneeLocalPoint = new()
    {
        { "腿/腿1", new Vector2(1163.5f, 1724f) }, // 右腿
        { "腿/腿2", new Vector2(840.5f,  1761f) }, // 左腿
    };
    /// <summary>一般腿膝蓋熱點在跟隨到圖層位置後再疊加的螢幕像素微調
    /// （座標系同 MLegKneeHotspotNudge：X 向右、Y 向下為正；預設 (0,0) 即不偏移）。</summary>
    [Export] public Vector2 LegKneeHotspotNudgeLeft  = new Vector2(0f, 0f);
    [Export] public Vector2 LegKneeHotspotNudgeRight = new Vector2(0f, 0f);

    // 使用者要求：M 腿打開之後，B（雞雞/插入互動，熱點 4）的判定點要直接改成跟著
    // 「m腿/小穴」這個圖層一起上下移動（M 腿打開時「小穴」本來就會露出來，判定點
    // 貼著它比較合理），並且要在跟隨到的位置上再往上 30px。沒開 M 腿時維持原本
    // HotspotPos[4] 的固定點，邏輯跟 MLegKneeHotspotNudge／UpdateMLegKneeHotspotTracking
    // 完全同一套，只是只有一個點、沒有左右之分。
    // 「往上 30px」跟 MLegKneeHotspotNudge 同一個座標系：GlobalPosition 直接加總的
    // 螢幕像素位移，Y 向下為正，所以「往上」是負值。
    [Export] public Vector2 MLegJijiHotspotNudge = new Vector2(0f, -20f);  // 使用者要求再向下10px

    /// <summary>「m腿/小穴」貼圖本身內容框中心點，數值來源：對 小穴_M.png 做 getbbox()
    /// 量出來的內容框 (860,1321,1098,1579) 取中心，皆為 2000x3200 攤平畫布上的像素座標
    /// （跟 MLegKneeLocalPoint／BreastPivotOverrides 同一套座標系，不用翻轉）。</summary>
    static readonly Vector2 MLegJijiLocalPoint = new Vector2(979f, 1450f);

    // ════════════════════════════════════════════════════════════
    // 使用者要求：沒開 M 腿時，拖曳膝蓋熱點（開 M 腿的手勢）過程中，對應的腿要先做一點
    // 輕微旋轉當作視覺回饋（腿1＝右腿逆時針、腿2＝左腿順時針），拖到判定成功後再走
    // 既有的「腿」↔「m腿」交叉淡出/淡入（ToggleMLegModeAsync），拖曳取消（沒拖到閾值就
    // 放開）則旋轉歸零。旋轉幅度隨拖曳距離（0～MLegKneeDragThreshold）线性growth到
    // LegDragRotationAmount，見 Interaction.cs 的 UpdateKneeDragRotationFeedback()。
    // ════════════════════════════════════════════════════════════
    [Export] public float LegDragRotationAmount = 3f;  // 拖到閾值時的最大旋轉角度（度）

    /// <summary>拖曳旋轉回饋要用的軸心點：「腿/腿1」（右腿）「腿/腿2」（左腿）貼圖內容框
    /// 的「上緣中點」，近似當作大腿根部（髖部）的位置——旋轉軸心設在這裡，視覺上才像
    /// 「大腿根部不動、整條腿小幅度擺動」，不是像預設的整張畫布中心那樣繞著小腿中段轉。
    /// 數值來源：對 腿1.png/腿2.png 做 getbbox() 量出來的內容框，取 (中心X, 上緣Y)，
    /// 皆為 2000x3200 攤平畫布上的像素座標（跟 MLegKneeLocalPoint 同一套座標系）。
    /// 只有在對應腿有非 0 的 LegDragExtraRotation 時才會套用這個 PivotOffset，沒有在拖曳時
    /// UpdateLegLayerDetails() 會把 PivotOffset 還原成預設中心，不影響既有的呼吸/原地
    /// 搖擺動畫視覺。</summary>
    static readonly Dictionary<string, Vector2> LegDragPivotOverride = new()
    {
        { "腿/腿1", new Vector2(1125.5f, 1289f) }, // 右腿
        { "腿/腿2", new Vector2(846.5f,  1347f) }, // 左腿
        // 腳/足跟著同一條腿用同一個髖部軸心轉，才不會跟腿的底部脫節（接縫對不起來）
        { "腿/腳1", new Vector2(1125.5f, 1289f) },
        { "腿/足1", new Vector2(1125.5f, 1289f) },
        { "腿/腳2", new Vector2(846.5f,  1347f) },
        { "腿/足2", new Vector2(846.5f,  1347f) },
    };

    /// <summary>UpdateKneeDragRotationFeedback() 每幀寫入、UpdateLegLayerDetails() 每幀讀取
    /// 並疊加在既有的呼吸/原地搖擺旋轉之上（跟 ExprLayerExtraOffset／TouchAnimOffset 是同一種
    /// 「額外疊加量」寫法，避免兩套系統搶著改同一個 RotationDegrees）。單位：度，
    /// 已經是最終要疊加的 Godot RotationDegrees 正負號（正＝順時針），不用再轉換。</summary>
    public readonly Dictionary<string, float> LegDragExtraRotation = new()
    {
        { "腿/腿1", 0f },
        { "腿/腿2", 0f },
        { "腿/腳1", 0f },
        { "腿/腳2", 0f },
        { "腿/足1", 0f },
        { "腿/足2", 0f },
    };

    // ════════════════════════════════════════════════════════════
    // 使用者要求：左右膝蓋熱點聯動——拖哪一邊都會讓兩條腿一起有旋轉回饋（不再只有被拖的
    // 那一邊動），同時再加上兩個「一點點就好」的拉伸效果：小穴左右拉伸、身體下緣拉伸，
    // 幅度都跟拖曳進度（0～1，同 LegDragExtraRotation 的 progress）成正比。
    // 沿用「後髮尾端呼吸擴張」那套 fragment shader（見 BuildUI.cs 的
    // SetupHairBottomFlareShaders／_hairFlareShader），幫「腿/小穴」「身體」各掛一份獨立的
    // ShaderMaterial，只在拖曳過程驅動 expand_amount，跟頭髮呼吸擴張完全獨立、互不影響。
    // 「腿/小穴」：taper_start_v=0、taper_end_v=1、base_ratio=1，代表整張貼圖統一比例的
    // 左右拉伸（不用像頭髮那樣分「髮根到髮尾」漸層，小穴本身範圍很小，要整個一起動）。
    // 「身體」：taper_start_v/taper_end_v 集中在內容框下緣附近，base_ratio=0，只有下緣
    // （接近腿部）附近會拉伸，上半身完全不動。
    // centerU/taperStartV/taperEndV 皆用 Python 對 小穴.png／身體.png 做 getbbox() 量出來的
    // 內容框換算（跟 HairBottomFlareRegions 同一套座標系：centerU=內容框中心X/畫布寬度，
    // taperStartV/taperEndV=內容框相對高度位置/畫布高度）。
    // ════════════════════════════════════════════════════════════
    [Export] public float MLegDragJiXueStretchAmount = 0.3f; // 小穴左右拉伸最大量（使用者要求維持一點點就好，不用像身體那樣加大）
    [Export] public float MLegDragBodyStretchAmount  = 0.07f; // 身體下緣拉伸最大量（使用者要求再大一點）

    static readonly Dictionary<string, Vector3> MLegDragStretchRegions = new()
    {
        { "m腿/小穴", new Vector3(0.4895f, 0f,      1f) },     // M 腿小穴內容框 (860..1098)，中心 979/2000
        { "腿/小穴", new Vector3(0.4883f, 0f,      1f) },      // 內容框 (766,1384)-(1187,1611)，整張統一拉伸
        { "身體",   new Vector3(0.5225f, 0.3716f, 0.5472f) }, // 內容框 (378,145)-(1712,1751)，下緣附近才拉伸
    };
    readonly Dictionary<string, ShaderMaterial> _mLegDragStretchMaterials = new();

    // 使用者要求：拖曳膝蓋熱點的旋轉回饋同時，腿1/腿2（右腿/左腿）連同足1/足2（右腳掌/
    // 左腳掌）也要跟著往左右移動一點，拖滿（拖曳進度=1）時最多移動 LegDragOffsetXAmount
    // （8px）。方向規則：腿1/足1 是右側 → 往右（+X）；腿2/足2 是左側 → 往左（−X），
    // 跟旋轉回饋（LegDragExtraRotation）同一套「聯動、拖哪邊兩邊都動」邏輯、同一個
    // progress 值，只是這次是位移不是旋轉。只套用在「腿」（未開 M 腿時可見的一般腿），
    // 不含「m腿」「腳」，範圍見 Interaction.cs 的 UpdateKneeDragRotationFeedback()。
    [Export] public float LegDragOffsetXAmount = 8f;
    /// <summary>腳1/腳2 拖曳時比腿/足多移動的像素（拖滿時總位移 = LegDragOffsetXAmount + 這個值）。</summary>
    [Export] public float LegDragFootExtraX = 0f;
    /// <summary>拖到最開時，整條腿（腿1/腿2/腳1/腳2/足1/足2）一起往內收的像素；用拖曳進度的平方，越接近最開收得越多，剛開始拖幾乎不影響。</summary>
    [Export] public float LegDragFootInwardX = 10f;
    /// <summary>拖曳時腿旋轉會讓綠點被帶向內側，這是往外補償的像素（拖滿時），左點往左、右點往右。</summary>
    [Export] public float LegDragKneeDotCompensation = 2f;

    /// <summary>UpdateKneeDragRotationFeedback() 每幀寫入、UpdateLegLayerDetails() 每幀讀取
    /// 並疊加在 Position.X 上（跟 LegDragExtraRotation 是同一種「額外疊加量」寫法）。
    /// 單位：像素，已經是最終要疊加的方向（右側正、左側負），不用再轉換。</summary>
    public readonly Dictionary<string, float> LegDragExtraOffsetX = new()
    {
        { "腿/腿1", 0f },
        { "腿/腿2", 0f },
        { "腿/腳1", 0f },
        { "腿/腳2", 0f },
        { "腿/足1", 0f },
        { "腿/足2", 0f },
    };

    [ExportGroup("摸胸速度等級（長按時累積，決定舒服/不舒服路線）")]
    [Export] public float BreastSpeedLevelMax  = 5f;
    [Export] public float BreastSpeedLevelRate = 0.005f;
    [Export] public float BreastSpeedPerLevel  = 0.4f;

    // ════════════════════════════════════════════════════════════
    // 使用者新規則：摸胸時高潮度會持續自動增長（跟熱點點擊那次性 +3 ComfortChest
    // 的算法是兩條獨立路徑），規則如下：
    //   • 單手摸：一開始每 5 秒 +1 高潮度；雙手一起摸：一開始每 4 秒 +1。
    //   • 不管單手雙手，只要「持續摸」滿 10 秒，這個間隔秒數就 +2（5→7→9→11→13→15，
    //     或 4→6→8→10→12→14），單手上限 15 秒、雙手上限 14 秒，到上限就不再延長。
    //   • 「無論單手雙手，只要延長後就不能再削減」——也就是這個間隔秒數只會越變越大，
    //     不會因為放開手、或單手/雙手互相切換而被打回比較小的值（詳見 Interaction.cs
    //     的 UpdateClimaxTouchGrowth() 實作說明）。
    // ════════════════════════════════════════════════════════════
    [ExportGroup("摸胸持續增長高潮度（單手/雙手，越摸間隔越長）")]
    [Export] public float ClimaxTouchIntervalSingleStart = 5f;  // 單手摸：一開始幾秒 +1 高潮度
    [Export] public float ClimaxTouchIntervalDualStart   = 4f;  // 雙手一起摸：一開始幾秒 +1 高潮度
    [Export] public float ClimaxTouchExtendStep          = 2f;  // 每次延長幾秒
    [Export] public float ClimaxTouchExtendEvery         = 10f; // 持續摸滿幾秒觸發一次延長
    [Export] public float ClimaxTouchIntervalSingleMax   = 15f; // 單手模式的間隔秒數上限
    [Export] public float ClimaxTouchIntervalDualMax     = 14f; // 雙手模式的間隔秒數上限

    // 上面那套機制的執行期計時狀態，不開放 Inspector 調（調了也會在下次摸胸時被邏輯蓋掉）：
    //   _climaxTouchInterval：目前生效中的「幾秒 +1 高潮度」秒數，0 代表還沒開始摸過。
    //   只會被加大，不會被縮小——放手時就地暫停（timer 不歸零、interval 不重置），
    //   下次不管用單手還雙手繼續摸，都從暫停的地方接著算。
    float _climaxTouchInterval;
    float _climaxTouchTickElapsed;   // 距離下一次「+1 高潮度」已經經過的秒數
    float _climaxTouchExtendElapsed; // 距離下一次「間隔 +2 秒」已經持續摸了幾秒

    /// <summary>觸摸動畫每幀疊加給「胸1」「胸2」的額外位移，供 UpdateBreathing() 的
    /// 胸部呼吸迴圈讀取並疊加在 basePos 之上——不能讓這裡直接寫 Layers["胸1"].Position，
    /// 那會跟每幀都在跑的胸部呼吸迴圈互搶同一個 Position（跟之前 ZZZ 眼皮翻轉遇到的
    /// SlideParts 對呼吸迴圈的衝突是同一種問題），比照 ExprLayerExtraOffset 的做法解決。
    /// 動畫沒在跑的部位不會出現在這個字典裡（呼吸迴圈讀不到就當作 0）。</summary>
    public readonly Dictionary<string, Vector2> TouchAnimOffset = new();

    /// <summary>「胸1」「胸2」貼圖實際內容的像素外框中心（拿來覆蓋 BuildCharacter() 預設的
    /// 整張畫布中心 pivot），觸摸動畫要對胸部做小角度旋轉時，一定要繞著「畫出來的那一小塊」
    /// 轉，不能繞著整張 2000x3200 透明畫布的中心轉——否則角度雖然只有幾度，畫面上會變成
    /// 一大段位移／甩飛（跟之前 EyeCloseController 的翻轉樞紐點是同一種 bug，這裡直接沿用
    /// 同樣的修法：用 Python/PIL 量測 getbbox() 算出來的中心點）。</summary>
    static readonly Dictionary<string, Vector2> BreastPivotOverrides = new()
    {
        { "胸1", new Vector2(977f, 998f) },
        { "胸2", new Vector2(873f, 988f) },
    };

    // 私有狀態
    Control _touchHandLeft, _touchHandRight;   // 觸摸動畫用的手部游標（非角色本身的手臂圖層）
    Control _touchHandLayer;                   // 手部游標共用的 FullRect 中介層（掛在 _canvas 下）
    bool    _touchAnimRunningLeft  = false;
    bool    _touchAnimRunningRight = false;
    float   _breastSpeedLevel = 0f;

    // ════════════════════════════════════════════════════════════
    // 頭髮尾端呼吸擴張（使用者要求：吸氣時「後髮」「前髮」最下面（貼圖實際畫出來的內容，
    // 不是整張 2000x3200 透明畫布的邊緣）左右微微撐開一點）。
    // 用 canvas_item 頂點 shader 做局部形變：只有 taper_start_y ~ taper_end_y 這段區間
    // 會被往外推，範圍以上（頭髮上半段/髮根）完全不動，避免整張圖被均勻拉寬變形。
    // ════════════════════════════════════════════════════════════

    // 使用者反應加上 base_ratio（上半段也跟著動）之後，整體動的幅度變太大了，
    // 從 0.25 砍到 0.12，又反應還是太大，再砍到 0.04（expand_amount 是上下段共用的整體
    // 強度係數，調小這一個數字就會讓「上半段的一點點」跟「髮尾的最多」同時等比例縮小，
    // 不用兩個數字分開調）。
    [Export] public float HairBottomExpandAmount = 0.04f;
    // 使用者反應「有了那個感覺，但不要只有髮尾在動」——原本 taper 區間以外（髮根／上半段）
    // 完全不動（權重＝0），這裡改成上半段也跟著動一點點（權重＝HairFlareBaseRatio），
    // 髮尾（taper 區間內）維持動最多（權重最終還是收斂到 1），讓整層頭髮看起來是「一起在動，
    // 只是越下面動越多」，不是死板地只有最下緣在動。
    [Export] public float HairFlareBaseRatio = 0.25f;

    // 使用者要求：後髮擴散（吸氣）的同時，兩條小辮子（後髮2＝左、後髮3＝右）也要各自
    // 向左右位移一點點意思一下，不用像後髮1/前髮那樣做完整的左右撐開形變（辮子有自己
    // 偏移的旋轉樞紐，直接套用形變 shader 會歪，見上面 HairBottomFlareRegions 的說明）——
    // 用最簡單的位置位移就好，跟後髮1/前髮共用同一個 inhale（吸氣）相位，數值刻意調小
    // （「一點點就好」），不需要 Inspector 常常調，先給常數。
    [Export] public float BraidExpandMoveX = 3f;

    /// <summary>「後髮1/2/3」「前髮」四層的形變區間，全部改用貼圖 UV（0~1）正規化座標，
    /// 不用像素座標——原本用像素座標＋VERTEX 的版本會整張圖只往同一個方向偏移（使用者
    /// 回報「只有向右移動，沒有左右擴張的感覺」），改用 UV 之後不管 Control 實際的
    /// Size/Position/Scale 怎麼變，UV.x/UV.y 保證就是「這個像素在整張貼圖裡的相對位置」，
    /// 不會再受任何座標系假設影響。
    /// centerU＝該圖層內容的左右中心線（bbox 中心 X / 貼圖寬度），
    /// taperStartV/taperEndV＝開始往外推～完全推到底的高度（bbox 上緣～下緣 / 貼圖高度，
    /// 不是整張 2000x3200 畫布的下緣，畫布下面一大段是透明留白，用畫布下緣當基準會看不出效果）。
    /// 四張圖畫布寬度都是 2000px，所以 widthPx 統一用 2000（用來把 uv 差值換算回實際像素位移）。</summary>
    // 只留「後髮1」（主要後髮塊）和「前髮」（瀏海）：這兩層維持 BuildCharacter 預設的
    // 整張畫布正中心 pivot（(0.5,0.5)），本身也沒有旋轉動畫，套用擴張效果才會左右對稱。
    // 「後髮2」「後髮3」原本也在清單裡，但這兩層是有自己一套持續搖擺動畫的辮子，旋轉樞紐
    // 刻意設在髮根（BraidAttachPivot1/2＝0.588/0.37，明顯偏左或偏右，不是內容中心），
    // 摆动時繞著那個偏移的樞紐轉，會讓疊加上去的擴張效果看起來像「一直被往某一邊拉」
    // ——這應該就是使用者回報「頭髮只往右擴張、感覺錨點在左邊」的真正原因，
    // 所以先把這兩層從擴張清單移除，等主要兩層的效果調到滿意後，如果還要幫辮子也做，
    // 需要另外設計「先抵消樞紐偏移」的版本。
    static readonly Dictionary<string, Vector3> HairBottomFlareRegions = new()
    {
        // (centerU, taperStartV, taperEndV) —— 全部是 0~1 的正規化座標
        { "後髮1", new Vector3(992.5f / 2000f, 1224f / 3200f, 1631f / 3200f) },
        { "前髮",  new Vector3(957.5f / 2000f,  751f / 3200f,  950f / 3200f) },
    };
    const float HairFlareCanvasWidthPx = 2000f;

    static Shader _hairFlareShader;
    readonly Dictionary<string, ShaderMaterial> _hairFlareMaterials = new();
}
