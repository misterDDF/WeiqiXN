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

    public async void OnSaveDuelScene(OnSaveDuelScene evt)
    {
        int saveSlotIndex = 0;
        string saveFilePath = GameSaveConfig.GetDuelSceneSavePath(saveSlotIndex);
        string recordFilePath = GameSaveConfig.GetDuelRecordSavePath(saveSlotIndex);
        string saveInfoFilePath = GameSaveConfig.GetDuelSaveInfoPath(saveSlotIndex);
        if (!KataGoDuelRecordFile.Save((DuelScene)scene, recordFilePath)) {
            EmitSaveResult(false, saveSlotIndex, "record file save failed");
            return;
        }

        if (!DuelSaveInfoFile.Save((DuelScene)scene, saveInfoFilePath, saveSlotIndex)) {
            EmitSaveResult(false, saveSlotIndex, "save info file save failed");
            return;
        }

        bool saveSceneSuccess = await Global.Instance.gameSaveManager.SaveDataAsync(scene, saveFilePath);
        EmitSaveResult(saveSceneSuccess, saveSlotIndex, saveSceneSuccess ? string.Empty : "scene data save failed");
    }

    private void EmitSaveResult(bool success, int saveSlotIndex, string errorMessage)
    {
        scene.EmitSystemEvent(new OnDuelSaveResult(success, saveSlotIndex, errorMessage));
    }
}
