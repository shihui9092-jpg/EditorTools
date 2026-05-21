//----------------------------------------------
//			  Transform值一键复位
// Copyright © 2012-2015 bobsong.net
//----------------------------------------------

using UnityEngine;
using UnityEditor;

[CanEditMultipleObjects]
[CustomEditor(typeof(Transform), true)]
/// <summary>
/// 自定义 Transform Inspector：
/// 在默认三轴编辑基础上，提供 P/R/S 一键复位按钮，并支持多选对象批量编辑。
/// </summary>
public class TransformKuaiSuResetInspector : Editor
{
	private const float ResetButtonWidth = 20f;
	private const float RotationFieldMinWidth = 30f;
	private const float FloatDifferenceThreshold = 0.0001f;

	// 通过序列化属性读取/写入 Transform 的本地位移、旋转、缩放
	private SerializedProperty localPositionProperty;
	private SerializedProperty localRotationProperty;
	private SerializedProperty localScaleProperty;

	private void OnEnable()
	{
		localPositionProperty = serializedObject.FindProperty("m_LocalPosition");
		localRotationProperty = serializedObject.FindProperty("m_LocalRotation");
		localScaleProperty = serializedObject.FindProperty("m_LocalScale");
	}

	/// <summary>
	/// 开始绘制Transform
	/// </summary>
	public override void OnInspectorGUI()
	{
		float cachedLabelWidth = EditorGUIUtility.labelWidth;
		EditorGUIUtility.labelWidth = 15f;

		try
		{
			serializedObject.Update();
			DrawPosition();
			DrawRotation();
			DrawScale();
			serializedObject.ApplyModifiedProperties();
		}
		finally
		{
			EditorGUIUtility.labelWidth = cachedLabelWidth;
		}
	}

	/// <summary>
	/// 绘制坐标
	/// </summary>
	private void DrawPosition()
	{
		using (new EditorGUILayout.HorizontalScope())
		{
			bool reset = GUILayout.Button("P", GUILayout.Width(ResetButtonWidth));

			EditorGUILayout.PropertyField(localPositionProperty.FindPropertyRelative("x"));
			EditorGUILayout.PropertyField(localPositionProperty.FindPropertyRelative("y"));
			EditorGUILayout.PropertyField(localPositionProperty.FindPropertyRelative("z"));

			if (reset) localPositionProperty.vector3Value = Vector3.zero;
		}
	}

	/// <summary>
	/// 绘制缩放
	/// </summary>
	private void DrawScale()
	{
		using (new EditorGUILayout.HorizontalScope())
		{
			bool reset = GUILayout.Button("S", GUILayout.Width(ResetButtonWidth));

			EditorGUILayout.PropertyField(localScaleProperty.FindPropertyRelative("x"));
			EditorGUILayout.PropertyField(localScaleProperty.FindPropertyRelative("y"));
			EditorGUILayout.PropertyField(localScaleProperty.FindPropertyRelative("z"));

			if (reset) localScaleProperty.vector3Value = Vector3.one;
		}
	}

	#region 旋转处理（四元数在 Inspector 中需要额外转换）
	enum Axes : int
	{
		None = 0,
		X = 1,
		Y = 2,
		Z = 4,
		All = 7,
	}

	private Axes CheckDifference(Transform targetTransform, Vector3 original)
	{
		Vector3 next = targetTransform.localEulerAngles;

		Axes axes = Axes.None;

		if (Differs(next.x, original.x)) axes |= Axes.X;
		if (Differs(next.y, original.y)) axes |= Axes.Y;
		if (Differs(next.z, original.z)) axes |= Axes.Z;

		return axes;
	}

	private Axes CheckDifference(SerializedProperty property)
	{
		Axes axes = Axes.None;

		if (property.hasMultipleDifferentValues)
		{
			// 多选对象时，用首个对象的欧拉角作为比较基准，
			// 再逐个对象检查 XYZ 哪些轴存在差异。
			Vector3 original = property.quaternionValue.eulerAngles;

			foreach (Object obj in serializedObject.targetObjects)
			{
				axes |= CheckDifference(obj as Transform, original);
				if (axes == Axes.All) break;
			}
		}
		return axes;
	}

	/// <summary>
	/// 绘制一个可编辑的浮点输入区域
	/// </summary>
	/// <param name="hidden">是否用 -- 表示多选且值不一致</param>
	private static bool FloatField(string name, ref float value, bool hidden, GUILayoutOption opt)
	{
		float newValue = value;
		EditorGUI.BeginChangeCheck();

		if (!hidden)
		{
			newValue = EditorGUILayout.FloatField(name, newValue, opt);
		}
		else
		{
			float.TryParse(EditorGUILayout.TextField(name, "--", opt), out newValue);
		}

		if (EditorGUI.EndChangeCheck() && Differs(newValue, value))
		{
			value = newValue;
			return true;
		}
		return false;
	}

	/// <summary>
	/// 比较两个浮点数是否有有效差异（避免 Mathf.Approximately 过于敏感）。
	/// </summary>

	private static bool Differs(float a, float b) { return Mathf.Abs(a - b) > FloatDifferenceThreshold; }

	/// <summary>
	/// 绘制旋转
	/// </summary>
	private void DrawRotation()
	{
		using (new EditorGUILayout.HorizontalScope())
		{
			bool reset = GUILayout.Button("R", GUILayout.Width(ResetButtonWidth));

			// Inspector 显示时将角度归一化到 [-180, 180]，便于手工编辑
			Vector3 visible = ((Transform)serializedObject.targetObject).localEulerAngles;

			visible.x = WrapAngle(visible.x);
			visible.y = WrapAngle(visible.y);
			visible.z = WrapAngle(visible.z);

			Axes changed = CheckDifference(localRotationProperty);
			Axes altered = Axes.None;

			GUILayoutOption opt = GUILayout.MinWidth(RotationFieldMinWidth);

			if (FloatField("X", ref visible.x, (changed & Axes.X) != 0, opt)) altered |= Axes.X;
			if (FloatField("Y", ref visible.y, (changed & Axes.Y) != 0, opt)) altered |= Axes.Y;
			if (FloatField("Z", ref visible.z, (changed & Axes.Z) != 0, opt)) altered |= Axes.Z;

			if (reset)
			{
				localRotationProperty.quaternionValue = Quaternion.identity;
			}
			else if (altered != Axes.None)
			{
				RegisterUndo("Change Rotation", serializedObject.targetObjects);

				foreach (Object obj in serializedObject.targetObjects)
				{
					Transform t = obj as Transform;
					// 仅覆写被用户修改的轴，其他轴保持原值
					Vector3 v = t.localEulerAngles;

					if ((altered & Axes.X) != 0) v.x = visible.x;
					if ((altered & Axes.Y) != 0) v.y = visible.y;
					if ((altered & Axes.Z) != 0) v.z = visible.z;

					t.localEulerAngles = v;
				}
			}
		}
	}

	/// <summary>
	/// 将角度规范到 [-180, 180] 区间
	/// </summary>

	[System.Diagnostics.DebuggerHidden]
	[System.Diagnostics.DebuggerStepThrough]
	private static float WrapAngle(float angle)
	{
		while (angle > 180f) angle -= 360f;
		while (angle < -180f) angle += 360f;
		return angle;
	}


	/// <summary>
	/// 为指定对象创建撤销记录
	/// </summary>
	private static void RegisterUndo(string name, params Object[] objects)
	{
		if (objects != null && objects.Length > 0)
		{
			UnityEditor.Undo.RecordObjects(objects, name);

			foreach (Object obj in objects)
			{
				if (obj == null) continue;
				EditorUtility.SetDirty(obj);
			}
		}
	}
	#endregion

}
