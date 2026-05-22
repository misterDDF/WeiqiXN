using System.Collections.Generic;
using UnityEngine;

public abstract class UIWidget : UILogicBase
{
    public UILogicBase owner;
    protected List<UIWidget> childWidgets = new List<UIWidget>();

    public abstract string widgetName { get; }

    public static string GetWidgetName<TWidget>() where TWidget : UIWidget
    {
        return typeof(TWidget).Name;
    }

    public static TWidget CreateWidgetInstance<TWidget>(UILogicBase owner) where TWidget : UIWidget, new()
    {
        TWidget widget = new TWidget();
        widget.owner = owner;
        return widget;
    }

    public void InitWidget(UILogicBase owner)
    {
        this.owner = owner;
    }

    public void UpdateWidget()
    {
        if (!isLoaded) return;

        foreach (var widget in childWidgets) {
            widget.UpdateWidget();
        }

        OnUpdate();
    }

    public void CloseWidget()
    {
        foreach (var widget in childWidgets) {
            widget.CloseWidget();
        }
        OnHide();
        OnClose();
    }

    protected override void OnOpen()
    {
        base.OnOpen();

        foreach (var widget in childWidgets) {
            widget.Open();
        }
    }
}

public abstract class UIWidgetWithBinder<TBinder> : UIWidget where TBinder : UIBinderBase
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

    public override void SetUIVisible(bool isVisible)
    {
        base.SetUIVisible(isVisible);

        foreach (var widget in childWidgets) {
            widget.SetUIVisible(isVisible);
        }
    }

    public void LoadWidget(bool isAsync = true)
    {
        string assetPath = UIUtils.GetWidgetPrefabPath(widgetName);
        if (isAsync) {
            Global.Instance.resourceManager.LoadGamePrefabAsync(this, assetPath, OnUnityResourceLoaded);
        } else {
            GameObject widgetGO = Global.Instance.resourceManager.LoadGamePrefab(assetPath);
            if (widgetGO != null) {
                OnUnityResourceLoaded(widgetGO);
            }
        }
    }
}
