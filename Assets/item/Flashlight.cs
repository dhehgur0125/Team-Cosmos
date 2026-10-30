using UnityEngine;

public class Flashlight : MonoBehaviour
{
    [Header("손전등 컴포넌트")]
    public Light spotLight; // 카메라에 달아줄 스포트라이트

    [Header("배터리 설정")]
    public float maxBattery = 100f;
    public float currentBattery;
    public float drainRate = 2f; // 초당 배터리 소모량

    private bool isOn = false;

    void Start()
    {
        currentBattery = maxBattery;
        
        if (spotLight != null)
        {
            spotLight.enabled = isOn; // 처음엔 꺼진 상태로 시작
        }
    }

    void Update()
    {
        if (spotLight == null) return;

        // F키를 누르고, 배터리가 남아있을 때만 작동
        if (Input.GetKeyDown(KeyCode.F))
        {
            if (currentBattery > 0)
            {
                isOn = !isOn;
                spotLight.enabled = isOn;
                Debug.Log(isOn ? "<color=yellow>[손전등 켜짐]</color>" : "<color=gray>[손전등 꺼짐]</color>");
            }
            else
            {
                Debug.Log("<color=red>[경고] 배터리가 없어 손전등을 켤 수 없습니다!</color>");
            }
        }

        // 손전등이 켜져 있으면 배터리 소모
        if (isOn)
        {
            currentBattery -= drainRate * Time.deltaTime;

            // 배터리가 방전되면 강제로 끄기
            if (currentBattery <= 0)
            {
                currentBattery = 0;
                isOn = false;
                spotLight.enabled = false;
                Debug.Log("<color=red>[방전] 손전등 배터리가 다 닳았습니다!</color>");
            }
        }
    }
}