using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MessageBox : MonoBehaviour
{
    public Text Text;
    public GameObject Panel;

    private float delayTime; // 倒计时时间（秒）
    private bool isCountingDown = false;

    void Awake()
    {
        MessageManage.MessageBox = this;
        Debug.Log("awake");
    }

    // Start is called before the first frame update
    void Start()
    {
        Debug.Log("start");

    }

    // Update is called once per frame
    void Update()
    {
        if (isCountingDown && delayTime > 0)
        {
            delayTime -= Time.deltaTime;
            // Debug.Log("delayTime: " + delayTime);
            
            if (delayTime <= 0)
            {
                isCountingDown = false;
                this.Panel.SetActive(false);
            }
        }
    }

    public void ShowMessage(string message, double delay = -1)
    {
        Text.text = message;
        this.Panel.SetActive(true);
        
        if (delay > 0)
        {
            this.delayTime = (float)delay;
            isCountingDown = true;
            Debug.Log("delayTime: " + this.delayTime);
        }
        else
        {
            isCountingDown = false;
        }
    }

    public void Close()
    {
        this.Panel.SetActive(false);
    }

    private void OnDestroy()
    {
        if (MessageManage.MessageBox == this)
        {
            MessageManage.MessageBox = null;
        }
    }
}
