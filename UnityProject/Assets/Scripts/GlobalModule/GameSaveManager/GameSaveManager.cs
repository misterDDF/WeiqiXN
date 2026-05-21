using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Threading.Tasks;
using XNClient.Logger;

public class GameSaveManager : ModuleBase
{
    public bool savingLock;

    public override void Init()
    {
        savingLock = false;
    }

    public void SaveData(SavableObj savableObj, string saveFilePath)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        XNLogger.LogWarn("Save data is skipped on WebGL platform.", ("saveFilePath", saveFilePath));
        return;
#else
        if (savingLock) {
            XNLogger.LogError("Saving lock is being occupied, save data failed.");
            return;
        }

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
#endif
    }

    public async Task<bool> SaveDataAsync(SavableObj savableObj, string saveFilePath)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        XNLogger.LogWarn("Save data async is skipped on WebGL platform.", ("saveFilePath", saveFilePath));
        await Task.CompletedTask;
        return false;
#else
        if (savingLock) {
            XNLogger.LogError("Saving lock is being occupied, save data async failed.");
            return false;
        }

        savingLock = true;
        try {
            Global.Instance.uiManager.ShowPage<SavingPopup>();
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
            await File.WriteAllTextAsync(saveFilePath, saveJObject.ToString());
            XNLogger.LogInfo("Save data async success.", ("saveRootName", saveRootName), ("saveFilePath", saveFilePath));
            return true;
        }
        catch (Exception ex) {
            XNLogger.LogError("Save data async failed.", ("saveFilePath", saveFilePath), ("err", ex.Message));
            return false;
        }
        finally {
            savingLock = false;
            Global.Instance.uiManager.ClosePage<SavingPopup>();
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


