using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using XNLogger = XNClient.Logger.XNLogger;

public class UIManager : ModuleBase
{
    public GameObject uiRoot;
    public GameObject uiEventSystemGO;
    public Camera uiCamera;
    public Camera sceneCamera;
    private Dictionary<UIContextType, UIContext> contextDict = new Dictionary<UIContextType, UIContext>();
    private bool isClosingSceneExitPages;

    public override void Init()
    {
        Global.Instance.eventManager.RegisterSystemEvent<OnActiveSceneChanged>(this, OnActiveSceneChanged);
        Global.Instance.eventManager.RegisterSystemEvent<OnExitMainScene>(this, OnExitMainScene);

        uiRoot = new GameObject(UIConfig.NAME_UI_ROOT);
        GameObject.DontDestroyOnLoad(uiRoot);
        EnsureUICamera();
        uiEventSystemGO = Global.Instance.resourceManager.LoadGamePrefabWithConfigId(UIConfig.UI_EVENTSYSTEM_CONFIG_ID);
        if (uiEventSystemGO != null) {
            GameObject.DontDestroyOnLoad(uiEventSystemGO);
        } else {
            XNLogger.LogError("UI event system go create failed!!!!!");
        }

        foreach (UIContextType type in Enum.GetValues(typeof(UIContextType))) {
            contextDict.TryAdd(type, new UIContext(type));
        }
        UpdateUICamera();
    }

    public void OnActiveSceneChanged(OnActiveSceneChanged evt)
    {
        UpdateUICamera();
    }

    public void OnExitMainScene(OnExitMainScene evt)
    {
        // 场景退出时弹窗直接销毁，不播放关闭过渡；仍在播放的关闭过渡立即结束。
        isClosingSceneExitPages = true;
        try {
            ConfirmPopup.CloseSceneExitRequests();
            foreach (UIContext context in contextDict.Values) {
                context.CloseSceneExitPages();
            }
        } finally {
            isClosingSceneExitPages = false;
        }
        UIPageTransition.CompleteAllClosing();
    }

    public override void Update()
    {
        base.Update();

        if (Input.GetKeyDown(KeyCode.Escape)) {
            TryHandleBackNavigation();
        }

        foreach (UIContext context in contextDict.Values) {
            context.Update();
        }
    }

    public bool TryHandleBackNavigation()
    {
        foreach (UIContext context in contextDict.Values.OrderByDescending(value => value.baseCanvasOrder)) {
            UIPage topPopupPage = context.GetTopPopupPage();
            if (topPopupPage != null) {
                return topPopupPage.TryHandleBackNavigation();
            }

            UIPage topMainPage = context.GetTopMainPage();
            if (topMainPage != null && topMainPage.TryHandleBackNavigation()) {
                return true;
            }
        }

        return false;
    }

    public void ShowPage<TPage>() where TPage : UIPage, new()
    {
        string pageName = UIPage.GetPageName<TPage>();
        UiPageDataType uiConfig = UiPageDataType.GetConfigData(pageName);
        if (uiConfig == null) {
            XNLogger.LogError("Invalid ui config, show page failed.", ("pageName", pageName));
            return;
        }
        UIContextType contextType = UIUtils.ParseUIContextType(uiConfig.contextType);

        if (uiConfig.isPopup) {
            if (contextDict.TryGetValue(contextType, out var uiContext)) {
                TPage page = UIPage.CreatePageInstance<TPage>(uiContext);
                uiContext.ShowPopupPage(page, false);
            } else {
                XNLogger.LogWarn("Invalid context type for show popup page", ("contextType", contextType.ToString()));
            }
        } else {
            if (contextDict.TryGetValue(contextType, out var uiContext)) {
                TPage page = UIPage.CreatePageInstance<TPage>(uiContext);
                uiContext.ShowMainPage(page, false);
            } else {
                XNLogger.LogWarn("Invalid context type for show main page", ("contextType", contextType.ToString()));
            }
        }
    }

    public void ClosePage<TPage>() where TPage : UIPage
    {
        TryClosePage<TPage>(true);
    }

    public bool TryClosePage<TPage>(bool logIfMissing = false) where TPage : UIPage
    {
        string pageName = UIPage.GetPageName<TPage>();
        UiPageDataType uiConfig = UiPageDataType.GetConfigData(pageName);
        if (uiConfig == null) {
            XNLogger.LogError("Invalid ui config, close page failed.", ("pageName", pageName));
            return false;
        }
        UIContextType contextType = UIUtils.ParseUIContextType(uiConfig.contextType);

        UIContext uiContext;
        if (uiConfig.isPopup) {
            if (contextDict.TryGetValue(contextType, out uiContext)) {
                TPage page = uiContext.GetPopupPage<TPage>();
                if (page != null) {
                    page.ClosePage();
                    return true;
                } else {
                    if (logIfMissing) {
                        XNLogger.LogWarn("Page not found, close popup page failed.", ("pageName", pageName), ("contextType", contextType.ToString()));
                    }
                }
            }
        } else {
            if (contextDict.TryGetValue(contextType, out uiContext)) {
                TPage page = uiContext.GetMainPage<TPage>();
                if (page != null) {
                    page.ClosePage();
                    return true;
                } else {
                    if (logIfMissing) {
                        XNLogger.LogWarn("Page not found, close main page failed.", ("pageName", pageName), ("contextType", contextType.ToString()));
                    }
                }
            }
        }

        return false;
    }

    public void ClosePage(UIPage page)
    {
        if (page.pageConfig.isPopup) {
            if (page.owner.ClosePopupPage(page)) {
                RecycleClosedPage(page);
            }
        } else {
            if (page.owner.CloseMainPage(page)) {
                RecycleClosedPage(page);
            }
        }
    }

    private void RecycleClosedPage(UIPage page)
    {
        if (!page.isLoaded) {
            return;
        }

        GameObject pageGO = page.gameObject;
        if (!page.pageConfig.isPopup || isClosingSceneExitPages) {
            GameObject.Destroy(pageGO);
            return;
        }

        // 弹窗播完关闭过渡再销毁；页面逻辑已从弹窗列表移除，同名弹窗可以立即重新打开。
        UIPageTransition.PlayClose(pageGO, () => GameObject.Destroy(pageGO));
    }

    public void UpdateUICamera()
    {
        EnsureUICamera();
        UpdateSceneCamera();

        if (uiCamera != null) {
            foreach (var kvp in contextDict) {
                kvp.Value.UpdateUICamera(uiCamera);
            }
        } else {
            XNLogger.LogError("UI camera not found, update ui camera failed.");
        }
    }

    public Camera GetSceneCamera()
    {
        if (sceneCamera != null) {
            return sceneCamera;
        }

        sceneCamera = Camera.main;
        if (sceneCamera != null) {
            ConfigureSceneCamera(sceneCamera);
        }
        return sceneCamera;
    }

    private void EnsureUICamera()
    {
        if (uiCamera == null) {
            GameObject uiCameraGO = new GameObject(UIConfig.NAME_UI_CAMERA);
            if (uiRoot != null) {
                uiCameraGO.transform.SetParent(uiRoot.transform, false);
            }
            uiCamera = uiCameraGO.AddComponent<Camera>();
        }

        ConfigureUICamera(uiCamera);
    }

    private void ConfigureUICamera(Camera camera)
    {
        if (camera == null) {
            return;
        }

        int uiLayerMask = UIConfig.GetUILayerMask();
        if (uiLayerMask == 0) {
            XNLogger.LogError("UI layer not found, configure ui camera failed.", ("layerName", UIConfig.NAME_UI_LAYER));
            return;
        }

        camera.clearFlags = CameraClearFlags.Nothing;
        camera.cullingMask = uiLayerMask;
        camera.eventMask = uiLayerMask;
        camera.depth = UIConfig.UI_CAMERA_DEPTH;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = UIConfig.UI_CAMERA_FAR_CLIP_PLANE;
        camera.useOcclusionCulling = false;
        camera.allowHDR = false;
        camera.allowMSAA = false;

        var cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
        if (cameraData == null) {
            cameraData = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
        }
        cameraData.renderType = CameraRenderType.Base;
        cameraData.renderShadows = false;
        cameraData.renderPostProcessing = false;
    }

    private void UpdateSceneCamera()
    {
        Scene activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (!activeScene.IsValid()) {
            sceneCamera = null;
            XNLogger.LogError("Active scene invalid, update scene camera failed.");
            return;
        }

        sceneCamera = FindActiveSceneCamera(activeScene);
        if (sceneCamera == null) {
            XNLogger.LogWarn("Camera not found in active scene, update scene camera failed.", ("sceneName", activeScene.name));
            return;
        }

        ConfigureSceneCamera(sceneCamera);
        XNLogger.LogInfo("Update scene camera success.", ("sceneCameraName", sceneCamera.gameObject.name));
    }

    private Camera FindActiveSceneCamera(Scene activeScene)
    {
        Camera mainCamera = Camera.main;
        if (mainCamera != null && mainCamera.gameObject.scene == activeScene) {
            return mainCamera;
        }

        foreach (var rootGO in activeScene.GetRootGameObjects()) {
            var camera = rootGO.GetComponentInChildren<Camera>();
            if (camera != null) {
                return camera;
            }
        }

        return null;
    }

    private void ConfigureSceneCamera(Camera camera)
    {
        if (camera == null) {
            return;
        }

        int uiLayerMask = UIConfig.GetUILayerMask();
        if (uiLayerMask != 0) {
            camera.cullingMask &= ~uiLayerMask;
        }

        var cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
        if (cameraData == null) {
            return;
        }

        cameraData.renderType = CameraRenderType.Base;
        if (uiCamera != null && cameraData.cameraStack.Contains(uiCamera)) {
            cameraData.cameraStack.Remove(uiCamera);
        }
    }
}
