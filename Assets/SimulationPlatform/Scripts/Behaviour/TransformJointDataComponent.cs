using UnityEngine;
using System;

public class TransformJointDataComponent : MonoBehaviour
{
    [Header("关节模型数据")]
    public JointModel jointModel;
    public JointParamInfo paramInfo;

    [Header("保存数据")]
    public int index;
    public Transform transformRef;
    public Vector3D position;

    [Header("任务事件绑定")]
    public MissionController missionController;

    [ContextMenu("刷新数据")]
    public void RefreshData()
    {
        transformRef = transform;
        position = new Vector3D(transform.position);
        if (missionController != null)
        {
            missionController.OnMissionIndexChanged += OnMissionIndexChangedHandler;
        }
    }

    private void Awake()
    {
    }

    private void OnDestroy()
    {
        if (missionController != null)
        {
            missionController.OnMissionIndexChanged -= OnMissionIndexChangedHandler;
        }
    }

    private void OnMissionIndexChangedHandler(int oldIndex, int newIndex)
    {
        if (newIndex == index && paramInfo != null)
        {
            paramInfo.SetJoint(jointModel);
            paramInfo.gameObject.SetActive(true);
        }
    }

    private void OnValidate()
    {
        RefreshData();
    }
}
