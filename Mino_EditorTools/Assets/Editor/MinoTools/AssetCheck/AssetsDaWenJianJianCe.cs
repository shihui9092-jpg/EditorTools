using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 检测 Assets 目录下是否存在单个文件超过 60MB；若存在则在 Console 强制报错。
/// 编辑器加载完成后自动全量扫描；资源导入/移动时增量检测。
/// </summary>
[InitializeOnLoad]
public static class AssetsDaWenJianJianCe
{
    private const long MaxFileSizeBytes = 60L * 1024 * 1024;
    private const float MaxFileSizeMegabytes = 60f;
    private const string MenuPath = "Tools/MinoTools/资源检测/Assets 单文件 60MB 检测";

    static AssetsDaWenJianJianCe()
    {
        EditorApplication.delayCall += RunDeferredFullScan;
    }

    [MenuItem(MenuPath)]
    public static void ScanFromMenu()
    {
        int errorCount = ScanAssetsFolderAndLogErrors();
        if (errorCount == 0)
        {
            Debug.Log($"[Assets 大文件检测] 未发现超过 {MaxFileSizeMegabytes:F0} MB 的单文件。");
        }
    }

    /// <summary>
    /// 全量扫描 Assets 目录，返回超标文件数量。
    /// </summary>
    public static int ScanAssetsFolderAndLogErrors()
    {
        string assetsRoot = Application.dataPath;
        if (!Directory.Exists(assetsRoot))
        {
            return 0;
        }

        string[] allFiles = Directory.GetFiles(assetsRoot, "*", SearchOption.AllDirectories);
        var oversizedPaths = new List<string>();

        for (int i = 0; i < allFiles.Length; i++)
        {
            string fullPath = allFiles[i];
            if (!TryGetOversizedAssetPath(fullPath, out string assetPath, out long fileBytes))
            {
                continue;
            }

            oversizedPaths.Add(assetPath);
            LogOversizedFileError(assetPath, fileBytes);
        }

        if (oversizedPaths.Count > 0)
        {
            Debug.LogError(
                $"[Assets 大文件检测] 共发现 {oversizedPaths.Count} 个文件超过 {MaxFileSizeMegabytes:F0} MB，请压缩或移出项目。",
                AssetDatabase.LoadAssetAtPath<Object>("Assets"));
        }

        return oversizedPaths.Count;
    }

    private static void RunDeferredFullScan()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += RunDeferredFullScan;
            return;
        }

        ScanAssetsFolderAndLogErrors();
    }

    private static bool TryGetOversizedAssetPath(string fullPath, out string assetPath, out long fileBytes)
    {
        assetPath = null;
        fileBytes = 0;

        if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
        {
            return false;
        }

        FileInfo info = new FileInfo(fullPath);
        if (info.Length <= MaxFileSizeBytes)
        {
            return false;
        }

        fileBytes = info.Length;
        assetPath = FullPathToAssetPath(fullPath);
        return !string.IsNullOrEmpty(assetPath);
    }

    private static void LogOversizedFileError(string assetPath, long fileBytes)
    {
        Object context = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
        Debug.LogError(
            $"[Assets 大文件检测] 单文件超过 {MaxFileSizeMegabytes:F0} MB：{assetPath}（{FormatFileSize(fileBytes)}）",
            context);
    }

    private static string FullPathToAssetPath(string fullPath)
    {
        string assetsRoot = Path.GetFullPath(Application.dataPath);
        string normalizedFullPath = Path.GetFullPath(fullPath);

        if (!normalizedFullPath.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string relative = normalizedFullPath.Substring(assetsRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return "Assets/" + relative.Replace('\\', '/');
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        double kilobytes = bytes / 1024d;
        if (kilobytes < 1024)
        {
            return $"{kilobytes:F2} KB";
        }

        double megabytes = kilobytes / 1024d;
        if (megabytes < 1024)
        {
            return $"{megabytes:F2} MB";
        }

        double gigabytes = megabytes / 1024d;
        return $"{gigabytes:F2} GB";
    }

    private class AssetsDaWenJianJianCePostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            CheckAssetPaths(importedAssets);
            CheckAssetPaths(movedAssets);
        }

        private static void CheckAssetPaths(string[] assetPaths)
        {
            if (assetPaths == null)
            {
                return;
            }

            for (int i = 0; i < assetPaths.Length; i++)
            {
                string assetPath = assetPaths[i];
                if (string.IsNullOrEmpty(assetPath) || AssetDatabase.IsValidFolder(assetPath))
                {
                    continue;
                }

                string fullPath = AssetPathToFullPath(assetPath);
                if (!TryGetOversizedAssetPath(fullPath, out string oversizedPath, out long fileBytes))
                {
                    continue;
                }

                LogOversizedFileError(oversizedPath, fileBytes);
            }
        }

        private static string AssetPathToFullPath(string assetPath)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string relative = assetPath.Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.Combine(projectRoot, relative));
        }
    }
}
