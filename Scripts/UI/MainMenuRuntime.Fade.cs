using Godot;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// MainMenuRuntime 第 6 段：畫面淡入淡出、背景切換、CG 圖層淡入淡出
/// （對應 Unity 原檔的 ScreenFade / FadeBGColor / FadeBG / FadeCG）
/// </summary>
public partial class MainMenuRuntime : Control
{
    // ═══════════════════════════════════════════════════════════
    //  全畫面淡入淡出
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 全畫面淡入／淡出遮罩
    /// toBlack = true 表示淡到指定顏色（預設黑），false 表示從該顏色淡回透明
    /// </summary>
    private async Task ScreenFade(bool toBlack, float duration, Color? color = null,
        CancellationToken ct = default)
    {
        if (_fadeImage == null || !IsInstanceValid(_fadeImage)) return;

        // 確保遮罩永遠在最上層（Unity: SetAsLastSibling）
        var parent = _fadeImage.GetParent();
        if (parent != null)
            parent.MoveChild(_fadeImage, parent.GetChildCount() - 1);

        Color c = color ?? Colors.Black;
        float startA = toBlack ? 0f : 1f;
        float endA   = toBlack ? 1f : 0f;

        // 底色先設好，只動 alpha
        _fadeImage.Color = new Color(c.R, c.G, c.B, startA);
        _fadeImage.Visible = true;

        await Lerp(FF(duration), p =>
        {
            float a = Mathf.Lerp(startA, endA, p);
            _fadeImage.Color = new Color(c.R, c.G, c.B, a);
        }, ct);

        _fadeImage.Color = new Color(c.R, c.G, c.B, endA);

        // 記錄目前是否為黑屏狀態，供其他流程判斷
        _screenBlack     = toBlack;
        _screenFadeColor = c;
    }

    // ═══════════════════════════════════════════════════════════
    //  背景純色淡入
    // ═══════════════════════════════════════════════════════════

    /// <summary>背景淡變成指定純色（完成後清空背景貼圖）</summary>
    private async Task FadeBGColor(Color targetColor, float duration = 0.6f,
        CancellationToken ct = default)
    {
        if (_bgImage == null || !IsInstanceValid(_bgImage)) return;

        Color startColor = _bgImage.Modulate;

        await Lerp(FF(duration), p =>
        {
            _bgImage.Modulate = startColor.Lerp(targetColor, p);
        }, ct);

        // 淡變完成，清掉貼圖只留純色
        _bgImage.Texture  = null;
        _bgImage.Modulate = targetColor;
    }

    // ═══════════════════════════════════════════════════════════
    //  背景交叉淡入
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 背景交叉淡入（新背景淡入蓋住舊背景，完成後把新背景移到 BG 層）
    /// </summary>
    private async Task FadeBG(Texture2D newTex, float duration = 0.6f,
        CancellationToken ct = default)
    {
        if (_bgImage == null || !IsInstanceValid(_bgImage)) return;
        if (_bgImage2 == null || !IsInstanceValid(_bgImage2)) return;

        // 第二層先擺上新圖、透明
        _bgImage2.Texture  = newTex;
        _bgImage2.Modulate = new Color(1f, 1f, 1f, 0f);
        _bgImage2.Visible  = true;

        await Lerp(FF(duration), p =>
        {
            _bgImage2.Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(p, 0f, 1f));
        }, ct);

        _bgImage2.Modulate = Colors.White;

        // 淡入完成，把新背景搬到主圖層，清空第二圖層
        _bgImage.Texture   = newTex;
        _bgImage.Modulate  = Colors.White;
        _bgImage2.Modulate = new Color(1f, 1f, 1f, 0f);
    }

    /// <summary>背景交叉淡入（用圖片名稱，會自動從 Resources 載入）</summary>
    private async Task FadeBG(string bgName, float duration = 0.6f,
        CancellationToken ct = default)
    {
        var tex = LoadTex($"Backgrounds/{bgName}");
        if (tex == null)
        {
            GD.PushWarning($"[Dialogue] 找不到背景：{bgName}");
            return;
        }
        await FadeBG(tex, duration, ct);
    }

    // ═══════════════════════════════════════════════════════════
    //  CG 圖層淡入淡出
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// CG 圖層淡入／淡出
    /// cgName 支援 "圖名, 幀數" 格式（幀數 / 60 = 秒數），僅 fadeIn 時解析
    /// </summary>
    private async Task FadeCG(int layer, string cgName, bool fadeIn, float duration = 0.6f,
        CancellationToken ct = default, DialogueSystem.CGLayerCmd? initCmd = null)
    {
        if (layer < 0 || layer >= _cgImages.Length) return;
        if (_cgImages[layer] == null || !IsInstanceValid(_cgImages[layer])) return;

        var img = _cgImages[layer];

        // 解析 "圖名, 幀數" 格式（僅 fadeIn 時）
        string actualName = cgName;
        bool keepTransform = false;
        if (fadeIn && !string.IsNullOrEmpty(cgName) && cgName.Contains(","))
        {
            var parts = cgName.Split(',');
            actualName = parts[0].Trim();
            // 逗號後的欄位：數字 = 幀數；"keep" = 只換圖，不重設縮放與位置
            for (int pi = 1; pi < parts.Length; pi++)
            {
                string tk = parts[pi].Trim();
                if (string.Equals(tk, "keep", System.StringComparison.OrdinalIgnoreCase)) keepTransform = true;
                else if (float.TryParse(tk, out float frames)) duration = frames / 60f;
            }
        }

        if (fadeIn)
        {
            var tex = LoadTex($"CG/{actualName}");
            if (tex == null)
            {
                GD.PushWarning($"[Dialogue] 找不到 CG：{actualName}");
                return;
            }

            img.Texture = tex;
            img.Visible = true;
            img.Name    = "CG_Layer_" + layer;

            // 確保 CG 永遠在對話框之下：放在對話框底板前面
            if (_dialogueBox != null && IsInstanceValid(_dialogueBox))
            {
                var parent = img.GetParent();
                if (parent != null && _dialogueBox.GetParent() == parent)
                {
                    int dlgIdx = _dialogueBox.GetIndex();
                    parent.MoveChild(img, Mathf.Max(0, dlgIdx - 1));
                }
            }

            // 重設位移/縮放（確保新 CG 從預設狀態開始）；圖名帶 keep 時保留目前的縮放與位置
            if (!keepTransform) ResetCGTransform(img);

            // 淡入開始前先決定好大小與位置：寫成 "數值, 0"（0 幀＝立即）的縮放/位移，
            // 在第一幀就套用，圖片一出現就是最終大小，不會先以預設大小閃一下
            if (initCmd.HasValue)
            {
                var ic = initCmd.Value;
                var sP = DialogueSystem.ParseValueWithDuration(ic.scale);
                var xP = DialogueSystem.ParseValueWithDuration(ic.x);
                var yP = DialogueSystem.ParseValueWithDuration(ic.y);
                if (!string.IsNullOrEmpty(ic.scale) && sP.durationSec <= 0f && sP.value > 0f)
                    SetScale(img, sP.value);
                bool ix = !string.IsNullOrEmpty(ic.x) && xP.durationSec <= 0f;
                bool iy = !string.IsNullOrEmpty(ic.y) && yP.durationSec <= 0f;
                if (ix || iy)
                    ApplyCGOffset(img, ix ? xP.value : img.OffsetLeft, iy ? yP.value : -img.OffsetTop);
            }
        }
        else
        {
            // 淡出：如果 CG 沒在顯示就跳過
            if (!img.Visible) return;
        }

        // duration <= 0 → 瞬間切換，不做淡入淡出
        if (duration <= 0f)
        {
            img.Modulate = new Color(1f, 1f, 1f, fadeIn ? 1f : 0f);
            if (!fadeIn) ClearCGLayer(img);
            return;
        }

        float start = fadeIn ? 0f : 1f;
        float end   = fadeIn ? 1f : 0f;

        await Lerp(FF(duration), p =>
        {
            float a = Mathf.Lerp(start, end, p);
            img.Modulate = new Color(1f, 1f, 1f, a);
        }, ct);

        img.Modulate = new Color(1f, 1f, 1f, end);

        if (!fadeIn) ClearCGLayer(img);
    }

    /// <summary>重設 CG 圖層的縮放與位移到預設值</summary>
    private static void ResetCGTransform(Control img)
    {
        if (img == null || !IsInstanceValid(img)) return;
        img.Scale        = Vector2.One;
        img.Position     = Vector2.Zero;
        img.OffsetLeft   = 0;
        img.OffsetTop    = 0;
        img.OffsetRight  = 0;
        img.OffsetBottom = 0;
    }

    /// <summary>清空 CG 圖層（淡出結束後呼叫）</summary>
    private static void ClearCGLayer(TextureRect img)
    {
        if (img == null || !IsInstanceValid(img)) return;
        img.Texture = null;
        ResetCGTransform(img);
        img.Visible = false;
    }
}
