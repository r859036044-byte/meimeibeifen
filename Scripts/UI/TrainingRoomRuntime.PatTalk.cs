using Godot;

/// <summary>
/// 摸頭時的台詞氣泡：主角（玩家）一個氣泡在畫面下方，被摸的角色一個氣泡在頭旁邊（跟著頭走）。
/// 台詞都在 Inspector「摸頭台詞」群組（PatPlayerLines／PatGirlLines），隨機挑一句，Play 中可即時改。
/// </summary>
public partial class TrainingRoomRuntime
{
    [ExportGroup("摸頭台詞")]
    [Export] public bool     PatTalkEnabled   = true;
    [Export] public string[] PatPlayerLines   = { "睡覺的時候還是挺可愛的嘛~" };   // 主角台詞（自行增加）
    [Export] public string[] PatGirlLines     = { "嗯……" };                       // 被摸的人台詞（自行增加）
    [Export] public float    PatTalkFirstDelay = 0.8f;   // 開始摸頭後多久出現第一句
    [Export] public float    PatTalkGap        = 1.0f;   // 主角說完到對方回應的間隔
    [Export] public float    PatTalkShowTime   = 3.0f;   // 每個氣泡停留秒數
    [Export] public float    PatTalkRepeat     = 9.0f;   // 一直摸的話，多久再來一輪
    [Export] public Vector2  PatGirlBubbleOffset   = new Vector2(40f, 10f);    // 對方氣泡尾巴尖端相對頭頂的位置（螢幕 px，越小越靠近頭）
    [Export] public float    PatPlayerBubbleBottom = 12f;    // 主角（第一人稱）氣泡：水平置中、貼畫面最底部，尾巴尖端離底部的距離（螢幕 px）

    // 位置：莉奈的氣泡（對話＋表情）在頭的左右兩側，不擋臉；男主的氣泡在雞雞正下方
    [Export(PropertyHint.Enum, "自動(空間大的一邊):0,左邊:1,右邊:2")] public int GirlBubbleSideMode = 0;
    [Export] public float GirlBubbleGapPx   = 14f;   // 氣泡尾巴尖端離前髮邊緣的距離（螢幕 px）
    [Export] public float GirlBubbleYOffset = 0f;    // 氣泡下緣（尾巴尖端）相對臉中心的偏移（螢幕 px，負＝往上、正＝往下）
    [Export] public float PlayerBubbleBottomPx = 10f;   // 男主氣泡：貼著畫面（房間）最下方，離底邊多遠（螢幕 px）
    [Export(PropertyHint.Range, "0.5,4,0.1")] public float BubbleScaleFar  = 1.0f;   // 鏡頭拉最遠時對話框的大小倍率
    [Export(PropertyHint.Range, "0.5,4,0.1")] public float BubbleScaleNear = 2.6f;   // 鏡頭拉最近（CamZoomMax）時的倍率
    [Export(PropertyHint.Range, "1,4,0.1")]   public float BubbleZoomCurve = 1.8f;   // 縮放曲線：越大，鏡頭拉遠時對話框縮得越多、而且縮得越早（1＝線性；>1 是先小後大的曲線）
    /// <summary>現在的對話框倍率：跟著鏡頭縮放（拉遠變小、拉近變大），中間走一條曲線。
    /// 背景拉遠被縮小時，對話框在視覺上會顯得特別大（視覺錯覺），所以拉遠時要縮得比線性更多。</summary>
    [Export(PropertyHint.Range, "0.5,4,0.1")] public float BubbleScalePlayer = 1.7f;   // 男主對話框：固定大小，不跟鏡頭縮放（2.0 縮小 15%）
    float BubbleK(bool isPlayer = false)
    {
        if (isPlayer) return BubbleScalePlayer;
        float t = Mathf.Clamp((_camZoom - 1.1f) / Mathf.Max(0.01f, CamZoomMax - 1.1f), 0f, 1f);
        return Mathf.Lerp(BubbleScaleFar, BubbleScaleNear, Mathf.Pow(t, BubbleZoomCurve));
    }
    const float BubbleTailLen = 16f;   // 未放大時的尾巴長度（螢幕 px），跟 BubbleShape.TailLen 一致
    // 畫布座標（2000x3200）：前髮左右邊緣、臉中心高度；雞雞中線與下緣（臉.png 735~1197 / 前髮.png 698~1218；雞雞.png 850~1119、1590~2128）
    const float HeadEdgeLeftX = 698f, HeadEdgeRightX = 1218f, HeadCenterY = 602f;
    const float JijiCenterX = 985f, JijiBottomY = 2128f;
    int _girlSide = 1;   // 目前女主氣泡在頭的哪一邊：+1 右、-1 左

    Vector2 CanvasToScreen(Control layer, Vector2 canvasPt) => _canvas.Transform * (layer.GetGlobalTransform() * canvasPt);

    /// <summary>頭的左／右側（前髮邊緣、臉中心高度）的螢幕座標；找不到前髮時回傳 null。</summary>
    Vector2? HeadSideScreen(int side)
    {
        if (_canvas == null || !Layers.TryGetValue("前髮", out var hair) || hair == null || !IsInstanceValid(hair)) return null;
        return CanvasToScreen(hair, new Vector2(side > 0 ? HeadEdgeRightX : HeadEdgeLeftX, HeadCenterY));
    }

    /// <summary>決定這次氣泡放左還是右（出現的當下決定，不會中途跳邊）。</summary>
    int PickGirlSide()
    {
        if (GirlBubbleSideMode == 1) return -1;
        if (GirlBubbleSideMode == 2) return 1;
        var l = HeadSideScreen(-1); var r = HeadSideScreen(1);
        if (l == null || r == null) return _girlSide;
        float vpX = GetViewport().GetVisibleRect().Size.X;
        return (vpX - r.Value.X) >= l.Value.X ? 1 : -1;   // 哪邊空間大放哪邊
    }

    Control _bubblePlayer, _bubbleGirl;
    Label   _bubblePlayerLbl, _bubbleGirlLbl;
    float   _patTalkT = -1f;
    int     _patTalkStep;   // 0＝等主角 1＝主角說了、等對方 2＝等下一輪
    readonly RandomNumberGenerator _patRng = new();

    void UpdatePatTalk(float dt)
    {
        if (!PatTalkEnabled || !_patOn)
        {
            if (_patTalkT >= 0f) HideBubble(_bubblePlayer);   // 女主氣泡留給點擊反應用（Play.cs），自己會自動收
            _patTalkT = -1f; _patTalkStep = 0;
            LayoutBubbles(dt);
            return;
        }
        if (_patTalkT < 0f) { _patTalkT = 0f; _patTalkStep = 0; }
        _patTalkT += dt;

        if (_patTalkStep == 0 && _patTalkT >= PatTalkFirstDelay)
        {
            ShowBubble(ref _bubblePlayer, ref _bubblePlayerLbl, Pick(PatPlayerLines), true);
            _patTalkStep = 1; _patTalkT = 0f;
        }
        else if (_patTalkStep == 1 && _patTalkT >= PatTalkGap)
        {
            ShowBubble(ref _bubbleGirl, ref _bubbleGirlLbl, Pick(PatGirlLines), false);
            _patTalkStep = 2; _patTalkT = 0f;
        }
        else if (_patTalkStep == 2 && _patTalkT >= PatTalkRepeat)
        {
            _patTalkStep = 0; _patTalkT = PatTalkFirstDelay;   // 下一輪立刻開始
        }

        LayoutBubbles(dt);
    }

    /// <summary>氣泡每幀的排版／自動收起（摸頭台詞和點擊反應共用）。</summary>
    void LayoutBubbles(float dt)
    {
        RescaleBubble(_bubblePlayer, _bubblePlayerLbl, true);
        RescaleBubble(_bubbleGirl, _bubbleGirlLbl, false);
        PlaceTail(_bubblePlayer, true);
        PlaceTail(_bubbleGirl, false);

        // 自動收起
        AutoHide(_bubblePlayer, dt);
        AutoHide(_bubbleGirl, dt);

        Vector2 vpG = GetViewport().GetVisibleRect().Size;

        // 女主氣泡：在頭的左右側，垂直中心對臉中心，尾巴朝向臉
        if (_bubbleGirl != null && IsInstanceValid(_bubbleGirl) && HeadSideScreen(_girlSide) is Vector2 edge)
        {
            Vector2 sz = _bubbleGirl.Size;
            float x = edge.X + _girlSide * (GirlBubbleGapPx + BubbleTailLen * BubbleK()) - (_girlSide < 0 ? sz.X : 0f);
            Vector2 pos = new Vector2(x, edge.Y + GirlBubbleYOffset - sz.Y);   // 尾巴貼在氣泡下緣 → 氣泡底邊（尖端）對臉中心高度，氣泡在臉的側上方
            // 鏡頭放大等原因超出畫面：夾在畫面內（留 8px 邊），被夾動了就把尾巴藏起來（尾巴指不到臉了）
            Vector2 clamped = new Vector2(
                Mathf.Clamp(pos.X, 8f, Mathf.Max(8f, vpG.X - sz.X - 8f)),
                Mathf.Clamp(pos.Y, 8f, Mathf.Max(8f, vpG.Y - sz.Y - 8f)));
            _bubbleGirl.Position = clamped;
            var shapeG = _bubbleGirl.GetNodeOrNull<BubbleShape>("Shape");
            if (shapeG != null) shapeG.TailVisible = clamped.IsEqualApprox(pos);
        }

        // 男主氣泡：X 對雞雞中線、Y 固定在畫面最下方，尾巴在氣泡下方朝下
        if (_bubblePlayer != null && IsInstanceValid(_bubblePlayer))
        {
            Vector2 sz = _bubblePlayer.Size;
            Vector2 pos;
            float bottomY = vpG.Y - sz.Y - BubbleTailLen * BubbleK(true) - PlayerBubbleBottomPx;   // 畫面最下方（尾巴在氣泡下面，要留出尾巴的高度）
            if (_canvas != null && Layers.TryGetValue("雞雞", out var jj) && jj != null && IsInstanceValid(jj))
            {
                Vector2 tip = CanvasToScreen(jj, new Vector2(JijiCenterX, JijiBottomY));   // 只有 X 跟著雞雞（雞雞的 Y 不管），Y 固定在畫面最下方
                pos = new Vector2(tip.X - sz.X * 0.5f, bottomY);
            }
            else
                pos = new Vector2((vpG.X - sz.X) * 0.5f, bottomY);   // 找不到雞雞：畫面最下方置中
            Vector2 clamped = new Vector2(
                Mathf.Clamp(pos.X, 8f, Mathf.Max(8f, vpG.X - sz.X - 8f)),
                Mathf.Clamp(pos.Y, 8f, Mathf.Max(8f, vpG.Y - sz.Y - 8f)));
            _bubblePlayer.Position = clamped;
            var shapeP = _bubblePlayer.GetNodeOrNull<BubbleShape>("Shape");
            if (shapeP != null) shapeP.TailVisible = clamped.IsEqualApprox(pos);
        }
    }

    /// <summary>鏡頭縮放時對話框跟著放大縮小：字、邊距、外形一起重算（倍率變化 3% 以上才更新）。</summary>
    void RescaleBubble(Control b, Label lbl, bool isPlayer)
    {
        if (b == null || lbl == null || !IsInstanceValid(b) || !IsInstanceValid(lbl)) return;
        float K = BubbleK(isPlayer);
        if (b.HasMeta("bk") && Mathf.Abs(K - (float)b.GetMeta("bk")) < 0.03f) return;
        b.SetMeta("bk", K);
        lbl.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt((isPlayer ? 24f : 22f) * K));
        if (b.GetThemeStylebox("panel") is StyleBoxEmpty pad)
        {
            pad.ContentMarginLeft = pad.ContentMarginRight = (isPlayer ? 22f : 23f) * K;
            pad.ContentMarginTop = pad.ContentMarginBottom = (isPlayer ? 12f : 13f) * K;
        }
        var shape = b.GetNodeOrNull<BubbleShape>("Shape");
        if (shape != null) shape.K = K;
        b.ResetSize();
    }

    /// <summary>尾巴在哪一邊：女主＝靠臉那一側的邊（氣泡在右邊→左緣、在左邊→右緣）；男主＝下緣，朝下。</summary>
    void PlaceTail(Control b, bool isPlayer)
    {
        if (b == null || !GodotObject.IsInstanceValid(b)) return;
        var shape = b.GetNodeOrNull<BubbleShape>("Shape");
        if (shape == null) return;
        shape.TailSide = isPlayer ? BubbleShape.Side.Bottom : (_girlSide > 0 ? BubbleShape.Side.Left : BubbleShape.Side.Right);
        if (!isPlayer && _canvas != null && Layers.TryGetValue("前髮", out var hair) && hair != null && IsInstanceValid(hair))
            shape.TailTarget = CanvasToScreen(hair, new Vector2((HeadEdgeLeftX + HeadEdgeRightX) * 0.5f, HeadCenterY));   // 尾巴指向莉奈的臉中心
    }

    string Pick(string[] lines)
    {
        if (lines == null || lines.Length == 0) return "";
        return lines[_patRng.RandiRange(0, lines.Length - 1)];
    }

    void ShowBubble(ref Control bubble, ref Label lbl, string text, bool isPlayer)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (!isPlayer) _girlSide = PickGirlSide();   // 女主氣泡每次出現時決定放頭的左邊還是右邊
        if (bubble == null || !IsInstanceValid(bubble)) (bubble, lbl) = BuildBubble(isPlayer);
        lbl.Text = text;
        bubble.ResetSize();
        bubble.SetMeta("hide_t", PatTalkShowTime);
        bubble.Visible = true;
        bubble.Modulate = new Color(1, 1, 1, 0);
        bubble.Scale = new Vector2(0.85f, 0.85f);
        var tw = bubble.CreateTween().SetParallel(true);
        tw.TweenProperty(bubble, "modulate:a", 1f, 0.18);
        tw.TweenProperty(bubble, "scale", Vector2.One, 0.25).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    void AutoHide(Control b, float dt)
    {
        if (b == null || !IsInstanceValid(b) || !b.Visible || !b.HasMeta("hide_t")) return;
        float t = (float)b.GetMeta("hide_t") - dt;
        b.SetMeta("hide_t", t);
        if (t <= 0f) { b.RemoveMeta("hide_t"); HideBubble(b); }
    }

    void HideBubble(Control b)
    {
        if (b == null || !IsInstanceValid(b) || !b.Visible) return;
        var tw = b.CreateTween();
        tw.TweenProperty(b, "modulate:a", 0f, 0.2);
        tw.TweenCallback(Callable.From(() => { if (IsInstanceValid(b)) b.Visible = false; }));
    }

    /// <summary>對話氣泡：圓角矩形＋∠ 形尾巴合成同一個外形（BubbleShape.cs），尾巴和氣泡銜接處沒有縫。
    /// 整體放大 BubbleScale 倍（字、框、圓角、尾巴一起）。</summary>
    (Control, Label) BuildBubble(bool isPlayer)
    {
        float K = BubbleK(isPlayer);
        var box = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        // 外形由 BubbleShape 畫；容器本身只負責字的邊距（框的粗細已包含在邊距裡）
        var pad = new StyleBoxEmpty();
        float mx = (isPlayer ? 22f : 23f) * K, my = (isPlayer ? 12f : 13f) * K;
        pad.ContentMarginLeft = pad.ContentMarginRight = mx;
        pad.ContentMarginTop = pad.ContentMarginBottom = my;
        box.AddThemeStyleboxOverride("panel", pad);

        var lbl = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        lbl.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt((isPlayer ? 24f : 22f) * K));
        lbl.AddThemeColorOverride("font_color", UiText);
        box.AddChild(lbl);

        box.AddChild(new BubbleShape
        {
            Name = "Shape", K = K, DoubleBorder = !isPlayer,   // 女主：深色框＋粉紅內框；男主：只有深色框
            OutlineColor = BarOutline, InnerBorderColor = new Color("e8b5ce"),
            FillColor = isPlayer ? new Color(1f, 1f, 1f) : new Color(1f, 0.97f, 0.99f),
        });

        box.SetMeta("bk", K);
        UiRoot.AddChild(box);
        return (box, lbl);
    }
}
