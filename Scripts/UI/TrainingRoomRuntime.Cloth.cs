using Godot;

/// <summary>
/// TrainingRoomRuntime — 衣服貼身變形。
/// 衣服（睡衣）要像真的貼在身上：
///   1. 跟身體一樣的整體縮放（呼吸脹縮）。
///   2. 套用跟「身體」同一組 shader 參數（M 腿上提漸層、拖膝蓋時的下緣拉伸），每幀從身體的材質複製。
///   3. 胸部區塊：胸1／胸2 相對身體的位移與縮放（呼吸、吸氣上提、摸胸動畫），衣服對應區域用 shader 取樣位移跟著動，
///      區域邊緣用平滑衰減，所以只有胸口那一塊會變形。
/// 所有參數是 Export，Play 模式下可即時調。
/// </summary>
public partial class TrainingRoomRuntime
{
    [ExportGroup("衣服微小透明")]
    [Export(PropertyHint.Range, "0,0.6,0.01")] public float ClothHemFadeAmt = 0.12f;     // 衣服最底下透明多少（0.12＝透出底下 12%）
    [Export(PropertyHint.Range, "0.005,0.2,0.005")] public float ClothHemFadeV = 0.03f;  // 下擺往上漸變的高度（V；0.03＝約 96px）
    [Export(PropertyHint.Range, "0,0.6,0.01")] public float ClothChestFadeAmt = 0.10f;   // 胸口中心透明多少（範圍用胸口變形區的大小，往外自然淡掉）
    [ExportGroup("")]
    [Export(PropertyHint.Range, "0,2,0.05")] public float ClothChestFollow  = 0.5f;   // 衣服胸口區塊跟著胸部動的比例（0＝不跟，1＝完全貼著）：1 → 0.5，變形減半
    [Export] public float ClothChestRadiusX = 260f;   // 胸口變形區橢圓半寬（畫布 px）
    [Export] public float ClothChestRadiusY = 230f;   // 胸口變形區橢圓半高（畫布 px）
    [Export(PropertyHint.Range, "0,0.95,0.05")] public float ClothChestInner = 0.35f; // 區域內側這個比例以內完全跟隨，往外平滑衰減到 0

    // ── 衣擺（衣服尾端）：呼吸吸氣時左右往外擴張，越靠下擺擴得越多 ──
    [Export] public float ClothHemBreathAmount = 0.03f;   // 吸氣時下擺最大擴張量（0.03＝約 3%）
    [Export] public float ClothHemStartV       = 0.40f;   // 衣擺擴張開始的位置（貼圖 V：0＝畫布頂、1＝底）
    [Export] public float ClothHemEndV         = 0.5275f; // 衣擺最下緣（衣服圖內容底部 y=1688/3200）
    [Export] public float ClothHemCenterU      = 0.488f;  // 衣擺中心線（衣服內容 644..1308 的中點）
    [Export] public float ClothHemBTouchGain   = 0f;      // 摸 B 時小穴變形量帶給衣擺的左右擴張比例（0＝不跟；使用者要求摸 B 時衣服最底不要再疊加左右位移）
    // ── 內褲：摸 B 時跟小穴一樣「中間往左右撐開」──
    [Export] public float ClothPantyHalfWidthU = 0.07f;  // 內褲中心撐開區半寬（貼圖 U；小穴是 0.105，內褲比小穴寬）
    [Export(PropertyHint.Range, "0.01,0.3,0.005")] public float ClothPantyHalfHeightV = 0.035f;   // 內褲變形範圍的上下半高（V；越小越只在 B 附近）
    [Export(PropertyHint.Range, "0,0.1,0.001")] public float ClothPantyShiftK = 0.03f;   // 內褲被撐時往下位移的量
    [Export(PropertyHint.Range, "0,1,0.01")] public float ClothPantyTwistK = 0.25f;   // 內褲被撐時的歪斜（旋轉感）   // M 腿還沒開、點 B 時，內褲上半部的變形比例（1＝不減弱，0＝上半部完全不變形）
    [Export] public float ClothPantyStretchGain = 1f;     // 內褲撐開量 ＝ 小穴撐開量 × 這個值

    // ── 衣擺上拉：抓衣服最下緣往上拉（只有被抓的那一點上移，左右順勢往那一點傾斜），下緣會有點捲起來。
    //    不是 Q 彈：用指數趨近、沒有過衝；放手只回復 ClothHemRecover，其餘維持。 ──
    [Export] public float ClothHemPullMax     = 0.1554f;  // 最多往上拉多少（貼圖 V）＝影響範圍的 97%
    [Export] public float ClothHemPullBandV   = 0.1671f;   // 衣擺上拉影響最下面 41%（衣服高約 0.4075）；範圍內是壓縮捲起，最多壓到剩 15%   // 往上受影響的範圍（V），越往上影響越小
    [Export] public float ClothHemPullWidthU  = 0.20f;   // 左右影響寬度（U，高斯半寬）
    [Export] public float ClothHemLean        = 0f;      // （已停用）左右不縮小
    [Export] public float ClothHemInward      = 0.7f;    // 往上拉後下擺向內收的程度（0＝不收；越大越尖）
    [Export] public float ClothHemRollV       = 0.014f;  // 捲邊厚度（V）
    [Export] public float ClothHemRollStrength = 1f;     // 捲邊明暗強度（0＝不捲）
    [Export] public float ClothHemGrabBandV   = 0.07f;  // 衣擺下緣往上多少範圍內可以抓（V）
    [Export(PropertyHint.Range, "0,1,0.05")] public float ClothHemRecover = 0f; // 放手回復的比例
    [Export] public float ClothHemFollow      = 12f;     // 跟隨滑鼠的速度（越大越快；指數趨近，不會彈）

    // ── 衣服上半（最上面 40%）：不受衣擺上拉影響；左上／右上兩個角可以各自拖曳（旋轉＋拉伸，也可以往下拉），放手維持 ──
    [Export] public float ClothTopV          = 0.12f;   // 衣服內容頂端（貼圖 V）
    [Export] public float ClothLeftU         = 0.322f;  // 衣服內容左緣（U）
    [Export] public float ClothRightU        = 0.654f;  // 衣服內容右緣（U）
    [Export(PropertyHint.Range, "0,10,0.5")] public float ClothPullRightJitterPx = 3f;   // 向上拉時右邊緣每個橫條再隨機往右 0~幾 px（階梯起伏的大小，只往右）
    [Export(PropertyHint.Range, "0,20,0.5")] public float ClothPullRightShiftPx = 5f;   // 向上拉時，右邊的衣服整體往右幾 px（補右邊階梯被切掉／破圖的地方）
    [Export] public float ClothTopFrac       = 0.36f;    // 上半區佔衣服高度比例
    [Export] public float ClothTopGrabRadius = 170f;    // （已停用）
    // 上半部新規則：最上面一小段（旋轉段）不拉伸、只旋轉——左半逆時針、右半順時針（兩邊往下垂）；
    // 影響段（旋轉段下面一段）跟著漸弱變形；兩邊都轉到解鎖角度以上，才能在旋轉段~36% 之間往下拉（壓縮）。不能往上拉。
    [Export] public float ClothTopRotFrac       = 0.10f;  // 旋轉段佔衣服高度比例（從衣服頂端算）
    [Export] public float ClothTopInflFrac      = 0.15f;  // 旋轉段下面受影響變形的範圍比例
    [Export] public float ClothYellowShiftPx    = 2f;     // 黃線位置微調（正=向下；0＝原位置）
    [Export] public bool  ClothBodyRotate       = false;  // 衣服本體上緣是否跟著旋轉（預設關：旋轉的是左右肩帶）
    [Export] public float ClothOrangeRoundU     = 0.06f;  // 橘線中間的圓弧半徑（U；0＝尖角，越大越圓）
    [Export] public float ClothOrangeShiftPx    = 6f;    // 橘線位置微調（正=向下、負=向上；單位畫布像素）
    // 套用微調後的實際比例（shader、變形、點擊判定全部共用，三者位置一致）
    // 位移單位 = 畫面上看到的像素（依衣服實際縮放換算成畫布像素）
    [Export] public float ClothOrangeTiltDeg    = 30f;    // 橘線傾斜角度：左半逆時針、右半順時針（兩邊往下垂成 ∧ 形）；0＝水平直線
    float ClothOrangeK => Mathf.Tan(Mathf.DegToRad(ClothOrangeTiltDeg)) * 2000f / 3200f;   // U 轉 V 的斜率（畫布 2000×3200）
    [Export] public float ClothTopXComp         = 0.5f;   // 旋轉時橫向跑位的補償比例（0＝不補償，1＝完全抵銷橫向位移）；左右兩邊都生效
    float ClothOrangeHalfW => Mathf.Max(0.01f, (ClothRightU - ClothLeftU) * 0.5f);
    float _clothPxScale = 1f;
    float ClothRotFracEff  => ClothTopRotFrac + (ClothYellowShiftPx + StrapLineFollowPx()) * _clothPxScale / 3200f / Mathf.Max(0.001f, ClothHemEndV - ClothTopV);
    // 橘線不能高過黃線（影響段長度不能變負，否則漸變會算反、衣服被壓扁），最少保留 0.01 比例
    float ClothInflFracEff => Mathf.Max(0.01f, ClothTopInflFrac + (ClothOrangeShiftPx - ClothYellowShiftPx) * _clothPxScale / 3200f / Mathf.Max(0.001f, ClothHemEndV - ClothTopV));
    [Export] public float ClothTopRotMax        = 90f;    // 最大旋轉角度（度）
    [Export] public float ClothTopStretchUnlockDeg = 90f; // 兩邊肩帶都拉到這個角度以上（90＝拉到底）才能往下拉
    [Export] public float ClothTopStretchMaxFrac = 0.35f; // 往下拉最多壓縮上半區（頂端~36%）的比例（原 0.93 會把衣服頂端一路拉到嘴巴）
    [Export] public bool  ClothShowGuides       = false;   // 臨時：在衣服上畫分界切線（白＝頂端、黃＝旋轉段下緣10%、橘＝影響段下緣、綠＝36%、青＝下擺範圍上緣、洋紅＝旋轉中心線）

    // 肩帶（剛體：先平移、再繞自己最底端旋轉）整套邏輯在 TrainingRoomRuntime.Strap.cs
    static readonly string[] ClothPartLayers = { "衣服/左帶", "衣服/右帶" };   // 衣服拆出來的兩條帶子：共用本體的材質與縮放
    ShaderMaterial _clothMat, _pantyMat;
    [Export] public float ClothTopGrabExtraFrac = 0.35f;   // 肩帶到底後，衣服上半部可以抓來往下拉的範圍再往下多多少（佔衣服高度比例）
    float[] _rotA = new float[2], _rotT = new float[2], _stA = new float[2], _stT = new float[2];   // 上半部：旋轉角（度，左/右）與下拉量（V，左/右）
    float[] _midRotS = new float[2], _midStS = new float[2];
    float _topStartA, _topStartVal, _topStartLY;
    float ClothTopCx => (ClothLeftU + ClothRightU) * 0.5f;
    float _midStartY, _midHemS0, _midHemS1; bool _midDrag;
    int _topDrag;
    bool ClothDragActive => _topDrag != 0 || _hemDrag || _midDrag || _pantyDrag || _pantyBedDrag;   // 正在拖衣服：綠點 hover 游標不要換
    float[] _hemPullA = new float[2], _hemTargetA = new float[2], _hemPullXA = { 0.40f, 0.58f };
    float _hemStartLocalY, _hemStartTarget; int _hemSlot;
    bool _hemDrag;

    const string ClothShaderCode = @"
shader_type canvas_item;
uniform sampler2D leg_tex : filter_linear;   // M 腿（腿／腳／足）離屏渲染：衣服在最上面時被 M 腿裁切
uniform vec2 lg_x = vec2(1.0, 0.0);
uniform vec2 lg_y = vec2(0.0, 1.0);
uniform vec2 lg_o = vec2(0.0);
uniform vec2 cl_size = vec2(2000.0, 3200.0);
uniform float lg_on = 0.0;
// ── 雞雞裁切衣服（MLegPantyAside.cs）：入口以下露在外面的雞雞蓋在衣服上面；入口以上（插進去的部分）衣服照常蓋住 ──
uniform sampler2D jc_tex : filter_linear;
uniform vec2 jc_x = vec2(1.0, 0.0);
uniform vec2 jc_y = vec2(0.0, 1.0);
uniform vec2 jc_o = vec2(0.0);
uniform float jc_on = 0.0;
uniform float jc_from_y = 1500.0;
uniform float jc_arch = 105.0;   // 雞雞裁衣服的起點弧度：兩側比中間低多少 px
uniform float expand_amount = 0.0;
uniform float center_u = 0.5225;
uniform float taper_start_v = 0.3716;
uniform float taper_end_v = 0.5472;
uniform float base_ratio = 0.0;
uniform float lift_top_v = 0.0;
uniform float lift_bottom_v = 0.0;
uniform float lift_neck_v = 0.12;
uniform float lift_navel_v = 0.36;
uniform float lift_bottom_end_v = 0.55;
uniform float stretch_s = 1.0;     // 全年齡下擺拉長：開始的位置（V）
uniform float stretch_ext = 0.0;   // 全年齡下擺拉長：往下多拉的量（V）
uniform float ta_hem = 0.0;      // 下擺微小透明：最底下的透明度（0~1）
uniform float ta_hem_h = 0.03;   // 下擺微小透明：往上漸變的高度（V）
uniform vec2 ta_pc = vec2(0.5, 0.5);   // 內褲中心（UV）
uniform vec2 bump_c1 = vec2(-10.0);   // 肚子的凸起：中心（UV）
uniform vec2 bump_r1 = vec2(0.05);    // 凸起範圍半徑（UV）
uniform float bump_k1 = 0.0;          // 凸起強度 0~1
uniform vec2 bump_c2 = vec2(-10.0);   // 插入的凸起
uniform vec2 bump_r2 = vec2(0.05);
uniform float bump_k2 = 0.0;
uniform vec2 mv_c = vec2(-10.0);   // 抽插時衣服跟著上下動：中心（UV）／範圍／位移（UV.y）
uniform vec2 mv_r = vec2(0.05);
uniform float mv_dy = 0.0;
uniform float bump_see = 0.0;         // 凸起位置衣服多透出多少（0.12＝衣服剩 88% 不透明，跟內褲位置一樣）
uniform vec2 ta_pr = vec2(300.0, 300.0); // 內褲位置的透明範圍半徑（px）
uniform float ta_pa = 0.0;       // 內褲位置的衣服透明度（0.12＝88% 不透明）
uniform float ta_chest = 0.0;    // 胸口微小透明：中心的透明度（0~1）
uniform float hem_amount = 0.0;
uniform float hem_start_v = 0.40;
uniform float hem_end_v = 0.5275;
uniform float hem_center_u = 0.488;
uniform vec2 rot_ang = vec2(0.0);
uniform vec2 st_amt = vec2(0.0);
uniform float line3_v = -1.0;   // 第三條標註線（橘線下面 StrapNoShrinkBelowPx）：這條以下不再內縮
uniform float crease_s = 0.0045;   // 每多拉這麼多（V）就多出一條折疊褶皺
uniform vec2 over_amt = vec2(0.0);   // 拉到上限後繼續拉的量（V，左/右）：中間折疊褶皺往下移、上緣繼續剛體位移
uniform vec2 top_ext = vec2(0.40, 0.57);   // 衣服上緣左右兩端（U）：折線的位置依這個算，不是整件衣服的最寬處
uniform float shrink_v = -1.0;      // 橘線位置（V）：以下的衣服開始往中間靠
uniform float shrink_k = 0.1;       // 橘線以下往中間靠的幅度（每往下拉 1px，往中間 k px）
uniform float top_spread_k = 0.5;   // 往下拉時左右外擴：每往下 1px，左半往左／右半往右 k px
uniform vec4 corner_t = vec4(0.0);   // 左/右肩帶底部的位移（UV）：xy＝左、zw＝右
uniform vec4 corner_p = vec4(0.0);   // 衣服左上角／右上角（綁在肩帶底部往上 1.5px）的原位（UV）
uniform float corner_r = 240.0;      // 角落綁定：完全跟著肩帶走的範圍之外，逐漸衰減到 0 的外半徑（畫布 px）
uniform vec4 bow_c = vec4(-10.0);    // 蝴蝶結（左/右上角）目前位置（UV）
uniform vec4 bow_d = vec4(0.0);      // 蝴蝶結中心的位移（UV）：範圍內整塊剛體平移，不變形
uniform vec2 bow_h = vec2(100.0, 60.0);   // 蝴蝶結不變形的半徑／往外漸變的寬度（畫布 px）
uniform float corner_h = 100.0;      // 角落綁定：完全跟著肩帶走（剛體）的半徑（畫布 px）
uniform float mark2_v = -1.0;       // 第二條標註線（橘色）：青線再往下一點，內縮開始的位置
uniform float mark_v = -1.0;        // 標註線 V（肩帶向下 StrapDownPx 的位置）；<0 不畫
uniform float top_cx = 0.488;
uniform float rot_frac = 0.10;
uniform float infl_frac = 0.15;
uniform float x_comp = 0.0;
uniform float orange_w = 0.0;   // 衣服半寬（U）：橘線在衣服左右邊緣與原高度相同，中間往上凸
uniform float orange_r = 0.06;  // 橘線中間圓弧半徑（U）：越大中間越圓、越平滑
uniform float orange_k = 0.0;   // 橘線斜率（UV：離中心每 1 單位 U，往下掉幾單位 V）
uniform float show_guides = 0.0;
uniform vec4 fan_a = vec4(0.0);      // 肩帶錨點目前位置（UV）：xy＝左、zw＝右
uniform vec2 fan_t = vec2(0.0);      // 左/右肩帶褶皺強度 0~1
uniform vec4 fan_p = vec4(18.0, 46.0, 70.0, 2.2);
uniform float fan_b = 8.0;           // 褶皺影響範圍：離衣服上緣往下幾 px（畫布）   // 兩條線的角度（度，離垂直向下）、長度(px)、線寬(px)
uniform float top_v = 0.12;
uniform float top_h = 0.163;
uniform float left_u = 0.322;
uniform float right_u = 0.654;
uniform float pull_rshift = 0.0025;   // 向上拉時右邊的衣服整體往右多少（U；5px/2000）
uniform float pull_rjit = 0.0015;     // 右邊緣每個橫條額外隨機再往右 0~這麼多（U；3px/2000）
uniform vec2 pull_amt2 = vec2(0.0);
uniform vec2 pull_xs = vec2(0.40, 0.58);
uniform float pull_w = 0.16;
uniform float pull_band = 0.12;
uniform float inward = 0.5;
uniform float zoom_k = 1.0;   // 鏡頭縮放：線寬（螢幕像素）跟著縮放，拉遠不會糊成一坨
uniform float pull_lean = 0.6;
uniform float roll_v = 0.014;
uniform float roll_str = 1.0;
uniform vec2 chest1_c = vec2(0.4885, 0.3119);
uniform vec2 chest1_off = vec2(0.0);
uniform float chest1_s = 1.0;
uniform vec2 chest2_c = vec2(0.4365, 0.3088);
uniform vec2 chest2_off = vec2(0.0);
uniform float chest2_s = 1.0;
uniform vec2 chest_r = vec2(260.0, 230.0);
uniform float chest_inner = 0.35;
varying vec4 vc;
void vertex() { vc = COLOR; }
void chest(inout vec2 uv, vec2 c, vec2 off, float s) {
    vec2 px = vec2(2000.0, 3200.0);
    vec2 d = (uv - c) * px / chest_r;
    float w = 1.0 - smoothstep(chest_inner, 1.0, length(d));
    uv -= w * off;
    uv = c + (uv - c) / (1.0 + w * (s - 1.0));
}
void fragment() {
    float gxa = (UV.x - pull_xs.x) / max(0.001, pull_w);
    float gxb = (UV.x - pull_xs.y) / max(0.001, pull_w);
    vec2 pxs = pull_xs; vec2 am = pull_amt2;
    if (pxs.x > pxs.y) { pxs = pxs.yx; am = am.yx; }
    float tmid = smoothstep(pxs.x, pxs.y + 0.0001, UV.x);
    float dxo = max(max(pxs.x - UV.x, UV.x - pxs.y), 0.0);
    // 兩個拉點之間用平滑內插補滿（不會在中間出現夾角），兩個拉點之外才往外衰減
    float wsum = mix(am.x, am.y, tmid) * exp(-pow(dxo / max(0.001, pull_w), 4.0));
    float pull_amt = wsum;
    float wy = smoothstep(hem_end_v - pull_band, hem_end_v, UV.y);
    wy *= step(top_v + top_h, UV.y);   // 最上面 40% 完全不受上拉影響
    float pw = wy;
    vec2 P = UV;
    if (stretch_ext > 0.0 && UV.y > stretch_s) {   // 全年齡：衣服最底下一段往下拉長
        float sh_ = hem_end_v - stretch_s;
        P.y = stretch_s + (UV.y - stretch_s) * sh_ / (sh_ + stretch_ext);
    }
    float frp = clamp(wsum / max(0.001, pull_band), 0.0, 1.0);
    float sx_ = UV.x * (12.0 + 28.0 * frp);   // 拉越高，鋸齒越多
    float sc_ = floor(sx_);
    float sh0 = fract(sin(sc_ * 12.9898) * 43758.5453);
    float sh1 = fract(sin((sc_ + 1.0) * 12.9898) * 43758.5453);
    float sf_ = fract(sx_); sf_ = sf_ * sf_ * (3.0 - 2.0 * sf_);
    float stairH = mix(sh0, sh1, sf_);   // 圓滑的起伏（不是硬階梯）
    float stair = (stairH - 0.5) * (0.008 + 0.022 * frp) * clamp(wsum / 0.03, 0.0, 1.0) * smoothstep(0.0, 0.3, wsum / max(0.001, pull_band) * 3.0);
    float bt = hem_end_v - pull_band;
    float comp = pull_band / max(0.001, pull_band - min(wsum, pull_band * 0.93));
    if (UV.y > bt) P.y = bt + (UV.y - bt) * comp;   // 下緣往上壓縮捲起（不是整片平移）
    P.y += stair * wy * comp;   // 乘上壓縮倍率：螢幕上看到的起伏大小不會因為壓縮而縮小
    // 左右邊緣也帶不規則的階梯：被折的區段每隔一小段橫向錯位一點
    float sideH = fract(sin(floor(UV.y * 60.0) * 78.233) * 43758.5453);
    float sideAmt = clamp(wsum / 0.03, 0.0, 1.0) * smoothstep(bt, bt + 0.03, UV.y);   // 漸入：不要在折起區段最上面突然錯位一大塊
    // 下擺是金字塔形（底部寬）：往上拉後越靠近邊緣越往內收，避免拉起來的底部變得比上面還寬
    float uR = clamp((UV.y - bt) / max(0.001, hem_end_v - wsum - bt), 0.0, 1.0);
    float uIn = pow(uR, 0.5) * step(bt, UV.y);   // 開根號：上面也會收，不只靠近邊緣
    float pf = clamp(wsum / max(0.001, pull_band), 0.0, 1.0);
    // 離底部 10%~30% 那一段（剛被拉伸的地方）往外擴一點點
    float bump = smoothstep(0.62, 0.72, uR) * (1.0 - smoothstep(0.88, 0.94, uR));
    float sq = 1.0 + inward * pf * uIn - 0.12 * pf * bump;
    P.x = hem_center_u + (P.x - hem_center_u) * sq;   // 邊緣被折時帶不規則的階梯狀
    {   // 階梯的橫向錯位：放在往內收（sq）之後，用「取樣座標 P.x」判斷是不是衣服邊緣。
        // 之前放在 sq 之前、用畫面位置 UV.x 判斷：拉起來後邊緣被收到畫面內側很多，根本沒被當成邊緣，
        // 某一個橫條往內（左）錯位一格，衣服右緣就整段縮進去，後面的身體跑出來。
        // 現在：右邊緣只准往外錯位（取樣往左＝內容往右）、左邊緣同理，邊緣不會再被吃掉；
        // 右邊的衣服在被拉起的區段整體再往右 pull_rshift。
        float stairX_ = (sideH - 0.5) * 0.014 * sideAmt;
        // 「這一列」真正的左右邊緣在哪：直接看貼圖透明度往左右找（right_u／left_u 是整件衣服最寬處，
        // 其他列的邊緣都比它內側，用它判斷會漏掉，那些列就還是會整段往左錯位）
        float wR_ = 0.0, wL_ = 0.0;
        if (sideAmt > 0.001) {
            float dR_ = 1.0, dL_ = 1.0;
            for (int i = 8; i >= 1; i--) {
                float o_ = 0.004 * float(i);
                if (texture(TEXTURE, vec2(P.x + o_, P.y)).a < 0.1) dR_ = o_;
                if (texture(TEXTURE, vec2(P.x - o_, P.y)).a < 0.1) dL_ = o_;
            }
            wR_ = 1.0 - smoothstep(0.012, 0.032, dR_);   // 離右緣 <12px 完全當邊緣、>32px 不管，中間漸變
            wL_ = 1.0 - smoothstep(0.012, 0.032, dL_);
        }
        // 右緣：只「一點點往右」（pull_rshift ＋ 0~pull_rjit）；左緣：只「一點點往左」（0~pull_rjit）；中間照舊隨機
        float edgeR_ = -(pull_rshift + sideH * pull_rjit) * sideAmt;
        float edgeL_ = sideH * pull_rjit * sideAmt;
        float sx_r = mix(stairX_, edgeR_, wR_);
        P.x += mix(sx_r, edgeL_, wL_);
    }
    // ───── 上半部：往下拉＝壓縮（跟下擺同一套）；旋轉段只旋轉（左逆時針／右順時針）─────
    float hh_ = hem_end_v - top_v;
    float y10_ = top_v + hh_ * rot_frac;
    float y25_ = y10_ + hh_ * infl_frac;
    float tb_ = top_v + top_h;
    float zone_ = max(0.001, tb_ - top_v);
    float sdp = mix(st_amt.x, st_amt.y, smoothstep(top_cx - 0.06, top_cx + 0.06, UV.x));
    float ovr = mix(over_amt.x, over_amt.y, smoothstep(top_cx - 0.06, top_cx + 0.06, UV.x));
    float dEff_ = min(sdp, zone_ * 0.93);
    float compT = zone_ / max(0.001, zone_ - dEff_);
    float frt = clamp(sdp / zone_, 0.0, 1.0);
    float tsx = UV.x * (10.0 + 20.0 * frt);
    float tsc = floor(tsx);
    float th0 = fract(sin(tsc * 15.731) * 43758.5453);
    float th1 = fract(sin((tsc + 1.0) * 15.731) * 43758.5453);
    float tsf = fract(tsx); tsf = tsf * tsf * (3.0 - 2.0 * tsf);
    float tstair = (mix(th0, th1, tsf) - 0.5) * (0.006 + 0.016 * frt) * clamp(sdp / 0.02, 0.0, 1.0);
    if (UV.y < tb_) {
        // 黃線以上：只整段平移、不縮放（大小不變）；黃線~綠線之間才壓縮
        float py10_ = tb_ - (tb_ - y10_) * compT;
        P.y = (UV.y < y10_) ? UV.y + (py10_ - y10_) : tb_ - (tb_ - UV.y) * compT;
        P.y += tstair * compT * (1.0 - smoothstep(0.0, 0.4, (UV.y - top_v - dEff_) / zone_)) * smoothstep(3.0 / 3200.0, 10.0 / 3200.0, UV.y - top_v - dEff_);   // 上緣不規則的起伏
    }
    {   // 往下拉超過肩帶向下的距離後，上半部左右外擴（左半往左、右半往右）
        float sdirT_ = 2.0 * (1.0 - smoothstep(top_cx - 0.06, top_cx + 0.06, UV.x)) - 1.0;
        float spwT_ = 1.0 - smoothstep(y10_, tb_, UV.y);
        float belowO_ = shrink_v >= 0.0 ? smoothstep(shrink_v, shrink_v + 0.012, UV.y) : 0.0;   // 橘線以下
        spwT_ *= 1.0 - belowO_;   // 橘線以下不再外擴
        if (line3_v >= 0.0) belowO_ *= 1.0 - smoothstep(line3_v - 0.003, line3_v + 0.003, UV.y);   // 第三條線以下完全不內縮
        P.x += sdirT_ * (sdp + ovr) * 3200.0 * top_spread_k / 2000.0 * spwT_;
        float topKeep_ = mix(0.25, 1.0, smoothstep(3.0 / 3200.0, 15.0 / 3200.0, UV.y - top_v - dEff_ - ovr));   // 衣服最上面 3px 附近：超過橘線時內縮減弱
        P.x -= sdirT_ * (sdp + ovr) * 3200.0 * shrink_k / 2000.0 * belowO_ * topKeep_;   // 橘線以下：左半往右、右半往左（往中間靠）
        P.y -= ovr * spwT_;   // 過拉：上緣（沒被壓縮的部分）繼續剛體往下，不變形
    }
    {   // 衣服最左上角／最右上角綁在肩帶底部：跟著肩帶位移，周圍平滑帶動
        vec2 PXc = vec2(2000.0, 3200.0);
        float wcl = 1.0 - smoothstep(corner_h, corner_r, length((UV - (corner_p.xy + corner_t.xy)) * PXc));
        float wcr = 1.0 - smoothstep(corner_h, corner_r, length((UV - (corner_p.zw + corner_t.zw)) * PXc));
        P -= wcl * corner_t.xy + wcr * corner_t.zw;
    }
    {   // 蝴蝶結範圍：不變形，只整塊跟著中心位移（等於把範圍內的變形全部反補償掉）
        vec2 PXb = vec2(2000.0, 3200.0);
        float wbl = 1.0 - smoothstep(bow_h.x, bow_h.x + bow_h.y, length((UV - bow_c.xy) * PXb));
        float wbr = 1.0 - smoothstep(bow_h.x, bow_h.x + bow_h.y, length((UV - bow_c.zw) * PXb));
        P = mix(P, UV + bow_d.xy, wbl);
        P = mix(P, UV + bow_d.zw, wbr);
    }
    {
        vec2 PXs = vec2(2000.0, 3200.0);
        float fyr = 1.0 - smoothstep(y10_, max(y10_ + 0.004, y25_ + (sqrt((P.x - top_cx) * (P.x - top_cx) + orange_r * orange_r) - orange_r) * orange_k), P.y);
        float wl_ = 1.0 - smoothstep(top_cx - 0.01, top_cx + 0.01, P.x);
        float ang_ = (rot_ang.y * (1.0 - wl_) - rot_ang.x * wl_) * fyr;
        vec2 d_ = (P - vec2(top_cx, y10_)) * PXs;
        float cs_ = cos(ang_), sn_ = sin(ang_);
        vec2 d2_ = vec2(d_.x * cs_ + d_.y * sn_, -d_.x * sn_ + d_.y * cs_);
        vec2 P0_ = P;
        P = vec2(top_cx, y10_) + d2_ / PXs;
        P.x = P0_.x + (P.x - P0_.x) * (1.0 - x_comp * fyr);   // 左右水平位移補償：旋轉造成的橫向跑位按比例抵銷
    }
    float raw = clamp((P.y - taper_start_v) / max(0.001, taper_end_v - taper_start_v), 0.0, 1.0);
    float t = mix(base_ratio, 1.0, raw);
    float shrink = 1.0 - t * expand_amount;
    vec2 uv = vec2(center_u + (P.x - center_u) * shrink, P.y);
    uv.y += lift_top_v * smoothstep(lift_neck_v, lift_navel_v, P.y) + lift_bottom_v * smoothstep(lift_navel_v, lift_bottom_end_v, P.y);
    float hem = smoothstep(hem_start_v, hem_end_v, P.y) * hem_amount;
    uv.x = hem_center_u + (uv.x - hem_center_u) * (1.0 - hem);
    chest(uv, chest1_c, chest1_off, chest1_s);
    chest(uv, chest2_c, chest2_off, chest2_s);
    float see_ = 0.0;   // 肚子／插入位置衣服的透明度（衣服蓋在它們上面，但保留一點透出）
    {   // 肚子／插入頂出來的凸起：以中心往外放大（透鏡）
        vec2 bd1_ = (uv - bump_c1) / bump_r1; float bl1_ = dot(bd1_, bd1_);
        if (bump_k1 > 0.0005) see_ = max(see_, bump_see * (1.0 - smoothstep(0.3, 1.0, sqrt(bl1_))));
        if (bump_k1 > 0.0005 && bl1_ < 1.0) uv = bump_c1 + (uv - bump_c1) * (1.0 - bump_k1 * (1.0 - bl1_));
        vec2 bd2_ = (uv - bump_c2) / bump_r2; float bl2_ = dot(bd2_, bd2_);
        if (bump_k2 > 0.0005 && bl2_ < 1.0) uv = bump_c2 + (uv - bump_c2) * (1.0 - bump_k2 * (1.0 - bl2_));
    }
    {   // 抽插時衣服跟著插入上下動（位移集中在插入位置，往外高斯衰減）
        vec2 md_ = (uv - mv_c) / mv_r;
        uv.y -= mv_dy * exp(-dot(md_, md_));
    }
    vec4 c = texture(TEXTURE, uv);
    if (c.a > 0.01 && pull_amt > 0.0005 && roll_str > 0.0) {
        float dist = roll_v;
        for (int i = 6; i >= 1; i--) {
            float st = roll_v * float(i) / 6.0;
            if (texture(TEXTURE, vec2(uv.x, uv.y + st)).a < 0.1) dist = st;
        }
        float bt = 1.0 - dist / roll_v;
        float rs = clamp(pull_amt / 0.02, 0.0, 1.0) * roll_str;
        float hl = 0.16 * sin(bt * 3.14159) * rs;
        float dk = 0.30 * smoothstep(0.7, 1.0, bt) * rs;
        c.rgb = c.rgb * (1.0 - dk) + vec3(hl);
    }
    if (c.a > 0.01) {
        float cr = 0.0;
        for (int sl = 0; sl < 2; sl++) {
            float amt = sl == 0 ? pull_amt2.x : pull_amt2.y;
            float gg = sl == 0 ? gxa : gxb;
            float gw = exp(-pow(gg, 4.0));
            if (amt < 0.003 || gw < 0.25) continue;   // 左右各自的折線，不連在一起
            float px = sl == 0 ? pull_xs.x : pull_xs.y;
            float edgeY = hem_end_v - wsum;
            float span = edgeY - bt;   // 折線只分布在「中間不變的 15%」下緣到衣服下緣之間
            for (int i = 1; i <= 2; i++) {
                float fr2 = clamp(amt / max(0.001, pull_band), 0.0, 1.0);
                float dd = i == 1 ? (0.012 + 0.035 * fr2) : (0.04 + 0.10 * fr2);   // 越上面、拉越高，離底部越遠
                float f = 1.0 - min(dd, span * 0.92) / max(0.001, span);
                float jit = fract(sin(float(i) * 91.7 + float(sl) * 13.1) * 43758.5453) - 0.5;
                // 假弧形：兩條直線在被拉的那一點相交成一個角（頂點朝上）
                float ly = bt + span * f + jit * 0.004 + abs(UV.x - px) * 0.16;
                float dl = abs(UV.y - ly);
                float w = fwidth(UV.y) * zoom_k;
                float seg = 1.0 - smoothstep(0.3, 0.55, abs(UV.x - px) / max(0.001, pull_w * (1.0 - 0.45 * f) * (1.0 - 0.3 * clamp(amt / pull_band, 0.0, 1.0))));   // 折線只在被拉的那一點附近，越靠下緣、拉越高就越短（跟著金字塔變窄）
                cr = max(cr, (1.0 - smoothstep(w * 0.15, w * 0.7, dl)) * seg * clamp(amt / 0.03, 0.0, 1.0));
            }
        }
        {   // 快拉到底時，中間多一條 V 形折痕（夾角約 150°，頂點朝下）
            float nf = smoothstep(0.55, 0.8, frp);
            float vcx = (pull_xs.x + pull_xs.y) * 0.5;
            float vEdge = hem_end_v - wsum;
            float vApex = bt + (vEdge - bt) * 0.4;
            float vly = vApex - abs(UV.x - vcx) * 0.1675;   // tan(15°) × (2000/3200)
            float vw = fwidth(UV.y) * zoom_k;
            float vm = 1.0 - smoothstep(0.08, 0.14, abs(UV.x - vcx));
            cr = max(cr, (1.0 - smoothstep(vw * 0.15, vw * 0.7, abs(UV.y - vly))) * vm * nf);
        }
        cr *= step(0.1, texture(TEXTURE, vec2(uv.x, uv.y + max(fwidth(uv.y), 1e-6) * 3.0 * zoom_k)).a);   // 不碰到衣服最下面 3px（螢幕像素）
        // 底部補一條邊線：折起來之後看不到原本的下緣線條
        float ew = max(fwidth(uv.y), 1e-6) * (1.0 + 1.5 * frp) * zoom_k;   // 螢幕像素寬度，壓縮很大時也看得到；越高越粗
        float eg = (1.0 - step(0.1, texture(TEXTURE, vec2(uv.x, uv.y + ew)).a)) * clamp(wsum / 0.01, 0.0, 1.0);
        cr = max(cr, eg);
        c.rgb = mix(c.rgb, mix(c.rgb * vec3(0.5, 0.1, 0.1), vec3(0.32, 0.02, 0.04), 0.7), clamp(cr, 0.0, 1.0) * (0.95 + 0.0));
    }
    if (c.a > 0.01 && sdp > 0.003) {
        float cr2 = 0.0;
        float fwT = max(fwidth(uv.y), 1e-6);
        for (int sl2 = 0; sl2 < 2; sl2++) {
            float amtT = sl2 == 0 ? st_amt.x : st_amt.y;
            if (amtT < 0.003) continue;
            float scx = sl2 == 0 ? mix(top_ext.x, top_cx, 0.5) : mix(top_cx, top_ext.y, 0.5);
            float sideM = sl2 == 0 ? 1.0 - smoothstep(top_cx - 0.02, top_cx + 0.02, UV.x) : smoothstep(top_cx - 0.02, top_cx + 0.02, UV.x);
            float edgeT = top_v + amtT;
            float spanT = tb_ - edgeT;
            float fr3 = clamp(amtT / zone_, 0.0, 1.0);
            for (int j = 1; j <= 2; j++) {
                float asy_ = sl2 == 0 ? 1.0 : 1.3;   // 右邊鏡射但不對稱：離上緣更遠一點、斜度與長度不同
                float dd2 = (j == 1 ? (0.003 + 0.010 * fr3) : (0.008 + 0.028 * fr3)) * asy_;   // 褶皺貼近上緣（不要跑到衣服很下面）
                float jt = fract(sin(float(j) * 57.3 + float(sl2) * 9.7) * 43758.5453) - 0.5;
                float ly2 = edgeT + min(dd2, spanT * 0.92) + jt * 0.004 - abs(UV.x - scx) * (sl2 == 0 ? 0.1675 : 0.125);   // 兩條直線相交成一個角（頂點朝下）
                float w2 = fwidth(UV.y) * zoom_k;
                float seg2 = 1.0 - smoothstep(0.3, 0.55, abs(UV.x - scx) / max(0.001, (top_ext.y - top_ext.x) * 0.35));
                cr2 = max(cr2, (1.0 - smoothstep(w2 * 0.15, w2 * 0.7, abs(UV.y - ly2))) * seg2 * sideM * clamp(amtT / 0.03, 0.0, 1.0));
            }
        }
        {   // 往下拉越多，中間多一條 V 形折痕（跟下擺同一套，頂點朝下）
            float nfT = smoothstep(0.3, 0.6, frt);
            float vApexT = top_v + dEff_ + (tb_ - top_v - dEff_) * 0.4;
            float vlyT = vApexT - abs(UV.x - top_cx) * 0.1675;
            float vwT = fwidth(UV.y) * zoom_k;
            float vmT = 1.0 - smoothstep(0.08, 0.14, abs(UV.x - top_cx));
            cr2 = max(cr2, (1.0 - smoothstep(vwT * 0.15, vwT * 0.7, abs(UV.y - vlyT))) * vmT * nfT);
        }
        cr2 *= step(0.1, texture(TEXTURE, vec2(uv.x, uv.y - fwT * 3.0 * zoom_k)).a);
        {   // 上緣被拉下來時，邊緣也有捲起的明暗（跟下擺同一套）
            float distT = roll_v;
            for (int ri = 6; ri >= 1; ri--) {
                float stT_ = roll_v * float(ri) / 6.0;
                if (texture(TEXTURE, vec2(uv.x, uv.y - stT_)).a < 0.1) distT = stT_;
            }
            float btT = 1.0 - distT / roll_v;
            float rsT = clamp(sdp / 0.02, 0.0, 1.0) * roll_str;
            c.rgb = c.rgb * (1.0 - 0.30 * smoothstep(0.7, 1.0, btT) * rsT) + vec3(0.16 * sin(btT * 3.14159) * rsT);
        }
        float ewT = fwT * (1.0 + 1.5 * frt) * zoom_k;
        float egT = (1.0 - step(0.1, texture(TEXTURE, vec2(uv.x, uv.y - ewT)).a)) * clamp(sdp / 0.01, 0.0, 1.0);
        cr2 = max(cr2, egT);
        c.rgb = mix(c.rgb, mix(c.rgb * vec3(0.5, 0.1, 0.1), vec3(0.32, 0.02, 0.04), 0.7), clamp(cr2, 0.0, 1.0) * 0.95);
    }
    float pullOwn_ = sdp + ovr;
    float pullOth_ = mix(st_amt.y + over_amt.y, st_amt.x + over_amt.x, smoothstep(top_cx - 0.06, top_cx + 0.06, UV.x));
    float pullAll_ = max(pullOwn_, 0.7 * pullOth_);   // 只拉一邊時另一半也會跟著出現褶皺
    if (c.a > 0.01 && pullAll_ > 0.0008) {   // 往下拉（青線以下）就逐條出現；不用等到拉到底。過拉：衣服原本沒動的中間部分出現折疊褶皺，隨過拉往下移動
        float fm2 = 0.0;
        float fw3 = fwidth(UV.y) * zoom_k;
        for (int fk = 0; fk < 3; fk++) {
            // 一小段一小段（不連成一條）：每格隨機決定有沒有，高度也各自錯開；整體限制在衣服上半部
            float cx_ = (UV.x - top_cx) * 26.0 + float(fk) * 3.7;   // 以衣服中心為準切格（左右對稱分布，不再全擠在左邊）
            float cellN = floor(cx_);
            float cellF = fract(cx_);
            float cellOn = step(0.3, fract(sin(cellN * 91.7 + float(fk) * 17.3) * 43758.5453));   // 每條分成幾大段，有些段沒有，不會一條貫穿
            float cellJ = (sin(cx_ * 0.9 + float(fk) * 2.1) * 0.6 + sin(cx_ * 2.3 + float(fk) * 4.0) * 0.4) * 0.5;   // 連續的高低起伏（不整齊但不斷）
            float fb = top_v + dEff_ + zone_ * (0.2 + 0.25 * float(fk)) + ovr * (0.25 + 0.1 * float(fk)) + cellJ * 0.004;   // 從衣服上緣算起，集中在上半部
            fb = min(fb, top_v + hh_ * 0.32);
            float fly = fb - abs(UV.x - top_cx) * 0.08;
            float segM = smoothstep(0.0, 0.3, cellF) * (1.0 - smoothstep(0.7, 1.0, cellF)) * cellOn;   // 每段兩端漸淡收尾（不像裁切線）
            fm2 = max(fm2, (1.0 - smoothstep(fw3 * 0.2, fw3 * 0.9, abs(UV.y - fly))) * clamp((pullAll_ - float(fk) * crease_s) / 0.0025, 0.0, 1.0) * segM);
        }
        c.rgb = mix(c.rgb, mix(c.rgb * vec3(0.5, 0.1, 0.1), vec3(0.32, 0.02, 0.04), 0.7), clamp(fm2, 0.0, 1.0) * 0.9);
    }
    if (c.a > 0.01 && (fan_t.x > 0.01 || fan_t.y > 0.01)) {
        // 肩帶錨點的「花灑」褶皺：從錨點往下向中間散開，每條肩帶 2 條線
        vec2 PXf = vec2(2000.0, 3200.0);
        float pxw = fwidth(UV.y) * 3200.0 * zoom_k;
        float fm = 0.0;
        for (int fs = 0; fs < 2; fs++) {
            float ft = fs == 0 ? fan_t.x : fan_t.y;
            if (ft < 0.01) continue;
            vec2 a0 = fs == 0 ? fan_a.xy : fan_a.zw;
            float fan_in = fs == 0 ? 1.0 : -1.0;
            vec2 pp = (UV - a0) * PXf;
            for (int fj = 0; fj < 2; fj++) {
                float an = radians(fj == 0 ? fan_p.x : fan_p.y);
                vec2 dr = vec2(sin(an) * fan_in, cos(an));
                float al = dot(pp, dr);
                float pe = abs(pp.x * dr.y - pp.y * dr.x);
                float lenf = fan_p.z * (fj == 0 ? 1.0 : 0.8) * ft;
                float bandV = a0.y + fan_b / 3200.0;
                float bm = 1.0 - smoothstep(a0.y + fan_b * 0.6 / 3200.0, bandV, UV.y);   // 只影響衣服上緣往下 fan_b px
                float lm = smoothstep(0.0, 8.0, al) * (1.0 - smoothstep(lenf * 0.55, lenf, al)) * bm;
                float wl = max(fan_p.w, pxw);
                fm = max(fm, (1.0 - smoothstep(wl * 0.3, wl, pe)) * lm * ft);
            }
        }
        c.rgb = mix(c.rgb, mix(c.rgb * vec3(0.5, 0.1, 0.1), vec3(0.32, 0.02, 0.04), 0.7), clamp(fm, 0.0, 1.0) * 0.9);
    }
    if (mark_v >= 0.0 && c.a > 0.01) {   // 標註線：肩帶向下走完（StrapDownPx）的位置
        float mw_ = fwidth(UV.y) * 1.2;
        float mm_ = 1.0 - smoothstep(mw_ * 0.4, mw_ * 1.0, abs(UV.y - mark_v));
        c.rgb = mix(c.rgb, vec3(0.0, 1.0, 1.0), mm_);
    }
    if (mark2_v >= 0.0 && c.a > 0.01) {
        float m2w_ = fwidth(UV.y) * 1.2;
        float m2_ = 1.0 - smoothstep(m2w_ * 0.4, m2w_ * 1.0, abs(UV.y - mark2_v));
        c.rgb = mix(c.rgb, vec3(1.0, 0.6, 0.0), m2_);
    }
    if (line3_v >= 0.0 && mark2_v >= 0.0 && c.a > 0.01) {   // 第三條標註線（洋紅）
        float m3w_ = fwidth(UV.y) * 1.2;
        c.rgb = mix(c.rgb, vec3(1.0, 0.0, 1.0), 1.0 - smoothstep(m3w_ * 0.4, m3w_ * 1.0, abs(UV.y - line3_v)));
    }
    if (show_guides > 0.5 && texture(TEXTURE, UV).a > 0.01) {
        float gw_ = fwidth(UV.y) * 1.5;
        vec3 gc = vec3(0.0); float gm = 0.0;
        float lv[6] = float[6](top_v, y10_, y25_ + (sqrt((UV.x - top_cx) * (UV.x - top_cx) + orange_r * orange_r) - orange_r) * orange_k, tb_, hem_end_v - pull_band, hem_end_v);
        vec3 lc[6] = vec3[6](vec3(1.0), vec3(1.0, 0.95, 0.0), vec3(1.0, 0.55, 0.0), vec3(0.1, 1.0, 0.2), vec3(0.0, 0.9, 1.0), vec3(1.0));
        for (int g = 0; g < 6; g++) {
            float m = 1.0 - smoothstep(gw_ * 0.4, gw_ * 1.0, abs(UV.y - lv[g]));
            if (m > gm) { gm = m; gc = lc[g]; }
        }
        float mv = (1.0 - smoothstep(fwidth(UV.x) * 0.4, fwidth(UV.x) * 1.0, abs(UV.x - top_cx))) * step(UV.y, tb_);
        if (mv > gm) { gm = mv; gc = vec3(1.0, 0.0, 1.0); }
        c.rgb = mix(c.rgb, gc, gm);
        c.a = max(c.a, gm);
    }
    {   // 微小的透明：衣服下擺和胸口一點點透出底下
        float hemT_ = smoothstep(hem_end_v - ta_hem_h, hem_end_v, UV.y);
        vec2 pr_ = TEXTURE_PIXEL_SIZE;
        float ch1_ = 1.0 - smoothstep(0.35, 1.0, length((UV - chest1_c - chest1_off) / pr_ / chest_r));
        float ch2_ = 1.0 - smoothstep(0.35, 1.0, length((UV - chest2_c - chest2_off) / pr_ / chest_r));
        float pf_ = 1.0 - smoothstep(0.3, 1.0, length((UV - ta_pc) / pr_ / max(ta_pr, vec2(1.0))));   // 內褲所在的位置：衣服大概 88% 不透明，往外擴開自然淡掉
        c.a *= 1.0 - clamp(hemT_ * ta_hem + max(ch1_, ch2_) * ta_chest + pf_ * ta_pa + see_, 0.0, 0.9);
    }
    if (lg_on > 0.5) {
        vec2 pj_ = UV * cl_size;
        vec2 lq_ = (lg_x * pj_.x + lg_y * pj_.y + lg_o) / vec2(2000.0, 3200.0);
        if (lq_.x >= 0.0 && lq_.x <= 1.0 && lq_.y >= 0.0 && lq_.y <= 1.0) c.a *= 1.0 - texture(leg_tex, lq_).a;
    }
    if (jc_on > 0.5) {
        vec2 pj3_ = UV * cl_size;
        vec2 jq3_ = (jc_x * pj3_.x + jc_y * pj3_.y + jc_o) / vec2(2000.0, 3200.0);
        if (jq3_.x >= 0.0 && jq3_.x <= 1.0 && jq3_.y >= 0.0 && jq3_.y <= 1.0) c.a *= 1.0 - smoothstep(0.38, 0.52, texture(jc_tex, jq3_).a) * smoothstep(jc_from_y + jc_arch * (1.0 - sqrt(max(0.0, 1.0 - pow(clamp(abs(pj3_.x - 985.0) / 130.0, 0.0, 1.0), 2.0)))) * step(1.0, jc_from_y), jc_from_y + jc_arch * (1.0 - sqrt(max(0.0, 1.0 - pow(clamp(abs(pj3_.x - 985.0) / 130.0, 0.0, 1.0), 2.0)))) * step(1.0, jc_from_y) + 2.0, pj3_.y);
    }
    COLOR = c * vc;
}
";

    static readonly string[] _clothCopyParams =
    {
        "expand_amount", "lift_top_v", "lift_bottom_v", "lift_neck_v", "lift_navel_v",
    };

    void SetupClothShader()
    {
        if (!Layers.TryGetValue("衣服", out var cloth) || cloth == null) return;
        _clothMat = new ShaderMaterial { Shader = new Shader { Code = ClothShaderCode } };
        // 胸部中心用胸1／胸2 的軸心（BreastPivotOverrides，畫布 px → UV）
        if (BreastPivotOverrides.TryGetValue("胸1", out var p1)) _clothMat.SetShaderParameter("chest1_c", new Vector2(p1.X / 2000f, p1.Y / 3200f));
        if (BreastPivotOverrides.TryGetValue("胸2", out var p2)) _clothMat.SetShaderParameter("chest2_c", new Vector2(p2.X / 2000f, p2.Y / 3200f));
        cloth.Material = _clothMat;
        ClothAutoBounds_();

        // 內褲：自己的 shader——只在 B（小穴）附近變形，有往下位移＋一點歪斜（旋轉感），不只是左右放大縮小
        if (Layers.TryGetValue("內褲", out var panty) && panty != null)
        {
            _pantyMat = new ShaderMaterial { Shader = new Shader { Code = PantyShaderCode } };
            _pantyMat.SetShaderParameter("center_u", 0.4865f);
            _pantyMat.SetShaderParameter("center_v", 1497f / 3200f);
            _pantyMat.SetShaderParameter("expand_amount", 0f);
            panty.Material = _pantyMat;
        }
    }

    const string PantyShaderCode = @"
shader_type canvas_item;
uniform float expand_amount = 0.0;
uniform float center_u = 0.4865;
uniform float center_v = 0.468;
uniform float half_width_u = 0.07;
uniform float half_height_v = 0.035;
uniform float shift_k = 0.03;
uniform float twist_k = 0.25;
uniform float top_v = 0.0;      // 內褲內容上緣（V）
uniform float mid_v = 0.5;      // 內褲內容中線（V）
uniform float bot_v = 1.0;      // 內褲內容下緣（V）
uniform float widen = 0.0;      // 左右變寬量
uniform float wob = 0.0;        // 輪廓不規則量
uniform float cx_u = 0.4865;    // 內褲中心（U）
uniform float pull_s = 0.0;     // 上半部被往下拉的量（V）
uniform float down_y = 0.0;     // 整件往下移的量（V）
uniform float wet_core = 0.5;    // 濕痕中心保持滿強度的範圍（越大越集中、邊緣越硬）
uniform float wet_k = 0.0;       // 濕痕強度 0~1
uniform vec2 wet_c = vec2(0.5, 0.5);   // 濕痕中心（內褲貼圖 UV）
uniform vec2 wet_r = vec2(0.08, 0.06); // 濕痕半徑（UV）
uniform vec3 wet_col = vec3(0.7, 0.74, 0.88);   // 色彩增值的顏色
uniform float aside_x = 0.0;     // 拉開：下緣被往右拉的量（U）
uniform float aside_s = 0.5;     // 拉開：往左退的範圍從這裡開始（U；內褲靠右的那一部分）
uniform float aside_b = 0.5;     // 拉開：B 的位置（U）
uniform float aside_bw = 0.02;   // 最底下這一小條的高度（V）
uniform float aside_pb = 1.0;    // 按在 50% 左右拉時，最底下向左補償的加重倍率
uniform float aside_cr = 900.0;  // 右下角褶皺最遠長多遠（內褲圖 px）
uniform vec2 aside_ao = vec2(0.25, 0.25);   // 月牙形外圓中心（相對 B 位置；單位＝內褲高度；x 往右、y 往上）
uniform vec2 aside_bo = vec2(-0.35, 0.0);    // 內圓中心相對外圓中心的位移（單位＝內褲高度）
uniform float aside_ra = 0.8;                // 外圓半徑（單位＝內褲高度）
uniform float aside_rb = 0.7;                // 內圓半徑（單位＝內褲高度）
uniform float pa_k = 1.0;        // 內褲底部相對上面的不透明度倍率（0.88/0.98）
uniform float aside_oth = 55.0;              // 花灑出發點在內圓邊上的角度（度；0＝正右、90＝正下）
uniform float aside_axd = 160.0;             // 花灑噴出去的方向（度；0＝正右、90＝正上、180＝正左）
uniform float aside_hw = 40.0;               // 花灑扇形的半角（度）
uniform float aside_cstep = 0.12;            // 相鄰兩條線的夾角（弧度）
uniform float aside_pa = 0.0;                // 目前往右拉開的量（U）
uniform float aside_tb = 1.5;                // 最底下那一條額外多移多少倍
uniform float aside_ta = 0.0;                // 月牙形整塊向左平移的量（U）
uniform float aside_dbg = 0.0;               // 1＝畫出輔助線
uniform float aside_fz = 1.0;     // 拉開：這條線（U）右邊完全不動
uniform float aside_tc = 0.45;    // 拉開：高過這個位置（離內褲下緣的比例）就完全不受拉開影響
uniform float aside_fr = 1.0;     // 往左退跟著「離按下位置越遠越弱」的比例（跟往右拉一致，才不會互相抵不掉）
uniform float aside_pu = 0.5;     // 拉開：按下的位置（U）
uniform float aside_nu = 0.1;     // 拉開：按下位置右邊這段（U）內全力跟著動
uniform float aside_fw = 0.5;     // 拉開：之後逐漸減弱的寬度（U）
uniform float aside_fk = 0.15;    // 拉開：很遠的地方保留的比例
uniform float aside_cv = 0.35;   // 褶皺弧度（越大越彎）
uniform float aside_body = 0.6;  // 非最底下的位置往左退的力道倍率
uniform float aside_ca = 0.14;   // 褶皺：相鄰兩條線的夾角（弧度）
uniform float aside_ck = 0.7;    // 右上褶皺：濃度
uniform float aside_mk = 0.5;    // 20~40% 位置補的力（相對往左退的力道）
uniform float aside_rp = 2.0;    // 往左退的「越下面越強」曲線指數
uniform float aside_bk = 3.0;    // 最底下這一小條往左退的倍率
uniform float aside_revb = 0.0;  // 最底下那一條用的往左退量（上限比較寬）
uniform float aside_rev = 0.0;   // 拉開：拉過 B 之後，B 右邊往左退的量（U）
uniform float aside_r = 1.0;     // 拉開：內褲右邊緣（U）
uniform float aside_rf = 0.12;   // 拉開：右邊緣往內多寬的範圍逐漸不動（U）
uniform float aside_h = 0.1;     // 拉開：受影響的高度（V）
uniform float fix_w = 1.0;      // 下面那一截跟著動的比例（0＝不動，1＝全動）
uniform float fix_cut = -1.0;   // >=0：這條線（V）以下的內褲不動
uniform float alpha_k = 1.0;    // 淡出
varying vec4 vc;
void vertex() { vc = COLOR; }
void fragment() {
    float down_y_ = down_y, pull_s_ = pull_s;
    if (fix_cut >= 0.0) { float fm_ = smoothstep(fix_cut - 0.012, fix_cut + 0.012, UV.y); float kk_ = mix(1.0, fix_w, fm_); down_y_ *= kk_; pull_s_ *= kk_; }   // 不是從 B 拉：下面一截一開始幾乎不動（fix_w 小），開始縮時才跟上
    float py_ = UV.y - down_y_;                 // 整件往下移
    float nz_ = fract(sin(floor(UV.x * 90.0) * 12.9898) * 43758.5453) - 0.5;
    float nz2_ = fract(sin(floor(UV.y * 140.0) * 78.233) * 43758.5453) - 0.5;
    float vis_ = 1.0, qy_ = py_;
    if (pull_s_ > 0.0) {                         // 整件內褲被往下拉：上緣往下、整體被壓縮（到下緣不動），上緣有不規則起伏
        float H_ = max(0.0001, bot_v - top_v);
        vis_ = smoothstep(top_v + pull_s_ + nz_ * wob * 2.0 - 0.0006, top_v + pull_s_ + nz_ * wob * 2.0 + 0.0006, py_);
        if (py_ < bot_v) qy_ = top_v + (py_ - top_v - pull_s_) * H_ / max(0.0001, H_ - pull_s_);
        qy_ += nz_ * wob * 0.5;
    }
    float dx = UV.x - center_u;
    float dy = qy_ - center_v;
    float xs_ = 1.0 + widen;
    float hx_ = (UV.x - cx_u) / xs_ + cx_u - UV.x + nz2_ * wob * 1.5;   // 左右先拉寬（除以放大倍率取樣）＋邊緣不規則
    float w = (1.0 - smoothstep(0.0, half_width_u, abs(dx))) * (1.0 - smoothstep(0.0, half_height_v, abs(dy)));
    float e = expand_amount * w;
    vec2 p = UV;
    p.x = center_u + dx * (1.0 - e) + dy * twist_k * e * 0.5 + hx_;   // 左右撐開＋一點歪斜＋被拉時的變寬／不規則
    float hz_ = clamp((bot_v - py_) / max(0.0001, bot_v - top_v), 0.0, 1.0);   // 離內褲下緣的距離（0＝最下面，1＝最上面）
    float aw_ = (1.0 - smoothstep(aside_tc, aside_tc + 0.17, hz_)) * pow(smoothstep(bot_v - aside_h, bot_v, py_), 1.6) * (1.0 - smoothstep(aside_r - aside_rf, aside_r, UV.x));   // 只有下面那一截（越上面越小），右邊邊不跟著大幅變形
    float wx_ = mix(aside_fk, 1.0, 1.0 - smoothstep(aside_pu + aside_nu, aside_pu + aside_nu + max(aside_fw, 0.02), UV.x));   // 離按下的位置超過一段距離，影響逐漸變小（不要整條內褲一起被拉走）
    vec2 tpx_ = 1.0 / TEXTURE_PIXEL_SIZE;
    vec2 up_ = UV * tpx_;
    float Hpx_ = max((bot_v - top_v) * tpx_.y, 1.0);
    vec2 cA_ = vec2(aside_fz * tpx_.x + aside_ao.x * Hpx_, bot_v * tpx_.y - aside_ao.y * Hpx_);   // 外圓中心
    vec2 cB_ = cA_ + aside_bo * Hpx_;                                                                  // 內圓中心（往左挖掉）
    float sf_ = max(0.015 * Hpx_, 1.0);
    float inA_ = 1.0 - smoothstep(aside_ra * Hpx_ - sf_, aside_ra * Hpx_, length(up_ - cA_));
    float inB_ = 1.0 - smoothstep(aside_rb * Hpx_ - sf_, aside_rb * Hpx_, length(up_ - cB_));
    float mask_ = inA_ * (1.0 - inB_);                    // 月牙形：外圓扣掉內圓。這一塊不變形、只平移
    float fz_ = 1.0 - mask_;
    p.x -= aside_x * aw_ * wx_ * fz_;
    float rb_ = smoothstep(aside_s, aside_s + 0.08, UV.x) * (1.0 - smoothstep(aside_r - aside_rf, aside_r, UV.x));
    float bw_ = pow(smoothstep(bot_v - aside_bw * 1.6, bot_v, py_), 1.5);   // 最底下那一小條
    float cm_ = pow(1.0 - smoothstep(0.0, 0.6, hz_), aside_rp);   // 底 20% 和 20~60% 合成同一條平滑曲線：最下面最大，往上漸漸變小，到 60% 沒有
    p.x -= mask_ * aside_ta * (pow(1.0 - hz_, 2.0) + aside_tb * bw_);   // 月牙形裡面：不變形，只有微量往右平移（越下面越多，最底下再加強）
    p.x += rb_ * fz_ * mix(1.0, wx_, aside_fr) * (aside_rev * aside_body * (cm_ + aside_mk * cm_) + aside_revb * aside_bk * aside_pb * bw_);   // 最底下那一小條再額外加強
    p.y = qy_ - shift_k * e;                                    // 往下位移
    COLOR = texture(TEXTURE, p) * vc;
    if (aside_pa > 0.0005 && COLOR.a > 0.01) {   // 褶皺：花灑（蓮蓬頭）——從月牙和內褲交界的一個點，往內褲裡面呈扇形噴開，細線，不碰到內褲上面
        float rbH_ = aside_rb * Hpx_;
        float th_ = radians(aside_oth);
        vec2 org_ = cB_ + rbH_ * vec2(cos(th_), sin(th_));                 // 出發點：內圓邊上的一點（oth＝角度，0＝正右，90＝正下）
        vec2 vv_ = up_ - org_;
        float rr_ = length(vv_);
        float ang_ = atan(-vv_.y, vv_.x);                                   // 數學角度（y 往上）
        float rel_ = mod(ang_ - radians(aside_axd) + 3.14159265, 6.2831853) - 3.14159265;
        float hw_ = radians(aside_hw);
        float cone_ = 1.0 - smoothstep(hw_ * 0.7, hw_, abs(rel_));          // 扇形範圍
        float aq_ = rel_ / max(aside_cstep, 0.03);
        float id_ = floor(aq_), fr_ = fract(aq_);
        float jt_ = fract(sin(id_ * 12.9898) * 43758.5453);
        float wpx_ = 0.8 + 0.9 * fract(jt_ * 7.3);                          // 線寬（px，很細）
        float ln_ = (1.0 - smoothstep(wpx_ * 0.3, wpx_, abs(fr_ - 0.5) * aside_cstep * max(rr_, 1.0))) * step(0.3, jt_) * cone_;
        float amt_ = clamp(aside_pa / 0.03, 0.0, 1.0);
        float Lm_ = min(aside_cr * amt_ * (0.45 + 0.55 * fract(jt_ * 3.7)), 0.45 * Hpx_);   // 每條長度不同，最多 45% 內褲高度
        float rg_ = smoothstep(3.0, 28.0, rr_) * (1.0 - smoothstep(0.35 * Lm_, max(Lm_, 1.0), rr_)) * (1.0 - smoothstep(0.30, 0.46, hz_));   // 內褲上面（超過 46% 高度）完全不會有
        vec3 cc_ = COLOR.rgb * vec3(0.62, 0.38, 0.44);   // 比之前深一點的暗紅
        COLOR.rgb = mix(COLOR.rgb, cc_, clamp(ln_ * rg_ * aside_ck, 0.0, 1.0));
    }
    if (wet_k > 0.001 && COLOR.a > 0.01) {   // 濕度：B 附近的內褲被「色彩增值」蓋上一塊濕痕（跟著布料一起變形）
        vec2 wd_ = (p - wet_c) / max(wet_r, vec2(0.001));
        float wn_ = fract(sin(dot(floor(p * 70.0), vec2(12.9898, 78.233))) * 43758.5453) - 0.5;
        float wm_ = 1.0 - smoothstep(wet_core, 1.0, length(wd_) + wn_ * 0.18);
        COLOR.rgb = mix(COLOR.rgb, COLOR.rgb * wet_col, clamp(wet_k * wm_, 0.0, 1.0));
    }
    if (pull_s_ > 0.0 && COLOR.a > 0.01) {   // 往下拉時的褶皺（照衣服上半部的折疊褶皺：一段一段、顏色一樣、集中在上半部）
        float Hc_ = max(0.0001, bot_v - top_v);
        float fwc_ = fwidth(UV.y);
        float crk_ = 0.0;
        for (int fk = 0; fk < 3; fk++) {
            float cxc_ = (UV.x - cx_u) * 38.0 + float(fk) * 3.7;
            float cN_ = floor(cxc_);
            float cF_ = fract(cxc_);
            float cOn_ = step(0.3, fract(sin(cN_ * 91.7 + float(fk) * 17.3) * 43758.5453));
            float cJ_ = fract(sin(cN_ * 37.1 + float(fk) * 5.9) * 43758.5453) - 0.5;
            float fb_ = top_v + pull_s_ + Hc_ * (0.14 + 0.16 * float(fk)) + cJ_ * 0.003;
            float fly_ = fb_ - abs(UV.x - cx_u) * 0.06;
            float sg_ = smoothstep(0.0, 0.25, cF_) * (1.0 - smoothstep(0.65, 0.9, cF_)) * cOn_;
            crk_ = max(crk_, (1.0 - smoothstep(fwc_ * 0.2, fwc_ * 0.9, abs(py_ - fly_))) * sg_ * clamp((pull_s_ - float(fk) * 0.004) / 0.0025, 0.0, 1.0));
        }
        COLOR.rgb = mix(COLOR.rgb, mix(COLOR.rgb * vec3(0.5, 0.1, 0.1), vec3(0.32, 0.02, 0.04), 0.7), clamp(crk_, 0.0, 1.0) * 0.9);
    }
    {   // 內褲底部（B 到會被拖動的那一段）比上面更透明一點：往下漸層
        float ph_ = max(0.15 * (bot_v - top_v), 0.001);
        float wb_ = smoothstep(wet_c.y - ph_, wet_c.y + ph_ * 0.3, UV.y);
        COLOR.a *= mix(1.0, pa_k, wb_);
    }
    COLOR.a *= vis_ * alpha_k;
    if (aside_dbg > 0.5) {   // 輔助線：紅＝月牙形（不變形區）、藍＝外圓、綠＝內圓、黃點＝外圓中心、白線＝B 的位置、紫線＝最底下加強那條的上緣
        float oa_ = mask_ * 0.38;
        vec3 oc_ = vec3(1.0, 0.15, 0.15);
        float lA2_ = 1.0 - smoothstep(0.0, 2.5, abs(length(up_ - cA_) - aside_ra * Hpx_));
        float lB2_ = 1.0 - smoothstep(0.0, 2.5, abs(length(up_ - cB_) - aside_rb * Hpx_));
        float dC_ = 1.0 - smoothstep(5.0, 8.0, length(up_ - cA_));
        float lBp_ = 1.0 - smoothstep(0.0, 2.0, abs(UV.x - aside_fz) * tpx_.x);
        float lBt_ = 1.0 - smoothstep(0.0, 2.0, abs(UV.y - (bot_v - aside_bw * 1.6)) * tpx_.y);
        oc_ = mix(oc_, vec3(0.2, 0.5, 1.0), lA2_); oa_ = max(oa_, lA2_);
        oc_ = mix(oc_, vec3(0.2, 1.0, 0.3), lB2_); oa_ = max(oa_, lB2_);
        oc_ = mix(oc_, vec3(1.0, 0.95, 0.2), dC_); oa_ = max(oa_, dC_);
        oc_ = mix(oc_, vec3(1.0, 1.0, 1.0), lBp_); oa_ = max(oa_, lBp_);
        oc_ = mix(oc_, vec3(0.9, 0.2, 0.9), lBt_); oa_ = max(oa_, lBt_);
        float dO_ = 1.0 - smoothstep(4.0, 7.0, length(up_ - (cB_ + aside_rb * Hpx_ * vec2(cos(radians(aside_oth)), sin(radians(aside_oth))))));   // 橘點＝花灑出發點
        oc_ = mix(oc_, vec3(1.0, 0.55, 0.1), dO_); oa_ = max(oa_, dO_);
        COLOR.rgb = mix(COLOR.rgb, oc_, oa_);
        COLOR.a = max(COLOR.a, oa_);
    }
}
";

    float CurrentPussyExpand()
    {
        string on = _mLegOpen ? "m腿/小穴" : "腿/小穴";
        if (_mLegDragStretchMaterials.TryGetValue(on, out var m) && m != null)
        {
            var v = m.GetShaderParameter("expand_amount");
            if (v.VariantType == Variant.Type.Float) return (float)v;
        }
        return 0f;
    }

    /// <summary>每幀（在 UpdateBreathing 之後）把身體／胸部這一幀的形變同步給衣服。</summary>
    bool _clothLinePrinted;
    void UpdateClothFollow()
    {
        if (_clothMat == null) return;
        if (!Layers.TryGetValue("衣服", out var cloth) || cloth == null) return;
        if (!Layers.TryGetValue("身體", out var body) || body == null) return;

        // 0. 每個畫面像素 = 幾個畫布像素（讓位移以畫面像素計）
        {
            float wps = (ScreenToWorld(new Vector2(0f, 1f)) - ScreenToWorld(Vector2.Zero)).Length();
            float gs = Mathf.Abs(cloth.GetGlobalTransform().Scale.Y);
            _clothPxScale = 1f;   // 不再做畫面像素換算（之前換算不準）；位移單位＝畫布像素
        }

        // 1. 整體縮放跟身體一致（呼吸脹縮）
        cloth.PivotOffset = body.PivotOffset;
        cloth.Scale = body.Scale;
        foreach (var pn in ClothPartLayers)
            if (Layers.TryGetValue(pn, out var part) && part != null) { part.PivotOffset = body.PivotOffset; part.Scale = body.Scale; }

        // 2. 複製身體 shader 的上提／拉伸參數
        if (_mLegDragStretchMaterials.TryGetValue("身體", out var bodyMat) && bodyMat != null)
        {
            foreach (var n in _clothCopyParams)
            {
                var v = bodyMat.GetShaderParameter(n);
                if (v.VariantType != Variant.Type.Nil) _clothMat.SetShaderParameter(n, v);
            }
        }

        // 衣擺：吸氣時往外擴（跟頭髮尾端同一種「只吸氣才動」），摸 B 時再跟著小穴變形量多擴一點
        float pussyExp = CurrentPussyExpand();
        float inhale = Mathf.Max(0f, Mathf.Sin(_breathTime * Mathf.Pi * 2f));
        _clothMat.SetShaderParameter("hem_amount", inhale * ClothHemBreathAmount + pussyExp * ClothHemBTouchGain);
        {   // 全年齡：衣服最底下 AllAgesHemStretchFrac 往下拉長 AllAgesHemStretchPx
            bool aa_ = AllAgesLocked;
            _clothMat.SetShaderParameter("stretch_s", ClothHemEndV - Mathf.Clamp(AllAgesHemStretchFrac, 0.01f, 1f) * (ClothHemEndV - ClothTopV));
            _clothMat.SetShaderParameter("stretch_ext", (aa_ ? AllAgesHemStretchPx : R18HemStretchPx) / 3200f);   // R18 也拉長裙子底部
        }
        UpdateClothBump();
        _clothMat.SetShaderParameter("ta_hem", AllAgesLocked ? 0f : ClothHemFadeAmt);
        _clothMat.SetShaderParameter("ta_hem_h", ClothHemFadeV);
        _clothMat.SetShaderParameter("ta_chest", AllAgesLocked ? 0f : ClothChestFadeAmt);
        _clothMat.SetShaderParameter("hem_start_v", ClothHemStartV);
        _clothMat.SetShaderParameter("hem_end_v", ClothHemEndV);
        _clothMat.SetShaderParameter("hem_center_u", ClothHemCenterU);

        // 衣擺上拉（指數趨近，不過衝）
        float dtc = (float)GetProcessDeltaTime();
        for (int hi = 0; hi < 2; hi++) _hemPullA[hi] += (_hemTargetA[hi] - _hemPullA[hi]) * (1f - Mathf.Exp(-ClothHemFollow * dtc));
        float fk = 1f - Mathf.Exp(-ClothHemFollow * dtc);
        for (int ti = 0; ti < 2; ti++)
        {
            _rotA[ti] += (_rotT[ti] - _rotA[ti]) * fk;
            _stA[ti]  += (_stT[ti]  - _stA[ti])  * fk;
        }
        _clothMat.SetShaderParameter("rot_ang", ClothBodyRotate ? new Vector2(Mathf.DegToRad(_rotA[0]), Mathf.DegToRad(_rotA[1])) : Vector2.Zero);   // 衣服本體不再旋轉（旋轉的是肩帶），避免本體上緣轉出一個大「肩帶」
        _clothMat.SetShaderParameter("st_amt", new Vector2(_stA[0], _stA[1]));
        _clothMat.SetShaderParameter("top_spread_k", StrapTopSpreadK);
        _clothMat.SetShaderParameter("shrink_k", StrapBelowShrinkK);
        if (_topExtU.X > 0f) _clothMat.SetShaderParameter("top_ext", _topExtU);
        for (int oi = 0; oi < 2; oi++) { _overA[oi] += (_overT[oi] - _overA[oi]) * fk; _extraA[oi] += (_extraT[oi] - _extraA[oi]) * fk; }
        _clothMat.SetShaderParameter("over_amt", new Vector2(_overA[0], _overA[1]));
        _clothMat.SetShaderParameter("top_cx", ClothTopCx);
        if (!_clothLinePrinted)
        {
            _clothLinePrinted = true;
            float hhp = ClothHemEndV - ClothTopV, y10p = ClothTopV + hhp * ClothRotFracEff, y25p = y10p + hhp * ClothInflFracEff;
            GD.Print($"[ClothLines] 黃Y={y10p * 3200f:F1}px(原{(ClothTopV + hhp * ClothTopRotFrac) * 3200f:F1}) 橘Y={y25p * 3200f:F1}px(原{(ClothTopV + hhp * (ClothTopRotFrac + ClothTopInflFrac)) * 3200f:F1}) shift=({ClothYellowShiftPx},{ClothOrangeShiftPx}) 每畫面px={_clothPxScale:F2}畫布px");
        }
        _clothMat.SetShaderParameter("orange_k", ClothOrangeK);
        _clothMat.SetShaderParameter("orange_r", ClothOrangeRoundU);
        _clothMat.SetShaderParameter("orange_w", ClothOrangeHalfW);
        _clothMat.SetShaderParameter("x_comp", ClothTopXComp);
        _clothMat.SetShaderParameter("rot_frac", ClothRotFracEff);
        _clothMat.SetShaderParameter("infl_frac", ClothInflFracEff);
        _clothMat.SetShaderParameter("show_guides", ClothShowGuides ? 1f : 0f);
        _clothMat.SetShaderParameter("top_v", ClothTopV);
        _clothMat.SetShaderParameter("top_h", Mathf.Max(0.01f, (ClothHemEndV - ClothTopV) * ClothTopFrac));
        _clothMat.SetShaderParameter("left_u", ClothLeftU);
        _clothMat.SetShaderParameter("right_u", ClothRightU);
        _clothMat.SetShaderParameter("pull_rshift", ClothPullRightShiftPx / 2000f);
        _clothMat.SetShaderParameter("pull_rjit", ClothPullRightJitterPx / 2000f);
        _clothMat.SetShaderParameter("pull_amt2", new Vector2(_hemPullA[0], _hemPullA[1]));
        _clothMat.SetShaderParameter("pull_xs", new Vector2(_hemPullXA[0], _hemPullXA[1]));
        _clothMat.SetShaderParameter("pull_w", ClothHemPullWidthU);
        _clothMat.SetShaderParameter("pull_band", ClothHemPullBandV);
        _clothMat.SetShaderParameter("inward", ClothHemInward);
        _clothMat.SetShaderParameter("zoom_k", _canvas != null ? Mathf.Clamp(_canvas.Transform.X.Length(), 0.05f, 4f) : 1f);
        _clothMat.SetShaderParameter("pull_lean", ClothHemLean);
        _clothMat.SetShaderParameter("roll_v", Mathf.Max(0.002f, ClothHemRollV));
        _clothMat.SetShaderParameter("roll_str", ClothHemRollStrength);

        // 內褲：中心撐開量跟小穴同步
        if (_pantyMat != null)
        {
            _pantyMat.SetShaderParameter("expand_amount", pussyExp * ClothPantyStretchGain);
            _pantyMat.SetShaderParameter("half_width_u", ClothPantyHalfWidthU);
            _pantyMat.SetShaderParameter("half_height_v", ClothPantyHalfHeightV);
            _pantyMat.SetShaderParameter("shift_k", ClothPantyShiftK);
            _pantyMat.SetShaderParameter("twist_k", ClothPantyTwistK);
            PantyPullApply();
        }

        // 3. 胸部區塊：胸相對身體的位移（畫布 px → UV）與縮放
        Vector2 bodyDelta = Vector2.Zero;
        if (_layerBasePos.TryGetValue("身體", out var bodyBase)) bodyDelta = body.Position - bodyBase;
        ClothApplyChest("胸1", "chest1", bodyDelta);
        ClothApplyChest("胸2", "chest2", bodyDelta);
        _clothMat.SetShaderParameter("chest_r", new Vector2(Mathf.Max(1f, ClothChestRadiusX), Mathf.Max(1f, ClothChestRadiusY)));
        _clothMat.SetShaderParameter("chest_inner", Mathf.Clamp(ClothChestInner, 0f, 0.95f));
    }

    void ClothApplyChest(string layer, string prefix, Vector2 bodyDelta)
    {
        Vector2 off = Vector2.Zero; float s = 1f;
        if (Layers.TryGetValue(layer, out var c) && c != null && _layerBasePos.TryGetValue(layer, out var bp))
        {
            Vector2 rel = (c.Position - bp) - bodyDelta;
            // 胸圖層的 Scale 是以 body 為基準的額外縮放；整體身體縮放已由 cloth.Scale 處理
            off = rel / new Vector2(2000f, 3200f) * ClothChestFollow;
            s = 1f + (c.Scale.X - 1f) * ClothChestFollow;
        }
        _clothMat.SetShaderParameter(prefix + "_off", off);
        _clothMat.SetShaderParameter(prefix + "_s", s);
    }

    /// <summary>跟 shader 一致：x 位置（U）的衣擺上拉量。兩個拉點之間平滑內插，之外往外衰減。</summary>
    float HemProfile(float ux)
    {
        float x0 = _hemPullXA[0], x1 = _hemPullXA[1], a0 = _hemPullA[0], a1 = _hemPullA[1];
        if (x0 > x1) { (x0, x1) = (x1, x0); (a0, a1) = (a1, a0); }
        float t = Mathf.Clamp((ux - x0) / Mathf.Max(0.0001f, x1 - x0 + 0.0001f), 0f, 1f); t = t * t * (3f - 2f * t);
        float dxo = Mathf.Max(Mathf.Max(x0 - ux, ux - x1), 0f);
        return Mathf.Lerp(a0, a1, t) * Mathf.Exp(-Mathf.Pow(dxo / Mathf.Max(0.001f, ClothHemPullWidthU), 4f));
    }

    static float SStep(float a, float b, float x) { float t = Mathf.Clamp((x - a) / Mathf.Max(1e-5f, b - a), 0f, 1f); return t * t * (3f - 2f * t); }

    /// <summary>跟 shader 一致：把畫面上的點（UV）反推回「衣服原圖」上的位置（先反推下拉壓縮、再反推旋轉），用來判斷抓到哪一段。</summary>
    Vector2 ClothTopInv(Vector2 uv)
    {
        float hh = ClothHemEndV - ClothTopV;
        float y10 = ClothTopV + hh * ClothRotFracEff, y25 = y10 + hh * ClothInflFracEff, tb = ClothTopV + hh * ClothTopFrac;
        float zone = Mathf.Max(0.001f, tb - ClothTopV), cx = ClothTopCx;
        float sdp = Mathf.Lerp(_stA[0], _stA[1], SStep(cx - 0.06f, cx + 0.06f, uv.X));
        float dEff = Mathf.Min(sdp, zone * 0.93f);
        Vector2 P = uv;
        if (uv.Y < tb)
        {   // 與 shader 一致：黃線以上只平移不縮放，黃線~綠線之間才壓縮
            float cT = zone / Mathf.Max(0.001f, zone - dEff);
            float p10 = tb - (tb - y10) * cT;
            P.Y = (uv.Y < y10) ? uv.Y + (p10 - y10) : tb - (tb - uv.Y) * cT;
        }
        {   // 與 shader 一致：往下拉時上半部左右外擴
            float sdirT = 2f * (1f - SStep(cx - 0.06f, cx + 0.06f, uv.X)) - 1f;
            float spwT = 1f - SStep(y10, tb, uv.Y);
            P.X += sdirT * sdp * 3200f * StrapTopSpreadK / 2000f * spwT;
        }
        float fy = 1f - SStep(y10, Mathf.Max(y10 + 0.004f, y25 + (Mathf.Sqrt((P.X - cx) * (P.X - cx) + ClothOrangeRoundU * ClothOrangeRoundU) - ClothOrangeRoundU) * ClothOrangeK), P.Y);
        float wl = 1f - SStep(cx - 0.01f, cx + 0.01f, P.X);
        float ang = ClothBodyRotate ? Mathf.DegToRad(_rotA[1] * (1f - wl) - _rotA[0] * wl) * fy : 0f;
        Vector2 px = new Vector2(2000f, 3200f);
        Vector2 d = (P - new Vector2(cx, y10)) * px;
        float cs = Mathf.Cos(ang), sn = Mathf.Sin(ang);
        Vector2 d2 = new Vector2(d.X * cs + d.Y * sn, -d.X * sn + d.Y * cs);
        Vector2 outP = new Vector2(cx, y10) + d2 / px;
        outP.X = P.X + (outP.X - P.X) * (1f - ClothTopXComp * fy);   // 與 shader 一致的橫向補償
        return outP;
    }

    /// <summary>上半部「往下拉」可抓範圍往下多出來的量：只有真的被拉下去了才會變大（沒拉時只留一小段），避免搶走下面的操作。</summary>
    [Export] public bool ClothDebugLog = true;   // 按下衣服時在 Output 印出被哪一區接手（[ClothDbg]）

    float TopExtraV(float hh)
    {
        float zone = hh * ClothTopFrac;
        float st = Mathf.Max(_stA[0], _stA[1]);
        float k = Mathf.Clamp(st / Mathf.Max(0.001f, zone * 0.3f), 0f, 1f);
        return hh * (0.03f + (ClothTopGrabExtraFrac - 0.03f) * k);
    }

    static Vector2 lpt_(Vector2 lp, float sx, float sy) => lp / new Vector2(sx, sy);

    bool ClothHemInput(InputEvent e)
    {
        if (!Layers.TryGetValue("衣服", out var cloth) || cloth == null || !cloth.IsVisibleInTree()) { _hemDrag = false; return false; }
        float sx = Mathf.Max(cloth.Size.X, 1f), sy = Mathf.Max(cloth.Size.Y, 1f);
        // M 腿打開、按在肛門上：衣服的判定不起作用（讓給肛門的突破流程）
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } ap_ && _mLegOpen && !_hemDrag && _topDrag == 0 && IsMouseOverMLegAnus(ap_.Position))
            return false;
        // 按在臉上：肩帶／衣服的判定不起作用（臉是給捏臉／摸頭用的）
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } fp_ && !_hemDrag && _topDrag == 0 && PinchOnLayer("臉", fp_.Position))
            return false;
        // 按在「內褲上、而且這裡的衣服已經被拉開」：衣服讓路給內褲（衣服的判定是用原圖，會把已經拉開的地方也算進去）
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mp && !IsPointerOverUi() && PantyWantsPress(ScreenToWorld(mp.Position)))
        { if (ClothDebugLog) GD.Print("[ClothDbg] 這個點交給內褲（衣服已拉開）"); return false; }
        // 被子還蓋著（沒踢開）時，衣服不能拉
        if (DuvetState != 1) { _hemDrag = false; _topDrag = 0; _midDrag = false; return false; }
        // 中間區（上面可拉的區域與下面可折的區域之間）：往下拖＝把下面放回去，往上拖＝把上面放回去（復位用）
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } mm0)
        {
            if (mm0.Pressed)
            {
                if (!IsPointerOverUi() && !_tongueTool && _handMode == HandMode.None)
                {
                    Vector2 wm = ScreenToWorld(mm0.Position);
                    Vector2 lm = cloth.GetGlobalTransform().AffineInverse() * wm;
                    float vm = lm.Y / sy;
                    float topEnd = ClothTopV + (ClothHemEndV - ClothTopV) * ClothTopFrac;
                    {   // 下擺已經被拉起來：抓在被拉起的下擺邊緣～原下緣之間，一律優先當成下擺（拉到最上面也拉得下來，不會被上半部搶走）
                        float uxh = lm.X / sx, liftH = HemProfile(uxh);
                        if (liftH > 0.01f)
                        {
                            float edgeH = ClothHemEndV - liftH;
                            if (vm >= edgeH - ClothHemGrabBandV && vm <= edgeH + ClothHemGrabBandV * 0.5f && IsPointOnLayer("衣服", wm)
                                && !IsPointOnLayer("m腿/腿1", wm) && !IsPointOnLayer("m腿/腿2", wm) && !IsPointOnLayer("m腿/小穴", wm) && !IsPointOnLayer("m腿/肛門", wm))
                            {
                                int slotH = Mathf.Abs(uxh - _hemPullXA[0]) <= Mathf.Abs(uxh - _hemPullXA[1]) ? 0 : 1;
                                if (ClothDebugLog) GD.Print($"[ClothDbg] 按下→已拉起的下擺（優先） v={vm:F3} 邊緣={edgeH:F3}");
                                _hemDrag = true; _hemStartLocalY = lm.Y; _hemSlot = slotH; _hemStartTarget = _hemTargetA[slotH];
                                return true;
                            }
                        }
                    }
                    float srcY_ = ClothTopInv(lpt_(lm, sx, sy)).Y;   // 用「沒被拉動前」的位置判斷，才不會跟上半部（肩帶／下拉）搶
                    float midTop_ = topEnd + TopExtraV(ClothHemEndV - ClothTopV);
                    float midBot_ = ClothHemEndV - Mathf.Max(ClothHemPullBandV, ClothHemGrabBandV);   // 跟下擺抓取區不重疊
                    if (srcY_ > midTop_ && vm < midBot_ && StrapHitSide(lm) < 0 && IsPointOnLayer("衣服", wm))
                    {
                        if (ClothDebugLog) GD.Print($"[ClothDbg] 按下→中間區（往下=放回下面，往上=放回上面） v={vm:F3} 上半結束={topEnd:F3} 下擺抓取起點={midBot_:F3} 下擺終點={ClothHemEndV:F3}");
                        _midDrag = true; _midStartY = lm.Y; _midRotS[0] = _rotT[0]; _midRotS[1] = _rotT[1]; _midStS[0] = _stT[0]; _midStS[1] = _stT[1]; _midHemS0 = _hemTargetA[0]; _midHemS1 = _hemTargetA[1];
                        return true;
                    }
                }
            }
            else if (_midDrag) { _midDrag = false; return true; }
        }
        else if (e is InputEventMouseMotion mm1 && _midDrag)
        {
            float dy = (cloth.GetGlobalTransform().AffineInverse() * ScreenToWorld(mm1.Position)).Y - _midStartY;
            if (dy >= 0f)
            {
                _hemTargetA[0] = Mathf.Max(0f, _midHemS0 - dy / sy); _hemTargetA[1] = Mathf.Max(0f, _midHemS1 - dy / sy);
                for (int q = 0; q < 2; q++) { _rotT[q] = _midRotS[q]; _stT[q] = _midStS[q]; }
            }
            else
            {
                float k = 1f - Mathf.Clamp(-dy / sy / 0.15f, 0f, 1f);
                for (int q = 0; q < 2; q++) { _rotT[q] = _midRotS[q] * k; _stT[q] = _midStS[q] * k; }
                _hemTargetA[0] = _midHemS0; _hemTargetA[1] = _midHemS1;
            }
            return true;
        }
        if (_midDrag) return true;
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } mbt)
        {
            if (mbt.Pressed)
            {
                if (IsPointerOverUi() || _tongueTool || _handMode != HandMode.None) return false;
                Vector2 wt = ScreenToWorld(mbt.Position);
                Vector2 lpt = cloth.GetGlobalTransform().AffineInverse() * wt;
                Vector2 uv = lpt / new Vector2(sx, sy);
                int hs = StrapHitSide(lpt);
                if (hs >= 0)
                {   // 抓住已經被拉開的肩帶：可以往回拉（往內拖）
                    if (ClothDebugLog) GD.Print("[ClothDbg] 按下→肩帶（回拉）");
                    _topDrag = 1 + hs; _topStartVal = _rotT[hs];
                    _rotTriggered = true; _rotTrigX = lpt.X; _topStartA = 0f; _strapY0 = lpt.Y; _strapSt0 = _stT[hs] + _overT[hs] + _extraT[hs];
                    return true;
                }
                Vector2 src = ClothTopInv(uv);
                float hh = ClothHemEndV - ClothTopV;
                float y10 = ClothTopV + hh * ClothRotFracEff, tb = ClothTopV + hh * ClothTopFrac;
                float y10Hit = y10;
                bool inX = src.X > ClothLeftU - 0.04f && src.X < ClothRightU + 0.04f;
                int side = src.X < ClothTopCx ? 0 : 1;
                if (inX && src.Y > ClothTopV - 0.03f && src.Y < y10Hit)
                {   // 旋轉段：左半逆時針、右半順時針（兩邊往下垂）
                    if (ClothDebugLog) GD.Print($"[ClothDbg] 按下→上半部旋轉段 src.Y={src.Y:F3} 結束={y10Hit:F3}");
                    _topDrag = 1 + side; _topStartVal = _rotT[side];
                    _topStartA = Mathf.Atan2(lpt.Y - y10 * sy, Mathf.Abs(lpt.X - ClothTopCx * sx));
                    _rotTriggered = false; _rotTrigX = lpt.X;   // 左右移動超過 StrapRotTriggerPx 才開始旋轉
                    _strapY0 = lpt.Y; _strapSt0 = _stT[side] + _overT[side] + _extraT[side];
                    return true;
                }
                if (inX && src.Y >= y10Hit && src.Y < tb + TopExtraV(hh) && IsPointOnLayer("衣服", wt))
                {   // 往下拉（壓縮）：兩邊都轉到解鎖角度以上才可以
                    if (_rotT[side] < StrapUnlockDeg) return false;   // 這一邊的肩帶拉過解鎖角度，這一邊就能往下拉（左右各自）
                    if (ClothDebugLog) GD.Print($"[ClothDbg] 按下→上半部下拉段 src.Y={src.Y:F3} 範圍={y10Hit:F3}~{tb + TopExtraV(hh):F3}");
                    _topDrag = 3 + side; _topStartVal = _stT[side]; _topStartLY = lpt.Y;
                    return true;
                }
            }
            else if (_topDrag != 0) { _topDrag = 0; return true; }
        }
        else if (e is InputEventMouseMotion mt && _topDrag != 0)
        {
            Vector2 lpt = cloth.GetGlobalTransform().AffineInverse() * ScreenToWorld(mt.Position);
            float hh = ClothHemEndV - ClothTopV;
            if (_topDrag <= 2)
            {
                int sd = _topDrag - 1;
                float y10 = ClothTopV + hh * ClothRotFracEff;
                float a = Mathf.Atan2(lpt.Y - y10 * sy, Mathf.Abs(lpt.X - ClothTopCx * sx));
                if (!_rotTriggered && _rotT[sd] >= StrapUnlockDeg)
                { _rotTriggered = true; _rotTrigX = lpt.X; _strapY0 = lpt.Y; _strapSt0 = _stT[sd] + _overT[sd] + _extraT[sd]; }   // 肩帶已拉到底：不用先左右動，直接往下拖就能拉衣服
                if (!_rotTriggered)
                {
                    if (Mathf.Abs(lpt.X - _rotTrigX) < StrapRotTriggerPx) return true;   // 還沒左右移動夠遠：不旋轉
                    _rotTriggered = true; _topStartA = a; _rotTrigX = lpt.X;   // 從觸發當下開始算，不會跳一下
                }
                // 已經在拉衣服（這一邊往下拉了）時，肩帶角度鎖住，只看上下；否則左右拖＝旋轉
                bool stretching = _rotT[sd] >= StrapUnlockDeg && _stT[sd] > 0.0005f;
                if (!stretching)
                {   // 滑鼠往外（左帶往左、右帶往右）拖 StrapDragPx＝轉滿；肩帶位置跟著滑鼠走
                    float outDx = (sd == 0 ? -1f : 1f) * (lpt.X - _rotTrigX);
                    _rotT[sd] = Mathf.Clamp(_topStartVal + outDx / Mathf.Max(1f, StrapDragPx) * ClothTopRotMax, 0f, ClothTopRotMax);
                }
                // 兩邊肩帶都拉到底之後，繼續往下拖：衣服上半部跟著往下（往回拖到 0 就停；沒拉到底時衣服回原位）
                if (_rotT[sd] >= StrapUnlockDeg)
                {
                    float zoneS = (ClothHemEndV - ClothTopV) * ClothTopFrac;
                    float capS = zoneS * ClothTopStretchMaxFrac + StrapExtraDownPx / 3200f;
                    float rawS = Mathf.Min(Mathf.Max(0f, _strapSt0 + (lpt.Y - _strapY0) / sy), StrapDressMaxV(sd));   // 肩帶到底了，衣服就不再往下
                    _stT[sd] = Mathf.Min(rawS, capS);
                    _overT[sd] = Mathf.Clamp(rawS - capS, 0f, StrapOverMaxPx / 3200f);
                    _extraT[sd] = Mathf.Max(0f, rawS - capS - StrapOverMaxPx / 3200f);   // 衣服已經停了，肩帶還能繼續往下的量   // 拉到上限後繼續拉：中間出現往下移動的折疊褶皺
                }
                else { _strapY0 = lpt.Y; _strapSt0 = _stT[sd] + _overT[sd] + _extraT[sd]; }
            }
            else
            {
                int sd = _topDrag - 3;
                float zone = hh * ClothTopFrac;
                float capB = zone * ClothTopStretchMaxFrac + StrapExtraDownPx / 3200f;
                float rawB = Mathf.Min(Mathf.Max(0f, _topStartVal + _overT[sd] + _extraT[sd] + (lpt.Y - _topStartLY) / sy), StrapDressMaxV(sd));   // 只能往下；肩帶到底了，衣服就不再往下
                _stT[sd] = Mathf.Min(rawB, capB);
                _overT[sd] = Mathf.Clamp(rawB - capB, 0f, StrapOverMaxPx / 3200f);
                _extraT[sd] = Mathf.Max(0f, rawB - capB - StrapOverMaxPx / 3200f);
            }
            return true;
        }
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
        {
            if (mb.Pressed)
            {
                if (IsPointerOverUi() || _tongueTool || _handMode != HandMode.None) return false;
                Vector2 w = ScreenToWorld(mb.Position);
                if (!IsPointOnLayer("衣服", w)) return false;
                // M 腿現在畫在衣服上面：點到腿就不當成抓衣擺
                if (IsPointOnLayer("m腿/腿1", w) || IsPointOnLayer("m腿/腿2", w) || IsPointOnLayer("m腿/小穴", w) || IsPointOnLayer("m腿/肛門", w)) return false;
                Vector2 lp = cloth.GetGlobalTransform().AffineInverse() * w;
                float v = lp.Y / sy;
                float ux = lp.X / sx;
                float gw = Mathf.Max(0.001f, ClothHemPullWidthU);
                int slot = ux < ClothHemCenterU ? 0 : 1;
                {   // 已經拉起來時，選對這個位置影響最大的那一邊（才拉得回去）
                    if (HemProfile(ux) > 0.01f) slot = Mathf.Abs(ux - _hemPullXA[0]) <= Mathf.Abs(ux - _hemPullXA[1]) ? 0 : 1;
                }
                float lifted = HemProfile(ux);
                float edge = ClothHemEndV - lifted;
                float bandTop = lifted > 0.01f ? Mathf.Min(ClothHemEndV - ClothHemPullBandV, edge - ClothHemGrabBandV) : (edge - ClothHemGrabBandV);   // 已經拉起來時，整段被壓縮的範圍都能抓，方便拉回去
                if (v < bandTop || v > edge + 0.01f) return false;
                if (ClothDebugLog) GD.Print($"[ClothDbg] 按下→下擺拉起 v={v:F3} 抓取起點={bandTop:F3} 邊緣={edge:F3}");
                _hemDrag = true; _hemStartLocalY = lp.Y; _hemSlot = slot; _hemStartTarget = _hemTargetA[slot];
                if (_hemPullA[slot] < 0.01f) _hemPullXA[slot] = ux;
                return true;
            }
            if (_hemDrag)
            {
                _hemDrag = false;
                _hemTargetA[_hemSlot] *= (1f - ClothHemRecover);
                return true;
            }
        }
        else if (e is InputEventMouseMotion mm && _hemDrag)
        {
            Vector2 lp = cloth.GetGlobalTransform().AffineInverse() * ScreenToWorld(mm.Position);
            _hemTargetA[_hemSlot] = Mathf.Clamp(_hemStartTarget + (_hemStartLocalY - lp.Y) / sy, 0f, ClothHemPullMax);
            return true;
        }
        return false;
    }

    [Export] public bool ClothAutoBounds = true;   // 開機時從衣服貼圖的不透明範圍自動算上緣／左右緣

    void ClothAutoBounds_()
    {
        if (!ClothAutoBounds || !Layers.TryGetValue("衣服", out var cl) || cl is not TextureRect tr || tr.Texture == null) return;
        var img = tr.Texture.GetImage();
        if (img == null) return;
        if (img.IsCompressed()) img.Decompress();
        img.Convert(Image.Format.Rgba8);
        int w = img.GetWidth(), h = img.GetHeight();
        var d = img.GetData();
        int minX = w, maxX = -1, minY = h;
        for (int y = 0; y < h; y++)
        {
            int row = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                if (d[row + x * 4 + 3] > 25)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                }
            }
        }
        if (maxX < 0) return;
        ClothTopV = (float)minY / h; ClothLeftU = (float)minX / w; ClothRightU = (maxX + 1f) / w;
        GD.Print($"[Cloth] 自動範圍 top={ClothTopV:F3} left={ClothLeftU:F3} right={ClothRightU:F3}");
    }

    // ── 肚子／插入出現時，衣服在那個位置跟著凸起、隨它移動 ──
    [Export(PropertyHint.Range, "0,0.8,0.01")] public float ClothBumpBelly  = 0.30f;   // 肚子的凸起強度（0＝不凸）
    [Export(PropertyHint.Range, "0,0.8,0.01")] public float ClothBumpInsert = 0.20f;   // 插入的凸起強度
    [Export(PropertyHint.Range, "0,0.5,0.01")] public float ClothBumpSeeThrough = 0.12f;   // 肚子／插入位置的衣服多透出幾成（0.12＝保留 88% 不透明）
    [Export(PropertyHint.Range, "0,1,0.05")] public float ClothBumpFollow = 0.3f;   // 凸起跟著肚子／插入抽插移動的比例（越小衣服動得越不明顯）
    [Export(PropertyHint.Range, "0.2,1.5,0.05")] public float ClothBumpRadius = 0.6f;   // 凸起範圍＝圖層內容半寬高 × 這個比例

    // ── 插入：向右擴張（舊的「用衣服裁切插入／雞雞／血」已移除，改成衣服直接畫在最上面）──
    [Export(PropertyHint.Range, "0,5,0.05")] public float ClothBumpSpeedGain = 1.5f;   // 抽插速度最大時，衣服凸起／晃動額外放大倍數
    [Export] public bool PlayTimeLimitOn = true;         // 遊玩時間限制
    [Export(PropertyHint.Range, "10,3600,1")] public float PlayTimeLimitSec = 300f;   // 5 分鐘
    float _playTime; bool _playTimeUp;
    [Export(PropertyHint.Range, "0,60,0.5")] public float InsertWidenBottomPx = 4f;   // 插入物件只有底部額外向右擴張幾 px，讓右下角伸到雞雞的右緣貼齊（雞雞右緣 x≈1119、插入內容右緣 x≈1098）
    [Export(PropertyHint.Range, "0,30,0.5")] public float InsertWidenRightPx = 0f;   // 插入向右擴張幾 px（以內容左緣為基準拉寬）
    [Export(PropertyHint.Range, "-30,60,0.5")] public float InsertShrinkBottomLeftPx = -3f;    // 插入物件只有底部的左邊往內（右）縮幾 px，讓左下角貼齊雞雞的左緣（插入內容左緣 x≈880、雞雞接口處左緣約 900~915）
    [Export(PropertyHint.Range, "0,1.5,0.05")] public float InsertShrinkTaperFollow = 0f;   // 插入左下角跟著雞雞變細的縮進量走多少比例（0＝不跟、1＝完全跟；縮太多就調小）
    void UpdateInsertMask()
    {
        if (_insertEdgeMat == null) return;
        _insertEdgeMat.SetShaderParameter("wid_lb", InsertShrinkBottomLeftPx / 210f);
        _insertEdgeMat.SetShaderParameter("wid_s", InsertWidenRightPx / 239f);
        _insertEdgeMat.SetShaderParameter("wid_b", InsertWidenBottomPx / 239f);
    }

    [Export] public bool ClothOnTop = true;   // 關掉＝回到舊的順序（雞雞在衣服上面，靠遮罩裁切）
    bool _clothTopApplied; bool _clothTopInit;
    static readonly string[] ClothTopGroup = { "洞", "雞雞", "精液", "開上", "開1", "插入", "男人肚子" };
    static readonly string[] ClothTopParts = { "衣服/左帶", "衣服/右帶", "衣服", "m腿/內褲", "蝴蝶結2" };   // m腿/內褲：在衣服上面，但被 M 腿的輪廓裁切（見 SetMPantyLegClip）   // 蝴蝶結2 要一起搬到衣服上面，不然被衣服蓋住
    static readonly string[] LegMaskLayers = { "m腿/腿1", "m腿/腿2", "m腿/腳1", "m腿/腳2", "m腿/足1", "m腿/足2" };
    [Export] public bool ClothLegClip = true;   // 衣服在最上面時，被 M 腿（腿／腳／足）裁切
    SubViewport _legMaskVP; Node2D _legMaskRoot;
    readonly System.Collections.Generic.Dictionary<string, TextureRect> _legDup = new();

    void MoveBefore(Control node, Control anchor)
    {
        int ai = anchor.GetIndex(), li = node.GetIndex();
        _charRoot.MoveChild(node, li < ai ? ai - 1 : ai);
    }

    void ApplyClothOnTop()
    {
        if (_charRoot == null) return;
        if (_clothTopInit && _clothTopApplied == ClothOnTop) return;
        if (!Layers.TryGetValue("前髮", out var hair) || hair == null || hair.GetParent() != _charRoot) return;
        if (_duvet != null && IsInstanceValid(_duvet) && _duvet.GetParent() == _charRoot) hair = _duvet;   // 被子在衣服上面：排在被子前面
        if (ClothOnTop)
        {   // 順序：…→ M 腿 → 肛門 → 洞／雞雞／精液／開／插入 → 衣服帶／衣服（最上）→ 前髮；衣服再用 M 腿遮罩裁掉
            foreach (var n in ClothTopGroup)
                if (Layers.TryGetValue(n, out var l) && l != null && l.GetParent() == _charRoot) MoveBefore(l, hair);
            foreach (var n in ClothTopParts)
                if (Layers.TryGetValue(n, out var l) && l != null && l.GetParent() == _charRoot) MoveBefore(l, hair);
        }
        else
        {
            if (Layers.TryGetValue("m腿/腿1", out var leg1) && leg1 != null && leg1.GetParent() == _charRoot)
                foreach (var n in ClothTopParts)
                    if (Layers.TryGetValue(n, out var l) && l != null && l.GetParent() == _charRoot) MoveBefore(l, leg1);
            foreach (var n in ClothTopGroup)
                if (Layers.TryGetValue(n, out var l) && l != null && l.GetParent() == _charRoot) _charRoot.MoveChild(l, -1);
        }
        _clothTopInit = true; _clothTopApplied = ClothOnTop;
        GD.Print($"[ClothTop] 衣服在最上面={ClothOnTop}");
    }

    /// <summary>把 M 腿各層離屏渲染到 _legMaskVP（charRoot 座標，縮 0.5），給衣服 shader 當遮罩。</summary>
    void UpdateClothLegMask()
    {
        if (_clothMat == null || _charRoot == null) return;
        bool on = ClothOnTop && ClothLegClip && Layers.TryGetValue("衣服", out var cl) && cl is TextureRect;
        _clothMat.SetShaderParameter("lg_on", on ? 1f : 0f);
        if (!on) { if (_legMaskVP != null) _legMaskVP.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled; SetMPantyLegClip(false); return; }
        var cr = (TextureRect)Layers["衣服"];
        if (_legMaskVP == null)
        {
            _legMaskVP = new SubViewport { TransparentBg = true, Size = new Vector2I(1000, 1600), RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            AddChild(_legMaskVP);
            _legMaskRoot = new Node2D { Scale = new Vector2(0.5f, 0.5f) };
            _legMaskVP.AddChild(_legMaskRoot);
        }
        _legMaskVP.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        foreach (var n in LegMaskLayers)
        {
            if (!Layers.TryGetValue(n, out var src) || src is not TextureRect st) continue;
            if (!_legDup.TryGetValue(n, out var d) || !IsInstanceValid(d))
            {
                d = new TextureRect { MouseFilter = Control.MouseFilterEnum.Ignore };
                _legMaskRoot.AddChild(d); _legDup[n] = d;
            }
            d.ExpandMode = st.ExpandMode; d.StretchMode = st.StretchMode;
            d.Texture = st.Texture; d.Size = st.Size; d.Position = st.Position;
            d.PivotOffset = st.PivotOffset; d.Scale = st.Scale; d.Rotation = st.Rotation;
            d.Material = st.Material; d.Modulate = st.Modulate; d.SelfModulate = st.SelfModulate;
            d.Visible = st.Visible && st.Texture != null;
        }
        Transform2D T = cr.GetTransform();
        _clothMat.SetShaderParameter("leg_tex", (Texture2D)_legMaskVP.GetTexture());
        _clothMat.SetShaderParameter("lg_x", T.X);
        _clothMat.SetShaderParameter("lg_y", T.Y);
        _clothMat.SetShaderParameter("lg_o", T.Origin);
        _clothMat.SetShaderParameter("cl_size", cr.Size);
        SetMPantyLegClip(true);
    }

    // ── M 腿內褲：跟衣服一樣被 M 腿（腿／腳／足）裁切；被衣服蓋住的部分靠圖層順序（衣服在它上面）──
    ShaderMaterial _mPantyMat;
    const string MPantyShaderCode = @"shader_type canvas_item;
uniform sampler2D leg_tex : filter_linear;
uniform vec2 lg_x = vec2(1.0, 0.0);
uniform vec2 lg_y = vec2(0.0, 1.0);
uniform vec2 lg_o = vec2(0.0);
uniform vec2 cl_size = vec2(2000.0, 3200.0);
uniform float lg_on = 0.0;
// ── 撥開（MLegPantyAside.cs）：抓內褲上面一點往右拉；右邊固定不動，左邊被推到右邊，布被壓向右邊變扁 ──
uniform float aside = 0.0;                    // 左邊被推過去多遠（畫布 px），0＝沒撥開
uniform vec2 shear_v = vec2(1370.0, 1440.0);  // 上轉折帶（畫布 px）：上面不動，過了這裡被推到全量
uniform vec2 fix_v = vec2(1570.0, 1680.0);    // 下轉折帶：被推的量慢慢回到 0，最底下固定不動
uniform vec3 edge_l = vec3(839.17, 70.6, -9.12);     // 內褲左緣 x(y)＝c0 + c1*t + c2*t*t，t＝(y-1400)/100（畫布 px，實測擬合）
uniform vec3 edge_r = vec3(1125.35, -77.5, 18.26);   // 內褲右緣
uniform float fold_k = 0.0;                   // 褶皺陰影濃度（0＝不要陰影，只留線條）
uniform float fold_freq = 0.10;               // 褶皺間距（越大越密）
uniform float right_follow = 0.05;            // 右邊也會跟著往右移一點點（相對左邊被推的量）
uniform sampler2D an_tex : filter_linear;   // 肛門圖層的貼圖：撥開時，內褲只在肛門的位置挖一個洞（下半部其他地方完整保留）
uniform vec2 an_off = vec2(0.0);            // 肛門圖層相對內褲圖層的位移（畫布 px）
uniform float an_on = 0.0;
uniform vec2 leg_offr = vec2(0.0);           // 右腿相對內褲的平移（px）：呼吸／擺動時邊緣跟著腿走，不露縫
uniform vec2 leg_offl = vec2(0.0);           // 左腿相對內褲的平移（px）
uniform vec2 leg_rot = vec2(0.0);           // 右腿／左腿目前的旋轉（弧度）：內褲和腳交接的地方跟著腳的旋轉做一點變形，才貼得緊
uniform float low_str = 0.9;                // 肛門那一帶（y≳1540）的內褲位移強度倍率（1＝跟上面一樣；越小位移越少）
uniform float seam_fade = 1.0;              // 內褲中間那條黑色縫線：內褲被拉開時淡化的程度（0＝不淡化）
uniform float low_fade = 1.0;                // 下半部淡出的程度（拉開越多，下半部越淡出；0＝不淡出）
uniform float max_reach = 140.0;             // 撥開時內褲最多能離原本右緣往右多遠（畫布 px）；再遠的一律隱藏，避免拉出一條長長的殘影
uniform sampler2D ed_tex : filter_linear;   // 烘好的邊線貼圖（內褲_M_edge.png）：R＝陡的邊的黑線、G＝斜／平的邊的黑線、B＝乾淨的圓角形狀
uniform float ed_on = 0.0;
uniform vec2 jz_rise = vec2(1300.0, 1500.0);   // 銜接處（插入時額外往右）從哪個高度開始漸入→完全生效（畫布 px）
uniform vec2 jz_fall = vec2(1500.0, 1550.0);   // 從哪個高度開始漸淡→消失
uniform vec2 jf_y = vec2(1440.0, 1525.0);      // 細帶開始變透明 → 完全消失的高度（畫布 px）
uniform sampler2D ins_tex : repeat_disable;   // 插入物件貼圖：內褲細帶沿它的下緣裁切
uniform vec4 ins_xf = vec4(0.0, 0.0, 1.0, 1.0);   // 插入圖層相對內褲圖層的位置 xy、縮放 zw
uniform vec2 ins_pv = vec2(0.0);
uniform float ins_on = 0.0;
uniform float jf_curve = 90.0;                 // 裁切線的弧度（左邊比右邊低多少 px）
uniform float jf_str = 1.0;                    // 變透明的強度（0＝不變）
uniform float anus_bulge = 0.0;             // 插入肛門時肛門附近內褲往外凸的程度（0～0.5）
uniform vec2 anus_c = vec2(1003.0, 1600.0);   // 肛門中心（內褲圖座標）
uniform float anus_r = 70.0;                // 凸起範圍半徑（px）
uniform float thrust_j = 0.0;               // 抽插時只在銜接處多撐開的 px（折角不動，只有銜接處膨脹）
uniform float thrust_r = 0.0;               // 抽插時銜接處右邊多跟著往右的比例
uniform float thrust_stretch = 0.0;         // 抽插時以小穴底部為固定點，往上的部分被拉長的比例（0＝不拉）
uniform float stretch_center = 1432.0;      // 變形中心（畫布 px）：雞雞和插入銜接處
uniform float stretch_width = 90.0;
uniform float stretch_anchor = 1579.0;      // 固定點（畫布 px）：這個高度以下完全不動，往上的部分被拉長（變形集中在上面）
uniform float hook_px = 0.0;                // 插入後貼著雞雞那一圈：邊界在入口高度往右凸出一個弧形（像勾住雞雞）的量（畫布 px）
uniform vec2 arc_v = vec2(1385.0, 1685.0);  // 月牙形：推動量沿著高度走一個橢圓輪廓，從這個範圍的上端開始、中間最大、下端回到 0
uniform float arc_peak = 1528.5;          // 月牙最凸（推動量最大）的高度（畫布 px）：上面從 arc_v.x 圓滑升到這裡，下面用餘弦快速收回到 arc_v.y（左下角固定）
uniform float arc_mix = 1.0;                // 1＝完全用月牙（橢圓）輪廓；0＝用原本「平台＋兩頭 smoothstep」
uniform float ins_rot = 0.0;               // 插入時彎曲處（肘）繞 ins_pivot 旋轉的角度（弧度；負＝逆時針）
uniform vec2 ins_pivot = vec2(1020.0, 1520.0);   // 彎曲處的中心（內褲圖座標 px）
uniform float ins_left = 0.3;              // 插入後左邊再往右多推的比例（乘上 hug_k 的平滑插入狀態）
uniform float low_hide = 1.0;               // 插入時小穴底部以下那一節內褲的隱藏程度（0＝不藏）
uniform float corner_push = 1.0;            // 插入後入口附近的內褲被推到右下角的程度（0＝不推）
uniform float hug_k = 0.0;                  // 插入後：入口下面右下角那一帶的內褲再往右貼著雞雞的程度（0＝不貼；平滑漸入）
uniform float line_steep = 0.35;            // 陡的（近垂直的）邊的黑線濃度倍率：直線的黑線少一點（0＝沒有）；斜的／平的邊一律全濃度
uniform float outline_k = 1.0;               // 拉伸（壓縮）後邊緣變細，補一圈描邊的濃度（0＝不補）
uniform float left_boost = 1.25;              // 左邊往右多縮的倍率（1＝原本；越大左邊越往右）
uniform float leg_relax = 0.0;               // 撥開時右上角被大腿切進來的部分往回讓多少（U；0.005≈10px；呼吸時腳往內轉會卡進內褲）
uniform float leg_cover = 1.0;               // 撥開時右腿邊窄帶：減弱腿的裁切、用白色布蓋住膚色的程度（0＝關）
uniform vec2 seg_y = vec2(1402.0, 1418.0);     // 補斷線：起點（深紅輪廓終點）、終點（虛線上端）的高度（畫布 px）
uniform vec2 ol_y = vec2(1334.0, 1414.0);     // 蓋掉腿的粗輪廓線：起訖高度（畫布 px，內褲上緣到夾角）
uniform float ol_r = 5.0;                     // 蓋輪廓線的右邊界：離腿遮罩左緣往右多少 px（深紅線在遮罩邊緣內側偏左，所以只蓋到這裡，不蓋大腿膚色）
uniform float ol_w = 4.0;                     // 輪廓線的寬度（從腿遮罩左緣往右多少 px）
uniform float ol_k = 0.0;                     // 蓋掉輪廓線的程度（0＝不蓋）
uniform float ell_dx = 20.8;                  // 橢圓中心離內褲右緣往內多少 px（最後位置：橢圓中心畫布 x≈1100）
uniform float ell_slope = 0.627;              // 內褲右緣往上往右偏的斜率（橢圓中心跟著右緣走，所以要扣掉）
uniform float ell_cy = 1414.0;                // 橢圓中心高度（內褲來源列）
uniform float ell_rx = 14.0;                  // 橢圓半徑 x（px）
uniform float ell_ry = 41.0;                  // 橢圓半徑 y（px）
uniform float ell_k = 1.0;                    // 橢圓裡面腿被內褲切掉的程度（0＝關）
uniform float body_k = 1.0;                   // 插入時窄帶本體不被腿裁切、補成不透明的程度（0＝關，回到以前）
uniform float clean_k = 1.0;                  // 補的線左邊／下面，露出的腿線條和破圖膚色用白布蓋住的程度（0＝不蓋）
uniform float clean_dn = 30.0;                // 往下清理到線下端再往下多少 px
uniform float seg_fade = 0.35;                // 末端淡化：從線的這個位置（0～1，0＝下端、1＝上端）開始往上漸漸變淡到消失
uniform float seg_curve = 0.33;               // 彎曲程度：上端往內（左）偏 seg_curve×seg_len px，下端垂直（0＝直線）
uniform float seg_lean_unused = 0.0;                // 往上向內（往左）斜多少（px／px），跟裡面那條線一樣
uniform vec2 seg_b = vec2(1116.0, 1414.0);   // 窄帶右邊那條線上端的位置（畫布 px）
uniform float seg_len = 44.0;                 // 往上加長幾 px（到內褲上緣）
uniform float seg_bx = 15.0;                  // 下端離大腿遮罩左緣往右多少 px（＝窄帶右邊虛線上端的位置；固定座標，線才會是直的）
uniform float seg_dx = 5.0;                   // 起點相對大腿遮罩左緣往右多少 px（深紅輪廓線本身在遮罩邊緣內側）
uniform float seg_in = 4.0;                   // 終點離內褲右緣往內多少 px
uniform float seg_k = 1.0;                    // 濃度（0＝不補）
uniform vec2 rv_y = vec2(1396.0, 1436.0);      // 右邊黑線往上延伸：上端（接到腿的深紅輪廓）、下端（接到原本虛線）的高度（畫布 px）
uniform float rv_in = 3.0;
uniform float rv_slope = 0.7;                // 往上時向內（往左）折的斜率（px／px；0＝垂直，越大越斜）                    // 這條線離內褲右緣往內多少 px
uniform float rv_k = 0.0;                     // 濃度（0＝不畫）
uniform vec2 rline = vec2(2040.22, -0.6682);   // 右邊黑線補畫：大腿輪廓直線 x＝rline.x＋rline.y×y（畫布 px，從 腿1_M_cut 的左緣量出來）
uniform vec2 rline_y = vec2(1320.0, 1420.0);   // 補畫的起訖高度（畫布 px）：上緣到夾角
uniform float rline_k = 0.0;                  // 補畫的濃度（0＝不補）
uniform vec2 ext_a = vec2(1081.0, 1374.0);   // 中間黑線往上補畫：起點（弧線原本的上端，畫布 px）
uniform vec2 ext_b = vec2(1072.0, 1363.0);   // 補畫的終點（內褲上緣）
uniform float ext_k = 0.0;                    // 補畫的濃度（0＝不補）
uniform float rl_k = 0.0;                      // 夾角上端讓最右邊的黑線透出腿外的程度（0＝不透：大腿上不會出現內褲的線；>0 會把內褲右緣的線畫到大腿上）
uniform float rin_top = 1.0;                  // 1＝內褲上緣（y<1400）不放過大腿輪廓（大腿上不會有橫的內褲黑線）；0＝跟以前一樣放過
uniform float cover_top = 1.0;               // 夾角（上端 y<1425）只讓「黑線」透出腿外、不補白色：夾角完整、黑線連續，又不會多出一塊白色（0＝上端也補白色）
uniform float cover_y0 = 1370.0;              // 補白色開始的高度（畫布 px）：再往上是內褲和腿夾角，不補（不要多出一塊白色）
uniform float cover_in = 14.0;               // 補白色的右邊界：離內褲右緣往內多少 px（中間那條黑線的位置；越大白色越往左縮）
uniform float leg_grow = 0.004;               // 撥開時大腿輪廓再往左多裁一點（U；0.004≈8px）
uniform vec2 slash_y = vec2(1400.0, 1470.0);  // 右上「歪進來的黑線」起訖高度（畫布 px）
uniform vec2 slash_in = vec2(4.0, 50.0);      // 黑線起點／終點離內褲右緣往內多遠（畫布 px；終點比起點多＝往左下歪進來）
uniform float slash_k = 0.0;                  // 黑線濃度
uniform float edge_pad = 8.0;                 // 開口左緣往左多留幾 px，才不會把內褲自己的左邊線切掉
uniform float jz_mid = 12.0;                    // 插入時中間這一段原本的位移（px）：50＋10
uniform float jz_arch = 105.0;                  // 銜接處推擠範圍上緣的球形凸起：兩側比中間低多少 px
uniform float dbg_lines = 0.0;                  // 1＝畫出除錯線
uniform float jj_arch = 105.0;                   // 雞雞裁內褲的起點弧度：兩側比中間低多少 px
uniform float jj_from_y = 1500.0;             // 雞雞蓋在內褲上面只限這條線以下（畫布 px）；這條線以上＝插進去的部分，內褲在雞雞上面
uniform float sag_k = 0.22;                   // 轉折處布往下垂（向下凹）的量，相對撥開距離
uniform float crease_frac = 0.22;             // 右側摺線的位置：從右緣往左，佔被壓縮那一段寬度的比例
// ── 雞雞裁切（內褲被雞雞蓋過去的地方裁掉，露出底下的雞雞）──
uniform sampler2D jj_tex : filter_linear;
uniform vec2 jj_x = vec2(1.0, 0.0);
uniform vec2 jj_y = vec2(0.0, 1.0);
uniform vec2 jj_o = vec2(0.0);
uniform vec2 jj_size = vec2(2000.0, 3200.0);
uniform float jj_on = 0.0;
// ── 內褲在衣服下面：用「衣服變形後的輪廓」裁切內褲（衣服蓋到的地方內褲不顯示，衣擺拉開的地方才看得到）──
uniform sampler2D cc_tex : filter_linear;
uniform float cc_on = 0.0;
uniform float cc_k = 1.0;
varying vec4 vc;
void vertex() { vc = COLOR; }
float W_(float y) {
    float a_ = clamp((y - shear_v.x) / max(1.0, shear_v.y - shear_v.x), 0.0, 1.0);
    float b_ = clamp((y - fix_v.x) / max(1.0, fix_v.y - fix_v.x), 0.0, 1.0);
    float wOld_ = (a_ * a_ * (3.0 - 2.0 * a_)) * (1.0 - b_ * b_ * (3.0 - 2.0 * b_));
    float yc_ = 0.5 * (arc_v.x + arc_v.y);
    float hr_ = max(1.0, 0.5 * (arc_v.y - arc_v.x));
    float t_ = (y - yc_) / hr_;
    float uU_ = clamp((y - arc_v.x) / max(1.0, arc_peak - arc_v.x), 0.0, 1.0);
    float wU_ = pow(max(0.0, sin(1.5708 * uU_)), 0.85);                       // 上半：圓滑往右凸（指數越小越圓）
    float uD_ = clamp((y - arc_peak) / max(1.0, arc_v.y - arc_peak), 0.0, 1.0);
    float wD_ = 1.0 - pow(uD_, 1.15);                                          // 下半：前面慢、後面快地收回（邊往內凹成圓弧，不是往外凸），到 arc_v.y 剛好是 0，左下角維持固定不會浮空
    float wArc_ = y < arc_peak ? wU_ : wD_;
    return mix(wOld_, wArc_, arc_mix);
}
float XLY_(float y) { float t = (y - 1400.0) / 100.0; return edge_l.x + edge_l.y * t + edge_l.z * t * t; }
float XRY_(float y) { float t = (y - 1400.0) / 100.0; return edge_r.x + edge_r.y * t + edge_r.z * t * t; }
float XRE_(float y) {   // 撥開後內褲右緣的實際位置
    float wd_ = max(1.0, XRY_(y) - XLY_(y));
    return XRY_(y) + min(aside * W_(y) * left_boost, wd_ * 0.8) * right_follow;
}
float D_(float y) {   // 這一列被推動的距離（跟 fragment 裡算的一樣），拿來看推動量隨高度變化多快
    float wd_ = max(1.0, XRY_(y) - XLY_(y));
    float lf_ = mix(1.0, low_str, smoothstep(1540.0, 1610.0, y));
    return min(aside * W_(y) * left_boost * lf_, wd_ * 0.8 * lf_);
}
float insA_(vec2 p) {
    vec2 q = ins_pv + (p - ins_xf.xy - ins_pv) / ins_xf.zw;
    vec2 uv = q / vec2(2000.0, 3200.0);
    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) return 0.0;
    return texture(ins_tex, uv).a;
}
void fragment() {
    vec2 px = UV * vec2(2000.0, 3200.0);
    {   // 內褲和腳交接的地方跟著腳的旋轉做一點變形：靠近右緣的跟右腿轉、靠近左緣的跟左腿轉（離邊緣越遠越不動）
        float wr_ = smoothstep(XRY_(px.y) - 70.0, XRY_(px.y) + 10.0, px.x);
        float wl_ = 1.0 - smoothstep(XLY_(px.y) - 10.0, XLY_(px.y) + 70.0, px.x);
        vec2 vr_ = px - vec2(1060.0, 1500.0);
        vec2 dr2_ = vec2(cos(leg_rot.x) * vr_.x - sin(leg_rot.x) * vr_.y, sin(leg_rot.x) * vr_.x + cos(leg_rot.x) * vr_.y) - vr_;
        vec2 vl_ = px - vec2(940.0, 1500.0);
        vec2 dl2_ = vec2(cos(leg_rot.y) * vl_.x - sin(leg_rot.y) * vl_.y, sin(leg_rot.y) * vl_.x + cos(leg_rot.y) * vl_.y) - vl_;
        px -= dr2_ * wr_ + dl2_ * wl_;
        float wrT_ = smoothstep(XRY_(px.y) - 140.0, XRY_(px.y) + 10.0, px.x);
        float wlT_ = 1.0 - smoothstep(XLY_(px.y) - 10.0, XLY_(px.y) + 140.0, px.x);
        px -= leg_offr * wrT_ + leg_offl * wlT_;   // 腿的平移也要跟（範圍比旋轉更寬，腿向上動時右上角才不露縫）
    }
    {   // 插入時彎曲的部分（斜邊轉成垂直的那個肘）逆時針旋轉：以彎曲處為中心、離中心越遠影響越小
        vec2 pv_ = px - ins_pivot;
        float rw_ = exp(-dot(pv_, pv_) / (110.0 * 110.0));
        vec2 dr3_ = vec2(cos(ins_rot) * pv_.x - sin(ins_rot) * pv_.y, sin(ins_rot) * pv_.x + cos(ins_rot) * pv_.y) - pv_;
        px -= dr3_ * rw_;
    }
    float b1 = clamp((px.y - shear_v.x) / max(1.0, shear_v.y - shear_v.x), 0.0, 1.0);
    float b2 = clamp((px.y - fix_v.x) / max(1.0, fix_v.y - fix_v.x), 0.0, 1.0);
    float w = W_(px.y);   // 推動量沿高度的輪廓（月牙形）
    float sag = sag_k * aside * (b1 * b1 * (3.0 - 2.0 * b1)) * sin(3.14159 * b2);   // 下轉折帶中間布往下垂，讓上下接得平順
    float ys0_ = px.y - sag;
    float stA_ = smoothstep(stretch_anchor + 8.0, stretch_anchor - 40.0, ys0_);   // 1＝固定點上方、0＝下方（下方完全不動）
    float sb_ = (ys0_ - stretch_center) / stretch_width;
    float ts_loc_ = thrust_stretch;   // 變形集中在雞雞和插入銜接處的內褲
    float ys_ = mix(ys0_, stretch_anchor + (ys0_ - stretch_anchor) / (1.0 + ts_loc_), stA_);   // 小穴底部固定，往上的部分被拉長（不是整塊往上移，也不是單純擴張）
    // 取圖用的來源列：內褲左右緣要用這一列的位置算，開口邊界才會剛好貼在內褲自己的邊線上
    float t_ = (ys_ - 1400.0) / 100.0;
    float xl = edge_l.x + edge_l.y * t_ + edge_l.z * t_ * t_;
    float xr = edge_r.x + edge_r.y * t_ + edge_r.z * t_ * t_;
    float wd = max(1.0, xr - xl);
    float lf_ = mix(1.0, low_str, smoothstep(1540.0, 1610.0, ys_));   // 肛門那一帶的位移強度降低一點
    // 插入後貼合雞雞：入口（約 y 1500）往下到左下角之前，推動量上限放寬、右邊也多跟著往右，內褲右下角往右擴張一點去貼著雞雞
    float hug_ = hug_k * smoothstep(1500.0, 1550.0, ys_) * (1.0 - smoothstep(1540.0, 1575.0, ys_));   // 銜接處下面就淡掉（下面不跟著被推），左下角不離開
    float zd_ = jz_arch * (1.0 - sqrt(max(0.0, 1.0 - pow(clamp(abs(px.x - 985.0) / 130.0, 0.0, 1.0), 2.0))));   // 銜接處上緣是球形的凸起（中間高、兩側低）
    float zj_ = smoothstep(jz_rise.x, jz_rise.y, ys_ - zd_) * (1.0 - smoothstep(jz_fall.x, jz_fall.y, ys_)) * step(0.5, aside);   // 銜接處的範圍（兩端平滑）
    float zup_ = smoothstep(jz_rise.x - 70.0, jz_rise.x, ys_ - zd_);   // 銜接處上方的漸層：往上慢慢不位移
    float xtra_ = 0.0;   // （已併入 jz_mid 的疊加位移，不再另外加）
    float dA_ = (aside * w * left_boost * lf_ + thrust_j * zj_) * (1.0 + 0.15 * hug_) * (1.0 + ins_left * hug_k);       // 想推的距離（插入後左邊再往右）
    float dB_ = wd * (0.8 + 0.15 * hug_) * lf_;                            // 上限（最多推掉內褲寬度的 8 成；下面依 low_str 降低）
    // 平滑取最小：用硬的 min 時，斜邊接到右邊折角的地方會有一個尖角；改成平滑取最小，交會處變成圓弧
    float d = pow(pow(max(dA_, 0.001), -8.0) + pow(max(dB_, 0.001), -8.0), -0.125);
    // 右邊固定：把 [xl, xr] 的布壓縮進 [xl+d, xr]（越拉越扁）；xl+d 以左變成開口
    {   // 弧形的勾：入口高度（約 y 1555）的推動量多一個平滑的凸起，邊界在那裡往右彎出一個弧形，像勾住雞雞
        float hh_ = (ys_ - 1555.0) / 32.0;
        d = min(d + hook_px * exp(-hh_ * hh_) * step(0.5, aside), wd * 0.8);
    }
    float kc_ = hug_k * corner_push * smoothstep(jz_rise.x, jz_rise.y, ys_ - zd_) * (1.0 - smoothstep(jz_fall.x, jz_fall.y, ys_));
    d = mix(d, max(d, wd * 0.95), kc_);   // 插入後：銜接處的內褲整個推擠到右下角（窄窄一條貼在雞雞右緣）
    float xz_ = 0.0;
    {   // 插入時：在原本（撥開）的位移之上再「疊加」額外向右的位移，最高 jz_mid（px）
        // 曲線（y 為畫布 px）：1350→1450 平滑升到最高、1450→1500 維持、1500→1580 平滑降回 0（上下各用一段 smoothstep，沒有台階，邊緣才不會有凸起）
        float wz_ = hug_k * smoothstep(1300.0, 1450.0, ys_) * (1.0 - smoothstep(1500.0, 1580.0, ys_)) * step(0.5, aside);
        d += jz_mid * wz_;
        xz_ = jz_mid * wz_ * smoothstep(1380.0, 1470.0, ys_);   // 右緣也一起平移（上端漸入：最上面不位移，右邊那條線才會跟上面的內褲連起來）同樣的量（整條帶子往右滑，不是只推左緣把布壓扁）
    }
    float dr = d * (right_follow + 0.04 * hug_ + thrust_r * zj_) + xz_;   // 右緣只跟一點點：之前貼雞雞那一帶跟 0.5×d（上百 px），銜接處下面的最外側折角就被拉出去一大段，露出皮膚色   // 銜接處右邊不動（不外擴）   // 右邊跟著往右移的量（一點點；插入後貼雞雞的那一帶多一些）
    float nw = max(1.0, wd - d + dr);            // 壓縮後的寬度：[xl+d, xr+dr]
    float xs = xl + (px.x - xl - d) * wd / nw;
    float comp = wd / nw;                        // 壓縮倍率（1＝沒壓）
    {   // 插入肛門：肛門附近的內褲跟著肛門往外凸（以肛門為中心放大，越近越凸）
        vec2 dvb_ = vec2(xs, ys_) - anus_c;
        float fb_ = anus_bulge * exp(-dot(dvb_, dvb_) / (anus_r * anus_r));
        xs = anus_c.x + dvb_.x * (1.0 - fb_);
        ys_ = anus_c.y + dvb_.y * (1.0 - fb_);
    }
    float env = clamp((comp - 1.0) / 1.5, 0.0, 1.0);
    float shade = env * sin(xs * fold_freq + px.y * 0.03 + sin(px.y * 0.06) * 1.5);   // 壓縮出來的垂直褶皺
    // 壓縮時一個輸出像素涵蓋 comp 個來源像素：橫向多點平均（預乘），線稿才會變成連續的線，不會變成一格一格的紅黑條紋
    vec4 c;
    {
        vec4 acc_ = vec4(0.0);
        for (int k = 0; k < 5; k++) {
            float ko_ = (float(k) - 2.0) * 0.25 * min(comp, 8.0);
            vec4 tk_ = texture(TEXTURE, vec2((xs + ko_) / 2000.0, ys_ / 3200.0));
            acc_ += vec4(tk_.rgb * tk_.a, tk_.a);
        }
        acc_ /= 5.0;
        c = vec4(acc_.a > 0.001 ? acc_.rgb / acc_.a : vec3(0.0), acc_.a);
    }
    {   // 內褲中間那條黑色縫線（約 x 958~1000、y 1438~1600）當成獨立的東西：內褲被拉開時淡化，脫離內褲就消失
        float sx_ = smoothstep(954.0, 964.0, xs) * (1.0 - smoothstep(994.0, 1004.0, xs));
        float sy_ = smoothstep(1434.0, 1446.0, ys_) * (1.0 - smoothstep(1594.0, 1606.0, ys_));
        float seamK_ = sx_ * sy_ * smoothstep(15.0, 70.0, aside) * seam_fade;
        vec3 fill_ = 0.5 * (texture(TEXTURE, vec2((xs - 24.0) / 2000.0, ys_ / 3200.0)).rgb + texture(TEXTURE, vec2((xs + 24.0) / 2000.0, ys_ / 3200.0)).rgb);
        float dkS_ = smoothstep(0.18, 0.50, 1.0 - dot(c.rgb, vec3(0.3333)));
        c.rgb = mix(c.rgb, fill_, seamK_ * dkS_);
    }
    // 開口：只有「真的被推動」的地方才切；沒推動的地方（最底下的尾巴、上面）完全不切，
    // 否則用估算的左緣去切，會把尾巴左下角那個向外翹的角整塊切掉。
    float hb = xl + d - edge_pad;
    float holeA = (d > 1.0) ? smoothstep(hb - 1.0, hb + 1.5, px.x) : 1.0;   // 開口邊緣柔邊（不要一格一格的鋸齒）
    c.a = smoothstep(0.3 * smoothstep(0.0, 6.0, d), 0.6, c.a) * holeA;
    float redge0_ = xr + dr;
    float edgeNear_ = max(1.0 - smoothstep(0.0, 14.0, px.x - (xl + d)), 1.0 - smoothstep(0.0, 14.0, redge0_ - px.x)) * smoothstep(1.0, 8.0, d);   // 左右邊緣 14px 內
    {   // 邊緣的線稿被壓縮後會變細變淡：把邊緣 14px 內的線加深，補回被壓掉的粗細
        float dkc_ = 1.0 - dot(c.rgb, vec3(0.3333));
        c.rgb = mix(c.rgb, vec3(0.30, 0.12, 0.12), clamp((comp - 1.0) * 0.35, 0.0, 0.8) * smoothstep(0.12, 0.45, dkc_) * edgeNear_ * outline_k * 0.5);
    }
    if (ed_on < 0.5) {   // 邊緣補黑線（沒有邊線貼圖時的備用做法）：只要被推動（d>1），可見形狀的邊緣就補一圈深色描邊——不再等「壓縮超過一定程度」才開，
        // 剪切剛開始的那條斜邊（壓縮還很小）也要有黑線。邊的外側和內側都補，線才夠明顯、連續。
        float rr_ = 3.0;
        float k03_ = 0.3 * smoothstep(0.0, 6.0, d);   // 跟上面實心化用同一條門檻，邊才判得準
        float aL_ = smoothstep(k03_, 0.6, texture(TEXTURE, vec2((xs - rr_ * comp) / 2000.0, ys_ / 3200.0)).a);
        float aR_ = smoothstep(k03_, 0.6, texture(TEXTURE, vec2((xs + rr_ * comp) / 2000.0, ys_ / 3200.0)).a);
        float aU_ = smoothstep(k03_, 0.6, texture(TEXTURE, vec2(xs / 2000.0, (ys_ - rr_) / 3200.0)).a);
        float aD_ = smoothstep(k03_, 0.6, texture(TEXTURE, vec2(xs / 2000.0, (ys_ + rr_) / 3200.0)).a);
        float edgeK_ = smoothstep(1.0, 8.0, d) * min(1.0, outline_k * 1.3);
        // 左右鄰居偵測到的邊＝陡的（近垂直）邊：黑線減少；上下鄰居偵測到的邊＝斜的／平的邊：黑線照樣強化
        float outH_ = (1.0 - smoothstep(0.1, 0.4, c.a)) * smoothstep(0.4, 0.8, max(aL_, aR_));
        float inH_ = smoothstep(0.5, 0.9, c.a) * (1.0 - smoothstep(0.15, 0.45, min(aL_, aR_)));
        float outV_ = (1.0 - smoothstep(0.1, 0.4, c.a)) * smoothstep(0.4, 0.8, max(aU_, aD_));
        float inV_ = smoothstep(0.5, 0.9, c.a) * (1.0 - smoothstep(0.15, 0.45, min(aU_, aD_)));
        float outE_ = max(outH_ * line_steep, outV_);   // 邊的外側
        float inE_ = max(inH_ * line_steep, inV_);       // 邊的內側
        // 開口切線（hb）本身也補一條黑線：萬一開口邊界落在布的中間，切口也不能是光禿禿一條虛線
        float cutL_ = (1.0 - smoothstep(0.0, 3.0, abs(px.x - hb - 1.5))) * smoothstep(0.5, 0.9, c.a / max(holeA, 0.001)) * smoothstep(1.0, 8.0, d) * min(1.0, outline_k * 1.3);
        float eo_ = max(max(outE_, inE_ * 0.85) * holeA * edgeK_, cutL_ * step(0.001, holeA)) * (1.0 - smoothstep(3.0, 7.0, comp) * (1.0 - edgeNear_));
        c.rgb = mix(c.rgb, vec3(0.30, 0.12, 0.12), eo_);
        c.a = max(c.a, eo_);
    }
    if (ed_on > 0.5) {   // 用烘好的乾淨邊線貼圖：形狀圓角、沒有雜訊，斜邊／下面的角也有連續的黑線；跟內褲同一套座標，會一起被拉、一起被壓
        vec4 em_ = vec4(0.0);
        float cleanA_ = 0.0;
        for (int k = 0; k < 5; k++) {
            float eko_ = (float(k) - 2.0) * 0.4 * min(comp, 3.0);   // 壓得很窄時取樣間距不要跟著暴增（會取到很遠的線，變成梳子狀橫紋）
            vec4 ek_ = texture(ed_tex, vec2((xs + eko_) / 2000.0, ys_ / 3200.0));
            em_ = max(em_, ek_);       // 線用取最大值：壓縮時線不會被平均掉變淡
            cleanA_ += ek_.b;
        }
        cleanA_ /= 5.0;
        float pushK_ = smoothstep(0.0, 6.0, d);
        c.a = mix(c.a, smoothstep(0.42, 0.58, cleanA_) * holeA, pushK_);   // 被推動時改用乾淨的圓角形狀（取代帶雜訊的霧邊緣）
        // 陡／斜要用「畫面上」的方向判斷：內褲被剪切、壓縮之後，原圖裡陡的邊在畫面上可能已經變成斜的
        float gx_ = dFdx(cleanA_);
        float gy_ = dFdy(cleanA_);
        float sh_ = abs(gy_) / (abs(gx_) + abs(gy_) + 0.0001);   // 1＝邊是平的／斜到近水平，0＝邊是垂直的
        float orient_ = mix(line_steep, 1.0, smoothstep(0.35, 0.65, sh_));
        float lineE_ = max(em_.r, em_.g) * orient_ * smoothstep(1.0, 8.0, d) * min(1.0, outline_k * 1.3) * holeA * (1.0 - max(clamp(kc_, 0.0, 1.0), smoothstep(3.0, 7.0, comp)) * (1.0 - edgeNear_));   // 被壓很窄的地方（不管在哪個高度）只留邊緣的線，中間的線全不畫   // 細帶中間不畫擠在一起的線，只留邊緣的線
        c.rgb = mix(c.rgb, vec3(0.30, 0.12, 0.12), lineE_);
        c.a = max(c.a, lineE_);
    }
    c.rgb *= 1.0 - fold_k * 0.30 * clamp(shade, 0.0, 1.0);   // 陰影（預設 fold_k＝0，不要）
    float dark_ = 1.0 - dot(c.rgb, vec3(0.3333));   // 被壓縮擠在一起的原本黑線（線稿）淡化一點，不要一堆黑線
    float nearR_ = smoothstep(redge0_ - 38.0, redge0_ - 22.0, px.x) * (1.0 - smoothstep(0.0, 6.0, px.x - redge0_));   // 右緣那條邊線附近：保留黑線
    c.rgb = mix(c.rgb, vec3(0.88, 0.85, 0.90), clamp(env * 0.45, 0.0, 0.45) * smoothstep(0.30, 0.65, dark_) * (1.0 - nearR_) * (1.0 - edgeNear_));   // 被擠壓出來的線條褶皺：明顯的保留，只淡化一點（邊緣的線不淡化）
    c.rgb = mix(c.rgb, vec3(0.90, 0.87, 0.92), clamp(kc_, 0.0, 1.0) * (1.0 - edgeNear_) * 0.9);   // 被擠成細帶的地方整條用平的布色，不要出現一格一格的橫紋   // 被擠成一條的地方，擠在一起的黑線一律淡掉，不要變成一堆像程式畫的黑線
    c.rgb = mix(c.rgb, vec3(0.90, 0.87, 0.92), smoothstep(3.0, 7.0, comp) * (1.0 - edgeNear_) * step(0.3, c.a));   // 擠得很窄的地方整條用平的布色，原本的線稿擠在一起會變成橫向破圖
    float cx_ = (xr + dr) - crease_frac * ((xr + dr) - (xl + d));   // 右側多出來的一條黑色摺線（布被壓出來的折邊）
    float cline = (1.0 - smoothstep(0.8, 2.4, abs(px.x - cx_))) * smoothstep(0.05, 0.4, comp - 1.0) * step(0.3, c.a);
    c.rgb = mix(c.rgb, vec3(0.30, 0.12, 0.12), cline * 0.45);
    {   // 右上歪進來的黑線：從內褲右緣上面一點，往左下歪進來（像布折進去的邊）
        vec2 pa_ = vec2(XRE_(slash_y.x) - slash_in.x, slash_y.x);
        vec2 pb_ = vec2(XRE_(slash_y.y) - slash_in.y, slash_y.y);
        vec2 ab_ = pb_ - pa_;
        float tt_ = clamp(dot(px - pa_, ab_) / max(1.0, dot(ab_, ab_)), 0.0, 1.0);
        float dist_ = length(px - (pa_ + ab_ * tt_));
        float sl_ = (1.0 - smoothstep(1.3, 3.3, dist_)) * smoothstep(1.0, 20.0, aside) * step(0.3, c.a) * (1.0 - 0.5 * tt_);
        c.rgb = mix(c.rgb, vec3(0.33, 0.12, 0.12), sl_ * slash_k);
    }
    {   // 下半部淡出的裁切：拉開越多，下半部（尾巴、肛門那一帶）越淡出，不要留一條被扯長的尾巴
        // 乾淨的切斷（不要半透明漸層）：拉開越多，切斷線從內褲最底下往上移，到最大時停在肛門上面（y≈1575），
        // 切斷處補一條細的深色邊線，看起來像內褲的下緣；肛門就露出來
        float pull_ = smoothstep(20.0, 120.0, aside) * low_fade;
        float yc_ = mix(1700.0, 1575.0, pull_);
        float aPre_ = c.a;
        c.a *= 1.0 - smoothstep(yc_, yc_ + 6.0, ys_);
        float hem_ = (1.0 - smoothstep(0.0, 2.5, abs(ys_ - yc_ - 1.0))) * step(0.3, aPre_) * pull_;
        c.rgb = mix(c.rgb, vec3(0.30, 0.12, 0.12), hem_ * 0.8);
        c.a = max(c.a, hem_ * aPre_);
        // （已拿掉）以前這裡有一條「剪切太陡的地方把深色線條刪掉」的規則，結果把斜邊的黑線也刪掉了，所以斜線怎麼補都看不到。
    }
    if (an_on > 0.5) {   // 肛門：撥開時只在肛門的位置挖洞，露出底下的肛門（下半部其他地方的內褲完整保留）
        vec2 auv_ = (px - an_off) / vec2(2000.0, 3200.0);
        if (auv_.x >= 0.0 && auv_.x <= 1.0 && auv_.y >= 0.0 && auv_.y <= 1.0)
            c.a *= 1.0 - smoothstep(0.05, 0.5, texture(an_tex, auv_).a) * smoothstep(40.0, 110.0, aside);
    }
    {   // 保險：撥開時，超過「原本右緣 + max_reach」的部分一律不顯示
        float xrc_ = edge_r.x + edge_r.y * clamp(t_, -0.5, 2.4) + edge_r.z * clamp(t_, -0.5, 2.4) * clamp(t_, -0.5, 2.4);
        c.a *= 1.0 - smoothstep(xrc_ + max_reach, xrc_ + max_reach + 6.0, px.x) * smoothstep(0.0, 6.0, d);
    }
    if (jj_on > 0.5) {
        vec2 pj2_ = UV * jj_size;
        vec2 jq_ = (jj_x * pj2_.x + jj_y * pj2_.y + jj_o) / vec2(2000.0, 3200.0);
        if (jq_.x >= 0.0 && jq_.x <= 1.0 && jq_.y >= 0.0 && jq_.y <= 1.0) {
            const vec2 oj_ = vec2(0.0007, 0.0004);   // 遮罩是半解析度，多取幾點平均，切口的線才不會變成一個一個的點
            float jraw_ = 0.25 * (texture(jj_tex, jq_ + oj_).a + texture(jj_tex, jq_ - oj_).a + texture(jj_tex, jq_ + vec2(oj_.x, -oj_.y)).a + texture(jj_tex, jq_ + vec2(-oj_.x, oj_.y)).a);
            float js_ = smoothstep(0.38, 0.52, jraw_);   // 裁切邊緣要俐落，不要淡出
            float jfy_ = jj_from_y + jj_arch * (1.0 - sqrt(max(0.0, 1.0 - pow(clamp(abs(px.x - 985.0) / 130.0, 0.0, 1.0), 2.0)))) * step(1.0, jj_from_y);   // 裁切起點是一條弧形，不是橫線
            float jw_ = smoothstep(jfy_, jfy_ + 2.0, px.y);
            float aPreJ_ = c.a;
            c.a *= 1.0 - js_ * jw_;   // 只有這條線以下的雞雞蓋在內褲上面；切口收乾淨（不柔化）
            // 沿著雞雞的切口畫一條黑線：插入後折進去的地方，左邊的邊線和右下角快被切掉的地方靠這條線連起來，內褲看起來是連貫的
            float jedge_ = (1.0 - smoothstep(0.0, 0.62, abs(js_ - 0.5))) * jw_ * step(0.3, aPreJ_) * smoothstep(0.2, 0.8, hug_k);   // 黑線只在插入後才畫，還沒插入時不要把內褲染暗
            c.rgb = mix(c.rgb, vec3(0.30, 0.12, 0.12), jedge_ * 0.9);
            c.a = max(c.a, jedge_ * 0.9 * aPreJ_);
        }
    }
    {   // 插入時：銜接處那條細內褲，沿著「插入」物件下面的黑線往下裁掉（裁切邊緣就是插入的下緣輪廓，不是橫的直線）
        float jk_ = max(hug_k, jj_on * step(0.5, aside));
        float xg_ = smoothstep(1000.0, 1040.0, px.x);
        float aPJ_ = c.a;
        if (ins_on > 0.5) {
            float here_ = insA_(px);
            float above_ = 0.0;
            for (int k = 1; k <= 14; k++) above_ = max(above_, insA_(px + vec2(-4.0 * float(k), -8.0 * float(k))));   // 往左上看，插入物件還在不在
            float below_ = (1.0 - smoothstep(0.1, 0.5, here_)) * smoothstep(0.1, 0.5, above_);
            float cutI_ = jk_ * jf_str * below_ * xg_;
            c.a *= 1.0 - cutI_;
            float edgeI_ = (1.0 - smoothstep(0.1, 0.5, here_)) * smoothstep(0.3, 0.6, insA_(px + vec2(-2.0, -3.0)));   // 插入下緣那圈線
            float lnI_ = edgeI_ * jk_ * jf_str * step(0.3, aPJ_) * xg_;
            c.rgb = mix(c.rgb, vec3(0.30, 0.12, 0.12), lnI_ * 0.6);
            c.a = max(c.a, lnI_ * 0.6 * aPJ_);
        }
    }
    {   // 右上角補洞：這一帶內褲被腿帶動、拉伸後偶爾會出現被切掉、露出底下皮膚色的缺口，周圍有內褲的話就把缺口補上（只補 12px 以內的洞）
        float gz_ = smoothstep(1500.0, 1440.0, px.y) * smoothstep(XRY_(px.y) - 80.0, XRY_(px.y) - 20.0, px.x) * step(0.5, aside);
        if (gz_ > 0.01 && c.a < 0.97) {
            float dil_ = 0.0;
            for (int gi = 0; gi < 8; gi++) {
                float ga_ = 6.2831853 * float(gi) / 8.0;
                vec2 go_ = vec2(cos(ga_), sin(ga_)) * (gi % 2 == 0 ? 7.0 : 13.0);
                dil_ = max(dil_, texture(TEXTURE, vec2((xs + go_.x) / 2000.0, (ys_ + go_.y) / 3200.0)).a);
            }
            float fillA_ = smoothstep(0.4, 0.8, dil_) * gz_;
            c.rgb = mix(c.rgb, vec3(0.90, 0.87, 0.92), fillA_ * (1.0 - c.a));
            c.a = max(c.a, fillA_ * 0.97);
        }
    }
    if (dbg_lines > 0.5 && px.x > 780.0 && px.x < 1200.0 && px.y > 1320.0 && px.y < 1700.0) {   // 除錯：畫出內褲各條裁切／推擠線（按 Delete 開關）
        float jfyD_ = jj_from_y + jj_arch * (1.0 - sqrt(max(0.0, 1.0 - pow(clamp(abs(px.x - 985.0) / 130.0, 0.0, 1.0), 2.0)))) * step(1.0, jj_from_y);
        float lnR_ = 1.0 - smoothstep(0.0, 2.0, abs(px.y - jfyD_));                         // 紅：雞雞裁內褲的起點（弧形）
        float zdD_ = jz_arch * (1.0 - sqrt(max(0.0, 1.0 - pow(clamp(abs(px.x - 985.0) / 130.0, 0.0, 1.0), 2.0))));
        float lnY_ = max(1.0 - smoothstep(0.0, 1.5, abs(px.y - zdD_ - jz_rise.x)), 1.0 - smoothstep(0.0, 1.5, abs(px.y - zdD_ - jz_rise.y)));   // 黃：銜接處推擠範圍上緣（球形凸起）
        float lnM_ = 1.0 - smoothstep(0.0, 1.5, abs(px.y - jf_y.y));                         // 紫：黑線（細帶裁切）高度
        float lnC_ = 0.0;
        if (jj_on > 0.5) {   // 青：雞雞輪廓裁內褲的邊（還沒插入時整個雞雞輪廓都在裁，所以這條才是當時的裁切線）
            vec2 pjD_ = UV * jj_size;
            vec2 jqD_ = (jj_x * pjD_.x + jj_y * pjD_.y + jj_o) / vec2(2000.0, 3200.0);
            if (jqD_.x >= 0.0 && jqD_.x <= 1.0 && jqD_.y >= 0.0 && jqD_.y <= 1.0) {
                const vec2 ojD_ = vec2(0.0007, 0.0004);
                float jrD_ = 0.25 * (texture(jj_tex, jqD_ + ojD_).a + texture(jj_tex, jqD_ - ojD_).a + texture(jj_tex, jqD_ + vec2(ojD_.x, -ojD_.y)).a + texture(jj_tex, jqD_ + vec2(-ojD_.x, ojD_.y)).a);
                lnC_ = 1.0 - smoothstep(0.0, 0.18, abs(smoothstep(0.38, 0.52, jrD_) - 0.5) - 0.3);
                lnC_ *= step(0.05, jrD_) * (1.0 - step(0.95, jrD_));
            }
        }
        c.rgb = mix(c.rgb, vec3(0.0, 0.9, 0.9), lnC_);
        c.a = max(c.a, lnC_);
        c.rgb = mix(c.rgb, vec3(1.0, 0.0, 0.0), lnR_);
        c.rgb = mix(c.rgb, vec3(1.0, 0.9, 0.0), lnY_ * 0.8);
        c.rgb = mix(c.rgb, vec3(0.8, 0.0, 1.0), lnM_);
        c.a = max(c.a, max(lnR_, max(lnY_ * 0.8, lnM_)));
    }
    if (cc_on > 0.5) {
        vec2 pj4_ = UV * jj_size;
        vec2 cq_ = (jj_x * pj4_.x + jj_y * pj4_.y + jj_o) / vec2(2000.0, 3200.0);
        if (cq_.x >= 0.0 && cq_.x <= 1.0 && cq_.y >= 0.0 && cq_.y <= 1.0) {
            const vec2 oc_ = vec2(0.0007, 0.0004);
            float ca_ = 0.25 * (texture(cc_tex, cq_ + oc_).a + texture(cc_tex, cq_ - oc_).a + texture(cc_tex, cq_ + vec2(oc_.x, -oc_.y)).a + texture(cc_tex, cq_ + vec2(-oc_.x, oc_.y)).a);
            ca_ = mix(ca_, smoothstep(0.30, 0.65, ca_), smoothstep(1.0, 10.0, aside));   // 撥開時衣服的切口也要乾淨
            c.a *= 1.0 - ca_ * cc_k;   // 衣服蓋到的地方，內褲被裁掉（內褲在衣服下面）
        }
    }
    if (lg_on > 0.5) {
        vec2 pj_ = UV * cl_size;
        vec2 lq_ = (lg_x * pj_.x + lg_y * pj_.y + lg_o) / vec2(2000.0, 3200.0);
        float ry_ = (pj_.y - 1326.0) / (1684.0 - 1326.0);   // 內褲內容框（畫布 px 1326~1684）由上到下 0~1
        float lw_ = 1.0 - smoothstep(0.45, 0.65, ry_);       // 只裁上半部；下半部（底下那一截）完整保留，不被腿切掉
        // 撥開時內褲右緣（往內約 45px）那條黑邊線不要被大腿擋斷：右上準備拐進去的線和右下的邊線本來是連著的。
        // 右緣以外、以及左邊照常被大腿裁切。
        float redge_ = xr + dr;
        float dk_ = smoothstep(0.30, 0.60, 1.0 - dot(c.rgb, vec3(0.3333)));   // 只有深色線條才露在大腿上面；淺色布和外圍淡霧（白色尖角）仍在大腿下面
        // 黑線以左的內褲（白色布）要全部出現，黑線右上的白角不能出現：
        // 不用估算的右緣（會跟真正的黑線差好幾 px，白角就跑出來），直接看圖本身——
        // 這個像素的右邊 ~90px（取圖來源）內真的有深色黑線，才算「黑線左邊」，才不被大腿裁切；黑線右邊沒有黑線了，留在大腿下面。
        float rd_ = 0.0;
        for (int i = 0; i < 12; i++) {
            for (int j = -2; j <= 2; j++) {   // 上下也各取幾列取最大：被壓扁的內褲上，橫向的線稿每隔幾列才有一條，只取一列會一行有一行沒有，變成橫條紋露出膚色（破圖）
                vec4 ts_ = texture(TEXTURE, vec2((xs + float(i) * 8.0) / 2000.0, (ys_ + float(j) * 6.0) / 3200.0));
                rd_ = max(rd_, ts_.a * smoothstep(0.30, 0.60, 1.0 - dot(ts_.rgb, vec3(0.3333))) * (j == 0 ? 1.0 : smoothstep(1410.0, 1440.0, px.y)));   // 上下多取的列只在窄帶那一段（y>1410）生效；上面大腿那一帶不要，不然內褲的線會透到大腿上
            }
        }
        float rin_ = rd_ * smoothstep(960.0, 1000.0, pj_.x);
        rin_ *= 1.0 - (1.0 - smoothstep(1385.0, 1415.0, pj_.y)) * rin_top;   // 內褲上緣那條深紅線（y<1400）不要被放過去：不然會越過腿的輪廓、橫著畫在大腿上   // 只有右半邊的黑線才保留；左邊腰帶尖端那條線要被大腿裁掉（不然會殘留在腳上）
        lw_ *= 1.0 - smoothstep(0.0, 6.0, d) * rin_;
        if (lq_.x >= 0.0 && lq_.x <= 1.0 && lq_.y >= 0.0 && lq_.y <= 1.0) {
            const vec2 o_ = vec2(0.0007, 0.0004);   // 遮罩是半解析度，多取幾點平均，邊緣才不會一格一格
            // 右上角（內褲上半部、右邊）大腿切進內褲的邊界往右退一點，白色布多留一點、黑線才能再往上連到左邊那條的高度
            float relaxK_ = smoothstep(0.0, 6.0, d) * (1.0 - smoothstep(0.30, 0.55, ry_)) * smoothstep(979.0, 1020.0, pj_.x);
            vec2 lqr_ = lq_ + vec2(leg_relax * relaxK_, 0.0);
            float la_ = 0.25 * (texture(leg_tex, lqr_ + o_).a + texture(leg_tex, lqr_ - o_).a + texture(leg_tex, lqr_ + vec2(o_.x, -o_.y)).a + texture(leg_tex, lqr_ + vec2(-o_.x, o_.y)).a);
            la_ = max(la_, texture(leg_tex, lq_ + vec2(-leg_grow, 0.0)).a * smoothstep(0.0, 6.0, d) * (1.0 - rin_));   // 撥開時大腿的輪廓再往左多裁一點（右緣黑邊線除外）
            la_ = mix(la_, smoothstep(0.30, 0.65, la_), smoothstep(1.0, 10.0, aside));   // 撥開時大腿的切口要乾淨（不要淡化）
            // 撥開後右腿邊那一段（約 y 1430~1545）：窄帶被腿輪廓裁掉的地方會露出腿的膚色 → 這裡減弱腿的裁切，讓白色布蓋住膚色（右緣黑線 rin_ 照舊保留）
            float cover_ = smoothstep(cover_y0, cover_y0 + 25.0, pj_.y) * (1.0 - smoothstep(1520.0, 1550.0, pj_.y)) * smoothstep(1000.0, 1040.0, pj_.x) * smoothstep(0.0, 6.0, d) * (1.0 - rin_) * leg_cover * smoothstep(0.3, 0.8, hug_k) * (1.0 - smoothstep(redge0_ - cover_in, redge0_ - cover_in + 12.0, px.x));   // 右邊只補到「中間那條黑線」為止（離右緣 cover_in px），線右邊的大腿照常裁切，不要多出白色
            float upK_ = (1.0 - smoothstep(1385.0, 1425.0, pj_.y)) * cover_top;   // 夾角上端：只留黑線
            // 夾角上端：不要讓內褲左邊那條弧線穿到腿外面（會形成 X 形的角）；只讓最右邊（內褲右緣 redge0_ 附近）那條黑線透出來，往上接到腿的輪廓
            float rightLine_ = dk_ * smoothstep(redge0_ - 12.0, redge0_ - 6.0, px.x) * (1.0 - smoothstep(redge0_ + 2.0, redge0_ + 8.0, px.x)) * smoothstep(1330.0, 1370.0, pj_.y) * smoothstep(1000.0, 1040.0, pj_.x) * smoothstep(0.0, 6.0, d) * leg_cover * smoothstep(0.3, 0.8, hug_k) * rl_k * smoothstep(0.3, 0.8, hug_k);
            float coverA_ = max(cover_ * (1.0 - upK_), rightLine_ * upK_);
            // 窄帶的本體（開口左緣 hb ～ 右緣 redge0_ 內側）：內褲在這裡自己決定形狀——不被腿裁切、透明度補成實的，這樣腿的輪廓線不會從半透明的內褲底下透出來（淡淡的斜線）
            float body_ = smoothstep(hb + 6.0, hb + 10.0, px.x) * (1.0 - smoothstep(redge0_ - 10.0, redge0_ - 6.0, px.x)) * smoothstep(1398.0, 1408.0, pj_.y) * (1.0 - smoothstep(1440.0, 1458.0, pj_.y))   // 本體只補窄帶上端（斜線出現的那一段），下面照原樣，左邊也退進來一點，不要在窄帶左邊多出一條白色
                  * smoothstep(1.0, 20.0, aside) * body_k * smoothstep(0.3, 0.8, hug_k);

            // 橢圓：補畫的線是橢圓的右上 1/4 弧（中心約畫布 (1100,1414)、半徑 16×45）；橢圓裡面的範圍＝腿要被內褲「切掉」的地方（內褲在這裡不被腿裁切、補成不透明）。橢圓中心跟著內褲右緣位移
            float cxE_ = redge0_ - ell_dx - ell_slope * (1416.7 - ys_);
            float exE_ = (px.x - cxE_) / ell_rx;
            float eyE_ = (ys_ - ell_cy) / ell_ry;
            float ell_ = (1.0 - smoothstep(0.92, 1.0, exE_ * exE_ + eyE_ * eyE_)) * (1.0 - smoothstep(ell_cy, ell_cy + 4.0, ys_)) * smoothstep(1.0, 20.0, aside) * ell_k * smoothstep(0.3, 0.8, hug_k);   // 只取橢圓上半（補畫的弧線那一邊），下半不要，不然會突出到大腿上
            body_ = max(body_, ell_);
            coverA_ = max(coverA_, body_);
            float aBefore_ = c.a;
            // 補布的顏色不用固定色塊：取這一列內褲自己左邊內部的布色（亮的那個），陰影和色調才會跟旁邊一樣
            vec3 cc1_ = texture(TEXTURE, vec2((xs - 20.0) / 2000.0, ys_ / 3200.0)).rgb;
            vec3 cc2_ = texture(TEXTURE, vec2((xs - 40.0) / 2000.0, ys_ / 3200.0)).rgb;
            vec3 cc3_ = texture(TEXTURE, vec2((xs - 60.0) / 2000.0, ys_ / 3200.0)).rgb;
            vec3 clothcol_ = cc1_;
            if (dot(cc2_, vec3(1.0)) > dot(clothcol_, vec3(1.0))) clothcol_ = cc2_;
            if (dot(cc3_, vec3(1.0)) > dot(clothcol_, vec3(1.0))) clothcol_ = cc3_;
            c.a *= 1.0 - la_ * lw_ * (1.0 - coverA_);
            c.rgb = mix(c.rgb, clothcol_, body_ * (1.0 - c.a));   // 本體裡原本透的地方，補上內褲自己的布色（不是固定色塊）
            c.a = max(c.a, body_);
            c.a *= 1.0 - smoothstep(redge0_ + 6.0, redge0_ + 14.0, px.x) * smoothstep(0.0, 6.0, d) * la_;
            {   // 清理：補的那條線的左邊和下面，露出來的腿的線條／破圖的膚色，一律用白色內褲布蓋住（只蓋線左邊，線右邊的大腿不動）
                float st2_ = clamp((seg_b.y - pj_.y) / max(1.0, seg_len), 0.0, 1.0);
                float bx_ = seg_b.x - 2.0 - seg_curve * seg_len * st2_ * st2_;
                float yk9_ = smoothstep(seg_b.y - seg_len - 2.0, seg_b.y - seg_len + 6.0, pj_.y) * (1.0 - smoothstep(seg_b.y + clean_dn - 8.0, seg_b.y + clean_dn, pj_.y));
                float bx2_ = min(bx_, redge0_ - 4.0);   // 右邊界不超過窄帶右緣（線下面右邊不要多出一塊白色）
                float xk9_ = (1.0 - smoothstep(bx2_ - 2.0, bx2_ + 1.0, pj_.x)) * smoothstep(seg_b.x - 75.0, seg_b.x - 60.0, pj_.x);
                float tk9_ = yk9_ * xk9_ * smoothstep(0.1, 0.5, la_) * (1.0 - c.a) * clean_k * smoothstep(0.3, 0.8, hug_k) * smoothstep(1.0, 20.0, aside);   // 只填「內褲被腿裁掉、露出腿」的地方（c.a 低），內褲自己的線和布不動
                float stub_ = smoothstep(seg_b.x - 32.0, seg_b.x - 28.0, pj_.x) * (1.0 - smoothstep(seg_b.x - 8.0, seg_b.x - 4.0, pj_.x)) * smoothstep(seg_b.y - 14.0, seg_b.y - 10.0, pj_.y) * (1.0 - smoothstep(seg_b.y + 12.0, seg_b.y + 16.0, pj_.y));   // 夾角下面那截露出的深紅腿線所在的小範圍
                float mk9_ = (1.0 - smoothstep(0.30, 0.75, c.r)) * smoothstep(0.1, 0.5, c.a) * stub_ * clean_k * smoothstep(0.3, 0.8, hug_k) * smoothstep(1.0, 20.0, aside);   // 露出的腿的深紅線（很深很紅）：線左邊的一律換成布色
                {   // 右上到左下那條粗的深紅線是腿自己的輪廓：沿著腿遮罩左緣把它用布色蓋掉（只蓋輪廓那一窄條，大腿的膚色不動）
                    float xe9_ = rline.x + rline.y * pj_.y;
                    float oy_ = smoothstep(ol_y.x - 2.0, ol_y.x + 3.0, pj_.y) * (1.0 - smoothstep(ol_y.y - 3.0, ol_y.y + 2.0, pj_.y));
                    float ox_ = smoothstep(xe9_ - ol_w - 1.5, xe9_ - ol_w, pj_.x) * (1.0 - smoothstep(xe9_ + ol_r, xe9_ + ol_r + 1.5, pj_.x));
                    float ok_ = oy_ * ox_ * ol_k * smoothstep(0.3, 0.8, hug_k) * smoothstep(1.0, 20.0, aside);
                    c.rgb = mix(c.rgb, clothcol_, ok_);
                    c.a = max(c.a, ok_);
                }
                float tkS_ = stub_ * (1.0 - c.a) * clean_k * smoothstep(0.3, 0.8, hug_k) * smoothstep(1.0, 20.0, aside);   // 露出腿線條的那一小塊：不管腿遮罩，只要內褲在這裡是透的，就補布
                tk9_ = max(tk9_, tkS_);
                c.rgb = mix(c.rgb, clothcol_, max(tk9_, mk9_));
                c.a = max(c.a, tk9_);
            }
            {   // 右邊那條黑線：直接沿著「大腿遮罩的左緣」畫（用遊戲實際的腿位置，不是估的座標）：從內褲上緣一路畫到夾角
                vec2 lqS_ = (lg_x * (pj_.x + 2.5) + lg_y * pj_.y + lg_o) / vec2(2000.0, 3200.0);
                float aS_ = (lqS_.x >= 0.0 && lqS_.x <= 1.0) ? texture(leg_tex, lqS_).a : 0.0;
                float aH_ = texture(leg_tex, lq_).a;
                float edgeL_ = (1.0 - smoothstep(0.3, 0.6, aH_)) * smoothstep(0.4, 0.7, aS_);
                float yk7_ = smoothstep(rline_y.x - 6.0, rline_y.x + 4.0, pj_.y) * (1.0 - smoothstep(rline_y.y - 4.0, rline_y.y + 6.0, pj_.y));
                float k7_ = edgeL_ * yk7_ * step(0.3, aBefore_) * smoothstep(1.0, 20.0, aside) * rline_k * smoothstep(0.3, 0.8, hug_k);
                c.rgb = mix(c.rgb, vec3(0.30, 0.12, 0.12), k7_ * 0.95);
                c.a = max(c.a, k7_ * 0.95 * step(0.3, aBefore_));
            }   // 內褲右緣以外、落在大腿上的黑線／殘影一律不顯示（大腿上不該有內褲的線）   // 順著 M 腿的輪廓裁切
            {   // 窄帶範圍內（開口左緣 hb ~ 右緣 redge0_）如果還是透出腿的膚色（沒有布），直接補一層白色布
                float inStrip_ = smoothstep(hb, hb + 3.0, px.x) * (1.0 - smoothstep(redge0_ - 4.0, redge0_, px.x));
                float fillW_ = cover_ * (1.0 - upK_) * inStrip_ * (1.0 - c.a) * la_;
                c.rgb = mix(c.rgb, clothcol_, fillW_ / max(c.a + fillW_, 0.001));
                c.a = max(c.a, fillW_);
            }
        }
    }
    {   // 夾角中間那條黑線（弧線）原本畫到半途就停了：順著原本的方向往上補一小段，接到內褲上緣（撥開時才畫；這一帶上端幾乎不被拉動，位置固定）
        vec2 pj5_ = UV * vec2(2000.0, 3200.0);
        vec2 ab5_ = ext_b - ext_a;
        float tt5_ = clamp(dot(pj5_ - ext_a, ab5_) / max(1.0, dot(ab5_, ab5_)), 0.0, 1.0);
        float dist5_ = length(pj5_ - (ext_a + ab5_ * tt5_));
        float k5_ = (1.0 - smoothstep(0.9, 2.2, dist5_)) * (1.0 - 0.5 * tt5_) * smoothstep(1.0, 20.0, aside) * step(0.3, c.a) * ext_k * smoothstep(0.3, 0.8, hug_k);
        c.rgb = mix(c.rgb, vec3(0.50, 0.36, 0.38), k5_);
    }
    {   // 右邊那條黑線（窄帶右緣的線）原本只畫到夾角就停了：順著大腿的輪廓往上補畫，一直畫到內褲上緣，長度跟左邊那條弧線差不多
        vec2 pj6_ = UV * vec2(2000.0, 3200.0);
        float xe6_ = rline.x + rline.y * pj6_.y - 1.6;   // 大腿輪廓（x＝a＋b×y）往內褲這一側偏 1.6px
        float yk6_ = smoothstep(rline_y.x - 3.0, rline_y.x + 6.0, pj6_.y) * (1.0 - smoothstep(rline_y.y - 4.0, rline_y.y, pj6_.y));
        float k6_ = (1.0 - smoothstep(0.8, 2.2, abs(pj6_.x - xe6_))) * yk6_ * smoothstep(1.0, 20.0, aside) * step(0.3, c.a) * 0.0;   // 舊的固定座標版已停用（改用腿遮罩左緣）
        c.rgb = mix(c.rgb, vec3(0.30, 0.12, 0.12), k6_ * 0.9);
    }
    {   // 窄帶右邊那條黑線（下面一段一段的虛線）往上延伸：沿著內褲右緣（redge0_）往上畫，一直接到右上角腿的深紅輪廓線（約 y 1384）；中間原本透出膚色的地方也用這條線連起來
        float ytop_ = rv_y.x, ybot_ = rv_y.y;
        float yk8_ = smoothstep(ytop_ - 4.0, ytop_ + 3.0, px.y) * (1.0 - smoothstep(ybot_ - 6.0, ybot_ + 4.0, px.y));
        float xl8_ = redge0_ - rv_in - rv_slope * (ybot_ - px.y);   // 內褲右緣（往上會往右偏）再扣掉 rv_slope：往上改成往左（向內）折，跟左邊那條弧線一樣
        float k8_ = (1.0 - smoothstep(0.8, 2.3, abs(px.x - xl8_))) * yk8_ * smoothstep(1.0, 20.0, aside) * rv_k * smoothstep(0.3, 0.8, hug_k);
        c.rgb = mix(c.rgb, vec3(0.42, 0.25, 0.27), k8_ * 0.95);
        c.a = max(c.a, k8_ * 0.95);
    }
    {   // 窄帶右邊那條線往上加長：畫成橢圓弧，位置＝「內褲右緣 redge0_」往內 e(ys_) px，所以會跟內褲的位移（拉開量、插入程度）一起走；再用腿的遮罩裁切（不會畫到大腿上）。
        // 最後位置（拉開 150、插入）：畫布 (1116,1414) 沿橢圓弧彎到 (1100,1370)。e 是這條弧相對內褲右緣的距離，對應內褲圖來源列 ys_：
        float u_ = clamp((1416.7 - ys_) / 41.0, 0.0, 1.0);
        float eS_ = u_ < 0.25 ? mix(4.8, 10.4, u_ / 0.25)
                  : u_ < 0.50 ? mix(10.4, 18.3, (u_ - 0.25) / 0.25)
                  : u_ < 0.75 ? mix(18.3, 29.5, (u_ - 0.50) / 0.25)
                              : mix(29.5, 49.2, (u_ - 0.75) / 0.25);
        float sd_ = abs(px.x - (redge0_ - eS_)) / 1.3;   // 弧線斜的，換成垂直線距的近似
        float syk_ = smoothstep(1374.7, 1376.7, ys_) * (1.0 - smoothstep(1415.7, 1417.7, ys_));
        vec2 lq9_ = (lg_x * (UV.x * cl_size.x) + lg_y * (UV.y * cl_size.y) + lg_o) / vec2(2000.0, 3200.0);
        float legA_ = (lg_on > 0.5 && lq9_.x >= 0.0 && lq9_.x <= 1.0 && lq9_.y >= 0.0 && lq9_.y <= 1.0) ? texture(leg_tex, lq9_).a : 0.0;
        float sk_ = (1.0 - smoothstep(0.5, 1.5, sd_)) * syk_ * smoothstep(1.0, 20.0, aside) * seg_k * smoothstep(0.3, 0.8, hug_k) * (1.0 - smoothstep(seg_fade, 1.0, u_));   // 上端淡化（只顯示這條線，不被腿裁切）
        c.rgb = mix(c.rgb, vec3(0.45, 0.28, 0.30), sk_ * 0.9);
        c.a = max(c.a, sk_);
    }
    COLOR = c * vc;
}
";
    void SetMPantyLegClip(bool on)
    {
        if (!Layers.TryGetValue("m腿/內褲", out var l) || l is not TextureRect mp) return;
        if (_mPantyMat == null) _mPantyMat = new ShaderMaterial { Shader = new Shader { Code = MPantyShaderCode } };
        if (mp.Material != _mPantyMat) mp.Material = _mPantyMat;
        _mPantyMat.SetShaderParameter("lg_on", on ? 1f : 0f);
        if (!on || _legMaskVP == null) return;
        Transform2D T = mp.GetTransform();
        _mPantyMat.SetShaderParameter("leg_tex", (Texture2D)_legMaskVP.GetTexture());
        _mPantyMat.SetShaderParameter("lg_x", T.X);
        _mPantyMat.SetShaderParameter("lg_y", T.Y);
        _mPantyMat.SetShaderParameter("lg_o", T.Origin);
        _mPantyMat.SetShaderParameter("cl_size", mp.Size);
    }
    // ── 抽插時衣服跟著上下動 ──
    [Export(PropertyHint.Range, "0,3,0.05")] public float ClothThrustMoveGain = 0.6f;     // 衣服跟插入上下移動的比例（0＝不動）
    [Export(PropertyHint.Range, "0.2,4,0.05")] public float ClothThrustRadius = 1.2f;    // 影響範圍（插入內容大小的倍數）
    void UpdateClothThrust()
    {
        float dy = 0f; Vector2 c = new Vector2(-10f, -10f), r = new Vector2(0.05f, 0.05f);
        if (ClothThrustMoveGain > 0f && Layers.TryGetValue("插入", out var l) && l != null && l.IsVisibleInTree() && l.Modulate.A > 0.02f
            && _layerBasePos.TryGetValue("插入", out var bp) && LayerContentRect("插入") is Rect2 cr && l.Size.X > 0f && l.Size.Y > 0f)
        {
            Vector2 off = l.Position - bp;
            dy = off.Y / 3200f * ClothThrustMoveGain * l.Modulate.A;
            c = cr.GetCenter() / l.Size;
            r = new Vector2(Mathf.Max(0.005f, cr.Size.X * 0.5f * ClothThrustRadius / l.Size.X), Mathf.Max(0.005f, cr.Size.Y * 0.5f * ClothThrustRadius / l.Size.Y));
        }
        _clothMat.SetShaderParameter("mv_c", c);
        _clothMat.SetShaderParameter("mv_r", r);
        _clothMat.SetShaderParameter("mv_dy", dy);
    }

    // ── 肚子的線條也套在衣服上同個位置（衣服範圍內才顯示）──
    [Export] public bool BellyLineOnCloth = true;
    [Export(PropertyHint.Range, "0,1,0.01")] public float BellyLineOnClothAlpha = 1f;
    TextureRect _bellyOnCloth; ShaderMaterial _bellyOnClothMat;
    const string BellyOnClothCode = @"shader_type canvas_item;
uniform float line_lo = 0.2;    // 線條判定：R-G 低於這個＝完全不顯示
uniform float line_hi = 0.4;    // 高於這個＝完全顯示
uniform sampler2D cloth_tex : filter_linear;
uniform vec2 bx = vec2(1.0, 0.0);
uniform vec2 by = vec2(0.0, 1.0);
uniform vec2 bo = vec2(0.0);
uniform vec2 csize = vec2(2000.0, 3200.0);
uniform vec2 bsize = vec2(2000.0, 3200.0);
varying vec4 vcol;
void vertex() { vcol = COLOR; }
void fragment() {
    vec4 c = texture(TEXTURE, UV) * vcol;
    vec2 pj_ = UV * bsize;
    vec2 q = (bx * pj_.x + by * pj_.y + bo) / csize;
    float a = (q.x < 0.0 || q.x > 1.0 || q.y < 0.0 || q.y > 1.0) ? 0.0 : texture(cloth_tex, q).a;
    c.a *= a;
    // 只保留肚子圖的紅色外框線（偏紅的像素：R-G 大）；膚色的塊面不要疊到衣服上
    vec4 t_ = texture(TEXTURE, UV);
    c.a *= smoothstep(line_lo, line_hi, t_.r - t_.g);
    COLOR = c;
}";
    void UpdateBellyOnCloth()
    {
        bool want = ClothOnTop && BellyLineOnCloth && Layers.TryGetValue("肚子", out var src) && src is TextureRect st && st.Texture != null
                    && Layers.TryGetValue("衣服", out var cl) && cl is TextureRect ct && ct.Texture != null && ct.GetParent() == _charRoot;
        if (!want) { if (_bellyOnCloth != null && IsInstanceValid(_bellyOnCloth)) _bellyOnCloth.Visible = false; return; }
        var s2 = (TextureRect)Layers["肚子"]; var c2 = (TextureRect)Layers["衣服"];
        if (_bellyOnCloth == null || !IsInstanceValid(_bellyOnCloth))
        {
            _bellyOnClothMat = new ShaderMaterial { Shader = new Shader { Code = BellyOnClothCode } };
            _bellyOnCloth = new TextureRect { Name = "BellyLineOnCloth", MouseFilter = Control.MouseFilterEnum.Ignore, Material = _bellyOnClothMat };
            _charRoot.AddChild(_bellyOnCloth);
        }
        { int ci = c2.GetIndex(), oi = _bellyOnCloth.GetIndex(); if (oi != ci + 1) _charRoot.MoveChild(_bellyOnCloth, oi < ci ? ci : ci + 1); }
        var d = _bellyOnCloth;
        d.ExpandMode = s2.ExpandMode; d.StretchMode = s2.StretchMode;
        d.Texture = s2.Texture; d.Size = s2.Size; d.Position = s2.Position;
        d.PivotOffset = s2.PivotOffset; d.Scale = s2.Scale; d.Rotation = s2.Rotation;
        d.Modulate = new Color(s2.Modulate.R, s2.Modulate.G, s2.Modulate.B, s2.Modulate.A * BellyLineOnClothAlpha);
        d.SelfModulate = s2.SelfModulate;
        d.Visible = s2.Visible;
        Transform2D T = c2.GetTransform().AffineInverse() * d.GetTransform();
        _bellyOnClothMat.SetShaderParameter("cloth_tex", c2.Texture);
        _bellyOnClothMat.SetShaderParameter("bx", T.X);
        _bellyOnClothMat.SetShaderParameter("by", T.Y);
        _bellyOnClothMat.SetShaderParameter("bo", T.Origin);
        _bellyOnClothMat.SetShaderParameter("csize", c2.Size);
        _bellyOnClothMat.SetShaderParameter("bsize", d.Size);
    }

    void UpdateClothTop()
    {
        if (ClothOnTop && _clothTopInit && _duvet != null && IsInstanceValid(_duvet) && _duvet.GetParent() == _charRoot
            && Layers.TryGetValue("衣服", out var cth) && cth != null && cth.GetParent() == _charRoot && _duvet.GetIndex() < cth.GetIndex())
            _clothTopInit = false;   // 被子後來才建立／被移動，重新排一次
        ApplyClothOnTop();
        UpdateMPantyFollow();   // 內褲先移到這一幀跟著抽插／呼吸的最終位置，下面的遮罩和撥開才不會差一步（抽插時裁切才會跟著動）
        UpdateClothLegMask();
        UpdateClothThrust();
        UpdateBellyOnCloth();
        UpdateMLegPantyAside((float)GetProcessDeltaTime());   // M 腿內褲撥開／裁切（MLegPantyAside.cs）：要在內褲位置定案之後
        UpdateBowLayers();
    }

    // ── M 腿內褲：跟著 M 腿的呼吸／搖擺／插入推擠一起動（腿1／腿2 的位移取平均，兩腿張開角度變大時內褲左右微微撐寬）──
    [Export(PropertyHint.Range, "0,3,0.05")] public float MPantyLegFollow = 1.2f;       // 跟著兩條腿平均位移的倍率（1＝剛好一樣，再多一點更貼）
    [Export(PropertyHint.Range, "0,0.03,0.0005")] public float MPantySpreadScale = 0.006f;   // 兩腿夾角每多 1 度，內褲左右撐寬多少（比例）
    void UpdateMPantyFollow()
    {
        if (!Layers.TryGetValue("m腿/內褲", out var mp) || mp == null || !mp.Visible) return;
        if (!Layers.TryGetValue("m腿/腿1", out var l1) || l1 == null || !Layers.TryGetValue("m腿/腿2", out var l2) || l2 == null) return;
        if (!_layerBasePos.TryGetValue("m腿/內褲", out var bp) || !_layerBasePos.TryGetValue("m腿/腿1", out var b1) || !_layerBasePos.TryGetValue("m腿/腿2", out var b2)) return;
        Vector2 delta = ((l1.Position - b1) + (l2.Position - b2)) * 0.5f;
        Vector2 thrust = MLegThrustOffset.TryGetValue("m腿/小穴", out var to) ? to : Vector2.Zero;   // 插入推擠：跟小穴一起往上
        mp.Position = bp + delta * MPantyLegFollow + thrust * (1f - MPantyThrustCut);   // 抽插時內褲往上的幅度減少（改成往右撐開，見 MLegPantyAside.cs）
        float spread = Mathf.Abs(l1.RotationDegrees - l2.RotationDegrees);
        mp.PivotOffset = mp.Size * 0.5f;
        mp.Scale = new Vector2(1f + spread * MPantySpreadScale, 1f);
    }

    // ── 蝴蝶結（內褲／衣服各一個）：不變形，只跟著所屬衣物「蝴蝶結原本那一點」移動 ──
    // 圖檔是整張畫布，但蝴蝶結畫的位置跟原本衣物上的不一樣，所以先用 Offset 平移到原位，
    // 再加上衣物變形後該點被推到哪裡的位移（不套 shader，所以蝴蝶結本身不變形）。
    [Export] public Vector2 BowPantyOffsetPx = new Vector2(-4f, 44f);     // 蝴蝶結.png → 內褲原蝴蝶結位置（畫布 px）
    [Export] public Vector2 BowClothOffsetPx = new Vector2(-26f, -390f);  // 蝴蝶結2.png → 衣服原蝴蝶結位置（畫布 px）
    static readonly Vector2 BowPantyAnchorPx = new Vector2(988f, 1439f);  // 內褲原蝴蝶結中心（畫布 px）
    static readonly Vector2 BowClothAnchorPx = new Vector2(965f, 1006f);  // 衣服原蝴蝶結中心（畫布 px）

    void UpdateBowLayers()
    {
        if (Layers.TryGetValue("蝴蝶結", out var bow) && bow != null && Layers.TryGetValue("內褲", out var panty) && panty != null)
        {
            bow.PivotOffset = panty.PivotOffset; bow.Scale = panty.Scale; bow.Rotation = panty.Rotation;
            bow.Modulate = panty.Modulate;
            bool shown = panty.Visible && !_pantyGone;
            Vector2 d = Vector2.Zero;
            if (_pantyMat != null)
            {   // 內褲 shader 的主要位移：整件下移（down_y）＋上緣下拉壓縮（pull_s）＋變寬（widen）
                float topV = PantyF("top_v", 0f), botV = PantyF("bot_v", 1f);
                float downY = PantyF("down_y", 0f), pullS = PantyF("pull_s", 0f);
                float fixCut = PantyF("fix_cut", -1f), fixW = PantyF("fix_w", 1f);
                float widen = PantyF("widen", 0f), cxU = PantyF("cx_u", 0.4865f);
                float v0 = BowPantyAnchorPx.Y / 3200f, k = fixCut >= 0f && v0 >= fixCut ? fixW : 1f;
                float h = Mathf.Max(0.0001f, botV - topV), ps = pullS * k;
                float vy = v0;
                if (ps > 0f) { vy = topV + ps + (v0 - topV) * (h - ps) / h; if (vy < topV + ps) shown = false; }
                vy += downY * k;
                d = new Vector2((BowPantyAnchorPx.X - cxU * 2000f) * widen, (vy - v0) * 3200f);
            }
            bow.Visible = shown;
            bow.Position = panty.Position + BowPantyOffsetPx + d;
        }
        if (Layers.TryGetValue("蝴蝶結2", out var bow2) && bow2 != null && Layers.TryGetValue("衣服", out var cloth) && cloth != null)
        {
            bow2.Modulate = cloth.Modulate; bow2.Visible = cloth.Visible;
            var px = new Vector2(2000f, 3200f);
            // 左右各取蝴蝶結旁邊一點，量出兩邊被推到哪：中心位移＝下移加成，兩邊高低差＝歪斜角度
            Vector2 cNow = BowClothAnchorPx + ClothBowShiftPx(BowClothAnchorPx);
            Vector2 lNow = BowClothAnchorPx + new Vector2(-BowTiltSpanPx, 0f); lNow += ClothBowShiftPx(lNow);
            Vector2 rNow = BowClothAnchorPx + new Vector2(BowTiltSpanPx, 0f); rNow += ClothBowShiftPx(rNow);
            Vector2 shift = cNow - BowClothAnchorPx;
            if (shift.Y > 0f) shift.Y *= BowClothDownGain;   // 肩帶拉下去時，蝴蝶結往下多走一點
            float tilt = Mathf.Clamp(Mathf.Atan2(rNow.Y - lNow.Y, rNow.X - lNow.X) * BowTiltGain, -Mathf.DegToRad(BowTiltMaxDeg), Mathf.DegToRad(BowTiltMaxDeg));
            // 以蝴蝶結中心為軸轉：中心在衣服座標的位置（含呼吸縮放）算出來，再把貼圖的蝴蝶結中心對上去
            Vector2 cLocal = BowClothAnchorPx + shift;
            Vector2 cScreen = cloth.Position + cloth.PivotOffset + (cLocal - cloth.PivotOffset) * cloth.Scale;
            bow2.PivotOffset = BowClothFileCenterPx; bow2.Scale = cloth.Scale; bow2.Rotation = cloth.Rotation + tilt;
            bow2.Position = cScreen - BowClothFileCenterPx;
        }
    }

    [Export] public float BowClothDownGain = 1.2f;     // 肩帶拉下時蝴蝶結向下位移的倍率
    [Export] public float BowTiltGain = 1.5f;          // 左右高低差換成歪斜角度的倍率
    [Export] public float BowTiltMaxDeg = 35f;         // 最多歪幾度
    [Export] public float BowTiltSpanPx = 40f;         // 量歪斜用的左右取樣距離（畫布 px）
    static readonly Vector2 BowClothFileCenterPx = new Vector2(991f, 1395.5f);   // 蝴蝶結2.png 裡蝴蝶結的中心

    // 沒被設定過的 shader 參數 GetShaderParameter 會回傳 Nil（轉成數值就是 0），要用 shader 裡寫的預設值
    float PantyF(string name, float def) { var v = _pantyMat.GetShaderParameter(name); return v.VariantType == Variant.Type.Nil ? def : v.AsSingle(); }
    float ClothF(string name, float def) { var v = _clothMat.GetShaderParameter(name); return v.VariantType == Variant.Type.Nil ? def : v.AsSingle(); }
    Vector2 ClothV2(string name, Vector2 def) { var v = _clothMat.GetShaderParameter(name); return v.VariantType == Variant.Type.Nil ? def : v.AsVector2(); }

    /// <summary>衣服原圖上某一點（畫布 px）在目前畫面上被推到哪：回傳位移（畫布 px）。先反推呼吸上提／收腰，再反推上半部拉扯。</summary>
    Vector2 ClothBowShiftPx(Vector2 anchorPx)
    {
        var px = new Vector2(2000f, 3200f);
        Vector2 anchor0 = anchorPx / px, uv0 = anchor0;
        if (_clothMat != null)
        {   // 摸胸：shader 最後一步 chest() 把胸口附近的衣服推開／放大；先反推這一點被推到哪
            Vector2 cr = ClothV2("chest_r", new Vector2(260f, 230f));
            float inner = ClothF("chest_inner", 0.35f);
            Vector2 c1 = ClothV2("chest1_c", new Vector2(0.4885f, 0.3119f)), o1 = ClothV2("chest1_off", Vector2.Zero);
            Vector2 c2 = ClothV2("chest2_c", new Vector2(0.4365f, 0.3088f)), o2 = ClothV2("chest2_off", Vector2.Zero);
            float s1 = ClothF("chest1_s", 1f), s2 = ClothF("chest2_s", 1f);
            Vector2 Chest(Vector2 uv, Vector2 c, Vector2 off, float s)
            {
                float w = 1f - SStep(inner, 1f, ((uv - c) * px / cr).Length());
                uv -= w * off;
                return c + (uv - c) / (1f + w * (s - 1f));
            }
            for (int i = 0; i < 8; i++) uv0 -= Chest(Chest(uv0, c1, o1, s1), c2, o2, s2) - anchor0;   // 找 uv0 使 chest2(chest1(uv0)) = 原位
        }
        Vector2 base0 = uv0;
        if (_clothMat != null)
        {   // 身體呼吸／撐開的上提與收腰（shader 最後一步：uv.y += lift…、x 依 expand 收縮）
            float liftTop = ClothF("lift_top_v", 0f), liftBot = ClothF("lift_bottom_v", 0f);
            float neck = ClothF("lift_neck_v", 0.12f), navel = ClothF("lift_navel_v", 0.36f);
            float botEnd = ClothF("lift_bottom_end_v", 0.55f), expand = ClothF("expand_amount", 0f);
            float baseR = ClothF("base_ratio", 0f), cu = ClothF("center_u", 0.5225f);
            float ts = ClothF("taper_start_v", 0.3716f), te = ClothF("taper_end_v", 0.5472f);
            float py = uv0.Y;
            for (int i = 0; i < 3; i++) py = base0.Y - liftTop * SStep(neck, navel, py) - liftBot * SStep(navel, botEnd, py);
            float shrink = 1f - Mathf.Lerp(baseR, 1f, Mathf.Clamp((py - ts) / Mathf.Max(0.001f, te - ts), 0f, 1f)) * expand;
            uv0 = new Vector2(cu + (base0.X - cu) / Mathf.Max(0.05f, shrink), py);
        }
        Vector2 x = uv0;
        // ClothTopInv＝畫面→原圖；反過來找原圖上這一點現在在畫面哪裡：Y 用二分法（壓縮很大時單純迭代會過衝），X 再微調
        for (int it = 0; it < 4; it++)
        {
            float lo = uv0.Y - 0.1f, hi = uv0.Y + 0.4f;
            for (int b = 0; b < 18; b++)
            {
                float mid = (lo + hi) * 0.5f;
                if (ClothTopInv(new Vector2(x.X, mid)).Y < uv0.Y) lo = mid; else hi = mid;
            }
            x.Y = (lo + hi) * 0.5f;
            x.X -= ClothTopInv(x).X - uv0.X;
        }
        return (x - anchor0) * px;
    }

    // M 腿＋內褲還穿著時：大腿（腿1_M／腿2_M）內側深紅線後面那一圈「被刷淡」的膚色會蓋在內褲上，
    // 換成預先裁掉那一塊的版本（腿1_M_cut／腿2_M_cut，只裁內褲範圍內、深紅線內側的淡化部分，其他不動）；
    // 內褲脫掉或關掉 M 腿就換回原圖。
    Texture2D _thighTexCut1, _thighTexCut2, _thighTexOrig1, _thighTexOrig2;
    void UpdateMLegPantyThighCut(bool pantyOn)
    {
        SwapThighTex("m腿/腿1", "Sprites/腿1_M", "Sprites/腿1_M_cut", ref _thighTexOrig1, ref _thighTexCut1, pantyOn);
        SwapThighTex("m腿/腿2", "Sprites/腿2_M", "Sprites/腿2_M_cut", ref _thighTexOrig2, ref _thighTexCut2, pantyOn);
    }
    bool _thighTexTried;
    void SwapThighTex(string layer, string origPath, string cutPath, ref Texture2D orig, ref Texture2D cut, bool pantyOn)
    {
        if (!Layers.TryGetValue(layer, out var c) || c is not TextureRect tr) return;
        if (!pantyOn && tr.Texture != null && orig == null) orig = tr.Texture as Texture2D;   // 直接記住原圖，不用重載
        orig ??= ResourceHelper.LoadTexAny(origPath);
        if (pantyOn && cut == null && !_thighTexTried) cut = ResourceHelper.LoadTexAny(cutPath);   // 載不到（還沒匯入）只試一次，不要每幀印警告
        if (pantyOn && layer == "m腿/腿2") _thighTexTried = true;
        var want = pantyOn && cut != null ? cut : orig;
        if (want != null && tr.Texture != want) tr.Texture = want;
    }

    bool _pantyHiddenByMLeg;
    void UpdateClothBump()
    {
        UpdateInsertMask();
        {   // M 腿開啟：原本的內褲隱藏（之後會換新的內褲圖）；關掉 M 腿再顯示回來
            if (Layers.TryGetValue("內褲", out var pn_) && pn_ != null)
            {
                if (_mLegOpen && pn_.Visible) { pn_.Visible = false; _pantyHiddenByMLeg = true; }
                else if (!_mLegOpen && _pantyHiddenByMLeg) { pn_.Visible = true; _pantyHiddenByMLeg = false; }
            }
        }
        // M 腿專用內褲：M 腿打開、而且內褲還沒脫才出現；脫了就不出現
        if (Layers.TryGetValue("m腿/內褲", out var mp_) && mp_ != null) mp_.Visible = _mLegOpen && !_pantyGone;
        UpdateMLegPantyThighCut(_mLegOpen && !_pantyGone);
        if (_clothMat == null) return;
        _clothMat.SetShaderParameter("bump_see", AllAgesLocked ? 0f : ClothBumpSeeThrough);
        BumpOne("肚子", 1, ClothBumpBelly);
        BumpOne("插入", 2, ClothBumpInsert);
    }

    void BumpOne(string layer, int slot, float k)
    {
        float sn = (_jijiPhase == 3 && _jijiStage >= 1 && !_s2Paused) ? Mathf.Clamp(_s2SpeedLv / Mathf.Max(S2SpeedLvMax, 0.01f), 0f, 1f) : 0f;
        float spd = 1f + ClothBumpSpeedGain * sn;   // 抽插越快，衣服下的晃動／凸起越強
        float kk = 0f; Vector2 c = new Vector2(-10f, -10f), r = new Vector2(0.05f, 0.05f);
        if (k > 0f && Layers.TryGetValue(layer, out var l) && l != null && l.IsVisibleInTree() && l.Modulate.A > 0.02f
            && _layerBasePos.TryGetValue(layer, out var bp) && LayerContentRect(layer) is Rect2 cr && l.Size.X > 0f && l.Size.Y > 0f)
        {
            Vector2 off = l.Position - bp;   // 圖層此刻相對原位的位移（跟著雞雞／抽插動）
            Vector2 ctr = cr.GetCenter() + off * ClothBumpFollow * spd;
            c = ctr / l.Size;
            r = new Vector2(Mathf.Max(0.005f, cr.Size.X * 0.5f * ClothBumpRadius / l.Size.X), Mathf.Max(0.005f, cr.Size.Y * 0.5f * ClothBumpRadius / l.Size.Y));
            kk = k * spd * l.Modulate.A;
        }
        _clothMat.SetShaderParameter("bump_c" + slot, c);
        _clothMat.SetShaderParameter("bump_r" + slot, r);
        _clothMat.SetShaderParameter("bump_k" + slot, kk);
    }
}
