using Newtonsoft.Json.Linq;
using System;
using System.IO;
using XNClient.Logger;

public class GameSaveManager : ModuleBase
{
    public override void Init()
    {
    }

    public bool SaveData(SavableObj savableObj, string saveFilePath)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        XNLogger.LogWarn("Save data is skipped on WebGL platform.", ("saveFilePath", saveFilePath));
        return false;
#else
        try {
            string saveRootName = Path.GetFileNameWithoutExtension(saveFilePath);
            string saveDirPath = Path.GetDirectoryName(saveFilePath);
            Directory.CreateDirectory(saveDirPath);
            if (!File.Exists(saveFilePath)) {
                File.Create(saveFilePath).Close();
            }

            if (string.IsNullOrEmpty(savableObj.savePath)) {
                savableObj.savePath = saveRootName;
            }
            JObject saveJObject = savableObj.SaveObj();
            File.WriteAllText(saveFilePath, saveJObject.ToString());
            XNLogger.LogInfo("Save data success.", ("saveRootName", saveRootName), ("saveFilePath", saveFilePath));
            return true;
        }
        catch (Exception ex) {
            XNLogger.LogError("Save data failed.", ("saveFilePath", saveFilePath), ("err", ex.Message));
            return false;
        }
#endif
    }

    public void LoadData(SavableObj savableObj, string saveFilePath)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        XNLogger.LogWarn("Load data is skipped on WebGL platform.", ("saveFilePath", saveFilePath));
        return;
#else
        if (!File.Exists(saveFilePath)) {
            XNLogger.LogError("Save file not exists, load save data failed.", ("saveFilePath", saveFilePath));
            return;
        }

        string saveRootName = Path.GetFileNameWithoutExtension(saveFilePath);
        if (string.IsNullOrEmpty(savableObj.savePath)) {
            savableObj.savePath = saveRootName;
        }
        string jsonStr = File.ReadAllText(saveFilePath);
        try {
            JObject jObject = JObject.Parse(jsonStr);
            savableObj.LoadObj(jObject);
            XNLogger.LogInfo("Load data success.", ("saveRootName", saveRootName), ("saveFilePath", saveFilePath));
        }
        catch (Exception ex) {
            XNLogger.LogError("Load data failed.", ("saveRootName", saveRootName), ("saveFilePath", saveFilePath), ("err", ex.Message));
        }
#endif
    }
}


