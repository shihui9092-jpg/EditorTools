using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 按钮命令绑定器：
/// 把按钮点击转换为 commandId，并交给 UICommandCenter 执行。
/// </summary>
[RequireComponent(typeof(Button))]
public class UIButtonCommandBinder : MonoBehaviour
{
    [Header("命令配置")]
    [SerializeField] private UICommandCenter commandCenter;
    [SerializeField] private string commandId;

    private Button _button;

    private void Awake()
    {
        _button = GetComponent<Button>();
    }

    private void OnEnable()
    {
        if (_button != null)
        {
            _button.onClick.AddListener(HandleClick);
        }
    }

    private void OnDisable()
    {
        if (_button != null)
        {
            _button.onClick.RemoveListener(HandleClick);
        }
    }

    public void HandleClick()
    {
        if (commandCenter == null)
        {
            Debug.LogWarning($"未绑定 UICommandCenter：{name}", this);
            return;
        }

        commandCenter.Execute(commandId);
    }
}
