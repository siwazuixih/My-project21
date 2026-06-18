using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ObserveParam : MonoBehaviour
{

    public static ObserveParam Instance;
    public InputField ObservationDistance;
    public EditorStyleVector3Control XYZPanel;
    public InputField BaseSpeedInput;
    public InputField ArmSpeedInput;
    public Toggle ToggleShowTarget;

    public GameObject Target;

    private ModelCollisionHighlighter lastSelectedObject;
    private MissionController missionController;

    public float? ObservationDistanceValue => GetFloat(ObservationDistance);
    public float? XValue => XYZPanel?.Value.x;
    public float? YValue => XYZPanel?.Value.y;
    public float? ZValue => XYZPanel?.Value.z;
    public float? BaseSpeedValue => GetFloat(BaseSpeedInput);
    public float? ArmSpeedValue => GetFloat(ArmSpeedInput);
    public bool ShowTarget => ToggleShowTarget != null && ToggleShowTarget.isOn;

    // Start is called before the first frame update
    private void Awake()
    {
        Instance = this;
    }
    
    void Start()
    {
        missionController = FindObjectOfType<MissionController>(true);

        if (ToggleShowTarget != null)
        {
            ToggleShowTarget.onValueChanged.AddListener(OnToggleShowTargetChanged);
        }
        if (ObservationDistance != null)
        {
            ObservationDistance.onValueChanged.AddListener(OnInputValueChanged);
        }
        if (XYZPanel != null)
        {
            XYZPanel.OnValueChanged += OnXYZValueChanged;
        }
    }

    private void OnInputValueChanged(string value)
    {
        if (ShowTarget && ModelCollisionHighlighter.selectedObject != null)
        {
            UpdateTargetPosition();
        }
    }

    private void OnXYZValueChanged(Vector3 value)
    {
        if (ShowTarget && ModelCollisionHighlighter.selectedObject != null)
        {
            UpdateTargetPosition();
        }
    }

    private void OnToggleShowTargetChanged(bool isOn)
    {
        if (!isOn)
        {
            if (Target != null)
            {
                Target.SetActive(false);
            }
            return;
        }

        if (ModelCollisionHighlighter.selectedObject == null)
        {
            return;
        }

        UpdateTargetPosition();
        if (Target != null)
        {
            Target.SetActive(true);
        }
    }

    private void UpdateTargetPosition()
    {
        if (Target == null || ModelCollisionHighlighter.selectedObject == null)
        {
            return;
        }

        Transform selectedTransform = ModelCollisionHighlighter.selectedObject.transform;
        Vector3 basePosition = selectedTransform.position;

        float distance = ObservationDistanceValue ?? 0f;
        float offsetX = XValue ?? 0f;
        float offsetY = YValue ?? 0f;
        float offsetZ = ZValue ?? 0f;

        bool useManualObservation = missionController == null || missionController.arm.useManualObservation;
        Vector3 robotPosition = missionController != null && missionController.refs.realRobotBase != null
            ? missionController.refs.realRobotBase.transform.position
            : Vector3.zero;
        Vector3 manualObservationVector = new Vector3(offsetX, offsetY, offsetZ);
        Vector3 targetPosition = ArmController.CalculateObservationPosition(
            basePosition,
            robotPosition,
            useManualObservation,
            manualObservationVector,
            distance);
        Target.transform.position = targetPosition;
    }

    // Update is called once per frame
    void Update()
    {
        if (ShowTarget && ModelCollisionHighlighter.selectedObject != lastSelectedObject)
        {
            lastSelectedObject = ModelCollisionHighlighter.selectedObject;
            if (lastSelectedObject != null)
            {
                UpdateTargetPosition();
                if (Target != null)
                {
                    Target.SetActive(true);
                }
            }
            else
            {
                if (Target != null)
                {
                    Target.SetActive(false);
                }
            }
        }
    }

    public float? GetFloat(InputField inputField)
    {
        float? value = null;
        if (inputField != null)
        {
            float f;
            if (float.TryParse(inputField.text, out f))
            {
                value = f;
            }
        }

        return value;
    }

    public float? GetFloat(TMP_InputField inputField)
    {
        float? value = null;
        if (inputField != null)
        {
            float f;
            if (float.TryParse(inputField.text, out f))
            {
                value = f;
            }
        }

        return value;
    }
}
