using Godot;

/// <summary>
/// MainMenuRuntime — OP 音樂：循環播放＋音量平滑補償。
/// meimei_op.ogg 後半（約 30 秒之後）比前半大聲約 6 dB。這裡走一條專用音軌（bus）加上增益，
/// 依播放位置用折線內插把後半壓回跟前半差不多，變化是連續的，不會突然變小聲。
/// </summary>
public partial class MainMenuRuntime
{
    const string OpBus = "MenuOpBgm";
    AudioEffectAmplify _opAmp;
    AudioStream _opStream;

    // (播放秒數, 補償 dB)：依實測響度（30s 起逐漸變大、130s 起最大約 +6 dB）反向補償
    static readonly (float t, float db)[] OpEq =
    {
        (0f, 0f), (20f, 0f), (30f, -1.0f), (40f, -2.5f), (50f, -3.5f), (60f, -4.4f),
        (70f, -4.6f), (90f, -4.8f), (110f, -5.5f), (130f, -6.2f), (200f, -6.2f),
    };

    /// <summary>開場曲載入後呼叫：開啟循環，並掛上補償音軌。</summary>
    void SetupOpBgm(AudioStream clip)
    {
        if (clip == null || _bgmSource == null) return;
        if (clip is AudioStreamOggVorbis ogg) ogg.Loop = true;
        else if (clip is AudioStreamMP3 mp3) mp3.Loop = true;
        else if (clip is AudioStreamWav wav) wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        _opStream = clip;

        if (AudioServer.GetBusIndex(OpBus) < 0)
        {
            AudioServer.AddBus();
            int idx = AudioServer.BusCount - 1;
            AudioServer.SetBusName(idx, OpBus);
            AudioServer.SetBusSend(idx, "Master");
            _opAmp = new AudioEffectAmplify();
            AudioServer.AddBusEffect(idx, _opAmp);
        }
        else
        {
            _opAmp = AudioServer.GetBusEffect(AudioServer.GetBusIndex(OpBus), 0) as AudioEffectAmplify;
        }
    }

    void UpdateOpBgmEq()
    {
        if (_bgmSource == null || !IsInstanceValid(_bgmSource) || _opAmp == null) return;
        bool isOp = _bgmSource.Stream != null && _bgmSource.Stream == _opStream;
        string want = isOp ? OpBus : "Master";
        if (_bgmSource.Bus != want) _bgmSource.Bus = want;
        if (!isOp) return;

        float p = _bgmSource.GetPlaybackPosition();
        float db = OpEq[OpEq.Length - 1].db;
        for (int i = 1; i < OpEq.Length; i++)
        {
            if (p <= OpEq[i].t)
            {
                float k = Mathf.Clamp((p - OpEq[i - 1].t) / Mathf.Max(0.01f, OpEq[i].t - OpEq[i - 1].t), 0f, 1f);
                db = Mathf.Lerp(OpEq[i - 1].db, OpEq[i].db, k);
                break;
            }
        }
        _opAmp.VolumeDb = db;
    }

    void CleanupOpBgmBus()
    {
        int idx = AudioServer.GetBusIndex(OpBus);
        if (idx >= 0) AudioServer.RemoveBus(idx);
        if (_bgmSource != null && IsInstanceValid(_bgmSource)) _bgmSource.Bus = "Master";
    }
}
