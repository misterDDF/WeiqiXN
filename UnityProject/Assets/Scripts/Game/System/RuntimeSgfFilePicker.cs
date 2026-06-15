using System;
using System.Runtime.InteropServices;
using System.Text;

#if UNITY_EDITOR
using UnityEditor;
#endif

public static class RuntimeSgfFilePicker
{
    public static string OpenSgfFilePanel()
    {
#if UNITY_EDITOR
        return EditorUtility.OpenFilePanel("导入棋谱", string.Empty, "sgf");
#elif UNITY_STANDALONE_WIN
        return OpenWindowsFilePanel();
#else
        return string.Empty;
#endif
    }

    public static string SaveSgfFilePanel(string defaultFileName)
    {
#if UNITY_EDITOR
        return EditorUtility.SaveFilePanel("导出棋谱", string.Empty, NormalizeDefaultFileName(defaultFileName), "sgf");
#elif UNITY_STANDALONE_WIN
        return SaveWindowsFilePanel(NormalizeDefaultFileName(defaultFileName));
#else
        return string.Empty;
#endif
    }

    public static bool IsSupported
    {
        get
        {
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
            return true;
#else
            return false;
#endif
        }
    }

    private static string NormalizeDefaultFileName(string defaultFileName)
    {
        if (string.IsNullOrWhiteSpace(defaultFileName)) {
            return "replay.sgf";
        }

        return defaultFileName.EndsWith(".sgf", StringComparison.OrdinalIgnoreCase)
            ? defaultFileName
            : $"{defaultFileName}.sgf";
    }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    private static string OpenWindowsFilePanel()
    {
        OpenFileName openFileName = new OpenFileName
        {
            structSize = Marshal.SizeOf(typeof(OpenFileName)),
            filter = "SGF Files\0*.sgf\0All Files\0*.*\0",
            file = new StringBuilder(1024),
            title = "导入棋谱",
            flags = 0x00080000 | 0x00001000 | 0x00000800,
            defExt = "sgf",
        };
        openFileName.maxFile = openFileName.file.Capacity;

        return GetOpenFileName(openFileName) ? openFileName.file.ToString() : string.Empty;
    }

    private static string SaveWindowsFilePanel(string defaultFileName)
    {
        OpenFileName openFileName = new OpenFileName
        {
            structSize = Marshal.SizeOf(typeof(OpenFileName)),
            filter = "SGF Files\0*.sgf\0All Files\0*.*\0",
            file = new StringBuilder(defaultFileName, 1024),
            title = "导出棋谱",
            flags = 0x00080000 | 0x00000800 | 0x00000002,
            defExt = "sgf",
        };
        openFileName.maxFile = openFileName.file.Capacity;

        return GetSaveFileName(openFileName) ? openFileName.file.ToString() : string.Empty;
    }

    [DllImport("Comdlg32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GetOpenFileName([In, Out] OpenFileName ofn);

    [DllImport("Comdlg32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GetSaveFileName([In, Out] OpenFileName ofn);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class OpenFileName
    {
        public int structSize;
        public IntPtr dlgOwner = IntPtr.Zero;
        public IntPtr instance = IntPtr.Zero;
        public string filter;
        public string customFilter = null;
        public int maxCustFilter = 0;
        public int filterIndex = 1;
        public StringBuilder file;
        public int maxFile;
        public string fileTitle = null;
        public int maxFileTitle = 0;
        public string initialDir = null;
        public string title;
        public int flags;
        public short fileOffset = 0;
        public short fileExtension = 0;
        public string defExt;
        public IntPtr custData = IntPtr.Zero;
        public IntPtr hook = IntPtr.Zero;
        public string templateName = null;
        public IntPtr reservedPtr = IntPtr.Zero;
        public int reservedInt = 0;
        public int flagsEx = 0;
    }
#endif
}
