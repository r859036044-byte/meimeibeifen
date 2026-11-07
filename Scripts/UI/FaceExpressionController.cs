using Godot;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// FaceExpressionController（Unity → Godot 移植版）
/// 以 Visible 切換的方式驅動角色臉部表情。
///
/// 使用方式：
///   1. PartGroups：填每個臉部部位的所有互斥圖層名稱
///   2. Expressions：填每種表情要顯示哪個圖層
///   3. 呼叫 SetExpression("害怕") 即可切換。
/// </summary>
public partial class FaceExpressionController : Node
{
    // ════════════════════════════════════════════════════════════
    //  Export
    // ════════════════════════════════════════════════════════════

    [Export] public NodePath RuntimePath;

    [Export] public FacePartGroup[] PartGroups = System.Array.Empty<FacePartGroup>();
    [Export] public ExpressionDef[] Expressions = System.Array.Empty<ExpressionDef>();

    [ExportGroup("閉眼控制")]
    [Export] public NodePath EyeCloseCtrlPath;
    [Export] public string[] CloseEyeExpressions  = new[] { "忍耐", "做愛(全閉)" };
    [Export] public string[] Closed2EyeExpressions = System.Array.Empty<string>();
    /// <summary>全閉3：翻轉眼皮（EyeCloseController.EyelidFlipParts），目前只有 ZZZ 用。
    /// 跟 Closed2EyeExpressions（翻轉睫毛，「做愛4」對照 Unity 真實資料）是完全獨立的機制，
    /// 不會互相影響。</summary>
    [Export] public string[] Closed3EyeExpressions = System.Array.Empty<string>();
    [Export] public string[] HalfCloseEyeExpressions = new[] { "做愛(半閉)" };
    [Export] public string[] HalfClose2EyeExpressions = System.Array.Empty<string>();

    [ExportGroup("初始設定")]
    [Export] public string DefaultExpression = "";

    // ════════════════════════════════════════════════════════════
    //  公開屬性
    // ════════════════════════════════════════════════════════════

    public string CurrentExpression => _currentExpr;

    /// <summary>供 DEV 面板顯示除錯用：InitAsync() 是否已經跑完（Expressions/PartGroups 是否已就緒）。</summary>
    public bool IsReady => _ready;

    // ════════════════════════════════════════════════════════════
    //  私有
    // ════════════════════════════════════════════════════════════

    readonly Dictionary<string, ExpressionDef> _exprMap
        = new Dictionary<string, ExpressionDef>();
    readonly Dictionary<string, HashSet<string>> _groupLayerSets
        = new Dictionary<string, HashSet<string>>();

    string             _currentExpr = "";
    bool               _ready       = false;
    TrainingRoomRuntime _runtime;
    EyeCloseController  _eyeCloseCtrl;

    // 表情專屬 Transform 覆寫
    struct ExprTransform
    {
        public string LayerPath;
        public float  RotZ;
        public float  OffsetX;
        public float  OffsetY;
    }

    static readonly Dictionary<string, ExprTransform[]> _exprTransformMap
        = new Dictionary<string, ExprTransform[]>
        {
            { "插入1", new ExprTransform[]
                {
                    // RotZ 已反轉正負號：Unity Y 軸朝上時正值＝逆時針，Godot Y 軸朝下時正值＝順時針，
                    // 直接照抄 Unity 數值會讓旋轉方向鏡射相反，故取負號才能得到相同視覺效果
                    new ExprTransform { LayerPath = "臉/層1",  RotZ = -30f },
                    new ExprTransform { LayerPath = "臉/層2",  RotZ =  30f },
                    new ExprTransform { LayerPath = "眉毛1",   OffsetX =  20f },
                    new ExprTransform { LayerPath = "眉毛2",   OffsetX = -20f },
                }
            },
            { "插入2", new ExprTransform[]
                {
                    // 同上，RotZ 已反轉正負號以對應 Godot 的旋轉方向慣例
                    new ExprTransform { LayerPath = "臉/層1",  RotZ =  30f, OffsetY = -5f },  // Y 反向（Godot Y 下正）
                    new ExprTransform { LayerPath = "臉/層2",  RotZ = -30f, OffsetY = -5f },
                    new ExprTransform { LayerPath = "眉毛1",   OffsetX =  20f },
                    new ExprTransform { LayerPath = "眉毛2",   OffsetX = -20f },
                }
            },
            // ZZZ（睡覺）：新內容，不是移植。使用者要求眉毛要有「垂下去」放鬆的感覺——
            // 這裡先用最安全的做法：只往下位移，不加旋轉（旋轉方向要對照實際貼圖軸心才準，
            // 之後如果視覺上還想要眉毛角度更斜垂，可以再加 RotZ 微調）。
            { "ZZZ", new ExprTransform[]
                {
                    new ExprTransform { LayerPath = "眉毛1", OffsetY = 15f },
                    new ExprTransform { LayerPath = "眉毛2", OffsetY = 15f },
                }
            },
        };

    readonly Dictionary<string, (Vector2 pos, float rotDeg)> _savedTransforms
        = new Dictionary<string, (Vector2, float)>();

    static readonly HashSet<string> _asymEyeExprSet
        = new HashSet<string> { "做愛(半閉)", "做愛3" };

    /// <summary>表情專屬的「閉眼時額外往下位移」量，套用在 EyeCloseController.ClosedExtraOffsetY。
    /// 目前只有 ZZZ（睡覺）：使用者反應原本的閉眼位置偏高，希望整組閉眼圖案再往下一點。</summary>
    static readonly Dictionary<string, float> _eyeExtraCloseOffsetMap
        = new Dictionary<string, float> { { "ZZZ", 20f } };

    /// <summary>表情專屬的「閉眼時垂下」旋轉角度，套用在 EyeCloseController.ClosedExtraRotateZ。
    /// 目前只有 ZZZ：改用旋轉（而不是翻轉貼圖）做出下垂的睡眼效果，比翻轉安全穩定，
    /// 不會有翻轉支點沒抓對、整張透明畫布鏡射跑位的風險。</summary>
    // 旋轉下垂的嘗試（ClosedExtraRotateZ）沒有達到使用者要的效果，使用者明確要求改用
    // 翻轉眼皮（全閉3）做這個「垂下」視覺，所以這裡先清空，機制保留但目前沒有表情在用。
    static readonly Dictionary<string, float> _eyeExtraCloseRotateMap
        = new Dictionary<string, float>();

    /// <summary>ZZZ（睡覺）表情要暫時隱藏的眼淚圖層——使用者反應閉眼睡覺的時候不應該還掛著眼淚。
    /// 其他表情不受影響（眼淚圖層預設一律顯示，只有切到 ZZZ 才會被藏起來，離開 ZZZ 會恢復顯示）。</summary>
    static readonly string[] _zzzHiddenTearLayers
        = { "表情/眼淚1", "表情/眼淚2", "表情/眼淚3", "表情/眼淚4" };

    static readonly string[] _allMouthKeys = new[]
    {
        "笑","笑2","小嘴","吐舌","張嘴1","張嘴2","斜動嘴","抖動嘴","小嘴吐舌","小嘴巴","表情/嘴巴"
    };

    static readonly string[] DefaultExpressionNames = new[]
    {
        "害怕", "有點驚訝", "忍耐", "害羞", "開心", "舒服", "傲嬌",
        "插入1", "插入2",
        "做愛(半閉)", "做愛(全閉)", "做愛白眼", "做愛3", "做愛4",
        "高潮1", "高潮2", "高潮",
        // ZZZ：使用者新增的睡覺表情，Unity 專案裡沒有這個東西（不是移植，是新內容）。
        // 沿用「做愛4」已經在用的全閉2（Closed2，睫毛翻轉版本的全閉眼）樣式，
        // 不畫新圖、不加新圖層，只是多一個表情名稱可以切換到同樣的閉眼視覺效果。
        "ZZZ",
    };

    // ════════════════════════════════════════════════════════════
    //  生命週期
    // ════════════════════════════════════════════════════════════

    public override void _Ready()
    {
        if (_runtime == null && RuntimePath != null && !RuntimePath.IsEmpty)
            _runtime = GetNodeOrNull<TrainingRoomRuntime>(RuntimePath);
        if (_eyeCloseCtrl == null && EyeCloseCtrlPath != null && !EyeCloseCtrlPath.IsEmpty)
            _eyeCloseCtrl = GetNodeOrNull<EyeCloseController>(EyeCloseCtrlPath);

        _ = InitAsync();
    }

    /// <summary>供程式建構時直接注入依賴（節點是純程式建立，NodePath 無法解析，
    /// 需在加入場景樹之前呼叫，_Ready() 內會優先保留已注入的值）。</summary>
    public void Configure(TrainingRoomRuntime runtime, EyeCloseController eyeCloseCtrl = null)
    {
        _runtime = runtime;
        if (eyeCloseCtrl != null) _eyeCloseCtrl = eyeCloseCtrl;
    }

    private async Task InitAsync()
    {
        // 整個方法都包進 try/catch（含最前面的 await）：_ = InitAsync() 是 fire-and-forget，
        // 如果 await 那一行本身丟例外（例如訊號名稱或時機有問題），.NET 對「沒人在等待的
        // Task 裡的例外」預設不會印到主控台，導致整段初始化悄悄卡死、_ready 永遠是 false，
        // 卻完全看不到任何錯誤訊息。這裡在最前面跟最後面都印一行，下次測試時只要看
        // Godot 的 Output 有沒有出現「InitAsync 開始」「InitAsync 完成」這兩行，
        // 就能知道到底是卡在 await 那一行，還是卡在後面哪個 EnsureXxx 方法。
        GD.Print("[FaceExpr] InitAsync 開始");
        try
        {
            // 等一小段時間讓 TrainingRoomRuntime 先完成初始化。
            // 原本用 await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame) 等一幀，
            // 但實測發現這個表情系統卡在這裡就不動了（_ready 永遠是 false，見除錯訊息）。
            // 改用 GetTree().CreateTimer(...).Timeout 這個計時器訊號——這是同一個專案裡
            // EyeCloseController.DelaySeconds() 已經在用、確定會正常觸發的寫法，比較保險。
            await ToSignal(GetTree().CreateTimer(0.05f), SceneTreeTimer.SignalName.Timeout);
            GD.Print("[FaceExpr] 已等到計時器觸發，開始跑 EnsureXxx");

            EnsureDefaultExpressions();
            GD.Print($"[FaceExpr] EnsureDefaultExpressions 完成，Expressions.Length={Expressions?.Length ?? -1}");
            EnsureEyeCloseArrays();
            EnsurePartGroups();
            EnsureRealFaceGroupsAndParts();
            EnsureDefaultParts();
            BuildCaches();
            _ready = true;
            GD.Print($"[FaceExpr] InitAsync 完成，_ready=true，Expressions.Length={Expressions?.Length ?? -1}");

            HideGroup("嘴巴");

            if (!string.IsNullOrEmpty(DefaultExpression))
            {
                SetExpression(DefaultExpression);
                _eyeCloseCtrl?.SnapToTarget();   // 預設表情的眼睛直接到位，不要看到睜眼→閉眼的過渡
            }
            else
                SetLayerActive("小嘴", true);
        }
        catch (System.Exception e)
        {
            GD.PrintErr($"[FaceExpr] InitAsync 初始化失敗，表情系統不會運作：{e}");
        }
    }

    // 在所有 _Process 之後再跑（模擬 LateUpdate）
    public override void _Process(double delta)
    {
        AnimateBrow((float)delta);   // 眉毛形態慢慢變化
        if (string.IsNullOrEmpty(_currentExpr)) return;
        if (!_exprTransformMap.TryGetValue(_currentExpr, out var overrides)) return;

        foreach (var ov in overrides)
        {
            if (Mathf.IsZeroApprox(ov.OffsetX) && Mathf.IsZeroApprox(ov.OffsetY)) continue;
            // 最上層圖層（眉毛1／眉毛2 等）的位置由呼吸迴圈管，偏移量已經透過 ExprLayerExtraOffset 疊進去了；
            // 這裡再用「存檔位置＋偏移」每幀覆蓋，會把呼吸的起伏蓋掉（眉毛不跟著呼吸的原因）
            if (_runtime?.Layers != null && _runtime.Layers.ContainsKey(ov.LayerPath)) continue;
            var ctrl = GetLayerControl(ov.LayerPath);
            if (ctrl == null) continue;
            if (!_savedTransforms.TryGetValue(ov.LayerPath, out var saved)) continue;

            var pos = ctrl.Position;
            if (!Mathf.IsZeroApprox(ov.OffsetX)) pos.X = saved.pos.X + ov.OffsetX;
            if (!Mathf.IsZeroApprox(ov.OffsetY)) pos.Y = saved.pos.Y + ov.OffsetY;
            ctrl.Position = pos;
        }
    }

    // ════════════════════════════════════════════════════════════
    //  公開 API
    // ════════════════════════════════════════════════════════════

    public void SetExpression(string expressionName)
    {
        if (!_ready)
        {
            GD.PrintErr("[FaceExpr] 尚未初始化");
            return;
        }
        if (string.IsNullOrEmpty(expressionName)) return;
        if (!_exprMap.TryGetValue(expressionName, out var def))
        {
            GD.PrintErr($"[FaceExpr] 找不到表情「{expressionName}」");
            return;
        }
        ForceHideAllMouths();
        ApplyExpression(def);
        _currentExpr = expressionName;

        // 閉眼控制
        if (_eyeCloseCtrl != null)
        {
            // 表情專屬的「閉眼時額外往下位移」量：目前只有 ZZZ 會設非 0 值，
            // 其他表情一律是 0（不影響原本的閉眼位置）。要在呼叫 SetClosed()/SetClosed2() 之前
            // 設好，因為 EyeCloseController._Process() 下一幀就會立刻用這個值計算 Apply()。
            _eyeCloseCtrl.ClosedExtraOffsetY =
                _eyeExtraCloseOffsetMap.TryGetValue(expressionName, out var extraY) ? extraY : 0f;
            _eyeCloseCtrl.ClosedExtraRotateZ =
                _eyeExtraCloseRotateMap.TryGetValue(expressionName, out var extraRot) ? extraRot : 0f;

            if (_asymEyeExprSet.Contains(expressionName))
            {
                _eyeCloseCtrl.SetEyeTargets(1f, _eyeCloseCtrl.HalfCloseAmount);
            }
            else
            {
                _eyeCloseCtrl.ClearEyeTargets();
                bool shouldClose  = System.Array.Exists(CloseEyeExpressions,      n => n == expressionName);
                bool shouldClose2 = System.Array.Exists(Closed2EyeExpressions,    n => n == expressionName);
                bool shouldClose3 = System.Array.Exists(Closed3EyeExpressions,    n => n == expressionName);
                bool shouldHalf   = System.Array.Exists(HalfCloseEyeExpressions,  n => n == expressionName);
                bool shouldHalf2  = System.Array.Exists(HalfClose2EyeExpressions, n => n == expressionName);

                if      (shouldClose)  _eyeCloseCtrl.SetClosed(true);
                else if (shouldClose2) _eyeCloseCtrl.SetClosed2(true);
                else if (shouldClose3) _eyeCloseCtrl.SetClosed3(true);
                else if (shouldHalf)   _eyeCloseCtrl.SetHalfClosed(true);
                else if (shouldHalf2)  _eyeCloseCtrl.SetHalfClosed2(true);
                else                   _eyeCloseCtrl.SetClosed(false);
            }
        }

        // ZZZ（睡覺）暫時把眼淚圖層藏起來（使用者反應：睡覺表情不應該掛著眼淚）。
        // 眼淚圖層在其他表情下預設一律顯示，所以離開 ZZZ 時這裡會把它們設回 true 還原。
        bool isZzz = expressionName == "ZZZ";
        foreach (var tearLayer in _zzzHiddenTearLayers)
            SetLayerActive(tearLayer, !isZzz);

        ApplyExpressionTransforms(expressionName);
    }

    public void SetExpression(int index)
    {
        if (Expressions == null || index < 0 || index >= Expressions.Length) return;
        SetExpression(Expressions[index].ExpressionName);
    }

    public void HideGroup(string groupName)
    {
        if (!_groupLayerSets.TryGetValue(groupName, out var set)) return;
        foreach (var ln in set) SetLayerActive(ln, false);
    }

    public void HideAll()
    {
        foreach (var kv in _groupLayerSets)
            foreach (var ln in kv.Value)
                SetLayerActive(ln, false);
        _currentExpr = "";
    }

    public void RebuildCaches() => BuildCaches();

    // ════════════════════════════════════════════════════════════
    //  內部
    // ════════════════════════════════════════════════════════════

    void ForceHideAllMouths()
    {
        if (_runtime?.Layers == null) return;
        foreach (var key in _allMouthKeys)
            if (_runtime.Layers.TryGetValue(key, out var node) && node != null)
                node.Visible = false;
    }

    void ApplyExpressionTransforms(string expressionName)
    {
        RestoreTransforms();
        if (!_exprTransformMap.TryGetValue(expressionName, out var overrides)) return;

        foreach (var ov in overrides)
        {
            var ctrl = GetLayerControl(ov.LayerPath);
            if (ctrl == null) continue;

            if (!_savedTransforms.ContainsKey(ov.LayerPath))
                _savedTransforms[ov.LayerPath] = (ctrl.Position, ctrl.RotationDegrees);

            var (origPos, origRot) = _savedTransforms[ov.LayerPath];

            if (!Mathf.IsZeroApprox(ov.OffsetX) || !Mathf.IsZeroApprox(ov.OffsetY))
            {
                ctrl.Position = origPos + new Vector2(ov.OffsetX, ov.OffsetY);
                // 有些圖層（例如「眉毛1」「眉毛2」）在 _headLayerSet 裡，每一幀都會被
                // TrainingRoomRuntime.UpdateBreathing() 的通用呼吸迴圈用 basePos + 呼吸位移
                // 直接覆蓋 Position，上面這行單純設一次的話下一幀就會被蓋掉、看不出效果。
                // 所以同時把這個偏移量寫進 ExprLayerExtraOffset，呼吸迴圈自己會在算 basePos
                // 時一併加上去；對沒有被呼吸迴圈接管的圖層（例如巢狀子節點「臉/層1」）
                // 這個字典項目不會被讀到，純粹是多寫一筆、無副作用。
                if (_runtime != null)
                    _runtime.ExprLayerExtraOffset[ov.LayerPath] = new Vector2(ov.OffsetX, ov.OffsetY);
            }

            if (!Mathf.IsZeroApprox(ov.RotZ))
                ctrl.RotationDegrees = origRot + ov.RotZ;
        }
    }

    void RestoreTransforms()
    {
        foreach (var kv in _savedTransforms)
        {
            var ctrl = GetLayerControl(kv.Key);
            if (ctrl != null)
            {
                ctrl.Position         = kv.Value.pos;
                ctrl.RotationDegrees  = kv.Value.rotDeg;
            }
            _runtime?.ExprLayerExtraOffset.Remove(kv.Key);
        }
        _savedTransforms.Clear();
        ApplyBrowTransform();   // 表情還原的是「套表情前」的角度，眉毛形態要重新蓋回去
    }

    // ════════════════════════════════════════════════════════════
    //  眉毛形態（跟表情獨立，可以疊在任何表情上）
    //    Normal       初始
    //    Frown        上下反轉（皺眉）
    //    Angry        左眉逆時針、右眉順時針 35°（生氣）
    //    Worried      左眉順時針、右眉逆時針 35°（煩惱）
    //    AngryFlip / WorriedFlip  生氣／煩惱 再整個上下反轉
    // ════════════════════════════════════════════════════════════

    //  編號（跟測試面板按鈕、SetBrowForm(int) 一致）：
    //    1 初始　2 皺眉(上下反轉)　3 生氣　4 煩惱　5 生氣(上下反轉)　6 煩惱(上下反轉)
    public enum BrowForm { Normal = 1, Frown = 2, Angry = 3, Worried = 4, AngryFlip = 5, WorriedFlip = 6 }

    /// <summary>用編號（1~6）切換眉毛形態；超出範圍忽略。</summary>
    public void SetBrowForm(int number)
    {
        if (number < 1 || number > 6) return;
        SetBrowForm((BrowForm)number);
    }

    public const float BrowTiltDeg = 35f;
    // 眉毛內容中心（2000x3200 貼圖像素）：旋轉與上下翻轉都繞著它，才會在原地轉／翻
    static readonly Vector2 BrowCenterLeft  = new Vector2(844.5f, 601.5f);    // 眉毛1（畫面左）
    static readonly Vector2 BrowCenterRight = new Vector2(1066f,  585.5f);    // 眉毛2（畫面右）

    BrowForm _browForm = BrowForm.Normal;
    public BrowForm CurrentBrowForm => _browForm;

    [Export] public float BrowMoveSec = 5f;   // 眉毛從一個形態慢慢變到另一個形態要幾秒（例如 2→6）；1.5 → 3 → 5

    // 目前畫面上的狀態（會慢慢追向目標）：傾斜 -1（煩惱）~ 0 ~ +1（生氣）、Y 縮放 +1 正常 ~ -1 反轉
    float _browTiltCur = 0f, _browScaleYCur = 1f;
    float _browTiltTgt = 0f; bool _browFlipTgt = false;

    /// <summary>切換眉毛形態。instant＝true 立刻到位；false 則花 BrowMoveSec 秒慢慢變過去
    /// （2→6 就是反轉狀態下眉毛慢慢轉成煩惱的角度）。</summary>
    public void SetBrowForm(BrowForm form, bool instant = false)
    {
        _browForm = form;
        float tilt = form switch
        {
            BrowForm.Angry or BrowForm.AngryFlip     =>  1f,
            BrowForm.Worried or BrowForm.WorriedFlip => -1f,
            _ => 0f,
        };
        SetBrowPose(tilt, form is BrowForm.Frown or BrowForm.AngryFlip or BrowForm.WorriedFlip, instant);
    }

    /// <summary>連續版：tilt＝-1（煩惱 4／6）~ 0（初始 1／2）~ +1（生氣 3／5），flip＝上下反轉（2／5／6 那一邊）。
    /// 在 1↔4 或 2↔6 之間的任何程度都可以，眉毛會慢慢追過去。</summary>
    public void SetBrowPose(float tilt, bool flip, bool instant = false)
    {
        _browTiltTgt = Mathf.Clamp(tilt, -1f, 1f);
        _browFlipTgt = flip;
        if (instant)
        {
            _browTiltCur = _browTiltTgt;
            _browScaleYCur = _browFlipTgt ? -1f : 1f;
        }
        ApplyBrowTransform();
    }

    void AnimateBrow(float dt)
    {
        float tgtScaleY = _browFlipTgt ? -1f : 1f;
        if (Mathf.IsEqualApprox(_browTiltCur, _browTiltTgt) && Mathf.IsEqualApprox(_browScaleYCur, tgtScaleY)) return;
        float step = dt * 2f / Mathf.Max(0.01f, BrowMoveSec);   // 全幅（傾斜 -1→+1、Y 縮放 +1→-1）剛好 BrowMoveSec 秒
        _browTiltCur   = Mathf.MoveToward(_browTiltCur,   _browTiltTgt, step);
        _browScaleYCur = Mathf.MoveToward(_browScaleYCur, tgtScaleY,    step);
        ApplyBrowTransform();
    }

    void ApplyBrowTransform()
    {
        ApplyBrowLayer("眉毛1", BrowCenterLeft,  -1f);   // 左眉：生氣＝逆時針（Godot 負值）
        ApplyBrowLayer("眉毛2", BrowCenterRight, +1f);   // 右眉：生氣＝順時針（Godot 正值）
    }

    void ApplyBrowLayer(string layer, Vector2 centerTex, float angrySign)
    {
        var ctrl = GetLayerControl(layer);
        if (ctrl == null) return;

        // 整個形態上下鏡射：先轉再翻 ＝ 先翻再轉相反角度（Control 是先縮放再旋轉）。
        // 用「目標」是否反轉決定角度正負，變形途中才不會在翻到一半時角度跳掉。
        float tilt = angrySign * _browTiltCur * BrowTiltDeg;
        ctrl.PivotOffset     = centerTex * ctrl.Size / new Vector2(2000f, 3200f);
        ctrl.Scale           = new Vector2(1f, _browScaleYCur);
        ctrl.RotationDegrees = _browFlipTgt ? -tilt : tilt;
    }

    Control GetLayerControl(string layerPath)
    {
        if (_runtime?.Layers == null) return null;
        if (_runtime.Layers.TryGetValue(layerPath, out var node) && node != null)
            return node as Control;

        int slash = layerPath.LastIndexOf('/');
        if (slash > 0)
        {
            string parentKey = layerPath.Substring(0, slash);
            string childName  = layerPath.Substring(slash + 1);
            if (_runtime.Layers.TryGetValue(parentKey, out var parentNode) && parentNode != null)
            {
                var child = parentNode.FindChild(childName, false);
                if (child is Control ctrl) return ctrl;
            }
        }
        return null;
    }

    void BuildCaches()
    {
        _exprMap.Clear();
        _groupLayerSets.Clear();

        if (PartGroups != null)
        {
            foreach (var g in PartGroups)
            {
                if (string.IsNullOrEmpty(g.GroupName) || g.LayerNames == null) continue;
                var set = new HashSet<string>();
                foreach (var ln in g.LayerNames)
                    if (!string.IsNullOrEmpty(ln)) set.Add(ln);
                _groupLayerSets[g.GroupName] = set;
            }
        }
        if (Expressions != null)
        {
            foreach (var e in Expressions)
                if (!string.IsNullOrEmpty(e.ExpressionName))
                    _exprMap[e.ExpressionName] = e;
        }
    }

    void ApplyExpression(ExpressionDef expr)
    {
        if (expr.Parts == null) return;
        var affected = new HashSet<string>();
        foreach (var p in expr.Parts)
            if (!string.IsNullOrEmpty(p.GroupName)) affected.Add(p.GroupName);

        foreach (var gName in affected)
        {
            if (!_groupLayerSets.TryGetValue(gName, out var set)) continue;
            foreach (var ln in set) SetLayerActive(ln, false);
        }
        foreach (var p in expr.Parts)
        {
            if (string.IsNullOrEmpty(p.GroupName) || string.IsNullOrEmpty(p.LayerName)) continue;
            SetLayerActive(p.LayerName, true);
        }
    }

    void SetLayerActive(string layerName, bool active)
    {
        if (_runtime?.Layers == null) return;

        if (_runtime.Layers.TryGetValue(layerName, out var node) && node != null)
        {
            node.Visible = active;
            return;
        }

        int slash = layerName.LastIndexOf('/');
        if (slash > 0)
        {
            string parentKey = layerName.Substring(0, slash);
            string childName  = layerName.Substring(slash + 1);
            if (_runtime.Layers.TryGetValue(parentKey, out var parentNode) && parentNode != null)
            {
                if (active) parentNode.Visible = true;
                var child = parentNode.FindChild(childName, false);
                if (child is CanvasItem ci) { ci.Visible = active; return; }
            }
        }
    }

    // ── EnsureXxx 方法（補齊預設值，避免缺少表情/群組/部位）─────────

    void EnsureDefaultExpressions()
    {
        if (Expressions == null) { Expressions = BuildDefaultExpressions(); return; }
        var existing = new HashSet<string>();
        foreach (var e in Expressions) if (!string.IsNullOrEmpty(e.ExpressionName)) existing.Add(e.ExpressionName);
        var toAdd = new List<ExpressionDef>();
        foreach (var name in DefaultExpressionNames)
            if (!existing.Contains(name))
                toAdd.Add(new ExpressionDef { ExpressionName = name, Parts = System.Array.Empty<FaceExprLayerMap>() });
        if (toAdd.Count > 0)
        {
            var merged = new ExpressionDef[Expressions.Length + toAdd.Count];
            Expressions.CopyTo(merged, 0);
            toAdd.CopyTo(merged, Expressions.Length);
            Expressions = merged;
        }
    }

    static ExpressionDef[] BuildDefaultExpressions()
    {
        var list = new ExpressionDef[DefaultExpressionNames.Length];
        for (int i = 0; i < list.Length; i++)
            list[i] = new ExpressionDef { ExpressionName = DefaultExpressionNames[i], Parts = System.Array.Empty<FaceExprLayerMap>() };
        return list;
    }

    void EnsurePartGroups()
    {
        var mouthLayerNames = new[]
        {
            "笑", "笑2", "小嘴", "吐舌", "張嘴1", "張嘴2", "斜動嘴", "抖動嘴", "小嘴吐舌", "小嘴巴", "表情/嘴巴"
        };
        if (PartGroups == null) PartGroups = System.Array.Empty<FacePartGroup>();

        FacePartGroup mouthGroup = null;
        for (int i = 0; i < PartGroups.Length; i++)
            if (PartGroups[i].GroupName == "嘴巴") { mouthGroup = PartGroups[i]; break; }

        if (mouthGroup == null)
        {
            var newGroups = new FacePartGroup[PartGroups.Length + 1];
            PartGroups.CopyTo(newGroups, 0);
            newGroups[PartGroups.Length] = new FacePartGroup { GroupName = "嘴巴", LayerNames = mouthLayerNames };
            PartGroups = newGroups;
        }
        else
        {
            var existing = new HashSet<string>(mouthGroup.LayerNames ?? System.Array.Empty<string>());
            var toAdd    = new List<string>();
            foreach (var name in mouthLayerNames) if (!existing.Contains(name)) toAdd.Add(name);
            if (toAdd.Count > 0)
            {
                int oldLen   = mouthGroup.LayerNames?.Length ?? 0;
                var newNames = new string[oldLen + toAdd.Count];
                mouthGroup.LayerNames?.CopyTo(newNames, 0);
                toAdd.CopyTo(newNames, oldLen);
                mouthGroup.LayerNames = newNames;
            }
        }
    }

    /// <summary>
    /// 從 Unity 場景檔 TrainingRoom.unity（fileID 903285456，FaceExpressionController 元件）
    /// 讀出來的真實 Inspector 資料補上去，這是「表情切換完全沒有畫面變化」的根本原因：
    /// 之前 Godot 這邊只有一個空殼的自動補齊邏輯（EnsurePartGroups 只會補「嘴巴」），
    /// Unity Inspector 裡實際手動設定的 PartGroups／忍耐表情的 Parts 從來沒有真的搬過來過。
    ///
    /// 附註（重要，不是我漏做）：Unity Inspector 裡原本還有兩個群組「眼」（臉/層1、臉/層2）
    /// 和「眉」（臉/層毛1、臉/層毛2），但這兩個名稱在 Unity 場景裡實際上找不到對應的
    /// GameObject（角色臉部底下的圖層後來改名成「眼1」「眼2」，Inspector 陣列忘記同步更新），
    /// 所以這兩個群組在 Unity 原本的遊戲裡也是死代碼、從來沒有真的產生任何效果——
    /// 這裡故意不搬過來，避免無中生有做出 Unity 自己都沒有的行為。
    /// 「臉紅」群組同理：Inspector 寫的是「臉/表情_臉紅」，但真正的圖層叫「表情_腮紅」
    /// （打字打錯字），這裡直接對應到 Godot 已經存在的正確圖層 key「表情/腮紅」。
    /// </summary>
    void EnsureRealFaceGroupsAndParts()
    {
        AddGroupIfMissing("眼皮",  new[] { "表情/眼皮" });
        AddGroupIfMissing("眼皮2", new[] { "表情/眼皮2" });
        AddGroupIfMissing("臉紅",  new[] { "表情/腮紅" });   // 對應 Unity「表情_腮紅」，Inspector 裡寫錯成「表情_臉紅」
        AddGroupIfMissing("鼻子",  new[] { "表情/鼻子" });

        // 忍耐：Unity Inspector 裡實際設定了 Parts（顯示閉眼用的眼皮/眼皮2 覆蓋圖層），
        // 之前 Godot 版本因為 Expressions 是空陣列自動補的，這個表情專屬的 Parts 從來沒有搬過來。
        SetExpressionParts("忍耐", new[]
        {
            new FaceExprLayerMap { GroupName = "眼皮",  LayerName = "表情/眼皮" },
            new FaceExprLayerMap { GroupName = "眼皮2", LayerName = "表情/眼皮2" },
        });
    }

    void AddGroupIfMissing(string groupName, string[] layerNames)
    {
        PartGroups ??= System.Array.Empty<FacePartGroup>();
        for (int i = 0; i < PartGroups.Length; i++)
            if (PartGroups[i].GroupName == groupName) return; // 已存在（可能是 Inspector 自訂過的），不覆蓋

        var newGroups = new FacePartGroup[PartGroups.Length + 1];
        PartGroups.CopyTo(newGroups, 0);
        newGroups[PartGroups.Length] = new FacePartGroup { GroupName = groupName, LayerNames = layerNames };
        PartGroups = newGroups;
    }

    /// <summary>只在該表情目前 Parts 是空陣列時才覆蓋（尊重使用者之後如果自己在 Inspector 改過的設定）。</summary>
    void SetExpressionParts(string expressionName, FaceExprLayerMap[] parts)
    {
        if (Expressions == null) return;
        for (int i = 0; i < Expressions.Length; i++)
        {
            if (Expressions[i].ExpressionName != expressionName) continue;
            if (Expressions[i].Parts == null || Expressions[i].Parts.Length == 0)
                Expressions[i].Parts = parts;
            return;
        }
    }

    void EnsureEyeCloseArrays()
    {
        CloseEyeExpressions   = EnsureContains(CloseEyeExpressions,   "做愛(全閉)");
        CloseEyeExpressions   = EnsureContains(CloseEyeExpressions,   "高潮1");
        CloseEyeExpressions   = EnsureContains(CloseEyeExpressions,   "高潮2");
        CloseEyeExpressions   = EnsureContains(CloseEyeExpressions,   "高潮");
        CloseEyeExpressions   = EnsureContains(CloseEyeExpressions,   "插入1");
        CloseEyeExpressions   = EnsureContains(CloseEyeExpressions,   "插入2");
        Closed2EyeExpressions = EnsureContains(Closed2EyeExpressions, "做愛4");
        // ZZZ（睡覺）：使用者明確確認要翻轉的是「眼皮」（不是睫毛），改用獨立的全閉3
        // （Closed3EyeExpressions → EyeCloseController.SetClosed3() → 翻轉 EyelidFlipParts），
        // 不會動到「做愛4」原本對照 Unity 真實資料的睫毛翻轉（全閉2）。
        Closed3EyeExpressions = EnsureContains(Closed3EyeExpressions, "ZZZ");
    }

    static string[] EnsureContains(string[] arr, string value)
    {
        if (arr == null) return new[] { value };
        foreach (var s in arr) if (s == value) return arr;
        var newArr = new string[arr.Length + 1];
        arr.CopyTo(newArr, 0);
        newArr[arr.Length] = value;
        return newArr;
    }

    void EnsureDefaultParts()
    {
        var mouthOverrides = new Dictionary<string, string>
        {
            { "插入1",      "抖動嘴"    },
            { "插入2",      "小嘴巴"    },   // 完全突破：小嘴巴
            { "做愛(半閉)", "表情/嘴巴" },
            { "做愛(全閉)", "小嘴"      },
            { "做愛3",      "小嘴"      },
            { "做愛4",      "小嘴"      },
            { "高潮1",      "小嘴吐舌"  },
            { "高潮2",      "小嘴吐舌"  },
            { "高潮",       "小嘴吐舌"  },
            // ZZZ（睡覺）：使用者提供新畫的嘴巴圖，指定當作 ZZZ 專用的嘴巴。
            // "張嘴1" 這個圖層名稱原本在「嘴巴」PartGroup 裡就存在（跟「笑2」「吐舌」「斜動嘴」
            // 一樣），但 Sprites 資料夾裡一直沒有對應的 張嘴1.png 檔案，是個一直顯示不出來的
            // 空圖層——這裡直接把使用者新給的嘴巴圖存成 張嘴1.png，剛好借用這個原本閒置的
            // 圖層名稱，不用額外加新的 PartGroup 圖層或多寫貼圖替換的程式碼。
            { "ZZZ",        "張嘴1"     },
        };
        const string defaultMouth = "小嘴";

        if (Expressions == null) return;
        foreach (var expr in Expressions)
        {
            if (string.IsNullOrEmpty(expr.ExpressionName)) continue;
            if (!mouthOverrides.TryGetValue(expr.ExpressionName, out var mouthLayer))
                mouthLayer = defaultMouth;

            bool hasMouth = false;
            if (expr.Parts != null)
                for (int i = 0; i < expr.Parts.Length; i++)
                    if (expr.Parts[i].GroupName == "嘴巴")
                    {
                        expr.Parts[i].LayerName = mouthLayer;
                        hasMouth = true;
                        break;
                    }

            if (!hasMouth)
            {
                int oldLen    = expr.Parts?.Length ?? 0;
                var newParts  = new FaceExprLayerMap[oldLen + 1];
                expr.Parts?.CopyTo(newParts, 0);
                newParts[oldLen] = new FaceExprLayerMap { GroupName = "嘴巴", LayerName = mouthLayer };
                expr.Parts = newParts;
            }
        }
    }
}

// ════════════════════════════════════════════════════════════════════════
//  資料結構
// ════════════════════════════════════════════════════════════════════════

[GlobalClass]
public partial class FacePartGroup : Resource
{
    [Export] public string   GroupName  = "";
    [Export] public string[] LayerNames = System.Array.Empty<string>();
}

[GlobalClass]
public partial class ExpressionDef : Resource
{
    [Export] public string             ExpressionName = "";
    [Export] public FaceExprLayerMap[] Parts          = System.Array.Empty<FaceExprLayerMap>();
}

[GlobalClass]
public partial class FaceExprLayerMap : Resource
{
    [Export] public string GroupName = "";
    [Export] public string LayerName = "";
}
