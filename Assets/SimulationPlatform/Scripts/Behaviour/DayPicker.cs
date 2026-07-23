using System;
using UnityEngine;
using UnityEngine.UI;
using ZTools;

public class DayPicker : MonoBehaviour
{
    public Button DateButton;
    public ZCalendar ZCalendar;

    public delegate void DateSelectedHandler(DateTime date);
    public event DateSelectedHandler OnDateSelected;

    private void Awake()
    {
    }

    private void Start()
    {
        if (DateButton != null)
        {
            DateButton.onClick.AddListener(OnButtonClick);
        }
       
        if (ZCalendar != null)
        {
            ZCalendar.onDayValueChanged.AddListener(OnDayValueChanged);
            ZCalendar.onComplete.AddListener(OnCalendarComplete);
        }
    }

    public void OnButtonClick()
    {
        if (ZCalendar != null)
        {
            //InitializeZCalendarIfNeeded();

            Text dateText = DateButton?.GetComponentInChildren<Text>();
            if (!string.IsNullOrEmpty(dateText?.text) && IsValidDateFormat(dateText.text))
            {
                ZCalendar.RefreshDate(dateText.text);
            }
            else
            {
                ZCalendar.RefreshDate(DateTime.Today);
            }
            ZCalendar.Show();
        }
    }

    private void InitializeZCalendarIfNeeded()
    {
        var modelField = typeof(ZCalendar).GetField("zCalendarModel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var controllerField = typeof(ZCalendar).GetField("zCalendarController", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (modelField != null && controllerField != null)
        {
            var zCalendarModel = modelField.GetValue(ZCalendar);
            var zCalendarController = controllerField.GetValue(ZCalendar);

            if (zCalendarModel == null)
            {
                zCalendarModel = ZCalendar.GetComponent<ZCalendarModel>();
                modelField.SetValue(ZCalendar, zCalendarModel);
            }

            if (zCalendarController == null)
            {
                ZCalendar.Init();
            }
        }
        else
        {
            ZCalendar.Init();
        }
    }

    private bool IsValidDateFormat(string text)
    {
        return DateTime.TryParseExact(text, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out _);
    }

    private void OnDayValueChanged(DateTime date)
    {
        DateTime selectedDate = ZCalendar?.CrtTime ?? date;
        
        Text dateText = DateButton?.GetComponentInChildren<Text>();
        if (dateText != null)
        {
            dateText.text = selectedDate.ToString("yyyy-MM-dd");
        }
        ZCalendar?.Hide();


        OnDateSelected?.Invoke(selectedDate);
    }

    private void OnCalendarComplete()
    {
        if (ZCalendar != null)
        {
            ZCalendar.Hide();
        }
    }

    public DateTime GetSelectedDate()
    {
        if (ZCalendar != null)
        {
            return ZCalendar.CrtTime;
        }
        return DateTime.Today;
    }

    public void SetDate(DateTime date)
    {
        Text dateText = DateButton?.GetComponentInChildren<Text>();
        if (dateText != null)
        {
            dateText.text = date.ToString("yyyy-MM-dd");
        }
        if (ZCalendar != null)
        {
            ZCalendar.RefreshDate(date);
        }
    }
}