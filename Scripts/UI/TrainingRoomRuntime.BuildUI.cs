using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// TrainingRoomRuntime — 場景建構：背景（雙層）、立繪分層、頂部 HUD、
/// 左上數值面板、精力/精液條、右側選單、底部操作按鈕。
/// 座標轉換沿用 MainMenuRuntime.ApplyPointAnchor / ApplyStretchAnchor
/// （已驗證可靠，避免直接用 SetAnchorsPreset 造成之後動畫時 rect 塌陷）。
/// 貼圖一律透過 ResourceHelper.LoadTexAny 載入，自動嘗試 png/jpg/jpeg/webp。
/// </summary>
public partial class TrainingRoomRuntime : Node
{
    void Build()
    {
        BuildBackground();
        BuildCharacter();
        // 第一天（R18、全年齡都一樣）進房間時被子是踢開的（不蓋被子）；第二天起全年齡一樣踢開、R18 蓋著。
        // （要在建被子之前設，淡入才會直接到位）
        bool firstDay_ = (DaySystem.Instance?.CurrentDay ?? 1) <= 1;
        DuvetState = (AllAgesLocked || firstDay_) ? 1 : 0;
        BuildDuvetLayer();
        BuildTopHUD();
        BuildStatsPanel();
        BuildStaminaSemenBars();
        BuildBottomButtons();
        BuildSideMenu();
        BuildHotspots();
        if (HotspotDotsHiddenAtStart) SetHotspotDotsHidden(true);
        ApplyBreastPivotOverrides();
        SetupHairBottomFlareShaders();
        SetupMLegDragStretchShaders();

        // 預設隱藏 m腿系（M腿模式打開才顯示）
        HideLayerGroup("m腿");
        HideLayerGroup("M腿");
        // 雞雞 + 男人肚子 預設隱藏，點擊熱點4才顯示
        SetLayer("雞雞", false);
        SetLayer("男人肚子", false);
        SetLayer("精液", false);
        foreach (var n in new[] { "肚子", "洞", "開上", "開1", "插入" }) SetLayer(n, false);   // 插入疊層：Jiji.cs 控制          // Unity 裡精液是雞雞的子物件 → 跟雞雞一起隱藏/顯示
        // 以下幾層在 Unity 場景裡預設是關閉的（m_IsActive: 0），Godot 之前全部顯示出來：
        SetLayer("妹妹正面", false);      // 這張圖的內容跟男人肚子相同，就是畫面上殘留的肚子
        SetLayer("後髮-", false);
        SetLayer("表情/高潮水", false);   // 高潮時由 TriggerClimax 打開

        // ArmIK 連結：場景裡原本沒有預先擺放 ArmIK / RightArmIK 節點（.tscn 是最小 stub），
        // 之前完全沒有建立這兩個控制器，導致熱點9/10（手肘拖動）沒有作用、
        // 也是「胸1/胸2/手1/手2」外觀對不上 Unity 的原因之一（缺少 IK 對這幾層的即時控制）。
        // 現在改為直接在程式碼建立，並套用 Unity 場景檔（TrainingRoom.unity）裡讀出的
        // 真實 Inspector 調校值（fileID 903285454=ArmIK, 903285455=RightArmIK）。
        if (ArmIKPath != null && !ArmIKPath.IsEmpty) ArmIK = GetNodeOrNull<ArmIK>(ArmIKPath);
        if (RightArmIKPath != null && !RightArmIKPath.IsEmpty) RightArmIK = GetNodeOrNull<ArmIK>(RightArmIKPath);
        if (ArmIK == null) ArmIK = BuildArmIK();
        if (RightArmIK == null) RightArmIK = BuildRightArmIK();
        BuildIKDebugDots();

        // 表情系統連結：同樣是 .tscn stub 沒有預先擺放節點的問題（跟 ArmIK 一樣），
        // 原本的 GetNodeOrNull("FaceExpressionController") 找不到任何東西，是永遠失敗的死碼。
        // 改為在程式碼中建立 FaceExpressionController / EyeCloseController / EyeFollowController /
        // IdleStateController 四個節點並直接注入依賴（見 TrainingRoomRuntime.Expression.cs）。
        BuildExpressionSystem();

        InitBreathLayers();
        InitClothLayers();
    }

    /// <summary>
    /// 衣物圖層初始對齊：衣服以身體頂端對齊（ClothOffsetY）、內褲以小穴底端對齊（PantyOffsetY）。
    /// 改的是 _layerBasePos，之後每幀以此為基準。
    /// </summary>
    Vector2 _clothBasePos, _pantyBasePos;   // 衣物圖層未加偏移的原始位置（畫布置中）

    void InitClothLayers()
    {
        if (Layers.TryGetValue("衣服", out var cloth) && cloth != null) _clothBasePos = cloth.Position;
        if (Layers.TryGetValue("內褲", out var panty) && panty != null) _pantyBasePos = panty.Position;
        SetupClothShader();   // 衣服貼身變形 shader（見 Cloth.cs）
        UpdateClothAlpha();   // 偏移＋不透明度每幀由這裡套用，所以 Play 模式下可即時調
    }

    /// <summary>每幀套用衣物不透明度（0~1），Play 模式下在 Inspector 改數值即時生效。</summary>
    void UpdateClothAlpha()
    {
        float clothA_ = AllAgesLocked ? 1f : ClothAlpha, pantyA_ = AllAgesLocked ? 1f : PantyAlpha;   // 全年齡：衣服／內褲完全不透明
        _layerBasePos["衣服"] = _clothBasePos + new Vector2(0f, ClothOffsetY);
        foreach (var pn in ClothPartLayers)
        {
            _layerBasePos[pn] = _clothBasePos + new Vector2(0f, ClothOffsetY);   // 帶子跟本體同一個位置
            if (Layers.TryGetValue(pn, out var part) && part != null) part.Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(clothA_, 0f, 1f));
        }
        _layerBasePos["內褲"] = _pantyBasePos + new Vector2(0f, PantyOffsetY);
        if (Layers.TryGetValue("衣服", out var cloth) && cloth != null)
            cloth.Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(clothA_, 0f, 1f));
        if (Layers.TryGetValue("內褲", out var panty) && panty != null)
            panty.Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(pantyA_, 0f, 1f));
    }

    // ── 左手 ArmIK（驅動 手臂2/手2 → 視覺上控制"胸2"）─────────────────
    // 數值來自 TrainingRoom.unity fileID 903285454（Unity Inspector 原始調校值）
    ArmIK BuildArmIK()
    {
        var ik = new ArmIK
        {
            Name = "ArmIK",
            ArmLayerName  = "手臂2",
            HandLayerName = "手2",
            ShoulderPivot = new Vector2(0.69502187f, 0.87475324f),
            HandPivot     = new Vector2(0.3813649f, 0.6612898f),
            ArmOffset     = new Vector2(-207.3f, -2.8f),
            ArmRotOffset  = 133.5f,
            HandOffset    = new Vector2(90f, 371.6f),
            HandRotOffset = 210.3f,
            ArmPosAdjust  = new Vector2(-10f, 0f),
            HandPosAdjust = new Vector2(1.5f, 5f),
            DragHandScale = 1.05f,
            DragHandScaleRight = 0.35f,
            DragHandScaleDown  = 0.35f,
            ReturnSpeed   = 10f,
            MirrorX       = false,
            FrozenBreathY = -1.2382683f,
            FreezeArmRotOffset  = 3f,
            FreezeHandPosOffset = new Vector2(15f, -10f),
            FreezeHandRotOffset = -2f,
            ArmRotPerUp    = -0.162f,
            ArmRotPerRight = -0.035f,
            ArmRotPerLeft  = -0.11f,
            HandRotPerUp    = 0.075f,
            HandRotPerDown  = 0f,
            HandRotPerRight = 0.1f,
            HandRotPerLeft  = 0.11f,
            DeadzoneX = 0f, DeadzoneY = 0f,
            UseDragProjection = false,
            DragReferenceDir  = new Vector2(-1f, 0.5f),
            ArmLiftEnable = true,
            ArmLiftStartDist = 100f,
            ArmLiftFullDist  = 120f,
            ArmLiftMaxRot    = -1f,
            ArmLiftCurve     = 0f,
            ArmLiftProjectAngle = 0f,
            ArmPreXRotBoost      = 0.65f,
            ArmPreXBoostFullDist = 182f,
            HandDragBoundsEnabled = true,
            HandDragBoundsMin = new Vector2(-250f, 0f),
            HandDragBoundsMax = new Vector2(100f, 250f),
            HandLiftEnable      = true,
            HandLiftStartDist   = 0f,
            HandLiftFullDist    = 186f,
            HandLiftMaxRot      = -27f,
            HandLiftProjectAngle= 0f,
            HandLiftXFullDist   = 280f,

            // 手腕貼合：兩側都試過會讓手飛走，先整個關掉，退回單純呼吸位移版本（不會飛走，
            // 但也還沒有自動貼合手腕）。之後要重新嘗試，再開 HandFollowWrist 並重新給
            // ArmElbow/HandJoint。Unity 場景檔裡這個欄位本身也是 0（沒開），所以退回關閉
            // 反而跟 Unity 一致。
            HandFollowWrist = false,

            // YOnlyBoost：Unity 場景檔 fileID 903285454（左手 ArmIK）這組是關閉的（之前誤填成右手的值）
            YOnlyBoostEnable      = false,
            YOnlyBoostArmMaxRot   = 0f,
            YOnlyBoostHandMaxRot  = 0f,
            YOnlyBoostYFullDist   = 200f,
            YOnlyBoostXFullDist   = 200f,
            BreathSuppress        = 0.516f,
        };
        AddChild(ik);
        return ik;
    }

    // ── 右手 RightArmIK（驅動 手臂1/手1 → 視覺上控制"胸1"）───────────
    // 數值來自 TrainingRoom.unity fileID 903285455（Unity Inspector 原始調校值）
    ArmIK BuildRightArmIK()
    {
        var ik = new RightArmIK
        {
            Name = "RightArmIK",
            ArmLayerName  = "手臂1",
            HandLayerName = "手1",
            ShoulderPivot = new Vector2(0.32f, 0.9f),
            HandPivot     = new Vector2(0.7f, 0.6f),
            ArmOffset     = Vector2.Zero,
            ArmRotOffset  = 0f,
            HandOffset    = Vector2.Zero,
            HandRotOffset = 0f,
            ArmPosAdjust  = new Vector2(-2f, 0f),
            HandPosAdjust = new Vector2(15f, -10f),   // 右手（手1）待機初始位置：X 正＝右、Y 負＝下；原本是 (10, 0)
            DragHandScale = 0.92f,
            DragHandScaleRight = 0.84f,
            DragHandScaleDown  = 0f,
            ReturnSpeed   = 10f,
            MirrorX       = false,
            FrozenBreathY = -0.848994f,
            FreezeArmRotOffset  = 6f,
            FreezeHandPosOffset = new Vector2(60f, 0f),
            FreezeHandRotOffset = -10f,
            ArmRotPerUp    = 0.1f,
            ArmRotPerRight = 0.11f,
            ArmRotPerLeft  = 0.13f,
            HandRotPerUp    = -0.09f,
            HandRotPerDown  = 0f,
            HandRotPerRight = 0.07f,
            HandRotPerLeft  = 0.28f,
            DeadzoneX = 0f, DeadzoneY = 0f,
            UseDragProjection = false,
            DragReferenceDir  = new Vector2(0f, 0.5f),
            ArmLiftEnable = true,
            ArmLiftStartDist = 100f,
            ArmLiftFullDist  = 120f,
            ArmLiftMaxRot    = 5f,
            ArmLiftCurve     = 0.5f,
            ArmLiftProjectAngle = 0f,
            ArmPreXRotBoost      = 0.1f,
            ArmPreXBoostFullDist = 200f,
            HandDragBoundsEnabled = true,
            HandDragBoundsMin = new Vector2(-35f, 0f),
            HandDragBoundsMax = new Vector2(240f, 220f),
            HandLiftEnable      = true,
            HandLiftStartDist   = 0f,
            HandLiftFullDist    = 0f,
            HandLiftMaxRot      = 3f,
            HandLiftProjectAngle= 61f,
            HandLiftXFullDist   = 414f,

            // YOnlyBoost：Unity 場景檔 fileID 903285455（右手 RightArmIK）原值
            YOnlyBoostEnable      = true,
            YOnlyBoostArmMaxRot   = 28f,
            YOnlyBoostHandMaxRot  = 28f,
            YOnlyBoostYFullDist   = 220f,
            YOnlyBoostXFullDist   = 240f,
            BreathSuppress        = 0.85f,

            // 手腕貼合：手1 這側從矩形幾何反推出來的 ArmElbow/HandJoint 實測會讓手飛到很奇怪的位置
            // （可能是手臂1 選錯裁切矩形，或手1 的裁切矩形其實不是「攤平畫布絕對座標」而是別的
            // 參考系，還沒查出精確原因），先關閉、退回原本單純呼吸位移的手1/手臂1，
            // 避免飛出去的視覺災難比沒對齊更糟。等有機會實際跑起來看畫面再重新校正這兩個值。
            HandFollowWrist = false,
        };
        AddChild(ik);
        return ik;
    }

    // ── 圖層群組顯示 / 隱藏 ────────────────────────────────────────
    public void HideLayerGroup(string prefix)
    {
        foreach (var kv in Layers) if (kv.Key.StartsWith(prefix)) kv.Value.Visible = false;
    }
    public void ShowLayerGroup(string prefix)
    {
        foreach (var kv in Layers) if (kv.Key.StartsWith(prefix)) kv.Value.Visible = true;
    }

    /// <summary>把某個圖層群組（用 key 前綴分組，例如「腿」「m腿」）全部設成同一個透明度，
    /// 供 M 腿模式切換做交叉淡出/淡入用（見 Interaction.cs 的 ToggleMLegModeAsync）。
    /// forceVisible=true 時順便把 Visible 打開——淡入前如果圖層還是 Visible=false，
    /// 只改 Modulate.A 也看不到任何東西，要先讓它可見才能淡進來。</summary>
    public void SetGroupAlpha(string prefix, float alpha, bool forceVisible = false)
    {
        foreach (var kv in Layers)
        {
            if (!kv.Key.StartsWith(prefix) || kv.Value == null) continue;
            if (forceVisible) kv.Value.Visible = true;
            var c = kv.Value.Modulate;
            c.A = alpha;
            kv.Value.Modulate = c;
        }
    }
    public void SetLayer(string layerName, bool active)
    {
        if (Layers.TryGetValue(layerName, out var c) && c != null) c.Visible = active;
        else GD.PushWarning($"[TrainingRoom] 找不到圖層：{layerName}");
    }

    // 目前所有跟「嘴巴表情」有關的圖層 key（各表情會依需要顯示/隱藏其中一個）。
    // 用來讓 DEV 面板的「換嘴巴」預覽功能找到「目前正顯示中的那一個」當作替換對象。
    /// <summary>套用「胸1」「胸2」的觸摸動畫專用樞紐點（BreastPivotOverrides，見 Fields.cs），
    /// 蓋掉 BuildCharacter() 預設給每個圖層的整張畫布中心 pivot。只在建構時呼叫一次，
    /// 靜止狀態下改 PivotOffset 不影響視覺位置，之後 AnimateHandTouch() 才會用到這個樞紐點。</summary>
    void ApplyBreastPivotOverrides()
    {
        foreach (var kv in BreastPivotOverrides)
        {
            if (Layers.TryGetValue(kv.Key, out var c) && c != null)
                c.PivotOffset = kv.Value;
        }
    }

    /// <summary>幫「後髮1/2/3」「前髮」四層各自掛一個 ShaderMaterial，做呼吸時尾端左右擴張的
    /// 局部形變（見 Fields.cs 的 HairBottomFlareRegions 註解）。只在建構時呼叫一次，之後
    /// 每一幀由 UpdateHairLayers()（Effects.cs）更新 shader 的 expand_amount 參數即可。
    /// 改用 UV（貼圖正規化座標）而不是 VERTEX 像素座標，避免受 Control 的 Size/Scale/
    /// Position 影響（之前用 VERTEX 版本使用者回報「只往同一個方向偏移」）。</summary>
    void SetupHairBottomFlareShaders()
    {
        if (_hairFlareShader == null)
        {
            // 改用「片段（fragment）」取樣位移，不用頂點（vertex）位移——原本的 vertex 版本
            // 兩次都被使用者回報「只往同一個方向偏移，沒有左右對稱擴張的感覺」，一直抓不到
            // 是哪裡的座標系假設錯了。fragment 版本改成反過來想：不去移動幾何頂點，而是在畫
            // 每個像素時，越靠近下緣就往「中心線」方向取樣越多，畫面上視覺效果一樣是「內容被
            // 往外撐開」，但完全不依賴 VERTEX/UV 在 Control 本地座標系的假設是否成立
            // ——UV 在 fragment shader 裡保證就是貼圖取樣座標（0~1），不會被其他因素影響，
            // 這是比 vertex 版本更可靠的寫法。
            //
            // 使用者確認「有了那個感覺」，但反應不要只有髮尾（taper 區間）在動，其他地方
            // （髮根／上半段）也要跟著動一點，只是髮尾動最多。原本 taper 區間以外 t=0，
            // 完全不受 expand_amount 影響；這裡加一個 base_ratio，把 t 的下限從 0 拉高到
            // base_ratio，讓上半段也套用「base_ratio 份」的擴張量，taper 區間內則照原本邏輯
            // 線性拉到滿（t=1），整層看起來是「一起呼吸擴張，越下面擺得越開」。
            _hairFlareShader = new Shader
            {
                Code = @"
shader_type canvas_item;

uniform float expand_amount = 0.0;
uniform float center_u = 0.5;
uniform float taper_start_v = 0.0;
uniform float taper_end_v = 1.0;
uniform float base_ratio = 0.0;
// 身體上提漸層（UV 單位，預設全 0＝不影響）：
//   脖子(lift_neck_v)→肚臍(lift_navel_v) 之間，額外上提量由 0 漸增到 lift_top_v（肚臍以下維持 lift_top_v）
//   肚臍→底部(lift_bottom_end_v) 之間，再額外漸增 lift_bottom_v
uniform float lift_top_v = 0.0;
uniform float lift_bottom_v = 0.0;
uniform float lift_neck_v = 0.12;
uniform float lift_navel_v = 0.36;
uniform float lift_bottom_end_v = 0.55;
// 摸頭用的頭髮彎曲（預設全 0＝不影響）：髮根跟著頭轉、越往髮尾越少跟（tip_follow），再加慣性延遲（lag）與波浪
uniform float pat_theta = 0.0;          // 圖層目前被摸頭疊加的旋轉（弧度）
uniform float pat_neck_v = 0.28;        // 旋轉軸（脖子）的 V
uniform float pat_root_v = 0.0;         // 髮根結束（以上完全跟頭）
uniform float pat_tip_v = 1.0;          // 髮尾
uniform float pat_tip_follow = 1.0;     // 髮尾跟著頭的比例（越小越不跟）
uniform float pat_lag_px = 0.0;         // 重力／慣性延遲造成的髮尾位移（貼圖 px）
uniform float pat_wave_px = 0.0;        // 波浪幅度（貼圖 px）
uniform float pat_wave_phase = 0.0;
uniform float pat_wave_freq = 3.0;
uniform vec2  pat_tex_px = vec2(2000.0, 3200.0);
uniform vec2  move_lag_px = vec2(0.0);    // 圖層整體移動（呼吸、上提、被推動）時髮尾的慣性延遲（貼圖 px）
varying vec4 vc;

void vertex() { vc = COLOR; }

void fragment() {
    float raw = clamp((UV.y - taper_start_v) / max(0.001, taper_end_v - taper_start_v), 0.0, 1.0);
    float t = mix(base_ratio, 1.0, raw);
    float shrink = 1.0 - t * expand_amount;
    vec2 uv = vec2(center_u + (UV.x - center_u) * shrink, UV.y);
    uv.y += lift_top_v * smoothstep(lift_neck_v, lift_navel_v, UV.y) + lift_bottom_v * smoothstep(lift_navel_v, lift_bottom_end_v, UV.y);
    float pg = smoothstep(pat_root_v, pat_tip_v, UV.y);
    float rigid = -pat_theta * (UV.y - pat_neck_v) * pat_tex_px.y;
    float follow = mix(1.0, pat_tip_follow, pg);
    float pshift = (follow - 1.0) * rigid + pat_lag_px * pg
                 + pat_wave_px * pg * sin(UV.y * pat_wave_freq * 6.2831853 - pat_wave_phase);
    uv.x -= pshift / pat_tex_px.x;
    uv -= move_lag_px * pg / pat_tex_px;
    COLOR = texture(TEXTURE, uv) * vc;   // vc＝頂點色×Modulate（保留淡入淡出，且不重複乘貼圖顏色）
}
"
            };
        }

        foreach (var kv in HairBottomFlareRegions)
        {
            if (!Layers.TryGetValue(kv.Key, out var ctrl) || ctrl == null) continue;
            var mat = new ShaderMaterial { Shader = _hairFlareShader };
            mat.SetShaderParameter("center_u", kv.Value.X);
            mat.SetShaderParameter("taper_start_v", kv.Value.Y);
            mat.SetShaderParameter("taper_end_v", kv.Value.Z);
            mat.SetShaderParameter("base_ratio", HairFlareBaseRatio);
            mat.SetShaderParameter("expand_amount", 0f);
            ctrl.Material = mat;
            _hairFlareMaterials[kv.Key] = mat;
        }
    }

    /// <summary>使用者要求：拖曳膝蓋熱點（開 M 腿手勢）過程中，小穴／身體下緣要各自有一點點
    /// 左右拉伸的視覺回饋，幅度隨拖曳進度變化。直接沿用上面 SetupHairBottomFlareShaders()
    /// 建好的同一份 _hairFlareShader（純參數化、無狀態，可以放心共用），幫「腿/小穴」「身體」
    /// 各掛一份獨立的 ShaderMaterial 實例，region 參數見 Fields.cs 的 MLegDragStretchRegions。
    /// 只在建構時呼叫一次，之後每一幀由 Interaction.cs 的 ApplyMLegDragStretch() 更新
    /// expand_amount 參數即可。</summary>
    Shader _centerStretchShader;

    void SetupMLegDragStretchShaders()
    {
        if (_hairFlareShader == null) SetupHairBottomFlareShaders(); // 保險：確保共用 shader 已建立

        foreach (var kv in MLegDragStretchRegions)
        {
            if (!Layers.TryGetValue(kv.Key, out var ctrl) || ctrl == null) continue;
            if (kv.Key == "腿/小穴" || kv.Key == "m腿/小穴")
            {
                // 使用者要求：小穴只有「中間」往左右擴張，整張圖（外框）不要擴張。
                // 用中心放大、邊緣不動的取樣：離中心越近放大越多，到內容框半寬處回到 1 倍，
                // 超出內容框完全不變，所以外輪廓維持原樣，只有中間被撐開。
                _centerStretchShader ??= new Shader
                {
                    Code = @"
shader_type canvas_item;
uniform float expand_amount = 0.0;
uniform float center_u = 0.5;
uniform float half_width_u = 0.105;
uniform float top_k = 1.0;     // 上半部的撐開比例（1＝不變；內褲用）
uniform float mid_v = -1.0;    // 上下半部的分界（V）；<0＝不分
uniform vec2 sd_l = vec2(0.0);   // 小穴用：左邊緣跟著左腿動的位移（px）
uniform vec2 sd_r = vec2(0.0);   // 小穴用：右邊緣跟著右腿動的位移（px）
varying vec4 vc;
void vertex() { vc = COLOR; }
void fragment() {
    float d = UV.x - center_u;
    float w = 1.0 - smoothstep(0.0, half_width_u, abs(d));
    float wv = mid_v < 0.0 ? 1.0 : mix(top_k, 1.0, smoothstep(mid_v - 0.04, mid_v + 0.04, UV.y));   // 上半部變形減弱
    float k = 1.0 - expand_amount * w * wv;
    float xp_ = UV.x / TEXTURE_PIXEL_SIZE.x;
    vec2 sd_ = sd_l * (1.0 - smoothstep(885.0, 960.0, xp_)) + sd_r * smoothstep(1020.0, 1094.0, xp_);   // 小穴左右邊緣各自跟著旁邊的腿動（其他圖層位移為 0，不受影響）
    COLOR = texture(TEXTURE, vec2(center_u + d * k, UV.y) - sd_ * TEXTURE_PIXEL_SIZE) * vc;   // 同上
}
"
                };
                var cmat = new ShaderMaterial { Shader = _centerStretchShader };
                cmat.SetShaderParameter("center_u", kv.Value.X);
                cmat.SetShaderParameter("half_width_u", kv.Key == "m腿/小穴" ? 0.07f : 0.105f);   // 小穴內容框 (766..1187)/2000 的半寬
                cmat.SetShaderParameter("expand_amount", 0f);
                ctrl.Material = cmat;
                _mLegDragStretchMaterials[kv.Key] = cmat;
                continue;
            }
            var mat = new ShaderMaterial { Shader = _hairFlareShader };
            mat.SetShaderParameter("center_u", kv.Value.X);
            mat.SetShaderParameter("taper_start_v", kv.Value.Y);
            mat.SetShaderParameter("taper_end_v", kv.Value.Z);
            // 「腿/小穴」用 base_ratio=1（整張統一拉伸）；「身體」用 base_ratio=0（只有下緣拉伸）。
            mat.SetShaderParameter("base_ratio", kv.Key == "腿/小穴" ? 1f : 0f);
            mat.SetShaderParameter("expand_amount", 0f);
            ctrl.Material = mat;
            _mLegDragStretchMaterials[kv.Key] = mat;
        }
    }

    static readonly string[] MouthLayerCandidates =
    {
        "笑", "笑2", "小嘴", "吐舌", "張嘴1", "張嘴2", "斜動嘴", "抖動嘴", "小嘴吐舌", "小嘴巴", "表情/嘴巴",
    };

    /// <summary>DEV 用：把「目前正顯示中的嘴巴圖層」換成另一張嘴巴貼圖，方便在 F1 面板直接
    /// 預覽 Resources/Sprites 資料夾裡每一張嘴巴圖實際套在角色臉上是什麼樣子
    /// （純肉眼比對用——使用者反應光看檔名很難分辨哪張是哪張嘴型）。
    /// spriteBaseName 不含副檔名，例如傳入 "笑" 會載入 Sprites/笑.png。
    /// 找不到目前顯示中的嘴巴圖層時，退回固定用「表情/嘴巴」（一定存在的基礎嘴巴圖層）。
    /// 換貼圖的置中/PivotOffset 規則跟 BuildCharacter() 完全一致，避免跟其他圖層量測方式不一致。
    /// 回傳 false 代表找不到嘴巴圖層或貼圖檔案，呼叫端可以用這個決定要不要顯示錯誤訊息。</summary>
    public bool PreviewMouthTexture(string spriteBaseName)
    {
        string targetLayerName = null;
        Control target = null;
        foreach (var name in MouthLayerCandidates)
        {
            if (Layers.TryGetValue(name, out var c) && c != null && c.Visible)
            {
                targetLayerName = name;
                target = c;
                break;
            }
        }
        if (target == null && Layers.TryGetValue("表情/嘴巴", out var baseC) && baseC != null)
        {
            targetLayerName = "表情/嘴巴";
            target = baseC;
        }
        if (target is not TextureRect tr || targetLayerName == null) return false;

        var tex = ResourceHelper.LoadTexAny("Sprites/" + spriteBaseName);
        if (tex == null) return false;

        tr.Texture = tex;
        tr.Size = new Vector2(tex.GetWidth(), tex.GetHeight());
        tr.Position = (new Vector2(1920f, 1080f) - tr.Size) / 2f;
        tr.PivotOffset = tr.Size / 2f;
        tr.Visible = true;
        _layerBasePos[targetLayerName] = tr.Position;
        return true;
    }

    // ── 1. 背景（雙層：底層 tiaojiaofang2 + 主層 調教房）────────────
    void BuildBackground()
    {
        var bgBase = new TextureRect
        {
            Name = "BG_Base",
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _canvas.AddChild(bgBase);
        MainMenuRuntime.ApplyPointAnchor(bgBase, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            BackgroundOffset, BackgroundSize);   // 與 BG 完全相同，避免位移後邊緣露出第二張背景（BackgroundBase* 參數已停用）
        var texBase = ResourceHelper.LoadTexAny("Backgrounds/bg_LinaiBed_night_room");
        if (texBase != null) { bgBase.Texture = texBase; bgBase.Modulate = Colors.White; }
        else bgBase.Modulate = new Color(0.12f, 0.08f, 0.18f);

        var bg = new TextureRect
        {
            Name = "BG",
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _canvas.AddChild(bg);
        MainMenuRuntime.ApplyPointAnchor(bg, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            BackgroundOffset, BackgroundSize);
        var tex = ResourceHelper.LoadTexAny("Backgrounds/bg_LinaiBed_night_room");
        if (tex != null) { bg.Texture = tex; bg.Modulate = Colors.White; }
        else bg.Modulate = new Color(0.05f, 0.02f, 0.08f);

        // 床鋪獨立透明圖層，沿用背景變換並位於角色下方。
        var bed = new TextureRect
        {
            Name = "BedLayer",
            Texture = ResourceHelper.LoadTexAny("Backgrounds/bg_LinaiBed_night_bed"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        bg.AddChild(bed);
        bed.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    }

    // ── 2. 立繪（分層系統，與 BedroomRuntime 相同架構）──────────────
    void BuildCharacter()
    {
        _charRoot = new Control { Name = "CharacterRoot" };
        MainMenuRuntime.ApplyPointAnchor(_charRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            CharPosition, new Vector2(1920f, 1080f));
        _charRoot.Scale = new Vector2(CharScale, CharScale);
        _canvas.AddChild(_charRoot);

        foreach (var layerName in LayerOrder)
        {
            string goName = layerName.Replace("/", "_");
            var img = new TextureRect
            {
                Name = goName,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
            };

            // 實際美術檔案是攤平放在 Sprites/ 底下（無 Meimei/ 子資料夾，也無 腿/、m腿/、表情/ 等分組子資料夾），
            // 例如圖層 "腿/腿1" 的實際檔案是 Sprites/腿1.png、"m腿/小穴" 的實際檔案是 Sprites/小穴.png。
            // 因此一律只取 "/" 最後一段（basename）去載入。
            string baseName = layerName.Contains("/") ? layerName[(layerName.LastIndexOf('/') + 1)..] : layerName;
            // _extraSpritePaths 優先：這張表是用來覆蓋 basename 規則會撞名到「錯誤圖」的例外情況
            // （例如 手1/手2 撞到另一個角色同名檔案），必須先查表，查不到才落回一般 basename 規則。
            var tex = (_extraSpritePaths.TryGetValue(layerName, out var ep) ? ResourceHelper.LoadTexAny(ep) : null)
                   ?? ResourceHelper.LoadTexAny("Sprites/" + baseName);

            if (tex != null)
            {
                img.Texture = tex;
                img.Size = new Vector2(tex.GetWidth(), tex.GetHeight());
                img.Position = (new Vector2(1920f, 1080f) - img.Size) / 2f;
                img.PivotOffset = img.Size / 2f;
                img.Visible = true;
            }
            else
            {
                img.Modulate = Colors.Transparent;
                img.Visible = false;
            }
            _charRoot.AddChild(img);
            Layers[layerName] = img;
            _layerBasePos[layerName] = img.Position;
            _layerBaseSizeDelta[layerName] = img.Size;

            // 手臂/手/辮子等圖層：用真實錨點（肩膀/髮根）取代預設的畫布正中心 pivot，
            // 讓呼吸/搖擺的旋轉看起來是從關節/髮根擺動，而不是整張圖繞中心轉。
            //
            // 注意：Godot 的 Control.PivotOffset 只影響「旋轉/縮放」的軸心，並不像 Unity RectTransform
            // 的 pivot 一樣會連動改變視覺位置——在 rotation=0、scale=(1,1) 的靜止狀態下，改 PivotOffset
            // 完全不會移動圖片。之前這裡誤加了一段「pivot 補償量」去平移 Position（照抄 Unity 的邏輯），
            // 結果變成每一幀的 basePos 都被多平移了一大段距離，才會讓手臂"飛到天上"。
            // 因此這裡只設定 PivotOffset，絕對不能再動 Position／_layerBasePos。
            if (tex != null && _customAttachPivot.TryGetValue(layerName, out var attachPivot))
            {
                ApplyLayerPivot(img, attachPivot);
            }

            // 前髮整體往下移（使用者要求的微調，跟旋轉錨點無關，單純調整靜止位置）。
            // 原本只下移 5px，使用者反應還是偏高，這裡改用可調欄位 FrontHairDropY，先加到 18px。
            if (tex != null && layerName == "前髮")
            {
                img.Position += new Vector2(0f, FrontHairDropY);
                _layerBasePos[layerName] = img.Position;
            }
        }
    }

    /// <summary>
    /// 套用非中心的 pivot（供頭髮/腿部/手臂細節系統使用）。
    /// 傳入值一律是「Unity 原始寫法」的 pivot（Y 軸 0=下 1=上）；
    /// Godot 的 PivotOffset 是像素座標、Y 軸 0=上 1=下，方向相反，這裡統一做 Y 翻轉，
    /// 呼叫端可以直接照抄 Unity 數值，不用自己換算。
    /// 只設定 PivotOffset，不動 Position——Godot 的 pivot 在靜止狀態下不影響視覺位置，
    /// 不需要（也不能）像 Unity 那樣額外做位置補償，否則會造成圖層飄移/飛出畫面。
    /// </summary>
    static void ApplyLayerPivot(Control img, Vector2 unityPivotNorm)
    {
        var godotPivotNorm = new Vector2(unityPivotNorm.X, 1f - unityPivotNorm.Y);
        img.PivotOffset = godotPivotNorm * img.Size;
    }

    void InitBreathLayers()
    {
        // 已在 BuildCharacter 中把初始 Position 存進 _layerBasePos；此處保留擴充點。
    }

    // ═══════════════════════════════════════════════════════════════
    //  UI 排版（2026-10-18 重新設計）：所有面板改成「浮動卡片」，留邊距、圓角、統一配色，
    //  不再整條貼邊擋住畫面。面板名稱（TopHUD/StatsPanel/VertBars/BottomBar/SidePanel）不變，
    //  版面編輯器（F2）和鏡頭的 UI 層搬移都依賴這些名稱。
    //  版面配置（1920x1080）：
    //    左上：狀態卡（心情/各部位舒服度）→ 下方接 精力/精液卡
    //    上中：高潮度膠囊
    //    左下：操作按鈕卡（口／左右手／雞雞／更多／手銬）
    //    右上：選單卡（流程／快速操作／劇情觸發）
    // ═══════════════════════════════════════════════════════════════
    static readonly Color UiCardBg     = new Color(1f, 1f, 1f, 0.90f);   // 白色 90% 不透明（原紫黑）
    static readonly Color UiCardBorder = new Color(0.82f, 0.38f, 0.58f);
    static readonly Color UiText       = new Color(0.22f, 0.10f, 0.18f);   // 白底深色字（加深提高對比）
    static readonly Color UiMuted      = new Color(0.62f, 0.42f, 0.56f);
    static readonly Color UiAccent     = new Color(0.90f, 0.38f, 0.60f);
    static readonly Color UiValue      = new Color(1f, 0.90f, 0.55f);

    /// <summary>浮動卡片底板：半透明深紫＋粉色細框＋圓角＋淡陰影。</summary>
    static StyleBoxFlat MkCardStyle()
    {
        var sb = new StyleBoxFlat { BgColor = UiCardBg, BorderColor = UiCardBorder };
        sb.SetBorderWidthAll(2);
        sb.SetCornerRadiusAll(14);
        sb.ShadowColor = new Color(0.5f, 0.15f, 0.3f, 0.20f);
        sb.ShadowSize = 8;
        return sb;
    }

    static readonly Color BarOutline = new Color(0.28f, 0.14f, 0.24f);

    /// <summary>數值條樣式：一律加 2px 深色外框，淺色底上才分得出條的範圍和填充量。</summary>
    static StyleBoxFlat MkBarStyle(Color c)
    {
        var sb = new StyleBoxFlat { BgColor = c, BorderColor = BarOutline };
        sb.SetBorderWidthAll(2);
        sb.SetCornerRadiusAll(6);
        return sb;
    }

    static void StyleBar(ProgressBar b, Color fill, Color back)
    {
        b.AddThemeStyleboxOverride("fill", MkBarStyle(fill));
        b.AddThemeStyleboxOverride("background", MkBarStyle(back));
    }

    /// <summary>卡片內的小節標題「── 文字 ──」，畫完把 y 往下推。</summary>
    void MkSection(Control parent, string name, string text, float width, ref float y)
    {
        var lb = MkLabel(parent, name, "── " + text + " ──", 13, UiMuted, new Vector2(0, y), new Vector2(width, 22));
        lb.HorizontalAlignment = HorizontalAlignment.Center;
        y += 26;
    }

    // ── 3. 上中：高潮度膠囊 ───────────────────────────────────────
    void BuildTopHUD()
    {
        const float W = 680f, H = 60f;
        var bar = new Panel { Name = "TopHUD" };
        bar.AnchorLeft = 0.5f; bar.AnchorRight = 0.5f; bar.AnchorTop = 0; bar.AnchorBottom = 0;
        bar.OffsetLeft = -W / 2f; bar.OffsetRight = W / 2f;
        bar.OffsetTop = 16; bar.OffsetBottom = 16 + H;
        bar.AddThemeStyleboxOverride("panel", MkCardStyle());
        _canvas.AddChild(bar);

        MkLabel(bar, "ClimaxLabel", "💗 高潮度", 20, UiAccent, new Vector2(18, 13), new Vector2(120, 34));

        _climaxBar = new ProgressBar { Name = "ClimaxBar", MinValue = 0, MaxValue = 100, Value = 0, ShowPercentage = false };
        _climaxBar.Position = new Vector2(140, 21); _climaxBar.Size = new Vector2(330, 18);
        StyleBar(_climaxBar, new Color(0.93f, 0.30f, 0.56f), CuteBarBack);
        bar.AddChild(_climaxBar);

        // 數值可能顯示「100 / 100（鎖定）」，寬度預留 184px、靠右對齊
        _climaxValueLabel = MkLabel(bar, "ClimaxValue", "0 / 100", 17, UiText, new Vector2(480, 14), new Vector2(184, 32));
        _climaxValueLabel.HorizontalAlignment = HorizontalAlignment.Right;
    }

    // ── 可愛風配色（左上狀態卡、左下圖示長條用）──────────────────
    static readonly Color CuteCardBg     = new Color(1f, 1f, 1f, 0.90f);   // 白色 90% 不透明
    static readonly Color CuteCardBorder = new Color(0.82f, 0.38f, 0.58f);          // 深粉框（提高對比）
    static readonly Color CuteText       = new Color(0.22f, 0.10f, 0.18f);          // 深色字（提高對比）
    static readonly Color CuteTitle      = new Color(0.90f, 0.38f, 0.60f);
    static readonly Color CuteValue      = new Color(0.62f, 0.12f, 0.34f);
    static readonly Color CuteBarBack    = new Color(0.93f, 0.90f, 0.92f);

    static StyleBoxFlat MkCuteCardStyle(int radius = 18)
    {
        var sb = new StyleBoxFlat { BgColor = CuteCardBg, BorderColor = CuteCardBorder };
        sb.SetBorderWidthAll(3);
        sb.SetCornerRadiusAll(radius);
        sb.ShadowColor = new Color(0.5f, 0.15f, 0.3f, 0.25f);
        sb.ShadowSize = 6;
        return sb;
    }

    // ── 4. 左上：狀態卡（心情/舒服度）────────────────────────────
    void BuildStatsPanel()
    {
        var panel = new Panel { Name = "StatsPanel" };
        panel.Position = new Vector2(20, 20); panel.Size = new Vector2(300, 238);
        panel.AddThemeStyleboxOverride("panel", MkCuteCardStyle());
        _canvas.AddChild(panel);

        MkLabel(panel, "StatsTitle", "♡ 狀態", 16, CuteTitle, new Vector2(16, 8), new Vector2(120, 26));

        (_moodLabel, _moodBar)                 = StatRow(panel, "心情",     38,  Mood,         100);
        (_comfortMouthLabel, _comfortMouthBar) = StatRow(panel, "嘴巴舒服", 70,  ComfortMouth, 100);
        (_comfortChestLabel, _comfortChestBar) = StatRow(panel, "胸部舒服", 102, ComfortChest, 100);
        (_comfortButtLabel, _comfortButtBar)   = StatRow(panel, "尻部舒服", 134, ComfortButt,  100);
        (_comfortHoleLabel, _comfortHoleBar)   = StatRow(panel, "小穴舒服", 166, ComfortHole,  200);
        (_bratLabel, _bratBar)                 = StatRow(panel, "雌小鬼",   198, BratValue,    100);
    }

    (Label, ProgressBar) StatRow(Control parent, string label, float y, float val, float max)
    {
        MkLabel(parent, label + "L", label, 15, CuteText, new Vector2(16, y), new Vector2(84, 26));
        var bar = new ProgressBar { MinValue = 0, MaxValue = max, Value = val, ShowPercentage = false };
        bar.Position = new Vector2(100, y + 6); bar.Size = new Vector2(148, 14);
        StyleBar(bar, new Color(0.93f, 0.36f, 0.60f), CuteBarBack);
        parent.AddChild(bar);
        var vLbl = MkLabel(parent, label + "V", $"{Mathf.RoundToInt(val)}", 14, CuteValue, new Vector2(252, y), new Vector2(36, 26));
        vLbl.HorizontalAlignment = HorizontalAlignment.Right;
        return (vLbl, bar);
    }

    // ── 5. 左下：精液直條（使用者要求從左上移到左下，排在精力直條右邊）────
    //  節點名沿用 "VertBars"（鏡頭 UI 層、F2 版面編輯器都靠這個名稱）。
    //  位置跟 BuildBottomButtons() 的尺寸算法一致：按鈕條(84) → 精力(42) → 精液(42)，間距 12。
    void BuildStaminaSemenBars()
    {
        const float Pad = 10f, Gap = 12f, StamW = 18f, PanelW = 42f;
        float btnBarW = Pad * 2f + IconBtnSize;
        float H = Pad * 2f + IconBtnSize * 3f + Gap * 2f;
        float left = 20f + btnBarW + Gap + PanelW + Gap;

        var panel = new Panel { Name = "VertBars", TooltipText = "勃起度" };
        panel.AnchorLeft = 0; panel.AnchorTop = 1; panel.AnchorRight = 0; panel.AnchorBottom = 1;
        panel.OffsetLeft = left; panel.OffsetRight = left + PanelW;
        panel.OffsetBottom = -20; panel.OffsetTop = -20 - H;
        panel.AddThemeStyleboxOverride("panel", MkCuteCardStyle((int)(PanelW / 2f)));
        _canvas.AddChild(panel);

        _semenBar = new ProgressBar
        {
            Name = "SemenBar", TooltipText = "勃起度（≥30 勃起，<30 軟掉）",
            FillMode = (int)ProgressBar.FillModeEnum.BottomToTop,
            MinValue = 0, MaxValue = 1, Value = 0, ShowPercentage = false,
        };
        _semenBar.Position = new Vector2((PanelW - StamW) / 2f, 14f);
        _semenBar.Size = new Vector2(StamW, H - 28f);
        var fill = MkBarStyle(new Color(0.95f, 0.70f, 0.30f)); fill.SetCornerRadiusAll(9);
        var back = MkBarStyle(CuteBarBack);                    back.SetCornerRadiusAll(9);
        _semenBar.AddThemeStyleboxOverride("fill", fill);
        _semenBar.AddThemeStyleboxOverride("background", back);
        panel.AddChild(_semenBar);
    }

    public void SetSemenLevel(float v)
    {
        _semenLevel = Mathf.Clamp(v, 0f, 1f);
        if (_semenBar != null) _semenBar.Value = _semenLevel;
    }

    // ── 6. 左下：可愛圖示直條（左手 / 右手 / 雞雞 ＋ 精力直條）────────
    //  使用者要求：直的膠囊長條；按鈕只顯示圖案，文字只在滑鼠移過去時（TooltipText）顯示；
    //  精力從左上移過來，做成獨立直條面板 StaminaPanel，放在按鈕條右側，由下往上填。
    const float IconBtnSize = 64f;

    void BuildBottomButtons()
    {
        const float Pad = 10f, Gap = 12f, StamW = 18f, StamPanelW = 42f;
        int count = 6;   // 衣服／左手／右手／🍆／👅／🐭
        float W = Pad * 2f + IconBtnSize;
        float H = Pad * 2f + IconBtnSize * count + Gap * (count - 1);

        var bar = new Panel { Name = "BottomBar" };
        bar.AnchorLeft = 0; bar.AnchorTop = 1; bar.AnchorRight = 0; bar.AnchorBottom = 1;
        bar.OffsetLeft = 20; bar.OffsetRight = 20 + W;
        bar.OffsetBottom = -20; bar.OffsetTop = -20 - H;
        bar.AddThemeStyleboxOverride("panel", MkCuteCardStyle((int)(W / 2f)));   // 直的膠囊形
        _canvas.AddChild(bar);

        float Y(int i) => Pad + (IconBtnSize + Gap) * i;

        _btnLeft  = MkIconBtn(bar, "BtnLeft",  "左手", new Color(1.00f, 0.72f, 0.82f), new Vector2(Pad, Y(1)));
        _btnRight = MkIconBtn(bar, "BtnRight", "右手", new Color(0.80f, 0.72f, 1.00f), new Vector2(Pad, Y(2)));
        _btnLeft.Pressed  += () => ToggleHandMode(HandMode.Left);
        _btnRight.Pressed += () => ToggleHandMode(HandMode.Right);

        // 手圖標：右手原圖、左手水平翻轉；圖示置中。找不到素材時退回用 ✋ 文字圖案。
        ApplyHandIconButtons(_btnLeft, _btnRight);
        RefreshHandModeButtons();   // 初始空白底
        foreach (var b in new[] { _btnLeft, _btnRight })
        {
            if (b.Icon != null) b.IconAlignment = HorizontalAlignment.Center;
            else { b.Text = "✋"; b.AddThemeFontSizeOverride("font_size", 30); }
        }

        var btnJiji = MkIconBtn(bar, "Btn2", "雞雞", new Color(1.00f, 0.80f, 0.66f), new Vector2(Pad, Y(3)));
        btnJiji.Text = "🍆";
        btnJiji.AddThemeFontSizeOverride("font_size", 30);
        btnJiji.Pressed += () => OnJijiButtonPressed();

        _btnTongue = MkIconBtn(bar, "BtnTongue", "舌頭（選取後點嘴巴）", Colors.White, new Vector2(Pad, Y(4)));
        _btnTongue.Text = "👅";
        _btnTongue.AddThemeFontSizeOverride("font_size", 30);
        _btnTongue.Pressed += () => ToggleTongueTool();
        RefreshTongueButton();

        _btnDrag = MkIconBtn(bar, "BtnDrag", "衣服模式（只拖衣服／被子，忽略綠點）", Colors.White, new Vector2(Pad, Y(0)));
        _btnDrag.Icon = MkDressIcon(); _btnDrag.IconAlignment = HorizontalAlignment.Center;   // 白色連衣裙
        _btnDrag.Pressed += () => ToggleDragTool();
        if (DemoMode.Enabled)
        {   // 試玩版：衣服模式停用（衣服物件不可控制）
            _btnDrag.Disabled = true;
            _btnDrag.Modulate = new Color(1f, 1f, 1f, 0.35f);
            _btnDrag.TooltipText = "試玩版未開放";
        }
        RefreshDragButton();

        _btnMouse = MkIconBtn(bar, "BtnMouse", "滑鼠（預設操作；選其他工具時取消選取）", Colors.White, new Vector2(Pad, Y(5)));
        _btnMouse.Icon = MkCursorIcon(); _btnMouse.IconAlignment = HorizontalAlignment.Center;   // 滑鼠游標箭頭
        _btnMouse.Pressed += () => SelectMouseTool();
        RefreshMouseButton();

        // 右下角：離開房間（＝結束今天，走 EndToday 流程）
        {
            float LW = Pad * 2f + IconBtnSize;
            var leave = new Panel { Name = "LeavePanel" };
            leave.AnchorLeft = 1; leave.AnchorTop = 1; leave.AnchorRight = 1; leave.AnchorBottom = 1;
            leave.OffsetRight = -20; leave.OffsetLeft = -20 - LW;
            leave.OffsetBottom = -20; leave.OffsetTop = -20 - LW;
            leave.AddThemeStyleboxOverride("panel", MkCuteCardStyle((int)(LW / 2f)));
            _canvas.AddChild(leave);
            var btnLeave = MkIconBtn(leave, "BtnLeave", "離開房間（結束今天）", new Color(1.00f, 0.70f, 0.70f), new Vector2(Pad, Pad));
            btnLeave.Text = "🚪";
            btnLeave.AddThemeFontSizeOverride("font_size", 30);
            btnLeave.Pressed += () => EndToday();
        }

        // 精力：獨立的直條面板（使用者要求不要跟按鈕條合在一起），放在按鈕條右側，由下往上填
        var stPanel = new Panel { Name = "StaminaPanel", TooltipText = "精力" };
        stPanel.AnchorLeft = 0; stPanel.AnchorTop = 1; stPanel.AnchorRight = 0; stPanel.AnchorBottom = 1;
        stPanel.OffsetLeft = 20 + W + Gap; stPanel.OffsetRight = 20 + W + Gap + StamPanelW;
        stPanel.OffsetBottom = -20; stPanel.OffsetTop = -20 - H;
        stPanel.AddThemeStyleboxOverride("panel", MkCuteCardStyle((int)(StamPanelW / 2f)));
        _canvas.AddChild(stPanel);

        _staminaBar = new ProgressBar
        {
            Name = "StaminaBar", TooltipText = "精力（男主感覺，90 可射）",
            FillMode = (int)ProgressBar.FillModeEnum.BottomToTop,
            MinValue = 0, MaxValue = 120, Value = Stamina, ShowPercentage = false,
        };
        _staminaBar.Position = new Vector2((StamPanelW - StamW) / 2f, 14f);
        _staminaBar.Size = new Vector2(StamW, H - 28f);
        var stFill = MkBarStyle(new Color(0.30f, 0.72f, 0.48f)); stFill.SetCornerRadiusAll(9);
        var stBack = MkBarStyle(CuteBarBack);                    stBack.SetCornerRadiusAll(9);
        _staminaBar.AddThemeStyleboxOverride("fill", stFill);
        _staminaBar.AddThemeStyleboxOverride("background", stBack);
        stPanel.AddChild(_staminaBar);
    }

    /// <summary>圓形圖示按鈕：不顯示文字，tooltip 顯示功能名稱；白色描邊、hover 變亮、按下變暗。</summary>
    Button MkIconBtn(Control parent, string name, string tooltip, Color bg, Vector2 pos)
    {
        var btn = new Button
        {
            Name = name, Text = "", TooltipText = tooltip,
            FocusMode = Control.FocusModeEnum.None,
            Position = pos, Size = new Vector2(IconBtnSize, IconBtnSize),
        };
        btn.AddThemeStyleboxOverride("normal",  MkRoundBtnStyle(bg));
        btn.AddThemeStyleboxOverride("hover",   MkRoundBtnStyle(bg.Lightened(0.25f)));
        btn.AddThemeStyleboxOverride("pressed", MkRoundBtnStyle(bg.Darkened(0.15f)));
        btn.AddThemeColorOverride("font_color", Colors.White);
        parent.AddChild(btn);
        return btn;
    }

    static StyleBoxFlat MkRoundBtnStyle(Color c)
    {
        var sb = new StyleBoxFlat { BgColor = c, BorderColor = BarOutline };   // 深色描邊（原白色，太淡）
        sb.SetBorderWidthAll(3);
        sb.SetCornerRadiusAll((int)(IconBtnSize / 2f));
        return sb;
    }

    /// <summary>幫底部的「左手」「右手」按鈕加上「手圖標」當圖示：右手按鈕用原圖，
    /// 左手按鈕用同一張圖水平翻轉（FlipX），不需要另外準備一張左手專用的圖示素材。</summary>
    static Texture2D _handIconRightTex, _handIconLeftTex;
    void ApplyHandIconButtons(Button btnLeft, Button btnRight)
    {
        if (_handIconRightTex == null)
        {
            var tex = ResourceHelper.LoadTexAny("Sprites/手圖標");
            if (tex == null) return;

            // 原圖 1024x1024、內容框約 (100,112)-(1021,975)，裁掉透明留白再縮成按鈕圖示大小。
            var img = tex.GetImage();
            img = img.GetRegion(new Rect2I(100, 112, 1021 - 100, 975 - 112));
            img.Resize(40, 40, Image.Interpolation.Lanczos);   // 配合 64px 圓形圖示鈕（原 32）
            _handIconRightTex = ImageTexture.CreateFromImage(img);

            var imgL = (Image)img.Duplicate();
            imgL.FlipX();
            _handIconLeftTex = ImageTexture.CreateFromImage(imgL);
        }

        btnRight.Icon = _handIconRightTex;
        btnRight.IconAlignment = HorizontalAlignment.Left;
        btnLeft.Icon = _handIconLeftTex;
        btnLeft.IconAlignment = HorizontalAlignment.Left;
    }

    void ToggleHandMode(HandMode mode)
    {
        _handMode = (_handMode == mode) ? HandMode.None : mode;
        if (_handMode != HandMode.None && _tongueTool) { _tongueTool = false; RefreshTongueButton(); }   // 按鈕不能同時選兩個
        if (_handMode != HandMode.None && _dragTool) SetDragTool(false);
        GD.Print($"[TrainingRoom] 手部模式：{_handMode}");
        RefreshHandModeButtons();
    }

    void ExitHandMode() { _handMode = HandMode.None; RefreshHandModeButtons(); }

    /// <summary>左手／右手按鈕：沒選時是空白（白底）；選中時變色並加紅色外圈，再點一次取消。
    /// 選中的手決定「摸小穴(B)」用哪一隻手；沒選＝自動分配。</summary>
    void RefreshHandModeButtons()
    {
        void Apply(Button b, Color bg, bool selected)
        {
            if (b == null || !IsInstanceValid(b)) return;
            Color fill = selected ? bg : Colors.White;
            StyleBoxFlat Mk(Color c)
            {
                var sb = MkRoundBtnStyle(c);
                if (selected) { sb.BorderColor = new Color(0.92f, 0.12f, 0.18f); sb.SetBorderWidthAll(5); }   // 紅色外圈
                return sb;
            }
            b.AddThemeStyleboxOverride("normal",  Mk(fill));
            b.AddThemeStyleboxOverride("hover",   Mk((selected ? fill.Lightened(0.15f) : fill.Darkened(0.06f))));
            b.AddThemeStyleboxOverride("pressed", Mk(fill.Darkened(0.12f)));
        }
        Apply(_btnLeft,  new Color(1.00f, 0.72f, 0.82f), _handMode == HandMode.Left);
        Apply(_btnRight, new Color(0.80f, 0.72f, 1.00f), _handMode == HandMode.Right);
        RefreshMouseButton();
    }

    /// <summary>🍆 按鈕：雞雞沒出現 → 淡入出現（Phase 1）；已經出現 → 收起（回 Phase 0）。
    /// 對照 Unity ShowJijiAtDefault / HideJiji，實際邏輯在 TrainingRoomRuntime.Jiji.cs。</summary>
    void OnJijiButtonPressed()
    {
        ExitHandMode();
        if (_jijiPhase == 0) ShowJiji();
        else HideJiji();
    }

    // ── 7. 右上：選單卡（流程 / 快速操作 / 劇情觸發）──────────────
    //  原本是貼齊右邊、整個螢幕高的黑條，改成有邊距的浮動卡片，高度依內容自動決定。
    //  （順便修正：原本「劇情觸發」標題 y=590 跟第一個開關 y=582 重疊）
    void BuildSideMenu()
    {
        const float W = 230f, Pad = 14f, Margin = 20f;
        float bw = W - Pad * 2f;

        var side = new Panel { Name = "SidePanel" };
        side.AnchorLeft = 1; side.AnchorRight = 1; side.AnchorTop = 0; side.AnchorBottom = 0;
        side.OffsetLeft = -W - Margin; side.OffsetRight = -Margin;
        side.OffsetTop = Margin;
        side.AddThemeStyleboxOverride("panel", MkCardStyle());
        _canvas.AddChild(side);

        float y = 12;
        var locName = MkLabel(side, "LocName", "調教室", 30, UiAccent, new Vector2(0, y), new Vector2(W, 44));
        locName.HorizontalAlignment = HorizontalAlignment.Center;
        y += 52;

        // 流程
        MkSection(side, "SecFlow", "流程", W, ref y);
        var end = MkButtonSimple(side, "BtnEnd", "結束今晚", new Color(0.30f, 0.10f, 0.14f), new Vector2(Pad, y), new Vector2(bw, 48));
        end.Pressed += () => EndToday();
        y += 54;
        var back = MkButtonSimple(side, "BtnBack", "← 返回", new Color(0.12f, 0.06f, 0.14f), new Vector2(Pad, y), new Vector2(bw, 40));
        back.Pressed += () => _ = FadeAndLoadAsync("Apartment");
        y += 46;
        var btnHome = MkButtonSimple(side, "BtnHome", "🏠 主畫面", new Color(0.08f, 0.04f, 0.12f), new Vector2(Pad, y), new Vector2(bw, 40));
        btnHome.Pressed += () => _ = FadeAndLoadAsync("MainMenu");
        y += 50;

        // 快速操作：摸了 / 高潮 / 插入 / 射精 / 清精液
        MkSection(side, "SecQuick", "快速操作", W, ref y);
        var btnTouched = MkButtonSimple(side, "BtnTouched", "✋ 摸了", new Color(0.22f, 0.10f, 0.32f), new Vector2(Pad, y), new Vector2(bw, 38));
        btnTouched.Pressed += () => OnTouchedPressed();
        y += 44;
        var btnOrgasm = MkButtonSimple(side, "BtnOrgasm", "💗 高潮", new Color(0.38f, 0.06f, 0.18f), new Vector2(Pad, y), new Vector2(bw, 38));
        btnOrgasm.Pressed += () => OnOrgasmPressed();
        y += 44;
        var btnInsert = MkButtonSimple(side, "BtnInsert", "🍑 插入", new Color(0.35f, 0.08f, 0.22f), new Vector2(Pad, y), new Vector2(bw, 38));
        btnInsert.Pressed += () => OnInsertPressed();
        y += 44;
        var btnEjac = MkButtonSimple(side, "BtnEjac", "💦 射精", new Color(0.30f, 0.04f, 0.30f), new Vector2(Pad, y), new Vector2(bw, 38));
        btnEjac.Pressed += () => TriggerEjaculation();
        y += 44;
        var btnClear = MkButtonSimple(side, "BtnClearEjac", "✕ 清精液", new Color(0.12f, 0.04f, 0.16f), new Vector2(Pad, y), new Vector2(bw, 32));
        btnClear.Pressed += () => ClearEjacDebris();
        y += 42;

        // 設置
        MkSection(side, "SecSettings", "設置", W, ref y);
        AddToggle(side, "TogGreenCursor", "綠點游標變化", y, () => GreenDotCursorEnabled, v => { GreenDotCursorEnabled = v; if (!v) ApplyHandCursor(); }, Pad, bw); y += 38;
        AddToggle(side, "TogHoverTip", "懸停提示氣泡", y, () => HoverTipEnabled, v => { HoverTipEnabled = v; if (!v) HoverTipHide(); }, Pad, bw); y += 38;

        // 劇情觸發開關
        MkSection(side, "TrigSep", "劇情觸發", W, ref y);
        AddToggle(side, "TogD2First",  "D2 首次進場", y, () => PendingDay2First,    v => PendingDay2First    = v, Pad, bw); y += 38;
        AddToggle(side, "TogD3First",  "D3 首次進場", y, () => PendingDay3First,    v => PendingDay3First    = v, Pad, bw); y += 38;
        AddToggle(side, "TogNight3",   "第三晚觸發",  y, () => PendingNight3,       v => PendingNight3       = v, Pad, bw); y += 38;
        AddToggle(side, "TogBreast",   "摸胸已發生",  y, () => HadBreastTouch,      v => HadBreastTouch      = v, Pad, bw); y += 38;
        AddToggle(side, "TogComfort",  "舒服路線",    y, () => BreastComfortRoute,  v => BreastComfortRoute  = v, Pad, bw); y += 38;
        AddToggle(side, "TogMiniGame", "小遊戲直入",  y, () => PendingMiniGameOnly, v => PendingMiniGameOnly = v, Pad, bw); y += 38;

        side.OffsetBottom = Margin + y + Pad - 6;   // 卡片高度跟著內容走
    }

    // ── UI 工廠方法 ───────────────────────────────────────────────
    Label MkLabel(Control parent, string name, string text, int size, Color color, Vector2 pos, Vector2 sz)
    {
        var lb = new Label { Name = name, Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        lb.AddThemeFontSizeOverride("font_size", size);
        lb.AddThemeColorOverride("font_color", color);
        lb.AutowrapMode = TextServer.AutowrapMode.Arbitrary;
        lb.Position = pos; lb.Size = sz;
        parent.AddChild(lb);
        return lb;
    }

    Button MkButtonSimple(Control parent, string name, string text, Color bg, Vector2 pos, Vector2 sz)
    {
        var btn = new Button { Name = name, Text = text };
        btn.Position = pos; btn.Size = sz;
        btn.AddThemeStyleboxOverride("normal", MkPanelStyle(bg));
        btn.AddThemeStyleboxOverride("hover", MkPanelStyle(new Color(Mathf.Min(bg.R + 0.15f, 1), Mathf.Min(bg.G + 0.15f, 1), Mathf.Min(bg.B + 0.15f, 1))));
        btn.AddThemeColorOverride("font_color", Colors.White);
        parent.AddChild(btn);
        return btn;
    }

    Button VBtn(Control parent, string name, string label, Color bg, float w, float h, float topY)
    {
        var btn = new Button { Name = name, Text = label };
        btn.Position = new Vector2(0, topY); btn.Size = new Vector2(w, h);
        btn.AddThemeStyleboxOverride("normal", MkPanelStyle(bg));
        btn.AddThemeStyleboxOverride("hover", MkPanelStyle(new Color(Mathf.Min(bg.R + 0.2f, 1), Mathf.Min(bg.G + 0.15f, 1), Mathf.Min(bg.B + 0.15f, 1))));
        btn.AddThemeStyleboxOverride("pressed", MkPanelStyle(new Color(bg.R * 0.75f, bg.G * 0.75f, bg.B * 0.75f)));
        btn.AddThemeColorOverride("font_color", Colors.White);
        parent.AddChild(btn);
        return btn;
    }

    static StyleBoxFlat MkPanelStyle(Color c)
    {
        var sb = new StyleBoxFlat { BgColor = c };
        sb.SetCornerRadiusAll(8);   // 按鈕圓角（原 5，配合卡片風格改 8）
        return sb;
    }
    // ── 劇情觸發開關：工廠方法 ────────────────────────────────────
    void AddToggle(Control parent, string name, string label, float topY,
                   Func<bool> getter, Action<bool> setter, float x = 5f, float width = 190f)
    {
        bool on = getter();
        var btn = new Button { Name = name, Text = (on ? "✓ " : "○ ") + label };
        btn.Position = new Vector2(x, topY);
        btn.Size     = new Vector2(width, 32);
        btn.AddThemeStyleboxOverride("normal",  MkToggleSty(on));
        btn.AddThemeStyleboxOverride("hover",   MkToggleSty(on));
        btn.AddThemeStyleboxOverride("pressed", MkToggleSty(on));
        btn.AddThemeFontSizeOverride("font_size", 13);
        btn.AddThemeColorOverride("font_color", Colors.White);
        btn.Pressed += () =>
        {
            bool v = !getter();
            setter(v);
            btn.Text = (v ? "✓ " : "○ ") + label;
            btn.AddThemeStyleboxOverride("normal",  MkToggleSty(v));
            btn.AddThemeStyleboxOverride("hover",   MkToggleSty(v));
            btn.AddThemeStyleboxOverride("pressed", MkToggleSty(v));
        };
        parent.AddChild(btn);
    }

    static StyleBoxFlat MkToggleSty(bool on)
    {
        var sb = new StyleBoxFlat
        {
            BgColor     = on ? new Color(0.08f, 0.25f, 0.10f) : new Color(0.14f, 0.08f, 0.18f),
            BorderColor = on ? new Color(0.35f, 0.75f, 0.35f) : new Color(0.38f, 0.22f, 0.52f),
        };
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(4);
        return sb;
    }

}
