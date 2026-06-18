using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

public class Login : MonoBehaviour
{
    public InputField AccountTxt;
    public InputField PwdTxt;
    public Button LoginBtn;
    public Text ErrorMsgTxt;
    public GameObject Alarm;
    public int targetSceneIndex = 1; // 默认跳转到索引为1的场景
    public string TargetSceneName = "Main";
    
    // Start is called before the first frame update
    void Start()
    {
        if (AccountTxt != null)
        {
            AccountTxt.Select();
            AccountTxt.ActivateInputField();
        }
        
        if (LoginBtn != null)
        {
            LoginBtn.onClick.AddListener(OnLoginBtnClick);
        }
        
        if (ErrorMsgTxt != null)
        {
            ErrorMsgTxt.text = "";
        }
    }
    
    private bool isSwitchingFields = false;
    
    void Update()
    {
        // 当焦点在AccountTxt上按下Tab键或回车键时
        if (EventSystem.current.currentSelectedGameObject == AccountTxt.gameObject)
        {
            if (Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                isSwitchingFields = true;
                SwitchToPasswordField();
            }
        }
        // 当在密码框按下回车键时，跳过刚切换焦点的情况
        if (EventSystem.current.currentSelectedGameObject == PwdTxt.gameObject && 
            (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) && 
            !isSwitchingFields)
        {
            OnLoginBtnClick();
        }
        
        // 重置标志位
        if (isSwitchingFields)
        {
            isSwitchingFields = false;
        }
    }
    
    private void SwitchToPasswordField()
    {
        if (PwdTxt != null)
        {
            PwdTxt.Select();
            PwdTxt.ActivateInputField();
        }
    }
    
    private void OnLoginBtnClick()
    {
        string account = AccountTxt != null ? AccountTxt.text : "";
        string password = PwdTxt != null ? PwdTxt.text : "";
        
        if (string.IsNullOrEmpty(account))
        {
            ShowErrorMsg("请输入账号");
            SetFocusToAccountTxt();
            return;
        }
        
        if (string.IsNullOrEmpty(password))
        {
            ShowErrorMsg("请输入密码");
            SetFocusToPwdTxt();
            return;
        }
        
        if (AccountManager.Validate(account, password))
        {
            // 登录成功，跳转到指定场景
            //SceneManager.LoadScene(targetSceneIndex);
            this.enabled = false;
            SceneManager.LoadScene(TargetSceneName);
        }
        else
        {
            ShowErrorMsg("账号或密码错误");
            SetFocusToAccountTxt();
        }
    }
    
    private void SetFocusToAccountTxt()
    {
        if (AccountTxt != null)
        {
            AccountTxt.Select();
            AccountTxt.ActivateInputField();
        }
    }
    
    private void SetFocusToPwdTxt()
    {
        if (PwdTxt != null)
        {
            PwdTxt.Select();
            PwdTxt.ActivateInputField();
        }
    }
    
    private void ShowErrorMsg(string msg)
    {
        if (ErrorMsgTxt != null)
        {
            ErrorMsgTxt.text = msg;
        }
        else
        {
            Debug.LogError(msg);
        }
        if (Alarm != null)
        {
            Alarm.SetActive(true);
        }
    }
}
