using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using XNLogger = XNClient.Logger.XNLogger;

public abstract class UIPage : UILogicBase
{
    public UIContext owner;
    private int _canvasOrder;
    public int canvasOrder
    {
        get
        {
            return _canvasOrder;
        }
        set
        {
            if (isLoaded) {
                canvas.sortingOrder = value;
            } else {
                AddResourceLoadedCB(() =>
                {
                    canvas.sortingOrder = value;
                });
            }
            _canvasOrder = value;
        }
    }
    public UiPageDataType pageConfig
    {
        get
        {
            return UiPageDataType.GetConfigData(pageName);
        }
    }

    public abstract string pageName { get; }
    protected List<UIWidget> childWidgets = new List<UIWidget>();

    private Canvas _canvas;
    public Canvas canvas
    {
        get
        {
            if (!isLoaded) {
                XNLogger.LogError("Try get canvas before ui resorece is loaded.", ("pageName", GetType().Name));
                return null;
            }
            if (_canvas == null) {
                _canvas = gameObject.GetComponent<Canvas>();
            }
            return _canvas;
        }
    }

    public static string GetPageName<TPage>() where TPage : UIPage
    {
        return typeof(TPage).Name;
    }

    public static TPage CreatePageInstance<TPage>(UIContext owner) where TPage : UIPage, new()
    {
        TPage page = new TPage();
        page.owner = owner;
        return page;
    }

    public void InitPage(UIContext owner)
    {
        this.owner = owner;
    }

    protected override void OnLoaded()
    {
        base.OnLoaded();

        var uiCamera = Global.Instance.uiManager.uiCamera;
        if (uiCamera != null) {
            canvas.worldCamera = uiCamera;
        }

        UICanvasResolutionProfile.ApplyRuntimeResolution(gameObject.GetComponent<CanvasScaler>());
    }

    protected override void OnOpen()
    {
        base.OnOpen();

        if (!pageConfig.isPopup) {
            if (owner.mainPageStack.Last?.Value == this) {
                if (owner.mainPageStack.Count > 1) {
                    var previousPage = owner.mainPageStack.Last.Previous.Value;
                    previousPage.SetUIVisible(false);
                }
                SetUIVisible(true);
            } else {
                SetUIVisible(false);
            }
        } else {
            SetUIVisible(true);
        }

        foreach (var widget in childWidgets) {
            widget.Open();
        }
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();

        foreach (var widget in childWidgets) {
            widget.UpdateWidget();
        }
    }

    protected override void OnClose()
    {
        foreach (var widget in childWidgets) {
            widget.CloseWidget();
        }

        base.OnClose();
    }

    public override void SetUIVisible(bool isVisible)
    {
        base.SetUIVisible(isVisible);
        canvas.enabled = isVisible;

        foreach (var widget in childWidgets) {
            widget.SetUIVisible(isVisible);
        }
    }

    public void LoadPage()
    {
        string assetPath = UIUtils.GetPagePrefabPath(pageName);
        bool isAsync = pageConfig == null || pageConfig.isLoadAsync;
        if (isAsync) {
            Global.Instance.resourceManager.LoadGamePrefabAsync(this, assetPath, OnUnityResourceLoaded);
        } else {
            GameObject pageGO = Global.Instance.resourceManager.LoadGamePrefab(assetPath);
            if (pageGO != null) {
                OnUnityResourceLoaded(pageGO);
            }
        }
    }

    public void UpdatePage()
    {
        if (!isLoaded) return;

        OnUpdate();
    }

    public void ClosePage()
    {
        foreach (var widget in childWidgets) {
            widget.CloseWidget();
        }
        OnHide();
        Global.Instance.uiManager.ClosePage(this);
        OnClose();
    }
}

public abstract class UIPageWithBinder<TBinder> : UIPage where TBinder : UIBinderBase
{
    public TBinder binder;

    protected override void OnLoaded()
    {
        base.OnLoaded();
        binder = gameObject.GetComponent<TBinder>();
        binder.InitWidgets(this);

        foreach (var widgetKV in binder.binderWidgets) {
            if (binder.binderWidgetGOs.TryGetValue(widgetKV.Key, out var widgetGO)) {
                UIWidget widget = widgetKV.Value;
                childWidgets.Add(widget);
                widget.OnUnityResourceLoaded(widgetGO);
            }
        }
    }
}

