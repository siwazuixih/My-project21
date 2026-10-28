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
        bool isNewJoint = Joint == null;

        // 快照旧值，用于生成编辑前后的字段对比日志
        string oldName = Joint?.Name ?? "";
        string oldModel = Joint?.Model ?? "";
        string oldComment = Joint?.Comment ?? "";
        string oldLead = Joint?.Param?.Lead ?? "";
        string oldPitch = Joint?.Param?.PitchDiameter ?? "";
        string oldTorque = Joint?.Craft?.Torque ?? "";
        string oldThreshold = Joint?.Craft?.Threshold ?? "";
        string oldAngle = Joint?.Craft?.Angle ?? "";
        string oldTravel = Joint?.Craft?.Travel ?? "";
        string oldModelFile = Joint?.Glb?.FilePath ?? "";

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
        Joint.Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

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

        if (isNewJoint)
        {
            // 新增：记录已填写的字段
            string detail = $"添加接头模型 - 名称：{Joint.Name}";
            if (!string.IsNullOrEmpty(Joint.Model)) detail += $"，模型：{Joint.Model}";
            if (!string.IsNullOrEmpty(Joint.Comment)) detail += $"，备注：{Joint.Comment}";
            if (!string.IsNullOrEmpty(Joint.Param.Lead)) detail += $"，导程：{Joint.Param.Lead}";
            if (!string.IsNullOrEmpty(Joint.Param.PitchDiameter)) detail += $"，节圆直径：{Joint.Param.PitchDiameter}";
            if (!string.IsNullOrEmpty(Joint.Craft.Torque)) detail += $"，转矩：{Joint.Craft.Torque}";
            if (!string.IsNullOrEmpty(Joint.Craft.Threshold)) detail += $"，阈值：{Joint.Craft.Threshold}";
            if (!string.IsNullOrEmpty(Joint.Craft.Angle)) detail += $"，角度：{Joint.Craft.Angle}";
            if (!string.IsNullOrEmpty(Joint.Craft.Travel)) detail += $"，行程：{Joint.Craft.Travel}";
            string fileInfo = OperationLogTool.FormatModelFileInfo(Joint.Glb?.FilePath);
            if (!string.IsNullOrEmpty(fileInfo)) detail += $"，模型文件：{fileInfo}";
            OperationLogTool.RecordLog(OperationType.模型管理, detail);
        }
        else
        {
            // 编辑：只记录实际变化的字段（Time 为内部修改时间，不参与对比）
            List<string> changes = new List<string>();
            AddChangedField(changes, "名称", oldName, Joint.Name);
            AddChangedField(changes, "模型", oldModel, Joint.Model);
            AddChangedField(changes, "备注", oldComment, Joint.Comment);
            AddChangedField(changes, "导程", oldLead, Joint.Param.Lead);
            AddChangedField(changes, "节圆直径", oldPitch, Joint.Param.PitchDiameter);
            AddChangedField(changes, "转矩", oldTorque, Joint.Craft.Torque);
            AddChangedField(changes, "阈值", oldThreshold, Joint.Craft.Threshold);
            AddChangedField(changes, "角度", oldAngle, Joint.Craft.Angle);
            AddChangedField(changes, "行程", oldTravel, Joint.Craft.Travel);

            string oldModelFileName = string.IsNullOrEmpty(oldModelFile)
                ? ""
                : Path.GetFileName(oldModelFile.Replace('\\', '/'));
            string newModelFileName = Joint.Glb != null && !string.IsNullOrEmpty(Joint.Glb.FilePath)
                ? Path.GetFileName(Joint.Glb.FilePath.Replace('\\', '/'))
                : "";
            if (!string.Equals(oldModelFileName, newModelFileName, StringComparison.Ordinal))
            {
                string changedFileInfo = OperationLogTool.FormatModelFileInfo(Joint.Glb?.FilePath);
                if (!string.IsNullOrEmpty(changedFileInfo))
                {
                    changes.Add($"模型文件：{changedFileInfo}");
                }
            }

            string editDetail = changes.Count > 0
                ? $"编辑接头模型 - {string.Join("，", changes)}"
                : $"编辑接头模型 - 名称：{Joint.Name}";
            OperationLogTool.RecordLog(OperationType.模型管理, editDetail);
        }
    }

    /// <summary>
    /// 比较字段新旧值，有变化时加入变更列表。
    /// </summary>
    private static void AddChangedField(List<string> changes, string fieldName, string oldValue, string newValue)
    {
        if (!string.Equals(oldValue ?? "", newValue ?? "", StringComparison.Ordinal))
        {
            string old = string.IsNullOrEmpty(oldValue) ? "空" : oldValue;
            changes.Add($"{fieldName}：{old}→{newValue}");
        }
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
