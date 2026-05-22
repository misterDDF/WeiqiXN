using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using XNLogger = XNClient.Logger.XNLogger;

public class UIManager : ModuleBase
{
    public GameObject uiRoot;
    public GameObject uiEventSystemGO;
    public Camera uiCamera;
    private Dictionary<UIContextType, UIContext> contextDict = new Dictionary<UIContextType, UIContext>();
    private class CachePageInfo
    {
        public float cacheDuration;
        public UIPage cachePage;

        public CachePageInfo(UIPage cachePage)
        {
            cacheDuration = 0;
            this.cachePage = cachePage;
        }
    }
    private Dictionary<string, CachePageInfo> cachePages = new Dictionary<string, CachePageInfo>();

    public override void Init()
    {
        Global.Instance.eventManager.RegisterSystemEvent<OnActiveSceneChanged>(this, OnActiveSceneChanged);
        Global.Instance.eventManager.RegisterSystemEvent<OnExitMainScene>(this, OnExitMainScene);

        uiRoot = new GameObject(UIConfig.NAME_UI_ROOT);
        GameObject.DontDestroyOnLoad(uiRoot);
        uiEventSystemGO = Global.Instance.resourceManager.LoadGamePrefabWithConfigId(UIConfig.UI_EVENTSYSTEM_CONFIG_ID);
        if (uiEventSystemGO != null) {
            GameObject.DontDestroyOnLoad(uiEventSystemGO);
        } else {
            XNLogger.LogError("UI event system go create failed!!!!!");
        }

        foreach (UIContextType type in Enum.GetValues(typeof(UIContextType))) {
            contextDict.TryAdd(type, new UIContext(type));
        }
    }

    public void OnActiveSceneChanged(OnActiveSceneChanged evt)
    {
        UpdateUICamera();
    }

    public void OnExitMainScene(OnExitMainScene evt)
    {
        if (contextDict.TryGetValue(UIContextType.Header, out var headerContext)) {
            headerContext.CloseAllMainPages();
        }
        if (contextDict.TryGetValue(UIContextType.General, out var generalContext)) {
            generalContext.CloseAllMainPages();
        }
    }

    public override void Update()
    {
        base.Update();

        List<string> pendingDeleteCachePage = new List<string>();
        foreach (var cachePageInfoKV in cachePages) {
            cachePageInfoKV.Value.cacheDuration += Time.deltaTime;
            if (cachePageInfoKV.Value.cacheDuration >= UIConfig.PAGE_GAMEOBJECT_CACHE_TIME) {
                GameObject.Destroy(cachePageInfoKV.Value.cachePage.gameObject);
                pendingDeleteCachePage.Add(cachePageInfoKV.Key);
            }
        }
        foreach (string pageName in pendingDeleteCachePage) {
            cachePages.Remove(pageName);
        }

        foreach (UIContext context in contextDict.Values) {
            context.Update();
        }
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
                if (cachePages.TryGetValue(pageName, out var cachePageInfo)) {
                    uiContext.ShowPopupPage(cachePageInfo.cachePage, true);
                    cachePages.Remove(pageName);
                } else {
                    TPage page = UIPage.CreatePageInstance<TPage>(uiContext);
                    uiContext.ShowPopupPage(page, false);
                }
            } else {
                XNLogger.LogWarn("Invalid context type for show popup page", ("contextType", contextType.ToString()));
            }
        } else {
            if (contextDict.TryGetValue(contextType, out var uiContext)) {
                TPage page = UIPage.CreatePageInstance<TPage>(uiContext);
                uiContext.ShowMainPage(page, false);
                cachePages.Remove(pageName);
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

        if (cachePages.TryGetValue(page.pageName, out var pageInfo)) {
            pageInfo.cacheDuration = 0;
            GameObject.Destroy(page.gameObject);
        } else {
            page.gameObject.SetActive(false);
            cachePages[page.pageName] = new CachePageInfo(page);
        }
    }

    public bool TryGetCachePage(string pageName, out UIPage cachePage)
    {
        cachePage = null;
        if (cachePages.TryGetValue(pageName, out var pageInfo)) {
            cachePage = pageInfo.cachePage;
            cachePages.Remove(pageName);
            return true;
        } else {
            return false;
        }
    }

    public void UpdateUICamera()
    {
        Scene activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (!activeScene.IsValid()) {
            uiCamera = null;
            XNLogger.LogError("Active scene invalid, update ui camera failed.");
            return;
        }

        foreach (var rootGO in activeScene.GetRootGameObjects()) {
            var camera = rootGO.GetComponentInChildren<Camera>();
            if (camera != null) {
                uiCamera = camera;
                XNLogger.LogInfo("Update ui camera success.", ("uiCameraName", uiCamera.gameObject.name));
                break;
            }
        }

        if (uiCamera != null) {
            foreach (var kvp in contextDict) {
                kvp.Value.UpdateUICamera(uiCamera);
            }
        } else {
            uiCamera = null;
            XNLogger.LogWarn("Camera not found in active scene, update ui camera failed.", ("sceneName", activeScene.name));
        }
    }
}
