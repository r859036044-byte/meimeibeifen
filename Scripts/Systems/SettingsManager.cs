using Godot;
using System;

/// <summary>
/// 全域設定管理（音量、語言、文字速度等）
/// 作為 Autoload 單例，貫穿整個遊戲
/// 使用 ConfigFile 取代 Unity 的 PlayerPrefs
/// </summary>
public partial class SettingsManager : Node
{
    // ═══════════════════════════════════════════════════════════
    //  單例
    // ═══════════════════════════════════════════════════════════
    public static SettingsManager Instance { get; private set; }

    // ═══════════════════════════════════════════════════════════
    //  設定值
    // ═══════════════════════════════════════════════════════════
    public float MasterVolume  { get; private set; } = 1f;
    public float MusicVolume   { get; private set; } = 0.8f;
    public float SFXVolume     { get; private set; } = 1f;
    public float VoiceVolume   { get; private set; } = 1f;
    public int   LanguageIndex { get; private set; } = 0;
    public bool  IsFullscreen  { get; private set; } = true;
    public int   TextSpeed     { get; private set; } = 1;
    /// <summary>調教房互動物件（手、舌頭）的不透明度 0.2~1。</summary>
    public float InteractAlpha { get; private set; } = 0.85f;
    public int ResolutionIndex { get; private set; } = 4;
    public static readonly Vector2I[] Resolutions = { new(960, 540), new(1280, 720), new(1366, 768), new(1600, 900), new(1920, 1080), new(2560, 1440), new(3840, 2160) };
    public static readonly string[] ResolutionLabels = { "960 × 540", "1280 × 720", "1366 × 768", "1600 × 900", "1920 × 1080", "2560 × 1440", "3840 × 2160" };

    // ═══════════════════════════════════════════════════════════
    //  常數
    // ═══════════════════════════════════════════════════════════
    public static readonly string[] Languages  = { "繁體中文", "简体中文", "English", "日本語" };
    public static readonly string[] TextSpeeds = { "慢", "普通", "快" };
    public static readonly float[]  TypeValues = { 0.06f, 0.03f, 0.01f };

    /// <summary>音量變更時觸發，供各場景即時更新音量</summary>
    public static event Action OnVolumeChanged;

    // 設定檔路徑
    private const string ConfigPath    = "user://settings.cfg";
    private const string Section       = "audio";
    private const string SectionUI     = "ui";
    private const string SectionGame   = "game";

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
        Load();

        // 延遲一幀確保視窗設定生效，避免被引擎初始化覆蓋
        Callable.From(() =>
        {
            if (IsFullscreen)
                SetFullscreen(true);
            else
                ApplyResolution();
        }).CallDeferred();
    }

    // ═══════════════════════════════════════════════════════════
    //  音量設定
    // ═══════════════════════════════════════════════════════════

    /// <summary>設定主音量（控制 Master 音訊匯流排）</summary>
    public void SetMasterVolume(float v)
    {
        MasterVolume = Mathf.Clamp(v, 0f, 1f);
        ApplyMasterVolume();
        OnVolumeChanged?.Invoke();
    }

    /// <summary>設定音樂音量</summary>
    public void SetMusicVolume(float v)
    {
        MusicVolume = Mathf.Clamp(v, 0f, 1f);
        OnVolumeChanged?.Invoke();
    }

    /// <summary>設定音效音量</summary>
    public void SetSFXVolume(float v)
    {
        SFXVolume = Mathf.Clamp(v, 0f, 1f);
        OnVolumeChanged?.Invoke();
    }

    /// <summary>設定語音音量</summary>
    public void SetVoiceVolume(float v)
    {
        VoiceVolume = Mathf.Clamp(v, 0f, 1f);
        OnVolumeChanged?.Invoke();
    }

    public void SetLanguage(int i)
    {
        LanguageIndex = Mathf.Clamp(i, 0, Languages.Length - 1);
        string[] locales = { "zh_TW", "zh_CN", "en", "ja" };
        TranslationServer.SetLocale(locales[LanguageIndex]);
    }
    public void SetResolution(int i)
    {
        ResolutionIndex = Mathf.Clamp(i, 0, Resolutions.Length - 1);
        if (!IsFullscreen)
        {
            ApplyResolution();
        }
    }

    /// <summary>套用解析度至視窗並置中</summary>
    public void ApplyResolution()
    {
        var targetSize = Resolutions[ResolutionIndex];

        // 1. 同步設置高階 Window 屬性（確保 Godot 編輯器內嵌遊戲檢視 SubWindow 響應尺寸變化）
        var win = GetWindow();
        if (win != null)
        {
            win.Mode = Window.ModeEnum.Windowed;
            win.Size = targetSize;
        }

        // 2. 設置底層作業系統視窗（適用於獨立視窗與發布後 .exe 遊戲）
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        DisplayServer.WindowSetSize(targetSize);

        var area = DisplayServer.ScreenGetUsableRect();
        var posX = area.Position.X + Mathf.Max(0, (area.Size.X - targetSize.X) / 2);
        var posY = area.Position.Y + Mathf.Max(0, (area.Size.Y - targetSize.Y) / 2);
        DisplayServer.WindowSetPosition(new Vector2I(posX, posY));
    }

    public void SetTextSpeed(int i)   => TextSpeed = Mathf.Clamp(i, 0, TypeValues.Length - 1);
    public void SetInteractAlpha(float a) => InteractAlpha = Mathf.Clamp(a, 0.2f, 1f);

    /// <summary>設定全螢幕</summary>
    public void SetFullscreen(bool f)
    {
        IsFullscreen = f;
        var win = GetWindow();
        if (f)
        {
            if (win != null)
                win.Mode = Window.ModeEnum.Fullscreen;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
        }
        else
        {
            if (win != null)
                win.Mode = Window.ModeEnum.Windowed;
            ApplyResolution();
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  儲存 / 載入（使用 ConfigFile）
    // ═══════════════════════════════════════════════════════════

    /// <summary>儲存設定到檔案</summary>
    public void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue(Section, "master",  MasterVolume);
        cfg.SetValue(Section, "music",   MusicVolume);
        cfg.SetValue(Section, "sfx",     SFXVolume);
        cfg.SetValue(Section, "voice",   VoiceVolume);
        cfg.SetValue(SectionUI,   "language",   LanguageIndex);
        cfg.SetValue(SectionUI,   "fullscreen", IsFullscreen);
        cfg.SetValue(SectionUI,   "textspeed",  TextSpeed);
        cfg.SetValue(SectionUI,   "resolution", ResolutionIndex);
        cfg.SetValue(SectionUI,   "interact_alpha", InteractAlpha);
        // 全年齡 / R18 設定
        bool r18 = PlayerDataManager.Instance?.R18ContentEnabled ?? false;
        cfg.SetValue(SectionGame, "r18",        r18);
        cfg.Save(ConfigPath);
    }

    /// <summary>從檔案載入設定</summary>
    public void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(ConfigPath) != Error.Ok)
        {
            // 檔案不存在，使用預設值
            ApplyMasterVolume();
            SetFullscreen(IsFullscreen);
            return;
        }

        MasterVolume  = (float)cfg.GetValue(Section, "master",  1.0f);
        MusicVolume   = (float)cfg.GetValue(Section, "music",   0.8f);
        SFXVolume     = (float)cfg.GetValue(Section, "sfx",     1.0f);
        VoiceVolume   = (float)cfg.GetValue(Section, "voice",   1.0f);
        LanguageIndex = (int)cfg.GetValue(SectionUI, "language",   0);
        IsFullscreen  = (bool)cfg.GetValue(SectionUI, "fullscreen", true);
        TextSpeed     = (int)cfg.GetValue(SectionUI, "textspeed",  1);
        SetInteractAlpha((float)cfg.GetValue(SectionUI, "interact_alpha", 0.85f));

        SetResolution((int)cfg.GetValue(SectionUI, "resolution", 4));
        SetLanguage(LanguageIndex);
        SetTextSpeed(TextSpeed);
        // 全年齡 / R18 設定（預設 true = R18 開啟）
        bool savedR18 = (bool)cfg.GetValue(SectionGame, "r18", false);
        if (PlayerDataManager.Instance != null)
            PlayerDataManager.Instance.SetR18(savedR18);
        // 套用音量
        ApplyMasterVolume();
        OnVolumeChanged?.Invoke();
        SetFullscreen(IsFullscreen);
    }

    /// <summary>將主音量套用到 Godot 音訊匯流排</summary>
    private void ApplyMasterVolume()
    {
        // 使用 Master 匯流排控制全域音量
        int busIdx = AudioServer.GetBusIndex("Master");
        if (busIdx >= 0)
        {
            // 音量 0 時靜音；否則轉換為分貝
            if (MasterVolume <= 0f)
                AudioServer.SetBusMute(busIdx, true);
            else
            {
                AudioServer.SetBusMute(busIdx, false);
                AudioServer.SetBusVolumeDb(busIdx, Mathf.LinearToDb(MasterVolume));
            }
        }
    }
}
