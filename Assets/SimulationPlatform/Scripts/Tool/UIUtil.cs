using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UIUtil
{
    public static bool IsPointerOverUI()
    {
        if (EventSystem.current == null)
            return false;

        // 1. 鼠标正在UI上
        if (EventSystem.current.IsPointerOverGameObject())
            return true;

        // 2. 输入框正在聚焦打字（正确写法！不会报错）
        if (IsAnyInputFieldFocused())
            return true;

        return false;
    }

    /// <summary>
    /// 判断是否有任何输入框正在输入
    /// </summary>
    static bool IsAnyInputFieldFocused()
    {
        // 先获取当前选中的对象
        GameObject focused = EventSystem.current.currentSelectedGameObject;
        if (focused == null) return false;

        // 判断是否是 TMP 输入框 或 普通输入框
        return focused.GetComponent<TMP_InputField>() != null
            || focused.GetComponent<InputField>() != null;
    }
}
