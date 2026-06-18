using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class StatusPanel : MonoBehaviour
{

    public Image Image;
    public StatusType StatusType;
    public Sprite Success;
    public Sprite Error;
    public Sprite Warning;
    public TextMeshProUGUI Text;
    public string Msg
    {
        get
        {
            return Text.text;
        }
        set
        {
            Text.text = value;
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        switch(StatusType)
        {
            case StatusType.SUCCESS:
                Image.sprite = Success;
                Text.color = new Color(0x05, 0xdf, 0x72);
                break;
            case StatusType.ERROR:
                Image.sprite = Error;
                Text.color = new Color(0xfb, 0x2c, 0x36);
                break;
            case StatusType.WARNING:
                Image.sprite = Warning;
                Text.color = new Color(0xff, 0x89, 0x04);
                break;
            case StatusType.NONE:
                Image.sprite = null;
                Text.color = Color.white; 
                break;
        }
    }
}
