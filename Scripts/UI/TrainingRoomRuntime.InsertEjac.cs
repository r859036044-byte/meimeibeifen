using Godot;

/// <summary>
/// TrainingRoomRuntime — 內射／外射、拔出後的奶油泡芙、過程中的汗和水。
///
/// 射精按鈕（💦 射精）：
///   • 突破後插入中（抽插中） → 內射：雞雞頂到最深、連續抽動兩輪（對照 Unity EjaculationSequence_Insert），
///     射的過程中會有一點點精液從洞口慢慢往下流；射完回到原本的抽插。內射量會記住。
///   • 其他情況 → 原本的外射（EjaculationSequenceAsync）。
/// 拔出時如果裡面有內射：洞口先鼓出一團精液（奶油泡芙），再慢慢往下流出幾道。
///
/// 汗和水（Unity 只有 汗1~4 / 腿/汗1 / 表情/小穴水 這些圖層，沒有動態邏輯，這裡補上）：
///   • 汗：撫摸、抽插會慢慢累積「出汗量」（越快越多），高潮、射精會一次加很多，沒動作時慢慢退。
///     臉上的汗先出現，接著身體、腿。圖層用淡入淡出，不是突然出現。
///   • 小穴水：高潮值越高越明顯，插入中、內射後更多。
/// </summary>
public partial class TrainingRoomRuntime
{
    // ── Export：內射 ─────────────────────────────────────────────────
    [Export] public float InsertEjacDeepPx     = 25f;    // 內射時比抽插最高點再頂深多少（螢幕 px）
    [Export] public float InsertEjacLeakCount  = 3f;     // 射的過程中從洞口流下來的道數
    // ── Export：奶油泡芙 ─────────────────────────────────────────────
    [Export] public float CreampieBlobPx       = 20f;    // 洞口鼓出來那團的大小（螢幕 px，內射越多越大）
    [Export] public float CreampieFlowLenPx    = 55f;    // 往下流的長度（螢幕 px）
    // ── Export：汗和水 ───────────────────────────────────────────────
    [Export] public float SweatGainTouch       = 0.012f; // 撫摸時每秒累積的出汗量（0~1）
    [Export] public float SweatGainThrust      = 0.02f;  // 抽插時每秒累積（再乘上 1＋速度等級）
    [Export] public float SweatDecay           = 0.004f; // 沒動作時每秒退多少
    [Export] public float SweatOnClimax        = 0.35f;  // 高潮一次加多少
    [Export] public float SweatOnEjac          = 0.15f;  // 射精一次加多少

    bool  _insertEjacActive;
    float _ejacBaseY, _ejacDeepY;
    int   _creampieAmount;       // 這次插入累積的內射次數（拔出時決定奶油泡芙的量）
    float _sweat;                // 出汗量 0~1
    float _pussyWater;           // 小穴水量 0~1（平滑用）
    bool  _sweatInit;

    // ════════════════════════════════════════════════════════════════
    //  射精分流
    // ════════════════════════════════════════════════════════════════

    bool CanInsertEjac => _jijiPhase == 3 && _jijiStage >= 1 && !_pullOutAnim && !_insertEjacActive;

    /// <summary>洞口下緣（洞.png 內容下緣中點）的場景座標。</summary>
    Vector2 HoleBottomWorld()
    {
        Vector2 local = new Vector2(987.5f, 1560f);
        if (Layers.TryGetValue("洞", out var h) && h != null) return h.GetGlobalTransform() * local;
        if (Layers.TryGetValue("m腿/小穴", out var p) && p != null) return p.GetGlobalTransform() * new Vector2(979f, 1560f);
        return CanvasToScreen(local);
    }

    // ════════════════════════════════════════════════════════════════
    //  內射
    // ════════════════════════════════════════════════════════════════

    async System.Threading.Tasks.Task InsertEjaculationAsync()
    {
        _ejacRunning = true;
        _insertEjacActive = true;
        _s2Paused = false; _pullActive = false;
        try
        {
            float s2c = 1f / CharScreenScale();
            float baseY = _jijiInsertY;
            float extra = _jijiInsertY - _s2OscY;                                  // 突破後的上移＋衝刺停留
            float deep  = Mathf.Max(baseY, JijiStage2MaxY + extra) + InsertEjacDeepPx * s2c;
            float back  = deep - 8f * s2c;
            _ejacBaseY = baseY; _ejacDeepY = deep;

            FaceController?.SetExpression("插入2");
            await MoveInsertAsync(baseY, deep, 0.15f);
            await PulseAsync(deep, 2);
            await MoveInsertAsync(deep, back, 0.2f);
            await WaitSecAsync(0.1f);
            await MoveInsertAsync(back, deep, 0.15f);

            // 射的過程中：洞口慢慢流下一點點
            int leaks = Mathf.Max(1, Mathf.RoundToInt(InsertEjacLeakCount * _ejacMul));
            for (int i = 0; i < leaks; i++)
            {
                if (_ejacStop) break;
                TaskLogger.Run(FlowFromHoleAsync(HoleBottomWorld() + new Vector2(Rf(-3f, 3f), 0f),
                                                 Rf(14f, 24f), Rf(1.6f, 2.6f), Rf(7f, 11f),
                                                 _firstTimeBleeding && i % 2 == 1 ? SemenPinkColor : SemenWhiteColor), "TrainingRoom.InsertLeak");
                await PulseAsync(deep, 1);
            }
            await WaitSecAsync(0.3f);

            _creampieAmount += Mathf.Max(1, Mathf.RoundToInt(_ejacMul * 1.5f));
            StartSemenFlow();   // 雞雞上的精液圖層：從尖端慢慢出現，銜接處聚集白色再暈開流開（Jiji.cs）
            _sweat = Mathf.Min(1f, _sweat + SweatOnEjac);
            await MoveInsertAsync(deep, baseY, 0.5f);
            if (IsInstanceValid(this) && _jijiPhase == 3) { _s2Paused = true; _s2SpeedLv = 0f; _pullActive = false; _playForced = false; }   // 內射後停止抽插，回到插入待機
        }
        finally
        {
            _insertEjacActive = false;
            _ejacRunning = false;
        }
    }

    async System.Threading.Tasks.Task WaitSecAsync(float s) =>
        await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);

    /// <summary>內射時直接控制雞雞深度（平滑移動），順便讓腿／插入層跟著頂的程度反應。</summary>
    async System.Threading.Tasks.Task MoveInsertAsync(float from, float to, float dur)
    {
        float t = 0f;
        while (t < dur && IsInstanceValid(this) && _jijiPhase == 3)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            t += (float)GetProcessDeltaTime();
            float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp(t / dur, 0f, 1f));
            _jijiInsertY = Mathf.Lerp(from, to, p);
            // 腿／插入層跟著頂的深度反應（0＝原本位置，1＝最深）
            _s2Sin = Mathf.Clamp((_jijiInsertY - _ejacBaseY) / Mathf.Max(_ejacDeepY - _ejacBaseY, 0.01f), 0f, 1f);
        }
        _jijiInsertY = to;
    }

    /// <summary>頂到底時的抽動（快速小幅上下）。</summary>
    async System.Threading.Tasks.Task PulseAsync(float deep, int times)
    {
        float a = 6f / CharScreenScale();
        for (int i = 0; i < times; i++)
        {
            await MoveInsertAsync(deep, deep + a, 0.06f);
            await MoveInsertAsync(deep + a, deep, 0.1f);
            _sweat = Mathf.Min(1f, _sweat + 0.02f);
            await WaitSecAsync(0.08f);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  流下來的精液（一團頭＋拉長的軌跡）
    // ════════════════════════════════════════════════════════════════

    /// <summary>從 start 慢慢往下流 lengthPx（螢幕 px），頭部是一小團、後面拉出一條變細的軌跡，流完留在原地。</summary>
    /// <param name="angleDeg">流動整體偏離垂直的角度（正＝往右），血流用來讓每一道方向不同</param>
    /// <param name="swayMul">左右彎曲幅度倍率</param>
    /// <param name="easePow">減速曲線的指數（越大越黏稠、後段越慢）</param>
    async System.Threading.Tasks.Task FlowFromHoleAsync(Vector2 start, float lengthPx, float dur, float headPx, Color? color = null,
                                                        float angleDeg = 0f, float swayMul = 1f, float easePow = 2f,
                                                        Node parentOverride = null, bool fadeAway = false)
    {
        if (_canvas == null) return;
        var col = color ?? SemenFlowColor();
        if (col.R > col.G * 3f) start += new Vector2(0f, -3f);   // 血的流出位置往上 3px
        var trailCol = new Color(col.R, col.G, col.B, col.A * 0.82f);
        bool isBloodFlow = col.R > col.G * 3f;
        var parent = parentOverride ?? (isBloodFlow ? BloodParent() : FluidParent());
        var made = fadeAway ? new System.Collections.Generic.List<Control>() : null;   // 血畫在洞／開1／插入上面；精液在雞雞下面
        var head = MkCircleTR(headPx, col);
        parent.AddChild(head); _ejacDebris.Add(head); made?.Add(head);
        if (isBloodFlow) for (int mi = 0; mi < 3; mi++) SpawnBloodMist(parent, start, headPx * Rf(3f, 6f), Rf(0.2f, 0.4f));   // 冒出處的霧化紅

        // 流動路徑不是直線：往下流的同時左右彎曲（兩個不同頻率的正弦波疊加＋整體偏移），
        // 軌跡由很多小段連起來，所以會跟著路徑彎。
        float drift = Rf(-5f, 5f);
        float a1 = Rf(2.0f, 4.0f), f1 = Rf(1.2f, 2.2f), ph1 = Rf(0f, Mathf.Tau);
        float a2 = Rf(0.8f, 1.8f), f2 = Rf(3.0f, 5.0f), ph2 = Rf(0f, Mathf.Tau);
        Vector2 PathAt(float e)   // e：0~1 的流動進度
        {
            float x = drift * e
                    + swayMul * a1 * e * Mathf.Sin(e * f1 * Mathf.Pi + ph1)
                    + swayMul * a2 * e * Mathf.Sin(e * f2 * Mathf.Pi + ph2)
                    + Mathf.Tan(Mathf.DegToRad(angleDeg)) * lengthPx * e;   // 整體偏斜，讓每一道流向不同
            return start + new Vector2(x, lengthPx * e);
        }

        Vector2 lastPt = start;
        float t = 0f;
        while (t < dur && IsInstanceValid(head))
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            t += (float)GetProcessDeltaTime();
            float p = Mathf.Clamp(t / dur, 0f, 1f);
            float ease = 1f - Mathf.Pow(1f - p, easePow);                        // 一開始流比較快，越後面越慢（黏稠）
            Vector2 pos = PathAt(ease);
            float hd = headPx * Mathf.Lerp(1f, 0.8f, p);
            head.Size = new Vector2(hd, hd * Mathf.Lerp(1f, 1.3f, p));
            head.Position = pos - head.Size * 0.5f;

            // 每走 2.5px 補一小段軌跡（寬度隨流動變細）
            while (lastPt.DistanceTo(pos) >= 2.5f)
            {
                Vector2 next = lastPt.MoveToward(pos, 2.5f);
                var seg = MkConnTR(trailCol);
                parent.AddChild(seg); _ejacDebris.Add(seg); made?.Add(seg);
                if (isBloodFlow)
                {
                    // 血：剛出來處集中（上粗），越往下越細；粗細與透明度帶隨機
                    float al = Mathf.Clamp((next.Y - start.Y) / Mathf.Max(lengthPx, 1f), 0f, 1f);
                    float wd = headPx * Mathf.Lerp(1.15f, 0.22f, Mathf.Pow(al, 0.8f)) * Rf(0.7f, 1.25f);
                    float aa = col.A * Rf(0.5f, 1f) * Mathf.Lerp(1f, 0.22f, Mathf.Pow(al, 0.7f));   // 越下面量越少、越透明
                    seg.SelfModulate = new Color(col.R, col.G, col.B, aa);
                    UpdateConnector(seg, lastPt, next, wd);
                    if (Rf(0f, 1f) < 0.4f * (1f - al * 0.6f)) SpawnBloodMist(parent, next, wd * Rf(2.2f, 4f), Rf(0.12f, 0.3f) * (1f - al * 0.5f));
                }
                else
                UpdateConnector(seg, lastPt, next, Mathf.Lerp(headPx * 0.95f, headPx * 0.55f, p));   // 厚一點
                lastPt = next;
            }
        }
        // 血流到底 → 滴到床上留下落紅
        if (fadeAway && made != null)
        {
            // 附著在雞雞上的血：流完後慢慢變淡消失（雞雞動得越久，上面的越模糊淡掉）
            float driftDy = Rf(10f, 20f);   // 附著的血慢慢往下滑一點
            float fadeLife = Rf(10f, 15f);
            foreach (var fn in made)
            {
                if (fn == null || !IsInstanceValid(fn)) continue;
                var ftw = fn.CreateTween();
                ftw.SetParallel(true);
                ftw.TweenProperty(fn, "position:y", fn.Position.Y + driftDy, fadeLife).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
                ftw.TweenProperty(fn, "self_modulate:a", 0f, fadeLife).SetEase(Tween.EaseType.In);
                var nn = fn;
                ftw.Chain().TweenCallback(Callable.From(() => { if (IsInstanceValid(nn)) nn.QueueFree(); }));
            }
            return;
        }
        if (col.R > col.G * 3f && IsInstanceValid(this)) AddBedBloodStain(lastPt.X);
    }

    // ════════════════════════════════════════════════════════════════
    //  奶油泡芙（拔出時由 PullOutAsync 呼叫）
    // ════════════════════════════════════════════════════════════════

    void OnPulledOutWithCreampie()
    {
        // 拔出後破處流血就結束（_firstTimeBleeding 還留著，給這次內射的粉紅精液用）
        _bleedActive = false; _jijiBloodBurst = false;   // 雞雞上沾的血不清掉，會殘留到用衛生紙擦掉為止
        foreach (var r in _jijiBloodRoots) if (r.Outer != null && IsInstanceValid(r.Outer)) r.Outer.QueueFree();   // 拔出後血只從洞流，雞雞上的血流一律收掉
        _jijiBloodRoots.Clear();
        if (_creampieAmount <= 0)
        {
            // 沒有內射但第一次：拔出後洞口流下兩道血
            if (_firstTimeBleeding) { TaskLogger.Run(BloodFlowsAsync(2, 0.35f), "TrainingRoom.PullOutBlood"); _firstTimeBleeding = false; }   // 只在破處後第一次拔出流
            return;
        }
        int amount = Mathf.Min(_creampieAmount, 3);
        _creampieAmount = 0;
        TaskLogger.Run(CreampieAsync(amount), "TrainingRoom.Creampie");
    }

    async System.Threading.Tasks.Task CreampieAsync(int amount)
    {
        if (_canvas == null) return;
        await WaitSecAsync(0.25f);                                        // 拔出後停一下才鼓出來
        Vector2 hole = HoleBottomWorld() + new Vector2(0f, -14f);  // -4 → -9 → -14（再往上 5）

        // 洞口鼓出來的一團：從 0 慢慢長大
        float size = CreampieBlobPx * (0.8f + 0.25f * amount);
        var blob = new TextureRect
        {
            Texture = CreateSemenBlobTex(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Size = new Vector2(size, size * 0.85f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SelfModulate = _firstTimeBleeding ? new Color(1f, 0.72f, 0.72f, 0.94f) : new Color(1f, 1f, 1f, 0.92f),   // 破處：混著血偏粉紅
        };
        blob.PivotOffset = new Vector2(size * 0.5f, 0f);                  // 從上緣（洞口）往下鼓
        blob.Position = hole - new Vector2(size * 0.5f, size * 0.15f);
        blob.Scale = Vector2.Zero;
        FluidParent().AddChild(blob); _ejacDebris.Add(blob);

        float t = 0f, grow = 0.9f;
        while (t < grow && IsInstanceValid(blob))
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            t += (float)GetProcessDeltaTime();
            float p = Mathf.Clamp(t / grow, 0f, 1f);
            float s = 1f - Mathf.Pow(1f - p, 3f);
            blob.Scale = new Vector2(s, s * Mathf.Lerp(0.6f, 1f, p));      // 先橫向鼓出，再往下垂
        }

        // 從泡芙底部慢慢往下流出幾道
        int flows = 1 + amount + (_firstTimeBleeding ? 1 : 0);
        for (int i = 0; i < flows; i++)
        {
            Vector2 from = hole + new Vector2(Rf(-size * 0.3f, size * 0.3f), size * 0.55f);
            // 第一次內射（破處）：白色精液、血絲、混血粉紅 三種輪流混在一起流出
            Color fc = !_firstTimeBleeding ? SemenWhiteColor
                     : (i % 3 == 0 ? SemenWhiteColor : i % 3 == 1 ? BloodColor : SemenPinkColor);
            bool isBlood = _firstTimeBleeding && i % 3 == 1;
            TaskLogger.Run(FlowFromHoleAsync(from, CreampieFlowLenPx * Rf(0.6f, 1.1f), Rf(2.0f, 3.2f), isBlood ? Rf(5f, 7f) : Rf(9f, 13f), fc),
                           "TrainingRoom.CreampieFlow");
            await WaitSecAsync(Rf(0.25f, 0.5f));
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  汗和水
    // ════════════════════════════════════════════════════════════════

    static readonly (string layer, float threshold)[] _sweatLayers =
    {
        ("汗4", 0.15f),     // 臉（額頭）最先出汗
        ("汗1", 0.30f),     // 胸口
        ("汗2", 0.45f),     // 身體
        ("腿/汗1", 0.55f),  // 腿
        ("汗3", 0.65f),     // 下半身
    };

    void UpdateSweatAndWater(float dt)
    {
        UpdateBleeding(dt);
        if (!_sweatInit)
        {
            _sweatInit = true;
            foreach (var (n, _) in _sweatLayers) { SetLayerQuiet(n, true); ApplyFlowShader(n, 0.12f, 38f); }
            SetLayerQuiet("表情/小穴水", true);
            ApplyFlowShader("表情/小穴水", 0.18f, 26f);
        }

        bool touching = _touchAnimRunningLeft || _touchAnimRunningRight || _bTouchRunning;
        bool thrusting = _jijiPhase == 3 && _jijiStage >= 1 && !_s2Paused;
        float gain = 0f;
        if (touching) gain += SweatGainTouch * (1f + _breastSpeedLevel * 0.3f);
        if (thrusting) gain += SweatGainThrust * (1f + _s2SpeedLv * 0.5f);
        if (_resistBlocking || _pullActive) gain += SweatGainThrust * 0.8f;   // 用力的時候也會出汗
        _sweat = gain > 0f ? Mathf.Min(1f, _sweat + gain * dt) : Mathf.Max(0f, _sweat - SweatDecay * dt);

        foreach (var (n, th) in _sweatLayers)
        {
            if (!Layers.TryGetValue(n, out var c) || c == null) continue;
            float a = Mathf.Clamp((_sweat - th) / 0.2f, 0f, 1f);
            c.Modulate = new Color(1f, 1f, 1f, a * a * (3f - 2f * a));
        }

        // 小穴水：高潮值 30 開始出現、越高越多；插入中／內射過更多
        float target = Mathf.Clamp((ClimaxLevel - 30f) / 60f, 0f, 1f);
        if (_jijiPhase == 3) target = Mathf.Max(target, 0.5f);
        if (_creampieAmount > 0 || _spasmsRunning) target = 1f;
        _pussyWater = Mathf.Lerp(_pussyWater, target, 1f - Mathf.Exp(-1.5f * dt));
        if (Layers.TryGetValue("表情/小穴水", out var w) && w != null)
            w.Modulate = new Color(1f, 1f, 1f, _pussyWater);
    }

    // 汗／水的「往下流動、流完重生」效果：把貼圖切成一條一條直欄，每欄以不同速度往下滑一小段、
    // 滑到底淡出後回到原位重新出現（重生），同時保留一層淡淡的靜態底圖，不會整個消失。
    static Shader _flowShader;

    void ApplyFlowShader(string layer, float speed, float slidePx)
    {
        if (!Layers.TryGetValue(layer, out var c) || c == null || c.Material != null) return;
        _flowShader ??= new Shader
        {
            Code = @"shader_type canvas_item;
uniform float speed = 0.12;      // 每秒幾個循環
uniform float slide_px = 38.0;   // 每次往下滑多少（貼圖像素）
uniform float col_px = 22.0;     // 每條直欄寬（貼圖像素）
uniform float base_alpha = 0.35; // 靜態底圖的透明度
varying vec4 vcol;
void vertex() { vcol = COLOR; }
float hash(float n) { return fract(sin(n * 12.9898) * 43758.5453); }
void fragment() {
    float col = floor(UV.x / (TEXTURE_PIXEL_SIZE.x * col_px));
    float h = hash(col);
    float cyc = fract(TIME * speed * (0.6 + h * 0.9) + h);
    float off = cyc * slide_px * TEXTURE_PIXEL_SIZE.y;
    vec4 moving = texture(TEXTURE, UV - vec2(0.0, off));
    moving.a *= smoothstep(0.0, 0.18, cyc) * (1.0 - smoothstep(0.7, 1.0, cyc));   // 出現→往下滑→淡出→重生
    moving.rgb *= 1.0;
    vec4 base = texture(TEXTURE, UV);
    base.a *= base_alpha;
    vec4 c = moving.a > base.a ? moving : base;
    COLOR = c * vcol;
}"
        };
        var mat = new ShaderMaterial { Shader = _flowShader };
        mat.SetShaderParameter("speed", speed);
        mat.SetShaderParameter("slide_px", slidePx);
        c.Material = mat;
    }

    // ════════════════════════════════════════════════════════════════
    //  第一次（破處）流血
    // ════════════════════════════════════════════════════════════════
    [Export] public float BloodDripIntervalMin = 1.8f;   // 抽插中偶爾從洞口滲出血的間隔（秒）
    [Export] public float BloodDripIntervalMax = 3.6f;
    [Export] public float JijiBloodGainPerSec  = 0.12f; // 抽插時雞雞沾血增加速度（0~1）

    static readonly Color BloodColor = new Color(0.88f, 0.09f, 0.11f, 0.92f);   // 使用者反應太暗太黑 → 更鮮豔的紅
    bool  _firstTimeBleeding;   // 這次插入是破處（流血、混血）
    float _jijiBloodFlowT;
    int _jijiBloodFlowActive;
    bool _jijiBloodBurst;
    float _jijiCutLocalY;   // 雞雞裁切線（入口）在雞雞圖層本地的 Y（Jiji.cs 每幀寫入）
    float _jijiFlush;       // 0~1：插入時雞雞入口的淡淡紅暈，平滑漸入漸出
    float _jijiBloodBlur;   // 0~1：雞雞上的血隨抽插越來越模糊淡掉（Jiji.cs shader blood_blur）
    float _jijiBlood;           // 雞雞沾的血 0~1（Jiji.cs 裁切 shader 的 blood_amt）
    float _bloodDripTimer;
    bool  _bleedActive;                     // 破處流血進行中（拔出後結束）
    int   _holeBloodDrips;                  // 這次破處洞口已經滲出的血滴數
    [Export] public int HoleBloodDripMax = 8;   // 洞口滲血的總數上限（雞雞上的血會自己淡掉，不設限）
    float _bloodSeed;
    float _bleedT;                                      // 破處後流血已經過了幾秒
    [Export] public float BleedStopSec = 60f;           // 破處後流血持續幾秒，超過就不再流血／滲血

    /// <summary>突破時呼叫：還是處女 → 破處（PlayerDataManager.TriggerVirginityLoss），開始流血。</summary>
    void OnBreakthroughBleed()
    {
        var pdm = PlayerDataManager.Instance;
        if (pdm == null || !pdm.IsVirgin) { _firstTimeBleeding = false; _bleedActive = false; return; }   // 已不是處女：不再觸發
        pdm.TriggerVirginityLoss();
        _firstTimeBleeding = true; _bleedActive = true; _bleedT = 0f;
        _bloodSeed = Rf(0f, 100f);   // 每次破處血痕分布都不同
        _jijiBlood = Mathf.Max(_jijiBlood, 0.35f);
        _jijiBloodBlur = 0f;
        _jijiBloodBurst = true;
        _holeBloodDrips = 0;
        _bloodDripTimer = Rf(BloodDripIntervalMin, BloodDripIntervalMax);
        TaskLogger.Run(BloodFlowsAsync(2, 0.2f), "TrainingRoom.BreakBlood");
        GD.Print("[Jiji] 破處 → 流血");
    }

    /// <summary>血的霧化紅暈：隨機大小／透明度的柔邊紅點，慢慢擴散淡掉（不是固定色塊）。</summary>
    void SpawnBloodMist(Node parent, Vector2 pos, float size, float alpha)
    {
        if (parent == null || !IsInstanceValid(parent)) return;
        var sz = new Vector2(size * Rf(0.8f, 1.3f), size * Rf(0.8f, 1.3f));
        var n = new TextureRect
        {
            Texture = GetSoftPuddleTex(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Size = sz,
            Position = pos + new Vector2(Rf(-3f, 3f), Rf(-3f, 3f)) - sz * 0.5f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SelfModulate = new Color(0.95f, 0.06f, 0.09f, alpha),
            PivotOffset = sz * 0.5f,
        };
        parent.AddChild(n);
        var tw = n.CreateTween();
        float life = Rf(0.9f, 1.8f);
        tw.SetParallel(true);
        tw.TweenProperty(n, "self_modulate:a", 0f, life);
        tw.TweenProperty(n, "scale", new Vector2(Rf(1.3f, 1.8f), Rf(1.3f, 1.8f)), life);
        tw.Chain().TweenCallback(Callable.From(() => { if (IsInstanceValid(n)) n.QueueFree(); }));
    }

    async System.Threading.Tasks.Task BloodFlowsAsync(int count, float delay)
    {
        // 一定有一條主流：幾乎直直往下、量比較多（較粗較長），只有輕微彎曲；其餘左右流動限定在小範圍內
        for (int i = 0; i <= count; i++)
        {
            await WaitSecAsync(delay + i * Rf(0.2f, 0.45f));
            if (i == 0)
            {
                TaskLogger.Run(FlowFromHoleAsync(HoleBottomWorld() + new Vector2(Rf(-2f, 2f), 0f),
                                                 Rf(40f, 58f), Rf(2.6f, 3.6f), Rf(5.5f, 7f), BloodColor,
                                                 Rf(-3f, 3f), Rf(0.6f, 1.1f), Rf(1.6f, 2.4f)), "TrainingRoom.BloodMain");
                continue;
            }
            float side = (i % 2 == 0) ? -1f : 1f;
            TaskLogger.Run(FlowFromHoleAsync(HoleBottomWorld() + new Vector2(side * Rf(1f, 4f), 0f),
                                             Rf(16f, 34f), Rf(1.8f, 3.0f), Rf(3.2f, 4.8f), BloodColor,
                                             side * Rf(4f, 12f), Rf(0.8f, 1.5f), Rf(1.6f, 3.0f)), "TrainingRoom.BloodFlow");
        }
    }

    // 雞雞上的血流：每一道各自有一組「外層裁切框＋內層」容器（雞雞的子物件）。
    // 外層跟著雞雞目前的入口裁切線、並裁掉線以上；內層固定在流出當下的位置，所以血附著在雞雞上跟著動，
    // 雞雞往上縮進洞裡時，血也像雞雞一樣被裁切看不到。
    sealed class JijiBloodRoot { public Control Outer, Inner; public float CutAtSpawn; public Vector2 Gs; }
    readonly System.Collections.Generic.List<JijiBloodRoot> _jijiBloodRoots = new();

    /// <summary>每幀由 Jiji.cs 呼叫：讓每道血的裁切框跟著目前的入口裁切線。</summary>
    void UpdateJijiBloodRoots()
    {
        Layers.TryGetValue("雞雞", out var jj);
        Transform2D jjInv = jj != null ? jj.GetGlobalTransform().AffineInverse() : Transform2D.Identity;
        for (int i = _jijiBloodRoots.Count - 1; i >= 0; i--)
        {
            var r = _jijiBloodRoots[i];
            if (r.Outer == null || !IsInstanceValid(r.Outer)) { _jijiBloodRoots.RemoveAt(i); continue; }
            r.Outer.Position = new Vector2(0f, _jijiCutLocalY);
            r.Inner.Position = new Vector2(0f, (r.CutAtSpawn - _jijiCutLocalY) * r.Gs.Y);

            // 血流裁切：用跟雞雞 shader 完全相同的形狀（水平線＋左右兩個圓）逐個節點裁切。
            // 原本只靠 Outer 的 ClipContents（長方形、只有水平線），沒有圓形裁切，
            // 而且雞雞傾斜時長方形裁切框會歪掉，所以血會露在雞雞已經被裁掉的地方。
            // 這裡改成：把每個血節點的中心換算到雞雞本地座標（含旋轉／縮放），算出保留比例，
            // 用 Modulate 的透明度淡出（不跟 self_modulate 的淡出動畫互相覆蓋）。
            if (jj == null) continue;
            foreach (var ch in r.Inner.GetChildren())
            {
                if (ch is not Control c || !IsInstanceValid(c)) continue;
                Vector2 pl = jjInv * (c.GetGlobalTransform() * (c.Size * 0.5f));
                float keep = JijiClipKeepLocal(pl);
                if (!Mathf.IsEqualApprox(c.Modulate.A, keep)) c.Modulate = new Color(1f, 1f, 1f, keep);
            }
        }
    }

    /// <summary>雞雞本地座標（貼圖像素）的點 p 在裁切後保留多少（0＝被裁掉，1＝完整保留）。
    /// 公式跟 Jiji.cs EnsureJijiClip() 的 shader 一致：水平（含傘形）裁切線＋左右兩個裁切圓。</summary>
    float JijiClipKeepLocal(Vector2 p)
    {
        if (!JijiClipEnabled) return 1f;
        float s2c = 1f / CharScreenScale();                        // 螢幕 px → 貼圖 px
        float f = Mathf.Max(JijiClipFeatherPx * s2c, 0.01f);       // 邊緣柔化
        float dx = Mathf.Clamp(Mathf.Abs(p.X - 984.5f) / 134.5f, 0f, 1f);
        float cut = _jijiCutLocalY + JijiClipArchPx * s2c * dx * dx;
        float keep = Mathf.SmoothStep(cut - f, cut + f, p.Y);
        if (JijiClipCircleRadius > 0f)
        {
            float off = ClipCircleOffsetEff * s2c, r = JijiClipCircleRadius * s2c, dip = JijiClipCircleDip * s2c;
            float cy = _jijiCutLocalY + dip - r - JijiTouchDepth() * JijiClipCircleFollow;
            float dl = p.DistanceTo(new Vector2(998f - off, cy));
            float dr = p.DistanceTo(new Vector2(998f + off, cy));
            keep *= Mathf.SmoothStep(r - f, r + f, Mathf.Min(dl, dr));
        }
        return keep;
    }

    /// <summary>從雞雞與插入的銜接處（入口裁切線）冒出一道血，沿著雞雞往下流，之後慢慢往下滑、淡掉。</summary>
    async System.Threading.Tasks.Task SpawnJijiBloodFlowAsync(float delay)
    {
        if (delay > 0f) await WaitSecAsync(delay);
        if (!Layers.TryGetValue("雞雞", out var jj) || jj == null || !IsInstanceValid(jj)) return;
        var s = jj.GetGlobalTransform().Scale;
        var gs = new Vector2(Mathf.Max(0.01f, Mathf.Abs(s.X)), Mathf.Max(0.01f, Mathf.Abs(s.Y)));
        var outer = new Control
        {
            Name = "JijiBloodClip", MouseFilter = Control.MouseFilterEnum.Ignore, ClipContents = false,   // 裁切改由 UpdateJijiBloodRoots() 逐節點計算（含圓形、含傾斜）
            Scale = new Vector2(1f / gs.X, 1f / gs.Y), Position = new Vector2(0f, _jijiCutLocalY),
            Size = new Vector2(4000f, 3000f),
        };
        var inner = new Control { Name = "JijiBloodInner", MouseFilter = Control.MouseFilterEnum.Ignore };
        outer.AddChild(inner);
        jj.AddChild(outer);
        _jijiBloodRoots.Add(new JijiBloodRoot { Outer = outer, Inner = inner, CutAtSpawn = _jijiCutLocalY, Gs = gs });
        var start = new Vector2((998f + Rf(-85f, 85f)) * gs.X, 3f + Mathf.Max(0f, _junctionSmooth - _jijiCutLocalY) * gs.Y);   // 和紅暈（平滑後的入口線）對齊   // 血的流出會再往上 3px → 正好在裁切線上
        _jijiBloodFlowActive++;
        try
        {
            await FlowFromHoleAsync(start, Rf(30f, 70f), Rf(2.8f, 4.5f), Rf(3.5f, 6f), BloodColor,
                                    Rf(-6f, 6f), Rf(0.6f, 1.2f), Rf(1.6f, 2.6f), inner, true);
        }
        finally { _jijiBloodFlowActive--; }
        // 淡出與下滑最久約 16 秒，之後整組容器回收
        if (IsInstanceValid(outer))
        {
            var tw = outer.CreateTween();
            tw.TweenInterval(17f);
            tw.TweenCallback(Callable.From(() => { if (IsInstanceValid(outer)) outer.QueueFree(); }));
        }
    }

    /// <summary>每幀：破處後抽插時雞雞越來越沾血，偶爾洞口再滲一點血。</summary>
    void UpdateBleeding(float dt)
    {
        if (_bleedActive)
        {
            _bleedT += dt;
            if (_bleedT >= BleedStopSec) { _bleedActive = false; GD.Print("[Jiji] 流血結束（已滿 " + BleedStopSec + " 秒）"); }
        }
        if (!_bleedActive || _jijiPhase != 3 || _jijiStage < 1) return;
        bool thrusting = !_s2Paused && !_pullOutAnim;
        if (thrusting) _jijiBlood = Mathf.Min(1f, _jijiBlood + JijiBloodGainPerSec * dt * (0.5f + _s2Sin));
        if (thrusting) _jijiBloodBlur = Mathf.Min(0.55f, _jijiBloodBlur + 0.05f * dt * (0.4f + _s2Sin));
        bool bloodSettled = _insFollowT > 1.2f;   // 剛插入（突破衝上去）那一下先不冒血，等插穩了再開始
        if (_jijiBloodBurst && bloodSettled)
        {
            _jijiBloodBurst = false;
            for (int bi = 0; bi < 2; bi++) TaskLogger.Run(SpawnJijiBloodFlowAsync(bi * 0.25f), "TrainingRoom.JijiBloodBurst");
        }
        if (bloodSettled) _jijiBloodFlowT -= dt;
        if (bloodSettled && _jijiBloodFlowT <= 0f && _jijiBloodFlowActive < 4 && !_pullOutAnim)
        {
            _jijiBloodFlowT = Rf(2.2f, 4.2f);
            TaskLogger.Run(SpawnJijiBloodFlowAsync(0f), "TrainingRoom.JijiBloodFlow");
        }
        _bloodDripTimer -= dt;
        if (_bloodDripTimer <= 0f && _holeBloodDrips < HoleBloodDripMax)
        {
            _holeBloodDrips++;
            _bloodDripTimer = Rf(BloodDripIntervalMin, BloodDripIntervalMax);
            TaskLogger.Run(FlowFromHoleAsync(HoleBottomWorld() + new Vector2(Rf(-5f, 5f), -4f),
                                             Rf(14f, 34f), Rf(1.5f, 2.8f), Rf(3.2f, 5f), BloodColor,
                                             Rf(-8f, 8f), Rf(0.7f, 1.3f), Rf(1.6f, 3.0f)), "TrainingRoom.BloodDrip");
        }
    }

    static readonly Color SemenWhiteColor = new Color(0.97f, 0.95f, 0.9f, 0.88f);
    static readonly Color SemenPinkColor  = new Color(0.96f, 0.72f, 0.72f, 0.88f);

    // ── 床上落紅：血流到底之後滴到床上，留下一塊會慢慢擴大的血漬（畫在角色後面、床上）──
    [Export] public float BedStainOffsetY = 70f;    // 血漬在洞口下方多遠（螢幕 px）
    [Export] public float BedStainMaxPx   = 46f;    // 血漬最大寬度（螢幕 px）
    TextureRect _bedStain;
    float _bedStainW;

    void AddBedBloodStain(float x)
    {
        if (_canvas == null) return;
        Vector2 center = HoleBottomWorld() + new Vector2(0f, BedStainOffsetY);
        if (_bedStain == null || !IsInstanceValid(_bedStain))
        {
            _bedStainW = 10f;
            _bedStain = MakeBedStainBlob(center, _bedStainW, new Color(0.84f, 0.07f, 0.10f, 0.68f));
        }
        else
        {
            _bedStainW = Mathf.Min(BedStainMaxPx, _bedStainW + Rf(3f, 6f));
            var sz = new Vector2(_bedStainW, _bedStainW * 0.55f);
            _bedStain.Size = sz;
            _bedStain.Position = center - sz * 0.5f;
        }
        // 周圍再點一小滴（不規則的落紅）
        Vector2 sat = new Vector2(x, center.Y) + new Vector2(Rf(-8f, 8f), Rf(-4f, 5f));
        MakeBedStainBlob(sat, Rf(4f, 8f), new Color(0.80f, 0.06f, 0.09f, 0.62f));
    }

    TextureRect MakeBedStainBlob(Vector2 center, float w, Color col)
    {
        var sz = new Vector2(w, w * 0.55f);
        var n = new TextureRect
        {
            Texture = GetSoftPuddleTex(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Size = sz, Position = center - sz * 0.5f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SelfModulate = col,
        };
        _canvas.AddChild(n);
        if (CharCanvasChild != null && IsInstanceValid(CharCanvasChild) && CharCanvasChild.GetParent() == _canvas)   // 人物外面包了光影 CanvasGroup，用最外層節點判斷
            _canvas.MoveChild(n, CharCanvasChild.GetIndex());   // 畫在角色後面（床上）
        _ejacDebris.Add(n);
        return n;
    }

    /// <summary>精液流的顏色：第一次（破處）混一點血偏粉紅。</summary>
    Color SemenFlowColor() => _firstTimeBleeding
        ? new Color(0.97f, 0.78f, 0.78f, 0.85f)
        : new Color(0.97f, 0.95f, 0.9f, 0.85f);

    // ════════════════════════════════════════════════════════════════
    //  液體圖層：放在雞雞正下面（奶油泡芙、流出的精液、血都畫在這裡）
    // ════════════════════════════════════════════════════════════════
    Control _fluidLayer;

    /// <summary>液體節點的父節點。容器掛在 _charRoot 底下、雞雞正前面，並抵銷 _charRoot 的縮放/位移，
    /// 所以子節點照樣用「_canvas 座標」擺放（跟之前一樣），只是畫的順序變成在雞雞下面。</summary>
    Node FluidParent()
    {
        UpdateFluidLayer();
        return _fluidLayer != null ? _fluidLayer : _canvas;
    }

    Control _bloodLayer;
    float _lowerBreathY;   // 下半身呼吸位移（Effects.cs 每幀寫入），血／液體層跟著動

    /// <summary>血流的父節點：放在「插入」層正上面（也就在洞、開1、開上上面），座標一樣抵銷 _charRoot 變換。</summary>
    Node BloodParent()
    {
        UpdateFluidLayer();
        return _bloodLayer != null ? _bloodLayer : _canvas;
    }

    void UpdateFluidLayer()
    {
        if (_charRoot == null) return;
        if (_fluidLayer == null || !IsInstanceValid(_fluidLayer))
        {
            _fluidLayer = new Control { Name = "InsertFluidLayer", MouseFilter = Control.MouseFilterEnum.Ignore };
            _charRoot.AddChild(_fluidLayer);
        }
        var inv = _charRoot.GetTransform().AffineInverse();
        _fluidLayer.Scale = inv.Scale;
        _fluidLayer.Position = inv.Origin;

        if (_bloodLayer == null || !IsInstanceValid(_bloodLayer))
        {
            _bloodLayer = new Control { Name = "InsertBloodLayer", MouseFilter = Control.MouseFilterEnum.Ignore };
            _charRoot.AddChild(_bloodLayer);
        }
        _bloodLayer.Scale = inv.Scale;
        // 血／液體要跟著下半身（洞、小穴）的呼吸與 M 腿推動一起位移
        Vector2 follow = new Vector2(0f, _lowerBreathY);
        _bloodLayer.Position = inv.Origin + follow;
        _fluidLayer.Position = inv.Origin + follow;
        // 找「插入／開1／開上／洞」裡排最上面的那個，血層放在它正上面
        int top = -1;
        foreach (var n in new[] { "插入", "開1", "開上", "洞" })
            if (Layers.TryGetValue(n, out var ov) && ov != null && ov.GetParent() == _charRoot) top = Mathf.Max(top, ov.GetIndex());
        bool inserting = _jijiPhase == 3 && _jijiStage >= 1 && !_pullOutAnim;
        bool hasJiji = Layers.TryGetValue("雞雞", out var jj) && jj != null && jj.GetParent() == _charRoot;
        if (hasJiji)
        {
            int ji = jj.GetIndex(), fi = _fluidLayer.GetIndex();
            if (fi != ji - 1) _charRoot.MoveChild(_fluidLayer, fi < ji ? ji - 1 : ji);   // 放到雞雞正下面
        }
        if (hasJiji)
        {
            // 血一律在洞／開1 上面、雞雞下面（插入中與拔出後都一樣）
            int ji = jj.GetIndex(), bi = _bloodLayer.GetIndex();
            if (bi != ji - 1) _charRoot.MoveChild(_bloodLayer, bi < ji ? ji - 1 : ji);
        }
        else if (top >= 0)
        {
            int bi = _bloodLayer.GetIndex();
            if (bi != top + 1) _charRoot.MoveChild(_bloodLayer, bi < top ? top : top + 1);   // 拔出後：維持在插入層上面
        }
    }

    /// <summary>由 TriggerClimax 呼叫：高潮一次出很多汗。</summary>
    void OnClimaxSweat() => _sweat = Mathf.Min(1f, _sweat + SweatOnClimax);
}
