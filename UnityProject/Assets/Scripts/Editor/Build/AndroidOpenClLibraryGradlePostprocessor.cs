using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor.Android;
using UnityEngine;

public sealed class AndroidOpenClLibraryGradlePostprocessor : IPostGenerateGradleAndroidProject
{
    private const string OpenClLibraryDirectoryName = "OpenCLNativeLibrary.androidlib";
    private const string OpenClLibraryNamespace = "com.weiqixn.openclnativelibrary";
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    public int callbackOrder => 0;

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        string gradleFilePath = ResolveOpenClLibraryGradleFile(path);
        if (string.IsNullOrEmpty(gradleFilePath)) {
            Debug.LogWarning($"OpenCL androidlib Gradle file not found, skip namespace patch. root: {path}");
            return;
        }

        string content = ReadUtf8WithoutLeadingBom(gradleFilePath, out bool removedLeadingBom);
        string patched = PatchGradleFile(content);
        if (!removedLeadingBom && string.Equals(content, patched, StringComparison.Ordinal)) {
            return;
        }

        File.WriteAllText(gradleFilePath, patched, Utf8NoBom);
        Debug.Log($"OpenCL androidlib Gradle namespace patched: {gradleFilePath}");
    }

    private static string ResolveOpenClLibraryGradleFile(string path)
    {
        string[] candidateRoots =
        {
            path,
            Path.Combine(path, "unityLibrary"),
            Directory.GetParent(path)?.FullName ?? string.Empty,
        };

        foreach (string root in candidateRoots) {
            if (string.IsNullOrEmpty(root)) {
                continue;
            }

            string candidate = Path.Combine(root, OpenClLibraryDirectoryName, "build.gradle");
            if (File.Exists(candidate)) {
                return candidate;
            }
        }

        return string.Empty;
    }

    private static string ReadUtf8WithoutLeadingBom(string path, out bool removedLeadingBom)
    {
        byte[] bytes = File.ReadAllBytes(path);
        removedLeadingBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

        string content = removedLeadingBom
            ? Utf8NoBom.GetString(bytes, 3, bytes.Length - 3)
            : Utf8NoBom.GetString(bytes);

        if (content.Length > 0 && content[0] == '\uFEFF') {
            removedLeadingBom = true;
            content = content.TrimStart('\uFEFF');
        }

        return content;
    }

    private static string PatchGradleFile(string content)
    {
        if (string.IsNullOrEmpty(content)) {
            return content;
        }

        string patched = Regex.Replace(
            content,
            @"namespace\s+""[^""]+""",
            $"namespace \"{OpenClLibraryNamespace}\"",
            RegexOptions.Multiline);

        if (!Regex.IsMatch(patched, @"buildFeatures\s*\{[\s\S]*?buildConfig\s+false[\s\S]*?\}", RegexOptions.Multiline)) {
            patched = Regex.Replace(
                patched,
                @"android\s*\{",
                "android {\n    buildFeatures {\n        buildConfig false\n    }",
                RegexOptions.Multiline);
        }

        return patched;
    }
}
