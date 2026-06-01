public class MainMenuScene : SceneBase
{
    public MainMenuScene(SceneDataType configData, SceneCreateParams sceneCreateParams) : base(configData, sceneCreateParams)
    {
    }

    public override void OnSceneLoaded()
    {
        base.OnSceneLoaded();

        GameAudio.PlayMainMenuBgm();
        Global.Instance.uiManager.ShowPage<MainMenuPage>();
    }
}
