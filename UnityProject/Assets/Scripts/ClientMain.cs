#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using XNLogger = XNClient.Logger.XNLogger;

public class ClientMain
{
    private static ClientMain _instance;
    private static ClientLifecycleProxy lifecycleProxy;
    public static ClientMain Instance
    {
        get
        {
            if (ClientMain._instance == null) {
                ClientMain._instance = new ClientMain();
            }
            return ClientMain._instance;
        }
    }

    private void Start()
    {
        XNLogger.Instance.Init();
        EnsureLifecycleProxy();
        Global.Instance.Start();
    }

    private static void Update()
    {
        Global.Instance.Update();
    }

    private static void FixedUpdate()
    {
        Global.Instance.FixedUpdate();
    }

    private static void LateUpdate()
    {
        Global.Instance.LateUpdate();
    }

    private void Destroy()
    {
        KataGoBootstrap.Stop();
        Global.Instance.Destroy();
        XNLogger.Instance.Destroy();
        if (lifecycleProxy != null) {
            GameObject.Destroy(lifecycleProxy.gameObject);
            lifecycleProxy = null;
        }
        _instance = null;
    }

#if UNITY_EDITOR
    [InitializeOnLoadMethod]
    public static void OnEditorLoaded()
    {
        // 设置编辑器启动时的入口场景
        var startScene = UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene;
        if (startScene == null || AssetDatabase.GetAssetPath(startScene) != GlobalConfig.PATH_START_SCENE) {
            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(GlobalConfig.PATH_START_SCENE);
            UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene = sceneAsset;
        }
    }

    public static void OnPlayModeStateChanged(PlayModeStateChange state)
    {

        if (state == PlayModeStateChange.ExitingEditMode) {
            OnEditorLoaded();
        } else if (state == PlayModeStateChange.ExitingPlayMode) {
            ClientMain.Instance.Destroy();
        }
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void OnSubsystemRegistration()
    {
        Application.quitting -= ClientMain.Instance.Destroy;
        Application.quitting += ClientMain.Instance.Destroy;
#if UNITY_EDITOR
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
        ClientMain.Instance.InitPlayerLoop();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void OnBeforeFirstSceneLoad()
    {
        ClientMain.Instance.Start();
    }

    private static void EnsureLifecycleProxy()
    {
        if (lifecycleProxy != null) {
            return;
        }

        GameObject lifecycleProxyGO = new GameObject("ClientLifecycleProxy");
        GameObject.DontDestroyOnLoad(lifecycleProxyGO);
        lifecycleProxy = lifecycleProxyGO.AddComponent<ClientLifecycleProxy>();
    }

    private struct CustomUpdate
    {
        public UnityEngine.LowLevel.PlayerLoopSystem func;
        public System.Type insertPosition;
    }

    public void InitPlayerLoop()
    {
        // 物体上挂的mono只负责表现相关，这里的update处理逻辑层，各个update阶段保持在所有mono的对应update之后
        var customSystems = UnityEngine.LowLevel.PlayerLoop.GetDefaultPlayerLoop();
        var customUpdates = new CustomUpdate[3];
        customUpdates[0] = new CustomUpdate()
        {
            func = new UnityEngine.LowLevel.PlayerLoopSystem()
            {
                updateDelegate = ClientMain.Update,
            },
            insertPosition = typeof(UnityEngine.PlayerLoop.Update)
        };

        customUpdates[1] = new CustomUpdate()
        {
            func = new UnityEngine.LowLevel.PlayerLoopSystem()
            {
                updateDelegate = ClientMain.FixedUpdate,
            },
            insertPosition = typeof(UnityEngine.PlayerLoop.FixedUpdate),
        };

        customUpdates[2] = new CustomUpdate()
        {
            func = new UnityEngine.LowLevel.PlayerLoopSystem()
            {
                updateDelegate = ClientMain.LateUpdate,
            },
            insertPosition = typeof(UnityEngine.PlayerLoop.PreLateUpdate),
        };

        for (int index = 0; index < customSystems.subSystemList.Length; index++) {
            var curSubSystem = customSystems.subSystemList[index];
            var currentType = curSubSystem.type;

            for (int typeIndex = 0; typeIndex < customUpdates.Length; typeIndex++) {
                var updateInfo = customUpdates[typeIndex];
                if (currentType == updateInfo.insertPosition) {
                    var len = curSubSystem.subSystemList.Length;
                    var newSubSystemList = new UnityEngine.LowLevel.PlayerLoopSystem[len + 1];
                    for (int i = 0; i < len; i++) {
                        newSubSystemList[i] = curSubSystem.subSystemList[i];
                    }
                    newSubSystemList[len] = updateInfo.func;

                    curSubSystem.subSystemList = newSubSystemList;
                    customSystems.subSystemList[index] = curSubSystem;
                    break;
                }
            }
        }

        UnityEngine.LowLevel.PlayerLoop.SetPlayerLoop(customSystems);
    }
}
