using Godot;

/// <summary>
/// 眉毛隨舒服程度變化（形態編號見 FaceExpressionController.BrowForm）：
///   舒服這一側　1 初始　→　4 很舒服；每個物件剛被觸碰的瞬間先是 3，再往 4 移動
///   不舒服這一側（上下反轉）　2 初始　→　6 很不舒服；剛觸碰時先是 2，再慢慢變成 6
///   （2／5／6 是 1／3／4 的上下鏡射；5 目前沒有用到）
/// 怎麼看舒服與否：
///   小穴插入中 → 看小穴：ArousalRateNow 為負（速度不對／太乾）＝不舒服，否則用穴高潮值當舒服程度
///   小穴沒插入 → 看其他：嘴巴＋胸部＋尻部 舒服度相加當舒服程度；摸 B／舔 B 速度不對（速率為負）＝不舒服
/// 沒有任何操作（停手超過 BrowLingerSec）→ 回到 1 號。
/// </summary>
public partial class TrainingRoomRuntime
{
    [ExportGroup("眉毛")]
    [Export] public bool  BrowReactOnOperate = true;
    [Export] public float BrowLingerSec      = 0.8f;    // 操作停止後多久才回到 1 號
    [Export] public float BrowOthersFull     = 150f;    // 小穴沒插入時：嘴＋胸＋尻 加起來到多少算「很舒服」（滿 300）
    [Export] public float BrowUncomfyEnter   = 0.15f;   // 不舒服程度超過這個 → 切到不舒服（反轉）那一側
    [Export] public float BrowUncomfyExit    = 0.05f;   // 低於這個 → 回到舒服那一側（兩個值拉開避免一直來回翻）
    [Export] public float BrowSmoothSec      = 1.2f;    // 舒服／不舒服程度的平滑時間

    float _browLingerT;
    int   _browPrevMask;
    float _browPos, _browNeg;     // 平滑後的舒服／不舒服程度（0~1）
    bool  _browUncomfy;

    void UpdateBrowOperate(float dt)
    {
        if (FaceController == null || !FaceController.IsReady) return;

        bool inserted = _jijiPhase == 3 && _jijiStage >= 1 && !_jijiAnus;   // 小穴插入中

        // 「物件」＝現在被碰的東西；新出現的那一位就是「剛剛觸碰」
        int mask = 0;
        if (_touchAnimRunningLeft)  mask |= 1 << 0;   // 左胸
        if (_touchAnimRunningRight) mask |= 1 << 1;   // 右胸
        if (NippleHandCount > 0)    mask |= 1 << 2;   // 乳頭
        if (_bTouchRunning)         mask |= 1 << 3;   // 摸 B
        if (_lickOn)                mask |= 1 << 4;   // 舔
        if (_patOn)                 mask |= 1 << 5;   // 摸頭
        if (_tongueDragOwner)       mask |= 1 << 6;   // 嘴
        if (_jijiPhase != 0)        mask |= 1 << 7;   // 雞雞出現／插入

        bool operating = BrowReactOnOperate && (mask != 0 ||
            Input.IsMouseButtonPressed(MouseButton.Left) ||
            Input.IsMouseButtonPressed(MouseButton.Right) ||
            Input.IsMouseButtonPressed(MouseButton.Middle));

        bool wasOn = _browLingerT > 0f;
        _browLingerT = operating ? BrowLingerSec : Mathf.Max(0f, _browLingerT - dt);
        bool isOn = _browLingerT > 0f;

        if (!isOn)
        {
            if (wasOn) { _browUncomfy = false; _browPos = _browNeg = 0f; FaceController.SetBrowForm(FaceExpressionController.BrowForm.Normal); }   // 1 號
            _browPrevMask = mask;
            return;
        }

        // ── 舒服／不舒服程度 ──
        float baseRate = inserted ? ArousalRateInsert : ArousalRateB;
        float negNow = ArousalRateNow < 0f
            ? Mathf.Clamp(-ArousalRateNow / Mathf.Max(0.01f, baseRate * ArousalPenaltyRatio), 0f, 1f) : 0f;
        float posNow = inserted
            ? Mathf.Clamp(ClimaxLevel / 100f, 0f, 1f)
            : Mathf.Clamp((ComfortMouth + ComfortChest + ComfortButt) / Mathf.Max(1f, BrowOthersFull), 0f, 1f);
        float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, BrowSmoothSec));
        _browPos = Mathf.Lerp(_browPos, posNow, k);
        _browNeg = Mathf.Lerp(_browNeg, negNow, k);

        if (!_browUncomfy && _browNeg > BrowUncomfyEnter) _browUncomfy = true;
        else if (_browUncomfy && _browNeg < BrowUncomfyExit) _browUncomfy = false;

        float amount = _browUncomfy ? _browNeg : _browPos;   // 0＝初始（1／2），1＝最大（4／6）
        float targetTilt = -amount;                          // 往煩惱（4／6）的角度移動

        bool justTouched = (mask & ~_browPrevMask) != 0 || !wasOn;   // 有新物件被碰到（或一開始就碰）
        _browPrevMask = mask;

        if (justTouched)
        {
            // 剛觸碰：舒服側先是 3（生氣角度）再往 4 移；不舒服側先是 2（反轉、水平）再往 6 移
            if (_browUncomfy) FaceController.SetBrowPose(0f, true, instant: true);
            else              FaceController.SetBrowPose(1f, false, instant: true);
        }
        FaceController.SetBrowPose(targetTilt, _browUncomfy);
    }
}
