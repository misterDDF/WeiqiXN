using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using UnityEngine;
using XNLogger = XNClient.Logger.XNLogger;

public class ResourceManager : ModuleBase
{
    private const string ASSET_BUNDLE_MANIFEST_FILE_NAME = "bundle_manifest.json";

    public static uint ResourceBinderInstanceIds;
    public ResourceLoaderBase resLoader;
    public Dictionary<string, AssetBundle> bundleDict = new Dictionary<string, AssetBundle>(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> path2BundleName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public bool isReady { get; private set; }
    public bool isFailed { get; private set; }

    private Dictionary<string, IAssetRequest> requestMap = new Dictionary<string, IAssetRequest>();
    private Dictionary<string, IResourceLoadHandler> loadHandlerMap = new Dictionary<string, IResourceLoadHandler>();
    private Dictionary<string, IResourceLoadBinder> binderMap = new Dictionary<string, IResourceLoadBinder>();

#if !UNITY_EDITOR
    private enum AssetBundlePreloadState
    {
        None,
        LoadingManifest,
        LoadingBundle,
        Done,
        Failed,
    }

    private AssetBundlePreloadState assetBundlePreloadState = AssetBundlePreloadState.None;
    private IStreamingAssetsTextRequest manifestRequest;
    private IStreamingAssetsAssetBundleRequest bundleRequest;
    private Queue<string> pendingBundleNames = new Queue<string>();
    private string currentBundleName;
#endif

    protected class PackInfoFile
    {
        [JsonProperty("bundles")] public Dictionary<string, BundleInfo> Bundles { get; set; }
    }

    protected class BundleInfo
    {
        [JsonProperty("hash")] public string Hash { get; set; }
        [JsonProperty("size")] public int Size { get; set; }
    }

    public override void Init()
    {
        isReady = false;
        isFailed = false;

#if UNITY_EDITOR
        resLoader = new AssetDatabaseLoader(this);
        isReady = true;
#else
        resLoader = new AssetBundleLoader(this);
        StartPreloadAssetBundles();
#endif
    }

    public override void Update()
    {
#if !UNITY_EDITOR
        UpdatePreloadAssetBundles();
#endif

        // AssetRequest
        List<string> pendingDeleteRequest = new List<string>();
        foreach (var requestKV in requestMap) {
            if (requestKV.Value.isLoaded) {
                pendingDeleteRequest.Add(requestKV.Key);
                continue;
            }
            requestKV.Value.Update();
        }
        foreach (string assetFullPath in pendingDeleteRequest) {
            requestMap.Remove(assetFullPath);
        }

        // ResourceLoadHandler
        List<string> pendingDeleteHandler = new List<string>();
        foreach (var handlerKV in loadHandlerMap) {
            if (handlerKV.Value.isCanceled) {
                pendingDeleteHandler.Add(handlerKV.Key);
            }
        }
        foreach (string loaderId in pendingDeleteHandler) {
            loadHandlerMap.Remove(loaderId);
        }

        // ResourceLoadBinder
        List<string> pendingDeleteBinder = new List<string>();
        foreach (var binderKV in binderMap) {
            var loadHandlerIds = binderKV.Value.loadHandlerIds;
            List<string> pendingDeleteBinderLoader = new List<string>();
            foreach (var loaderId in loadHandlerIds) {
                if (!loadHandlerMap.ContainsKey(loaderId)) {
                    pendingDeleteBinderLoader.Add(loaderId);
                }
            }
            foreach (var loaderId in pendingDeleteBinderLoader) {
                loadHandlerIds.Remove(loaderId);
            }

            if (loadHandlerIds.Count <= 0) {
                pendingDeleteBinder.Add(binderKV.Key);
            }
        }
        foreach (string binderId in pendingDeleteBinder) {
            binderMap.Remove(binderId);
        }
    }

#if !UNITY_EDITOR
    private void StartPreloadAssetBundles()
    {
        string manifestRelativePath = $"AssetBundles/{ASSET_BUNDLE_MANIFEST_FILE_NAME}";
        manifestRequest = StreamingAssetsReader.Default.ReadText(manifestRelativePath);
        assetBundlePreloadState = AssetBundlePreloadState.LoadingManifest;
        XNLogger.LogInfo("Start preload asset bundle manifest.", ("manifestPath", manifestRequest.PathOrUrl));
    }

    private void UpdatePreloadAssetBundles()
    {
        switch (assetBundlePreloadState) {
            case AssetBundlePreloadState.LoadingManifest:
                UpdateAssetBundleManifestRequest();
                break;
            case AssetBundlePreloadState.LoadingBundle:
                UpdateAssetBundleRequest();
                break;
        }
    }

    private void UpdateAssetBundleManifestRequest()
    {
        if (manifestRequest == null || !manifestRequest.IsDone) {
            return;
        }

        if (!manifestRequest.IsSuccess) {
            XNLogger.LogError(
                "Load asset bundle manifest failed.",
                ("path", manifestRequest.PathOrUrl),
                ("error", manifestRequest.Error)
            );
            FinishAssetBundlePreload(false);
            return;
        }

        try {
            JArray bundleNameArray = JArray.Parse(manifestRequest.Text);
            foreach (JToken bundleNameToken in bundleNameArray) {
                string bundleName = bundleNameToken.Value<string>();
                if (!string.IsNullOrEmpty(bundleName)) {
                    pendingBundleNames.Enqueue(bundleName);
                }
            }
        }
        catch (Exception ex) {
            XNLogger.LogError("Parse asset bundle manifest failed.", ("error", ex.Message));
            FinishAssetBundlePreload(false);
            return;
        }
        finally {
            manifestRequest.Dispose();
            manifestRequest = null;
        }

        if (pendingBundleNames.Count <= 0) {
            XNLogger.LogWarn("Asset bundle manifest is empty.");
            FinishAssetBundlePreload(true);
            return;
        }

        StartNextAssetBundleRequest();
    }

    private void StartNextAssetBundleRequest()
    {
        if (pendingBundleNames.Count <= 0) {
            FinishAssetBundlePreload(true);
            return;
        }

        currentBundleName = pendingBundleNames.Dequeue();
        string bundleRelativePath = $"AssetBundles/{currentBundleName}";
        bundleRequest = StreamingAssetsReader.Default.LoadAssetBundle(bundleRelativePath);
        assetBundlePreloadState = AssetBundlePreloadState.LoadingBundle;
        XNLogger.LogInfo("Start preload asset bundle.", ("bundleName", currentBundleName), ("bundlePath", bundleRequest.PathOrUrl));
    }

    private void UpdateAssetBundleRequest()
    {
        if (bundleRequest == null || !bundleRequest.IsDone) {
            return;
        }

        if (!bundleRequest.IsSuccess) {
            XNLogger.LogError(
                "Load asset bundle failed.",
                ("bundleName", currentBundleName),
                ("path", bundleRequest.PathOrUrl),
                ("error", bundleRequest.Error)
            );
            FinishAssetBundlePreload(false);
            return;
        }

        AssetBundle bundle = bundleRequest.AssetBundle;
        if (bundle == null) {
            XNLogger.LogError("Loaded asset bundle is null.", ("bundleName", currentBundleName));
            FinishAssetBundlePreload(false);
            return;
        }

        RegisterLoadedAssetBundle(currentBundleName, bundle);
        bundleRequest.Dispose();
        bundleRequest = null;
        currentBundleName = string.Empty;
        StartNextAssetBundleRequest();
    }

    private void FinishAssetBundlePreload(bool success)
    {
        assetBundlePreloadState = success ? AssetBundlePreloadState.Done : AssetBundlePreloadState.Failed;
        isReady = success;
        isFailed = !success;

        manifestRequest?.Dispose();
        manifestRequest = null;
        bundleRequest?.Dispose();
        bundleRequest = null;

        if (success) {
            XNLogger.LogInfo("Preload asset bundles success.", ("bundleCount", bundleDict.Count.ToString()));
        }
    }
#endif

    private void RegisterLoadedAssetBundle(string bundleName, AssetBundle bundle)
    {
        if (bundle == null) {
            XNLogger.LogError("Register null asset bundle failed.", ("bundleName", bundleName));
            return;
        }

        string canonicalBundleName = string.IsNullOrEmpty(bundle.name) ? bundleName : bundle.name;
        bundleDict[bundleName] = bundle;
        if (!string.Equals(bundleName, canonicalBundleName, StringComparison.OrdinalIgnoreCase)) {
            bundleDict[canonicalBundleName] = bundle;
        }

        foreach (string assetPath in bundle.GetAllAssetNames()) {
            path2BundleName[assetPath] = canonicalBundleName;
        }
    }

    public GameObject LoadGamePrefabWithConfigId(string configId)
    {
        var config = GamePrefabDataType.GetConfigData(configId);
        if (config != null) {
            return LoadGamePrefab(config.resPath);
        } else {
            XNLogger.LogError("Config id invalid, laod game prefab failed.", ("configId", configId));
            return null;
        }
    }

    public GameObject LoadGamePrefab(string assetPath)
    {
        GameObject asset = LoadAsset<GameObject>(assetPath);
        if (asset != null) {
            var go = GameObject.Instantiate(asset);
            return go;
        }

        return null;
    }

    public IResourceLoadHandler LoadGamePrefabAsyncWithConfigId(IResourceLoadBinder binder, string configId, Action<GameObject> goInstantiateCB)
    {
        var config = GamePrefabDataType.GetConfigData(configId);
        if (config != null) {
            return LoadGamePrefabAsync(binder, config.resPath, goInstantiateCB);
        } else {
            XNLogger.LogError("Config id invalid, load game prefab async failed.", ("configId", configId));
            return null;
        }
    }

    public IResourceLoadHandler LoadGamePrefabAsync(IResourceLoadBinder binder, string assetPath, Action<GameObject> goInstantiateCB)
    {
        Action<GameObject> assetLoadedCB = (GameObject asset) =>
        {
            GameObject go = GameObject.Instantiate(asset);
            goInstantiateCB.Invoke(go);
        };
        var loadHandler = LoadAssetAsync<GameObject>(binder, assetPath, assetLoadedCB);
        if (loadHandler != null) {
            return loadHandler;
        }

        return null;
    }

    public TAsset LoadAsset<TAsset>(string assetPath) where TAsset : UnityEngine.Object
    {
        string assetFullPath = ResourceUtils.GetAssetFullPath<TAsset>(assetPath);
        if (string.IsNullOrEmpty(assetFullPath)) {
            return null;
        }
        return resLoader.Loadasset<TAsset>(assetFullPath);
    }

    public IResourceLoadHandler LoadAssetAsync<TAsset>(IResourceLoadBinder binder, string assetPath, Action<TAsset> assetLoadedCB) where TAsset : UnityEngine.Object
    {
        string assetFullPath = ResourceUtils.GetAssetFullPath<TAsset>(assetPath);
        if (string.IsNullOrEmpty(assetFullPath)) {
            return null;
        }
        AssetRequest<TAsset> request;
        if (!requestMap.TryGetValue(assetFullPath, out var _request)) {
            request = resLoader.LoadAssetAsync<TAsset>(assetFullPath);
        } else {
            request = (AssetRequest<TAsset>)_request;
        }
        if (request == null) {
            return null;
        }

        IResourceLoadHandler loadHandler = new ResourceLoadHandler<TAsset>(binder.binderId, assetFullPath, assetLoadedCB);
        if (!binderMap.ContainsKey(binder.binderId)) {
            binderMap[binder.binderId] = binder;
        }
        binder.loadHandlerIds.Add(loadHandler.loaderId);
        loadHandlerMap[loadHandler.loaderId] = loadHandler;

        requestMap[assetFullPath] = request;
        request.AddAssetLoadCB(loadHandler.OnAssetRequestLoaded);

        return loadHandler;
    }

    public void OnResourceBinderDestroyed(string binderId)
    {
        if (binderMap.TryGetValue(binderId, out var binder)) {
            foreach (string loaderId in binder.loadHandlerIds) {
                if (loadHandlerMap.TryGetValue(loaderId, out var loader)) {
                    loader.Cancel();
                }
            }
            binderMap.Remove(binderId);
        }
    }

    public override void OnDestroy()
    {
#if !UNITY_EDITOR
        manifestRequest?.Dispose();
        bundleRequest?.Dispose();
        pendingBundleNames.Clear();
#endif
        HashSet<AssetBundle> unloadedBundles = new HashSet<AssetBundle>();
        foreach (AssetBundle bundle in bundleDict.Values) {
            if (bundle != null) {
                if (unloadedBundles.Contains(bundle)) {
                    continue;
                }

                bundle.Unload(false);
                unloadedBundles.Add(bundle);
            }
        }

        bundleDict.Clear();
        path2BundleName.Clear();
        requestMap.Clear();
        loadHandlerMap.Clear();
        binderMap.Clear();
        base.OnDestroy();
    }
}
