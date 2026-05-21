using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MinoTools.MaterialPropertyCleanup
{
    /// <summary>
    /// 清理材质中已不被当前 Shader 使用的冗余序列化属性。
    /// </summary>
    internal static class MatYuShengPropertyCleaner
    {
        [MenuItem("Tools/MinoTools/\u6750\u8d28\u5c5e\u6027\u6e05\u7406\u5de5\u5177/\u626b\u63cf\u5197\u4f59\u5c5e\u6027(\u4e0d\u4fee\u6539)")]
        private static void ScanUnusedMaterialProperties()
        {
            ProcessSelectedMaterials(applyChanges: false);
        }

        [MenuItem("Tools/MinoTools/\u6750\u8d28\u5c5e\u6027\u6e05\u7406\u5de5\u5177/\u6e05\u7406\u5197\u4f59\u5c5e\u6027")]
        private static void CleanUnusedMaterialProperties()
        {
            ProcessSelectedMaterials(applyChanges: true);
        }

        private static void ProcessSelectedMaterials(bool applyChanges)
        {
            List<string> materialPaths = CollectSelectedMaterialPaths();
            if (materialPaths.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "请在 Project 视图中选择材质或包含材质的文件夹。", "确定");
                return;
            }

            int changedCount = 0;
            int scannedCount = 0;
            int removedTex = 0;
            int removedFloat = 0;
            int removedColor = 0;

            try
            {
                for (int i = 0; i < materialPaths.Count; i++)
                {
                    string path = materialPaths[i];
                    float progress = (i + 1) / (float)materialPaths.Count;
                    EditorUtility.DisplayProgressBar(
                        applyChanges ? "清理材质冗余属性" : "扫描材质冗余属性",
                        path,
                        progress);

                    Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material == null || material.shader == null)
                    {
                        continue;
                    }

                    scannedCount++;
                    CleanupResult result = CleanupMaterial(material, applyChanges);
                    removedTex += result.removedTexEnv;
                    removedFloat += result.removedFloat;
                    removedColor += result.removedColor;

                    if (result.changed)
                    {
                        changedCount++;
                        Debug.Log($"{(applyChanges ? "已清理" : "检测到")}冗余属性: {path}");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (applyChanges && changedCount > 0)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            string summary =
                $"扫描材质: {scannedCount}\n" +
                $"{(applyChanges ? "实际修改" : "检测命中")}: {changedCount}\n" +
                $"冗余 TexEnv: {removedTex}\n" +
                $"冗余 Float: {removedFloat}\n" +
                $"冗余 Color: {removedColor}";
            EditorUtility.DisplayDialog(applyChanges ? "清理完成" : "扫描完成", summary, "确定");
        }

        private static List<string> CollectSelectedMaterialPaths()
        {
            HashSet<string> pathSet = new HashSet<string>();
            Object[] selections = Selection.GetFiltered(typeof(Object), SelectionMode.Assets);
            foreach (Object item in selections)
            {
                string path = AssetDatabase.GetAssetPath(item);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                if (path.EndsWith(".mat"))
                {
                    pathSet.Add(path);
                    continue;
                }

                if (AssetDatabase.IsValidFolder(path))
                {
                    string[] guids = AssetDatabase.FindAssets("t:Material", new[] { path });
                    foreach (string guid in guids)
                    {
                        string materialPath = AssetDatabase.GUIDToAssetPath(guid);
                        if (!string.IsNullOrEmpty(materialPath) && materialPath.EndsWith(".mat"))
                        {
                            pathSet.Add(materialPath);
                        }
                    }
                }
            }

            return pathSet.OrderBy(p => p).ToList();
        }

        private static CleanupResult CleanupMaterial(Material material, bool applyChanges)
        {
            HashSet<string> validPropertyNames = GetShaderPropertyNames(material.shader);
            SerializedObject serializedMaterial = new SerializedObject(material);
            SerializedProperty savedProperties = serializedMaterial.FindProperty("m_SavedProperties");
            if (savedProperties == null)
            {
                return default;
            }

            SerializedProperty texEnvs = savedProperties.FindPropertyRelative("m_TexEnvs");
            SerializedProperty floats = savedProperties.FindPropertyRelative("m_Floats");
            SerializedProperty colors = savedProperties.FindPropertyRelative("m_Colors");

            int removedTex = RemoveUnusedEntries(texEnvs, validPropertyNames, applyChanges);
            int removedFloat = RemoveUnusedEntries(floats, validPropertyNames, applyChanges);
            int removedColor = RemoveUnusedEntries(colors, validPropertyNames, applyChanges);

            bool changed = removedTex + removedFloat + removedColor > 0;
            if (changed && applyChanges)
            {
                Undo.RecordObject(material, "Clean Material Unused Properties");
                serializedMaterial.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(material);
            }

            return new CleanupResult
            {
                changed = changed,
                removedTexEnv = removedTex,
                removedFloat = removedFloat,
                removedColor = removedColor
            };
        }

        private static int RemoveUnusedEntries(SerializedProperty arrayProperty, HashSet<string> validNames, bool applyChanges)
        {
            if (arrayProperty == null || !arrayProperty.isArray)
            {
                return 0;
            }

            int removed = 0;
            for (int i = arrayProperty.arraySize - 1; i >= 0; i--)
            {
                SerializedProperty element = arrayProperty.GetArrayElementAtIndex(i);
                SerializedProperty key = element.FindPropertyRelative("first");
                if (key == null)
                {
                    continue;
                }

                if (validNames.Contains(key.stringValue))
                {
                    continue;
                }

                removed++;
                if (applyChanges)
                {
                    arrayProperty.DeleteArrayElementAtIndex(i);
                }
            }

            return removed;
        }

        private static HashSet<string> GetShaderPropertyNames(Shader shader)
        {
            int count = ShaderUtil.GetPropertyCount(shader);
            HashSet<string> names = new HashSet<string>();
            for (int i = 0; i < count; i++)
            {
                names.Add(ShaderUtil.GetPropertyName(shader, i));
            }
            return names;
        }

        private struct CleanupResult
        {
            public bool changed;
            public int removedTexEnv;
            public int removedFloat;
            public int removedColor;
        }
    }
}
