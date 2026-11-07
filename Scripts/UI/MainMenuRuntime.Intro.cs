using Godot;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public partial class MainMenuRuntime : Control
{
    // ═══════════════════════════════════════════════════════════
    //  Part2 專用的小型建構輔助
    //  （對應 Unity 裡「new GameObject + RectTransform 全螢幕拉伸」的樣板）
    // ═══════════════════════════════════════════════════════════

    /// <summary>建立一個鋪滿父節點的純色遮罩（對應 Unity 的白屏 / 黑屏 Image）</summary>
    private ColorRect MakeIntroFlash(Node parent, string name, Color color)
    {
        var rect = new ColorRect
        {
            Name        = name,
            Color       = color,
            MouseFilter = MouseFilterEnum.Ignore,   // 對應 raycastTarget = false
        };
        parent.AddChild(rect);
        ApplyStretchAnchor(rect, V2(0f, 0f), V2(1f, 1f), V2(0f, 0f), V2(0f, 0f));
        // 對應 transform.SetAsLastSibling()
        parent.MoveChild(rect, parent.GetChildCount() - 1);
        return rect;
    }

    /// <summary>建立一個鋪滿父節點的圖片層（對應 Unity 的全螢幕 Image 圖層）</summary>
    private TextureRect MakeIntroTexLayer(Node parent, string name, Texture2D tex, bool preserveAspect = false)
    {
        var layer = new TextureRect
        {
            Name        = name,
            Texture     = tex,
            ExpandMode  = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = preserveAspect
                              ? TextureRect.StretchModeEnum.KeepAspectCentered
                              : TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,   // 對應 raycastTarget = false
        };
        parent.AddChild(layer);
        ApplyStretchAnchor(layer, V2(0f, 0f), V2(1f, 1f), V2(0f, 0f), V2(0f, 0f));
        return layer;
    }

    /// <summary>
    /// 對應 Unity 的 rt.offsetMin = rt.offsetMax = off
    /// （全螢幕拉伸的圖層整體平移，Y 由 ApplyStretchAnchor 自動翻轉）
    /// </summary>
    private static void SetStretchOffset(Control c, Vector2 off)
    {
        if (c == null || !IsInstanceValid(c)) return;
        ApplyStretchAnchor(c, V2(0f, 0f), V2(1f, 1f), off, off);
    }

    /// <summary>遞迴收集子孫節點中的某型別（對應 Unity 的 GetComponentsInChildren）</summary>
    private static void CollectDescendants<T>(Node root, List<T> outList) where T : class
    {
        foreach (var child in root.GetChildren())
        {
            if (child is T typed) outList.Add(typed);
            CollectDescendants(child, outList);
        }
    }

    // ── Logo開場 ─────────────────────────────────────────────────

    async Task LogoIntro(CancellationToken ct = default)
    {
        // Logo 出現的這段本來就要等：先在背景把調教室的貼圖／場景預載，之後進調教室的黑屏就短很多
        foreach (var relPath in TrainingRoomRuntime.GetPreloadSpritePaths())
            ResourceHelper.PreloadTexAny(relPath);
        ResourceLoader.LoadThreadedRequest("res://Scenes/TrainingRoom.tscn");
        SetActive(_logoPanel, true);
        SetActive(_mainPanel, false);

        // 取得 Logo 面板內的圖片和文字，用於淡入淡出
        var logoImages = new List<CanvasItem>();
        var logoTexts  = new List<Label>();
        CollectDescendants(_logoPanel, logoImages);
        CollectDescendants(_logoPanel, logoTexts);
        // Label 也是 CanvasItem，要從圖片清單裡剔除，避免重複套用透明度
        logoImages.RemoveAll(n => n is Label);

        // 初始全透明
        foreach (var img in logoImages)
        {
            // 背景面板保持全黑不動
            if (img.Name == "LogoPanel") continue;
            SetAlpha(img, 0f);
        }
        foreach (var txt in logoTexts)
            SetAlpha(txt, 0f);

        // ── Logo 淡入（1秒）──
        float dur = 1f;
        await Lerp(dur, p =>
        {
            float a = p;
            foreach (var img in logoImages)
            {
                if (img.Name == "LogoPanel") continue;
                SetAlpha(img, a);
            }
            foreach (var txt in logoTexts)
                SetAlpha(txt, a * 0.45f);
        }, ct);

        // 停留一下
        await Wait(1.5f, ct);

        // ── Logo 淡出（1秒）──
        await Lerp(dur, p =>
        {
            float a = 1f - p;
            foreach (var img in logoImages)
            {
                if (img.Name == "LogoPanel") continue;
                SetAlpha(img, a);
            }
            foreach (var txt in logoTexts)
                SetAlpha(txt, a * 0.45f);
        }, ct);

        // ── 白屏閃光 → 標題畫面 ──
        // 建立白色遮罩
        var flashRect = MakeIntroFlash(_logoPanel, "WhiteFlash", new Color(1f, 1f, 1f, 0f));

        // 白屏快速亮起（0.15秒）
        float flashInDur = 0.15f;
        await Lerp(flashInDur, p => flashRect.Color = new Color(1f, 1f, 1f, p), ct);
        flashRect.Color = new Color(1f, 1f, 1f, 1f);

        // 切換到主選單面板（在白屏下面）
        SetActive(_logoPanel, false);
        SetActive(_mainPanel, true);

        // 將白色遮罩移到主選單上
        flashRect.GetParent().RemoveChild(flashRect);
        _mainPanel.AddChild(flashRect);
        ApplyStretchAnchor(flashRect, V2(0f, 0f), V2(1f, 1f), V2(0f, 0f), V2(0f, 0f));
        _mainPanel.MoveChild(flashRect, _mainPanel.GetChildCount() - 1);

        // 播放主選單 BGM
        var opClip = LoadBgm("Audio/bgm/meimei_op");
        SetupOpBgm(opClip);   // 循環＋後半音量補償（OpBgm.cs）
        if (opClip != null && _bgmSource != null)
        {
            _bgmSource.Stream = opClip;
            _bgmFading = true;   // 標記 BGM 淡入中，防止 ApplyVolumes 覆蓋
            SetVol(_bgmSource, 0f);
            _bgmSource.Play();
            _bgmSource.Seek(Mathf.Min(BgmStartTime, (float)opClip.GetLength()));
            _ = FadeInBGM(BgmFadeInDur, ct);
            // 定時淡出（如果設定了）
            if (BgmFadeOutAt > 0f)
                _ = BgmScheduledFadeOut(ct);
        }

        // 先隱藏按鈕
        if (_menuBtnContainer != null) SetActive(_menuBtnContainer, false);
        if (_titleGO != null) SetActive(_titleGO, false);

        // 取得主選單背景引用（Godot 裡 Image 與 RectTransform 是同一個節點）
        var mainBg = _mainPanel.GetNodeOrNull<TextureRect>("BG/BGTex");
        // 開場動畫用 SetOffsetPos/SetScale 操作（模擬 Unity RectTransform 的定點錨點語意），
        // 但 BGTex 建立時是「全螢幕拉伸」錨點（0,0~1,1），兩者混用會讓 Offset 被不對稱拉扯、
        // 矩形跑掉甚至塌陷到看不見。先轉成定點錨點＋固定 1920x1080 大小，動畫才會正確。
        if (mainBg != null)
            ApplyPointAnchor(mainBg, V2(0.5f, 0.5f), V2(0.5f, 0.5f), Vector2.Zero, V2(RefWidth, RefHeight));

        // 足夠寬度的原生文字按鈕，避免子標籤溢出與描邊重疊。
        var skipBtn = new Button { Name = "BtnSkipIntro", Text = "SKIP ›" };
        _mainPanel.AddChild(skipBtn);
        UIStyle.ApplyButtonStyle(skipBtn);
        skipBtn.AddThemeFontSizeOverride("font_size", 24);
        ApplyPointAnchor(skipBtn, V2(1f, 1f), V2(1f, 1f), V2(-110f, -56f), V2(164f, 56f));
        // 對應 Unity 的 overrideSorting + sortingOrder = 999
        skipBtn.ZIndex = 999;
        _mainPanel.MoveChild(skipBtn, _mainPanel.GetChildCount() - 1);

        bool _skipIntroClicked = false;
        skipBtn.Pressed += () =>
        {
            _skipIntroClicked = true;
            Kill(skipBtn); // 按下後立刻消失
        };

        // ════════════════════════════════════════════════════════
        //  場景1: bg_PlayerRoom — 放大偏左，往右平移
        // ════════════════════════════════════════════════════════
        if (mainBg != null)
        {
            var s1Tex = LoadTex("Backgrounds/bg_PlayerRoom");
            if (s1Tex != null) { mainBg.Texture = s1Tex; mainBg.Modulate = Colors.White; }

            // 設定放大 + 起始位移
            SetScale(mainBg, Scene1Scale);
            SetOffsetPos(mainBg, Scene1Start);
        }

        // 場景1 平移 + 白屏淡出同時進行（前2秒閃光淡出）
        {
            float flashOutDur = 2f;
            float t = 0f;
            while (t < Scene1Duration && !_skipIntroClicked)
            {
                t += (float)GetProcessDeltaTime();
                float progress = Mathf.Clamp(t / Scene1Duration, 0f, 1f);
                if (mainBg != null)
                    SetOffsetPos(mainBg, Scene1Start.Lerp(Scene1End, progress));
                // 閃光在前2秒內淡出
                if (t <= flashOutDur)
                    flashRect.Color = new Color(1f, 1f, 1f, 1f - Mathf.Clamp(t / flashOutDur, 0f, 1f));
                await NextFrame(ct);
            }
            Kill(flashRect);
        }

        // ════════════════════════════════════════════════════════
        //  第二次白屏閃光（場景1 → 場景2）
        //  閃光淡出時場景2已經在移動
        // ════════════════════════════════════════════════════════
        if (!_skipIntroClicked)
        {
            var f2Rect = MakeIntroFlash(_mainPanel, "WhiteFlash2", new Color(1f, 1f, 1f, 0f));

            // 閃光亮起（0.15秒）
            await Lerp(0.15f, p => f2Rect.Color = new Color(1f, 1f, 1f, p), ct);
            f2Rect.Color = Colors.White;

            // 在白屏下切換為場景2背景
            if (mainBg != null)
            {
                var s2Tex = LoadTex("Backgrounds/bg_LinaiRoom(day)");
                if (s2Tex != null) { mainBg.Texture = s2Tex; mainBg.Modulate = Colors.White; }
                SetScale(mainBg, Scene2Scale);
                SetOffsetPos(mainBg, Scene2Start);
            }

            // 場景2平移 + 閃光淡出同時進行（前2秒閃光淡出）
            float flashFade2 = 2f;
            float t = 0f;
            while (t < Scene2Duration && !_skipIntroClicked)
            {
                t += (float)GetProcessDeltaTime();
                float progress = Mathf.Clamp(t / Scene2Duration, 0f, 1f);
                if (mainBg != null)
                    SetOffsetPos(mainBg, Scene2Start.Lerp(Scene2End, progress));
                // 閃光在前2秒內淡出
                if (t <= flashFade2)
                    f2Rect.Color = new Color(1f, 1f, 1f, 1f - Mathf.Clamp(t / flashFade2, 0f, 1f));
                await NextFrame(ct);
            }
            Kill(f2Rect);
        }

        // ════════════════════════════════════════════════════════
        //  第三次白屏閃光（場景2 → 場景3）
        //  閃光淡出時場景3已經在移動
        // ════════════════════════════════════════════════════════
        if (!_skipIntroClicked)
        {
            var f3Rect = MakeIntroFlash(_mainPanel, "WhiteFlash3", new Color(1f, 1f, 1f, 0f));

            // 閃光亮起（0.15秒）
            await Lerp(0.15f, p => f3Rect.Color = new Color(1f, 1f, 1f, p), ct);
            f3Rect.Color = Colors.White;

            // 在白屏下切換為 bg_LivingRoom + 設定放大
            if (mainBg != null)
            {
                var livTex = LoadTex("Backgrounds/bg_LivingRoom");
                if (livTex != null) { mainBg.Texture = livTex; mainBg.Modulate = Colors.White; }
                SetScale(mainBg, Scene3ScaleStart);
                SetOffsetPos(mainBg, Scene3PanStart);
            }

            // 場景3拉遠 + 閃光淡出同時進行（前2秒閃光淡出）
            float flashFade3 = 2f;
            float t = 0f;
            while (t < Scene3ZoomDur && !_skipIntroClicked)
            {
                t += (float)GetProcessDeltaTime();
                float progress = Mathf.Clamp(t / Scene3ZoomDur, 0f, 1f);
                float curScale = Mathf.Lerp(Scene3ScaleStart, Scene3ScaleEnd, progress);
                if (mainBg != null)
                {
                    SetScale(mainBg, curScale);
                    SetOffsetPos(mainBg, Scene3PanStart);
                }
                // 閃光在前2秒內淡出
                if (t <= flashFade3)
                    f3Rect.Color = new Color(1f, 1f, 1f, 1f - Mathf.Clamp(t / flashFade3, 0f, 1f));
                await NextFrame(ct);
            }
            Kill(f3Rect);
        }

        // 拉遠結束 → 往右平移，同時交叉淡入 bg_ApartmentDoor
        Texture2D doorTex = null;
        if (!_skipIntroClicked)
        {
            // 在背景上面建立第二層圖（ApartmentDoor）
            doorTex = LoadTex("Backgrounds/bg_ApartmentDoor");
            var doorLayer = MakeIntroTexLayer(_mainPanel, "BG_ApartmentDoor", doorTex);
            doorLayer.Modulate = new Color(1f, 1f, 1f, 0f); // 初始透明
            // 放在背景上面、跳過按鈕下面
            if (mainBg != null)
                _mainPanel.MoveChild(doorLayer, mainBg.GetIndex() + 1);

            // 往右平移 + 交叉淡入
            float t = 0f;
            while (t < Scene3PanDur && !_skipIntroClicked)
            {
                t += (float)GetProcessDeltaTime();
                float progress = Mathf.Clamp(t / Scene3PanDur, 0f, 1f);
                // LivingRoom 往右移 + 慢慢消失
                if (mainBg != null)
                {
                    SetOffsetPos(mainBg, Scene3PanStart.Lerp(Scene3PanEnd, progress));
                    mainBg.Modulate = new Color(1f, 1f, 1f, 1f - progress);
                }
                // ApartmentDoor 慢慢出現
                doorLayer.Modulate = new Color(1f, 1f, 1f, progress);
                await NextFrame(ct);
            }

            // 完成：把背景換成 ApartmentDoor，移除臨時圖層
            if (mainBg != null)
            {
                mainBg.Texture = doorTex;
                mainBg.Modulate = Colors.White;
                SetScale(mainBg, 1f);
                SetOffsetPos(mainBg, Vector2.Zero);
            }
            Kill(doorLayer);
        }

        // ════════════════════════════════════════════════════════
        //  場景4: ApartmentDoor — 走路靠近 + 鏡頭拉近 + CG
        //  全部在 MainPanel 上直接操作
        // ════════════════════════════════════════════════════════
        if (!_skipIntroClicked && mainBg != null)
        {
            // 設定場景4初始放大
            SetScale(mainBg, Scene4StartScale);

            // 走路靠近效果（上下搖擺 + 放大移動）
            {
                float startScale = mainBg.Scale.X;
                Vector2 startPos = new Vector2(mainBg.Position.X, -mainBg.Position.Y); // 轉回 Unity 語意
                float endScale = Scene4WalkScale;
                float[] amps  = { 0f, 10f, 20f, 35f, 55f, 80f };
                float[] freqs = { 0f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f };
                int lv = Mathf.Clamp(Scene4WalkSway, 1, 5);
                float swayAmp = amps[lv];
                float swayFreq = freqs[lv];

                float t = 0f;
                while (t < Scene4WalkDur && !_skipIntroClicked)
                {
                    t += (float)GetProcessDeltaTime();
                    float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp(t / Scene4WalkDur, 0f, 1f));
                    Vector2 basePos = startPos.Lerp(Scene4WalkTarget, p);
                    float swayY = Mathf.Sin(t * swayFreq * Mathf.Pi * 2f) * swayAmp * (1f - p);
                    SetOffsetPos(mainBg, basePos + new Vector2(0f, swayY));
                    SetScale(mainBg, Mathf.Lerp(startScale, endScale, p));
                    await NextFrame(ct);
                }
                SetScale(mainBg, endScale);
                SetOffsetPos(mainBg, Scene4WalkTarget);
            }

            // 鏡頭拉近某個點
            {
                float startScale = mainBg.Scale.X;
                Vector2 startPos = new Vector2(mainBg.Position.X, -mainBg.Position.Y);
                float endScale = Scene4ZoomScale;

                float t = 0f;
                while (t < Scene4ZoomDur && !_skipIntroClicked)
                {
                    t += (float)GetProcessDeltaTime();
                    float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp(t / Scene4ZoomDur, 0f, 1f));
                    SetScale(mainBg, Mathf.Lerp(startScale, endScale, p));
                    SetOffsetPos(mainBg, startPos.Lerp(Scene4ZoomTarget, p));
                    await NextFrame(ct);
                }
            }

            // 閃光 → CG 出現（閃光期間背景可繼續移動）
            if (!_skipIntroClicked)
            {
                var fcgRect = MakeIntroFlash(_mainPanel, "WhiteFlashCG", new Color(1f, 1f, 1f, 0f));

                // 閃光亮起
                await Lerp(0.15f, p => fcgRect.Color = new Color(1f, 1f, 1f, p), ct);
                fcgRect.Color = Colors.White;

                // 場景4閃光後隱藏跳過按鈕
                Kill(skipBtn);

                // 在白屏下建立 CG 圖層，設定在底部
                var cgLayer = MakeIntroTexLayer(_mainPanel, "CG_Intro",
                    LoadTex($"CG/{Scene4CGName}"), preserveAspect: true);
                SetStretchOffset(cgLayer, Scene4CGStart);
                SetScale(cgLayer, Scene4CGScale);
                cgLayer.Modulate = Colors.White;
                // CG 在閃光下面
                _mainPanel.MoveChild(cgLayer, fcgRect.GetIndex());

                // CG 背景圖（在 CG 下層，與 CG 同 offset/scale）
                _cgBgGO = null;
                TextureRect cgBgLayer = null;
                if (!string.IsNullOrEmpty(Scene4CGBG))
                {
                    var cgBgTex = LoadTex($"CG/{Scene4CGBG}");
                    if (cgBgTex != null)
                    {
                        cgBgLayer = MakeIntroTexLayer(_mainPanel, "CG_IntroBG", cgBgTex, preserveAspect: true);
                        SetStretchOffset(cgBgLayer, Scene4CGStart);
                        SetScale(cgBgLayer, Scene4CGScale);
                        cgBgLayer.Modulate = Colors.White;
                        _cgBgGO = cgBgLayer;
                        // 放在 CG 的正下方
                        _mainPanel.MoveChild(cgBgLayer, cgLayer.GetIndex());
                    }
                }

                // 閃光淡出 + 背景移動（如果開啟）
                Vector2 fmStart = new Vector2(mainBg.Position.X, -mainBg.Position.Y);
                {
                    float t = 0f;
                    float fadeOutTime = FlashMoveEnabled ? Mathf.Max(1.5f, FlashMoveDur) : 1.5f;
                    while (t < fadeOutTime)
                    {
                        t += (float)GetProcessDeltaTime();
                        // 閃光淡出
                        if (t <= 1.5f)
                            fcgRect.Color = new Color(1f, 1f, 1f, 1f - Mathf.Clamp(t / 1.5f, 0f, 1f));
                        // 背景移動
                        if (FlashMoveEnabled)
                        {
                            float mp = Mathf.Clamp(t / FlashMoveDur, 0f, 1f);
                            SetOffsetPos(mainBg, fmStart.Lerp(FlashMoveTarget, mp));
                        }
                        await NextFrame(ct);
                    }
                }
                Kill(fcgRect);

                // CG 從下往上慢慢移動（背景同步）
                {
                    float t = 0f;
                    while (t < Scene4CGPanDur && !_skipIntroClicked)
                    {
                        t += (float)GetProcessDeltaTime();
                        float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp(t / Scene4CGPanDur, 0f, 1f));
                        Vector2 curOff = Scene4CGStart.Lerp(Scene4CGEnd, p);
                        SetStretchOffset(cgLayer, curOff);
                        if (cgBgLayer != null) SetStretchOffset(cgBgLayer, curOff);
                        await NextFrame(ct);
                    }
                }
                // CG 停在結束位置，不消失（_introCG 記住引用，最後清理）
                _introCG = cgLayer;

                // ════════════════════════════════════════════════════════
                //  場景5: CG 節奏切換 — 瞬間切換 scale 共4次
                // ════════════════════════════════════════════════════════
                if (!_skipIntroClicked)
                {
                    // 等待指定秒數後才開始（可被跳過中斷）
                    float t = 0f;
                    while (t < Scene5Wait && !_skipIntroClicked)
                    { t += (float)GetProcessDeltaTime(); await NextFrame(ct); }

                    float[] s5Scales   = { Scene5Scale1, Scene5Scale2, Scene5Scale3, Scene5Scale4 };
                    string[] s5CGs     = { Scene5CG1, Scene5CG2, Scene5CG3, Scene5CG4 };
                    Vector2[] s5Offsets = { Scene5Offset1, Scene5Offset2, Scene5Offset3, Scene5Offset4 };
                    for (int i = 0; i < s5Scales.Length && !_skipIntroClicked; i++)
                    {
                        // 瞬間切換 scale + 位移（背景同步）
                        SetStretchOffset(cgLayer, s5Offsets[i]);
                        SetScale(cgLayer, s5Scales[i]);
                        if (cgBgLayer != null)
                        {
                            SetStretchOffset(cgBgLayer, s5Offsets[i]);
                            SetScale(cgBgLayer, s5Scales[i]);
                        }
                        // 瞬間切換圖片
                        var s5Tex = LoadTex($"CG/{s5CGs[i]}");
                        if (s5Tex != null) cgLayer.Texture = s5Tex;
                        // 等待節奏間隔
                        await Wait(Scene5Interval, ct);
                    }
                }
            }
        }

        // ════════════════════════════════════════════════════════
        //  場景6: 黑屏 → TitleName 放大縮小 → 閃光 → 主選單
        // ════════════════════════════════════════════════════════
        if (!_skipIntroClicked)
        {
            // 等待（可被跳過中斷）
            float t = 0f;
            while (t < Scene6Wait && !_skipIntroClicked)
            { t += (float)GetProcessDeltaTime(); await NextFrame(ct); }
        }
        if (!_skipIntroClicked)
        {
            // 清理 CG 背景
            if (_cgBgGO != null) { Kill(_cgBgGO); _cgBgGO = null; }
            // 立刻黑屏遮罩
            var blackRect = MakeIntroFlash(_mainPanel, "BlackScreen", Colors.Black);

            // 清理 CG + 黑屏下立刻把背景切回 LivingRoom
            if (_introCG != null) { Kill(_introCG); _introCG = null; }
            if (mainBg != null)
            {
                var livTex = LoadTex("Backgrounds/bg_LivingRoom");
                if (livTex != null) { mainBg.Texture = livTex; mainBg.Modulate = Colors.White; }
                SetScale(mainBg, 1f);
                SetOffsetPos(mainBg, Vector2.Zero);
            }

            // 在黑屏上建立放大版 TitleName
            var titleShow = ImgTex(_mainPanel, "TitleNameShow", LoadTex("UI/TitleName"),
                V2(0.5f, 0.5f), V2(0.5f, 0.5f), Scene6TitlePos, TitleSize);
            SetScale(titleShow, V2(Scene6TitleScaleStart.X, Scene6TitleScaleStart.Y));
            _mainPanel.MoveChild(titleShow, _mainPanel.GetChildCount() - 1);

            // 等待後開始縮小（可被跳過中斷）
            float t = 0f;
            while (t < Scene6ShrinkDelay && !_skipIntroClicked)
            { t += (float)GetProcessDeltaTime(); await NextFrame(ct); }

            // TitleName 慢慢縮小
            t = 0f;
            while (t < Scene6ShrinkDur && !_skipIntroClicked)
            {
                t += (float)GetProcessDeltaTime();
                float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp(t / Scene6ShrinkDur, 0f, 1f));
                float sx = Mathf.Lerp(Scene6TitleScaleStart.X, Scene6TitleScaleEnd.X, p);
                float sy = Mathf.Lerp(Scene6TitleScaleStart.Y, Scene6TitleScaleEnd.Y, p);
                SetScale(titleShow, V2(sx, sy));
                await NextFrame(ct);
            }

            // 縮小完成後等待（可被跳過中斷）
            t = 0f;
            while (t < Scene6HoldDur && !_skipIntroClicked)
            { t += (float)GetProcessDeltaTime(); await NextFrame(ct); }

            // 閃光蓋在黑屏和TitleName上面，全白後才清理
            var fLastRect = MakeIntroFlash(_mainPanel, "WhiteFlashLast", new Color(1f, 1f, 1f, 0f));

            // 閃光亮起（0.15秒）
            await Lerp(0.15f, p => fLastRect.Color = new Color(1f, 1f, 1f, p), ct);
            fLastRect.Color = Colors.White;

            // 全白後才清理黑屏和TitleName
            Kill(titleShow);
            Kill(blackRect);

            // 顯示按鈕
            if (_menuBtnContainer != null) SetActive(_menuBtnContainer, true);
            if (_titleGO != null) SetActive(_titleGO, true);

            // 白屏淡出
            await Lerp(1.5f, p => fLastRect.Color = new Color(1f, 1f, 1f, 1f - p), ct);
            Kill(fLastRect);
        }
        else
        {
            // 跳過時清理 CG
            if (_introCG != null) { Kill(_introCG); _introCG = null; }

            // 立刻白屏 + 清理所有臨時物件 + 跳音樂
            foreach (var child in new List<Node>(_mainPanel.GetChildren()))
            {
                string n = child.Name;
                if (n == "BG" || n == "BtnContainer" || n == "TitleName"
                    || n == "BtnSkipIntro") continue;
                Kill(child);
            }
            if (_bgmSource != null && _bgmSource.Stream != null)
            {
                _bgmSource.Seek(Mathf.Min(SkipMusicTime, (float)_bgmSource.Stream.GetLength()));
                _bgmFading = false;  // 跳過開場，立即結束淡入流程
                ApplyVolumes();
            }
            if (mainBg != null)
            {
                var livTex2 = LoadTex("Backgrounds/bg_LivingRoom");
                if (livTex2 != null) { mainBg.Texture = livTex2; mainBg.Modulate = Colors.White; }
                SetScale(mainBg, 1f);
                SetOffsetPos(mainBg, Vector2.Zero);
            }

            var fLastRect = MakeIntroFlash(_mainPanel, "WhiteFlashLast", Colors.White);

            if (_menuBtnContainer != null) SetActive(_menuBtnContainer, true);
            if (_titleGO != null) SetActive(_titleGO, true);

            await Lerp(1.5f, p => fLastRect.Color = new Color(1f, 1f, 1f, 1f - p), ct);
            Kill(fLastRect);
        }

        // 銷毀跳過按鈕（可能已被提前銷毀）
        Kill(skipBtn);

        // ── 標題圖片（從左滑到右）──
        StartTitleSlide(ct);
    }

    /// <summary>標題圖片（從左滑到右）。LogoIntro 結束與「直接回開始畫面」共用。</summary>
    void StartTitleSlide(CancellationToken ct)
    {
        // 固定載入 random title image (2)
        _randomTextures = LoadRandomTitleTextures("CG/random_title_image");
        var fixedTex = LoadTex("CG/random_title_image/random title image (2)");
        if (fixedTex != null)
        {
            // 全屏大小（和背景一樣）
            var slide = MakeIntroTexLayer(_mainPanel, "RandomTitleSlide", fixedTex);
            SetStretchOffset(slide, SlideStart);
            slide.Modulate = new Color(1f, 1f, 1f, 0f); // 初始透明
            // 放在按鈕下面（背景上面）
            _mainPanel.MoveChild(slide, 1);

            // Godot 裡圖片節點與 RectTransform 是同一個節點
            _slideRT  = slide;
            _slideImg = slide;
            _prevSlideStart    = SlideStart;
            _prevSlideEnd      = SlideEnd;
            _prevSlideDuration = SlideDuration;

            _slideCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _ = SlideImage(_slideCts.Token);
        }
    }

    /// <summary>
    /// 從遊戲中「返回主畫面」：跳過 LOGO 與開場動畫，直接顯示開始畫面。
    /// </summary>
    void TitleDirect(CancellationToken ct)
    {
        SetActive(_logoPanel, false);
        SetActive(_mainPanel, true);

        // 開場曲（與按下 SKIP 後的狀態一致）
        var opClip = LoadBgm("Audio/bgm/meimei_op");
        SetupOpBgm(opClip);   // 循環＋後半音量補償（OpBgm.cs）
        if (opClip != null && _bgmSource != null)
        {
            _bgmSource.Stream = opClip;
            _bgmFading = false;
            _bgmSource.Play();
            _bgmSource.Seek(Mathf.Min(SkipMusicTime, (float)opClip.GetLength()));
            ApplyVolumes();
        }

        var mainBg = _mainPanel.GetNodeOrNull<TextureRect>("BG/BGTex");
        if (mainBg != null)
        {
            ApplyPointAnchor(mainBg, V2(0.5f, 0.5f), V2(0.5f, 0.5f), Vector2.Zero, V2(RefWidth, RefHeight));
            var livTex = LoadTex("Backgrounds/bg_LivingRoom");
            if (livTex != null) { mainBg.Texture = livTex; mainBg.Modulate = Colors.White; }
            SetScale(mainBg, 1f);
            SetOffsetPos(mainBg, Vector2.Zero);
        }

        if (_menuBtnContainer != null) SetActive(_menuBtnContainer, true);
        if (_titleGO != null) SetActive(_titleGO, true);

        StartTitleSlide(ct);
    }

    /// <summary>
    /// 對應 Unity 的 Resources.LoadAll&lt;Texture2D&gt;(資料夾)
    /// Godot 需要自行走訪目錄
    /// </summary>
    private static Texture2D[] LoadRandomTitleTextures(string relDir)
    {
        var list = new List<Texture2D>();
        string dirPath = "res://Resources/" + relDir;
        using var dir = DirAccess.Open(dirPath);
        if (dir == null)
        {
            GD.PushWarning($"[MainMenu] 找不到資料夾：{relDir}");
            return list.ToArray();
        }

        foreach (var file in dir.GetFiles())
        {
            // 匯入後的資源會多一個 .import 尾綴，要還原成原始檔名
            string fname = file.EndsWith(".import") ? file.Substring(0, file.Length - 7) : file;
            string ext = fname.GetExtension().ToLower();
            if (ext != "png" && ext != "jpg" && ext != "jpeg" && ext != "webp") continue;

            var tex = ResourceLoader.Load<Texture2D>(dirPath + "/" + fname);
            if (tex != null && !list.Contains(tex)) list.Add(tex);
        }
        return list.ToArray();
    }

    // 滑動圖片協程
    // 用 offset 做水平滑動，垂直完美貼合
    void ApplySlideOffset(float x)
    {
        if (_slideRT == null || !IsInstanceValid(_slideRT)) return;
        // 對應 Unity 的 offsetMin = offsetMax = new Vector2(x, 0)
        _slideRT.OffsetLeft   = x;
        _slideRT.OffsetRight  = x;
        _slideRT.OffsetTop    = 0f;
        _slideRT.OffsetBottom = 0f;
    }

    async Task SlideImage(CancellationToken ct = default)
    {
        float elapsed = 0f;
        bool clickedDuringSlide = false;
        // Unity 的 wasPressedThisFrame 需要自行做邊緣偵測
        bool prevMouseDown = Input.IsMouseButtonPressed(MouseButton.Left);

        while (elapsed < SlideDuration && _slideRT != null && IsInstanceValid(_slideRT))
        {
            elapsed += (float)GetProcessDeltaTime();
            float progress = Mathf.Clamp(elapsed / SlideDuration, 0f, 1f);
            float curX = Mathf.Lerp(SlideStart.X, SlideEnd.X, progress);
            ApplySlideOffset(curX);
            // 淡入效果
            float alpha = (SlideFadeIn > 0f) ? Mathf.Clamp(elapsed / SlideFadeIn, 0f, 1f) : 1f;
            if (_slideImg != null) _slideImg.Modulate = new Color(1f, 1f, 1f, alpha);

            // 滑動中點擊 → 跳到終點並切換圖片
            bool mouseDown = Input.IsMouseButtonPressed(MouseButton.Left);
            bool mouseJustPressed = mouseDown && !prevMouseDown;
            prevMouseDown = mouseDown;

            if (SlideClickSwitch && !_slideSwitching && mouseJustPressed)
            {
                ApplySlideOffset(SlideEnd.X);
                if (_slideImg != null) _slideImg.Modulate = Colors.White;
                clickedDuringSlide = true;
                break;
            }
            await NextFrame(ct);
        }
        // 滑完後停在結束位置
        ApplySlideOffset(SlideEnd.X);
        if (_slideImg != null) _slideImg.Modulate = Colors.White;
        _slideCts = null;

        // 滑動中被點擊 → 立即切換
        if (clickedDuringSlide)
            _ = SwitchSlideImage(ct);
    }

    // 點擊切換圖片（淡出當前 → 換圖 → 淡入新圖）
    async Task SwitchSlideImage(CancellationToken ct = default)
    {
        if (_slideImg == null || !IsInstanceValid(_slideImg)
            || _randomTextures == null || _randomTextures.Length < 2)
            return;
        _slideSwitching = true;

        // 淡出
        await Lerp(SlideSwitchFade, p => _slideImg.Modulate = new Color(1f, 1f, 1f, 1f - p), ct);

        // 換一張不同的圖
        var curTex = _slideImg.Texture;
        Texture2D newTex;
        do { newTex = _randomTextures[GD.RandRange(0, _randomTextures.Length - 1)]; }
        while (newTex == curTex && _randomTextures.Length > 1);

        _slideImg.Texture = newTex;

        // 淡入
        await Lerp(SlideSwitchFade, p => _slideImg.Modulate = new Color(1f, 1f, 1f, p), ct);
        _slideImg.Modulate = Colors.White;
        _slideSwitching = false;
    }

    // 重新播放滑動
    void RestartSlide()
    {
        if (_slideRT == null || !IsInstanceValid(_slideRT)) return;
        if (_slideCts != null) { _slideCts.Cancel(); _slideCts.Dispose(); }
        _slideCts = CancellationTokenSource.CreateLinkedTokenSource(_lifeCts.Token);
        _ = SlideImage(_slideCts.Token);
    }

    /// <summary>
    /// 白屏閃光工具：flashIn > 0 → 亮起，flashOut > 0 → 淡出
    /// 傳入 parent 決定遮罩放在哪個面板下
    /// </summary>
    async Task WhiteFlash(Node flashInParent, float flashInDur, float flashOutDur,
        Node flashOutParent, CancellationToken ct = default)
    {
        ColorRect fRect = null;

        // 閃光亮起
        if (flashInDur > 0f && flashInParent != null)
        {
            fRect = MakeIntroFlash(flashInParent, "WhiteFlash", new Color(1f, 1f, 1f, 0f));
            await Lerp(flashInDur, p => fRect.Color = new Color(1f, 1f, 1f, p), ct);
            fRect.Color = Colors.White;
        }

        // 如果需要淡出但還沒建立遮罩（純淡出模式）
        if (flashOutDur > 0f)
        {
            if (fRect == null && flashOutParent != null)
                fRect = MakeIntroFlash(flashOutParent, "WhiteFlash", Colors.White);

            if (fRect != null)
                await Lerp(flashOutDur, p => fRect.Color = new Color(1f, 1f, 1f, 1f - p), ct);
        }

        if (fRect != null) Kill(fRect);
    }

    async Task FadeInBGM(float duration, CancellationToken ct = default)
    {
        // 淡入目標 = 場景音量 × 使用者音量設定，避免淡入完後 ApplyVolumes 造成突然降音
        float targetVol = BgmVolume * (SettingsManager.Instance?.MusicVolume ?? 1f);
        try
        {
            await Lerp(duration, p =>
            {
                if (_bgmSource == null || !IsInstanceValid(_bgmSource)) return;
                SetVol(_bgmSource, Mathf.Lerp(0f, targetVol, p));
            }, ct);

            if (_bgmSource != null && IsInstanceValid(_bgmSource))
                SetVol(_bgmSource, targetVol);
        }
        finally
        {
            // 不論正常完成或被取消，都清除淡入旗標並套用使用者音量設定
            _bgmFading = false;
            ApplyVolumes();
        }
    }

    // 定時淡出：音樂播放到指定秒數時開始淡出
    async Task BgmScheduledFadeOut(CancellationToken ct = default)
    {
        // 等待音樂播到淡出時間點
        while (_bgmSource != null && IsInstanceValid(_bgmSource)
               && _bgmSource.Playing && _bgmSource.GetPlaybackPosition() < BgmFadeOutAt)
            await NextFrame(ct);

        // 淡出
        if (_bgmSource == null || !IsInstanceValid(_bgmSource)) return;
        float startVol = GetVol(_bgmSource);

        try
        {
            _bgmFading = true;  // 淡出期間防止 ApplyVolumes 覆蓋
            float t = 0f;
            while (t < BgmFadeOutDur)
            {
                t += (float)GetProcessDeltaTime();
                if (_bgmSource == null || !IsInstanceValid(_bgmSource)) return;
                SetVol(_bgmSource, Mathf.Lerp(startVol, BgmFadeOutTarget, Mathf.Clamp(t / BgmFadeOutDur, 0f, 1f)));
                await NextFrame(ct);
            }
            if (_bgmSource != null && IsInstanceValid(_bgmSource))
                SetVol(_bgmSource, BgmFadeOutTarget);
        }
        finally
        {
            // 不論正常完成或被取消，都清除旗標
            _bgmFading = false;
        }
    }
}
