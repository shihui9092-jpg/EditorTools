using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 检测指定工程文件夹内，哪些资源未被「当前已打开场景」引用。
/// 基于场景序列化引用与依赖链（含材质/贴图等间接依赖），不包含运行时动态加载。
/// </summary>
public class SceneWeiYongAssetCheckWin : EditorWindow
{
    [Serializable]
    private class AssetRow
    {
        public string assetPath;
        public string extension;
        public string assetType;
        public long diskBytes;
        public bool isUnused;
    }

    [Serializable]
    private class MoveRecord
    {
        public string originalPath;
        public string movedPath;
    }

    private const string UnusedRootFolderPath = "Assets/Unused";

    [SerializeField] private DefaultAsset targetFolder;
    [SerializeField] private bool includeInactive = true;
    [SerializeField] private bool includeIndirectDependencies = true;
    [SerializeField] private bool scanSubFolders = true;
    [SerializeField] private bool ignoreScriptAssets = true;
    [SerializeField] private bool onlyShowUnused = true;
    [SerializeField] private string extensionFilter = string.Empty;
    [SerializeField] private List<MoveRecord> _lastMoveRecords = new List<MoveRecord>();

    private readonly List<AssetRow> _usedRows = new List<AssetRow>();
    private readonly List<AssetRow> _unusedRows = new List<AssetRow>();
    private readonly HashSet<string> _extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _moveFailures = new List<string>();
    private Vector2 _scroll;
    private string _lastSceneName = string.Empty;
    private string _lastFolderPath = string.Empty;
    private int _folderAssetCount;

    [MenuItem("Tools/MinoTools/场景资源检测/文件夹资源未使用检测")]
    public static void Open()
    {
        SceneWeiYongAssetCheckWin window = GetWindow<SceneWeiYongAssetCheckWin>("场景未引用资源");
        window.minSize = new Vector2(820f, 480f);
    }

    private void OnGUI()
    {
        DrawHeader();
        EditorGUILayout.Space(6);
        DrawConfig();
        EditorGUILayout.Space(8);
        DrawButtons();
        EditorGUILayout.Space(8);
        DrawSummary();
        EditorGUILayout.Space(6);
        DrawList();
    }

    private void DrawHeader()
    {
        EditorGUILayout.HelpBox(
            "对比「目标文件夹」与「当前打开场景」的资源引用关系，列出文件夹内未被场景使用的资源。\n" +
            "检测范围：场景文件依赖 + 场景层级内序列化引用 +（可选）间接依赖链。\n" +
            "不包含：Resources.Load、Addressables、代码动态加载等运行时引用。\n" +
            "建议先保存场景后再扫描，未保存的修改可能未被场景文件依赖捕获。",
            MessageType.Info);
    }

    private void DrawConfig()
    {
        targetFolder = (DefaultAsset)EditorGUILayout.ObjectField("目标文件夹", targetFolder, typeof(DefaultAsset), false);
        includeInactive = EditorGUILayout.Toggle("包含非激活节点", includeInactive);
        includeIndirectDependencies = EditorGUILayout.Toggle("包含间接依赖（材质→贴图等）", includeIndirectDependencies);
        scanSubFolders = EditorGUILayout.Toggle("扫描子文件夹", scanSubFolders);
        ignoreScriptAssets = EditorGUILayout.Toggle("忽略 .cs 脚本", ignoreScriptAssets);
        onlyShowUnused = EditorGUILayout.Toggle("仅显示未使用列表", onlyShowUnused);
        extensionFilter = EditorGUILayout.TextField("格式过滤（可选，逗号分隔）", extensionFilter);
        EditorGUILayout.HelpBox("格式过滤示例：.prefab,.mat,.png ；留空表示不过滤。", MessageType.None);
    }

    private void DrawButtons()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("开始扫描", GUILayout.Height(30)))
            {
                RunScan();
            }

            GUI.enabled = _usedRows.Count > 0 || _unusedRows.Count > 0;
            if (GUILayout.Button("清空结果", GUILayout.Height(30)))
            {
                ClearRows();
            }

            if (GUILayout.Button("导出 Markdown 报告", GUILayout.Height(30)))
            {
                ExportMarkdownReport();
            }

            GUI.enabled = _unusedRows.Count > 0;
            if (GUILayout.Button("转移未使用资源", GUILayout.Height(30)))
            {
                MoveUnusedAssetsToNewFolder();
            }

            GUI.enabled = _lastMoveRecords.Count > 0;
            if (GUILayout.Button("一键恢复原路径", GUILayout.Height(30)))
            {
                RestoreMovedAssetsToOriginalPaths();
            }

            GUI.enabled = true;
        }
    }

    private void DrawSummary()
    {
        if (_folderAssetCount == 0 && _unusedRows.Count == 0 && _usedRows.Count == 0)
        {
            return;
        }

        EditorGUILayout.LabelField("扫描汇总", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"当前场景：{_lastSceneName}");
        EditorGUILayout.LabelField($"目标文件夹：{_lastFolderPath}");
        EditorGUILayout.LabelField($"文件夹资源总数：{_folderAssetCount}");
        EditorGUILayout.LabelField($"场景已引用：{_usedRows.Count}");
        EditorGUILayout.LabelField($"场景未引用：{_unusedRows.Count}");
    }

    private void DrawList()
    {
        List<AssetRow> source = onlyShowUnused ? _unusedRows : new List<AssetRow>();
        if (!onlyShowUnused)
        {
            source.AddRange(_usedRows);
            source.AddRange(_unusedRows);
            source.Sort((a, b) => string.Compare(a.assetPath, b.assetPath, StringComparison.OrdinalIgnoreCase));
        }

        if (source.Count == 0)
        {
            return;
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        for (int i = 0; i < source.Count; i++)
        {
            AssetRow row = source[i];
            using (new EditorGUILayout.VerticalScope("box"))
            {
                string tag = row.isUnused ? "[未使用]" : "[已使用]";
                EditorGUILayout.LabelField($"{i + 1}. {tag} {row.assetPath}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"类型: {row.assetType} | 格式: {row.extension} | 大小: {FormatSize(row.diskBytes)}");
                if (GUILayout.Button("定位资源", GUILayout.Width(90f)))
                {
                    UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(row.assetPath);
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                }
            }
        }

        EditorGUILayout.EndScrollView();
    }

    private void RunScan()
    {
        ClearRows();
        BuildExtensions();

        string folderPath = AssetDatabase.GetAssetPath(targetFolder);
        if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
        {
            EditorUtility.DisplayDialog("提示", "请先指定有效的 Project 文件夹。", "确定");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid())
        {
            EditorUtility.DisplayDialog("提示", "当前没有有效场景，请先打开一个场景。", "确定");
            return;
        }

        _lastSceneName = string.IsNullOrEmpty(scene.path) ? scene.name + "（未保存）" : scene.path;
        _lastFolderPath = folderPath;

        HashSet<string> usedPaths = SceneAssetUsageCollector.CollectUsedAssetPaths(
            scene,
            includeInactive,
            includeIndirectDependencies);

        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
        string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { folderPath });

        for (int i = 0; i < guids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (AssetDatabase.IsValidFolder(assetPath))
            {
                continue;
            }

            if (!scanSubFolders)
            {
                string parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
                if (!string.Equals(parent, folderPath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            string ext = Path.GetExtension(assetPath);
            if (_extensions.Count > 0 && (string.IsNullOrEmpty(ext) || !_extensions.Contains(ext)))
            {
                continue;
            }

            if (ignoreScriptAssets && string.Equals(ext, ".cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(assetPath, scene.path, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _folderAssetCount++;

            AssetRow row = CreateRow(assetPath, ext, projectRoot);
            if (usedPaths.Contains(assetPath))
            {
                row.isUnused = false;
                _usedRows.Add(row);
            }
            else
            {
                row.isUnused = true;
                _unusedRows.Add(row);
            }
        }

        _usedRows.Sort((a, b) => string.Compare(a.assetPath, b.assetPath, StringComparison.OrdinalIgnoreCase));
        _unusedRows.Sort((a, b) => string.Compare(a.assetPath, b.assetPath, StringComparison.OrdinalIgnoreCase));

        EditorUtility.DisplayDialog(
            "扫描完成",
            $"场景：{_lastSceneName}\n" +
            $"文件夹资源：{_folderAssetCount}\n" +
            $"已引用：{_usedRows.Count}\n" +
            $"未引用：{_unusedRows.Count}",
            "确定");
    }

    private static AssetRow CreateRow(string assetPath, string ext, string projectRoot)
    {
        UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
        long bytes = 0;
        string fullPath = Path.Combine(projectRoot, assetPath);
        if (File.Exists(fullPath))
        {
            bytes = new FileInfo(fullPath).Length;
        }

        return new AssetRow
        {
            assetPath = assetPath,
            extension = string.IsNullOrEmpty(ext) ? "(无)" : ext.ToLowerInvariant(),
            assetType = asset != null ? asset.GetType().Name : "Unknown",
            diskBytes = bytes
        };
    }

    private void BuildExtensions()
    {
        _extensions.Clear();
        if (string.IsNullOrWhiteSpace(extensionFilter))
        {
            return;
        }

        string[] tokens = extensionFilter.Split(new[] { ',', ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < tokens.Length; i++)
        {
            string ext = tokens[i].Trim();
            if (string.IsNullOrWhiteSpace(ext))
            {
                continue;
            }

            if (!ext.StartsWith("."))
            {
                ext = "." + ext;
            }

            _extensions.Add(ext.ToLowerInvariant());
        }
    }

    private void ClearRows()
    {
        _usedRows.Clear();
        _unusedRows.Clear();
        _moveFailures.Clear();
        _folderAssetCount = 0;
        _lastSceneName = string.Empty;
        _lastFolderPath = string.Empty;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        double kb = bytes / 1024d;
        if (kb < 1024)
        {
            return $"{kb:F2} KB";
        }

        double mb = kb / 1024d;
        return $"{mb:F2} MB";
    }

    private void ExportMarkdownReport()
    {
        if (_folderAssetCount == 0)
        {
            EditorUtility.DisplayDialog("提示", "没有可导出的扫描结果，请先执行扫描。", "确定");
            return;
        }

        string defaultName = $"SceneUnusedAssets_{DateTime.Now:yyyyMMdd_HHmmss}.md";
        string outputPath = EditorUtility.SaveFilePanel("导出场景未引用资源报告", Application.dataPath, defaultName, "md");
        if (string.IsNullOrEmpty(outputPath))
        {
            return;
        }

        using (var writer = new StreamWriter(outputPath, false, Encoding.UTF8))
        {
            writer.WriteLine("# 场景未引用资源检测报告");
            writer.WriteLine();
            writer.WriteLine($"- 扫描时间：`{DateTime.Now:yyyy-MM-dd HH:mm:ss}`");
            writer.WriteLine($"- 当前场景：`{_lastSceneName}`");
            writer.WriteLine($"- 目标文件夹：`{_lastFolderPath}`");
            writer.WriteLine($"- 包含间接依赖：`{includeIndirectDependencies}`");
            writer.WriteLine($"- 文件夹资源总数：`{_folderAssetCount}`");
            writer.WriteLine($"- 已引用：`{_usedRows.Count}`");
            writer.WriteLine($"- 未引用：`{_unusedRows.Count}`");
            writer.WriteLine();
            writer.WriteLine("## 未引用列表");
            writer.WriteLine();
            WriteRows(writer, _unusedRows);
            writer.WriteLine();
            writer.WriteLine("## 已引用列表");
            writer.WriteLine();
            WriteRows(writer, _usedRows);
        }

        EditorUtility.RevealInFinder(outputPath);
    }

    private void MoveUnusedAssetsToNewFolder()
    {
        if (_unusedRows.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "当前没有未使用资源可转移。", "确定");
            return;
        }

        if (!EnsureFolderPathExists(UnusedRootFolderPath))
        {
            EditorUtility.DisplayDialog("提示", "无法创建默认目录 Assets/Unused，请检查目录权限。", "确定");
            return;
        }

        string targetRoot = UnusedRootFolderPath;

        int movedCount = 0;
        _moveFailures.Clear();
        _lastMoveRecords.Clear();
        List<AssetRow> rows = new List<AssetRow>(_unusedRows);
        for (int i = 0; i < rows.Count; i++)
        {
            AssetRow row = rows[i];
            if (row.assetPath.StartsWith(targetRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string extensionFolder = BuildExtensionFolderName(row.extension);
            string typeFolder = SanitizeFolderName(row.assetType, "UnknownType");
            string targetFolder = EnsureChildFolder(targetRoot, extensionFolder);
            targetFolder = EnsureChildFolder(targetFolder, typeFolder);

            string fileName = Path.GetFileName(row.assetPath);
            string destinationPath = AssetDatabase.GenerateUniqueAssetPath($"{targetFolder}/{fileName}");
            string originalPath = row.assetPath;
            string error = AssetDatabase.MoveAsset(row.assetPath, destinationPath);
            if (!string.IsNullOrEmpty(error))
            {
                _moveFailures.Add($"{row.assetPath} -> {error}");
                continue;
            }

            row.assetPath = destinationPath;
            _lastMoveRecords.Add(new MoveRecord
            {
                originalPath = originalPath,
                movedPath = destinationPath
            });
            movedCount++;
        }

        _unusedRows.Sort((a, b) => string.Compare(a.assetPath, b.assetPath, StringComparison.OrdinalIgnoreCase));
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(targetRoot);
        EditorGUIUtility.PingObject(Selection.activeObject);

        string message = $"新目录：{targetRoot}\n成功转移：{movedCount}\n失败：{_moveFailures.Count}";
        if (_moveFailures.Count > 0)
        {
            int preview = Mathf.Min(_moveFailures.Count, 5);
            for (int i = 0; i < preview; i++)
            {
                message += $"\n- {_moveFailures[i]}";
            }

            if (_moveFailures.Count > preview)
            {
                message += $"\n...其余 {_moveFailures.Count - preview} 条失败请查看控制台。";
            }

            Debug.LogWarning("[SceneWeiYongAssetCheckWin] 资源转移失败详情：\n" + string.Join("\n", _moveFailures));
        }

        EditorUtility.DisplayDialog("转移完成", message, "确定");
    }

    private void RestoreMovedAssetsToOriginalPaths()
    {
        if (_lastMoveRecords.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "没有可恢复的转移记录。", "确定");
            return;
        }

        int restoredCount = 0;
        _moveFailures.Clear();
        var remainedRecords = new List<MoveRecord>();

        for (int i = 0; i < _lastMoveRecords.Count; i++)
        {
            MoveRecord record = _lastMoveRecords[i];
            if (record == null || string.IsNullOrEmpty(record.originalPath) || string.IsNullOrEmpty(record.movedPath))
            {
                continue;
            }

            UnityEngine.Object movedObj = AssetDatabase.LoadMainAssetAtPath(record.movedPath);
            if (movedObj == null)
            {
                _moveFailures.Add($"{record.movedPath} -> 资源不存在，无法恢复");
                remainedRecords.Add(record);
                continue;
            }

            string parentFolder = Path.GetDirectoryName(record.originalPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parentFolder) || !EnsureFolderPathExists(parentFolder))
            {
                _moveFailures.Add($"{record.movedPath} -> 原目录无效：{record.originalPath}");
                remainedRecords.Add(record);
                continue;
            }

            if (AssetDatabase.LoadMainAssetAtPath(record.originalPath) != null)
            {
                _moveFailures.Add($"{record.movedPath} -> 原路径已存在同名资源：{record.originalPath}");
                remainedRecords.Add(record);
                continue;
            }

            string error = AssetDatabase.MoveAsset(record.movedPath, record.originalPath);
            if (!string.IsNullOrEmpty(error))
            {
                _moveFailures.Add($"{record.movedPath} -> {error}");
                remainedRecords.Add(record);
                continue;
            }

            for (int rowIndex = 0; rowIndex < _unusedRows.Count; rowIndex++)
            {
                if (string.Equals(_unusedRows[rowIndex].assetPath, record.movedPath, StringComparison.OrdinalIgnoreCase))
                {
                    _unusedRows[rowIndex].assetPath = record.originalPath;
                    break;
                }
            }

            restoredCount++;
        }

        _lastMoveRecords = remainedRecords;
        _unusedRows.Sort((a, b) => string.Compare(a.assetPath, b.assetPath, StringComparison.OrdinalIgnoreCase));
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string message = $"恢复成功：{restoredCount}\n恢复失败：{_moveFailures.Count}";
        if (_moveFailures.Count > 0)
        {
            int preview = Mathf.Min(_moveFailures.Count, 5);
            for (int i = 0; i < preview; i++)
            {
                message += $"\n- {_moveFailures[i]}";
            }

            if (_moveFailures.Count > preview)
            {
                message += $"\n...其余 {_moveFailures.Count - preview} 条失败请查看控制台。";
            }

            Debug.LogWarning("[SceneWeiYongAssetCheckWin] 资源恢复失败详情：\n" + string.Join("\n", _moveFailures));
        }

        EditorUtility.DisplayDialog("恢复完成", message, "确定");
    }

    private static bool EnsureFolderPathExists(string assetFolderPath)
    {
        string normalized = assetFolderPath.Replace('\\', '/').TrimEnd('/');
        if (AssetDatabase.IsValidFolder(normalized))
        {
            return true;
        }

        if (!normalized.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] parts = normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        string current = "Assets";
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (AssetDatabase.IsValidFolder(next))
            {
                current = next;
                continue;
            }

            string guid = AssetDatabase.CreateFolder(current, parts[i]);
            if (string.IsNullOrEmpty(guid))
            {
                return false;
            }

            current = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(current))
            {
                return false;
            }
        }

        return AssetDatabase.IsValidFolder(normalized);
    }

    private static string EnsureChildFolder(string parentAssetPath, string folderName)
    {
        string candidate = $"{parentAssetPath}/{folderName}";
        if (AssetDatabase.IsValidFolder(candidate))
        {
            return candidate;
        }

        AssetDatabase.CreateFolder(parentAssetPath, folderName);
        return candidate;
    }

    private static string BuildExtensionFolderName(string extension)
    {
        if (string.IsNullOrEmpty(extension) || extension == "(无)")
        {
            return "NoExtension";
        }

        string value = extension.Trim().TrimStart('.');
        if (string.IsNullOrEmpty(value))
        {
            return "NoExtension";
        }

        return SanitizeFolderName(value.ToUpperInvariant(), "NoExtension");
    }

    private static string SanitizeFolderName(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        string result = value.Trim();
        char[] invalidChars = Path.GetInvalidFileNameChars();
        for (int i = 0; i < invalidChars.Length; i++)
        {
            result = result.Replace(invalidChars[i], '_');
        }

        if (string.IsNullOrWhiteSpace(result))
        {
            return fallback;
        }

        return result;
    }

    private static void WriteRows(StreamWriter writer, List<AssetRow> rows)
    {
        if (rows.Count == 0)
        {
            writer.WriteLine("- 无");
            return;
        }

        for (int i = 0; i < rows.Count; i++)
        {
            AssetRow row = rows[i];
            writer.WriteLine($"- `{row.assetPath}` | 类型={row.assetType} | 格式={row.extension} | 大小={FormatSize(row.diskBytes)}");
        }
    }
}

/// <summary>
/// 收集当前场景引用的工程资源路径（Assets/...）。
/// </summary>
internal static class SceneAssetUsageCollector
{
    public static HashSet<string> CollectUsedAssetPaths(Scene scene, bool includeInactive, bool includeIndirect)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(scene.path))
        {
            AddDependencies(paths, scene.path, includeIndirect);
        }

        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            CollectFromGameObject(roots[i], paths, includeInactive, includeIndirect);
        }

        return paths;
    }

    private static void CollectFromGameObject(GameObject go, HashSet<string> paths, bool includeInactive, bool includeIndirect)
    {
        if (go == null)
        {
            return;
        }

        if (!includeInactive && !go.activeInHierarchy)
        {
            return;
        }

        Component[] components = go.GetComponents<Component>();
        for (int i = 0; i < components.Length; i++)
        {
            Component comp = components[i];
            if (comp == null)
            {
                continue;
            }

            CollectSerializedReferences(comp, paths, includeIndirect);

            if (comp is Renderer renderer)
            {
                AddMaterialReferences(renderer.sharedMaterials, paths, includeIndirect);
            }

            if (comp is UnityEngine.UI.Graphic graphic)
            {
                AddMaterialReferences(new[] { graphic.material }, paths, includeIndirect);
            }
        }

        Transform transform = go.transform;
        for (int c = 0; c < transform.childCount; c++)
        {
            CollectFromGameObject(transform.GetChild(c).gameObject, paths, includeInactive, includeIndirect);
        }
    }

    private static void CollectSerializedReferences(UnityEngine.Object target, HashSet<string> paths, bool includeIndirect)
    {
        SerializedObject so = new SerializedObject(target);
        SerializedProperty prop = so.GetIterator();
        while (prop.Next(true))
        {
            if (prop.propertyType != SerializedPropertyType.ObjectReference)
            {
                continue;
            }

            UnityEngine.Object refObj = prop.objectReferenceValue;
            if (refObj == null)
            {
                continue;
            }

            AddObjectPath(paths, refObj, includeIndirect);
        }
    }

    private static void AddMaterialReferences(Material[] materials, HashSet<string> paths, bool includeIndirect)
    {
        if (materials == null)
        {
            return;
        }

        for (int i = 0; i < materials.Length; i++)
        {
            AddObjectPath(paths, materials[i], includeIndirect);
        }
    }

    private static void AddObjectPath(HashSet<string> paths, UnityEngine.Object obj, bool includeIndirect)
    {
        if (obj == null)
        {
            return;
        }

        string path = AssetDatabase.GetAssetPath(obj);
        if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        paths.Add(path);
        if (includeIndirect)
        {
            AddDependencies(paths, path, true);
        }
    }

    private static void AddDependencies(HashSet<string> paths, string assetPath, bool recursive)
    {
        string[] deps = AssetDatabase.GetDependencies(assetPath, recursive);
        for (int i = 0; i < deps.Length; i++)
        {
            string dep = deps[i];
            if (string.IsNullOrEmpty(dep) || !dep.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            paths.Add(dep);
        }
    }
}
