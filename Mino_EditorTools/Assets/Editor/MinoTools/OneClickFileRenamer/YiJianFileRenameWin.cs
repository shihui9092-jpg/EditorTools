using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 一键文件重命名工具（支持选中文件夹递归收集文件）。
/// </summary>
public class YiJianFileRenameWin : EditorWindow
{
    private enum RenameMode
    {
        PrefixSuffix,
        ReplaceText,
        RegexReplace,
        Sequential
    }

    private struct RenamePreview
    {
        public string AssetPath;
        public string OldName;
        public string NewName;
    }

    private RenameMode _mode = RenameMode.PrefixSuffix;

    [SerializeField] private string prefix = string.Empty;
    [SerializeField] private string suffix = string.Empty;

    [SerializeField] private string findText = string.Empty;
    [SerializeField] private string replaceText = string.Empty;
    [SerializeField] private string regexPattern = string.Empty;
    [SerializeField] private string regexReplacement = string.Empty;
    [SerializeField] private bool regexIgnoreCase = false;

    [SerializeField] private string baseName = "Asset";
    [SerializeField] private int startIndex = 1;
    [SerializeField] private int numberPadding = 3;
    [SerializeField] private string separator = "_";
    [SerializeField] private bool includeFilesInSelectedFolders = true;
    [SerializeField] private bool includeMetaFiles = false;
    [SerializeField] private bool enableExtensionFilter = false;
    [SerializeField] private string extensionFilter = ".png,.prefab,.mat";

    private Vector2 _scrollPos;
    private List<string> _selectedAssetPaths = new List<string>();
    private readonly List<RenamePreview> _previewList = new List<RenamePreview>();
    private string _regexError = string.Empty;

    [MenuItem("Tools/MinoTools/命名工具/一键更改文件名")]
    public static void Open()
    {
        YiJianFileRenameWin window = GetWindow<YiJianFileRenameWin>("一键命名");
        window.minSize = new Vector2(680f, 420f);
        window.RefreshSelectionAndPreview();
    }

    private void OnFocus()
    {
        RefreshSelectionAndPreview();
    }

    private void OnSelectionChange()
    {
        RefreshSelectionAndPreview();
        Repaint();
    }

    private void OnGUI()
    {
        DrawHeader();
        EditorGUILayout.Space(8);

        EditorGUI.BeginChangeCheck();
        _mode = (RenameMode)EditorGUILayout.EnumPopup("重命名模式", _mode);
        DrawModeFields();
        DrawCommonFields();
        bool changed = EditorGUI.EndChangeCheck();

        if (changed)
        {
            BuildPreview();
        }

        EditorGUILayout.Space(10);
        DrawButtons();
        EditorGUILayout.Space(10);
        DrawPreviewArea();
    }

    private void DrawHeader()
    {
        EditorGUILayout.HelpBox(
            "批量重命名当前选中的文件资源（支持选中文件夹递归收集）。\n" +
            "建议先看预览，再执行一键重命名。",
            MessageType.Info);
    }

    private void DrawModeFields()
    {
        switch (_mode)
        {
            case RenameMode.PrefixSuffix:
                EditorGUILayout.HelpBox("在原文件名基础上添加前缀与后缀。", MessageType.None);
                break;

            case RenameMode.ReplaceText:
                findText = EditorGUILayout.TextField("查找文本", findText);
                replaceText = EditorGUILayout.TextField("替换文本", replaceText);
                break;

            case RenameMode.RegexReplace:
                regexPattern = EditorGUILayout.TextField("正则表达式", regexPattern);
                regexReplacement = EditorGUILayout.TextField("替换为", regexReplacement);
                regexIgnoreCase = EditorGUILayout.Toggle("忽略大小写", regexIgnoreCase);
                if (!string.IsNullOrEmpty(_regexError))
                {
                    EditorGUILayout.HelpBox(_regexError, MessageType.Error);
                }
                break;

            case RenameMode.Sequential:
                baseName = EditorGUILayout.TextField("基础名", baseName);
                separator = EditorGUILayout.TextField("连接符", separator);
                startIndex = EditorGUILayout.IntField("起始序号", Mathf.Max(0, startIndex));
                numberPadding = EditorGUILayout.IntSlider("序号位数", Mathf.Clamp(numberPadding, 1, 8), 1, 8);
                break;
        }
    }

    private void DrawCommonFields()
    {
        prefix = EditorGUILayout.TextField("统一前缀", prefix);
        suffix = EditorGUILayout.TextField("统一后缀", suffix);
        includeFilesInSelectedFolders = EditorGUILayout.Toggle("包含选中文件夹内文件（递归）", includeFilesInSelectedFolders);
        includeMetaFiles = EditorGUILayout.Toggle("包含 .meta 文件", includeMetaFiles);
        enableExtensionFilter = EditorGUILayout.Toggle("启用扩展名过滤", enableExtensionFilter);
        if (enableExtensionFilter)
        {
            extensionFilter = EditorGUILayout.TextField("扩展名列表（逗号分隔）", extensionFilter);
            EditorGUILayout.HelpBox("示例：.png,.prefab,.mat（支持带或不带点）", MessageType.None);
            if (IsExtensionFilterConfigInvalid())
            {
                EditorGUILayout.HelpBox(
                    "已启用扩展名过滤，但列表为空或没有有效后缀。请至少填写一个扩展名（如 png），或关闭「启用扩展名过滤」。此时「一键重命名」已禁用。",
                    MessageType.Warning);
            }
        }
    }

    private void DrawButtons()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("刷新选择", GUILayout.Height(28)))
            {
                RefreshSelectionAndPreview();
            }

            bool canRename = _previewList.Count > 0 && !IsExtensionFilterConfigInvalid();
            GUI.enabled = canRename;
            if (GUILayout.Button("一键重命名", GUILayout.Height(28)))
            {
                ExecuteRename();
            }

            GUI.enabled = true;
        }
    }

    private void DrawPreviewArea()
    {
        EditorGUILayout.LabelField($"已选中文件数：{_selectedAssetPaths.Count}，可重命名项：{_previewList.Count}", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        if (_previewList.Count == 0)
        {
            if (IsExtensionFilterConfigInvalid())
            {
                EditorGUILayout.HelpBox(
                    "当前扩展名过滤无效：未填写有效后缀时不会纳入任何文件。请填写扩展名列表或关闭过滤。",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox("没有可重命名的文件。请在 Project 视图中先选中资源文件。", MessageType.Warning);
            }
        }
        else
        {
            for (int i = 0; i < _previewList.Count; i++)
            {
                RenamePreview item = _previewList[i];
                EditorGUILayout.LabelField($"{i + 1}. {item.OldName}  ->  {item.NewName}");
                EditorGUILayout.LabelField(item.AssetPath, EditorStyles.miniLabel);
                EditorGUILayout.Space(2);
            }
        }

        EditorGUILayout.EndScrollView();
    }

    private void RefreshSelectionAndPreview()
    {
        _selectedAssetPaths = CollectSelectedFileAssets();
        BuildPreview();
    }

    private List<string> CollectSelectedFileAssets()
    {
        HashSet<string> resultSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string[] guids = Selection.assetGUIDs;

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }

            if (AssetDatabase.IsValidFolder(path))
            {
                if (includeFilesInSelectedFolders)
                {
                    CollectAssetsFromFolder(path, resultSet);
                }
                continue;
            }

            if (ShouldIncludeAssetPath(path))
            {
                resultSet.Add(path);
            }
        }

        List<string> result = new List<string>(resultSet);
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    private void CollectAssetsFromFolder(string folderPath, HashSet<string> resultSet)
    {
        string[] childGuids = AssetDatabase.FindAssets(string.Empty, new[] { folderPath });
        for (int i = 0; i < childGuids.Length; i++)
        {
            string childPath = AssetDatabase.GUIDToAssetPath(childGuids[i]);
            if (string.IsNullOrEmpty(childPath))
            {
                continue;
            }

            if (AssetDatabase.IsValidFolder(childPath))
            {
                continue;
            }

            if (ShouldIncludeAssetPath(childPath))
            {
                resultSet.Add(childPath);
            }
        }
    }

    private bool IsExtensionFilterConfigInvalid()
    {
        if (!enableExtensionFilter)
        {
            return false;
        }

        return GetNormalizedExtensionTokens().Count == 0;
    }

    private List<string> GetNormalizedExtensionTokens()
    {
        List<string> list = new List<string>();
        if (string.IsNullOrWhiteSpace(extensionFilter))
        {
            return list;
        }

        string[] tokens = extensionFilter.Split(new[] { ',', ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < tokens.Length; i++)
        {
            string normalized = NormalizeExtensionToken(tokens[i]);
            if (string.IsNullOrEmpty(normalized))
            {
                continue;
            }

            list.Add(normalized);
        }

        return list;
    }

    private bool ShouldIncludeAssetPath(string assetPath)
    {
        if (!includeMetaFiles && assetPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!enableExtensionFilter)
        {
            return true;
        }

        List<string> allowedExtensions = GetNormalizedExtensionTokens();
        if (allowedExtensions.Count == 0)
        {
            return false;
        }

        string assetExtension = Path.GetExtension(assetPath);
        if (string.IsNullOrEmpty(assetExtension))
        {
            return false;
        }

        for (int i = 0; i < allowedExtensions.Count; i++)
        {
            if (string.Equals(assetExtension, allowedExtensions[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private string NormalizeExtensionToken(string token)
    {
        string value = token?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (!value.StartsWith("."))
        {
            value = "." + value;
        }

        return value;
    }

    private void BuildPreview()
    {
        _previewList.Clear();
        _regexError = string.Empty;

        for (int i = 0; i < _selectedAssetPaths.Count; i++)
        {
            string path = _selectedAssetPaths[i];
            string oldName = Path.GetFileNameWithoutExtension(path);
            string newName = BuildNewName(oldName, i);

            if (string.IsNullOrWhiteSpace(newName))
            {
                continue;
            }

            newName = newName.Trim();
            if (string.Equals(oldName, newName, StringComparison.Ordinal))
            {
                continue;
            }

            _previewList.Add(new RenamePreview
            {
                AssetPath = path,
                OldName = oldName,
                NewName = newName
            });
        }
    }

    private string BuildNewName(string oldName, int index)
    {
        string modeName = oldName;

        switch (_mode)
        {
            case RenameMode.PrefixSuffix:
                break;

            case RenameMode.ReplaceText:
                if (!string.IsNullOrEmpty(findText))
                {
                    modeName = oldName.Replace(findText, replaceText);
                }
                break;

            case RenameMode.RegexReplace:
                if (!TryRegexReplace(oldName, out modeName))
                {
                    return string.Empty;
                }
                break;

            case RenameMode.Sequential:
                int number = Mathf.Max(0, startIndex) + index;
                string numberPart = number.ToString().PadLeft(Mathf.Clamp(numberPadding, 1, 8), '0');
                modeName = $"{baseName}{separator}{numberPart}";
                break;
        }

        return $"{prefix}{modeName}{suffix}";
    }

    private bool TryRegexReplace(string oldName, out string replacedName)
    {
        replacedName = oldName;
        if (string.IsNullOrEmpty(regexPattern))
        {
            return true;
        }

        try
        {
            RegexOptions options = regexIgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
            replacedName = Regex.Replace(oldName, regexPattern, regexReplacement, options);
            return true;
        }
        catch (ArgumentException ex)
        {
            _regexError = $"正则表达式无效：{ex.Message}";
            return false;
        }
    }

    private void ExecuteRename()
    {
        if (IsExtensionFilterConfigInvalid())
        {
            EditorUtility.DisplayDialog(
                "扩展名过滤无效",
                "已启用扩展名过滤，但未填写有效的扩展名列表。请先填写至少一个后缀，或关闭「启用扩展名过滤」后再执行。",
                "确定");
            return;
        }

        if (_previewList.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "没有可执行的重命名项。", "确定");
            return;
        }

        int successCount = 0;
        List<string> failedMessages = new List<string>();

        AssetDatabase.StartAssetEditing();
        try
        {
            for (int i = 0; i < _previewList.Count; i++)
            {
                RenamePreview item = _previewList[i];
                string finalName = MakeUniqueNameIfNeeded(item.AssetPath, item.NewName);
                string error = AssetDatabase.RenameAsset(item.AssetPath, finalName);

                if (string.IsNullOrEmpty(error))
                {
                    successCount++;
                }
                else
                {
                    failedMessages.Add($"{item.AssetPath} -> {item.NewName} | 错误：{error}");
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        RefreshSelectionAndPreview();

        string resultMessage = $"重命名完成：成功 {successCount} 个，失败 {failedMessages.Count} 个。";
        if (failedMessages.Count > 0)
        {
            for (int i = 0; i < failedMessages.Count; i++)
            {
                Debug.LogWarning(failedMessages[i]);
            }
        }

        EditorUtility.DisplayDialog("执行结果", resultMessage, "确定");
    }

    private string MakeUniqueNameIfNeeded(string assetPath, string expectedName)
    {
        string directory = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
        string extension = Path.GetExtension(assetPath);

        if (string.IsNullOrEmpty(directory))
        {
            return expectedName;
        }

        string candidate = expectedName;
        int attempt = 1;
        while (attempt <= 9999)
        {
            string candidatePath = $"{directory}/{candidate}{extension}";
            bool samePath = string.Equals(candidatePath, assetPath, StringComparison.OrdinalIgnoreCase);
            bool exists = File.Exists(candidatePath);

            if (samePath || !exists)
            {
                return candidate;
            }

            candidate = $"{expectedName}_{attempt}";
            attempt++;
        }

        return $"{expectedName}_{Guid.NewGuid():N}".Substring(0, expectedName.Length + 9);
    }
}
