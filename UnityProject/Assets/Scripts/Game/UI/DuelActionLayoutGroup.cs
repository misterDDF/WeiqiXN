using UnityEngine;
using UnityEngine.UI;

public class DuelActionLayoutGroup : LayoutGroup
{
    [SerializeField] private float spacing;

    public override void CalculateLayoutInputHorizontal()
    {
        base.CalculateLayoutInputHorizontal();
        SetLayoutInputForAxis(padding.horizontal, -1f, -1f, 0);
    }

    public override void CalculateLayoutInputVertical()
    {
        SetLayoutInputForAxis(padding.vertical, -1f, -1f, 1);
    }

    public override void SetLayoutHorizontal()
    {
        ArrangeChildren();
    }

    public override void SetLayoutVertical()
    {
        ArrangeChildren();
    }

    private void ArrangeChildren()
    {
        int childCount = rectChildren.Count;
        if (childCount == 0) {
            return;
        }

        Rect rect = rectTransform.rect;
        bool horizontal = rect.height < 150f;
        float availableWidth = Mathf.Max(0f, rect.width - padding.horizontal - spacing * (horizontal ? childCount - 1 : 0));
        float availableHeight = Mathf.Max(0f, rect.height - padding.vertical - spacing * (!horizontal ? childCount - 1 : 0));
        float childWidth = horizontal ? availableWidth / childCount : availableWidth;
        float childHeight = horizontal ? availableHeight : availableHeight / childCount;

        for (int index = 0; index < childCount; index++) {
            RectTransform child = rectChildren[index];
            float x = padding.left + (horizontal ? index * (childWidth + spacing) : 0f);
            float y = padding.top + (!horizontal ? index * (childHeight + spacing) : 0f);
            SetChildAlongAxis(child, 0, x, childWidth);
            SetChildAlongAxis(child, 1, y, childHeight);
        }
    }
}
