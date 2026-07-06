using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;
using System;

public class HandTrackingManager : MonoBehaviour
{
    public List<HandTrackingDataSettings> dataSettings = new List<HandTrackingDataSettings>();

    private XRHandSubsystem handSubsystem;
    private Dictionary<string, GameObject> spawnedObjDic = new Dictionary<string, GameObject>();
    // Start is called before the first frame update
    void Start()
    {
        GetHandSubsystem();
    }

    // Update is called once per frame
    void Update()
    {
        if (!CheckHandSubsystem())
        {
            return;
        }
        // 물체와 손 관절의 동기화된 추적 처리
        TrackHands();
    }
    private void OnDestroy()
    {
        if (!CheckHandSubsystem())
        {
            return;
        }
        handSubsystem.trackingAcquired -= OnHandTrackingAcquired;
        handSubsystem.trackingLost -= OnHandTrackingLost;
    }
    /// <summary>
    /// HandSubsystem이 있는 확인 
    /// </summary>
    /// <returns></returns>
    private bool CheckHandSubsystem()
    {
        if (handSubsystem == null)
        {
#if !UNITY_EDITOR
                Debug.LogError("Could not find Hand Subsystem");
#endif
            enabled = false;
            return false;
        }

        return true;
    }
    /// <summary>
    /// HandSubsystem 얻고, 초기화 수행
    /// </summary>
    private void GetHandSubsystem()
    {
        XRGeneralSettings xrGeneralSettings = XRGeneralSettings.Instance;
        if (xrGeneralSettings == null)
        {
            Debug.LogError("XR general settings not set");
            return;
        }

        XRManagerSettings manager = xrGeneralSettings.Manager;
        if (manager != null)
        {
            XRLoader loader = manager.activeLoader;
            if (loader != null)
            {
                handSubsystem = loader.GetLoadedSubsystem<XRHandSubsystem>();
                if (!CheckHandSubsystem())
                    return;

                handSubsystem.Start();
                handSubsystem.trackingAcquired += OnHandTrackingAcquired; //제스처가 감지된 이벤트
                handSubsystem.trackingLost += OnHandTrackingLost; //추적에서 벗어난 이벤트
                //handSubsystem.updatedHands += OnUpdateHands; //손 추적 데이터를 업데이트하는 이벤트
            }
        }
    }



    private void OnUpdateHands(XRHandSubsystem handSubsystem, XRHandSubsystem.UpdateSuccessFlags updateSuccessFlags, XRHandSubsystem.UpdateType updateType)
    {
        //사용자 정의 로직 실행
    }

    private void OnHandTrackingAcquired(XRHand hand)
    {
        foreach (HandTrackingDataSettings handTrackingData in dataSettings)
        {
            //왼손을 추적하면 왼손을 따라 움직이는 모든 물체가 표시되며, 오른손도 마찬가지
            if (hand.handedness == handTrackingData.handedness)
            {
                string trackingKey = GetTrackingKey(handTrackingData.handedness, handTrackingData.handJointID);
                if (spawnedObjDic.TryGetValue(trackingKey, out GameObject trackingObj))
                {
                    trackingObj.SetActive(true);
                    
                }
            }
        }
    }

    private void OnHandTrackingLost(XRHand hand)
    {
        foreach(HandTrackingDataSettings handTrackingData in dataSettings)
        {
            //왼손 추적 신호가 끊기면, 왼손을 따라가는 모든 물체가 숨겨집니다. 오른손도 마찬가지
            if (hand.handedness == handTrackingData.handedness)
            {
                string trackingKey = GetTrackingKey(handTrackingData.handedness, handTrackingData.handJointID);
                if(spawnedObjDic.TryGetValue(trackingKey, out GameObject trackingObj))
                {
                    trackingObj.SetActive(false);
                    
                }
            }
        }
    }
    

    private void TrackHands()
    {
        foreach(HandTrackingDataSettings handTrackingData in dataSettings)
        {
            XRHand hand = default(XRHand);
            if (handTrackingData.handedness == Handedness.Left)
            {
                hand = handSubsystem.leftHand;
            }
            else if (handTrackingData.handedness == Handedness.Right)
            {
                hand = handSubsystem.rightHand;
            }
            //HandSubsystem이 실행 중이라면, 해당 손의 오브젝트 팔로우 기능을 처리합니다.
            if (handSubsystem.running)
            {
                //우리가 정의한 손 관절을 얻는다
                XRHandJoint joint = hand.GetJoint(handTrackingData.handJointID);
                if (joint.id == XRHandJointID.Invalid)
                {
                    return;
                }
                GameObject spawnedObj = null;
                //스크립트에서 참조하는 prefabToSpawn은 프리팹으로, 처음에는 씬에 생성되어 있지 않으므로 먼저 인스턴스화해야 합니다.
                string trackingKey = GetTrackingKey(handTrackingData.handedness, handTrackingData.handJointID);
                if (!spawnedObjDic.ContainsKey(trackingKey))
                {
                    spawnedObj = GameObject.Instantiate(handTrackingData.prefabToSpawn);
                    //손 관절과 생성된 오브젝트 간의 대응 관계를 딕셔너리에 저장하여, 다음 번에는 해당 손 관절에 바인딩된 오브젝트를 바로 얻을 수 있도록 한다
                    spawnedObjDic.Add(trackingKey, spawnedObj);
                }
                else
                {
                    spawnedObj = spawnedObjDic[trackingKey];
                }
                //물체와 손 관절의 동기화된 추적 처리
                AttachObjToJoint(spawnedObj, joint);
            }
        }
        
        
    }
    private void AttachObjToJoint(GameObject spawnedObj, XRHandJoint joint)
    {
        if(joint.TryGetPose(out Pose pose))
        {
            //Pose에는 관절의 위치 및 회전 데이터가 포함됩니다.
            spawnedObj.transform.SetPositionAndRotation(pose.position, pose.rotation);
            
        }
    }

    private string GetTrackingKey(Handedness handedness, XRHandJointID jointID)
    {
        return $"{handedness}:{jointID}";
    }

    public XRHandJoint GetSpecificHandJoint(Handedness handedness, XRHandJointID jointID)
    {
        if (!CheckHandSubsystem())
        {
            return default(XRHandJoint);
        }
        else
        {
            XRHand hand = default(XRHand);
            if (handedness == Handedness.Left)
            {
                hand = handSubsystem.leftHand;
            }
            else if (handedness == Handedness.Right)
            {
                hand = handSubsystem.rightHand; 
            }
            if (handSubsystem.running)
            {
                return hand.GetJoint(jointID);
            }
            else
            {
                return default(XRHandJoint);
            }
        }
    }
}
[Serializable]
public struct HandTrackingDataSettings
{
    public Handedness handedness; // 어느 손
    public XRHandJointID handJointID; //어느 관절?
    public GameObject prefabToSpawn; //해당 관절 부위에 생성해야 할 객체
}
