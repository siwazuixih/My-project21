using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ConfirmBox : MonoBehaviour
{
    public Text MsgTxt;
    public Button BtnYes;
    public Button BtnNo;
    
    private System.Action<bool> onConfirmCallback;
    
    // Start is called before the first frame update
    void Start()
    {
        if (BtnYes != null)
        {
            BtnYes.onClick.AddListener(OnBtnYesClick);
        }
        if (BtnNo != null)
        {
            BtnNo.onClick.AddListener(OnBtnNoClick);
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    
    /// <summary>
    /// 显示确认对话框
    /// </summary>
    /// <param name="message">对话框显示的文本</param>
    /// <param name="callback">确认后的回调函数，参数为true表示确认，false表示取消</param>
    public void Show(string message, System.Action<bool> callback)
    {
        if (MsgTxt != null)
        {
            MsgTxt.text = message;
        }
        onConfirmCallback = callback;
        gameObject.SetActive(true);
    }
    
    /// <summary>
    /// 点击确认按钮的处理方法
    /// </summary>
    private void OnBtnYesClick()
    {
        Debug.Log("点击了确认按钮");
        if (onConfirmCallback != null)
        {
            onConfirmCallback.Invoke(true);
        }
        gameObject.SetActive(false);
        Destroy(gameObject);
    }
    
    /// <summary>
    /// 点击取消按钮的处理方法
    /// </summary>
    private void OnBtnNoClick()
    {
        Debug.Log("点击了取消按钮");
        if (onConfirmCallback != null)
        {
            onConfirmCallback.Invoke(false);
        }
        gameObject.SetActive(false);
        Destroy(gameObject);
    }

    // 防止内存泄漏，清空回调
    private void OnDestroy()
    {
        onConfirmCallback = null;
        if (BtnYes != null)
        {
            BtnYes.onClick.RemoveListener(OnBtnYesClick);
        }
        if (BtnNo != null)
        {
            BtnNo.onClick.RemoveListener(OnBtnNoClick);
        }
    }
}
