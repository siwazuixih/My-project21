using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SceneList : MonoBehaviour
{
    public Transform SceneTableContent;
    public GameObject SceneItemPrefab;
    public Button RefreshBtn;
    
    // Start is called before the first frame update
    void Start()
    {
        if (RefreshBtn != null)
        {
            RefreshBtn.onClick.AddListener(RefreshSceneList);
        }
        
        // 初始化时刷新列表
        RefreshSceneList();
    }
    
    /// <summary>
    /// 刷新关节列表
    /// </summary>
    public void RefreshSceneList()
    {
        // 清空现有列表
        ClearSceneList();
        
        // 从ModelManager获取Scenes
        if (ModelManager.XmlModel != null)
        {
            for (int i = 0; i < ModelManager.XmlModel.Scenes.Count; i++)
            {
                SceneModel scene = ModelManager.XmlModel.Scenes[i];
                AddSceneItem(scene, i + 1);
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
    private void ClearSceneList()
    {
        if (SceneTableContent != null)
        {
            foreach (Transform child in SceneTableContent)
            {
                Destroy(child.gameObject);
            }
        }
    }

    /// <summary>
    /// 添加关节项到表格
    /// </summary>
    /// <param name="scene">关节模型</param>
    /// <param name="index"></param>
    private void AddSceneItem(SceneModel scene, int index)
    {
        if (SceneItemPrefab == null || SceneTableContent == null)
        {
            Debug.LogError("SceneItemPrefab或SceneTableContent未设置");
            return;
        }
        
        // 实例化关节项
        GameObject sceneItem = Instantiate(SceneItemPrefab, SceneTableContent);
        
        // 设置关节项的文本内容
        Text[] texts = sceneItem.GetComponentsInChildren<Text>();
        if (texts.Length >= 3)
        {
            texts[0].text = index + "";
            texts[1].text = scene.Name ?? "";
            /*texts[2].text = scene.Model ?? "";*/
            texts[2].text = scene.Comment ?? "";
        }
        
        // 添加点击事件
        Button[] buttons = sceneItem.GetComponentsInChildren<Button>();
        if (buttons.Length > 0)
        {
            // 第一个按钮：编辑场景
            buttons[0].onClick.AddListener(() => OnSceneItemClick(scene));
        }
        if (buttons.Length > 3)
        {
            // 第四个按钮：删除场景
            buttons[1].onClick.AddListener(() => OnDeleteSceneClick(scene));
            buttons[1].GetComponentsInChildren<Text>()[0].text = buttons[3].GetComponentsInChildren<Text>()[0].text;
            buttons[2].gameObject.SetActive(false);
            buttons[3].gameObject.SetActive(false);
        }
    }
    
    /// <summary>
    /// 关节项点击事件
    /// </summary>
    /// <param name="scene">被点击的关节模型</param>
    private void OnSceneItemClick(SceneModel scene)
    {
        Debug.Log($"点击了关节: {scene.Name} (ID: {scene.Id})");
        
        // 查找SceneEdit
        SceneEdit SceneEdit = FindObjectOfType<SceneEdit>(true);
        if (SceneEdit != null)
        {
            // 将scene赋值给SceneEdit
            SceneEdit.Scene = scene;
            // 显示SceneEdit
            SceneEdit.gameObject.SetActive(true);
            Debug.Log("SceneEdit已显示并赋值");
        }
        else
        {
            Debug.LogError("未找到SceneEdit，请确保场景中存在该组件");
            // 可以在这里添加创建SceneEdit的逻辑（如果需要）
        }
    }
    
    /// <summary>
    /// 删除场景点击事件
    /// </summary>
    /// <param name="scene">要删除的场景模型</param>
    private void OnDeleteSceneClick(SceneModel scene)
    {
        Debug.Log($"点击了删除场景: {scene.Name} (ID: {scene.Id})");
        
        // 调用PopupManager弹出删除提示
        // 注意：这里假设PopupManager有ShowConfirm方法，如果没有，需要根据实际情况调整
        PopupManager.Instance.ShowConfirmCancelPopup($"确定要删除场景 '{scene.Name}' 吗？", (result) => {
            if (result)
            {
                // 用户确认删除
                Debug.Log($"删除场景: {scene.Name} (ID: {scene.Id})");
                
                // 从ModelManager中删除场景
                ModelManager.RemoveScene(scene.Id);
                // 保存更改
                ModelManager.Save();
                // 刷新场景列表
                RefreshSceneList();
                Debug.Log($"场景 {scene.Name} 删除成功");
            }
            else
            {
                // 用户取消删除
                Debug.Log("用户取消删除场景");
            }
        });
    }
}
