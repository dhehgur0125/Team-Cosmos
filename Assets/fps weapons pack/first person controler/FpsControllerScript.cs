using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FpsControllerScript : MonoBehaviour {
	public int CurrentGun=0;
	public GameObject HandRig;
	public GameObject[] GunSlots;
	public float MouseSensitivity;
	public float MoveSpeed;
	public float SprintSpeed;
	public bool HeadBob;
	public Transform FpsCam;

	public KeyCode Gun1;
	public KeyCode Gun2;
	public KeyCode Gun3;
	public KeyCode Gun4;
	public KeyCode Forward;
	public KeyCode Backward;
	public KeyCode Left;
	public KeyCode Right;
	public KeyCode Fire;
	public KeyCode Ads;
	public KeyCode Reload;
	public KeyCode Sprint;

	Rigidbody rgbdy;
	AnimationCurve IdleCurve;
	float IdleHeadBobAmount = 0.02f;
	float IdleHeadBobSpeed = 1.0f;
	float WalkingHeadBobAmount = 0.1f;
	float WalkingHeadBobSpeed = 0.25f;
	AnimationCurve WalkingHeadBobCurve;





	void Start()
	{
		Cursor.lockState = CursorLockMode.Locked;
		//Activate our CurrentGun
		GunSlots[CurrentGun].SetActive(true);
		//define our curves here
		IdleCurve = new AnimationCurve(new Keyframe(0, 0.5f-(IdleHeadBobAmount/2.0f)), new Keyframe(IdleHeadBobSpeed, 0.5f+(IdleHeadBobAmount/2.0f)));
		IdleCurve.preWrapMode = WrapMode.PingPong;
		IdleCurve.postWrapMode = WrapMode.PingPong;
		WalkingHeadBobCurve = new AnimationCurve(new Keyframe(0, 0.5f-(WalkingHeadBobAmount/2.0f)), new Keyframe(WalkingHeadBobSpeed, 0.5f+(WalkingHeadBobAmount/2.0f)));
		WalkingHeadBobCurve.preWrapMode = WrapMode.PingPong;
		WalkingHeadBobCurve.postWrapMode = WrapMode.PingPong;

		//get rigidbiody
		rgbdy = GetComponent<Rigidbody> ();
		rgbdy.freezeRotation = true;

	}

	void Update()
	{

	//Gun Swapping Controls here 
		int swapChecker = CurrentGun;//ONLY CHANGE "CurrentGun" VARIABLE AFTER THIS LINE

	//Get Input and set CurrentGun
		if (Input.GetKeyDown (Gun1)) {
			CurrentGun=0;
		}
		if (Input.GetKeyDown (Gun2)) {
			CurrentGun=1;
		}
		if (Input.GetKeyDown (Gun3)) {
			CurrentGun=2;
		}
		if (Input.GetKeyDown (Gun4)) {
			CurrentGun=3;
		}

		//check for gun change and disable/enable the proper weapon slots ONLY CHANGE "CurrentGun" VARIABLE BEFORE THIS LINE
		if (swapChecker != CurrentGun) {
			foreach (GameObject gun in GunSlots)
				gun.SetActive(false);	
			GunSlots[CurrentGun].SetActive(true);
			GunSlots [CurrentGun].GetComponent<Animator> ().Play (GunSlots [CurrentGun].name+"Draw",0,0.0f);
			HandRig.GetComponent<Animator> ().Play (GunSlots [CurrentGun].name+"Draw",0,0.0f);
		}

	//Gun Controls Here
		//LeftClickDown
		if(Input.GetKeyDown(Fire)){
			GunSlots [CurrentGun].GetComponent<Animator> ().Play (GunSlots [CurrentGun].name+"HipFire",0,0.0f);
			HandRig.GetComponent<Animator> ().Play (GunSlots [CurrentGun].name+"HipFire",0,0.0f);
		}
		//RightClickDown
		if(Input.GetKeyDown(Ads)){
			GunSlots [CurrentGun].GetComponent<Animator> ().Play (GunSlots [CurrentGun].name+"AdsUp",0,0.0f);
			HandRig.GetComponent<Animator> ().Play (GunSlots [CurrentGun].name+"AdsUp",0,0.0f);
		}
		//RightClickUp
		if(Input.GetKeyUp(Ads)){
			GunSlots [CurrentGun].GetComponent<Animator> ().Play (GunSlots [CurrentGun].name+"AdsDown",0,0.0f);
			HandRig.GetComponent<Animator> ().Play (GunSlots [CurrentGun].name+"AdsDown",0,0.0f);
		}
		//Right & Left Click
		if(Input.GetKeyDown(Fire) && Input.GetKey(Ads)){
			GunSlots [CurrentGun].GetComponent<Animator> ().Play (GunSlots [CurrentGun].name+"AdsFire",0,0.0f);
			HandRig.GetComponent<Animator> ().Play (GunSlots [CurrentGun].name+"AdsFire",0,0.0f);
		}
		if (Input.GetKeyDown (Reload)) {
			GunSlots [CurrentGun].GetComponent<Animator> ().Play (GunSlots [CurrentGun].name+"Reload", 0, 0.0f);
			HandRig.GetComponent<Animator> ().Play (GunSlots [CurrentGun].name+"Reload",0,0.0f);
		}
		if (Input.GetKeyDown (KeyCode.Q)) {
			GunSlots [CurrentGun].GetComponent<Animator> ().Play (GunSlots [CurrentGun].name+"DisplayKey", 0, 0.0f);
			HandRig.GetComponent<Animator> ().Play (GunSlots [CurrentGun].name+"DisplayKey",0,0.0f);
		}
			

	//Zoom Controls
		if (Input.GetKey (Ads)) {
			FpsCam.GetComponent<Camera> ().fieldOfView = Mathf.Lerp (FpsCam.GetComponent<Camera> ().fieldOfView, 30, 0.5f);
		} else {
			FpsCam.GetComponent<Camera> ().fieldOfView = Mathf.Lerp (FpsCam.GetComponent<Camera> ().fieldOfView, 60, 0.5f);
		}
			
	}

	void LateUpdate()
	{
		FpsCam.Rotate (0, Input.GetAxis ("Mouse X")*MouseSensitivity, 0,Space.World);
		Vector3 CamClamper = FpsCam.localRotation.eulerAngles;
		CamClamper.x -= Input.GetAxis ("Mouse Y") * MouseSensitivity;
		if (CamClamper.x <180 && CamClamper.x > 80) {
			CamClamper.x = 80;
		}
		if (CamClamper.x >180 && CamClamper.x < 280) {
			CamClamper.x = 280;
		}
		FpsCam.localRotation = Quaternion.Euler(CamClamper);
	}

	void FixedUpdate()
	{

		//Movement system here
		Vector3 MoveVec = Vector3.zero;
		//MOVEMENT INPUTS
		if (Input.GetKey (Forward)) {//Forward
			MoveVec.z += 1;
		}
		if (Input.GetKey (Left)) {//left
			MoveVec.x -= 1;
		}
		if (Input.GetKey (Backward)) {//Backward
			MoveVec.z -= 1;
		}
		if (Input.GetKey (Right)) {//right
			MoveVec.x += 1;
		}

		//fix movement vectors
		MoveVec = FpsCam.TransformDirection (MoveVec);
		MoveVec.y = 0;
		MoveVec.Normalize();
		if (Input.GetKey (KeyCode.LeftShift) && !Input.GetKey(KeyCode.Mouse1)) {
			MoveVec *= SprintSpeed;
		} else {
			MoveVec *= MoveSpeed;
		}
		MoveVec *= Time.deltaTime;

		//HeadBob movement
		if(HeadBob)
			FpsCam.localPosition = new Vector3(FpsCam.localPosition.x, IdleCurve.Evaluate(Time.time), FpsCam.localPosition.z);
		if(HeadBob && MoveVec != Vector3.zero)
			FpsCam.localPosition = new Vector3(FpsCam.localPosition.x, WalkingHeadBobCurve.Evaluate(Time.time), FpsCam.localPosition.z);
		//do Movement
		rgbdy.linearVelocity = new Vector3 (MoveVec.x,rgbdy.linearVelocity.y,MoveVec.z);
	}
}