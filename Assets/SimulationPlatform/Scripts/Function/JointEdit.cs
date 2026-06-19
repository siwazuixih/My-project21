using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

public class JointEdit : ModelImport
{
    public Button SaveJointBtn;
    public InputField JointNameInput;
    public InputField JointModelInput;
    public InputField JointCommentInput;
    public InputField LeadInput;
    public InputField PictchDiameterInput;
    public InputField TorqueInput;
    public InputField ThresholdInput;
    public InputField AngleInput;
    public InputField TravelInput;


    public JointModel Joint { get; set; }
    
    // Start is called before the first frame update
    void Start()
    {
        base.Start();

        if (SaveJointBtn != null)
        {
            SaveJointBtn.onClick.AddListener(OnSaveJointBtnClick);
        }

        // 初始化输入框值
        InitializeInputFields();
    }
    
    // 当GameObject激活时调用
    private void OnEnable()
    {
        // 显示对应文本
        InitializeInputFields();
        
        // 加载模型
        LoadModelFromJoint();
    }
    
    private void InitializeInputFields()
    {
        if (Joint != null)
        {
            if (JointNameInput != null)
            {
                JointNameInput.text = Joint.Name;
            }
            if (JointModelInput != null)
            {
                JointModelInput.text = Joint.Model;
            }
            if (JointCommentInput != null)
            {
                JointCommentInput.text = Joint.Comment;
            }
            if (LeadInput != null)
            {
                LeadInput.text = Joint.Param.Lead;
            }    
            if (PictchDiameterInput != null)
            {
                PictchDiameterInput.text = Joint.Param.PitchDiameter;
            }
            if (TorqueInput != null)
            {
                TorqueInput.text = Joint.Craft.Torque;
            }
            if (ThresholdInput != null)
            {
                ThresholdInput.text = Joint.Craft.Threshold;
            }
            if (AngleInput != null)
            {
                AngleInput.text = Joint.Craft.Angle;
            }
            if (TravelInput != null)
            {
                TravelInput.text = Joint.Craft.Travel;
            }
        }
    }

    private void OnSaveJointBtnClick()
    {
        Debug.Log("Save Joint button clicked");
        // 保存输入框值到joint对象
        SaveJointValues();
    }
    protected override void OnExitBtnClick()
    {
        base.OnExitBtnClick();
        
        // 将joint置为null
        Joint = null;
        
        // 清空输入框内容
        ClearInputFields();
        
        // 退出后刷新关节列表
        RefreshJointList();
    }
    
    /// <summary>
    /// 清空输入框内容
    /// </summary>
    private void ClearInputFields()
    {
        if (JointNameInput != null)
        {
            JointNameInput.text = "";
        }
        if (JointModelInput != null)
        {
            JointModelInput.text = "";
        }
        if (JointCommentInput != null)
        {
            JointCommentInput.text = "";
        }
        if (ModelPathInput != null)
        {
            ModelPathInput.text = "";
        }
        if (LeadInput != null)
        {
            LeadInput.text = "";
        }
        if (PictchDiameterInput != null)
        {
            PictchDiameterInput.text = "";
        }
        if (TorqueInput != null)
        {
            TorqueInput.text = "";
        }
        if (ThresholdInput != null)
        {
            ThresholdInput.text = "";
        }
        if (AngleInput != null)
        {
            AngleInput.text = "";
        }
        if (TravelInput != null)
        {
            TravelInput.text = "";
        }
    }
    
    /// <summary>
    /// 刷新关节列表
    /// </summary>
    private void RefreshJointList()
    {
        // 查找JointList组件
        JointList jointList = FindObjectOfType<JointList>();
        if (jointList != null)
        {
            jointList.RefreshJointList();
            Debug.Log("关节列表已刷新");
        }
        else
        {
            Debug.LogWarning("未找到JointList组件，无法刷新关节列表");
        }
    }



    private void SaveJointValues()
    {
        if (Joint == null)
        {
            Joint = new JointModel();
            ModelManager.AddJoint(Joint);
        }
        if (JointNameInput != null)
        {
            Joint.Name = JointNameInput.text;
        }
        if (JointModelInput != null)
        {
            Joint.Model = JointModelInput.text;
        }
        if (JointCommentInput != null)
        {
            Joint.Comment = JointCommentInput.text;
        }
        if (LeadInput != null)
        {
            Joint.Param.Lead = LeadInput.text;
        }
        if (PictchDiameterInput != null)
        {
            Joint.Param.PitchDiameter = PictchDiameterInput.text;
        }
        if (TorqueInput != null)
        {
            Joint.Craft.Torque = TorqueInput.text;
        }
        if (ThresholdInput != null)
        {
            Joint.Craft.Threshold = ThresholdInput.text;
        }
        if (AngleInput != null)
        {
            Joint.Craft.Angle = AngleInput.text;
        }
        if (TravelInput != null)
        {
            Joint.Craft.Travel = TravelInput.text;
        }

        // 生成uuid（如果还没有）
        if (string.IsNullOrEmpty(Joint.Id))
        {
            Joint.Id = Guid.NewGuid().ToString();
        }

        // 复制文件到uuid命名的文件夹下
        string physicalModelPath = PathTool.ResolvePhysicalPath(modelFilePath);
        bool needCopy = false;
        
        if (!string.IsNullOrEmpty(physicalModelPath) && System.IO.File.Exists(physicalModelPath))
        {
            if (Joint.Glb == null || string.IsNullOrEmpty(Joint.Glb.FilePath))
            {
                needCopy = true;
            }
            else
            {
                string existingFilePath = PathTool.ResolvePhysicalPath(Joint.Glb.FilePath);
                needCopy = !System.IO.File.Exists(existingFilePath);
            }
        }

        if (needCopy && !string.IsNullOrEmpty(physicalModelPath) && System.IO.File.Exists(physicalModelPath))
        {
            string folderName = Joint.Id;
            string targetPath = FileManager.CopyFileToProjectFiles(folderName, physicalModelPath);

            if (!string.IsNullOrEmpty(targetPath))
            {
                if (Joint.Glb == null)
                {
                    Joint.Glb = new GlbModel();
                }

                string relativePath = PathTool.GetRelativePathFromExecutableDir(targetPath);
                Joint.Glb.FilePath = relativePath;
                Debug.Log($"设置Joint.Glb.FilePath为相对路径: {relativePath}");
                Debug.Log($"原始完整路径: {targetPath}");
            }
        }

        // 直接保存到xml
        ModelManager.Save();
        MessageManage.ShowMessage("保存成功", 1);
        Debug.Log("Joint已存在，只更新值并保存");
    }
    
    /// <summary>
    /// 从Joint的glb.FilePath加载模型
    /// </summary>
    private void LoadModelFromJoint()
    {
        if (Joint != null && Joint.Glb != null && !string.IsNullOrEmpty(Joint.Glb.FilePath))
        {
            string path = LoadModelFromFile(Joint.Glb.FilePath);
            if (ModelPathInput != null)
            {
                ModelPathInput.text = path;
            }
        }
        else
        {
            Debug.Log("Joint或模型路径为空，无法加载模型");
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
