using System;
using UnityEngine;
using UnityEngine.UI;

public class DayChoose : MonoBehaviour
{
    public Button TodayButton;
    public Button WeekButton;
    public Button MonthButton;

    private Button _selectedButton;
    private Text _todayText;
    private Text _weekText;
    private Text _monthText;
    private Image _todayImage;
    private Image _weekImage;
    private Image _monthImage;

    private Color _selectedColor = new Color32(255, 255, 255, 255);
    private Color _unselectedColor = new Color32(125, 211, 252, 255);

    void Start()
    {
        _todayText = TodayButton?.GetComponentInChildren<Text>();
        _weekText = WeekButton?.GetComponentInChildren<Text>();
        _monthText = MonthButton?.GetComponentInChildren<Text>();
        _todayImage = TodayButton?.GetComponent<Image>();
        _weekImage = WeekButton?.GetComponent<Image>();
        _monthImage = MonthButton?.GetComponent<Image>();

        if (TodayButton != null)
        {
            TodayButton.onClick.AddListener(() => OnButtonClick(TodayButton));
        }
        if (WeekButton != null)
        {
            WeekButton.onClick.AddListener(() => OnButtonClick(WeekButton));
        }
        if (MonthButton != null)
        {
            MonthButton.onClick.AddListener(() => OnButtonClick(MonthButton));
        }

        OnButtonClick(WeekButton);
    }

    private void OnButtonClick(Button clickedButton)
    {
        _selectedButton = clickedButton;

        SetButtonState(TodayButton, _todayText, _todayImage, clickedButton == TodayButton);
        SetButtonState(WeekButton, _weekText, _weekImage, clickedButton == WeekButton);
        SetButtonState(MonthButton, _monthText, _monthImage, clickedButton == MonthButton);
    }

    private void SetButtonState(Button button, Text text, Image image, bool isSelected)
    {
        if (text != null)
        {
            text.color = isSelected ? _selectedColor : _unselectedColor;
        }

        if (image != null)
        {
            Color imageColor = image.color;
            imageColor.a = isSelected ? 1f : 0f;
            image.color = imageColor;
        }
    }

    public (DateTime StartTime, DateTime EndTime) GetTimeRange()
    {
        DateTime now = DateTime.Now;
        DateTime startTime;
        DateTime endTime;

        if (_selectedButton == TodayButton)
        {
            startTime = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0);
            endTime = new DateTime(now.Year, now.Month, now.Day, 23, 59, 59);
        }
        else if (_selectedButton == WeekButton)
        {
            startTime = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0).AddDays(-6);
            endTime = new DateTime(now.Year, now.Month, now.Day, 23, 59, 59);
        }
        else if (_selectedButton == MonthButton)
        {
            startTime = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0).AddDays(-29);
            endTime = new DateTime(now.Year, now.Month, now.Day, 23, 59, 59);
        }
        else
        {
            startTime = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0);
            endTime = new DateTime(now.Year, now.Month, now.Day, 23, 59, 59);
        }

        return (startTime, endTime);
    }

    public void SetSelectedButton(Button button)
    {
        if (button == TodayButton || button == WeekButton || button == MonthButton)
        {
            OnButtonClick(button);
        }
    }
}