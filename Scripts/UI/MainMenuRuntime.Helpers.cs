using Godot;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// MainMenuRuntime 的共用輔助方法（UI 建構、座標轉換、資源載入、動畫等待）
///
/// 【核心：Unity → Godot 座標轉換】
///   Unity RectTransform：Y 軸朝上，anchorMin/Max 從左下角算
///   Godot Control：      Y 軸朝下，Anchor 從左上角算
///   本檔的 SetRT / Img / Txt 等方法會自動翻轉 Y，
///   因此所有 Unity 的座標常數可以原封不動照抄。
/// </summary>
public partial class MainMenuRuntime : Control
{
    // ═══════════════════════════════════════════════════════════
    //  座標轉換核心
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 套用 Unity RectTransform 的定位語意到 Godot Control
    /// 點錨點模式（aMin == aMax）：使用 pos（anchoredPosition）與 sd（sizeDelta）
    /// </summary>
    public static void ApplyPointAnchor(Control c, Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 sd)
    {
        // Unity 錨點 Y 從下往上，Godot 從上往下 → 翻轉
        float ax = aMin.X;
        float ay = 1f - aMin.Y;

        c.AnchorLeft   = ax;
        c.AnchorRight  = aMax.X;
        c.AnchorTop    = 1f - aMax.Y;
        c.AnchorBottom = ay;

        // Unity anchoredPosition 以 pivot(0.5,0.5) 為中心、Y 朝上
        // Godot 偏移以左上角為基準、Y 朝下 → pos.Y 取負
        float cx = pos.X;
        float cy = -pos.Y;

        c.OffsetLeft   = cx - sd.X * 0.5f;
        c.OffsetRight  = cx + sd.X * 0.5f;
        c.OffsetTop    = cy - sd.Y * 0.5f;
        c.OffsetBottom = cy + sd.Y * 0.5f;

        // 樞紐設在中心，讓縮放/旋轉行為與 Unity 一致
        c.PivotOffset = new Vector2(sd.X * 0.5f, sd.Y * 0.5f);
    }

    /// <summary>
    /// 套用 Unity RectTransform 的拉伸語意到 Godot Control
    /// 拉伸模式（aMin != aMax）：使用 offsetMin（左,下）與 offsetMax（右,上）
    /// </summary>
    public static void ApplyStretchAnchor(Control c, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
    {
        c.AnchorLeft   = aMin.X;
        c.AnchorRight  = aMax.X;
        // Y 翻轉：Unity 的 max(上) 對應 Godot 的 top
        c.AnchorTop    = 1f - aMax.Y;
        c.AnchorBottom = 1f - aMin.Y;

        c.OffsetLeft   = oMin.X;
        c.OffsetRight  = oMax.X;
        // Unity offsetMax.y 是相對上緣（負值代表內縮）→ Godot top 需取負
        c.OffsetTop    = -oMax.Y;
        c.OffsetBottom = -oMin.Y;
    }

    /// <summary>Unity SetRT 的等價方法（點錨點）</summary>
    public void SetRT(Control go, Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 sd)
        => ApplyPointAnchor(go, aMin, aMax, pos, sd);

    /// <summary>簡寫：建立 Vector2（對應 Unity 的 V2）</summary>
    public static Vector2 V2(float x, float y) => new Vector2(x, y);

    // ═══════════════════════════════════════════════════════════
    //  資源載入（Godot 需要完整副檔名，Unity 不需要）
    // ═══════════════════════════════════════════════════════════

    // 已載入資源快取，避免重複讀取
    private static readonly Dictionary<string, Texture2D> _texCache = new();

    /// <summary>
    /// 對應 Unity 的 Resources.Load&lt;Sprite&gt;("路徑")
    /// 會自動嘗試 .png / .jpg / .jpeg / .webp 各種副檔名
    /// </summary>
    public static Texture2D LoadTex(string relPath)
    {
        if (string.IsNullOrEmpty(relPath)) return null;
        if (_texCache.TryGetValue(relPath, out var cached)) return cached;

        string basePath = "res://Resources/" + relPath;
        string[] exts = { "", ".png", ".jpg", ".jpeg", ".webp" };

        foreach (var ext in exts)
        {
            string full = basePath + ext;
            if (ResourceLoader.Exists(full))
            {
                var tex = ResourceLoader.Load<Texture2D>(full);
                if (tex != null)
                {
                    _texCache[relPath] = tex;
                    return tex;
                }
            }
        }

        GD.PushWarning($"[MainMenu] 找不到圖片資源：{relPath}");
        _texCache[relPath] = null;
        return null;
    }

    /// <summary>載入音訊資源（.ogg / .wav / .mp3）</summary>
    public static AudioStream LoadBgm(string relPath) => LoadAudio(relPath);

    public static AudioStream LoadAudio(string relPath)
    {
        if (string.IsNullOrEmpty(relPath)) return null;

        string basePath = "res://Resources/" + relPath;
        string[] exts = { "", ".ogg", ".wav", ".mp3" };

        foreach (var ext in exts)
        {
            string full = basePath + ext;
            if (ResourceLoader.Exists(full))
            {
                var s = ResourceLoader.Load<AudioStream>(full);
                if (s != null) return s;
            }
        }

        GD.PushWarning($"[MainMenu] 找不到音訊資源：{relPath}");
        return null;
    }

    // ═══════════════════════════════════════════════════════════
    //  UI 建構輔助（對應 Unity 的 Img / Txt / Btn / Lbl）
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 建立純色圖塊（拉伸模式）— 對應 Unity 的 Img()
    /// 純色使用 ColorRect，比 TextureRect 更輕量
    /// </summary>
    public ColorRect Img(Node p, string n, Color c, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
    {
        var go = new ColorRect { Name = n, Color = c };
        go.MouseFilter = MouseFilterEnum.Ignore;
        p.AddChild(go);
        ApplyStretchAnchor(go, aMin, aMax, oMin, oMax);
        return go;
    }

    /// <summary>
    /// 建立圖片節點（點錨點模式）
    /// Unity 的 Image + sprite 對應 Godot 的 TextureRect
    /// </summary>
    public TextureRect ImgTex(Node p, string n, Texture2D tex, Vector2 aMin, Vector2 aMax,
        Vector2 pos, Vector2 sd, bool preserveAspect = false)
    {
        var go = new TextureRect
        {
            Name          = n,
            Texture       = tex,
            ExpandMode    = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode   = preserveAspect
                                ? TextureRect.StretchModeEnum.KeepAspectCentered
                                : TextureRect.StretchModeEnum.Scale,
            MouseFilter   = MouseFilterEnum.Ignore,
        };
        p.AddChild(go);
        ApplyPointAnchor(go, aMin, aMax, pos, sd);
        return go;
    }

    /// <summary>
    /// 建立文字（點錨點模式）— 對應 Unity 的 Txt() 完整版
    /// TextAnchor 由 UAlign 列舉對應
    /// </summary>
    public Label Txt(Node p, string n, string text, int size, Color c, UAlign align,
        Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 sd)
    {
        var lb = MakeLabel(n, text, size, c, align);
        p.AddChild(lb);
        ApplyPointAnchor(lb, aMin, aMax, pos, sd);
        return lb;
    }

    /// <summary>建立文字（不設定位置）— 對應 Unity 的 Txt() 簡版</summary>
    public Label Txt(Node p, string n, string text, int size, Color c, UAlign align)
    {
        var lb = MakeLabel(n, text, size, c, align);
        p.AddChild(lb);
        return lb;
    }

    /// <summary>建立 Label 並套用字體大小、顏色、對齊</summary>
    private static Label MakeLabel(string n, string text, int size, Color c, UAlign align)
    {
        var lb = new Label
        {
            Name        = n,
            Text        = text ?? "",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        lb.AddThemeFontSizeOverride("font_size", size);
        lb.AddThemeColorOverride("font_color", c);
        ApplyAlign(lb, align);
        return lb;
    }

    /// <summary>Unity TextAnchor → Godot 水平/垂直對齊</summary>
    public static void ApplyAlign(Label lb, UAlign a)
    {
        switch (a)
        {
            case UAlign.UpperLeft:
                lb.HorizontalAlignment = HorizontalAlignment.Left;
                lb.VerticalAlignment   = VerticalAlignment.Top; break;
            case UAlign.UpperCenter:
                lb.HorizontalAlignment = HorizontalAlignment.Center;
                lb.VerticalAlignment   = VerticalAlignment.Top; break;
            case UAlign.UpperRight:
                lb.HorizontalAlignment = HorizontalAlignment.Right;
                lb.VerticalAlignment   = VerticalAlignment.Top; break;
            case UAlign.MiddleLeft:
                lb.HorizontalAlignment = HorizontalAlignment.Left;
                lb.VerticalAlignment   = VerticalAlignment.Center; break;
            case UAlign.MiddleCenter:
                lb.HorizontalAlignment = HorizontalAlignment.Center;
                lb.VerticalAlignment   = VerticalAlignment.Center; break;
            case UAlign.MiddleRight:
                lb.HorizontalAlignment = HorizontalAlignment.Right;
                lb.VerticalAlignment   = VerticalAlignment.Center; break;
            case UAlign.LowerLeft:
                lb.HorizontalAlignment = HorizontalAlignment.Left;
                lb.VerticalAlignment   = VerticalAlignment.Bottom; break;
            case UAlign.LowerCenter:
                lb.HorizontalAlignment = HorizontalAlignment.Center;
                lb.VerticalAlignment   = VerticalAlignment.Bottom; break;
            case UAlign.LowerRight:
                lb.HorizontalAlignment = HorizontalAlignment.Right;
                lb.VerticalAlignment   = VerticalAlignment.Bottom; break;
        }
    }

    /// <summary>對應 Unity 的 Lbl()：橘黃色小標籤</summary>
    public void Lbl(Node p, string text, Vector2 pos)
    {
        Txt(p, text, text, 18, new Color(0.95f, 0.7f, 0.2f), UAlign.MiddleLeft,
            V2(0.5f, 0.5f), V2(0.5f, 0.5f), pos + V2(-20, 0), V2(600, 28));
    }

    /// <summary>
    /// 建立按鈕 — 對應 Unity 的 Btn()
    /// 使用共用白粉色原生樣式，保留 T 文字節點供既有程式調整
    /// </summary>
    public Button Btn(Node p, string n, string lbl, Color bg, Vector2 anc, Vector2 pos,
        float w = 360, float h = 96)
    {
        var btn = new Button
        {
            Name  = n,
            Text  = "",                            // 文字另用 Label 疊上，才能精準控制顏色
            Flat  = true,
        };
        p.AddChild(btn);
        ApplyPointAnchor(btn, anc, anc, pos, V2(w, h));

        UIStyle.ApplyButtonStyle(btn);

        // 文字層：撐滿按鈕、置中
        var tx = new Label
        {
            Name                 = "T",
            Text                 = lbl,
            HorizontalAlignment  = HorizontalAlignment.Center,
            VerticalAlignment    = VerticalAlignment.Center,
            AutowrapMode         = TextServer.AutowrapMode.Arbitrary,
            MouseFilter          = MouseFilterEnum.Ignore,
        };
        tx.AddThemeFontSizeOverride("font_size", 40);
        btn.AddChild(tx);
        // SetAnchorsPreset(FullRect) 在部分情況下無法依 Button 父節點正確撐開子節點，
        // 改用已驗證可靠的定點錨點＋明確尺寸（與按鈕本身相同 w×h），避免文字寬度塌陷成 1px。
        ApplyPointAnchor(tx, V2(0.5f, 0.5f), V2(0.5f, 0.5f), Vector2.Zero, V2(w, h));

        tx.AddThemeColorOverride("font_color", UIStyle.BtnTextColor);
        if (w >= 180 && h >= 46)
        {
            var sparkle = new Label { Text = "✦", MouseFilter = MouseFilterEnum.Ignore };
            sparkle.AddThemeColorOverride("font_color", new Color("d994b2"));
            sparkle.AddThemeFontSizeOverride("font_size", h >= 70 ? 20 : 13);
            btn.AddChild(sparkle);
            sparkle.Position = new Vector2(w - 30, 5);
            var heart = new HeartDecoration
            {
                Size = V2(h >= 70 ? 19 : 14, h >= 70 ? 19 : 14),
                Position = V2(14, h - (h >= 70 ? 28 : 22))
            };
            btn.AddChild(heart);
        }

        return btn;
    }

    /// <summary>取得按鈕上的文字節點（對應 Unity 的 btn.transform.Find("T")）</summary>
    public static Label BtnLabel(Button b) => b?.GetNodeOrNull<Label>("T");

    /// <summary>建立全螢幕勾選框 — 對應 Unity 的 MkToggle()</summary>
    public CheckBox MkToggle(Node p, Vector2 pos)
    {
        var tog = new CheckBox { Name = "FST", ButtonPressed = true, Text = "" };
        p.AddChild(tog);
        ApplyPointAnchor(tog, V2(0.5f, 0.5f), V2(0.5f, 0.5f), pos, V2(32, 32));

        Txt(p, "FSL", "全螢幕模式", 17, Colors.White, UAlign.MiddleLeft,
            V2(0.5f, 0.5f), V2(0.5f, 0.5f), pos + V2(90, 0), V2(200, 30));

        return tog;
    }

    /// <summary>建立音量滑桿列 — 對應 Unity 的 SlRow()</summary>
    public (HSlider, Label) SlRow(Node p, string lbl, Vector2 pos, float val)
    {
        Txt(p, lbl, lbl, 17, Colors.White, UAlign.MiddleLeft,
            V2(0.5f, 0.5f), V2(0.5f, 0.5f), pos + V2(-270, 0), V2(220, 28));

        var sl = new HSlider
        {
            Name     = lbl + "S",
            MinValue = 0,
            MaxValue = 1,
            Step     = 0.01,
            Value    = val,
        };
        p.AddChild(sl);
        ApplyPointAnchor(sl, V2(0.5f, 0.5f), V2(0.5f, 0.5f), pos + V2(55, 0), V2(260, 18));

        // 滑桿配色（對應 Unity 的深褐底 + 橘黃填充）
        var bgSb = new StyleBoxFlat { BgColor = new Color(0.2f, 0.1f, 0.15f) };
        var fgSb = new StyleBoxFlat { BgColor = new Color(0.85f, 0.5f, 0.15f) };
        sl.AddThemeStyleboxOverride("slider", bgSb);
        sl.AddThemeStyleboxOverride("grabber_area", fgSb);
        sl.AddThemeStyleboxOverride("grabber_area_highlight", fgSb);

        var vLbl = Txt(p, lbl + "V", Mathf.RoundToInt(val * 100) + "%", 16,
            new Color(1f, 0.85f, 0.4f), UAlign.MiddleLeft,
            V2(0.5f, 0.5f), V2(0.5f, 0.5f), pos + V2(200, 0), V2(60, 26));

        return (sl, vLbl);
    }

    /// <summary>建立左右切換列 — 對應 Unity 的 CycRow()</summary>
    public (Button, Label, Button) CycRow(Node p, Vector2 pos, string[] opts, int def = 0)
    {
        var prev = Btn(p, "P", "<", new Color(0.4f, 0.2f, 0.05f), V2(0.5f, 0.5f), pos + V2(-130, 0), 36, 36);
        var disp = Txt(p, "D", opts[def], 19, Colors.White, UAlign.MiddleCenter,
            V2(0.5f, 0.5f), V2(0.5f, 0.5f), pos, V2(200, 36));
        var next = Btn(p, "N", ">", new Color(0.4f, 0.2f, 0.05f), V2(0.5f, 0.5f), pos + V2(130, 0), 36, 36);
        return (prev, disp, next);
    }

    /// <summary>套用文字描邊（對應 Unity 的 Outline 元件）</summary>
    public static void SetOutline(Label lb, float size, Color color)
    {
        if (lb == null) return;
        lb.AddThemeConstantOverride("outline_size", Mathf.RoundToInt(size * 2f));
        lb.AddThemeColorOverride("font_outline_color", color);
    }

    // ═══════════════════════════════════════════════════════════
    //  程序化圓角矩形（對應 Unity 的 MakeRoundRect）
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 建立圓角矩形樣式 — 對應 Unity 的 MakeRoundRect()
    /// Unity 逐像素畫 Texture2D，Godot 直接用 StyleBoxFlat 更有效率
    /// </summary>
    public static StyleBoxFlat MakeRoundRect(int r, Color fill, Color border, int bw)
    {
        var sb = new StyleBoxFlat
        {
            BgColor                = fill,
            CornerRadiusTopLeft    = r,
            CornerRadiusTopRight   = r,
            CornerRadiusBottomLeft = r,
            CornerRadiusBottomRight= r,
            CornerDetail           = 8,
            AntiAliasing           = true,
            AntiAliasingSize       = 1.5f,
        };

        if (bw > 0)
        {
            sb.BorderColor       = border;
            sb.BorderWidthTop    = bw;
            sb.BorderWidthBottom = bw;
            sb.BorderWidthLeft   = bw;
            sb.BorderWidthRight  = bw;
        }

        return sb;
    }

    /// <summary>建立套用圓角樣式的面板節點</summary>
    public Panel RoundPanel(Node p, string n, int r, Color fill, Color border, int bw,
        Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 sd)
    {
        var panel = new Panel { Name = n, MouseFilter = MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride("panel", MakeRoundRect(r, fill, border, bw));
        p.AddChild(panel);
        ApplyPointAnchor(panel, aMin, aMax, pos, sd);
        return panel;
    }

    // ═══════════════════════════════════════════════════════════
    //  等待輔助（對應 Unity 協程的 yield return）
    // ═══════════════════════════════════════════════════════════

    /// <summary>對應 yield return new WaitForSeconds(s)</summary>
    public async Task Wait(float seconds, CancellationToken ct = default)
    {
        if (seconds <= 0f) { await NextFrame(ct); return; }
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>對應 yield return null（等一幀）</summary>
    public async Task NextFrame(CancellationToken ct = default)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>對應 yield return new WaitUntil(() => cond())</summary>
    public async Task WaitUntil(Func<bool> cond, CancellationToken ct = default)
    {
        while (!cond())
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            ct.ThrowIfCancellationRequested();
        }
    }

    /// <summary>對應 yield return new WaitWhile(() => cond())</summary>
    public async Task WaitWhile(Func<bool> cond, CancellationToken ct = default)
        => await WaitUntil(() => !cond(), ct);

    /// <summary>
    /// 依時間逐幀插值（對應 Unity 協程裡的 while(t &lt; dur) 迴圈）
    /// onStep 收到 0~1 的正規化進度
    /// </summary>
    public async Task Lerp(float duration, Action<float> onStep, CancellationToken ct = default)
    {
        if (duration <= 0f) { onStep(1f); return; }

        float t = 0f;
        while (t < duration)
        {
            t += (float)GetProcessDeltaTime();
            onStep(Mathf.Clamp(t / duration, 0f, 1f));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            ct.ThrowIfCancellationRequested();
        }
        onStep(1f);
    }

    // ═══════════════════════════════════════════════════════════
    //  節點操作輔助
    // ═══════════════════════════════════════════════════════════

    /// <summary>設定節點透明度（對應 Unity 的 img.color = new Color(r,g,b,a)）</summary>
    public static void SetAlpha(CanvasItem node, float a)
    {
        if (node == null || !IsInstanceValid(node)) return;
        var m = node.Modulate;
        m.A = a;
        node.Modulate = m;
    }

    /// <summary>取得節點透明度</summary>
    public static float GetAlpha(CanvasItem node)
        => (node != null && IsInstanceValid(node)) ? node.Modulate.A : 0f;

    /// <summary>
    /// 設定 Control 的縮放（以中心為樞紐，對應 Unity 的 transform.localScale）
    /// </summary>
    public static void SetScale(Control c, float s) => SetScale(c, new Vector2(s, s));

    /// <summary>設定 Control 的縮放（XY 分別指定）</summary>
    public static void SetScale(Control c, Vector2 s)
    {
        if (c == null || !IsInstanceValid(c)) return;
        c.PivotOffset = c.Size * 0.5f;   // 確保從中心縮放
        c.Scale = s;
    }

    /// <summary>
    /// 平移節點（對應 Unity 的 rt.anchoredPosition = pos）
    /// 自動翻轉 Y 軸
    /// </summary>
    public static void SetAnchoredPos(Control c, Vector2 unityPos, Vector2 size)
    {
        if (c == null || !IsInstanceValid(c)) return;
        float cx = unityPos.X;
        float cy = -unityPos.Y;
        c.OffsetLeft   = cx - size.X * 0.5f;
        c.OffsetRight  = cx + size.X * 0.5f;
        c.OffsetTop    = cy - size.Y * 0.5f;
        c.OffsetBottom = cy + size.Y * 0.5f;
    }

    /// <summary>用 Position 直接平移（不改動錨點），Y 自動翻轉</summary>
    public static void SetOffsetPos(Control c, Vector2 unityOffset)
    {
        if (c == null || !IsInstanceValid(c)) return;
        c.Position = new Vector2(unityOffset.X, -unityOffset.Y);
    }

    /// <summary>安全銷毀節點（對應 Unity 的 Destroy）</summary>
    public static void Kill(Node n)
    {
        if (n != null && IsInstanceValid(n)) n.QueueFree();
    }

    /// <summary>設定節點顯隱（對應 Unity 的 SetActive）</summary>
    public static void SetActive(CanvasItem n, bool v)
    {
        if (n != null && IsInstanceValid(n)) n.Visible = v;
    }

    // ═══════════════════════════════════════════════════════════
    //  音訊輔助（線性音量介面，讓淡入淡出邏輯與 Unity 一致）
    // ═══════════════════════════════════════════════════════════

    /// <summary>取得播放器線性音量（0~1）</summary>
    public static float GetVol(AudioStreamPlayer p)
    {
        if (p == null || !IsInstanceValid(p)) return 0f;
        return Mathf.DbToLinear(p.VolumeDb);
    }

    /// <summary>設定播放器線性音量（0~1），對應 Unity 的 source.volume</summary>
    public static void SetVol(AudioStreamPlayer p, float linear)
    {
        if (p == null || !IsInstanceValid(p)) return;
        p.VolumeDb = linear <= 0.0001f ? -80f : Mathf.LinearToDb(Mathf.Clamp(linear, 0f, 1f));
    }

    /// <summary>播放音效（對應 Unity 的 PlayOneShot）</summary>
    public void PlaySE(string audioPath)
    {
        var stream = LoadAudio(audioPath);
        if (stream == null || _seSource == null) return;
        _seSource.Stream = stream;
        _seSource.Play();
    }
}

/// <summary>Unity TextAnchor 的對應列舉（保持原始命名以便 1:1 照抄）</summary>
public enum UAlign
{
    UpperLeft, UpperCenter, UpperRight,
    MiddleLeft, MiddleCenter, MiddleRight,
    LowerLeft, LowerCenter, LowerRight,
}
