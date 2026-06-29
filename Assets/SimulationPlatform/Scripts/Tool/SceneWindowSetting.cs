using UnityEngine;

/// <summary>
/// 单个场景的窗口大小配置脚本
/// </summary>
public class SceneWindowSetting : MonoBehaviour
{
    [Header("当前场景窗口配置")]
    [Tooltip("窗口宽度")]
    public int windowWidth = 1280; // 默认宽度（如主菜单：1280）

    [Tooltip("窗口高度")]
    public int windowHeight = 720; // 默认高度（如主菜单：720）

    [Tooltip("是否全屏（打包后生效，编辑器中仅调整窗口大小）")]
    public bool isFullScreen = false;

    private void Awake()
    {
        // 场景加载完成后，立即设置窗口大小（Awake() 比 Start() 执行更早，避免画面闪烁）
        SetSceneWindowSize();
    }

    /// <summary>
    /// 设置当前场景的窗口大小
    /// </summary>
    private void SetSceneWindowSize()
    {
        // 核心API：设置窗口分辨率
        // 参数说明：宽度、高度、是否全屏、刷新率（0=自动适配显示器刷新率）
        Screen.SetResolution(windowWidth, windowHeight, isFullScreen, 0);

        // 编辑器中额外提示（可选）
#if UNITY_EDITOR
        Debug.Log($"当前场景：{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
        Debug.Log($"窗口大小已设置为：{windowWidth}x{windowHeight}，全屏状态：{isFullScreen}");
#endif
    }

    // 可选：场景切换时，若需要保留该窗口大小，可在此处处理
    private void OnDestroy()
    {
        // 如需场景切换后恢复默认窗口大小，可在此处添加代码
        // 示例：Screen.SetResolution(1920, 1080, false, 0);
    }
}