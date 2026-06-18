using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class ProjectList : MonoBehaviour
{
    public Transform ProjectTableContent;
    public GameObject ProjectItemPrefab;
    public Button RefreshBtn;
    
    // Start is called before the first frame update
    void Start()
    {
        if (RefreshBtn != null)
        {
            RefreshBtn.onClick.AddListener(RefreshProjectList);
        }
        
        // 初始化时刷新列表
        RefreshProjectList();
    }
    
    /// <summary>
    /// 刷新项目列表
    /// </summary>
    public void RefreshProjectList()
    {
        // 清空现有列表
        ClearProjectList();
        
        // 从ProjectManager获取Projects
        if (ProjectManager.XmlProject != null)
        {
            for (int i = 0; i < ProjectManager.XmlProject.Projects.Count; i++)
            {
                Project project = ProjectManager.XmlProject.Projects[i];
                AddProjectItem(project, i + 1);
            }
        }
        else
        {
            Debug.LogError("XmlProject为空，请确保ProjectManager已加载");
        }
    }
    
    /// <summary>
    /// 清空项目列表
    /// </summary>
    private void ClearProjectList()
    {
        if (ProjectTableContent != null)
        {
            foreach (Transform child in ProjectTableContent)
            {
                Destroy(child.gameObject);
            }
        }
    }

    /// <summary>
    /// 添加项目项到表格
    /// </summary>
    /// <param name="project">项目模型</param>
    /// <param name="index"></param>
    private void AddProjectItem(Project project, int index)
    {
        if (ProjectItemPrefab == null || ProjectTableContent == null)
        {
            Debug.LogError("ProjectItemPrefab或ProjectTableContent未设置");
            return;
        }
        
        // 实例化项目项
        GameObject projectItem = Instantiate(ProjectItemPrefab, ProjectTableContent);
        
        // 设置项目项的文本内容
        Text[] texts = projectItem.GetComponentsInChildren<Text>();
        if (texts.Length >= 3)
        {
            texts[0].text = index + "";
            texts[1].text = project.Name ?? "";
            /*texts[2].text = project.Model ?? "";*/
            texts[2].text = project.Comment ?? "";
        }
        
        // 添加点击事件
        Button[] buttons = projectItem.GetComponentsInChildren<Button>();
        if (buttons.Length > 0)
        {
            // 第一个按钮：编辑项目
            buttons[0].onClick.AddListener(() => OnProjectItemClick(project));
        }
        if (buttons.Length > 1)
        {
            // 第二个按钮：项目仿真
            buttons[1].onClick.AddListener(() => OnSimulateProjectClick(project));
        }
        if (buttons.Length > 2)
        {
            // 第三个按钮：项目记录
            buttons[2].onClick.AddListener(() => OnRecordProjectClick(project));
        }
        if (buttons.Length > 3)
        {
            // 第四个按钮：删除项目
            buttons[3].onClick.AddListener(() => OnDeleteProjectClick(project));
        }
    }
    
    /// <summary>
    /// 项目项点击事件
    /// </summary>
    /// <param name="project">被点击的项目模型</param>
    private void OnProjectItemClick(Project project)
    {
        Debug.Log($"点击了项目: {project.Name} (ID: {project.Id})");
        
        // 查找ProjectEdit
        ProjectEdit projectEdit = FindObjectOfType<ProjectEdit>(true);
        if (projectEdit != null)
        {
            // 将project赋值给ProjectEdit
            projectEdit.Project = project;
            // 显示ProjectEdit
            projectEdit.gameObject.SetActive(true);
            Debug.Log("ProjectEdit已显示并赋值");
        }
        else
        {
            Debug.LogError("未找到ProjectEdit，请确保场景中存在该组件");
            // 可以在这里添加创建ProjectEdit的逻辑（如果需要）
        }
    }

    /// <summary>
    /// 项目仿真点击事件
    /// </summary>
    /// <param name="project">要删除的项目模型</param>
    private void OnSimulateProjectClick(Project project)
    {
        Debug.Log($"点击了项目仿真: {project.Name} (ID: {project.Id})");
        RunManager.Project = project;
        RunManager.RunStatus = RunStatus.IDLE;
        RunManager.RunType = RunType.SIMULATION;
        GameObject.Find("SimulationPlatform").SetActive(false);
        SceneManager.LoadScene("RunScene");

    }

    /// <summary>
    /// 项目记录点击事件
    /// </summary>
    /// <param name="project">要删除的项目模型</param>
    private void OnRecordProjectClick(Project project)
    {
        Debug.Log($"点击了项目记录: {project.Name} (ID: {project.Id})");
    }

    /// <summary>
    /// 删除项目点击事件
    /// </summary>
    /// <param name="project">要删除的项目模型</param>
    private void OnDeleteProjectClick(Project project)
    {
        Debug.Log($"点击了删除项目: {project.Name} (ID: {project.Id})");
        
        // 调用PopupManager弹出删除提示
        PopupManager.Instance.ShowConfirmCancelPopup($"确定要删除项目 '{project.Name}' 吗？", (result) => {
            if (result)
            {
                // 用户确认删除
                Debug.Log($"删除项目: {project.Name} (ID: {project.Id})");
                
                // 从ProjectManager中删除项目
                ProjectManager.RemoveProject(project.Id);
                // 保存更改
                ProjectManager.Save();
                // 刷新项目列表
                RefreshProjectList();
                Debug.Log($"项目 {project.Name} 删除成功");
            }
            else
            {
                // 用户取消删除
                Debug.Log("用户取消删除项目");
            }
        });
    }
}
