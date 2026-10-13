using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class RobotTaskManager : NetworkBehaviour
{
    public static RobotTaskManager Instance { get; private set; }

    [Header("대기열 설정")]
    [Tooltip("관제탑이 기억할 수 있는 최대 예약 개수")]
    [SerializeField] private int maxQueueSize = 10;
    [SerializeField] private int currentQueueCount = 0;

    private List<Vector3> taskList = new List<Vector3>();
    private List<RobotCommandManager> allRobots = new List<RobotCommandManager>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void RegisterRobot(RobotCommandManager robot)
    {
        if (!IsServer) return;
        
        if (!allRobots.Contains(robot))
        {
            allRobots.Add(robot);
            Debug.Log($"[관제탑] 새 로봇 등록 완료! 현재 등록된 총 로봇 수: {allRobots.Count}대");
            
            // 🌟 [핵심 수정] 로봇이 새로 등록되었는데 대기열에 밀린 일이 있다면 즉시 할당!
            TryAssignTasks();
        }
    }

    public void AddMiningTask(Vector3 targetPos)
    {
        if (!IsServer) return;

        if (taskList.Count >= maxQueueSize)
        {
            Debug.Log($"[관제탑] 대기열이 가득 찼습니다! ({maxQueueSize}개) 더 이상 예약을 받을 수 없습니다.");
            return;
        }

        if (!taskList.Contains(targetPos))
        {
            taskList.Add(targetPos);
            currentQueueCount = taskList.Count;
            Debug.Log($"[관제탑] 새 광물 좌표 접수! 현재 대기열: {currentQueueCount}/{maxQueueSize}");
            TryAssignTasks();
        }
    }

    public void CancelTask(Vector3 targetPos)
    {
        if (!IsServer) return;

        if (taskList.Contains(targetPos))
        {
            taskList.Remove(targetPos);
            currentQueueCount = taskList.Count;
            Debug.Log($"[관제탑] 대기열에서 좌표 취소됨: {targetPos}");
            return;
        }

        foreach (var robot in allRobots)
        {
            if (!robot.IsIdle && robot.TargetPosition == targetPos)
            {
                Debug.Log($"[관제탑] 해당 좌표({targetPos})로 이동 중인 로봇을 강제 호출합니다!");
                robot.ForceCancelTask();
                return;
            }
        }
    }

    public void TryAssignTasks()
    {
        if (!IsServer) return;
        if (taskList.Count == 0) return;

        foreach (RobotCommandManager robot in allRobots)
        {
            // 로봇이 놀고 있고, 줄 일이 남아있다면
            if (robot.IsIdle && taskList.Count > 0)
            {
                Vector3 nextTask = taskList[0];
                taskList.RemoveAt(0); 
                currentQueueCount = taskList.Count;
                
                Debug.Log($"[관제탑] 로봇에게 작업 할당! 남은 대기열: {taskList.Count}개");
                robot.AssignTask(nextTask);
            }
        }
    }
}