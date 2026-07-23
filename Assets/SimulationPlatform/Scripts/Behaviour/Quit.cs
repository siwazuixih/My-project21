using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class Quit : MonoBehaviour
{
     // 直接调用此方法退出游戏，给按钮绑定OnClick用
    public void Exit()
    {
        OperationLogTool.RecordLog(OperationType.系统操作, "退出登录");

        SceneManager.LoadScene("LoginScene");
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
