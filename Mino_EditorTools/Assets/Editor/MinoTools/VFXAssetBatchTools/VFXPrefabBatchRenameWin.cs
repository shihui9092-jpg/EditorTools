using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace ETools
{
    public class VFXPrefabBatchRenameWin : EditorWindow
    {
        private UnityEngine.Object sourcePrefab;
        private string sourceKeyName;
        private string targetKeyName;
        private Vector2 scrollPos;
        private List<string> dependencies;

        [MenuItem("Tools/MinoTools/特效资源工具/重命名Prefab")]
        private static void OpenWindow()
        {
            GetWindow<VFXPrefabBatchRenameWin>("重命名Prefab").Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            sourcePrefab = EditorGUILayout.ObjectField("Prefab", sourcePrefab, typeof(GameObject), false);
            sourceKeyName = EditorGUILayout.TextField("关键词", sourceKeyName);
            targetKeyName = EditorGUILayout.TextField("新关键词", targetKeyName);

            if (GUILayout.Button("开始重命名", GUILayout.Height(28)))
            {
                ExecuteRename();
            }

            EditorGUILayout.Space(8);
            if (sourcePrefab != null)
            {
                DrawDependencies();
            }
        }

        private void DrawDependencies()
        {
            string sourcePath = AssetDatabase.GetAssetPath(sourcePrefab);
            if (string.IsNullOrEmpty(sourcePath))
            {
                return;
            }

            dependencies = AssetDatabase.GetDependencies(sourcePath).OrderBy(p => p).ToList();
            if (dependencies.Count > 0)
            {
                dependencies.Remove(sourcePath);
            }

            EditorGUILayout.LabelField("依赖列表", EditorStyles.boldLabel);
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(220));
            foreach (string dep in dependencies)
            {
                EditorGUILayout.LabelField(dep);
            }
            EditorGUILayout.EndScrollView();
        }

        private void ExecuteRename()
        {
            if (sourcePrefab == null)
            {
                EditorUtility.DisplayDialog("警告", "请先选择 prefab 资源。", "确定");
                return;
            }

            if (string.IsNullOrWhiteSpace(sourceKeyName) || string.IsNullOrWhiteSpace(targetKeyName))
            {
                EditorUtility.DisplayDialog("警告", "关键词和新关键词都不能为空。", "确定");
                return;
            }

            string sourcePath = AssetDatabase.GetAssetPath(sourcePrefab);
            if (string.IsNullOrEmpty(sourcePath))
            {
                EditorUtility.DisplayDialog("警告", "选中对象不是有效的 prefab 资源。", "确定");
                return;
            }

            string src = sourceKeyName.Trim();
            string dst = targetKeyName.Trim();
            if (string.Equals(src, dst, StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("警告", "新关键词与旧关键词一致，无需重命名。", "确定");
                return;
            }

            List<string> paths = AssetDatabase.GetDependencies(sourcePath, true).Distinct().ToList();
            paths.Sort((a, b) => b.Length.CompareTo(a.Length));

            int renamedCount = 0;
            List<string> failedPaths = new List<string>();

            foreach (string path in paths)
            {
                string fileName = path.Substring(path.LastIndexOf("/") + 1);
                if (fileName.IndexOf(src, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                string extension = AssetDatabase.IsValidFolder(path) ? string.Empty : System.IO.Path.GetExtension(fileName);
                string nameWithoutExt = AssetDatabase.IsValidFolder(path) ? fileName : System.IO.Path.GetFileNameWithoutExtension(fileName);
                string newName = Regex.Replace(nameWithoutExt, Regex.Escape(src), dst, RegexOptions.IgnoreCase);

                if (string.Equals(nameWithoutExt, newName, StringComparison.Ordinal))
                {
                    continue;
                }

                string error = AssetDatabase.RenameAsset(path, newName);
                if (string.IsNullOrEmpty(error))
                {
                    renamedCount++;
                }
                else
                {
                    failedPaths.Add(path + " => " + error);
                }
            }

            AssetDatabase.Refresh();

            if (failedPaths.Count > 0)
            {
                string msg = "部分资源重命名失败，请查看 Console。\n失败数: " + failedPaths.Count;
                foreach (string item in failedPaths)
                {
                    Debug.LogError(item);
                }
                EditorUtility.DisplayDialog("完成", msg + "\n成功数: " + renamedCount, "确定");
                return;
            }

            EditorUtility.DisplayDialog("完成", "重命名完成，成功数量: " + renamedCount, "确定");
        }
    }
}
