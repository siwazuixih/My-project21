using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public class UserItem : MonoBehaviour
{
    public UserList userList;
    public UserEdit userEdit;

    private Account _account;
    public Account Account
    {
        get { return _account; }
        set
        {
            _account = value;
            SetValue();
        }
    }

    private void SetValue()
    {
        if (User != null)
        {
            User.text = _account?.AccountName;
        }
        if (Name != null)
        {
            Name.text = _account?.UserName;
        }
        if (Department != null)
        {
            Department.text = _account?.Department;
        }
        if (_account != null && _account.Valid)
        {
            if (Valid != null)
            {
                Valid.SetActive(true);
            }
            if (Invalid != null)
            {
                Invalid.SetActive(false);
            }
            if (ValidButton != null)
            {
                ValidButton.text = "禁用";
            }
        }
        else
        {
            if (Valid != null)
            {
                Valid.SetActive(false);
            }
            if (Invalid != null)
            {
                Invalid.SetActive(true);
            }
            if (ValidButton != null)
            {
                ValidButton.text = "启用";
            }
        }

        if (Time != null)
        {
            Time.text = _account?.LoginTime;
        }
    }

    public Text User;
    public Text Name;
    public Text Department;
    public GameObject Valid;
    public GameObject Invalid;
    public Text Time;
    public Text ValidButton;
    public Text EditButton;

    public void OnEditClick()
    {
        if (userList != null)
        {
            userList.gameObject.SetActive(false);
        }
        if (userEdit != null && _account != null)
        {
            userEdit.ShowEditPanel(_account);
        }
        else
        {
            Debug.LogError("userEdit或Account为空");
        }
    }
}
