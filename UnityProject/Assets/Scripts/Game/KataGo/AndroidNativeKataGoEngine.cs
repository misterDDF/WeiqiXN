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

    public AndroidNativeKataGoEngine(string nativeLibraryName)
    {
        bridgeApi = BridgeApi.Create(nativeLibraryName);
    }

    public void Start(string configPath, string modelPath, string workingDirectory)
    {
        ThrowIfDisposed();
        Stop();

        BridgeBackend = ReadBridgeBackend(bridgeApi);

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

    public void Stop()
    {
        if (engine != IntPtr.Zero) {
            bridgeApi.destroyEngine(engine);
            engine = IntPtr.Zero;
        }

        BridgeBackend = string.Empty;
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

    private delegate void FreeStringDelegate(IntPtr value);

    private delegate void DestroyEngineDelegate(IntPtr engine);

    private delegate IntPtr GetBridgeBackendDelegate();

    private sealed class BridgeApi
    {
        public readonly CreateEngineDelegate createEngine;
        public readonly AnalyzeDelegate analyze;
        public readonly FreeStringDelegate freeString;
        public readonly DestroyEngineDelegate destroyEngine;
        public readonly GetBridgeBackendDelegate getBridgeBackend;

        private BridgeApi(
            CreateEngineDelegate createEngine,
            AnalyzeDelegate analyze,
            FreeStringDelegate freeString,
            DestroyEngineDelegate destroyEngine,
            GetBridgeBackendDelegate getBridgeBackend)
        {
            this.createEngine = createEngine;
            this.analyze = analyze;
            this.freeString = freeString;
            this.destroyEngine = destroyEngine;
            this.getBridgeBackend = getBridgeBackend;
        }

        public static BridgeApi Create(string nativeLibraryName)
        {
            if (string.Equals(nativeLibraryName, "katago_bridge_opencl", StringComparison.OrdinalIgnoreCase)) {
                return new BridgeApi(
                    OpenClNative.kg_create_engine,
                    OpenClNative.kg_analyze,
                    OpenClNative.kg_free_string,
                    OpenClNative.kg_destroy_engine,
                    OpenClNative.kg_get_bridge_backend);
            }

            if (string.Equals(nativeLibraryName, "katago_bridge_eigen", StringComparison.OrdinalIgnoreCase)) {
                return new BridgeApi(
                    EigenNative.kg_create_engine,
                    EigenNative.kg_analyze,
                    EigenNative.kg_free_string,
                    EigenNative.kg_destroy_engine,
                    EigenNative.kg_get_bridge_backend);
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

        [DllImport("katago_bridge_opencl", CallingConvention = CallingConvention.Cdecl)]
        public static extern void kg_free_string(IntPtr value);

        [DllImport("katago_bridge_opencl", CallingConvention = CallingConvention.Cdecl)]
        public static extern void kg_destroy_engine(IntPtr engine);

        [DllImport("katago_bridge_opencl", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr kg_get_bridge_backend();
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

        [DllImport("katago_bridge_eigen", CallingConvention = CallingConvention.Cdecl)]
        public static extern void kg_free_string(IntPtr value);

        [DllImport("katago_bridge_eigen", CallingConvention = CallingConvention.Cdecl)]
        public static extern void kg_destroy_engine(IntPtr engine);

        [DllImport("katago_bridge_eigen", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr kg_get_bridge_backend();
    }
}
#endif
