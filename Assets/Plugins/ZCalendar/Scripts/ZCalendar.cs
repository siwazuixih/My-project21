using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using UnityEngine;
using UnityEngine.Events;

namespace ZTools
{
    [RequireComponent(typeof(ZCalendarModel))]
    public class ZCalendar : MonoBehaviour
    {
        /// <summary>
        /// 数据更新时，可获取到每一个日期，并对其进行操作
        /// </summary>
        public class UpdateDateEvent : UnityEvent<DateTime> { }
        public UpdateDateEvent m_DayRefresh = new UpdateDateEvent();
        /// <summary>
        /// 可以获取到点击的某一天
        /// </summary>
        public class ChoiceDayEvent : UnityEvent<DateTime> { }
        public ChoiceDayEvent m_DayValueChanged = new ChoiceDayEvent();
        /// <summary>
        /// 选择区间时间事件
        /// </summary>
        public class RangeTimeEvent : UnityEvent<DateTime, DateTime> { }
        public RangeTimeEvent m_RangeTimeEvent = new RangeTimeEvent();
        /// <summary>
        /// 日历加载结束
        /// </summary>
        public class CompleteEvent : UnityEvent { }
        public CompleteEvent m_completeEvent = new CompleteEvent();
        /// <summary>
        /// 获取当前选中的天对象
        /// </summary>
        public DateTime CrtTime { get; set; }
        /// <summary>
        /// model
        /// </summary>
        private ZCalendarModel zCalendarModel;
        /// <summary>
        /// controller
        /// </summary>
        private ZCalendarController zCalendarController;
        public CompleteEvent onComplete
        {
            set { m_completeEvent = value; }
            get { return m_completeEvent; }
        }
        public RangeTimeEvent onRangeTimeValueChanged
        {
            set { m_RangeTimeEvent = value; }
            get { return m_RangeTimeEvent; }
        }
        public ChoiceDayEvent onDayValueChanged
        {
            set { m_DayValueChanged = value; }
            get { return m_DayValueChanged; }
        }
        public UpdateDateEvent onDayRefresh
        {
            set { m_DayRefresh = value; }
            get { return m_DayRefresh; }
        }
        public event Action<DateTime> dayValueChangedDayItemListenerEvent;
        public event Action<DateTime, DateTime> RangeTimeChangedDayItemListenerEvent;
        /// <summary>
        /// 入口
        /// </summary>
        private void Start()
        {
            zCalendarModel = this.GetComponent<ZCalendarModel>();
            // 开启时自动初始化
            if (zCalendarModel.awake2Init)
            {
                Init();
            }
        }
        /// <summary>
        /// 初始化
        /// </summary>
        public void Init()
        {
            zCalendarController = new ZCalendarController()
            {
                zCalendar = this,
                zCalendarModel = zCalendarModel,
                pos = this.transform.localPosition
            };
            zCalendarController.Init();
            RefreshDate();
        }
        /// <summary>
        /// 按照现在时间初始化
        /// </summary>
        public void RefreshDate()
        {
            zCalendarController.RefreshDate(DateTime.Today);
        }
        /// <summary>
        /// 按照DateTime格式初始化日历
        /// </summary>
        public void RefreshDate(DateTime dateTime)
        {
            if (dateTime.Hour != 0 || dateTime.Month != 0 || dateTime.Second != 0)
            {
                dateTime = new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, 0, 0, 0);
            }
            zCalendarController.RefreshDate(dateTime);
        }
        /// <summary>
        /// 按照YYYY-MM-DD格式初始化日历
        /// </summary>
        public void RefreshDate(string dateTime)
        {
            if (!dateTime.Contains("-") || dateTime.Contains(":"))
            {
                Debug.LogError("wrong format: Time divided by '-' and do not contains hour minute or second");
                return;
            }
            string[] dateTimes = dateTime.Split('-');
            zCalendarController.RefreshDate(new DateTime(int.Parse(dateTimes[0]), int.Parse(dateTimes[1]), int.Parse(dateTimes[2])));
        }
        /// <summary>
        /// 初始化区间日历
        /// </summary>
        public void RefreshDate(DateTime startDateTime, DateTime endDateTime)
        {
            if (!zCalendarModel.rangeCalendar)
            {
                Debug.LogError("ZCalendar Init Error：The config is not RangeCalendar!!!");
                return;
            }
            if (startDateTime.Hour != 0 || startDateTime.Minute != 0 || startDateTime.Second != 0)
            {
                startDateTime = new DateTime(startDateTime.Year, startDateTime.Month, startDateTime.Day, 0, 0, 0);
            }
            if (endDateTime.Hour != 0 || endDateTime.Minute != 0 || endDateTime.Second != 0)
            {
                endDateTime = new DateTime(endDateTime.Year, endDateTime.Month, endDateTime.Day, 0, 0, 0);
            }
            zCalendarController.RefreshDate(startDateTime, endDateTime);
        }
        /// <summary>
        /// 初始化区间日历
        /// </summary>
        /// <param name="startDateTime">开始时间，以‘-’分割</param>
        /// <param name="endDateTime">结束时间，以‘-’分割</param>
        public void RefreshDate(string startDateTime, string endDateTime)
        {
            if (!startDateTime.Contains("-") || !endDateTime.Contains("-"))
            {
                Debug.LogError("wrong format: Time divided by '-' and do not contains hour minute or second");
                return;
            }
            string[] startDateTimes = startDateTime.Split('-');
            string[] endDataTimes = endDateTime.Split('-');
            zCalendarController.RefreshDate(new DateTime(int.Parse(startDateTimes[0]), int.Parse(startDateTimes[1]), int.Parse(startDateTimes[2])), new DateTime(int.Parse(endDataTimes[0]), int.Parse(endDataTimes[1]), int.Parse(endDataTimes[2])));
        }
        /// <summary>
        /// 显示弹窗
        /// </summary>
        public void Show()
        {
            zCalendarController.Show();
        }
        /// <summary>
        /// 隐藏弹窗
        /// </summary>
        public void Hide()
        {
            zCalendarController.Hide();
        }

        /// <summary>
        /// 切换时间
        /// </summary>
        /// <param name="obj"></param>
        [Obsolete("事件触发器，请使用UpdateDateEvent获取切换月份时加载的时间对象")]
        public void UpdateDate(DateTime obj)
        {
            m_DayRefresh.Invoke(obj);
        }
        DateTime choiceTime;
        /// <summary>
        /// 日期点击
        /// </summary>
        [Obsolete("事件触发器，请使用ChoiceDayEvent获取当前选择的时间")]
        public void DayClick(DateTime dayItem)
        {
            choiceTime = new DateTime(dayItem.Year, dayItem.Month, dayItem.Day, zCalendarModel.hour.choiceTime, zCalendarModel.min.choiceTime, zCalendarModel.second.choiceTime);
            dayValueChangedDayItemListenerEvent.Invoke(dayItem);
            m_DayValueChanged.Invoke(dayItem.AddHours(zCalendarModel.hour.choiceTime).AddMinutes(zCalendarModel.min.choiceTime).AddSeconds(zCalendarModel.second.choiceTime));
            CrtTime = dayItem;
        }
        public void TimeChoice()
        {
            if (zCalendarModel.rangeCalendar)
            {
                RangeCalendar(rangeStartTime, rangeEndTime);
            }
            else
            {
                DayClick(CrtTime);
            }
        }
        /// <summary>
        /// 加载结束
        /// </summary>
        [Obsolete("事件触发器，请使用CompleteEvent获取日历加载完成事件")]
        public void DateComplete()
        {
            onComplete?.Invoke();
        }
        DateTime rangeStartTime, rangeEndTime;
        /// <summary>
        /// 区间日期选择
        /// </summary>
        /// <param name="firstDay"></param>
        /// <param name="secondDay"></param>
        [Obsolete("事件触发器，请使用RangeTimeEvent获取区间时间")]
        public void RangeCalendar(DateTime firstDay, DateTime secondDay )
        {
            RangeTimeChangedDayItemListenerEvent?.Invoke(firstDay, secondDay);
            m_RangeTimeEvent?.Invoke(firstDay.AddHours(zCalendarModel.hour.choiceTime).AddMinutes(zCalendarModel.min.choiceTime).AddSeconds(zCalendarModel.second.choiceTime), secondDay.AddHours(zCalendarModel.hour.choiceTime).AddMinutes(zCalendarModel.min.choiceTime).AddSeconds(zCalendarModel.second.choiceTime));
            rangeStartTime = firstDay;
            rangeEndTime = secondDay;
        }
        private void OnDestroy()
        {
            zCalendarController = null;
            GC.Collect();
        }
    }
}
