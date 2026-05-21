using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
internal sealed class Win32KataGoProcess : IDisposable
{
    private const int HandleFlagInherit = 0x00000001;
    private const int StartfUseStdHandles = 0x00000100;
    private const int CreateNoWindow = 0x08000000;
    private const uint WaitTimeout = 0x00000102;
    private const uint StillActive = 259;

    private readonly ConcurrentQueue<string> stdoutLines = new ConcurrentQueue<string>();
    private readonly AutoResetEvent stdoutLineReady = new AutoResetEvent(false);
    private readonly object stateLock = new object();

    private IntPtr processHandle;
    private IntPtr threadHandle;
    private IntPtr stdinWriteHandle;
    private IntPtr stdoutReadHandle;
    private IntPtr stderrReadHandle;
    private Task stdoutReaderTask;
    private Task stderrReaderTask;
    private bool disposed;

    public bool IsRunning
    {
        get
        {
            lock (stateLock) {
                return IsProcessRunningNoLock();
            }
        }
    }

    public void Start(string exePath, string arguments, string workingDirectory)
    {
        ThrowIfDisposed();
        Stop();

        IntPtr stdinRead = IntPtr.Zero;
        IntPtr stdoutWrite = IntPtr.Zero;
        IntPtr stderrWrite = IntPtr.Zero;

        try {
            CreatePipePair(out stdinRead, out stdinWriteHandle, parentKeepsReadHandle: false);
            CreatePipePair(out stdoutReadHandle, out stdoutWrite, parentKeepsReadHandle: true);
            CreatePipePair(out stderrReadHandle, out stderrWrite, parentKeepsReadHandle: true);

            STARTUPINFO startupInfo = new STARTUPINFO
            {
                cb = Marshal.SizeOf<STARTUPINFO>(),
                dwFlags = StartfUseStdHandles,
                hStdInput = stdinRead,
                hStdOutput = stdoutWrite,
                hStdError = stderrWrite,
            };

            string commandLineText = $"{QuoteArgument(exePath)} {arguments}";
            StringBuilder commandLine = new StringBuilder(commandLineText);

            bool success = CreateProcessW(
                exePath,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                true,
                CreateNoWindow,
                IntPtr.Zero,
                workingDirectory,
                ref startupInfo,
                out PROCESS_INFORMATION processInformation);

            if (!success) {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"CreateProcessW failed: {commandLineText}");
            }

            processHandle = processInformation.hProcess;
            threadHandle = processInformation.hThread;

            CloseHandleIfNeeded(ref stdinRead);
            CloseHandleIfNeeded(ref stdoutWrite);
            CloseHandleIfNeeded(ref stderrWrite);

            IntPtr stdoutRead = stdoutReadHandle;
            IntPtr stderrRead = stderrReadHandle;
            stdoutReaderTask = Task.Run(() => ReadPipeLines(stdoutRead, line => EnqueueStdoutLine(line)));
            stderrReaderTask = Task.Run(() => ReadPipeLines(stderrRead, line => Debug.Log($"[KataGo] {line}")));
        }
        catch {
            CloseHandleIfNeeded(ref stdinRead);
            CloseHandleIfNeeded(ref stdoutWrite);
            CloseHandleIfNeeded(ref stderrWrite);
            Stop();
            throw;
        }
    }

    public void WriteLine(string line)
    {
        ThrowIfDisposed();
        byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
        if (!WriteFile(stdinWriteHandle, bytes, bytes.Length, out int bytesWritten, IntPtr.Zero) || bytesWritten != bytes.Length) {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "WriteFile failed for KataGo stdin.");
        }
    }

    public string ReadOutputLineBefore(DateTime deadline, CancellationToken cancellationToken, string operationName, int timeoutMs)
    {
        ThrowIfDisposed();
        while (DateTime.UtcNow < deadline) {
            cancellationToken.ThrowIfCancellationRequested();

            if (stdoutLines.TryDequeue(out string line)) {
                return line;
            }

            if (!IsRunning && stdoutLines.IsEmpty) {
                throw new InvalidOperationException($"{operationName} failed because KataGo exited before returning result.");
            }

            TimeSpan remaining = deadline - DateTime.UtcNow;
            int waitMs = Math.Min(100, Math.Max(1, (int)remaining.TotalMilliseconds));
            stdoutLineReady.WaitOne(waitMs);
        }

        Stop();
        throw new TimeoutException($"{operationName} timed out after {timeoutMs}ms. KataGo process was stopped and will be restarted before the next analyze request.");
    }

    public bool TryReadOutputLineBefore(
        DateTime deadline,
        CancellationToken cancellationToken,
        string operationName,
        int timeoutMs,
        int maxWaitMs,
        out string line)
    {
        ThrowIfDisposed();
        line = null;

        DateTime waitUntil = DateTime.UtcNow.AddMilliseconds(Math.Max(1, maxWaitMs));
        if (waitUntil > deadline) {
            waitUntil = deadline;
        }

        while (DateTime.UtcNow < waitUntil) {
            cancellationToken.ThrowIfCancellationRequested();

            if (stdoutLines.TryDequeue(out line)) {
                return true;
            }

            if (!IsRunning && stdoutLines.IsEmpty) {
                throw new InvalidOperationException($"{operationName} failed because KataGo exited before returning result.");
            }

            TimeSpan remainingToDeadline = deadline - DateTime.UtcNow;
            TimeSpan remainingToWaitUntil = waitUntil - DateTime.UtcNow;
            int waitMs = Math.Min(100, Math.Max(1, (int)Math.Min(remainingToDeadline.TotalMilliseconds, remainingToWaitUntil.TotalMilliseconds)));
            stdoutLineReady.WaitOne(waitMs);
        }

        if (DateTime.UtcNow >= deadline) {
            Stop();
            throw new TimeoutException($"{operationName} timed out after {timeoutMs}ms. KataGo process was stopped and will be restarted before the next analyze request.");
        }

        return false;
    }

    public void Stop()
    {
        lock (stateLock) {
            if (processHandle == IntPtr.Zero
                && threadHandle == IntPtr.Zero
                && stdinWriteHandle == IntPtr.Zero
                && stdoutReadHandle == IntPtr.Zero
                && stderrReadHandle == IntPtr.Zero) {
                return;
            }

            CloseHandleIfNeeded(ref stdinWriteHandle);

            if (processHandle != IntPtr.Zero && IsProcessRunningNoLock()) {
                uint waitResult = WaitForSingleObject(processHandle, 2000);
                if (waitResult == WaitTimeout) {
                    TerminateProcess(processHandle, 1);
                    WaitForSingleObject(processHandle, 1000);
                }
            }

            CloseHandleIfNeeded(ref stdoutReadHandle);
            CloseHandleIfNeeded(ref stderrReadHandle);
            CloseHandleIfNeeded(ref threadHandle);
            CloseHandleIfNeeded(ref processHandle);
        }

        WaitReaderTask(stdoutReaderTask);
        WaitReaderTask(stderrReaderTask);
        stdoutReaderTask = null;
        stderrReaderTask = null;

        while (stdoutLines.TryDequeue(out _)) {
        }
    }

    public void Dispose()
    {
        if (disposed) {
            return;
        }

        Stop();
        stdoutLineReady.Dispose();
        disposed = true;
    }

    private static void CreatePipePair(out IntPtr readHandle, out IntPtr writeHandle, bool parentKeepsReadHandle)
    {
        SECURITY_ATTRIBUTES securityAttributes = new SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
            bInheritHandle = true,
        };

        if (!CreatePipe(out readHandle, out writeHandle, ref securityAttributes, 0)) {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe failed.");
        }

        IntPtr parentHandle = parentKeepsReadHandle ? readHandle : writeHandle;
        if (!SetHandleInformation(parentHandle, HandleFlagInherit, 0)) {
            int error = Marshal.GetLastWin32Error();
            CloseHandleIfNeeded(ref readHandle);
            CloseHandleIfNeeded(ref writeHandle);
            throw new Win32Exception(error, "SetHandleInformation failed.");
        }
    }

    private void EnqueueStdoutLine(string line)
    {
        stdoutLines.Enqueue(line);
        try {
            stdoutLineReady.Set();
        }
        catch (ObjectDisposedException) {
        }
    }

    private static void ReadPipeLines(IntPtr readHandle, Action<string> onLine)
    {
        byte[] bytes = new byte[4096];
        char[] chars = new char[4096];
        Decoder decoder = Encoding.UTF8.GetDecoder();
        StringBuilder pendingLine = new StringBuilder();

        while (readHandle != IntPtr.Zero) {
            bool success = ReadFile(readHandle, bytes, bytes.Length, out int bytesRead, IntPtr.Zero);
            if (!success || bytesRead <= 0) {
                break;
            }

            int charCount = decoder.GetChars(bytes, 0, bytesRead, chars, 0, false);
            pendingLine.Append(chars, 0, charCount);
            FlushCompleteLines(pendingLine, onLine);
        }

        if (pendingLine.Length > 0) {
            onLine(pendingLine.ToString().TrimEnd('\r'));
        }
    }

    private static void FlushCompleteLines(StringBuilder pendingLine, Action<string> onLine)
    {
        while (true) {
            string text = pendingLine.ToString();
            int newlineIndex = text.IndexOf('\n');
            if (newlineIndex < 0) {
                return;
            }

            string line = text.Substring(0, newlineIndex).TrimEnd('\r');
            pendingLine.Remove(0, newlineIndex + 1);
            onLine(line);
        }
    }

    private bool IsProcessRunningNoLock()
    {
        if (processHandle == IntPtr.Zero) {
            return false;
        }

        if (!GetExitCodeProcess(processHandle, out uint exitCode)) {
            return false;
        }

        return exitCode == StillActive;
    }

    private static void WaitReaderTask(Task task)
    {
        try {
            task?.Wait(200);
        }
        catch (AggregateException) {
        }
    }

    private void ThrowIfDisposed()
    {
        if (disposed) {
            throw new ObjectDisposedException(nameof(Win32KataGoProcess));
        }
    }

    private static string QuoteArgument(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static void CloseHandleIfNeeded(ref IntPtr handle)
    {
        if (handle == IntPtr.Zero) {
            return;
        }

        IntPtr handleToClose = handle;
        handle = IntPtr.Zero;
        CloseHandle(handleToClose);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out IntPtr hReadPipe, out IntPtr hWritePipe, ref SECURITY_ATTRIBUTES lpPipeAttributes, int nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetHandleInformation(IntPtr hObject, int dwMask, int dwFlags);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(
        string lpApplicationName,
        StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        int dwCreationFlags,
        IntPtr lpEnvironment,
        string lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(IntPtr hFile, byte[] lpBuffer, int nNumberOfBytesToRead, out int lpNumberOfBytesRead, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(IntPtr hFile, byte[] lpBuffer, int nNumberOfBytesToWrite, out int lpNumberOfBytesWritten, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, int dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }
}
#endif
