using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class OperationList : MonoBehaviour
{
    public Transform tablecontent;
    public GameObject prefab;
    public Button PrevButton;
    public Button NextButton;
    public Text TotalCountText;
    public Text CurrentPageText;
    public DayChoose dayChoose;
    public InputField AccountInputField;
    public TMP_Dropdown TypeDropdown;
    public Button SearchButton;

    private int _currentPage = 1;
    private const int PageSize = 10;
    private long _totalCount = 0;

    void Start()
    {
        if (PrevButton != null)
        {
            PrevButton.onClick.AddListener(OnPrevClick);
        }
        if (NextButton != null)
        {
            NextButton.onClick.AddListener(OnNextClick);
        }
        if (SearchButton != null)
        {
            SearchButton.onClick.AddListener(OnSearchClick);
        }

        InitTypeDropdown();

        if (dayChoose != null && dayChoose.WeekButton != null)
        {
            dayChoose.SetSelectedButton(dayChoose.WeekButton);
        }

        RefreshOperationList();
    }

    private void InitTypeDropdown()
    {
        if (TypeDropdown != null)
        {
            TypeDropdown.ClearOptions();
            List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData>();
            options.Add(new TMP_Dropdown.OptionData("全部"));
            foreach (OperationType type in Enum.GetValues(typeof(OperationType)))
            {
                options.Add(new TMP_Dropdown.OptionData(type.ToString()));
            }
            TypeDropdown.AddOptions(options);
        }
    }

    void OnEnable()
    {
        _currentPage = 1;
        RefreshOperationList();
    }

    public void RefreshOperationList()
    {
        ClearOperationList();

        string accountOrName = AccountInputField?.text?.Trim();
        string operationType = TypeDropdown != null && TypeDropdown.value > 0 
            ? TypeDropdown.options[TypeDropdown.value].text 
            : null;
        
        DateTime? startTime = null;
        DateTime? endTime = null;
        if (dayChoose != null)
        {
            var timeRange = dayChoose.GetTimeRange();
            startTime = timeRange.StartTime;
            endTime = timeRange.EndTime;
        }

        _totalCount = OperationLogTool.GetLogCount(accountOrName, startTime, endTime, operationType, accountOrName);
        int skip = (_currentPage - 1) * PageSize;

        List<OperationLog> logs = OperationLogTool.QueryLogs(accountOrName, startTime, endTime, operationType, accountOrName, skip, PageSize);
        if (logs != null)
        {
            for (int i = 0; i < logs.Count; i++)
            {
                OperationLog log = logs[i];
                AddOperationItem(log);
            }
        }
        else
        {
            Debug.LogError("获取操作日志失败");
        }

        UpdatePaginationUI();
    }

    private void OnSearchClick()
    {
        _currentPage = 1;
        RefreshOperationList();
    }

    private void UpdatePaginationUI()
    {
        if (TotalCountText != null)
        {
            TotalCountText.text = $"{_totalCount}";
        }

        int totalPages = GetTotalPages();
        if (CurrentPageText != null)
        {
            CurrentPageText.text = $"{_currentPage}/{totalPages}";
        }

        if (PrevButton != null)
        {
            PrevButton.interactable = _currentPage > 1;
        }

        if (NextButton != null)
        {
            NextButton.interactable = _currentPage < totalPages;
        }
    }

    private int GetTotalPages()
    {
        if (_totalCount <= 0)
        {
            return 1;
        }
        return (int)Mathf.Ceil((float)_totalCount / PageSize);
    }

    private void OnPrevClick()
    {
        if (_currentPage > 1)
        {
            _currentPage--;
            RefreshOperationList();
        }
    }

    private void OnNextClick()
    {
        if (_currentPage < GetTotalPages())
        {
            _currentPage++;
            RefreshOperationList();
        }
    }

    private void ClearOperationList()
    {
        if (tablecontent != null)
        {
            foreach (Transform child in tablecontent)
            {
                Destroy(child.gameObject);
            }
        }
    }

    private void AddOperationItem(OperationLog log)
    {
        if (prefab == null || tablecontent == null)
        {
            Debug.LogError("prefab或tablecontent未设置");
            return;
        }

        GameObject operationItemGO = Instantiate(prefab, tablecontent);

        OperationItem operationItem = operationItemGO.GetComponent<OperationItem>();
        if (operationItem == null)
        {
            operationItem = operationItemGO.AddComponent<OperationItem>();
        }
        operationItem.operationList = this;
        operationItem.OperationLog = log;
    }
}