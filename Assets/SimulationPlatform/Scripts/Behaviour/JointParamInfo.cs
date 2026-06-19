using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class JointParamInfo : MonoBehaviour
{
    public Text Lead;
    public Text PicthDiameter;
    public Text Torque;
    public Text Angle;
    public Text Threshold;
    public Text Travel;

    private JointModel _Joint;

    public void SetJoint(JointModel joint)
    {
        _Joint = joint;
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (_Joint != null)
        {
            if (Lead != null)
            {
                Lead.text = _Joint.Param?.Lead;
            }
            if (PicthDiameter != null)
            {
                PicthDiameter.text = _Joint.Param?.PitchDiameter;
            }
            if (Torque != null)
            {
                Torque.text = _Joint.Craft?.Torque;
            }
            if (Angle != null)
            {
                Angle.text = _Joint.Craft?.Angle;
            }
            if (Threshold != null)
            {
                Threshold.text = _Joint.Craft?.Threshold;
            }
            if (Travel != null)
            {
                Travel.text = _Joint.Craft?.Travel;
            }
        }
    }
}
