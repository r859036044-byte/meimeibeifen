using Godot;

/// <summary>
/// TrainingRoomRuntime — 表情系統建構。
///
/// 背景說明：跟 ArmIK / RightArmIK 一樣的問題 —— TrainingRoom.tscn 只是一個
/// 只掛了腳本的最小 stub 場景，完全沒有預先擺放 FaceExpressionController /
/// EyeCloseController / EyeFollowController / IdleStateController 這幾個節點。
/// 舊的 Build() 只寫了一行 GetNodeOrNull("FaceExpressionController") 想找節點，
/// 但場景裡根本不存在這個節點，所以永遠是 null，等於整個表情系統從未真正運作。
///
/// 這裡改成跟 BuildArmIK()/BuildRightArmIK() 一樣的做法：直接在程式碼裡
/// new 出四個控制器、把層級字典（Layers）裡對應的 Control 節點直接指派給它們
/// （不透過 NodePath／GetNodeOrNull，因為這些節點本來就不是 .tscn 裡的固定節點），
/// 再 AddChild 掛進場景樹。
/// </summary>
public partial class TrainingRoomRuntime : Node
{
    // 眼白+眼球的裁切遮罩容器（各眼一個），供 BuildEyeCloseController() 的 MaskParts 使用。
    Control _eyeClipMask1;
    Control _eyeClipMask2;

    void BuildExpressionSystem()
    {
        // 重要：0~4 這幾步（眼睛遮罩/追蹤/表情系統）用 try/catch 包起來，是因為之前如果
        // 這裡任何一步丟例外，C# 的例外會直接中斷整個 BuildExpressionSystem()，導致底下
        // 第 5 步「ExpressionTester（F1 開發面板）」永遠不會被建立——這正是玩家反應
        // 「DEV 面板好像沒做進去」最可能的根本原因（面板本身沒問題，是被前面的例外拖累）。
        // 現在改成：就算眼睛/表情系統初始化失敗，也會印出錯誤訊息並繼續往下把 DEV 面板做出來，
        // 讓面板本身的問題（如果有）不會被其他子系統的例外連帶隱藏。
        try
        {
            // ── 0. 眼白/眼球 真正的裁切遮罩（在建立 EyeCloseController 之前先做，
            //    這樣底下 BuildEyeCloseController() 拿到的就已經是重新掛好父節點的版本，
            //    後面 EyeFollowController 抓 BasePos 時也會抓到正確的新座標）───────
            _eyeClipMask1 = BuildEyeClipWrapper("EyeClipMask1", "表情/眼白",  "表情/眼球1");
            _eyeClipMask2 = BuildEyeClipWrapper("EyeClipMask2", "表情/眼白2", "表情/眼球2");

            // ── 1. EyeCloseController（閉眼/眨眼動畫）──────────────────
            EyeCloseCtrl = BuildEyeCloseController();
            AddChild(EyeCloseCtrl);

            // ── 2. EyeFollowController（眼球追蹤滑鼠）──────────────────
            EyeFollowCtrl = new EyeFollowController
            {
                Name    = "EyeFollowController",
                Runtime = this,
            };
            AddChild(EyeFollowCtrl);

            // ── 3. IdleStateController（待機狀態切換，控制眼球追蹤開關）──
            IdleStateCtrl = new IdleStateController { Name = "IdleStateController" };
            AddChild(IdleStateCtrl);
            IdleStateCtrl.ConfigureEyeFollow(EyeFollowCtrl);

            // ── 4. FaceExpressionController（表情切換總控制器）─────────
            FaceController = new FaceExpressionController { Name = "FaceExpressionController" };
            // 使用者指定：進入房間時預設要顯示 ZZZ（睡覺）表情，而不是原本的無表情狀態。
            // DefaultExpression 是在 InitAsync() 完成初始化後，若非空字串就會自動 SetExpression()
            // 套用的表情（見 FaceExpressionController.cs InitAsync() 尾端邏輯）。
            FaceController.DefaultExpression = "ZZZ";
            FaceController.Configure(this, EyeCloseCtrl);
            AddChild(FaceController);
            // PartGroups / Expressions 留空，交給 FaceExpressionController 內建的
            // EnsureDefaultExpressions()/EnsurePartGroups()/EnsureDefaultParts() 自動補齊預設值
            // （這是既有邏輯，非本次新增）。
        }
        catch (System.Exception e)
        {
            GD.PrintErr($"[TrainingRoomRuntime] 表情/眼睛子系統初始化失敗，DEV 面板仍會繼續建立：{e}");
        }

        // ── 5. ExpressionTester（開發用表情測試面板，F1 開關）───────
        // 故意放在 try/catch 外面、且不依賴上面任何一步是否成功，
        // 這樣即使眼睛/表情系統壞掉，F1 面板依然會出現，方便直接用面板測試/除錯。
        var testerLayer = new CanvasLayer { Name = "ExpressionTesterLayer", Layer = 50 };
        AddChild(testerLayer);
        var tester = new ExpressionTester { Name = "ExpressionTester" };
        tester.Configure(FaceController, IdleStateCtrl, this);
        testerLayer.AddChild(tester);

        // 如果是從 F9 開發者工具的「進入房間」按鈕切過來的，自動把面板打開，
        // 不用切過來之後還要再手動按一次 F1。
        //
        // 重要：這裡不能像之前一樣「同一幀」直接呼叫 tester.OpenPanel()。
        // FaceExpressionController.InitAsync() 是非同步的（要 await 一個 0.05 秒的計時器
        // 才會把 Expressions 填好、把 IsReady 設成 true，見該檔案 InitAsync() 開頭註解），
        // 而 BuildExpressionSystem() 這整段是同步執行完的——如果在這裡立刻 OpenPanel()，
        // ExpressionTester.RebuildExpressionList() 會在 FaceController 都還沒 ready 的時候
        // 就把「選表情」清單建成空的，而且因為只有「開啟面板時」才會重建列表，
        // 之後沒有使用者手動再關開一次 F1 的話，這個空列表就會一直卡住不會自動補上。
        // 這正是「JITAIYANJIUBU → 進入房間」這條路徑跟直接執行場景／手動按 F1
        // 表現不一致的根本原因：手動按 F1 通常已經等過這 0.05 秒，才會看起來正常。
        //
        // 修法：改成非同步等待 FaceController.IsReady 變 true 之後才真正開面板，
        // 這樣兩個入口（手動 F1 / 密技自動開）最終看到的都是同一份、已經準備好的表情清單。
        if (DevModeManager.Instance != null && DevModeManager.Instance.PendingAutoOpenExpressionTester)
        {
            DevModeManager.Instance.PendingAutoOpenExpressionTester = false;
            TaskLogger.Run(AutoOpenExpressionTesterWhenReady(tester), "TrainingRoom.AutoOpenExpressionTester");
        }
    }

    async System.Threading.Tasks.Task AutoOpenExpressionTesterWhenReady(ExpressionTester tester)
    {
        try
        {
            // 最多等 3 秒（60 次 x 0.05 秒），避免 FaceController 初始化失敗時無限等待卡住。
            for (int i = 0; i < 60 && IsInstanceValid(this) && FaceController != null && !FaceController.IsReady; i++)
                await ToSignal(GetTree().CreateTimer(0.05f), SceneTreeTimer.SignalName.Timeout);

            if (IsInstanceValid(this) && IsInstanceValid(tester))
                tester.OpenPanel();
        }
        catch (System.Exception e)
        {
            GD.PrintErr($"[TrainingRoomRuntime] 自動開啟 DEV 表情面板時發生例外：{e}");
        }
    }

    // 眼皮/睫毛/眼角/下睫毛的「位移」資料來自 Unity 版 EyeCloseController.Reset()
    // 編輯器預設值（CloseOffsetY：眼皮 -30、睫毛 -25、眼角 -20、下睫毛 +20）。
    // 眼淚 1/2 → 群組1（左眼）、眼淚 3/4 → 群組2（右眼）的對應是依 LayerOrder 命名順序推測，
    // 尚未經視覺驗證，若之後看到眼淚方向跑到對側眼睛，只需對調這裡的分組。
    EyeCloseController BuildEyeCloseController()
    {
        Control Get(string key) => Layers.TryGetValue(key, out var c) ? c : null;

        var ctrl = new EyeCloseController { Name = "EyeCloseController" };

        ctrl.SlideParts = new EyeCloseController.SlidePart[]
        {
            new EyeCloseController.SlidePart { RT = Get("表情/眼皮"),   CloseOffsetY = -30f, EyeGroup = 1 },
            new EyeCloseController.SlidePart { RT = Get("表情/眼皮2"),  CloseOffsetY = -30f, EyeGroup = 2 },
            new EyeCloseController.SlidePart { RT = Get("表情/睫毛1"),  CloseOffsetY = -25f, EyeGroup = 1 },
            new EyeCloseController.SlidePart { RT = Get("表情/睫毛2"),  CloseOffsetY = -25f, EyeGroup = 2 },
            new EyeCloseController.SlidePart { RT = Get("眼角1"),       CloseOffsetY = -20f, EyeGroup = 1 },
            new EyeCloseController.SlidePart { RT = Get("眼角2"),       CloseOffsetY = -20f, EyeGroup = 2 },
            new EyeCloseController.SlidePart { RT = Get("表情/下睫毛1"), CloseOffsetY =  20f, EyeGroup = 1 },
            new EyeCloseController.SlidePart { RT = Get("表情/下睫毛2"), CloseOffsetY =  20f, EyeGroup = 2 },
        };

        // 眼白／眼球的閉眼效果改用「真正的裁切遮罩」：BuildExpressionSystem() 一開始已經呼叫
        // BuildEyeClipWrapper() 把眼白+眼球各自包進一個 ClipContents=true 的 Control 容器
        // （_eyeClipMask1/2），這裡讓 MaskPart 直接縮小「容器」的高度，容器會把超出範圍的
        // 眼白/眼球畫面裁掉——跟 Unity 原版「眼球是眼白的子物件，靠眼白的 RectMask2D 一起裁掉」
        // 是同一種效果，眼白/眼球本身的貼圖完全不會被拉伸變形，不會再有壓扁穿模的問題。
        ctrl.MaskParts = new EyeCloseController.MaskPart[]
        {
            new EyeCloseController.MaskPart { RT = _eyeClipMask1, CloseHeight = 3f, EyeGroup = 1 },
            new EyeCloseController.MaskPart { RT = _eyeClipMask2, CloseHeight = 3f, EyeGroup = 2 },
        };

        // 使用者反應眼淚圖層的高度要整體往下移 3px，這裡統一對 4 個眼淚圖層都加上
        // ExtraOffsetY=3（Godot 座標，正值＝往下），之後如果只想動其中某幾個，
        // 直接在 Inspector 個別調整 ExtraOffsetY 即可，不用改程式碼。
        const float tearExtraOffsetY = 5.3f;   // 3→4.5→5.3：再往下 0.8px
        const float tearExtraLeft = 8f;        // 左右眼的眼淚都整個向左移 8px（原本 3，再向左 5）
        const float tearExpandX = 30f;         // 閉眼時眼淚拉長的量（原 20）
        // 閉眼時眼淚要停在「睫毛和臉的交界處」：Unity 版眼淚是掛在下睫毛底下的子物件，閉眼時下睫毛往上移 20（CloseOffsetY = +20），
        // 眼淚跟著它一起移；Godot 版眼淚是獨立圖層、OffsetY 原本是 0，所以閉眼後眼淚留在原位，掉到睫毛下面一截。
        // 這裡讓眼淚的閉眼位移跟下睫毛一樣（OffsetY＝下睫毛的 CloseOffsetY，Unity 座標往上為正）。
        const float tearCloseFollow = 20f;
        ctrl.TearParts = new EyeCloseController.TearPart[]
        {
            new EyeCloseController.TearPart { RT = Get("表情/眼淚1"), EyeGroup = 1, ExpandX = tearExpandX, ExtraLeft = tearExtraLeft, ExtraOffsetY = tearExtraOffsetY, OffsetY = tearCloseFollow },
            new EyeCloseController.TearPart { RT = Get("表情/眼淚2"), EyeGroup = 1, ExpandX = tearExpandX, ExtraLeft = tearExtraLeft, ExtraOffsetY = tearExtraOffsetY, OffsetY = tearCloseFollow },
            new EyeCloseController.TearPart { RT = Get("表情/眼淚3"), EyeGroup = 2, ExpandX = tearExpandX, ExtraLeft = tearExtraLeft, ExtraOffsetY = tearExtraOffsetY, OffsetY = tearCloseFollow },
            new EyeCloseController.TearPart { RT = Get("表情/眼淚4"), EyeGroup = 2, ExpandX = tearExpandX, ExtraLeft = tearExtraLeft, ExtraOffsetY = tearExtraOffsetY, OffsetY = tearCloseFollow },
        };

        // 全閉2（睫毛上下翻轉，"反過來的閉眼"）／半閉2（下睫毛翻轉）：
        // 數值取自 Unity 場景檔 TrainingRoom.unity（fileID 903285460, EyeCloseController）
        // 的真實 Inspector 調校值 —— ClosedFlipParts＝表情_睫毛1/2、HalfFlipParts＝表情_下睫毛1/2，
        // OffsetY 皆為 0。這是使用者記憶中「把眼睛反過來的閉眼」的真正機制：
        // 全閉2（SetClosed2，「做愛4」「ZZZ」睡覺表情都用這個）在 半閉→全閉 的過程中，
        // 睫毛的 scaleY 會從 1 線性變化到 -1（上下翻轉過來），而不是像一般全閉一樣單純位移。
        //
        // 注意：Unity 原始資料裡還有 HalfFlipCounterParts（表情_眼淚1~4），但那是因為 Unity
        // 版的眼淚是掛在「下睫毛」RectTransform 底下的子物件，下睫毛翻轉時子物件的世界縮放
        // 會被父物件的負縮放連帶影響，所以需要子物件也翻一次負負得正抵消掉。Godot 版的眼淚
        // （表情/眼淚1~4）是扁平化架構下 _charRoot 的獨立 sibling，並非 下睫毛 的子節點，
        // 沒有父子縮放繼承的問題，所以不需要移植 HalfFlipCounterParts，眼淚會保持正常顯示。
        ctrl.ClosedFlipParts = new EyeCloseController.FlipPart[]
        {
            new EyeCloseController.FlipPart { RT = Get("表情/睫毛1"),  OffsetY = 0f, EyeGroup = 1 },
            new EyeCloseController.FlipPart { RT = Get("表情/睫毛2"),  OffsetY = 0f, EyeGroup = 2 },
        };
        ctrl.HalfFlipParts = new EyeCloseController.FlipPart[]
        {
            new EyeCloseController.FlipPart { RT = Get("表情/下睫毛1"), OffsetY = 0f, EyeGroup = 1 },
            new EyeCloseController.FlipPart { RT = Get("表情/下睫毛2"), OffsetY = 0f, EyeGroup = 2 },
        };

        // 全閉3（ZZZ 睡覺表情專用）：先前套用在「眼皮」，使用者最新指示改成先不管眼皮，
        // 換成「睫毛＋眼角」一起做翻轉。跟 ClosedFlipParts 用同一種翻轉機制，只是換了要
        // 翻轉的圖層，獨立成自己的一組陣列，不會影響「做愛4」原本對照 Unity 真實資料的
        // 睫毛翻轉效果（那組是 ClosedFlipParts，全閉2，跟這裡的全閉3完全獨立）。
        // EyeGroup 1＝睫毛1/眼角1（畫面右邊那隻眼，量測貼圖內容中心 x≈1076/1112 > 畫布中線 1000）、
        // EyeGroup 2＝睫毛2/眼角2（畫面左邊那隻眼，x≈835/800 < 1000）。
        // 使用者看了全閉狀態的截圖（這時眼角已因 HideWhenFlipped 隱藏，畫面上只剩睫毛）之後，
        // 反應鏡射旋轉（右眼CW/左眼CCW 或反過來）讓左右眼高度看起來不對稱、像是「旋轉偏差」。
        // 明確要求改成「兩眼同方向」——不要鏡射，左右眼統一都設成逆時針
        // （Godot RotationDegrees 正值＝順時針，逆時針要給負值）。
        // 眼角雖然全閉時會被隱藏，但半閉→全閉的過渡畫面看得到，為了跟睫毛視覺一致，
        // 一併改成同方向、不鏡射。
        // 最新指示：在原本 -15 度的基礎上再多轉 15 度（累加成 -30 度），
        // 左眼（群組2）的垂直補償從 -5px 加大成 -20px。
        // 最新指示：左眼（群組2）不要旋轉了，角度改回 0 度；右眼（群組1）改成 10 度
        // （沿用先前一路都是負值＝逆時針的方向慣例，10 度給 -10f；如果實際看起來方向
        // 反了，直接說「要順時針」，把這兩個 -10f 改成 +10f 即可）。
        // 另外右眼再往左移動約 3px——用新加的 OffsetX 欄位（正值＝往右移，負值＝往左移，
        // 見 EyeCloseController.cs 的 FlipPart.OffsetX 說明），這裡給 -3f。
        // 左眼的 OffsetY 垂直補償（-20px）維持不動，使用者這次沒提到要改。
        ctrl.EyelidFlipParts = new EyeCloseController.FlipPart[]
        {
            new EyeCloseController.FlipPart { RT = Get("表情/睫毛1"), OffsetY =   0f, OffsetX = -3f, EyeGroup = 1, ClosedRotateZ = -10f },
            new EyeCloseController.FlipPart { RT = Get("表情/睫毛2"), OffsetY = -20f, OffsetX =  0f, EyeGroup = 2, ClosedRotateZ =   0f },
            new EyeCloseController.FlipPart { RT = Get("眼角1"),      OffsetY =   0f, OffsetX = -3f, EyeGroup = 1, ClosedRotateZ = -10f, HideWhenFlipped = true },
            new EyeCloseController.FlipPart { RT = Get("眼角2"),      OffsetY = -20f, OffsetX =  0f, EyeGroup = 2, ClosedRotateZ =   0f, HideWhenFlipped = true },
        };

        return ctrl;
    }

    /// <summary>
    /// 把 whiteKey／eyeballKey 兩個圖層（眼白＋眼球）從 _charRoot 移到一個新的
    /// ClipContents=true 的小容器底下，容器大小＝兩者 open 狀態矩形的聯集（外加一點邊界）。
    /// 之後只要縮小這個容器的高度，超出範圍的眼白/眼球畫面就會被「裁掉」而不是「壓扁」，
    /// 對應 Unity 原版「眼球是眼白的子物件、靠眼白的 RectMask2D 一起裁切」的效果。
    ///
    /// 容器插入 _charRoot 的位置＝原本眼白（或眼球，如果眼白缺失）的 sibling index，
    /// 讓繪製順序跟之前一樣（眼皮/睫毛等在 LayerOrder 裡排在眼白/眼球之後，繪製順序不受影響）。
    /// </summary>
    Control BuildEyeClipWrapper(string wrapperName, string whiteKey, string eyeballKey)
    {
        Layers.TryGetValue(whiteKey, out var whiteCtrl);
        Layers.TryGetValue(eyeballKey, out var eyeballCtrl);
        if (whiteCtrl == null && eyeballCtrl == null) return null;

        Rect2 rect = default;
        bool hasRect = false;
        foreach (var c in new[] { whiteCtrl, eyeballCtrl })
        {
            if (c == null) continue;
            var r = new Rect2(c.Position, c.Size);
            rect = hasRect ? rect.Merge(r) : r;
            hasRect = true;
        }
        rect = rect.Grow(6f); // 留一點邊界，避免睜眼狀態下邊緣被誤裁

        var wrapper = new Control
        {
            Name = wrapperName,
            Position = rect.Position,
            Size = rect.Size,
            ClipContents = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        int insertIndex = (whiteCtrl ?? eyeballCtrl).GetIndex();
        _charRoot.AddChild(wrapper);
        _charRoot.MoveChild(wrapper, insertIndex);

        foreach (var c in new[] { whiteCtrl, eyeballCtrl })
        {
            if (c == null) continue;
            Vector2 localPos = c.Position - rect.Position;
            _charRoot.RemoveChild(c);
            wrapper.AddChild(c);
            c.Position = localPos; // 換算成相對容器的本地座標，畫面上視覺位置不變
        }

        return wrapper;
    }
}
