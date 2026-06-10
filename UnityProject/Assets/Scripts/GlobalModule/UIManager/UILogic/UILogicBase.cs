using System;
using System.Collections.Generic;
using UnityEngine;
using XNLogger = XNClient.Logger.XNLogger;

public abstract class UILogicBase : ITimerAttacher, IEventReceiver, IResourceLoadBinder
{
    public bool isLoaded;
    public bool isVisible;
    private GameObject _gameObject;
    public GameObject gameObject
    {
        get
        {
            if (!isLoaded) {
                XNLogger.LogError("Try get gameObject before ui resource is loaded", ("uiClsName", GetType().Name));
                return null;
            }
            return _gameObject;
        }
    }
    private Transform _transform;
    public Transform transform
    {
        get
        {
            if (!isLoaded) {
                XNLogger.LogError("Try get transform before ui resource is loaded", ("uiClsName", GetType().Name));
                return null;
            }
            if (_transform == null) {
                _transform = gameObject.transform;
            }
            return _transform;
        }
    }
    private RectTransform _rectTransform;
    public RectTransform rectTransform
    {
        get
        {
            if (!isLoaded) {
                XNLogger.LogError("Try get rectTransform before ui resorece is loaded.", ("pageName", GetType().Name));
                return null;
            }
            if (_rectTransform == null) {
                _rectTransform = gameObject.GetComponent<RectTransform>();
            }
            return _rectTransform;
        }
    }
    private List<Action> resourceLoadedCBs = new List<Action>();

    public void OnUnityResourceLoaded(GameObject uiGameObject)
    {
        _gameObject = uiGameObject;
        ApplyUILayer(uiGameObject);
        isLoaded = true;
        transform.SetParent(Global.Instance.uiManager.uiRoot.transform);
        transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        transform.localScale = Vector3.one;
        OnLoaded();

        foreach (var loadedCB in resourceLoadedCBs) {
            loadedCB.Invoke();
        }
        resourceLoadedCBs.Clear();

        Open();
    }

    public void Open()
    {
        gameObject.SetActive(true);
        OnOpen();
    }

    protected virtual void OnLoaded()
    {

    }

    private void ApplyUILayer(GameObject uiGameObject)
    {
        int uiLayer = LayerMask.NameToLayer(UIConfig.NAME_UI_LAYER);
        if (uiLayer < 0) {
            XNLogger.LogError("UI layer not found, apply ui layer failed.", ("layerName", UIConfig.NAME_UI_LAYER));
            return;
        }

        SetLayerRecursively(uiGameObject.transform, uiLayer);
    }

    private void SetLayerRecursively(Transform target, int layer)
    {
        target.gameObject.layer = layer;
        for (int i = 0; i < target.childCount; i++) {
            SetLayerRecursively(target.GetChild(i), layer);
        }
    }

    protected virtual void OnOpen()
    {

    }

    protected virtual void OnShow()
    {

    }

    protected virtual void OnUpdate()
    {

    }

    protected virtual void OnHide()
    {

    }

    protected virtual void OnClose()
    {

        resourceLoadedCBs.Clear();
        OnTimerAttacherDestroyed();
        OnEventReceiverDestroyed();
        OnResourceBinderDestroyed();
    }

    public virtual void SetUIVisible(bool isVisible)
    {
        if (!isLoaded) {
            AddResourceLoadedCB(() =>
            {
                SetUIVisible(isVisible);
            });
            return;
        }

        this.isVisible = isVisible;
        if (isVisible) {
            OnShow();
        } else {
            OnHide();
        }
    }

    public void AddResourceLoadedCB(Action loadedCB)
    {
        if (isLoaded) {
            loadedCB.Invoke();
        } else {
            resourceLoadedCBs.Add(loadedCB);
        }
    }

    #region Timer
    private List<string> _attachedTimerIds = new List<string>();
    public List<string> attachedTimerIds => _attachedTimerIds;

    public SecondTimeoutTimer SetSecondTimeout(float targetSeconds, Action timerCB)
    {
        return Global.Instance.timerManager.SetSecondTimeout(this, targetSeconds, timerCB);
    }

    public SecondIntervalTimer SetSecondInterval(float intervalSeconds, Action timerCB, int targetRepeatTimes = -1, float firstDelaySeconds = 0)
    {
        return Global.Instance.timerManager.SetSecondInterval(this, intervalSeconds, timerCB, targetRepeatTimes, firstDelaySeconds);
    }

    public FrameTimeoutTimer SetFrameTimeout(int targetFrames, Action timerCB)
    {
        return Global.Instance.timerManager.SetFrameTimeout(this, targetFrames, timerCB);
    }

    public FrameIntervalTimer SetFrameInterval(int intervalFrames, Action timerCB, int targetRepeatTimes = -1, int firstDelayFrames = 0)
    {
        return Global.Instance.timerManager.SetFrameInterval(this, intervalFrames, timerCB, targetRepeatTimes, firstDelayFrames);
    }

    public void OnTimerAttacherDestroyed()
    {
        Global.Instance.timerManager.RemoveTimersByAttacher(this);
    }
    #endregion

    #region Event
    private List<ISystemEventHandler> _registeredSystemEventHandlers = new List<ISystemEventHandler>();
    private List<IEntityEventHandler> _registeredEntityEventHandlers = new List<IEntityEventHandler>();
    public List<ISystemEventHandler> registeredSystemEventHandlers => _registeredSystemEventHandlers;
    public List<IEntityEventHandler> registeredEntityEventHandlers => _registeredEntityEventHandlers;

    public void OnEventReceiverDestroyed()
    {
        Global.Instance.eventManager.UnregisterEventsByReceiver(this);
    }

    public void EmitSystemEvent<TEvent>(TEvent systemEvent) where TEvent : SystemEventBase
    {
        Global.Instance.eventManager.EmitSystemEvent(systemEvent);
    }

    public void RegisterSystemEvent<TEvent>(Action<TEvent> eventCB) where TEvent : SystemEventBase
    {
        Global.Instance.eventManager.RegisterSystemEvent(this, eventCB);
    }

    public void UnregisterSystemEvent(ISystemEventHandler handler)
    {
        Global.Instance.eventManager.UnregisterSystemEvent(handler);
    }

    public void EmitEntityEvent(EntityBase entity, EntityEventBase entityEvent)
    {
        Global.Instance.eventManager.EmitEntityEvent(entity, entityEvent);
    }

    public void RegisterEntityEvent<TEntity, TEvent>(Action<TEntity, TEvent> eventCB) where TEntity : EntityBase where TEvent : EntityEventBase
    {
        Global.Instance.eventManager.RegisterEntityEvent(this, eventCB);
    }

    public void UnregisterEntityEvent(IEntityEventHandler handler)
    {
        Global.Instance.eventManager.UnregisterEntityEvent(handler);
    }
    #endregion

    #region Resource
    private string _binderId;
    public string binderId
    {
        get
        {
            if (string.IsNullOrEmpty(_binderId)) {
                _binderId = $"{GetType().Name}_{ResourceManager.ResourceBinderInstanceIds}";
                ResourceManager.ResourceBinderInstanceIds += 1;
            }
            return _binderId;
        }
    }

    private HashSet<string> _loadHandlerIds = new HashSet<string>();
    public HashSet<string> loadHandlerIds => _loadHandlerIds;

    public void OnResourceBinderDestroyed()
    {
        Global.Instance.resourceManager.OnResourceBinderDestroyed(binderId);
    }
    #endregion
}

