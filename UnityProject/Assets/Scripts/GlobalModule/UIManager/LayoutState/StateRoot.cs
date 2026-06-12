using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class StateRoot : MonoBehaviour
{
    [SerializeField] private List<StateConfig> states = new List<StateConfig>();
    [SerializeField] private int currentStateIndex;

    public IReadOnlyList<StateConfig> States => states;
    public int CurrentStateIndex => currentStateIndex;

    public void SetState(string stateName, bool force = false)
    {
        for (int i = 0; i < states.Count; i++) {
            if (states[i].name == stateName) {
                SetState(i, force);
                return;
            }
        }

        Debug.LogWarning($"State not found: {stateName}", this);
    }

    public void SetState(int stateIndex, bool force = false)
    {
        if (stateIndex < 0 || stateIndex >= states.Count) {
            Debug.LogWarning($"Invalid state index: {stateIndex}", this);
            return;
        }

        if (!force && currentStateIndex == stateIndex) {
            return;
        }

        currentStateIndex = stateIndex;
        states[stateIndex].Apply();
    }

#if UNITY_EDITOR
    public List<StateConfig> EditableStates => states;

    public void CaptureCurrentState()
    {
        if (currentStateIndex < 0 || currentStateIndex >= states.Count) {
            return;
        }

        states[currentStateIndex].Capture();
    }
#endif
}

[Serializable]
public class StateConfig
{
    public string name;
    [SerializeField] private List<StateElement> elements = new List<StateElement>();

    public List<StateElement> Elements => elements;

    public void Apply()
    {
        foreach (var element in elements) {
            element.Apply();
        }
    }

#if UNITY_EDITOR
    [NonSerialized] public bool isFoldout;

    public void Capture()
    {
        foreach (var element in elements) {
            element.Capture();
        }
    }
#endif
}

[Serializable]
public class StateElement
{
    public string name;
    public StateElementType elementType;
    public UnityEngine.Object target;
    [SerializeField] private StateElementProperty property = new StateElementProperty();

    public StateElementProperty Property => property;

    public Type TargetType
    {
        get
        {
            switch (elementType) {
                case StateElementType.GameObjectActive:
                    return typeof(GameObject);
                case StateElementType.RectTransform:
                    return typeof(RectTransform);
                case StateElementType.VerticalLayoutGroup:
                    return typeof(VerticalLayoutGroup);
                default:
                    return typeof(UnityEngine.Object);
            }
        }
    }

    public void Apply()
    {
        switch (elementType) {
            case StateElementType.GameObjectActive:
                if (target is GameObject gameObjectTarget) {
                    gameObjectTarget.SetActive(property.boolValue);
                }
                break;
            case StateElementType.RectTransform:
                if (target is RectTransform rectTransformTarget) {
                    property.Apply(rectTransformTarget);
                }
                break;
            case StateElementType.VerticalLayoutGroup:
                if (target is VerticalLayoutGroup layoutGroupTarget) {
                    property.Apply(layoutGroupTarget);
                }
                break;
        }
    }

#if UNITY_EDITOR
    public void Capture()
    {
        switch (elementType) {
            case StateElementType.GameObjectActive:
                if (target is GameObject gameObjectTarget) {
                    property.boolValue = gameObjectTarget.activeSelf;
                } else if (target is Component componentTarget) {
                    property.boolValue = componentTarget.gameObject.activeSelf;
                }
                break;
            case StateElementType.RectTransform:
                if (target is RectTransform rectTransformTarget) {
                    property.Capture(rectTransformTarget);
                }
                break;
            case StateElementType.VerticalLayoutGroup:
                if (target is VerticalLayoutGroup layoutGroupTarget) {
                    property.Capture(layoutGroupTarget);
                }
                break;
        }
    }
#endif
}

public enum StateElementType
{
    GameObjectActive,
    RectTransform,
    VerticalLayoutGroup,
}

[Serializable]
public class StateElementProperty
{
    public bool boolValue = true;

    public Vector2 anchorMin;
    public Vector2 anchorMax;
    public Vector2 anchoredPosition;
    public Vector2 sizeDelta;
    public Vector2 pivot;
    public Vector3 localScale = Vector3.one;

    public RectOffset padding = new RectOffset();
    public TextAnchor childAlignment = TextAnchor.UpperLeft;
    public float spacing;
    public bool childControlWidth;
    public bool childControlHeight;
    public bool childForceExpandWidth;
    public bool childForceExpandHeight;

    public void Apply(RectTransform target)
    {
        target.anchorMin = anchorMin;
        target.anchorMax = anchorMax;
        target.pivot = pivot;
        target.sizeDelta = sizeDelta;
        target.anchoredPosition = anchoredPosition;
        target.localScale = localScale;
    }

    public void Apply(VerticalLayoutGroup target)
    {
        target.padding = new RectOffset(padding.left, padding.right, padding.top, padding.bottom);
        target.childAlignment = childAlignment;
        target.spacing = spacing;
        target.childControlWidth = childControlWidth;
        target.childControlHeight = childControlHeight;
        target.childForceExpandWidth = childForceExpandWidth;
        target.childForceExpandHeight = childForceExpandHeight;
    }

#if UNITY_EDITOR
    public void Capture(RectTransform target)
    {
        anchorMin = target.anchorMin;
        anchorMax = target.anchorMax;
        anchoredPosition = target.anchoredPosition;
        sizeDelta = target.sizeDelta;
        pivot = target.pivot;
        localScale = target.localScale;
    }

    public void Capture(VerticalLayoutGroup target)
    {
        padding = new RectOffset(target.padding.left, target.padding.right, target.padding.top, target.padding.bottom);
        childAlignment = target.childAlignment;
        spacing = target.spacing;
        childControlWidth = target.childControlWidth;
        childControlHeight = target.childControlHeight;
        childForceExpandWidth = target.childForceExpandWidth;
        childForceExpandHeight = target.childForceExpandHeight;
    }
#endif
}

#if UNITY_EDITOR
[CustomEditor(typeof(StateRoot))]
public class StateRootEditor : Editor
{
    private StateRoot stateRoot;
    private SerializedProperty statesProperty;
    private SerializedProperty currentStateIndexProperty;

    private void OnEnable()
    {
        stateRoot = (StateRoot)target;
        RefreshSerializedProperties();
    }

    private void RefreshSerializedObject()
    {
        serializedObject.Update();
        RefreshSerializedProperties();
    }

    private void RefreshSerializedProperties()
    {
        statesProperty = serializedObject.FindProperty("states");
        currentStateIndexProperty = serializedObject.FindProperty("currentStateIndex");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawCurrentState(stateRoot);
        EditorGUILayout.Space(4);
        DrawStates();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawCurrentState(StateRoot stateRoot)
    {
        string[] stateNames = new string[stateRoot.States.Count];
        for (int i = 0; i < stateNames.Length; i++) {
            string stateName = stateRoot.States[i].name;
            stateNames[i] = string.IsNullOrEmpty(stateName) ? $"State {i}" : $"{i} ({stateName})";
        }

        if (stateNames.Length > 0) {
            int selectedIndex = Mathf.Clamp(currentStateIndexProperty.intValue, 0, stateNames.Length - 1);
            int newIndex = EditorGUILayout.Popup("当前", selectedIndex, stateNames);
            if (newIndex != currentStateIndexProperty.intValue) {
                ApplyStateIndex(newIndex);
            }
        } else {
            EditorGUILayout.HelpBox("未配置状态。", MessageType.Info);
        }

        using (new EditorGUILayout.HorizontalScope()) {
            if (GUILayout.Button("应用", GUILayout.Width(44))) {
                ApplyStateIndex(currentStateIndexProperty.intValue);
            }

            if (GUILayout.Button("读取", GUILayout.Width(44))) {
                Undo.RecordObject(stateRoot, "Capture State");
                stateRoot.CaptureCurrentState();
                RefreshSerializedObject();
                EditorUtility.SetDirty(stateRoot);
            }
        }

        EditorGUILayout.HelpBox("先应用状态，调整目标 UI 后读取当前值。", MessageType.Info);
    }

    private void DrawStates()
    {
        using (new EditorGUILayout.HorizontalScope()) {
            EditorGUILayout.LabelField($"状态 ({stateRoot.EditableStates.Count})", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("加状态", GUILayout.Width(58))) {
                Undo.RecordObject(stateRoot, "Add State");
                var state = new StateConfig { name = $"State{stateRoot.EditableStates.Count}" };
                stateRoot.EditableStates.Add(state);
                RefreshSerializedObject();
                currentStateIndexProperty.intValue = statesProperty.arraySize - 1;
                EditorUtility.SetDirty(stateRoot);
            }
        }

        using (new EditorGUI.IndentLevelScope()) {
            for (int i = 0; i < stateRoot.EditableStates.Count; i++) {
                DrawState(i);
            }
        }
    }

    private void DrawState(int stateIndex)
    {
        StateConfig state = stateRoot.EditableStates[stateIndex];
        bool isCurrent = stateIndex == currentStateIndexProperty.intValue;
        using (new EditorGUILayout.VerticalScope(isCurrent ? EditorStyles.helpBox : GUIStyle.none)) {
            using (new EditorGUILayout.HorizontalScope()) {
                state.isFoldout = EditorGUILayout.Foldout(state.isFoldout, $"{stateIndex} ({state.name})", true);
                if (GUILayout.Button("应用", GUILayout.Width(44))) {
                    ApplyStateIndex(stateIndex);
                }
                if (GUILayout.Button("读取", GUILayout.Width(44))) {
                    Undo.RecordObject(stateRoot, "Capture State");
                    state.Capture();
                    RefreshSerializedObject();
                    currentStateIndexProperty.intValue = stateIndex;
                    EditorUtility.SetDirty(stateRoot);
                }
                using (new EditorGUI.DisabledScope(stateIndex <= 0)) {
                    if (GUILayout.Button("上", GUILayout.Width(28))) {
                        Undo.RecordObject(stateRoot, "Move State");
                        Swap(stateRoot.EditableStates, stateIndex, stateIndex - 1);
                        RefreshSerializedObject();
                        currentStateIndexProperty.intValue = stateIndex - 1;
                        EditorUtility.SetDirty(stateRoot);
                        return;
                    }
                }
                using (new EditorGUI.DisabledScope(stateIndex >= stateRoot.EditableStates.Count - 1)) {
                    if (GUILayout.Button("下", GUILayout.Width(28))) {
                        Undo.RecordObject(stateRoot, "Move State");
                        Swap(stateRoot.EditableStates, stateIndex, stateIndex + 1);
                        RefreshSerializedObject();
                        currentStateIndexProperty.intValue = stateIndex + 1;
                        EditorUtility.SetDirty(stateRoot);
                        return;
                    }
                }
                if (GUILayout.Button("删", GUILayout.Width(28))) {
                    Undo.RecordObject(stateRoot, "Delete State");
                    stateRoot.EditableStates.RemoveAt(stateIndex);
                    RefreshSerializedObject();
                    currentStateIndexProperty.intValue = Mathf.Clamp(currentStateIndexProperty.intValue, 0, statesProperty.arraySize - 1);
                    EditorUtility.SetDirty(stateRoot);
                    return;
                }
            }

            if (!state.isFoldout) {
                return;
            }

            using (new EditorGUI.IndentLevelScope()) {
                using (var check = new EditorGUI.ChangeCheckScope()) {
                    string newName = EditorGUILayout.TextField("名", state.name);
                    if (check.changed) {
                        Undo.RecordObject(stateRoot, "Rename State");
                        state.name = newName;
                        RefreshSerializedObject();
                        EditorUtility.SetDirty(stateRoot);
                    }
                }

                DrawElements(state, stateIndex);
            }
        }
    }

    private void DrawElements(StateConfig state, int stateIndex)
    {
        using (new EditorGUILayout.HorizontalScope()) {
            EditorGUILayout.LabelField($"元素 ({state.Elements.Count})", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("加元素", GUILayout.Width(58))) {
                Undo.RecordObject(stateRoot, "Add Element");
                state.Elements.Add(new StateElement {
                    name = $"Element{state.Elements.Count}",
                    elementType = StateElementType.RectTransform,
                });
                RefreshSerializedObject();
                EditorUtility.SetDirty(stateRoot);
            }
        }

        for (int i = 0; i < state.Elements.Count; i++) {
            DrawElement(state, stateIndex, i);
        }
    }

    private void DrawElement(StateConfig state, int stateIndex, int elementIndex)
    {
        StateElement element = state.Elements[elementIndex];
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
            using (new EditorGUILayout.HorizontalScope()) {
                using (var check = new EditorGUI.ChangeCheckScope()) {
                    element.name = EditorGUILayout.TextField(element.name, GUILayout.MinWidth(80));
                    element.elementType = (StateElementType)EditorGUILayout.EnumPopup(element.elementType, GUILayout.Width(150));
                    element.target = EditorGUILayout.ObjectField(element.target, element.TargetType, true);
                    if (check.changed) {
                        Undo.RecordObject(stateRoot, "Edit Element");
                        RefreshSerializedObject();
                        EditorUtility.SetDirty(stateRoot);
                    }
                }

                if (GUILayout.Button("读", GUILayout.Width(28))) {
                    Undo.RecordObject(stateRoot, "Read Element");
                    element.Capture();
                    RefreshSerializedObject();
                    EditorUtility.SetDirty(stateRoot);
                }
                if (GUILayout.Button("设", GUILayout.Width(28))) {
                    Undo.RecordObject(stateRoot, "Set Element");
                    element.Apply();
                    EditorUtility.SetDirty(stateRoot);
                }
                using (new EditorGUI.DisabledScope(elementIndex <= 0)) {
                    if (GUILayout.Button("上", GUILayout.Width(28))) {
                        Undo.RecordObject(stateRoot, "Move Element");
                        Swap(state.Elements, elementIndex, elementIndex - 1);
                        RefreshSerializedObject();
                        EditorUtility.SetDirty(stateRoot);
                        return;
                    }
                }
                using (new EditorGUI.DisabledScope(elementIndex >= state.Elements.Count - 1)) {
                    if (GUILayout.Button("下", GUILayout.Width(28))) {
                        Undo.RecordObject(stateRoot, "Move Element");
                        Swap(state.Elements, elementIndex, elementIndex + 1);
                        RefreshSerializedObject();
                        EditorUtility.SetDirty(stateRoot);
                        return;
                    }
                }
                if (GUILayout.Button("删", GUILayout.Width(28))) {
                    Undo.RecordObject(stateRoot, "Delete Element");
                    state.Elements.RemoveAt(elementIndex);
                    RefreshSerializedObject();
                    EditorUtility.SetDirty(stateRoot);
                    return;
                }
            }

            using (new EditorGUI.IndentLevelScope()) {
                using (var check = new EditorGUI.ChangeCheckScope()) {
                    DrawElementProperty(element);
                    if (check.changed) {
                        Undo.RecordObject(stateRoot, "Edit Element Property");
                        if (stateIndex == currentStateIndexProperty.intValue) {
                            element.Apply();
                        }
                        RefreshSerializedObject();
                        EditorUtility.SetDirty(stateRoot);
                    }
                }
            }
        }
    }

    private void DrawElementProperty(StateElement element)
    {
        StateElementProperty property = element.Property;
        switch (element.elementType) {
            case StateElementType.GameObjectActive:
                property.boolValue = EditorGUILayout.Toggle("显示", property.boolValue);
                break;
            case StateElementType.RectTransform:
                property.anchorMin = EditorGUILayout.Vector2Field("锚小", property.anchorMin);
                property.anchorMax = EditorGUILayout.Vector2Field("锚大", property.anchorMax);
                property.anchoredPosition = EditorGUILayout.Vector2Field("位置", property.anchoredPosition);
                property.sizeDelta = EditorGUILayout.Vector2Field("尺寸", property.sizeDelta);
                property.pivot = EditorGUILayout.Vector2Field("轴心", property.pivot);
                property.localScale = EditorGUILayout.Vector3Field("缩放", property.localScale);
                break;
            case StateElementType.VerticalLayoutGroup:
                property.childAlignment = (TextAnchor)EditorGUILayout.EnumPopup("对齐", property.childAlignment);
                property.spacing = EditorGUILayout.FloatField("间距", property.spacing);
                property.childControlWidth = EditorGUILayout.Toggle("控宽", property.childControlWidth);
                property.childControlHeight = EditorGUILayout.Toggle("控高", property.childControlHeight);
                property.childForceExpandWidth = EditorGUILayout.Toggle("展宽", property.childForceExpandWidth);
                property.childForceExpandHeight = EditorGUILayout.Toggle("展高", property.childForceExpandHeight);
                DrawPadding(property.padding);
                break;
        }
    }

    private void DrawPadding(RectOffset padding)
    {
        EditorGUILayout.LabelField("边距");
        using (new EditorGUI.IndentLevelScope()) {
            padding.left = EditorGUILayout.IntField("左", padding.left);
            padding.right = EditorGUILayout.IntField("右", padding.right);
            padding.top = EditorGUILayout.IntField("上", padding.top);
            padding.bottom = EditorGUILayout.IntField("下", padding.bottom);
        }
    }

    private void ApplyStateIndex(int stateIndex)
    {
        Undo.RecordObject(stateRoot, "Apply State");
        currentStateIndexProperty.intValue = stateIndex;
        serializedObject.ApplyModifiedProperties();
        stateRoot.SetState(stateIndex, true);
        EditorUtility.SetDirty(stateRoot);
    }

    private static void Swap<T>(IList<T> list, int currentIndex, int targetIndex)
    {
        (list[currentIndex], list[targetIndex]) = (list[targetIndex], list[currentIndex]);
    }
}

#endif
