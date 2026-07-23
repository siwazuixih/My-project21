using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UserEdit : MonoBehaviour
{
    public List<Text> TitleText;
    public InputField AccountNameInput;
    public InputField UserNameInput;
    public InputField DepartmentInput;
    public InputField PasswordInput;
    public Button ConfirmButton;
    public Button CancelButton;
    public Button BackButton;
    public UserList userList;
    public Toggle ValidToggle;
    public PrivilegeToggleControl privilegeToggleControl;

    private Text toggleStatusLabel;

    private Account currentAccount;

    void Start()
    {
        if (ConfirmButton != null)
        {
            ConfirmButton.onClick.AddListener(OnConfirmClick);
        }
        if (CancelButton != null)
        {
            CancelButton.onClick.AddListener(OnCancelClick);
        }
        if (BackButton != null)
        {
            BackButton.onClick.AddListener(OnCancelClick);
        }
        if (ValidToggle != null)
        {
            toggleStatusLabel = ValidToggle.GetComponentInChildren<Text>();
            ValidToggle.onValueChanged.AddListener(OnValidToggleChanged);
        }

        //gameObject.SetActive(false);
    }

    public void ShowEditPanel(Account account)
    {
        currentAccount = account;

        if (TitleText != null)
        {
            TitleText.ForEach(item => item.text = account == null ? "新建用户" : "编辑用户");
        }

        if (account == null)
        {
            if (AccountNameInput != null)
            {
                AccountNameInput.text = "";
                AccountNameInput.interactable = true;
            }
            if (UserNameInput != null)
            {
                UserNameInput.text = "";
            }
            if (DepartmentInput != null)
            {
                DepartmentInput.text = "";
            }
            if (PasswordInput != null)
            {
                PasswordInput.text = "";
            }
            if (ValidToggle != null)
            {
                ValidToggle.isOn = true;
                OnValidToggleChanged(true);
            }
        }
        else
        {
            if (AccountNameInput != null)
            {
                AccountNameInput.text = account.AccountName;
                AccountNameInput.interactable = false;
            }
            if (UserNameInput != null)
            {
                UserNameInput.text = account.UserName;
            }
            if (DepartmentInput != null)
            {
                DepartmentInput.text = account.Department;
            }
            if (PasswordInput != null)
            {
                PasswordInput.text = account.Password;
            }
            if (ValidToggle != null)
            {
                ValidToggle.isOn = account.Valid;
                OnValidToggleChanged(account.Valid);
            }
            privilegeToggleControl?.InitPrivilege(account);
        }

        gameObject.SetActive(true);
    }

    private void OnConfirmClick()
    {
        Account account = new Account();
        account.AccountName = AccountNameInput?.text ?? "";
        account.UserName = UserNameInput?.text ?? "";
        account.Department = DepartmentInput?.text ?? "";
        account.Password = PasswordInput?.text ?? "";
        account.Valid = ValidToggle != null ? ValidToggle.isOn : true;

        privilegeToggleControl?.SetPrivilege(account);

        string result;
        if (currentAccount == null)
        {
            result = AccountManager.Add(account);
        }
        else
        {
            result = AccountManager.Update(account);
        }

        if (string.IsNullOrEmpty(result))
        {
            AccountManager.Save();
            if (currentAccount != null && currentAccount.AccountName == AccountManager.GetCurrentAccountName())
            {
                UpdateAllPrivilegeControls();
            }
            if (userList != null)
            {
                userList.RefreshUserList();
                userList.gameObject.SetActive(true);
            }
            gameObject.SetActive(false);

            if (currentAccount == null)
            {
                OperationLogTool.RecordLog(OperationType.用户管理, $"新建用户 - 账号：{account.AccountName}，姓名：{account.UserName}");
            }
            else
            {
                OperationLogTool.RecordLog(OperationType.用户管理, $"编辑用户 - 账号：{account.AccountName}，姓名：{account.UserName}");
            }
        }
        else
        {
            Debug.LogError(currentAccount == null ? "新增失败：" + result : "更新失败：" + result);
        }
    }

    private void OnCancelClick()
    {
        gameObject.SetActive(false);
        currentAccount = null;
        if (userList != null)
        {
            userList.gameObject.SetActive(true);
        }
    }

    private void UpdateAllPrivilegeControls()
    {
        PrivilegeControl[] privilegeControls = FindObjectsOfType<PrivilegeControl>(true);
        foreach (PrivilegeControl control in privilegeControls)
        {
            control.CheckPrivilege();
        }
    }

    private void OnValidToggleChanged(bool isOn)
    {
        if (toggleStatusLabel != null)
        {
            toggleStatusLabel.text = isOn ? "启用" : "禁用";
            Color color;
            if (isOn)
            {
                ColorUtility.TryParseHtmlString("#05DF72", out color);
            }
            else
            {
                ColorUtility.TryParseHtmlString("#FF6467", out color);
            }
            toggleStatusLabel.color = color;
        }
    }
}
