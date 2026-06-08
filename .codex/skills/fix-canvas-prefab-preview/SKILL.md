---
name: fix-canvas-prefab-preview
description: Diagnose and fix Unity Prefab Mode preview issues where opening a UI prefab shows an unexpected `Canvas (environment)` because the prefab root Canvas identity or root RectTransform editor-state is wrong. Use when Codex needs to inspect or repair a Unity `.prefab` root Canvas / RectTransform / CanvasScaler / GraphicRaycaster relationship, especially when a self-contained Canvas root was saved with content/design-layout RectTransform values, without comparing against other prefabs or changing child UI layout.
---

# Fix Canvas Prefab Preview

## Scope

Use this skill only for root Canvas preview failures where Unity Prefab Mode creates `Canvas (environment)` because the target UI prefab's root Canvas structure or root RectTransform editor-state is wrong. This includes both a broken root Canvas identity and the subtler case where the root Canvas is present but its `RectTransform` was saved as a content/design-layout node.

Do not use this workflow for unrelated preview differences, runtime UI bugs, sorting-order design changes, missing EventSystem problems, broken scripts, missing assets, localization, dynamic layout behavior, or ordinary child-widget prefabs that intentionally rely on a parent Canvas.

Do not compare the target prefab against other prefabs. Diagnose the target prefab directly from its own serialized root object graph and Unity component semantics.

## Cause Summary

A self-contained screen, page, or popup prefab should have its actual prefab root act as the Canvas root. In serialized prefab terms, the root `RectTransform` has no parent, belongs to the root `GameObject`, and the same root object owns the `Canvas` plus any required Canvas support components such as `CanvasScaler` and `GraphicRaycaster`.

`Canvas (environment)` appears when Prefab Mode needs to supply an external Canvas to preview UI content or to work around a root Canvas whose serialized editor-state no longer looks like a neutral Canvas root. For the fix covered by this skill, common signs are:

- The actual root object is not the object with the `Canvas` component.
- The root `RectTransform` is no longer the top-level transform with `m_Father: {fileID: 0}`.
- The root object is missing `Canvas`, `CanvasScaler`, or `GraphicRaycaster` even though this prefab is supposed to be a complete UI screen or popup.
- The root `RectTransform` was saved with content/design-layout values, such as `m_LocalScale: {x: 1, y: 1, z: 1}`, center anchors, a non-zero design `m_SizeDelta`, or center pivot, while child content already carries the real layout.
- The root `RectTransform` was changed to behave like a full-screen content layout node, such as four-way stretch anchors or child-layout sizing values.
- The root `Canvas` render mode was changed as a layout workaround instead of preserving the prefab's intended Canvas mode.

Do not assume `Screen Space - Overlay` is the cause by itself. First prove whether the root `RectTransform` was repurposed as a design/content surface. The repair is to restore the prefab root as a neutral Canvas root. Do not solve this by adding another wrapper Canvas, changing child layout, reparenting the UI tree, or moving full-screen layout responsibility onto the Canvas root.

## Root Inspection

Locate the actual prefab root before editing. Use the target prefab path and name supplied by the user:

```powershell
rg -n -C 70 "m_Name: TargetPrefabName" path\to\TargetPrefab.prefab
rg -n -C 20 "m_Father: \{fileID: 0\}" path\to\TargetPrefab.prefab
```

Confirm all of these before modifying fields:

- The prefab root `GameObject` is the UI screen, page, or popup root the user asked to fix.
- The root `RectTransform` has `m_GameObject` equal to the root `GameObject` fileID.
- The root `RectTransform` has `m_Father: {fileID: 0}`.
- The root object owns the `Canvas` component.
- If the prefab is a complete interactive UI screen or popup, the root object also owns `CanvasScaler` and `GraphicRaycaster`, unless the user's project explicitly uses a different root Canvas setup.

If the prefab is only a reusable child widget and is intended to live under another Canvas, stop. `Canvas (environment)` is expected for that kind of prefab and this skill should not force it to become a root Canvas prefab.

## Safe Repair

Prefer Unity editor APIs or an explicit editor tool when changing prefab structure. If the smallest safe change is manual YAML editing, patch only the verified root object blocks with fileID-qualified context.

Repair only the fields required to restore root Canvas identity:

- Ensure the actual prefab root object owns the `Canvas` component.
- Ensure the root `RectTransform` belongs to that root object.
- Ensure the root `RectTransform` has no parent.
- Restore neutral Canvas-root transform values when the root was incorrectly turned into a content/design-layout node: zero anchored position, zero size delta, zero or neutral anchors as appropriate for a Canvas root, and a neutral pivot that matches the project's intended root Canvas convention.
- If the root Canvas has child content that carries the real layout, keep that child layout intact and move only the root Canvas editor-state back to neutral values. A confirmed safe repair for this failure mode can be root scale `(0,0,0)`, anchors `(0,0)`, anchored position `(0,0)`, size delta `(0,0)`, and pivot `(0,0)`, while preserving the root Canvas component fields.
- Preserve the prefab's intended `Canvas.renderMode`; change it only if the unexpected `Canvas (environment)` was caused by a mistaken render-mode edit and the intended mode is known from the target prefab's history, user instruction, or project convention.
- Keep `CanvasScaler` and `GraphicRaycaster` on the root if this prefab is a complete screen or popup.

Do not broadly replace serialized fields such as `m_LocalScale`, `m_AnchorMin`, `m_AnchorMax`, `m_SizeDelta`, `m_Pivot`, `m_RenderMode`, `m_OverrideSorting`, or `m_SortingOrder`. These fields occur many times in child layout. Broad replacement can damage the UI while appearing to fix the preview.

Do not add `PanelRoot`, reparent children, rebuild the UI tree, alter child `LayoutGroup` / `ContentSizeFitter` settings, or change content anchors unless the user separately asked for a layout migration. Child content layout is outside this skill's fix.

## Diff Gate

Inspect the prefab diff immediately after every edit:

```powershell
git diff -- path\to\TargetPrefab.prefab
```

For this skill, the diff should be limited to the actual root `GameObject`, root `RectTransform`, root `Canvas`, and directly required root Canvas support components. If child nodes, layout containers, controls, binder references, or unrelated serialized objects changed, restore those changes before continuing.

A common failure mode is patching the first `RectTransform` in the file instead of the root `RectTransform`. Unity prefab YAML object order is not the same as hierarchy order; always use fileIDs to prove the root before editing.

Avoid keeping large prefab rewrites from `PrefabUtility.LoadPrefabContents` / `SaveAsPrefabAsset` unless the user explicitly accepts a Unity-normalized rebuild and the root Canvas identity plus child layout have both been inspected.

## Validation

After prefab changes, make Unity import the asset through `Assets/Refresh` or an equivalent Editor entry, then check the Unity Console for errors.

File diffs cannot prove the visual Prefab Mode issue is fixed. Ask the user to open the target prefab in Prefab Mode and confirm whether the unexpected `Canvas (environment)` is gone.

Clean up temporary diagnostic scripts, temporary prefab assets, and temporary folders unless the user asked to keep them.
