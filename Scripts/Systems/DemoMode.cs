using Godot;

/// <summary>
/// 試玩版總開關。
///
/// 開啟時：
///   1. 主選單：不播 LOGO／OP 開場動畫，直接停在標題畫面（等同 SkipIntroOnce）。
///   2. 按「開始遊戲」：不跳「要不要看序章」確認，也不播序章劇情，直接進 TrainingRoom。
///   3. TrainingRoom：衣服相關物件（衣服、衣擺、肩帶、內褲）全部無法操作，衣服模式按鈕停用。
///
/// 開關方式（任選一種）：
///   - 改下面的 DefaultEnabled（true／false）。
///   - 匯出時在 Export Preset → Features → Custom 加上自訂 feature tag「demo」，
///     不用改程式碼就能出一份試玩版（DefaultEnabled 維持 false 即可）。
/// </summary>
public static class DemoMode
{
    /// <summary>試玩版預設開關：true＝開啟。</summary>
    public const bool DefaultEnabled = true;

    static bool? _cached;

    public static bool Enabled => _cached ??= DefaultEnabled || OS.HasFeature("demo");
}
