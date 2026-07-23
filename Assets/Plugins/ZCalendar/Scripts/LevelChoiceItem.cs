/*
* Made By JacobKay
*/
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System;
using Unity.VisualScripting;
using System.Net.Sockets;
using Demo;

public class LevelChoiceItem : MonoBehaviour
{
    #region variable
    LoopVerticalScrollRect layout;
    public Text txt;
    public Image choiceBox;

    Vector2 rangeArea;
    bool isChoice = false;
    int index;
    float crtPos = 0;
    float centerPos;
    Vector2 scaleRange = new Vector2(0.8f, 1);
    RectTransform trm;
    Color origColor;
    InitOnStart prtObj;
    int levelNum = 0;
    #endregion

    #region Property
    public string Text
    {
        set
        {
            txt.text = value;
        }
    }
    #endregion

    #region endregion
    private void Start()
    {
        this.GetComponent<Button>().onClick.AddListener(() =>
        {
            layout.ScrollToCellWithinTime(index - 1, 0.1f);
        });
        origColor = choiceBox.color;
        prtObj = transform.GetComponentInParent<InitOnStart>();
        centerPos = prtObj.ChoiceBox.transform.position.y;
        layout = GetComponentInParent<LoopVerticalScrollRect>();
        rangeArea.x = centerPos - (transform as RectTransform).sizeDelta.y / 2f;
        rangeArea.y = centerPos + (transform as RectTransform).sizeDelta.y / 2f;
    }
    void ScrollCellIndex(int idx)
    {
        if (prtObj == null)
        {
            prtObj = transform.GetComponentInParent<InitOnStart>();
        }
        index = idx;
        int val = idx + prtObj.addVal;
        if (val % prtObj.maxValue < 0)
        {
            levelNum = prtObj.maxValue + (val % prtObj.maxValue);
        }
        else if (val % prtObj.maxValue > 0)
        {
            levelNum = val % prtObj.maxValue;
        }
        else if (val % prtObj.maxValue == 0)
        {
            levelNum = 0;
        }
        txt.text = levelNum.ToString("00");
    }
    float lastPos = 0;
    long crtTime = 0;
    private void Update()
    {
        if (transform.position.y >= rangeArea.x && transform.position.y <= rangeArea.y)
        {
            if (layout.isPointDown)
            {
                isChoice = false;
                return;
            }
            if (isChoice) return;

            float val = transform.position.y > crtPos ? transform.position.y - crtPos : crtPos - transform.position.y;
            crtPos = transform.position.y;
            if (val <= 0.4f)
            {
                origColor.a = 0.8f;
                choiceBox.color = origColor;
                //choice
                prtObj.choiceTime = levelNum;
                isChoice = true;
                Move2Center();
            }
        }
        else
        {
            origColor.a = 0;
            choiceBox.color = origColor;
            isChoice = false;
        }
    }
    void UpdateSize()
    {
        txt.transform.localScale = Vector3.one * (scaleRange.y - (scaleRange.y - scaleRange.x) * ((centerPos >= transform.position.y) ? centerPos - transform.position.y : transform.position.y - centerPos) / (transform as RectTransform).sizeDelta.y);
    }
    /// <summary>
    /// 移动到中间
    /// </summary>
    void Move2Center()
    {
        layout.ScrollToCellWithinTime(index - 1, 0.1f);
        CancelInvoke();
        Invoke("TimeChoice", 0.1f);
    }
    void TimeChoice()
    {
        if (!prtObj.isInit)
        {
            prtObj.zCalendar.TimeChoice();
        }
        if (prtObj.isInit)
        {
            prtObj.isInit = false;
        }
    }
    #endregion
}
