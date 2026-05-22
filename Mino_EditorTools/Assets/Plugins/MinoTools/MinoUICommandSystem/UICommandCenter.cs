using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>
/// UI 命令中心：
/// 1. 在 Inspector 里配置 commandId -> UnityEvent 映射。
/// 2. 按钮只需要传入 commandId，即可触发对应逻辑。
/// </summary>
public class UICommandCenter : MonoBehaviour
{
    [Serializable]
    public class UICommandEntry
    {
        [SerializeField] private string commandId;
        [SerializeField] private UnityEvent onExecute;

        public string CommandId => commandId;

        public void Execute()
        {
            onExecute?.Invoke();
        }
    }

    [Header("命令配置")]
    [SerializeField] private List<UICommandEntry> commandEntries = new List<UICommandEntry>();

    private readonly Dictionary<string, UICommandEntry> _commandMap = new Dictionary<string, UICommandEntry>();

    private void Awake()
    {
        RebuildCommandMap();
    }

    public void RebuildCommandMap()
    {
        _commandMap.Clear();

        for (int i = 0; i < commandEntries.Count; i++)
        {
            UICommandEntry entry = commandEntries[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.CommandId))
            {
                continue;
            }

            if (_commandMap.ContainsKey(entry.CommandId))
            {
                Debug.LogWarning($"检测到重复 commandId：{entry.CommandId}，后续同名项将被忽略。", this);
                continue;
            }

            _commandMap.Add(entry.CommandId, entry);
        }
    }

    public void Execute(string commandId)
    {
        if (string.IsNullOrWhiteSpace(commandId))
        {
            Debug.LogWarning("commandId 为空，无法执行命令。", this);
            return;
        }

        if (!_commandMap.TryGetValue(commandId, out UICommandEntry entry))
        {
            Debug.LogWarning($"未找到命令：{commandId}", this);
            return;
        }

        entry.Execute();
    }

    // ==============================
    // 下方是常见通用动作（可直接在 UnityEvent 中复用）
    // ==============================

    public void SetObjectActive(GameObject target)
    {
        if (target == null) return;
        target.SetActive(true);
    }

    public void SetObjectInactive(GameObject target)
    {
        if (target == null) return;
        target.SetActive(false);
    }

    public void ToggleObjectActive(GameObject target)
    {
        if (target == null) return;
        target.SetActive(!target.activeSelf);
    }

    public void SetTimeScale(float value)
    {
        Time.timeScale = Mathf.Max(0f, value);
    }

    public void LoadSceneByName(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogWarning("场景名为空，无法加载。", this);
            return;
        }

        SceneManager.LoadScene(sceneName);
    }

    public void ReloadActiveScene()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid())
        {
            Debug.LogWarning("当前激活场景无效，无法重载。", this);
            return;
        }

        SceneManager.LoadScene(activeScene.name);
    }

    public void QuitGame()
    {
        Application.Quit();
    }
}
