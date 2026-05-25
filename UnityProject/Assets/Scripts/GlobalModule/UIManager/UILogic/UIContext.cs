using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using XNLogger = XNClient.Logger.XNLogger;

public class UIContext
{
    public readonly UIContextType contextType;
    public LinkedList<UIPage> mainPageStack = new LinkedList<UIPage>();
    public List<UIPage> popupList = new List<UIPage>();
    public int baseCanvasOrder;

    public UIContext(UIContextType contextType)
    {
        this.contextType = contextType;
        baseCanvasOrder = UIUtils.GetUIContextBaseOrder(contextType);
    }

    public void Update()
    {
        if (mainPageStack.Count > 0) {
            mainPageStack.Last.Value.UpdatePage();
        }

        foreach (UIPage popupPage in popupList.ToList()) {
            if (popupList.Contains(popupPage)) {
                popupPage.UpdatePage();
            }
        }
    }

    public void OnDestroy()
    {

    }

    public TPage GetMainPage<TPage>() where TPage : UIPage
    {
        LinkedListNode<UIPage> node = mainPageStack.Last;
        string pageName = UIPage.GetPageName<TPage>();
        while (node != null) {
            if (node.Value.pageName == pageName) {
                return (TPage)node.Value;
            }

            node = node.Previous;
        }

        return null;
    }

    public TPage GetPopupPage<TPage>() where TPage : UIPage
    {
        TPage page = null;
        foreach (var _page in popupList) {
            if (_page.pageName == UIPage.GetPageName<TPage>()) {
                page = (TPage)_page;
                break;
            }
        }

        return page;
    }

    public void ShowMainPage(UIPage mainPage, bool isCachePage)
    {
        mainPage.canvasOrder = baseCanvasOrder + mainPageStack.Count * UIConfig.MAINPAGE_INCREASE_CANVAS_ORDER;
        mainPageStack.AddLast(mainPage);
        if (isCachePage) {
            mainPage.InitPage(this);
            mainPage.Open();
        } else {
            mainPage.LoadPage();
        }
        XNLogger.LogInfo("UIContext show main page.", ("contextType", contextType.ToString()), ("pageName", mainPage.pageName));
    }

    public bool CloseMainPage(UIPage mainPage)
    {
        if (mainPageStack.Contains(mainPage)) {
            if (mainPageStack.Count > 0) {
                if (mainPageStack.Last.Value != mainPage) {
                    XNLogger.LogWarn("Try to close main page not on stack top", ("pageName", mainPage.pageName), ("contextType", contextType.ToString()));
                    mainPageStack.Remove(mainPage);
                } else {
                    mainPageStack.RemoveLast();
                }
            }
            CloseAllPopupPages();

            if (mainPageStack.Count > 0) {
                mainPageStack.Last.Value.SetUIVisible(true);
            }
            XNLogger.LogInfo("UIContext close main page.", ("contextType", contextType.ToString()), ("pageName", mainPage.pageName));
            return true;
        } else {
            XNLogger.LogError("Target main page not in current context", ("pageName", mainPage.pageName), ("contextType", contextType.ToString()));
            return false;
        }
    }

    public void ShowPopupPage(UIPage popupPage, bool isCachePage)
    {
        popupPage.canvasOrder = GetPopupCanvasOrder(popupList.Count);
        if (isCachePage) {
            popupPage.InitPage(this);
            popupPage.Open();
        } else {
            popupPage.LoadPage();
        }
        popupList.Add(popupPage);
        XNLogger.LogInfo("UIContext show popup page.", ("contextType", contextType.ToString()), ("pageName", popupPage.pageName));
    }

    public bool ClosePopupPage(UIPage popupPage)
    {
        if (popupList.Contains(popupPage)) {
            popupList.Remove(popupPage);
            for (int i = 0; i < popupList.Count; i++) {
                popupList[i].canvasOrder = GetPopupCanvasOrder(i);
            }
            XNLogger.LogInfo("UIContext close popup page.", ("contextType", contextType.ToString()), ("pageName", popupPage.pageName));
            return true;
        } else {
            XNLogger.LogError("Target popup page not in current context", ("pageName", popupPage.pageName), ("contextType", contextType.ToString()));
            return false;
        }
    }

    private int GetPopupCanvasOrder(int popupIndex)
    {
        return baseCanvasOrder +
            mainPageStack.Count * UIConfig.MAINPAGE_INCREASE_CANVAS_ORDER +
            popupIndex * UIConfig.POPUP_INCREASE_CANVAS_ORDER;
    }

    public void CloseAllMainPages()
    {
        while (mainPageStack.Count > 0) {
            UIPage topPage = mainPageStack.Last.Value;
            topPage.ClosePage();
        }
        XNLogger.LogInfo("UIContext close all main pages.", ("contextType", contextType.ToString()));
    }

    public void CloseAllPopupPages()
    {
        // 创建副本防止迭代器遍历时删除报错
        foreach (var page in popupList.ToList()) {
            page.ClosePage();
        }
        popupList.Clear();
        XNLogger.LogInfo("UIContext close all popup pages.", ("contextType", contextType.ToString()));
    }

    public void UpdateUICamera(Camera uiCamera)
    {
        foreach (var mainPage in mainPageStack) {
            if (mainPage.isLoaded) {
                mainPage.canvas.worldCamera = uiCamera;
            }
        }

        foreach (var popupPage in popupList) {
            if (popupPage.isLoaded) {
                popupPage.canvas.worldCamera = uiCamera;
            }
        }
    }
}

