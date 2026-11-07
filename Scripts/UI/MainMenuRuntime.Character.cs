using Godot;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// MainMenuRuntime 第 5 段：角色立繪淡入淡出、CG 圖層動畫、選項流程、存讀檔
/// （對應 Unity 版 MainMenuRuntime.cs 第 3573~4466 行）
/// </summary>
public partial class MainMenuRuntime : Control
{
	// ═══════════════════════════════════════════════════════════
	//  本段專用欄位（part1 未宣告，若他段已宣告請刪除此區塊）
	// ═══════════════════════════════════════════════════════════

	// 存讀檔控制器（Unity 是同物件上的 Component，Godot 改為子節點）
	private SaveLoadController _saveLoadCtrl;

	// 滑鼠是否停在存讀檔按鈕上（停留時阻擋對話推進）
	private bool _hoveringSaveLoad;

	// 立繪貼圖的資源名稱（Unity 用 sprite.name 比對，Godot 需自行記錄）
	private string _charBodyTexName, _charFaceTexName, _char2TexName;

	// Spine 立繪（掛在 _charBodyImage 底下，跟著身體圖的位置／縮放／淡入淡出走）
	private SpineChar _spineChar;

	// 立繪預設尺寸（Unity 的 sizeDelta 650x1338）
	private static readonly Vector2 CharDefaultSize = new Vector2(650f, 1338f);

	// ═══════════════════════════════════════════════════════════
	//  角色立繪淡入 / 淡出
	// ═══════════════════════════════════════════════════════════

	/// <summary>
	/// 立繪淡入淡出（含身體 / 表情分層）
	/// name 格式："身體,表情"、"圖名,fade"、單一圖名
	/// </summary>
	async Task FadeChar(string name, bool fadeIn, CancellationToken ct = default)
	{
		float duration = 0.5f;

		if (fadeIn)
		{
			string bodyName = null;
			string faceName = null;
			string char2Name = null;
			bool forceFade = false;

			if (name.Contains(","))
			{
				var parts = name.Split(',');
				string lastPart = parts[parts.Length - 1].Trim().ToLower();
				if (lastPart == "fade")
				{
					forceFade = true;
					string imgName = parts[0].Trim();
					if (imgName.ToLower().Contains("linai"))
						bodyName = imgName;
					else if (imgName.Contains(" "))
						char2Name = imgName;
					else
						faceName = imgName;
				}
				else
				{
					bodyName = parts[0].Trim();
					faceName = parts.Length > 1 ? parts[1].Trim() : null;
				}
			}
			else if (name.ToLower().Contains("linai"))
			{
				bodyName = name.Trim();
			}
			else if (name.Contains(" "))
			{
				char2Name = name.Trim();
			}
			else
			{
				faceName = name.Trim();
			}

			// 要顯示圖片立繪 → 先關掉 Spine 立繪
			if (_spineChar != null && (bodyName != null || faceName != null))
				_spineChar.Visible = false;

			bool hasBody = false;
			bool hasFace = false;
			bool hasChar2 = false;

			if (bodyName != null)
			{
				var spr = LoadTex("Character/" + bodyName);
				if (spr != null)
				{
					_charBodyImage.Texture = spr;
					_charBodyTexName = bodyName;
					SetActive(_charBodyImage, true);   // Unity 的 image.enabled = true
					hasBody = true;
				}
			}

			if (faceName != null)
			{
				var spr = LoadTex("Character/" + faceName);
				if (spr != null)
				{
					_charFaceImage.Texture = spr;
					_charFaceTexName = faceName;
					SetActive(_charFaceImage, true);
					hasFace = true;
				}
			}
			else if (bodyName != null)
			{
				// 只換身體沒換表情 → 表情先設為全透明
				_charFaceImage.Modulate = new Color(1f, 1f, 1f, 0f);
			}

			if (char2Name != null)
			{
				var spr = LoadTex("Character/" + char2Name);
				if (spr != null)
				{
					_char2Image.Texture = spr;
					_char2TexName = char2Name;
					SetActive(_char2Image, true);
					hasChar2 = true;
				}
			}


			// Linai 淡入
			if (hasBody)
			{
				if (GetAlpha(_charBodyImage) < 0.9f || forceFade)
				{
					float startA = GetAlpha(_charBodyImage);
					await Lerp(FF(duration), p =>
					{
						float a = Mathf.Lerp(startA, 1f, p);
						_charBodyImage.Modulate = new Color(1f, 1f, 1f, a);
						if (hasFace) _charFaceImage.Modulate = new Color(1f, 1f, 1f, a);
					}, ct);
				}
				_charBodyImage.Modulate = Colors.White;
				if (hasFace) _charFaceImage.Modulate = Colors.White;
			}
			else if (hasFace)
			{
				if (forceFade)
				{
					float startA = GetAlpha(_charFaceImage);
					await Lerp(FF(duration), p =>
					{
						_charFaceImage.Modulate = new Color(1f, 1f, 1f, Mathf.Lerp(startA, 1f, p));
					}, ct);
				}
				_charFaceImage.Modulate = Colors.White;
			}

			// 第二角色淡入
			if (hasChar2)
			{
				if (forceFade)
				{
					float startA = GetAlpha(_char2Image);
					await Lerp(FF(duration), p =>
					{
						_char2Image.Modulate = new Color(1f, 1f, 1f, Mathf.Lerp(startA, 1f, p));
					}, ct);
				}
				_char2Image.Modulate = Colors.White;
			}
		}
		else
		{
			float bodyStartA  = GetAlpha(_charBodyImage);
			float faceStartA  = GetAlpha(_charFaceImage);
			float char2StartA = GetAlpha(_char2Image);

			await Lerp(FF(duration), p =>
			{
				_charBodyImage.Modulate = new Color(1f, 1f, 1f, Mathf.Lerp(bodyStartA, 0f, p));
				_charFaceImage.Modulate = new Color(1f, 1f, 1f, Mathf.Lerp(faceStartA, 0f, p));
				_char2Image.Modulate    = new Color(1f, 1f, 1f, Mathf.Lerp(char2StartA, 0f, p));
			}, ct);

			_charBodyImage.Modulate = new Color(1f, 1f, 1f, 0f);
			_charFaceImage.Modulate = new Color(1f, 1f, 1f, 0f);
			_char2Image.Modulate    = new Color(1f, 1f, 1f, 0f);
			_charBodyImage.Texture  = null;
			_charFaceImage.Texture  = null;
			_char2Image.Texture     = null;
			_charBodyTexName = null;
			_charFaceTexName = null;
			_char2TexName    = null;

			// 位置與尺寸復原（Unity 的 anchoredPosition = zero、sizeDelta = 650x1338）
			// 注意：立繪節點的錨點是「底部置中」(AnchorTop/Bottom = 1)，所以必須用
			// DlgSetCharRect 這組底部對齊的算式。之前用的 SetAnchoredPos 是「中心對齊」
			// 的通用版（OffsetTop = -高/2、OffsetBottom = +高/2），套在底部錨點上會讓
			// 整張立繪往下位移半個身高（669px），畫面上就只剩一顆頭。
			DlgSetCharRect(_charBodyImage, Vector2.Zero, CharDefaultSize);
			DlgSetCharRect(_charFaceImage, Vector2.Zero, CharDefaultSize);
			DlgSetCharRect(_char2Image,    Vector2.Zero, CharDefaultSize);
			SetScale(_charBodyImage, 1f);
			SetScale(_charFaceImage, 1f);
			SetScale(_char2Image,    1f);
			if (_spineChar != null) _spineChar.Visible = false;
		}
	}

	// ── Spine 立繪 ──────────────────────────────────────────────
	static bool HasSpine(PrologueLine l)
		=> !string.IsNullOrWhiteSpace(l.chOutfit) || !string.IsNullOrWhiteSpace(l.chSkin)
		|| !string.IsNullOrWhiteSpace(l.chHair)   || !string.IsNullOrWhiteSpace(l.chSocks)
		|| !string.IsNullOrWhiteSpace(l.chFace)   || !string.IsNullOrWhiteSpace(l.chAnim);

	/// <summary>套用這一行的 Spine 設定；尚未顯示時淡入（instant=true 則直接顯示）</summary>
	async Task SpineApply(PrologueLine l, bool instant, CancellationToken ct = default)
	{
		if (_spineChar == null || !_spineChar.EnsureLoaded()) return;

		string outfit = !string.IsNullOrWhiteSpace(l.chOutfit) ? l.chOutfit : l.chSkin;
		_spineChar.Apply(outfit, l.chHair, l.chSocks, l.chFace, l.chAnim);

		// 已經在畫面上 → 只換外觀，不重新淡入
		if (_spineChar.Visible && GetAlpha(_charBodyImage) > 0.01f) return;

		// 圖片立繪還在 → 先清掉
		if (_charBodyImage.Texture != null || _charFaceImage.Texture != null)
		{
			_charBodyImage.Texture = null;
			_charFaceImage.Texture = null;
			_charBodyTexName = null;
			_charFaceTexName = null;
			_charFaceImage.Modulate = new Color(1f, 1f, 1f, 0f);
			_charBodyImage.Modulate = new Color(1f, 1f, 1f, 0f);
		}

		_spineChar.Visible = true;
		if (instant)
		{
			_charBodyImage.Modulate = Colors.White;
			return;
		}

		float startA = GetAlpha(_charBodyImage);
		await Lerp(FF(0.5f), p =>
		{
			_charBodyImage.Modulate = new Color(1f, 1f, 1f, Mathf.Lerp(startA, 1f, p));
		}, ct);
		_charBodyImage.Modulate = Colors.White;
	}

	// ── 選擇性隱藏立繪 ──────────────────────────────────────────
	/// <summary>只淡出指定名稱的立繪（names 以逗號分隔）</summary>
	async Task FadeCharSelective(string names, CancellationToken ct = default)
	{
		float duration = 0.5f;
		var parts = names.Split(',');
		bool hideBody = false;
		bool hideFace = false;
		bool hideChar2 = false;

		foreach (var p in parts)
		{
			string n = p.Trim();
			// Unity 用 sprite.name 比對，Godot 改用記錄下來的資源名稱
			if (_charBodyImage.Texture != null && _charBodyTexName == n)
				hideBody = true;
			if (_charFaceImage.Texture != null && _charFaceTexName == n)
				hideFace = true;
			if (_char2Image.Texture != null && _char2TexName == n)
				hideChar2 = true;
		}
		if (!hideBody && !hideFace && !hideChar2) return;

		float bodyStartA  = GetAlpha(_charBodyImage);
		float faceStartA  = GetAlpha(_charFaceImage);
		float char2StartA = GetAlpha(_char2Image);

		await Lerp(FF(duration), p =>
		{
			if (hideBody)  _charBodyImage.Modulate = new Color(1f, 1f, 1f, Mathf.Lerp(bodyStartA, 0f, p));
			if (hideFace)  _charFaceImage.Modulate = new Color(1f, 1f, 1f, Mathf.Lerp(faceStartA, 0f, p));
			if (hideChar2) _char2Image.Modulate    = new Color(1f, 1f, 1f, Mathf.Lerp(char2StartA, 0f, p));
		}, ct);

		if (hideBody)
		{
			_charBodyImage.Modulate = new Color(1f, 1f, 1f, 0f);
			_charBodyImage.Texture  = null;
			_charBodyTexName = null;
		}
		if (hideFace)
		{
			_charFaceImage.Modulate = new Color(1f, 1f, 1f, 0f);
			_charFaceImage.Texture  = null;
			_charFaceTexName = null;
		}
		if (hideChar2)
		{
			_char2Image.Modulate = new Color(1f, 1f, 1f, 0f);
			_char2Image.Texture  = null;
			_char2TexName = null;
		}
		// 身體與表情都清空了 → 位置尺寸復原
		if (_charBodyImage.Texture == null && _charFaceImage.Texture == null)
		{
			DlgSetCharRect(_charBodyImage, Vector2.Zero, CharDefaultSize);
			DlgSetCharRect(_charFaceImage, Vector2.Zero, CharDefaultSize);
		}
		if (_char2Image.Texture == null)
		{
			DlgSetCharRect(_char2Image, Vector2.Zero, CharDefaultSize);
		}
	}

	/// <summary>處理 hideCH 指令："" / "0" / "1" = 全部淡出，其他 = 指定名稱淡出</summary>
	async Task HandleHideCH(string hideCH, CancellationToken ct = default)
	{
		string val = (hideCH ?? "").Trim();
		if (val == "" || val == "0" || val == "1")
			await FadeChar("", false, ct);
		else
			await FadeCharSelective(val, ct);
	}

	// ── 第二角色顯示（淡入）──────────────────────────────────────
	async Task FadeChar2Show(string name, CancellationToken ct = default)
	{
		float duration = 0.5f;
		bool forceFade = false;
		string imgName = name.Trim();

		if (imgName.Contains(","))
		{
			var parts = imgName.Split(',');
			string last = parts[parts.Length - 1].Trim().ToLower();
			if (last == "fade") { forceFade = true; imgName = parts[0].Trim(); }
		}

		var spr = LoadTex("Character/" + imgName);
		if (spr == null) return;
		_char2Image.Texture = spr;
		_char2TexName = imgName;
		SetActive(_char2Image, true);

		if (forceFade)
		{
			float startA = GetAlpha(_char2Image);
			await Lerp(FF(duration), p =>
			{
				_char2Image.Modulate = new Color(1f, 1f, 1f, Mathf.Lerp(startA, 1f, p));
			}, ct);
		}
		_char2Image.Modulate = Colors.White;
	}

	// ── 第二角色隱藏（淡出）──────────────────────────────────────
	/// <summary>參數 fadeIn 沿用 Unity 原簽章（實際上一律做淡出）</summary>
	async Task FadeChar2(bool fadeIn, CancellationToken ct = default)
	{
		float duration = 0.5f;
		float startA = GetAlpha(_char2Image);

		await Lerp(FF(duration), p =>
		{
			_char2Image.Modulate = new Color(1f, 1f, 1f, Mathf.Lerp(startA, 0f, p));
		}, ct);

		_char2Image.Modulate = new Color(1f, 1f, 1f, 0f);
		_char2Image.Texture  = null;
		_char2TexName = null;
		DlgSetCharRect(_char2Image, Vector2.Zero, CharDefaultSize);
	}

	// ═══════════════════════════════════════════════════════════
	//  音訊
	// ═══════════════════════════════════════════════════════════

	/// <summary>延遲播放音效</summary>
	async Task PlaySEDelayed(AudioStream clip, float delay, CancellationToken ct = default)
	{
		await Wait(delay, ct);
		if (clip == null || _seSource == null) return;
		_seSource.Stream = clip;   // 對應 Unity 的 PlayOneShot
		_seSource.Play();
	}

	/// <summary>音訊淡出後停止，並把音量還原成原值</summary>
	async Task FadeOutAudio(AudioStreamPlayer src, float duration, CancellationToken ct = default)
	{
		if (src == null || !IsInstanceValid(src)) return;
		float startVol = GetVol(src);

		await Lerp(FF(duration), p => SetVol(src, Mathf.Lerp(startVol, 0f, p)), ct);

		src.Stop();
		SetVol(src, startVol);
	}

	// ME 播完後恢復 BGM 音量
	async Task RestoreBGMAfterME(float targetVol, CancellationToken ct = default)
	{
		await WaitWhile(() => _meSource != null && IsInstanceValid(_meSource) && _meSource.Playing, ct);

		await Lerp(0.5f, p => SetVol(_bgmSource, Mathf.Lerp(0.2f, targetVol, p)), ct);

		SetVol(_bgmSource, targetVol);
	}

	// ═══════════════════════════════════════════════════════════
	//  CG 圖層動畫
	// ═══════════════════════════════════════════════════════════

	/// <summary>
	/// CG 單層位移 / 縮放動畫（供平行呼叫）
	/// Unity 的 offsetMin/offsetMax 同值 = 整塊平移；Godot 對應四個 Offset*，Y 需取負
	/// </summary>
	async Task AnimCGLayer(int layer, DialogueSystem.CGLayerCmd cmd, System.Action onDone,
		CancellationToken ct = default)
	{
		var cgRT = _cgImages[layer];
		if (cgRT == null || !IsInstanceValid(cgRT)) { onDone?.Invoke(); return; }

		var cgSP = DialogueSystem.ParseValueWithDuration(cmd.scale);
		var cgXP = DialogueSystem.ParseValueWithDuration(cmd.x);
		var cgYP = DialogueSystem.ParseValueWithDuration(cmd.y);

		float curScale = cgRT.Scale.X;
		float targetScale = !string.IsNullOrEmpty(cmd.scale) && cgSP.value > 0f
			? cgSP.value : curScale;
		bool hasCgX = !string.IsNullOrEmpty(cmd.x);
		bool hasCgY = !string.IsNullOrEmpty(cmd.y);

		// 目前的位移（Unity 語意：Y 朝上，故由 Godot 的 OffsetTop 取負還原）
		float curOffX = cgRT.OffsetLeft;
		float curOffY = -cgRT.OffsetTop;
		float tgtOffX = hasCgX ? cgXP.value : curOffX;
		float tgtOffY = hasCgY ? cgYP.value : curOffY;

		// 計算動畫持續時間：有明確指定幀數的欄位才納入計算
		float cgDur = 0f;
		bool hasExplicitDur = false;
		if (!string.IsNullOrEmpty(cmd.scale)) { cgDur = Mathf.Max(cgDur, cgSP.durationSec); hasExplicitDur = true; }
		if (hasCgX) { cgDur = Mathf.Max(cgDur, cgXP.durationSec); hasExplicitDur = true; }
		if (hasCgY) { cgDur = Mathf.Max(cgDur, cgYP.durationSec); hasExplicitDur = true; }
		// 沒有任何欄位指定幀數時使用預設 0.4 秒
		if (!hasExplicitDur) cgDur = 0.4f;

		bool needMove = Mathf.Abs(targetScale - curScale) > 0.001f
					 || Mathf.Abs(tgtOffX - curOffX) > 0.1f
					 || Mathf.Abs(tgtOffY - curOffY) > 0.1f;
		if (needMove)
		{
			if (cgDur <= 0f)
			{
				// 幀數為 0 → 瞬移到指定位置
				SetScale(cgRT, targetScale);
				ApplyCGOffset(cgRT, tgtOffX, tgtOffY);
			}
			else
			{
				float startScale = curScale;
				float startOffX  = curOffX;
				float startOffY  = curOffY;

				await Lerp(FF(cgDur), t =>
				{
					float p = Mathf.SmoothStep(0f, 1f, t);
					float s = Mathf.Lerp(startScale, targetScale, p);
					SetScale(cgRT, s);
					float ox = Mathf.Lerp(startOffX, tgtOffX, p);
					float oy = Mathf.Lerp(startOffY, tgtOffY, p);
					ApplyCGOffset(cgRT, ox, oy);
				}, ct);

				SetScale(cgRT, targetScale);
				ApplyCGOffset(cgRT, tgtOffX, tgtOffY);
			}
		}
		onDone?.Invoke();
	}

	/// <summary>
	/// 套用 CG 平移（對應 Unity 的 offsetMin = offsetMax = (x, y)）
	/// Unity Y 朝上、Godot Y 朝下 → Top/Bottom 取負
	/// </summary>
	private static void ApplyCGOffset(Control cg, float x, float y)
	{
		if (cg == null || !IsInstanceValid(cg)) return;
		cg.OffsetLeft   = x;
		cg.OffsetRight  = x;
		cg.OffsetTop    = -y;
		cg.OffsetBottom = -y;
	}

	// CG 淡入 / 淡出包裝：結束後呼叫 callback（供平行淡出用）
	async Task FadeCGThen(int layer, string cgName, bool fadeIn, System.Action onDone,
		CancellationToken ct = default)
	{
		await FadeCG(layer, cgName, fadeIn, 0.6f, ct);
		onDone?.Invoke();
	}

	// ═══════════════════════════════════════════════════════════
	//  魔王印記選擇流程
	// ═══════════════════════════════════════════════════════════

	async Task RunSealChoiceFlow(CancellationToken ct = default)
	{
		if (_contentText != null) _contentText.Text = "";
		if (_speakerText != null) _speakerText.Text = "";

		// ── Step 1：魔王印記選擇（2 個按鈕）─────────────────────
		_reader?.Stop(); // 選擇點需要玩家手動選擇。
		_choiceResult = -1;
		var panel1 = BuildChoicePanel(
			_dialoguePanel,
			"到底要不要發動魔王印記保護雙紅？",
			new[] {
				"【發動魔王印記】\n（雙紅不會受到魔物的特殊攻擊）",
                "【不發動魔王印記】\n（雙紅可能受到魔物的特殊攻擊）"
			}
		);
		await WaitUntil(() => _choiceResult >= 0, ct);

		// Unity 的 PlayerPrefs → Godot 用 ConfigFile 存到 user:// 目錄
		var flagCfg = new ConfigFile();
		flagCfg.Load("user://gameflags.cfg");   // 檔案不存在也沒關係，直接寫新的
		flagCfg.SetValue("flags", "seal_active", _choiceResult == 0 ? 1 : 0);
		flagCfg.Save("user://gameflags.cfg");

		Kill(panel1);

		// ── Step 2：雙紅回來的對話 ───────────────────────────────
		await PlayHardcodedLines(new string[][] {
			new[]{"雙紅", "村長……我回來了。魔物都已經解決了。"},
			new[]{"魔王", "妳沒受傷吧？"},
			new[]{"雙紅", "沒有受傷……可是，祭典已經結束了，大家都散了。"},
			new[]{"雙紅", "這樣我就不能在祭典上完成交配，不能當村長的妻子了……"},
		}, ct);

		// ── Step 3：最終選擇（3 個按鈕）────────────────────────────
		if (_contentText != null) _contentText.Text = "";
		if (_speakerText != null) _speakerText.Text = "";
		_reader?.Stop(); // 選擇點需要玩家手動選擇。
		_choiceResult = -1;
		var panel2 = BuildChoicePanel(
			_dialoguePanel,
			"【請選擇接下來的劇情走向】",
			new[] {
				"今天還沒結束呢，現在一樣可以做",
				"等妳真正明白時",
                "現在也可以做，不過換個方式"
			}
		);
		await WaitUntil(() => _choiceResult >= 0, ct);
		int finalChoice = _choiceResult;
		Kill(panel2);

		// 使用者反應點擊開始後、進調教室前的黑畫面等太久：不管下面選哪個結局，
		// 接下來一定會呼叫 FadeToBlackAndLoad("TrainingRoom", ...)，而 TrainingRoomRuntime
		// 場景 _Ready() 裡的 Build() 要同步讀取＋解壓縮 70 幾張立繪貼圖，全部擠在
		// ChangeSceneToFile 那一瞬間才做，畫面才會卡住那麼久。這裡提前在背景執行緒
		// 丟出所有立繪/背景貼圖的預先載入請求，跟接下來還要播好幾句結局對話的時間重疊，
		// 對話播完、真的切場景時，貼圖大多已經在 Godot 資源快取裡，Build() 幾乎不用再等。
		foreach (var relPath in TrainingRoomRuntime.GetPreloadSpritePaths())
			ResourceHelper.PreloadTexAny(relPath);
		// 場景檔本身（.tscn）也是 ChangeSceneToFile 內部要同步讀的資源，順便一起預先丟背景載入。
		ResourceLoader.LoadThreadedRequest("res://Scenes/TrainingRoom.tscn");

		// ── Step 4：依選擇播放結局 ───────────────────────────────
		if (finalChoice == 0)
		{
			await PlayHardcodedLines(new string[][] {
				new[]{"魔王", "今天可還沒結束呢。祭典雖然散了，但交配季的規矩還在，我們現在一樣可以做。"},
				new[]{"雙紅", "村長……真的可以嗎？"},
				new[]{"魔王", "過來吧，我現在就讓妳成為我真正的妻子。"},
			}, ct);
			await FadeToBlackAndLoad("TrainingRoom", ct);
		}
		else if (finalChoice == 1)
		{
			await PlayHardcodedLines(new string[][] {
				new[]{"魔王", "今天發生了太多事，妳討伐魔物也累了，交配的事就留到下次吧。"},
				new[]{"雙紅", "可是……村長，我現在身體還是好熱，發情期還沒有退……如果不做的話，我就不能算是一個合格的狐狸村女戰士了，而且我也想成為你的妻子……"},
				new[]{"魔王", "聽好，雙紅。妳不需要用這種方式來證明自己的價值。我希望等妳真正明白自己的心意，而不是被發情期的本能或是村子的義務驅使時，我們再來做這件事。"},
				new[]{"雙紅", "村長……"},
				new[]{"魔王", "去休息吧。"},
				new[]{"雙紅", "……我知道了。村長晚安。"},
				new[]{"旁白", "雙紅雖然帶著一絲失落與難耐，但還是乖乖聽從了指示，躺上床休息，並在魔王的陪伴下漸漸入睡。房間內再次恢復了寧靜，魔王獨自看著雙紅熟睡的臉龐。"},
				new[]{"魔王", "真是個傻丫頭……"},
				new[]{"魔王", "等妳真正知道一切真相的那一天，要是還願意做這個決定的話……到時候再說吧。"},
			}, ct);
			await FadeToBlackAndLoad("TrainingRoom", ct);
		}
		else
		{
			await PlayHardcodedLines(new string[][] {
				new[]{"魔王", "現在也可以做，不過……我們換個方式。"},
				new[]{"雙紅", "換個方式？交配……還有其他方式嗎？"},
				new[]{"魔王", "跟我來就知道了。"},
				new[]{"旁白", "魔王根據村長的記憶，帶著雙紅來到了隱藏在宅邸地下的秘密調教房。一推開門，極度濃烈的迷情狐尾草氣味撲鼻而來。"},
				new[]{"魔王（內心）", "這老傢伙居然在地下弄了這種地方……。"},
				new[]{"雙紅", "村長……這裡是哪裡？這些奇怪的東西是做什麼用的？你什麼時候在地下弄了這樣的一個房間？"},
				new[]{"魔王", "這當然是專門為妳準備的。"},
				new[]{"雙紅", "為我準備的……？"},
				new[]{"魔王", "是的，為了你的第一次特地準備的，去那張床上躺著吧。"},
			}, ct);
			await FadeToBlackAndLoad("TrainingRoom", ct);
		}
	}

	// 建立選擇面板（疊加在 parent 之上）
	Control BuildChoicePanel(Node parent, string title, string[] labels)
	{
		if (_contentText != null) _contentText.Text = "";
		if (_speakerText != null) _speakerText.Text = "";

		float boxH = 100f + labels.Length * 90f;

		// 半透明黑幕（撐滿父節點）
		var panel = new ColorRect
		{
			Name        = "ChoicePanel",
			Color       = new Color(0f, 0f, 0f, 0.75f),
			MouseFilter = MouseFilterEnum.Stop,   // 擋住底下的點擊
		};
		parent.AddChild(panel);
		ApplyStretchAnchor(panel, Vector2.Zero, Vector2.One, Vector2.Zero, Vector2.Zero);

		// 中央方框
		var box = new ColorRect
		{
			Name        = "Box",
			Color       = new Color(0.05f, 0.02f, 0.09f, 0.97f),
			MouseFilter = MouseFilterEnum.Stop,
		};
		panel.AddChild(box);
		ApplyPointAnchor(box, V2(0.5f, 0.5f), V2(0.5f, 0.5f), Vector2.Zero, V2(740f, boxH));

		// 頂部裝飾線（貼齊方框上緣，高 2px）
		var lineGO = new ColorRect
		{
			Name        = "TopLine",
			Color       = new Color(0.75f, 0.50f, 0.15f, 0.6f),
			MouseFilter = MouseFilterEnum.Ignore,
		};
		box.AddChild(lineGO);
		ApplyStretchAnchor(lineGO, V2(0f, 1f), V2(1f, 1f), V2(0f, -2f), Vector2.Zero);

		// 標題
		// Unity 的 pivot 在上緣（0.5,1），helper 以中心為樞紐 → Y 再往下補半個高度
		var tTx = Txt(box, "Title", title, 22, new Color(1f, 0.85f, 0.40f), UAlign.MiddleCenter,
			V2(0.5f, 1f), V2(0.5f, 1f), V2(0f, -18f - 54f * 0.5f), V2(700f, 54f));
		tTx.AutowrapMode = TextServer.AutowrapMode.Arbitrary;

		// 按鈕顏色（最多3種）
		Color[] btnCols = {
			new Color(0.50f, 0.12f, 0.20f),
			new Color(0.14f, 0.20f, 0.46f),
			new Color(0.22f, 0.14f, 0.42f),
		};

		for (int i = 0; i < labels.Length; i++)
		{
			int idx = i;
			Color col = i < btnCols.Length ? btnCols[i] : btnCols[btnCols.Length - 1];

			var button = new Button { Name = "Choice" + i, Text = "", Flat = false };
			box.AddChild(button);
			// Unity pivot 在上緣 → Y 補半個按鈕高度（78 / 2）
			ApplyPointAnchor(button, V2(0.5f, 1f), V2(0.5f, 1f),
				V2(0f, -82f - i * 90f - 78f * 0.5f), V2(680f, 78f));

			UIStyle.ApplyButtonStyle(button);

			// Unity 的 ColorBlock：normal / highlighted / pressed
			Color hoverCol = new Color(Mathf.Min(col.R + 0.18f, 1f), Mathf.Min(col.G + 0.12f, 1f), Mathf.Min(col.B + 0.12f, 1f));
			Color pressCol = new Color(col.R * 0.8f, col.G * 0.8f, col.B * 0.8f);
			button.AddThemeStyleboxOverride("normal",  MakeRoundRect(0, col, col, 0));
			button.AddThemeStyleboxOverride("hover",   MakeRoundRect(0, hoverCol, hoverCol, 0));
			button.AddThemeStyleboxOverride("pressed", MakeRoundRect(0, pressCol, pressCol, 0));
			button.AddThemeStyleboxOverride("focus",   MakeRoundRect(0, col, col, 0));

			button.Pressed += () => _choiceResult = idx;

			// 按鈕文字（左右內縮 16、上下內縮 4）
			var lTx = new Label
			{
				Name                = "T",
				Text                = labels[i],
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment   = VerticalAlignment.Center,
				AutowrapMode        = TextServer.AutowrapMode.Arbitrary,
				MouseFilter         = MouseFilterEnum.Ignore,
			};
			lTx.AddThemeFontSizeOverride("font_size", 20);
			lTx.AddThemeColorOverride("font_color", new Color(0.95f, 0.92f, 0.95f));
			lTx.AddThemeConstantOverride("line_spacing", 8);   // 對應 Unity 的 lineSpacing = 1.4
			button.AddChild(lTx);
			ApplyStretchAnchor(lTx, Vector2.Zero, Vector2.One, V2(16f, 4f), V2(-16f, -4f));
		}

		return panel;
	}

	// ── 選擇框按鈕（序章 JSON 的 "choice"）Inspector 可調參數，Play 中即時生效 ──
	[ExportGroup("選擇框按鈕")]
	[Export] public Vector2 ChoiceBtnSize = new Vector2(560f, 76f);   // 單顆按鈕寬、高
	[Export] public float   ChoiceBtnGap  = 34f;                      // 按鈕之間的間距
	[Export] public Vector2 ChoicePos     = new Vector2(0f, -140f);   // 整組按鈕相對畫面中央的位移（負 Y = 往上）
	[Export] public int     ChoiceFontSize = 28;                      // 按鈕文字大小

	// 只有按鈕的選擇面板：無黑框、無遮罩、無標題，按鈕垂直排列在畫面中央
	// （序章 JSON 的 "choice" 使用；舊的 BuildChoicePanel 保持原樣給其他流程用）
	Control BuildButtonOnlyChoice(Node parent, string[] labels)
	{
		// 全透明容器（撐滿父節點，擋住底下點擊，避免選擇時誤觸對話推進）
		var panel = new Control
		{
			Name        = "ChoicePanel",
			MouseFilter = MouseFilterEnum.Stop,
		};
		parent.AddChild(panel);
		ApplyStretchAnchor(panel, Vector2.Zero, Vector2.One, Vector2.Zero, Vector2.Zero);

		// 垂直排列容器
		var vbox = new VBoxContainer { Name = "Buttons", MouseFilter = MouseFilterEnum.Ignore };
		panel.AddChild(vbox);

		Color[] btnCols = {
			new Color(0.50f, 0.12f, 0.20f),
			new Color(0.14f, 0.20f, 0.46f),
			new Color(0.22f, 0.14f, 0.42f),
		};

		var buttons = new System.Collections.Generic.List<Button>();
		var texts   = new System.Collections.Generic.List<Label>();

		for (int i = 0; i < labels.Length; i++)
		{
			int idx = i;
			Color col = i < btnCols.Length ? btnCols[i] : btnCols[btnCols.Length - 1];

			var button = new Button { Name = "Choice" + i, Text = "", Flat = false };
			vbox.AddChild(button);

			UIStyle.ApplyButtonStyle(button);

			Color hoverCol = new Color(Mathf.Min(col.R + 0.18f, 1f), Mathf.Min(col.G + 0.12f, 1f), Mathf.Min(col.B + 0.12f, 1f));
			Color pressCol = new Color(col.R * 0.8f, col.G * 0.8f, col.B * 0.8f);
			button.AddThemeStyleboxOverride("normal",  MakeRoundRect(0, col, col, 0));
			button.AddThemeStyleboxOverride("hover",   MakeRoundRect(0, hoverCol, hoverCol, 0));
			button.AddThemeStyleboxOverride("pressed", MakeRoundRect(0, pressCol, pressCol, 0));
			button.AddThemeStyleboxOverride("focus",   MakeRoundRect(0, col, col, 0));

			button.Pressed += () => _choiceResult = idx;

			var lTx = new Label
			{
				Name                = "T",
				Text                = labels[i],
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment   = VerticalAlignment.Center,
				AutowrapMode        = TextServer.AutowrapMode.Arbitrary,
				MouseFilter         = MouseFilterEnum.Ignore,
			};
			lTx.AddThemeColorOverride("font_color", new Color(0.95f, 0.92f, 0.95f));
			button.AddChild(lTx);
			ApplyStretchAnchor(lTx, Vector2.Zero, Vector2.One, V2(16f, 4f), V2(-16f, -4f));

			buttons.Add(button);
			texts.Add(lTx);
		}

		// 把 Inspector 數值套到按鈕（建立時先套一次，之後每幀同步，所以 Play 中改 Inspector 即時生效）
		void ApplyLayout()
		{
			if (!IsInstanceValid(vbox)) return;
			int n = buttons.Count;
			float totalH = n * ChoiceBtnSize.Y + Mathf.Max(n - 1, 0) * ChoiceBtnGap;
			vbox.AddThemeConstantOverride("separation", (int)ChoiceBtnGap);
			ApplyPointAnchor(vbox, V2(0.5f, 0.5f), V2(0.5f, 0.5f), ChoicePos, V2(ChoiceBtnSize.X, totalH));
			for (int k = 0; k < n; k++)
			{
				buttons[k].CustomMinimumSize = ChoiceBtnSize;
				texts[k].AddThemeFontSizeOverride("font_size", ChoiceFontSize);
			}
		}
		ApplyLayout();

		var tree = GetTree();
		Action onFrame = ApplyLayout;
		tree.ProcessFrame += onFrame;
		panel.TreeExiting += () => tree.ProcessFrame -= onFrame;

		return panel;
	}

	// 播放硬編碼對話行（每行 = { speaker, text }）
	async Task PlayHardcodedLines(string[][] lines, CancellationToken ct = default)
	{
		foreach (var line in lines)
		{
			string speaker = line.Length > 0 ? line[0] : "";
			string text    = line.Length > 1 ? line[1] : "";
			if (_speakerText != null) _speakerText.Text = speaker;
			if (_nameBarImage != null && IsInstanceValid(_nameBarImage))
			{
				if (speaker.Contains("莉奈") || speaker.Contains("Linai"))
					_nameBarImage.Modulate = new Color(0.85f, 0.65f, 0.75f, 0.95f);
				else if (speaker.Contains("男主") || speaker.Contains("主角"))
					_nameBarImage.Modulate = new Color(0.55f, 0.70f, 0.90f, 0.95f);
				else
					_nameBarImage.Modulate = new Color(0.65f, 0.12f, 0.25f, 0.95f);
			}
			await TypeLine(text, ct);

			_waitingClick = true;
			_autoAdvance  = false;
			if (_autoPlaySpeed > 0) _ = AutoAdvanceTimer(ct);
			await WaitUntil(() => !_waitingClick, ct);
			await NextFrame(ct);
		}
	}

	// ═══════════════════════════════════════════════════════════
	//  劇情資料結構
	// ═══════════════════════════════════════════════════════════

	/// <summary>序章單行台詞（欄位名稱與 JSON 鍵一致，請勿改名）</summary>
	class PrologueLine
	{
		public string speaker;
		public string text;
		public string spriteName;
		public string bgName;
		public float bgScale;   // 背景縮放（1.0=原大，2.0=放大200%，0或不填=不改）
		public string bgX;      // 背景X位移（像素，不填=不改，"0"=回原點）
		public string bgY;      // 背景Y位移（像素，不填=不改，"0"=回原點）
		public string showCG;   // 淡入 CG（Resources/CG pictures/<name>）
		public string hideCG;   // 非空 → 台詞結束後淡出 CG
		public string cgScale;  // CG 縮放（"1.5" 或 "1.5, 30" 帶幀數）
		public string cgX;      // CG X位移（"200" 或 "-200, 60" 帶幀數）
		public string cgY;      // CG Y位移（"100" 或 "-100, 60" 帶幀數）
		// CG 多層控制（1~10，編號越大圖層越上面）
		public string showCG1; public string hideCG1; public string cgScale1; public string cgX1; public string cgY1;
		public string showCG2; public string hideCG2; public string cgScale2; public string cgX2; public string cgY2;
		public string showCG3; public string hideCG3; public string cgScale3; public string cgX3; public string cgY3;
		public string showCG4; public string hideCG4; public string cgScale4; public string cgX4; public string cgY4;
		public string showCG5; public string hideCG5; public string cgScale5; public string cgX5; public string cgY5;
		public string showCG6; public string hideCG6; public string cgScale6; public string cgX6; public string cgY6;
		public string showCG7; public string hideCG7; public string cgScale7; public string cgX7; public string cgY7;
		public string showCG8; public string hideCG8; public string cgScale8; public string cgX8; public string cgY8;
		public string showCG9; public string hideCG9; public string cgScale9; public string cgX9; public string cgY9;
		public string showCG10; public string hideCG10; public string cgScale10; public string cgX10; public string cgY10;
		public string showCH;   // 立繪顯示（Resources/Character/<name>）
		public string hideCH;   // 非空 → 立繪淡出
		// Spine 立繪：只要有下面任何一個欄位，就改用 Spine 角色顯示（沒寫的欄位維持上一個狀態）
		public string chOutfit; // 服裝皮膚：school / school2 / shirt / sleep / sleep2 / nude
		public string chSkin;   // chOutfit 的別名
		public string chHair;   // 髮型動畫：twin / long / none（無後髮）
		public string chSocks;  // 襪子動畫：white / black / none
		public string chFace;   // 表情動畫：angry1 / laugh / cry ...
		public string chAnim;   // 身體基礎動畫（預設 idle）
		public bool chAsync;    // true → 立繪位移/縮放不等待、也不受優先 fadeout 延後（邊移動邊白屏/邊打字）
		public string chScale;  // 立繪縮放（"1.5" 或 "1.5, 30" 帶幀數）
		public string chX;      // 立繪X位移（"200" 或 "-200, 60" 帶幀數）
		public string chY;      // 立繪Y位移（"100" 或 "-100, 60" 帶幀數）
		// 第二角色控制
		public string showCH2;
		public string hideCH2;
		public string chScale2;
		public string chX2;
		public string chY2;
		public string bgm;      // 播放BGM（Resources/Audio/bgm/<name>），"stop"=停止
		public string se;       // 播放SE（Resources/Audio/se/<name>）
		public string me;       // 播放ME（Resources/Audio/me/<name>）
		public string bgAnim;   // 背景動畫（"approach"=靠近效果，"stop"=停止）
		public string breath;   // 第一人稱呼吸晃動（"on" / "on, 強度" / "off"），套用在全部 CG 圖層
		public float autoNext;  // 自動跳下一行（幀數，60=1秒，0或不填=需要點擊）
		public bool cgAsync;     // true → CG 鏡頭移動不等待，邊運鏡邊打字（台詞結束後才等鏡頭跑完）
		public string fadeout;       // 非空 → 畫面淡入黑屏（預設黑色，向下相容）
		public string fadeoutBlack;  // "fadeout black": "幀數" → 黑屏
		public string fadeoutWhite;  // "fadeout white": "幀數" → 白屏
		public string fadein;        // 非空 → 恢復畫面（顏色自動對應上次 fadeout）
		public float wait;      // 暫停流程（幀數，60幀=1秒）
		public bool r18Only;    // true → 僅 R18 模式播放（舊欄位，保留相容）
		public string branchStart;   // "r18" 或 "allAges" → 分歧段落開始
		public bool branchEnd;       // true → 分歧段落結束
		public string addVar;         // 非空 → 該劇情變數 +1（純控制行）
		public string varStart;       // 非空 → 變數 >0 才播放此段，否則跳到 varEnd
		public bool varEnd;          // true → 變數條件段落結束
		public string gotoIfAllAges; // 非空 → 全年齡模式時跳到指定場景（而非預設目標）
		public string gotoIfR18;     // 非空 → R18 模式時跳到指定場景（而非預設目標）
		// 選擇框分歧：{ "choice": ["A","B"] } → 顯示選擇框；
		// 之後用 { "optionStart": 0 } ... { "optionEnd": true } 包住對應選項的劇情
		public string[] choice;      // 非空 → 顯示選擇框，選項文字依序為陣列內容
		public string choiceTitle;   // 選擇框標題（可省略）
		public int optionStart = -1; // >=0 → 選項段落開始（0=第一個選項）；沒選到該選項就跳到 optionEnd
		public bool optionEnd;       // true → 選項段落結束
	}

	// PrologueLine 的 CG 多層存取（對應 DialogueSystem.GetCGLayer）
	static DialogueSystem.CGLayerCmd GetPrologueCGLayer(PrologueLine line, int layer)
	{
		return layer switch
		{
			0  => new DialogueSystem.CGLayerCmd { show = line.showCG,   hide = line.hideCG,   scale = line.cgScale,   x = line.cgX,   y = line.cgY },
			1  => new DialogueSystem.CGLayerCmd { show = line.showCG1,  hide = line.hideCG1,  scale = line.cgScale1,  x = line.cgX1,  y = line.cgY1 },
			2  => new DialogueSystem.CGLayerCmd { show = line.showCG2,  hide = line.hideCG2,  scale = line.cgScale2,  x = line.cgX2,  y = line.cgY2 },
			3  => new DialogueSystem.CGLayerCmd { show = line.showCG3,  hide = line.hideCG3,  scale = line.cgScale3,  x = line.cgX3,  y = line.cgY3 },
			4  => new DialogueSystem.CGLayerCmd { show = line.showCG4,  hide = line.hideCG4,  scale = line.cgScale4,  x = line.cgX4,  y = line.cgY4 },
			5  => new DialogueSystem.CGLayerCmd { show = line.showCG5,  hide = line.hideCG5,  scale = line.cgScale5,  x = line.cgX5,  y = line.cgY5 },
			6  => new DialogueSystem.CGLayerCmd { show = line.showCG6,  hide = line.hideCG6,  scale = line.cgScale6,  x = line.cgX6,  y = line.cgY6 },
			7  => new DialogueSystem.CGLayerCmd { show = line.showCG7,  hide = line.hideCG7,  scale = line.cgScale7,  x = line.cgX7,  y = line.cgY7 },
			8  => new DialogueSystem.CGLayerCmd { show = line.showCG8,  hide = line.hideCG8,  scale = line.cgScale8,  x = line.cgX8,  y = line.cgY8 },
			9  => new DialogueSystem.CGLayerCmd { show = line.showCG9,  hide = line.hideCG9,  scale = line.cgScale9,  x = line.cgX9,  y = line.cgY9 },
			10 => new DialogueSystem.CGLayerCmd { show = line.showCG10, hide = line.hideCG10, scale = line.cgScale10, x = line.cgX10, y = line.cgY10 },
			_  => new DialogueSystem.CGLayerCmd(),
		};
	}

	/// <summary>序章劇本資料（對應 JSON 檔）</summary>
	class PrologueData
	{
		public string id;
		public PrologueLine[] lines;
	}

	// ═══════════════════════════════════════════════════════════
	//  存讀檔功能
	// ═══════════════════════════════════════════════════════════

	/// <summary>
	/// 確保存讀檔控制器存在
	/// Unity 是 AddComponent&lt;SaveLoadController&gt;()，Godot 改為建立子節點
	/// </summary>
	void EnsureSaveLoadController()
	{
		if (_saveLoadCtrl != null && IsInstanceValid(_saveLoadCtrl)) return;

		// 先找現有子節點，沒有才新建
		_saveLoadCtrl = GetNodeOrNull<SaveLoadController>("SaveLoadController");
		if (_saveLoadCtrl == null)
		{
			_saveLoadCtrl = new SaveLoadController { Name = "SaveLoadController" };
			AddChild(_saveLoadCtrl);
		}

		// 設定存檔回調：取得當前遊戲狀態
		SaveLoadController.OnGetSaveData = () =>
		{
			var pdm = PlayerDataManager.Instance;
			var sd = new SaveLoadController.SaveData();
			// Unity 的 SceneManager.GetActiveScene().name → Godot 取目前場景節點名稱
			sd.sceneName = GetTree()?.CurrentScene?.Name ?? "";
			sd.saveTime  = System.DateTime.Now.ToString("yyyy/MM/dd  HH:mm:ss");
			if (pdm != null) sd.lineIndex = pdm.CurrentNight;
			return sd;
		};

		// 設定讀檔回調：載入遊戲狀態
		SaveLoadController.OnApplyLoadData = (sd) =>
		{
			GD.Print($"[SaveLoad] 讀取存檔：場景={sd.sceneName} 時間={sd.saveTime}");
			if (!string.IsNullOrEmpty(sd.sceneName))
				GetTree().ChangeSceneToFile($"res://Scenes/{sd.sceneName}.tscn");
		};
	}

	/// <summary>
	/// 為按鈕加上懸停偵測，懸停時阻擋對話推進
	/// Unity 用 EventTrigger，Godot 用 Control 內建的 MouseEntered / MouseExited 訊號
	/// </summary>
	void AddHoverBlock(Control btn)
	{
		if (btn == null || !IsInstanceValid(btn)) return;
		btn.MouseEntered += () => _hoveringSaveLoad = true;
		btn.MouseExited  += () => _hoveringSaveLoad = false;
	}

	void OpenSavePanel()
	{
		EnsureSaveLoadController();
		_saveLoadCtrl.CaptureAndOpen();
	}

	void OpenLoadPanel()
	{
		EnsureSaveLoadController();
		_saveLoadCtrl.Open(SaveLoadController.Mode.Load);
	}

	// ═══════════════════════════════════════════════════════════
	//  對話上排按鈕（自動 / 快進 / 跳過 / 存檔 / 讀檔）
	// ═══════════════════════════════════════════════════════════

	void SyncTopBtns()
	{
		if (_topBtnCfg == null || _tbAuto == null) return;
		ApplyTopBtn(_tbAuto, _tbAutoTxt, _topBtnCfg.AutoPlayPos, _topBtnCfg.AutoPlayW);
		ApplyTopBtn(_tbFF,   _tbFFTxt,   _topBtnCfg.FastFwdPos,  _topBtnCfg.FastFwdW);
		ApplyTopBtn(_tbSkip, _tbSkipTxt, _topBtnCfg.SkipPos,     _topBtnCfg.SkipW);
		ApplyTopBtn(_tbSave, _tbSaveTxt, _topBtnCfg.SavePos,     _topBtnCfg.SaveW);
		ApplyTopBtn(_tbLoad, _tbLoadTxt, _topBtnCfg.LoadPos,     _topBtnCfg.LoadW);
	}

	/// <summary>
	/// 套用單顆上排按鈕的尺寸 / 位置 / 字級 / 描邊
	/// Unity 的 Outline 元件與獨立文字 RectTransform 在 Godot 由 Label 自身處理
	/// </summary>
	void ApplyTopBtn(Control rt, Label txt, Vector2 pos, float w)
	{
		if (rt == null || !IsInstanceValid(rt)) return;

		// sizeDelta + anchoredPosition 一次套用（Y 由 helper 翻轉）
		SetAnchoredPos(rt, pos, new Vector2(w, _topBtnCfg.BtnHeight));

		if (txt != null && IsInstanceValid(txt))
		{
			txt.AddThemeFontSizeOverride("font_size", _topBtnCfg.FontSize);
			// 描邊：Unity 的 effectColor / effectDistance → Godot 的 outline_size / font_outline_color
			SetOutline(txt, _topBtnCfg.OutlineSize,
				new Color(0f, 0f, 0f, Mathf.Clamp(_topBtnCfg.OutlineAlpha, 0f, 1f)));
		}
	}
}
