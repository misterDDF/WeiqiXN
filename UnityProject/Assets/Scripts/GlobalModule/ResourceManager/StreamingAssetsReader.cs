using System;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using XNLogger = XNClient.Logger.XNLogger;

public sealed class StreamingAssetsReader
{
    public static StreamingAssetsReader Default { get; } = new StreamingAssetsReader();

    public IStreamingAssetsTextRequest ReadText(string relativePath)
    {
        string assetPath = BuildStreamingAssetsPath(relativePath);
#if (UNITY_ANDROID && !UNITY_EDITOR) || (UNITY_WEBGL && !UNITY_EDITOR)
        UnityWebRequest request = UnityWebRequest.Get(assetPath);
        request.SendWebRequest();
        return new UnityWebRequestTextRequest(assetPath, request);
#else
        return new FileTextRequest(assetPath);
#endif
    }

    public IStreamingAssetsAssetBundleRequest LoadAssetBundle(string relativePath)
    {
        string assetPath = BuildStreamingAssetsPath(relativePath);
#if (UNITY_ANDROID && !UNITY_EDITOR) || (UNITY_WEBGL && !UNITY_EDITOR)
        UnityWebRequest request = UnityWebRequestAssetBundle.GetAssetBundle(assetPath);
        request.SendWebRequest();
        return new UnityWebRequestAssetBundleRequest(assetPath, request);
#else
        return new FileAssetBundleRequest(assetPath);
#endif
    }

    public IStreamingAssetsFileCopyRequest CopyToFile(string relativePath, string destinationPath)
    {
        string assetPath = BuildStreamingAssetsPath(relativePath);
#if (UNITY_ANDROID && !UNITY_EDITOR) || (UNITY_WEBGL && !UNITY_EDITOR)
        return new UnityWebRequestFileCopyRequest(assetPath, destinationPath);
#else
        return new FileCopyRequest(assetPath, destinationPath);
#endif
    }

    private static string BuildStreamingAssetsPath(string relativePath)
    {
        string normalizedRelativePath = (relativePath ?? string.Empty).Replace('\\', '/').TrimStart('/');
#if (UNITY_ANDROID && !UNITY_EDITOR) || (UNITY_WEBGL && !UNITY_EDITOR)
        return $"{Application.streamingAssetsPath}/{normalizedRelativePath}";
#else
        return Path.Combine(Application.streamingAssetsPath, normalizedRelativePath);
#endif
    }

    private sealed class FileTextRequest : IStreamingAssetsTextRequest
    {
        private readonly string path;
        private bool isRead;
        private bool isSuccess;
        private string text;
        private string error;

        public FileTextRequest(string path)
        {
            this.path = path;
        }

        public bool IsDone
        {
            get
            {
                ReadIfNeeded();
                return true;
            }
        }

        public bool IsSuccess
        {
            get
            {
                ReadIfNeeded();
                return isSuccess;
            }
        }

        public string Text
        {
            get
            {
                ReadIfNeeded();
                return text;
            }
        }

        public string Error
        {
            get
            {
                ReadIfNeeded();
                return error;
            }
        }

        public string PathOrUrl => path;

        public void Dispose()
        {
        }

        private void ReadIfNeeded()
        {
            if (isRead) {
                return;
            }

            isRead = true;
            try {
                text = File.ReadAllText(path);
                isSuccess = true;
            }
            catch (Exception ex) {
                error = $"{ex.GetType().Name}: {ex.Message}";
                text = string.Empty;
                isSuccess = false;
            }
        }
    }

    private sealed class UnityWebRequestTextRequest : IStreamingAssetsTextRequest
    {
        private readonly string url;
        private UnityWebRequest request;

        public UnityWebRequestTextRequest(string url, UnityWebRequest request)
        {
            this.url = url;
            this.request = request;
        }

        public bool IsDone => request == null || request.isDone;
        public bool IsSuccess => request != null && request.isDone && request.result == UnityWebRequest.Result.Success;
        public string Text => request?.downloadHandler?.text ?? string.Empty;
        public string Error => request?.error ?? string.Empty;
        public string PathOrUrl => url;

        public void Dispose()
        {
            request?.Dispose();
            request = null;
        }
    }

    private sealed class FileAssetBundleRequest : IStreamingAssetsAssetBundleRequest
    {
        private readonly string path;
        private bool isLoaded;
        private bool isSuccess;
        private AssetBundle assetBundle;
        private string error;

        public FileAssetBundleRequest(string path)
        {
            this.path = path;
        }

        public bool IsDone
        {
            get
            {
                LoadIfNeeded();
                return true;
            }
        }

        public bool IsSuccess
        {
            get
            {
                LoadIfNeeded();
                return isSuccess;
            }
        }

        public AssetBundle AssetBundle
        {
            get
            {
                LoadIfNeeded();
                return assetBundle;
            }
        }

        public string Error
        {
            get
            {
                LoadIfNeeded();
                return error;
            }
        }

        public string PathOrUrl => path;

        public void Dispose()
        {
        }

        private void LoadIfNeeded()
        {
            if (isLoaded) {
                return;
            }

            isLoaded = true;
            try {
                assetBundle = AssetBundle.LoadFromFile(path);
                isSuccess = assetBundle != null;
                if (!isSuccess) {
                    error = "AssetBundle.LoadFromFile returned null.";
                }
            }
            catch (Exception ex) {
                error = $"{ex.GetType().Name}: {ex.Message}";
                assetBundle = null;
                isSuccess = false;
            }
        }
    }

    private sealed class UnityWebRequestAssetBundleRequest : IStreamingAssetsAssetBundleRequest
    {
        private readonly string url;
        private UnityWebRequest request;
        private AssetBundle assetBundle;

        public UnityWebRequestAssetBundleRequest(string url, UnityWebRequest request)
        {
            this.url = url;
            this.request = request;
        }

        public bool IsDone => request == null || request.isDone;

        public bool IsSuccess
        {
            get
            {
                if (request == null || !request.isDone || request.result != UnityWebRequest.Result.Success) {
                    return false;
                }

                return AssetBundle != null;
            }
        }

        public AssetBundle AssetBundle
        {
            get
            {
                if (assetBundle == null && request != null && request.isDone && request.result == UnityWebRequest.Result.Success) {
                    assetBundle = DownloadHandlerAssetBundle.GetContent(request);
                }

                return assetBundle;
            }
        }

        public string Error => request?.error ?? string.Empty;
        public string PathOrUrl => url;

        public void Dispose()
        {
            request?.Dispose();
            request = null;
        }
    }

    private sealed class FileCopyRequest : IStreamingAssetsFileCopyRequest
    {
        private readonly string sourcePath;
        private readonly string destinationPath;
        private bool isCopied;
        private bool isSuccess;
        private string error;
        private long bytesCopied;

        public FileCopyRequest(string sourcePath, string destinationPath)
        {
            this.sourcePath = sourcePath;
            this.destinationPath = destinationPath;
        }

        public bool IsDone
        {
            get
            {
                CopyIfNeeded();
                return true;
            }
        }

        public bool IsSuccess
        {
            get
            {
                CopyIfNeeded();
                return isSuccess;
            }
        }

        public float Progress
        {
            get
            {
                CopyIfNeeded();
                return isSuccess ? 1f : 0f;
            }
        }

        public long BytesCopied
        {
            get
            {
                CopyIfNeeded();
                return bytesCopied;
            }
        }

        public string Error
        {
            get
            {
                CopyIfNeeded();
                return error;
            }
        }

        public string SourcePathOrUrl => sourcePath;
        public string DestinationPath => destinationPath;

        public void Dispose()
        {
        }

        private void CopyIfNeeded()
        {
            if (isCopied) {
                return;
            }

            isCopied = true;
            try {
                string destinationDirectory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(destinationDirectory)) {
                    Directory.CreateDirectory(destinationDirectory);
                }

                File.Copy(sourcePath, destinationPath, true);
                bytesCopied = new FileInfo(destinationPath).Length;
                isSuccess = true;
            }
            catch (Exception ex) {
                error = $"{ex.GetType().Name}: {ex.Message}";
                bytesCopied = 0;
                isSuccess = false;
            }
        }
    }

    private sealed class UnityWebRequestFileCopyRequest : IStreamingAssetsFileCopyRequest
    {
        private readonly string url;
        private readonly string destinationPath;
        private readonly string tempPath;
        private UnityWebRequest request;
        private bool isFinished;
        private bool isSuccess;
        private string error;

        public UnityWebRequestFileCopyRequest(string url, string destinationPath)
        {
            this.url = url;
            this.destinationPath = destinationPath;
            tempPath = destinationPath + ".tmp";

            string destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationDirectory)) {
                Directory.CreateDirectory(destinationDirectory);
            }

            if (File.Exists(tempPath)) {
                File.Delete(tempPath);
            }

            request = UnityWebRequest.Get(url);
            request.downloadHandler = new DownloadHandlerFile(tempPath);
            request.SendWebRequest();
        }

        public bool IsDone
        {
            get
            {
                TryFinish();
                return isFinished;
            }
        }

        public bool IsSuccess
        {
            get
            {
                TryFinish();
                return isSuccess;
            }
        }

        public float Progress => request != null ? Mathf.Clamp01(request.downloadProgress) : (isSuccess ? 1f : 0f);
        public long BytesCopied => File.Exists(tempPath) ? new FileInfo(tempPath).Length : (File.Exists(destinationPath) ? new FileInfo(destinationPath).Length : 0);
        public string Error => error ?? request?.error ?? string.Empty;
        public string SourcePathOrUrl => url;
        public string DestinationPath => destinationPath;

        public void Dispose()
        {
            request?.Dispose();
            request = null;
        }

        private void TryFinish()
        {
            if (isFinished || request == null || !request.isDone) {
                return;
            }

            isFinished = true;
            if (request.result != UnityWebRequest.Result.Success) {
                error = request.error;
                isSuccess = false;
                CleanupTempFile();
                return;
            }

            try {
                if (File.Exists(destinationPath)) {
                    File.Delete(destinationPath);
                }

                File.Move(tempPath, destinationPath);
                isSuccess = true;
            }
            catch (Exception ex) {
                error = $"{ex.GetType().Name}: {ex.Message}";
                isSuccess = false;
                CleanupTempFile();
            }
        }

        private void CleanupTempFile()
        {
            try {
                if (File.Exists(tempPath)) {
                    File.Delete(tempPath);
                }
            }
            catch (Exception ex) {
                XNLogger.LogWarn("Cleanup copied temp file failed.", ("path", tempPath), ("error", ex.Message));
            }
        }
    }
}

public interface IStreamingAssetsTextRequest : IDisposable
{
    bool IsDone { get; }
    bool IsSuccess { get; }
    string Text { get; }
    string Error { get; }
    string PathOrUrl { get; }
}

public interface IStreamingAssetsAssetBundleRequest : IDisposable
{
    bool IsDone { get; }
    bool IsSuccess { get; }
    AssetBundle AssetBundle { get; }
    string Error { get; }
    string PathOrUrl { get; }
}

public interface IStreamingAssetsFileCopyRequest : IDisposable
{
    bool IsDone { get; }
    bool IsSuccess { get; }
    float Progress { get; }
    long BytesCopied { get; }
    string Error { get; }
    string SourcePathOrUrl { get; }
    string DestinationPath { get; }
}
