using Godot;
using System.Collections.Generic;

/// <summary>
/// 眼淚圈：眼淚圖層顯示時，從眼淚的位置冒出「中間透明的白色圓圈」，往下並左右擺動，
/// 離開臉（臉圖層該處已經透明）就消失。
/// 位置都是畫布 px（2000x3200，跟圖層同一個座標系）。
/// </summary>
public partial class TrainingRoomRuntime
{
    [ExportGroup("眼淚圈")]
    [Export] public bool TearRingEnabled = true;
    [Export] public float TearRingRadius = 9f;           // 水滴圓底的半徑（畫布 px）
    [Export] public float TearRingTipRatio = 2.3f;       // 水滴尖端高度 ＝ 半徑 × 這個值
    [Export(PropertyHint.Range, "0.3,1.2,0.05")] public float TearRingSlim = 0.6f;   // 水滴寬度倍率（越小越瘦；1＝原本的圓底）
    [Export] public float TearRingWidth = 2.5f;          // 外框線寬
    [Export] public float TearRingAlpha = 0.9f;          // 白色不透明度
    [Export] public float TearRingFall = 24f;            // 起始下落速度（畫布 px/秒）：慢慢流
    [Export] public float TearRingAccel = 7f;            // 往下越來越快（px/秒²）
    [Export] public float TearRingDrift = 0.55f;         // 斜著流：橫向／縱向的比例（左半臉往左下、右半臉往右下；0＝直直往下）
    [Export] public float TearRingStartOut = 3f;         // 水滴起點再往外移多少（左半臉往左、右半臉往右，畫布 px）
    [Export] public bool TearHideOuterCorner = true;     // 關掉眼角外側兩張小眼淚圖（眼淚3、眼淚4）；眼淚1、2 保留。只是畫成透明，圖層還在
    [Export] public float TearRingSideSpeed = 9f;        // 開頭往左／右的速度（畫布 px/秒）
    [Export] public float TearRingSideDown = 5f;         // 開頭同時往下的速度（畫布 px/秒）：不是純水平，而是很斜的緩降
    [Export] public float TearRingSideTime = 1.2f;       // 這個開頭階段花多久（秒），之後平滑轉成往左下／右下流
    [Export] public float TearRingSway = 2.5f;           // 再加一點點左右擺動（畫布 px）
    [Export] public float TearRingSwayHz = 0.9f;         // 左右擺動頻率（次/秒）
    [Export] public float TearRingIntervalCalm = 5f;     // 不痛時，每隔多久冒一個（秒）
    [Export] public float TearRingIntervalPain = 3f;     // 痛苦值滿時，最快每隔多久冒一個（秒）
    [Export(PropertyHint.Range, "0,1,0.05")] public float TearRingPainSlow = 0.5f;   // 痛苦值滿時，流動速度變成幾倍的減少量（0.5＝變成一半）
    [Export] public int TearRingMax = 3;                 // 同時最多幾個
    [Export] public float TearRingFadeOut = 0.12f;       // 離開臉之後淡掉的秒數

    // 眼淚圖層實際畫出內容的範圍（2000x3200 畫布 px，量 alpha bbox）：從這個範圍的下緣冒出來
    const float TearRingCenterX = 990f;   // 臉的左右中線（臉圖 alpha 範圍 734～1246）
    static readonly (string layer, Rect2 rc)[] _tearSrc =
    {
        ("表情/眼淚1", new Rect2(1030f, 660f, 75f, 16f)),
        ("表情/眼淚2", new Rect2(813f, 669f, 75f, 17f)),
        ("表情/眼淚3", new Rect2(795f, 684f, 18f, 27f)),
        ("表情/眼淚4", new Rect2(1105f, 669f, 20f, 24f)),
    };

    /// <summary>水滴外框（只有線、中間透明）：尖端在上、圓底在下；上半部的線完全透明，只有下半部的外框看得到。</summary>
    sealed partial class TearRing : Control
    {
        public float Radius = 9f, TipRatio = 2.3f, Width = 2.5f, Slim = 0.6f;
        public override void _Draw()
        {
            float r = Radius, h = r * Mathf.Max(1.2f, TipRatio);
            float phi = Mathf.Acos(r / h);                      // 尖端到圓的切線角
            var pts = new List<Vector2>(); var cols = new List<Color>();
            void Add(Vector2 p)
            {
                pts.Add(new Vector2(p.X * Slim, p.Y));   // 寬度縮瘦
                // 以整個水滴高度（-h ~ +r）的中線為界：上半部 alpha=0，往下漸入到完全不透明
                float u = (p.Y - (-h + (h + r) * 0.5f)) / (r * 0.9f);
                cols.Add(new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp(u, 0f, 1f))));
            }
            Add(new Vector2(0f, -h));
            float a0 = -Mathf.Pi * 0.5f + phi, a1 = Mathf.Pi * 1.5f - phi;
            const int N = 36;
            for (int i = 0; i <= N; i++) { float a = Mathf.Lerp(a0, a1, i / (float)N); Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r); }
            Add(new Vector2(0f, -h));
            DrawPolylineColors(pts.ToArray(), cols.ToArray(), Width, true);
        }
    }

    sealed class TearRingState
    {
        public TearRing Node; public Vector2 Origin, Pos; public float T, Phase, Dead = -1f, Dir = 1f;
    }

    readonly List<TearRingState> _tearRings = new();
    float _tearTimer;
    Image _tearFaceImg; bool _tearFaceTried;

    bool TearFaceInside(Vector2 canvasPos)
    {
        if (!_tearFaceTried)
        {
            _tearFaceTried = true;
            if (Layers.TryGetValue("臉", out var f) && f is TextureRect ft && ft.Texture != null)
            {
                _tearFaceImg = ft.Texture.GetImage();
                if (_tearFaceImg != null && _tearFaceImg.IsCompressed()) _tearFaceImg.Decompress();
            }
        }
        if (_tearFaceImg == null) return true;   // 取不到臉的圖就不判定
        Vector2 off = Layers.TryGetValue("臉", out var fl) && fl != null ? fl.Position : Vector2.Zero;
        var p = canvasPos - off;
        int x = Mathf.RoundToInt(p.X), y = Mathf.RoundToInt(p.Y);
        if (x < 0 || y < 0 || x >= _tearFaceImg.GetWidth() || y >= _tearFaceImg.GetHeight()) return false;
        return _tearFaceImg.GetPixel(x, y).A > 0.2f;
    }

    void UpdateTearRings(float dt)
    {
        if (!TearRingEnabled) { ClearTearRings(); return; }

        for (int k = 2; k < _tearSrc.Length; k++)   // 眼淚3、4
            if (Layers.TryGetValue(_tearSrc[k].layer, out var ol) && ol != null)
                ol.SelfModulate = new Color(1f, 1f, 1f, TearHideOuterCorner ? 0f : 1f);   // 每幀設定；不動 Visible／Modulate，那些是表情系統在管，水滴要靠它們判斷

        // 1) 生成：整個臉共用一個計時；不痛 5 秒一個，痛苦值越高越快（最快 3 秒一個）
        var visible = new List<int>();
        for (int i = 0; i < 2; i++)   // 水滴只從眼淚1、2（下眼瞼那兩條）冒出
            if (Layers.TryGetValue(_tearSrc[i].layer, out var tl0) && tl0 != null && tl0.GetParent() != null
                && tl0.IsVisibleInTree() && tl0.Modulate.A > 0.05f) visible.Add(i);   
        if (visible.Count == 0) { _tearTimer = 0f; }
        else
        {
            _tearTimer -= dt;
            if (_tearTimer <= 0f && _tearRings.Count < TearRingMax)
            {
                float pain = Mathf.Clamp(_pain, 0f, 1f);
                _tearTimer = Mathf.Lerp(TearRingIntervalCalm, TearRingIntervalPain, pain) * (float)GD.RandRange(0.9, 1.1);
                var (name, rc) = _tearSrc[visible[(int)(GD.Randi() % (uint)visible.Count)]];
                var tl = Layers[name]; var parent = tl.GetParent();
                var node = new TearRing { Radius = TearRingRadius, TipRatio = TearRingTipRatio, Width = TearRingWidth, Slim = TearRingSlim, MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(1f, 1f, 1f, TearRingAlpha) };
                parent.AddChild(node);
                parent.MoveChild(node, tl.GetIndex() + 1);   // 疊在眼淚圖層上面、前髮下面
                var origin = tl.Position + new Vector2(rc.Position.X + (float)GD.RandRange(0.15, 0.85) * rc.Size.X, rc.End.Y);
                origin.X += (origin.X < TearRingCenterX ? -1f : 1f) * TearRingStartOut;   // 起點再往外移
                node.Position = origin;
                _tearRings.Add(new TearRingState { Node = node, Origin = origin, Pos = origin, Phase = (float)GD.RandRange(0.0, Mathf.Tau), Dir = origin.X < TearRingCenterX ? -1f : 1f });
            }
        }

        // 2) 更新：往下＋左右擺動；離開臉就淡掉消失
        for (int i = _tearRings.Count - 1; i >= 0; i--)
        {
            var r = _tearRings[i];
            if (r.Node == null || !IsInstanceValid(r.Node)) { _tearRings.RemoveAt(i); continue; }
            float rdt = dt * Mathf.Lerp(1f, 1f - TearRingPainSlow, Mathf.Clamp(_pain, 0f, 1f));   // 痛苦值越高流得越慢
            r.T += rdt;
            // 開頭：一邊往左／右、一邊慢慢往下（很斜的緩降）；之後平滑過渡成往左下／右下流，越流越快
            float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp(r.T / Mathf.Max(0.01f, TearRingSideTime), 0f, 1f));
            float vyFall = TearRingFall + TearRingAccel * Mathf.Max(0f, r.T - TearRingSideTime);
            var vStart = new Vector2(r.Dir * TearRingSideSpeed, TearRingSideDown);
            var vFlow = new Vector2(r.Dir * TearRingDrift * vyFall, vyFall);
            var vel = vStart.Lerp(vFlow, u);
            r.Pos += vel * rdt;
            var pos = r.Pos + new Vector2(TearRingSway * Mathf.Sin(r.T * TearRingSwayHz * Mathf.Tau + r.Phase) * u, 0f);
            var heading = vel.Normalized();                                         // 前進方向
            r.Node.Position = pos;
            r.Node.Rotation = Mathf.Atan2(-heading.X, heading.Y);                 // 水滴尖端朝來的方向

            if (r.Dead < 0f)
            {
                // 圓圈最下緣離開臉就開始淡出
                if (!TearFaceInside(pos + heading * TearRingRadius)) r.Dead = 0f;   // 水滴圓底最前端
            }
            if (r.Dead >= 0f)
            {
                r.Dead += dt;
                float a = 1f - r.Dead / Mathf.Max(0.01f, TearRingFadeOut);
                if (a <= 0f) { r.Node.QueueFree(); _tearRings.RemoveAt(i); continue; }
                r.Node.Modulate = new Color(1f, 1f, 1f, TearRingAlpha * a);
            }
        }
    }

    void ClearTearRings()
    {
        foreach (var r in _tearRings) if (r.Node != null && IsInstanceValid(r.Node)) r.Node.QueueFree();
        _tearRings.Clear();
    }
}
