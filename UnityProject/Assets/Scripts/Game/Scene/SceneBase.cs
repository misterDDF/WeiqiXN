using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using XNLogger = XNClient.Logger.XNLogger;

public class SceneBase : SavableObj, ITimerAttacher, IEventReceiver, IResourceLoadBinder
{
    public readonly SceneDataType configData;
    public SceneCreateParams sceneCreateParams;
    public bool isLoaded;
    protected AsyncOperation unitySceneLoadAsync;
    public Dictionary<string, EntityBase> entityDict = new Dictionary<string, EntityBase>();
    public Dictionary<string, HashSet<EntityBase>> entityTypeDict = new Dictionary<string, HashSet<EntityBase>>();
    public Dictionary<Type, SceneComponentBase> compDict = new Dictionary<Type, SceneComponentBase>();

    protected UnityEngine.SceneManagement.Scene unityScene;
    private List<SystemBase> systemList = new List<SystemBase>();
    private HashSet<string> systemNames = new HashSet<string>();

    public bool isMainScene => Global.Instance.sceneManager.mainScene == this;

    public SceneBase(SceneDataType configData, SceneCreateParams sceneCreateParams)
    {
        this.configData = configData;
        this.sceneCreateParams = sceneCreateParams;
    }

    #region LifeCycle
    public void OnUnitySceneLoaded(UnityEngine.SceneManagement.Scene unityScene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (unityScene.name != configData.unitySceneName) {
            return;
        }

        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnUnitySceneLoaded;
        this.unityScene = unityScene;
        unitySceneLoadAsync = null;
        isLoaded = true;
        if (isMainScene) {
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(unityScene);
            EmitSystemEvent(new OnActiveSceneChanged());
        }
        OnSceneLoaded();
        XNLogger.LogInfo("Unity scene load success.", ("sceneTypeId", configData.id), ("unitySceneName", unityScene.name));
    }

    public virtual void OnSceneLoaded()
    {
        Global.Instance.uiManager.TryClosePage<LoadingPage>();
        Global.ReleaseKeepAwake(Global.KeepAwakeReason.Startup);
    }

    protected virtual void OnUpdate()
    {
        if (!isLoaded && unitySceneLoadAsync != null) {
            float progress = Mathf.Clamp01(unitySceneLoadAsync.progress / 0.9f);
            LoadingPage.SetProgress(
                MessageText.Get("scene_loading_status"),
                MessageText.Format("scene_loading_detail", configData.unitySceneName),
                progress);
            return;
        }

        foreach (var system in systemList) {
            system.OnUpdate();
        }
    }

    public virtual void OnSceneExit()
    {
        foreach (var entity in entityDict.Values.ToList()) {
            entity.Destroy();
        }
        entityDict.Clear();
        foreach (var comp in compDict.Values.ToList()) {
            comp.OnDestroy();
        }
        compDict.Clear();

        if (!isLoaded) {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnUnitySceneLoaded;
        }
        OnTimerAttacherDestroyed();
        OnEventReceiverDestroyed();
        OnResourceBinderDestroyed();

        if (isMainScene) {
            EmitSystemEvent(new OnExitMainScene(this));
        }
    }

    public void RestoreSceneData(string saveFilePath)
    {
        Global.Instance.gameSaveManager.LoadData(this, saveFilePath);
    }

    public void Update()
    {
        OnUpdate();
    }

    public void AddComponent<TComponent>(TComponent comp) where TComponent : SceneComponentBase
    {
        if (compDict.ContainsKey(typeof(TComponent))) {
            XNLogger.LogError("Try add duplicated component to scene, add scene component failed.", ("component", typeof(TComponent).Name));
        } else {
            compDict[typeof(TComponent)] = comp;
        }
    }

    public TComponent GetComponent<TComponent>() where TComponent : SceneComponentBase
    {
        if (compDict.TryGetValue(typeof(TComponent), out SceneComponentBase comp)) {
            return (TComponent)comp;
        } else {
            return null;
        }
    }
    #endregion

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

    public void OnEventReceiverDestroyed()
    {
        Global.Instance.eventManager.UnregisterEventsByReceiver(this);
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

    public void LoadScene()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnUnitySceneLoaded;
        try {
            unitySceneLoadAsync = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(configData.unitySceneName);
            LoadingPage.SetProgress(
                MessageText.Get("scene_loading_status"),
                MessageText.Format("scene_loading_detail", configData.unitySceneName),
                0f);
            if (!LoadingPage.hasActivePage) {
                Global.Instance.uiManager.ShowPage<LoadingPage>();
            }
            XNLogger.LogInfo("Load scene async start.", ("sceneTypeId", configData.id), ("unitySceneName", configData.unitySceneName));
        }
        catch (Exception ex) {
            XNLogger.LogError("Load unity scene async error.", ("unitySceneName", configData.unitySceneName), ("exception", ex.Message));
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnUnitySceneLoaded;
        }
    }

    protected void AddSystem(SystemBase system)
    {
        if (systemNames.Contains(system.systemName)) {
            XNLogger.LogError("Duplicated system add to same scene. systemName:{system.systemName}", ("systemName", system.systemName));
            return;
        }
        system.Init();
        systemList.Add(system);
        systemNames.Add(system.systemName);
    }

    public TSystem GetSystem<TSystem>() where TSystem : SystemBase
    {
        foreach (SystemBase system in systemList) {
            if (system is TSystem targetSystem) {
                return targetSystem;
            }
        }

        return null;
    }

    public void AddEntity(EntityBase entity)
    {
        if (entity == null) {
            XNLogger.LogError("Try add null entity, add entity failed.", ("sceneTypeId", configData.id));
            return;
        }
        if (entityDict.ContainsKey(entity.guid)) {
            XNLogger.LogError("Duplicated entity guid, add entity failed.", ("guid", entity.guid));
            return;
        }
        entityDict[entity.guid] = entity;

        HashSet<EntityBase> entSet;
        if (!entityTypeDict.TryGetValue(entity.entityType, out entSet)) {
            entSet = new HashSet<EntityBase>();
            entityTypeDict[entity.entityType] = entSet;
        }
        entSet.Add(entity);
        EmitEntityEvent(entity, new OnEntityCreated());
        XNLogger.LogInfo("Add entity success.", ("guid", entity.guid));
    }

    public void RemoveEntity(EntityBase entity)
    {
        if (!entityDict.ContainsKey(entity.guid)) {
            XNLogger.LogError("Target entity not in scene, remove entity failed.", ("guid", entity.guid), ("sceneTypeId", configData.id));
            return;
        }
        EmitEntityEvent(entity, new OnEntityDestroyed());
        entityDict.Remove(entity.guid);

        HashSet<EntityBase> entSet;
        if (entityTypeDict.TryGetValue(entity.entityType, out entSet)) {
            entSet.Remove(entity);
        }
        XNLogger.LogInfo("Remove entity success.", ("guid", entity.guid), ("sceneTypeId", configData.id));
    }

    public EntityBase GetEntity(string guid)
    {
        if (entityDict.TryGetValue(guid, out var entity)) {
            return entity;
        }

        return null;
    }

    public TEntity GetEntity<TEntity>(string guid) where TEntity : EntityBase
    {
        if (entityDict.TryGetValue(guid, out var entity)) {
            if (entity.entityType == EntityBase.GetEntityType<TEntity>()) {
                return (TEntity)entity;
            }
        }

        return null;
    }
}

