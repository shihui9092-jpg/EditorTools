using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 在 Hierarchy 每行右侧绘制激活开关，支持多选批量切换、Undo 与 Prefab Overrides 记录。
/// </summary>
[InitializeOnLoad]
public static class HierarchyHuoYueToggle
{
    private const string EnabledPrefsKey = "MinoTools.HierarchyHuoYueToggle.Enabled";
    private const string MenuPath = "Tools/MinoTools/Hierarchy工具/Hierarchy 行内激活开关";

    private const float ToggleWidth = 18f;
    private const float ToggleRightMargin = 20f;

    static HierarchyHuoYueToggle()
    {
        RegisterCallbacks();
    }

    private static void RegisterCallbacks()
    {
        EditorApplication.hierarchyWindowItemOnGUI -= DrawHierarchyToggle;
        EditorApplication.hierarchyWindowItemOnGUI += DrawHierarchyToggle;

        AssemblyReloadEvents.beforeAssemblyReload -= UnregisterCallbacks;
        AssemblyReloadEvents.beforeAssemblyReload += UnregisterCallbacks;

        EditorApplication.quitting -= UnregisterCallbacks;
        EditorApplication.quitting += UnregisterCallbacks;
    }

    private static void UnregisterCallbacks()
    {
        EditorApplication.hierarchyWindowItemOnGUI -= DrawHierarchyToggle;
        AssemblyReloadEvents.beforeAssemblyReload -= UnregisterCallbacks;
        EditorApplication.quitting -= UnregisterCallbacks;
    }

    [MenuItem(MenuPath)]
    private static void ToggleFeatureEnabled()
    {
        EditorPrefs.SetBool(EnabledPrefsKey, !IsFeatureEnabled());
    }

    [MenuItem(MenuPath, true)]
    private static bool ToggleFeatureEnabledValidate()
    {
        Menu.SetChecked(MenuPath, IsFeatureEnabled());
        return true;
    }

    private static bool IsFeatureEnabled()
    {
        return EditorPrefs.GetBool(EnabledPrefsKey, true);
    }

    private static void DrawHierarchyToggle(int instanceID, Rect rect)
    {
        if (!IsFeatureEnabled())
            return;

        Event currentEvent = Event.current;
        if (currentEvent.type != EventType.Repaint && currentEvent.type != EventType.MouseDown)
            return;

        GameObject gameObject = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
        if (gameObject == null || (gameObject.hideFlags & HideFlags.HideInHierarchy) != 0)
            return;

        Rect toggleRect = new Rect(rect.xMax - ToggleRightMargin, rect.y, ToggleWidth, rect.height);
        bool parentInactive = IsParentInactiveInHierarchy(gameObject);

        if (currentEvent.type == EventType.Repaint)
        {
            using (new EditorGUI.DisabledScope(parentInactive))
            {
                EditorGUI.Toggle(toggleRect, gameObject.activeSelf);
            }

            return;
        }

        if (!toggleRect.Contains(currentEvent.mousePosition))
            return;

        bool targetActive = !gameObject.activeSelf;
        ApplyActiveState(gameObject, targetActive);
        currentEvent.Use();
    }

    private static bool IsParentInactiveInHierarchy(GameObject gameObject)
    {
        Transform parent = gameObject.transform.parent;
        return parent != null && !parent.gameObject.activeInHierarchy;
    }

    private static GameObject[] GetToggleTargets(GameObject clickedObject)
    {
        GameObject[] selectedObjects = Selection.gameObjects;
        if (selectedObjects == null || selectedObjects.Length == 0)
            return new[] { clickedObject };

        for (int i = 0; i < selectedObjects.Length; i++)
        {
            if (selectedObjects[i] == clickedObject)
                return selectedObjects;
        }

        return new[] { clickedObject };
    }

    private static void ApplyActiveState(GameObject clickedObject, bool active)
    {
        if (!CanModifyActiveState())
            return;

        GameObject[] targets = GetToggleTargets(clickedObject);
        if (targets.Length == 0)
            return;

        bool hasPendingChange = false;
        for (int i = 0; i < targets.Length; i++)
        {
            GameObject target = targets[i];
            if (target != null && target.activeSelf != active)
            {
                hasPendingChange = true;
                break;
            }
        }

        if (!hasPendingChange)
            return;

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(active ? "启用 Hierarchy 物体" : "禁用 Hierarchy 物体");

        for (int i = 0; i < targets.Length; i++)
        {
            GameObject target = targets[i];
            if (target == null || target.activeSelf == active)
                continue;

            Undo.RecordObject(target, "Hierarchy 激活开关");
            target.SetActive(active);

            if (PrefabUtility.IsPartOfPrefabInstance(target))
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    private static bool CanModifyActiveState()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        return activeScene.IsValid() && activeScene.isLoaded;
    }
}
