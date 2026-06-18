using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;


[XmlRoot("Accounts")]
[Serializable]
public class XmlAccount
{
    [XmlElement("Account")]
    public List<Account> Accounts { get; set; } = new List<Account>();
}
