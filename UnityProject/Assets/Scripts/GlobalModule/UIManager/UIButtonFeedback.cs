using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 按钮反馈：底板的悬停、按下、禁用色由 Selectable 的 ColorTint（UIPalette 色块）负责，本组件补 ColorTint 管不到的子节点。
// 按下且指针在按钮内时，子节点下沉 1px；抬起、移出、不可交互或禁用时复位。下沉期间被外部改写位置（如布局重排）的子节点，复位时保留外部写入。
// 不可交互时文字和图标降低不透明度，与底板的禁用色一起读作“不可点”。
// 只实现指针进出、按下和抬起接口，不截走 ScrollRect 的拖动和滚轮。
[DisallowMultipleComponent]
[RequireComponent(typeof(Selectable))]
public class UIButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    public const float SinkDistance = 1f;
    public const float DisabledContentAlpha = 0.45f;

    private struct SunkChild
    {
        public Transform transform;
        public Vector3 restPosition;
        public Vector3 sunkPosition;
    }

    private readonly List<SunkChild> sunkChildren = new List<SunkChild>();
    private readonly List<Graphic> graphicBuffer = new List<Graphic>();
    private Selectable selectable;
    private bool isPointerDown;
    private bool isPointerInside;
    private bool isInteractable;
    private bool isSunk;

    private void Awake()
    {
        selectable = GetComponent<Selectable>();
    }

    // 淡出途中被隐藏时 Graphic 的补间会中断，重新启用时按当前状态直接设到终值。
    private void OnEnable()
    {
        isInteractable = selectable.IsInteractable();
        ApplyContentAlpha(0f);
    }

    private void OnDisable()
    {
        isPointerDown = false;
        isPointerInside = false;
        RefreshSink();
    }

    // Selectable 不提供可交互状态变化的回调，只能每帧比对。
    private void LateUpdate()
    {
        bool interactable = selectable.IsInteractable();
        if (interactable == isInteractable) {
            return;
        }

        isInteractable = interactable;
        ApplyContentAlpha(selectable.colors.fadeDuration);
        RefreshSink();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isPointerInside = true;
        RefreshSink();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isPointerInside = false;
        RefreshSink();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) {
            return;
        }

        isPointerDown = true;
        RefreshSink();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) {
            return;
        }

        isPointerDown = false;
        RefreshSink();
    }

    private void RefreshSink()
    {
        bool shouldSink = isPointerDown && isPointerInside && isInteractable && isActiveAndEnabled;
        if (shouldSink == isSunk) {
            return;
        }

        isSunk = shouldSink;
        if (isSunk) {
            Sink();
        } else {
            Restore();
        }
    }

    // 只移动直接子节点；底板若在子节点里，它所在的那一支不动。
    private void Sink()
    {
        Graphic plate = selectable.targetGraphic;
        foreach (Transform child in transform) {
            if (!child.gameObject.activeSelf || (plate != null && plate.transform.IsChildOf(child))) {
                continue;
            }

            Vector3 restPosition = child.localPosition;
            Vector3 sunkPosition = restPosition + Vector3.down * SinkDistance;
            child.localPosition = sunkPosition;
            sunkChildren.Add(new SunkChild { transform = child, restPosition = restPosition, sunkPosition = sunkPosition });
        }
    }

    private void Restore()
    {
        foreach (SunkChild child in sunkChildren) {
            if (child.transform != null && child.transform.localPosition == child.sunkPosition) {
                child.transform.localPosition = child.restPosition;
            }
        }

        sunkChildren.Clear();
    }

    // 用 CanvasRenderer 的透明度淡化，不改 Graphic.color，页面代码照常写文字颜色。
    // 底板归 ColorTint 管；嵌套的 Selectable 由它自己的反馈组件处理。
    private void ApplyContentAlpha(float duration)
    {
        float alpha = isInteractable ? 1f : DisabledContentAlpha;
        Graphic plate = selectable.targetGraphic;
        GetComponentsInChildren(true, graphicBuffer);
        foreach (Graphic graphic in graphicBuffer) {
            if (graphic != plate && graphic.GetComponentInParent<Selectable>(true) == selectable) {
                graphic.CrossFadeAlpha(alpha, duration, true);
            }
        }

        graphicBuffer.Clear();
    }
}
