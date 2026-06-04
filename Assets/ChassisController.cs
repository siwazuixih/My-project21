using UnityEngine;
using UnityEngine.AI;
using Mujoco;
using System.Collections;
using RobotLogic;

public class ChassisController : MonoBehaviour
{
    private MissionController mgr;
    public Vector3[] pathCorners { get; private set; }
    public int currentCornerIndex = 0;
    
    public float currentContinuousAngle = 0f;
    public float currentFacingAngle = 0f;
    private Coroutine dynamicPathCoroutine;

    public float debug_MotorX, debug_MotorZ;
    public enum MoveMode { Idle, Rotating, Moving, FinalAligning }
    public MoveMode moveMode = MoveMode.Idle;

    public void Init(MissionController manager)
    {
        mgr = manager;
        if (mgr.chassis.actuatorRot != null) 
            currentFacingAngle = (float)mgr.chassis.actuatorRot.Control * -Mathf.Rad2Deg;
    }

    public void CalculateAndStartPath(bool useCache = false)
    {
        if (!mgr.refs.targetObject) return;
        bool pathFound = false;

        if (useCache && mgr.hasPrecalculated && mgr.currentMissionIndex < mgr.globalPathCache.Count)
        {
            pathCorners = mgr.globalPathCache[mgr.currentMissionIndex];
            currentCornerIndex = (pathCorners.Length > 1) ? 1 : 0;
            pathFound = true;
        }

        if (!pathFound)
        {
            if (RecalculateNavMeshPath()) pathFound = true;
        }

        if (pathFound)
        {
            if (mgr.chassis.actuatorRot != null)
            {
                float rawAngle = (float)mgr.chassis.actuatorRot.Control * -Mathf.Rad2Deg;
                currentContinuousAngle = rawAngle - mgr.chassis.headingOffset;
                currentFacingAngle = currentContinuousAngle;
            }
            
            moveMode = MoveMode.Rotating;
            if (mgr.chassis.enableDynamicAvoidance) {
                if (dynamicPathCoroutine != null) StopCoroutine(dynamicPathCoroutine);
                dynamicPathCoroutine = StartCoroutine(DynamicAvoidanceRoutine());
            }
        }
    }

    public bool RecalculateNavMeshPath()
    {
        NavMeshPath path = new NavMeshPath();
        if (NavMesh.CalculatePath(GetRobotPosition(), mgr.refs.targetObject.position, NavMesh.AllAreas, path))
        {
            pathCorners = path.corners;
            currentCornerIndex = 1;
            return true;
        }
        return false;
    }

    public void Tick()
    {
        if (mgr.chassis.actuatorX) debug_MotorX = (float)mgr.chassis.actuatorX.Control;
        if (mgr.chassis.actuatorZ) debug_MotorZ = (float)mgr.chassis.actuatorZ.Control;

        if (pathCorners == null || pathCorners.Length == 0 || moveMode == MoveMode.Idle) return;
        if (currentCornerIndex >= pathCorners.Length) currentCornerIndex = pathCorners.Length - 1;

        Vector3 targetPoint = pathCorners[currentCornerIndex];
        Vector3 robotPos = GetRobotPosition();

        if (moveMode == MoveMode.FinalAligning)
        {
            RotateToFinalHeading();
            return;
        }
        
        Vector3 dir = targetPoint - robotPos; dir.y = 0;
        float distToPathNode = dir.magnitude;
        bool shouldStop = false;

        if (currentCornerIndex < pathCorners.Length - 1)
        {
            if (distToPathNode < mgr.chassis.stopDistance) shouldStop = true;
        }
        else if (distToPathNode < mgr.chassis.stopDistance) shouldStop = true;

        if (shouldStop)
        {
            if (currentCornerIndex < pathCorners.Length - 1)
            {
                currentCornerIndex++;
                moveMode = MoveMode.Rotating;
            }
            else
            {
                moveMode = MoveMode.FinalAligning;
            }
            return;
        }

        Vector3 navDir = targetPoint - robotPos; navDir.y = 0;
        float targetAngle = Quaternion.LookRotation(navDir).eulerAngles.y;
        float angleDiff = Mathf.DeltaAngle(currentFacingAngle, targetAngle);

        if (moveMode == MoveMode.Rotating)
        {
            if (Mathf.Abs(angleDiff) > mgr.chassis.alignThreshold) {
                float step = mgr.chassis.turnSpeed * Mathf.Rad2Deg * Time.deltaTime;
                float newAngle = Mathf.MoveTowardsAngle(currentFacingAngle, targetAngle, step);
                currentContinuousAngle += Mathf.DeltaAngle(currentFacingAngle, newAngle);
                currentFacingAngle = newAngle;
            } else moveMode = MoveMode.Moving;
        }
        else if (moveMode == MoveMode.Moving)
        {
            if (Mathf.Abs(angleDiff) > 15f) { moveMode = MoveMode.Rotating; return; }
            
            Vector3 step = navDir.normalized * mgr.chassis.moveSpeed * Time.deltaTime;
            float dx = step.x; float dz = step.z;

            if (mgr.chassis.swapXZ) { float t = dx; dx = dz; dz = t; }
            if (mgr.chassis.invertX) dx = -dx;
            if (mgr.chassis.invertZ) dz = -dz;

            if (mgr.chassis.actuatorX) mgr.chassis.actuatorX.Control += dx;
            if (mgr.chassis.actuatorZ) mgr.chassis.actuatorZ.Control += dz;
        }

        if (mgr.chassis.actuatorRot) 
            mgr.chassis.actuatorRot.Control = -(currentContinuousAngle + mgr.chassis.headingOffset) * Mathf.Deg2Rad;
    }

    private void RotateToFinalHeading()
    {
        float targetAngle = GetFinalHeadingAngle();
        float angleDiff = Mathf.DeltaAngle(currentFacingAngle, targetAngle);

        if (Mathf.Abs(angleDiff) > mgr.chassis.alignThreshold)
        {
            float step = mgr.chassis.turnSpeed * Mathf.Rad2Deg * Time.deltaTime;
            float newAngle = Mathf.MoveTowardsAngle(currentFacingAngle, targetAngle, step);
            currentContinuousAngle += Mathf.DeltaAngle(currentFacingAngle, newAngle);
            currentFacingAngle = newAngle;
        }
        else
        {
            currentContinuousAngle += Mathf.DeltaAngle(currentFacingAngle, targetAngle);
            currentFacingAngle = targetAngle;
            moveMode = MoveMode.Idle;
        }

        if (mgr.chassis.actuatorRot)
            mgr.chassis.actuatorRot.Control = -(currentContinuousAngle + mgr.chassis.headingOffset) * Mathf.Deg2Rad;

        if (moveMode == MoveMode.Idle)
        {
            if (dynamicPathCoroutine != null) StopCoroutine(dynamicPathCoroutine);
            mgr.OnChassisReachedTarget();
        }
    }

    private float GetFinalHeadingAngle()
    {
        if (mgr.currentMissionIndex < mgr.snapshots.Count)
            return mgr.snapshots[mgr.currentMissionIndex].precalcChassisAngle;

        if (pathCorners != null && pathCorners.Length >= 2)
        {
            Vector3 lastSegment = pathCorners[pathCorners.Length - 1] - pathCorners[pathCorners.Length - 2];
            lastSegment.y = 0;
            if (lastSegment.sqrMagnitude > 0.0001f)
                return Quaternion.LookRotation(lastSegment).eulerAngles.y;
        }

        return currentFacingAngle;
    }

    private IEnumerator DynamicAvoidanceRoutine()
    {
        WaitForSeconds wait = new WaitForSeconds(mgr.chassis.avoidanceUpdateInterval);
        while (moveMode != MoveMode.Idle) { yield return wait; RecalculateNavMeshPath(); }
    }

    public Vector3 GetRobotPosition() => mgr.refs.realRobotBase ? mgr.refs.realRobotBase.transform.position : transform.position;

    public unsafe void TeleportSimulationRelative(Vector3 moveDelta, Vector3 targetFwd)
    {
        if (mgr.chassis.actuatorX?.Joint != null) {
            float dx = moveDelta.x;
            if (mgr.chassis.invertX) dx = -dx;
            MjScene.Instance.Data->qpos[mgr.chassis.actuatorX.Joint.QposAddress] += dx;
        }
        if (mgr.chassis.actuatorZ?.Joint != null) {
            float dz = moveDelta.z;
            if (mgr.chassis.invertZ) dz = -dz;
            MjScene.Instance.Data->qpos[mgr.chassis.actuatorZ.Joint.QposAddress] += dz;
        }
        if (mgr.chassis.actuatorRot?.Joint != null) {
            float angleRad = -(Quaternion.LookRotation(targetFwd).eulerAngles.y + mgr.chassis.headingOffset) * Mathf.Deg2Rad;
            MjScene.Instance.Data->qpos[mgr.chassis.actuatorRot.Joint.QposAddress] = angleRad;
        }
        MujocoLib.mj_forward(MjScene.Instance.Model, MjScene.Instance.Data);
    }

    public void StopMovement()
    {
        moveMode = MoveMode.Idle;
        pathCorners = null; // 清空路径
        
        // 停止动态避障协程
        if (dynamicPathCoroutine != null) StopCoroutine(dynamicPathCoroutine);
        
        // 电机输出归零，防止重置后由于惯性继续滑动
        if (mgr.chassis.actuatorX) mgr.chassis.actuatorX.Control = 0;
        if (mgr.chassis.actuatorZ) mgr.chassis.actuatorZ.Control = 0;
        if (mgr.chassis.actuatorRot) mgr.chassis.actuatorRot.Control = 0;
    }
}
