using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PopupManager : MonoBehaviour
{
    // 单例实例
    public static PopupManager Instance { get; private set; }

    [Header("预制体引用")]
    [SerializeField] private ConfirmBox confirmCancelPopupPrefab;

    // 缓存弹窗实例，避免重复创建
    private ConfirmBox popupInstance;

    private Canvas canvas;

    private void Awake()
    {
        // 单例初始化
        Instance = this;

        canvas = GameObject.FindObjectOfType<Canvas>();

        // 预创建弹窗实例（可选，提升性能）
        if (confirmCancelPopupPrefab != null)
        {
            popupInstance = Instantiate(confirmCancelPopupPrefab, canvas.transform);
            popupInstance.gameObject.SetActive(false);
        }
        else
        {
            Debug.LogError("请为PopupManager赋值ConfirmCancelPopup预制体！");
        }
    }

    /// <summary>
    /// 显示确认取消弹窗
    /// </summary>
    /// <param name="message">要显示的文本</param>
    /// <param name="onResult">点击后的回调（返回true/false）</param>
    public void ShowConfirmCancelPopup(string message, Action<bool> onResult)
    {
        if (canvas == null)
        {
            canvas = GameObject.FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                Debug.LogError("找不到Canvas对象，无法显示弹窗！");
                return;
            }
        }

        if (confirmCancelPopupPrefab == null)
        {
            Debug.LogError("ConfirmCancelPopup预制体未赋值，无法显示弹窗！");
            return;
        }

        if (popupInstance == null)
        {
            // 如果未预创建，则动态创建
            popupInstance = Instantiate(confirmCancelPopupPrefab, canvas.transform);
        }

        // 初始化并显示弹窗
        popupInstance.Show(message, onResult);
    }
}
