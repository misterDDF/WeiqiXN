using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using XNClient.Logger;

public static class UIUtils
{
    public static string GetPagePrefabPath(string pageName)
    {
        return $"UI/Prefab/Page/{pageName}";
    }

    public static string GetWidgetPrefabPath(string widgetName)
    {
        return $"UI/Prefab/Widget/{widgetName}";
    }

    public static UIContextType ParseUIContextType(string contextTypeStr)
    {
        if (Enum.TryParse(contextTypeStr, out UIContextType t)) {
            return t;
        } else {
            XNLogger.LogError("Parse ui context type string failed.", ("contextTypeStr", contextTypeStr));
            return UIContextType.General;
        }
    }

    public static int GetUIContextBaseOrder(UIContextType contextType)
    {
        int typeValue = (int)contextType;
        return typeValue * UIConfig.CONTEXT_INCREASE_CANVAS_ORDER;
    }

    public static bool IsPortrait(UnityEngine.Rect rect)
    {
        return rect.height > rect.width;
    }

    public static bool IsPortrait(UnityEngine.RectTransform rectTransform)
    {
        return rectTransform != null && IsPortrait(rectTransform.rect);
    }

    public static int GetVisibleListItemCount(RectTransform content, float itemHeight, float fallbackSpacing = 0f)
    {
        if (content == null || itemHeight <= 0f) {
            return 1;
        }

        Canvas.ForceUpdateCanvases();
        float contentHeight = content.rect.height;
        if (contentHeight <= 0f) {
            return 1;
        }

        float spacing = fallbackSpacing;
        VerticalLayoutGroup layoutGroup = content.GetComponent<VerticalLayoutGroup>();
        if (layoutGroup != null) {
            spacing = layoutGroup.spacing;
            contentHeight -= layoutGroup.padding.vertical;
        }

        float safeSpacing = Mathf.Max(0f, spacing);
        float itemStep = itemHeight + safeSpacing;
        if (itemStep <= 0f) {
            return 1;
        }

        int visibleCount = Mathf.FloorToInt((Mathf.Max(0f, contentHeight) + safeSpacing) / itemStep);
        return Mathf.Max(1, visibleCount);
    }

    public static bool IsPointerOverUI()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null) {
            return false;
        }

        for (int i = 0; i < Input.touchCount; i++) {
            if (eventSystem.IsPointerOverGameObject(Input.GetTouch(i).fingerId)) {
                return true;
            }
        }

        return eventSystem.IsPointerOverGameObject();
    }
}

