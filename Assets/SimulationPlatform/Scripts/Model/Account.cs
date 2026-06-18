using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;


[Serializable]
public class Account
{
    [XmlAttribute("AccountName")]
    public string AccountName { get; set; }

    [XmlAttribute("UserName")]
    public string UserName { get; set; }

    [XmlAttribute("Pwd")]
    public string Password { get; set; }

    [XmlAttribute("Department")]
    public string Department { get; set; }

    [XmlElement("Privilege")]
    public string Privileges
    {
        get
        {
            return string.Join(";", PrivilegeList.Select(x => x.GetId()).ToList());
        }
        set
        {
            PrivilegeList.Clear();
            if (value != null)
            {
                var list = value.Split(";");
                foreach (var item in list)
                {
                    Privilege privilege = PrivilegeExtension.GetPrivilege(item);
                    if (privilege != Privilege.None)
                    {
                        PrivilegeList.Add(privilege);
                    }
                }
            }
        }
    }

    public List<Privilege> PrivilegeList { get; set; } = new List<Privilege>();
    public Account() { }
}
