using Godot;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>場景共用閱讀控制。選擇點明確 Stop，歷史視窗阻擋推進，計時綁定目前句子。</summary>
public partial class NovelReader : Node
{
    public enum Playback { Manual, Auto, Fast }
    // 自動播放倍速（1~4），整個遊戲共用
    static int _autoSpeed = 1;
    /// <summary>自動播放時的倍速（打字速度用）；非自動模式為 1。</summary>
    public static float AutoTypeMul => _sessionMode == Playback.Auto ? _autoSpeed : 1f;
    Button _historyButton, _speedButton;
    CanvasLayer _skipLayer;
    Label _skipLabel;
    static readonly List<(string Speaker, string Text)> History = new();
    static Playback _sessionMode;
    public static void ResetPlayback() => _sessionMode = Playback.Manual;
    public static void ClearHistory() { History.Clear(); ResetPlayback(); }
    // 回放使用獨立歷史與播放模式，結束後還原主遊戲的閱讀狀態。
    public static Action BeginIsolatedReading()
    {
        var previousHistory = History.ToArray();
        var previousMode = _sessionMode;
        ClearHistory();
        return () => {
            History.Clear();
            History.AddRange(previousHistory);
            _sessionMode = previousMode;
        };
    }
    public Playback Mode { get; private set; }
    /// <summary>
    /// 是否有彈出視窗擋在對話上（歷史紀錄、選單、設定）。
    /// 各場景都用這個判斷「現在不要推進對話」，所以選單/設定打開時也要算進來。
    /// </summary>
    public bool HistoryOpen => _history?.Visible == true || MenuOpen;

    /// <summary>選單或設定視窗是否開著</summary>
    public bool MenuOpen => _menuPopup?.Visible == true || _settingsPopup?.Visible == true || _quitPopup?.Visible == true;
    public bool Paused => HistoryOpen || UIHidden || (_blocked?.Invoke() ?? false);
    /// <summary>隱藏對話框模式（右鍵或工具列按鈕切換，點任意處恢復），方便欣賞 CG。</summary>
    public bool UIHidden { get; private set; }
    float _hidBoxA = 1f, _hidBadgeA = 1f;
    /// <summary>劇情進行中：即使對話框暫時隱藏（立繪移動、淡入淡出…），仍保留工具列與選單鈕。</summary>
    public bool KeepChrome { get; set; }
    /// <summary>目前是否處於快進狀態（供各場景動畫加速使用）。</summary>
    public bool FastNow => IsFast;
    /// <summary>跳過／快進時，劇情等待與動畫的加速倍率（原本各場景 6～12 倍，統一成 20 倍）。</summary>
    public const float SkipSpeedMult = 20f;
    /// <summary>true＝即使沒有對話框也一直顯示右上角「☰ 選單」鈕（調教房用）。</summary>
    public bool AlwaysShowMenu;
    public int LineVersion { get; private set; }
    Control _box, _content, _oldBar;
    Func<bool> _typing, _waiting, _locked, _blocked;
    Action _reveal, _advance;
    HBoxContainer _bar;
    Button _autoButton, _fastButton;
    CanvasLayer _layer, _chrome;
    ColorRect _history;
    VBoxContainer _entries;
    ScrollContainer _scroll;
    Control _previousFocus;
    float _elapsed;
    int _length;
    bool _wasVisible;
    bool _boxShownOnce, _wasChromeOn;   // 工具列/選單要等對話框第一次出現才一起淡入
    Panel _paper;
    bool _holdArmed;
    float _holdSeconds;
    bool HoldFast => _holdArmed && _holdSeconds >= 0.45f;
    bool IsFast => Mode == Playback.Fast || HoldFast;
    Control _speakerBadge;
    // 右上角選單（保存／讀檔／歷史紀錄／設定）
    Button _menuButton;
    Control _menuPopup;
    Control _menuCard;
    Control _settingsPopup;
    Control _quitPopup;
    Label _speakerName;
    Panel _face;
    HeartDecoration _heartIcon;
    FishDecoration _fishIcon;
    void CancelHold() { _holdArmed = false; _holdSeconds = 0; }

    public static NovelReader Install(Node owner, Control box, Control content, Control oldBar,
        Func<bool> typing, Func<bool> waiting, Func<bool> locked, Action reveal, Action advance, Func<bool> blocked = null)
    {
        var reader = new NovelReader { _box = box, _content = content, _oldBar = oldBar,
            _typing = typing, _waiting = waiting, _locked = locked, _reveal = reveal, _advance = advance, _blocked = blocked };
        owner.AddChild(reader);
        reader.Mode = _sessionMode;
        reader.Build();
        reader.RefreshButtons();
        return reader;
    }
    public void BeginLine(string speaker, string text)
    {
        // 這行語音已在台詞開始時提前播放過，就不重播（避免從頭再來）
        if (_voicePre && _voicePreText == text) _voicePre = false;
        else { _voicePre = false; PlayDailyVoice(speaker, text); }
        LineVersion++;
        _elapsed = 0;
        _length = text?.Length ?? 0;
        if (!string.IsNullOrWhiteSpace(text))
        {
            History.Add((speaker ?? "", text));
            if (History.Count > 500) History.RemoveAt(0);
        }
        bool isMale   = IsMaleSpeaker(speaker);
        bool isFather = !isMale && IsFatherSpeaker(speaker);
        var contentColor = isMale ? new Color("2a4a7a") : isFather ? new Color("1a1a1a") : new Color("654657");
        if (_content is Label label) label.AddThemeColorOverride("font_color", contentColor);
        if (_content is RichTextLabel rich) rich.AddThemeColorOverride("default_color", contentColor);
        // 控制名字欄顯示（有說話者才顯示）
        if (_speakerBadge != null)
            _speakerBadge.Visible = !string.IsNullOrWhiteSpace(speaker);
        ApplySpeakerStyle(speaker);
    }
    public void Stop()
    {
        StopDailyVoice();
        CancelHold();
        Mode = _sessionMode = Playback.Manual;
        _elapsed = 0;
        RefreshButtons();
    }
    public void ToggleFast() => SetMode(Playback.Fast);
    public void ToggleAuto() => SetMode(Playback.Auto);
    void SetMode(Playback mode)
    {
        CancelHold();
        Mode = _sessionMode = Mode == mode ? Playback.Manual : mode;
        _elapsed = 0;
        RefreshButtons();
    }
    void RefreshButtons()
    {
        if (_autoButton == null) return;
        _autoButton.Text = Mode == Playback.Auto ? "自動 · 開" : "自動播放";
        _fastButton.Text = Mode == Playback.Fast ? "跳過 · 停止" : "跳過";
        if (_speedButton != null) _speedButton.Text = _autoSpeed + "X";
        UpdateSkipOverlay();
        _autoButton.ButtonPressed = Mode == Playback.Auto;
        _fastButton.ButtonPressed = Mode == Playback.Fast;
    }
    void Build()
    {
        _box.MouseFilter = Control.MouseFilterEnum.Ignore;
        if (_box is ColorRect rect) rect.Color = Colors.Transparent;
        if (_box is Panel panel) panel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        _paper = new Panel { Name = "NovelPaper", MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false }; // 隱藏直到 ApplySpeakerStyle 設定好正確樣式後才顯示
        _paper.AddThemeStyleboxOverride("panel", Paper());
        _box.AddChild(_paper);
        _paper.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _paper.OffsetLeft = 18; _paper.OffsetRight = -18;
        _box.MoveChild(_paper, 0);
        var ornament = new Label { Text = "✦  ·  ♡  ·  ✦", MouseFilter = Control.MouseFilterEnum.Ignore };
        ornament.AddThemeFontSizeOverride("font_size", 16);
        ornament.AddThemeColorOverride("font_color", new Color("cd87a5"));
        _paper.AddChild(ornament);
        ornament.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomRight);
        ornament.Position = new Vector2(-160, -30);
        foreach (Node node in _box.GetChildren())
            if (node is Label hint && hint != _content && hint.Text.Contains("點擊"))
            {
                hint.AddThemeColorOverride("font_color", new Color("a8758e"));
                hint.Text = "▼ 點擊 / 長按快進";
            }
        // 自動播放/快進工具列放在獨立的最上層，對話框隱藏時（立繪移動等）也能繼續使用快進。
        _chrome = new CanvasLayer { Layer = 101 };
        AddChild(_chrome);
        _bar = new HBoxContainer { Name = "NovelToolbar", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _chrome.AddChild(_bar);
        _bar.Size = new Vector2(124 + (156 + 72 + 1) + 120 + 24, 46);   // 歷史記錄 / [自動播放|倍速] / 跳過
        _bar.AddThemeConstantOverride("separation", 12);
        // 選單按鈕改掛在畫面右上角（見 Build() 結尾的 BuildMenuButton）
        _historyButton = MakeButton("歷史記錄", OpenHistory);
        _historyButton.CustomMinimumSize = new Vector2(124, 44);
        _historyButton.TooltipText = "H：歷史記錄";
        // 自動播放 + 倍速：同一個外框，中間一條分隔線
        var autoFrame = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        autoFrame.AddThemeStyleboxOverride("panel", new StyleBoxFlat {
            BgColor = new Color("fff9fd"), BorderColor = new Color("e8b5ce"),
            BorderWidthTop = 2, BorderWidthBottom = 2, BorderWidthLeft = 2, BorderWidthRight = 2,
            CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14, CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14,
            AntiAliasing = true });
        _bar.AddChild(autoFrame);
        var autoRow = new HBoxContainer();
        autoRow.AddThemeConstantOverride("separation", 0);
        autoFrame.AddChild(autoRow);
        Button FlatBtn(string text, Action act)
        {
            var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
            b.SetMeta("preserve_button_style", true);   // 不讓 CuteUIEffects 套上獨立圓角外框
            b.AddThemeFontSizeOverride("font_size", 21);
            b.AddThemeColorOverride("font_color", new Color("80516a"));
            b.AddThemeColorOverride("font_pressed_color", new Color("80516a"));
            b.AddThemeColorOverride("font_hover_color", new Color("a24f78"));
            StyleBoxFlat Fill(Color c) => new StyleBoxFlat { BgColor = c };
            b.AddThemeStyleboxOverride("normal", Fill(Colors.Transparent));
            b.AddThemeStyleboxOverride("hover", Fill(new Color(0.91f, 0.71f, 0.81f, 0.25f)));
            b.AddThemeStyleboxOverride("pressed", Fill(new Color(0.91f, 0.71f, 0.81f, 0.45f)));
            b.AddThemeStyleboxOverride("hover_pressed", Fill(new Color(0.91f, 0.71f, 0.81f, 0.55f)));
            b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            b.Pressed += act;
            return b;
        }
        _autoButton = FlatBtn("自動播放", () => SetMode(Playback.Auto));
        _autoButton.CustomMinimumSize = new Vector2(156, 42);
        autoRow.AddChild(_autoButton);
        var divider = new VSeparator();
        divider.AddThemeStyleboxOverride("separator", new StyleBoxLine { Color = new Color("e8b5ce"), Thickness = 2, Vertical = true });
        autoRow.AddChild(divider);
        _speedButton = FlatBtn("1X", () => { _autoSpeed = _autoSpeed % 4 + 1; RefreshButtons(); });
        _speedButton.CustomMinimumSize = new Vector2(72, 42);
        _speedButton.TooltipText = "自動播放倍速（1X～4X）";
        autoRow.AddChild(_speedButton);
        _autoButton.TooltipText = "A：依文字長度停留後翻頁；選擇點停止";
        _fastButton = MakeButton("跳過", () => SetMode(Playback.Fast));
        _fastButton.CustomMinimumSize = new Vector2(120, 44);
        _fastButton.TooltipText = "F：黑屏快速跳過；到選擇點自動停止，不代選選項";
        _autoButton.ToggleMode = _fastButton.ToggleMode = true;
        var hideButton = MakeButton("隱藏對話框", HideUI);
        hideButton.TooltipText = "右鍵：隱藏對話框，點任意處恢復";
        _layer = new CanvasLayer { Layer = 100 };
        AddChild(_layer);

        // ── 畫面右上角的選單按鈕（保存/讀檔/歷史紀錄/設定 都收在裡面）──
        _menuButton = UIStyle.CreateStyledButton("☰ 選單", 20);
        _menuButton.Name = "NovelMenuButton";
        _menuButton.CustomMinimumSize = new Vector2(132, 46);
        _menuButton.TooltipText = "保存 / 讀檔 / 設定";
        _layer.AddChild(_menuButton);
        _menuButton.AnchorLeft = 1f; _menuButton.AnchorRight = 1f;
        _menuButton.AnchorTop = 0f;  _menuButton.AnchorBottom = 0f;
        _menuButton.OffsetLeft = -156f; _menuButton.OffsetRight = -24f;
        _menuButton.OffsetTop = 20f;    _menuButton.OffsetBottom = 66f;
        _menuButton.Pressed += ToggleMenu;
        _history = new ColorRect { Color = new Color(0.14f, 0.07f, 0.12f, 0.84f), Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _layer.AddChild(_history);
        _history.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var page = new Panel { MouseFilter = Control.MouseFilterEnum.Stop };
        _history.AddChild(page);
        page.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        page.AnchorLeft = 0.15f; page.AnchorRight = 0.85f;
        page.AnchorTop = 0.08f; page.AnchorBottom = 0.92f;
        page.AddThemeStyleboxOverride("panel", Paper());
        var title = new Label { Text = "對話手札   /   本次遊戲的歷史記錄", Position = new Vector2(32, 22) };
        title.AddThemeFontSizeOverride("font_size", 28);
        title.AddThemeColorOverride("font_color", new Color("80516a"));
        page.AddChild(title);
        var close = UIStyle.CreateStyledButton("返回閱讀  ×", 20);
        close.CustomMinimumSize = new Vector2(165, 44);
        page.AddChild(close);
        close.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        close.OffsetLeft = -200; close.OffsetRight = -30; close.OffsetTop = 20; close.OffsetBottom = 64;
        close.Pressed += CloseHistory;
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        page.AddChild(_scroll);
        _scroll.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _scroll.OffsetLeft = 36; _scroll.OffsetRight = -36; _scroll.OffsetTop = 92; _scroll.OffsetBottom = -30;
        _entries = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _entries.AddThemeConstantOverride("separation", 24);
        _scroll.AddChild(_entries);
    }
    static StyleBoxFlat Paper() => new StyleBoxFlat {
        BgColor = new Color("fff9fdf5"), BorderColor = new Color("e8b5ce"),
        BorderWidthTop = 3, BorderWidthBottom = 2, BorderWidthLeft = 2, BorderWidthRight = 2,
        CornerRadiusTopLeft = 28, CornerRadiusTopRight = 28, CornerRadiusBottomLeft = 20, CornerRadiusBottomRight = 20,
        ShadowColor = new Color(0.30f, 0.13f, 0.23f, 0.18f), ShadowSize = 6, AntiAliasing = true
    };
    Button MakeButton(string text, Action action)
    {
        var button = UIStyle.CreateStyledButton(text, 21);
        button.CustomMinimumSize = new Vector2(156, 44);
        _bar.AddChild(button);
        button.Pressed += action;
        return button;
    }
    public override void _Process(double delta)
    {
        UpdateDailyVoice();
        if (!IsInstanceValid(_box)) return;
        if (IsInstanceValid(_speakerBadge)) _speakerBadge.Modulate = new Color(1, 1, 1, _speakerBadge.Modulate.A);
        if (_holdArmed)
        {
            if (!Input.IsMouseButtonPressed(MouseButton.Left) || Paused || !_box.IsVisibleInTree() || OverButton() || !GetWindow().HasFocus()) CancelHold();
            else _holdSeconds += (float)delta;
        }
        // 舊列保留存讀檔功能；只移除已被統一工具列取代的播放按鈕。
        if (IsInstanceValid(_oldBar))
        {
            foreach (Node node in _oldBar.GetChildren())
            {
                if (node is not Control c) continue;
                string name = c.Name.ToString();
                var label = c.GetNodeOrNull<Label>("T");
                string text = label?.Text ?? "";
                // 播放控制與存讀檔全部由統一工具列/選單接手，舊列整排隱藏。
                // 注意：節點保留在場景樹裡不刪除，選單的「保存/讀檔」是直接觸發
                // 這些既有按鈕的 pressed 訊號，沿用各場景自己接好的處理函式。
                c.Visible = false;
            }
            _oldBar.Visible = false;
        }
        bool visible = _box.IsVisibleInTree();
        // 名字框跟著台詞框走：台詞框不見或沒有說話者時一律隱藏。
        // 以前名字框只在 BeginLine 時設定一次，各場景用 SetDialogueUIVisible(false)
        // 關掉台詞框時（autoNext、空白台詞、淡出黑屏、選擇框…）名字框不會跟著關，
        // 就會出現「只剩名字框」；而且場景會先把名字改成下一行的空白說話者，
        // 又會出現「名字框還在但字不見」。這裡每幀同步，所有場景一起修正。
        if (IsInstanceValid(_speakerBadge))
        {
            bool hasName = IsInstanceValid(_speakerName) && !string.IsNullOrWhiteSpace(_speakerName.Text);
            bool badgeOn = visible && hasName;
            if (_speakerBadge.Visible != badgeOn) _speakerBadge.Visible = badgeOn;
        }
        if (UIHidden)
        {
            if (!visible) ShowUI();   // 對話框被劇情關掉時自動解除，避免卡住
            else
            {
                _box.Modulate = new Color(_box.Modulate.R, _box.Modulate.G, _box.Modulate.B, 0f);
                if (IsInstanceValid(_speakerBadge)) _speakerBadge.Modulate = new Color(1, 1, 1, 0f);
                return;
            }
        }
        // 選單按鈕／工具列：對話框顯示，或劇情進行中（KeepChrome，如立繪移動、淡入淡出）時顯示
        if (visible) _boxShownOnce = true;
        bool chromeOn = visible;   // 對話框不見時，工具列與選單鈕一起消失
        if (chromeOn && !_wasChromeOn) FadeInChrome();
        _wasChromeOn = chromeOn;
        if (IsInstanceValid(_menuButton)) _menuButton.Visible = chromeOn || AlwaysShowMenu;
        if (IsInstanceValid(_bar))
        {
            _bar.Visible = chromeOn && !HistoryOpen;
            var r = _box.GetGlobalRect();
            _bar.Position = new Vector2(r.End.X - 26 - _bar.Size.X, r.Position.Y - 57);
        }
        if (!chromeOn && !AlwaysShowMenu && MenuOpen) CloseMenu();
        if (visible && !_wasVisible && IsInstanceValid(_paper) && _paper.Visible)
        {
            // 對話框剛顯示：用淡入動畫讓 _paper 出現（樣式已由 ApplySpeakerStyle 設定）
            _paper.Modulate = new Color(1, 1, 1, 0);
            _paper.PivotOffset = new Vector2(_paper.Size.X * 0.5f, _paper.Size.Y);
            _paper.Scale = new Vector2(1, 0.96f);
            var tween = _paper.CreateTween();
            tween.SetParallel(true);
            tween.TweenProperty(_paper, "modulate:a", 1f, 0.22);
            tween.TweenProperty(_paper, "scale", Vector2.One, 0.28).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        }
        _wasVisible = visible;
        if (!visible || Paused || (!_typing() && !_waiting())) { _elapsed = 0; return; }
        if (IsFast && _typing()) { _reveal(); return; }
        if (_typing() || !_waiting() || _locked()) { _elapsed = 0; return; }
        _elapsed += (float)delta;
        float delay = Mode == Playback.Fast ? 0.03f : HoldFast ? 0.10f : Mathf.Clamp(1.25f + _length * 0.055f, 2f, 9f) / _autoSpeed;
        if (Mode == Playback.Auto && _dailyVoice?.Playing == true) return;
        if ((Mode != Playback.Manual || HoldFast) && _elapsed >= delay) { _elapsed = 0; _advance(); }
    }
    // 跳過模式：不遮黑屏，右上角顯示小提示文字，到選擇點（Stop）或再按一次就解除
    void UpdateSkipOverlay()
    {
        bool on = Mode == Playback.Fast && IsInsideTree();
        if (on && _skipLayer == null)
        {
            _skipLayer = new CanvasLayer { Layer = 99 };   // 低於選單鈕(100)與工具列(101)，高於劇情畫面
            // 透明容器，只攔截點擊用於停止跳過
            var bg = new ColorRect { Color = new Color(0f, 0f, 0f, 0f), MouseFilter = Control.MouseFilterEnum.Stop };
            bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            bg.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true }) Stop(); };
            _skipLayer.AddChild(bg);
            // 右下角小提示
            _skipLabel = new Label { Text = "跳過中… 點擊停止",
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
            _skipLabel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _skipLabel.AddThemeFontSizeOverride("font_size", 18);
            _skipLabel.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.92f, 0.70f));
            _skipLabel.AddThemeConstantOverride("outline_size", 2);
            _skipLabel.AddThemeColorOverride("font_outline_color", new Color(0.2f, 0.1f, 0.15f, 0.8f));
            _skipLabel.OffsetRight = -16f;
            _skipLabel.OffsetBottom = -16f;
            bg.AddChild(_skipLabel);
            AddChild(_skipLayer);
        }
        else if (!on && _skipLayer != null)
        {
            _skipLayer.QueueFree();
            _skipLayer = null;
        }
    }
    void FadeInChrome()
    {
        // 與對話框 _paper 的淡入同步（0.22 秒）
        foreach (CanvasItem item in new CanvasItem[] { _bar, _menuButton })
        {
            if (item == null || !IsInstanceValid(item)) continue;
            item.Modulate = new Color(1, 1, 1, 0);
            item.CreateTween().TweenProperty(item, "modulate:a", 1f, 0.22);
        }
    }
    bool OverButton()
    {
        for (Node n = GetViewport().GuiGetHoveredControl(); n != null; n = n.GetParent())
            if (n is BaseButton || n is ScrollContainer) return true;
        return false;
    }
    public override void _Input(InputEvent ev)
    {
        if (ev is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }) CancelHold();
        if (UIHidden)
        {
            // 隱藏中：任何點擊或按鍵只負責把對話框叫回來，不推進劇情
            if ((ev is InputEventMouseButton { Pressed: true }) || (ev is InputEventKey { Pressed: true, Echo: false }))
            { ShowUI(); GetViewport().SetInputAsHandled(); }
            return;
        }
        if (HistoryOpen)
        {
            if (ev is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
            { if (MenuOpen) CloseMenu(); else CloseHistory(); GetViewport().SetInputAsHandled(); }
            return;
        }
        if (!IsInstanceValid(_box) || !_box.IsVisibleInTree() || Paused) return;
        if (ev is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode == Key.H) OpenHistory();
            else if (key.Keycode == Key.A) SetMode(Playback.Auto);
            else if (key.Keycode == Key.F) SetMode(Playback.Fast);
            else if (key.Keycode == Key.Escape) Stop();
            else if (key.Keycode == Key.Space && GetViewport().GuiGetFocusOwner() is not BaseButton) ManualAdvance();
            else return;
            GetViewport().SetInputAsHandled();
        }
        else if (ev is InputEventMouseButton mouse && mouse.Pressed && !OverButton())
        {
            if (mouse.ButtonIndex == MouseButton.WheelUp) OpenHistory();
            else if (mouse.ButtonIndex == MouseButton.Right) HideUI();
            else if (mouse.ButtonIndex == MouseButton.Left)
            {
                bool canHold = _typing() || _waiting();
                ManualAdvance();
                GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
                _holdArmed = canHold; _holdSeconds = 0;
            }
            else return;
            GetViewport().SetInputAsHandled();
        }
    }
    public void HideUI()
    {
        if (UIHidden || !IsInstanceValid(_box) || !_box.IsVisibleInTree() || HistoryOpen) return;
        CancelHold();
        UIHidden = true;
        _hidBoxA = _box.Modulate.A;
        _hidBadgeA = IsInstanceValid(_speakerBadge) ? _speakerBadge.Modulate.A : 1f;
        _box.Modulate = new Color(_box.Modulate.R, _box.Modulate.G, _box.Modulate.B, 0f);
        if (IsInstanceValid(_speakerBadge)) _speakerBadge.Modulate = new Color(1, 1, 1, 0f);
        if (IsInstanceValid(_chrome)) _chrome.Visible = false;
        if (IsInstanceValid(_layer)) _layer.Visible = false;
        GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
    }
    public void ShowUI()
    {
        if (!UIHidden) return;
        UIHidden = false;
        if (IsInstanceValid(_box)) _box.Modulate = new Color(_box.Modulate.R, _box.Modulate.G, _box.Modulate.B, _hidBoxA);
        if (IsInstanceValid(_speakerBadge)) _speakerBadge.Modulate = new Color(1, 1, 1, _hidBadgeA);
        if (IsInstanceValid(_chrome)) _chrome.Visible = true;
        if (IsInstanceValid(_layer)) _layer.Visible = true;
    }
    void ManualAdvance()
    {
        Stop();
        if (_typing()) _reveal();
        else if (_waiting() && !_locked()) _advance();
    }
    public void OpenHistory()
    {
        if (HistoryOpen) return;
        _previousFocus = GetViewport().GuiGetFocusOwner();
        Stop();
        foreach (Node child in _entries.GetChildren()) { _entries.RemoveChild(child); child.QueueFree(); }
        if (History.Count == 0) AddEntry("對話手札", "還沒有對話紀錄。閱讀過的文字會保留在這裡。");
        foreach (var entry in History) AddEntry(entry.Speaker, entry.Text);
        _history.Show();
        _history.GetChild(0).GetChild<Button>(1).GrabFocus();
        Callable.From(() => { if (IsInstanceValid(_scroll)) _scroll.ScrollVertical = (int)_scroll.GetVScrollBar().MaxValue; }).CallDeferred();
    }
    void AddEntry(string speaker, string text)
    {
        var entry = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        entry.AddThemeConstantOverride("separation", 12);
        _entries.AddChild(entry);
        var name = new Label { Text = string.IsNullOrEmpty(speaker) ? "旁白" : speaker };
        name.AddThemeFontSizeOverride("font_size", 23);
        name.AddThemeColorOverride("font_color", new Color("b0577f"));
        entry.AddChild(name);
        var body = new RichTextLabel { BbcodeEnabled = false, FitContent = true, ScrollActive = false,
            Text = text, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SelectionEnabled = true };
        body.AddThemeFontSizeOverride("normal_font_size", 24);
        body.AddThemeColorOverride("default_color", new Color("654657"));
        body.AddThemeConstantOverride("line_separation", 10);
        entry.AddChild(body);
        var line = new HSeparator { CustomMinimumSize = new Vector2(0, 12) };
        line.AddThemeStyleboxOverride("separator", new StyleBoxLine { Color = new Color("eac9d9"), Thickness = 1 });
        entry.AddChild(line);
    }
    public void SetSpeakerBadge(Control badge, Label name)
    {
        if (_speakerBadge != null || badge == null || name == null) return;
        _speakerBadge = badge; _speakerName = name;
        badge.Modulate = Colors.White; // 恢復不透明（BuildUI 初始設成透明以防閃爍）
        badge.Visible = true; // 確保 nameBar 在接管後立即可見
        if (badge is TextureRect) badge.SelfModulate = Colors.Transparent;
        if (badge is Panel panel) panel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        var face = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
        var style = Paper(); style.BgColor = new Color("fff4fad9"); // 不透明度 85%（建立時的初始值，之後由 ApplySpeakerStyle 依角色覆蓋）
        style.CornerRadiusTopLeft = style.CornerRadiusBottomRight = 20;
        style.CornerRadiusTopRight = style.CornerRadiusBottomLeft = 12;
        style.ShadowSize = 3;
        face.AddThemeStyleboxOverride("panel", style);
        badge.AddChild(face); face.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); badge.MoveChild(face, 0);
        _heartIcon = new HeartDecoration { Position = new Vector2(15, 14), Size = new Vector2(18, 18) };
        _fishIcon  = new FishDecoration  { Position = new Vector2(12, 12), Size = new Vector2(22, 22), Visible = false };
        face.AddChild(_heartIcon);
        face.AddChild(_fishIcon);
        _face = face;
        name.AddThemeColorOverride("font_color", new Color("a04c72"));
        name.AddThemeConstantOverride("outline_size", 0);
        name.AddThemeColorOverride("font_shadow_color", Colors.Transparent);
        // 依當前說話者立即套用正確樣式，避免初始化時閃出預設粉紅框
        ApplySpeakerStyle(name.Text ?? "");
    }
    /// <summary>
    /// 取出說話者的「主體名字」：把括號註記去掉（全形（）與半形()都處理）。
    /// 例如「男主（內心）」「男主（旁白）」→「男主」、「父親（電話音）」→「父親」、
    /// 「莉奈（隔著門大喊）」→「莉奈」。
    ///
    /// 原本是寫 speaker == "男主(內心)" 這種半形字面值比對，但劇本 JSON 裡用的是
    /// 全形括號「男主（內心）」，比對永遠不成立，內心獨白就套到了預設的粉紅色外框。
    /// 改成比對主體名字之後，任何括號註記變體都會自動沿用該角色的外框設計。
    /// </summary>
    static string BaseSpeaker(string speaker)
    {
        if (string.IsNullOrWhiteSpace(speaker)) return "";
        int i = speaker.IndexOfAny(new[] { '（', '(' });
        return (i >= 0 ? speaker.Substring(0, i) : speaker).Trim();
    }

    static bool IsMaleSpeaker(string speaker)
    {
        string b = BaseSpeaker(speaker);
        return b == "男主" || b == "主角";
    }

    static bool IsFatherSpeaker(string speaker) => BaseSpeaker(speaker) == "父親";

    // ═══════════════════════════════════════════════════════════
    //  右上角選單：保存 / 讀檔 / 歷史紀錄 / 設定
    //
    //  保存與讀檔不在這裡重寫邏輯——各場景（MainMenu 有真正的存讀檔面板，
    //  其他場景目前還是 TODO stub）本來就各自建好了按鈕並接好處理函式，
    //  選單只是去找出舊按鈕列裡那顆按鈕、直接發它的 pressed 訊號，
    //  所以不用改動 7 個場景的程式，各場景以後把 stub 補成真功能也會自動生效。
    // ═══════════════════════════════════════════════════════════

    void ToggleMenu()
    {
        if (_menuPopup == null) BuildMenuPopup();
        if (_settingsPopup != null && _settingsPopup.Visible) { _settingsPopup.Visible = false; return; }
        bool show = !_menuPopup.Visible;
        _menuPopup.Visible = show;
        if (show) CallDeferred(nameof(PositionMenuCard));
    }

    /// <summary>把選單卡片貼在「選單按鈕」正上方靠右對齊（各場景對話框高度不同，所以動態算）</summary>
    void PositionMenuCard()
    {
        if (!IsInstanceValid(_menuCard) || !IsInstanceValid(_menuButton)) return;
        // 卡片掛在 CanvasLayer 底下、沒有父容器管版面，要自己收縮成內容大小
        _menuCard.Size = _menuCard.GetCombinedMinimumSize();
        Rect2 btn = _menuButton.GetGlobalRect();
        var vp = _menuPopup.GetViewportRect().Size;
        float x = Mathf.Clamp(btn.End.X - _menuCard.Size.X, 8f, vp.X - _menuCard.Size.X - 8f);
        float y = Mathf.Clamp(btn.End.Y + 10f, 8f, vp.Y - _menuCard.Size.Y - 8f);
        _menuCard.Position = new Vector2(x, y);
    }

    void CloseMenu()
    {
        if (_menuPopup != null) _menuPopup.Visible = false;
        if (_settingsPopup != null) _settingsPopup.Visible = false;
        if (_quitPopup != null) _quitPopup.Visible = false;
    }

    /// <summary>在舊按鈕列裡找出文字/名稱符合的按鈕（含 MainMenu 那種包一層 Hit 子按鈕的寫法）</summary>
    BaseButton FindSceneButton(params string[] keys)
    {
        if (!IsInstanceValid(_oldBar)) return null;
        foreach (Node node in _oldBar.GetChildren())
        {
            if (node is not Control c) continue;
            string name = c.Name.ToString();
            string text = c.GetNodeOrNull<Label>("T")?.Text ?? (c as Button)?.Text ?? "";
            bool hit = false;
            foreach (string k in keys)
                if (name.Contains(k) || text.Contains(k)) { hit = true; break; }
            if (!hit) continue;
            if (c is BaseButton bb) return bb;
            var inner = c.GetNodeOrNull<BaseButton>("Hit");
            if (inner != null) return inner;
        }
        return null;
    }

    void TriggerSceneButton(BaseButton btn, string what)
    {
        CloseMenu();
        if (btn == null || !IsInstanceValid(btn))
        {
            GD.PushWarning($"[NovelReader] 這個場景沒有接上「{what}」功能");
            return;
        }
        btn.EmitSignal(BaseButton.SignalName.Pressed);
    }

    void BuildMenuPopup()
    {
        _menuPopup = new Control { Name = "NovelMenu", MouseFilter = Control.MouseFilterEnum.Stop, Visible = false };
        _layer.AddChild(_menuPopup);
        _menuPopup.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        // 點空白處關閉
        var dim = new Button { Flat = true, Name = "Dim" };
        dim.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        dim.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
        dim.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
        dim.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        _menuPopup.AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        dim.Pressed += CloseMenu;

        // 選單卡片：靠右上，對齊選單按鈕下方
        var card = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        var sty = Paper();
        sty.CornerRadiusTopLeft = sty.CornerRadiusTopRight = 18;
        sty.CornerRadiusBottomLeft = sty.CornerRadiusBottomRight = 18;
        sty.ContentMarginLeft = sty.ContentMarginRight = 14;
        sty.ContentMarginTop = sty.ContentMarginBottom = 14;
        card.AddThemeStyleboxOverride("panel", sty);
        _menuPopup.AddChild(card);
        _menuCard = card;
        // 位置在 ShowMenu() 時依「選單按鈕」的實際位置計算（各場景對話框高度不同）
        card.AnchorLeft = card.AnchorRight = card.AnchorTop = card.AnchorBottom = 0f;
        card.CustomMinimumSize = new Vector2(260, 0);

        var vb = new VBoxContainer();
        vb.AddThemeConstantOverride("separation", 8);
        card.AddChild(vb);

        vb.AddChild(MenuItem("💾  保存", () => TriggerSceneButton(FindSceneButton("Save", "保存"), "保存")));
        vb.AddChild(MenuItem("📂  讀檔", () => TriggerSceneButton(FindSceneButton("Load", "讀檔"), "讀檔")));
        vb.AddChild(MenuItem("⚙  設定", () => { if (_menuPopup != null) _menuPopup.Visible = false; OpenSettings(); }));
        vb.AddChild(MenuItem("🏠  返回主畫面", () =>
        {
            if (_menuPopup != null) _menuPopup.Visible = false;
            // 圖鑑回放中：直接關閉回放，回到原本的圖鑑
            if (GalleryReplaySession.Active != null) { GalleryReplaySession.Active.Close(); return; }
            OpenQuitConfirm();
        }));
    }

    // ═══════════════════════════════════════════════════════════
    //  返回主畫面（直接到開始畫面，不重播 LOGO 與開場動畫）
    // ═══════════════════════════════════════════════════════════
    void OpenQuitConfirm()
    {
        if (_quitPopup == null) BuildQuitPopup();
        _quitPopup.Visible = true;
    }

    void BuildQuitPopup()
    {
        _quitPopup = new Control { Name = "NovelQuitConfirm", MouseFilter = Control.MouseFilterEnum.Stop, Visible = false };
        _layer.AddChild(_quitPopup);
        _quitPopup.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0.14f, 0.07f, 0.12f, 0.72f), MouseFilter = Control.MouseFilterEnum.Stop };
        _quitPopup.AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var center = new CenterContainer();
        _quitPopup.AddChild(center);
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var card = new PanelContainer { CustomMinimumSize = new Vector2(520, 0), MouseFilter = Control.MouseFilterEnum.Stop };
        var sty = Paper();
        sty.ContentMarginLeft = sty.ContentMarginRight = 40;
        sty.ContentMarginTop = sty.ContentMarginBottom = 32;
        card.AddThemeStyleboxOverride("panel", sty);
        center.AddChild(card);
        var vb = new VBoxContainer();
        vb.AddThemeConstantOverride("separation", 18);
        card.AddChild(vb);
        var title = new Label { Text = "返回主畫面？", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 30);
        title.AddThemeColorOverride("font_color", new Color("79475f"));
        vb.AddChild(title);
        var note = new Label { Text = "未存檔的進度將不會保留", HorizontalAlignment = HorizontalAlignment.Center };
        note.AddThemeFontSizeOverride("font_size", 20);
        note.AddThemeColorOverride("font_color", new Color("a8758e"));
        vb.AddChild(note);
        var yes = UIStyle.CreateStyledButton("返回主畫面", 24);
        yes.CustomMinimumSize = new Vector2(300, 54);
        yes.Pressed += ReturnToTitle;
        vb.AddChild(yes);
        var no = UIStyle.CreateStyledButton("繼續遊戲", 24);
        no.CustomMinimumSize = new Vector2(300, 54);
        no.Pressed += () => { if (_quitPopup != null) _quitPopup.Visible = false; };
        vb.AddChild(no);
    }

    bool _leaving;
    void ReturnToTitle()
    {
        if (_leaving) return;
        _leaving = true;
        Stop();
        ClearHistory();
        MainMenuRuntime.SkipIntroOnce = true;
        var tree = GetTree();
        var black = new ColorRect { Color = new Color(0, 0, 0, 0), MouseFilter = Control.MouseFilterEnum.Stop };
        _chrome.AddChild(black);
        black.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var tween = black.CreateTween();
        tween.TweenProperty(black, "color:a", 1f, 0.35);
        tween.TweenCallback(Callable.From(() => { tree.ChangeSceneToFile("res://Scenes/MainMenu.tscn"); }));
    }

    Button MenuItem(string text, Action action)
    {
        var b = UIStyle.CreateStyledButton(text, 20);
        b.CustomMinimumSize = new Vector2(232, 46);
        b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        b.Alignment = HorizontalAlignment.Left;
        b.Pressed += action;
        return b;
    }

    // ═══════════════════════════════════════════════════════════
    //  設定視窗（各場景共用，直接讀寫 SettingsManager）
    // ═══════════════════════════════════════════════════════════
    void OpenSettings()
    {
        if (_settingsPopup == null) BuildSettingsPopup();
        _settingsPopup.Visible = true;
    }

    void BuildSettingsPopup()
    {
        _settingsPopup = new Control { Name = "NovelSettings", MouseFilter = Control.MouseFilterEnum.Stop, Visible = false };
        _layer.AddChild(_settingsPopup);
        _settingsPopup.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var dim = new ColorRect { Color = new Color(0.14f, 0.07f, 0.12f, 0.72f), MouseFilter = Control.MouseFilterEnum.Stop };
        _settingsPopup.AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var center = new CenterContainer();
        _settingsPopup.AddChild(center);
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var card = new PanelContainer { CustomMinimumSize = new Vector2(620, 0), MouseFilter = Control.MouseFilterEnum.Stop };
        var sty = Paper();
        sty.ContentMarginLeft = sty.ContentMarginRight = 40;
        sty.ContentMarginTop = sty.ContentMarginBottom = 32;
        card.AddThemeStyleboxOverride("panel", sty);
        center.AddChild(card);

        var vb = new VBoxContainer();
        vb.AddThemeConstantOverride("separation", 18);
        card.AddChild(vb);

        var title = new Label { Text = "設　定", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 30);
        title.AddThemeColorOverride("font_color", new Color("79475f"));
        vb.AddChild(title);
        vb.AddChild(new HSeparator());

        var sm = SettingsManager.Instance;
        vb.AddChild(VolumeRow("音　樂", sm?.MusicVolume ?? 0.8f, v => SettingsManager.Instance?.SetMusicVolume(v)));
        vb.AddChild(VolumeRow("音　效", sm?.SFXVolume ?? 1f,   v => SettingsManager.Instance?.SetSFXVolume(v)));
        vb.AddChild(VolumeRow("語　音", sm?.VoiceVolume ?? 1f, v => SettingsManager.Instance?.SetVoiceVolume(v)));
        vb.AddChild(VolumeRow("手・舌透明度", sm?.InteractAlpha ?? 0.85f, v => { SettingsManager.Instance?.SetInteractAlpha(v); SettingsManager.Instance?.Save(); }));

        // 文字速度：循環切換
        var speedRow = new HBoxContainer();
        speedRow.AddThemeConstantOverride("separation", 16);
        var speedLbl = new Label { Text = "文字速度", CustomMinimumSize = new Vector2(120, 0), VerticalAlignment = VerticalAlignment.Center };
        speedLbl.AddThemeFontSizeOverride("font_size", 20);
        speedLbl.AddThemeColorOverride("font_color", new Color("79475f"));
        speedRow.AddChild(speedLbl);
        int si = Mathf.Clamp(sm?.TextSpeed ?? 1, 0, SettingsManager.TextSpeeds.Length - 1);
        var speedBtn = UIStyle.CreateStyledButton(SettingsManager.TextSpeeds[si], 20);
        speedBtn.CustomMinimumSize = new Vector2(150, 44);
        speedBtn.Pressed += () =>
        {
            var s2 = SettingsManager.Instance;
            if (s2 == null) return;
            int next = (s2.TextSpeed + 1) % SettingsManager.TextSpeeds.Length;
            s2.SetTextSpeed(next);
            speedBtn.Text = SettingsManager.TextSpeeds[next];
        };
        speedRow.AddChild(speedBtn);
        vb.AddChild(speedRow);

        // 全年齡模式：打勾即時生效並存檔
        var ageCheck = new CheckBox { Text = "使用全年齡模式（取消勾選為 R18 版）", FocusMode = Control.FocusModeEnum.None };
        ageCheck.AddThemeFontSizeOverride("font_size", 20);
        foreach (var cn in new[] { "font_color", "font_pressed_color", "font_hover_color", "font_focus_color", "font_hover_pressed_color", "font_disabled_color" })
            ageCheck.AddThemeColorOverride(cn, new Color("79475f"));
        ageCheck.ButtonPressed = !(PlayerDataManager.Instance?.R18ContentEnabled ?? false);
        ageCheck.Toggled += on =>
        {
            PlayerDataManager.Instance?.SetR18(!on);
            SettingsManager.Instance?.Save();
        };
        vb.AddChild(ageCheck);

        vb.AddChild(new HSeparator());
        var close = UIStyle.CreateStyledButton("關　閉", 22);
        close.CustomMinimumSize = new Vector2(200, 50);
        close.Pressed += () => { if (_settingsPopup != null) _settingsPopup.Visible = false; };
        vb.AddChild(close);
    }

    static Control VolumeRow(string label, float value, Action<float> onChanged)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);

        var lbl = new Label { Text = label, CustomMinimumSize = new Vector2(120, 0), VerticalAlignment = VerticalAlignment.Center };
        lbl.AddThemeFontSizeOverride("font_size", 20);
        lbl.AddThemeColorOverride("font_color", new Color("79475f"));
        row.AddChild(lbl);

        var slider = new HSlider
        {
            MinValue = 0, MaxValue = 1, Step = 0.01,
            Value = value,
            CustomMinimumSize = new Vector2(300, 32),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        row.AddChild(slider);

        var pct = new Label { Text = Mathf.RoundToInt(value * 100) + "%", CustomMinimumSize = new Vector2(64, 0), VerticalAlignment = VerticalAlignment.Center };
        pct.AddThemeFontSizeOverride("font_size", 18);
        pct.AddThemeColorOverride("font_color", new Color("a04c72"));
        row.AddChild(pct);

        slider.ValueChanged += v =>
        {
            onChanged?.Invoke((float)v);
            pct.Text = Mathf.RoundToInt(v * 100) + "%";
        };
        return row;
    }

    void ApplySpeakerStyle(string speaker)
    {
        bool isNarrator = string.IsNullOrWhiteSpace(speaker);
        bool isMale   = IsMaleSpeaker(speaker);
        bool isFather = !isNarrator && !isMale && IsFatherSpeaker(speaker);

        // ── 更新說話者名牌面板邊框 ──────────────────────────────
        if (_face != null)
        {
            var faceStyle = new StyleBoxFlat
            {
                BgColor             = isMale   ? new Color("f0f5ffd9")   // 不透明度 85%，與底板一致
                                    : isFather ? new Color("f4f4f4d9")
                                    :            new Color("fff4fad9"),
                BorderColor         = isMale   ? new Color("4a80cc")
                                    : isFather ? new Color("222222")
                                    :            new Color("e8b5ce"),
                BorderWidthTop      = 3, BorderWidthBottom = 2,
                BorderWidthLeft     = 2, BorderWidthRight  = 2,
                CornerRadiusTopLeft = 20, CornerRadiusBottomRight = 20,
                CornerRadiusTopRight = 12, CornerRadiusBottomLeft = 12,
                ShadowColor = isMale   ? new Color(0.10f, 0.20f, 0.45f, 0.15f)
                            : isFather ? new Color(0.05f, 0.05f, 0.05f, 0.18f)
                            :            new Color(0.30f, 0.13f, 0.23f, 0.15f),
                ShadowSize  = 3, AntiAliasing = true
            };
            _face.AddThemeStyleboxOverride("panel", faceStyle);
        }

        // ── 更新名字文字顏色 ─────────────────────────────────────
        if (_speakerName != null)
            _speakerName.AddThemeColorOverride("font_color",
                isMale   ? new Color("1a4080")
                : isFather ? new Color("111111")
                :             new Color("a04c72"));

        // ── 切換圖示（父親不顯示愛心）────────────────────────────
        if (_heartIcon != null) _heartIcon.Visible = !isMale && !isFather;
        if (_fishIcon  != null) _fishIcon.Visible  =  isMale;

        // ── 更新主對話框大框邊框 ──────────────────────────────────
        if (IsInstanceValid(_paper))
        {
            var paperStyle = new StyleBoxFlat
            {
                BgColor           = isNarrator ? new Color(1f, 1f, 1f, 0.85f) // 旁白：純白，不透明度 85%，無框
                                              : new Color("fff9fdd9"),          // 其他：不透明度 85%（d9 = 217/255）
                BorderColor       = isNarrator ? Colors.Transparent
                                  : isMale    ? new Color("4a80cc")
                                  : isFather  ? new Color("222222")
                                  :             new Color("e8b5ce"),
                BorderWidthTop    = isNarrator ? 0 : 3,
                BorderWidthBottom = isNarrator ? 0 : 2,
                BorderWidthLeft   = isNarrator ? 0 : 2,
                BorderWidthRight  = isNarrator ? 0 : 2,
                CornerRadiusTopLeft = 28, CornerRadiusTopRight  = 28,
                CornerRadiusBottomLeft = 20, CornerRadiusBottomRight = 20,
                ShadowColor = isNarrator ? Colors.Transparent
                                        : new Color(0.30f, 0.13f, 0.23f, 0.18f),
                ShadowSize  = isNarrator ? 0 : 6, AntiAliasing = true
            };
            _paper.AddThemeStyleboxOverride("panel", paperStyle);
            if (!_paper.Visible)
            {
                // 第一次顯示：設好樣式後才讓 _paper 現身，並做淡入動畫
                _paper.Visible = true;
                _paper.Modulate = new Color(1, 1, 1, 0);
                _paper.PivotOffset = new Vector2(_paper.Size.X * 0.5f, _paper.Size.Y);
                _paper.Scale = new Vector2(1, 0.96f);
                var tween = _paper.CreateTween();
                tween.SetParallel(true);
                tween.TweenProperty(_paper, "modulate:a", 1f, 0.22);
                tween.TweenProperty(_paper, "scale", Vector2.One, 0.28).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
            }
        }
    }
    void CloseHistory()
    {
        _history.Hide();
        _elapsed = 0;
        if (IsInstanceValid(_previousFocus)) _previousFocus.GrabFocus();
    }
    public async Task<bool> WaitScriptDelay(float seconds, CancellationToken ct = default)
    {
        int version = LineVersion;
        float elapsed = 0;
        while (elapsed < seconds || Paused)
        {
            if (ct.IsCancellationRequested || !IsInsideTree() || version != LineVersion || !_waiting()) return false;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (ct.IsCancellationRequested || !IsInstanceValid(this) || !IsInsideTree()) return false;
            if (!Paused) elapsed += (float)GetProcessDeltaTime() * (IsFast ? SkipSpeedMult : 1f);
        }
        return version == LineVersion && !Paused && _waiting();
    }
}
