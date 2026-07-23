using UnityEngine;
using UnityEngine.UI;

public class OperationItem : MonoBehaviour
{
    public OperationList operationList;

    private OperationLog _operationLog;
    public OperationLog OperationLog
    {
        get { return _operationLog; }
        set
        {
            _operationLog = value;
            SetValue();
        }
    }

    private void SetValue()
    {
        if (OperationTime != null)
        {
            OperationTime.text = _operationLog?.CreateTime.ToString("yyyy-MM-dd HH:mm:ss");
        }
        if (UserName != null)
        {
            UserName.text = _operationLog?.UserName;
        }
        if (AccountName != null)
        {
            AccountName.text = "@" + _operationLog?.AccountName;
        }
        if (OperationType != null)
        {
            OperationType.text = _operationLog?.Operation;
        }
        if (Detail != null)
        {
            Detail.text = _operationLog?.Detail;
        }
    }

    public Text OperationTime;
    public Text UserName;
    public Text AccountName;
    public Text OperationType;
    public Text Detail;
}