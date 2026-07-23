using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class MenuItem : MonoBehaviour
{
    public List<GameObject> menuItems;
    public List<GameObject> contentPanels;

    void Start()
    {
        BindAllMenuClickEvent();
    }

    // 自动给所有菜单按钮绑定点击事件
    void BindAllMenuClickEvent()
    {
        for (int i = 0; i < menuItems.Count; i++)
        {
            GameObject menuObj = menuItems[i];
            if (menuObj == null) continue;

            // 获取按钮组件（UGUI Button）
            Button btn = menuObj.GetComponent<Button>();
            if (btn == null)
            {
                Debug.LogWarning($"菜单 {menuObj.name} 身上没有Button组件，无法绑定点击事件");
                continue;
            }

            // 清除原有事件，防止重复绑定
            btn.onClick.RemoveAllListeners();
            // 传入当前索引i
            int tempIndex = i;
            btn.onClick.AddListener(() =>
            {
                OnClickMenuItem(tempIndex);
            });
        }
    }

    /// <summary>
    /// 点击菜单项触发，传入当前点击的菜单项索引
    /// </summary>
    /// <param name="index">菜单项下标</param>
    public void OnClickMenuItem(int index)
    {
        // 索引越界保护
        if (index < 0 || index >= contentPanels.Count)
        {
            Debug.LogError($"索引{index}超出面板列表范围");
            return;
        }

        // 1. 全部面板先隐藏
        foreach (GameObject panel in contentPanels)
        {
            if (panel != null)
                panel.SetActive(false);
        }
        foreach (GameObject menu in menuItems)
        {
            menu.GetComponentInChildren<PrivilegeControl>()?.SetChose(false);
        }

        // 2. 激活当前选中的面板
        GameObject targetPanel = contentPanels[index];
        targetPanel.SetActive(true);

        // 3. 在当前面板下查找 PrivilegeControl 组件
        PrivilegeControl ctrl = menuItems[index].GetComponentInChildren<PrivilegeControl>();
        if (ctrl != null)
        {
            // 调用SetChose，传入true
            ctrl.SetChose(true);
        }
        else
        {
            Debug.LogWarning($"面板 {targetPanel.name} 下未找到 PrivilegeControl");
        }
    }
}