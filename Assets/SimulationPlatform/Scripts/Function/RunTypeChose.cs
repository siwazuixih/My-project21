using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 运行记录类型筛选按钮组（全部/仿真/真实运行）。
/// 点击切换选中按钮，激活按钮 Image 透明度为 255，否则为 0；
/// 返回当前筛选条件并触发运行记录列表重新查询。
/// </summary>
public class RunTypeChose : MonoBehaviour
{
    /// <summary>全部按钮（默认激活）</summary>
    public Button AllBtn;

    /// <summary>仿真按钮</summary>
    public Button SimBtn;

    /// <summary>真实运行按钮</summary>
    public Button RealBtn;

    /// <summary>运行记录列表（点击切换后重新查询）</summary>
    public TaskList taskList;

    /// <summary>当前筛选条件（null=全部，SIMULATION=仿真，REALTIME=真实运行）</summary>
    public RunType? CurrentFilter { get; private set; }

    void Start()
    {
        if (AllBtn != null)
        {
            AllBtn.onClick.AddListener(() => OnBtnClicked(null, AllBtn));
        }
        if (SimBtn != null)
        {
            SimBtn.onClick.AddListener(() => OnBtnClicked(RunType.SIMULATION, SimBtn));
        }
        if (RealBtn != null)
        {
            RealBtn.onClick.AddListener(() => OnBtnClicked(RunType.REALTIME, RealBtn));
        }

        // 默认激活全部
        Select(null, AllBtn);
    }

    private void OnBtnClicked(RunType? mode, Button clicked)
    {
        Select(mode, clicked);

        // 触发运行记录列表重新查询
        if (taskList != null)
        {
            taskList.SetFilterMode(mode);
        }
        else
        {
            Debug.LogWarning("RunTypeChose 未绑定 taskList，切换筛选后无法刷新列表");
        }
    }

    /// <summary>
    /// 更新选中状态与按钮透明度：激活按钮 Image 透明度 255，其余 0。
    /// </summary>
    private void Select(RunType? mode, Button selected)
    {
        CurrentFilter = mode;
        SetButtonActive(AllBtn, selected == AllBtn);
        SetButtonActive(SimBtn, selected == SimBtn);
        SetButtonActive(RealBtn, selected == RealBtn);
    }

    private static void SetButtonActive(Button button, bool active)
    {
        if (button == null)
        {
            return;
        }
        Image image = button.GetComponent<Image>();
        if (image != null)
        {
            Color color = image.color;
            color.a = active ? 1f : 0f;
            image.color = color;
        }
    }
}
