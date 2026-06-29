using Assets.SimulationPlatform.Scripts.Model;
using System;
using System.Reflection;

public static class PrivilegeExtension
{
    public static Privilege GetPrivilege(string id)
    {
        if (id == null)
        {
            return Privilege.None;
        }
        foreach (Privilege privilege in Enum.GetValues(typeof(Privilege)))
        {
            if (string.Equals(privilege.GetId(), id))
            {
                return privilege;
            }
        }
        return Privilege.None;
    }

    public static string GetDescription(this Enum enumValue)
    {
        // 1. 获取枚举成员的FieldInfo
        FieldInfo fieldInfo = enumValue.GetType().GetField(enumValue.ToString());
        if (fieldInfo == null)
        {
            return enumValue.ToString();
        }

        // 2. 获取字段上的自定义特性
        PrivilegeAttribute attribute = fieldInfo.GetCustomAttribute<PrivilegeAttribute>();

        // 3. 返回特性中的描述，无特性则返回枚举名称
        return attribute?.Desc ?? enumValue.ToString();
    }
    public static string GetName(this Enum enumValue)
    {
        FieldInfo fieldInfo = enumValue.GetType().GetField(enumValue.ToString());
        if (fieldInfo == null)
        {
            return enumValue.ToString();
        }

        PrivilegeAttribute attribute = fieldInfo.GetCustomAttribute<PrivilegeAttribute>();
        return attribute?.Name ?? enumValue.ToString();
    }
    public static string GetId(this Enum enumValue)
    {
        FieldInfo fieldInfo = enumValue.GetType().GetField(enumValue.ToString());
        if (fieldInfo == null)
        {
            return enumValue.ToString();
        }

        PrivilegeAttribute attribute = fieldInfo.GetCustomAttribute<PrivilegeAttribute>();
        return attribute?.Id ?? enumValue.ToString();
    }
    
    public static bool GetVisible(this Enum enumValue)
    {
        FieldInfo fieldInfo = enumValue.GetType().GetField(enumValue.ToString());
        if (fieldInfo == null)
        {
            return false;
        }

        PrivilegeAttribute attribute = fieldInfo.GetCustomAttribute<PrivilegeAttribute>();
        return attribute?.Visible ?? false;
    }
}
