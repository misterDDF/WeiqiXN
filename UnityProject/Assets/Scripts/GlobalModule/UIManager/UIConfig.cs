using System.IO;
using UnityEngine;

public enum UIContextType
{
    General = 1,
    Header = 2,
    Loading = 6,
    TopMost = 8,
}

public static class UIConfig
{
    public readonly static string PATH_UI_SCRIPT_FOLDER = Path.Combine(Application.dataPath, "Scripts", "Game", "UI");
    public readonly static string PATH_UI_BINDER_EXPORT = Path.Combine(PATH_UI_SCRIPT_FOLDER, "Binder");
    public readonly static string PATH_UI_LOGIC_EXPORT = Path.Combine(PATH_UI_SCRIPT_FOLDER, "Logic");
    public const string NAME_UI_ROOT = "uiRoot";
    public const string UI_EVENTSYSTEM_CONFIG_ID = "eventSystem";

    public const int CONTEXT_INCREASE_CANVAS_ORDER = 1000;
    public const int MAINPAGE_INCREASE_CANVAS_ORDER = 20;
    public const int POPUP_INCREASE_CANVAS_ORDER = 1;
    public const float PAGE_GAMEOBJECT_CACHE_TIME = 30;
}
