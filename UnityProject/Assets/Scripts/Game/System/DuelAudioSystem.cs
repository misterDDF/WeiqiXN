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
    }

    private void OnAfterAddChessToBoard(OnAfterAddChessToBoard evt)
    {
        if (evt == null) {
            return;
        }

        GameAudio.PlayStonePlace();
    }
}
