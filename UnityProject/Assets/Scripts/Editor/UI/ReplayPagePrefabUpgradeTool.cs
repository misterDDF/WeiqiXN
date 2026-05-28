#if UNITY_EDITOR
using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class ReplayPagePrefabUpgradeTool
{
    private const string PrefabPath = "Assets/UI/Prefab/Page/ReplayPage.prefab";

    [MenuItem("WeiqiXN/UI/Upgrade Replay Page Chart")]
    public static void UpgradeReplayPageChart()
    {
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
        try {
            Transform panelSide = FindChildRecursive(prefabRoot.transform, "panel_side");
            if (panelSide == null) {
                Debug.LogError("ReplayPage upgrade failed: panel_side not found.");
                return;
            }

            RectTransform panelSideRect = panelSide.GetComponent<RectTransform>();
            panelSideRect.anchoredPosition = new Vector2(-24f, -72f);
            panelSideRect.sizeDelta = new Vector2(360f, 300f);

            TextMeshProUGUI moveDetail = FindChildRecursive(panelSide, "txt_move_detail")?.GetComponent<TextMeshProUGUI>();
            if (moveDetail != null) {
                RectTransform rect = moveDetail.rectTransform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.anchoredPosition = new Vector2(0f, -30f);
                rect.sizeDelta = new Vector2(-36f, 42f);
            }

            TextMeshProUGUI actionHint = FindChildRecursive(panelSide, "txt_analysis_placeholder")?.GetComponent<TextMeshProUGUI>();
            if (actionHint != null) {
                RectTransform rect = actionHint.rectTransform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.anchoredPosition = new Vector2(0f, -76f);
                rect.sizeDelta = new Vector2(-36f, 34f);
                actionHint.fontSize = 14f;
            }

            RectTransform chartArea = EnsureRect(panelSide, "rect_chart_area");
            chartArea.anchorMin = new Vector2(0f, 0f);
            chartArea.anchorMax = new Vector2(1f, 0f);
            chartArea.pivot = new Vector2(0.5f, 0f);
            chartArea.anchoredPosition = new Vector2(0f, 18f);
            chartArea.sizeDelta = new Vector2(-36f, 158f);

            Image chartBackground = chartArea.GetComponent<Image>();
            if (chartBackground == null) {
                chartBackground = chartArea.gameObject.AddComponent<Image>();
            }
            chartBackground.color = new Color(0.02f, 0.02f, 0.02f, 0.52f);
            chartBackground.raycastTarget = false;

            ReplayAnalysisChartGraphic chart = EnsureChildComponent<ReplayAnalysisChartGraphic>(chartArea, "chart_analysis");
            RectTransform chartRect = chart.rectTransform;
            chartRect.anchorMin = Vector2.zero;
            chartRect.anchorMax = Vector2.one;
            chartRect.offsetMin = new Vector2(8f, 18f);
            chartRect.offsetMax = new Vector2(-8f, -18f);
            chart.raycastTarget = false;

            Image scrubberHit = EnsureChildComponent<Image>(chartArea, "img_move_scrubber_hit");
            RectTransform scrubRect = scrubberHit.rectTransform;
            scrubRect.anchorMin = Vector2.zero;
            scrubRect.anchorMax = Vector2.one;
            scrubRect.offsetMin = Vector2.zero;
            scrubRect.offsetMax = Vector2.zero;
            scrubberHit.color = new Color(1f, 1f, 1f, 0.001f);
            scrubberHit.raycastTarget = true;

            Image cursor = EnsureChildComponent<Image>(chartArea, "img_chart_cursor");
            RectTransform cursorRect = cursor.rectTransform;
            cursorRect.anchorMin = new Vector2(0.5f, 0f);
            cursorRect.anchorMax = new Vector2(0.5f, 1f);
            cursorRect.pivot = new Vector2(0.5f, 0.5f);
            cursorRect.anchoredPosition = Vector2.zero;
            cursorRect.sizeDelta = new Vector2(3f, 0f);
            cursor.color = new Color(1f, 1f, 1f, 0.86f);
            cursor.raycastTarget = false;

            TextMeshProUGUI preview = EnsureChildText(chartArea, "txt_scrub_preview");
            RectTransform previewRect = preview.rectTransform;
            previewRect.anchorMin = new Vector2(0f, 0f);
            previewRect.anchorMax = new Vector2(1f, 0f);
            previewRect.pivot = new Vector2(0.5f, 0f);
            previewRect.anchoredPosition = new Vector2(0f, 2f);
            previewRect.sizeDelta = new Vector2(-8f, 18f);
            preview.fontSize = 13f;
            preview.alignment = TextAlignmentOptions.Center;
            preview.color = new Color(1f, 1f, 1f, 0.9f);
            preview.text = "黑胜率 -- · 目差 --";
            preview.raycastTarget = false;

            Bind(prefabRoot, preview, chartArea, scrubberHit, cursor, chart);
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
            Debug.Log("ReplayPage chart upgrade finished.");
        }
        finally {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    [MenuItem("WeiqiXN/UI/Ensure Replay Chart Legend")]
    public static void EnsureReplayChartLegend()
    {
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
        try {
            PreservePageRootTransform(prefabRoot);

            Transform chartArea = FindChildRecursive(prefabRoot.transform, "rect_chart_area");
            if (chartArea == null) {
                Debug.LogError("ReplayPage chart legend upgrade failed: rect_chart_area not found.");
                return;
            }

            TextMeshProUGUI winrateLegend = EnsureChildText(chartArea, "txt_chart_legend_winrate");
            ConfigureLegendLabel(
                winrateLegend,
                "胜率",
                new Color(0.25f, 0.72f, 1f, 0.95f),
                new Vector2(12f, -4f),
                TextAlignmentOptions.Left);

            TextMeshProUGUI scoreLegend = EnsureChildText(chartArea, "txt_chart_legend_score");
            ConfigureLegendLabel(
                scoreLegend,
                "目差",
                new Color(1f, 0.76f, 0.28f, 0.95f),
                new Vector2(70f, -4f),
                TextAlignmentOptions.Left);

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
            Debug.Log("ReplayPage chart legend upgrade finished.");
        }
        finally {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static void PreservePageRootTransform(GameObject prefabRoot)
    {
        RectTransform rootRect = prefabRoot.GetComponent<RectTransform>();
        if (rootRect == null) {
            return;
        }

        rootRect.localScale = Vector3.zero;
        rootRect.pivot = Vector2.zero;
    }

    private static RectTransform EnsureRect(Transform parent, string name)
    {
        Transform found = parent.Find(name);
        if (found != null) {
            return found.GetComponent<RectTransform>();
        }

        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    private static T EnsureChildComponent<T>(Transform parent, string name) where T : Component
    {
        Transform found = parent.Find(name);
        if (found == null) {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            found = go.transform;
        }

        T component = found.GetComponent<T>();
        return component != null ? component : found.gameObject.AddComponent<T>();
    }

    private static TextMeshProUGUI EnsureChildText(Transform parent, string name)
    {
        TextMeshProUGUI text = EnsureChildComponent<TextMeshProUGUI>(parent, name);
        return text;
    }

    private static void ConfigureLegendLabel(
        TextMeshProUGUI label,
        string text,
        Color textColor,
        Vector2 anchoredPosition,
        TextAlignmentOptions alignment)
    {
        RectTransform rect = label.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = new Vector2(54f, 18f);
        label.fontSize = 12f;
        label.alignment = alignment;
        label.color = textColor;
        label.text = text;
        label.raycastTarget = false;
    }

    private static Transform FindChildRecursive(Transform root, string name)
    {
        if (root.name == name) {
            return root;
        }

        foreach (Transform child in root) {
            Transform found = FindChildRecursive(child, name);
            if (found != null) {
                return found;
            }
        }

        return null;
    }

    private static void Bind(
        GameObject prefabRoot,
        TextMeshProUGUI preview,
        RectTransform chartArea,
        Image scrubberHit,
        Image cursor,
        ReplayAnalysisChartGraphic chart)
    {
        ReplayPageUI binder = prefabRoot.GetComponent<ReplayPageUI>();
        if (binder != null) {
            binder.txt_scrub_preview = preview;
            binder.rect_chart_area = chartArea;
            binder.img_move_scrubber_hit = scrubberHit;
            binder.img_chart_cursor = cursor;
            binder.chart_analysis = chart;
            EditorUtility.SetDirty(binder);
        }

        UIBinderEditor binderEditor = prefabRoot.GetComponent<UIBinderEditor>();
        if (binderEditor == null) {
            return;
        }

        UpsertNode(binderEditor, "txt_scrub_preview", preview);
        UpsertNode(binderEditor, "rect_chart_area", chartArea);
        UpsertNode(binderEditor, "img_move_scrubber_hit", scrubberHit);
        UpsertNode(binderEditor, "img_chart_cursor", cursor);
        UpsertNode(binderEditor, "chart_analysis", chart);
        binderEditor.generateTime = DateTime.UtcNow.Ticks;
        EditorUtility.SetDirty(binderEditor);
    }

    private static void UpsertNode(UIBinderEditor binderEditor, string name, UnityEngine.Object value)
    {
        UIBinderNode node = binderEditor.nodeList.FirstOrDefault(item => item.name == name);
        if (node == null) {
            binderEditor.nodeList.Add(new UIBinderNode(name, value));
        } else {
            node.value = value;
        }
    }
}
#endif
