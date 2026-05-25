using System;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json.Linq;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
internal sealed class Win32NativeKataGoEngine : IDisposable
{
    private const int ErrorBufferSize = 4096;

    private IntPtr engine;
    private bool disposed;

    public bool IsRunning => engine != IntPtr.Zero;

    public void Start(string libraryDirectory, string configPath, string modelPath, string workingDirectory)
    {
        ThrowIfDisposed();
        Stop();

        if (!SetDllDirectory(libraryDirectory)) {
            throw new InvalidOperationException($"SetDllDirectory failed for KataGo bridge directory: {libraryDirectory}");
        }

        StringBuilder error = new StringBuilder(ErrorBufferSize);
        int result = kg_create_engine(configPath, modelPath, workingDirectory, out engine, error, error.Capacity);
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
        int result = kg_analyze(engine, requestJson, timeoutMs, out IntPtr responsePtr, error, error.Capacity);
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
                kg_free_string(responsePtr);
            }
        }
    }

    public void Stop()
    {
        if (engine == IntPtr.Zero) {
            return;
        }

        kg_destroy_engine(engine);
        engine = IntPtr.Zero;
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

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    [DllImport("katago_bridge", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int kg_create_engine(
        string configPath,
        string modelPath,
        string workingDirectory,
        out IntPtr outEngine,
        StringBuilder errorBuffer,
        int errorBufferSize);

    [DllImport("katago_bridge", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int kg_analyze(
        IntPtr engine,
        string requestJson,
        int timeoutMs,
        out IntPtr outResponseJson,
        StringBuilder errorBuffer,
        int errorBufferSize);

    [DllImport("katago_bridge", CallingConvention = CallingConvention.Cdecl)]
    private static extern void kg_free_string(IntPtr value);

    [DllImport("katago_bridge", CallingConvention = CallingConvention.Cdecl)]
    private static extern void kg_destroy_engine(IntPtr engine);
}
#endif
