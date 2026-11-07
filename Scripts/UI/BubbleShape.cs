using Godot;

/// <summary>
/// 對話氣泡的外形：圓角矩形＋「∠」形尾巴合成「同一個」多邊形再畫外框，
/// 所以尾巴和氣泡是一整塊，銜接處沒有縫、沒有多出來的線。
/// 掛在 PanelContainer（氣泡）底下當 Node2D 子節點（ShowBehindParent），尺寸直接讀父節點 Size。
///
/// ∠ 樣式：一邊是水平的（和氣泡底邊同一條線），另一邊斜斜收回氣泡側邊；
/// 左右側的尾巴在下緣偏上一點：水平下緣伸出去到尖端，斜邊從尖端往上連回氣泡側邊。
/// 外框顏色層：外框(深色) → [女主：粉紅內框] → 底色；每一層都是上一層往內縮，所以尾巴那一圈也一路包到尖端。
/// </summary>
public partial class BubbleShape : Node2D
{
    public enum Side { None, Left, Right, Top, Bottom }

    public Side  TailSide = Side.None;     // 尾巴在氣泡的哪一邊（Left＝在左緣、朝左）
    public bool  TailVisible = true;
    public Vector2? TailTarget;            // 左右側尾巴要指向的點（螢幕座標，例如莉奈的臉中心）；null＝水平朝外
    public float K = 1f;                   // 整體放大倍率（圓角、框、尾巴都跟著放大）
    public float Radius = 30f, Border = 3f, TailLen = 16f, TailBaseW = 13f, TailRise = 10f;   // 圓角大（高度不夠時自動變成膠囊形）；尾巴離底邊往上 TailRise   // 尾巴小小的：長 16、底 13（再乘 K）
    public bool  DoubleBorder;             // 女主：深色框 ＋ 粉紅內框
    public Color OutlineColor = new Color(0.28f, 0.14f, 0.24f);
    public Color InnerBorderColor = new Color("e8b5ce");
    public Color FillColor = Colors.White;

    Vector2 _lastSize; Side _lastSide; bool _lastVis; Vector2 _lastTarget; Vector2 _lastPos;

    public override void _Ready() { ShowBehindParent = true; }

    public override void _Process(double delta)
    {
        if (GetParent() is not Control box) return;
        Vector2 tgt = TailTarget ?? Vector2.Zero, pos = box.GlobalPosition;
        if (box.Size != _lastSize || TailSide != _lastSide || TailVisible != _lastVis
            || !tgt.IsEqualApprox(_lastTarget) || !pos.IsEqualApprox(_lastPos))
        {
            _lastSize = box.Size; _lastSide = TailSide; _lastVis = TailVisible; _lastTarget = tgt; _lastPos = pos;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (GetParent() is not Control box) return;
        Vector2 sz = box.Size;
        if (sz.X < 4f || sz.Y < 4f) return;

        Vector2[] outer = RoundedRect(sz, Mathf.Min(Radius * K, Mathf.Min(sz.X, sz.Y) * 0.5f));
        if (TailSide != Side.None && TailVisible)
        {
            var merged = Geometry2D.MergePolygons(outer, TailPoly(sz));
            if (merged.Count > 0) outer = Smooth(Largest(merged), 5f * K);
        }

        float b = Border * K;
        DrawColoredPolygon(outer, OutlineColor);
        if (DoubleBorder)
        {
            DrawColoredPolygon(Inset(outer, b), InnerBorderColor);
            DrawColoredPolygon(Inset(outer, b * 2f), FillColor);
        }
        else
            DrawColoredPolygon(Inset(outer, b), FillColor);
    }

    /// <summary>∠ 形尾巴三角形（底邊稍微伸進氣泡裡，合併時才會完全銜接）。尖端在邊的中心線上。</summary>
    Vector2[] TailPoly(Vector2 sz)
    {
        float len = TailLen * K, bw = TailBaseW * K, ov = 3f * K;
        float r = Mathf.Min(Radius * K, Mathf.Min(sz.X, sz.Y) * 0.5f);
        float inX = Mathf.Max(ov, r * 0.6f);     // 底邊要完全藏在氣泡裡面（圓角大，側邊是弧形）
        switch (TailSide)
        {
            case Side.Left:
            case Side.Right:
            {
                bool left = TailSide == Side.Left;
                float sign = left ? -1f : 1f;                       // 朝外的方向
                float ex = left ? 0f : sz.X;                        // 尾巴所在的那條邊
                float yc = sz.Y - (TailRise + TailBaseW * 0.5f) * K - ov;   // 底邊中心：離氣泡底邊往上一點
                // 尖端朝向目標（莉奈的臉）：方向限制在朝外 ±65° 內
                float ang = Mathf.DegToRad(20f);   // 沒有目標：稍微朝下
                if (TailTarget is Vector2 tg)
                {
                    Vector2 tl = GetParent() is Control bx ? bx.GetGlobalTransform().AffineInverse() * tg : tg;
                    Vector2 d = tl - new Vector2(ex, yc);
                    ang = Mathf.Atan2(d.Y, d.X * sign);
                    ang = Mathf.Clamp(ang, Mathf.DegToRad(-65f), Mathf.DegToRad(65f));
                }
                Vector2 tip = new Vector2(ex, yc) + new Vector2(Mathf.Cos(ang) * sign, Mathf.Sin(ang)) * len;
                float bx0 = ex - sign * inX;                        // 底邊 x（在氣泡裡面）
                return new[] { new Vector2(bx0, yc - bw * 0.5f), tip, new Vector2(bx0, yc + bw * 0.5f) };
            }
            case Side.Bottom:   // ∠：尖端在下緣中心朝下，右緣垂直（x＝中心），左緣斜回氣泡底邊
            {   float cx = sz.X * 0.5f;
                return new[] { new Vector2(cx - bw, sz.Y - ov), new Vector2(cx, sz.Y + len), new Vector2(cx, sz.Y - ov) };
            }
            default:   // Top：尖端在上緣中心，右緣垂直（x＝中心），左緣斜
            {   float cx = sz.X * 0.5f;
                return new[] { new Vector2(cx - bw, ov), new Vector2(cx, -len), new Vector2(cx, ov) };
            }
        }
    }

    static Vector2[] RoundedRect(Vector2 sz, float r, bool squareBL = false, bool squareBR = false)
    {
        const int seg = 10;
        var pts = new System.Collections.Generic.List<Vector2>();
        void Corner(Vector2 c, float a0)
        {
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.DegToRad(a0 + 90f * i / seg);
                pts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
            }
        }
        Corner(new Vector2(sz.X - r, r),        -90f);   // 右上
        if (squareBR) pts.Add(new Vector2(sz.X, sz.Y)); else Corner(new Vector2(sz.X - r, sz.Y - r),   0f);   // 右下
        if (squareBL) pts.Add(new Vector2(0f, sz.Y));   else Corner(new Vector2(r, sz.Y - r),         90f);   // 左下
        Corner(new Vector2(r, r),               180f);   // 左上
        return pts.ToArray();
    }

    /// <summary>閉運算：先往外撐 c 再往內縮 c。凸角（尾巴尖端）維持，凹角（尾巴和氣泡交接處）被填成圓弧，看起來是一整塊。</summary>
    static Vector2[] Smooth(Vector2[] poly, float c)
    {
        var o = Geometry2D.OffsetPolygon(poly, c, Geometry2D.PolyJoinType.Round);
        if (o.Count == 0) return poly;
        var i = Geometry2D.OffsetPolygon(Largest(o), -c, Geometry2D.PolyJoinType.Round);
        return i.Count == 0 ? poly : Largest(i);
    }

    static Vector2[] Inset(Vector2[] poly, float d)
    {
        var res = Geometry2D.OffsetPolygon(poly, -d, Geometry2D.PolyJoinType.Round);
        return res.Count > 0 ? Largest(res) : poly;
    }

    static Vector2[] Largest(Godot.Collections.Array<Vector2[]> polys)
    {
        Vector2[] best = polys[0]; float bestA = -1f;
        foreach (var p in polys)
        {
            float a = 0f;
            for (int i = 0; i < p.Length; i++) { var u = p[i]; var v = p[(i + 1) % p.Length]; a += u.X * v.Y - v.X * u.Y; }
            a = Mathf.Abs(a);
            if (a > bestA) { bestA = a; best = p; }
        }
        return best;
    }
}
