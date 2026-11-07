using Godot;
using System.Collections.Generic;

/// <summary>
/// TrainingRoomRuntime — 操作優化＋小穴呼吸擴張。
///
/// 右鍵的規則（使用者要求）：
///   • 右鍵點在「雞雞」或「小穴」上 → 雞雞退一階（插入中→拔回入口→退回→收起）；
///     雞雞沒出現、正在摸 B 時 → 停止摸 B
///   • 右鍵點在「胸部」上 → 停止那一邊的摸胸
///   • 右鍵點在空白位置 → 鏡頭放大時可以按住右鍵拖動畫面（跟中鍵一樣）
///   之前是畫面任何地方按右鍵都會讓雞雞退一階，容易誤觸。
///
/// 判定是否點在某個圖層上：用貼圖本身的透明度（alpha），點在圖片實際畫出來的地方才算，
/// 不是整張 2000x3200 的透明畫布。
///
/// 小穴呼吸擴張：m腿/小穴 跟著呼吸左右微微撐開，上面撐比較多、下面比較少（曲線），幅度很小。
/// </summary>
public partial class TrainingRoomRuntime
{
    // ── Export：小穴呼吸擴張 ─────────────────────────────────────────
    [Export] public float PussyBreathExpand      = 0.025f;  // 吸氣到最大時，小穴上緣左右撐開的比例（0.025＝2.5%）
    [Export] public float PussyBreathCurvePow    = 1.6f;    // 上多下少的曲線：越大越集中在上面

    // ════════════════════════════════════════════════════════════════
    //  圖層 alpha 命中判定
    // ════════════════════════════════════════════════════════════════

    readonly Dictionary<string, Image> _hitImageCache = new();

    /// <summary>場景座標 worldPos 是否點在圖層實際畫出來（不透明）的地方。</summary>
    bool IsPointOnLayer(string layerName, Vector2 worldPos, float alphaThreshold = 0.1f)
    {
        if (!Layers.TryGetValue(layerName, out var c) || c == null || !c.IsVisibleInTree()) return false;
        if (c is not TextureRect tr || tr.Texture == null) return false;
        if (!_hitImageCache.TryGetValue(layerName, out var img) || img == null)
        {
            img = tr.Texture.GetImage();
            if (img == null) return false;
            if (img.IsCompressed()) img.Decompress();
            _hitImageCache[layerName] = img;
        }
        Vector2 local = c.GetGlobalTransform().AffineInverse() * worldPos;
        // TextureRect 大小可能跟貼圖不同（Scale 拉伸），換算成貼圖像素
        float sx = img.GetWidth() / Mathf.Max(c.Size.X, 1f), sy = img.GetHeight() / Mathf.Max(c.Size.Y, 1f);
        int px = (int)(local.X * sx), py = (int)(local.Y * sy);
        if (px < 0 || py < 0 || px >= img.GetWidth() || py >= img.GetHeight()) return false;
        return img.GetPixel(px, py).A > alphaThreshold;
    }

    static readonly string[] _pussyHitLayers = { "m腿/小穴", "腿/小穴", "插入", "開1", "洞" };

    /// <summary>右鍵點在 B 綠點或摸 B 的手附近也算（一般腿的 B 綠點不一定壓在小穴貼圖的不透明像素上，
    /// 之前只認小穴貼圖，所以一般腿右鍵 B 取消不了）。</summary>
    bool IsOnBTouchTarget(Vector2 w)
    {
        const float m = 24f;
        if (_hotspotRTs != null && _hotspotRTs.Length > 4 && _hotspotRTs[4] != null
            && _hotspotRTs[4].GetGlobalRect().Grow(m).HasPoint(w)) return true;
        if (_bTouchHand != null && IsInstanceValid(_bTouchHand)
            && _bTouchHand.GetGlobalRect().Grow(m).HasPoint(w)) return true;
        return false;
    }

    bool IsPointOnPussy(Vector2 worldPos)
    {
        foreach (var n in _pussyHitLayers) if (IsPointOnLayer(n, worldPos)) return true;
        return false;
    }

    // ════════════════════════════════════════════════════════════════
    //  右鍵
    // ════════════════════════════════════════════════════════════════

    /// <summary>右鍵按下：依點到的東西決定停止哪個動作；空白處（放大時）開始拖動畫面。有處理就回傳 true。</summary>
    bool HandleRightClick(Vector2 screenPos)
    {
        if (IsPointerOverUi()) return false;
        Vector2 w = ScreenToWorld(screenPos);

        bool onJiji  = _jijiPhase > 0 && IsMouseOverJiji(w);
        bool onPussy = IsPointOnPussy(w);

        // 雞雞／小穴
        if (_jijiPhase > 0 && (onJiji || onPussy)) { OnJijiRightClick(); return true; }
        if (_bTouchRunning && (onPussy || IsOnBTouchTarget(w))) { CancelBTouch(); return true; }

        // 摸乳頭：右鍵點乳頭取消
        if (_nipL.On && IsPointOnLayer("奶頭2", w)) { CancelNipple("NL"); return true; }
        if (_nipR.On && IsPointOnLayer("奶頭1", w)) { CancelNipple("NR"); return true; }
        // 右鍵點胸部也可以取消摸乳頭（胸2＝左邊那顆／奶頭2、胸1＝右邊那顆／奶頭1）
        if (_nipL.On && IsPointOnLayer("胸2", w)) { CancelNipple("NL"); return true; }
        if (_nipR.On && IsPointOnLayer("胸1", w)) { CancelNipple("NR"); return true; }
        // 舔：右鍵點正在舔的地方停止
        if (_lickOn && IsOnLickTarget(w)) { StopLick(); return true; }
        // 舌頭：右鍵點嘴巴停止
        if (_tongueOn && OnMouthWorld(w)) { StopTongue(); return true; }

        // 摸頭：右鍵點頭取消
        if (_patOn && InPatZoneWorld(w)) { CancelPat(); return true; }

        // 胸部（左手摸的是「胸2」、右手摸的是「胸1」，見 CancelHandTouchAnim 的對調說明）
        if (_touchAnimRunningLeft && IsPointOnLayer("胸2", w))  { CancelHandTouchAnim(true);  return true; }
        if (_touchAnimRunningRight && IsPointOnLayer("胸1", w)) { CancelHandTouchAnim(false); return true; }

        // 空白處：放大時拖動畫面
        if (_camZoomTarget > 1.001f)
        {
            _camPanning = true;
            _camPanLast = screenPos;
            return true;
        }
        return false;
    }

    // ════════════════════════════════════════════════════════════════
    //  小穴呼吸擴張（m腿/小穴）
    // ════════════════════════════════════════════════════════════════

    [Export] public bool PussyBindToLegs = true;   // 小穴與腿交接的左右邊緣，跟著旁邊那條腿一起呼吸／擺動（不露縫）
    ShaderMaterial _pussyBreathMat;

    /// <summary>小穴左右邊緣（跟腿的交接處）相對小穴本身的位移：用腿此刻實際的變換（含擺動旋轉）算出來。</summary>
    void PussyLegBind(Control pussy, out Vector2 sdL, out Vector2 sdR)
    {
        Vector2 jL = new Vector2(885f, 1430f), jR = new Vector2(1094f, 1430f);
        sdL = Vector2.Zero; sdR = Vector2.Zero;
        if (!PussyBindToLegs) return;
        if (Layers.TryGetValue("m腿/腿2", out var legL) && legL != null) sdL = legL.GetTransform() * jL - pussy.GetTransform() * jL;
        if (Layers.TryGetValue("m腿/腿1", out var legR) && legR != null) sdR = legR.GetTransform() * jR - pussy.GetTransform() * jR;
    }

    void UpdatePussyBreath()
    {
        if (!Layers.TryGetValue("m腿/小穴", out var c) || c == null) return;
        if (_pussyBreathMat == null)
        {
            if (c.Material is ShaderMaterial exM)
            {   // 小穴已經掛了「中心撐開」材質（BuildUI.cs）：不覆蓋，只把腿綁定的位移餵給它
                if (_centerStretchShader != null && exM.Shader == _centerStretchShader) { Vector2 l_, r_; PussyLegBind(c, out l_, out r_); exM.SetShaderParameter("sd_l", l_); exM.SetShaderParameter("sd_r", r_); }
                return;
            }
            if (c.Material != null) return;   // 已經有別的 shader 就不覆蓋
            var sh = new Shader
            {
                Code = @"shader_type canvas_item;
uniform float amount = 0.0;     // 目前上緣撐開比例
uniform float curve_pow = 1.6;
uniform vec2 sd_l = vec2(0.0);   // 左腿（腿2）在交接處相對小穴的位移（px）：小穴左緣貼著它動
uniform vec2 sd_r = vec2(0.0);   // 右腿（腿1）在交接處相對小穴的位移（px）
varying vec4 vcol;
void vertex() { vcol = COLOR; }
void fragment() {
    vec2 px = TEXTURE_PIXEL_SIZE;
    // 小穴_M 內容：x 860~1098（中心 979）、y 1321~1579
    float cx = 979.0 * px.x;
    float t  = clamp((UV.y - 1321.0 * px.y) / (258.0 * px.y), 0.0, 1.0);   // 0＝上緣，1＝下緣
    float w  = amount * pow(1.0 - t, curve_pow);                            // 上面撐多、下面撐少
    vec2 uv  = vec2(cx + (UV.x - cx) / (1.0 + w), UV.y);
    float xp_ = UV.x / px.x;
    vec2 sd_ = sd_l * (1.0 - smoothstep(885.0, 960.0, xp_)) + sd_r * smoothstep(1020.0, 1094.0, xp_);   // 左右邊緣各自跟著旁邊的腿動，中間不動（呼吸時不露縫）
    COLOR = texture(TEXTURE, uv - sd_ * px) * vcol;
}"
            };
            _pussyBreathMat = new ShaderMaterial { Shader = sh };
            c.Material = _pussyBreathMat;
        }
        float inhale = (Mathf.Sin(_breathTime * Mathf.Pi * 2f) + 1f) * 0.5f;   // 跟呼吸同步 0~1
        _pussyBreathMat.SetShaderParameter("amount", PussyBreathExpand * inhale);
        _pussyBreathMat.SetShaderParameter("curve_pow", PussyBreathCurvePow);
        // 綁定：小穴左右邊緣（跟腿的交接線）跟著旁邊那條腿一起動／轉，呼吸時不會露出縫
        PussyLegBind(c, out var sdL, out var sdR);
        _pussyBreathMat.SetShaderParameter("sd_l", sdL);
        _pussyBreathMat.SetShaderParameter("sd_r", sdR);
    }
}
