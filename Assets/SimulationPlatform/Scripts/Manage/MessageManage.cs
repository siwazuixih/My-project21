using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

internal static class MessageManage
{
    public static MessageBox MessageBox { get; set; }

    public static void ShowMessage(string message, double delay = -1)
    {
        if (MessageBox != null)
        {
            MessageBox.ShowMessage(message, delay);
        }
    }

    public static void Close()
    {
        if (MessageBox != null)
        {
            MessageBox.Close();
        }
    }
}

