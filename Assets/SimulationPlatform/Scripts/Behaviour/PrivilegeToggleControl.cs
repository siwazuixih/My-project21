using UnityEngine;
using UnityEngine.UI;

public class PrivilegeToggleControl : MonoBehaviour
{
    public Toggle UserManageToggle;
    public Toggle ProjectManageToggle;
    public Toggle SimulationToggle;
    public Toggle RealRunToggle;
    public Toggle ModelManageToggle;
    public Toggle OperationLogToggle;

    public void SetPrivilege(Account account)
    {
        if (UserManageToggle != null && UserManageToggle.isOn)
        {
            if (!account.PrivilegeList.Contains(Privilege.Menu_Privilege))
            {
                account.PrivilegeList.Add(Privilege.Menu_Privilege);
            }
            account.PrivilegeList.Add(Privilege.Module_Account);
        }
        if (OperationLogToggle != null && OperationLogToggle.isOn)
        {
            if (!account.PrivilegeList.Contains(Privilege.Menu_Privilege))
            {
                account.PrivilegeList.Add(Privilege.Menu_Privilege);
            }
            account.PrivilegeList.Add(Privilege.Module_Log);
        }
        if (ProjectManageToggle != null && ProjectManageToggle.isOn)
        {
            if (!account.PrivilegeList.Contains(Privilege.Menu_Project))
            {
                account.PrivilegeList.Add(Privilege.Menu_Project);
            }
            account.PrivilegeList.Add(Privilege.Module_Project);
        }
        if (SimulationToggle != null && SimulationToggle.isOn)
        {
            if (!account.PrivilegeList.Contains(Privilege.Menu_Project))
            {
                account.PrivilegeList.Add(Privilege.Menu_Project);
            }
            account.PrivilegeList.Add(Privilege.Module_Simulation);
        }
        if (RealRunToggle != null && RealRunToggle.isOn)
        {
            if (!account.PrivilegeList.Contains(Privilege.Menu_Project))
            {
                account.PrivilegeList.Add(Privilege.Menu_Project);
            }
            account.PrivilegeList.Add(Privilege.Module_RealRun);
        }
        if (ModelManageToggle != null && ModelManageToggle.isOn)
        {
            account.PrivilegeList.Add(Privilege.Menu_Model);
        }
    }

    public void InitPrivilege(Account account)
    {
        if (account == null || account.PrivilegeList == null)
        {
            return;
        }

        if (UserManageToggle != null)
        {
            UserManageToggle.isOn = account.PrivilegeList.Contains(Privilege.Module_Account);
        }
        if (OperationLogToggle != null)
        {
            OperationLogToggle.isOn = account.PrivilegeList.Contains(Privilege.Module_Log);
        }
        if (ProjectManageToggle != null)
        {
            ProjectManageToggle.isOn = account.PrivilegeList.Contains(Privilege.Module_Project);
        }
        if (SimulationToggle != null)
        {
            SimulationToggle.isOn = account.PrivilegeList.Contains(Privilege.Module_Simulation);
        }
        if (RealRunToggle != null)
        {
            RealRunToggle.isOn = account.PrivilegeList.Contains(Privilege.Module_RealRun);
        }
        if (ModelManageToggle != null)
        {
            ModelManageToggle.isOn = account.PrivilegeList.Contains(Privilege.Menu_Model);
        }
    }
}