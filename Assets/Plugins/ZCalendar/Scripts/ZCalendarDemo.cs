/*
 * JacobKay --20220903
 */
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ZTools;
/// <summary>
/// 使用示例
/// </summary>
public class ZCalendarDemo : MonoBehaviour
{
    public ZCalendar zCalendar;
    // Start is called before the first frame update
    void Awake()
    {
        zCalendar.onDayRefresh.AddListener(ZCalendar_UpdateDateEvent);
        zCalendar.onDayValueChanged.AddListener(ZCalendar_ChoiceDayEvent);
        zCalendar.onRangeTimeValueChanged.AddListener(ZCalendar_RangeTimeEvent);
        zCalendar.onComplete.AddListener(ZCalendar_CompleteEvent);
        //zCalendar.RefreshDate("2023-10-01", "2023-11-21");
        //zCalendar.RefreshDate(System.DateTime.Now);
        //zCalendar.RefreshDate("2022-02-02");
        //zCalendar.Show();
        //zCalendar.Hide();
    }
    /// <summary>
    /// 加载结束
    /// </summary>
    private void ZCalendar_CompleteEvent()
    {
        Debug.Log("ZCalendar加载结束");
        if (null != zCalendar.CrtTime)
        {
            Debug.Log($"当前时间{zCalendar.CrtTime.Day}");
        }
    }

    /// <summary>
    /// 区间时间
    /// </summary>
    /// <param name="arg1"></param>
    /// <param name="arg2"></param>
    private void ZCalendar_RangeTimeEvent(DateTime arg1, DateTime arg2)
    {
        if (GetComponent<ZCalendarModel>().timeChoice)
        {
            Debug.Log($"选择的时间区间：{arg1.ToString("yyyy-MM-dd HH:mm:ss")}到{arg2.ToString("yyyy-MM-dd HH:mm:ss")}");
        }
        else
        {
            Debug.Log($"选择的日期区间：{arg1.ToString("yyyy-MM-dd")}到{arg2.ToString("yyyy-MM-dd")}");
        }
    }

    /// <summary>
    /// 获取选择的日期
    /// </summary>
    /// <param name="obj"></param>
    private void ZCalendar_ChoiceDayEvent(DateTime obj)
    {
        if (GetComponent<ZCalendarModel>().timeChoice)
        {
            Debug.Log($"选择的时间：{obj.ToString("yyyy-MM-dd HH:mm:ss")}");
        }
        else
        {
            Debug.Log($"选择的日期：{obj.ToString("yyyy-MM-dd")}");
        }
    }

    /// <summary>
    /// 切换月份时，可拿到每一天的item对象
    /// </summary>
    /// <param name="obj"></param>
    private void ZCalendar_UpdateDateEvent(DateTime obj)
    {
        //Debug.Log($"加载日期：{obj.Day}");
    }
}
