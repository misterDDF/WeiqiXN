public class ReplayAudioSystem : SystemBase
{
    public override string systemName => GetSystemName<ReplayAudioSystem>();

    private readonly StoneMoveAudio stoneAudio = new StoneMoveAudio();

    public ReplayAudioSystem(SceneBase scene) : base(scene)
    {
    }

    public void PlayMove(DuelMoveResult result)
    {
        if (result == null || !result.accepted || result.coords == null) {
            return;
        }

        stoneAudio.Cancel();
        stoneAudio.ScheduleCapture(result.pendingRemovePosIndexes.Count);
        stoneAudio.SchedulePlace();
    }

    public void CancelPendingSounds()
    {
        stoneAudio.Cancel();
    }

    public override void OnUpdate()
    {
        base.OnUpdate();
        stoneAudio.Update();
    }

    public override void OnDestroy()
    {
        stoneAudio.Cancel();
        base.OnDestroy();
    }
}
