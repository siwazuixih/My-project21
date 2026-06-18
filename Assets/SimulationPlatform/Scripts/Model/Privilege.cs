using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


public enum Privilege
{
    None,
    #region 权限管理
    [Privilege("A47B4F0E-0AEF-4322-BB8C-5D2691341BC0", "权限管理", "权限管理", false)]
    Menu_Privilege,

    #region 账号管理
    [Privilege("8DE4B38C-8918-4D59-BD0A-266FB0DC6EFB", "账号管理", "账号管理", false)]
    Module_Account,
    #endregion

    #region 日志管理
    [Privilege("8926201B-7A7F-4E71-9F65-8014497602EC", "日志管理", "日志管理", false)]
    Module_Log,
    #endregion

    #endregion

    #region 模型管理
    [Privilege("ABD34F0E-159B-44F1-B29E-09360EFFF10C", "模型管理", "模型管理", false)]
    Menu_Model,

    #region 功能权限
    #endregion
    #endregion

    #region 项目管理
    [Privilege("7C426B03-CB68-46E3-A88C-2258890E3D24", "项目管理", "项目管理", false)]
    Menu_Project,

    #region 项目设置
    [Privilege("61FB6CA6-24DD-46E6-8C31-8473F74EC225", "项目设置", "项目设置", true)]
    Module_Project,

    [Privilege("BA25B601-7AA7-4698-95AC-D09BFA1844CB", "仿真操作", "仿真操作", false)]
    Module_Simulation,

    [Privilege("80EDC760-D0AA-49BE-884C-E80C21C12AE7", "仿真记录", "仿真记录", true)]
    Module_Record,
    #endregion
    #endregion
}
