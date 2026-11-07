using Godot;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

/// <summary>
/// TrainingRoomRuntime — 待播對話（PlayPendingDialogue）、日夜切換流程
/// （ConsumePendingFlags / EndToday / TrainingEndFlow）、簡化版第三晚小遊戲、淡場轉場。
///
/// 【已知簡化】Unity 原版對話框支援多段 JSON（陣列＝分支選項，如「算了不跟你吵」
/// vs「教訓妹妹」）、CG 圖層淡入淡出、立繪、背景切換、BGM/SE、打字機快進/自動播放等
/// 完整對話引擎（與 LivingRoomRuntime 的對話系統同級）。這裡使用 Godot 專案既有的
/// DialogueSystem.DialogueData（只支援單一物件、不支援頂層陣列分支），故只播放
/// 主線對話文字（speaker + text，打字機效果、點擊/空白鍵推進），不處理分支選項、
/// CG/背景/立繪動畫。若 JSON 是分支陣列格式，只播放陣列第一段。
/// </summary>
public partial class TrainingRoomRuntime : Node
{
    // ── 進場時消費 Pending 旗標，決定第幾晚 ─────────────────────────
    void ConsumePendingFlags()
    {
        var pdm = PlayerDataManager.Instance;
        if (pdm == null) return;

        if (PendingDay2First)
        {
            PendingDay2First = false;
            pdm.CurrentNight = 1;
            pdm.ResetTodayTracking();
        }
        else if (PendingDay3First)
        {
            PendingDay3First = false;
            pdm.CurrentNight = 2;
            pdm.ResetTodayTracking();
        }
        else if (PendingNight3)
        {
            PendingNight3 = false;
            pdm.CurrentNight = 3;
            pdm.ResetTodayTracking();

            if (PendingMiniGameOnly)
            {
                PendingMiniGameOnly = false;
                _pendingDialogueDone = true;
                TaskLogger.Run(MiniGameNight3Async(), "TrainingRoom.MiniGameNight3Async");
            }
            else if (pdm.IdleNightCount >= 2)
            {
                _pendingDialogueDone = true;
                TaskLogger.Run(MiniGameNight3Async(), "TrainingRoom.TimingChallenge");
            }
            else
            {
                _pendingDialogueDone = true;
            }
        }
    }

    async Task PlayPendingDialogueThenSignalAsync(string dialogueName)
    {
        await PlayPendingDialogueAsync(dialogueName);
        _pendingDialogueDone = true;
        await MiniGameNight3Async();
    }

    // ── 待播 JSON 對話（簡化版對話框：speaker + 打字機 + 點擊推進）───
    public async Task PlayPendingDialogueAsync(string dialogueName)
    {
        var path = $"res://Resources/Data/Dialogues/{dialogueName}.json";
        if (!ResourceLoader.Exists(path))
        {
            GD.PushWarning($"[TrainingRoom] 找不到對話檔：{path}");
            return;
        }
        string raw;
        using (var f = FileAccess.Open(path, FileAccess.ModeFlags.Read))
            raw = f.GetAsText();
        raw = DialogueSystem.PreprocessJson(raw);

        DialogueSystem.DialogueData data = null;
        try
        {
            data = DialogueSystem.ParseDialogueData(raw);
        }
        catch
        {
            try
            {
                var arr = JsonSerializer.Deserialize<List<DialogueSystem.DialogueData>>(raw,
                    GameJson.Options());
                if (arr != null && arr.Count > 0) data = arr[0];
            }
            catch (System.Exception e)
            {
                GD.PushWarning($"[TrainingRoom] {dialogueName} 解析失敗：{e.Message}");
            }
        }

        if (data == null || data.Lines == null || data.Lines.Count == 0) return;

        var container = new Control { Name = "PendingDialogueContainer", MouseFilter = Control.MouseFilterEnum.Stop };
        container.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        UiRoot.AddChild(container);   // 對話框不跟著鏡頭縮放

        var boxHeight = 220f;
        var box = new Panel { Name = "DialogueBox" };
        box.AnchorLeft = 0; box.AnchorRight = 1; box.AnchorTop = 1; box.AnchorBottom = 1;
        box.OffsetTop = -boxHeight; box.OffsetBottom = 0;
        box.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.88f) });
        container.AddChild(box);

        var nameBar = new Panel { Name = "NameBar" };
        nameBar.Position = new Vector2(50, -boxHeight - 44); nameBar.Size = new Vector2(260, 40);
        nameBar.AnchorTop = 1; nameBar.AnchorBottom = 1;
        nameBar.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.65f, 0.12f, 0.25f, 0.95f) });
        container.AddChild(nameBar);
        var spkLbl = new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        spkLbl.AddThemeFontSizeOverride("font_size", 22);
        spkLbl.AddThemeColorOverride("font_color", Colors.White);
        spkLbl.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        nameBar.AddChild(spkLbl);

        var contentLbl = new RichTextLabel { BbcodeEnabled = false };
        contentLbl.Position = new Vector2(60, 20); contentLbl.Size = new Vector2(0, boxHeight - 40);
        contentLbl.AnchorRight = 1; contentLbl.OffsetRight = -60;
        contentLbl.AddThemeFontSizeOverride("normal_font_size", 24);
        contentLbl.AddThemeColorOverride("default_color", new Color(0.98f, 0.96f, 1f));
        box.AddChild(contentLbl);

        var hint = new Label { Text = "▼ 點擊繼續", HorizontalAlignment = HorizontalAlignment.Right };
        hint.AnchorLeft = 1; hint.AnchorRight = 1; hint.AnchorTop = 1; hint.AnchorBottom = 1;
        hint.OffsetLeft = -200; hint.OffsetTop = -30; hint.OffsetRight = -20; hint.OffsetBottom = -6;
        hint.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.4f));
        box.AddChild(hint);

        bool skipTyping = false, typing = false, waiting = false;
        if (_menuReader != null) _menuReader.AlwaysShowMenu = false;   // 對話期間用對話自己的選單鈕，避免重複
        var reader = NovelReader.Install(container, box, contentLbl, null,
            () => typing, () => waiting, () => false,
            () => skipTyping = true, () => waiting = false);
        reader.SetSpeakerBadge(nameBar, spkLbl);
        foreach (var line in data.Lines)
        {
            spkLbl.Text = line.speaker ?? "";
            nameBar.Visible = !string.IsNullOrEmpty(line.speaker);
            string full = line.text ?? "";
            reader.BeginLine(line.speaker, full);
            contentLbl.Text = "";
            skipTyping = false;
            typing = true;
            for (int i = 0; i < full.Length; i++)
            {
                while (reader.Paused) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (skipTyping) { contentLbl.Text = full; break; }
                contentLbl.Text += full[i];
                await ToSignal(GetTree().CreateTimer(0.025f), SceneTreeTimer.SignalName.Timeout);
            }
            typing = false;
            waiting = true;
            while (waiting) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        reader.Stop();
        container.QueueFree();
        if (_menuReader != null && IsInstanceValid(_menuReader)) _menuReader.AlwaysShowMenu = true;
    }

    // ── 常駐選單鈕：沿用 NovelReader 的「☰ 選單」（保存／讀檔／設定），沒有對話時也顯示 ──
    NovelReader _menuReader;
    void SetupMenuReader()
    {
        var dummyBox = new Control { Name = "MenuReaderDummyBox", Visible = false };
        AddChild(dummyBox);
        var dummyText = new Label { Visible = false };
        dummyBox.AddChild(dummyText);
        _menuReader = NovelReader.Install(this, dummyBox, dummyText, null,
            () => false, () => false, () => false, () => { }, () => { });
        _menuReader.AlwaysShowMenu = true;
    }

    // ── EndToday：判斷分支並轉場 ─────────────────────────────────
    bool _endingDay;
    bool _timingChallengeActive;
    public void EndToday()
    {
        if (_endingDay || _timingChallengeActive) return;
        if (!TissueAllowLeave()) return;   // 精液沒擦乾淨不能離開（Tissue.cs）
        var pdm = PlayerDataManager.Instance;
        if (pdm == null) return;

        _endingDay = true;
        if (pdm.TodayDidNothing)
        {
            TaskLogger.Run(IdleEndFlowAsync(), "TrainingRoom.IdleEndFlowAsync");
            return;
        }

        if (pdm.TodayInserted && !pdm.TodayInsertOrgasm)
            pdm.TodayInsertNoOrg = true;

        var branch = pdm.GetDay2Branch();
        bool success = branch != PlayerDataManager.Day2Branch.InsertNoOrgasm;
        TaskLogger.Run(TrainingEndFlowAsync(success), "TrainingRoom.TrainingEndFlowAsync");
    }

    async Task IdleEndFlowAsync()
    {
        await FadeToBlackAsync();

        var pdm = PlayerDataManager.Instance;
        if (pdm == null) { GetTree().ChangeSceneToFile("res://Scenes/Ending4.tscn"); return; }

        int night = pdm.CurrentNight;
        if (pdm.TodayDidNothing) pdm.IdleNightCount++;

        if (night == 1 || night == 2)
        {
            pdm.ResetTodayTracking();
            LivingRoomRuntime.PendingDay3 = true;
            GetTree().ChangeSceneToFile("res://Scenes/LivingRoom.tscn");
        }
        else
        {
            pdm.ResetTodayTracking();
            GetTree().ChangeSceneToFile("res://Scenes/Ending4.tscn");
        }
    }

    async Task TrainingEndFlowAsync(bool success)
    {
        await FadeToBlackAsync();

        var pdm = PlayerDataManager.Instance;
        if (pdm == null) { GetTree().ChangeSceneToFile("res://Scenes/Ending4.tscn"); return; }

        pdm.IdleNightCount = 0; // 只有連續未操作才累積。
        int night = pdm.CurrentNight;
        pdm.ResetTodayTracking();

        if (!success)
        {
            GetTree().ChangeSceneToFile("res://Scenes/Ending4.tscn");
            return;
        }

        if (night == 1 || night == 2)
        {
            // LivingRoomRuntime 只公開 PendingDay3 旗標；日夜文本內容由
            // LivingRoomRuntime 依 PlayerDataManager.CurrentNight 自行判斷。
            LivingRoomRuntime.PendingDay3 = true;
            GetTree().ChangeSceneToFile("res://Scenes/LivingRoom.tscn");
        }
        else
        {
            GetTree().ChangeSceneToFile("res://Scenes/Ending2.tscn");
        }
    }

    // ── 第三天非露骨時機挑戰 ──
    async Task MiniGameNight3Async()
    {
        if (_timingChallengeActive) return;
        _timingChallengeActive = true;
        bool won;
        try { won = await TimingChallenge.Play(this); }
        finally { _timingChallengeActive = false; }
        if (won && (PlayerDataManager.Instance?.IdleNightCount ?? 0) >= 2)
        {
            Ending2Runtime.UseNothingRoute = true;
            GetTree().ChangeSceneToFile("res://Scenes/Ending2.tscn");
        }
        else GetTree().ChangeSceneToFile((PlayerDataManager.Instance?.IdleNightCount ?? 0) >= 2 ? "res://Scenes/Ending1.tscn" : "res://Scenes/Ending4.tscn");
    }

    // ── 淡場 ─────────────────────────────────────────────────────
    async Task FadeToBlackAsync()
    {
        var overlay = new ColorRect { Color = new Color(0, 0, 0, 0), MouseFilter = Control.MouseFilterEnum.Stop };
        overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        UiRoot.AddChild(overlay);

        float t = 0f;
        while (t < 1f)
        {
            t += (float)GetProcessDeltaTime() * 1.5f;
            overlay.Color = new Color(0, 0, 0, Mathf.Clamp(t, 0f, 1f));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    async Task FadeAndLoadAsync(string scene)
    {
        await FadeToBlackAsync();
        GetTree().ChangeSceneToFile($"res://Scenes/{scene}.tscn");
    }
}
