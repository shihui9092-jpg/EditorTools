using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomEditor(typeof(MinoCameraController))]
public class MinoCameraControllerEditor : Editor
{
    private SerializedProperty isCameraLockedProperty;
    private SerializedProperty capturePresetIndexProperty;
    private SerializedProperty cameraPresetSlotsProperty;

    private ReorderableList presetSlotsReorderableList;
    private bool isParameterConfigExpanded = true;
    private bool isMainParametersExpanded = true;
    private bool isPresetListExpanded = true;
    private bool isPlayModeCaptureExpanded = true;

    // 快速保存：第一次点击某机位按钮锁定，第二次同按钮保存；-1 表示未待命
    private static readonly string[] MainParameterPropertyNames =
    {
        "orbitFocus",
        "displayTarget",
        "mainLightTransform",
        "enableDragRotateTarget",
        "enableDragRotateMainLight",
        "orbitHeight",
        "orbitOffset",
        "orbitDistance",
        "scrollZoomSpeed",
        "minOrbitDistance",
        "maxOrbitDistance",
        "orbitYawSpeed",
        "orbitPitchSpeed",
        "pitchMinLimit",
        "pitchMaxLimit",
        "targetYawRotateSpeed"
    };

    private void OnEnable()
    {
        isCameraLockedProperty = serializedObject.FindProperty("isCameraLocked");
        capturePresetIndexProperty = serializedObject.FindProperty("capturePresetIndex");
        cameraPresetSlotsProperty = serializedObject.FindProperty("cameraPresetSlots");

        int instanceId = target.GetInstanceID();
        isParameterConfigExpanded = SessionState.GetBool(GetParameterConfigFoldoutKey(instanceId), true);
        isMainParametersExpanded = SessionState.GetBool(GetMainParametersFoldoutKey(instanceId), true);
        isPresetListExpanded = SessionState.GetBool(GetPresetListFoldoutKey(instanceId), true);
        isPlayModeCaptureExpanded = SessionState.GetBool(GetPlayModeCaptureFoldoutKey(instanceId), true);

        presetSlotsReorderableList = new ReorderableList(serializedObject, cameraPresetSlotsProperty, true, true, true, true)
        {
            drawHeaderCallback = DrawPresetSlotsHeader,
            drawElementCallback = DrawPresetSlotElement,
            onAddCallback = OnAddPresetSlot,
            onRemoveCallback = OnRemovePresetSlot,
            elementHeightCallback = GetPresetSlotElementHeight
        };
    }

    private static string GetParameterConfigFoldoutKey(int instanceId)
    {
        return $"MinoCameraController.ParameterConfig.{instanceId}";
    }

    private static string GetMainParametersFoldoutKey(int instanceId)
    {
        return $"MinoCameraController.MainParams.{instanceId}";
    }

    private static string GetPresetListFoldoutKey(int instanceId)
    {
        return $"MinoCameraController.PresetList.{instanceId}";
    }

    private static string GetPlayModeCaptureFoldoutKey(int instanceId)
    {
        return $"MinoCameraController.PlayModeCapture.{instanceId}";
    }

    private static string GetQuickSaveArmKey(int instanceId)
    {
        return $"MinoCameraController.QuickSaveArm.{instanceId}";
    }

    private static int GetQuickSaveArmedIndex(int instanceId)
    {
        return SessionState.GetInt(GetQuickSaveArmKey(instanceId), -1);
    }

    private static void SetQuickSaveArmedIndex(int instanceId, int presetIndex)
    {
        SessionState.SetInt(GetQuickSaveArmKey(instanceId), presetIndex);
    }

    private void ClearQuickSaveArmState(MinoCameraController controller)
    {
        SetQuickSaveArmedIndex(controller.GetInstanceID(), -1);
    }

    private void SyncCameraLockState(MinoCameraController controller, bool locked)
    {
        isCameraLockedProperty.boolValue = locked;
        controller.SetCameraLocked(locked);
        if (!locked)
        {
            ClearQuickSaveArmState(controller);
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        int instanceId = target.GetInstanceID();
        MinoCameraController controller = (MinoCameraController)target;

        DrawFoldableParameterConfig(instanceId, controller);

        EditorGUILayout.Space(6f);
        DrawFoldableMainParameters(instanceId);

        EditorGUILayout.Space(6f);
        DrawFoldablePresetList(instanceId);

        EditorGUILayout.Space(6f);
        DrawFoldablePlayModeCapturePanel(instanceId);

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawFoldableParameterConfig(int instanceId, MinoCameraController controller)
    {
        isParameterConfigExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(
            isParameterConfigExpanded,
            "参数配置");

        if (isParameterConfigExpanded)
        {
            DrawParameterConfigContent(controller);
        }

        EditorGUILayout.EndFoldoutHeaderGroup();
        SessionState.SetBool(GetParameterConfigFoldoutKey(instanceId), isParameterConfigExpanded);
    }

    private void DrawParameterConfigContent(MinoCameraController controller)
    {
        bool requestExport = false;
        bool requestImport = false;

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("导出参数配置", GUILayout.Height(24f)))
            {
                requestExport = true;
            }

            if (GUILayout.Button("导入参数配置", GUILayout.Height(24f)))
            {
                requestImport = true;
            }
        }

        // 模态对话框（OpenFilePanel / DisplayDialog）不能在 HorizontalScope 内调用，否则会破坏布局栈
        if (requestExport)
        {
            MinoCameraControllerParameterIO.ExportToConfigFile(controller);
        }

        if (requestImport)
        {
            if (MinoCameraControllerParameterIO.ImportFromConfigFile(controller))
            {
                serializedObject.Update();
                Debug.Log("[MinoCameraController] 已导入参数配置。", controller);
            }
        }

        EditorGUILayout.HelpBox(
            "导出：保存 MinoCameraController、同物体 Camera 组件全部可序列化字段，相机 Transform（本地位置/旋转/缩放），\n" +
            "当前运行时轨道角（orbitYaw / orbitPitch / smoothedOrbitDistance），以及 GameObject 的 Tag / Layer。\n" +
            "机位列表与 orbitFocus / displayTarget / mainLightTransform 引用使用 GlobalObjectId 保存。\n" +
            $"自动保存到 {MinoCameraControllerParameterIO.ExportProfilesAssetFolder}/。\n" +
            "导入：打开上述目录，选择 .minocamera.json 覆盖当前挂载对象；跨场景引用缺失时需手动重绑。旧版配置无 Transform 字段时行为与此前一致。",
            MessageType.Info);
    }

    private void DrawFoldableMainParameters(int instanceId)
    {
        isMainParametersExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(
            isMainParametersExpanded,
            "相机与轨道参数");

        if (isMainParametersExpanded)
        {
            EditorGUI.indentLevel++;
            for (int i = 0; i < MainParameterPropertyNames.Length; i++)
            {
                SerializedProperty property = serializedObject.FindProperty(MainParameterPropertyNames[i]);
                if (property != null)
                {
                    EditorGUILayout.PropertyField(property, true);
                }
            }

            EditorGUI.indentLevel--;
        }

        EditorGUILayout.EndFoldoutHeaderGroup();
        SessionState.SetBool(GetMainParametersFoldoutKey(instanceId), isMainParametersExpanded);
    }

    private void DrawFoldablePresetList(int instanceId)
    {
        int slotCount = cameraPresetSlotsProperty != null ? cameraPresetSlotsProperty.arraySize : 0;
        string foldoutTitle = slotCount > 0 ? $"机位列表（{slotCount}）" : "机位列表";

        isPresetListExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(isPresetListExpanded, foldoutTitle);
        if (isPresetListExpanded)
        {
            presetSlotsReorderableList.DoLayoutList();
        }

        EditorGUILayout.EndFoldoutHeaderGroup();
        SessionState.SetBool(GetPresetListFoldoutKey(instanceId), isPresetListExpanded);
    }

    private void DrawFoldablePlayModeCapturePanel(int instanceId)
    {
        isPlayModeCaptureExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(
            isPlayModeCaptureExpanded,
            "运行模式 · 机位录制");

        if (isPlayModeCaptureExpanded)
        {
            DrawPlayModePresetCapturePanelContent();
        }

        EditorGUILayout.EndFoldoutHeaderGroup();
        SessionState.SetBool(GetPlayModeCaptureFoldoutKey(instanceId), isPlayModeCaptureExpanded);
    }

    private void DrawPresetSlotsHeader(Rect rect)
    {
        EditorGUI.LabelField(rect, "机位（名称 / 快捷键 / 镜头参数）");
    }

    private void DrawPresetSlotElement(Rect rect, int index, bool isActive, bool isFocused)
    {
        SerializedProperty slotProperty = cameraPresetSlotsProperty.GetArrayElementAtIndex(index);
        SerializedProperty nameProperty = slotProperty.FindPropertyRelative("presetName");
        SerializedProperty keyProperty = slotProperty.FindPropertyRelative("activationKey");
        SerializedProperty viewProperty = slotProperty.FindPropertyRelative("view");

        float lineHeight = EditorGUIUtility.singleLineHeight;
        float spacing = EditorGUIUtility.standardVerticalSpacing;
        float y = rect.y + 2f;

        Rect nameRect = new Rect(rect.x, y, rect.width * 0.45f, lineHeight);
        Rect keyRect = new Rect(rect.x + rect.width * 0.47f, y, rect.width * 0.53f, lineHeight);
        y += lineHeight + spacing;

        EditorGUI.PropertyField(nameRect, nameProperty, GUIContent.none);
        EditorGUI.PropertyField(keyRect, keyProperty, new GUIContent("快捷键"));

        float viewHeight = EditorGUI.GetPropertyHeight(viewProperty, true);
        Rect viewRect = new Rect(rect.x, y, rect.width, viewHeight);
        EditorGUI.PropertyField(viewRect, viewProperty, new GUIContent("镜头参数"), true);
    }

    private float GetPresetSlotElementHeight(int index)
    {
        SerializedProperty slotProperty = cameraPresetSlotsProperty.GetArrayElementAtIndex(index);
        SerializedProperty viewProperty = slotProperty.FindPropertyRelative("view");
        float lineHeight = EditorGUIUtility.singleLineHeight;
        float spacing = EditorGUIUtility.standardVerticalSpacing;
        return lineHeight + spacing + EditorGUI.GetPropertyHeight(viewProperty, true) + 6f;
    }

    private void OnAddPresetSlot(ReorderableList list)
    {
        MinoCameraController controller = (MinoCameraController)target;
        int newIndex = list.serializedProperty.arraySize;
        controller.AddPresetSlot($"机位{newIndex + 1}");
        serializedObject.Update();
        capturePresetIndexProperty.intValue = list.serializedProperty.arraySize - 1;
    }

    private void OnRemovePresetSlot(ReorderableList list)
    {
        if (list.serializedProperty.arraySize <= 1)
        {
            EditorUtility.DisplayDialog("删除机位", "至少需要保留 1 个机位。", "确定");
            return;
        }

        MinoCameraController controller = (MinoCameraController)target;
        int removeIndex = list.index >= 0 ? list.index : list.serializedProperty.arraySize - 1;
        if (!controller.RemovePresetSlot(removeIndex))
        {
            return;
        }

        serializedObject.Update();
        int count = list.serializedProperty.arraySize;
        if (count > 0)
        {
            capturePresetIndexProperty.intValue = Mathf.Clamp(capturePresetIndexProperty.intValue, 0, count - 1);
        }
    }

    private void DrawPlayModePresetCapturePanelContent()
    {
        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "进入 Play 模式后：调整到满意镜头 → 锁定相机 (L) → 保存到选中机位。\n" +
                "切换：各机位快捷键（未按 Shift）；保存：Shift + 该机位快捷键。",
                MessageType.Info);
            EditorGUILayout.PropertyField(isCameraLockedProperty, new GUIContent("锁定相机（预览）"));
            DrawCapturePresetIndexPopup();
            return;
        }

        MinoCameraController controller = (MinoCameraController)target;

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(isCameraLockedProperty, new GUIContent("锁定相机"));
        if (EditorGUI.EndChangeCheck())
        {
            SyncCameraLockState(controller, isCameraLockedProperty.boolValue);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(controller.IsCameraLocked ? "解锁相机 (L)" : "锁定相机 (L)", GUILayout.Height(24f)))
            {
                SyncCameraLockState(controller, !controller.IsCameraLocked);
            }
        }

        DrawCapturePresetIndexPopup();

        int presetIndex = capturePresetIndexProperty.intValue;
        string presetLabel = GetPresetDisplayName(presetIndex);

        using (new EditorGUI.DisabledScope(!controller.IsCameraLocked))
        {
            EditorGUILayout.HelpBox(
                controller.IsCameraLocked
                    ? $"将保存到「{presetLabel}」：世界坐标、欧拉角、轨道高度/偏移/距离。"
                    : "建议先锁定相机再保存，避免保存后镜头被轨道逻辑继续带动。",
                controller.IsCameraLocked ? MessageType.None : MessageType.Warning);
        }

        if (GUILayout.Button($"保存当前镜头到「{presetLabel}」", GUILayout.Height(28f)))
        {
            SaveCurrentViewToPreset(controller, presetIndex);
        }

        EditorGUILayout.HelpBox("快速保存：首次点击机位按钮锁定相机，再次点击同一按钮保存。", MessageType.None);
        DrawPresetQuickSaveButtons(controller);
    }

    private void DrawCapturePresetIndexPopup()
    {
        int count = cameraPresetSlotsProperty != null ? cameraPresetSlotsProperty.arraySize : 0;
        if (count == 0)
        {
            EditorGUILayout.HelpBox("无机位槽位，请先在机位列表中添加。", MessageType.Warning);
            return;
        }

        string[] options = new string[count];
        for (int i = 0; i < count; i++)
        {
            options[i] = GetPresetDisplayName(i);
        }

        int index = Mathf.Clamp(capturePresetIndexProperty.intValue, 0, count - 1);
        int newIndex = EditorGUILayout.Popup("保存目标机位", index, options);
        capturePresetIndexProperty.intValue = newIndex;
    }

    private string GetPresetDisplayName(int index)
    {
        if (cameraPresetSlotsProperty == null || index < 0 || index >= cameraPresetSlotsProperty.arraySize)
        {
            return $"机位 {index + 1}";
        }

        SerializedProperty slotProperty = cameraPresetSlotsProperty.GetArrayElementAtIndex(index);
        SerializedProperty nameProperty = slotProperty.FindPropertyRelative("presetName");
        SerializedProperty keyProperty = slotProperty.FindPropertyRelative("activationKey");

        string name = string.IsNullOrWhiteSpace(nameProperty.stringValue) ? $"机位{index + 1}" : nameProperty.stringValue;
        KeyCode key = (KeyCode)keyProperty.intValue;
        if (key == KeyCode.None)
        {
            return name;
        }

        return $"{name} ({key})";
    }

    private void DrawPresetQuickSaveButtons(MinoCameraController controller)
    {
        int count = cameraPresetSlotsProperty.arraySize;
        if (count == 0)
        {
            return;
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("快速保存", EditorStyles.miniLabel);

        int instanceId = controller.GetInstanceID();
        int armedIndex = GetQuickSaveArmedIndex(instanceId);

        const int buttonsPerRow = 4;
        int rowCount = Mathf.CeilToInt(count / (float)buttonsPerRow);
        for (int row = 0; row < rowCount; row++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int col = 0; col < buttonsPerRow; col++)
                {
                    int index = row * buttonsPerRow + col;
                    if (index >= count)
                    {
                        break;
                    }

                    SerializedProperty slotProperty = cameraPresetSlotsProperty.GetArrayElementAtIndex(index);
                    string shortName = slotProperty.FindPropertyRelative("presetName").stringValue;
                    if (string.IsNullOrWhiteSpace(shortName))
                    {
                        shortName = $"{index + 1}";
                    }

                    string buttonLabel = GetQuickSaveButtonLabel(shortName, index, armedIndex, controller.IsCameraLocked);
                    if (GUILayout.Button(buttonLabel, GUILayout.Height(22f)))
                    {
                        HandleQuickSaveButtonClick(controller, index);
                    }
                }
            }
        }
    }

    private static string GetQuickSaveButtonLabel(string shortName, int index, int armedIndex, bool isCameraLocked)
    {
        if (armedIndex != index)
        {
            return shortName;
        }

        return isCameraLocked ? $"{shortName} ·保存" : $"{shortName} ·锁定";
    }

    private void HandleQuickSaveButtonClick(MinoCameraController controller, int presetIndex)
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("快速保存", "请在运行模式下使用快速保存。", "确定");
            return;
        }

        int instanceId = controller.GetInstanceID();
        int armedIndex = GetQuickSaveArmedIndex(instanceId);
        capturePresetIndexProperty.intValue = presetIndex;

        if (armedIndex == presetIndex && controller.IsCameraLocked)
        {
            SaveCurrentViewToPreset(controller, presetIndex);
            SetQuickSaveArmedIndex(instanceId, -1);
            return;
        }

        if (!controller.IsCameraLocked)
        {
            SyncCameraLockState(controller, true);
        }

        SetQuickSaveArmedIndex(instanceId, presetIndex);
        serializedObject.ApplyModifiedProperties();

        string label = GetPresetDisplayName(presetIndex);
        Debug.Log($"[MinoCameraController] 已锁定相机，再次点击「{label}」将保存当前镜头。", controller);
    }

    private void SaveCurrentViewToPreset(MinoCameraController controller, int presetIndex)
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("机位保存", "请在运行模式下保存机位。", "确定");
            return;
        }

        if (!controller.CaptureCurrentViewToPreset(presetIndex))
        {
            EditorUtility.DisplayDialog("机位保存", "保存失败：机位下标无效。", "确定");
            return;
        }

        capturePresetIndexProperty.intValue = presetIndex;
        serializedObject.Update();
        EditorUtility.SetDirty(controller);

        string label = GetPresetDisplayName(presetIndex);
        SerializedProperty slotProperty = cameraPresetSlotsProperty.GetArrayElementAtIndex(presetIndex);
        SerializedProperty viewProperty = slotProperty.FindPropertyRelative("view");
        SerializedProperty worldPosition = viewProperty.FindPropertyRelative("worldPosition");
        Debug.Log($"[MinoCameraController] 已保存到「{label}」，Pos={worldPosition.vector3Value}", controller);
    }
}
