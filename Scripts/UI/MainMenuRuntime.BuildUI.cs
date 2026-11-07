using Godot;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// MainMenuRuntime — 第 3 段：UI 建構
/// 對應 Unity 原始碼 MainMenuRuntime.cs 第 1213~2052 行
/// 內容：Build / BuildLogoPanel / BuildMainPanel / BuildSkipConfirmPanel /
///       BuildSettingsPanel / BuildSoundRow / BuildCycleRow
/// </summary>
public partial class MainMenuRuntime : Control
{
    // ═══════════════════════════════════════════════════════════
    //  UI 總建構進入點
    // ═══════════════════════════════════════════════════════════

    void Build()
    {
        // Unity 的 transform 根節點，在 Godot 就是本 Control 自己
        Node t = this;

        // 全域背景（狐狸村背景圖）
        var globalBGGO = Img(t, "GlobalBG", new Color(0.06f, 0.02f, 0.04f),
            V2(0, 0), V2(1, 1), V2(0, 0), V2(0, 0));
        var globalBGTex = LoadTex("Backgrounds/bg_LivingRoom");
        if (globalBGTex != null)
        {
            // Godot 的純色用 ColorRect、貼圖用 TextureRect，
            // 因此有貼圖時額外疊一層 TextureRect 蓋住底色
            var globalBGImg = new TextureRect
            {
                Name        = "GlobalBGTex",
                Texture     = globalBGTex,
                ExpandMode  = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            globalBGGO.AddChild(globalBGImg);
            globalBGImg.SetAnchorsPreset(LayoutPreset.FullRect);
        }

        // ── Logo畫面 ──────────────────────────────────────────────
        _logoPanel = BuildLogoPanel(t);

        // ── 主選單 ────────────────────────────────────────────────
        _mainPanel = BuildMainPanel(t);
        SetActive(_mainPanel, false);

        // ── 設定面板 ──────────────────────────────────────────────
        _settingsPanel = BuildSettingsPanel(t);
        SetActive(_settingsPanel, false);

        // ── 跳過劇情確認 ──────────────────────────────────────────
        _skipPanel = BuildSkipConfirmPanel(t);
        SetActive(_skipPanel, false);

        // ── 圖鑒房（在對話框之下，主選單之上）──────────────────────
        _galleryScreen = new GalleryScreen { Name = "GalleryScreen" };
        AddChild(_galleryScreen);

        // ── 劇情對話框（最上層）──────────────────────────────────
        _dialoguePanel = BuildDialoguePanel(t);
        SetActive(_dialoguePanel, false);
    }

    // ── Logo開場面板 ─────────────────────────────────────────────

    Control BuildLogoPanel(Node parent)
    {
        var panel = Img(parent, "LogoPanel", new Color(0, 0, 0, 1),
            V2(0, 0), V2(1, 1), V2(0, 0), V2(0, 0));
        Node pt = panel;

        // 工作室Logo（LOGO2.png）
        var studioSp = LoadTex("UI/LOGO");
        if (studioSp != null)
        {
            // preserveAspect = true → KeepAspectCentered
            ImgTex(pt, "StudioLogo", studioSp,
                V2(0.5f, 0.5f), V2(0.5f, 0.5f), V2(0, 30), V2(1080, 450), true);
        }
        else
        {
            // 找不到圖時退回半透明色塊 + 文字
            var slGO = new ColorRect
            {
                Name        = "StudioLogo",
                Color       = new Color(0.15f, 0.05f, 0.08f, 0.6f),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            panel.AddChild(slGO);
            ApplyPointAnchor(slGO, V2(0.5f, 0.5f), V2(0.5f, 0.5f), V2(0, 30), V2(1080, 450));

            Txt(slGO, "T", "吉太研究部", 56, new Color(1f, 0.85f, 0.4f), UAlign.MiddleCenter,
                V2(0, 0), V2(1, 1), V2(0, 0), V2(0, 0));
        }

        // （"presents" 字樣已移除）

        return panel;
    }

    // ── 主選單面板 ───────────────────────────────────────────────

    Control BuildMainPanel(Node parent)
    {
        var panel = new Control { Name = "MainPanel", MouseFilter = MouseFilterEnum.Pass };
        parent.AddChild(panel);
        ApplyStretchAnchor(panel, V2(0, 0), V2(1, 1), V2(0, 0), V2(0, 0));

        Node pt = panel;

        // 背景（客廳背景圖）
        var bgGO = Img(pt, "BG", new Color(0.08f, 0.03f, 0.05f),
            V2(0, 0), V2(1, 1), V2(0, 0), V2(0, 0));
        var bgSp = LoadTex("Backgrounds/bg_LivingRoom");
        if (bgSp != null)
        {
            var bgImg = new TextureRect
            {
                Name        = "BGTex",
                Texture     = bgSp,
                ExpandMode  = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            bgGO.AddChild(bgImg);
            bgImg.SetAnchorsPreset(LayoutPreset.FullRect);
        }

        // （暫時移除：底部/頂部裝飾、標題圖片、副標題、裝飾線）

        // 按鈕容器（用於統一隱藏/顯示）
        var btnContainer = new Control { Name = "BtnContainer", MouseFilter = MouseFilterEnum.Pass };
        panel.AddChild(btnContainer);
        ApplyStretchAnchor(btnContainer, V2(0, 0), V2(1, 1), V2(0, 0), V2(0, 0));
        _menuBtnContainer = btnContainer;

        // 標題圖片（TitleName.png）— 用 TextureRect 避免 Sprite 裁切
        var titleTex = LoadTex("UI/TitleName");
        var titleGO = ImgTex(pt, "TitleName", titleTex,
            V2(0.5f, 0.5f), V2(0.5f, 0.5f), TitlePos, TitleSize);
        titleGO.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        if (titleTex == null) SetAlpha(titleGO, 0f);   // 沒有貼圖就完全透明
        _titleRT = titleGO;
        SetActive(titleGO, false);   // 跟按鈕一起顯示
        _titleGO = titleGO;

        // 按鈕（根據設計文件）
        // 置中、放大 20%（300→360, 80→96），間距 110px
        Node bct = btnContainer;
        var btnStart   = Btn(bct, "BtnStart",    "開始遊戲", new Color(0.60f, 0.15f, 0.18f), V2(0.5f, 0.5f), BtnStartPos);
        var btnLoad    = Btn(bct, "BtnLoad",     "繼續遊戲", new Color(0.22f, 0.14f, 0.38f), V2(0.5f, 0.5f), BtnLoadPos);
        var btnSett    = Btn(bct, "BtnSettings", "設　　定", new Color(0.12f, 0.15f, 0.22f), V2(0.5f, 0.5f), BtnSettPos);
        var btnQuit    = Btn(bct, "BtnQuit",     "退出遊戲", new Color(0.14f, 0.08f, 0.10f), V2(0.5f, 0.5f), BtnQuitPos);
        var btnGallery = Btn(bct, "BtnGallery",  "圖　　鑒", new Color(0.18f, 0.10f, 0.28f), V2(0.5f, 0.5f), BtnGalleryPos);
        _btnStartRT   = btnStart;
        _btnLoadRT    = btnLoad;
        _btnSettRT    = btnSett;
        _btnQuitRT    = btnQuit;
        _btnGalleryRT = btnGallery;

        btnGallery.Pressed += () => _galleryScreen?.Open();
        BuildMiniGameEntry(btnContainer);   // 左下角小遊戲入口

        // R18/全年齡切換按鈕已移到開發者工具面板（F9 召喚）裡，
        // 主選單不再直接顯示這顆按鈕。PlayerDataManager.SetR18/ToggleR18 邏輯保留不動。

        Txt(bct, "Ver", "v0.1 alpha", 14, new Color(1, 1, 1, 0.22f), UAlign.LowerRight,
            V2(1, 0), V2(1, 0), V2(-16, 14), V2(140, 28));

        // 事件
        btnStart.Pressed += () =>
        {
            NovelReader.ClearHistory();
            SetActive(_mainPanel, false);
            SetActive(_skipPanel, true);
        };
        btnLoad.Disabled = true;
        BtnLabel(btnLoad)?.AddThemeColorOverride("font_color", new Color("978993"));   // TODO: 存檔系統
        btnSett.Pressed += () =>
        {
            SetActive(_mainPanel, false);
            SetActive(_settingsPanel, true);
        };
        btnQuit.Pressed += () => GetTree().Quit();

        return panel;
    }

    // ── 跳過劇情確認面板 ─────────────────────────────────────────

    Control BuildSkipConfirmPanel(Node parent)
    {
        var panel = Img(parent, "SkipPanel", new Color(0, 0, 0, 0.88f),
            V2(0, 0), V2(1, 1), V2(0, 0), V2(0, 0));
        panel.MouseFilter = MouseFilterEnum.Stop;   // 遮罩需要擋住底下的點擊
        Node pt = panel;

        // 奶白卡片、細粉框與柔和投影。
        var box = new Panel { Name = "Box", MouseFilter = MouseFilterEnum.Stop };
        pt.AddChild(box);
        ApplyPointAnchor(box, V2(0.5f, 0.5f), V2(0.5f, 0.5f), Vector2.Zero, V2(660, 360));
        var card = MakeRoundRect(30, new Color("fffafd"), new Color("edb8cf"), 2);
        card.ShadowColor = new Color(0.34f, 0.12f, 0.24f, 0.22f);
        card.ShadowSize = 12;
        card.ShadowOffset = V2(0, 6);
        box.AddThemeStyleboxOverride("panel", card);
        Node bt = box;

        var badge = new HeartDecoration { Size = V2(26, 26), Position = V2(317, 20) };
        box.AddChild(badge);

        // 以框中心（0.5, 0.5）為基準，向上偏移
        var titleGO = Txt(bt, "Title", "要先看看故事的開始嗎？", 28, new Color("79475f"), UAlign.MiddleCenter,
            V2(0.5f, 0.5f), V2(0.5f, 0.5f), V2(0, 80), V2(540, 44));
        titleGO.AddThemeConstantOverride("outline_size", 0);
        Txt(bt, "Subtitle", "慢慢讀完序章，或直接開始遊戲", 17, new Color("a7778e"), UAlign.MiddleCenter,
            V2(0.5f, 0.5f), V2(0.5f, 0.5f), V2(0, 38), V2(520, 30));
        // 修正：Img() 用的是「拉伸錨點」語意，oMin/oMax 是從錨點算起的左右偏移量，不是置中寬高。
        // 原本 oMin=(0,0)/oMax=(520,2) 會讓線從卡片正中央開始往右延伸 520px，
        // 導致右側超出卡片邊界（就是畫面上看到的那條突出來的線）。
        // 改成以中心點左右對稱各 260px（共 520 寬），才會真的置中在卡片裡。
        Img(bt, "PinkDivider", new Color("f4dbe6"),
            V2(0.5f, 0.5f), V2(0.5f, 0.5f), V2(-260, -1), V2(260, 1));

        var btnWatch = Btn(bt, "BtnWatch", "觀看劇情", Colors.Black,
            V2(0.5f, 0.5f), V2(-130, -65), 200, 52);
        var btnSkip  = Btn(bt, "BtnSkip",  "直接開始", Colors.Black,
            V2(0.5f, 0.5f), V2(130, -65), 200, 52);
        foreach (var btn in new[] { btnWatch, btnSkip })
            BtnLabel(btn)?.AddThemeFontSizeOverride("font_size", 22);

        btnWatch.Pressed += () =>
        {
            GameFlowManager.Instance?.SetSkipPrologue(false);
            NewGameReset.ResetAll();   // 開新遊戲：所有變數歸零
            SetActive(panel, false);
            // 播放序章劇情
            TaskLogger.Run(PlayPrologue(_lifeCts.Token), "MainMenu.PlayPrologue");
        };
        btnSkip.Pressed += () =>
        {
            GameFlowManager.Instance?.SetSkipPrologue(true);
            NewGameReset.ResetAll();   // 開新遊戲：所有變數歸零
            SetActive(panel, false);
            // 跳過劇情 → 進入 TrainingRoom
            TaskLogger.Run(FadeToBlackAndLoad("TrainingRoom", _lifeCts.Token), "MainMenu.SkipToTrainingRoom");
        };

        return panel;
    }

    // ── 設定面板（純程式繪製版）─────────────────────────────────
    // 備份位置：Assets/Scripts/UI/BuildSettingsPanel_backup.cs.bak
    //
    // 註：Unity 版的 MakeRoundRect(w,h,r,...) 逐像素產生 Texture2D，
    //     Godot 改用 helpers 裡的 MakeRoundRect(r, fill, border, bw) 產生 StyleBoxFlat，
    //     StyleBoxFlat 會自動貼合節點大小，因此不需要 w/h 參數。
    //     Unity 版的 MakeSetPart() 全檔未被呼叫（死程式碼），故不移植。

    readonly Dictionary<string, OptionButton> _settingsDropdowns = new();

    Control BuildSettingsPanel(Node parent)
    {
        var panel = Img(parent, "Settings", new Color(0.14f, 0.07f, 0.12f, 0.82f),
            V2(0, 0), V2(1, 1), V2(0, 0), V2(0, 0));
        panel.MouseFilter = MouseFilterEnum.Stop;
        var card = MakeRoundRect(32, new Color("fffafd"), new Color("edb8cf"), 2);
        card.ShadowColor = new Color(0.34f, 0.12f, 0.24f, 0.22f);
        card.ShadowSize = 12; card.ShadowOffset = V2(0, 6);
        var bg = new Panel { Name = "SetBg", MouseFilter = MouseFilterEnum.Stop };
        panel.AddChild(bg);
        ApplyPointAnchor(bg, V2(0.5f, 0.5f), V2(0.5f, 0.5f), SetPanelPos, SetPanelSize);
        bg.AddThemeStyleboxOverride("panel", card);
        _setBgRT = bg;
        var head = new Control { Name = "SetHead", MouseFilter = MouseFilterEnum.Ignore };
        panel.AddChild(head);
        ApplyPointAnchor(head, V2(0.5f, 0.5f), V2(0.5f, 0.5f), SetHeadPos, SetHeadSize);
        _setHeadRT = head;
        _setHeadTxt = Txt(head, "Text", "偏好設定", 34, new Color("79475f"), UAlign.MiddleCenter,
            V2(0, 0), V2(1, 1), V2(0, 0), V2(0, 0));
        head.AddChild(new HeartDecoration { Position = V2(62, 21), Size = V2(22, 22) });
        head.AddChild(new HeartDecoration { Position = V2(336, 21), Size = V2(22, 22) });
        var labelSp = MakeRoundRect(15, new Color("fff0f6"), new Color("f0cede"), 1);
        var grooveSp = MakeRoundRect(12, new Color("f5e5ee"), new Color("eed0df"), 1);
        var trackSp = MakeRoundRect(12, new Color("ed9cbe"), new Color("ed9cbe"), 0);
        var dispSp = MakeRoundRect(18, new Color("fffafd"), new Color("edb8cf"), 2);
        Color text = new Color("79475f");
        float rowY = SetRowStartY;
        _setSnd1RT = BuildSoundRow(panel, "音樂", rowY, SetRowW, SetRowH, labelSp, grooveSp, trackSp, text,
            out _setBtn1Txt, out _settBgmSlider);
        rowY -= SetRowGap;
        _setSnd2RT = BuildSoundRow(panel, "音效", rowY, SetRowW, SetRowH, labelSp, grooveSp, trackSp, text,
            out _setBtn2Txt, out _settSeSlider);
        rowY -= SetRowGap;
        _setSnd3RT = BuildSoundRow(panel, "語音", rowY, SetRowW, SetRowH, labelSp, grooveSp, trackSp, text,
            out _setBtn3Txt, out _settVoiceSlider);
        rowY -= SetRowGap;
        _setLangBtnRT = BuildCycleRow(panel, "解析度", rowY, SetRowW, SetRowH, SettingsManager.ResolutionLabels, 4, labelSp, dispSp, dispSp, text,
            out _setLangBtnTxt, out _settResIdx, out _settResOpts);
        rowY -= SetRowGap;
        _setLangBtn2RT = BuildCycleRow(panel, "語言", rowY, SetRowW, SetRowH, SettingsManager.Languages, 0, labelSp, dispSp, dispSp, text,
            out _setLangBtn2Txt, out _settLangIdx, out _settLangOpts);
        rowY -= SetRowGap;
        _setLangBtn3RT = BuildCycleRow(panel, "文字速度", rowY, SetRowW, SetRowH, SettingsManager.TextSpeeds, 1, labelSp, dispSp, dispSp, text,
            out _setLangBtn3Txt, out _settSpdIdx, out _settSpdOpts);
        rowY -= SetRowGap;
        BuildCycleRow(panel, "顯示模式", rowY, SetRowW, SetRowH, new[] { "全螢幕", "視窗模式" }, 0, labelSp, dispSp, dispSp, text,
            out _, out var modeIndex, out _);
        rowY -= SetRowGap;
        // 全年齡模式：打勾 = 全年齡（關閉 R18）；套用設定時才生效並存檔
        var ageRow = new Control { Name = "Row_AllAges", MouseFilter = MouseFilterEnum.Pass };
        panel.AddChild(ageRow);
        ApplyPointAnchor(ageRow, V2(0.5f, 0.5f), V2(0.5f, 0.5f), V2(0, rowY), V2(SetRowW, SetRowH));
        var ageCaption = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        ageRow.AddChild(ageCaption); ageCaption.AddThemeStyleboxOverride("panel", labelSp);
        ApplyStretchAnchor(ageCaption, V2(0, 0), V2(0, 1), V2(0, 0), V2(105, 0));
        Txt(ageCaption, "T", "全年齡", 20, text, UAlign.MiddleCenter, V2(0, 0), V2(1, 1), V2(0, 0), V2(0, 0));
        var allAgesCheck = new CheckBox { Name = "AllAgesCheck", Text = "使用全年齡模式（取消勾選為 R18 版）", FocusMode = FocusModeEnum.None };
        allAgesCheck.AddThemeFontSizeOverride("font_size", 20);
        // 各種狀態（含勾選+滑過）字色都固定，避免勾選後字變白
        foreach (var cn in new[] { "font_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color", "font_disabled_color" })
            allAgesCheck.AddThemeColorOverride(cn, text);
        allAgesCheck.AddThemeColorOverride("font_hover_color", new Color("b84e7e"));
        ageRow.AddChild(allAgesCheck);
        ApplyStretchAnchor(allAgesCheck, V2(0, 0), V2(1, 1), V2(125, 0), V2(0, 0));
        allAgesCheck.ButtonPressed = !(PlayerDataManager.Instance?.R18ContentEnabled ?? false);
        // 打勾/取消立即生效並存檔（不必等「套用設定」，避免勾了直接按返回而沒生效）
        allAgesCheck.Toggled += on =>
        {
            PlayerDataManager.Instance?.SetR18(!on);
            SettingsManager.Instance?.Save();
        };
        _settingsDropdowns["解析度"].ItemSelected += selected => {
            modeIndex[0] = 1;
            _settingsDropdowns["顯示模式"].Select(1);
            SettingsManager.Instance?.SetResolution((int)selected);
            SettingsManager.Instance?.SetFullscreen(false);
            SettingsManager.Instance?.Save();
        };
        _settingsDropdowns["顯示模式"].ItemSelected += selected => {
            modeIndex[0] = (int)selected;
            SettingsManager.Instance?.SetFullscreen((int)selected == 0);
            SettingsManager.Instance?.Save();
        };
        Txt(panel, "SettingsHint", "選擇解析度會切換至視窗模式；全螢幕使用螢幕原生大小。", 16, new Color("a7778e"), UAlign.MiddleCenter,
            V2(0.5f, 0.5f), V2(0.5f, 0.5f), V2(0, -272), V2(660, 32));
        var apply = Btn(panel, "SetBtn7", "套用設定", Colors.White, V2(0.5f, 0.5f), SetBtnApplyPos, SetBtnApplySize.X, SetBtnApplySize.Y);
        var cancel = Btn(panel, "SetBtn8", "返回", Colors.White, V2(0.5f, 0.5f), SetBtnExitPos, SetBtnExitSize.X, SetBtnExitSize.Y);
        _setBtn7RT = apply; _setBtn8RT = cancel;
        _setBtn7Txt = BtnLabel(apply); _setBtn8Txt = BtnLabel(cancel);
        _setBtn7Txt.AddThemeFontSizeOverride("font_size", 26);
        _setBtn8Txt.AddThemeFontSizeOverride("font_size", 26);
        float originalMusic = 0.8f, originalSfx = 1f, originalVoice = 1f;
        panel.VisibilityChanged += () => {
            if (!panel.Visible || SettingsManager.Instance == null) return;
            var settings = SettingsManager.Instance;
            originalMusic = settings.MusicVolume; originalSfx = settings.SFXVolume; originalVoice = settings.VoiceVolume;
            _settBgmSlider.Value = originalMusic; _settSeSlider.Value = originalSfx; _settVoiceSlider.Value = originalVoice;
            _settResIdx[0] = settings.ResolutionIndex; _settingsDropdowns["解析度"].Select(_settResIdx[0]);
            _settLangIdx[0] = settings.LanguageIndex; _settingsDropdowns["語言"].Select(_settLangIdx[0]);
            _settSpdIdx[0] = settings.TextSpeed; _settingsDropdowns["文字速度"].Select(_settSpdIdx[0]);
            modeIndex[0] = settings.IsFullscreen ? 0 : 1; _settingsDropdowns["顯示模式"].Select(modeIndex[0]);
            allAgesCheck.ButtonPressed = !(PlayerDataManager.Instance?.R18ContentEnabled ?? false);
        };
        _settBgmSlider.ValueChanged += v => SettingsManager.Instance?.SetMusicVolume((float)v);
        _settSeSlider.ValueChanged += v => SettingsManager.Instance?.SetSFXVolume((float)v);
        _settVoiceSlider.ValueChanged += v => SettingsManager.Instance?.SetVoiceVolume((float)v);
        apply.Pressed += () => {
            var settings = SettingsManager.Instance;
            settings?.SetLanguage(_settLangIdx[0]);
            settings?.SetTextSpeed(_settSpdIdx[0]);
            if (modeIndex[0] == 0)
            {
                settings?.SetResolution(_settResIdx[0]);
                settings?.SetFullscreen(true);
            }
            else
            {
                settings?.SetFullscreen(false);
                settings?.SetResolution(_settResIdx[0]);
            }
            PlayerDataManager.Instance?.SetR18(!allAgesCheck.ButtonPressed);
            settings?.Save();
            SetActive(panel, false);
            SetActive(_mainPanel, true);
        };
        cancel.Pressed += () => {
            SettingsManager.Instance?.SetMusicVolume(originalMusic);
            SettingsManager.Instance?.SetSFXVolume(originalSfx);
            SettingsManager.Instance?.SetVoiceVolume(originalVoice);
            SetActive(panel, false); SetActive(_mainPanel, true);
        };
        return panel;
    }

    // 程式繪製：音量滑桿列（模仿原圖：標籤 + 喇叭 + 凹槽滑桿）
    // 子元件用 anchor 定位，容器大小變時自動伸展
    Control BuildSoundRow(Node parent, string label, float yPos,
        float totalW, float h, StyleBoxFlat labelSp, StyleBoxFlat grooveSp, StyleBoxFlat trackSp,
        Color txtCol, out Label valTxt, out HSlider slider)
    {
        float labelW  = 105f;
        float iconW   = 40f;
        float padLeft = labelW + iconW + 8f;

        // 整列容器
        var rowGO = new Control { Name = "Row_" + label, MouseFilter = MouseFilterEnum.Pass };
        parent.AddChild(rowGO);
        ApplyPointAnchor(rowGO, V2(0.5f, 0.5f), V2(0.5f, 0.5f), V2(0, yPos), V2(totalW, h));

        // 標籤（錨定左側，固定寬度）
        var lblGO = new Panel { Name = "Lbl", MouseFilter = MouseFilterEnum.Ignore };
        lblGO.AddThemeStyleboxOverride("panel", labelSp);
        rowGO.AddChild(lblGO);
        ApplyStretchAnchor(lblGO, V2(0, 0), V2(0, 1), V2(0, 0), V2(labelW, 0));
        var lblTxtGO = Txt(lblGO, "T", label, 22, txtCol, UAlign.MiddleCenter,
            V2(0, 0), V2(1, 1), V2(0, 0), V2(0, 0));
        lblTxtGO.AddThemeConstantOverride("outline_size", 0);

        // 喇叭圖示（錨定左側，在標籤右邊）
        var iconTxt = new Label
        {
            Name                = "Icon",
            Text                = "♪",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            MouseFilter         = MouseFilterEnum.Ignore,
        };
        iconTxt.AddThemeFontSizeOverride("font_size", 22);
        iconTxt.AddThemeColorOverride("font_color", txtCol);
        rowGO.AddChild(iconTxt);
        ApplyStretchAnchor(iconTxt, V2(0, 0), V2(0, 1),
            V2(labelW + 2f, 0), V2(labelW + 2f + iconW, 0));

        // 滑桿（錨定左右伸展，左邊留出標籤+圖示空間）
        slider = new HSlider
        {
            Name     = "Sl",
            MinValue = 0,
            MaxValue = 1,
            Step     = 0.01,
            Value    = 0.8f,
        };
        rowGO.AddChild(slider);
        ApplyStretchAnchor(slider, V2(0, 0), V2(1, 1), V2(padLeft, 3f), V2(0, -3f));

        // 凹槽背景 + 填充軌道
        // （Unity 是 Groove / FillArea / Fill 三個 Image 節點，
        //   Godot 的 HSlider 內建這兩層，改用 StyleBox 覆寫即可）
        slider.AddThemeStyleboxOverride("slider", grooveSp);
        slider.AddThemeStyleboxOverride("grabber_area", trackSp);
        slider.AddThemeStyleboxOverride("grabber_area_highlight", trackSp);

        var thumb = Godot.Image.CreateEmpty(24, 24, false, Godot.Image.Format.Rgba8);
        thumb.Fill(Colors.Transparent);
        for (int x = 0; x < 24; x++)
            for (int y = 0; y < 24; y++)
            {
                float distance = new Vector2(x - 11.5f, y - 11.5f).Length();
                if (distance <= 10f) thumb.SetPixel(x, y, distance > 7.5f ? new Color("fffafd") : new Color("e986b1"));
            }
        var knob = ImageTexture.CreateFromImage(thumb);
        slider.AddThemeIconOverride("grabber", knob);
        slider.AddThemeIconOverride("grabber_highlight", knob);
        slider.AddThemeIconOverride("grabber_disabled", knob);
        valTxt = null;
        return rowGO;
    }

    // 程式繪製：循環選擇列（模仿原圖：標籤 + 漸層顯示條 + ▼箭頭）
    // 子元件用 anchor 定位，容器大小變時自動伸展
    Control BuildCycleRow(Node parent, string label, float yPos,
        float totalW, float h, string[] options, int defIdx,
        StyleBoxFlat labelSp, StyleBoxFlat dispSp, StyleBoxFlat arrowSp,
        Color txtCol, out Label displayTxt, out int[] idxRef, out string[] optsRef)
    {
        optsRef = options; idxRef = new[] { defIdx };
        var row = new Control { Name = "Row_" + label, MouseFilter = MouseFilterEnum.Pass };
        parent.AddChild(row);
        ApplyPointAnchor(row, V2(0.5f, 0.5f), V2(0.5f, 0.5f), V2(0, yPos), V2(totalW, h));
        var caption = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        row.AddChild(caption); caption.AddThemeStyleboxOverride("panel", labelSp);
        ApplyStretchAnchor(caption, V2(0, 0), V2(0, 1), V2(0, 0), V2(105, 0));
        Txt(caption, "T", label, 20, txtCol, UAlign.MiddleCenter, V2(0, 0), V2(1, 1), V2(0, 0), V2(0, 0));
        var selector = new OptionButton { Name = "Select", FitToLongestItem = false };
        row.AddChild(selector);
        ApplyStretchAnchor(selector, V2(0, 0), V2(1, 1), V2(153, 0), V2(0, 0));
        foreach (string option in options) selector.AddItem(option);
        selector.Select(defIdx);
        if (label == "解析度")
        {
            selector.TooltipText = "切換視窗解析度（全螢幕時將以原生解析度等比縮放呈現）。";
        }
        UIStyle.ApplyButtonStyle(selector);
        selector.AddThemeFontSizeOverride("font_size", 20);
        selector.AddThemeConstantOverride("modulate_arrow", 1);
        var popup = selector.GetPopup();
        var popupStyle = MakeRoundRect(18, new Color("fffafd"), new Color("edb8cf"), 2);
        popupStyle.ContentMarginLeft = popupStyle.ContentMarginRight = 14;
        popupStyle.ContentMarginTop = popupStyle.ContentMarginBottom = 12;
        popupStyle.ShadowColor = new Color(0.34f, 0.12f, 0.24f, 0.18f); popupStyle.ShadowSize = 8;
        popup.AddThemeStyleboxOverride("panel", popupStyle);
        popup.AddThemeStyleboxOverride("hover", MakeRoundRect(10, new Color("fce6f0"), Colors.Transparent, 0));
        popup.AddThemeColorOverride("font_color", txtCol);
        popup.AddThemeColorOverride("font_hover_color", new Color("b84e7e"));
        popup.AddThemeFontSizeOverride("font_size", 21);
        popup.AddThemeConstantOverride("v_separation", 14);
        _settingsDropdowns[label] = selector;
        displayTxt = new Label { Text = options[defIdx], Visible = false };
        selector.AddChild(displayTxt);
        var index = idxRef; var legacyLabel = displayTxt;
        selector.ItemSelected += selected => { index[0] = (int)selected; legacyLabel.Text = options[(int)selected]; };
        return row;
    }
}
