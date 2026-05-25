using System.IO;
using UnityEngine;

public static class GameSaveConfig
{
    public static string SaveRootPath => GetSaveRootPath();
    public static string UserSaveFilePath => Path.Combine(SaveRootPath, "User.json");
    public static string ReplayRootPath => Path.Combine(SaveRootPath, "replay");
    public static string ReplayIndexPath => Path.Combine(ReplayRootPath, "ReplayIndex.json");

    public static string GetDuelSceneSavePath(int saveSlotIndex)
    {
        return Path.Combine(GetSaveSlotPath(saveSlotIndex), "DuelScene.json");
    }

    public static string GetDuelRecordSavePath(int saveSlotIndex)
    {
        return Path.Combine(GetSaveSlotPath(saveSlotIndex), "DuelRecord.json");
    }

    public static string GetDuelSaveInfoPath(int saveSlotIndex)
    {
        return Path.Combine(GetSaveSlotPath(saveSlotIndex), "SaveInfo.json");
    }

    public static string GetSaveSlotPath(int saveSlotIndex)
    {
        return Path.Combine(SaveRootPath, saveSlotIndex.ToString());
    }

    public static string GetReplayGamePath(string gameId)
    {
        return Path.Combine(ReplayRootPath, gameId);
    }

    public static string GetReplayDuelScenePath(string gameId)
    {
        return Path.Combine(GetReplayGamePath(gameId), "DuelScene.json");
    }

    public static string GetReplayDuelRecordPath(string gameId)
    {
        return Path.Combine(GetReplayGamePath(gameId), "DuelRecord.json");
    }

    public static string GetReplayDuelSaveInfoPath(string gameId)
    {
        return Path.Combine(GetReplayGamePath(gameId), "SaveInfo.json");
    }

    private static string GetSaveRootPath()
    {
#if UNITY_EDITOR
        DirectoryInfo unityProjectDir = Directory.GetParent(Application.dataPath);
        DirectoryInfo workspaceRootDir = unityProjectDir?.Parent;
        if (workspaceRootDir != null) {
            return Path.Combine(workspaceRootDir.FullName, "save");
        }
#endif

#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX
        DirectoryInfo playerRootDir = Directory.GetParent(Application.dataPath);
        if (playerRootDir != null) {
            return Path.Combine(playerRootDir.FullName, "save");
        }
#endif

        return Application.persistentDataPath;
    }

    public const string SavableObj_Type_Field_Name = "_type";
    public const string SavableDict_Inner_Dict_Field_Name = "_innerDict";
    public const string SavableSet_Inner_Set_Field_Name = "_innerSet";
    public const string SavableList_Inner_List_Field_Name = "_innerList";
    public const string SavableList_Count_Field_Name = "_count";
}
