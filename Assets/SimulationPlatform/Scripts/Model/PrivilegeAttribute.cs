using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


[AttributeUsage(AttributeTargets.Enum | AttributeTargets.Field, AllowMultiple = false)]
public class PrivilegeAttribute : Attribute
{
    public PrivilegeAttribute(string id, string name, string desc, bool visible)
    {
        Id = id;
        Name = name;
        Desc = desc;
        Visible = visible;
    }

    public string Id { get; }
    public string Name { get; }
    public string Desc { get; }

    public bool Visible { get; }
}
