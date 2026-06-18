using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

public class ProjectEdit : ModelImport
{
    public Button SaveProjectBtn;
    public InputField ProjectNameInput;
    public InputField ProjectModelInput;
    public InputField ProjectCommentInput;
    public Dropdown ProjectDropdown;
    
    public Project Project { get; set; }
    
    // 场景ID列表，用于下拉菜单选择
    private List<string> sceneIdList = new List<string>();
    private string selectedSceneId;

    // Start is called before the first frame update
    void Start()
    {
        base.Start();

        if (SaveProjectBtn != null)
        {
            SaveProjectBtn.onClick.AddListener(OnSaveProjectBtnClick);
        }

        if (ProjectDropdown != null)
        {
            ProjectDropdown.onValueChanged.AddListener(OnSceneDropdownValueChanged);
        }

        // 初始化输入框值
        //InitializeInputFields();
    }
    
    // 当GameObject激活时调用
    private void OnEnable()
    {
        // 显示对应文本
        InitializeInputFields();
        
        // 填充场景下拉菜单
        PopulateSceneDropdown();

        // 加载模型
        if (Project != null && Project.Scene != null && Project.Scene.Id != null)
        {
            LoadModelFromProject(Project.Scene.Id);
        }
    }
    
    private void InitializeInputFields()
    {
        if (Project != null)
        {
            if (ProjectNameInput != null)
            {
                ProjectNameInput.text = Project.Name;
            }
            /*if (ProjectModelInput != null)
            {
                ProjectModelInput.text = Project.Model;
            }*/
            if (ProjectCommentInput != null)
            {
                ProjectCommentInput.text = Project.Comment;
            }
        }
    }

    private void OnSaveProjectBtnClick()
    {
        Debug.Log("Save Project button clicked");
        // 保存输入框值到Project对象
        SaveProjectValues();
    }
    protected override void OnExitBtnClick()
    {
        base.OnExitBtnClick();
        
        // 将Project置为null
        Project = null;
        
        // 清空输入框内容
        ClearInputFields();
        
        // 退出后刷新项目列表
        RefreshProjectList();
    }
    
    /// <summary>
    /// 清空输入框内容
    /// </summary>
    private void ClearInputFields()
    {
        if (ProjectNameInput != null)
        {
            ProjectNameInput.text = "";
        }
        if (ProjectCommentInput != null)
        {
            ProjectCommentInput.text = "";
        }
    }
    
    /// <summary>
    /// 刷新项目列表
    /// </summary>
    private void RefreshProjectList()
    {
        // 查找ProjectList组件
        ProjectList projectList = FindObjectOfType<ProjectList>();
        if (projectList != null)
        {
            projectList.RefreshProjectList();
            Debug.Log("项目列表已刷新");
        }
        else
        {
            Debug.LogWarning("未找到ProjectList组件，无法刷新项目列表");
        }
    }



    private void SaveProjectValues()
    {
        if (Project == null)
        {
            Project = new Project();
            ProjectManager.AddProject(Project);
        }
        if (ProjectNameInput != null)
        {
            Project.Name = ProjectNameInput.text;
        }
        /*if (ProjectModelInput != null)
        {
            Project.Model = ProjectModelInput.text;
        }*/
        if (ProjectCommentInput != null)
        {
            Project.Comment = ProjectCommentInput.text;
        }

        // 生成uuid（如果还没有）
        if (string.IsNullOrEmpty(Project.Id))
        {
            Project.Id = Guid.NewGuid().ToString();
        }

        // 更新Project的Scene
        if (Project.Scene == null)
        {
            Project.Scene = new Scene();
        }
        Project.Scene.Id = selectedSceneId;
        Debug.Log("Project.Scene.Id已更新为: " + selectedSceneId);

        // 直接保存到xml
        ProjectManager.Save();
        MessageManage.ShowMessage("保存成功", 1);
        Debug.Log("Project已存在，只更新值并保存");
    }

    /// <summary>
    /// 填充场景下拉菜单
    /// </summary>
    private void PopulateSceneDropdown()
    {
        if (ProjectDropdown == null)
        {
            Debug.LogWarning("ProjectDropdown未赋值");
            return;
        }
        
        // 清空下拉菜单和ID列表
        ProjectDropdown.options.Clear();
        sceneIdList.Clear();
        
        // 获取所有场景
        var scenes = ModelManager.XmlModel.Scenes;
        if (scenes == null || scenes.Count == 0)
        {
            Debug.LogWarning("没有可用的场景");
            return;
        }

        selectedSceneId = null;

        // 为每个场景创建选项
        foreach (var scene in scenes)
        {
            if (!string.IsNullOrEmpty(scene.Name))
            {
                Dropdown.OptionData option = new Dropdown.OptionData();
                option.text = scene.Name;
                ProjectDropdown.options.Add(option);
                sceneIdList.Add(scene.Id);
                if (selectedSceneId == null)
                {
                    selectedSceneId = scene.Id;
                }
            }
        }
        
        // 如果Project有Scene，设置默认选中项
        if (Project != null && Project.Scene != null && !string.IsNullOrEmpty(Project.Scene.Id))
        {
            int index = sceneIdList.IndexOf(Project.Scene.Id);
            if (index >= 0)
            {
                ProjectDropdown.value = index;
                selectedSceneId = Project.Scene.Id;
            }
        }
        LoadModelFromProject(selectedSceneId);

        Debug.Log("场景下拉菜单填充完成，共" + ProjectDropdown.options.Count + "个场景");
    }
    
    /// <summary>
    /// 处理场景下拉菜单选择事件
    /// </summary>
    private void OnSceneDropdownValueChanged(int value)
    {
        if (value >= 0 && value < sceneIdList.Count)
        {
            selectedSceneId = sceneIdList[value];
            Debug.Log("选择了场景ID: " + selectedSceneId);
            LoadModelFromProject(selectedSceneId);
        }
    }
    
    /// <summary>
    /// 从Project的glb.FilePath加载模型
    /// </summary>
    private void LoadModelFromProject(string sceneId)
    {
        if (sceneId != null)
        {
            var sceneModel = ModelManager.GetScene(sceneId);
            if (sceneModel != null && sceneModel.Glb != null && !string.IsNullOrEmpty(sceneModel.Glb.FilePath))
            {
                LoadModelFromFile(sceneModel.Glb.FilePath);
                return;
            }
        }
        Debug.Log("Scene或模型路径为空，无法加载模型");
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
