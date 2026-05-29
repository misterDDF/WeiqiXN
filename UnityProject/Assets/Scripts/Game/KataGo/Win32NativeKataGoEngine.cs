using System;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json.Linq;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
internal sealed class Win32NativeKataGoEngine : IDisposable
{
    private const int ErrorBufferSize = 4096;
    private const int LoadLibrarySearchDefaultDirs = 0x00001000;
    private const int LoadLibrarySearchDllLoadDir = 0x00000100;

    private IntPtr engine;
    private IntPtr library;
    private KgCreateEngine kgCreateEngine;
    private KgAnalyze kgAnalyze;
    private KgFreeString kgFreeString;
    private KgDestroyEngine kgDestroyEngine;
    private KgGetBridgeBackend kgGetBridgeBackend;
    private KgSupportsConcurrentAnalyze kgSupportsConcurrentAnalyze;
    private bool disposed;

    public bool IsRunning => engine != IntPtr.Zero;
    public string BridgeBackend { get; private set; } = string.Empty;
    public bool SupportsConcurrentAnalyze { get; private set; }

    public void Start(string libraryPath, string configPath, string modelPath, string workingDirectory)
    {
        ThrowIfDisposed();
        Stop();

        LoadBridgeLibrary(libraryPath);
        BridgeBackend = ReadBridgeBackend();
        SupportsConcurrentAnalyze = kgSupportsConcurrentAnalyze != null && kgSupportsConcurrentAnalyze() != 0;

        StringBuilder error = new StringBuilder(ErrorBufferSize);
        int result = kgCreateEngine(configPath, modelPath, workingDirectory, out engine, error, error.Capacity);
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
        int result = kgAnalyze(engine, requestJson, timeoutMs, out IntPtr responsePtr, error, error.Capacity);
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
                kgFreeString(responsePtr);
            }
        }
    }

    public void Stop()
    {
        if (engine != IntPtr.Zero) {
            kgDestroyEngine(engine);
            engine = IntPtr.Zero;
        }

        UnloadBridgeLibrary();
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
            throw new ObjectDisposedException(nameof(Win32NativeKataGoEngine));
        }
    }

    private void LoadBridgeLibrary(string libraryPath)
    {
        if (string.IsNullOrWhiteSpace(libraryPath)) {
            throw new ArgumentException("KataGo bridge library path is empty.", nameof(libraryPath));
        }

        library = LoadLibraryEx(libraryPath, IntPtr.Zero, LoadLibrarySearchDllLoadDir | LoadLibrarySearchDefaultDirs);
        if (library == IntPtr.Zero) {
            int err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"LoadLibraryEx failed for KataGo bridge: {libraryPath}, win32Error: {err}");
        }

        kgCreateEngine = LoadFunction<KgCreateEngine>("kg_create_engine");
        kgAnalyze = LoadFunction<KgAnalyze>("kg_analyze");
        kgFreeString = LoadFunction<KgFreeString>("kg_free_string");
        kgDestroyEngine = LoadFunction<KgDestroyEngine>("kg_destroy_engine");
        kgGetBridgeBackend = LoadFunction<KgGetBridgeBackend>("kg_get_bridge_backend");
        kgSupportsConcurrentAnalyze = LoadFunction<KgSupportsConcurrentAnalyze>("kg_supports_concurrent_analyze", false);
    }

    private T LoadFunction<T>(string functionName, bool isRequired = true) where T : Delegate
    {
        IntPtr proc = GetProcAddress(library, functionName);
        if (proc == IntPtr.Zero) {
            if (!isRequired) {
                return null;
            }

            int err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"KataGo bridge export not found: {functionName}, win32Error: {err}");
        }

        return Marshal.GetDelegateForFunctionPointer<T>(proc);
    }

    private string ReadBridgeBackend()
    {
        IntPtr infoPtr = kgGetBridgeBackend();
        if (infoPtr == IntPtr.Zero) {
            throw new InvalidOperationException("kg_get_bridge_backend returned null.");
        }

        string backend = PtrToUtf8String(infoPtr).Trim();
        if (string.IsNullOrWhiteSpace(backend)) {
            throw new InvalidOperationException("kg_get_bridge_backend returned empty backend name.");
        }

        return backend.ToLowerInvariant();
    }

    private void UnloadBridgeLibrary()
    {
        kgCreateEngine = null;
        kgAnalyze = null;
        kgFreeString = null;
        kgDestroyEngine = null;
        kgGetBridgeBackend = null;
        kgSupportsConcurrentAnalyze = null;
        BridgeBackend = string.Empty;
        SupportsConcurrentAnalyze = false;

        if (library == IntPtr.Zero) {
            return;
        }

        FreeLibrary(library);
        library = IntPtr.Zero;
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

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate int KgCreateEngine(
        string configPath,
        string modelPath,
        string workingDirectory,
        out IntPtr outEngine,
        StringBuilder errorBuffer,
        int errorBufferSize);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate int KgAnalyze(
        IntPtr engine,
        string requestJson,
        int timeoutMs,
        out IntPtr outResponseJson,
        StringBuilder errorBuffer,
        int errorBufferSize);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void KgFreeString(IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void KgDestroyEngine(IntPtr engine);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr KgGetBridgeBackend();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int KgSupportsConcurrentAnalyze();

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, int dwFlags);

    [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);
}
#endif
