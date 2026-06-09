#if UNITY_EDITOR
using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class DuelMoveConfirmPopupPrefabTool
{
    private const string PrefabPath = "Assets/UI/Prefab/Page/DuelMoveConfirmPopup.prefab";

    [MenuItem("WeiqiXN/UI/Create Duel Move Confirm Popup")]
    public static void CreatePrefab()
    {
        GameObject root = CreateRoot();
        try {
            RectTransform panelRoot = CreateRect("PanelRoot", root.transform);
            Stretch(panelRoot);

            Image controlsPanel = CreateImage(panelRoot, "panel_controls", new Color(0.92f, 0.96f, 0.94f, 0.92f));
            RectTransform controlsRect = controlsPanel.rectTransform;
            controlsRect.anchorMin = new Vector2(0.5f, 0f);
            controlsRect.anchorMax = new Vector2(0.5f, 0f);
            controlsRect.pivot = new Vector2(0.5f, 0f);
            controlsRect.anchoredPosition = new Vector2(0f, 88f);
            controlsRect.sizeDelta = new Vector2(560f, 300f);

            Button confirm = CreateButton(controlsRect, "btn_confirm", "确认", new Vector2(0f, 112f), new Vector2(240f, 96f), 38f, new Color(0.3f, 0.62f, 0.42f, 1f));
            Button up = CreateButton(controlsRect, "btn_move_up", "^", new Vector2(0f, 230f), new Vector2(88f, 72f), 40f, new Color(0.38f, 0.68f, 0.48f, 1f));
            Button down = CreateButton(controlsRect, "btn_move_down", "v", new Vector2(0f, 18f), new Vector2(88f, 72f), 40f, new Color(0.38f, 0.68f, 0.48f, 1f));
            Button left = CreateButton(controlsRect, "btn_move_left", "<", new Vector2(-182f, 112f), new Vector2(88f, 72f), 40f, new Color(0.38f, 0.68f, 0.48f, 1f));
            Button right = CreateButton(controlsRect, "btn_move_right", ">", new Vector2(182f, 112f), new Vector2(88f, 72f), 40f, new Color(0.38f, 0.68f, 0.48f, 1f));
            Button cancel = CreateButton(controlsRect, "btn_cancel", "X", new Vector2(254f, 266f), new Vector2(56f, 56f), 32f, new Color(0.7f, 0.76f, 0.72f, 1f));

            Bind(root, confirm, cancel, up, down, left, right);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetImporter importer = AssetImporter.GetAtPath(PrefabPath);
            if (importer != null) {
                importer.assetBundleName = "ui_main_prefab";
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"DuelMoveConfirmPopup prefab created: {PrefabPath}");
        }
        finally {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static GameObject CreateRoot()
    {
        GameObject root = new GameObject(
            "DuelMoveConfirmPopup",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster),
            typeof(UIBinderEditor),
            typeof(DuelMoveConfirmPopupUI));

        RectTransform rect = root.GetComponent<RectTransform>();
        rect.localScale = Vector3.zero;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        rect.pivot = Vector2.zero;

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.pixelPerfect = false;

        CanvasScaler canvasScaler = root.GetComponent<CanvasScaler>();
        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = UICanvasResolutionProfile.EditorMobilePreviewReferenceResolution;
        canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        canvasScaler.matchWidthOrHeight = 0f;

        return root;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    private static Image CreateImage(Transform parent, string name, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = true;
        return image;
    }

    private static Button CreateButton(
        Transform parent,
        string name,
        string label,
        Vector2 anchoredPosition,
        Vector2 size,
        float fontSize,
        Color color)
    {
        Image image = CreateImage(parent, name, color);
        RectTransform rect = image.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        Button button = image.gameObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.94f, 1f, 0.96f, 1f);
        colors.pressedColor = new Color(0.78f, 0.9f, 0.82f, 1f);
        colors.selectedColor = Color.white;
        button.colors = colors;

        TextMeshProUGUI text = CreateText(rect, "txt_label", label, fontSize);
        text.color = Color.white;
        text.fontStyle = FontStyles.Bold;
        text.enableWordWrapping = false;
        text.raycastTarget = false;
        return button;
    }

    private static TextMeshProUGUI CreateText(Transform parent, string name, string text, float fontSize)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        Stretch(rect);

        TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.alignment = TextAlignmentOptions.Center;
        return label;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.localScale = Vector3.one;
    }

    private static void Bind(
        GameObject root,
        Button confirm,
        Button cancel,
        Button up,
        Button down,
        Button left,
        Button right)
    {
        DuelMoveConfirmPopupUI binder = root.GetComponent<DuelMoveConfirmPopupUI>();
        binder.btn_confirm = confirm;
        binder.btn_cancel = cancel;
        binder.btn_move_up = up;
        binder.btn_move_down = down;
        binder.btn_move_left = left;
        binder.btn_move_right = right;
        EditorUtility.SetDirty(binder);

        UIBinderEditor binderEditor = root.GetComponent<UIBinderEditor>();
        binderEditor.nodeList.Clear();
        binderEditor.nodeList.Add(new UIBinderNode("btn_confirm", confirm));
        binderEditor.nodeList.Add(new UIBinderNode("btn_cancel", cancel));
        binderEditor.nodeList.Add(new UIBinderNode("btn_move_up", up));
        binderEditor.nodeList.Add(new UIBinderNode("btn_move_down", down));
        binderEditor.nodeList.Add(new UIBinderNode("btn_move_left", left));
        binderEditor.nodeList.Add(new UIBinderNode("btn_move_right", right));
        binderEditor.generateTime = DateTime.UtcNow.Ticks;
        EditorUtility.SetDirty(binderEditor);
    }
}
#endif
