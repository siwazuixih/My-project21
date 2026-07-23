using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UserList : MonoBehaviour
{
    public Transform tablecontent;
    public GameObject prefab;
    public Button AddButton;
    public UserEdit userEdit;

    // Start is called before the first frame update
    void Start()
    {
        RefreshUserList();
        
        if (AddButton != null)
        {
            AddButton.onClick.AddListener(OnAddClick);
        }
    }

    /// <summary>
    /// 新增用户点击事件
    /// </summary>
    private void OnAddClick()
    {
        if (userEdit != null)
        {
            gameObject.SetActive(false);
            userEdit.ShowEditPanel(null);
        }
        else
        {
            Debug.LogError("userEdit未设置");
        }
    }

    /// <summary>
    /// 刷新用户列表
    /// </summary>
    public void RefreshUserList()
    {
        ClearUserList();

        if (AccountManager.XmlAccount != null)
        {
            for (int i = 0; i < AccountManager.XmlAccount.Accounts.Count; i++)
            {
                Account account = AccountManager.XmlAccount.Accounts[i];
                AddUserItem(account);
            }
        }
        else
        {
            Debug.LogError("XmlAccount为空，请确保AccountManager已加载");
        }
    }

    /// <summary>
    /// 清空用户列表
    /// </summary>
    private void ClearUserList()
    {
        if (tablecontent != null)
        {
            foreach (Transform child in tablecontent)
            {
                Destroy(child.gameObject);
            }
        }
    }

    /// <summary>
    /// 添加用户项到表格
    /// </summary>
    /// <param name="account">账号模型</param>
    private void AddUserItem(Account account)
    {
        if (prefab == null || tablecontent == null)
        {
            Debug.LogError("prefab或tablecontent未设置");
            return;
        }

        // 实例化用户项
        GameObject userItemGO = Instantiate(prefab, tablecontent);

        // 添加UserItem组件并设置Account属性
        UserItem userItem = userItemGO.GetComponent<UserItem>();
        if (userItem == null)
        {
            userItem = userItemGO.AddComponent<UserItem>();
        }
        userItem.userList = this;
        userItem.userEdit = userEdit;
        userItem.Account = account;

        // 绑定按钮点击事件
        Button[] buttons = userItemGO.GetComponentsInChildren<Button>(true);
        if (buttons.Length > 0)
        {
            // 第一个按钮：编辑
            buttons[0].onClick.AddListener(userItem.OnEditClick);
        }
        if (buttons.Length > 1)
        {
            // 第二个按钮：重置
            buttons[1].onClick.AddListener(() => OnResetClick(userItem));
        }
        if (buttons.Length > 2)
        {
            // 第三个按钮：启用/禁用
            if (account.AccountName != "admin")
            {
                buttons[2].onClick.AddListener(() => OnEnableClick(userItem));
            } else
            {
                buttons[2].gameObject.SetActive(false);
            }
        }
    }

    /// <summary>
    /// 编辑用户点击事件
    /// </summary>
    /// <param name="userItem">被点击的用户项</param>
    private void OnEditClick(UserItem userItem)
    {
        if (userEdit != null && userItem != null && userItem.Account != null)
        {
            userEdit.ShowEditPanel(userItem.Account);
        }
        else
        {
            Debug.LogError("userEdit或userItem或Account为空");
        }
    }

    /// <summary>
    /// 重置用户点击事件
    /// </summary>
    /// <param name="account">被点击的账号</param>
    private void OnResetClick(UserItem userItem)
    {
        userItem.Account.Password = "123456";
        AccountManager.Save();

        OperationLogTool.RecordLog(OperationType.用户管理, $"重置用户 - 账号：{userItem.Account.AccountName}");
    }

    /// <summary>
    /// 启用/禁用用户点击事件
    /// </summary>
    /// <param name="account">被点击的账号</param>
    private void OnEnableClick(UserItem userItem)
    {
        // TODO: 实现启用/禁用用户逻辑
        userItem.Account.Valid = !userItem.Account.Valid;
        userItem.Account = userItem.Account;
        AccountManager.Save();
        RefreshUserList();

        string action = userItem.Account.Valid ? "启用用户" : "禁用用户";
        OperationLogTool.RecordLog(OperationType.用户管理, $"{action} - 账号：{userItem.Account.AccountName}");
    }
}
