using Godot;

/// <summary>TrainingRoomRuntime — 內褲往下拉：
///  按住內褲（沒被衣服蓋住的部分）往下拖：
///   1) 先只有內褲「上面 50%」被往下拉變形；
///   2) 上半部變形到極限後，整件內褲往下移動，最多到腿一半的位置；
///   3) 再往下拉就淡出，淡完之後內褲出現在踢開的被子上面。
///  放手時：還沒淡完就彈回原位；淡完的內褲留在被子上。被子蓋回去（DuvetState≠1）就全部復位。</summary>
public partial class TrainingRoomRuntime
{
    [ExportGroup("內褲下拉")]
    [Export] public bool PantyPullEnabled = true;
    [Export] public bool PantyDebugLog = true;   // 按下左鍵時，如果不能拖內褲，在 Output 印出原因
    [Export(PropertyHint.Range, "0.05,0.8,0.01")] public float PantyTopDeformFrac = 0.30f;   // 整件內褲最多被壓縮拉下多少（佔內褲高度）
    [Export(PropertyHint.Range, "0,0.8,0.01")] public float PantyWidenStartFrac = 0f;   // 變寬從變形階段的哪個位置才開始（0.3＝前 30% 完全不變寬，越大越晚變寬）
    [Export(PropertyHint.Range, "0,1,0.01")] public float PantyNarrowKeep = 0.85f;        // 最寬之後只縮回到最寬的幾成（0.85＝只縮 15%；1＝完全不縮；0＝縮回原寬）
    [Export] public float PantyNarrowDelayPx = 45f;     // 變寬到最寬之後「開始變窄」的位置再往下多少 px（越大，越晚才開始變窄）
    [Export(PropertyHint.Range, "0,0.6,0.01")] public float PantyWidenK = 0.12f;     // 拉的過程中左右先變寬多少（拉到極限時會縮回去）
    [Export(PropertyHint.Range, "0,0.03,0.0005")] public float PantyWobbleK = 0.008f;   // 變形後輪廓的不規則程度（避免形狀太規則）
    [Export] public float PantyDownMaxPx = 220f;       // 整件內褲最多往下移多少 px（約到腿一半）
    [Export] public float PantyRemoveExtraPx = 210f;    // 內褲拉到最下面之後，再往下拉多少 px 放手才算真的脫掉
    [Export] public float PantyFadeTime = 0.3f;        // 確定脫掉後，內褲淡出（同時出現在床上）的時間（秒）
    [Export] public float PantyPullFollow = 16f;       // 內褲跟著滑鼠的速度（越大越跟手）
    [Export] public Vector2 PantyBedUV = new Vector2(0.45f, 0.82f);   // 淡出後內褲出現在床上的位置（背景 UV，0~1）
    [Export] public float PantyBedScale = 1f;          // 床上內褲的大小（1＝跟穿在身上一樣大）
    [Export] public Vector2 PantyBedOffsetPx = new Vector2(-50f, 20f);   // 床上內褲位置微調（畫布 px；左、上為負）
    [Export] public float PantyBedRotDeg = -12f;       // 床上內褲的傾斜角度
    [Export(PropertyHint.Range, "0,1,0.01")] public float PantyBedAlpha = 0.9f;
    [Export] public float PantyRestorePx = 80f;       // 抓著床上的內褲往上拖多遠（畫面 px）就穿回去；沒拖到就彈回原位

    bool _pantyDrag, _pantyGone, _pantyBedDrag;
    float _pantyFadeA;   // 確定脫掉之後的淡出進度 0~1
    Vector2 _pantyBedOff, _pantyBedGrab;
    float _pantyStartLY, _pantyStartT;
    float _pantyPullT, _pantyPullA;          // 拉動量（V；目標／顯示）
    float _pantyTopV = -1f, _pantyMidV2 = -1f, _pantyBotV = 1f;   // 內褲內容上緣／中線／下緣（V），開機掃 alpha
    Vector2 _pantyCenterPx;
    float _pantyRightU = 1f, _pantyLeftU = 0f;
    TextureRect _pantyBed;

    void PantyScanOnce(TextureRect tr)
    {
        if (_pantyTopV >= 0f || tr?.Texture == null) return;
        try
        {
            var im = tr.Texture.GetImage();
            if (im.IsCompressed()) im.Decompress();
            int w = im.GetWidth(), h = im.GetHeight(), x0 = w, x1 = 0, y0 = h, y1 = 0;
            for (int y = 0; y < h; y += 4)
                for (int x = 0; x < w; x += 8)
                    if (im.GetPixel(x, y).A > 0.3f) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
            if (y1 > y0 && x1 > x0)
            {
                _pantyTopV = y0 / (float)h; _pantyMidV2 = (y0 + y1) * 0.5f / h; _pantyBotV = y1 / (float)h;
                _pantyCenterPx = new Vector2((x0 + x1) * 0.5f, (y0 + y1) * 0.5f);
                _pantyRightU = x1 / (float)w; _pantyLeftU = x0 / (float)w;
            }
        }
        catch (System.Exception) { }
    }

    /// <summary>變寬曲線：從 0 緩緩變到最寬，過了「最寬點（原本在一半，再往下 PantyNarrowDelayPx）」才開始變窄，拉到極限時回到 0。</summary>
    float PantyWidenCurve(float s)
    {
        float sm = PantyDLen;
        float peak = Mathf.Clamp(sm * 0.5f + PantyNarrowDelayPx / 3200f, sm * 0.1f, sm * 0.95f);
        float st = sm * Mathf.Clamp(PantyWidenStartFrac, 0f, 0.8f);   // 還沒到這裡完全不變寬
        peak = Mathf.Max(peak, st + sm * 0.05f);
        if (s <= st) return 0f;
        if (s <= peak) return Mathf.Sin(Mathf.Pi * 0.5f * Mathf.Clamp((s - st) / (peak - st), 0f, 1f));
        float back = 1f - Mathf.Cos(Mathf.Pi * 0.5f * Mathf.Clamp((s - peak) / Mathf.Max(0.0001f, sm - peak), 0f, 1f));   // 0→1
        return Mathf.Lerp(1f, Mathf.Clamp(PantyNarrowKeep, 0f, 1f), back);   // 最寬之後只縮回一點點
    }

    [Export] public float PantyDeformLenPx = 220f;   // 「上半變形（變寬、壓縮）」這一段要拖多遠（畫布 px）：越大＝變形越慢、整件開始往下平移的位置越下面
    [Export] public float PantyFixBottomFrac = 0.2f;     // 不是從 B 拉時：內褲下面這一截（佔內褲高度）不動
    [Export(PropertyHint.Range, "0,1,0.01")] public float PantyFixBottomMove = 0.15f;   // 不是從 B 拉時：下面那一截一開始跟著動的比例（0＝完全不動）
    [Export(PropertyHint.Range, "0,1,0.01")] public float PantyNonBPreFold = 0.2f;       // 不是從 B 拉時：開始縮之前，壓扁／褶皺只做到最大的幾成
    [Export(PropertyHint.Range, "0.05,1,0.01")] public float PantyNonBFoldSpeed = 0.35f; // 開始縮之後，多快折到最大（佔剩下距離的比例；越小越快）
    // ── 拉開：穿著的時候，抓內褲最下面那一截左右拉（左→右），把下緣拉到一邊 ──
    [Export] public bool PantyAsideEnabled = true;
    [Export(PropertyHint.Range, "0.05,0.6,0.01")] public float PantyAsideZoneFrac = 0.8f;   // 內褲下面多高的範圍（佔內褲高度）按住可以拉開
    [Export(PropertyHint.Range, "0,0.5,0.01")] public float PantyAsideEdgeFadeU = 0.12f;      // 右邊緣往內這一段（佔內褲圖寬）逐漸不跟著拉開（0＝右邊也一起動）
    [Export] public float PantyAsideOverMaxPx = 90f;                                         // 拉過 B 的位置之後，最多再往右多少 px（畫布 px），到了就是極限、不能繼續拉
    [Export] public float PantyAsideMaxPx = 260f;                                            // 最多拉開多遠（畫布 px）
    [Export] public float PantyAsideTrigPx = 8f;                                             // 按下後移動多遠（螢幕 px）才判斷是「左右拉開」還是「往下拉」
    [Export] public float PantyAsideRecoverFollow = 40f;                                     // 內褲開始往下移之後，拉開的位移復原得多快（越大越快）
    [Export(PropertyHint.Range, "0,1.5,0.05")] public float PantyAsideRecoverAtFrac = 1f;   // 拉到變形階段的幾成開始復原拉開（1＝內褲開始整件往下移的那一刻；0＝一開始拉就復原）
    [Export] public float PantyAsideFollow = 14f;                                            // 拉開的跟手速度
    [Export(PropertyHint.Range, "0,5,0.05")] public float PantyAsideReverseK = 2f;          // 拉過 B 的位置之後，B 右邊的部分往左退多少（1＝拉過多少就退多少）
    [Export(PropertyHint.Range, "0,0.2,0.005")] public float PantyAsideBottomFrac = 0.2f;   // 最底下這一小條的高度（佔內褲高度）
    [Export(PropertyHint.Range, "0,10,0.1")] public float PantyAsideBottomK = 0.5f;            // 這一小條往左退的倍率（再乘上 PantyAsideReverseK）
    float _pantyAsideOverT, _pantyAsideOverA;
    [Export(PropertyHint.Range, "0.05,1,0.01")] public float PantyAsidePressMaxFrac = 0.5f;   // 只有按在內褲下面這個比例以內，才能拉開（0.5＝超過 50% 的位置按下去不會拉開，改成原本的往下拉）
    [Export] public float PantyAsidePressGain = 0.5f;   // 按在最下面時，拉開的距離放大多少（0.5＝最下面按時，滑鼠移 1 內褲多拉 1.5；越上面越小，最上面＝1）
    float _pantyPressW, _pantyPressHz, _pantyPressU = 0.5f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float PantyBottomAlpha = 0.88f;           // 內褲底部（B 到會被拖動的那一段）的不透明度；上面是 PantyAlpha，往下漸層
    [Export(PropertyHint.Range, "0,0.6,0.01")] public float ClothPantyFadeAmt = 0.12f;       // 衣服在內褲的位置透明多少（0.12＝88% 不透明）
    [Export(PropertyHint.Range, "0.5,3,0.05")] public float ClothPantyFadeSpread = 1.3f;     // 衣服這塊透明範圍比內褲大多少倍（往外自然淡掉）
    [Export] public bool PantyAsideDebugDraw = false;                               // 在畫面上畫出月牙形輔助線（紅＝月牙形不變形區、藍＝外圓、綠＝內圓、黃點＝外圓中心、白線＝B、紫線＝最底下加強那條的上緣）
    [Export] public Vector2 PantyAsideCrescentCenter = new Vector2(0.25f, 0.25f);  // 外圓中心相對 B 的位置（單位＝內褲高度；x 往右、y 往上）
    [Export] public Vector2 PantyAsideCrescentShiftPx = new Vector2(-40f, 50f);     // 月牙形整個位置微調（畫布 px；x 負＝往左；y 正＝往上）
    [Export(PropertyHint.Range, "0,6,0.1")] public float PantyAsideCrescentBottomK = 0.2f;   // 月牙形最底下那一條，往右多移幾倍（越下面越多的基礎上再加強）
    [Export] public Vector2 PantyAsideCrescentInnerOffset = new Vector2(-0.26f, 0f); // 內圓中心相對外圓中心（負 x＝往左挖）
    [Export] public float PantyAsideCrescentOuterR = 0.6f;                         // 外圓半徑（單位＝內褲高度）
    [Export] public float PantyAsideCrescentInnerR = 0.52f;                         // 內圓半徑（單位＝內褲高度）
    [Export(PropertyHint.Range, "0,2,0.05")] public float PantyAsideCrescentMoveK = 0.1f;   // 月牙形整塊往左平移的量（相對往右拉的量；越下面越多，最底下再加強）
    [Export] public float PantyAsideFreezeShiftPx = 0f;   // 「右邊完全不動」的那條線：以 B 的位置為基準，再往右（正）或往左（負）移多少 px（畫布 px）；這條線右邊的內褲不管怎麼拉都完全不動
    [Export(PropertyHint.Range, "0.1,1,0.01")] public float PantyAsideTopCut = 0.45f;       // 內褲上面（離下緣超過這個比例）完全不受拉開影響，只有下面會被拉
    [Export(PropertyHint.Range, "0,0.5,0.01")] public float PantyAsideNearU = 0.10f;      // 按下的位置右邊這一段（佔內褲圖寬）內全力跟著被拉動
    [Export(PropertyHint.Range, "0.02,1,0.01")] public float PantyAsideFalloffU = 0.15f;   // 超過上面那一段之後，影響多寬逐漸減弱（越小越集中在按下的位置附近）
    [Export(PropertyHint.Range, "0,1,0.01")] public float PantyAsideFarKeep = 1f;      // 離按下的位置很遠的地方，還保留多少比例的位移（0＝完全不動）
    [Export(PropertyHint.Range, "0,1.5,0.05")] public float PantyAsideAutoComp = 0.7f;       // 往右拉的時候，右半邊被推走的量，同步用這個比例往左補償回去（不用等到拉過 B；0＝只有拉過 B 才補償）
    [Export(PropertyHint.Range, "0,2,0.05")] public float PantyAsideRevMaxRatio = 0.9f;        // 右半邊往左退，最多抵消往右拉的幾成（0.9＝最多退掉 90%，想往右拉不會反而變成往左）
    [Export(PropertyHint.Range, "0,6,0.1")] public float PantyAsideBottomMaxRatio = 3f;        // 最底下那一條往左退的上限（相對往右拉的量）；比上面寬，最底下才會大幅向左
    [Export] public float PantyAsideMidPressBoost = 4f;   // 按在內褲 50% 左右的位置往右拉時，最底下向左的補償加重多少（1＝加重一倍；0＝不加重）
    [Export(PropertyHint.Range, "0.05,0.6,0.01")] public float PantyAsideMidPressWidth = 0.2f;   // 「50% 左右」的範圍寬度（佔內褲高度）
    int _pantyMode;                 // 0＝往下拉、1＝還在判斷、2＝左右拉開
    Vector2 _pantyPressScreen;
    float _pantyAsideStartX, _pantyAsideStart, _pantyAsideT, _pantyAsideA;

    // 「縮」的位置：變寬到最寬、開始變窄的位置。過了這裡就不能掰腿（M 腿）。
    float PantyNarrowStartV => Mathf.Clamp(PantyDLen * 0.5f + PantyNarrowDelayPx / 3200f, PantyDLen * 0.1f, PantyDLen * 0.95f);
    [Export] public float PantyBlockLegExtraPx = 5f;   // 過了「縮」的位置再往下多少 px 才開始限制掰腿
    [Export(PropertyHint.Range, "0,1,0.01")] public float PantyBlockLegMaxFrac = 0.3f;   // 內褲拉過「縮」的位置之後，掰腿最多只能掰到最大的幾成（到不了就不會進 M 腿）
    public bool PantyBlocksMLeg => PantyPullEnabled && !_pantyGone && _pantyPullT > PantyNarrowStartV + PantyBlockLegExtraPx / 3200f;

    [Export(PropertyHint.Range, "-1,1.5,0.05")] public float PantyAsideCreaseCurve = 0.3f;  // 褶皺的弧度（0＝直線，越大越彎；越往上越偏右）
    [Export(PropertyHint.Range, "0,1.5,0.05")] public float PantyAsideBodyK = 0.6f;          // 非最底下的位置往左退的力道倍率（調小＝上面更不動，最底下相對更強）
    [Export(PropertyHint.Range, "0,360,1")] public float PantyAsideSprayOriginDeg = 55f;     // 花灑出發點在月牙內圓邊上的位置（度；0＝正右、90＝正下）
    [Export(PropertyHint.Range, "0,360,1")] public float PantyAsideSprayDirDeg = 160f;       // 花灑噴出去的方向（度；0＝正右、90＝正上、180＝正左）
    [Export(PropertyHint.Range, "5,90,1")] public float PantyAsideSprayHalfDeg = 40f;        // 花灑扇形的半角（度）
    [Export(PropertyHint.Range, "0.03,0.5,0.01")] public float PantyAsideSprayStep = 0.12f;  // 相鄰兩條褶皺的夾角（弧度；越小越密）
    [Export] public float PantyAsideCreaseReachPx = 260f;                                    // 右下角往外長的褶皺，最遠長多遠（內褲圖 px）
    [Export(PropertyHint.Range, "8,300,1")] public float PantyAsideCreaseAngle = 16f; // 褶皺從內褲最底往上長，相鄰兩條線的間距（內褲圖 px；越小越密）
    [Export(PropertyHint.Range, "0,1,0.01")] public float PantyAsideCreaseK = 0.5f;           // 右上斜褶皺的濃度（0＝不出現）
    [Export(PropertyHint.Range, "0,3,0.05")] public float PantyAsideMidK = 0.08f;              // 離內褲下緣 20~60% 的位置多補的向左力（越下面越大；0＝不補）
    [Export(PropertyHint.Range, "0.5,4,0.05")] public float PantyAsideReversePow = 2.2f;       // 往左退「越下面越強」的曲線：越大＝越集中在最下面、上面幾乎不動
    [Export(PropertyHint.Range, "0.05,1,0.01")] public float PantyAsideReverseFrac = 0.5f;   // 往左退的範圍：內褲靠右的幾成（0.5＝右半邊整片都會）
    [Export] public float PantyAsideReverseShiftPx = 1f;   // 「開始向左退」的判定點相對 B 的位置微調（畫布 px；負＝往左）

    /// <summary>B 綠點在內褲圖上的橫向位置（U），含 PantyAsideReverseShiftPx 微調。</summary>
    float PantyBU(TextureRect panty)
    {
        if (_hotspotRTs != null && _hotspotRTs.Length > 4 && _hotspotRTs[4] != null)
        {
            var lp = panty.GetGlobalTransform().AffineInverse() * _hotspotRTs[4].GetGlobalRect().GetCenter();
            return (lp.X + PantyAsideReverseShiftPx) / Mathf.Max(1f, panty.Size.X);
        }
        return 0.5f;
    }

    bool _pantyMoved; Vector2 _pantyDownScreen;   // 點在內褲上（不在 B）時：沒拖動就放手＝當成點小穴
    bool _pantyOnB = true;                               // 這次抓的是不是 B（按下時決定）
    float PantyDLen => Mathf.Max(PantySMax, PantyDeformLenPx / 3200f);

    bool PantyPointOnB(Vector2 screenPos)
    {
        if (_hotspotRTs == null || _hotspotRTs.Length <= 4 || _hotspotRTs[4] == null || !_hotspotRTs[4].Visible) return false;
        return _hotspotRTs[4].GetGlobalRect().HasPoint(screenPos);
    }   // 變形階段的拖動長度（V）

    float PantySMax => Mathf.Max(0.0005f, (_pantyBotV - _pantyTopV) * PantyTopDeformFrac);
    float PantyDownTotalV => PantyDownMaxV + Mathf.Max(0f, PantyRemoveExtraPx) / 3200f;   // 內褲實際往下走的總量：連「額外」那段也會跟著往下（脫掉的位置才會真的變低）
    float PantyDownMaxV => PantyDownMaxPx / 3200f;
    float PantyDoneT => PantyDLen + PantyDownMaxV + Mathf.Max(0f, PantyRemoveExtraPx) / 3200f;   // 拖到這裡放手＝脫掉

    bool PantyInput(InputEvent e)
    {
        if (!PantyPullEnabled) return false;
        bool pressed = e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true };
        if (!Layers.TryGetValue("內褲", out var pc) || pc is not TextureRect panty || !panty.IsVisibleInTree() || _pantyTopV < 0f)
        { if (pressed && PantyDebugLog) GD.Print($"[PantyDbg] 不能拖：內褲不可見或還沒掃到範圍（top={_pantyTopV}）"); _pantyDrag = false; return false; }
        if (DuvetState != 1 || _mLegOpen || _pantyGone) { if (pressed && PantyDebugLog) GD.Print($"[PantyDbg] 不能拖：被子狀態={DuvetState}（要 1＝踢開）M腿={_mLegOpen} 已脫={_pantyGone}"); _pantyDrag = false; return false; }
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
        {
            if (mb.Pressed)
            {
                if (IsPointerOverUi() || _tongueTool || _handMode != HandMode.None) { if (PantyDebugLog) GD.Print($"[PantyDbg] 不能拖：滑鼠在 UI/綠點上={IsPointerOverUi()} 舌頭工具={_tongueTool} 手模式={_handMode}"); return false; }
                Vector2 w = ScreenToWorld(mb.Position);
                if (PantyCoveredByDress(w)) { if (PantyDebugLog) GD.Print("[PantyDbg] 不能拖：這個點被衣服蓋住（衣服優先）"); return false; }   // 被衣服蓋住的部分交給衣服
                // 內褲已經被拉下去時，判定點跟著往上換算回原位
                var lp = panty.GetGlobalTransform().AffineInverse() * w;
                float downV = Mathf.Clamp(_pantyPullA - PantyDLen, 0f, PantyDownTotalV);
                float vy = lp.Y / Mathf.Max(1f, panty.Size.Y) - downV;
                float vx = lp.X / Mathf.Max(1f, panty.Size.X);
                if (!PantyHit(panty, vx, vy)) { if (PantyDebugLog) GD.Print($"[PantyDbg] 不能拖：這個點不在內褲的不透明範圍內（u={vx:F3} v={vy:F3}）"); return false; }
                _pantyOnB = PantyPointOnB(mb.Position);
                if (PantyDebugLog) GD.Print($"[PantyDbg] 按下：{(_pantyOnB ? "B 上→壓扁＋往下" : "不在 B→下面不動、只往下")}");
                _pantyMoved = false; _pantyDownScreen = mb.Position;
                _pantyDrag = true; _pantyStartLY = lp.Y; _pantyStartT = _pantyPullT;
                _pantyMode = 0;
                if (PantyAsideEnabled && _pantyPullA < 0.004f && vy >= _pantyBotV - PantyAsideZoneFrac * (_pantyBotV - _pantyTopV)
                    && (_pantyBotV - vy) <= PantyAsidePressMaxFrac * (_pantyBotV - _pantyTopV))
                {
                    _pantyMode = 1; _pantyPressScreen = mb.Position; _pantyPressU = vx;
                    float zh_ = Mathf.Max(0.001f, PantyAsideZoneFrac * (_pantyBotV - _pantyTopV));
                    float t_ = Mathf.Clamp((vy - (_pantyBotV - zh_)) / zh_, 0f, 1f);
                    _pantyPressHz = Mathf.Clamp((_pantyBotV - vy) / Mathf.Max(0.001f, _pantyBotV - _pantyTopV), 0f, 1f);   // 按下的位置離下緣多高（0＝最下面，1＝最上面）
                    _pantyPressW = Mathf.Pow(t_ * t_ * (3f - 2f * t_), 1.6f);   // 按下的位置越靠下面越大（0~1）
                }   // 按在下面那一截：先判斷是左右拉開還是往下拉
                return true;
            }
            if (_pantyDrag)
            {
                _pantyDrag = false;
                if (!_pantyOnB && !_pantyMoved && _pantyPullT < 0.002f && _pantyAsideT < 0.002f)
                {   // 點內褲下面（沒向右／向下拖）：當成點小穴 B
                    _pantyMode = 0;
                    if (PantyDebugLog) GD.Print("[PantyDbg] 沒拖動就放手：當成點小穴");
                    OnHotspotClicked(4);
                    return true;
                }
                if (_pantyMode != 0) { _pantyMode = 0; return true; }   // 拉開／只是點了一下：不做脫掉判定
                string verdict_ = _pantyPullT >= PantyDoneT - 0.0005f ? "脫掉" : "沒到，停住";
                if (PantyDebugLog) GD.Print($"[PantyDbg] 放手：拉到 {_pantyPullT * 3200f:F0}px／脫掉門檻 {PantyDoneT * 3200f:F0}px（上半變形 {PantyDLen * 3200f:F0} + 下移 {PantyDownMaxPx:F0} + 額外 {PantyRemoveExtraPx:F0}）  變窄延遲={PantyNarrowDelayPx:F0}px 變寬={PantyWidenK:F2} → {verdict_}");
                if (_pantyPullT >= PantyDoneT - 0.0005f) { _pantyGone = true; _pantyPullT = PantyDoneT; _pantyFadeA = 0f; }   // 確定脫掉：才開始淡出，內褲出現在床上
                // 還沒淡完：停在放手的位置（不彈回去），再抓住往上拖可以拉回去
                return true;
            }
        }
        else if (e is InputEventMouseMotion mm && _pantyDrag)
        {
            if ((mm.Position - _pantyDownScreen).Length() >= PantyAsideTrigPx) _pantyMoved = true;
            var lp = panty.GetGlobalTransform().AffineInverse() * ScreenToWorld(mm.Position);
            if (_pantyMode == 1)
            {
                Vector2 dd = mm.Position - _pantyPressScreen;
                if (dd.Length() < PantyAsideTrigPx) return true;
                if (Mathf.Abs(dd.X) > Mathf.Abs(dd.Y))
                {
                    _pantyMode = 2; _pantyAsideStartX = lp.X; _pantyAsideStart = _pantyAsideT;
                    if (PantyDebugLog) GD.Print($"[PantyDbg] 拉開開始：按下U={_pantyPressU:F2} 高度比={_pantyPressHz:F2}  近={PantyAsideNearU:F2} 衰減={PantyAsideFalloffU:F2} 遠處保留={PantyAsideFarKeep:F2} 頂部切={PantyAsideTopCut:F2} 範圍={PantyAsideZoneFrac:F2} 加成={PantyAsidePressGain:F1}/{PantyAsideMidPressBoost:F1} 底K={PantyAsideBottomK:F1}");
                }
                else _pantyMode = 0;
            }
            if (_pantyMode == 2)
            {
                float szx = Mathf.Max(1f, panty.Size.X);
                float limX = PantyBU(panty) * szx + PantyAsideOverMaxPx;   // 過了 B 之後最多再拉這麼遠，到了就是極限
                float curX = Mathf.Min(lp.X, limX);
                _pantyAsideT = Mathf.Clamp(_pantyAsideStart + (curX - _pantyAsideStartX) * (1f + PantyAsidePressGain * _pantyPressW) / szx, 0f, PantyAsideMaxPx / szx);
                _pantyAsideOverT = Mathf.Max(0f, curX / szx - PantyBU(panty));   // 滑鼠拉過 B 多遠
                return true;
            }
            _pantyPullT = Mathf.Clamp(_pantyStartT + (lp.Y - _pantyStartLY) / Mathf.Max(1f, panty.Size.Y), 0f, PantyDoneT);
            return true;
        }
        return false;
    }

    /// <summary>這個點現在是不是被衣服蓋住：衣擺已經被拉高時，衣擺邊緣以下就不算蓋住（用拉高後的衣擺判斷，不是原本的貼圖形狀）。</summary>
    /// <summary>這個點現在是不是「內褲要接手」：內褲看得到、點在內褲上、而且這裡的衣服已經被拉開（視覺上沒蓋住）。衣服的判定用來讓路。</summary>
    bool PantyWantsPress(Vector2 w)
    {
        if (!PantyPullEnabled || DuvetState != 1 || _mLegOpen || _pantyGone || _pantyTopV < 0f) return false;
        if (!Layers.TryGetValue("內褲", out var pc) || pc is not TextureRect panty || !panty.IsVisibleInTree()) return false;
        if (PantyCoveredByDress(w)) return false;
        var lp = panty.GetGlobalTransform().AffineInverse() * w;
        float downV = Mathf.Clamp(_pantyPullA - PantyDLen, 0f, PantyDownTotalV);
        return PantyHit(panty, lp.X / Mathf.Max(1f, panty.Size.X), lp.Y / Mathf.Max(1f, panty.Size.Y) - downV);
    }

    bool PantyCoveredByDress(Vector2 w)
    {
        if (!IsPointOnLayer("衣服", w)) return false;
        if (!Layers.TryGetValue("衣服", out var cloth) || cloth == null) return true;
        var lp = cloth.GetGlobalTransform().AffineInverse() * w;
        float v = lp.Y / Mathf.Max(1f, cloth.Size.Y), ux = lp.X / Mathf.Max(1f, cloth.Size.X);
        float edge = ClothHemEndV - HemProfile(ux);   // 衣擺被拉高後的邊緣（V）
        return v <= edge + 0.003f;
    }

    /// <summary>床上的內褲：抓住可以拖動，往上拖超過 PantyRestorePx 放手就穿回去（跟踢開的被子蓋回去一樣）；沒拖到就彈回。</summary>
    bool PantyBedInput(InputEvent e)
    {
        if (!_pantyGone || DuvetState != 1 || _pantyBed == null || !_pantyBed.Visible) { _pantyBedDrag = false; return false; }
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
        {
            if (mb.Pressed)
            {
                if (IsPointerOverUi() || _tongueTool || _handMode != HandMode.None) return false;
                Vector2 w = ScreenToWorld(mb.Position);
                if (IsPointOnLayer("衣服", w) || IsPointOnLayer("m腿/腿1", w) || IsPointOnLayer("m腿/腿2", w)) return false;   // 衣服／腿在上面：優先給衣服，不被床上的內褲搶走
                var lp = _pantyBed.GetGlobalTransform().AffineInverse() * w;
                if (!PantyHit(_pantyBed, lp.X / Mathf.Max(1f, _pantyBed.Size.X), lp.Y / Mathf.Max(1f, _pantyBed.Size.Y))) return false;
                _pantyBedDrag = true; _pantyBedGrab = _pantyBedOff - w;
                return true;
            }
            if (_pantyBedDrag)
            {
                _pantyBedDrag = false;
                if (-_pantyBedOff.Y >= PantyRestorePx)
                { _pantyGone = false; _pantyPullT = 0f; _pantyPullA = 0f; _pantyFadeA = 0f; _pantyBedOff = Vector2.Zero; }   // 穿回去
                return true;
            }
        }
        else if (e is InputEventMouseMotion mm && _pantyBedDrag)
        {
            _pantyBedOff = ScreenToWorld(mm.Position) + _pantyBedGrab;
            return true;
        }
        return false;
    }

    bool PantyHit(TextureRect panty, float u, float v)
    {
        if (u < 0f || u > 1f || v < 0f || v > 1f) return false;
        if (!_hitImageCache.TryGetValue("內褲", out var img) || img == null)
        {
            img = panty.Texture.GetImage();
            if (img == null) return false;
            if (img.IsCompressed()) img.Decompress();
            _hitImageCache["內褲"] = img;
        }
        int x = Mathf.Clamp((int)(u * img.GetWidth()), 0, img.GetWidth() - 1), y = Mathf.Clamp((int)(v * img.GetHeight()), 0, img.GetHeight() - 1);
        return img.GetPixel(x, y).A > 0.1f;
    }

    /// <summary>每幀（Cloth.cs 更新內褲 shader 時）：算顯示值、推 shader 參數、管理床上的內褲。</summary>
    void PantyPullApply()
    {
        if (_pantyMat == null || !Layers.TryGetValue("內褲", out var pc) || pc is not TextureRect panty) return;
        PantyScanOnce(panty);
        if (DuvetState != 1 && (_pantyGone || _pantyPullT != 0f || _pantyPullA != 0f))
        { _pantyGone = false; _pantyDrag = false; _pantyPullT = 0f; _pantyPullA = 0f; _pantyFadeA = 0f; }
        float dt = (float)GetProcessDeltaTime();
        _pantyPullA += (_pantyPullT - _pantyPullA) * Mathf.Clamp(PantyPullFollow * dt, 0f, 1f);
        float u = Mathf.Clamp(_pantyPullA / PantyDLen, 0f, 1f);   // 變形階段進度 0~1（越慢才到 1）
        float fixW = 1f;
        float s = u * PantySMax;
        if (!_pantyOnB)
        {   // 不從 B 拉：開始縮（過了最寬點）之前只做一點點壓扁，之後迅速折到最大，下面那一截也才跟上
            float sm_ = PantyDLen;
            float pk_ = Mathf.Clamp(sm_ * 0.5f + PantyNarrowDelayPx / 3200f, sm_ * 0.1f, sm_ * 0.95f);
            float pu_ = pk_ / sm_;
            float fold = u <= pu_ ? 0f : Mathf.Clamp((u - pu_) / Mathf.Max(0.02f, (1f - pu_) * PantyNonBFoldSpeed), 0f, 1f);
            fold = fold * fold * (3f - 2f * fold);
            float amt = Mathf.Lerp(PantyNonBPreFold * (pu_ > 0f ? Mathf.Min(1f, u / pu_) : 1f), 1f, fold);
            s = amt * PantySMax;
            fixW = Mathf.Lerp(PantyFixBottomMove, 1f, fold);
        }
        _pantyMat.SetShaderParameter("fix_w", fixW);
        float down = Mathf.Clamp(_pantyPullA - PantyDLen, 0f, PantyDownTotalV);
        _pantyFadeA = _pantyGone ? Mathf.Min(1f, _pantyFadeA + dt / Mathf.Max(0.01f, PantyFadeTime)) : 0f;
        float fade = _pantyFadeA;
        _pantyMat.SetShaderParameter("top_v", _pantyTopV);
        _pantyMat.SetShaderParameter("mid_v", _pantyMidV2);
        _pantyMat.SetShaderParameter("bot_v", _pantyBotV);
        _pantyMat.SetShaderParameter("widen", PantyWidenK * PantyWidenCurve(Mathf.Min(_pantyPullA, PantyDLen)));   // 先變寬、再縮回去
        _pantyMat.SetShaderParameter("wob", PantyWobbleK * u);
        _pantyMat.SetShaderParameter("cx_u", _pantyCenterPx.X / Mathf.Max(1f, panty.Size.X));
        _pantyMat.SetShaderParameter("pull_s", s);
        _pantyMat.SetShaderParameter("fix_cut", _pantyOnB ? -1f : _pantyBotV - Mathf.Clamp(PantyFixBottomFrac, 0f, 0.9f) * (_pantyBotV - _pantyTopV));
        _pantyMat.SetShaderParameter("down_y", down);
        _pantyMat.SetShaderParameter("alpha_k", 1f - fade);
        PantyWetApply(panty);
        _pantyMat.SetShaderParameter("pa_k", AllAgesLocked ? 1f : Mathf.Clamp(PantyBottomAlpha / Mathf.Max(0.01f, PantyAlpha), 0f, 1.2f));
        if (_clothMat != null && _pantyTopV >= 0f && Layers.TryGetValue("衣服", out var clothL_) && clothL_ is TextureRect clothTr_ && clothTr_.Texture != null && panty.Texture != null)
        {   // 衣服在內褲的位置，大概 ClothPantyFadeAmt 的透明，往外擴開（換算到衣服貼圖 UV／px）
            Vector2 pSz_ = panty.Texture.GetSize(), cSz_ = clothTr_.Texture.GetSize();
            Vector2 cScr_ = new Vector2(Mathf.Max(1f, clothTr_.Size.X), Mathf.Max(1f, clothTr_.Size.Y));
            Vector2 ctr_ = panty.Position + new Vector2(_pantyCenterPx.X / pSz_.X * panty.Size.X, _pantyMidV2 * panty.Size.Y);
            Vector2 rScr_ = new Vector2((_pantyRightU - _pantyLeftU) * 0.5f * panty.Size.X, (_pantyBotV - _pantyTopV) * 0.5f * panty.Size.Y) * ClothPantyFadeSpread;
            _clothMat.SetShaderParameter("ta_pc", (ctr_ - clothTr_.Position) / cScr_);
            _clothMat.SetShaderParameter("ta_pr", new Vector2(rScr_.X * cSz_.X / cScr_.X, rScr_.Y * cSz_.Y / cScr_.Y));
            _clothMat.SetShaderParameter("ta_pa", AllAgesLocked ? 0f : ClothPantyFadeAmt);
        }
        if (_pantyGone || DuvetState != 1) { _pantyAsideT = 0f; _pantyAsideOverT = 0f; }
        if (_pantyAsideT < 0.0005f) _pantyAsideOverT = 0f;   // 脫掉／被子蓋回去：拉開復原
        // 脫內褲：底部被拉開的情況下，內褲一開始往下移（底部開始產生位移），拉開的位移就快速復原
        bool slidingDown_ = _pantyPullA > PantyDLen * PantyAsideRecoverAtFrac;
        if (slidingDown_) { _pantyAsideT = 0f; _pantyAsideOverT = 0f; }
        float fol_ = (slidingDown_ ? PantyAsideRecoverFollow : PantyAsideFollow) * (float)GetProcessDeltaTime();
        _pantyAsideA += (_pantyAsideT - _pantyAsideA) * Mathf.Clamp(fol_, 0f, 1f);
        _pantyAsideOverA += (Mathf.Min(_pantyAsideOverT, _pantyAsideA) - _pantyAsideOverA) * Mathf.Clamp(fol_, 0f, 1f);
        _pantyMat.SetShaderParameter("aside_bw", Mathf.Max(0.0005f, PantyAsideBottomFrac * (_pantyBotV - _pantyTopV)));
        _pantyMat.SetShaderParameter("aside_bk", PantyAsideBottomK);
        _pantyMat.SetShaderParameter("aside_rp", PantyAsideReversePow);
        float dh_ = Mathf.Abs(_pantyPressHz - 0.5f) / Mathf.Max(0.05f, PantyAsideMidPressWidth);
        float bump_ = 1f - Mathf.Clamp(dh_, 0f, 1f); bump_ = bump_ * bump_ * (3f - 2f * bump_);
        _pantyMat.SetShaderParameter("aside_pb", 1f + PantyAsideMidPressBoost * (_pantyMode == 2 || _pantyAsideT > 0.0005f ? bump_ : 0f));
        float hPx_ = Mathf.Max(1f, (_pantyBotV - _pantyTopV) * panty.Size.Y);
        _pantyMat.SetShaderParameter("aside_ao", PantyAsideCrescentCenter + PantyAsideCrescentShiftPx / hPx_);   // 月牙整個往左／右微調（px）
        _pantyMat.SetShaderParameter("aside_bo", PantyAsideCrescentInnerOffset);
        _pantyMat.SetShaderParameter("aside_ra", PantyAsideCrescentOuterR);
        _pantyMat.SetShaderParameter("aside_rb", PantyAsideCrescentInnerR);
        _pantyMat.SetShaderParameter("aside_ta", _pantyAsideA * PantyAsideCrescentMoveK);
        _pantyMat.SetShaderParameter("aside_tb", PantyAsideCrescentBottomK);
        _pantyMat.SetShaderParameter("aside_pa", _pantyAsideA);
        _pantyMat.SetShaderParameter("aside_dbg", PantyAsideDebugDraw ? 1f : 0f);
        _pantyMat.SetShaderParameter("aside_tc", PantyAsideTopCut);
        _pantyMat.SetShaderParameter("aside_fz", PantyBU(panty) + PantyAsideFreezeShiftPx / Mathf.Max(1f, panty.Size.X));
        _pantyMat.SetShaderParameter("aside_fr", 1f);
        _pantyMat.SetShaderParameter("aside_pu", _pantyPressU);
        _pantyMat.SetShaderParameter("aside_nu", PantyAsideNearU);
        _pantyMat.SetShaderParameter("aside_fw", PantyAsideFalloffU);
        _pantyMat.SetShaderParameter("aside_fk", PantyAsideFarKeep);
        _pantyMat.SetShaderParameter("aside_oth", PantyAsideSprayOriginDeg);
        _pantyMat.SetShaderParameter("aside_axd", PantyAsideSprayDirDeg);
        _pantyMat.SetShaderParameter("aside_hw", PantyAsideSprayHalfDeg);
        _pantyMat.SetShaderParameter("aside_cstep", PantyAsideSprayStep);
        _pantyMat.SetShaderParameter("aside_cr", PantyAsideCreaseReachPx);
        _pantyMat.SetShaderParameter("aside_ca", PantyAsideCreaseAngle);
        _pantyMat.SetShaderParameter("aside_cv", PantyAsideCreaseCurve);
        _pantyMat.SetShaderParameter("aside_body", PantyAsideBodyK);
        _pantyMat.SetShaderParameter("aside_ck", PantyAsideCreaseK);
        _pantyMat.SetShaderParameter("aside_mk", PantyAsideMidK);
        _pantyMat.SetShaderParameter("aside_s", _pantyRightU - Mathf.Clamp(PantyAsideReverseFrac, 0.05f, 1f) * (_pantyRightU - _pantyLeftU));
        _pantyMat.SetShaderParameter("aside_b", PantyBU(panty));
        float revRaw_ = Mathf.Max(_pantyAsideOverA * PantyAsideReverseK, _pantyAsideA * PantyAsideAutoComp);   // 拉過 B 才有的往左退，加上「只要右邊被推就同步補償」
        _pantyMat.SetShaderParameter("aside_rev", Mathf.Min(revRaw_, _pantyAsideA * PantyAsideRevMaxRatio));     // 往左退最多抵消往右拉的幾成：想往右拉不會反而往左跑
        _pantyMat.SetShaderParameter("aside_revb", Mathf.Min(revRaw_, _pantyAsideA * PantyAsideBottomMaxRatio)); // 最底下那一條的上限比較寬
        _pantyMat.SetShaderParameter("aside_x", _pantyAsideA);
        _pantyMat.SetShaderParameter("aside_r", _pantyRightU);
        _pantyMat.SetShaderParameter("aside_rf", Mathf.Max(0.001f, PantyAsideEdgeFadeU));
        _pantyMat.SetShaderParameter("aside_h", Mathf.Max(0.001f, PantyAsideZoneFrac * (_pantyBotV - _pantyTopV)));
        PantyBedUpdate(panty);
    }

    // ── 濕度：B 附近的內褲被色彩增值蓋上濕痕，濕度越高越深；往下拉之後慢慢淡掉 ──
    [Export] public bool PantyWetEnabled = true;
    [Export(PropertyHint.Range, "0,1,0.01")] public float PantyWetMax = 0.85f;                  // 濕度 100 時濕痕的強度
    [Export(PropertyHint.Range, "0.3,3,0.05")] public float PantyWetCurve = 1.2f;               // 濕度→強度的曲線（>1：低濕度時淡、高濕度才明顯）
    [Export(PropertyHint.Range, "0,0.95,0.01")] public float PantyWetCore = 0.5f;               // 濕痕中心保持滿強度的比例（越大越集中、邊緣越硬）
    [Export] public Color PantyWetColor = new Color(0.70f, 0.74f, 0.88f);                       // 色彩增值的顏色（越暗越深）
    [Export] public Vector2 PantyWetRadiusUV = new Vector2(0.06f, 0.04f);                       // 濕痕大小（內褲貼圖 UV，左右／上下）
    [Export] public Vector2 PantyWetOffsetUV = new Vector2(0f, 0f);                             // 濕痕位置微調（內褲貼圖 UV；相對 B 的位置）
    [Export(PropertyHint.Range, "0,4,0.05")] public float PantyWetFadeRate = 1.3f;              // 往下拉的進度乘上這個值＝淡掉的程度（1.3＝拉到 77% 就全淡掉；越大淡得越快）
    float _pantyWetK;

    void PantyWetApply(TextureRect panty)
    {
        float wet01 = Mathf.Clamp(Wetness / 100f, 0f, 1f);
        float prog = Mathf.Clamp(_pantyPullA / Mathf.Max(0.0001f, PantyDLen + PantyDownTotalV), 0f, 1f);
        float k = PantyWetEnabled ? PantyWetMax * Mathf.Pow(wet01, PantyWetCurve) * (1f - Mathf.Clamp(prog * PantyWetFadeRate, 0f, 1f)) : 0f;
        _pantyWetK = Mathf.Lerp(_pantyWetK, k, Mathf.Clamp(6f * (float)GetProcessDeltaTime(), 0f, 1f));   // 平滑，不會跳
        Vector2 c = new Vector2(0.5f, 0.6f);
        if (_hotspotRTs != null && _hotspotRTs.Length > 4 && _hotspotRTs[4] != null)
        {
            var lp = panty.GetGlobalTransform().AffineInverse() * _hotspotRTs[4].GetGlobalRect().GetCenter();   // B 綠點中心 → 內褲圖本地座標
            c = new Vector2(lp.X / Mathf.Max(1f, panty.Size.X), lp.Y / Mathf.Max(1f, panty.Size.Y));
        }
        _pantyMat.SetShaderParameter("wet_k", _pantyWetK);
        _pantyMat.SetShaderParameter("wet_c", c + PantyWetOffsetUV);
        _pantyMat.SetShaderParameter("wet_r", PantyWetRadiusUV);
        _pantyMat.SetShaderParameter("wet_core", PantyWetCore);
        _pantyMat.SetShaderParameter("wet_col", new Vector3(PantyWetColor.R, PantyWetColor.G, PantyWetColor.B));
    }

    void PantyBedUpdate(TextureRect panty)
    {
        bool show = _pantyGone && DuvetState == 1;
        if (_pantyBed == null)
        {
            if (!show) return;
            var bg = _canvas?.GetNodeOrNull<Control>("BG");
            if (bg == null || _duvetKicked == null) return;
            _pantyBed = new TextureRect
            {
                Name = "PantyOnBed", Texture = panty.Texture, MouseFilter = Control.MouseFilterEnum.Ignore,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
            };
            var bowTex = ResourceHelper.LoadTexAny("Sprites/蝴蝶結");   // 內褲上的蝴蝶結：脫下來之後也跟著出現在床上的內褲上
            if (bowTex != null)
                _pantyBed.AddChild(new TextureRect
                {
                    Name = "BowOnPanty", Texture = bowTex, MouseFilter = Control.MouseFilterEnum.Ignore,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
                    Position = BowPantyOffsetPx, Size = bowTex.GetSize(),
                });
            _canvas.AddChild(_pantyBed);
            _canvas.MoveChild(_pantyBed, _duvetKicked.GetIndex() + 1);   // 在踢開的被子上面、角色下面
        }
        _pantyBed.Visible = show;
        if (!show) return;
        var bg2 = _canvas.GetNodeOrNull<Control>("BG");
        if (bg2 == null) return;
        _pantyBed.Size = panty.Size;
        _pantyBed.PivotOffset = _pantyCenterPx;
        var gs = panty.GetGlobalTransform().Scale;   // 跟穿在身上時一樣的大小（含人物縮放、鏡頭縮放以外的比例）
        _pantyBed.Scale = new Vector2(Mathf.Abs(gs.X), Mathf.Abs(gs.Y)) * PantyBedScale;
        _pantyBed.Rotation = Mathf.DegToRad(PantyBedRotDeg);
        Vector2 target = bg2.GetGlobalTransform() * (bg2.Size * PantyBedUV);   // _canvas 是 CanvasLayer：子節點的位置就是畫布座標
        if (!_pantyBedDrag) _pantyBedOff *= Mathf.Clamp(1f - 14f * (float)GetProcessDeltaTime(), 0f, 1f);   // 沒抓著：平滑彈回原位
        _pantyBed.Position = target + PantyBedOffsetPx + _pantyBedOff - _pantyCenterPx;
        var night = _charRoot != null ? _charRoot.Modulate : Colors.White;   // 跟人物一樣吃夜晚濾鏡
        _pantyBed.Modulate = new Color(night.R, night.G, night.B, PantyBedAlpha * _pantyFadeA);   // 隨淡出慢慢出現
    }
}
