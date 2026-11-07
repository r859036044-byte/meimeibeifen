using Godot;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// MainMenuRuntime 第 4 段：對話面板建構與序章播放流程
/// （對應 Unity 版 MainMenuRuntime.cs 第 2211~3572 行）
/// </summary>
public partial class MainMenuRuntime : Control
{
    NovelReader _reader;
    float _cuteMenuTime;
    // 建立面板時先套用新版介面，不等待第一句文字。
    void EnsureReader()
    {
        _reader ??= NovelReader.Install(this, _dialogueBox, _contentText, _btnBarGO,
            () => _isTyping, () => _waitingClick, () => _lockClick,
            () => _skipTyping = true, () => _waitingClick = false, () => _saveLoadCtrl != null && _saveLoadCtrl.IsOpen());
        _reader.SetSpeakerBadge(_nameBarGO, _speakerText);
    }

    // 快進時動畫（立繪移動、淡入淡出）同步加速
    float FF(float seconds) => (_reader?.FastNow ?? false) ? seconds / NovelReader.SkipSpeedMult : seconds;

    void BeginReadingLine(string text)
    {
        EnsureReader();
        _reader.BeginLine(_speakerText?.Text, text);
    }

    // ═══════════════════════════════════════════════════════════
    //  本段專用的小型矩形輔助（Unity RectTransform 語意 → Godot Control）
    //  命名加 Dlg 前綴避免與其他段落衝突
    // ═══════════════════════════════════════════════════════════

    /// <summary>1x1 純白貼圖：Unity 的 Image 在 sprite=null 時仍會用 color 畫出色塊，
    /// Godot 的 TextureRect 則不會，故用白色貼圖 + Modulate 模擬</summary>
    private static Texture2D _dlgWhiteTex;
    private static Texture2D DlgWhiteTex()
    {
        if (_dlgWhiteTex != null) return _dlgWhiteTex;
        var img = Godot.Image.CreateEmpty(1, 1, false, Godot.Image.Format.Rgba8);
        img.Fill(Colors.White);
        _dlgWhiteTex = ImageTexture.CreateFromImage(img);
        return _dlgWhiteTex;
    }

    /// <summary>取得點錨點節點的 sizeDelta（中心樞紐）</summary>
    private static Vector2 DlgRectSize(Control c)
        => c == null ? Vector2.Zero : new Vector2(c.OffsetRight - c.OffsetLeft, c.OffsetBottom - c.OffsetTop);

    /// <summary>取得點錨點節點的 anchoredPosition（中心樞紐，回傳 Unity 座標系）</summary>
    private static Vector2 DlgRectPos(Control c)
        => c == null ? Vector2.Zero
                     : new Vector2((c.OffsetLeft + c.OffsetRight) * 0.5f, -(c.OffsetTop + c.OffsetBottom) * 0.5f);

    /// <summary>設定點錨點節點的位置（保持原尺寸）</summary>
    private static void DlgSetPos(Control c, Vector2 unityPos)
    {
        if (c == null || !IsInstanceValid(c)) return;
        SetAnchoredPos(c, unityPos, DlgRectSize(c));
    }

    /// <summary>設定點錨點節點的位置與尺寸</summary>
    private static void DlgSetPosSize(Control c, Vector2 unityPos, Vector2 size)
    {
        if (c == null || !IsInstanceValid(c)) return;
        SetAnchoredPos(c, unityPos, size);
    }

    /// <summary>
    /// 立繪專用：Unity 的 anchor(0.5,0)、pivot(0.5,0)（底部中心）
    /// Godot 對應：AnchorLeft/Right = 0.5、AnchorTop/Bottom = 1（底部）
    /// </summary>
    private static void DlgSetCharRect(Control c, Vector2 unityPos, Vector2 size)
    {
        if (c == null || !IsInstanceValid(c)) return;
        c.AnchorLeft = 0.5f; c.AnchorRight = 0.5f;
        c.AnchorTop  = 1f;   c.AnchorBottom = 1f;
        c.OffsetLeft   = unityPos.X - size.X * 0.5f;
        c.OffsetRight  = unityPos.X + size.X * 0.5f;
        c.OffsetBottom = -unityPos.Y;              // 底部對齊：Y 朝上 → 取負
        c.OffsetTop    = -unityPos.Y - size.Y;
        c.PivotOffset  = new Vector2(size.X * 0.5f, size.Y);  // 樞紐設在底部中心
    }

    /// <summary>取得立繪節點的 sizeDelta</summary>
    private static Vector2 DlgCharSize(Control c)
        => c == null ? Vector2.Zero : new Vector2(c.OffsetRight - c.OffsetLeft, c.OffsetBottom - c.OffsetTop);

    /// <summary>取得立繪節點的 anchoredPosition（Unity 座標系，底部中心樞紐）</summary>
    private static Vector2 DlgCharPos(Control c)
        => c == null ? Vector2.Zero : new Vector2((c.OffsetLeft + c.OffsetRight) * 0.5f, -c.OffsetBottom);

    /// <summary>
    /// 拉伸節點（全螢幕 BG / CG）的平移
    /// Unity 對拉伸 RT 設 anchoredPosition 等同於同時位移 offsetMin/offsetMax
    /// </summary>
    private static void DlgSetStretchOffset(Control c, float unityX, float unityY)
    {
        if (c == null || !IsInstanceValid(c)) return;
        c.OffsetLeft = unityX; c.OffsetRight = unityX;
        c.OffsetTop  = -unityY; c.OffsetBottom = -unityY;
    }

    /// <summary>
    /// 背景位移限制：背景放大到 scale 倍時，最多只能移動 (scale-1)/2 個畫面大小，
    /// 超過就會露出圖片外的黑邊。scale≤1 時完全不能移動。
    /// </summary>
    private static Vector2 ClampBgOffset(Vector2 pos, float scale)
    {
        float mx = Mathf.Max(0f, (scale - 1f) * RefWidth  * 0.5f);
        float my = Mathf.Max(0f, (scale - 1f) * RefHeight * 0.5f);
        return new Vector2(Mathf.Clamp(pos.X, -mx, mx), Mathf.Clamp(pos.Y, -my, my));
    }

    /// <summary>取得拉伸節點目前的位移（Unity 座標系）</summary>
    private static Vector2 DlgGetStretchOffset(Control c)
        => c == null ? Vector2.Zero : new Vector2(c.OffsetLeft, -c.OffsetTop);

    /// <summary>
    /// 讀取並反序列化對話 JSON（只處理檔案讀取，不解讀內容）
    /// 對應 Unity 的 Resources.Load&lt;TextAsset&gt; + JsonUtility.FromJson
    /// </summary>
    private static T LoadJsonData<T>(string relPath) where T : class
    {
        string path = $"res://Resources/{relPath}.json";
        if (!FileAccess.FileExists(path)) return null;

        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null) return null;

        string jsonText = DialogueSystem.PreprocessJson(file.GetAsText());
        try
        {
            // 統一使用 GameJson.Options()（含 FlexibleStringConverter），
            // 否則像 prologue.json 那種把 chX/bgX/cgScale 寫成裸數字的劇本會解析失敗。
            return System.Text.Json.JsonSerializer.Deserialize<T>(jsonText, GameJson.Options());
        }
        catch (Exception e)
        {
            // 這裡原本用 PushWarning，解析失敗只會變成一條不起眼的警告，
            // 劇情直接沒反應卻查不到原因，所以改成明確的錯誤訊息。
            GD.PrintErr($"[Dialogue] JSON 解析失敗：{relPath} - {e.Message}");
            return null;
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  對話面板建構
    // ═══════════════════════════════════════════════════════════

    Control BuildDialoguePanel(Node parent)
    {
        // 全螢幕覆蓋
        var panel = new Control { Name = "DialoguePanel", MouseFilter = MouseFilterEnum.Ignore };
        parent.AddChild(panel);
        ApplyStretchAnchor(panel, Vector2.Zero, Vector2.One, Vector2.Zero, Vector2.Zero);
        var pt = panel;

        // ── 字體 ──────────────────────────────────────────────────
        // Godot 使用專案 Theme 的預設字體即可支援中日韓字元，
        // 若 Resources/Fonts 下有自訂字體則優先套用
        Font cjkFont = ResourceLoader.Exists("res://Resources/Fonts/NotoSansTC-Regular.ttf")
            ? ResourceLoader.Load<Font>("res://Resources/Fonts/NotoSansTC-Regular.ttf")
            : null;

        // ── 1. 場景背景（全螢幕，雙圖層用於交叉淡入）────────────
        _bgImage = new TextureRect
        {
            Name        = "BG",
            Texture     = DlgWhiteTex(),
            ExpandMode  = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        pt.AddChild(_bgImage);
        ApplyStretchAnchor(_bgImage, Vector2.Zero, Vector2.One, Vector2.Zero, Vector2.Zero);
        _bgImage.Modulate = new Color(0.08f, 0.04f, 0.12f);

        _bgImage2 = new TextureRect
        {
            Name        = "BG2",
            Texture     = DlgWhiteTex(),
            ExpandMode  = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,   // raycastTarget = false
        };
        pt.AddChild(_bgImage2);
        ApplyStretchAnchor(_bgImage2, Vector2.Zero, Vector2.One, Vector2.Zero, Vector2.Zero);
        _bgImage2.Modulate = new Color(1f, 1f, 1f, 0f);

        // ── 2. CG 圖層（BG 之上、對話框之下）── 多層 ────────────
        // 全部 CG 層放進同一個容器，呼吸晃動只動這個容器，不影響各層自己的位移/縮放
        _cgBreathRoot = new Control { Name = "CG_BreathRoot", MouseFilter = MouseFilterEnum.Ignore };
        pt.AddChild(_cgBreathRoot);
        ApplyStretchAnchor(_cgBreathRoot, Vector2.Zero, Vector2.One, Vector2.Zero, Vector2.Zero);
        for (int _ci = 0; _ci < DialogueSystem.CG_LAYER_COUNT; _ci++)
        {
            var cgGO = new TextureRect
            {
                Name        = "CG_Layer_" + _ci,
                ExpandMode  = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,  // preserveAspect = true
                MouseFilter = MouseFilterEnum.Ignore,                          // raycastTarget = false
            };
            _cgBreathRoot.AddChild(cgGO);
            ApplyStretchAnchor(cgGO, Vector2.Zero, Vector2.One, Vector2.Zero, Vector2.Zero);
            cgGO.Modulate = new Color(1f, 1f, 1f, 0f);
            cgGO.Visible  = false;
            _cgImages[_ci] = cgGO;
        }

        // ── 2.5 立繪圖層（CG 之上、對話框之下）──────────────────
        // 容器（管理身體+表情的渲染順序）
        var charRoot = new Control { Name = "CharRoot", MouseFilter = MouseFilterEnum.Ignore };
        pt.AddChild(charRoot);
        ApplyStretchAnchor(charRoot, Vector2.Zero, Vector2.One, Vector2.Zero, Vector2.Zero);

        // 身體圖層（先建立 → 渲染在下）
        _charBodyImage = new TextureRect
        {
            Name        = "CharBody",
            ExpandMode  = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        charRoot.AddChild(_charBodyImage);
        DlgSetCharRect(_charBodyImage, Vector2.Zero, new Vector2(650f, 1338f));
        _charBodyImage.Modulate = new Color(1f, 1f, 1f, 0f);

        // Spine 立繪：掛在身體圖底下（跟著它的位置／縮放／淡入淡出），平常隱藏
        _spineChar = new SpineChar { Name = "CharSpine", Visible = false };
        _charBodyImage.AddChild(_spineChar);
        _spineChar.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // 表情圖層（後建立 → 渲染在上，同級兄弟）
        _charFaceImage = new TextureRect
        {
            Name        = "CharFace",
            ExpandMode  = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        charRoot.AddChild(_charFaceImage);
        DlgSetCharRect(_charFaceImage, Vector2.Zero, new Vector2(650f, 1338f));
        _charFaceImage.Modulate = new Color(1f, 1f, 1f, 0f);

        // 第二角色圖層（如 yellow mao，身體表情合一）
        _char2Image = new TextureRect
        {
            Name        = "Char2",
            ExpandMode  = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        charRoot.AddChild(_char2Image);
        DlgSetCharRect(_char2Image, Vector2.Zero, new Vector2(650f, 1338f));
        _char2Image.Modulate = new Color(1f, 1f, 1f, 0f);

        // ── 3. 底部對話框（佔螢幕下方 ~28%）──────────────────────
        float boxHeight = 260f;

        // 對話框底板 - 半透明漸層黑
        var box = new ColorRect { Name = "Box", Color = new Color(0f, 0f, 0f, 0.88f) };
        _dialogueBox = box;
        pt.AddChild(box);
        // Unity: anchor (0,0)-(1,0)、pivot (0.5,0)、sizeDelta (0, boxHeight)
        box.AnchorLeft = 0f; box.AnchorRight = 1f;
        box.AnchorTop  = 1f; box.AnchorBottom = 1f;
        box.OffsetLeft = 0f; box.OffsetRight = 0f;
        box.OffsetTop  = -boxHeight; box.OffsetBottom = 0f;

        // 對話框頂部裝飾線
        var topLine = new ColorRect { Name = "TopLine", Color = new Color(1f, 1f, 1f, 0.15f), MouseFilter = MouseFilterEnum.Ignore };
        box.AddChild(topLine);
        topLine.AnchorLeft = 0f; topLine.AnchorRight = 1f;
        topLine.AnchorTop  = 0f; topLine.AnchorBottom = 0f;
        topLine.OffsetLeft = 0f; topLine.OffsetRight = 0f;
        topLine.OffsetTop  = 0f; topLine.OffsetBottom = 2f;

        // ── 4. 說話者名稱欄（對話框左上角外側）───────────────────
        var nameBar = new TextureRect
        {
            Name        = "NameBar",
            Texture     = NameBarSprite.Get(),   // 圓角 + 左透明漸層名字框
            ExpandMode  = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _nameBarGO   = nameBar;
        _nameBarImage = nameBar;
        pt.AddChild(nameBar);
        // Unity: anchor (0,0)、pivot (0,0)、pos (50, boxHeight+4)、size (300,48)（原始: (260f,42f)）
        nameBar.AnchorLeft = 0f; nameBar.AnchorRight = 0f;
        nameBar.AnchorTop  = 1f; nameBar.AnchorBottom = 1f;
        nameBar.OffsetLeft   = 50f;
        nameBar.OffsetRight  = 50f + 300f;
        nameBar.OffsetBottom = -(boxHeight + 4f);
        nameBar.OffsetTop    = -(boxHeight + 4f) - 48f;
        nameBar.Modulate     = new Color(0.65f, 0.12f, 0.25f, 0.95f);

        _speakerText = new Label
        {
            Name                = "SpeakerTxt",
            Text                = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            MouseFilter         = MouseFilterEnum.Ignore,
        };
        nameBar.AddChild(_speakerText);
        ApplyStretchAnchor(_speakerText, Vector2.Zero, Vector2.One, new Vector2(14f, 0f), new Vector2(-14f, 0f));
        _speakerText.AddThemeFontSizeOverride("font_size", 26);   // 原始: 22
        _speakerText.AddThemeColorOverride("font_color", Colors.White);
        if (cjkFont != null) _speakerText.AddThemeFontOverride("font", cjkFont);
        // 純白文字 + 黑色外框（原始 effectDistance (1.5f,-1.5f)，此處 (2f,-2f)）
        SetOutline(_speakerText, 2f, Colors.Black);

        // ── 5. 對話內文 ──────────────────────────────────────────
        _contentText = new Label
        {
            Name                = "ContentTxt",
            Text                = "",
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment   = VerticalAlignment.Top,
            AutowrapMode        = TextServer.AutowrapMode.Arbitrary,   // horizontalOverflow = Wrap
            ClipText            = true,                                // verticalOverflow = Truncate
            MouseFilter         = MouseFilterEnum.Ignore,
        };
        box.AddChild(_contentText);
        ApplyStretchAnchor(_contentText, Vector2.Zero, Vector2.One, new Vector2(60f, 20f), new Vector2(-60f, -20f));
        _contentText.AddThemeFontSizeOverride("font_size", 28);   // 原始: 24
        _contentText.AddThemeColorOverride("font_color", new Color(0.98f, 0.96f, 1f));
        _contentText.AddThemeConstantOverride("line_spacing", 14);  // lineSpacing = 1.5f
        if (cjkFont != null) _contentText.AddThemeFontOverride("font", cjkFont);

        // 點擊提示（右下角▼閃爍）
        var hint = new Label
        {
            Name                = "HintTxt",
            Text                = "▼ 點擊繼續",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment   = VerticalAlignment.Bottom,
            MouseFilter         = MouseFilterEnum.Ignore,
        };
        box.AddChild(hint);
        // Unity: anchor (1,0)、pivot (1,0)、pos (-20,10)、size (180,24)
        hint.AnchorLeft = 1f; hint.AnchorRight = 1f;
        hint.AnchorTop  = 1f; hint.AnchorBottom = 1f;
        hint.OffsetRight  = -20f;
        hint.OffsetLeft   = -20f - 180f;
        hint.OffsetBottom = -10f;
        hint.OffsetTop    = -10f - 24f;
        hint.AddThemeFontSizeOverride("font_size", 15);
        hint.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.4f));
        if (cjkFont != null) hint.AddThemeFontOverride("font", cjkFont);

        // ── 6. 右上角控制按鈕列 ──────────────────────────────────
        var btnBar = new Control { Name = "ControlBar", MouseFilter = MouseFilterEnum.Ignore };
        _btnBarGO = btnBar;
        pt.AddChild(btnBar);
        // Unity: anchor (1,0)、pivot (1,0)、pos (-20, boxHeight+6)、size (680,32)
        btnBar.AnchorLeft = 1f; btnBar.AnchorRight = 1f;
        btnBar.AnchorTop  = 1f; btnBar.AnchorBottom = 1f;
        btnBar.OffsetRight  = -20f;
        btnBar.OffsetLeft   = -20f - 680f;
        btnBar.OffsetBottom = -(boxHeight + 6f);
        btnBar.OffsetTop    = -(boxHeight + 6f) - 32f;

        // 按鈕從右到左：自動播放 | 快速播放 | 跳過
        _topBtnCfg = ResourceLoader.Exists("res://Resources/DialogueTopBtnConfig.tres")
            ? ResourceLoader.Load<DialogueTopBtnConfig>("res://Resources/DialogueTopBtnConfig.tres")
            : null;
        if (_topBtnCfg == null) _topBtnCfg = new DialogueTopBtnConfig();

        var btnAuto = MakeTopBtn(btnBar, "BtnAuto", "自動播放", new Vector2(1f, 0.5f), _topBtnCfg.AutoPlayPos, _topBtnCfg.AutoPlayW);
        var btnFF   = MakeTopBtn(btnBar, "BtnFF",   "快速播放", new Vector2(1f, 0.5f), _topBtnCfg.FastFwdPos,  _topBtnCfg.FastFwdW);
        var btnSkip = MakeTopBtn(btnBar, "BtnSkip", "跳過",     new Vector2(1f, 0.5f), _topBtnCfg.SkipPos,     _topBtnCfg.SkipW);
        // 保存/讀檔按鈕
        var btnSave = MakeTopBtn(btnBar, "BtnSave", "💾 保存",  new Vector2(1f, 0.5f), _topBtnCfg.SavePos,     _topBtnCfg.SaveW);
        var btnLoad2= MakeTopBtn(btnBar, "BtnLoad", "📂 讀檔",  new Vector2(1f, 0.5f), _topBtnCfg.LoadPos,     _topBtnCfg.LoadW);

        // 儲存引用供實時更新（Godot 的 Label 即是 RectTransform + Outline 的合體，故不再另存）
        _tbAuto = btnAuto; _tbAutoTxt = btnAuto.GetNode<Label>("T");
        _tbFF   = btnFF;   _tbFFTxt   = btnFF  .GetNode<Label>("T");
        _tbSkip = btnSkip; _tbSkipTxt = btnSkip.GetNode<Label>("T");
        _tbSave = btnSave; _tbSaveTxt = btnSave.GetNode<Label>("T");
        _tbLoad = btnLoad2;_tbLoadTxt = btnLoad2.GetNode<Label>("T");

        AddHoverBlock(btnSave);
        btnSave.GetNode<Button>("Hit").Pressed += () => { OpenSavePanel(); };
        AddHoverBlock(btnLoad2);
        btnLoad2.GetNode<Button>("Hit").Pressed += () => { OpenLoadPanel(); };

        _fastFwdBtnTxt  = _tbFFTxt;
        _autoPlayBtnTxt = _tbAutoTxt;

        btnFF.GetNode<Button>("Hit").Pressed += () => _reader?.ToggleFast();

        btnAuto.GetNode<Button>("Hit").Pressed += () => _reader?.ToggleAuto();

        btnSkip.GetNode<Button>("Hit").Pressed += () => _reader?.ToggleFast();

        // ── 黑屏遮罩（最上層，初始透明）──────────────────────────
        _fadeImage = new ColorRect
        {
            Name        = "FadeOverlay",
            Color       = new Color(0f, 0f, 0f, 0f),
            MouseFilter = MouseFilterEnum.Ignore,   // raycastTarget = false
        };
        pt.AddChild(_fadeImage);
        ApplyStretchAnchor(_fadeImage, Vector2.Zero, Vector2.One, Vector2.Zero, Vector2.Zero);
        pt.MoveChild(_fadeImage, pt.GetChildCount() - 1);

        EnsureReader();
        return panel;
    }

    /// <summary>對話框上方的簡潔文字按鈕</summary>
    Control MakeTopBtn(Node parent, string name, string label, Vector2 anchor, Vector2 pos, float width = 120f)
    {
        var go = new Control { Name = name, MouseFilter = MouseFilterEnum.Ignore };  // 背景不接受點擊
        parent.AddChild(go);
        // Unity: anchorMin = anchorMax = anchor、pivot (1,0.5)（右對齊 pivot）、sizeDelta (width,30)
        go.AnchorLeft = anchor.X; go.AnchorRight = anchor.X;
        go.AnchorTop  = 1f - anchor.Y; go.AnchorBottom = 1f - anchor.Y;
        go.OffsetRight  = pos.X;
        go.OffsetLeft   = pos.X - width;
        go.OffsetTop    = -pos.Y - 15f;
        go.OffsetBottom = -pos.Y + 15f;

        // 文字（只有文字區域可點擊 → 文字上疊一個透明 Button）
        var tx = new Label
        {
            Name                = "T",
            Text                = label,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            MouseFilter         = MouseFilterEnum.Ignore,
        };
        go.AddChild(tx);
        ApplyStretchAnchor(tx, Vector2.Zero, Vector2.One, Vector2.Zero, Vector2.Zero);
        tx.AddThemeFontSizeOverride("font_size", 14);
        tx.AddThemeColorOverride("font_color", Colors.White);
        SetOutline(tx, 3f, new Color(0f, 0f, 0f, 1f));

        // 點擊熱區（對應 Unity 的 Button + ColorBlock）
        var hit = new Button { Name = "Hit", Flat = true, Text = "" };
        go.AddChild(hit);
        ApplyStretchAnchor(hit, Vector2.Zero, Vector2.One, Vector2.Zero, Vector2.Zero);
        var hlSb = new StyleBoxFlat { BgColor = new Color(0.3f, 0.15f, 0.35f, 0.8f) };
        var prSb = new StyleBoxFlat { BgColor = new Color(0.1f, 0.05f, 0.15f, 0.9f) };
        hit.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        hit.AddThemeStyleboxOverride("hover",  hlSb);
        hit.AddThemeStyleboxOverride("pressed", prSb);

        return go;
    }

    // ── 劇情播放協程 ─────────────────────────────────────────────

    async Task PlayPrologue(CancellationToken ct = default)
    {
        SetActive(_dialoguePanel, true);
        // 一開始先隱藏對話框，第一句台詞才顯示（避免先出現空白對話框）
        SetDialogueUIVisible(false);
        // 劇情播放期間（含立繪移動、淡入淡出）保留工具列與選單，可快進／返回主畫面
        EnsureReader();
        _reader.KeepChrome = true;

        // 從Resources載入prologue.json
        var data = LoadJsonData<PrologueData>("Data/Dialogues/Prologue/prologue");
        if (data == null || data.lines == null || data.lines.Length == 0)
        {
            GD.PrintErr("[Dialogue] 找不到或無法解析 Resources/Data/Dialogues/prologue.json");
            if (_reader != null) _reader.KeepChrome = false;
            SetActive(_dialoguePanel, false);
            SetActive(_mainPanel, true);
            return;
        }

        GD.Print($"[Dialogue] 開始播放序章，共 {data.lines.Length} 行");

        // 新的一輪：清掉上一輪留下的劇情變數（day1_blanket / day2_blanket…），
        // 否則重玩時兩個選項的變數都 > 0，兩段互斥的劇情會一起播出來
        PlayerDataManager.Instance?.ClearDialogueVar();

        bool isR18 = PlayerDataManager.Instance?.R18ContentEnabled ?? false;
        int activeChoice = -1; // 最近一次選擇框的結果（0 起算）
        string lastSpk = "", lastTxt = ""; // 最近一句顯示的台詞（選擇框出現時要保留在對話框上）

        for (int _i = 0; _i < data.lines.Length; _i++)
        {
            var line = data.lines[_i];

            // ── 分歧跳躍邏輯 ─────────────────────────────────────
            // 全年齡模式：遇到 r18 分歧開始 → 跳到對應的 branchEnd
            // R18 模式：  遇到 allAges 分歧開始 → 跳到對應的 branchEnd
            if (!string.IsNullOrEmpty(line.branchStart))
            {
                bool shouldSkip = (!isR18 && line.branchStart == "r18")
                               || ( isR18 && line.branchStart == "allAges");
                if (shouldSkip)
                {
                    // 跳到「對應的」branchEnd（支援巢狀），for 迴圈結尾 _i++ 後到下一行
                    _i = DialogueSystem.FindBlockEnd(data.lines, _i,
                        l => !string.IsNullOrEmpty(l.branchStart), l => l.branchEnd);
                    continue;
                }
                // 若不跳過（應播放此分歧），直接繼續播放，不做任何事
                continue; // branchStart 本身是純控制標記，不渲染台詞
            }

            // branchEnd 本身也是純控制標記，不渲染台詞
            if (line.branchEnd)
                continue;

            // ── 劇情變數：addVar +1 / varStart 條件段落 ──
            if (!string.IsNullOrEmpty(line.addVar))
            {
                PlayerDataManager.Instance?.AddDialogueVar(line.addVar.Trim());
                if (string.IsNullOrEmpty(line.text)) continue;
            }
            if (!string.IsNullOrEmpty(line.varStart))
            {
                if ((PlayerDataManager.Instance?.GetDialogueVar(line.varStart.Trim()) ?? 0) <= 0)
                    _i = DialogueSystem.FindBlockEnd(data.lines, _i,
                        l => !string.IsNullOrEmpty(l.varStart), l => l.varEnd);
                continue;
            }
            if (line.varEnd) continue;

            // ── 選擇框分歧 ───────────────────────────────────────
            // { "choice": ["蓋被子","不蓋被子"] } → 顯示選擇框並等待玩家選擇
            if (line.choice != null && line.choice.Length > 0)
            {
                _reader?.Stop(); // 選擇點需要玩家手動選擇
                // 選擇框出現時，對話框保持顯示上一句台詞
                SetDialogueUIVisible(true);
                if (_speakerText != null) _speakerText.Text = lastSpk;
                if (_nameBarGO != null) SetActive(_nameBarGO, !string.IsNullOrEmpty(lastSpk));
                if (_contentText != null) { _contentText.Text = lastTxt; _contentText.VisibleCharacters = -1; }
                _choiceResult = -1;
                var choicePanel = BuildButtonOnlyChoice(_dialoguePanel, line.choice);
                await WaitUntil(() => _choiceResult >= 0, ct);
                activeChoice = _choiceResult;
                Kill(choicePanel);
                continue;
            }

            // { "optionStart": n } → 不是玩家選的那個選項就跳到對應的 optionEnd
            if (line.optionStart >= 0)
            {
                if (line.optionStart != activeChoice)
                    _i = DialogueSystem.FindBlockEnd(data.lines, _i,
                        l => l.optionStart >= 0, l => l.optionEnd);
                continue;
            }

            // optionEnd 只是控制標記，不渲染台詞
            if (line.optionEnd)
                continue;

            // 舊的 r18Only 旗標相容（已棄用，保留以防萬一）
            if (line.r18Only && !isR18)
                continue;

            // 背景切換（支援 "bg_name, fade" 格式淡入）
            if (!string.IsNullOrEmpty(line.bgName))
            {
                bool useFade = false;
                string bgFileName = line.bgName;
                if (line.bgName.Contains(","))
                {
                    var parts = line.bgName.Split(',');
                    bgFileName = parts[0].Trim();
                    useFade = parts.Length > 1 && parts[1].Trim().ToLower() == "fade";
                }

                // 同行有 bgScale/bgX/bgY → 先套用，讓圖片出現時就是改過的大小和位置
                bool hasBgTransform = line.bgScale > 0f || !string.IsNullOrEmpty(line.bgX) || !string.IsNullOrEmpty(line.bgY);
                if (hasBgTransform)
                {
                    if (line.bgScale > 0f)
                    {
                        Vector2 s = new Vector2(line.bgScale, line.bgScale);
                        SetScale(_bgImage, s); SetScale(_bgImage2, s);
                    }
                    Vector2 cur = DlgGetStretchOffset(_bgImage);
                    float px = cur.X;
                    float py = cur.Y;
                    if (!string.IsNullOrEmpty(line.bgX)) { float.TryParse(line.bgX.Trim(), out px); }
                    if (!string.IsNullOrEmpty(line.bgY)) { float.TryParse(line.bgY.Trim(), out py); }
                    DlgSetStretchOffset(_bgImage,  px, py);
                    DlgSetStretchOffset(_bgImage2, px, py);
                }

                // 純色背景：black / white（只覆蓋背景，不影響 CG 和立繪）
                if (bgFileName.ToLower() == "black")
                {
                    if (useFade)
                        await FadeBGColor(Colors.Black, 0.6f, ct);
                    else
                    { _bgImage.Texture = DlgWhiteTex(); _bgImage.Modulate = Colors.Black; }
                }
                else if (bgFileName.ToLower() == "white")
                {
                    if (useFade)
                        await FadeBGColor(Colors.White, 0.6f, ct);
                    else
                    { _bgImage.Texture = DlgWhiteTex(); _bgImage.Modulate = Colors.White; }
                }
                else
                {
                    var bgSp = LoadTex($"Backgrounds/{bgFileName}");
                    if (bgSp != null)
                    {
                        if (useFade)
                        {
                            _bgImage2.Texture = bgSp;
                            var bgParent = _bgImage2.GetParent();
                            bgParent.MoveChild(_bgImage2, bgParent.GetChildCount() - 1);
                            int bgIdx = _bgImage.GetIndex();
                            bgParent.MoveChild(_bgImage2, bgIdx + 1);
                            await FadeBG(bgSp, 0.6f, ct);
                        }
                        else
                        {
                            _bgImage.Texture  = bgSp;
                            _bgImage.Modulate = Colors.White;
                        }
                    }
                    else
                    {
                        _bgImage.Modulate = new Color(0.05f, 0.02f, 0.06f);
                    }
                }
            }

            // 背景位置/大小平滑過渡（有 bgAnim 或同行有 bgName 時跳過）
            if (string.IsNullOrEmpty(line.bgAnim) && string.IsNullOrEmpty(line.bgName))
            {
                Vector2 curScale = _bgImage.Scale;
                Vector2 curPos   = DlgGetStretchOffset(_bgImage);
                Vector2 targetScale = (line.bgScale > 0f)
                    ? new Vector2(line.bgScale, line.bgScale)
                    : curScale;
                bool hasBgX = !string.IsNullOrEmpty(line.bgX);
                bool hasBgY = !string.IsNullOrEmpty(line.bgY);
                float bgPosX = hasBgX ? 0f : curPos.X;
                float bgPosY = hasBgY ? 0f : curPos.Y;
                if (hasBgX) float.TryParse(line.bgX.Trim(), out bgPosX);
                if (hasBgY) float.TryParse(line.bgY.Trim(), out bgPosY);
                Vector2 targetBgPos = ClampBgOffset(new Vector2(bgPosX, bgPosY), targetScale.X);
                bool bgNeedMove = targetScale != curScale || targetBgPos != curPos;
                if (bgNeedMove)
                {
                    Vector2 startScale = curScale;
                    Vector2 startPos   = curPos;
                    float dur = 0.4f;
                    await Lerp(FF(dur), p01 =>
                    {
                        float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp(p01, 0f, 1f));
                        SetScale(_bgImage, startScale.Lerp(targetScale, p));
                        Vector2 np = ClampBgOffset(startPos.Lerp(targetBgPos, p), _bgImage.Scale.X);
                        DlgSetStretchOffset(_bgImage, np.X, np.Y);
                        // BG2 同步
                        SetScale(_bgImage2, _bgImage.Scale);
                        DlgSetStretchOffset(_bgImage2, np.X, np.Y);
                    }, ct);
                    SetScale(_bgImage, targetScale);
                    DlgSetStretchOffset(_bgImage, targetBgPos.X, targetBgPos.Y);
                    SetScale(_bgImage2, targetScale);
                    DlgSetStretchOffset(_bgImage2, targetBgPos.X, targetBgPos.Y);
                }
            }

            // 第一人稱呼吸晃動（CG 容器整體輕微起伏）
            if (!string.IsNullOrEmpty(line.breath))
            {
                var bp = line.breath.Split(',');
                _breathOn = bp[0].Trim().ToLower() == "on";
                if (bp.Length > 1 && float.TryParse(bp[1].Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float bs)) _breathPower = bs;
                else if (_breathOn) _breathPower = 1f;
            }

            // 背景動畫（靠近效果，帶搖擺）
            if (!string.IsNullOrEmpty(line.bgAnim))
            {
                string animStr = line.bgAnim.Trim().ToLower();
                if (animStr.StartsWith("approach"))
                {
                    // 解析：approach, 幀數, 強度(1~5)
                    float animFrames = 120f; // 預設2秒
                    int swayLevel = 3;       // 預設3級
                    if (animStr.Contains(","))
                    {
                        var parts = animStr.Split(',');
                        if (parts.Length > 1) { float.TryParse(parts[1].Trim(), out float f); if (f > 0f) animFrames = f; }
                        if (parts.Length > 2) { int.TryParse(parts[2].Trim(), out int lv); if (lv >= 1 && lv <= 5) swayLevel = lv; }
                    }
                    float animDur = animFrames / 60f;

                    // 讀取同行的 bgScale/bgX/bgY 當目標
                    Vector2 curScale = _bgImage.Scale;
                    Vector2 curPos   = DlgGetStretchOffset(_bgImage);
                    Vector2 targetScale = (line.bgScale > 0f)
                        ? new Vector2(line.bgScale, line.bgScale)
                        : curScale;
                    bool hasBgX = !string.IsNullOrEmpty(line.bgX);
                    bool hasBgY = !string.IsNullOrEmpty(line.bgY);
                    float bgPX = hasBgX ? 0f : curPos.X;
                    float bgPY = hasBgY ? 0f : curPos.Y;
                    if (hasBgX) float.TryParse(line.bgX.Trim(), out bgPX);
                    if (hasBgY) float.TryParse(line.bgY.Trim(), out bgPY);
                    Vector2 targetPos = new Vector2(bgPX, bgPY);

                    // 停掉舊的再啟動新的（背景執行，不擋對話）
                    if (_bgAnimCts != null) { _bgAnimCts.Cancel(); _bgAnimCts = null; }
                    _bgAnimCts = new CancellationTokenSource();
                    _ = BgApproachAnim(targetScale, targetPos, animDur, swayLevel, _bgAnimCts.Token);
                }
                else if (animStr == "stop")
                {
                    if (_bgAnimCts != null) { _bgAnimCts.Cancel(); _bgAnimCts = null; }
                    await BgAnimReset(0.6f, ct);
                }
            }

            // 音樂播放
            if (!string.IsNullOrEmpty(line.bgm))
            {
                if (line.bgm.Trim().ToLower() == "stop")
                {
                    _ = FadeOutAudio(_bgmSource, 0.8f, ct);
                }
                else
                {
                    var clip = LoadBgm("Audio/bgm/" + line.bgm.Trim());
                    if (clip != null) { _bgmSource.Stream = clip; SetVol(_bgmSource, BgmVolume * (SettingsManager.Instance?.MusicVolume ?? 1f)); _bgmSource.Play(); }
                }
            }
            // 語音：台詞一開始就播，不等鏡頭／淡入動畫跑完
            if (!string.IsNullOrEmpty(line.text))
            {
                EnsureReader();
                _reader.PreplayVoice((line.speaker ?? "").Trim(), line.text);
            }
            if (!string.IsNullOrEmpty(line.se))
            {
                string seName = line.se;
                float seDelay = 0f;
                if (line.se.Contains(","))
                {
                    var parts = line.se.Split(',');
                    seName = parts[0].Trim();
                    float.TryParse(parts[1].Trim(), out float frames);
                    seDelay = frames / 60f; // 幀數轉秒（60fps）
                }
                var clip = LoadAudio("Audio/se/" + seName.Trim());
                if (clip != null)
                {
                    if (seDelay > 0f)
                        _ = PlaySEDelayed(clip, seDelay, ct);
                    else
                    { _seSource.Stream = clip; _seSource.Play(); }
                }
            }
            if (!string.IsNullOrEmpty(line.me))
            {
                var clip = LoadAudio("Audio/me/" + line.me.Trim());
                if (clip != null)
                {
                    // ME 播放時暫時降低 BGM 音量
                    float bgmVol = GetVol(_bgmSource);
                    SetVol(_bgmSource, 0.2f);
                    _meSource.Stream = clip;
                    _meSource.Play();
                    _ = RestoreBGMAfterME(bgmVol, ct);
                }
                else GD.PushWarning("[Audio] 找不到 ME: Audio/me/" + line.me.Trim());
            }

            // ── 先偵測 fadeout 資訊（判斷是否有優先旗標）──────────
            string _foVal = null; Color _foColor = Colors.Black; bool _foPriority = false;
            if (!string.IsNullOrEmpty(line.fadeoutWhite))
                { _foVal = line.fadeoutWhite; _foColor = Colors.White; }
            else if (!string.IsNullOrEmpty(line.fadeoutBlack))
                { _foVal = line.fadeoutBlack; _foColor = Colors.Black; }
            else if (!string.IsNullOrEmpty(line.fadeout))
                { _foVal = line.fadeout;      _foColor = Colors.Black; }
            string _foFrames = "";
            if (_foVal != null)
            {
                var _foParsed = DialogueSystem.ParseFadeoutValue(_foVal);
                _foFrames   = _foParsed.framesStr;
                _foPriority = _foParsed.priority;
            }

            // ── fadein（黑屏恢復，在處理 bg/showCH 之後）──────────
            // 先處理 bg/CG/立繪（黑屏後面執行），fadein 放到後面

            // 等待（暫停流程）
            if (line.wait > 0f)
                await Wait(line.wait / 60f, ct);

            // ── 解析 hideCG 優先順序（多層）─────────────────────────
            // hideCG（layer 0）有值時，自動擴展到所有已顯示的層
            bool[] _hasHideCGArr = new bool[DialogueSystem.CG_LAYER_COUNT];
            bool[] _hideCGPriorityArr = new bool[DialogueSystem.CG_LAYER_COUNT];
            var _cg0 = GetPrologueCGLayer(line, 0);
            bool _hideAll = _cg0.hasHide; // hideCG 有值 → 隱藏全部
            bool _hideAllPriority = DialogueSystem.IsHideCGPriority(_cg0.hide);
            for (int _li = 0; _li < DialogueSystem.CG_LAYER_COUNT; _li++)
            {
                var _cg = GetPrologueCGLayer(line, _li);
                // 該層自己有 hideCG，或者 hideCG（layer0）要隱藏全部且該層正在顯示
                bool layerActive = _cgImages[_li] != null && _cgImages[_li].Visible;
                _hasHideCGArr[_li] = _cg.hasHide || (_hideAll && layerActive);
                _hideCGPriorityArr[_li] = _cg.hasHide ? DialogueSystem.IsHideCGPriority(_cg.hide) : _hideAllPriority;
            }

            // ── hideCG 優先執行（所有層平行淡出）— fadeout 優先旗標時延後 ──
            if (!_foPriority)
            {
                int _hidePCount = 0;
                for (int _li = 0; _li < DialogueSystem.CG_LAYER_COUNT; _li++)
                {
                    if (_hasHideCGArr[_li] && _hideCGPriorityArr[_li])
                    { _hidePCount++; int li = _li; _ = FadeCGThen(li, "", false, () => _hidePCount--, ct); }
                }
                await WaitWhile(() => _hidePCount > 0, ct);
            }

            // CG 淡入（在台詞出現前）— 如果 fadeout 有優先旗標則延後
            for (int _li = 0; _li < DialogueSystem.CG_LAYER_COUNT; _li++)
            {
                var _cg = GetPrologueCGLayer(line, _li);
                if (!_foPriority && !string.IsNullOrEmpty(_cg.show))
                    await FadeCG(_li, _cg.show, true, 0.6f, ct, _cg);
            }

            // CG 位移/縮放平滑過渡（所有層平行執行）— 如果 fadeout 有優先旗標則延後
            // cgAsync=true → 鏡頭不等待，邊運鏡邊打字；該行台詞結束（點擊後）才會等鏡頭跑完
            int _cgMoving = 0;
            if (!_foPriority)
            {
                for (int _li = 0; _li < DialogueSystem.CG_LAYER_COUNT; _li++)
                {
                    if (_cgImages[_li] == null) continue;
                    var _cg = GetPrologueCGLayer(line, _li);
                    bool hasAny = !string.IsNullOrEmpty(_cg.scale) || !string.IsNullOrEmpty(_cg.x) || !string.IsNullOrEmpty(_cg.y);
                    if (!hasAny) continue;
                    _cgMoving++;
                    _ = AnimCGLayer(_li, _cg, () => _cgMoving--, ct);
                }
                if (!line.cgAsync) await WaitWhile(() => _cgMoving > 0, ct);
            }

            // 判斷 chScale 是否帶圖名（延後到 showCH 之後執行）
            bool _chScaleDeferred = false;
            string _csRaw = line.chScale;
            if (!string.IsNullOrEmpty(_csRaw) && _csRaw.Contains(","))
            {
                float _t; if (!float.TryParse(_csRaw.Split(',')[0].Trim(), out _t))
                {
                    _chScaleDeferred = true;
                    _csRaw = _csRaw.Split(',')[_csRaw.Split(',').Length - 1].Trim();
                }
            }

            // 立繪位置/大小平滑過渡 — 如果帶圖名則延後，如果 fadeout 有優先旗標則延後
            if ((!_foPriority || line.chAsync) && !_chScaleDeferred)
            {
                Vector2 curSize = DlgCharSize(_charBodyImage);
                Vector2 curPos  = DlgCharPos(_charBodyImage);
                var chSP = DialogueSystem.ParseValueWithDuration(_csRaw);
                var chXP = DialogueSystem.ParseValueWithDuration(line.chX);
                var chYP = DialogueSystem.ParseValueWithDuration(line.chY);
                Vector2 targetSize = !string.IsNullOrEmpty(line.chScale) && chSP.value > 0f
                    ? new Vector2(650f * chSP.value, 1338f * chSP.value)
                    : curSize;
                bool hasX = !string.IsNullOrEmpty(line.chX);
                bool hasY = !string.IsNullOrEmpty(line.chY);
                float posX = hasX ? chXP.value : curPos.X;
                float posY = hasY ? chYP.value : curPos.Y;
                Vector2 targetPos = new Vector2(posX, posY);
                float chDur = 0.4f;
                if (!string.IsNullOrEmpty(line.chScale)) chDur = Mathf.Max(chDur, chSP.durationSec);
                if (hasX) chDur = Mathf.Max(chDur, chXP.durationSec);
                if (hasY) chDur = Mathf.Max(chDur, chYP.durationSec);

                bool needMove = targetSize != curSize || targetPos != curPos;
                if (needMove)
                {
                    Vector2 startSize = curSize;
                    Vector2 startPos  = curPos;
                    async Task RunChMove()
                    {
                        await Lerp(FF(chDur), p01 =>
                        {
                            float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp(p01, 0f, 1f));
                            Vector2 s   = startSize.Lerp(targetSize, p);
                            Vector2 pos = startPos.Lerp(targetPos, p);
                            DlgSetCharRect(_charBodyImage, pos, s);
                            DlgSetCharRect(_charFaceImage, pos, s);
                        }, ct);
                        DlgSetCharRect(_charBodyImage, targetPos, targetSize);
                        DlgSetCharRect(_charFaceImage, targetPos, targetSize);
                    }
                    // chAsync=true → 立繪在背景移動，不擋住後面的白屏／台詞
                    if (line.chAsync) _ = RunChMove();
                    else await RunChMove();
                }
            }

            // 同一行有 hideCH + showCH → 先淡出舊圖再淡入新圖
            if (!_foPriority && line.hideCH != null && !string.IsNullOrEmpty(line.showCH))
                await HandleHideCH(line.hideCH, ct);

            // 帶圖名的 chScale → hideCH 之後、showCH 之前設好大小，讓新圖一出來就是正確尺寸
            if (!_foPriority && _chScaleDeferred)
            {
                var chSP = DialogueSystem.ParseValueWithDuration(_csRaw);
                if (chSP.value > 0f)
                {
                    Vector2 targetSize = new Vector2(650f * chSP.value, 1338f * chSP.value);
                    DlgSetCharRect(_charBodyImage, DlgCharPos(_charBodyImage), targetSize);
                    DlgSetCharRect(_charFaceImage, DlgCharPos(_charFaceImage), targetSize);
                }
            }

            // ── 第二角色位移/縮放（在顯示之前先設好位置）──
            if (!_foPriority && _char2Image != null && (!string.IsNullOrEmpty(line.chScale2) || !string.IsNullOrEmpty(line.chX2) || !string.IsNullOrEmpty(line.chY2)))
            {
                Vector2 c2Size = DlgCharSize(_char2Image);
                Vector2 c2Pos  = DlgCharPos(_char2Image);
                var c2SP = DialogueSystem.ParseValueWithDuration(line.chScale2);
                var c2XP = DialogueSystem.ParseValueWithDuration(line.chX2);
                var c2YP = DialogueSystem.ParseValueWithDuration(line.chY2);
                Vector2 tgtSize = !string.IsNullOrEmpty(line.chScale2) && c2SP.value > 0f
                    ? new Vector2(650f * c2SP.value, 1338f * c2SP.value) : c2Size;
                bool hasX2 = !string.IsNullOrEmpty(line.chX2);
                bool hasY2 = !string.IsNullOrEmpty(line.chY2);
                float px = hasX2 ? c2XP.value : c2Pos.X;
                float py = hasY2 ? c2YP.value : c2Pos.Y;
                Vector2 tgtPos = new Vector2(px, py);
                float c2Dur = 0.4f;
                if (!string.IsNullOrEmpty(line.chScale2)) c2Dur = Mathf.Max(c2Dur, c2SP.durationSec);
                if (hasX2) c2Dur = Mathf.Max(c2Dur, c2XP.durationSec);
                if (hasY2) c2Dur = Mathf.Max(c2Dur, c2YP.durationSec);
                bool need = tgtSize != c2Size || tgtPos != c2Pos;
                if (need)
                {
                    Vector2 sSize = c2Size;
                    Vector2 sPos  = c2Pos;
                    await Lerp(FF(c2Dur), p01 =>
                    {
                        float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp(p01, 0f, 1f));
                        DlgSetCharRect(_char2Image, sPos.Lerp(tgtPos, p), sSize.Lerp(tgtSize, p));
                    }, ct);
                    DlgSetCharRect(_char2Image, tgtPos, tgtSize);
                }
            }

            // 第二角色隱藏（在顯示之前）
            if (!_foPriority && line.hideCH2 != null && !string.IsNullOrEmpty(line.showCH2))
                await FadeChar2(false, ct);

            // 立繪淡入 — 兩個角色同時出現
            if (!_foPriority)
            {
                if (HasSpine(line)) await SpineApply(line, false, ct);
                bool hasCH  = !string.IsNullOrEmpty(line.showCH);
                bool hasCH2 = !string.IsNullOrEmpty(line.showCH2);
                if (hasCH && hasCH2)
                {
                    var co1 = FadeChar(line.showCH, true, ct);
                    var co2 = FadeChar2Show(line.showCH2, ct);
                    await co1;
                    await co2;
                }
                else if (hasCH)
                    await FadeChar(line.showCH, true, ct);
                else if (hasCH2)
                    await FadeChar2Show(line.showCH2, ct);
            }

            // 只隱藏不顯示第二角色
            if (!_foPriority && line.hideCH2 != null && string.IsNullOrEmpty(line.showCH2))
                await FadeChar2(false, ct);

            // ── fadein（恢復畫面，顏色自動對應上次 fadeout）────────────
            if (!string.IsNullOrEmpty(line.fadein))
            {
                float.TryParse(line.fadein.Trim(), out float frames);
                float dur = frames > 0f ? frames / 60f : 0.5f;
                await ScreenFade(false, dur, _screenFadeColor, ct);
                _screenBlack = false;
                // autoNext==1 → fadein 結束後直接跳過本行
                if (Mathf.IsEqualApprox(line.autoNext, 1f))
                    continue;
            }

            // ── hideCG 非優先執行（所有層平行淡出，在 text 之前）─────────
            if (!_foPriority)
            {
                int _hideNPCount = 0;
                for (int _li = 0; _li < DialogueSystem.CG_LAYER_COUNT; _li++)
                {
                    if (_hasHideCGArr[_li] && !_hideCGPriorityArr[_li])
                    { _hideNPCount++; int li = _li; _ = FadeCGThen(li, "", false, () => _hideNPCount--, ct); }
                }
                await WaitWhile(() => _hideNPCount > 0, ct);
            }

            // 說話者
            string _spk = (line.speaker ?? "").Trim();
            if (_speakerText != null) _speakerText.Text = _spk;
            // 沒有 speaker → 隱藏名字框
            if (_nameBarGO != null)
                SetActive(_nameBarGO, !string.IsNullOrEmpty(_spk));
            if (_nameBarImage != null && !string.IsNullOrEmpty(_spk))
            {
                if (_spk.Contains("莉奈") || _spk.Contains("Linai"))
                    _nameBarImage.Modulate = new Color(0.85f, 0.65f, 0.75f, 0.95f);
                else if (_spk.Contains("男主") || _spk.Contains("主角"))
                    _nameBarImage.Modulate = new Color(0.55f, 0.70f, 0.90f, 0.95f);
                else
                    _nameBarImage.Modulate = new Color(0.65f, 0.12f, 0.25f, 0.95f);
            }

            // ── 優先 fadeout：在台詞前率先黑屏，然後在黑屏後面處理延後操作 ──
            if (_foPriority && _foVal != null)
            {
                SetDialogueUIVisible(false);
                float.TryParse(_foFrames, out float _pfFrames);
                float _pfDur = _pfFrames > 0f ? _pfFrames / 60f : 0.5f;
                _screenFadeColor = _foColor;
                await ScreenFade(true, _pfDur, _foColor, ct);
                _screenBlack = true;

                // 延後的 hideCG 優先（所有層平行淡出）
                {
                    int _hdpC = 0;
                    for (int _li = 0; _li < DialogueSystem.CG_LAYER_COUNT; _li++)
                    {
                        if (_hasHideCGArr[_li] && _hideCGPriorityArr[_li])
                        { _hdpC++; int li = _li; _ = FadeCGThen(li, "", false, () => _hdpC--, ct); }
                    }
                    await WaitWhile(() => _hdpC > 0, ct);
                }

                // 延後的 showCG（多層）
                for (int _li = 0; _li < DialogueSystem.CG_LAYER_COUNT; _li++)
                {
                    var _cg = GetPrologueCGLayer(line, _li);
                    if (!string.IsNullOrEmpty(_cg.show))
                        await FadeCG(_li, _cg.show, true, 0.6f, ct, _cg);
                }
                // CG 位移/縮放（螢幕遮住狀態，直接設定不需動畫）（多層）
                for (int _li = 0; _li < DialogueSystem.CG_LAYER_COUNT; _li++)
                {
                    if (_cgImages[_li] == null) continue;
                    var _cg = GetPrologueCGLayer(line, _li);
                    var cgNode = _cgImages[_li];
                    var cgSP2 = DialogueSystem.ParseValueWithDuration(_cg.scale);
                    if (!string.IsNullOrEmpty(_cg.scale) && cgSP2.value > 0f)
                        SetScale(cgNode, new Vector2(cgSP2.value, cgSP2.value));
                    bool hasCgX2 = !string.IsNullOrEmpty(_cg.x);
                    bool hasCgY2 = !string.IsNullOrEmpty(_cg.y);
                    if (hasCgX2 || hasCgY2)
                    {
                        var cxP = DialogueSystem.ParseValueWithDuration(_cg.x);
                        var cyP = DialogueSystem.ParseValueWithDuration(_cg.y);
                        Vector2 cgCur = DlgGetStretchOffset(cgNode);
                        float ox = hasCgX2 ? cxP.value : cgCur.X;
                        float oy = hasCgY2 ? cyP.value : cgCur.Y;
                        DlgSetStretchOffset(cgNode, ox, oy);
                    }
                }
                // 立繪位置/大小（螢幕遮住狀態，直接設定不需動畫）
                {
                    string _csRaw2 = line.chScale;
                    if (!string.IsNullOrEmpty(_csRaw2) && _csRaw2.Contains(","))
                    {
                        float _t2; if (!float.TryParse(_csRaw2.Split(',')[0].Trim(), out _t2))
                            _csRaw2 = _csRaw2.Split(',')[_csRaw2.Split(',').Length - 1].Trim();
                    }
                    var chSP2 = DialogueSystem.ParseValueWithDuration(_csRaw2);
                    Vector2 bSize = DlgCharSize(_charBodyImage);
                    if (!string.IsNullOrEmpty(line.chScale) && chSP2.value > 0f)
                    {
                        bSize = new Vector2(650f * chSP2.value, 1338f * chSP2.value);
                        DlgSetCharRect(_charBodyImage, DlgCharPos(_charBodyImage), bSize);
                        DlgSetCharRect(_charFaceImage, DlgCharPos(_charFaceImage), bSize);
                    }
                    bool hasX = !string.IsNullOrEmpty(line.chX);
                    bool hasY = !string.IsNullOrEmpty(line.chY);
                    if (hasX || hasY)
                    {
                        var cxP = DialogueSystem.ParseValueWithDuration(line.chX);
                        var cyP = DialogueSystem.ParseValueWithDuration(line.chY);
                        Vector2 bPos = DlgCharPos(_charBodyImage);
                        float posX = hasX ? cxP.value : bPos.X;
                        float posY = hasY ? cyP.value : bPos.Y;
                        Vector2 np = new Vector2(posX, posY);
                        DlgSetCharRect(_charBodyImage, np, DlgCharSize(_charBodyImage));
                        DlgSetCharRect(_charFaceImage, np, DlgCharSize(_charFaceImage));
                    }
                }
                if (HasSpine(line)) await SpineApply(line, true, ct);
                if (!string.IsNullOrEmpty(line.showCH))
                    await FadeChar(line.showCH, true, ct);

                // 延後的 hideCG 非優先（所有層平行淡出）
                {
                    int _hdnC = 0;
                    for (int _li = 0; _li < DialogueSystem.CG_LAYER_COUNT; _li++)
                    {
                        if (_hasHideCGArr[_li] && !_hideCGPriorityArr[_li])
                        { _hdnC++; int li = _li; _ = FadeCGThen(li, "", false, () => _hdnC--, ct); }
                    }
                    await WaitWhile(() => _hdnC > 0, ct);
                }

                if (line.hideCH != null)
                    await HandleHideCH(line.hideCH, ct);
                // _screenBlack 已設為 true，接下來由黑屏文字處理邏輯繼續
            }

            // 黑屏中處理
            if (_screenBlack)
            {
                // 有 text → 在遮罩前方顯示對話（不管有沒有 speaker）
                bool _hasText = !string.IsNullOrEmpty(line.text);
                if (_hasText)
                {
                    var boxParent = _dialogueBox.GetParent();
                    boxParent.MoveChild(_dialogueBox, boxParent.GetChildCount() - 1);
                    SetActive(_dialogueBox, true);
                    SetActive(_nameBarGO, !string.IsNullOrEmpty(_spk));
                    if (!string.IsNullOrEmpty(_spk))
                    {
                        var nbParent = _nameBarGO.GetParent();
                        nbParent.MoveChild(_nameBarGO, nbParent.GetChildCount() - 1);
                    }
                    SetActive(_btnBarGO, true);
                    await TypeLine(line.text, ct);

                    if (line.autoNext > 1f)
                    {
                        _waitingClick = true;
                        _lockClick    = false; // 允許手動點擊跳過，避免死鎖
                        _autoAdvance  = false;
                        _ = AutoNextTimer(line.autoNext / 60f, ct);
                        await WaitUntil(() => !_waitingClick, ct);
                        _lockClick = false;
                    }
                    else if (!Mathf.IsEqualApprox(line.autoNext, 1f))
                    {
                        _waitingClick = true;
                        _autoAdvance  = false;
                        if (_autoPlaySpeed > 0) _ = AutoAdvanceTimer(ct);
                        await WaitUntil(() => !_waitingClick, ct);
                    }
                    await NextFrame(ct);
                    SetDialogueUIVisible(false);
                    var fadeParent = _fadeImage.GetParent();
                    fadeParent.MoveChild(_fadeImage, fadeParent.GetChildCount() - 1);
                }
                else
                {
                    await NextFrame(ct);
                }
                if (line.hideCH != null)
                    await HandleHideCH(line.hideCH, ct);
                continue;
            }

            // autoNext == 1 或台詞為空 → 隱藏對話框，跳過文字，直接進下一行（避免出現空白對話框）
            if (Mathf.IsEqualApprox(line.autoNext, 1f) || string.IsNullOrEmpty(line.text))
            {
                SetDialogueUIVisible(false);
                await NextFrame(ct);
            }
            else
            {
                // 確保對話框可見（可能被前面的 autoNext==1 或 fadein 跳行隱藏）
                SetDialogueUIVisible(true);
                // 如果沒有 speaker，名字框要保持隱藏
                if (_nameBarGO != null && string.IsNullOrEmpty(_spk))
                    SetActive(_nameBarGO, false);
                // 打字機效果
                await TypeLine(line.text, ct);
                lastSpk = (line.speaker ?? "").Trim();
                lastTxt = line.text;

                // 等待點擊、自動播放、或 autoNext 計時
                if (line.autoNext > 1f)
                {
                    // autoNext > 1：允許手動點擊跳過，避免死鎖
                    _waitingClick = true;
                    _lockClick    = false;
                    _autoAdvance  = false;
                    float autoSec = line.autoNext / 60f;
                    _ = AutoNextTimer(autoSec, ct);
                    await WaitUntil(() => !_waitingClick, ct);
                    _lockClick = false;
                    await NextFrame(ct);
                }
                else
                {
                    _waitingClick = true;
                    _autoAdvance  = false;
                    if (_autoPlaySpeed > 0) _ = AutoAdvanceTimer(ct);
                    await WaitUntil(() => !_waitingClick, ct);
                    await NextFrame(ct);
                }
            }

            // ── cgAsync：台詞結束後，若鏡頭還沒跑完，等它跑完再進下一行 ──
            if (line.cgAsync) await WaitWhile(() => _cgMoving > 0, ct);

            // ── 等待 approach 動畫結束（不可跳過）─────────────────────
            await WaitWhile(() => _bgAnimCts != null, ct);

            // ── fadeout（點擊後，畫面淡出）─────────────────────────
            // 使用迴圈開頭已解析的 _foVal / _foColor / _foPriority / _foFrames
            if (_foVal != null)
            {
                // 隱藏對話框
                SetDialogueUIVisible(false);
                float.TryParse(_foFrames, out float frames);
                float dur = frames > 0f ? frames / 60f : 0.5f;
                _screenFadeColor = _foColor;
                await ScreenFade(true, dur, _foColor, ct);
                _screenBlack = true;

                // 優先旗標：螢幕已遮住，現在才處理延後的 hideCG / showCG / showCH
                if (_foPriority)
                {
                    // 延後的 hideCG 優先（所有層平行淡出）
                    {
                        int _hdpC = 0;
                        for (int _li = 0; _li < DialogueSystem.CG_LAYER_COUNT; _li++)
                        {
                            if (_hasHideCGArr[_li] && _hideCGPriorityArr[_li])
                            { _hdpC++; int li = _li; _ = FadeCGThen(li, "", false, () => _hdpC--, ct); }
                        }
                        await WaitWhile(() => _hdpC > 0, ct);
                    }

                    // 延後的 showCG（多層）
                    for (int _li = 0; _li < DialogueSystem.CG_LAYER_COUNT; _li++)
                    {
                        var _cg = GetPrologueCGLayer(line, _li);
                        if (!string.IsNullOrEmpty(_cg.show))
                            await FadeCG(_li, _cg.show, true, 0.6f, ct, _cg);
                    }
                    // CG 位移/縮放（螢幕遮住狀態，直接設定不需動畫）（多層）
                    for (int _li = 0; _li < DialogueSystem.CG_LAYER_COUNT; _li++)
                    {
                        if (_cgImages[_li] == null) continue;
                        var _cg = GetPrologueCGLayer(line, _li);
                        var cgNode = _cgImages[_li];
                        var cgSP2 = DialogueSystem.ParseValueWithDuration(_cg.scale);
                        if (!string.IsNullOrEmpty(_cg.scale) && cgSP2.value > 0f)
                            SetScale(cgNode, new Vector2(cgSP2.value, cgSP2.value));
                        bool hasCgX2 = !string.IsNullOrEmpty(_cg.x);
                        bool hasCgY2 = !string.IsNullOrEmpty(_cg.y);
                        if (hasCgX2 || hasCgY2)
                        {
                            var cxP = DialogueSystem.ParseValueWithDuration(_cg.x);
                            var cyP = DialogueSystem.ParseValueWithDuration(_cg.y);
                            Vector2 cgCur = DlgGetStretchOffset(cgNode);
                            float ox = hasCgX2 ? cxP.value : cgCur.X;
                            float oy = hasCgY2 ? cyP.value : cgCur.Y;
                            DlgSetStretchOffset(cgNode, ox, oy);
                        }
                    }
                    // 立繪位置/大小（螢幕遮住狀態，直接設定不需動畫）
                    {
                        // 預處理 chScale — 支援 "圖名, 數值" 格式
                        string _csRaw2 = line.chScale;
                        if (!string.IsNullOrEmpty(_csRaw2) && _csRaw2.Contains(","))
                        {
                            float _t2; if (!float.TryParse(_csRaw2.Split(',')[0].Trim(), out _t2))
                                _csRaw2 = _csRaw2.Split(',')[_csRaw2.Split(',').Length - 1].Trim();
                        }
                        var chSP2 = DialogueSystem.ParseValueWithDuration(_csRaw2);
                        if (!string.IsNullOrEmpty(line.chScale) && chSP2.value > 0f)
                        {
                            Vector2 s = new Vector2(650f * chSP2.value, 1338f * chSP2.value);
                            DlgSetCharRect(_charBodyImage, DlgCharPos(_charBodyImage), s);
                            DlgSetCharRect(_charFaceImage, DlgCharPos(_charFaceImage), s);
                        }
                        bool hasX = !string.IsNullOrEmpty(line.chX);
                        bool hasY = !string.IsNullOrEmpty(line.chY);
                        if (hasX || hasY)
                        {
                            var cxP = DialogueSystem.ParseValueWithDuration(line.chX);
                            var cyP = DialogueSystem.ParseValueWithDuration(line.chY);
                            Vector2 bPos = DlgCharPos(_charBodyImage);
                            float posX = hasX ? cxP.value : bPos.X;
                            float posY = hasY ? cyP.value : bPos.Y;
                            Vector2 np = new Vector2(posX, posY);
                            DlgSetCharRect(_charBodyImage, np, DlgCharSize(_charBodyImage));
                            DlgSetCharRect(_charFaceImage, np, DlgCharSize(_charFaceImage));
                        }
                    }
                    if (HasSpine(line)) await SpineApply(line, true, ct);
                    if (!string.IsNullOrEmpty(line.showCH))
                        await FadeChar(line.showCH, true, ct);

                    // 延後的 hideCG 非優先（所有層平行淡出）
                    {
                        int _hdnC = 0;
                        for (int _li = 0; _li < DialogueSystem.CG_LAYER_COUNT; _li++)
                        {
                            if (_hasHideCGArr[_li] && !_hideCGPriorityArr[_li])
                            { _hdnC++; int li = _li; _ = FadeCGThen(li, "", false, () => _hdnC--, ct); }
                        }
                        await WaitWhile(() => _hdnC > 0, ct);
                    }
                }

                if (line.hideCH != null)
                    await HandleHideCH(line.hideCH, ct);
                continue;
            }

            // 立繪淡出（點擊後）— 如果同行有 showCH 則已在前面執行過，跳過
            if (line.hideCH != null && string.IsNullOrEmpty(line.showCH))
                await HandleHideCH(line.hideCH, ct);
        }

        // 劇情結束 → 決定跳轉目標（gotoIfAllAges / gotoIfR18 優先，否則預設 TrainingRoom）
        string nextScene = "TrainingRoom";
        foreach (var l in data.lines)
        {
            if (!isR18 && !string.IsNullOrEmpty(l.gotoIfAllAges)) { nextScene = l.gotoIfAllAges; break; }
            if ( isR18 && !string.IsNullOrEmpty(l.gotoIfR18))     { nextScene = l.gotoIfR18;     break; }
        }
        GD.Print($"[Dialogue] 序章播放完畢，前往 {nextScene}");
        if (_reader != null) _reader.KeepChrome = false;
        SetActive(_dialoguePanel, false);
        await FadeToBlackAndLoad(nextScene, ct);
    }

    // ── 播放 Day1LinaiFirst（12）對話（沿用序章 UI）──────────────
    async Task PlayDay1LinaiFirst(CancellationToken ct = default)
    {
        var data = LoadJsonData<DialogueSystem.DialogueData>("Data/Dialogues/Prologue/Day1LinaiFirst（12）");
        if (data == null)
        {
            GD.PrintErr("[MainMenu] 找不到 Day1LinaiFirst（12）.json");
            return;
        }
        if (data.Lines == null || data.Lines.Count == 0)
            return;

        // 沿用序章的對話面板
        SetActive(_dialoguePanel, true);

        foreach (var line in data.Lines)
        {
            _speakerText.Text = !string.IsNullOrEmpty(line.speaker) ? line.speaker : "";
            // 打字機效果（Godot 用 VisibleCharacters 逐字顯示）
            string fullText = !string.IsNullOrEmpty(line.text) ? line.text : "";
            _contentText.Text = fullText;
            _contentText.VisibleCharacters = 0;

            bool prevPressed = Input.IsMouseButtonPressed(MouseButton.Left);
            for (int i = 0; i < fullText.Length; i++)
            {
                bool nowPressed = Input.IsMouseButtonPressed(MouseButton.Left);
                if (nowPressed && !prevPressed)
                {
                    _contentText.VisibleCharacters = -1;   // 全部顯示
                    break;
                }
                prevPressed = nowPressed;
                _contentText.VisibleCharacters = i + 1;
                await Wait(0.03f, ct);
            }
            _contentText.VisibleCharacters = -1;

            // 等待點擊進入下一行
            await NextFrame(ct);
            bool prevMouse = Input.IsMouseButtonPressed(MouseButton.Left);
            bool prevEnter = Input.IsKeyPressed(Key.Enter);
            while (true)
            {
                bool nowMouse = Input.IsMouseButtonPressed(MouseButton.Left);
                bool nowEnter = Input.IsKeyPressed(Key.Enter);
                if (nowMouse && !prevMouse) break;
                if (nowEnter && !prevEnter) break;
                prevMouse = nowMouse; prevEnter = nowEnter;
                await NextFrame(ct);
            }
        }

        GD.Print("[MainMenu] Day1LinaiFirst（12）播放完畢");
    }

    async Task FadeToBlackAndLoad(string sceneName, CancellationToken ct = default)
    {
        // 進入 TrainingRoom 時確保設定第一晚旗標
        if (sceneName == "TrainingRoom")
            TrainingRoomRuntime.PendingDay2First = true;

        var fadeImg = new ColorRect
        {
            Name        = "FadeOverlay",
            Color       = new Color(0, 0, 0, 0),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(fadeImg);
        ApplyStretchAnchor(fadeImg, Vector2.Zero, Vector2.One, Vector2.Zero, Vector2.Zero);

        await Lerp(1f, p =>
        {
            fadeImg.Color = new Color(0, 0, 0, Mathf.Clamp(p, 0f, 1f));
        }, ct);

        GetTree().ChangeSceneToFile($"res://Scenes/{sceneName}.tscn");
    }

    async Task TypeLine(string text, CancellationToken ct = default)
    {
        BeginReadingLine(text);
        if (_contentText == null) return;
        _contentText.Text = text ?? "";
        _contentText.VisibleCharacters = 0;
        _isTyping   = true;
        _skipTyping = false;

        string full = text ?? "";
        for (int i = 0; i < full.Length; i++)
        {
            while (_reader.Paused) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (_skipTyping) { _contentText.VisibleCharacters = -1; break; }
            _contentText.VisibleCharacters = i + 1;
            float speed = _fastForward ? 0.005f : 0.03f;
            await Wait(speed, ct);
        }
        _contentText.VisibleCharacters = -1;   // 確保全部顯示
        _isTyping = false;
    }

    // 設定面板部件即時同步輔助方法
    void SyncSetPart(Control rt, Vector2 pos, Vector2 scale, Vector2 native, Label txt, string text, int fontSize, Vector2 textOffset)
    {
        if (rt == null) return;
        DlgSetPosSize(rt, pos, new Vector2(native.X * scale.X, native.Y * scale.Y));
        if (txt != null)
        {
            txt.Text = text ?? "";
            txt.AddThemeFontSizeOverride("font_size", Mathf.Max(1, fontSize));
            DlgSetPos(txt, textOffset);
            // 即時同步描邊深度和顏色
            txt.AddThemeConstantOverride("outline_size", 0);
            txt.AddThemeColorOverride("font_color", new Color("79475f"));
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  每幀更新（Unity 的 Update + LateUpdate 合併，Godot 沒有 LateUpdate）
    // ═══════════════════════════════════════════════════════════
    // ── 第一人稱呼吸晃動 ──────────────────────────────────────
    Control _cgBreathRoot;
    bool  _breathOn;
    float _breathPower = 1f, _breathT, _breathW;
    void UpdateBreath(float dt)
    {
        if (_cgBreathRoot == null || !IsInstanceValid(_cgBreathRoot)) return;
        // 開關時用 _breathW 慢慢淡入淡出，避免畫面突然跳動
        _breathW = Mathf.MoveToward(_breathW, _breathOn ? 1f : 0f, dt / 0.8f);
        if (_breathW <= 0f && _cgBreathRoot.Position == Vector2.Zero) return;
        _breathT += dt;
        float k = _breathW * _breathPower;
        const float Tau = Mathf.Pi * 2f;
        // 吸吐約 4 秒一循環：上下為主，左右較慢較小，再帶一點點縮放
        float y = Mathf.Sin(_breathT * Tau / 4.0f) * 7f + Mathf.Sin(_breathT * Tau / 1.7f) * 1.2f;
        float x = Mathf.Sin(_breathT * Tau / 7.3f + 1.1f) * 4f;
        float s = 1f + Mathf.Sin(_breathT * Tau / 4.0f - 0.6f) * 0.006f;
        var size = _cgBreathRoot.Size;
        _cgBreathRoot.PivotOffset = size * 0.5f;
        _cgBreathRoot.Position = new Vector2(x, y) * k;
        _cgBreathRoot.Scale = Vector2.One * Mathf.Lerp(1f, s, Mathf.Min(k, 1f));
    }

    public override void _Process(double delta)
    {
        // ① 原本的 Update 邏輯（含提前 return，故抽成獨立方法）
        UpdateDialogueInput((float)delta);
        UpdateOpBgmEq();
        UpdateBreath((float)delta);

        // ② 原本的 LateUpdate 邏輯：即時同步 Inspector 數值到 UI 物件
        if (_titleRT != null)
            DlgSetPosSize(_titleRT, TitlePos, TitleSize);

        if (_btnStartRT != null) DlgSetPos(_btnStartRT, BtnStartPos);
        if (_btnLoadRT  != null) DlgSetPos(_btnLoadRT,  BtnLoadPos);
        if (_btnSettRT  != null) DlgSetPos(_btnSettRT,  BtnSettPos);
        if (_btnQuitRT  != null) DlgSetPos(_btnQuitRT,  BtnQuitPos);
        if (_btnGalleryRT != null) DlgSetPos(_btnGalleryRT, BtnGalleryPos);

        _cuteMenuTime += (float)delta;
        if (_titleRT != null && _titleRT.IsVisibleInTree())
        {
            _titleRT.PivotOffset = _titleRT.Size * 0.5f;
            _titleRT.Scale = Vector2.One * (1f + 0.018f * Mathf.Sin(_cuteMenuTime * 1.65f));
        }
        var menuButtons = new[] { _btnStartRT, _btnLoadRT, _btnSettRT, _btnQuitRT, _btnGalleryRT };
        for (int i = 0; i < menuButtons.Length; i++)
            if (menuButtons[i] != null && menuButtons[i].IsVisibleInTree())
                menuButtons[i].Position += new Vector2(0, 0.8f * Mathf.Sin(_cuteMenuTime * 0.70f + i * 0.45f));

        // 設定面板即時同步
        if (_setBgRT   != null) DlgSetPosSize(_setBgRT,   SetPanelPos,    SetPanelSize);
        if (_setHeadRT != null) DlgSetPosSize(_setHeadRT, SetHeadPos,     SetHeadSize);
        if (_setBtn7RT != null) DlgSetPosSize(_setBtn7RT, SetBtnApplyPos, SetBtnApplySize);
        if (_setBtn8RT != null) DlgSetPosSize(_setBtn8RT, SetBtnExitPos,  SetBtnExitSize);

        // 列佈局即時同步（移動整列容器）
        Vector2 rowSize = new Vector2(SetRowW, SetRowH);
        if (_setSnd1RT != null) DlgSetPosSize(_setSnd1RT, new Vector2(0, SetRowStartY), rowSize);
        if (_setSnd2RT != null) DlgSetPosSize(_setSnd2RT, new Vector2(0, SetRowStartY - SetRowGap), rowSize);
        if (_setSnd3RT != null) DlgSetPosSize(_setSnd3RT, new Vector2(0, SetRowStartY - SetRowGap * 2), rowSize);
        if (_setLangBtnRT  != null) DlgSetPosSize(_setLangBtnRT,  new Vector2(0, SetRowStartY - SetRowGap * 3), rowSize);
        if (_setLangBtn2RT != null) DlgSetPosSize(_setLangBtn2RT, new Vector2(0, SetRowStartY - SetRowGap * 4), rowSize);
        if (_setLangBtn3RT != null) DlgSetPosSize(_setLangBtn3RT, new Vector2(0, SetRowStartY - SetRowGap * 5), rowSize);

        // 偵測滑動參數變化 → 重播滑動
        if (_slideRT != null)
        {
            if (_prevSlideStart != SlideStart || _prevSlideEnd != SlideEnd
                || _prevSlideDuration != SlideDuration)
            {
                _prevSlideStart = SlideStart;
                _prevSlideEnd = SlideEnd;
                _prevSlideDuration = SlideDuration;
                RestartSlide();
            }
            // 滑動結束後的點擊切換圖片改由 _Input 處理（見 _Input）
        }
    }

    /// <summary>原 Unity Update() 的對話輸入邏輯（保留原本的提前 return 流程）</summary>
    void UpdateDialogueInput(float delta)
    {
        SyncTopBtns(); // 推進由 NovelReader 統一處理，避免 UI 點擊穿透。
    }

    /// <summary>推進對話：打字中則跳過打字機，否則結束等待點擊</summary>
    void AdvanceDialogue()
    {
        if (_isTyping) _skipTyping = true;
        else if (_waitingClick && !_lockClick) _waitingClick = false;
    }

    // ═══════════════════════════════════════════════════════════
    //  輸入事件（Unity 的 Input.GetMouseButtonDown / spaceKey.wasPressedThisFrame）
    // ═══════════════════════════════════════════════════════════
    public override void _Input(InputEvent @event)
    {
        if (_galleryScreen != null && _galleryScreen.Visible) return;
        if (_reader != null && (_waitingClick || _isTyping || _reader.HistoryOpen)) return;
        bool clicked = @event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true };
        if (clicked && _slideRT != null && SlideClickSwitch && _slideCts == null && !_slideSwitching)
            _ = SwitchSlideImage(_lifeCts.Token);
    }

    // 自動播放計時器
    async Task AutoAdvanceTimer(CancellationToken ct = default)
    {
        float delay = _autoPlaySpeed == 1 ? 2f
                    : _autoPlaySpeed == 2 ? 1f
                    : _autoPlaySpeed == 3 ? 0.5f
                                          : 0.15f;   // X4
        await Wait(delay, ct);
        _autoAdvance = true;
    }

    // ── 背景靠近動畫（帶搖擺慢慢移動到目標位置/大小）─────────
    // 搖擺強度：1=輕微 2=小幅度 3=普通 4=大幅度 5=超強
    async Task BgApproachAnim(Vector2 targetScale, Vector2 targetPos, float duration, int swayLevel = 3, CancellationToken ct = default)
    {
        Vector2 startScale = _bgImage.Scale;
        Vector2 startPos   = DlgGetStretchOffset(_bgImage);
        // 根據等級設定幅度和頻率
        float[] amps  = { 0f, 10f, 20f, 35f, 55f, 80f }; // 1~5 級像素幅度
        float[] freqs = { 0f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f };
        int lv = Mathf.Clamp(swayLevel, 1, 5);
        float swayAmp  = amps[lv];
        float swayFreq = freqs[lv];

        try
        {
            await Lerp(FF(duration), p01 =>
            {
                float t = p01 * duration;
                float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp(p01, 0f, 1f));

                // 基礎位置（往目標平滑過渡）
                Vector2 basePos = startPos.Lerp(targetPos, p);
                // 上下晃動（走路感）
                float swayFade = 1f - p;
                float swayY = Mathf.Sin(t * swayFreq * Mathf.Pi * 2f) * swayAmp * swayFade;

                Vector2 np = basePos + new Vector2(0f, swayY);
                // 限制在放大後多出來的範圍內，避免走路晃動或位移太多時露出圖片外的黑幕
                np = ClampBgOffset(np, startScale.Lerp(targetScale, p).X);
                DlgSetStretchOffset(_bgImage,  np.X, np.Y);
                SetScale(_bgImage, startScale.Lerp(targetScale, p));
                DlgSetStretchOffset(_bgImage2, np.X, np.Y);
                SetScale(_bgImage2, _bgImage.Scale);
            }, ct);
        }
        catch (OperationCanceledException)
        {
            // 被 stop 指令中斷 → 保持當下狀態，交由 BgAnimReset 收尾
            _bgAnimCts = null;
            return;
        }

        // 確保最終精確到位（同樣不超出放大後的範圍）
        targetPos = ClampBgOffset(targetPos, targetScale.X);
        SetScale(_bgImage, targetScale);
        DlgSetStretchOffset(_bgImage, targetPos.X, targetPos.Y);
        SetScale(_bgImage2, targetScale);
        DlgSetStretchOffset(_bgImage2, targetPos.X, targetPos.Y);
        _bgAnimCts = null;
    }

    // 停止動畫後平滑回到正常狀態
    async Task BgAnimReset(float duration, CancellationToken ct = default)
    {
        Vector2 startScale = _bgImage.Scale;
        Vector2 startPos   = DlgGetStretchOffset(_bgImage);
        Vector2 endScale = Vector2.One;
        Vector2 endPos   = Vector2.Zero;

        await Lerp(FF(duration), p01 =>
        {
            float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp(p01, 0f, 1f));
            SetScale(_bgImage, startScale.Lerp(endScale, p));
            Vector2 np = startPos.Lerp(endPos, p);
            DlgSetStretchOffset(_bgImage, np.X, np.Y);
            SetScale(_bgImage2, _bgImage.Scale);
            DlgSetStretchOffset(_bgImage2, np.X, np.Y);
        }, ct);

        SetScale(_bgImage, endScale);
        DlgSetStretchOffset(_bgImage, endPos.X, endPos.Y);
        SetScale(_bgImage2, endScale);
        DlgSetStretchOffset(_bgImage2, endPos.X, endPos.Y);
    }

    // autoNext 計時（時間到自動跳下一行）
    async Task AutoNextTimer(float seconds, CancellationToken ct = default)
    {
        if (_reader != null && await _reader.WaitScriptDelay(seconds, ct))
            _waitingClick = false;
    }

    // 控制對話框 UI 顯隱（Box + 按鈕列）
    // 名字框不在此控制：顯示時由 BeginLine / NovelReader.BeginLine 根據 speaker 決定；
    // 隱藏時一律跟著對話框一起隱藏。
    void SetDialogueUIVisible(bool visible)
    {
        if (_dialogueBox != null) SetActive(_dialogueBox, visible);
        if (!visible && _nameBarGO != null) SetActive(_nameBarGO, false);
        if (_btnBarGO    != null) SetActive(_btnBarGO,    visible);
    }
}
