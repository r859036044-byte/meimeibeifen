using Godot;
using System.Threading.Tasks;

/// <summary>
/// 調教室場景 — Unity TrainingRoomRuntime.cs 的 Godot 4 C# 移植版。
///
/// 移植範圍說明（詳見各 partial 檔案開頭註解）：
///   • 完整移植：背景（雙層）、立繪分層系統（Layers 字典）、頂部 HUD（高潮度）、
///     左上數值面板（心情/舒服度）、精力條、精液條、右側選單、底部操作按鈕、
///     5+ 個綠色互動熱點、呼吸微動畫、痙攣抖動效果（TriggerClimax/Spasms）、
///     噴水效果（簡化版）、待播對話（PlayPendingDialogue）、EndToday 流程、
///     日/夜切換與場景轉換、PendingDay2First/PendingDay3First/PendingNight3/
///     PendingMiniGameOnly/HadBreastTouch/BreastComfortRoute 等靜態旗標。
///   • 簡化移植（原版是數千行、極度細膩的手動調校物理動畫，如雞雞插入三階段
///     位移/阻力突破分數系統/深插振盪/M腿展開網格裁切/精液噴射流體网格），
///     改為較輕量但仍可運作、可互動、有明確狀態機的等價系統，詳見
///     TrainingRoomRuntime.Interaction.cs 開頭註解與最終報告的「已知落差」。
///   • Spine 骨骼動畫（耳朵/尾巴）：Unity 原始碼中 BuildSpineEars()/BuildSpineTail()
///     從未被 Build() 呼叫（已被註解掉，meimei 角色改用一般貼圖圖層
///     "耳朵1"/"耳朵2"/"_尾巴2"，已含在 LayerOrder 中以一般立繪圖層載入），
///     因此本移植版沒有、也不需要任何 Spine 相關程式碼。
/// </summary>
public partial class TrainingRoomRuntime : Node
{
	public override void _Ready()
	{
		NovelReader.ResetPlayback();
		SceneBgm.Install(this, SceneBgm.Night); // 進入自由操作場景時停止自動／快進。
		Name = "TrainingRoomRuntime";

		_canvas = new CanvasLayer { Layer = 10 };
		AddChild(_canvas);

		ConsumePendingFlags();
		Build();
		SetupCharShade();   // 人物包進光影 CanvasGroup（Layout.cs）
		SetupCameraLayers();   // HUD/選單搬到不縮放的 UI 層（見 Camera.cs）
		_camOffset = CamDefaultOffset();   // 預設鏡頭：置中後視角再往上移
		SetupHeadPat();   // 摸頭（HeadPat.cs）
		SetupMenuReader();
		SetupHelpButton();   // 右上角「?」操作說明（Help.cs）   // 常駐右上角「☰ 選單」鈕（跟其他頁面一樣）
		SetupLayoutEditor();   // 版面編輯器（F2，只動背景和 UI，不碰人物；見 Layout.cs）
		TaskLogger.Run(FadeInAsync(), "TrainingRoom.FadeInAsync");
		ApplyHandCursor();

		SetProcess(true);
	}

	// ── 滑鼠游標：進調教房換成手形，離開時改回系統預設 ─────────────────
	// Unity 原版其實沒有「進房間就把 OS 游標整個換成手形」的機制（原本只有拖曳互動時
	// 换成一張跟著滑鼠跑的手部 UI 小圖，見 Interaction.cs 的觸摸動畫），這是新增的功能：
	// 使用者要求進入這個高度互動的場景時，滑鼠整體用手形游標，離開時要換回原本的游標。
	// 快取縮小後的游標貼圖：胸部熱點的 hover 提示會頻繁呼叫 ApplyHandCursor() 復原游標
	// （見 Interaction.cs 的 MouseExited），每次都重新載入＋縮放貼圖沒必要，處理一次存起來。
	Texture2D _roomCursorTex;

	// 游標尺寸：使用者反應之前的 48px 太小，改成 80px。
	const float RoomCursorSize = 80f;
	// 「游標.png」已經是裁切好的食指點擊手勢圖（2014x1985），指尖大約在 (35,23) 這個像素位置
	// （用 Python 掃描找最靠近左上角的非透明像素量出來的），换算成比例存起來，
	// 不管最後游標縮放到多大，熱點都精準對在指尖上。
	static readonly Vector2 RoomCursorTipRatio = new Vector2(35f / 2014f, 23f / 1985f);

	void ApplyHandCursor()
	{
		_hoverCursorKind = 0;   // 綠點半透明手游標結束（Interaction.cs）
		if (_roomCursorTex == null)
		{
			// 使用者提供了新的手勢圖（食指指向的點擊手勢，比原本「手圖標」更清楚是可互動提示），
			// 已經存成 Sprites/游標.png——注意不要跟「手2」（角色右手臂/手部圖層本體貼圖）搞混，
			// 那張還要留給 ArmIK 用來畫實際的手，不能覆蓋掉。
			var tex = ResourceHelper.LoadTexAny("Sprites/游標");
			if (tex == null) return;

			var img = tex.GetImage();
			img.Resize((int)RoomCursorSize, (int)RoomCursorSize, Image.Interpolation.Lanczos);
			_roomCursorTex = ImageTexture.CreateFromImage(img);
		}
		var hotspot = RoomCursorTipRatio * RoomCursorSize;
		Input.SetCustomMouseCursor(_roomCursorTex, Input.CursorShape.Arrow, hotspot);
	}

	public override void _ExitTree()
	{
		// 離開調教房（切換場景／這個節點被移除）時，把游標改回系統預設，
		// 不要讓手形游標殘留到其他場景（例如小說對話畫面）。
		Input.SetCustomMouseCursor(null);
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;

		UpdateDuvet();   // 被子位置／配色／折疊參數（Inspector 可即時調）
		UpdateClothAlpha();   // 衣物不透明度（Inspector 可即時調）
		PinchProcess(dt);   // 捏臉回彈＋全年齡鎖（Pinch.cs）
		UpdateClimaxHud();
		UpdatePlayEnd(dt);   // 射精按鈕（PlayEnd.cs）
		UpdateBreathing(dt);
		UpdateBrowOperate(dt);   // 男主操作時眉毛上下反轉（Brow.cs）
		UpdateEmotes(dt);        // 氣泡表情：心情上升冒愛心／下降冒毛線團（Emote.cs）
		UpdateTearRings(dt);   // 眼淚圈（TearRings.cs）
		UpdateClothFollow();   // 衣服跟著身體／胸部變形（要在呼吸算完之後）
		UpdateStrap();   // 肩帶（Strap.cs）：剛體平移＋旋轉，要在 UpdateClothFollow 之後
		// 摸胸持續增長高潮度（單手/雙手不同起始間隔、越摸間隔越長），見
		// TrainingRoomRuntime.Interaction.cs 的 UpdateClimaxTouchGrowth() 說明。
		UpdateClimaxTouchGrowth(dt);
		// 胸部熱點（2/3）改成跟著胸部圖層實際位置走，要在 UpdateBreathing() 把這一幀的
		// 呼吸/摸胸位移算完之後才能抓到最新位置，見 Interaction.cs 的說明。
		UpdateBreastHotspotTracking();
		// M 腿膝蓋熱點（6/7）打開後要黏著 M 腿實際的膝蓋位置，同理要在 UpdateBreathing()
		// 之後呼叫，見 Interaction.cs 的 UpdateMLegKneeHotspotTracking() 說明。
		UpdateMLegKneeHotspotTracking();
		// B（雞雞/插入互動）熱點打開 M 腿後要黏著「m腿/小穴」的位置走，同理見
		// Interaction.cs 的 UpdateMLegJijiHotspotTracking() 說明。
		UpdateMLegJijiHotspotTracking();

		// ArmIK.LateProcess 相當於 Unity 的 LateUpdate：要在呼吸系統把手臂/手的 Position
		// 設定好之後才執行，這樣拖動中/回彈中/凍結中可以覆蓋呼吸的結果，閒置時
		// HandFollowWrist 也才能抓到「這一幀呼吸擺動後」手臂的真實位置去貼手腕。
		// 之前這裡完全沒有呼叫，導致 ArmIK 的拖動/回彈/手腕貼合全部是死碼、沒有任何效果。
		ArmIK?.LateProcess(dt);
		RightArmIK?.LateProcess(dt);
		UpdateIKDebugDots();

		UpdateSpasms(dt);
		UpdateSquirt(dt);
		UpdatePuddleFade(dt);
		UpdateJiji(dt);
		UpdateClothTop();   // 衣服順序／M 腿遮罩（Cloth.cs）
		UpdateCamera(dt);
		UpdateCharShade();   // 人物光影濾鏡（Layout.cs）
		UpdateNightFilter();   // 人物夜晚濾鏡（Layout.cs）
		UpdateF1OnlyUi();   // 高潮度／狀態卡／右側選單只在 F1 面板打開時顯示（Layout.cs）
		// 遮擋要放在最後：這一幀所有位移（雞雞、抽插、晃動、鏡頭）都算完才量位置，才會完整蓋住、不會慢一幀
		UpdateCensor();
	}

	async Task FadeInAsync()
	{
		var overlay = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
		overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		UiRoot.AddChild(overlay);
		overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		float t = 1f;
		while (t > 0f)
		{
			t -= (float)GetProcessDeltaTime() * 1.5f;
			overlay.Color = new Color(0, 0, 0, Mathf.Clamp(t, 0f, 1f));
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}
		overlay.QueueFree();
	}
}
