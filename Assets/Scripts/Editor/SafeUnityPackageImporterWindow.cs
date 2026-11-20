using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

public sealed class SafePackageImportReport
{
    public int ExtractedAssetCount { get; set; }
    public int ExtractedMetaCount { get; set; }
    public int SkippedEdm4uCount { get; set; }
    public int SkippedInvalidPathCount { get; set; }
    public bool ContainsGooglePlayGames { get; set; }
    public bool EnabledPlayGamesDefine { get; set; }
    public List<string> ExtractedPaths { get; } = new List<string>();
    public List<string> TopLevelFolders { get; } = new List<string>();
}

/// <summary>
/// Pure C# <c>.unitypackage</c> (.tar.gz) extractor that bypasses Unity 6's broken
/// <c>PackageImportTreeView</c> IMGUI bug (<c>NullReferenceException</c> on <c>./</c> entries)
/// and protects the project's existing External Dependency Manager (<c>EDM4U v1.2.189</c>).
/// </summary>
public static class SafeUnityPackageImporter
{
    private sealed class PackageEntryData
    {
        public string RawPathname;
        public byte[] AssetBytes;
        public byte[] MetaBytes;
    }

    public static SafePackageImportReport InspectPackage(
        string unityPackagePath,
        bool skipExternalDependencyManager = true)
    {
        return ProcessPackageArchive(
            unityPackagePath,
            projectRootPath: null,
            writeToDisk: false,
            skipExternalDependencyManager: skipExternalDependencyManager);
    }

    public static SafePackageImportReport ExtractPackageToProjectRoot(
        string unityPackagePath,
        string projectRootPath,
        bool skipExternalDependencyManager = true)
    {
        if (string.IsNullOrWhiteSpace(projectRootPath))
            throw new ArgumentException("Project root path cannot be empty.", nameof(projectRootPath));

        return ProcessPackageArchive(
            unityPackagePath,
            projectRootPath: Path.GetFullPath(projectRootPath),
            writeToDisk: true,
            skipExternalDependencyManager: skipExternalDependencyManager);
    }

    public static SafePackageImportReport ImportIntoCurrentProject(
        string unityPackagePath,
        bool skipExternalDependencyManager = true,
        bool autoEnablePlayGamesSymbol = true)
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
        SafePackageImportReport report = ExtractPackageToProjectRoot(
            unityPackagePath,
            projectRoot,
            skipExternalDependencyManager);

        if (autoEnablePlayGamesSymbol && report.ContainsGooglePlayGames)
        {
            report.EnabledPlayGamesDefine = EnsureAndroidDefineSymbol("GOOGLE_PLAY_GAMES");
        }

        AssetDatabase.Refresh();
        return report;
    }

    public static bool EnsureAndroidDefineSymbol(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return false;

        string currentDefines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android);
        string[] parts = string.IsNullOrEmpty(currentDefines)
            ? Array.Empty<string>()
            : currentDefines.Split(';');

        var symbols = new List<string>(parts.Length + 1);
        for (int i = 0; i < parts.Length; i++)
        {
            string trimmed = parts[i].Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;
            if (string.Equals(trimmed, symbol, StringComparison.Ordinal))
                return false;
            symbols.Add(trimmed);
        }

        symbols.Add(symbol);
        PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Android, string.Join(";", symbols));
        return true;
    }

    private static SafePackageImportReport ProcessPackageArchive(
        string unityPackagePath,
        string projectRootPath,
        bool writeToDisk,
        bool skipExternalDependencyManager)
    {
        if (string.IsNullOrWhiteSpace(unityPackagePath) || !File.Exists(unityPackagePath))
            throw new FileNotFoundException("UnityPackage file not found.", unityPackagePath);

        Dictionary<string, PackageEntryData> groupedEntries = ReadTarGzEntries(unityPackagePath);
        var report = new SafePackageImportReport();
        var topFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (PackageEntryData entry in groupedEntries.Values)
        {
            if (!TryNormalizeArchiveAssetPath(entry.RawPathname, out string normalizedPath))
            {
                report.SkippedInvalidPathCount++;
                continue;
            }

            if (skipExternalDependencyManager && IsExternalDependencyManagerPath(normalizedPath))
            {
                report.SkippedEdm4uCount++;
                continue;
            }

            if (normalizedPath.IndexOf("GooglePlayGames", StringComparison.OrdinalIgnoreCase) >= 0)
                report.ContainsGooglePlayGames = true;

            TrackTopLevelFolder(normalizedPath, topFolders);
            report.ExtractedPaths.Add(normalizedPath);

            if (entry.AssetBytes != null || entry.MetaBytes == null)
                report.ExtractedAssetCount++;
            if (entry.MetaBytes != null)
                report.ExtractedMetaCount++;

            if (!writeToDisk)
                continue;

            string fullTargetPath = ResolveSafeFullPath(projectRootPath, normalizedPath);
            if (fullTargetPath == null)
            {
                report.SkippedInvalidPathCount++;
                continue;
            }

            if (entry.AssetBytes != null)
            {
                string parentDir = Path.GetDirectoryName(fullTargetPath);
                if (!string.IsNullOrEmpty(parentDir))
                    Directory.CreateDirectory(parentDir);

                File.WriteAllBytes(fullTargetPath, entry.AssetBytes);
            }
            else if (entry.MetaBytes != null)
            {
                Directory.CreateDirectory(fullTargetPath);
            }

            if (entry.MetaBytes != null)
            {
                string metaPath = fullTargetPath + ".meta";
                string metaParent = Path.GetDirectoryName(metaPath);
                if (!string.IsNullOrEmpty(metaParent))
                    Directory.CreateDirectory(metaParent);

                File.WriteAllBytes(metaPath, entry.MetaBytes);
            }
        }

        report.TopLevelFolders.AddRange(topFolders);
        report.TopLevelFolders.Sort(StringComparer.OrdinalIgnoreCase);
        return report;
    }

    internal static bool TryNormalizeArchiveAssetPath(string rawPathname, out string normalizedPath)
    {
        normalizedPath = null;
        if (string.IsNullOrWhiteSpace(rawPathname))
            return false;

        string firstLine = rawPathname.Replace("\r", string.Empty).Split('\n')[0].Trim();
        if (string.IsNullOrEmpty(firstLine))
            return false;

        firstLine = firstLine.Replace('\\', '/');
        while (firstLine.StartsWith("./", StringComparison.Ordinal))
            firstLine = firstLine.Substring(2);
        firstLine = firstLine.Trim('/');

        if (string.IsNullOrEmpty(firstLine) || firstLine.IndexOf(':') >= 0)
            return false;

        string[] segments = firstLine.Split('/');
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i] == ".." || segments[i] == "." || string.IsNullOrWhiteSpace(segments[i]))
                return false;
        }

        bool validRoot = firstLine.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
            || firstLine.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase);
        if (!validRoot)
            return false;

        normalizedPath = firstLine;
        return true;
    }

    private static bool IsExternalDependencyManagerPath(string normalizedPath)
    {
        return normalizedPath.Equals("Assets/ExternalDependencyManager", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith("Assets/ExternalDependencyManager/", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Equals("Assets/PlayServicesResolver", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith("Assets/PlayServicesResolver/", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveSafeFullPath(string projectRootFullPath, string normalizedRelativePath)
    {
        string combined = Path.GetFullPath(Path.Combine(projectRootFullPath, normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        string rootWithSep = projectRootFullPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? projectRootFullPath
            : projectRootFullPath + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
            return null;

        return combined;
    }

    private static void TrackTopLevelFolder(string normalizedPath, HashSet<string> topFolders)
    {
        string[] segs = normalizedPath.Split('/');
        if (segs.Length >= 2)
            topFolders.Add(segs[0] + "/" + segs[1]);
        else if (segs.Length == 1)
            topFolders.Add(segs[0]);
    }

    private static Dictionary<string, PackageEntryData> ReadTarGzEntries(string filePath)
    {
        var entries = new Dictionary<string, PackageEntryData>(StringComparer.Ordinal);
        using FileStream fs = File.OpenRead(filePath);
        using var gzip = new GZipStream(fs, CompressionMode.Decompress);

        byte[] header = new byte[512];
        string gnuLongName = null;

        while (ReadExactOrZero(gzip, header, 512) == 512)
        {
            if (IsAllZeroBlock(header))
                break;

            char typeFlag = (char)header[156];
            long fileSize = ParseOctal(header, 124, 12);
            string entryPath = !string.IsNullOrEmpty(gnuLongName)
                ? gnuLongName
                : ExtractTarEntryName(header);
            gnuLongName = null;

            byte[] payload = ReadTarPayload(gzip, fileSize);

            if (typeFlag == 'L')
            {
                gnuLongName = Encoding.UTF8.GetString(payload).TrimEnd('\0', '\n', '\r');
                continue;
            }

            if (typeFlag == '5' || string.IsNullOrEmpty(entryPath))
                continue;

            if (typeFlag != '0' && typeFlag != '\0')
                continue;

            string cleaned = entryPath.Replace('\\', '/');
            while (cleaned.StartsWith("./", StringComparison.Ordinal))
                cleaned = cleaned.Substring(2);
            cleaned = cleaned.Trim('/');

            int slashIdx = cleaned.IndexOf('/');
            if (slashIdx <= 0 || slashIdx >= cleaned.Length - 1)
                continue;

            string assetGuidKey = cleaned.Substring(0, slashIdx);
            string fileKind = cleaned.Substring(slashIdx + 1);

            if (!entries.TryGetValue(assetGuidKey, out PackageEntryData data))
            {
                data = new PackageEntryData();
                entries[assetGuidKey] = data;
            }

            if (string.Equals(fileKind, "pathname", StringComparison.Ordinal))
                data.RawPathname = Encoding.UTF8.GetString(payload).TrimEnd('\0');
            else if (string.Equals(fileKind, "asset", StringComparison.Ordinal))
                data.AssetBytes = payload;
            else if (string.Equals(fileKind, "asset.meta", StringComparison.Ordinal))
                data.MetaBytes = payload;
        }

        return entries;
    }

    private static byte[] ReadTarPayload(Stream stream, long size)
    {
        if (size <= 0)
            return Array.Empty<byte>();

        byte[] payload = new byte[size];
        int read = ReadExactOrZero(stream, payload, (int)size);
        if (read < size)
            throw new EndOfStreamException("Unexpected end of TAR stream while reading entry payload.");

        int remainder = (int)(size % 512);
        if (remainder > 0)
        {
            int padding = 512 - remainder;
            byte[] padBuf = new byte[padding];
            ReadExactOrZero(stream, padBuf, padding);
        }

        return payload;
    }

    private static int ReadExactOrZero(Stream stream, byte[] buffer, int count)
    {
        int offset = 0;
        while (offset < count)
        {
            int n = stream.Read(buffer, offset, count - offset);
            if (n <= 0)
                break;
            offset += n;
        }
        return offset;
    }

    private static bool IsAllZeroBlock(byte[] block)
    {
        for (int i = 0; i < block.Length; i++)
        {
            if (block[i] != 0)
                return false;
        }
        return true;
    }

    private static string ExtractTarEntryName(byte[] header)
    {
        string name = ReadNullTerminatedUtf8(header, 0, 100);
        string prefix = ReadNullTerminatedUtf8(header, 345, 155);
        return string.IsNullOrEmpty(prefix) ? name : (prefix.TrimEnd('/') + "/" + name.TrimStart('/'));
    }

    private static string ReadNullTerminatedUtf8(byte[] buffer, int offset, int length)
    {
        int end = offset;
        int limit = Mathf.Min(buffer.Length, offset + length);
        while (end < limit && buffer[end] != 0)
            end++;

        return end == offset ? string.Empty : Encoding.UTF8.GetString(buffer, offset, end - offset);
    }

    private static long ParseOctal(byte[] buffer, int offset, int length)
    {
        long value = 0;
        int limit = Mathf.Min(buffer.Length, offset + length);
        for (int i = offset; i < limit; i++)
        {
            byte b = buffer[i];
            if (b == 0 || b == ' ')
            {
                if (value > 0) break;
                continue;
            }
            if (b >= '0' && b <= '7')
                value = (value << 3) + (b - '0');
        }
        return value;
    }
}

/// <summary>
/// Editor window for safely importing <c>.unitypackage</c> files (such as Google Play Games Services)
/// without triggering the Unity 6 <c>PackageImportTreeView</c> UI crash or overwriting EDM4U.
/// </summary>
public class SafeUnityPackageImporterWindow : EditorWindow
{
    private string packagePath = string.Empty;
    private bool skipExternalDependencyManager = true;
    private bool autoEnablePlayGamesSymbol = true;
    private SafePackageImportReport previewReport;
    private string statusMessage = string.Empty;
    private Vector2 scrollPos;

    [MenuItem("Tools/Gravita/Safe Import UnityPackage (GPGS / Bypass TreeView Bug)")]
    public static void OpenWindow()
    {
        var window = GetWindow<SafeUnityPackageImporterWindow>("Safe Package Importer");
        window.minSize = new Vector2(520f, 360f);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Safe .unitypackage Importer (Unity 6 Workaround)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Tránh lỗi NullReferenceException ở cửa sổ PackageImportTreeView của Unity 6, " +
            "đồng thời giữ nguyên External Dependency Manager (EDM4U v1.2.189) đang dùng cho Firebase/AdMob.",
            MessageType.Info);

        EditorGUILayout.Space(6f);

        using (new EditorGUILayout.HorizontalScope())
        {
            packagePath = EditorGUILayout.TextField("Package Path", packagePath);
            if (GUILayout.Button("Browse...", GUILayout.Width(90f)))
            {
                string selected = EditorUtility.OpenFilePanel("Select .unitypackage", "", "unitypackage");
                if (!string.IsNullOrEmpty(selected))
                {
                    packagePath = selected;
                    previewReport = null;
                    statusMessage = string.Empty;
                }
            }
        }

        skipExternalDependencyManager = EditorGUILayout.ToggleLeft(
            "Skip bundled ExternalDependencyManager / PlayServicesResolver (Keep EDM4U v1.2.189)",
            skipExternalDependencyManager);
        autoEnablePlayGamesSymbol = EditorGUILayout.ToggleLeft(
            "Auto-enable ANDROID Scripting Define 'GOOGLE_PLAY_GAMES' when GPGS files are detected",
            autoEnablePlayGamesSymbol);

        EditorGUILayout.Space(8f);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Preview Contents", GUILayout.Height(28f)))
                RunPreview();

            if (GUILayout.Button("Import Safely Now", GUILayout.Height(28f)))
                RunImport();
        }

        if (!string.IsNullOrEmpty(statusMessage))
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.HelpBox(statusMessage, MessageType.None);
        }

        if (previewReport != null)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(
                $"Top-Level Folders ({previewReport.TopLevelFolders.Count}) | " +
                $"Assets: {previewReport.ExtractedAssetCount} | Skipped EDM4U: {previewReport.SkippedEdm4uCount}",
                EditorStyles.miniBoldLabel);

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.ExpandHeight(true));
            for (int i = 0; i < previewReport.TopLevelFolders.Count; i++)
                EditorGUILayout.LabelField("• " + previewReport.TopLevelFolders[i]);
            EditorGUILayout.EndScrollView();
        }
    }

    private void RunPreview()
    {
        try
        {
            previewReport = SafeUnityPackageImporter.InspectPackage(packagePath, skipExternalDependencyManager);
            statusMessage = $"Preview OK — Assets: {previewReport.ExtractedAssetCount}, " +
                            $"Skipped EDM4U: {previewReport.SkippedEdm4uCount}, " +
                            $"GPGS detected: {previewReport.ContainsGooglePlayGames}.";
        }
        catch (Exception ex)
        {
            statusMessage = "Preview failed: " + ex.Message;
        }
    }

    private void RunImport()
    {
        try
        {
            SafePackageImportReport report = SafeUnityPackageImporter.ImportIntoCurrentProject(
                packagePath,
                skipExternalDependencyManager,
                autoEnablePlayGamesSymbol);
            previewReport = report;
            statusMessage = $"Imported {report.ExtractedAssetCount} assets ({report.ExtractedMetaCount} metas). " +
                            $"Skipped EDM4U: {report.SkippedEdm4uCount}. " +
                            (report.EnabledPlayGamesDefine ? "Added GOOGLE_PLAY_GAMES Android define." : string.Empty);
            Debug.Log("[SafeUnityPackageImporter] " + statusMessage);
        }
        catch (Exception ex)
        {
            statusMessage = "Import failed: " + ex.Message;
            Debug.LogError("[SafeUnityPackageImporter] " + ex);
        }
    }
}
