using UnityEngine;
using UnityEngine.UI;

public class JointReplaceItem : MonoBehaviour
{
    public JointReplace JointReplace;

    private JointReplaceRecord _record;
    public JointReplaceRecord Record
    {
        get { return _record; }
        set
        {
            _record = value;
            SetValue();
        }
    }

    private int _index;
    public int Index
    {
        get { return _index; }
        set
        {
            _index = value;
            SetValue();
        }
    }

    private void SetValue()
    {
        if (IndexText != null)
        {
            IndexText.text = (_index + 1).ToString();
        }
        if (PositionText != null && _record != null && _record.Position != null)
        {
            PositionText.text = $"({_record.Position.X:G3}, {_record.Position.Y:G3}, {_record.Position.Z:G3})";
        }
        if (ModelText != null)
        {
            ModelText.text = GetJointModelName(_record?.JointId) ?? "";
        }
    }

    public void OnDeleteClick()
    {
        if (JointReplace != null && _record != null)
        {
            JointReplace.RemoveReplaceRecord(_record);
        }
    }

    private string GetJointModelName(string jointId)
    {
        if (string.IsNullOrEmpty(jointId) || ModelManager.XmlModel?.Joints == null)
        {
            return null;
        }

        var jointModel = ModelManager.XmlModel.Joints.Find(j => j.Id == jointId);
        return jointModel?.Name;
    }

    public Text IndexText;
    public Text PositionText;
    public Text ModelText;
    public Button DeleteButton;

    private void Awake()
    {
        if (DeleteButton != null)
        {
            DeleteButton.onClick.AddListener(OnDeleteClick);
        }
    }
}
