using UnityEngine;

public static class BuildConfig
{
    public static bool BUILD_BUNDLE_DISABLE_WRITE_TYPE_TREE = false;
    public readonly static string PATH_BUILDIN_ASSETBUNDLE = Application.streamingAssetsPath + "/AssetBundles";

    public readonly static string PATH_PACK_SCENE = Application.dataPath + "/Scenes";
    public readonly static string PATH_PACK_MODEL = Application.dataPath + "/Models";
    public readonly static string PATH_PACK_JSON = Application.dataPath + "/Config/DataJson";
    public readonly static string PATH_PACK_UI_PREFAB = Application.dataPath + "/UI/Prefab";
    public readonly static string PATH_PACK_UI_TEXTUER = Application.dataPath + "/UI/Textures";

    public const string AB_LABEL_SCENE = "scene";
    public const string AB_LABEL_MODEL = "model";
    public const string AB_LABEL_JSON = "config_json";
    public const string AB_LABEL_UI_PREFAB = "ui_main_prefab";
    public const string AB_LABEL_UI_TEXTURE = "ui_main_texture";
    public const string AB_LABEL_DEBUG = "debug";
    public const string AB_LABEL_RUNTIME_ASSET = "runtime_asset";
    public const string PATH_RUNTIME_ASSET_CONFIG = "Assets/Config/DataJson/runtime_asset/runtime_asset.json";
    public const string PATH_DEBUG_CONSOLE_PREFAB = "Assets/Plugins/IngameDebugConsole/IngameDebugConsole.prefab";

    public const string BUILD_PATH_ROOT = "../Build";
    public readonly static string BUILD_PATH_WINDOWS = $"{BUILD_PATH_ROOT}/PC/WeiqiXN.exe";
    public const string BUILD_PATH_WEBGL = "../WebGL";
    public const string ASSET_BUNDLE_MANIFEST_FILE_NAME = "bundle_manifest.json";
}
