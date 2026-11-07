using Godot;
using System;

/// <summary>
/// 莉奈頭旁的氣泡表情（RPG Maker 風）：全部用程式畫，不需要圖檔。
///   Heart    好感：氣泡裡一顆粉紅愛心（會輕輕跳動）
///   Tangle   不好：一團毛線（亂麻），彈出來會抖一下
///   Exclaim  驚嘆號　　Question  問號
/// 用法：EmoteBubble.Spawn(parent, EmoteBubble.Kind.Heart, () => 螢幕座標);
/// 座標＝氣泡「底部中心」；每幀重新呼叫 Func，所以可以跟著頭／對話框走。
/// </summary>
public partial class EmoteBubble : Control
{
    public enum Kind { Heart, Tangle, Exclaim, Question }

    public Kind EmoteKind;
    public Func<Vector2> Anchor;
    public Func<Vector2> TailTarget;   // 尾巴要指向的點（螢幕座標，例如莉奈的臉中心）；null＝維持左下 45°
    public float HoldSec = 1.2f;      // 表情氣泡停留秒數

    static readonly Color Outline = new Color("4a3a4f");
    static readonly Color HeartFill = new Color("ed70a3");
    static readonly Color HeartLine = new Color("e85b95");

    float _age;
    Vector2[] _scribble;
    public static EmoteBubble Spawn(Node parent, Kind kind, Func<Vector2> anchor, float holdSec = 1.2f, Func<Vector2> tailTarget = null)
    {
        var e = new EmoteBubble { EmoteKind = kind, Anchor = anchor, HoldSec = holdSec, TailTarget = tailTarget };
        parent.AddChild(e);
        return e;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ZIndex = 50;
        var rng = new RandomNumberGenerator(); rng.Randomize();

        {
            Size = new Vector2(72f, 76f);
            PivotOffset = new Vector2(36f, 72f);   // 從尾巴尖端彈出來
            if (EmoteKind == Kind.Tangle)
            {
                // 一團毛線：亂數但固定的幾圈環繞線
                _scribble = new Vector2[70];
                for (int i = 0; i < _scribble.Length; i++)
                {
                    float t = i / (float)(_scribble.Length - 1);
                    float a = t * Mathf.Tau * 3.2f + rng.RandfRange(-0.25f, 0.25f);
                    float r = 6f + 15f * Mathf.Abs(Mathf.Sin(t * 7.3f + 0.6f)) + rng.RandfRange(-2.5f, 2.5f);
                    _scribble[i] = new Vector2(36f, 32f) + new Vector2(Mathf.Cos(a) * r * 1.05f, Mathf.Sin(a * 1.17f + 0.8f) * r);
                }
            }
            Scale = new Vector2(0.3f, 0.3f);
        }
        Reposition();
    }

    static readonly Vector2 Center = new Vector2(36f, 32f);   // 圓圈中心（本地座標）
    Vector2 _tailDir = new Vector2(-0.8f, 0.6f);   // 尾巴方向（單位向量，本地座標系，Y 向下為正）；預設左下 45° 左右

    void Reposition()
    {
        if (Anchor == null) return;
        Vector2 a = Anchor();
        Position = a - new Vector2(Size.X * 0.5f, Size.Y - 4f);   // 氣泡底部中心對齊錨點（跟之前一樣）

        if (TailTarget != null)
        {
            Vector2 d = TailTarget() - (Position + Center);   // 目標在圓心的哪個方向（螢幕座標，跟本地座標同軸）
            if (d.LengthSquared() > 1f) _tailDir = d.Normalized();
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _age += dt;
        Reposition();

        // 彈出：0.3 → 1.15 → 1；停留 HoldSec；最後 0.3 秒淡出並往上飄一點
        float s = _age < 0.16f ? Mathf.Lerp(0.3f, 1.15f, _age / 0.16f)
                : _age < 0.28f ? Mathf.Lerp(1.15f, 1f, (_age - 0.16f) / 0.12f) : 1f;
        Scale = new Vector2(s, s);
        float outT = Mathf.Clamp((_age - HoldSec) / 0.3f, 0f, 1f);
        Modulate = new Color(1, 1, 1, 1f - outT);
        if (EmoteKind == Kind.Tangle && _age < 0.9f)
            Rotation = Mathf.Sin(_age * 45f) * 0.09f * (1f - _age / 0.9f);   // 毛線團彈出來時抖一下
        else
            Rotation = 0f;
        if (_age >= HoldSec + 0.3f) QueueFree();
        QueueRedraw();
    }

    public override void _Draw()
    {
        var c = Center;
        // 氣泡：白底圓＋深色描邊＋尾巴（尾巴朝向 TailTarget，例如莉奈的臉；沒有目標就維持左下 45°）
        Vector2 perp = new Vector2(-_tailDir.Y, _tailDir.X);   // 垂直於尾巴方向，決定尾巴底部的寬度
        Vector2 baseC = c + _tailDir * 26f;        // 尾巴底邊中心（貼在圓周上）
        Vector2 tip   = c + _tailDir * 44f;         // 尾巴尖端
        DrawColoredPolygon(new[] { baseC + perp * 9f, baseC - perp * 9f, tip }, Outline);
        DrawColoredPolygon(new[] { baseC + perp * 6f, baseC - perp * 6f, c + _tailDir * 38f }, Colors.White);
        DrawCircle(c, 31f, Outline);
        DrawCircle(c, 28f, Colors.White);

        switch (EmoteKind)
        {
            case Kind.Exclaim:
            {
                var col = new Color("e8456b");
                DrawColoredPolygon(new[] { new Vector2(c.X - 5f, c.Y - 19f), new Vector2(c.X + 5f, c.Y - 19f), new Vector2(c.X + 3f, c.Y + 4f), new Vector2(c.X - 3f, c.Y + 4f) }, col);
                DrawCircle(new Vector2(c.X, c.Y + 14f), 3.8f, col);
                break;
            }
            case Kind.Question:
            {
                var col = new Color("5b8fd9");
                var arc = new Vector2[22];
                for (int i = 0; i < arc.Length; i++)
                {
                    float a = Mathf.DegToRad(Mathf.Lerp(-185f, 40f, i / (float)(arc.Length - 1)));
                    arc[i] = new Vector2(c.X, c.Y - 9f) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 11f;
                }
                DrawPolyline(arc, col, 5f, true);
                DrawLine(arc[^1], new Vector2(c.X, c.Y + 3f), col, 5f, true);
                DrawCircle(new Vector2(c.X, c.Y + 15f), 3.6f, col);
                break;
            }
            case Kind.Heart:
            {
                float pulse = 1f + 0.08f * Mathf.Sin(_age * 14f);   // 輕輕跳動
                var pts = new Vector2[64];
                for (int i = 0; i < pts.Length; i++)
                {
                    float t = Mathf.Tau * i / pts.Length;
                    float hx = 16f * Mathf.Pow(Mathf.Sin(t), 3);
                    float hy = 13f * Mathf.Cos(t) - 5f * Mathf.Cos(2f * t) - 2f * Mathf.Cos(3f * t) - Mathf.Cos(4f * t);
                    pts[i] = new Vector2(c.X + hx * 1.05f * pulse, c.Y + 1f - hy * 1.05f * pulse);
                }
                DrawColoredPolygon(pts, HeartFill);
                var line = new Vector2[pts.Length + 1]; pts.CopyTo(line, 0); line[^1] = pts[0];
                DrawPolyline(line, HeartLine, 1.6f, true);
                break;
            }
            case Kind.Tangle:
                DrawPolyline(_scribble, new Color("3a3a42"), 2.4f, true);
                break;
        }
    }
}
