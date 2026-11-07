using Godot;
using System.Collections.Generic;

/// <summary>
/// 舌頭：左下角 👅 按鈕選取（紅圈＝已選，再點取消）→ 點嘴巴開始（嘴巴換成 舌頭1→2→3→2 循環動畫），
/// 按住左鍵在嘴上往上拖加速、往下拖減速；右鍵點嘴巴停止。進行中會累積高潮值與嘴部舒服度。
/// 舌頭圖是整張 2000x3200 畫布（跟臉同一座標），所以疊一張 TextureRect 每幀複製「臉」圖層的變換，
/// 會跟著呼吸／摸頭一起動。
/// </summary>
public partial class TrainingRoomRuntime
{
    [ExportGroup("舌頭")]
    [Export] public float TongueFrameSpeed   = 2.5f;     // 每秒換幾格（等級 0）
    [Export] public float TongueSpeedLvRate  = 0.005f;  // 拖曳每 1 螢幕 px 改變多少等級
    [Export] public float TongueSpeedLvMax   = 5f;
    [Export] public float TongueSpeedPerLv   = 0.3f;    // 每級動畫加速
    [Export] public float ArousalRateMouth   = 1.5f;    // 舌頭進行中的高潮值累積（跟胸／下方同一套曲線）
    [Export] public float TongueComfortRate  = 2f;      // 每秒增加多少嘴部舒服度（ComfortMouth，上限 100）
    [Export] public string TongueOutMouthLayer = "小嘴吐舌";   // 嘴部舒服度到頂之後，嘴巴固定換成這張（吐舌頭）
    bool _mouthTongueOut;   // 嘴部舒服度到頂過 → 嘴巴維持吐舌頭

    static readonly string[] _tongueFrames = { "Sprites/舌頭1", "Sprites/舌頭2", "Sprites/舌頭3", "Sprites/舌頭2" };
    static readonly string[] _mouthLayerNames = { "表情/嘴巴", "笑", "笑2", "小嘴", "吐舌", "張嘴1", "張嘴2", "斜動嘴", "抖動嘴", "小嘴吐舌", "小嘴巴" };

    bool  _tongueTool;      // 左下角 👅 已選
    bool  _tongueOn;        // 舌頭動作進行中
    bool  _tongueDragOwner;
    float _tongueT, _tongueSpeedLv, _tongueLastMouseY;
    TextureRect _tongueOverlay;
    Texture2D[] _tongueTex;
    Button _btnTongue;
    readonly Dictionary<string, bool> _mouthVisSaved = new();

    void ToggleTongueTool()
    {
        _tongueTool = !_tongueTool;
        if (_tongueTool && _dragTool) SetDragTool(false);
        if (_tongueTool && _handMode != HandMode.None) { _handMode = HandMode.None; RefreshHandModeButtons(); }   // 按鈕不能同時選兩個
        if (!_tongueTool && _tongueOn) StopTongue();
        RefreshTongueButton();
    }

    void RefreshTongueButton()
    {
        if (_btnTongue == null || !IsInstanceValid(_btnTongue)) return;
        Color bg = new Color(1.00f, 0.66f, 0.72f);
        Color fill = _tongueTool ? bg : Colors.White;
        StyleBoxFlat Mk(Color c)
        {
            var sb = MkRoundBtnStyle(c);
            if (_tongueTool) { sb.BorderColor = new Color(0.92f, 0.12f, 0.18f); sb.SetBorderWidthAll(5); }
            return sb;
        }
        _btnTongue.AddThemeStyleboxOverride("normal",  Mk(fill));
        _btnTongue.AddThemeStyleboxOverride("hover",   Mk(_tongueTool ? fill.Lightened(0.15f) : fill.Darkened(0.06f)));
        _btnTongue.AddThemeStyleboxOverride("pressed", Mk(fill.Darkened(0.12f)));
        RefreshMouseButton();
    }

    bool OnMouthWorld(Vector2 world)
    {
        var h = (_hotspotRTs != null && _hotspotRTs.Length > 1) ? _hotspotRTs[1] : null;
        return h != null && h.GetGlobalRect().Grow(20f).HasPoint(world);
    }

    /// <summary>嘴巴熱點被點：有選 👅 就開始舌頭（回傳 true＝已處理）。</summary>
    bool TongueOnMouthClicked()
    {
        if (!_tongueOn) { StopLick(); StartTongue(); }   // 嘴巴只能用舌頭（不用先選 👅）；舌頭只有一條，先停掉舔
        return true;
    }

    /// <summary>由 _Input 呼叫（不吃掉事件）：記錄這次左鍵拖曳是不是從嘴巴開始的（用來調速度）。</summary>
    void TongueInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
        {
            Vector2 w = ScreenToWorld(mb.Position);
            bool overUi = IsPointerOverUi();
            _tongueDragOwner = mb.Pressed && !overUi && (OnMouthWorld(w) || (_lickOn && IsOnLickTarget(w)));
            if (_tongueDragOwner) _tongueLastMouseY = mb.Position.Y;
            // 乳頭本體也可以點（選了 👅 時）
            if (mb.Pressed && !overUi && _tongueTool && !_tongueDragOwner)
            {
                string nip = NippleAt(w);
                if (nip != null) { ToggleLick(nip); _tongueDragOwner = _lickOn; _tongueLastMouseY = mb.Position.Y; }
            }
        }
        else if (e is InputEventMouseMotion mm && _tongueTool && !IsPointerOverUi())
        {
            Vector2 mw = ScreenToWorld(mm.Position);
            bool onNip = NippleAt(mw) != null || (_jijiPhase == 0 && IsPointOnPussy(mw));   // 乳頭、小穴（一般腿／M 腿）都換成舌頭游標
            if (onNip && _hoverCursorKind != 3) ApplyTongueCursor();
            else if (!onNip && _tongueNipHover && _hoverCursorKind == 3) ApplyHandCursor();
            _tongueNipHover = onNip;
        }
    }

    void StartTongue()
    {
        StopLick();
        _tongueTex ??= new Texture2D[_tongueFrames.Length];
        for (int i = 0; i < _tongueFrames.Length; i++) _tongueTex[i] ??= ResourceHelper.LoadTexAny(_tongueFrames[i]);
        if (_tongueTex[0] == null) return;
        if (!Layers.TryGetValue("臉", out var face) || face == null) return;

        if (_tongueOverlay == null || !IsInstanceValid(_tongueOverlay))
        {
            _tongueOverlay = new TextureRect
            {
                Name = "TongueOverlay",
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            var parent = face.GetParent();
            parent.AddChild(_tongueOverlay);
            // 畫在嘴巴圖層那一層（最後一個嘴巴圖層之後）
            int idx = -1;
            foreach (var n in _mouthLayerNames)
                if (Layers.TryGetValue(n, out var ml) && ml != null && ml.GetParent() == parent) idx = Mathf.Max(idx, ml.GetIndex());
            if (idx >= 0) parent.MoveChild(_tongueOverlay, idx + 1);
        }

        // 先記下嘴巴圖層的顯示狀態，舌頭期間藏起來，結束再還原
        _mouthVisSaved.Clear();
        foreach (var n in _mouthLayerNames)
            if (Layers.TryGetValue(n, out var ml) && ml != null) _mouthVisSaved[n] = ml.Visible;

        _tongueOverlay.Visible = true;
        _tongueOn = true;
        _tongueT = 0f;
        _tongueSpeedLv = 0f;
        PlayerDataManagerTouch();
    }

    void StopTongue()
    {
        _tongueOn = false;
        if (_tongueOverlay != null && IsInstanceValid(_tongueOverlay)) _tongueOverlay.Visible = false;
        if (!_mouthTongueOut)
            foreach (var kv in _mouthVisSaved)
                if (Layers.TryGetValue(kv.Key, out var ml) && ml != null) ml.Visible = kv.Value;
        _mouthVisSaved.Clear();
        if (_mouthTongueOut) ApplyTongueOutMouth();
    }

    /// <summary>嘴巴固定成吐舌頭：其他嘴巴圖層全藏，只顯示 TongueOutMouthLayer（每幀壓住，表情系統改不掉）。</summary>
    void ApplyTongueOutMouth()
    {
        foreach (var n in _mouthLayerNames)
        {
            if (!Layers.TryGetValue(n, out var ml) || ml == null) continue;
            bool want = n == TongueOutMouthLayer;
            if (ml.Visible != want) ml.Visible = want;
            if (want && ml.Modulate.A < 0.99f) ml.Modulate = new Color(ml.Modulate.R, ml.Modulate.G, ml.Modulate.B, 1f);
        }
    }

    /// <summary>每幀（HeadPatLate，所有圖層更新完、摸頭疊加之後）呼叫。</summary>
    void UpdateTongue(float dt)
    {
        if (!_mouthTongueOut && ComfortMouth >= 100f) _mouthTongueOut = true;   // 舒服到頂 → 之後嘴巴變吐舌頭
        if (!_tongueOn && _mouthTongueOut) ApplyTongueOutMouth();
        if (!_tongueOn || _tongueOverlay == null || !IsInstanceValid(_tongueOverlay)) return;

        if (_tongueDragOwner && Input.IsMouseButtonPressed(MouseButton.Left))
        {
            float y = GetViewport().GetMousePosition().Y;
            _tongueSpeedLv = Mathf.Clamp(_tongueSpeedLv + (_tongueLastMouseY - y) * TongueSpeedLvRate, 0f, TongueSpeedLvMax);
            _tongueLastMouseY = y;
        }

        _tongueT += dt * TongueFrameSpeed * (1f + _tongueSpeedLv * TongueSpeedPerLv);
        int f = (int)Mathf.Floor(_tongueT) % _tongueTex.Length;
        if (_tongueTex[f] != null) _tongueOverlay.Texture = _tongueTex[f];

        // 跟著「臉」圖層的變換（呼吸、摸頭旋轉都會帶到）
        if (Layers.TryGetValue("臉", out var face) && face != null)
        {
            _tongueOverlay.Size = face.Size;
            _tongueOverlay.Position = face.Position;
            _tongueOverlay.PivotOffset = face.PivotOffset;
            _tongueOverlay.RotationDegrees = face.RotationDegrees;
            _tongueOverlay.Scale = face.Scale;
        }
        // 表情系統可能會把嘴巴打開，每幀壓回隱藏
        foreach (var n in _mouthLayerNames)
            if (Layers.TryGetValue(n, out var ml) && ml != null && ml.Visible) ml.Visible = false;

        _tongueOverlay.Modulate = new Color(1f, 1f, 1f, SettingsManager.Instance?.InteractAlpha ?? 0.85f);   // 嘴上的舌頭也套用透明度設定
        ComfortMouth = Mathf.Min(100f, ComfortMouth + TongueComfortRate * (1f + _tongueSpeedLv * 0.2f) * dt);
    }

    // ════════════════════════════════════════════════════════════════
    //  舔：選了 👅 後點胸部綠點／乳頭／B → 舌頭在那裡上下舔（舌頭只有一條，跟嘴巴互斥）
    //  L＝畫面左邊那顆（胸2／奶頭2，跟左手同一顆）、R＝畫面右邊（胸1／奶頭1）、B＝下方
    // ════════════════════════════════════════════════════════════════
    [ExportGroup("舔")]
    [Export] public float   LickSpeed        = 2.5f;     // 舔的基本速度
    [Export] public float   LickTongueSize   = 28f;     // 舌頭圖大小（場景 px，原 70）
    [Export] public float   LickStrokePx     = 26f;     // 每一下往上舔的距離（畫布 px）
    [Export] public float   LickBreastMovePx = 4f;      // 胸／乳頭被舔時往上推的量（畫布 px）
    [Export] public float   LickArousalMult  = 1.2f;    // 舔的高潮值倍率（相對用手）
    [Export] public Vector2 LickTongueNudge  = new Vector2(0f, 10f);   // 舌頭位置微調（場景 px）
    [Export] public Vector2 LickTongueNudgeB = new Vector2(2f, 15f);   // 舔 B 時再額外微調（場景 px，Y 正＝往下）
    [Export] public string  TongueIconSprite = "舌頭3";   // 舌頭游標／舔用的圖（自動裁掉透明邊）；可改 舌頭1／舌頭2／舌頭3／小嘴吐舌／吐舌笑
    [Export] public float   TongueCursorScale = 0.3f;     // 舌頭游標相對手游標的大小
    [Export(PropertyHint.Range, "0,1")] public float LickTongueAlpha = 1f;   // 舔的舌頭透明度（1＝不透明）
    static readonly Dictionary<string, Vector2> _nippleCenter = new()
    {
        { "奶頭2", new Vector2(830.5f, 1001f) },    // L（PIL 內容框中心）
        { "奶頭1", new Vector2(1099.5f, 1001f) },   // R
    };

    bool   _lickOn, _tongueNipHover;
    string _lickTarget;
    float  _lickT;
    TextureRect _lickTongue;

    static string BreastOf(string t) => t == "L" ? "胸2" : "胸1";
    static string NippleOf(string t) => t == "L" ? "奶頭2" : "奶頭1";

    string NippleAt(Vector2 w)
    {
        if (IsPointOnLayer("奶頭2", w)) return "L";
        if (IsPointOnLayer("奶頭1", w)) return "R";
        return null;
    }

    bool IsOnLickTarget(Vector2 w)
    {
        if (!_lickOn) return false;
        if (_lickTarget == "A") return IsMouseOverMLegAnus(GetViewport().GetMousePosition());
        if (_lickTarget == "B") return IsPointOnPussy(w) || (_hotspotRTs != null && _hotspotRTs[4] != null && _hotspotRTs[4].GetGlobalRect().Grow(20f).HasPoint(w));
        int hi = _lickTarget == "L" ? 2 : 3;
        return IsPointOnLayer(NippleOf(_lickTarget), w) || IsPointOnLayer(BreastOf(_lickTarget), w)
            || (_hotspotRTs != null && _hotspotRTs[hi] != null && _hotspotRTs[hi].GetGlobalRect().HasPoint(w));
    }

    void ToggleLick(string target)
    {
        if (_lickOn && _lickTarget == target) { StopLick(); return; }
        StartLick(target);
    }

    void StartLick(string target)
    {
        StopLick();
        if (_tongueOn) StopTongue();
        // 同一個地方有手在弄就先拿掉手
        if (target == "L" && _touchAnimRunningLeft)  CancelHandTouchAnim(true);
        if (target == "R" && _touchAnimRunningRight) CancelHandTouchAnim(false);
        if (target == "B" && _bTouchRunning) CancelBTouch();
        if (target == "L") CancelNipple("NL");
        if (target == "R") CancelNipple("NR");

        var tex = TongueCursorTexture((int)LickTongueSize);
        if (tex == null) return;
        if (_touchHandLayer == null)
        {
            _touchHandLayer = new Control { Name = "TouchHandLayer", MouseFilter = Control.MouseFilterEnum.Ignore };
            _touchHandLayer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _canvas.AddChild(_touchHandLayer);
        }
        _lickTongue = new TextureRect
        {
            Name = "LickTongue", Texture = tex,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Size = new Vector2(LickTongueSize, LickTongueSize),
        };
        _lickTongue.PivotOffset = new Vector2(LickTongueSize * 0.5f, LickTongueSize);   // 以舌根（下緣）為軸
        _touchHandLayer.AddChild(_lickTongue);
        _lickOn = true; _lickTarget = target; _lickT = 0f; _tongueSpeedLv = 0f;
        HadBreastTouch = true;
        PlayerDataManagerTouch();
    }

    void StopLick()
    {
        if (!_lickOn) return;
        _lickOn = false;
        if (_lickTongue != null && IsInstanceValid(_lickTongue)) _lickTongue.QueueFree();
        _lickTongue = null;
        if (_lickTarget == "B") SetPussyDeform(0f);
        else if (_lickTarget != "A")
        {
            TouchAnimOffset.Remove(BreastOf(_lickTarget));
            TouchAnimOffset.Remove(NippleOf(_lickTarget));
        }
        _lickTarget = null;
    }

    /// <summary>每幀（HeadPatLate）呼叫。</summary>
    void UpdateLick(float dt)
    {
        if (!_lickOn || _lickTongue == null || !IsInstanceValid(_lickTongue)) return;
        if (_tongueDragOwner && Input.IsMouseButtonPressed(MouseButton.Left))
        {
            float y = GetViewport().GetMousePosition().Y;
            _tongueSpeedLv = Mathf.Clamp(_tongueSpeedLv + (_tongueLastMouseY - y) * TongueSpeedLvRate, 0f, TongueSpeedLvMax);
            _tongueLastMouseY = y;
        }
        _lickT += dt * LickSpeed * (1f + _tongueSpeedLv * TongueSpeedPerLv);
        // 舔：快速往上、慢慢回來（0→1→0）
        float ph = _lickT % 1f;
        float stroke = ph < 0.35f ? Mathf.SmoothStep(0f, 1f, ph / 0.35f) : 1f - Mathf.SmoothStep(0f, 1f, (ph - 0.35f) / 0.65f);

        Vector2 tip;
        if (_lickTarget == "B")
        {
            tip = PussyRubPoint();
            if (_mLegOpen && _jijiPhase == 0 && !_holeLeftOpen) { _holeLeftOpen = true; _holeLeftScale = 0f; }   // 舔濕 → 洞慢慢張開（Jiji.cs）
            SetPussyDeform(stroke * (_mLegOpen ? BTouchMLegPussyDeformAmount : BTouchPussyDeformAmount));
        }
        else if (_lickTarget == "A")
        {
            if (!AnusCenterScreen(out tip)) tip = GetViewport().GetMousePosition();
        }
        else
        {
            string nip = NippleOf(_lickTarget);
            Vector2 push = new Vector2(0f, -stroke * LickBreastMovePx);
            TouchAnimOffset[BreastOf(_lickTarget)] = push;
            TouchAnimOffset[nip] = push;
            tip = Layers.TryGetValue(nip, out var nc) && nc != null ? nc.GetGlobalTransform() * _nippleCenter[nip] : Vector2.Zero;
        }
        float strokeScene = stroke * LickStrokePx * CharScreenScale();
        _lickTongue.GlobalPosition = tip - new Vector2(LickTongueSize * 0.5f, LickTongueSize * 0.85f)
                                   + LickTongueNudge + (_lickTarget == "B" || _lickTarget == "A" ? LickTongueNudgeB : Vector2.Zero) + new Vector2(0f, -strokeScene + LickStrokePx * CharScreenScale() * 0.5f);
        _lickTongue.RotationDegrees = Mathf.Sin(_lickT * Mathf.Tau) * 4f;
        _lickTongue.Modulate = new Color(1f, 1f, 1f, LickTongueAlpha);
    }

    /// <summary>舌頭圖（游標／舔用）：沒有專用素材時，從「舌頭2」裁出舌頭那一塊放大。
    /// 之後如果有專用圖，放在 Sprites/舌頭游標.png 會優先使用。</summary>
    ImageTexture TongueCursorTexture(int size)
    {
        Image img = null;
        var custom = ResourceHelper.LoadTexAny("Sprites/舌頭游標");
        if (custom != null) img = custom.GetImage();
        else
        {
            var t2 = ResourceHelper.LoadTexAny("Sprites/" + TongueIconSprite) ?? ResourceHelper.LoadTexAny("Sprites/舌頭2");
            if (t2 == null) return null;
            var full = t2.GetImage();
            if (full.IsCompressed()) full.Decompress();
            Rect2I used = full.GetUsedRect();   // 自動裁掉透明邊（整張 2000x3200 畫布只取有畫的部分）
            if (used.Size.X <= 0 || used.Size.Y <= 0) return null;
            img = full.GetRegion(used.Grow(4).Intersection(new Rect2I(Vector2I.Zero, full.GetSize())));
        }
        if (img == null) return null;
        if (img.IsCompressed()) img.Decompress();
        float sc = size / (float)Mathf.Max(img.GetWidth(), img.GetHeight());
        img.Resize(Mathf.Max(1, (int)(img.GetWidth() * sc)), Mathf.Max(1, (int)(img.GetHeight() * sc)), Image.Interpolation.Lanczos);
        return ImageTexture.CreateFromImage(img);
    }

    ImageTexture _tongueCursorTexSmall; int _tongueCursorSmallSize;

    /// <summary>游標換成舌頭（嘴巴、或選了 👅 時的胸部／乳頭／B）。</summary>
    void ApplyTongueCursor()
    {
        if (!GreenDotCursorEnabled || ClothDragActive) return;
        int size = Mathf.Max(12, (int)(HoverCursorSizeNow() * TongueCursorScale));
        if (_tongueCursorTexSmall == null || _tongueCursorSmallSize != size)
        {
            _tongueCursorTexSmall = TongueCursorTexture(size);
            if (_tongueCursorTexSmall == null) return;
            _tongueCursorSmallSize = size;
        }
        _hoverCursorKind = 3;
        _hoverCursorSize = HoverCursorSizeNow();   // 用未縮放的值比對，鏡頭縮放才重算
        Input.SetCustomMouseCursor(_tongueCursorTexSmall, Input.CursorShape.Arrow,
            new Vector2(_tongueCursorTexSmall.GetWidth() / 2f, _tongueCursorTexSmall.GetHeight() / 2f));
    }
}
