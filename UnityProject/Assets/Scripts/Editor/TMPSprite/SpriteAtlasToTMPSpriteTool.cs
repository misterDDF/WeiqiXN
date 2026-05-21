using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.Sprites;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
using XNClient.Logger;
using static TMPro.SpriteAssetUtilities.TexturePacker_JsonArray;
using Object = UnityEngine.Object;

public class SpriteAtlasToTMPSpriteTool
{
    public static string ActivePlatformName => GetPlatformName(EditorUserBuildSettings.activeBuildTarget);

    public static string GetPlatformName(BuildTarget buildTarget)
    {
        string platformName;
        switch (buildTarget) {
            // 注意这个platformName字符串是unity定死的，不能随便写
            case BuildTarget.Android:
                platformName = "Android";
                break;
            case BuildTarget.iOS:
                platformName = "iPhone";
                break;
            case BuildTarget.WebGL:
                platformName = "WebGL";
                break;
            case BuildTarget.NoTarget:
                platformName = "DefaultTexturePlatform";
                break;
            default:
                platformName = "Standalone";
                break;
        }
        return platformName;
    }
    private static readonly string STANDARD_SPRITEATLAS_PATH = "Assets/UI/UITexture/Standard.spriteatlas";
    private static readonly string TMP_SPRITE_PATH = "Assets/UI/TextMesh Pro/Sprites/";

    [MenuItem(CustomEditorMenuPaths.SpriteAtlasTools + "/按文件夹创建图集", true)]
    public static bool ExportFolderspriteAtlasCondition()
    {
        if (Selection.objects.Length != 1) {
            return false;
        }
        var selectObj = Selection.objects[0];
        return Directory.Exists(AssetDatabase.GetAssetPath(selectObj));
    }

    [MenuItem(CustomEditorMenuPaths.SpriteAtlasTools + "/按文件夹创建图集")]
    public static void ExportFolderSpriteAtlas()
    {
        var selectObj = Selection.objects[0];
        string dirPath = AssetDatabase.GetAssetPath(selectObj);
        if (Directory.Exists(dirPath)) {
            DirectoryInfo rootDirInfo = new DirectoryInfo(dirPath);
            SpriteAtlas sa = new SpriteAtlas();
            // 导出图集时默认复制一份标砖图集案例的配置
            var standardSpriteAtlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(STANDARD_SPRITEATLAS_PATH);
            if (standardSpriteAtlas != null) {
                sa.SetPackingSettings(standardSpriteAtlas.GetPackingSettings());
                sa.SetTextureSettings(standardSpriteAtlas.GetTextureSettings());
                sa.SetPlatformSettings(standardSpriteAtlas.GetPlatformSettings(ActivePlatformName));
                sa.SetPlatformSettings(standardSpriteAtlas.GetPlatformSettings(GetPlatformName(BuildTarget.NoTarget)));
            }
            AssetDatabase.CreateAsset(sa, Path.Combine(Path.GetDirectoryName(dirPath), $"{rootDirInfo.Name}.spriteatlas"));

            TraverseSpriteAtlas(rootDirInfo, ref sa);

            // 单打一个图集时会有神秘的m_PackedSprites缓存没清的问题，导致后续取spriteUV时unity认为sprite not packed，没找到好的解决方案，目前重新packAll一遍是能解决问题的
            SpriteAtlasUtility.PackAllAtlases(EditorUserBuildSettings.activeBuildTarget);
            SpriteAtlasUtility.CleanupAtlasPacking();
        }
    }

    [MenuItem(CustomEditorMenuPaths.Root + "/spriteAtlas转TMP sprite asset", true)]
    public static bool SpriteAtlasToTMPSpriteCondition()
    {
        foreach (Object obj in Selection.objects) {
            if (obj is SpriteAtlas) {
                return true;
            }
        }
        return false;
    }

    [MenuItem(CustomEditorMenuPaths.Root + "/spriteAtlas转TMP sprite asset")]
    public static void SpriteAtlasToTMPSpriteAction()
    {
        foreach (Object obj in Selection.objects) {
            if (obj is SpriteAtlas atlas) {
                try {
                    SpriteAtlasToTMPSprite(atlas);
                    EditorUtility.DisplayDialog("导出成功", "导出成功", "关闭");
                }
                catch (Exception e) {
                    XNLogger.LogError("SpriteAtlas convert to tmp sprite asset failed.", ("err", e.Message));
                    EditorUtility.DisplayDialog("导出失败", "导出失败，查看控制台报错！", "关闭");
                }
            }
        }
    }

    // 递归遍历文件夹下的所有sprite/texture加入图集
    private static void TraverseSpriteAtlas(DirectoryInfo dirInfo, ref SpriteAtlas sa)
    {
        var childDirs = dirInfo.GetDirectories();
        if (childDirs.Length > 0) {
            foreach (DirectoryInfo childDirInfo in childDirs) {
                TraverseSpriteAtlas(childDirInfo, ref sa);
            }
        }
        foreach (FileInfo pngFile in dirInfo.GetFiles("*.png", SearchOption.AllDirectories)) {
            string allPath = pngFile.FullName;
            string assetPath = allPath.Substring(allPath.IndexOf("Assets"));
            Object asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
            if (assetPath.GetType() == typeof(Sprite) || assetPath.GetType() == typeof(Texture2D)) {
                SpriteAtlasExtensions.Add(sa, new Object[] { asset });
            }
        }
    }

    // 一个将图集转成tmp插图片需要的sprite asset的简易工具，注意只对v1版本的textruePacker生效
    //https://github.com/MingLQing/SpriteAtlasToTMPSprite
    public static void SpriteAtlasToTMPSprite(SpriteAtlas atlas)
    {
        if (atlas == null || atlas.spriteCount <= 0) {
            XNLogger.LogError("Sprites of atlas is empty, convert to TMP sprite asset failed.", ("atlasName", atlas.name));
            return;
        }

        // temporary settings
        TextureImporterPlatformSettings platformSettings = atlas.GetPlatformSettings(ActivePlatformName);
        bool backupOverridden = platformSettings.overridden;
        platformSettings.overridden = false;
        atlas.SetPlatformSettings(platformSettings);

        TextureImporterPlatformSettings defaultPlatformSettings = atlas.GetPlatformSettings(GetPlatformName(BuildTarget.NoTarget));
        TextureImporterFormat backupFormat = defaultPlatformSettings.format;
        defaultPlatformSettings.format = TextureImporterFormat.RGBA32;
        atlas.SetPlatformSettings(defaultPlatformSettings);

        SpriteAtlasTextureSettings textureSetting = atlas.GetTextureSettings();
        SpriteAtlasTextureSettings backupTextureSetting = textureSetting;
        textureSetting.readable = true;
        atlas.SetTextureSettings(textureSetting);

        SpriteAtlasPackingSettings packingSettings = atlas.GetPackingSettings();
        SpriteAtlasPackingSettings backupPackingSettings = packingSettings;
        packingSettings.enableRotation = false;
        packingSettings.enableTightPacking = false;
        atlas.SetPackingSettings(packingSettings);

        SpriteAtlasUtility.PackAtlases(new SpriteAtlas[] { atlas }, EditorUserBuildSettings.activeBuildTarget, false);

        // export png
        try {
            ExportSpriteAtlasTexture(atlas);
            ExportSpriteAsset(atlas);
        }
        catch (Exception ex) {
            XNLogger.LogError("SpriteAtlas to tmp sprite error.", ("err", ex.Message));
        } finally {
            // reset settings
            platformSettings.overridden = backupOverridden;
            atlas.SetPlatformSettings(platformSettings);

            defaultPlatformSettings.format = backupFormat;
            atlas.SetPlatformSettings(defaultPlatformSettings);

            atlas.SetTextureSettings(backupTextureSetting);
            atlas.SetPackingSettings(backupPackingSettings);
        }

        SpriteAtlasUtility.PackAtlases(new SpriteAtlas[] { atlas }, EditorUserBuildSettings.activeBuildTarget, false);
    }

    private static void ExportSpriteAtlasTexture(SpriteAtlas atlas)
    {
        Sprite[] sprites = new Sprite[1];
        atlas.GetSprites(sprites);
        Texture2D texture = SpriteUtility.GetSpriteTexture(sprites[0], true);

        byte[] pngBytes = texture.EncodeToPNG();
        string path = Path.Combine(TMP_SPRITE_PATH, Path.GetFileName(AssetDatabase.GetAssetPath(atlas))).Replace(".spriteatlas", ".png");
        File.WriteAllBytes(path, pngBytes);

        AssetDatabase.Refresh();

        TextureImporter textureImporter = (TextureImporter)AssetImporter.GetAtPath(path);
        textureImporter.textureType = TextureImporterType.Sprite;
        textureImporter.sRGBTexture = true;
        textureImporter.alphaSource = TextureImporterAlphaSource.FromInput;
        textureImporter.alphaIsTransparency = true;
        textureImporter.SaveAndReimport();
    }

    private static SpriteDataObject GetSpriteDataObject(SpriteAtlas atlas)
    {
        Sprite[] sprites = new Sprite[atlas.spriteCount];
        atlas.GetSprites(sprites);
        Texture2D texture = SpriteUtility.GetSpriteTexture(sprites[0], true);

        Meta meta = new Meta()
        {
            app = "",
            version = "1.0.0",
            image = $"{atlas.name}.png",
            format = "RGBA8888",
            size = new SpriteSize() { w = texture.width, h = texture.height },
            scale = 1,
        };

        List<Frame> frames = new List<Frame>(sprites.Length);
        for (int i = 0; i < sprites.Length; i++) {
            Sprite sprite = sprites[i];

            List<Vector2> uvs = SpriteUtility.GetSpriteUVs(sprite, true).ToList();
            uvs.Sort((a, b) => a.x.CompareTo(b.x));
            float minX = uvs[0].x;
            uvs.Sort((a, b) => a.y.CompareTo(b.y));
            float minY = uvs[0].y;

            float w = Mathf.RoundToInt(sprite.textureRect.width);
            float h = Mathf.RoundToInt(sprite.textureRect.height);

            float x = minX * texture.width;
            float y = texture.height - minY * texture.height - h;

            Frame frame = new Frame()
            {
                filename = sprite.name.Replace("(Clone)", ".png"),
                frame = new SpriteFrame() { x = x, y = y, w = w, h = h },
                rotated = false,
                trimmed = false,
                spriteSourceSize = new SpriteFrame() { x = 0, y = 0, w = w, h = h },
                sourceSize = new SpriteSize() { w = w, h = h },
                pivot = new Vector2(0f, 1f),
            };
            frames.Add(frame);
        }
        return new SpriteDataObject() { frames = frames, meta = meta };
    }

    private static void ExportSpriteAsset(SpriteAtlas atlas)
    {
        SpriteDataObject spriteDataObject = GetSpriteDataObject(atlas);
        string texturePath = Path.Combine(TMP_SPRITE_PATH, Path.GetFileName(AssetDatabase.GetAssetPath(atlas))).Replace(".spriteatlas", ".png");
        Texture2D spriteAtlasTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);

        string path = Path.Combine(TMP_SPRITE_PATH, Path.GetFileName(AssetDatabase.GetAssetPath(atlas))).Replace(".spriteatlas", ".asset");
        TMP_SpriteAsset spriteAsset = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(path);

        if (spriteAsset == null) {
            spriteAsset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            AssetDatabase.CreateAsset(spriteAsset, path);
        }

        spriteAsset.spriteSheet = spriteAtlasTexture;

        List<TMP_SpriteGlyph> spriteGlyphTable = new List<TMP_SpriteGlyph>();
        List<TMP_SpriteCharacter> spriteCharacterTable = new List<TMP_SpriteCharacter>();

        MethodInfo populateSpriteTablesMethod = typeof(TMP_SpriteAssetImporter).GetMethod("PopulateSpriteTables", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo addDefaultMaterialMethod = typeof(TMP_SpriteAssetImporter).GetMethod("AddDefaultMaterial", BindingFlags.Static | BindingFlags.NonPublic);

        var spriteCharacterTableProperty = typeof(TMP_SpriteAsset).GetProperty("spriteCharacterTable", BindingFlags.Instance | BindingFlags.Public);
        var spriteGlyphTableProperty = typeof(TMP_SpriteAsset).GetProperty("spriteGlyphTable", BindingFlags.Instance | BindingFlags.Public);
        var versionProperty = typeof(TMP_SpriteAsset).GetProperty("version", BindingFlags.Instance | BindingFlags.Public);

        populateSpriteTablesMethod.Invoke(null, new object[] { spriteDataObject, spriteCharacterTable, spriteGlyphTable });

        // 读表设置自定义character的glyph参数
        foreach (var character in spriteCharacterTable) {
            TmpSpriteDataType tmpSpriteData = TmpSpriteDataType.GetConfigData(character.name);
            if (tmpSpriteData != null) {
                var glyph = spriteGlyphTable[(int)character.glyphIndex];
                glyph.scale = tmpSpriteData.scale;
                var metrices = glyph.metrics;
                metrices.horizontalBearingX = tmpSpriteData.bx;
                metrices.horizontalBearingY = tmpSpriteData.by;
                metrices.horizontalAdvance = tmpSpriteData.ad;
                glyph.metrics = metrices;
            } else {
                XNLogger.LogError("Config not found for tmp sprite asset character.", ("characterName", character.name));
            }
        }

        spriteCharacterTableProperty.SetValue(spriteAsset, spriteCharacterTable);
        spriteGlyphTableProperty.SetValue(spriteAsset, spriteGlyphTable);
        versionProperty.SetValue(spriteAsset, "1.1.0");

        addDefaultMaterialMethod.Invoke(null, new object[] { spriteAsset });

        EditorUtility.SetDirty(spriteAsset);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}
