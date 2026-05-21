using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 扫描项目中像素内容相同的重复贴图，合并材质引用并安全删除冗余资源。
/// </summary>
public class ChongFuTexCleaner : EditorWindow
{
    private const string MenuPath = "Tools/MinoTools/特效资源工具/重复贴图清理";
    private const string ScanFolderPrefsKey = "MinoTools.ChongFuTexCleaner.ScanFolder";
    private const string UseFolderScopePrefsKey = "MinoTools.ChongFuTexCleaner.UseFolderScope";

    private enum ScanScope
    {
        EntireAssets,
        SpecificFolder
    }

    private class TexItem
    {
        public bool Checked;
        public Texture2D Tex;
        public string Name;
        public string Path;
        public string KeepPath;
    }

    private readonly List<TexItem> _duplicateList = new List<TexItem>();
    private readonly Dictionary<string, Texture2D> _originMap = new Dictionary<string, Texture2D>();
    private readonly Dictionary<string, string> _pathToKey = new Dictionary<string, string>();
    private readonly List<string> _hashFailedPaths = new List<string>();

    private Vector2 _scrollPos;
    private string _scanSummary = "尚未扫描";
    private ScanScope _scanScope = ScanScope.EntireAssets;
    private DefaultAsset _scanFolderAsset;
    private string _scanFolderPath = "Assets";

    [MenuItem(MenuPath)]
    public static void ShowTool()
    {
        GetWindow<ChongFuTexCleaner>("重复贴图清理工具");
    }

    private void OnEnable()
    {
        _scanScope = EditorPrefs.GetBool(UseFolderScopePrefsKey, false)
            ? ScanScope.SpecificFolder
            : ScanScope.EntireAssets;
        _scanFolderPath = EditorPrefs.GetString(ScanFolderPrefsKey, "Assets");
        _scanFolderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(_scanFolderPath);
    }

    private void OnGUI()
    {
        GUILayout.Space(8);
        EditorGUILayout.HelpBox(
            "推荐流程：扫描 → 替换材质贴图引用 → 确认列表 → 删除已勾选",
            MessageType.Info);

        DrawScanScopeSettings();

        GUILayout.Space(6);

        if (GUILayout.Button("扫描重复贴图", GUILayout.Height(32)))
        {
            ScanTextures();
        }

        if (GUILayout.Button("替换材质贴图引用", GUILayout.Height(32)))
        {
            ReplaceMaterials();
        }

        GUILayout.Space(6);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("全选")) SetAllChecked(true);
        if (GUILayout.Button("取消全选")) SetAllChecked(false);
        GUILayout.EndHorizontal();

        if (GUILayout.Button("删除已勾选", GUILayout.Height(30)))
        {
            DeleteChecked();
        }

        GUILayout.Space(8);
        EditorGUILayout.LabelField("扫描结果", _scanSummary, EditorStyles.wordWrappedLabel);

        if (_hashFailedPaths.Count > 0)
        {
            EditorGUILayout.HelpBox(
                $"有 {_hashFailedPaths.Count} 张贴图无法计算像素哈希，已跳过（详见 Console）。",
                MessageType.Warning);
        }

        GUILayout.Space(6);
        EditorGUILayout.LabelField("重复贴图列表", EditorStyles.boldLabel);

        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
        for (int i = 0; i < _duplicateList.Count; i++)
        {
            DrawDuplicateItemRow(_duplicateList[i]);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawScanScopeSettings()
    {
        EditorGUILayout.LabelField("扫描范围", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        _scanScope = (ScanScope)EditorGUILayout.Popup(
            "范围模式",
            (int)_scanScope,
            new[] { "整个 Assets", "指定文件夹" });
        if (EditorGUI.EndChangeCheck())
            SaveScanScopePrefs();

        if (_scanScope == ScanScope.SpecificFolder)
        {
            EditorGUI.BeginChangeCheck();
            _scanFolderAsset = (DefaultAsset)EditorGUILayout.ObjectField(
                "目标文件夹",
                _scanFolderAsset,
                typeof(DefaultAsset),
                false);
            if (EditorGUI.EndChangeCheck())
            {
                UpdateScanFolderFromAsset();
                SaveScanScopePrefs();
            }

            EditorGUILayout.LabelField("文件夹路径", _scanFolderPath, EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("使用 Project 选中文件夹"))
                ApplyProjectSelectionAsScanFolder();

            if (GUILayout.Button("重置为 Assets"))
                SetScanFolder("Assets");
            EditorGUILayout.EndHorizontal();

            if (!IsValidScanFolderPath(_scanFolderPath))
            {
                EditorGUILayout.HelpBox(
                    "请指定 Assets 下的有效文件夹。可在 Project 中选中文件夹后点击「使用 Project 选中文件夹」。",
                    MessageType.Warning);
            }
        }
        else
        {
            EditorGUILayout.LabelField("当前将扫描整个 Assets 目录", EditorStyles.miniLabel);
        }
    }

    private void ApplyProjectSelectionAsScanFolder()
    {
        if (Selection.activeObject == null)
        {
            EditorUtility.DisplayDialog("重复贴图清理", "请先在 Project 中选中一个文件夹。", "确定");
            return;
        }

        string path = AssetDatabase.GetAssetPath(Selection.activeObject);
        if (!AssetDatabase.IsValidFolder(path))
        {
            EditorUtility.DisplayDialog("重复贴图清理", "当前选中项不是文件夹，请重新选择。", "确定");
            return;
        }

        SetScanFolder(path);
    }

    private void SetScanFolder(string folderPath)
    {
        _scanFolderPath = NormalizeAssetsFolderPath(folderPath);
        _scanFolderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(_scanFolderPath);
        _scanScope = ScanScope.SpecificFolder;
        SaveScanScopePrefs();
        Repaint();
    }

    private void UpdateScanFolderFromAsset()
    {
        if (_scanFolderAsset == null)
        {
            _scanFolderPath = "Assets";
            return;
        }

        string path = AssetDatabase.GetAssetPath(_scanFolderAsset);
        if (!AssetDatabase.IsValidFolder(path))
        {
            _scanFolderAsset = null;
            _scanFolderPath = "Assets";
            return;
        }

        _scanFolderPath = NormalizeAssetsFolderPath(path);
    }

    private void SaveScanScopePrefs()
    {
        EditorPrefs.SetBool(UseFolderScopePrefsKey, _scanScope == ScanScope.SpecificFolder);
        EditorPrefs.SetString(ScanFolderPrefsKey, _scanFolderPath);
    }

    private string[] GetScanSearchFolders()
    {
        if (_scanScope == ScanScope.EntireAssets)
            return new[] { "Assets" };

        return new[] { _scanFolderPath };
    }

    private string GetScanScopeDescription()
    {
        return _scanScope == ScanScope.EntireAssets
            ? "整个 Assets"
            : _scanFolderPath;
    }

    private static bool IsValidScanFolderPath(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath))
            return false;

        if (!folderPath.StartsWith("Assets", StringComparison.Ordinal))
            return false;

        return AssetDatabase.IsValidFolder(folderPath);
    }

    private static string NormalizeAssetsFolderPath(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath))
            return "Assets";

        return folderPath.Replace('\\', '/').TrimEnd('/');
    }

    private void DrawDuplicateItemRow(TexItem item)
    {
        GUILayout.BeginHorizontal("Box");
        item.Checked = EditorGUILayout.Toggle(item.Checked, GUILayout.Width(24));

        Rect previewRect = GUILayoutUtility.GetRect(50, 50, GUILayout.Width(50));
        if (item.Tex != null)
            EditorGUI.DrawPreviewTexture(previewRect, item.Tex);

        EditorGUILayout.BeginVertical();
        EditorGUILayout.LabelField(item.Name, EditorStyles.boldLabel);
        EditorGUILayout.LabelField("重复: " + item.Path, EditorStyles.miniLabel);
        EditorGUILayout.LabelField("保留: " + item.KeepPath, EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();

        GUILayout.EndHorizontal();
    }

    private void ScanTextures()
    {
        if (_scanScope == ScanScope.SpecificFolder && !IsValidScanFolderPath(_scanFolderPath))
        {
            EditorUtility.DisplayDialog(
                "重复贴图清理",
                "请先指定有效的扫描文件夹（Assets 下的目录）。",
                "确定");
            return;
        }

        _duplicateList.Clear();
        _originMap.Clear();
        _pathToKey.Clear();
        _hashFailedPaths.Clear();

        string[] searchFolders = GetScanSearchFolders();
        string[] textureGuids = AssetDatabase.FindAssets("t:Texture2D", searchFolders);
        int scannedCount = 0;
        int duplicateCount = 0;

        try
        {
            for (int i = 0; i < textureGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(textureGuids[i]);
                float progress = textureGuids.Length > 0 ? (float)i / textureGuids.Length : 1f;
                if (EditorUtility.DisplayCancelableProgressBar(
                        "扫描重复贴图 - " + GetScanScopeDescription(),
                        path,
                        progress))
                {
                    break;
                }

                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture == null)
                    continue;

                scannedCount++;

                if (!TryGetTextureKey(path, texture, out string key))
                    continue;

                if (!_originMap.ContainsKey(key))
                {
                    _originMap.Add(key, texture);
                    continue;
                }

                Texture2D keepTexture = _originMap[key];
                if (texture == keepTexture)
                    continue;

                duplicateCount++;
                _duplicateList.Add(new TexItem
                {
                    Checked = false,
                    Tex = texture,
                    Name = texture.name,
                    Path = path,
                    KeepPath = AssetDatabase.GetAssetPath(keepTexture)
                });
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        _scanSummary =
            $"范围 [{GetScanScopeDescription()}]：已扫描 {scannedCount} 张贴图，发现 {duplicateCount} 张重复贴图，保留组 {_originMap.Count} 个。";
        Repaint();
    }

    private void ReplaceMaterials()
    {
        if (_originMap.Count == 0)
        {
            EditorUtility.DisplayDialog("重复贴图清理", "请先执行「扫描重复贴图」。", "确定");
            return;
        }

        string[] materialGuids = AssetDatabase.FindAssets("t:Material", new[] { "Assets" });
        int replacedMaterialCount = 0;
        int replacedReferenceCount = 0;

        try
        {
            for (int i = 0; i < materialGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(materialGuids[i]);
                float progress = materialGuids.Length > 0 ? (float)i / materialGuids.Length : 1f;
                if (EditorUtility.DisplayCancelableProgressBar(
                        "替换材质贴图引用",
                        path,
                        progress))
                {
                    break;
                }

                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                    continue;

                SerializedObject serializedObject = new SerializedObject(material);
                SerializedProperty property = serializedObject.GetIterator();
                bool isDirty = false;
                int materialReplaceCount = 0;

                while (property.NextVisible(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference)
                        continue;

                    Texture2D texture = property.objectReferenceValue as Texture2D;
                    if (texture == null)
                        continue;

                    string texturePath = AssetDatabase.GetAssetPath(texture);
                    if (string.IsNullOrEmpty(texturePath))
                        continue;

                    if (!TryGetTextureKey(texturePath, texture, out string key))
                        continue;

                    if (!_originMap.TryGetValue(key, out Texture2D originTexture) || texture == originTexture)
                        continue;

                    property.objectReferenceValue = originTexture;
                    isDirty = true;
                    materialReplaceCount++;
                }

                if (!isDirty)
                    continue;

                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(material);
                replacedMaterialCount++;
                replacedReferenceCount += materialReplaceCount;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets();
        }

        EditorUtility.DisplayDialog(
            "重复贴图清理",
            $"已处理 {replacedMaterialCount} 个材质，替换 {replacedReferenceCount} 处贴图引用。",
            "确定");
    }

    private void SetAllChecked(bool isChecked)
    {
        for (int i = 0; i < _duplicateList.Count; i++)
            _duplicateList[i].Checked = isChecked;
    }

    private void DeleteChecked()
    {
        List<TexItem> itemsToDelete = new List<TexItem>();
        for (int i = 0; i < _duplicateList.Count; i++)
        {
            if (_duplicateList[i].Checked)
                itemsToDelete.Add(_duplicateList[i]);
        }

        if (itemsToDelete.Count == 0)
        {
            EditorUtility.DisplayDialog("重复贴图清理", "请先勾选要删除的重复贴图。", "确定");
            return;
        }

        StringBuilder messageBuilder = new StringBuilder();
        messageBuilder.AppendLine($"即将删除 {itemsToDelete.Count} 张贴图：");
        int previewCount = Mathf.Min(itemsToDelete.Count, 8);
        for (int i = 0; i < previewCount; i++)
            messageBuilder.AppendLine("- " + itemsToDelete[i].Path);

        if (itemsToDelete.Count > previewCount)
            messageBuilder.AppendLine($"... 另有 {itemsToDelete.Count - previewCount} 张");

        List<string> stillReferencedPaths = CollectStillReferencedPaths(itemsToDelete);
        if (stillReferencedPaths.Count > 0)
        {
            messageBuilder.AppendLine();
            messageBuilder.AppendLine($"警告：仍有 {stillReferencedPaths.Count} 张贴图被其它资源引用。");
            messageBuilder.AppendLine("建议先执行「替换材质贴图引用」。是否仍要删除？");
        }
        else
        {
            messageBuilder.AppendLine();
            messageBuilder.AppendLine("未检测到其它资源引用（材质范围外引用仍需自行确认）。");
        }

        if (!EditorUtility.DisplayDialog(
                "确认删除重复贴图",
                messageBuilder.ToString(),
                "删除",
                "取消"))
        {
            return;
        }

        AssetDatabase.StartAssetEditing();
        try
        {
            for (int i = 0; i < itemsToDelete.Count; i++)
            {
                TexItem item = itemsToDelete[i];
                if (!AssetDatabase.DeleteAsset(item.Path))
                    Debug.LogWarning($"删除失败: {item.Path}");
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
        }

        for (int i = _duplicateList.Count - 1; i >= 0; i--)
        {
            if (_duplicateList[i].Checked)
                _duplicateList.RemoveAt(i);
        }

        _scanSummary = $"剩余重复贴图 {_duplicateList.Count} 张。";
        Repaint();
    }

    private List<string> CollectStillReferencedPaths(List<TexItem> itemsToDelete)
    {
        List<string> referencedPaths = new List<string>();
        for (int i = 0; i < itemsToDelete.Count; i++)
        {
            string path = itemsToDelete[i].Path;
            if (FindReferencers(path).Count > 0)
                referencedPaths.Add(path);
        }

        return referencedPaths;
    }

    private List<string> FindReferencers(string assetPath)
    {
        List<string> referencers = new List<string>();
        string[] guids = AssetDatabase.FindAssets("ref:" + assetPath, new[] { "Assets" });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (!string.IsNullOrEmpty(path) && path != assetPath)
                referencers.Add(path);
        }

        if (referencers.Count > 0)
            return referencers;

        string[] allPaths = AssetDatabase.GetAllAssetPaths();
        for (int i = 0; i < allPaths.Length; i++)
        {
            string candidatePath = allPaths[i];
            if (!candidatePath.StartsWith("Assets/", StringComparison.Ordinal) || candidatePath == assetPath)
                continue;

            string[] dependencies = AssetDatabase.GetDependencies(candidatePath, false);
            for (int j = 0; j < dependencies.Length; j++)
            {
                if (dependencies[j] != assetPath)
                    continue;

                referencers.Add(candidatePath);
                break;
            }
        }

        return referencers;
    }

    private bool TryGetTextureKey(string path, Texture2D texture, out string key)
    {
        key = null;
        if (string.IsNullOrEmpty(path) || texture == null)
            return false;

        if (_pathToKey.TryGetValue(path, out key))
            return !string.IsNullOrEmpty(key);

        string pixelHash = ComputePixelHash(path);
        if (string.IsNullOrEmpty(pixelHash))
        {
            _pathToKey[path] = null;
            if (!_hashFailedPaths.Contains(path))
                _hashFailedPaths.Add(path);
            return false;
        }

        key = BuildTextureKey(pixelHash, texture);
        _pathToKey[path] = key;
        return true;
    }

    private static string BuildTextureKey(string pixelHash, Texture2D texture)
    {
        return pixelHash + "_" + texture.width + "_" + texture.height + "_" +
               texture.wrapModeU + "_" + texture.wrapModeV;
    }

    private string ComputePixelHash(string path)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            Debug.LogWarning($"[重复贴图清理] 无法获取 TextureImporter: {path}");
            return null;
        }

        bool oldReadable = importer.isReadable;
        TextureImporterCompression oldCompression = importer.textureCompression;

        try
        {
            importer.isReadable = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                Debug.LogWarning($"[重复贴图清理] 重新导入后无法加载贴图: {path}");
                return null;
            }

            Color32[] pixels = texture.GetPixels32();
            if (pixels == null || pixels.Length == 0)
            {
                Debug.LogWarning($"[重复贴图清理] 像素为空: {path}");
                return null;
            }

            byte[] rawBytes = new byte[pixels.Length * 4];
            Buffer.BlockCopy(pixels, 0, rawBytes, 0, rawBytes.Length);

            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(rawBytes);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[重复贴图清理] 计算哈希失败: {path}\n{exception}");
            return null;
        }
        finally
        {
            importer.isReadable = oldReadable;
            importer.textureCompression = oldCompression;
            importer.SaveAndReimport();
        }
    }
}
