using System.Collections.Generic;
using UnityEngine;
using XNClient.Logger;

public class Global
{
    private const float MinKataGoWarmupLoadingSeconds = 1.5f;

    private enum StartupState
    {
        None,
        LoadingResources,
        WarmingKataGo,
        Running,
        Failed,
    }

    public static Global _instance;
    public static Global Instance
    {
        get
        {
            if (Global._instance == null) {
                Global._instance = new Global();
            }
            return Global._instance;
        }
    }
    public List<ModuleBase> moduleList = new List<ModuleBase>();

    public EventManager eventManager;
    public ResourceManager resourceManager;
    public TimerManager timerManager;
    public GameSaveManager gameSaveManager;
    public ReddotManager reddotManager;
    public LanRoomService lanRoomService;
    public UIManager uiManager;
    public SceneManager sceneManager;

    private StartupState startupState = StartupState.None;
    private float kataGoWarmupStartTime;

    public void Start()
    {
        eventManager = new EventManager();
        moduleList.Add(eventManager);

        resourceManager = new ResourceManager();
        moduleList.Add(resourceManager);

        timerManager = new TimerManager();
        moduleList.Add(timerManager);

        gameSaveManager = new GameSaveManager();
        moduleList.Add(gameSaveManager);

        reddotManager = new ReddotManager();
        moduleList.Add(reddotManager);

        lanRoomService = new LanRoomService();
        moduleList.Add(lanRoomService);

        startupState = StartupState.LoadingResources;
        TryFinishStartup();
    }

    private void TryFinishStartup()
    {
        if (startupState == StartupState.LoadingResources) {
            TryStartKataGoWarmup();
            return;
        }

        if (startupState == StartupState.WarmingKataGo) {
            TryFinishKataGoWarmup();
        }
    }

    private void TryStartKataGoWarmup()
    {
        if (resourceManager == null) {
            return;
        }

        if (resourceManager.isFailed) {
            startupState = StartupState.Failed;
            XNLogger.LogError("Global startup failed because resource manager preload failed.");
            return;
        }

        if (!resourceManager.isReady) {
            return;
        }

        uiManager = new UIManager();
        moduleList.Add(uiManager);

        sceneManager = new SceneManager();
        moduleList.Add(sceneManager);

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        GameObject debugConsoleGO = resourceManager.LoadGamePrefabWithConfigId(GlobalConfig.INGAME_DEBUG_CONSOLE_PREFAB_CONFIG_ID);
        if (debugConsoleGO != null) {
            GameObject.DontDestroyOnLoad(debugConsoleGO);
            XNLogger.LogInfo("Ingame debug console go loaded.");
        }
#endif

        LoadingPage.SetProgress("AI模型预热中...", "正在启动本地 KataGo 分析引擎。", 0.05f);
        uiManager.ShowPage<LoadingPage>();
        kataGoWarmupStartTime = Time.realtimeSinceStartup;
        KataGoBootstrap.Start();
        startupState = StartupState.WarmingKataGo;
    }

    private void TryFinishKataGoWarmup()
    {
        KataGoBootstrap.KataGoStartupStatus kataGoStatus = KataGoBootstrap.GetStartupStatus();
        LoadingPage.SetProgress(kataGoStatus.statusText, kataGoStatus.detailText, kataGoStatus.progress);

        if (!kataGoStatus.isFinished) {
            return;
        }

        if (Time.realtimeSinceStartup - kataGoWarmupStartTime < MinKataGoWarmupLoadingSeconds) {
            return;
        }

        if (kataGoStatus.isFailed) {
            XNLogger.LogError(
                "Global startup continues after KataGo warmup failed.",
                ("engine", kataGoStatus.engineName),
                ("detail", kataGoStatus.detailText));
        } else if (kataGoStatus.isSkipped) {
            XNLogger.LogWarn("Global startup continues without KataGo warmup.", ("detail", kataGoStatus.detailText));
        } else {
            XNLogger.LogInfo(
                "Global startup KataGo warmup finished.",
                ("engine", kataGoStatus.engineName),
                ("detail", kataGoStatus.detailText));
        }

        LoadingPage.SetProgress("场景加载中...", "正在进入主菜单。", 0.95f);
        sceneManager.EnterMainScene(SceneConfig.MAIN_MENU_SCENE_TYPE_ID, SceneCreateParams.Default);
        User.Instance.Init();
        startupState = StartupState.Running;
    }

    public void Update()
    {
        foreach (var module in moduleList) {
            module.Update();
        }

        TryFinishStartup();
    }

    public void FixedUpdate()
    {
        foreach (var module in moduleList) {
            module.FixedUpdate();
        }
    }

    public void LateUpdate()
    {
        foreach (var module in moduleList) {
            module.LateUpdate();
        }
    }

    public void Destroy()
    {
        for (int i = moduleList.Count - 1; i >= 0; i--) {
            moduleList[i].OnDestroy();
        }
        moduleList.Clear();
        User.Instance.Destroy();
        startupState = StartupState.None;
        _instance = null;
    }
}
