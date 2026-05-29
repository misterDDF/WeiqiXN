using System;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json.Linq;

#if UNITY_ANDROID && !UNITY_EDITOR
internal sealed class AndroidNativeKataGoEngine : IDisposable
{
    private const int ErrorBufferSize = 4096;

    private readonly BridgeApi bridgeApi;
    private IntPtr engine;
    private bool disposed;

    public bool IsRunning => engine != IntPtr.Zero;
    public string BridgeBackend { get; private set; } = string.Empty;
    public bool SupportsConcurrentAnalyze { get; private set; }
    public bool SupportsAnalyzeMany { get; private set; }

    public AndroidNativeKataGoEngine(string nativeLibraryName)
    {
        bridgeApi = BridgeApi.Create(nativeLibraryName);
    }

    public void Start(string configPath, string modelPath, string workingDirectory)
    {
        ThrowIfDisposed();
        Stop();

        BridgeBackend = ReadBridgeBackend(bridgeApi);
        SupportsConcurrentAnalyze = bridgeApi.supportsConcurrentAnalyze() != 0;
        SupportsAnalyzeMany = bridgeApi.supportsAnalyzeMany() != 0;

        StringBuilder error = new StringBuilder(ErrorBufferSize);
        int result = bridgeApi.createEngine(configPath, modelPath, workingDirectory, out engine, error, error.Capacity);
        if (result == 0 || engine == IntPtr.Zero) {
            string message = error.ToString();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(message) ? "kg_create_engine failed." : message);
        }
    }

    public JObject Analyze(JObject query, int timeoutMs)
    {
        ThrowIfDisposed();
        if (engine == IntPtr.Zero) {
            throw new InvalidOperationException("KataGo native engine is not running.");
        }

        string requestJson = query.ToString(Newtonsoft.Json.Formatting.None);
        StringBuilder error = new StringBuilder(ErrorBufferSize);
        int result = bridgeApi.analyze(engine, requestJson, timeoutMs, out IntPtr responsePtr, error, error.Capacity);
        try {
            if (result == 0 || responsePtr == IntPtr.Zero) {
                string message = error.ToString();
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(message) ? "kg_analyze failed." : message);
            }

            string responseJson = PtrToUtf8String(responsePtr);
            return JObject.Parse(responseJson);
        }
        finally {
            if (responsePtr != IntPtr.Zero) {
                bridgeApi.freeString(responsePtr);
            }
        }
    }

    public JArray AnalyzeMany(JObject query, int timeoutMs)
    {
        ThrowIfDisposed();
        if (!SupportsAnalyzeMany || bridgeApi.analyzeMany == null) {
            throw new NotSupportedException("KataGo native bridge does not export kg_analyze_many.");
        }

        if (engine == IntPtr.Zero) {
            throw new InvalidOperationException("KataGo native engine is not running.");
        }

        string requestJson = query.ToString(Newtonsoft.Json.Formatting.None);
        StringBuilder error = new StringBuilder(ErrorBufferSize);
        int result = bridgeApi.analyzeMany(engine, requestJson, timeoutMs, out IntPtr responsePtr, error, error.Capacity);
        try {
            if (result == 0 || responsePtr == IntPtr.Zero) {
                string message = error.ToString();
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(message) ? "kg_analyze_many failed." : message);
            }

            string responseJson = PtrToUtf8String(responsePtr);
            return JArray.Parse(responseJson);
        }
        finally {
            if (responsePtr != IntPtr.Zero) {
                bridgeApi.freeString(responsePtr);
            }
        }
    }

    public void Stop()
    {
        if (engine != IntPtr.Zero) {
            bridgeApi.destroyEngine(engine);
            engine = IntPtr.Zero;
        }

        BridgeBackend = string.Empty;
        SupportsConcurrentAnalyze = false;
        SupportsAnalyzeMany = false;
    }

    public void Dispose()
    {
        if (disposed) {
            return;
        }

        Stop();
        disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (disposed) {
            throw new ObjectDisposedException(nameof(AndroidNativeKataGoEngine));
        }
    }

    private static string ReadBridgeBackend(BridgeApi api)
    {
        IntPtr infoPtr = api.getBridgeBackend();
        if (infoPtr == IntPtr.Zero) {
            throw new InvalidOperationException("kg_get_bridge_backend returned null.");
        }

        string backend = PtrToUtf8String(infoPtr).Trim();
        if (string.IsNullOrWhiteSpace(backend)) {
            throw new InvalidOperationException("kg_get_bridge_backend returned empty backend name.");
        }

        return backend.ToLowerInvariant();
    }

    private static string PtrToUtf8String(IntPtr ptr)
    {
        int length = 0;
        while (Marshal.ReadByte(ptr, length) != 0) {
            length++;
        }

        byte[] bytes = new byte[length];
        Marshal.Copy(ptr, bytes, 0, length);
        return Encoding.UTF8.GetString(bytes);
    }

    private delegate int CreateEngineDelegate(
        string configPath,
        string modelPath,
        string workingDirectory,
        out IntPtr outEngine,
        StringBuilder errorBuffer,
        int errorBufferSize);

    private delegate int AnalyzeDelegate(
        IntPtr engine,
        string requestJson,
        int timeoutMs,
        out IntPtr outResponseJson,
        StringBuilder errorBuffer,
        int errorBufferSize);

    private delegate int AnalyzeManyDelegate(
        IntPtr engine,
        string requestJson,
        int timeoutMs,
        out IntPtr outResponsesJson,
        StringBuilder errorBuffer,
        int errorBufferSize);

    private delegate void FreeStringDelegate(IntPtr value);

    private delegate void DestroyEngineDelegate(IntPtr engine);

    private delegate IntPtr GetBridgeBackendDelegate();

    private delegate int SupportsConcurrentAnalyzeDelegate();

    private delegate int SupportsAnalyzeManyDelegate();

    private sealed class BridgeApi
    {
        public readonly CreateEngineDelegate createEngine;
        public readonly AnalyzeDelegate analyze;
        public readonly AnalyzeManyDelegate analyzeMany;
        public readonly FreeStringDelegate freeString;
        public readonly DestroyEngineDelegate destroyEngine;
        public readonly GetBridgeBackendDelegate getBridgeBackend;
        public readonly SupportsConcurrentAnalyzeDelegate supportsConcurrentAnalyze;
        public readonly SupportsAnalyzeManyDelegate supportsAnalyzeMany;

        private BridgeApi(
            CreateEngineDelegate createEngine,
            AnalyzeDelegate analyze,
            AnalyzeManyDelegate analyzeMany,
            FreeStringDelegate freeString,
            DestroyEngineDelegate destroyEngine,
            GetBridgeBackendDelegate getBridgeBackend,
            SupportsConcurrentAnalyzeDelegate supportsConcurrentAnalyze,
            SupportsAnalyzeManyDelegate supportsAnalyzeMany)
        {
            this.createEngine = createEngine;
            this.analyze = analyze;
            this.analyzeMany = analyzeMany;
            this.freeString = freeString;
            this.destroyEngine = destroyEngine;
            this.getBridgeBackend = getBridgeBackend;
            this.supportsConcurrentAnalyze = supportsConcurrentAnalyze;
            this.supportsAnalyzeMany = supportsAnalyzeMany;
        }

        public static BridgeApi Create(string nativeLibraryName)
        {
            if (string.Equals(nativeLibraryName, "katago_bridge_opencl", StringComparison.OrdinalIgnoreCase)) {
                return new BridgeApi(
                    OpenClNative.kg_create_engine,
                    OpenClNative.kg_analyze,
                    OpenClNative.kg_analyze_many,
                    OpenClNative.kg_free_string,
                    OpenClNative.kg_destroy_engine,
                    OpenClNative.kg_get_bridge_backend,
                    OpenClNative.TrySupportsConcurrentAnalyze,
                    OpenClNative.kg_supports_analyze_many);
            }

            if (string.Equals(nativeLibraryName, "katago_bridge_eigen", StringComparison.OrdinalIgnoreCase)) {
                return new BridgeApi(
                    EigenNative.kg_create_engine,
                    EigenNative.kg_analyze,
                    EigenNative.kg_analyze_many,
                    EigenNative.kg_free_string,
                    EigenNative.kg_destroy_engine,
                    EigenNative.kg_get_bridge_backend,
                    EigenNative.TrySupportsConcurrentAnalyze,
                    EigenNative.kg_supports_analyze_many);
            }

            throw new ArgumentException($"Unsupported Android KataGo native library: {nativeLibraryName}", nameof(nativeLibraryName));
        }
    }

    private static class OpenClNative
    {
        [DllImport("katago_bridge_opencl", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int kg_create_engine(
            string configPath,
            string modelPath,
            string workingDirectory,
            out IntPtr outEngine,
            StringBuilder errorBuffer,
            int errorBufferSize);

        [DllImport("katago_bridge_opencl", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int kg_analyze(
            IntPtr engine,
            string requestJson,
            int timeoutMs,
            out IntPtr outResponseJson,
            StringBuilder errorBuffer,
            int errorBufferSize);

        [DllImport("katago_bridge_opencl", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int kg_analyze_many(
            IntPtr engine,
            string requestJson,
            int timeoutMs,
            out IntPtr outResponsesJson,
            StringBuilder errorBuffer,
            int errorBufferSize);

        [DllImport("katago_bridge_opencl", CallingConvention = CallingConvention.Cdecl)]
        public static extern void kg_free_string(IntPtr value);

        [DllImport("katago_bridge_opencl", CallingConvention = CallingConvention.Cdecl)]
        public static extern void kg_destroy_engine(IntPtr engine);

        [DllImport("katago_bridge_opencl", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr kg_get_bridge_backend();

        public static int TrySupportsConcurrentAnalyze()
        {
            try {
                return kg_supports_concurrent_analyze();
            }
            catch (EntryPointNotFoundException) {
                return 0;
            }
        }

        [DllImport("katago_bridge_opencl", CallingConvention = CallingConvention.Cdecl)]
        private static extern int kg_supports_concurrent_analyze();

        [DllImport("katago_bridge_opencl", CallingConvention = CallingConvention.Cdecl)]
        public static extern int kg_supports_analyze_many();
    }

    private static class EigenNative
    {
        [DllImport("katago_bridge_eigen", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int kg_create_engine(
            string configPath,
            string modelPath,
            string workingDirectory,
            out IntPtr outEngine,
            StringBuilder errorBuffer,
            int errorBufferSize);

        [DllImport("katago_bridge_eigen", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int kg_analyze(
            IntPtr engine,
            string requestJson,
            int timeoutMs,
            out IntPtr outResponseJson,
            StringBuilder errorBuffer,
            int errorBufferSize);

        [DllImport("katago_bridge_eigen", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int kg_analyze_many(
            IntPtr engine,
            string requestJson,
            int timeoutMs,
            out IntPtr outResponsesJson,
            StringBuilder errorBuffer,
            int errorBufferSize);

        [DllImport("katago_bridge_eigen", CallingConvention = CallingConvention.Cdecl)]
        public static extern void kg_free_string(IntPtr value);

        [DllImport("katago_bridge_eigen", CallingConvention = CallingConvention.Cdecl)]
        public static extern void kg_destroy_engine(IntPtr engine);

        [DllImport("katago_bridge_eigen", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr kg_get_bridge_backend();

        public static int TrySupportsConcurrentAnalyze()
        {
            try {
                return kg_supports_concurrent_analyze();
            }
            catch (EntryPointNotFoundException) {
                return 0;
            }
        }

        [DllImport("katago_bridge_eigen", CallingConvention = CallingConvention.Cdecl)]
        private static extern int kg_supports_concurrent_analyze();

        [DllImport("katago_bridge_eigen", CallingConvention = CallingConvention.Cdecl)]
        public static extern int kg_supports_analyze_many();
    }
}
#endif
