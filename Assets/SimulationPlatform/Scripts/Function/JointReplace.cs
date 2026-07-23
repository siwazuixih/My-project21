using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.IO;
using System.Threading.Tasks;

public class JointReplace : MonoBehaviour
{
    public Dropdown JointSelectDropdown;
    public JointParamInfo JointParam;
    public Button CloseDropdownButton;
    public Button SelectJointButton;
    public Button ReplaceButton;

    public GameObject RootGameObject;

    public Simulation Simulation;

    public List<JointReplaceRecord> ReplaceRecords = new List<JointReplaceRecord>();

    public Transform tablecontent;
    public GameObject prefab;

    public Button NoReplacesButton;
    public Button ShowReplacesButton;
    public Button HideReplacesButton;
    public Button CollapseButton;
    public GameObject JointsPanel;

    private List<JointModel> jointList = new List<JointModel>();
    private bool _isPanelVisible = false;

    private void Awake()
    {
    }

    private void Start()
    {
        if (ReplaceButton != null)
        {
            ReplaceButton.onClick.AddListener(OnReplaceClicked);
        }
        if (SelectJointButton != null)
        {
            SelectJointButton.onClick.AddListener(PerformReplace);
            SelectJointButton.gameObject.SetActive(false);
        }
        if (JointSelectDropdown != null)
        {
            JointSelectDropdown.onValueChanged.AddListener(OnJointSelectChanged);
            JointSelectDropdown.gameObject.SetActive(false);
        }
        if (CloseDropdownButton != null)
        {
            CloseDropdownButton.onClick.AddListener(OnCloseDropdownClicked);
            CloseDropdownButton.gameObject.SetActive(false);
        }
        if (ShowReplacesButton != null)
        {
            ShowReplacesButton.onClick.AddListener(OnShowReplacesClicked);
        }
        if (HideReplacesButton != null)
        {
            HideReplacesButton.onClick.AddListener(OnHideReplacesClicked);
        }
        if (CollapseButton != null)
        {
            CollapseButton.onClick.AddListener(OnHideReplacesClicked);
        }
        UpdateReplacesButtonState();
    }

    private void OnReplaceClicked()
    {
        if (ModelCollisionHighlighter.selectedObject == null)
        {
            Debug.Log("没有高亮的物体可以替换");
            return;
        }

        if (JointSelectDropdown == null)
        {
            Debug.LogError("JointSelectDropdown未赋值");
            return;
        }

        PopulateJointDropdown();

        JointSelectDropdown.gameObject.SetActive(true);
        if (CloseDropdownButton != null)
        {
            CloseDropdownButton.gameObject.SetActive(true);
        }
        if (SelectJointButton != null)
        {
            SelectJointButton.gameObject.SetActive(true);
        }
        Debug.Log("已弹出接头选择下拉框");
    }

    private void PerformReplace()
    {
        if (ModelCollisionHighlighter.selectedObject == null)
        {
            Debug.Log("没有高亮的物体可以替换");
            return;
        }

        if (JointSelectDropdown.value < 0 || JointSelectDropdown.value >= jointList.Count)
        {
            Debug.Log("请先从下拉框中选择一个接头模型");
            return;
        }

        JointModel selectedJoint = jointList[JointSelectDropdown.value];
        if (selectedJoint.Glb == null || string.IsNullOrEmpty(selectedJoint.Glb.FilePath))
        {
            Debug.Log("选中的接头模型没有关联的GLB文件");
            return;
        }

        Transform highlightedTransform = ModelCollisionHighlighter.selectedObject.transform;
        Vector3 position = highlightedTransform.localPosition;
        Quaternion rotation = highlightedTransform.localRotation;
        Vector3 scale = highlightedTransform.localScale;
        Transform parent = highlightedTransform.parent;
        string oldObjectName = highlightedTransform.gameObject.name;

        string executableDir = PathTool.GetExecutableDirPath();
        string fullModelPath = Path.Combine(executableDir, selectedJoint.Glb.FilePath);

        if (File.Exists(fullModelPath))
        {
            GameObject oldObject = ModelCollisionHighlighter.selectedObject.gameObject;

            SaveReplaceRecord(oldObject, selectedJoint, position, rotation);

            if (Simulation != null)
            {
                Simulation.StartCoroutine(Simulation.InstantiateReplacedModel(fullModelPath, position, rotation, scale, parent, oldObject, selectedJoint, JointParam, Simulation.MissionController));
            }
            Debug.Log($"正在替换物体 {oldObjectName} 为 {selectedJoint.Name}");
        }
        else
        {
            Debug.LogError($"模型文件不存在: {fullModelPath}");
        }

        HidePanel();
    }

    private void SaveReplaceRecord(GameObject oldObject, JointModel selectedJoint, Vector3 position, Quaternion rotation)
    {
        if (oldObject == null || selectedJoint == null || RootGameObject == null)
        {
            return;
        }

        string relativePath = JointReplaceRecord.CalculateRelativePath(oldObject.transform, RootGameObject.transform);
        string hierarchyIndices = JointReplaceRecord.CalculateHierarchyIndices(oldObject.transform, RootGameObject.transform);
        Vector3 worldPosition = oldObject.transform.position;

        JointReplaceRecord record = new JointReplaceRecord(
            selectedJoint.Id,
            oldObject.name,
            relativePath,
            hierarchyIndices,
            position,
            rotation,
            worldPosition
        );

        ReplaceRecords.Add(record);
        Debug.Log($"已保存替换记录: 接头ID={selectedJoint.Id}, 被替换物体={oldObject.name}, 相对路径={relativePath}, 层级索引={hierarchyIndices}, 本地位置={position}, 本地旋转={rotation}, 世界坐标={worldPosition}");
        RefreshReplaceRecordList();
    }

    private void OnCloseDropdownClicked()
    {
        HidePanel();
        Debug.Log("已关闭接头选择下拉框");
    }

    public void HidePanel()
    {
        JointSelectDropdown.gameObject.SetActive(false);
        if (CloseDropdownButton != null)
        {
            CloseDropdownButton.gameObject.SetActive(false);
        }
        if (SelectJointButton != null)
        {
            SelectJointButton.gameObject.SetActive(false);
        }
        if (JointParam != null)
        {
            JointParam.gameObject.SetActive(false);
        }
    }

    public void RemoveReplaceRecord(JointReplaceRecord record)
    {
        if (record != null && ReplaceRecords.Contains(record))
        {
            RestoreReplacedObject(record);
            ReplaceRecords.Remove(record);
            Debug.Log($"已删除替换记录: 接头ID={record.JointId}, 被替换物体={record.ReplacedObjectName}");
            RefreshReplaceRecordList();
        }
    }

    private void RestoreReplacedObject(JointReplaceRecord record)
    {
        if (RootGameObject == null || record == null || string.IsNullOrEmpty(record.HierarchyIndices))
        {
            return;
        }

        Transform current = RootGameObject.transform;
        string[] indices = record.HierarchyIndices.Split('/');
        bool found = true;

        foreach (string indexStr in indices)
        {
            if (!int.TryParse(indexStr, out int index) || index < 0 || index >= current.childCount)
            {
                found = false;
                break;
            }
            current = current.GetChild(index);
        }

        if (!found || current == null)
        {
            Debug.LogWarning("未找到被替换的物体");
            return;
        }

        Transform parent = current.parent;
        if (parent == null)
        {
            return;
        }

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            ModelCollisionHighlighter highlighter = child.GetComponent<ModelCollisionHighlighter>();
            if (highlighter != null && highlighter.isReplacedJoint)
            {
                Destroy(child.gameObject);
                Debug.Log($"已销毁替换的接头: {child.name}");
                break;
            }
        }

        current.gameObject.SetActive(true);
        current.transform.localPosition = record.Position.GetVector3();
        current.transform.localRotation = record.Rotation.GetQuaternion();
        Debug.Log($"已恢复被替换的物体: {current.name}");
    }

    public void RefreshReplaceRecordList()
    {
        ClearReplaceRecordList();

        for (int i = 0; i < ReplaceRecords.Count; i++)
        {
            AddReplaceRecordItem(ReplaceRecords[i], i);
        }
        UpdateReplacesButtonState();
    }

    private void ClearReplaceRecordList()
    {
        if (tablecontent != null)
        {
            foreach (Transform child in tablecontent)
            {
                Destroy(child.gameObject);
            }
        }
    }

    private void AddReplaceRecordItem(JointReplaceRecord record, int index)
    {
        if (prefab == null || tablecontent == null)
        {
            Debug.LogError("prefab或tablecontent未设置");
            return;
        }

        GameObject itemGO = Instantiate(prefab, tablecontent);

        JointReplaceItem item = itemGO.GetComponent<JointReplaceItem>();
        if (item == null)
        {
            item = itemGO.AddComponent<JointReplaceItem>();
        }
        item.JointReplace = this;
        item.Record = record;
        item.Index = index;
    }

    private void PopulateJointDropdown()
    {
        if (JointSelectDropdown == null)
        {
            Debug.LogWarning("JointSelectDropdown未赋值");
            return;
        }

        JointSelectDropdown.options.Clear();
        jointList.Clear();

        if (ModelManager.XmlModel == null || ModelManager.XmlModel.Joints == null || ModelManager.XmlModel.Joints.Count == 0)
        {
            Debug.LogWarning("没有可用的接头模型");
            return;
        }

        foreach (var joint in ModelManager.XmlModel.Joints)
        {
            if (!string.IsNullOrEmpty(joint.Name) && joint.Glb != null && !string.IsNullOrEmpty(joint.Glb.FilePath))
            {
                Dropdown.OptionData option = new Dropdown.OptionData();
                option.text = joint.Name;
                JointSelectDropdown.options.Add(option);
                jointList.Add(joint);
            }
        }

        JointSelectDropdown.value = -1;
        Debug.Log("接头下拉菜单填充完成，共" + jointList.Count + "个接头");
    }

    private void OnJointSelectChanged(int value)
    {
        if (value >= 0 && value < jointList.Count)
        {
            JointModel selectedJoint = jointList[value];
            Debug.Log("选择了接头: " + selectedJoint.Name);

            if (JointParam != null)
            {
                JointParam.gameObject.SetActive(true);
                JointParam.SetJoint(selectedJoint);
            }
        }
    }

    private void UpdateReplacesButtonState()
    {
        bool hasRecords = ReplaceRecords != null && ReplaceRecords.Count > 0;

        if (NoReplacesButton != null)
        {
            NoReplacesButton.gameObject.SetActive(!hasRecords);
        }

        if (ShowReplacesButton != null)
        {
            ShowReplacesButton.gameObject.SetActive(hasRecords && !_isPanelVisible);
            ShowReplacesButton.GetComponentInChildren<Text>().text = $"查看已布置的接头({ReplaceRecords.Count})";
        }

        if (HideReplacesButton != null)
        {
            HideReplacesButton.gameObject.SetActive(hasRecords && _isPanelVisible);
            HideReplacesButton.GetComponentInChildren<Text>().text = $"查看已布置的接头({ReplaceRecords.Count})";
        }

        if (JointsPanel != null)
        {
            JointsPanel.SetActive(_isPanelVisible);
        }
    }

    private void OnShowReplacesClicked()
    {
        _isPanelVisible = true;
        RefreshReplaceRecordList();
    }

    private void OnHideReplacesClicked()
    {
        _isPanelVisible = false;
        UpdateReplacesButtonState();
    }
}
