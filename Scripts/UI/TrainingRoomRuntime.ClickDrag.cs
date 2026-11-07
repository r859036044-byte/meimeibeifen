using Godot;

/// <summary>
/// 「點一下」和「按著拖」的判斷（熱點用）：
///   按下後滑鼠移動超過 ClickDragPx（螢幕 px）就算「拖」，放開時不再當成點擊。
///   這樣「按住物件上下拖調整速度」不會被判成「點了物件」（點了會把正在做的摸／舔取消）。
/// B 熱點（雞雞／小穴）原本是「按下就觸發」；現在若按下時正在摸 B／舔 B（再點一次會取消），
/// 改成「放開時沒拖才算點擊」，所以可以按著它上下拖調速度。
/// </summary>
public partial class TrainingRoomRuntime
{
    [ExportGroup("操作判定")]
    [Export(PropertyHint.Range, "2,40,1")] public float ClickDragPx = 10f;   // 按下後移動多遠（螢幕 px）就算拖、不算點擊

    Vector2 _clkDownPos;
    bool _clkDragged;
    bool _hs4Pending;   // B 熱點：按下時延後，放開沒拖才觸發 OnHotspotClicked(4)

    void ClickDragBegin()
    {
        _clkDownPos = GetViewport().GetMousePosition();
        _clkDragged = false;
    }

    /// <summary>從按下到現在有沒有拖（含目前位置離按下點的距離）。</summary>
    bool ClickWasDrag() => _clkDragged || (GetViewport().GetMousePosition() - _clkDownPos).Length() > ClickDragPx;

    /// <summary>B 熱點按下時要不要延後觸發：正在摸 B／舔 B（再按會取消）時延後，讓按住上下拖可以調速度。</summary>
    bool HotspotBShouldDefer() =>
        _jijiPhase == 0 && (_bTouchRunning || (_lickOn && _lickTarget == "B"));

    /// <summary>按下 B 熱點／小穴：要延後就記下來，否則立刻觸發。</summary>
    void HotspotBPress()
    {
        ClickDragBegin();
        if (HotspotBShouldDefer()) _hs4Pending = true;
        else OnHotspotClicked(4);
    }

    /// <summary>_Input 最前面呼叫：追蹤拖動、處理延後的 B 熱點。</summary>
    void ClickDragInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm && (mm.ButtonMask & MouseButtonMask.Left) != 0
            && (mm.Position - _clkDownPos).Length() > ClickDragPx)
            _clkDragged = true;

        if (_hs4Pending && e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
        {
            _hs4Pending = false;
            if (!ClickWasDrag()) OnHotspotClicked(4);
        }
    }
}
