using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class GameObjectTreePanel : MonoBehaviour
{
    public GameObject Panel;
    public RectTransform ContentRoot;
    public Button CloseButton;
    public Button GenerateButton;
    public GameObject TreeItemPrefab;
    public ToggleGroup TreeToggleGroup;

    private AutoColliderGen_Final _colliderGen;
    private GameObject _currentModel;
    private List<TreeItem> _treeItems = new List<TreeItem>();
    private TreeItem _selectedItem;

    void Awake()
    {
        if (CloseButton != null)
        {
            CloseButton.onClick.AddListener(OnCloseClicked);
        }
        if (GenerateButton != null)
        {
            GenerateButton.onClick.AddListener(OnGenerateClicked);
        }
    }

    public void Show(GameObject model, AutoColliderGen_Final colliderGen)
    {
        _currentModel = model;
        _colliderGen = colliderGen;
        _selectedItem = null;
        ClearTree();
        BuildTree(model.transform, 0);
        Panel.SetActive(true);
    }

    public void Hide()
    {
        Panel.SetActive(false);
    }

    private void ClearTree()
    {
        foreach (var item in _treeItems)
        {
            Destroy(item.gameObject);
        }
        _treeItems.Clear();
    }

    private void BuildTree(Transform parent, int depth)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            GameObject itemObj = Instantiate(TreeItemPrefab, ContentRoot);
            TreeItem treeItem = itemObj.GetComponent<TreeItem>();
            
            if (treeItem != null)
            {
                treeItem.Initialize(child.gameObject, depth, this, TreeToggleGroup);
                _treeItems.Add(treeItem);
            }
            
            if (child.childCount > 0)
            {
                BuildTree(child, depth + 1);
            }
        }
    }

    public void OnItemSelected(TreeItem item)
    {
        _selectedItem = item;
    }

    private void OnCloseClicked()
    {
        Hide();
    }

    private async void OnGenerateClicked()
    {
        if (_colliderGen == null || _selectedItem == null) return;
        
        _colliderGen.targetObject = _selectedItem.TargetObject;
        _colliderGen.hollowParts.Clear();
        _colliderGen.hollowParts.Add(_selectedItem.TargetObject);
        
        await _colliderGen.Generate();
        Hide();
    }
}