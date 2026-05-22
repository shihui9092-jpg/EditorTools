using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// MinoCameraController 参数配置文件导出与导入（编辑/运行模式均可用）。
/// </summary>
public static class MinoCameraParamIO
{
    private const string ConfigFileExtension = ".minocamera.json";
    private const string ConfigFileExtensionWithoutDot = "minocamera.json";

    /// <summary>MinoCameraController 脚本所在资源目录。</summary>
    private const string ScriptAssetFolder = "Assets/Plugins/MinoTools/MinoCameraController";

    /// <summary>导出相机参数 JSON 的专用子文件夹（语义：相机参数配置档案）。</summary>
    private const string ExportProfilesFolderName = "CameraParameterProfiles";

    /// <summary>导出目录资源路径。</summary>
    public static string ExportProfilesAssetFolder => $"{ScriptAssetFolder}/{ExportProfilesFolderName}";

    [Serializable]
    private class MinoCameraParameterConfigBundle
    {
        public string componentJson;
        public string orbitFocusGlobalId;
        public string displayTargetGlobalId;
        public string mainLightGlobalId;
        public string cameraComponentJson;
        public bool hasGameObjectTag;
        public string gameObjectTag;
        public bool hasGameObjectLayer;
        public int gameObjectLayer;
        public bool hasTransform;
        public Vector3 localPosition;
        public Vector3 localRotation;
        public Vector3 localScale;
        public bool hasRuntimeOrbit;
        public float orbitYaw;
        public float orbitPitch;
        public float smoothedOrbitDistance;
    }

    /// <summary>
    /// 导出当前参数到 CameraParameterProfiles 文件夹（含 MinoCameraController、同物体 Camera、Transform、运行时轨道角、Tag、Layer）。
    /// </summary>
    public static bool ExportToConfigFile(MinoCameraController controller)
    {
        if (controller == null)
        {
            return false;
        }

        if (!EnsureExportProfilesFolderExists())
        {
            EditorUtility.DisplayDialog("导出参数配置", "无法创建导出目录 CameraParameterProfiles。", "确定");
            return false;
        }

        string fileName = BuildExportFileName(controller);
        string assetPath = $"{ExportProfilesAssetFolder}/{fileName}";
        string fullPath = Path.Combine(GetExportProfilesFullDirectory(), fileName);

        MinoCameraParameterConfigBundle bundle = CreateBundleFromController(controller);
        string bundleJson = JsonUtility.ToJson(bundle, false);
        File.WriteAllText(fullPath, bundleJson, Encoding.UTF8);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

        UnityEngine.Object exportedAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
        if (exportedAsset != null)
        {
            EditorGUIUtility.PingObject(exportedAsset);
            Selection.activeObject = exportedAsset;
        }

        Debug.Log($"[MinoCameraController] 已导出到：{assetPath}", controller);
        return true;
    }

    /// <summary>从 CameraParameterProfiles 目录选择配置文件并导入。</summary>
    public static bool ImportFromConfigFile(MinoCameraController controller)
    {
        if (controller == null)
        {
            return false;
        }

        string bundleJson = ReadJsonFromFilePanel("导入相机参数配置");
        if (string.IsNullOrEmpty(bundleJson))
        {
            return false;
        }

        if (!TryParseBundle(bundleJson, out MinoCameraParameterConfigBundle bundle, out string parseError))
        {
            EditorUtility.DisplayDialog("导入参数配置", parseError ?? "配置数据格式无效。", "确定");
            return false;
        }

        return ApplyBundleToController(controller, bundle, "导入相机参数");
    }

    private static bool EnsureExportProfilesFolderExists()
    {
        if (AssetDatabase.IsValidFolder(ExportProfilesAssetFolder))
        {
            return true;
        }

        if (!AssetDatabase.IsValidFolder(ScriptAssetFolder))
        {
            return false;
        }

        string guid = AssetDatabase.CreateFolder(ScriptAssetFolder, ExportProfilesFolderName);
        return !string.IsNullOrEmpty(guid);
    }

    private static string BuildExportFileName(MinoCameraController controller)
    {
        string sceneName = controller.gameObject.scene.name;
        if (string.IsNullOrEmpty(sceneName))
        {
            sceneName = "Untitled";
        }

        string objectName = SanitizeFileName(controller.gameObject.name);
        string scenePart = SanitizeFileName(sceneName);
        string timePart = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        return $"{scenePart}_{objectName}_{timePart}{ConfigFileExtension}";
    }

    private static string SanitizeFileName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return "Unnamed";
        }

        char[] invalidChars = Path.GetInvalidFileNameChars();
        char[] buffer = rawName.ToCharArray();
        for (int i = 0; i < buffer.Length; i++)
        {
            if (Array.IndexOf(invalidChars, buffer[i]) >= 0)
            {
                buffer[i] = '_';
            }
        }

        return new string(buffer).Trim();
    }

    private static MinoCameraParameterConfigBundle CreateBundleFromController(MinoCameraController controller)
    {
        GameObject gameObject = controller.gameObject;
        SerializedObject serializedObject = new SerializedObject(controller);
        serializedObject.Update();

        MinoCameraParameterConfigBundle bundle = new MinoCameraParameterConfigBundle
        {
            componentJson = EditorJsonUtility.ToJson(controller, true),
            orbitFocusGlobalId = CaptureReferenceGlobalId(serializedObject, "orbitFocus"),
            displayTargetGlobalId = CaptureReferenceGlobalId(serializedObject, "displayTarget"),
            mainLightGlobalId = CaptureReferenceGlobalId(serializedObject, "mainLightTransform"),
            hasGameObjectTag = true,
            gameObjectTag = gameObject.tag,
            hasGameObjectLayer = true,
            gameObjectLayer = gameObject.layer
        };

        Camera camera = gameObject.GetComponent<Camera>();
        if (camera != null)
        {
            bundle.cameraComponentJson = EditorJsonUtility.ToJson(camera, true);
        }

        Transform transform = gameObject.transform;
        bundle.hasTransform = true;
        bundle.localPosition = transform.localPosition;
        bundle.localRotation = transform.localEulerAngles;
        bundle.localScale = transform.localScale;

        controller.CaptureRuntimeOrbitState(
            out float orbitYaw,
            out float orbitPitch,
            out float smoothedOrbitDistance);
        bundle.hasRuntimeOrbit = true;
        bundle.orbitYaw = orbitYaw;
        bundle.orbitPitch = orbitPitch;
        bundle.smoothedOrbitDistance = smoothedOrbitDistance;

        return bundle;
    }

    private static string CaptureReferenceGlobalId(SerializedObject serializedObject, string propertyName)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
        {
            return string.Empty;
        }

        return ObjectToGlobalIdString(property.objectReferenceValue);
    }

    private static string ObjectToGlobalIdString(UnityEngine.Object obj)
    {
        if (obj == null)
        {
            return string.Empty;
        }

        GlobalObjectId globalId = GlobalObjectId.GetGlobalObjectIdSlow(obj);
        return globalId.ToString();
    }

    private static bool TryParseBundle(string json, out MinoCameraParameterConfigBundle bundle, out string error)
    {
        bundle = null;
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "配置文件为空。";
            return false;
        }

        string trimmedJson = json.Trim();
        if (trimmedJson.StartsWith("{", StringComparison.Ordinal))
        {
            if (trimmedJson.IndexOf("\"componentJson\"", StringComparison.Ordinal) >= 0)
            {
                try
                {
                    bundle = JsonUtility.FromJson<MinoCameraParameterConfigBundle>(trimmedJson);
                    if (bundle != null && !string.IsNullOrEmpty(bundle.componentJson))
                    {
                        return true;
                    }

                    error = "配置包缺少 componentJson 字段或内容为空。";
                    return false;
                }
                catch (Exception exception)
                {
                    error = $"解析配置包失败：{exception.Message}";
                    return false;
                }
            }
        }

        // 兼容旧版：文件内仅为 MinoCameraController 的 EditorJson
        if (trimmedJson.IndexOf("\"MonoBehaviour\"", StringComparison.Ordinal) >= 0
            || trimmedJson.IndexOf("orbitDistance", StringComparison.Ordinal) >= 0)
        {
            bundle = new MinoCameraParameterConfigBundle
            {
                componentJson = trimmedJson,
                orbitFocusGlobalId = string.Empty,
                displayTargetGlobalId = string.Empty,
                mainLightGlobalId = string.Empty,
                cameraComponentJson = string.Empty,
                hasGameObjectTag = false,
                hasGameObjectLayer = false
            };
            return true;
        }

        error = "无法识别的配置格式，请使用本工具导出的 .minocamera.json 文件。";
        return false;
    }

    private static bool ApplyBundleToController(
        MinoCameraController controller,
        MinoCameraParameterConfigBundle bundle,
        string undoName)
    {
        GameObject gameObject = controller.gameObject;
        Undo.RecordObject(controller, undoName);
        Undo.RecordObject(gameObject, undoName);

        EditorJsonUtility.FromJsonOverwrite(bundle.componentJson, controller);

        Camera camera = gameObject.GetComponent<Camera>();
        if (camera != null && !string.IsNullOrEmpty(bundle.cameraComponentJson))
        {
            Undo.RecordObject(camera, undoName);
            EditorJsonUtility.FromJsonOverwrite(bundle.cameraComponentJson, camera);
        }

        if (bundle.hasGameObjectTag && !string.IsNullOrEmpty(bundle.gameObjectTag))
        {
            try
            {
                gameObject.tag = bundle.gameObjectTag;
            }
            catch (UnityException exception)
            {
                Debug.LogWarning(
                    $"[MinoCameraController] 无法设置 Tag「{bundle.gameObjectTag}」：{exception.Message}",
                    controller);
            }
        }

        if (bundle.hasGameObjectLayer)
        {
            gameObject.layer = bundle.gameObjectLayer;
        }

        SerializedObject serializedObject = new SerializedObject(controller);
        serializedObject.Update();

        int restoredCount = 0;
        restoredCount += ApplyReferenceGlobalId(serializedObject, "orbitFocus", bundle.orbitFocusGlobalId, controller);
        restoredCount += ApplyReferenceGlobalId(serializedObject, "displayTarget", bundle.displayTargetGlobalId, controller);
        restoredCount += ApplyReferenceGlobalId(serializedObject, "mainLightTransform", bundle.mainLightGlobalId, controller);

        serializedObject.ApplyModifiedProperties();

        Transform transform = gameObject.transform;
        if (bundle.hasTransform)
        {
            Undo.RecordObject(transform, undoName);
            transform.localPosition = bundle.localPosition;
            transform.localEulerAngles = bundle.localRotation;
            transform.localScale = bundle.localScale;
            EditorUtility.SetDirty(transform);
        }

        if (bundle.hasRuntimeOrbit)
        {
            controller.ApplyRuntimeOrbitState(
                bundle.orbitYaw,
                bundle.orbitPitch,
                bundle.smoothedOrbitDistance,
                applyTransformNow: true);
        }
        else if (bundle.hasTransform)
        {
            Vector3 eulerAngles = transform.localEulerAngles;
            controller.ApplyRuntimeOrbitState(
                eulerAngles.y,
                eulerAngles.x,
                controller.CurrentOrbitDistance,
                applyTransformNow: false);
        }

        if (camera != null && !string.IsNullOrEmpty(bundle.cameraComponentJson))
        {
            EditorUtility.SetDirty(camera);
        }

        if (Application.isPlaying)
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
            PrefabUtility.RecordPrefabInstancePropertyModifications(transform);
            if (camera != null)
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(camera);
            }
        }

        EditorUtility.SetDirty(controller);
        EditorUtility.SetDirty(gameObject);

        int missingCount = CountMissingReferences(bundle) - restoredCount;
        if (missingCount > 0)
        {
            Debug.LogWarning(
                $"[MinoCameraController] 导入完成，但有 {missingCount} 个场景引用在当前环境中无法解析，请在 Inspector 中重新绑定。",
                controller);
        }

        return true;
    }

    private static int ApplyReferenceGlobalId(
        SerializedObject serializedObject,
        string propertyName,
        string globalIdString,
        MinoCameraController controller)
    {
        if (string.IsNullOrEmpty(globalIdString))
        {
            return 0;
        }

        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
        {
            return 0;
        }

        if (!GlobalObjectId.TryParse(globalIdString, out GlobalObjectId globalId))
        {
            Debug.LogWarning($"[MinoCameraController] 无法解析引用 ID：{propertyName}", controller);
            return 0;
        }

        UnityEngine.Object resolved = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalId);
        if (resolved == null)
        {
            Debug.LogWarning($"[MinoCameraController] 未找到引用对象：{propertyName}（{globalIdString}）", controller);
            return 0;
        }

        property.objectReferenceValue = resolved;
        return 1;
    }

    private static int CountMissingReferences(MinoCameraParameterConfigBundle bundle)
    {
        int count = 0;
        if (!string.IsNullOrEmpty(bundle.orbitFocusGlobalId))
        {
            count++;
        }

        if (!string.IsNullOrEmpty(bundle.displayTargetGlobalId))
        {
            count++;
        }

        if (!string.IsNullOrEmpty(bundle.mainLightGlobalId))
        {
            count++;
        }

        return count;
    }

    private static string GetExportProfilesFullDirectory()
    {
        string profilesRelativePath = ExportProfilesAssetFolder.Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", profilesRelativePath));
    }

    private static bool IsSupportedConfigFilePath(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return false;
        }

        string extension = Path.GetExtension(filePath);
        if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
        {
            return filePath.EndsWith(ConfigFileExtension, StringComparison.OrdinalIgnoreCase)
                || filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(extension, ".minocamera", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadJsonFromFilePanel(string title)
    {
        if (!EnsureExportProfilesFolderExists())
        {
            EditorUtility.DisplayDialog(title, "导出目录 CameraParameterProfiles 不存在，请先导出一份配置。", "确定");
            return null;
        }

        string defaultDirectory = GetExportProfilesFullDirectory();
        string openPath = EditorUtility.OpenFilePanelWithFilters(
            title,
            defaultDirectory,
            new[]
            {
                "Mino 相机参数配置",
                ConfigFileExtensionWithoutDot,
                "JSON 文件",
                "json",
                "所有文件",
                "*"
            });

        if (string.IsNullOrEmpty(openPath) || !File.Exists(openPath))
        {
            return null;
        }

        if (!IsSupportedConfigFilePath(openPath))
        {
            EditorUtility.DisplayDialog(
                title,
                $"请选择 {ConfigFileExtension} 或 .json 配置文件。\n当前文件：{Path.GetFileName(openPath)}",
                "确定");
            return null;
        }

        return File.ReadAllText(openPath, Encoding.UTF8);
    }
}
