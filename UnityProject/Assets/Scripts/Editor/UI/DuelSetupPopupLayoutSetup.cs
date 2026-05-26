#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class DuelSetupPopupLayoutSetup
{
    private const string PrefabPath = "Assets/UI/Prefab/Page/DuelSetupPopup.prefab";

    [MenuItem("WeiqiXN/UI/Setup DuelSetupPopup 16:9 Layout")]
    public static void Setup()
    {
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
        try {
            RectTransform rootRect = Require<RectTransform>(prefabRoot.transform, string.Empty);
            SetFullStretch(rootRect);

            StateRoot stateRoot = EnsurePlatformStateRoot(prefabRoot.transform);
            UpdateBinderReferences(prefabRoot, stateRoot);

            StateRoot rootStateRoot = prefabRoot.GetComponent<StateRoot>();
            if (rootStateRoot != null) {
                Object.DestroyImmediate(rootStateRoot, true);
            }

            stateRoot.EditableStates.Clear();
            stateRoot.EditableStates.Add(CreateLandscape(prefabRoot.transform));
            stateRoot.SetState(0, true);

            EditorUtility.SetDirty(stateRoot);
            EditorUtility.SetDirty(prefabRoot);
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
        } finally {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static StateRoot EnsurePlatformStateRoot(Transform root)
    {
        Transform platform = root.Find("sr_platform");
        if (platform == null) {
            GameObject platformGo = new GameObject("sr_platform", typeof(RectTransform));
            platform = platformGo.transform;
            platform.SetParent(root, false);
        }

        platform.SetSiblingIndex(0);
        RectTransform platformRect = Require<RectTransform>(platform, string.Empty);
        SetFullStretch(platformRect);

        StateRoot stateRoot = platform.GetComponent<StateRoot>();
        if (stateRoot == null) {
            stateRoot = platform.gameObject.AddComponent<StateRoot>();
        }

        return stateRoot;
    }

    private static void UpdateBinderReferences(GameObject prefabRoot, StateRoot stateRoot)
    {
        UIBinderEditor binderEditor = prefabRoot.GetComponent<UIBinderEditor>();
        if (binderEditor != null) {
            int nodeIndex = binderEditor.nodeList.FindIndex(node => node.name == "sr_platform");
            if (nodeIndex >= 0) {
                binderEditor.nodeList[nodeIndex].value = stateRoot;
            } else {
                binderEditor.nodeList.Insert(0, new UIBinderNode("sr_platform", stateRoot));
            }
            EditorUtility.SetDirty(binderEditor);
        }

        DuelSetupPopupUI binder = prefabRoot.GetComponent<DuelSetupPopupUI>();
        if (binder == null) {
            binder = prefabRoot.AddComponent<DuelSetupPopupUI>();
        }

        binder.sr_platform = stateRoot;
        EditorUtility.SetDirty(binder);
    }

    private static StateConfig CreateLandscape(Transform root)
    {
        StateConfig state = new StateConfig { name = "Landscape" };
        AddRect(state, "mask", Require<RectTransform>(root, "mask"), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        AddRect(state, "panel_main", Require<RectTransform>(root, "panel_main"), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(920f, 660f), new Vector2(0.5f, 0.5f));
        AddRect(state, "bg", Require<RectTransform>(root, "panel_main/bg"), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        AddRect(state, "btn_close", Require<RectTransform>(root, "panel_main/btn_close"), Vector2.one, Vector2.one, new Vector2(-46f, -42f), new Vector2(56f, 56f), new Vector2(0.5f, 0.5f));
        AddRect(state, "btn_start", Require<RectTransform>(root, "panel_main/btn_start"), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(310f, -255f), new Vector2(166f, 48f), new Vector2(0.5f, 0.5f));
        AddRect(state, "PanelSettings", Require<RectTransform>(root, "panel_main/PanelSettings"), Vector2.zero, Vector2.one, new Vector2(-102.9f, -172.43f), new Vector2(-269.8061f, 0f), new Vector2(0.5f, 0.5f));
        AddVerticalLayout(state, "PanelSettings_layout", Require<VerticalLayoutGroup>(root, "panel_main/PanelSettings"), TextAnchor.UpperLeft, 0f);
        return state;
    }

    private static void AddRect(StateConfig state, string name, RectTransform target, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 sizeDelta, Vector2 pivot)
    {
        StateElement element = new StateElement {
            name = name,
            elementType = StateElementType.RectTransform,
            target = target,
        };
        element.Property.anchorMin = anchorMin;
        element.Property.anchorMax = anchorMax;
        element.Property.anchoredPosition = anchoredPosition;
        element.Property.sizeDelta = sizeDelta;
        element.Property.pivot = pivot;
        element.Property.localScale = Vector3.one;
        state.Elements.Add(element);
    }

    private static void AddVerticalLayout(StateConfig state, string name, VerticalLayoutGroup target, TextAnchor alignment, float spacing)
    {
        StateElement element = new StateElement {
            name = name,
            elementType = StateElementType.VerticalLayoutGroup,
            target = target,
        };
        element.Property.childAlignment = alignment;
        element.Property.spacing = spacing;
        element.Property.childControlWidth = true;
        element.Property.childControlHeight = false;
        element.Property.childForceExpandWidth = true;
        element.Property.childForceExpandHeight = true;
        state.Elements.Add(element);
    }

    private static void SetFullStretch(RectTransform rectTransform)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.sizeDelta = Vector2.zero;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.localScale = Vector3.one;
    }

    private static T Require<T>(Transform root, string path) where T : Component
    {
        Transform target = string.IsNullOrEmpty(path) ? root : root.Find(path);
        if (target == null || !target.TryGetComponent(out T component)) {
            throw new MissingReferenceException($"{typeof(T).Name} not found: {path}");
        }

        return component;
    }
}
#endif
