using UnityEditor;
using UnityEngine;

/// <summary>
/// 本体材质控制组件的 Inspector 按钮扩展。
/// </summary>
[CustomEditor(typeof(PiLiangMatMaker))]
public class PiLiangMatMakerEditor : Editor
{
    private SerializedProperty 材质生成配置数组属性;
    private SerializedProperty 总文件夹名属性;

    private void OnEnable()
    {
        材质生成配置数组属性 = serializedObject.FindProperty("materialGenerateConfigs");
        总文件夹名属性 = serializedObject.FindProperty("totalFolderName");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawMaterialConfigArray();
        EditorGUILayout.Space(6f);
        EditorGUILayout.PropertyField(总文件夹名属性, new GUIContent("总文件夹名（创建于 Assets 下）"));
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(8f);

        using (new EditorGUI.DisabledScope(!HasAnyMaterialConfigured()))
        {
            if (GUILayout.Button("生成当前材质属性的新材质球", GUILayout.Height(28f)))
            {
                GenerateMaterialForTargets();
            }
        }
    }

    private bool HasAnyMaterialConfigured()
    {
        if (材质生成配置数组属性 == null || !材质生成配置数组属性.isArray)
            return false;

        for (int 索引 = 0; 索引 < 材质生成配置数组属性.arraySize; 索引++)
        {
            SerializedProperty 配置项属性 = 材质生成配置数组属性.GetArrayElementAtIndex(索引);
            SerializedProperty 本体材质属性 = 配置项属性.FindPropertyRelative("bodyMaterial");
            if (本体材质属性 != null && 本体材质属性.objectReferenceValue != null)
                return true;
        }

        return false;
    }

    private void DrawMaterialConfigArray()
    {
        if (材质生成配置数组属性 == null)
            return;

        if (材质生成配置数组属性.arraySize <= 0)
        {
            材质生成配置数组属性.arraySize = 1;
        }

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("材质生成配置列表", EditorStyles.boldLabel);
        if (GUILayout.Button("+", GUILayout.Width(28f), GUILayout.Height(20f)))
        {
            int 新索引 = 材质生成配置数组属性.arraySize;
            材质生成配置数组属性.InsertArrayElementAtIndex(新索引);
            SerializedProperty 新配置项属性 = 材质生成配置数组属性.GetArrayElementAtIndex(新索引);
            SerializedProperty 新材质属性 = 新配置项属性.FindPropertyRelative("bodyMaterial");
            SerializedProperty 新文件夹属性 = 新配置项属性.FindPropertyRelative("generatedFolderName");
            if (新材质属性 != null)
            {
                新材质属性.objectReferenceValue = null;
            }

            if (新文件夹属性 != null)
            {
                新文件夹属性.stringValue = string.Empty;
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4f);
        int 待删除索引 = -1;

        for (int 索引 = 0; 索引 < 材质生成配置数组属性.arraySize; 索引++)
        {
            SerializedProperty 配置项属性 = 材质生成配置数组属性.GetArrayElementAtIndex(索引);
            SerializedProperty 本体材质属性 = 配置项属性.FindPropertyRelative("bodyMaterial");
            SerializedProperty 生成文件夹名属性 = 配置项属性.FindPropertyRelative("generatedFolderName");

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"配置项 {索引 + 1}", EditorStyles.boldLabel);
            if (GUILayout.Button("-", GUILayout.Width(28f), GUILayout.Height(18f)))
            {
                待删除索引 = 索引;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.PropertyField(本体材质属性, new GUIContent("本体材质球"));
            EditorGUILayout.PropertyField(生成文件夹名属性, new GUIContent("生成文件夹名"));
            EditorGUILayout.EndVertical();
        }

        if (待删除索引 >= 0 && 材质生成配置数组属性.arraySize > 1)
        {
            材质生成配置数组属性.DeleteArrayElementAtIndex(待删除索引);
        }
    }

    private void GenerateMaterialForTargets()
    {
        for (int 索引 = 0; 索引 < targets.Length; 索引++)
        {
            PiLiangMatMaker 材质工具组件 = targets[索引] as PiLiangMatMaker;
            if (材质工具组件 == null)
                continue;

            Undo.RecordObject(材质工具组件, "生成材质副本");
            材质工具组件.GenerateMaterialFromCurrent();
            EditorUtility.SetDirty(材质工具组件);
        }

        serializedObject.Update();
    }
}
