using Godot;
using System.Collections.Generic;

/// <summary>
/// 捏臉（全年齡／R18 兩個模式都能用）：在臉頰上按住左鍵拖動，臉上所有圖層（臉、表情、腮紅…）
/// 在按下的位置附近被一起往拖動方向拉扯，放手後彈性回彈。
/// 同時也放「全年齡鎖」：全年齡模式下只剩摸頭、蓋被子、捏臉、鏡頭，其他互動全部關掉（綠點也隱藏）。
/// Godot 搜尋關鍵字：PinchEnabled／AllAgesRestrict
/// </summary>
public partial class TrainingRoomRuntime
{
    [Export] public bool PinchEnabled = true;
    [Export] public bool PinchDebugLog = false;
    [Export(PropertyHint.Range, "20,500,1")] public float PinchRadiusPx = 170f;             // 被拉動的範圍半徑
    [Export(PropertyHint.Range, "0,300,1")] public float PinchMaxXPx = 5.5f;                 // 左右最多能拉開多遠
    [Export(PropertyHint.Range, "0,300,1")] public float PinchMaxYPx = 6f;                 // 上下最多能拉開多遠（要比左右小）
    [Export(PropertyHint.Range, "0.1,2,0.05")] public float PinchFollow = 0.18f;             // 滑鼠移動多少，臉就被拉多少（倍率）
    [Export(PropertyHint.Range, "20,1200,5")] public float PinchSpring = 260f;              // 回彈的力道
    [Export(PropertyHint.Range, "1,40,0.5")] public float PinchDamp = 9f;                   // 回彈的阻尼（越小晃越久）
    [Export(PropertyHint.Range, "0,0.9,0.01")] public float PinchCheekInnerFrac = 0f;        // 臉中線附近這個比例（嘴巴）不能捏，只能捏兩側臉頰
    [Export(PropertyHint.Range, "0,0.9,0.01")] public float PinchTopFrac = 0.3f;            // 臉上緣往下這個比例以上不能捏（眼睛／額頭）
    [Export(PropertyHint.Range, "0,300,1")] public float PinchBottomExtraPx = 5f;            // 臉下緣（下巴）再往下多這麼多也能捏
    [Export(PropertyHint.Range, "0,200,1")] public float PinchEyeMarginPx = 35f;            // 眼睛周圍這麼遠以內不能捏
    [Export(PropertyHint.Range, "16,256,1")] public int PinchCursorSize = 72;                // 捏臉游標（pinch_hand.png）大小
    [Export] public Vector2 PinchCursorHotspot = new Vector2(0.3f, 0.3f);                    // 游標熱點（圖上的比例位置，預設在拇指／食指捏的地方）
    [Export(PropertyHint.Range, "0,200,1")] public float PinchTopExtraPx = 10f;             // 判定上緣再往上擴張多少 px
    [Export(PropertyHint.Range, "0,60,1")] public float PinchMouthMarginPx = 0f;             // 只有嘴巴綠點（含外擴這麼多 px）不能捏
    [Export(PropertyHint.Range, "0,200,1")] public float PinchSideInsetPx = 0f;             // 臉左右邊緣往內這麼多 px 以外（側邊頭髮蓋住的地方）不給捏
    [Export(PropertyHint.Range, "0,1,0.01")] public float PinchCenterMinK = 0.2f;            // 抓在臉正中間時幅度只剩這個比例，越往兩側越接近 1
    [Export] public Vector2 MLegExtraOffset = Vector2.Zero;                                 // M 腿小穴細節圖層的位置微調（px，Play 中可調，往下＝+Y）
    [Export(PropertyHint.Range, "-100,100,1")] public float MLegHemLeftExtraPx = 35f;   // 左邊的設定值比右邊多幾 px（左右漸層；數值越大＝線越低＝拉得越少，要左邊拉更多就填負數）
    [Export] public bool MLegHemAutoLift = true;                                             // M 腿打開時，衣服下半低於「內褲頭頂＋下面這個距離」的部分自動拉上來
    [Export(PropertyHint.Range, "-100,200,1")] public float MLegHemAutoLiftPx = 65f;         // 內褲頭頂往下多少 px 以內可以留著，再低就拉上來
    [Export(PropertyHint.Range, "0,10,0.1")] public float PinchHoldPx = 3f;                  // 長按捏：按住不動時，臉往外被捏出去多少 px（要小於 PinchMaxXPx）
    [Export(PropertyHint.Range, "0,3,0.05")] public float PinchHoldDelay = 0.3f;             // 按住多久之後才開始長按捏（秒）
    [Export(PropertyHint.Range, "0.05,3,0.05")] public float PinchHoldRamp = 0.5f;           // 長按捏從 0 到滿要多久（秒）
    [Export(PropertyHint.Range, "0.5,3,0.05")] public float PinchOuterGain = 1.4f;           // 臉兩側的效果倍率（正中間不變，仍是 PinchCenterMinK）
    public const bool PinchDebugDraw = false;                                             // 把可以捏的範圍用綠點畫出來（預設開著，調完請關掉）
    /// <summary>全年齡模式只開放摸頭／蓋被子／捏臉（可關掉方便在 R18 下測試全年齡）。</summary>
    [Export] public bool AllAgesRestrict = true;
    [Export(PropertyHint.Range, "0.05,1,0.01")] public float AllAgesHemStretchFrac = 0.3f;   // 全年齡：衣服最底下這個比例會往下拉長
    [Export(PropertyHint.Range, "0,600,1")] public float R18HemStretchPx = 0f;                 // R18：衣服最底下（同上比例）往下拉長多少 px（0＝不拉長，維持原樣）
    [Export(PropertyHint.Range, "0,600,1")] public float AllAgesHemStretchPx = 300f;          // 全年齡：往下拉長多少 px

    static readonly string[] PinchLayerNames =
    {
        "臉的陰影","臉","表情/眼白","表情/眼白2","表情/眼球1","表情/眼球2","表情/眼皮","表情/眼皮2",
        "表情/睫毛1","表情/睫毛2","表情/下睫毛1","表情/下睫毛2","眼角1","眼角2","表情/鼻子","表情/嘴巴","表情/腮紅",
        "笑","笑2","小嘴","吐舌","張嘴1","張嘴2","斜動嘴","抖動嘴","小嘴吐舌","小嘴巴","眉毛1","眉毛2",
    };
    const string PinchShaderCode = @"
shader_type canvas_item;
uniform vec2 pc = vec2(0.5, 0.5);
uniform vec2 pd = vec2(0.0, 0.0);
uniform vec2 pr = vec2(0.1, 0.1);
varying vec4 vc;
void vertex() { vc = COLOR; }
void fragment() {
    float f = 1.0 - smoothstep(0.0, 1.0, length((UV - pc) / pr));
    f *= f;
    COLOR = texture(TEXTURE, UV - pd * f) * vc;
}
";
    Shader _pinchShader;
    bool _pinchCursorOn; ImageTexture _pinchCursorTex; int _pinchCursorTexSize;
    void PinchCursor(bool on)
    {
        if (on == _pinchCursorOn) return;
        _pinchCursorOn = on;
        if (!on) { ApplyHandCursor(); return; }
        if (_pinchCursorTex == null || _pinchCursorTexSize != PinchCursorSize)
        {
            var tex = ResourceHelper.LoadTexAny("Sprites/pinch_hand");
            if (tex == null) { _pinchCursorOn = false; return; }
            var img = tex.GetImage();
            if (img.IsCompressed()) img.Decompress();
            img.Resize(PinchCursorSize, PinchCursorSize, Image.Interpolation.Lanczos);
            _pinchCursorTex = ImageTexture.CreateFromImage(img); _pinchCursorTexSize = PinchCursorSize;
        }
        Input.SetCustomMouseCursor(_pinchCursorTex, Input.CursorShape.Arrow, PinchCursorHotspot * PinchCursorSize);
    }
    readonly Dictionary<string, ShaderMaterial> _pinchMats = new();
    bool _pinchDrag, _pinchActive; bool _pinchSwallowUp; int _pinchSide; float _pinchK = 1f;   // 按下時在臉的左邊（-1）還是右邊（+1）
    Vector2 _pinchGrab, _pinchOff, _pinchVel;
    Rect2 _pinchFaceUV = new Rect2(0f, 0f, 0f, 0f); bool _pinchScanned;

    bool AllAgesLocked => AllAgesRestrict && PlayerDataManager.Instance?.R18ContentEnabled != true;

    void PinchScan()
    {
        if (_pinchScanned) return;
        if (!Layers.TryGetValue("臉", out var fl) || fl is not TextureRect tr || tr.Texture == null) return;
        try
        {
            var im = tr.Texture.GetImage();
            if (im.IsCompressed()) im.Decompress();
            int w = im.GetWidth(), h = im.GetHeight(), x0 = w, x1 = 0, y0 = h, y1 = 0;
            for (int y = 0; y < h; y += 4)
                for (int x = 0; x < w; x += 4)
                    if (im.GetPixel(x, y).A > 0.3f) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
            if (x1 > x0 && y1 > y0) { _pinchFaceUV = new Rect2(x0 / (float)w, y0 / (float)h, (x1 - x0) / (float)w, (y1 - y0) / (float)h); _pinchScanned = true; }
        }
        catch { }
    }

    // 判定一律用「螢幕座標」配合圖層自己的 GetGlobalTransformWithCanvas 換算，不再經過 ScreenToWorld，
    // 這樣不管鏡頭縮放／平移到哪裡，滑鼠在畫面上的位置跟圖層的對應都是對的。
    Vector2 PinchLocal(CanvasItem c, Vector2 screen) => c.GetGlobalTransformWithCanvas().AffineInverse() * screen;

    bool PinchOnLayer(string layerName, Vector2 screen)
    {
        if (!Layers.TryGetValue(layerName, out var c) || c == null || !c.IsVisibleInTree() || c is not TextureRect tr || tr.Texture == null) return false;
        if (!_hitImageCache.TryGetValue(layerName, out var img) || img == null)
        {
            img = tr.Texture.GetImage();
            if (img == null) return false;
            if (img.IsCompressed()) img.Decompress();
            _hitImageCache[layerName] = img;
        }
        Vector2 local = PinchLocal(c, screen);
        float sx = img.GetWidth() / Mathf.Max(c.Size.X, 1f), sy = img.GetHeight() / Mathf.Max(c.Size.Y, 1f);
        int px = (int)(local.X * sx), py = (int)(local.Y * sy);
        if (px < 0 || py < 0 || px >= img.GetWidth() || py >= img.GetHeight()) return false;
        return img.GetPixel(px, py).A > 0.1f;
    }

    bool PinchHit(Vector2 screenPos)
    {
        if (!PinchEnabled) return PinchNo("PinchEnabled 關閉");
        if (_handMode != HandMode.None) return PinchNo("左下角選了手模式");
        if (_tongueTool) return PinchNo("選了舌頭工具");
        if (_jijiPhase == 3) return PinchNo("插入中");
        if (IsPointerOverUi()) return PinchNo("滑鼠在 UI／綠點按鈕上：" + GetViewport().GuiGetHoveredControl()?.GetPath());
        return PinchHitScreen(screenPos);
    }

    readonly Dictionary<string, Rect2> _pinchEyeBox = new();   // 眼睛圖層內容外框（圖層本地 px）
    static Rect2 PinchScanBox(TextureRect tr)
    {
        try
        {
            var im = tr.Texture.GetImage();
            if (im.IsCompressed()) im.Decompress();
            int w = im.GetWidth(), h = im.GetHeight(), x0 = w, x1 = -1, y0 = h, y1 = -1;
            for (int y = 0; y < h; y += 4)
                for (int x = 0; x < w; x += 4)
                    if (im.GetPixel(x, y).A > 0.2f) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
            if (x1 < 0) return new Rect2();
            float sx = tr.Size.X / Mathf.Max(1, w), sy = tr.Size.Y / Mathf.Max(1, h);
            return new Rect2(x0 * sx, y0 * sy, (x1 - x0) * sx, (y1 - y0) * sy);
        }
        catch { return new Rect2(); }
    }

    string _pinchWhy = "";
    bool PinchNo(string why) { _pinchWhy = why; return false; }

    bool PinchHitScreen(Vector2 sp)
    {
        PinchScan();
        if (!_pinchScanned || !Layers.TryGetValue("臉", out var face) || face == null || !face.IsVisibleInTree()) return PinchNo("還沒掃到臉／臉看不到");
        float z_ = Mathf.Max(0.01f, _camZoom);   // 下面的 px 數值都是「場景 px」，換成螢幕 px 要乘縮放
        if (_hotspotRTs != null && _hotspotRTs.Length > 1 && _hotspotRTs[1] != null && _hotspotRTs[1].IsVisibleInTree()
            && _hotspotRTs[1].GetGlobalRect().Grow(PinchMouthMarginPx * z_).HasPoint(sp)) return PinchNo("嘴巴綠點");
        bool onFace_ = PinchOnLayer("臉", sp);
        for (float dy_ = Mathf.Min(PinchBottomExtraPx, 20f); !onFace_ && dy_ > 0.01f && dy_ <= PinchBottomExtraPx + 0.5f; dy_ += Mathf.Max(1f, Mathf.Min(PinchBottomExtraPx, 20f)))   // 下巴下面多判定一段
            onFace_ = PinchOnLayer("臉", sp - new Vector2(0f, dy_ * z_));
        if (!onFace_) return PinchNo("不在臉上（含下巴下面延伸）");
        // 眼睛周圍不給捏：用眼睛圖層內容的外框（跟閉眼／眨眼無關）往外擴 PinchEyeMarginPx
        foreach (var en_ in new[] { "表情/眼白", "表情/眼白2", "表情/眼球1", "表情/眼球2", "表情/眼皮", "表情/眼皮2", "表情/睫毛1", "表情/睫毛2", "表情/下睫毛1", "表情/下睫毛2", "眼角1", "眼角2" })
        {
            if (!Layers.TryGetValue(en_, out var el_) || el_ == null || el_ is not TextureRect etr_ || etr_.Texture == null) continue;
            if (!_pinchEyeBox.TryGetValue(en_, out var box_)) { box_ = PinchScanBox(etr_); _pinchEyeBox[en_] = box_; }
            if (box_.Size.X <= 0f) continue;
            if (box_.Grow(PinchEyeMarginPx).HasPoint(PinchLocal(el_, sp))) return PinchNo("眼睛附近：" + en_);
        }
        var lp = PinchLocal(face, sp);
        float u = lp.X / Mathf.Max(1f, face.Size.X), v = lp.Y / Mathf.Max(1f, face.Size.Y);
        float insetU_ = PinchSideInsetPx / Mathf.Max(1f, face.Size.X);
        if (u < _pinchFaceUV.Position.X + insetU_ || u > _pinchFaceUV.End.X - insetU_) return PinchNo("超過臉的左右邊界（側邊頭髮）");
        float cx = _pinchFaceUV.Position.X + _pinchFaceUV.Size.X * 0.5f, hw = _pinchFaceUV.Size.X * 0.5f;
        if (Mathf.Abs(u - cx) < hw * PinchCheekInnerFrac) return PinchNo("臉中線（嘴巴）附近");
        if (v < _pinchFaceUV.Position.Y + _pinchFaceUV.Size.Y * PinchTopFrac - PinchTopExtraPx / Mathf.Max(1f, face.Size.Y)) return PinchNo("太上面");
        return true;
    }

    /// <summary>由 _Input 呼叫：回傳 true＝這個事件被捏臉吃掉。</summary>
    bool PinchInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
        {
            if (mb.Pressed)
            {
                if (!PinchHit(mb.Position)) { if (PinchDebugLog && PinchOnLayer("臉", mb.Position)) GD.Print($"[PinchDbg] 按在臉上但不能捏：{_pinchWhy}"); return false; }
                if (Layers.TryGetValue("臉", out var fc_) && fc_ != null)
                {
                    float uu_ = PinchLocal(fc_, mb.Position).X / Mathf.Max(1f, fc_.Size.X), cx_ = _pinchFaceUV.Position.X + _pinchFaceUV.Size.X * 0.5f;
                    _pinchSide = uu_ < cx_ ? -1 : 1;
                    float t_ = Mathf.Clamp(Mathf.Abs(uu_ - cx_) / Mathf.Max(0.001f, _pinchFaceUV.Size.X * 0.5f), 0f, 1f);
                    _pinchK = Mathf.Lerp(PinchCenterMinK, PinchOuterGain, t_ * t_ * (3f - 2f * t_));   // 越中間越小
                }
                PinchCursor(true);
                _pinchPeak = 0f; _pinchDrag = true; _pinchActive = true; _pinchPressT = 0f; _pinchDragD = Vector2.Zero; _pinchGrab = mb.Position;   // 螢幕座標 _pinchOff = Vector2.Zero; _pinchVel = Vector2.Zero;
                if (PinchDebugLog) GD.Print($"[PinchDbg] 按下臉頰 screen={_pinchGrab} 縮放={_camZoom:F2}");
                return true;
            }
            if (_pinchDrag) { _pinchDrag = false; BratOnPinchRelease(_pinchPeak); PinchCursor(PinchHit(mb.Position)); return true; }
            if (_pinchSwallowUp) { _pinchSwallowUp = false; return true; }   // 到上限放手後，吃掉這次的放開
        }
        else if (e is InputEventMouseMotion mm && _pinchDrag)
        {
            _pinchDragD = (mm.Position - _pinchGrab) / Mathf.Max(0.01f, _camZoom) * PinchFollow * _pinchK;   // 換成場景 px（拖動的部分）
            PinchApplyOff();
            return true;
        }
        return false;
    }

    Vector2 _pinchDragD; float _pinchPressT; float _pinchPeak;

    /// <summary>拖動的量＋長按的量 → 實際位移（左邊只往左、右邊只往右；左右到上限就放手；上下軟上限）。</summary>
    void PinchApplyOff()
    {
        Vector2 d = _pinchDragD;
        float hold = Mathf.Clamp((_pinchPressT - PinchHoldDelay) / Mathf.Max(0.01f, PinchHoldRamp), 0f, 1f);
        hold = hold * hold * (3f - 2f * hold);
        d.X = _pinchSide < 0 ? Mathf.Min(d.X, 0f) : Mathf.Max(d.X, 0f);   // 左邊只能往左捏、右邊只能往右捏
        d.X += _pinchSide * PinchHoldPx * hold * _pinchK;                    // 長按：不用拖，按住不放臉也會慢慢被捏出去
        _pinchPeak = Mathf.Max(_pinchPeak, Mathf.Clamp(Mathf.Abs(d.X) / Mathf.Max(0.01f, PinchMaxXPx), 0f, 1f));
        if (Mathf.Abs(d.X) >= PinchMaxXPx)
        {   // 左右拉到上限：超過就放手（臉彈回去），這次按住不再捏
            d.X = Mathf.Sign(d.X) * PinchMaxXPx;
            _pinchOff = new Vector2(d.X, _pinchOff.Y);
            _pinchDrag = false; _pinchSwallowUp = true; BratOnPinchRelease(1f);
            if (PinchDebugLog) GD.Print("[PinchDbg] 拉到左右上限，放手");
            return;
        }
        float ry_ = Mathf.Max(0.01f, PinchMaxYPx);
        _pinchOff = new Vector2(d.X, ry_ * Mathf.Tanh(d.Y / ry_));
    }

    int _pinchDbgLast = -1; Control _pinchDbgCtl; readonly List<Vector2> _pinchDbgPts = new(); float _pinchDbgT; Rect2 _pinchDbgFace;
    void PinchDebugUpdate(float dt)
    {
        if (!PinchDebugDraw || !Layers.TryGetValue("臉", out var face) || face == null || !face.IsVisibleInTree())
        { if (_pinchDbgCtl != null) _pinchDbgCtl.Visible = false; return; }
        if (_pinchDbgCtl == null || !IsInstanceValid(_pinchDbgCtl))
        {
            _pinchDbgCtl = new Control { Name = "PinchDebug", MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = 4000 };
            face.AddChild(_pinchDbgCtl);
            _pinchDbgCtl.Draw += () =>
            {
                var inv = face.GetGlobalTransformWithCanvas().AffineInverse();   // 螢幕座標 → 臉圖層本地
                foreach (var p in _pinchDbgPts) _pinchDbgCtl.DrawRect(new Rect2(inv * p - new Vector2(4f, 4f), new Vector2(8f, 8f)), new Color(0.2f, 1f, 0.3f, 0.55f));
                _pinchDbgCtl.DrawRect(new Rect2(inv * _pinchDbgFace.Position, inv * _pinchDbgFace.End - inv * _pinchDbgFace.Position), new Color(1f, 0.9f, 0.2f, 0.9f), false, 2f);   // 黃框＝臉的範圍
            };
        }
        if (_pinchDbgCtl.GetParent() != face) { _pinchDbgCtl.GetParent()?.RemoveChild(_pinchDbgCtl); face.AddChild(_pinchDbgCtl); }
        _pinchDbgCtl.Visible = true;
        _pinchDbgT -= dt;
        if (_pinchDbgT > 0f) return;
        _pinchDbgT = 0.25f;
        PinchScan();
        _pinchDbgPts.Clear();
        if (!_pinchScanned) return;
        var xf = face.GetGlobalTransformWithCanvas();
        Vector2 sz = face.Size;
        Vector2 a = xf * new Vector2(_pinchFaceUV.Position.X * sz.X, _pinchFaceUV.Position.Y * sz.Y);
        Vector2 b = xf * new Vector2(_pinchFaceUV.End.X * sz.X, _pinchFaceUV.End.Y * sz.Y);
        _pinchDbgFace = new Rect2(a, b - a);
        float z_ = Mathf.Max(0.01f, _camZoom), y1 = b.Y + PinchBottomExtraPx * z_, stp = 12f * z_;
        for (float y = a.Y; y <= y1; y += stp)
            for (float x = a.X; x <= b.X; x += stp)
                if (PinchHitScreen(new Vector2(x, y))) _pinchDbgPts.Add(new Vector2(x, y));
        _pinchDbgCtl.QueueRedraw();
    }

    float _pinchHoldT;
    void PinchCursorFrame()
    {   // 每幀確認：滑鼠在可以捏的臉頰上（或正在捏）就維持捏的手，別的系統換掉游標也會被蓋回來
        bool hit_ = !_pinchDrag && PinchEnabled && !Input.IsMouseButtonPressed(MouseButton.Left) && PinchHit(GetViewport().GetMousePosition());
        if (hit_) _pinchHoldT = 0.2f; else _pinchHoldT -= GetProcessDeltaTime() > 0 ? (float)GetProcessDeltaTime() : 0.016f;
        bool want = _pinchDrag || hit_ || (_pinchHoldT > 0f && PinchEnabled && !Input.IsMouseButtonPressed(MouseButton.Left));   // 離開判定 0.2 秒內還維持，不閃
        if (want)
        {
            if (_pinchCursorOn) { _pinchCursorOn = false; }   // 強制重設，讓下面重新套一次
            PinchCursor(true);
        }
        else PinchCursor(false);
    }

    Vector2 _mlegExtraBase; bool _mlegExtraBaseSet;
    void MLegHemLift()
    {   // M 腿打開：衣服下擺的邊緣如果低於內褲頭頂（＋容許距離），就把下擺往上拉到那條線（只往上拉，不會往下放）
        if (!MLegHemAutoLift || !_mLegOpen || _pantyTopV < 0f || _hemDrag || _midDrag || _topDrag != 0) return;
        for (int i = 0; i < 2; i++)   // 0＝左、1＝右：左邊的設定值多 MLegHemLeftExtraPx（漸層）
        {
            float need = ClothHemEndV - (_pantyTopV + (MLegHemAutoLiftPx + (i == 0 ? MLegHemLeftExtraPx : 0f)) / 3200f);   // 下擺要被拉起的高度（V）
            if (need <= 0f) continue;
            need = Mathf.Min(need, ClothHemPullMax);
            if (_hemTargetA[i] < need) _hemTargetA[i] = need;
        }
    }

    void PinchProcess(float dt)
    {
        BratProcess(dt);   // 雌小鬼互動（Brat.cs）
        MLegHemLift();
        if (Layers.ContainsKey("m腿/小穴細節") && _layerBasePos.TryGetValue("m腿/小穴細節", out var mb0_))
        {
            if (!_mlegExtraBaseSet) { _mlegExtraBase = mb0_; _mlegExtraBaseSet = true; }
            _layerBasePos["m腿/小穴細節"] = _mlegExtraBase + MLegExtraOffset;
        }
        PinchCursorFrame();
        PinchDebugUpdate(dt);
        if (_hotspotRTs != null)
        {
            // 鼻子的綠點沒有功能：永遠隱藏、不擋滑鼠（不然會擋住捏臉）
            if (_hotspotRTs.Length > 0 && _hotspotRTs[0] != null)
            {
                _hotspotRTs[0].Visible = false; _hotspotRTs[0].MouseFilter = Control.MouseFilterEnum.Ignore;
                if (_hotspotImgs != null && _hotspotImgs[0] != null) _hotspotImgs[0].Visible = false;
                if (_hotspotTexts != null && _hotspotTexts[0] != null) _hotspotTexts[0].Visible = false;
            }
            // 全年齡鎖：綠點按鈕全部停用（F1 還是可以顯示／隱藏綠點的圖，只是點不了）
            if (AllAgesLocked)
                for (int i = 1; i < _hotspotRTs.Length; i++)
                    if (_hotspotRTs[i] != null)
                    {   // 按鈕停用（點不了、不擋滑鼠），綠點的圖還是照 F1 顯示／隱藏
                        _hotspotRTs[i].Visible = true;
                        if (_hotspotRTs[i].GetNodeOrNull<Button>("Btn") is Button hb_) { hb_.Disabled = true; hb_.MouseFilter = Control.MouseFilterEnum.Ignore; }
                    }
        }
        if (_pinchDrag) { _pinchPressT += dt; PinchApplyOff(); }
        if (!_pinchActive) return;
        if (!_pinchDrag)
        {   // 放手後彈性回彈
            _pinchVel += -_pinchOff * PinchSpring * dt;
            _pinchVel *= Mathf.Exp(-PinchDamp * dt);
            _pinchOff += _pinchVel * dt;
            if (_pinchOff.Length() < 0.15f && _pinchVel.Length() < 1f) { _pinchOff = Vector2.Zero; _pinchActive = false; }
        }
        _pinchShader ??= new Shader { Code = PinchShaderCode };
        foreach (var name in PinchLayerNames)
        {
            if (!Layers.TryGetValue(name, out var c) || c == null) continue;
            if (!_pinchMats.TryGetValue(name, out var m))
            {
                if (c.Material != null) continue;   // 這層已經有別的材質：不碰
                m = new ShaderMaterial { Shader = _pinchShader };
                c.Material = m; _pinchMats[name] = m;
            }
            Vector2 sz = new Vector2(Mathf.Max(1f, c.Size.X), Mathf.Max(1f, c.Size.Y));
            var inv = c.GetGlobalTransformWithCanvas().AffineInverse();   // 螢幕 → 圖層本地
            Vector2 pc = inv * _pinchGrab / sz;
            Vector2 pd = (inv * (_pinchGrab + _pinchOff * _camZoom) - inv * _pinchGrab) / sz;
            m.SetShaderParameter("pc", pc);
            m.SetShaderParameter("pd", pd);
            m.SetShaderParameter("pr", new Vector2(PinchRadiusPx, PinchRadiusPx) / sz);
        }
        if (!_pinchActive)
            foreach (var kv in _pinchMats) kv.Value.SetShaderParameter("pd", Vector2.Zero);
    }
}
