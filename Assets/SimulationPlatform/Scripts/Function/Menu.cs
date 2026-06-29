using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class Menu : MonoBehaviour
{
    // 菜单引用
    public GameObject ProjectMenu;
    public GameObject ModelMenu;
    public GameObject PrivilegeMenu;
    
    // 面板引用
    public GameObject ContentPanel;
    private GameObject ProjectManagePanel;
    private GameObject ModelManagePanel;
    private GameObject PrivilegeManagePanel;
    
    // Start is called before the first frame update
    void Start()
    {
        // 查找面板
        FindPanels();
        
        // 添加点击事件监听
        AddClickListeners();
        
        // 默认显示所有可显示的面板
        ShowDefaultPanels();
    }
    
    private void FindPanels()
    {
        if (ContentPanel != null)
        {
            ProjectManagePanel = ContentPanel.transform.Find("ProjectManage").gameObject;
            ModelManagePanel = ContentPanel.transform.Find("ModelManage").gameObject;
            PrivilegeManagePanel = ContentPanel.transform.Find("PrivilegeManage").gameObject;
        }
    }
    
    private void AddClickListeners()
    {
        // 为ProjectMenu添加点击事件
        if (ProjectMenu != null)
        {
            Button btn = ProjectMenu.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.AddListener(ShowProjectPanel);
            }
        }
        
        // 为ModelMenu添加点击事件
        if (ModelMenu != null)
        {
            Button btn = ModelMenu.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.AddListener(ShowModelPanel);
            }
        }
        
        // 为PrivilegeMenu添加点击事件
        if (PrivilegeMenu != null)
        {
            Button btn = PrivilegeMenu.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.AddListener(ShowPrivilegePanel);
            }
        }
    }
    
    private void ShowDefaultPanels()
    {
        ProjectManagePanel.SetActive(false);
        ModelManagePanel.SetActive(false);
        PrivilegeManagePanel.SetActive(false);
        // 默认显示所有面板
        if (ProjectManagePanel != null && ProjectMenu.activeSelf)
        {
            ProjectManagePanel.SetActive(true);
        } 
        else if (ModelManagePanel != null && ModelMenu.activeSelf)
        {
            ModelManagePanel.SetActive(true);
        } 
        else if (PrivilegeManagePanel != null && PrivilegeMenu.activeSelf)
        {
            PrivilegeManagePanel.SetActive(true);
        }
    }
    
    private void ShowProjectPanel()
    {
        // 显示ProjectManage面板，隐藏其他面板
        if (ProjectManagePanel != null) ProjectManagePanel.SetActive(true);
        if (ModelManagePanel != null) ModelManagePanel.SetActive(false);
        if (PrivilegeManagePanel != null) PrivilegeManagePanel.SetActive(false);
    }
    
    private void ShowModelPanel()
    {
        // 显示ModelManage面板，隐藏其他面板
        if (ProjectManagePanel != null) ProjectManagePanel.SetActive(false);
        if (ModelManagePanel != null) ModelManagePanel.SetActive(true);
        if (PrivilegeManagePanel != null) PrivilegeManagePanel.SetActive(false);
    }
    
    private void ShowPrivilegePanel()
    {
        // 显示PrivilegeManage面板，隐藏其他面板
        if (ProjectManagePanel != null) ProjectManagePanel.SetActive(false);
        if (ModelManagePanel != null) ModelManagePanel.SetActive(false);
        if (PrivilegeManagePanel != null) PrivilegeManagePanel.SetActive(true);
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
