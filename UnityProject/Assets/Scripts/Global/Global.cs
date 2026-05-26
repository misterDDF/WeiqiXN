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
        PreparingKataGoRuntime,
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
    private KataGoRuntimePreparer kataGoRuntimePreparer;

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
            TryStartPostResourceStartup();
            return;
        }

        if (startupState == StartupState.PreparingKataGoRuntime) {
            TryFinishKataGoRuntimePrepare();
            return;
        }

        if (startupState == StartupState.WarmingKataGo) {
            TryFinishKataGoWarmup();
        }
    }

    private void TryStartPostResourceStartup()
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

        InitUiAndSceneManagers();

#if UNITY_ANDROID && !UNITY_EDITOR
        LoadingPage.SetProgress(
            MessageText.Get("katago_runtime_prepare_status"),
            MessageText.Get("katago_runtime_prepare_checking"),
            0.2f);
        uiManager.ShowPage<LoadingPage>();
        kataGoRuntimePreparer = new KataGoRuntimePreparer();
        kataGoRuntimePreparer.Start();
        startupState = StartupState.PreparingKataGoRuntime;
#else
        StartKataGoWarmup();
#endif
    }

    private void InitUiAndSceneManagers()
    {
        if (uiManager != null && sceneManager != null) {
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
    }

    private void TryFinishKataGoRuntimePrepare()
    {
        if (kataGoRuntimePreparer == null) {
            StartKataGoWarmup();
            return;
        }

        kataGoRuntimePreparer.Update();
        LoadingPage.SetProgress(
            kataGoRuntimePreparer.StatusText,
            kataGoRuntimePreparer.DetailText,
            Mathf.Lerp(0.2f, 0.55f, kataGoRuntimePreparer.Progress));

        if (!kataGoRuntimePreparer.IsDone) {
            return;
        }

        if (kataGoRuntimePreparer.IsFailed) {
            string prepareError = kataGoRuntimePreparer.Error;
            XNLogger.LogError("Android KataGo runtime prepare failed before warmup.", ("error", prepareError));
            kataGoRuntimePreparer.Dispose();
            kataGoRuntimePreparer = null;
            FinishStartupAfterKataGoFailure(prepareError);
            return;
        }

        kataGoRuntimePreparer.Dispose();
        kataGoRuntimePreparer = null;
        StartKataGoWarmup();
    }

    private void StartKataGoWarmup()
    {
        LoadingPage.SetProgress(
            MessageText.Get("katago_warmup_status"),
            MessageText.Get("katago_starting_detail"),
            ResolveKataGoWarmupDisplayProgress(0.05f));
        if (!LoadingPage.hasActivePage) {
            uiManager.ShowPage<LoadingPage>();
        }
        kataGoWarmupStartTime = Time.realtimeSinceStartup;
        KataGoBootstrap.Start();
        startupState = StartupState.WarmingKataGo;
    }

    private void TryFinishKataGoWarmup()
    {
        KataGoBootstrap.KataGoStartupStatus kataGoStatus = KataGoBootstrap.GetStartupStatus();
        LoadingPage.SetProgress(kataGoStatus.statusText, kataGoStatus.detailText, ResolveKataGoWarmupDisplayProgress(kataGoStatus.progress));

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

        EnterMainMenuAfterStartup();
    }

    private void FinishStartupAfterKataGoFailure(string detail)
    {
        LoadingPage.SetProgress(
            MessageText.Get("katago_failed_status"),
            string.IsNullOrEmpty(detail) ? MessageText.Get("katago_all_engines_unavailable") : detail,
            0.9f);
        XNLogger.LogError("Global startup continues after Android KataGo runtime prepare failed.", ("detail", detail ?? string.Empty));
        EnterMainMenuAfterStartup();
    }

    private void EnterMainMenuAfterStartup()
    {
        LoadingPage.SetProgress(
            MessageText.Get("scene_loading_status"),
            MessageText.Get("scene_enter_main_menu"),
            0.95f);
        sceneManager.EnterMainScene(SceneConfig.MAIN_MENU_SCENE_TYPE_ID, SceneCreateParams.Default);
        User.Instance.Init();
        startupState = StartupState.Running;
    }

    private static float ResolveKataGoWarmupDisplayProgress(float kataGoProgress)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return Mathf.Lerp(0.55f, 0.9f, Mathf.Clamp01(kataGoProgress));
#else
        return kataGoProgress;
#endif
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
        kataGoRuntimePreparer?.Dispose();
        kataGoRuntimePreparer = null;
        User.Instance.Destroy();
        startupState = StartupState.None;
        _instance = null;
    }
}
