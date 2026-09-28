using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 弹窗开合过渡：打开时内容透明度 0 → 1，窗口从下方 6px 移到原位，180ms ease-out；关闭时反向播放，结束后回调销毁。
// CanvasGroup 运行时只挂在 Canvas 根下带图形的内容根上（PanelRoot / panel_root，或没有包装层时的 mask、panel_main），不改 prefab；
// 位移只作用于内容根里的非遮罩窗口节点，临时偏移 localPosition，期间外部写入（如 StateRoot 应用状态）视为新的静止位置。
// 自身 Update 使用真实时间，不依赖 UIContext.Update，页面逻辑关闭后仍能播完。
public class UIPageTransition : MonoBehaviour
{
    private const float DurationSeconds = 0.18f;
    private const float RiseDistance = 6f;

    private static readonly List<UIPageTransition> closingTransitions = new List<UIPageTransition>();

    private class MotionTarget
    {
        public Transform transform;
        public Vector3 restPosition;
        public Vector3 appliedPosition;
    }

    private readonly List<CanvasGroup> canvasGroups = new List<CanvasGroup>();
    private readonly List<MotionTarget> motionTargets = new List<MotionTarget>();
    private bool isTargetsCollected;
    private bool isClosing;
    private float progress;
    private Action onClosed;

    public static void PlayOpen(GameObject pageRoot)
    {
        if (pageRoot == null) {
            return;
        }

        UIPageTransition transition = pageRoot.GetComponent<UIPageTransition>();
        if (transition == null) {
            transition = pageRoot.AddComponent<UIPageTransition>();
        }
        transition.BeginOpen();
    }

    // 没有过渡或页面不可见时立即回调。
    public static void PlayClose(GameObject pageRoot, Action onClosed)
    {
        UIPageTransition transition = pageRoot != null ? pageRoot.GetComponent<UIPageTransition>() : null;
        Canvas canvas = pageRoot != null ? pageRoot.GetComponent<Canvas>() : null;
        if (transition == null || !pageRoot.activeInHierarchy || (canvas != null && !canvas.enabled)) {
            onClosed?.Invoke();
            return;
        }

        transition.BeginClose(onClosed);
    }

    // 场景退出时立即结束所有关闭过渡。
    public static void CompleteAllClosing()
    {
        foreach (UIPageTransition transition in closingTransitions.ToArray()) {
            transition.FinishClose();
        }
    }

    private void BeginOpen()
    {
        CollectTargets();
        isClosing = false;
        onClosed = null;
        closingTransitions.Remove(this);
        progress = 0f;
        SetBlocksRaycasts(true);
        Apply();
        enabled = true;
    }

    private void BeginClose(Action onClosed)
    {
        CollectTargets();
        this.onClosed = onClosed;
        if (!isClosing) {
            isClosing = true;
            closingTransitions.Add(this);
        }
        SetBlocksRaycasts(false);
        ClearSelectionInPage();
        enabled = true;
    }

    private void Update()
    {
        progress = Mathf.MoveTowards(progress, isClosing ? 0f : 1f, Time.unscaledDeltaTime / DurationSeconds);
        Apply();
        if (isClosing) {
            if (progress <= 0f) {
                FinishClose();
            }
        } else if (progress >= 1f) {
            enabled = false;
        }
    }

    private void OnDestroy()
    {
        closingTransitions.Remove(this);
    }

    private void FinishClose()
    {
        if (!isClosing) {
            return;
        }

        isClosing = false;
        closingTransitions.Remove(this);
        Action callback = onClosed;
        onClosed = null;
        callback?.Invoke();
    }

    // ease-out；关闭时 progress 反向走同一条曲线，即先慢后快。
    private void Apply()
    {
        float eased = 1f - (1f - progress) * (1f - progress);
        foreach (CanvasGroup canvasGroup in canvasGroups) {
            if (canvasGroup != null) {
                canvasGroup.alpha = eased;
            }
        }

        Vector3 offset = Vector3.down * (RiseDistance * (1f - eased));
        foreach (MotionTarget target in motionTargets) {
            if (target.transform == null) {
                continue;
            }

            if (target.transform.localPosition != target.appliedPosition) {
                target.restPosition = target.transform.localPosition;
            }
            target.appliedPosition = target.restPosition + offset;
            target.transform.localPosition = target.appliedPosition;
        }
    }

    private void CollectTargets()
    {
        if (isTargetsCollected) {
            return;
        }

        isTargetsCollected = true;
        foreach (Transform contentRoot in transform) {
            if (!HasGraphic(contentRoot)) {
                continue;
            }

            CanvasGroup canvasGroup = contentRoot.GetComponent<CanvasGroup>();
            if (canvasGroup == null) {
                canvasGroup = contentRoot.gameObject.AddComponent<CanvasGroup>();
            }
            canvasGroups.Add(canvasGroup);

            if (IsMask(contentRoot)) {
                continue;
            }

            if (!IsPanelRoot(contentRoot)) {
                AddMotionTarget(contentRoot);
                continue;
            }

            // 包装层本身铺满（可能自带遮罩图），只位移其中的窗口节点。
            foreach (Transform child in contentRoot) {
                if (!IsMask(child) && HasGraphic(child)) {
                    AddMotionTarget(child);
                }
            }
        }
    }

    private void AddMotionTarget(Transform target)
    {
        Vector3 position = target.localPosition;
        motionTargets.Add(new MotionTarget { transform = target, restPosition = position, appliedPosition = position });
    }

    private void SetBlocksRaycasts(bool blocksRaycasts)
    {
        foreach (CanvasGroup canvasGroup in canvasGroups) {
            if (canvasGroup != null) {
                canvasGroup.blocksRaycasts = blocksRaycasts;
            }
        }
    }

    // 关闭中的输入框不再接收键盘输入。
    private void ClearSelectionInPage()
    {
        EventSystem eventSystem = EventSystem.current;
        GameObject selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
        if (selected != null && selected.transform.IsChildOf(transform)) {
            eventSystem.SetSelectedGameObject(null);
        }
    }

    private static bool HasGraphic(Transform node)
    {
        return node.GetComponentInChildren<Graphic>(true) != null;
    }

    private static bool IsMask(Transform node)
    {
        return node.name.IndexOf("mask", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsPanelRoot(Transform node)
    {
        return string.Equals(node.name.Replace("_", string.Empty), "PanelRoot", StringComparison.OrdinalIgnoreCase);
    }
}
