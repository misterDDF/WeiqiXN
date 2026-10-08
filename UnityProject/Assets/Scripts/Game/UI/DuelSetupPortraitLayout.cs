using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
public class DuelSetupPortraitLayout : MonoBehaviour
{
    [Serializable]
    public class Field
    {
        public RectTransform row;
        public TextMeshProUGUI label;
        public TMP_Dropdown dropdown;
    }

    [Serializable]
    public class TextAppearance
    {
        public TMP_Text text;
        public bool preserveColor;
        public float landscapeSize;
        public Color landscapeColor;
        public float portraitSize;
        public Color portraitColor;
        public TextAlignmentOptions landscapeAlignment;
        public TextAlignmentOptions portraitAlignment;
    }

    public RectTransform canvasContent;
    public RectTransform paper;
    public RectTransform settings;
    public GridLayoutGroup landscapeGrid;
    public RectTransform handle;
    public RectTransform eyebrow;
    public RectTransform heading;
    public RectTransform intro;
    public RectTransform close;
    public RectTransform headerLine;
    public RectTransform boardLabel;
    public RectTransform[] boards;
    public RectTransform rulesLabel;
    public RectTransform timeLabel;
    public Field[] ruleFields;
    public Field[] timeFields;
    public Field byoyomiCount;
    public Field byoyomiTime;
    public RectTransform summary;
    public RectTransform summaryLine;
    public TextMeshProUGUI summaryLabel;
    public TextMeshProUGUI summaryMain;
    public TextMeshProUGUI summaryDetail;
    public RectTransform start;
    public RectTransform footerHint;
    public TextAppearance[] textAppearances;
    public Color landscapePaperColor;
    public Color landscapeSummaryColor;
    public RectOffset padding = new RectOffset();
    public float rowHeight = 76f;
    public float rowSpacing = 12f;
    public float sectionSpacing = 26f;
    public float labelWidth = 146f;

    private Vector2 lastCanvasSize;
    private int lastVisibleFields = -1;
    private string lastSummary;
    private string lastDetail;
    private Image paperImage;
    private Image summaryImage;

    private void OnEnable()
    {
        if (landscapeGrid != null) landscapeGrid.enabled = false;
        lastVisibleFields = -1;
        Canvas.willRenderCanvases += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        Canvas.willRenderCanvases -= Refresh;
        if (landscapeGrid != null) landscapeGrid.enabled = true;
        if (paperImage != null) paperImage.color = landscapePaperColor;
        if (summaryImage != null) summaryImage.color = landscapeSummaryColor;
        if (textAppearances == null) return;
        foreach (TextAppearance appearance in textAppearances) {
            if (appearance.text == null) continue;
            appearance.text.fontSize = appearance.landscapeSize;
            appearance.text.alignment = appearance.landscapeAlignment;
            if (!appearance.preserveColor) appearance.text.color = appearance.landscapeColor;
        }
    }

    public void Rebuild()
    {
        lastVisibleFields = -1;
        Refresh();
    }

    private void Refresh()
    {
        if (!isActiveAndEnabled || canvasContent == null || paper == null || summaryMain == null) return;
        int visibleFields = 0;
        int bit = 0;
        foreach (Field field in ruleFields) {
            if (field.row.gameObject.activeSelf) visibleFields |= 1 << bit;
            bit++;
        }
        foreach (Field field in timeFields) {
            if (field.row.gameObject.activeSelf) visibleFields |= 1 << bit;
            bit++;
        }
        if (byoyomiCount.row.gameObject.activeSelf) visibleFields |= 1 << bit;
        if (byoyomiTime.row.gameObject.activeSelf) visibleFields |= 1 << (bit + 1);
        Vector2 canvasSize = canvasContent.rect.size;
        if (canvasSize.x <= 0 || canvasSize.y <= 0) return;
        if (visibleFields == lastVisibleFields && canvasSize == lastCanvasSize &&
            summaryMain.text == lastSummary && summaryDetail.text == lastDetail) return;

        lastVisibleFields = visibleFields;
        lastCanvasSize = canvasSize;
        lastSummary = summaryMain.text;
        lastDetail = summaryDetail.text;
        if (paperImage == null) paperImage = paper.GetComponent<Image>();
        if (summaryImage == null) summaryImage = summary.GetComponent<Image>();
        paperImage.color = UIPalette.Paper;
        summaryImage.color = Color.clear;
        float height = Arrange(1f, canvasSize.x);
        float availableHeight = Mathf.Max(1f, canvasSize.y - 64f);
        if (height > availableHeight) height = Arrange(availableHeight / height, canvasSize.x);
        paper.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
    }

    private float Arrange(float scale, float width)
    {
        foreach (TextAppearance appearance in textAppearances) {
            appearance.text.fontSize = appearance.portraitSize * scale;
            appearance.text.alignment = appearance.portraitAlignment;
            if (!appearance.preserveColor) appearance.text.color = appearance.portraitColor;
        }
        float left = padding.left * scale;
        float contentWidth = width - padding.horizontal * scale;
        float top = padding.top * scale;
        Place(handle, (width - 58f * scale) * 0.5f, top, 58f * scale, 5f * scale);
        Place(eyebrow, left, top + 26f * scale, contentWidth, 32f * scale);
        Place(heading, left, top + 60f * scale, contentWidth - 80f * scale, 80f * scale);
        Place(close, width - left - 72f * scale, top + 50f * scale, 72f * scale, 72f * scale);
        Place(intro, left, top + 142f * scale, contentWidth, 38f * scale);
        Place(headerLine, left, top + 196f * scale, contentWidth, 1f);
        Place(boardLabel, left, top + 220f * scale, contentWidth, 36f * scale);
        float boardWidth = (contentWidth - 24f * scale) / boards.Length;
        for (int index = 0; index < boards.Length; index++) {
            Place(boards[index], left + index * (boardWidth + 12f * scale), top + 264f * scale, boardWidth, 80f * scale);
        }

        float cursor = top + 344f * scale;
        cursor = ArrangeSection(ruleFields, rulesLabel, cursor, contentWidth, left, scale);
        cursor = ArrangeSection(timeFields, timeLabel, cursor, contentWidth, left, scale);
        bool hasByoyomi = byoyomiCount.row.gameObject.activeSelf || byoyomiTime.row.gameObject.activeSelf;
        if (hasByoyomi) {
            cursor += 16f * scale;
            float halfWidth = (contentWidth - 20f * scale) * 0.5f;
            ArrangeField(byoyomiCount, left, cursor, halfWidth, scale, true);
            ArrangeField(byoyomiTime, left + halfWidth + 20f * scale, cursor, halfWidth, scale, true);
            cursor += (rowHeight + 40f) * scale;
        }

        cursor += sectionSpacing * scale;
        Place(summaryLine, left, cursor, contentWidth, 1f);
        float summaryTop = cursor + 22f * scale;
        float mainHeight = Mathf.Max(36f * scale, summaryMain.GetPreferredValues(summaryMain.text, contentWidth, 0).y);
        float detailHeight = Mathf.Max(28f * scale, summaryDetail.GetPreferredValues(summaryDetail.text, contentWidth, 0).y);
        float summaryHeight = 36f * scale + mainHeight + 8f * scale + detailHeight;
        Place(summary, left, summaryTop, contentWidth, summaryHeight);
        Place(summaryLabel.rectTransform, 0, 0, contentWidth, 34f * scale);
        Place(summaryMain.rectTransform, 0, 36f * scale, contentWidth, mainHeight);
        Place(summaryDetail.rectTransform, 0, 44f * scale + mainHeight, contentWidth, detailHeight);
        cursor = summaryTop + summaryHeight + 16f * scale;
        Place(start, left, cursor, contentWidth, 84f * scale);
        cursor += 98f * scale;
        Place(footerHint, left, cursor, contentWidth, 36f * scale);
        return cursor + (36f + padding.bottom) * scale;
    }

    private float ArrangeSection(Field[] fields, RectTransform label, float cursor, float width, float left, float scale)
    {
        int visibleCount = 0;
        foreach (Field field in fields) if (field.row.gameObject.activeSelf) visibleCount++;
        label.gameObject.SetActive(visibleCount > 0);
        if (visibleCount == 0) return cursor;
        cursor += sectionSpacing * scale;
        Place(label, left, cursor, width, 38f * scale);
        cursor += 44f * scale;
        foreach (Field field in fields) {
            if (!field.row.gameObject.activeSelf) continue;
            ArrangeField(field, left, cursor, width, scale, false);
            cursor += (rowHeight + rowSpacing) * scale;
        }
        return cursor - rowSpacing * scale;
    }

    private void ArrangeField(Field field, float left, float top, float width, float scale, bool stacked)
    {
        Place(field.row, left, top, width, (rowHeight + (stacked ? 40f : 0)) * scale);
        Place(field.label.rectTransform, 0, stacked ? 0 : 18f * scale, stacked ? width : labelWidth * scale, 40f * scale);
        float dropdownLeft = stacked ? 0 : (labelWidth + 20f) * scale;
        Place((RectTransform)field.dropdown.transform, dropdownLeft, stacked ? 40f * scale : 0, width - dropdownLeft, rowHeight * scale);
    }

    private static void Place(RectTransform target, float left, float top, float width, float height)
    {
        Vector2 anchor = new Vector2(0, 1);
        target.anchorMin = anchor;
        target.anchorMax = anchor;
        target.pivot = anchor;
        target.anchoredPosition = new Vector2(left, -top);
        target.sizeDelta = new Vector2(width, height);
    }
}
