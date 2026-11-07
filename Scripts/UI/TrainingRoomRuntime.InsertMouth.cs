using Godot;
using System.Collections.Generic;

/// <summary>
/// TrainingRoomRuntime — 插入時的嘴巴大小：
/// 剛開始插入（突破）時嘴巴（小嘴／抖動嘴／小嘴吐舌…目前顯示的那張）比較大，
/// 小穴舒服度（ComfortHole）越高就越小，舒服到 InsertMouthComfortFull 時回到正常大小；
/// 拔出／沒插入時慢慢回到正常。縮放以嘴巴圖的內容中心為準（不是整張畫布中心）。
/// </summary>
public partial class TrainingRoomRuntime
{
    [ExportGroup("插入時嘴巴大小")]
    [Export(PropertyHint.Range, "1,3,0.01")]   public float InsertMouthBigScale = 1.5f;        // 剛開始插入時嘴巴放大倍率（相對於脫衣後縮小的嘴巴再放大 50%）
    [Export(PropertyHint.Range, "1,200,1")]    public float InsertMouthComfortFull = 100f;      // 小穴舒服度到這個值時嘴巴縮回正常大小
    [Export(PropertyHint.Range, "0.5,20,0.1")] public float InsertMouthLerpSpeed = 5f;          // 大小變化的跟隨速度

    [Export(PropertyHint.Range, "0.5,1,0.01")] public float MouthUndressScale = 0.85f;   // 被脫（內褲脫掉／撥開、衣服拉開、掰腿…）時嘴巴縮成小嘴巴的縮小版（1＝不縮）
    [Export(PropertyHint.Range, "0.2,3,0.05")] public float MouthUndressFull = 1f;           // 暴露度累積到這個值時嘴巴縮到最小
    [Export(PropertyHint.Range, "0.4,1,0.01")] public float MouthFullUndressScaleX = 0.8f;   // 完全脫掉（內褲脫掉）時嘴巴左右再壓縮成幾倍（抖動嘴那種扁扁的）；1＝不壓
    float _imFull;   // 完全脫掉的平滑權重 0~1
    float _imScale = 1f;
    readonly Dictionary<string, (Vector2 pos, Vector2 off)> _imApplied = new();

    /// <summary>每幀（HeadPat.cs，在所有圖層位置／摸頭疊加之後）呼叫。</summary>
    void UpdateInsertMouth(float dt)
    {
        bool inserted = _jijiPhase == 3 && _jijiStage >= 1 && !_pullOutAnim;
        float comfort01 = Mathf.Clamp(ComfortHole / Mathf.Max(1f, InsertMouthComfortFull), 0f, 1f);
        float base_ = Mathf.Lerp(1f, MouthUndressScale, Mathf.Clamp(ExposureScore() / Mathf.Max(0.05f, MouthUndressFull), 0f, 1f));   // 被脫得越多嘴巴越小
        float target = base_ * (inserted ? Mathf.Lerp(InsertMouthBigScale, 1f, comfort01) : 1f);
        _imScale = Mathf.Lerp(_imScale, target, 1f - Mathf.Exp(-InsertMouthLerpSpeed * dt));
        if (!inserted && Mathf.Abs(_imScale - target) < 0.002f) _imScale = target;

        _imFull = Mathf.Lerp(_imFull, _pantyGone ? 1f : 0f, 1f - Mathf.Exp(-InsertMouthLerpSpeed * dt));
        float sxK = Mathf.Lerp(1f, MouthFullUndressScaleX, _imFull);   // 完全脫掉：左右壓縮
        Vector2 mScale = new Vector2(_imScale * sxK, _imScale);

        foreach (var name in _mouthLayerNames)
        {
            if (!Layers.TryGetValue(name, out var c) || c == null) continue;

            // 先扣掉上一幀自己加的位移（如果這一幀沒有別的系統重算過位置）
            Vector2 pos = c.Position;
            bool had = _imApplied.TryGetValue(name, out var prev);
            if (had)
            {
                if (pos.IsEqualApprox(prev.pos)) pos -= prev.off;
                _imApplied.Remove(name);
            }

            if ((Mathf.Abs(mScale.X - 1f) < 0.0005f && Mathf.Abs(mScale.Y - 1f) < 0.0005f) || !c.Visible)
            {
                c.Position = pos;
                if (had) c.Scale = Vector2.One;
                continue;
            }

            Vector2 m = LayerContentRect(name)?.GetCenter() ?? c.PivotOffset;   // 嘴巴圖內容中心（圖層本地座標）
            Vector2 off = new Vector2((1f - mScale.X) * (m.X - c.PivotOffset.X), (1f - mScale.Y) * (m.Y - c.PivotOffset.Y));   // 以內容中心為縮放中心
            c.Scale = mScale;
            c.Position = pos + off;
            _imApplied[name] = (c.Position, off);
        }
    }

    // ── 拔出後的表情：抖動嘴 2 秒 → 小嘴巴 → 越來越平淡 ──
    [ExportGroup("拔出後表情")]
    [Export] public string[] PullOutExprSeq  = { "插入1", "插入2" };   // 插入1＝抖動嘴、插入2＝小嘴巴，後面逐漸平淡
    [Export] public float[]  PullOutExprSecs = { 2f };                               // 每個表情維持幾秒（最後一個維持到被別的表情取代）
    int _pullExprGen;

    async System.Threading.Tasks.Task PullOutExprSeqAsync()
    {
        int gen = ++_pullExprGen;
        string prev = null;
        for (int i = 0; i < PullOutExprSeq.Length; i++)
        {
            if (!IsInstanceValid(this) || FaceController == null || gen != _pullExprGen) return;
            if (i > 0 && FaceController.CurrentExpression != prev) return;   // 被別的系統（高潮、再次插入…）換掉了：不再接手
            FaceController.SetExpression(PullOutExprSeq[i]);
            prev = PullOutExprSeq[i];
            if (i >= PullOutExprSeq.Length - 1) return;
            float sec = i < PullOutExprSecs.Length ? PullOutExprSecs[i] : 2f;
            await ToSignal(GetTree().CreateTimer(Mathf.Max(0.1f, sec)), SceneTreeTimer.SignalName.Timeout);
            if (!IsInstanceValid(this) || _jijiPhase == 3) return;   // 又插進去了
        }
    }
}
