using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class AccountManager
{
    public static XmlAccount XmlAccount;
    private static Account currentAccount;
    public static void Load()
    {
        try
        {
            XmlAccount = XmlConfigTool.DeserializeFromXml<XmlAccount>("Configs", "accounts.xml");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
        if (XmlAccount == null)
        {
            XmlAccount = new XmlAccount();
        }
        if (XmlAccount.Accounts.Count == 0)
        {
            XmlAccount.Accounts = new List<Account>();
            Account admin = new Account();
            admin.AccountName = "admin";
            admin.UserName = "admin";
            admin.Password = "admin";
            foreach (Privilege privilege in Enum.GetValues(typeof(Privilege)))
            {
                admin.PrivilegeList.Add(privilege);
            }
            XmlAccount.Accounts.Add(admin);
        }
    }

    public static void Save()
    {
        try
        {
            XmlConfigTool.SerializeToXml<XmlAccount>(XmlAccount, "Configs", "accounts.xml");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    public static string Add(Account account)
    {
        foreach (var item in XmlAccount.Accounts)
        {
            if (item.AccountName == account.AccountName)
            {
                return "账号已存在，不能重复注册";
            }
        }
        XmlAccount.Accounts.Add(account);
        return string.Empty;
    }

    public static string Update(Account account)
    {
        for (int i = 0; i < XmlAccount.Accounts.Count; i++)
        {
            if (XmlAccount.Accounts[i].AccountName == account.AccountName)
            {
                XmlAccount.Accounts[i].UserName = account.UserName;
                XmlAccount.Accounts[i].Department = account.Department;
                XmlAccount.Accounts[i].Password = account.Password;
                XmlAccount.Accounts[i].Valid = account.Valid;
                XmlAccount.Accounts[i].PrivilegeList = account.PrivilegeList;
                return string.Empty;
            }
        }
        return "账号不存在";
    }

    public static bool Validate(string account, string password)
    {
        foreach (var item in XmlAccount.Accounts)
        {
            if (item.AccountName == account && item.Password == password && item.Valid)
            {
                currentAccount = item;
                currentAccount.LoginTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                Save();
                return true;
            }
        }
        return false;
    }
    
    /// <summary>
    /// 检查当前登录用户是否拥有指定权限
    /// </summary>
    /// <param name="privilege">需要检查的权限</param>
    /// <returns>是否拥有该权限</returns>
    public static bool HasPrivilege(Privilege privilege)
    {
        if (currentAccount == null)
        {
            return false;
        }
        return currentAccount.PrivilegeList.Contains(privilege);
    }
    
    /// <summary>
    /// 获取当前登录用户的用户名
    /// </summary>
    /// <returns>当前登录用户的用户名</returns>
    public static string GetCurrentUserName()
    {
        if (currentAccount == null)
        {
            return string.Empty;
        }
        return currentAccount.UserName;
    }
    
    /// <summary>
    /// 获取当前登录用户的账号名
    /// </summary>
    /// <returns>当前登录用户的账号名</returns>
    public static string GetCurrentAccountName()
    {
        if (currentAccount == null)
        {
            return string.Empty;
        }
        return currentAccount.AccountName;
    }
}
