public class DuelSaveSystem : SystemBase
{
    public override string systemName => GetSystemName<DuelSaveSystem>();

    public DuelSaveSystem(DuelScene scene) : base(scene)
    {

    }

    public override void Init()
    {
        base.Init();
        scene.RegisterSystemEvent<OnSaveDuelScene>(OnSaveDuelScene);
    }

    public void OnSaveDuelScene(OnSaveDuelScene evt)
    {
        int saveSlotIndex = 0;
        string saveFilePath = GameSaveConfig.GetDuelSceneSavePath(saveSlotIndex);
        string recordFilePath = GameSaveConfig.GetDuelRecordSavePath(saveSlotIndex);
        string saveInfoFilePath = GameSaveConfig.GetDuelSaveInfoPath(saveSlotIndex);
        if (!KataGoDuelRecordFile.Save((DuelScene)scene, recordFilePath)) {
            return;
        }

        if (!DuelSaveInfoFile.Save((DuelScene)scene, saveInfoFilePath, saveSlotIndex)) {
            return;
        }

        _ = Global.Instance.gameSaveManager.SaveDataAsync(scene, saveFilePath);
    }
}
