using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TreeItem : MonoBehaviour
{
    public TextMeshProUGUI NameText;
    public Toggle SelectionToggle;
    public RectTransform IndentTransform;

    private GameObject _targetObject;
    private GameObjectTreePanel _panel;

    public GameObject TargetObject => _targetObject;
    public bool IsSelected => SelectionToggle.isOn;

    public void Initialize(GameObject target, int depth, GameObjectTreePanel panel, ToggleGroup toggleGroup)
    {
        _targetObject = target;
        _panel = panel;
        
        NameText.text = target.name;
        IndentTransform.offsetMin = new Vector2(depth * 20, IndentTransform.offsetMin.y);
        
        if (SelectionToggle != null)
        {
            SelectionToggle.group = toggleGroup;
            SelectionToggle.onValueChanged.AddListener(OnSelectionChanged);
        }
    }

    private void OnSelectionChanged(bool isSelected)
    {
        if (isSelected)
        {
            _panel.OnItemSelected(this);
            Debug.Log($"Selected: {_targetObject.name}");
        }
    }
}