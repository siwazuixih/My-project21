using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class Header : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        // 查找子GameObject的AccountTxt
        Transform accountTxtTransform = transform.Find("Container/Image/AccountTxt");
        if (accountTxtTransform != null)
        {
            Text accountTxt = accountTxtTransform.GetComponent<Text>();
            if (accountTxt != null)
            {
                // 设置Text属性为当前账号的UserName
                string userName = AccountManager.GetCurrentUserName();
                accountTxt.text = userName;
            }
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
