#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;

// 生成 V3 主题字体的动态 TMP 字体资产（思源黑体 Medium、思源宋体 SemiBold）。
// 创建走 TMP 自带的“Font Asset”菜单逻辑，参数与现有 Regular 相同；已存在的资产只校正设置，不重建，避免丢掉已缓存的字形。
public static class UIThemeFontAssetTool
{
    public const string RegularFontAssetPath = "Assets/UI/Font/SourceHanSansSC-Regular SDF.asset";
    public const string MediumFontAssetPath = "Assets/UI/Font/SourceHanSansCN-Medium SDF.asset";
    public const string SerifFontAssetPath = "Assets/UI/Font/SourceHanSerifCN-SemiBold SDF.asset";

    private const string MediumSourceFontPath = "Assets/UI/Font/SourceHanSansCN-Medium.otf";
    private const string SerifSourceFontPath = "Assets/UI/Font/SourceHanSerifCN-SemiBold.otf";
    private const string FontBundleName = "font";

    [MenuItem(CustomEditorMenuPaths.UI + "/生成主题字体资产")]
    public static void Generate()
    {
        TMP_FontAsset regular = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(RegularFontAssetPath);
        if (regular == null) {
            Debug.LogError($"Regular font asset not found: {RegularFontAssetPath}");
            return;
        }

        bool changed = false;
        changed |= EnsureFontAsset(MediumSourceFontPath, MediumFontAssetPath, regular);
        changed |= EnsureFontAsset(SerifSourceFontPath, SerifFontAssetPath, regular);
        if (changed) {
            AssetDatabase.SaveAssets();
        }

        Debug.Log($"Theme font assets checked. changed: {changed}");
    }

    private static bool EnsureFontAsset(string sourceFontPath, string fontAssetPath, TMP_FontAsset fallback)
    {
        Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(sourceFontPath);
        if (sourceFont == null) {
            Debug.LogError($"Source font not found: {sourceFontPath}");
            return false;
        }

        bool changed = SetBundleName(sourceFontPath);
        TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontAssetPath);
        if (fontAsset == null) {
            Object[] previousSelection = Selection.objects;
            Selection.objects = new Object[] { sourceFont };
            TMP_FontAsset_CreationMenu.CreateFontAsset();
            Selection.objects = previousSelection;
            fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontAssetPath);
            if (fontAsset == null) {
                Debug.LogError($"Create font asset failed: {fontAssetPath}");
                return changed;
            }

            changed = true;
        }

        // Regular 是完整字符集，CN 子集缺的生僻字回落到它。
        if (!fontAsset.isMultiAtlasTexturesEnabled || fontAsset.fallbackFontAssetTable == null
            || fontAsset.fallbackFontAssetTable.Count != 1 || fontAsset.fallbackFontAssetTable[0] != fallback) {
            fontAsset.isMultiAtlasTexturesEnabled = true;
            fontAsset.fallbackFontAssetTable = new List<TMP_FontAsset> { fallback };
            EditorUtility.SetDirty(fontAsset);
            changed = true;
        }

        changed |= SetBundleName(fontAssetPath);
        return changed;
    }

    private static bool SetBundleName(string assetPath)
    {
        AssetImporter importer = AssetImporter.GetAtPath(assetPath);
        if (importer == null || importer.assetBundleName == FontBundleName) {
            return false;
        }

        importer.assetBundleName = FontBundleName;
        return true;
    }
}
#endif
