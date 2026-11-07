using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// ExpressionTester（開發用，Unity 版移植）
///
/// 移植說明：
///   Unity 原版用 OnGUI（IMGUI）手動畫 Rect，Godot 沒有對應的即時模式 GUI，
///   改用真正的 Control 節點樹（PanelContainer/VBoxContainer/Button）搭建，
///   排版交給容器自動處理，不用像 Unity 版那樣手算每個按鈕的 y 座標。
///
///   Unity 版還包含大量射精/禁射區/插入狀態/警戒值等除錯功能（EjacDebugColors、
///   NoShootZoneA/B、ResistProgressNorm、JijiPhasePublic、PlayerDataManager 警戒/高潮值……）。
///   這些都屬於「精液/插入」子系統，Godot 這邊目前只有搬過 TriggerClimax()，
///   其餘方法/欄位在 TrainingRoomRuntime 裡還沒有對應實作，故本次移植先只保留
///   表情測試本體（選表情／待機狀態／目前表情／觸發高潮），其餘功能待該子系統
///   移植完成後再回來補上，不在這裡憑空亂猜介面。
///
/// 快捷鍵：
///   F1     直接開關整個 DEV 面板（跟點右側小 DEV 按鈕效果一樣）
///   Tab    面板開啟時循環切換表情
///   1~8    面板開啟時快速選擇前 8 個表情
/// </summary>
public partial class ExpressionTester : Control
{
    // ════════════════════════════════════════════════════════════
    //  外部依賴（由 TrainingRoomRuntime.Expression.cs 於建立時直接注入）
    // ════════════════════════════════════════════════════════════

    FaceExpressionController _faceController;
    IdleStateController      _idleController;
    TrainingRoomRuntime      _trr;

    public void Configure(FaceExpressionController faceController, IdleStateController idleController, TrainingRoomRuntime trr)
    {
        _faceController = faceController;
        _idleController = idleController;
        _trr = trr;
    }

    // ════════════════════════════════════════════════════════════
    //  表情分類（跟 Unity 版一致：符合任一關鍵字即歸入該類）
    // ════════════════════════════════════════════════════════════

    struct ExprCategory
    {
        public string Name;
        public string[] Keywords;
    }

    static readonly ExprCategory[] _categories = new ExprCategory[]
    {
        new ExprCategory { Name = "一般", Keywords = new[] { "害怕", "驚訝", "忍耐", "害羞", "開心", "舒服", "傲嬌" } },
        new ExprCategory { Name = "插入", Keywords = new[] { "插入" } },
        new ExprCategory { Name = "做愛", Keywords = new[] { "做愛" } },
        new ExprCategory { Name = "高潮", Keywords = new[] { "高潮" } },
    };

    static readonly string[] FallbackExpressionNames = new[]
    {
        "害怕", "有點驚訝", "忍耐", "害羞", "開心", "舒服", "高潮", "傲嬌",
    };

    // ════════════════════════════════════════════════════════════
    //  狀態
    // ════════════════════════════════════════════════════════════

    bool _panelOpen  = false;
    int  _currentIndex = -1;

    Button  _devBtn;
    Control _window;
    Label   _currentExprLabel;
    Label   _statusLabel;
    Label   _debugInfoLabel;
    Label   _semenLabel;
    Button  _squirtBtn;
    Button  _ejacDebugColorsBtn;
    Button  _noShootZoneBtn;
    VBoxContainer _noShootZoneBody;
    Button  _eyeZoneBtn;
    Button  _hideFrontHairBtn;
    bool    _frontHairHidden = false;

    // ── 換嘴巴圖片預覽（DEV）──────────────────────────────────
    // 使用者反應光看檔名很難分辨 Resources/Sprites 裡哪張圖是哪個嘴型，
    // 這裡列出目前資料夾裡所有跟嘴巴/笑容/舌頭有關的圖檔，做成縮圖按鈕清單，
    // 點下去就直接套用到角色臉上目前正顯示的嘴巴圖層（見 TrainingRoomRuntime.
    // PreviewMouthTexture()），方便用肉眼比對、真的找到想要的那張嘴巴圖。
    static readonly string[] MouthSpriteChoices =
    {
        "吐舌笑", "嘴巴", "大笑", "小嘴", "小嘴吐舌",
        "張嘴1", "張嘴2", "張嘴3", "抖動嘴", "笑", "舌頭1", "舌頭2", "舌頭3",
        // 張嘴1＝使用者新畫、指定給 ZZZ 專用的嘴巴圖（見 FaceExpressionController.cs
        // EnsureDefaultParts() 的 mouthOverrides："ZZZ" → "張嘴1"），加進這裡才能在
        // 縮圖清單裡也看得到、選得到。
    };
    Button  _mouthPickerToggleBtn;
    PanelContainer _mouthPickerWindow;
    bool    _mouthPickerOpen = false;

    readonly List<Button> _idleButtons = new();
    readonly Dictionary<string, VBoxContainer> _categoryBodies = new();
    readonly Dictionary<string, bool> _categoryFolded = new();
    VBoxContainer _exprListContainer;

    bool _dragging;
    Vector2 _dragGrabOffset;

    // ════════════════════════════════════════════════════════════
    //  生命週期
    // ════════════════════════════════════════════════════════════

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore; // 面板以外的地方點擊要能穿透到遊戲畫面

        BuildDevButton();
        BuildWindow();
        _window.Visible = false;

        RefreshCurrentExprLabel();
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Process(double delta)
    {
        if (_panelOpen)
        {
            RefreshCurrentExprLabel();
            RefreshStatusLabel();
        }
        // 換嘴巴視窗貼著主 DEV 視窗的右邊，主視窗被拖動時要跟著一起移動。
        if (_mouthPickerOpen && _mouthPickerWindow != null && _window != null)
            _mouthPickerWindow.Position = _window.Position - new Vector2(Mathf.Max(_mouthPickerWindow.Size.X, _mouthPickerWindow.GetCombinedMinimumSize().X) + 10f, 0f);   // 貼在主視窗左邊（右邊是調教室的選單卡）
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;

        if (key.Keycode == Key.F1)
        {
            // 原本 F1 只是「顯示/隱藏那顆小 DEV 按鈕」，面板本身還要再點一次按鈕才會打開——
            // 這個二段式設計讓人誤以為「按 F1 關掉」之後 F1 就沒反應了（其實只是按鈕被藏起來，
            // 面板真的關掉了，要嘛再按一次 F1 讓按鈕出現、要嘛就已經達到「關閉」的目的）。
            // 簡化成 F1 直接開關整個面板，跟點 DEV 按鈕的效果一樣，不需要多一層小按鈕狀態。
            SetPanelOpen(!_panelOpen);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (!_panelOpen) return;

        // 數字鍵 1~8（主鍵盤 + 小鍵盤）快速選表情
        if (key.Keycode >= Key.Key1 && key.Keycode <= Key.Key8)
        {
            ApplyExpression((int)(key.Keycode - Key.Key1));
            GetViewport().SetInputAsHandled();
            return;
        }
        if (key.Keycode >= Key.Kp1 && key.Keycode <= Key.Kp8)
        {
            ApplyExpression((int)(key.Keycode - Key.Kp1));
            GetViewport().SetInputAsHandled();
            return;
        }

        if (key.Keycode == Key.Tab)
        {
            int total = ExprCount;
            if (total > 0) ApplyExpression((_currentIndex + 1) % total);
            GetViewport().SetInputAsHandled();
        }
    }

    // ════════════════════════════════════════════════════════════
    //  表情操作
    // ════════════════════════════════════════════════════════════

    int ExprCount => _faceController?.Expressions?.Length ?? FallbackExpressionNames.Length;

    string GetExprName(int i)
    {
        var exprs = _faceController?.Expressions;
        if (exprs != null && i < exprs.Length) return exprs[i].ExpressionName;
        return i < FallbackExpressionNames.Length ? FallbackExpressionNames[i] : $"E{i}";
    }

    void ApplyExpression(int index)
    {
        _currentIndex = index;
        if (_faceController == null) return;
        var exprs = _faceController.Expressions;
        if (exprs != null && index >= 0 && index < exprs.Length)
            _faceController.SetExpression(exprs[index].ExpressionName);
        else if (index >= 0 && index < FallbackExpressionNames.Length)
            _faceController.SetExpression(FallbackExpressionNames[index]);
        RefreshCurrentExprLabel();
    }

    void RefreshCurrentExprLabel()
    {
        if (_currentExprLabel == null) return;
        string cur = !string.IsNullOrEmpty(_faceController?.CurrentExpression)
            ? _faceController.CurrentExpression : "—";
        _currentExprLabel.Text = $"現在：{cur}";
    }

    /// <summary>對應 Unity 版 OnGUI 裡「插入/處女、警戒/高潮值、穴淫/濕度」那幾行文字，
    /// 以及噴水/精液按鈕的即時顯示（PlayerDataManager 這幾個欄位本來就已經移植過了）。</summary>
    void RefreshStatusLabel()
    {
        var pdm = PlayerDataManager.Instance;
        if (_statusLabel != null)
        {
            if (pdm == null)
            {
                _statusLabel.Text = "（找不到 PlayerDataManager）";
            }
            else
            {
                string insertSt = pdm.TodayInserted ? "已插入" : "未插入";
                string virginSt = pdm.IsVirgin ? "處女" : "非處女";
                string orgType  = pdm.MeimeiHasOrgasmed ? $"（{pdm.LastOrgasmType}）" : "";
                string resistStr = (_trr != null && _trr.IsResisting)
                    ? $"　阻力：{_trr.ResistProgressNorm * 100f:F0}%"
                    : "";
                _statusLabel.Text =
                    $"{insertSt} | {virginSt}\n" +
                    $"小穴：{_trr?.XiaoXueStatus ?? "—"}{resistStr}\n" +
                    $"穴淫：{pdm.CurrentVaginaLewd:F0}/{pdm.MaxVaginaLewd:F0}　濕：{pdm.CurrentVaginaWetness:F0}/{pdm.MaxVaginaWetness:F0}\n" +
                    $"警戒：{pdm.CurrentAlertLevel:F0}/{pdm.MaxAlertLevel:F0}　高潮：{pdm.CurrentMeimeiOrgasm:F0}/{pdm.MaxMeimeiOrgasm:F0}{orgType}";
            }
        }

        if (_squirtBtn != null)
        {
            bool on = _trr != null && _trr.IsSquirting;
            _squirtBtn.Text = on ? "💧 噴水  ■ 停止" : "💧 噴水  ▶ 開始";
        }

        if (_semenLabel != null)
            _semenLabel.Text = _trr != null ? $"精液：{_trr.SemenLevel:F0}/100" : "精液：—";
    }

    /// <summary>更新射精除錯顏色／禁射區／閉眼判定範圍 這三個開關按鈕的文字和禁射區滑桿的顯示。
    /// 這三個開關目前只是把 TrainingRoomRuntime 上對應的 [Export] 欄位值切換掉，
    /// 還沒有任何畫面/碰撞邏輯真的去讀這些欄位（見 TRR 那邊欄位註解），單純先把面板做出來。</summary>
    void RefreshHideFrontHairButton()
    {
        if (_hideFrontHairBtn == null) return;
        _hideFrontHairBtn.Text = _frontHairHidden ? "顯示前髮" : "隱藏前髮（方便看表情）";
    }

    void RefreshMouthPickerToggleButton()
    {
        if (_mouthPickerToggleBtn == null) return;
        _mouthPickerToggleBtn.Text = _mouthPickerOpen ? "關閉換嘴巴圖片清單" : "換嘴巴圖片（縮圖清單）";
    }

    void SetMouthPickerOpen(bool open)
    {
        _mouthPickerOpen = open;
        if (_mouthPickerWindow != null)
        {
            _mouthPickerWindow.Visible = open;
            if (open && _window != null)
                _mouthPickerWindow.Position = _window.Position - new Vector2(Mathf.Max(_mouthPickerWindow.Size.X, _mouthPickerWindow.GetCombinedMinimumSize().X) + 10f, 0f);   // 貼在主視窗左邊（右邊是調教室的選單卡）
        }
        RefreshMouthPickerToggleButton();
    }

    /// <summary>建立「換嘴巴圖片」縮圖清單視窗——跟主 DEV 視窗一樣是獨立的 PanelContainer，
    /// 貼在主視窗右邊（見 _Process() 裡的位置同步）。清單內容是 MouthSpriteChoices 裡每一張
    /// 嘴巴/笑容/舌頭相關貼圖，用 Button.Icon 直接顯示縮圖，點下去呼叫
    /// TrainingRoomRuntime.PreviewMouthTexture() 把角色臉上目前顯示中的嘴巴圖層換成這張，
    /// 讓使用者能用肉眼比對、真的找到自己要的那張嘴巴圖（而不是只能用檔名用猜的）。</summary>
    void BuildMouthPickerWindow()
    {
        _mouthPickerWindow = new PanelContainer
        {
            CustomMinimumSize = new Vector2(150f, 0f),
            MouseFilter = MouseFilterEnum.Stop,
            Visible = false,
        };
        var sty = new StyleBoxFlat { BgColor = new Color("1c1024f0"), BorderColor = new Color("6a3888") };
        sty.SetBorderWidthAll(2);
        sty.SetCornerRadiusAll(10);
        _mouthPickerWindow.AddThemeStyleboxOverride("panel", sty);
        AddChild(_mouthPickerWindow);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(150f, 420f),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _mouthPickerWindow.AddChild(scroll);

        var vb = new VBoxContainer();
        vb.AddThemeConstantOverride("separation", 4);
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 6);
        margin.AddThemeConstantOverride("margin_right", 6);
        margin.AddThemeConstantOverride("margin_top", 6);
        margin.AddThemeConstantOverride("margin_bottom", 6);
        margin.AddChild(vb);
        scroll.AddChild(margin);

        vb.AddChild(MakeSectionLabel("嘴巴圖片"));

        foreach (var name in MouthSpriteChoices)
        {
            var tex = ResourceHelper.LoadTexAny("Sprites/" + name);
            var btn = new Button
            {
                Text = name,
                CustomMinimumSize = new Vector2(0, 34),
                IconAlignment = HorizontalAlignment.Left,
                ExpandIcon = true,
            };
            btn.AddThemeConstantOverride("icon_max_width", 32);
            if (tex != null) btn.Icon = tex;
            StyleFlatButton(btn, "160f28", "3a2858");
            btn.Pressed += () =>
            {
                bool ok = _trr != null && _trr.PreviewMouthTexture(name);
                if (!ok) GD.PushWarning($"[ExpressionTester] 換嘴巴圖片失敗：{name}");
            };
            vb.AddChild(btn);
        }
    }

    void RefreshEjacToggles()
    {
        if (_trr == null) return;
        if (_ejacDebugColorsBtn != null)
            _ejacDebugColorsBtn.Text = _trr.EjacDebugColors ? "射精除錯顏色：開" : "射精除錯顏色：關";
        if (_noShootZoneBtn != null)
            _noShootZoneBtn.Text = _trr.EjacShowNoShootZone ? "▼ 禁射區範圍（收起）" : "▶ 禁射區範圍";
        if (_noShootZoneBody != null)
            _noShootZoneBody.Visible = _trr.EjacShowNoShootZone;
        if (_eyeZoneBtn != null)
            _eyeZoneBtn.Text = _trr.EjacShowEyeZone ? "閉眼判定範圍：開" : "閉眼判定範圍：關";
    }

    /// <summary>做一列「標籤 + 兩個滑桿（X/Y）」，用來調整一個 Vector2 欄位（禁射區的 offset/size）。</summary>
    static Control MakeVectorSliderRow(string labelX, string labelY, Func<Vector2> getter, Action<Vector2> setter, float min, float max)
    {
        var row = new HBoxContainer();

        void AddAxis(string axisLabel, bool isX)
        {
            var lbl = new Label { Text = axisLabel, CustomMinimumSize = new Vector2(16, 0) };
            lbl.AddThemeFontSizeOverride("font_size", 9);
            row.AddChild(lbl);

            var slider = new HSlider
            {
                MinValue = min, MaxValue = max, Step = 1,
                Value = isX ? getter().X : getter().Y,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(40, 16),
            };
            var valLbl = new Label { CustomMinimumSize = new Vector2(30, 0) };
            valLbl.AddThemeFontSizeOverride("font_size", 9);
            valLbl.Text = slider.Value.ToString("F0");
            slider.ValueChanged += v =>
            {
                var cur = getter();
                cur = isX ? new Vector2((float)v, cur.Y) : new Vector2(cur.X, (float)v);
                setter(cur);
                valLbl.Text = v.ToString("F0");
            };
            row.AddChild(slider);
            row.AddChild(valLbl);
        }

        AddAxis(labelX, true);
        AddAxis(labelY, false);
        return row;
    }

    static int GetCategoryIndex(string exprName)
    {
        for (int c = 0; c < _categories.Length; c++)
            foreach (var kw in _categories[c].Keywords)
                if (exprName.Contains(kw)) return c;
        return 0;
    }

    // ════════════════════════════════════════════════════════════
    //  UI 建構
    // ════════════════════════════════════════════════════════════

    void BuildDevButton()
    {
        _devBtn = new Button
        {
            Text = "DEV",
            CustomMinimumSize = new Vector2(50, 28),
        };
        _devBtn.SetAnchorsPreset(LayoutPreset.CenterRight);
        _devBtn.Position = new Vector2(-58, -14);
        StyleFlatButton(_devBtn, "26262a", "555555");
        _devBtn.Pressed += () => SetPanelOpen(true);
        AddChild(_devBtn);
    }

    /// <summary>供外部（例如 F9 開發者工具的「進入房間」按鈕）直接打開面板用，
    /// 對應 Unity 版「進去房間就能立刻看到展開的 DEV 面板」的體驗。</summary>
    public void OpenPanel() => SetPanelOpen(true);

    /// <summary>F1 面板是否打開（調教房用來決定開發用 UI：高潮度／狀態卡／右側選單要不要顯示）。</summary>
    public bool IsPanelOpen => _panelOpen;

    void SetPanelOpen(bool open)
    {
        _panelOpen = open;
        _window.Visible = open;
        _devBtn.Visible = !open;
        if (!open) SetMouthPickerOpen(false); // 主面板關掉時，換嘴巴視窗沒有意義，一起收起來
        if (open)
        {
            RebuildExpressionList();
            RefreshCurrentExprLabel();
            RefreshStatusLabel();
            // 重蓋表情清單後，視窗的 Size 是用「舊內容」算出來的舊尺寸（在 BuildWindow()
            // 第一次呼叫 PositionWindow() 時，清單通常還是空的，見 RebuildExpressionList()
            // 的說明），新按鈕生出來後如果不重新量一次，VBoxContainer 的實際內容會比
            // 視窗當初固定下來的 Size 還高，導致按鈕被擠壓到看不見/點不到。
            // CallDeferred 是因為新加入的子節點要等這一幀版面重新計算完才量得到正確高度。
            CallDeferred(nameof(PositionWindow));
        }
    }

    /// <summary>清掉舊的表情分類按鈕，重新讀一次 _faceController.Expressions 蓋出來。
    /// 每次打開面板都重蓋一次，確保不會再卡在 FaceExpressionController 還沒初始化完成
    /// 時期讀到的空清單（見 BuildWindow() 裡的說明）。</summary>
    void RebuildExpressionList()
    {
        if (_exprListContainer == null) return;
        foreach (var child in _exprListContainer.GetChildren())
        {
            _exprListContainer.RemoveChild(child); // 立刻從樹上移除，避免跟新蓋的按鈕同時並存
            child.QueueFree();
        }
        _categoryBodies.Clear();
        _categoryFolded.Clear();
        BuildExpressionList(_exprListContainer);

        if (_debugInfoLabel != null)
        {
            if (_faceController == null)
                _debugInfoLabel.Text = "[除錯] FaceController 是 null";
            else
                _debugInfoLabel.Text = $"[除錯] ready={_faceController.IsReady} 表情數={_faceController.Expressions?.Length ?? -1}";
        }
    }

    void BuildWindow()
    {
        const float winWidth = 220f;

        _window = new PanelContainer
        {
            CustomMinimumSize = new Vector2(winWidth, 0),
            MouseFilter = MouseFilterEnum.Stop,
        };
        var sty = new StyleBoxFlat { BgColor = new Color("1c1024f0"), BorderColor = new Color("6a3888") };
        sty.SetBorderWidthAll(2);
        sty.SetCornerRadiusAll(10);
        ((PanelContainer)_window).AddThemeStyleboxOverride("panel", sty);
        AddChild(_window);

        var vbOuter = new VBoxContainer();
        vbOuter.AddThemeConstantOverride("separation", 0);
        _window.AddChild(vbOuter);

        vbOuter.AddChild(BuildTitleBar());

        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 8);
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        margin.AddChild(content);
        vbOuter.AddChild(margin);

        // ── 選表情（依分類折疊）───────────────────────────────
        // 這裡不能直接 BuildExpressionList(content)：ExpressionTester._Ready() 執行的當下
        // FaceExpressionController 還沒初始化完成（它的 Expressions 要等一幀之後 InitAsync()
        // 才會真的填好資料，這裡先讀到的 ExprCount 是 0），會蓋出一個完全沒有分類按鈕、
        // 只剩「選表情」標題文字的空面板，導致使用者點了沒反應。改成用一個容器包住，
        // 每次面板打開時（SetPanelOpen(true)）都重新蓋一次，確保拿到的是最新的表情清單。
        content.AddChild(MakeSectionLabel("選表情"));
        _exprListContainer = new VBoxContainer();
        _exprListContainer.AddThemeConstantOverride("separation", 2);
        content.AddChild(_exprListContainer);
        BuildExpressionList(_exprListContainer);

        // 除錯用：直接在面板上顯示 FaceExpressionController 是否初始化完成、
        // 目前讀到幾個表情。如果「選表情」下面又是空的，看這行文字就知道是不是
        // FaceExpressionController 那邊初始化失敗（而不用再去翻 Godot 的 Output 視窗）。
        _debugInfoLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _debugInfoLabel.AddThemeFontSizeOverride("font_size", 10);
        _debugInfoLabel.AddThemeColorOverride("font_color", new Color("888888"));
        content.AddChild(_debugInfoLabel);

        content.AddChild(new HSeparator());

        // ── 隱藏前髮（方便觀察表情變化，前髮常常會擋住眉毛/眼睛/嘴巴的動作）────────
        _hideFrontHairBtn = MakeButton("", () =>
        {
            _frontHairHidden = !_frontHairHidden;
            _trr?.SetLayer("前髮", !_frontHairHidden);
            RefreshHideFrontHairButton();
        });
        content.AddChild(_hideFrontHairBtn);
        RefreshHideFrontHairButton();

        // ── 眉毛形態測試（操作時會自動切成皺眉；這裡可以手動看每一種）──────────────
        content.AddChild(MakeSectionLabel("眉毛形態"));
        var browGrid = new GridContainer { Columns = 3 };
        browGrid.AddThemeConstantOverride("h_separation", 4);
        browGrid.AddThemeConstantOverride("v_separation", 4);
        void AddBrowBtn(string text, FaceExpressionController.BrowForm f)
        {
            var b = MakeButton(text, () => _faceController?.SetBrowForm(f));
            b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            browGrid.AddChild(b);
        }
        AddBrowBtn("1 初始",       FaceExpressionController.BrowForm.Normal);
        AddBrowBtn("2 皺眉(反轉)", FaceExpressionController.BrowForm.Frown);
        AddBrowBtn("3 生氣",       FaceExpressionController.BrowForm.Angry);
        AddBrowBtn("4 煩惱",       FaceExpressionController.BrowForm.Worried);
        AddBrowBtn("5 生氣(反轉)", FaceExpressionController.BrowForm.AngryFlip);
        AddBrowBtn("6 煩惱(反轉)", FaceExpressionController.BrowForm.WorriedFlip);
        content.AddChild(browGrid);

        // ── 氣泡表情測試 ──────────────
        content.AddChild(MakeSectionLabel("氣泡表情"));
        var emoteGrid = new GridContainer { Columns = 4 };
        emoteGrid.AddThemeConstantOverride("h_separation", 4);
        void AddEmoteBtn(string text, EmoteBubble.Kind k)
        {
            var b = MakeButton(text, () => _trr?.ShowEmote(k));
            b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            emoteGrid.AddChild(b);
        }
        AddEmoteBtn("愛心", EmoteBubble.Kind.Heart);
        AddEmoteBtn("毛線", EmoteBubble.Kind.Tangle);
        AddEmoteBtn("！",   EmoteBubble.Kind.Exclaim);
        AddEmoteBtn("？",   EmoteBubble.Kind.Question);
        content.AddChild(emoteGrid);

        // ── 隱藏綠點（熱點綠圈＋文字藏起來，熱點仍然可以點）──────────────
        Button hideDotsBtn = null;
        hideDotsBtn = MakeButton("", () =>
        {
            if (_trr == null) return;
            _trr.SetHotspotDotsHidden(!_trr.HotspotDotsHidden);
            hideDotsBtn.Text = _trr.HotspotDotsHidden ? "顯示綠點" : "隱藏綠點（熱點仍可點）";
        });
        hideDotsBtn.Text = (_trr != null && _trr.HotspotDotsHidden) ? "顯示綠點" : "隱藏綠點（熱點仍可點）";
        content.AddChild(hideDotsBtn);

        // ── 查看裁切範圍（雞雞插入時被裁掉的部分顯示成半透明紅色，並畫出裁切線與裁切圓）──
        Button clipDbgBtn = null;
        clipDbgBtn = MakeButton("", () =>
        {
            if (_trr == null) return;
            _trr.ClipDebug = !_trr.ClipDebug;
            clipDbgBtn.Text = _trr.ClipDebug ? "關閉裁切範圍顯示" : "查看裁切範圍（雞雞）";
        });
        clipDbgBtn.Text = (_trr != null && _trr.ClipDebug) ? "關閉裁切範圍顯示" : "查看裁切範圍（雞雞）";
        content.AddChild(clipDbgBtn);

        // ── 遮擋：雞雞／小穴 方塊覆蓋、12px 馬賽克（兩種只能開一種，再按一次關閉）──
        Button boxBtn = null, mosaicBtn = null;
        void RefreshCensorBtns()
        {
            var m = _trr?.Censor ?? TrainingRoomRuntime.CensorMode.None;
            boxBtn.Text    = m == TrainingRoomRuntime.CensorMode.Box    ? "關閉方塊覆蓋" : "方塊覆蓋（雞雞／小穴／胸）";
            mosaicBtn.Text = m == TrainingRoomRuntime.CensorMode.Mosaic ? "關閉馬賽克"   : "馬賽克 8px（雞雞／小穴）";
        }
        boxBtn = MakeButton("", () =>
        {
            if (_trr == null) return;
            _trr.SetCensor(_trr.Censor == TrainingRoomRuntime.CensorMode.Box ? TrainingRoomRuntime.CensorMode.None : TrainingRoomRuntime.CensorMode.Box);
            RefreshCensorBtns();
        });
        mosaicBtn = MakeButton("", () =>
        {
            if (_trr == null) return;
            _trr.SetCensor(_trr.Censor == TrainingRoomRuntime.CensorMode.Mosaic ? TrainingRoomRuntime.CensorMode.None : TrainingRoomRuntime.CensorMode.Mosaic);
            RefreshCensorBtns();
        });
        RefreshCensorBtns();
        content.AddChild(boxBtn);
        content.AddChild(mosaicBtn);

        // ── 測試用：恢復處女（方便重複測試第一次插入的破處流血）──
        content.AddChild(MakeButton("恢復處女（測試破處流血）", () =>
        {
            var pdm = PlayerDataManager.Instance;
            if (pdm != null) pdm.IsVirgin = true;
        }, "402030", "a05060"));

        // ── 換嘴巴圖片（右側會多開一個縮圖清單視窗）─────────────────
        _mouthPickerToggleBtn = MakeButton("", () => SetMouthPickerOpen(!_mouthPickerOpen));
        content.AddChild(_mouthPickerToggleBtn);

        // ── 人物濾鏡：第一版（夜晚）／第二版（光影）各自獨立開關（邏輯在 TrainingRoomRuntime.Layout.cs）──
        Button nightBtn = null, shadeBtn = null;
        nightBtn = MakeButton(_trr != null ? _trr.NightFilterLabel() : "夜晚濾鏡（無調教房）", () =>
        {
            if (_trr == null) return;
            _trr.ToggleNightFilter();
            nightBtn.Text = _trr.NightFilterLabel();
        });
        shadeBtn = MakeButton(_trr != null ? _trr.ShadeFilterLabel() : "光影濾鏡（無調教房）", () =>
        {
            if (_trr == null) return;
            _trr.ToggleShadeFilter();
            shadeBtn.Text = _trr.ShadeFilterLabel();
        });
        content.AddChild(nightBtn);
        // 第一版強度滑桿（0%～100%）
        var nightRow = new HBoxContainer();
        var nightLbl = new Label { Text = $"夜晚強度 {Mathf.RoundToInt((_trr?.NightFilterStrengthValue ?? 1f) * 100)}%", CustomMinimumSize = new Vector2(120, 0) };
        var nightSlider = new HSlider
        {
            MinValue = 0, MaxValue = 1, Step = 0.05, Value = _trr?.NightFilterStrengthValue ?? 1f,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(120, 24),
        };
        nightSlider.ValueChanged += v =>
        {
            if (_trr == null) return;
            _trr.NightFilterStrengthValue = (float)v;
            nightLbl.Text = $"夜晚強度 {Mathf.RoundToInt((float)v * 100)}%";
        };
        nightRow.AddChild(nightLbl);
        nightRow.AddChild(nightSlider);
        content.AddChild(nightRow);
        content.AddChild(shadeBtn);
        BuildMouthPickerWindow();
        RefreshMouthPickerToggleButton();

        content.AddChild(new HSeparator());

        // ── 待機 1/2/3 ────────────────────────────────────────
        content.AddChild(MakeSectionLabel("待機"));
        for (int i = 1; i <= 3; i++)
        {
            int idleIdx = i;
            var btn = MakeButton($"待機{idleIdx}", () => _idleController?.SetState(idleIdx));
            _idleButtons.Add(btn);
            content.AddChild(btn);
        }

        // ── 以下區塊使用者表示用不到（功能跟右側選單重複），收進隱藏容器不顯示。
        //    不直接刪掉程式，是因為其他刷新函式（目前表情、狀態、射精開關…）還會用到這些元件，
        //    留在隱藏容器裡就不會出現空參考錯誤。之後確定不要再整段刪。
        var hiddenDev = new VBoxContainer { Name = "HiddenDevSection", Visible = false };
        content.AddChild(hiddenDev);
        hiddenDev.AddChild(new HSeparator());

        // ── 目前表情 ──────────────────────────────────────────
        _currentExprLabel = new Label { Text = "現在：—", HorizontalAlignment = HorizontalAlignment.Center };
        _currentExprLabel.AddThemeFontSizeOverride("font_size", 13);
        _currentExprLabel.AddThemeColorOverride("font_color", new Color("aaffaa"));
        hiddenDev.AddChild(_currentExprLabel);

        // ── 插入/處女、警戒/高潮值、穴淫/濕度 狀態顯示 ─────────────────
        // 對應 Unity 版 OnGUI 裡的同一段資訊；PlayerDataManager 這幾個欄位
        // Godot 版本來就已經移植過了，這裡只是把顯示補上去。
        hiddenDev.AddChild(new HSeparator());
        _statusLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _statusLabel.AddThemeFontSizeOverride("font_size", 11);
        _statusLabel.AddThemeColorOverride("font_color", new Color("ffaacc"));
        hiddenDev.AddChild(_statusLabel);

        // ── 高潮 / 噴水 / 精液 ───────────────────────────────────
        hiddenDev.AddChild(new HSeparator());
        hiddenDev.AddChild(MakeButton("高潮", () => _trr?.TriggerClimax(), "5c1420", "c02040"));

        _squirtBtn = MakeButton("", () =>
        {
            if (_trr == null) return;
            if (_trr.IsSquirting) _trr.StopSquirt(); else _trr.StartSquirt();
            RefreshStatusLabel();
        }, "10253a", "2050a0");
        hiddenDev.AddChild(_squirtBtn);

        var semenRow = new HBoxContainer();
        _semenLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _semenLabel.AddThemeFontSizeOverride("font_size", 11);
        _semenLabel.AddThemeColorOverride("font_color", new Color("ffcc88"));
        _semenLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        semenRow.AddChild(_semenLabel);
        var fillBtn = MakeButton("填滿", () => _trr?.SetSemenLevel(100f), "553010", "a06020");
        fillBtn.CustomMinimumSize = new Vector2(50, 26);
        semenRow.AddChild(fillBtn);
        hiddenDev.AddChild(semenRow);

        // ── 射精 / 清理精液 ───────────────────────────────────────
        // 對應 Unity 版「射精」「清理精液」按鈕；背後的粒子/落點碰撞子系統
        // 還沒有移植（見 TrainingRoomRuntime.TriggerEjaculation()/ClearEjacDebris() 的說明），
        // 按下去目前只會印一行除錯訊息，不會有實際畫面效果，先把版面對齊 Unity。
        hiddenDev.AddChild(MakeButton("💦 射精", () => _trr?.TriggerEjaculation(), "6a5a30", "c0a050"));
        hiddenDev.AddChild(MakeButton("清理精液", () => _trr?.ClearEjacDebris(), "5a2020", "c06060"));

        // ── 射精除錯顏色 ─────────────────────────────────────────
        _ejacDebugColorsBtn = MakeButton("", () =>
        {
            if (_trr == null) return;
            _trr.EjacDebugColors = !_trr.EjacDebugColors;
            RefreshEjacToggles();
        }, "202030", "555580");
        hiddenDev.AddChild(_ejacDebugColorsBtn);

        // ── 禁射區範圍（開關 + A/B 座標/大小 滑桿）───────────────────
        _noShootZoneBtn = MakeButton("", () =>
        {
            if (_trr == null) return;
            _trr.EjacShowNoShootZone = !_trr.EjacShowNoShootZone;
            RefreshEjacToggles();
        }, "302020", "805050");
        hiddenDev.AddChild(_noShootZoneBtn);

        _noShootZoneBody = new VBoxContainer { Visible = false };
        _noShootZoneBody.AddThemeConstantOverride("separation", 2);
        _noShootZoneBody.AddChild(MakeSectionLabel("禁A"));
        _noShootZoneBody.AddChild(MakeVectorSliderRow("oX", "oY",
            () => _trr?.NoShootZoneA_Offset ?? Vector2.Zero, v => { if (_trr != null) _trr.NoShootZoneA_Offset = v; }, -300f, 300f));
        _noShootZoneBody.AddChild(MakeVectorSliderRow("sX", "sY",
            () => _trr?.NoShootZoneA_Size ?? Vector2.Zero, v => { if (_trr != null) _trr.NoShootZoneA_Size = v; }, 10f, 600f));
        _noShootZoneBody.AddChild(MakeSectionLabel("禁B"));
        _noShootZoneBody.AddChild(MakeVectorSliderRow("oX", "oY",
            () => _trr?.NoShootZoneB_Offset ?? Vector2.Zero, v => { if (_trr != null) _trr.NoShootZoneB_Offset = v; }, -300f, 300f));
        _noShootZoneBody.AddChild(MakeVectorSliderRow("sX", "sY",
            () => _trr?.NoShootZoneB_Size ?? Vector2.Zero, v => { if (_trr != null) _trr.NoShootZoneB_Size = v; }, 10f, 600f));
        hiddenDev.AddChild(_noShootZoneBody);

        // ── 閉眼判定範圍 ─────────────────────────────────────────
        _eyeZoneBtn = MakeButton("", () =>
        {
            if (_trr == null) return;
            _trr.EjacShowEyeZone = !_trr.EjacShowEyeZone;
            RefreshEjacToggles();
        }, "203040", "5080a0");
        hiddenDev.AddChild(_eyeZoneBtn);

        RefreshEjacToggles();

        // ── 結束今天 ─────────────────────────────────────────────
        hiddenDev.AddChild(new HSeparator());
        hiddenDev.AddChild(MakeButton("結束今天", () => _trr?.EndToday(), "805510", "c09020"));

        content.AddChild(new HSeparator());
        content.AddChild(MakeButton("關閉（F1 可再打開）", () => SetPanelOpen(false)));

        CallDeferred(nameof(PositionWindow));
    }

    void PositionWindow()
    {
        _window.Size = _window.GetCombinedMinimumSize();
        var vpSize = GetViewportRect().Size;
        _window.Position = new Vector2(vpSize.X - _window.Size.X - 280, 80);   // 避開右邊調教室選單卡（寬 230＋邊距 20）
    }

    void BuildExpressionList(VBoxContainer parent)
    {
        var buckets = new Dictionary<string, List<(int idx, string name)>>();
        foreach (var cat in _categories) buckets[cat.Name] = new List<(int, string)>();

        int n = ExprCount;
        for (int i = 0; i < n; i++)
        {
            string name = GetExprName(i);
            int catIdx = GetCategoryIndex(name);
            buckets[_categories[catIdx].Name].Add((i, name));
        }

        foreach (var cat in _categories)
        {
            var list = buckets[cat.Name];
            if (list.Count == 0) continue;

            _categoryFolded[cat.Name] = true;
            var body = new VBoxContainer { Visible = false };
            _categoryBodies[cat.Name] = body;

            Button hdrBtn = null;
            hdrBtn = MakeButton($"▶ {cat.Name} ({list.Count})", () =>
            {
                bool folded = !_categoryFolded[cat.Name];
                _categoryFolded[cat.Name] = folded;
                body.Visible = !folded;
                hdrBtn.Text = (folded ? "▶ " : "▼ ") + $"{cat.Name} ({list.Count})";
            }, "241830", "5a3878");
            parent.AddChild(hdrBtn);

            foreach (var (idx, exprName) in list)
            {
                var btn = MakeButton(exprName, () => ApplyExpression(idx), "160f28", "3a2858");
                body.AddChild(btn);
            }
            parent.AddChild(body);
        }
    }

    Control BuildTitleBar()
    {
        var bar = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, CustomMinimumSize = new Vector2(0, 32) };
        var sty = new StyleBoxFlat
        {
            BgColor = new Color("2a1838"),
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
        };
        sty.ContentMarginLeft = 10;
        sty.ContentMarginRight = 6;
        bar.AddThemeStyleboxOverride("panel", sty);

        var hb = new HBoxContainer();
        bar.AddChild(hb);

        var title = new Label { Text = "表情測試 (DEV)", VerticalAlignment = VerticalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 14);
        title.AddThemeColorOverride("font_color", new Color("ffcc44"));
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        hb.AddChild(title);

        var closeBtn = new Button { Text = "✕", Flat = true, CustomMinimumSize = new Vector2(26, 26) };
        closeBtn.AddThemeColorOverride("font_color", new Color("d0a8e0"));
        closeBtn.Pressed += () => SetPanelOpen(false);
        hb.AddChild(closeBtn);

        bar.GuiInput += OnTitleBarGuiInput;
        return bar;
    }

    void OnTitleBarGuiInput(InputEvent ev)
    {
        if (ev is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
        {
            if (mb.Pressed)
            {
                _dragging = true;
                _dragGrabOffset = GetViewport().GetMousePosition() - _window.Position;
            }
            else
            {
                _dragging = false;
            }
        }
        else if (ev is InputEventMouseMotion && _dragging)
        {
            _window.Position = GetViewport().GetMousePosition() - _dragGrabOffset;
        }
    }

    // ════════════════════════════════════════════════════════════
    //  小工具
    // ════════════════════════════════════════════════════════════

    static Label MakeSectionLabel(string text)
    {
        var lbl = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        lbl.AddThemeFontSizeOverride("font_size", 12);
        lbl.AddThemeColorOverride("font_color", new Color("cccccc"));
        return lbl;
    }

    static Button MakeButton(string text, Action onPressed, string bg = "202024", string border = "555555")
    {
        var btn = new Button { Text = text, CustomMinimumSize = new Vector2(0, 26) };
        StyleFlatButton(btn, bg, border);
        btn.Pressed += onPressed;
        return btn;
    }

    static void StyleFlatButton(Button btn, string bg, string border)
    {
        var normal = new StyleBoxFlat { BgColor = new Color(bg), BorderColor = new Color(border) };
        normal.SetBorderWidthAll(1);
        normal.SetCornerRadiusAll(6);

        var hover = new StyleBoxFlat { BgColor = new Color(bg), BorderColor = new Color(border) };
        hover.BgColor = hover.BgColor.Lightened(0.15f);
        hover.SetBorderWidthAll(1);
        hover.SetCornerRadiusAll(6);

        btn.AddThemeStyleboxOverride("normal", normal);
        btn.AddThemeStyleboxOverride("hover", hover);
        btn.AddThemeStyleboxOverride("pressed", hover);
        btn.AddThemeStyleboxOverride("focus", normal);
        btn.AddThemeColorOverride("font_color", new Color("f0f0f0"));
    }
}
