using System;
using XNClient.Logger;

public class SceneManager : ModuleBase
{
    public SceneBase mainScene;

    public override void Init()
    {

    }

    public override void Update()
    {
        base.Update();
        if (mainScene != null) {
            mainScene.Update();
        }
    }

    public void EnterMainScene(string sceneTypeId, SceneCreateParams sceneCreateParams)
    {
        SceneDataType sceneData = SceneDataType.GetConfigData(sceneTypeId);
        if (sceneData == null) {
            XNLogger.LogError("Scene config invalid, enter main scene failed.", ("sceneTypeId", sceneTypeId));
            return;
        }

        if (CreateSceneWithConfigData(sceneData, sceneCreateParams, out SceneBase scene)) {
            ExitMainScene();
            mainScene = scene;
        } else {
            XNLogger.LogError("Create scene with config data failed, enter main scene failed.", ("sceneTypeId", sceneTypeId));
            return;
        }

        if (!string.IsNullOrEmpty(sceneCreateParams.saveFilePath)) {
            scene.RestoreSceneData(sceneCreateParams.saveFilePath);
        }
        scene.LoadScene();
        XNLogger.LogInfo("Enter main scene success.", ("sceneTypeId", sceneTypeId));
    }

    public void ExitMainScene()
    {
        if (mainScene != null) {
            XNLogger.LogInfo("Exit main scene success.", ("sceneTypeId", mainScene.configData.id));
            mainScene.OnSceneExit();
            mainScene = null;
        }
    }

    private bool CreateSceneWithConfigData(SceneDataType sceneData, SceneCreateParams sceneCreateParams, out SceneBase scene)
    {
        scene = null;
        if (Enum.TryParse(sceneData.sceneType, out SceneConfig.SceneTypeEnum sceneType)) {
            switch (sceneType) {
                case SceneConfig.SceneTypeEnum.MainMenu:
                    scene = new MainMenuScene(sceneData, sceneCreateParams);
                    return true;
                case SceneConfig.SceneTypeEnum.Duel:
                    scene = new DuelScene(sceneData, sceneCreateParams);
                    return true;
                case SceneConfig.SceneTypeEnum.Replay:
                    scene = new ReplayScene(sceneData, sceneCreateParams);
                    return true;
            }
        }

        return false;
    }
}

