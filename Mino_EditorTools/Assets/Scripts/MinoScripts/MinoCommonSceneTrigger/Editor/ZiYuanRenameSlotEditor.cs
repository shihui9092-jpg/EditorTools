using UnityEditor;
using UnityEngine;

/// <summary>
/// 通用资源凹槽的 Inspector 面板。
/// </summary>
[CustomEditor(typeof(ZiYuanRenameSlot))]
public class ZiYuanRenameSlotEditor : Editor
{
    private SerializedProperty 资源重命名配置数组属性;
    private string resultMessage;
    private MessageType resultMessageType = MessageType.None;

    private void OnEnable()
    {
        资源重命名配置数组属性 = serializedObject.FindProperty("assetRenameConfigs");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.HelpBox("可放入：图片(Texture2D/Sprite)、材质(Material)、预制体(Prefab)。", MessageType.Info);
        DrawRenameConfigArray();

        using (new EditorGUI.DisabledScope(!CanRenameAssetFile()))
        {
            if (GUILayout.Button("批量将凹槽文件改为自定义名称", GUILayout.Height(24f)))
            {
                RenameAssetFilesForTargets();
            }
        }

        if (!string.IsNullOrWhiteSpace(resultMessage))
        {
            EditorGUILayout.HelpBox(resultMessage, resultMessageType);
        }

        serializedObject.ApplyModifiedProperties();
    }

    private bool CanRenameAssetFile()
    {
        if (资源重命名配置数组属性 == null || !资源重命名配置数组属性.isArray)
            return false;

        for (int 索引 = 0; 索引 < 资源重命名配置数组属性.arraySize; 索引++)
        {
            SerializedProperty 配置项属性 = 资源重命名配置数组属性.GetArrayElementAtIndex(索引);
            SerializedProperty 资源属性 = 配置项属性.FindPropertyRelative("targetAsset");
            SerializedProperty 命名属性 = 配置项属性.FindPropertyRelative("customAssetName");

            if (资源属性 == null || 命名属性 == null)
                continue;

            if (资源属性.objectReferenceValue == null)
                continue;

            if (string.IsNullOrWhiteSpace(命名属性.stringValue))
                continue;

            return true;
        }

        return false;
    }

    private void RenameAssetFilesForTargets()
    {
        int successCount = 0;
        int failedCount = 0;
        string firstSuccessMessage = string.Empty;

        for (int 索引 = 0; 索引 < targets.Length; 索引++)
        {
            ZiYuanRenameSlot 资源凹槽组件 = targets[索引] as ZiYuanRenameSlot;
            if (资源凹槽组件 == null)
                continue;

            Undo.RecordObject(资源凹槽组件, "重命名凹槽资源文件");
            bool 重命名成功 = 资源凹槽组件.TryRenameAllAssetFiles(out string 结果消息);
            if (!重命名成功)
            {
                failedCount++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(firstSuccessMessage))
            {
                firstSuccessMessage = 结果消息;
            }

            successCount++;
            EditorUtility.SetDirty(资源凹槽组件);
        }

        if (successCount > 0)
        {
            resultMessageType = MessageType.Info;
            resultMessage = successCount == 1
                ? $"命名成功。{firstSuccessMessage}"
                : $"命名成功：{successCount} 个，失败：{failedCount} 个。";
        }
        else
        {
            resultMessageType = MessageType.Warning;
            resultMessage = "未完成命名，请检查凹槽资源或自定义文件名。";
        }

        serializedObject.Update();
    }

    private void DrawRenameConfigArray()
    {
        if (资源重命名配置数组属性 == null)
            return;

        if (资源重命名配置数组属性.arraySize <= 0)
        {
            资源重命名配置数组属性.arraySize = 1;
        }

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("资源重命名列表", EditorStyles.boldLabel);
        if (GUILayout.Button("+", GUILayout.Width(28f), GUILayout.Height(20f)))
        {
            int 新索引 = 资源重命名配置数组属性.arraySize;
            资源重命名配置数组属性.InsertArrayElementAtIndex(新索引);
            SerializedProperty 新配置项属性 = 资源重命名配置数组属性.GetArrayElementAtIndex(新索引);
            SerializedProperty 新资源属性 = 新配置项属性.FindPropertyRelative("targetAsset");
            SerializedProperty 新命名属性 = 新配置项属性.FindPropertyRelative("customAssetName");
            if (新资源属性 != null)
            {
                新资源属性.objectReferenceValue = null;
            }

            if (新命名属性 != null)
            {
                新命名属性.stringValue = string.Empty;
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4f);
        int 待删除索引 = -1;
        for (int 索引 = 0; 索引 < 资源重命名配置数组属性.arraySize; 索引++)
        {
            SerializedProperty 配置项属性 = 资源重命名配置数组属性.GetArrayElementAtIndex(索引);
            SerializedProperty 资源属性 = 配置项属性.FindPropertyRelative("targetAsset");
            SerializedProperty 命名属性 = 配置项属性.FindPropertyRelative("customAssetName");

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"配置项 {索引 + 1}", EditorStyles.boldLabel);
            if (GUILayout.Button("-", GUILayout.Width(28f), GUILayout.Height(18f)))
            {
                待删除索引 = 索引;
            }
            EditorGUILayout.EndHorizontal();

            Object 当前资源 = 资源属性.objectReferenceValue;
            Object 新资源 = EditorGUILayout.ObjectField("资源凹槽", 当前资源, typeof(Object), false);
            if (新资源 != 当前资源)
            {
                if (新资源 == null || ZiYuanRenameSlot.IsSupportedAsset(新资源))
                {
                    资源属性.objectReferenceValue = 新资源;
                }
            }

            if (资源属性.objectReferenceValue != null &&
                !ZiYuanRenameSlot.IsSupportedAsset(资源属性.objectReferenceValue))
            {
                EditorGUILayout.HelpBox("当前资源类型不受支持，已自动拦截。", MessageType.Warning);
                资源属性.objectReferenceValue = null;
            }

            EditorGUILayout.PropertyField(命名属性, new GUIContent("自定义文件名"));
            EditorGUILayout.EndVertical();
        }

        if (待删除索引 >= 0 && 资源重命名配置数组属性.arraySize > 1)
        {
            资源重命名配置数组属性.DeleteArrayElementAtIndex(待删除索引);
        }
    }
}
