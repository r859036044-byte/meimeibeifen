using Godot;

/// <summary>
/// 遊戲流程管理 - 難度選擇 → 劇情確認 → 進遊戲
/// 作為 Autoload 單例，貫穿整個遊戲
/// 負責場景切換、16:9 強制比例、自訂游標
/// </summary>
public partial class GameFlowManager : Node
{
    // ═══════════════════════════════════════════════════════════
    //  單例
    // ═══════════════════════════════════════════════════════════
    public static GameFlowManager Instance { get; private set; }

    // ═══════════════════════════════════════════════════════════
    //  難度
    // ═══════════════════════════════════════════════════════════
    public enum Difficulty { Easy, Normal, Hard }
    public Difficulty CurrentDifficulty { get; private set; } = Difficulty.Normal;
    public bool SkipPrologue { get; private set; } = false;

    // 難度名稱與描述
    public static readonly string[] DiffNames = { "初心者", "普通", "挑戰者" };
    public static readonly string[] DiffDescs = {
        "適合初次遊玩\n敵人較弱，資源充裕",
        "標準遊戲體驗\n推薦大多數玩家",
        "高難度挑戰\n敵人強力，不能大意"
    };

    // ═══════════════════════════════════════════════════════════
    //  自訂游標
    // ═══════════════════════════════════════════════════════════
    private static Resource _customCursorRes;

    // ═══════════════════════════════════════════════════════════
    //  16:9 比例
    // ═══════════════════════════════════════════════════════════
    private const float TargetAspect = 16f / 9f;

    // 設定檔
    private const string ConfigPath = "user://settings.cfg";
    private const string Section    = "game";

    // ═══════════════════════════════════════════════════════════
    //  生命週期
    // ═══════════════════════════════════════════════════════════
    public override void _Ready()
    {
        if (Instance != null && Instance != this)
        {
            QueueFree();
            return;
        }
        Instance = this;

        // 載入自訂游標（res://Resources/Sprites/youbiao.png）
        ApplyCustomCursor();

        // 載入設定
        LoadSettings();
    }

    public override void _Process(double delta)
    {
        // Godot 的 stretch mode "canvas_items" + aspect "keep" 已自動處理等比縮放
        // 不需要像 Unity 手動計算 viewport
        // 若需要視窗模式下鎖定比例，可在此處理
        var mode = DisplayServer.WindowGetMode();
        if (mode != DisplayServer.WindowMode.Fullscreen && mode != DisplayServer.WindowMode.ExclusiveFullscreen)
        {
            var winSize = DisplayServer.WindowGetSize();
            int w = winSize.X;
            int h = winSize.Y;

            // 確保最小尺寸
            if (w < 960)
            {
                w = 960;
                h = 540;
                DisplayServer.WindowSetSize(new Vector2I(w, h));
            }
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  自訂游標
    // ═══════════════════════════════════════════════════════════

    /// <summary>套用自訂游標（供場景切換後重新呼叫）</summary>
    public static void ApplyCustomCursor()
    {
        if (_customCursorRes == null)
        {
            // 嘗試載入游標圖片
            string path = "res://Resources/Sprites/youbiao.png";
            if (ResourceLoader.Exists(path))
            {
                _customCursorRes = ResourceLoader.Load(path);
            }
        }

        if (_customCursorRes is Texture2D cursorTex)
        {
            // 熱點設在圖片中心
            var hotspot = new Vector2(cursorTex.GetWidth() / 2f, cursorTex.GetHeight() / 2f);
            Input.SetCustomMouseCursor(_customCursorRes, Input.CursorShape.Arrow, hotspot);
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  難度 & 劇情設定
    // ═══════════════════════════════════════════════════════════
    public void SetDifficulty(Difficulty d) => CurrentDifficulty = d;
    public void SetSkipPrologue(bool skip)  => SkipPrologue = skip;

    // ═══════════════════════════════════════════════════════════
    //  場景切換
    // ═══════════════════════════════════════════════════════════

    /// <summary>前往狐狸村房間（Hub 場景）</summary>
    public void GoToFoxVillage()
    {
        SaveSettings();
        GetTree().ChangeSceneToFile("res://Scenes/FoxVillage.tscn");
    }

    /// <summary>開始遊戲（進入指定關卡）</summary>
    public void StartGame(string sceneName = "Level1")
    {
        SaveSettings();
        GetTree().ChangeSceneToFile($"res://Scenes/{sceneName}.tscn");
    }

    /// <summary>進入調教房場景</summary>
    public void GoToTrainingRoom()
    {
        SaveSettings();
        GetTree().ChangeSceneToFile("res://Scenes/TrainingRoom.tscn");
    }

    /// <summary>進入溫泉場景</summary>
    public void GoToHotSpring()
    {
        SaveSettings();
        GetTree().ChangeSceneToFile("res://Scenes/HotSpring.tscn");
    }

    /// <summary>切換到任意場景（通用）</summary>
    public void GoToScene(string sceneName)
    {
        SaveSettings();
        GetTree().ChangeSceneToFile($"res://Scenes/{sceneName}.tscn");
    }

    // ═══════════════════════════════════════════════════════════
    //  設定儲存 / 載入
    // ═══════════════════════════════════════════════════════════
    private void SaveSettings()
    {
        var cfg = new ConfigFile();
        // 先載入已有的設定（避免覆蓋音量等其他設定）
        cfg.Load(ConfigPath);
        cfg.SetValue(Section, "difficulty",    (int)CurrentDifficulty);
        cfg.SetValue(Section, "skip_prologue", SkipPrologue);
        cfg.Save(ConfigPath);
    }

    private void LoadSettings()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(ConfigPath) != Error.Ok) return;

        CurrentDifficulty = (Difficulty)(int)cfg.GetValue(Section, "difficulty", (int)Difficulty.Normal);
        SkipPrologue      = (bool)cfg.GetValue(Section, "skip_prologue", false);
    }
}
