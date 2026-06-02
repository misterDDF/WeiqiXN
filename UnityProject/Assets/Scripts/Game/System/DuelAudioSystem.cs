public class DuelAudioSystem : SystemBase
{
    public override string systemName => GetSystemName<DuelAudioSystem>();

    public DuelAudioSystem(SceneBase scene) : base(scene)
    {
    }

    public override void Init()
    {
        base.Init();
        scene.RegisterSystemEvent<OnAfterAddChessToBoard>(OnAfterAddChessToBoard);
        scene.RegisterSystemEvent<OnAfterCaptureChessFromBoard>(OnAfterCaptureChessFromBoard);
    }

    private void OnAfterAddChessToBoard(OnAfterAddChessToBoard evt)
    {
        if (evt == null) {
            return;
        }

        GameAudio.PlayStonePlace();
    }

    private void OnAfterCaptureChessFromBoard(OnAfterCaptureChessFromBoard evt)
    {
        if (evt == null || evt.captureCount <= 0) {
            return;
        }

        GameAudio.PlayStoneCapture(evt.captureCount);
    }
}
