using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class JointList : MonoBehaviour
{
    public Transform JointTableContent;
    public GameObject JointItemPrefab;
    public Button RefreshBtn;
    
    // Start is called before the first frame update
    void Start()
    {
        if (RefreshBtn != null)
        {
            RefreshBtn.onClick.AddListener(RefreshJointList);
        }
        
        // 初始化时刷新列表
        RefreshJointList();
    }
    
    /// <summary>
    /// 刷新关节列表
    /// </summary>
    public void RefreshJointList()
    {
        // 清空现有列表
        ClearJointList();
        
        // 从ModelManager获取Joints
        if (ModelManager.XmlModel != null)
        {
            for (int i = 0; i < ModelManager.XmlModel.Joints.Count; i++)
            {
                JointModel joint = ModelManager.XmlModel.Joints[i];
                AddJointItem(joint, i + 1);
            }
        }
        else
        {
            Debug.LogError("XmlModel为空，请确保ModelManager已加载");
        }
    }
    
    /// <summary>
    /// 清空关节列表
    /// </summary>
    private void ClearJointList()
    {
        if (JointTableContent != null)
        {
            foreach (Transform child in JointTableContent)
            {
                Destroy(child.gameObject);
            }
        }
    }

    /// <summary>
    /// 添加关节项到表格
    /// </summary>
    /// <param name="joint">关节模型</param>
    /// <param name="index"></param>
    private void AddJointItem(JointModel joint, int index)
    {
        if (JointItemPrefab == null || JointTableContent == null)
        {
            Debug.LogError("JointItemPrefab或JointTableContent未设置");
            return;
        }
        
        // 实例化关节项
        GameObject jointItem = Instantiate(JointItemPrefab, JointTableContent);
        
        // 设置关节项的文本内容
        Text[] texts = jointItem.GetComponentsInChildren<Text>();
        if (texts.Length >= 4)
        {
            texts[0].text = index + "";
            texts[1].text = joint.Name ?? "";
            texts[2].text = joint.Model ?? "";
            texts[3].text = joint.Comment ?? "";
        }
        
        // 添加点击事件
        Button[] buttons = jointItem.GetComponentsInChildren<Button>();
        if (buttons.Length > 0)
        {
            // 第一个按钮：编辑关节
            buttons[0].onClick.AddListener(() => OnJointItemClick(joint));
        }
        if (buttons.Length > 3)
        {
            // 第四个按钮：删除场景
            buttons[1].onClick.AddListener(() => OnDeleteJointClick(joint));
            buttons[1].GetComponentsInChildren<Text>()[0].text = buttons[3].GetComponentsInChildren<Text>()[0].text;
            buttons[2].gameObject.SetActive(false);
            buttons[3].gameObject.SetActive(false);
        }
    }
    
    /// <summary>
    /// 关节项点击事件
    /// </summary>
    /// <param name="joint">被点击的关节模型</param>
    private void OnJointItemClick(JointModel joint)
    {
        Debug.Log($"点击了关节: {joint.Name} (ID: {joint.Id})");
        
        // 查找JointEdit
        JointEdit jointEdit = FindObjectOfType<JointEdit>(true);
        if (jointEdit != null)
        {
            // 将joint赋值给JointEdit
            jointEdit.Joint = joint;
            // 显示JointEdit
            jointEdit.gameObject.SetActive(true);
            Debug.Log("JointEdit已显示并赋值");
        }
        else
        {
            Debug.LogError("未找到JointEdit，请确保场景中存在该组件");
            // 可以在这里添加创建JointEdit的逻辑（如果需要）
        }
    }
    
    /// <summary>
    /// 删除关节点击事件
    /// </summary>
    /// <param name="joint">要删除的关节模型</param>
    private void OnDeleteJointClick(JointModel joint)
    {
        Debug.Log($"点击了删除关节: {joint.Name} (ID: {joint.Id})");
        
        // 调用PopupManager弹出删除提示
        PopupManager.Instance.ShowConfirmCancelPopup($"确定要删除关节 '{joint.Name}' 吗？", (result) => {
            if (result)
            {
                // 用户确认删除
                Debug.Log($"删除关节: {joint.Name} (ID: {joint.Id})");
                
                // 从ModelManager中删除关节
                ModelManager.RemoveJoint(joint.Id);
                // 保存更改
                ModelManager.Save();
                // 刷新关节列表
                RefreshJointList();
                Debug.Log($"关节 {joint.Name} 删除成功");
            }
            else
            {
                // 用户取消删除
                Debug.Log("用户取消删除关节");
            }
        });
    }
}
