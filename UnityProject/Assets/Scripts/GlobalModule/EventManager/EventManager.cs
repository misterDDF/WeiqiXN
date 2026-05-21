using System;
using System.Collections.Generic;
using XNClient.Logger;

public class EventManager : ModuleBase
{
    private Dictionary<string, List<ISystemEventHandler>> systemEventHandlers = new Dictionary<string, List<ISystemEventHandler>>();
    private Dictionary<string, List<IEntityEventHandler>> entityEventHandlers = new Dictionary<string, List<IEntityEventHandler>>();

    public override void Init()
    {

    }

    public override void OnDestroy()
    {
        systemEventHandlers.Clear();
        entityEventHandlers.Clear();
    }

    public void EmitSystemEvent<TEvent>(TEvent systemEvent) where TEvent : SystemEventBase
    {
        if (LoggerConfig.ENABLE_EVENT_VERBOSE_LOG) {
            XNLogger.LogInfo("Emit system event.", ("eventName", systemEvent.GetEventType()));
        }

        if (systemEventHandlers.TryGetValue(systemEvent.GetEventType(), out var handlerSet)) {
            foreach (var handler in handlerSet) {
                handler.Execute(systemEvent);
            }
        }
    }

    public ISystemEventHandler RegisterSystemEvent<TEvent>(IEventReceiver receiver, Action<TEvent> eventCB) where TEvent : SystemEventBase
    {
        SystemEventHandler<TEvent> handler = new SystemEventHandler<TEvent>(receiver, eventCB);
        List<ISystemEventHandler> handlerList;
        if (!systemEventHandlers.TryGetValue(SystemEventBase.GetEventType<TEvent>(), out handlerList)) {
            handlerList = new List<ISystemEventHandler>();
            systemEventHandlers.Add(SystemEventBase.GetEventType<TEvent>(), handlerList);
        }
        receiver.registeredSystemEventHandlers.Add(handler);
        handlerList.Add(handler);
        return handler;
    }

    public void UnregisterSystemEvent(ISystemEventHandler handler)
    {
        if (systemEventHandlers.TryGetValue(handler.eventType, out var handlerSet)) {
            handler.receiver.registeredSystemEventHandlers.Remove(handler);
            handlerSet.Remove(handler);
        }
    }

    public void EmitEntityEvent<TEntity, TEvent>(TEntity entity, TEvent entityEvent) where TEntity : EntityBase where TEvent : EntityEventBase
    {
        if (LoggerConfig.ENABLE_EVENT_VERBOSE_LOG) {
            XNLogger.LogInfo("Emit entity event.", ("eventName", entityEvent.GetEventType()), ("entityType", entity.entityType));
        }

        if (entityEventHandlers.TryGetValue(entityEvent.GetEventType(), out var handlerSet)) {
            foreach (var handler in handlerSet) {
                if (handler.CanHandle(entity)) {
                    handler.Execute(entity, entityEvent);
                }
            }
        }
    }

    public void EmitEntityEvent(EntityBase entity, EntityEventBase entityEvent)
    {
        if (LoggerConfig.ENABLE_EVENT_VERBOSE_LOG) {
            XNLogger.LogInfo("Emit entity event.", ("eventName", entityEvent.GetEventType()), ("entityType", entity.entityType));
        }

        if (entityEventHandlers.TryGetValue(entityEvent.GetEventType(), out var handlerSet)) {
            foreach (var handler in handlerSet) {
                if (handler.CanHandle(entity)) {
                    handler.Execute(entity, entityEvent);
                }
            }
        }
    }

    public IEntityEventHandler RegisterEntityEvent<TEntity, TEvent>(IEventReceiver receiver, Action<TEntity, TEvent> eventCB) where TEntity : EntityBase where TEvent : EntityEventBase
    {
        EntityEventHandler<TEntity, TEvent> handler = new EntityEventHandler<TEntity, TEvent>(receiver, eventCB);
        List<IEntityEventHandler> handlerList;
        if (!entityEventHandlers.TryGetValue(EntityEventBase.GetEventType<TEvent>(), out handlerList)) {
            handlerList = new List<IEntityEventHandler>();
            entityEventHandlers.Add(EntityEventBase.GetEventType<TEvent>(), handlerList);
        }
        receiver.registeredEntityEventHandlers.Add(handler);
        handlerList.Add(handler);
        return handler;
    }

    public void UnregisterEntityEvent(IEntityEventHandler handler)
    {
        if (entityEventHandlers.TryGetValue(handler.eventType, out var handlerSet)) {
            handler.receiver.registeredEntityEventHandlers.Remove(handler);
            handlerSet.Remove(handler);
        }
    }

    public void UnregisterEventsByReceiver(IEventReceiver eventReceiver)
    {
        foreach (var handler in eventReceiver.registeredSystemEventHandlers) {
            if (systemEventHandlers.TryGetValue(handler.eventType, out var handlerSet)) {
                handlerSet.Remove(handler);
            }
        }
        eventReceiver.registeredSystemEventHandlers.Clear();

        foreach (var handler in eventReceiver.registeredEntityEventHandlers) {
            if (entityEventHandlers.TryGetValue(handler.eventType, out var handlerSet)) {
                handlerSet.Remove(handler);
            }
        }
        eventReceiver.registeredEntityEventHandlers.Clear();
    }
}

