using Godot;

/// <summary>
/// TrainingRoomRuntime — 衛生紙模式：
/// 身上／床上還有精液時出現「🧻 衛生紙」按鈕，按下進入擦拭模式（滑鼠變衛生紙）。
/// 按住左鍵在精液上擦，精液會慢慢變淡消失；全部擦乾淨之前，「離開房間」會被擋下。
/// </summary>
public partial class TrainingRoomRuntime
{
    [ExportGroup("衛生紙")]
    [Export] public float TissueRadius   = 70f;    // 擦拭半徑（螢幕 px）
    [Export] public float TissueWipeRate = 1.6f;   // 每秒擦掉多少透明度（1＝0.6 秒擦掉）
    [Export] public float TissueSize = 110f;           // 衛生紙大小（螢幕 px）
    [Export] public float TissueUsePerBlob = 0.07f;    // 每擦掉一整團精液，衛生紙用掉多少（1＝揉成一團）
    [Export] public float TissueBallWipeMul = 0.45f;   // 揉成一團後擦拭速度剩多少倍（右鍵可換新的一張）
    [Export] public string[] TissueNeedLines = { "（還沒擦乾淨…先用衛生紙擦乾淨再走。）", "（精液還留著，要擦乾淨才行。）" };
    [Export] public string[] TissueDoneLines = { "（擦乾淨了。）" };

    bool   _tissueOn;
    Button _tissueBtn;
    ColorRect _tissueCursor;
    ShaderMaterial _tissueMat;
    float  _tissueUsed;   // 0＝新的一張（方形、有折痕）→ 1＝揉成一團
    bool   _tissueHadDebris;

    const string TissueShaderCode = @"shader_type canvas_item;
uniform float used = 0.0;
uniform float seed = 0.0;
float h21(vec2 p) { p = fract(p * vec2(123.34, 456.21) + seed); p += dot(p, p + 45.32); return fract(p.x * p.y); }
float vn(vec2 p) {
    vec2 i = floor(p), f = fract(p); f = f * f * (3.0 - 2.0 * f);
    return mix(mix(h21(i), h21(i + vec2(1, 0)), f.x), mix(h21(i + vec2(0, 1)), h21(i + vec2(1, 1)), f.x), f.y);
}
float fbm(vec2 p) { float a = 0.5, s = 0.0; for (int i = 0; i < 4; i++) { s += a * vn(p); p *= 2.07; a *= 0.5; } return s; }
void fragment() {
    vec2 p = (UV - 0.5) * 2.0;
    float c = cos(-0.2), sn = sin(-0.2);
    p = vec2(c * p.x - sn * p.y, sn * p.x + c * p.y);            // 稍微歪一點
    float u = smoothstep(0.0, 1.0, used);
    float rsq = max(abs(p.x), abs(p.y));                          // 方形
    float rci = length(p) * 0.92;                                  // 圓團
    float r = mix(rsq, rci, smoothstep(0.25, 0.95, u));
    float size = mix(0.82, 0.55, u);
    float edgeN = (fbm(p * 3.0 + 7.0) - 0.5) * (0.04 + 0.30 * u);  // 越皺邊緣越不規則
    float m = 1.0 - smoothstep(size - 0.025, size + 0.01, r + edgeN);
    float freq = 3.0 + 9.0 * u;
    float n1 = fbm(p * freq + 1.3);
    float n2 = fbm(p * freq * 1.9 + 9.1);
    float crease = 1.0 - smoothstep(0.0, 0.06 + 0.03 * u, abs(n1 - 0.5));   // 褶皺線
    crease = max(crease, (1.0 - smoothstep(0.0, 0.05, abs(n2 - 0.5))) * u);
    float fold = (1.0 - smoothstep(0.0, 0.025, abs(p.x))) * 0.5 + (1.0 - smoothstep(0.0, 0.025, abs(p.y))) * 0.35;   // 折兩折的痕跡
    fold *= (1.0 - u);
    vec3 col = vec3(0.98, 0.97, 0.96);
    col *= 1.0 - crease * (0.10 + 0.22 * u);
    col *= 1.0 - fold * 0.16;
    col *= 0.93 + 0.10 * n2;                                       // 紙面起伏
    col = mix(col, vec3(0.93, 0.90, 0.80), clamp(used * 0.7, 0.0, 0.7) * (0.4 + 0.6 * n1));   // 越用越髒（精液）
    float shade = smoothstep(size * 0.35, size, r) * 0.18 + u * 0.22 * (1.0 - smoothstep(0.0, size, length(p - vec2(-0.2, -0.25))));
    col *= 1.0 - shade * (0.5 + 0.5 * u);
    float rim = smoothstep(size - 0.10, size, r + edgeN) * m;
    col = mix(col, vec3(0.78, 0.76, 0.76), rim * 0.55);          // 邊緣一圈淡灰，像紙的厚度
    COLOR = vec4(col, m);
}
";

    static bool DebrisAlive(Control d) => d != null && GodotObject.IsInstanceValid(d) && !d.IsQueuedForDeletion() && d.Visible;

    int TissueRemaining()
    {
        int n = 0;
        foreach (var d in _ejacDebris) if (DebrisAlive(d) && d.Modulate.A > 0.05f && d.SelfModulate.A > 0.05f) n++;
        if (JijiHasBlood()) n++;
        if (JijiHasSemen()) n++;
        return n;
    }

    /// <summary>離開房間前呼叫：還有精液沒擦乾淨就擋下並進入衛生紙模式。回傳 true＝可以離開。</summary>
    bool TissueAllowLeave()
    {
        if (TissueRemaining() <= 0) return true;
        if (!_tissueOn) _tissueUsed = 0f;
        _tissueOn = true;
        SayMale(TissueNeedLines);
        return false;
    }

    bool JijiHasBlood() => _jijiBlood > 0.04f && JijiVisibleForTissue();
    bool JijiHasSemen() => _jijiSemenShown && _semAlpha > 0.05f && JijiVisibleForTissue();
    bool JijiVisibleForTissue() => _jijiPhase > 0 && Layers.TryGetValue("雞雞", out var j) && j != null && j.Visible;

    /// <summary>雞雞內容範圍（螢幕座標）；滑鼠在上面擦就能擦掉雞雞上的血和精液。</summary>
    bool MouseOnJiji(Vector2 m)
    {
        if (!Layers.TryGetValue("雞雞", out var j) || j == null || !j.Visible) return false;
        var r = LayerContentRect("雞雞");
        if (r == null) return false;
        var t = j.GetGlobalTransform();
        Vector2 a = t * r.Value.Position, b = t * (r.Value.Position + new Vector2(r.Value.Size.X, 0f));
        Vector2 c2 = t * (r.Value.Position + r.Value.Size), d2 = t * (r.Value.Position + new Vector2(0f, r.Value.Size.Y));
        float minX = Mathf.Min(Mathf.Min(a.X, b.X), Mathf.Min(c2.X, d2.X)), maxX = Mathf.Max(Mathf.Max(a.X, b.X), Mathf.Max(c2.X, d2.X));
        float minY = Mathf.Min(Mathf.Min(a.Y, b.Y), Mathf.Min(c2.Y, d2.Y)), maxY = Mathf.Max(Mathf.Max(a.Y, b.Y), Mathf.Max(c2.Y, d2.Y));
        return new Rect2(minX - TissueRadius * 0.4f, minY - TissueRadius * 0.4f, maxX - minX + TissueRadius * 0.8f, maxY - minY + TissueRadius * 0.8f).HasPoint(m);
    }

    bool TissueOverUi(Vector2 p)
    {
        if (_tissueBtn != null && IsInstanceValid(_tissueBtn) && _tissueBtn.GetGlobalRect().HasPoint(p)) return true;
        var leave = UiRoot?.GetNodeOrNull<Control>("LeavePanel");
        return leave != null && leave.GetGlobalRect().HasPoint(p);
    }

    /// <summary>_Input 最前面呼叫：擦拭模式下左鍵不要傳給其他互動（按鈕除外）。</summary>
    bool TissueInput(InputEvent e)
    {
        if (!_tissueOn) return false;
        if (e is InputEventMouseButton rb && rb.ButtonIndex == MouseButton.Right && rb.Pressed && !TissueOverUi(rb.Position))
        {   // 右鍵：換一張新的衛生紙
            _tissueUsed = 0f; _tissueMat?.SetShaderParameter("seed", GD.Randf() * 10f);
            return true;
        }
        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && !TissueOverUi(mb.Position)) return true;
        return false;
    }

    void UpdateTissue(float dt)
    {
        var parent = UiRoot;
        if (parent == null) return;
        if (_tissueBtn == null || !IsInstanceValid(_tissueBtn))
        {
            _tissueBtn = MakeCornerBtn(parent, "BtnTissue", "🧻 衛生紙", -556f, -416f);
            _tissueBtn.Visible = false;
            _tissueBtn.Pressed += () => { _tissueOn = !_tissueOn; if (_tissueOn) _tissueUsed = 0f; };
        }
        if (_tissueCursor == null || !IsInstanceValid(_tissueCursor))
        {
            _tissueMat = new ShaderMaterial { Shader = new Shader { Code = TissueShaderCode } };
            _tissueMat.SetShaderParameter("seed", GD.Randf() * 10f);
            _tissueCursor = new ColorRect { Name = "TissueCursor", Color = Colors.White, MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = 4000, Visible = false, Material = _tissueMat };
            parent.AddChild(_tissueCursor);
        }
        _tissueCursor.Size = new Vector2(TissueSize, TissueSize);

        int remain = TissueRemaining();
        bool any = remain > 0;
        if (any) _tissueHadDebris = true;
        _tissueBtn.Visible = any || _tissueOn;
        _tissueBtn.Text = _tissueOn ? "🧻 收起" : "🧻 衛生紙";

        if (!any && _tissueOn)
        {   // 擦乾淨了
            _tissueOn = false;
            HideJijiSemen();
            if (_tissueHadDebris) { _tissueHadDebris = false; SayMale(TissueDoneLines); }
        }

        _tissueCursor.Visible = _tissueOn;
        if (!_tissueOn) return;

        Vector2 m = GetViewport().GetMousePosition();
        _tissueCursor.Position = m - _tissueCursor.Size * 0.5f;
        _tissueMat.SetShaderParameter("used", _tissueUsed);
        if (!Input.IsMouseButtonPressed(MouseButton.Left) || TissueOverUi(m)) return;

        if (MouseOnJiji(m))
        {   // 擦雞雞：血和精液慢慢變淡，擦完就消失
            float k = TissueWipeRate * Mathf.Lerp(1f, TissueBallWipeMul, _tissueUsed) * dt * 0.5f;
            if (JijiHasBlood()) { float b0 = _jijiBlood; _jijiBlood = Mathf.Max(0f, _jijiBlood - k); _tissueUsed = Mathf.Min(1f, _tissueUsed + (b0 - _jijiBlood) * TissueUsePerBlob); if (_jijiBlood < 0.04f) { _jijiBlood = 0f; _jijiBloodBlur = 0f; } }
            if (JijiHasSemen()) { float s0 = _semAlpha; _semAlpha = Mathf.Max(0f, _semAlpha - k); _tissueUsed = Mathf.Min(1f, _tissueUsed + (s0 - _semAlpha) * TissueUsePerBlob); if (_semAlpha < 0.05f) HideJijiSemen(); }
        }

        foreach (var d in _ejacDebris.ToArray())
        {
            if (!DebrisAlive(d)) continue;
            Vector2 c = d.GetGlobalTransform() * (d.Size * 0.5f);
            if (c.DistanceTo(m) > TissueRadius) continue;
            var mod = d.Modulate;
            float dA_ = TissueWipeRate * Mathf.Lerp(1f, TissueBallWipeMul, _tissueUsed) * dt;   // 揉成團之後擦得比較慢
            mod.A -= dA_;
            _tissueUsed = Mathf.Min(1f, _tissueUsed + dA_ * TissueUsePerBlob);
            d.Modulate = mod;
            if (mod.A <= 0.05f) { _ejacDebris.Remove(d); d.QueueFree(); }
        }
    }
}
