using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PrivilegeControl : MonoBehaviour
{
    public Privilege requiredPrivilege;
    
    // Start is called before the first frame update
    void Start()
    {
        CheckPrivilege();
    }
    
    private void CheckPrivilege()
    {
        // 1. 检查当前账号是否拥有该权限
        bool hasPrivilege = AccountManager.HasPrivilege(requiredPrivilege);
        
        // 2. 检查该权限的Visible属性是否为true
        bool isVisible = requiredPrivilege.GetVisible();
        
        // 3. 当拥有权限或Visible为true时显示该GameObject
        gameObject.SetActive(hasPrivilege || isVisible);
    }
    
    // Update is called once per frame
    void Update()
    {
        
    }
}
