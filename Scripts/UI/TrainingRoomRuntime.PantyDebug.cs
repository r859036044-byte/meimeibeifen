using Godot;

/// <summary>
/// 除錯用：遊戲裡按 Insert／Home／End 逐項關掉／打開最近加的內褲效果，畫面左上會顯示目前狀態，
/// 用來找出「右上角被切掉露出皮膚」、「細帶梳子紋」是哪一項造成的（不用進 Inspector 調數字）。
///   Insert：內褲邊緣跟著腿平移（MPantyLegOffFollow）
///   Home：銜接處往右下角推擠（MPantyCornerPush）
///   End：雞雞裁切內褲（MPantyJijiClip）
/// </summary>
public partial class TrainingRoomRuntime
{
    Label _pdLabel;
    float _pdSavedLeg = 1f, _pdSavedCorner = 1f;
    bool _pdLegOff, _pdCornerOff;
    public bool PantyDebugLines;   // Delete：畫出內褲裁切／推擠線（紅＝雞雞裁內褲起點、黃＝銜接處推擠範圍、紫＝細帶裁切黑線）

    bool PantyDebugKeys(InputEvent e)
    {
        if (e is not InputEventKey k || !k.Pressed || k.Echo) return false;
        if (k.Keycode == Key.Insert)
        {
            _pdLegOff = !_pdLegOff;
            if (_pdLegOff) { _pdSavedLeg = MPantyLegOffFollow; MPantyLegOffFollow = 0f; } else MPantyLegOffFollow = _pdSavedLeg;
        }
        else if (k.Keycode == Key.Home)
        {
            _pdCornerOff = !_pdCornerOff;
            if (_pdCornerOff) { _pdSavedCorner = MPantyCornerPush; MPantyCornerPush = 0f; } else MPantyCornerPush = _pdSavedCorner;
        }
        else if (k.Keycode == Key.End) MPantyJijiClip = !MPantyJijiClip;
        else if (k.Keycode == Key.Delete) PantyDebugLines = !PantyDebugLines;
        else return false;
        PdShow();
        return true;
    }

    void PdShow()
    {
        var parent = UiRoot; if (parent == null) return;
        if (_pdLabel == null || !IsInstanceValid(_pdLabel))
        {
            _pdLabel = new Label { Name = "PantyDebugLbl", MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = 4001 };
            _pdLabel.Position = new Vector2(12f, 90f);
            _pdLabel.AddThemeFontSizeOverride("font_size", 18);
            parent.AddChild(_pdLabel);
        }
        _pdLabel.Text = $"Insert 跟腿平移：{(_pdLegOff ? "關" : "開")}   Home 銜接處推擠：{(_pdCornerOff ? "關" : "開")}   End 雞雞裁內褲：{(MPantyJijiClip ? "開" : "關")}   Delete 畫裁切線：{(PantyDebugLines ? "開" : "關")}\n目前：{((_jijiPhase == 3 && _jijiStage >= 1) ? "插入中（紅／黃線有效；青色是雞雞輪廓）" : "未插入（雞雞整個輪廓都在裁內褲＝青色線；紅／黃線沒作用）")}";
        var tw = CreateTween();
        _pdLabel.Modulate = Colors.White;
        tw.TweenInterval(PantyDebugLines ? 60f : 4f);
        tw.TweenProperty(_pdLabel, "modulate:a", 0f, 0.6f);
    }
}
