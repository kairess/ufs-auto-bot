using System;
using System.Collections;
using System.Collections.Generic;
using BeautifyEffect;
using BitStrap;
using CodeStage.AdvancedFPSCounter;
using Colorful;
using Moonlit.FootstepPro;
using Moonlit.IceFishing;
using UltimateWater;
using uNature.Core.Seekers;
using UnityEngine;
using UnityEngine.PostProcessing;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.UI;
using UnityStandardAssets.ImageEffects;
using VolumetricFogAndMist;

// Token: 0x02000744 RID: 1860
[RequireComponent(typeof(SphereCollider))]
public class FishingPlayer : MonoBehaviour
{
	// Token: 0x06002B53 RID: 11091 RVA: 0x000F8FB4 File Offset: 0x000F71B4
	private void Awake()
	{
		this.transform = base.GetComponent<Transform>();
		if (VRManager.IsVROn() && VRControllersManager.Instance && VRControllersManager.Instance.IsLeftHanded() && GameController.Instance)
		{
			VRControllersManager.Instance.SetLeftHanded(false);
			this.vrWasLeftHanded = true;
		}
	}

	// Token: 0x06002B54 RID: 11092 RVA: 0x00007702 File Offset: 0x00005902
	private void Start()
	{
	}

	// Token: 0x06002B55 RID: 11093 RVA: 0x000F9018 File Offset: 0x000F7218
	private void OnDestroy()
	{
		if (this.postProcessingBehaviour_1 && !VRManager.IsVROn())
		{
			AntialiasingModel.Settings settings = this.postProcessingBehaviour_1.profile.antialiasing.settings;
			settings.taaSettings.stationaryBlending = 0.85f;
			settings.taaSettings.motionBlending = 0.75f;
			this.postProcessingBehaviour_1.profile.antialiasing.settings = settings;
		}
		WaterProjectSettings.Instance.ClipWaterCameraRange = true;
		WaterProjectSettings.Instance.CameraClipRange = 1000f;
	}

	// Token: 0x06002B56 RID: 11094 RVA: 0x000F90A8 File Offset: 0x000F72A8
	public void Initialize()
	{
		this.gameController = GameController.Instance;
		this.characteController = base.GetComponent<CharacterController>();
		this.ufpsController = base.GetComponent<vp_FPController>();
		this.ufpsInput = base.GetComponent<vp_FPInput>();
		this.ufpsWeaponHandler = base.GetComponent<vp_FPWeaponHandler>();
		this.ufpsWeapon = base.GetComponentInChildren<vp_FPWeapon>();
		this.ufpsPlayerEventHandler = base.GetComponent<vp_FPPlayerEventHandler>();
		this.ufpsSimpleCrosshair = base.GetComponent<vp_SimpleCrosshair>();
		this.ufpsCamera = base.GetComponentInChildren<vp_FPCamera>();
		this.ufpsCamera.GetComponent<UnderwaterIME>().EffectEnabled = false;
		this.ufpsCameraCamera = this.ufpsCamera.GetComponent<Camera>();
		this.ufpsInteractiveManage = base.GetComponent<vp_FPInteractManager>();
		this.ufpsFootstepManager = base.GetComponent<vp_FootstepManager>();
		this.defaultCrosshair = this.ufpsSimpleCrosshair.m_ImageCrosshair;
		this.freeCamera = this.ufpsCamera.GetComponent<MyFreeCamera>();
		this.freeCamera.enabled = false;
		this.underwaterCamera.fishingPlayer = this;
		this.gaussianBlur = this.ufpsWeaponCamera.GetComponent<GaussianBlur>();
		if (this.gaussianBlur)
		{
			this.gaussianBlur.enabled = false;
		}
		this.fxDrunk = this.ufpsWeaponCamera.GetComponent<CameraFilterPack_FX_Drunk>();
		this.fxBlood1 = this.ufpsWeaponCamera.GetComponent<CameraFilterPack_AAA_Blood_Hit>();
		this.volumetricFog = this.ufpsCamera.GetComponent<VolumetricFog>();
		this.hueFocus = this.ufpsWeaponCamera.GetComponent<HueFocus>();
		this.grayscale = this.ufpsWeaponCamera.GetComponent<global::Colorful.Grayscale>();
		this.vintageFast = this.ufpsWeaponCamera.GetComponent<VintageFast>();
		this.sunShafts = this.ufpsWeaponCamera.GetComponent<SunShafts>();
		this.waterDropPro = this.ufpsWeaponCamera.GetComponent<CameraFilterPack_AAA_WaterDropPro>();
		this.waterDropProUnderwater = this.underwaterCamera.GetComponent<CameraFilterPack_AAA_WaterDropPro>();
		this.currentWaterDropPro = this.waterDropPro;
		this.hunterBrightness = this.ufpsWeaponCamera.GetComponent<CameraFilterPack_Colors_Brightness>();
		this.depthOfField = this.ufpsCamera.GetComponent<UnityStandardAssets.ImageEffects.DepthOfField>();
		this.waterCamera = this.ufpsCamera.GetComponent<WaterCamera>();
		this.underwaterWaterCamera = this.underwaterCamera.GetComponent<WaterCamera>();
		this.waterRaindropsIME = this.ufpsWeaponCamera.GetComponent<WaterRaindropsIME>();
		this.waterDropsRainMy = this.rain.GetComponent<WaterDropsRainMy>();
		this.postProcessingBehaviour_1 = this.ufpsCamera.GetComponent<PostProcessingBehaviour>();
		this.postProcessingBehaviour_2 = this.ufpsWeaponCamera.GetComponent<PostProcessingBehaviour>();
		this.beautify = this.ufpsWeaponCamera.GetComponent<Beautify>();
		this.amplifyMotionEffect = this.ufpsCamera.GetComponent<AmplifyMotionEffect>();
		this.amplifyOcclusion = this.ufpsCamera.GetComponent<AmplifyOcclusionEffect>();
		this.amplifyOcclusionUnderwater = this.underwaterCamera.GetComponent<AmplifyOcclusionEffect>();
		this.unSeeker = this.ufpsCamera.GetComponent<UNSeeker>();
		if (VRManager.IsVROn())
		{
			this.postProcessLayer = this.ufpsCamera.GetComponent<PostProcessLayer>();
		}
		else
		{
			this.postProcessLayer = this.ufpsWeaponCamera.GetComponent<PostProcessLayer>();
			this.ufpsCamera.GetComponent<PostProcessLayer>().antialiasingMode = PostProcessLayer.Antialiasing.None;
		}
		this.depthOfField.focalLength = 0.8f;
		this.waterSimulationSpace = base.GetComponentInChildren<WaterSimulationArea>(true);
		if (GlobalSettings.Instance)
		{
			this.waterSimulationSpace.gameObject.SetActive(false);
		}
		this.maskCamera = this.ufpsCamera.GetComponentInChildren<MaskCamera>(true);
		this.maskCamera.gameObject.SetActive(this.gameController.iceLevel);
		this.floatCamera.fishingPlayer = this;
		this.floatCamera.Initialize();
		this.baitIndicator = global::UnityEngine.Object.Instantiate<BaitIndicator>(this.baitIndicatorPrefab);
		this.gameController.hudManager.hudFishing.baitIndicator = this.baitIndicator;
		this.SetDrunkLevel(0f, true);
		if (this.fxDrunk)
		{
			this.fxDrunk.enabled = false;
		}
		this.drillingController = base.GetComponent<DrillingController>();
		this.drillingController.fishingPlayer = this;
		this.ufpsController.MotorAcceleration = (this.currentMotorAcceleration = this.motorAccelerationValues.x);
		this.ufpsBodyAnimator.gameObject.SetActive(false);
		if (this.currentHands == null)
		{
			this.currentHands = base.GetComponentInChildren<FishingHands>(false);
			if (this.currentHands)
			{
				this.currentHands.SetFishingPlayer();
			}
		}
		this.RefreshWeatherSettings();
		this.ChangeState(FishingPlayer.PlayerState.NORMAL);
		this.BlockMouseLook(true, 4f);
		this.ovrPlayerController.gameObject.SetActive(VRManager.IsVROn());
		this.afpsCounter.gameObject.SetActive(VRManager.IsVROn() && VRManager.Instance.showAfpsCounter);
		if (GlobalSettings.Instance && GlobalSettings.Instance.turnOnMyCheats && VRManager.IsVROn())
		{
			this.afpsCounter.gameObject.SetActive(true);
		}
		this.afpsCounter.Shadow = true;
		if (VRManager.IsVROn())
		{
			VRManager.Instance.InitializePlayer(this);
			HUDManager.Instance.InitializeVR();
			this.ChangeVRCameraHeight(VRManager.Instance.cameraHeight);
			this.ufpsCameraCamera.nearClipPlane = 0.1f;
			this.defaultHoleDistance = -0.2f;
		}
	}

	// Token: 0x06002B57 RID: 11095 RVA: 0x000F95C4 File Offset: 0x000F77C4
	public void LateInitialize()
	{
		if (this.wasLateInitialized)
		{
			return;
		}
		this.UpdateFOV(true);
		this.ufpsWeaponCamera.depth = 2f;
		HUDManager.Instance.ShowRadar(false);
		this.SetPosition(this.startTransform.position);
		this.SetRotation(this.startTransform.eulerAngles);
		if (this.gameController.iceLevel)
		{
			this.ufpsWeapon.ShakeSpeed = 0.13f;
			this.ufpsWeapon.ShakeAmplitude = new Vector3(0.25f, 0.4f, 0.4f);
		}
		LeanTween.delayedCall(0.01f, delegate
		{
			this.currentHands.ChangeRod();
		});
		this.ResetFishing(true);
		this.currentHands.fishingRod.ResetBend();
		this.currentHands.flashLight.enabled = false;
		this.SitDown(-1f, true);
		LeanTween.delayedCall((!this.gameController.spawnAllAtStart) ? 2f : 0.5f, delegate
		{
			this.gameController.hudManager.FadeDark(0f, 1.5f, true);
			if (VRManager.IsVROn())
			{
			}
		});
		if (this.gameController.iceLevel)
		{
			if (!GlobalSettings.Instance || GlobalSettings.Instance.skillsManager.GetSkill(SkillsManager.SkillType.USE_DRILLER_1).isUnlocked)
			{
				TutorialManager.Instance.ShowTutorial(TutorialManager.TutorialsId.DRILL_01, 2f);
			}
		}
		else
		{
			TutorialManager.Instance.ShowTutorial(TutorialManager.TutorialsId.ENTER_FISHERY_01, 2f);
		}
		if (this.gameController.oceanLevel)
		{
			FishingPlayer.playerWasInBoat = true;
			BoatSimulator boatSimulator = global::UnityEngine.Object.FindObjectOfType<BoatSimulator>();
			this.EnterBoatInstant(true, boatSimulator);
		}
		else if (FishingPlayer.playerWasInBoat)
		{
			BoatSimulator boatSimulator2 = global::UnityEngine.Object.FindObjectOfType<BoatSimulator>();
			boatSimulator2.ReturnBoat();
			this.EnterBoatInstant(true, boatSimulator2);
			FishingPlayer.playerWasInBoat = false;
		}
		this.BlockMouseLook(false, 4f);
		this.CurrentMouseLookSensitivity = new Vector2(4f, 4f);
		this.MouseLookSensitivity = this.CurrentMouseLookSensitivity;
		this.RefreshInputSettings();
		this.waterSimulationSpace.enabled = true;
		for (int i = 0; i < this.gameController.fishSpawners.Count; i++)
		{
			if (this.gameController.fishSpawners[i].gameObject.activeSelf && (this.gameController.spawnAllAtStart || this.gameController.fishSpawners[i].spawnAtStart))
			{
				this.gameController.fishSpawners[i].CheckBehaviorDistanceQuick();
				this.gameController.fishSpawners[i].CheckBehaviorDistance();
			}
		}
		if (VRManager.Instance)
		{
			VRManager.Instance.LateInitializePlayer(this);
		}
		if (VRManager.IsVROn() && this.vrWasLeftHanded)
		{
			VRControllersManager.Instance.SetLeftHanded(true);
		}
		this.wasLateInitialized = true;
	}

	// Token: 0x06002B58 RID: 11096 RVA: 0x000F98A8 File Offset: 0x000F7AA8
	private void Update()
	{
		if (!GameController.Instance)
		{
			base.GetComponent<vp_FPInput>().MakeUpdate();
		}
		if (this.gameController == null || !this.gameController.isInitialized || this.currentHands == null)
		{
			return;
		}
		if (!this.vrLateUpdateHands)
		{
			this.UpdateVRHands();
		}
		if (this.gameController.IsPauseMenu())
		{
			return;
		}
		if (this.gameController.IsQuickMenu())
		{
			return;
		}
		if (this.isHandsCameraVisible && (!this.currentHands.fishingRod.rodMegaAttach || !this.currentHands.fishingRod.rodMegaAttach.attached))
		{
			return;
		}
		if (BugReporter.Instance && BugReporter.Instance.isVisible)
		{
			return;
		}
		if (VRManager.IsVROn())
		{
			this.UpdateRoomscale();
		}
		if (HUDManager.Instance.hudMultiplayer.isInInputMode)
		{
			if (this.currentState != FishingPlayer.PlayerState.ICE_FISHING)
			{
				if (this.currentState == FishingPlayer.PlayerState.FISHING)
				{
				}
			}
			if (this.currentHands && this.currentHands.fishingLine)
			{
				if (this.currentHands.baitWasThrown)
				{
					this.currentHands.fishingLine.MakeUpdate();
				}
				else
				{
					this.currentHands.fishingLine.currentTension = 0f;
				}
			}
			return;
		}
		if (this.currentState == FishingPlayer.PlayerState.DEATH)
		{
			return;
		}
		this.ufpsInput.MakeUpdate();
		if (this.allowRun && !this.isDrinking && !this.boatSimulator)
		{
			this.ufpsInput.InputRun();
			if (!VRManager.IsVROn())
			{
				if (UtilitiesInput.GetButton("RUN"))
				{
					this.ufpsWeapon.BobRate.x = 1.2f;
				}
				else
				{
					this.ufpsWeapon.BobRate.x = 0.8f;
				}
			}
			if (UtilitiesInput.GetButtonDown("RUN"))
			{
				this.Zoom(false, 0.2f, 0.75f);
			}
		}
		else
		{
			this.ufpsInput.FPPlayer.Run.TryStop(true);
			this.ufpsWeapon.BobRate.x = 0.8f;
		}
		if (this.isPissing || this.isDrinking || this.isEating || this.isPooing)
		{
			return;
		}
		this.UpdateParams();
		if (this.currentHands.baitWasThrown)
		{
			this.underwaterCamera.UpdateProperHeight();
		}
		if (GlobalSettings.Instance == null || (GlobalSettings.Instance && GlobalSettings.Instance.turnOnCheats))
		{
			if (Input.GetKeyDown(KeyCode.PageUp))
			{
				Fish fish = global::UnityEngine.Object.Instantiate<Fish>(this.tempFishPrefab);
				this.fish = fish;
				this.FishCatch(null);
			}
			if (Input.GetKeyDown(KeyCode.M))
			{
				this.gameController.hudManager.ShowRadar(!this.gameController.hudManager.minimapGui.activeSelf);
			}
			if (Input.GetKey(KeyCode.LeftControl))
			{
				if (Input.GetKeyDown(KeyCode.R))
				{
					this.currentHands.currentRope.regenerateRope(true);
					if (this.currentHands.fishingFloat)
					{
						this.currentHands.floatRope.regenerateRope(true);
					}
				}
				if (Input.GetKeyDown(KeyCode.C) && this.fish)
				{
					this.fish.DurabilityCurrent = Fish.DurabilityMin;
				}
				if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.V))
				{
					if (this.fish)
					{
						this.FishCatch(null);
					}
					else if (this.junk)
					{
						base.StartCoroutine(this.ShowCatchObject());
					}
				}
			}
		}
		if (this.gameController.iceLevel)
		{
			this.CheckIsOnIce();
		}
		if (VRManager.IsVROn() && this.currentState != FishingPlayer.PlayerState.WATCH_FISH && this.currentState != FishingPlayer.PlayerState.ICE_FISHING && (this.currentState != FishingPlayer.PlayerState.DRILLING || !this.drillingController.placeChosen) && this.currentState != FishingPlayer.PlayerState.DRIVING_BOAT)
		{
			float num = ((!this.currentHands.baitWasThrown) ? 0f : Mathf.Abs(VRInputManager.GetAxis("LOOK_VERTICAL")));
			if (num < 0.25f)
			{
				if (VRManager.Instance.playerRotateStyle == VRManager.PlayerRotateStyle.STEP)
				{
					if (UtilitiesInput.GetButtonDown("VR_TURN_LEFT") || OVRInput.GetDown(OVRInput.Button.SecondaryThumbstickLeft, OVRInput.Controller.Gamepad) || UtilitiesInput.isRThumbstickLeft)
					{
						this.SetRotation(new Vector3(this.transform.eulerAngles.x, this.transform.eulerAngles.y - VRManager.Instance.turnAngle, this.transform.eulerAngles.z));
					}
					else if (UtilitiesInput.GetButtonDown("VR_TURN_RIGHT") || OVRInput.GetDown(OVRInput.Button.SecondaryThumbstickRight, OVRInput.Controller.Gamepad) || UtilitiesInput.isRThumbstickRight)
					{
						this.SetRotation(new Vector3(this.transform.eulerAngles.x, this.transform.eulerAngles.y + VRManager.Instance.turnAngle, this.transform.eulerAngles.z));
					}
				}
				else if (VRManager.Instance.playerRotateStyle == VRManager.PlayerRotateStyle.FADE_STEP)
				{
					if (UtilitiesInput.GetButtonDown("VR_TURN_LEFT") || OVRInput.GetDown(OVRInput.Button.SecondaryThumbstickLeft, OVRInput.Controller.Gamepad) || UtilitiesInput.isRThumbstickLeft)
					{
						base.StartCoroutine(this.SetRotationFade(new Vector3(this.transform.eulerAngles.x, this.transform.eulerAngles.y - VRManager.Instance.turnAngle, this.transform.eulerAngles.z), VRManager.Instance.fadeDuration));
					}
					else if (UtilitiesInput.GetButtonDown("VR_TURN_RIGHT") || OVRInput.GetDown(OVRInput.Button.SecondaryThumbstickRight, OVRInput.Controller.Gamepad) || UtilitiesInput.isRThumbstickRight)
					{
						base.StartCoroutine(this.SetRotationFade(new Vector3(this.transform.eulerAngles.x, this.transform.eulerAngles.y + VRManager.Instance.turnAngle, this.transform.eulerAngles.z), VRManager.Instance.fadeDuration));
					}
				}
				else if (VRManager.Instance.playerRotateStyle == VRManager.PlayerRotateStyle.FREE && Mathf.Abs(UtilitiesInput.lookAxis.x) > 0.3f)
				{
					this.SetRotation(new Vector3(this.transform.eulerAngles.x, this.transform.eulerAngles.y + VRManager.Instance.freeTurnSpeed * Time.deltaTime * UtilitiesInput.lookAxis.x, this.transform.eulerAngles.z));
				}
			}
			if (VRManager.Instance.IsPlayerFollowCamera())
			{
			}
		}
		if (UtilitiesInput.GetButtonDown("FLASHLIGHT"))
		{
			this.ToggleFlashlight();
		}
		if (UtilitiesInput.GetButtonDown("HUNTER_VISION"))
		{
			this.TurnOnHunterVision();
		}
		if ((this.currentState == FishingPlayer.PlayerState.FISHING || this.currentState == FishingPlayer.PlayerState.ICE_FISHING) && !Input.GetKey(KeyCode.LeftControl) && UtilitiesInput.GetButtonDown("UNDERWATER_CAMERA"))
		{
			if (GlobalSettings.Instance && !GlobalSettings.Instance.playerSettings.IsCasual())
			{
				this.gameController.hudManager.ShowMessage(global::Utilities.GetTranslation("HUD_MESSAGE/NO_UNDERWATER_REALISTIC", false), 3.5f);
			}
			else if (!this.underwaterCamera.isTurnedOn && this.currentHands.ThrowObjectOnWater() && this.underwaterCamera.HasProperHeight() && !this.grayscale.enabled && this.isHandsCameraVisible && !this.currentHands.fishingRod.isOnRodStand && (!this.currentHands.isGroundRig || this.fish || !GlobalSettings.Instance))
			{
				base.StartCoroutine(this.underwaterCamera.TurnOn(true, false));
			}
			else if (this.underwaterCamera.isTurnedOn)
			{
				base.StartCoroutine(this.underwaterCamera.TurnOn(false, false));
			}
		}
		if (UtilitiesInput.GetButtonDown("EQUIPMENT_SET_1"))
		{
			base.StartCoroutine(this.ChangeEquipmentSet(0));
		}
		else if (UtilitiesInput.GetButtonDown("EQUIPMENT_SET_2"))
		{
			base.StartCoroutine(this.ChangeEquipmentSet(1));
		}
		else if (UtilitiesInput.GetButtonDown("EQUIPMENT_SET_3"))
		{
			base.StartCoroutine(this.ChangeEquipmentSet(2));
		}
		else if (UtilitiesInput.GetButtonDown("EQUIPMENT_SET_4"))
		{
			base.StartCoroutine(this.ChangeEquipmentSet(3));
		}
		else if (UtilitiesInput.GetButtonDown("EQUIPMENT_SET_5"))
		{
			base.StartCoroutine(this.ChangeEquipmentSet(4));
		}
		this.CheckFloatDepthInput();
		if (FishingHands.rodStand && this.currentState == FishingPlayer.PlayerState.FISHING && !this.underwaterCamera.isTurnedOn)
		{
			float num2 = Vector3.Distance(this.transform.position, FishingHands.rodStand.transform.position);
			if (!FishingHands.rodStand.gameObject.activeSelf && UtilitiesInput.GetButtonDown("ROD_STAND_PUT"))
			{
				FishingHands.rodStand.PutStand();
				this.gameController.hudManager.UpdateControls();
			}
			else if (FishingHands.rodStand.gameObject.activeSelf && !FishingHands.rodStand.isBoatStand && UtilitiesInput.GetButtonDown("ROD_STAND_TAKE"))
			{
				if (num2 > 3f)
				{
					this.gameController.hudManager.ShowMessage(global::Utilities.GetTranslation("HUD_MESSAGE/ROD_POD_FAR", false), 3.5f);
				}
				else if (FishingHands.rodStand.rodPlacesOccupied > 0)
				{
					this.gameController.hudManager.ShowMessage(global::Utilities.GetTranslation("HUD_MESSAGE/ROD_POD_NOT_EMPTY", false), 3.5f);
				}
				else
				{
					FishingHands.rodStand.TakeStand();
					this.gameController.hudManager.UpdateControls();
				}
			}
			else if (FishingHands.rodStand.gameObject.activeSelf && !this.currentHands.fishingRod.isOnRodStand && !this.IsFishOnCurrentRod() && UtilitiesInput.GetButtonDown("ROD_STAND_PUT_ROD"))
			{
				if (FishingHands.rodStand.gameObject.activeSelf && this.currentHands.bait.isOnWater && !this.currentHands.fishingRod.isOnRodStand && num2 < 3f)
				{
					FishingHands.rodStand.PutRod(this.currentHands.fishingRod);
					this.gameController.hudManager.UpdateControls();
				}
				else if (!this.currentHands.bait.isOnWater)
				{
					this.gameController.hudManager.ShowMessage(global::Utilities.GetTranslation("HUD_MESSAGE/ROD_POD_CAST", false), 3.5f);
				}
				else if (num2 >= 3f)
				{
					this.gameController.hudManager.ShowMessage(global::Utilities.GetTranslation("HUD_MESSAGE/ROD_POD_FAR", false), 3.5f);
				}
			}
		}
		if (UtilitiesInput.GetButtonDown("BOILIE_2") && this.currentState == FishingPlayer.PlayerState.ICE_FISHING && !this.isThrowingExplosive && !this.fish)
		{
			if (!GlobalSettings.Instance || GlobalSettings.Instance.equipmentManager.HasBoilieEquiped())
			{
				this.currentHands.ThrowBoilieIce();
			}
			return;
		}
		if ((this.currentState == FishingPlayer.PlayerState.NORMAL || this.currentState == FishingPlayer.PlayerState.FISHING) && !this.currentHands.baitWasThrown && !this.gameController.hudManager.hudFishing.throwStrengthBar.gameObject.activeInHierarchy && !this.isThrowingExplosive && !this.currentHands.isThrowingNear && !this.currentHands.isThrowing)
		{
			if (UtilitiesInput.GetButtonDown("BOILIE") && this.currentState == FishingPlayer.PlayerState.FISHING)
			{
				if (!this.currentHands.currentBoilie && this.isHandsCameraVisible && (!GlobalSettings.Instance || GlobalSettings.Instance.equipmentManager.HasBoilieEquiped()))
				{
					this.currentHands.ThrowBoilie();
				}
				return;
			}
			if (UtilitiesInput.GetButtonUp("BOILIE") && this.currentState == FishingPlayer.PlayerState.FISHING && this.currentHands.currentBoilie && VRManager.Instance.IsVRGroundBait())
			{
				this.currentHands.ThrowBoilieEvent();
				return;
			}
			if ((GlobalSettings.Instance == null || (GlobalSettings.Instance && GlobalSettings.Instance.turnOnCheats)) && Input.GetKeyDown(KeyCode.F10))
			{
				this.gameController.QuickJump();
			}
		}
		if (UtilitiesInput.GetButtonDown("RESET_PLAYER") && !this.gameController.oceanLevel && !this.boatSimulator && (this.currentState == FishingPlayer.PlayerState.NORMAL || this.currentState == FishingPlayer.PlayerState.FISHING))
		{
			base.StartCoroutine(this.gameController.ResetPlayer(true));
		}
		if (this.isPissing)
		{
			if (UtilitiesInput.isReelingIn)
			{
				float num3 = this.pissingParticlesOld.transform.localEulerAngles.x + Time.deltaTime * 80f;
				if (num3 > 50f && num3 < 180f)
				{
					num3 = 50f;
				}
				this.pissingParticlesOld.transform.localEulerAngles = new Vector3(num3, this.pissingParticlesOld.transform.localEulerAngles.y, this.pissingParticlesOld.transform.localEulerAngles.z);
			}
			else if (UtilitiesInput.isReelingOut)
			{
				float num4 = this.pissingParticlesOld.transform.localEulerAngles.x - Time.deltaTime * 80f;
				if (num4 < 330f && num4 > 180f)
				{
					num4 = 330f;
				}
				this.pissingParticlesOld.transform.localEulerAngles = new Vector3(num4, this.pissingParticlesOld.transform.localEulerAngles.y, this.pissingParticlesOld.transform.localEulerAngles.z);
			}
		}
		if (this.boatSimulator != null && !this.gameController.oceanLevel && !this.currentHands.baitWasThrown && !this.IsSomethingOnBait() && UtilitiesInput.GetButtonDown("BOAT_EXIT"))
		{
			base.StartCoroutine(this.EnterBoat(false, this.boatSimulator));
		}
		if ((this.currentState == FishingPlayer.PlayerState.DRIVING_BOAT && VRManager.IsVROn()) || (this.currentState == FishingPlayer.PlayerState.ICE_FISHING && VRManager.IsVROn()))
		{
			float num5 = 0.3f;
			Vector3 vector = new Vector3(1f, 0.5f, 1f);
			Vector3 vector2 = new Vector3((Mathf.Abs(UtilitiesInput.moveAxis.x) <= 0.2f) ? 0f : (UtilitiesInput.moveAxis.x * Time.deltaTime * num5), (Mathf.Abs(UtilitiesInput.lookAxis.x) >= 0.5f) ? 0f : (UtilitiesInput.lookAxis.y * Time.deltaTime * num5), (Mathf.Abs(UtilitiesInput.moveAxis.y) <= 0.2f) ? 0f : (UtilitiesInput.moveAxis.y * Time.deltaTime * num5));
			if (this.currentState == FishingPlayer.PlayerState.ICE_FISHING)
			{
				vector2.y = 0f;
			}
			else if (!VRManager.Instance.IsVRDriveBoat() || this.boatSimulator.isKayak || !this.boatSimulator.vrBoatWheel)
			{
				vector2.x = (vector2.z = 0f);
			}
			this.ufpsCamera.transform.parent.localPosition += vector2;
			this.ufpsCamera.transform.parent.localPosition = new Vector3(Mathf.Clamp(this.ufpsCamera.transform.parent.localPosition.x, -vector.x, vector.x), Mathf.Clamp(this.ufpsCamera.transform.parent.localPosition.y, -vector.y, vector.y), Mathf.Clamp(this.ufpsCamera.transform.parent.localPosition.z, -vector.z, vector.z));
			Vector3 localPosition = this.ufpsCamera.transform.parent.localPosition;
			this.ovrPlayerController.transform.localPosition = localPosition;
			this.vrWheelMoveLastPosition = localPosition;
		}
		if (this.currentState == FishingPlayer.PlayerState.NORMAL)
		{
			if (this.isOnIce && !this.drillingController.Drilling && this.isHandsCameraVisible)
			{
				if (UtilitiesInput.GetButtonDown("TRY_DRILL") && !Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift))
				{
					if (!this.isDuringAnimation && this.currentHands.hasRequiredEquipments)
					{
						if (!GlobalSettings.Instance)
						{
							this.ChangeState(FishingPlayer.PlayerState.DRILLING);
						}
						else if (!GlobalSettings.Instance.skillsManager.GetSkill(SkillsManager.SkillType.USE_DRILLER_1).isUnlocked)
						{
							GameController.Instance.hudManager.ShowMessage(global::Utilities.GetTranslation("HUD_MESSAGE/NO_SKILL", false), 3.5f);
						}
						else if (!GlobalSettings.Instance.equipmentManager.FindEquipment(EquipmentObject.EquipmentType.OTHER, "DRILLER_01").isBought)
						{
							GameController.Instance.hudManager.ShowMessage(global::Utilities.GetTranslation("HUD_MESSAGE/NO_AUGER", false), 3.5f);
						}
						else
						{
							this.ChangeState(FishingPlayer.PlayerState.DRILLING);
						}
					}
					return;
				}
				Debug.DrawRay(this.transform.position + this.ufpsCamera.PositionOffset + new Vector3(0f, -0.5f, 0f), this.ufpsCamera.transform.forward * 1.7f, Color.yellow);
				RaycastHit raycastHit;
				if (!this.isDuringAnimation && this.currentHands.hasRequiredEquipments && Physics.Raycast(this.transform.position + this.ufpsCamera.PositionOffset + new Vector3(0f, -0.5f, 0f), this.ufpsCamera.transform.forward, out raycastHit, 1.7f) && raycastHit.transform.tag == "Hole" && Vector3.Distance(this.transform.position, raycastHit.collider.GetComponent<Hole>().holeCenter.position) > 1f)
				{
					if (!VRManager.IsVROn())
					{
						this.gameController.hudManager.cursorChooseHole.SetActive(true);
					}
					if (UtilitiesInput.GetButtonDown("USE_HOLE"))
					{
						this.gameController.hudManager.cursorChooseHole.SetActive(false);
						this.drillingController.currentHole = raycastHit.collider.GetComponent<Hole>();
						this.ChangeState(FishingPlayer.PlayerState.ICE_FISHING);
					}
				}
				else
				{
					this.gameController.hudManager.cursorChooseHole.SetActive(false);
				}
			}
			if (this.boatSimulator != null)
			{
				if (UtilitiesInput.GetButtonDown("BOAT_DRIVE"))
				{
					Debug.LogError("DriveBoat FishingPlayer");
					base.StartCoroutine(this.DriveBoat(true));
				}
			}
		}
		else if (this.currentState == FishingPlayer.PlayerState.DRIVING_BOAT)
		{
			if ((VRManager.Instance.IsControllersInput() && UtilitiesInput.GetButtonDown("BOAT_DRIVE_STOP")) || (!VRManager.Instance.IsControllersInput() && UtilitiesInput.GetButtonDown("BOAT_DRIVE")))
			{
				base.StartCoroutine(this.DriveBoat(false));
				if (this.boatSimulator.rodStand)
				{
					TutorialManager.Instance.ShowTutorial(TutorialManager.TutorialsId.TROLLING_01, 1.5f);
				}
			}
			if (!Cursor.visible)
			{
				Vector3 vector3 = this.ufpsCamera.transform.localEulerAngles;
				if (UtilitiesInput.lookAxis.x != 0f)
				{
					vector3 += new Vector3(0f, UtilitiesInput.lookAxis.x * Time.deltaTime * 200f, 0f) * ((!GlobalSettings.Instance) ? 1f : GlobalSettings.Instance.playerSettings.mouseSensitivity);
				}
				if (UtilitiesInput.lookAxis.y != 0f)
				{
					vector3 -= new Vector3(UtilitiesInput.lookAxis.y * Time.deltaTime * 200f, 0f, 0f) * ((!GlobalSettings.Instance) ? 1f : GlobalSettings.Instance.playerSettings.invertYAxis) * ((!GlobalSettings.Instance) ? 1f : GlobalSettings.Instance.playerSettings.mouseSensitivity);
				}
				float num6 = ((!this.boatSimulator.isKayak) ? 120f : 150f);
				if (vector3.y > num6 && vector3.y < 180f)
				{
					vector3.y = num6;
				}
				else if (vector3.y < 360f - num6 && vector3.y > 180f)
				{
					vector3.y = 360f - num6;
				}
				if (vector3.x > this.boatSimulator.playerDownRotateMax && vector3.x < 180f)
				{
					vector3.x = this.boatSimulator.playerDownRotateMax;
				}
				else if (vector3.x < 290f && vector3.x > 180f)
				{
					vector3.x = 290f;
				}
				vector3.z = 0f;
				this.ufpsCamera.transform.localEulerAngles = vector3;
			}
		}
		else if (this.currentState == FishingPlayer.PlayerState.FISHING_NET)
		{
			this.currentHands.fishingNet.UpdateNetInput();
		}
		else if (this.currentState == FishingPlayer.PlayerState.WATCH_FISH)
		{
			this.SetShowObjectPosition();
			if (UtilitiesInput.lookAxis.x != 0f)
			{
				if (this.currentWatchStyle != Fish.WatchStyle.HANDS)
				{
					if (this.currentWatchStyle != Fish.WatchStyle.BOAT)
					{
						if (this.fish)
						{
							this.fish.transform.Rotate(0f, 0f, UtilitiesInput.lookAxis.x * Time.deltaTime * 350f);
						}
						else if (this.junk)
						{
							this.junk.transform.Rotate(UtilitiesInput.lookAxis.x * Time.deltaTime * 350f * this.junk.rotationAxis);
						}
					}
				}
			}
		}
		else if (this.currentState == FishingPlayer.PlayerState.DRILLING)
		{
			if (UtilitiesInput.GetButtonDown("TRY_DRILL") && !this.drillingController.placeChosen && this.isDrillerReady)
			{
				this.drillingController.ShowDriller(false);
				this.ChangeState(FishingPlayer.PlayerState.NORMAL);
				this.CameraLookAt(this.transform.position + this.transform.forward * 3f + new Vector3(0f, 1.4f, 0f), Vector3.zero, 0.5f);
				this.gameController.hudManager.UpdateControls();
				return;
			}
			bool flag = this.drillingController.CanIDrill();
			if (!this.drillingController.placeChosen)
			{
				this.drillingController.Auger.FadeDriller(!flag);
			}
			if (!this.drillingController.placeChosen && UtilitiesInput.GetButtonDown("START_DRILL") && flag && this.isDrillerReady)
			{
				this.BlockMouseLook(true, 4f);
				Cursor.visible = true;
				Cursor.lockState = CursorLockMode.None;
				this.gameController.hudManager.drillingIconStart.SetActive(false);
				this.gameController.hudManager.drillingIconDrill.SetActive(true);
				this.drillingController.Auger.Position(this.drillingController.DrillPoint.position);
				this.drillingController.StartDrilling();
				this.CameraLookAt(this.drillingController.DrillPoint.transform, new Vector3(0f, 0.45f, 0f), 0.2f);
				this.drillingController.placeChosen = true;
				this.gameController.hudManager.UpdateControls();
			}
		}
		else if (this.currentState == FishingPlayer.PlayerState.ICE_FISHING)
		{
			this.fishingController.UpdateIceFishing();
		}
		else if (this.currentState == FishingPlayer.PlayerState.FISHING)
		{
			this.fishingController.UpdateFishing();
		}
		if (this.currentHands && this.currentHands.fishingLine)
		{
			if (this.currentHands.baitWasThrown)
			{
				this.currentHands.fishingLine.MakeUpdate();
			}
			else
			{
				this.currentHands.currentRope.rate = 0f;
				this.currentHands.fishingLine.currentTension = 0f;
			}
		}
	}

	// Token: 0x06002B59 RID: 11097 RVA: 0x000FB404 File Offset: 0x000F9604
	private void CheckFloatDepthInput()
	{
		if (!GlobalSettings.Instance)
		{
			return;
		}
		if (!this.CanChangeEquipment() && (!this.currentHands.fishingRod || !this.currentHands.fishingRod.isOnRodStand))
		{
			return;
		}
		if (!this.currentHands.fishingFloat)
		{
			return;
		}
		if (UtilitiesInput.GetButtonUp("FLOAT_LINE_INCREASE") || UtilitiesInput.GetButtonUp("FLOAT_LINE_DECREASE"))
		{
			this.currentHands.ChangeFloat();
			return;
		}
		if (UtilitiesInput.GetButtonDown("FLOAT_LINE_INCREASE") || UtilitiesInput.GetButtonDown("FLOAT_LINE_DECREASE"))
		{
			this.lastFloatDepthInputDown = Time.realtimeSinceStartup;
		}
		if (Time.realtimeSinceStartup - this.lastFloatDepthInput < this.floatDepthInputDelay)
		{
			return;
		}
		EquipmentManager equipmentManager = GlobalSettings.Instance.equipmentManager;
		float currentFloatDepth = equipmentManager.currentSetSpecificParameters.currentFloatDepth;
		float num = currentFloatDepth;
		if (UtilitiesInput.GetButton("FLOAT_LINE_INCREASE"))
		{
			if (currentFloatDepth < equipmentManager.maxFloatDepth)
			{
				if (Time.realtimeSinceStartup - this.lastFloatDepthInputDown >= this.fastFloatDepthDelay)
				{
					num = Mathf.Clamp(num + this.fastFloatDepthIncrese, equipmentManager.minFloatDepth, equipmentManager.maxFloatDepth);
				}
				else
				{
					num += 1f;
				}
			}
		}
		else
		{
			if (!UtilitiesInput.GetButton("FLOAT_LINE_DECREASE"))
			{
				return;
			}
			if (currentFloatDepth > equipmentManager.minFloatDepth)
			{
				if (Time.realtimeSinceStartup - this.lastFloatDepthInputDown >= this.fastFloatDepthDelay)
				{
					num = Mathf.Clamp(num - this.fastFloatDepthIncrese, equipmentManager.minFloatDepth, equipmentManager.maxFloatDepth);
				}
				else
				{
					num -= 1f;
				}
			}
		}
		this.lastFloatDepthInput = Time.realtimeSinceStartup;
		equipmentManager.currentSetSpecificParameters.currentFloatDepth = num;
		string text = num + " cm";
		if (GlobalSettings.Instance.playerSettings.imperialUnits)
		{
			text = text + "\n(" + UtilitiesUnits.GetLengthCmString(num, "F1", true) + ")";
		}
		HUDManager.Instance.ShowMessage(global::Utilities.GetTranslation("GUI/OPTIONS_FLOAT_LENGTH", false) + ": " + text, 3f);
	}

	// Token: 0x06002B5A RID: 11098 RVA: 0x000FB62C File Offset: 0x000F982C
	private void LateUpdate()
	{
		if (this.gameController == null || !this.gameController.isInitialized || this.currentHands == null)
		{
			return;
		}
		if (this.vrLateUpdateHands)
		{
			this.UpdateVRHands();
		}
		if (BugReporter.Instance && BugReporter.Instance.isVisible)
		{
			return;
		}
		if (this.gameController.IsQuickMenu())
		{
			return;
		}
		if (Time.deltaTime == 0f)
		{
			return;
		}
		this.UpdateVRHUD();
		if (!VRManager.IsVROn())
		{
			this.ufpsCameraCamera.fieldOfView = this.CurrentFieldOfView * this.ZoomFieldOfView;
			this.UpdateFOV(false);
			Camera camera = this.baitIndicatorCamera;
			float num = this.ufpsCameraCamera.fieldOfView;
			this.ufpsWeaponCamera.fieldOfView = num;
			num = num;
			this.hunterCamera.fieldOfView = num;
			camera.fieldOfView = num;
			if (this.gameController.iceLevel)
			{
				this.maskCamera.GetComponent<Camera>().fieldOfView = this.ufpsCameraCamera.fieldOfView;
			}
			if (this.postProcessingBehaviour_1)
			{
				if (!GlobalSettings.Instance && this.CurrentFieldOfView == 100f)
				{
					this.CurrentFieldOfView = 60f;
				}
				else if (GlobalSettings.Instance)
				{
					this.CurrentFieldOfView = GlobalSettings.Instance.renderSettings.fov;
				}
			}
			if (this.gameController.iceLevel || this.underwaterCamera.isTurnedOn)
			{
				this.baitIndicatorCamera.fieldOfView = (this.CurrentFieldOfView = 60f);
			}
		}
		if (!this.currentHands.fishingRod.rodMegaAttach || !this.currentHands.fishingRod.rodMegaAttach.attached)
		{
			return;
		}
		if (this.currentState == FishingPlayer.PlayerState.DRILLING)
		{
			if (VRManager.IsVROn())
			{
				this.drillingController.DrillPoint.transform.parent = this.ufpsCamera.transform;
				this.drillingController.DrillPoint.localPosition = new Vector3(0f, 0.1f, 0.65f);
			}
			if (!this.drillingController.Drilling)
			{
				this.drillingController.Auger.Position(this.drillingController.DrillPoint.position);
			}
		}
		else if (this.currentState == FishingPlayer.PlayerState.FISHING_NET)
		{
			this.currentHands.fishingNet.LateUpdateNetInput();
		}
		else if (this.currentState == FishingPlayer.PlayerState.WATCH_FISH)
		{
			if (this.fish)
			{
				if (this.currentWatchStyle == Fish.WatchStyle.HOOK_LIGHT)
				{
					this.currentHands.UpdateWatchFishLine(this.currentHands.bait.ropeRigidbody.transform);
					this.fish.transform.eulerAngles = new Vector3(-90f, this.fish.transform.eulerAngles.y, this.fish.transform.eulerAngles.z);
				}
			}
			else if (this.junk)
			{
				this.currentHands.UpdateWatchFishLine(this.junk.hookPosition);
			}
			if (UtilitiesInput.GetVibration(0) > 0f || UtilitiesInput.GetVibration(1) > 0f)
			{
				UtilitiesInput.StopVibration(true);
				UtilitiesInput.StopVibration(false);
			}
		}
		this.CheckWater();
		if ((!this.gameController.iceLevel && this.currentHands.baitWasThrown) || (this.gameController.iceLevel && this.currentHands.bait.isOnWater))
		{
			if (this.gameController.iceLevel)
			{
				this.distanceToBait = Vector2.Distance(new Vector2(this.drillingController.currentHole.transform.position.x, this.drillingController.currentHole.transform.position.z), new Vector2(this.currentHands.throwObject.transform.position.x, this.currentHands.throwObject.transform.position.z));
			}
			else
			{
				this.distanceToBait = Vector2.Distance(new Vector2(this.transform.position.x, this.transform.position.z), new Vector2(this.currentHands.throwObject.transform.position.x, this.currentHands.throwObject.transform.position.z));
			}
			this.gameController.hudManager.hudFishing.UpdateDistance(this.distanceToBait);
			this.gameController.hudManager.hudFishing.UpdateDepth(Mathf.Clamp(this.currentHands.bait.transform.position.y + 0.1f, -666f, 0f));
			this.gameController.hudManager.hudFishing.UpdateLineLength(this.currentHands.fishingLine.stretchToDistance, this.currentHands.fishingLine.currentTension);
		}
		else
		{
			this.gameController.hudManager.hudFishing.UpdateDistance(0f);
			this.gameController.hudManager.hudFishing.UpdateDepth(0f);
			this.gameController.hudManager.hudFishing.UpdateLineLength(0f, 0f);
		}
		if (this.fish)
		{
			if (!GlobalSettings.Instance || GlobalSettings.Instance.turnOnMyCheats)
			{
				this.gameController.hudManager.hudFishing.fishDurability.gameObject.SetActive(true);
				this.gameController.hudManager.hudFishing.strengthDifferenceSlider.gameObject.SetActive(true);
			}
			this.gameController.hudManager.hudFishing.UpdateFishDurability(this.fish.DurabilityCurrentRatio);
			this.gameController.hudManager.hudFishing.UpdateStrengthDifference(this.PlayerFishStrengthDiff);
		}
		else
		{
			this.gameController.hudManager.hudFishing.fishDurability.gameObject.SetActive(false);
			this.gameController.hudManager.hudFishing.strengthDifferenceSlider.gameObject.SetActive(false);
		}
		if (this.boatSimulator && this.boatSimulator.currentState == BoatSimulator.BoatState.PLAYER_FISHING && this.checkBoatHeight > 0f)
		{
			RaycastHit raycastHit;
			if (Physics.Raycast(this.transform.position + this.transform.up * 0.05f, -this.transform.up, out raycastHit, this.checkBoatHeight))
			{
				if (raycastHit.collider.gameObject.layer == LayerMask.NameToLayer("PlayerCollider"))
				{
					Debug.LogError("Boat foot no hit 1");
					this.SetPosition(this.boatSimulator.fishingPosition.position);
				}
			}
			else
			{
				Debug.LogError("Boat foot no hit 2");
				this.SetPosition(this.boatSimulator.fishingPosition.position);
			}
		}
		else if (this.transform.position.y <= -1f && this.isInWater && !this.canFallInWater && !this.isTeleporting)
		{
			this.SetPosition(this.prevPosition);
		}
		else
		{
			this.prevPosition = this.transform.position;
		}
		if (this.boatSimulator && this.boatSimulator.isKayak && this.boatSimulator.currentState == BoatSimulator.BoatState.PLAYER_FISHING)
		{
			this.SetPosition(this.boatSimulator.fishingPosition.position);
		}
		this.CalculateFishDistanceBehavior();
		this.gameController.hudManager.hudFishing.UpdateFishState((!this.fish || !this.fish.isFighting) ? string.Empty : "Fish caught");
		this.gameController.hudManager.hudFishing.UpdateDrag(this.currentHands.reel.currentDrag * 100f, this.currentHands.reel.isDragActive);
		this.gameController.hudManager.hudFishing.UpdateReelSpeed(this.currentHands.currentUserReelSpeed);
		this.gameController.hudManager.hudFishing.UpdateReelSpeedNew(this.currentHands.currentUserReelSpeed);
		this.gameController.hudManager.hudFishing.UpdateTension(this.currentHands.fishingLine.GetTension());
		this.gameController.hudManager.UpdateLuckBar(this.gameController.junkManager.GetCurrentLuck());
		this.gameController.hudManager.UpdateDrunkBar(this.drunkLevel);
		this.gameController.hudManager.UpdatePissBar(this.pissingLevel);
		this.gameController.hudManager.UpdateFoodBar(this.foodLevel);
		this.gameController.hudManager.UpdatePooBar(this.pooLevel);
		this.gameController.hudManager.UpdateStrengthBar(this.GetCurrentStrength());
	}

	// Token: 0x06002B5B RID: 11099 RVA: 0x000FBFFC File Offset: 0x000FA1FC
	private void FixedUpdate()
	{
		if (this.currentState != FishingPlayer.PlayerState.DRIVING_BOAT)
		{
			if (this.currentState == FishingPlayer.PlayerState.FISHING_NET)
			{
				this.currentHands.fishingNet.FixedUpdateNetInput();
			}
		}
	}

	// Token: 0x06002B5C RID: 11100 RVA: 0x000FC02B File Offset: 0x000FA22B
	private void OnTriggerEnter(Collider other)
	{
		if (LayerMask.LayerToName(other.gameObject.layer) == "Water")
		{
			this.isInWater = true;
			this.gameController.hudManager.UpdateControls();
		}
	}

	// Token: 0x06002B5D RID: 11101 RVA: 0x000FC063 File Offset: 0x000FA263
	private void OnTriggerExit(Collider other)
	{
		if (LayerMask.LayerToName(other.gameObject.layer) == "Water")
		{
			this.isInWater = false;
			this.gameController.hudManager.UpdateControls();
		}
	}

	// Token: 0x06002B5E RID: 11102 RVA: 0x000FC09C File Offset: 0x000FA29C
	private void OnCollisionEnter(Collision col)
	{
		if (col.gameObject)
		{
			Debug.Log(string.Concat(new object[]
			{
				"Player collide with: ",
				col.gameObject.name,
				" at pos: ",
				this.transform.position.y
			}));
		}
	}

	// Token: 0x06002B5F RID: 11103 RVA: 0x000FC104 File Offset: 0x000FA304
	public void ResetPlayer(GameObject spawnPoint)
	{
		this.currentHands.StopFishing(false);
		this.ResetFishing(true);
		this.ResetJunk();
		if (!this.boatSimulator)
		{
			this.SetPosition(spawnPoint.transform.position);
			this.SetRotation(spawnPoint.transform.eulerAngles);
		}
		if (this.fxBlood1)
		{
			this.fxBlood1.enabled = false;
		}
		if (this.gaussianBlur)
		{
			this.gaussianBlur.enabled = false;
		}
		this.pissingLevel = 0f;
		this.pooLevel = 0f;
		this.foodLevel = 0f;
		this.drunkLevel = 0f;
		this.strengthLevel = 0f;
		this.gameController.fisheryExitCounter = 0;
		if (this.gameController.iceLevel)
		{
			this.currentHands.ShowRightArm();
			this.currentHands.ShowLeftArm();
			this.currentHands.StopIceFishing();
			this.drillingController.currentHole = null;
		}
		this.SitDown(-1f, true);
		if (this.boatSimulator)
		{
			base.StartCoroutine(this.DriveBoat(false));
		}
		else
		{
			this.ChangeState(FishingPlayer.PlayerState.NORMAL);
		}
		if (this.gameController.iceLevel)
		{
			this.fishingController.FinishIceFishing();
		}
	}

	// Token: 0x06002B60 RID: 11104 RVA: 0x000FC268 File Offset: 0x000FA468
	public void CheckWater()
	{
		if (this.transform.position.y <= this.resetPlayerInWater && !this.gameController.iceLevel)
		{
			AudioController.Play("Footsteps_Water_01", this.transform);
			base.StartCoroutine(this.gameController.ResetPlayer(false));
			return;
		}
		if (this.waterMaxClipRange > 1000f)
		{
			WaterProjectSettings.Instance.CameraClipRange = Mathf.Lerp(1000f, this.waterMaxClipRange, (this.ufpsCamera.transform.position.y - 20f) / 50f);
		}
		if (this.gameController.endlessWaterArea)
		{
			bool flag = !this.boatSimulator && this.transform.position.y < 0f;
			if (flag != this.isInWater)
			{
				this.isInWater = flag;
				this.gameController.hudManager.UpdateControls();
			}
		}
		if (!this.gameController.allowFishing || this.gameController.iceLevel)
		{
			return;
		}
		bool flag2 = true;
		if ((this.ufpsCamera.transform.localEulerAngles.x > 180f && this.ufpsCamera.transform.localEulerAngles.x < 320f) || (this.ufpsCamera.transform.localEulerAngles.x < 180f && this.ufpsCamera.transform.localEulerAngles.x > 10f))
		{
			flag2 = false;
		}
		else
		{
			Debug.DrawRay(this.transform.position + this.ufpsCamera.PositionOffset, this.transform.forward * this.checkWaterDistance, Color.yellow);
			RaycastHit raycastHit;
			if (Physics.Raycast(this.transform.position + this.ufpsCamera.PositionOffset, this.transform.forward, out raycastHit, this.checkWaterDistance, this.checkWaterMask))
			{
				flag2 = false;
			}
			else
			{
				Debug.DrawRay(this.transform.position + this.ufpsCamera.PositionOffset + this.transform.forward * this.checkWaterDistance, -Vector3.up * 500f, Color.blue);
				RaycastHit raycastHit2;
				if (Physics.Raycast(this.transform.position + this.ufpsCamera.PositionOffset + this.transform.forward * this.checkWaterDistance, -Vector3.up, out raycastHit2, 500f, this.checkWaterMask))
				{
					if (this.gameController.endlessWaterArea)
					{
						flag2 = raycastHit2.point.y < 0f;
					}
					else
					{
						flag2 = LayerMask.LayerToName(raycastHit2.collider.gameObject.layer) == "Water";
					}
				}
			}
		}
		if (flag2 && !TutorialManager.Instance.throwNearTutorial.wasShown)
		{
			TutorialManager.Instance.ShowTutorial(TutorialManager.TutorialsId.THROW_NEAR_01, 0f);
		}
		else if (flag2 && !TutorialManager.Instance.throwFlyTutorial.wasShown && this.currentHands.isFlyRig && !VRManager.IsVROn())
		{
			TutorialManager.Instance.ShowTutorial(TutorialManager.TutorialsId.FLY_FISHING_01, 0f);
		}
		bool flag3 = (this.ufpsCamera.transform.localEulerAngles.x <= 180f || this.ufpsCamera.transform.localEulerAngles.x >= 320f) && (this.ufpsCamera.transform.localEulerAngles.x >= 180f || this.ufpsCamera.transform.localEulerAngles.x <= 10f);
		flag3 = (flag2 = true);
		if (this.fishingController.canThrowNear != flag3)
		{
			this.fishingController.canThrowNear = flag3;
			this.gameController.hudManager.UpdateControls();
		}
		if (flag2)
		{
			if (this.currentState == FishingPlayer.PlayerState.NORMAL)
			{
				this.ChangeState(FishingPlayer.PlayerState.FISHING);
			}
		}
		else if (this.currentState == FishingPlayer.PlayerState.FISHING && this.ufpsInput.AllowGameplayInput && !this.currentHands.baitWasThrown)
		{
			this.ChangeState(FishingPlayer.PlayerState.NORMAL);
		}
		this.CheckVRCameraHeight();
	}

	// Token: 0x06002B61 RID: 11105 RVA: 0x000FC74C File Offset: 0x000FA94C
	public void CheckIsOnIce()
	{
		RaycastHit raycastHit;
		if (Physics.Raycast(this.transform.position + this.transform.up * 0.05f, -this.transform.up, out raycastHit, 10f))
		{
			this.isOnIce = raycastHit.collider.gameObject.HasTag("ICE_DRILL");
		}
		else
		{
			this.isOnIce = false;
		}
	}

	// Token: 0x06002B62 RID: 11106 RVA: 0x000FC7C7 File Offset: 0x000FA9C7
	public void ShowCrosshair(bool show)
	{
		this.ufpsPlayerEventHandler.Crosshair.Set((!show) ? this.defaultCrosshair : null);
	}

	// Token: 0x06002B63 RID: 11107 RVA: 0x000FC7F0 File Offset: 0x000FA9F0
	public void Pause(bool pause)
	{
		if (this.ufpsInput == null || this.ufpsWeapon == null)
		{
			return;
		}
		if (pause)
		{
			this.AllowGameplayInput = this.ufpsInput.AllowGameplayInput;
			this.MouseLookSensitivity = this.ufpsInput.MouseLookSensitivity;
			this.RotationSpringStiffness = this.ufpsWeapon.RotationSpringStiffness;
			this.RotationSpringDamping = this.ufpsWeapon.RotationSpringDamping;
			this.ufpsInput.AllowGameplayInput = false;
			this.ufpsInput.MouseLookSensitivity = new Vector2(0f, 0f);
			this.ufpsWeapon.RotationSpringStiffness = 1f;
			this.ufpsWeapon.RotationSpringDamping = 1f;
		}
		else if (this.MouseLookSensitivity.x >= 0f)
		{
			this.ufpsInput.AllowGameplayInput = this.AllowGameplayInput;
			this.ufpsInput.MouseLookSensitivity = this.MouseLookSensitivity;
			this.ufpsWeapon.RotationSpringStiffness = this.RotationSpringStiffness;
			this.ufpsWeapon.RotationSpringDamping = this.RotationSpringDamping;
			this.RefreshInputSettings();
		}
		this.ufpsWeapon.Refresh();
	}

	// Token: 0x06002B64 RID: 11108 RVA: 0x000FC920 File Offset: 0x000FAB20
	public void ChangeState(FishingPlayer.PlayerState newState)
	{
		this.gameController.hudManager.cursorChooseHole.SetActive(false);
		if (newState != FishingPlayer.PlayerState.DRIVING_BOAT)
		{
			UtilitiesInput.StopVibration();
		}
		if (newState == FishingPlayer.PlayerState.NORMAL)
		{
			this.gameController.hudManager.ChangeGameState(HUDManager.GameState.NORMAL);
			if (this.currentHands)
			{
				if ((this.currentHands.baitWasThrown && !this.currentHands.fishingRod.isOnRodStand) || this.currentState == FishingPlayer.PlayerState.WATCH_FISH || this.currentState == FishingPlayer.PlayerState.ICE_FISHING)
				{
					this.fishingController.FinishFishing();
				}
				if (this.currentState == FishingPlayer.PlayerState.FISHING_NET)
				{
					this.currentHands.fishingNet.HideNet(false);
				}
			}
			if (this.currentState == FishingPlayer.PlayerState.ICE_FISHING)
			{
				this.drillingController.currentHole.GetComponent<CapsuleCollider>().enabled = true;
				if (this.drillingController.currentHole.aroundHoleCollider)
				{
					this.drillingController.currentHole.aroundHoleCollider.enabled = true;
				}
				this.drillingController.currentHole = null;
				this.drillingController.placeChosen = false;
				this.characteController.radius = 0.45f;
				this.SitDown(-1f, true);
			}
			else if (this.currentState == FishingPlayer.PlayerState.DRILLING)
			{
				this.drillingController.placeChosen = false;
				this.currentHands.ShowWalkingDriller(true);
			}
			else if (this.currentState == FishingPlayer.PlayerState.FISHING_NET)
			{
				this.SitDown(-1f, true);
			}
			this.allowRun = true;
			this.ufpsInput.AllowGameplayInput = true;
			this.BlockMouseLook(false, 4f);
			this.AllowInteraction(true);
			Cursor.visible = false;
			Cursor.lockState = CursorLockMode.Locked;
			this.SetHorizontalRotationLimit(-360f, 360f);
		}
		else if (newState == FishingPlayer.PlayerState.FISHING)
		{
			this.gameController.hudManager.ChangeGameState(HUDManager.GameState.FISHING);
			if (this.currentState == FishingPlayer.PlayerState.FISHING_NET)
			{
				this.currentHands.fishingNet.HideNet(false);
			}
		}
		else if (newState == FishingPlayer.PlayerState.FISHING_NET)
		{
			this.gameController.hudManager.ChangeGameState(HUDManager.GameState.FISHING_NET);
			this.currentHands.reel.rotationAudioObject.Pause();
			this.currentHands.reel.spoolAudioObject.Pause();
			this.currentHands.fishingNet.ShowNet();
			this.ufpsController.Stop();
			this.ufpsInput.AllowGameplayInput = false;
			this.BlockMouseLook(true, 4f);
			this.AllowInteraction(false);
			this.allowRun = false;
		}
		else if (newState == FishingPlayer.PlayerState.DRIVING_BOAT)
		{
			this.gameController.hudManager.ChangeGameState(HUDManager.GameState.DRIVING_BOAT);
			this.ufpsInput.AllowGameplayInput = false;
			this.BlockMouseLook(true, 4f);
			this.AllowInteraction(false);
			this.allowRun = false;
		}
		else if (newState == FishingPlayer.PlayerState.WATCH_FISH)
		{
			this.currentHands.StopFishing(false);
			if (this.currentState == FishingPlayer.PlayerState.FISHING_NET)
			{
				this.currentHands.fishingNet.HideNet(false);
			}
			this.Zoom(false, 0.7f, 0.75f);
			if (this.fish)
			{
				this.gameController.hudManager.PlayerCaughtFish(this.fish);
			}
			else if (this.junk)
			{
				this.gameController.hudManager.PlayerCaughtJunk(this.junk);
			}
			if (this.gameController.iceLevel)
			{
			}
			if (this.currentState == FishingPlayer.PlayerState.FISHING_NET && (!this.boatSimulator || !this.boatSimulator.isKayak))
			{
				this.SitDown(-1f, true);
			}
			this.ufpsController.Stop();
			this.ufpsInput.AllowGameplayInput = false;
			this.BlockMouseLook(true, 4f);
			this.AllowInteraction(false);
			this.TurnOnSpring(false, -1f);
			this.allowRun = false;
			Cursor.visible = true;
			Cursor.lockState = CursorLockMode.None;
		}
		else if (newState == FishingPlayer.PlayerState.DRILLING)
		{
			this.ufpsController.Stop();
			this.gameController.hudManager.ChangeGameState(HUDManager.GameState.DRILLING);
			this.ufpsInput.AllowGameplayInput = false;
			this.ufpsInput.MouseLookSensitivity = new Vector2(3f, 0f);
			this.AllowInteraction(false);
			this.allowRun = false;
			Cursor.visible = false;
			Cursor.lockState = CursorLockMode.Locked;
			this.gameController.hudManager.drillingIconStart.SetActive(true);
			this.gameController.hudManager.drillingIconDrill.SetActive(false);
			this.currentHands.ShowWalkingDriller(false);
			this.isDrillerReady = false;
			this.drillingController.ShowDriller(true);
			this.CameraLookAt(this.drillingController.DrillPoint.transform, new Vector3(0f, 0.45f, 0f), 0.5f);
			LeanTween.delayedCall(0.6f, delegate
			{
				this.isDrillerReady = true;
			});
		}
		else if (newState == FishingPlayer.PlayerState.ICE_FISHING)
		{
			TutorialManager.Instance.ShowTutorial(TutorialManager.TutorialsId.ICE_FISHING_01, 3f);
			this.ufpsController.Stop();
			this.characteController.radius = 0.05f;
			this.drillingController.currentHole.GetComponent<CapsuleCollider>().enabled = false;
			if (this.currentState != FishingPlayer.PlayerState.WATCH_FISH)
			{
				this.SitDown(1f, true);
			}
			this.currentHands.transform.localRotation = Quaternion.identity;
			if (VRManager.Instance.IsControllersInput())
			{
				this.currentHands.transform.localPosition = Vector3.zero;
			}
			else
			{
				this.currentHands.transform.localPosition = new Vector3(0f, Mathf.Lerp(-0.35f, 0f, 0.6f), Mathf.Lerp(-0.2f, 0f, 0.6f));
			}
			this.gameController.hudManager.ChangeGameState(HUDManager.GameState.ICE_FISHING);
			this.gameController.hudManager.ShowHudFishing(true);
			this.currentHands.StartIceFishing();
			this.allowRun = false;
			this.ufpsInput.AllowGameplayInput = false;
			this.BlockMouseLook(true, 4f);
			this.AllowInteraction(false);
			this.TurnOnSpring(false, -1f);
			Cursor.visible = false;
			Cursor.lockState = CursorLockMode.Locked;
			this.currentHands.ShowWalkingDriller(false);
			this.ResetFishing(true);
			this.currentHands.baitWasThrown = true;
		}
		else if (newState == FishingPlayer.PlayerState.ATTRACTOR)
		{
			this.gameController.hudManager.ChangeGameState(HUDManager.GameState.ATTRACTOR);
			this.AllowInteraction(false);
		}
		else if (newState == FishingPlayer.PlayerState.DEATH)
		{
		}
		this.gameController.hudManager.ShowPullFishCursor(false);
		this.gameController.hudManager.ShowNetFishCursor(false);
		this.currentState = newState;
		if (this.fishingPlayerRemote)
		{
			this.fishingPlayerRemote.StateChanged();
		}
	}

	// Token: 0x06002B65 RID: 11109 RVA: 0x000FCFF4 File Offset: 0x000FB1F4
	public bool IsCurrentHands(FishingHands fishingHands)
	{
		return fishingHands == this.currentHands;
	}

	// Token: 0x06002B66 RID: 11110 RVA: 0x000FD002 File Offset: 0x000FB202
	public bool IsFishOnCurrentRod()
	{
		return this.fish && this.fish.storedBait.fishingHands == this.currentHands;
	}

	// Token: 0x06002B67 RID: 11111 RVA: 0x000FD032 File Offset: 0x000FB232
	[Button]
	public void FloatRopeTest()
	{
		this.allFishingHands[0].floatRopeDepth = (float)(Mathf.RoundToInt(this.allFishingHands[0].floatRopeDepth * 100f) + 1) * 0.01f;
		this.ChangeHands(0);
	}

	// Token: 0x06002B68 RID: 11112 RVA: 0x000FD071 File Offset: 0x000FB271
	[Button]
	public void ChangeHands0()
	{
		this.ChangeHands(0);
	}

	// Token: 0x06002B69 RID: 11113 RVA: 0x000FD07A File Offset: 0x000FB27A
	[Button]
	public void ChangeHands1()
	{
		this.ChangeHands(1);
	}

	// Token: 0x06002B6A RID: 11114 RVA: 0x000FD083 File Offset: 0x000FB283
	[Button]
	public void ChangeHands2()
	{
		this.ChangeHands(2);
	}

	// Token: 0x06002B6B RID: 11115 RVA: 0x000FD08C File Offset: 0x000FB28C
	[Button]
	public void ChangeHands3()
	{
		this.ChangeHands(3);
	}

	// Token: 0x06002B6C RID: 11116 RVA: 0x000FD098 File Offset: 0x000FB298
	public void ChangeHands(int id)
	{
		if (id >= this.allFishingHands.Count)
		{
			Debug.LogError("Not enough fishing hands");
			return;
		}
		if (this.currentHands && this.currentHands.baitWasThrown && !this.currentHands.fishingRod.isOnRodStand)
		{
			return;
		}
		FishingHands fishingHands = this.allFishingHands[id];
		if (this.currentHands && !this.currentHands.fishingRod.isOnRodStand)
		{
			this.currentHands.gameObject.SetActive(false);
			this.currentHands.ParentAllObjects();
		}
		bool flag = false;
		if (this.currentHands && this.currentHands.flashLight.enabled)
		{
			flag = true;
			this.currentHands.flashLight.enabled = false;
		}
		fishingHands.gameObject.SetActive(true);
		this.currentHands = null;
		fishingHands.ChangeEquipment();
		fishingHands.ChangeRod();
		fishingHands.PrepareNewHands();
		fishingHands.flashLight.enabled = flag;
		this.floatCamera.TurnOn(false);
	}

	// Token: 0x06002B6D RID: 11117 RVA: 0x000FD1C0 File Offset: 0x000FB3C0
	public void ChangeHandsRodStand(FishingHands newHands)
	{
		if (!this.currentHands.fishingRod.isOnRodStand)
		{
			this.currentHands.gameObject.SetActive(false);
			this.currentHands.ParentAllObjects();
		}
		newHands.flashLight.enabled = this.currentHands.flashLight.enabled;
		this.currentHands.flashLight.enabled = false;
		newHands.gameObject.SetActive(true);
		this.currentHands = newHands;
		this.fishingController.fishingHands = newHands;
		this.floatCamera.TurnOn(newHands.isFloatRig);
	}

	// Token: 0x06002B6E RID: 11118 RVA: 0x000FD25C File Offset: 0x000FB45C
	public void RefreshWeatherSettings()
	{
		UniStormWeatherSystem_C uniStormWeatherSystem_C = ((!this.gameController) ? null : this.gameController.uniStormSystem);
		if (uniStormWeatherSystem_C != null)
		{
			uniStormWeatherSystem_C.cameraObject = this.unistormCamera;
			uniStormWeatherSystem_C.rain = this.rain;
			uniStormWeatherSystem_C.snow = this.snow;
			uniStormWeatherSystem_C.butterflies = this.lightningBugs;
			uniStormWeatherSystem_C.rainMist = this.rainMist;
			uniStormWeatherSystem_C.rainSplashes = this.rainSplash;
			uniStormWeatherSystem_C.lightningSpawn = this.lightningPosition;
			uniStormWeatherSystem_C.snowMistFog = this.snowDust;
			uniStormWeatherSystem_C.mistFog = this.rainStreaks;
			uniStormWeatherSystem_C.windyLeaves = this.windyLeaves;
			uniStormWeatherSystem_C.lightningBolt1 = this.lightningBolt1;
			GameObject gameObject = GameObject.Find("SunGlow");
			if (gameObject)
			{
				SunShafts component = this.unistormCamera.GetComponent<SunShafts>();
				component.sunTransform = gameObject.transform;
			}
		}
	}

	// Token: 0x06002B6F RID: 11119 RVA: 0x000FD349 File Offset: 0x000FB549
	public void ResetJunk()
	{
		if (this.junk)
		{
			global::UnityEngine.Object.Destroy(this.junk.gameObject);
			this.junk = null;
		}
	}

	// Token: 0x06002B70 RID: 11120 RVA: 0x000FD374 File Offset: 0x000FB574
	public void ResetFishing(bool breakFish = true)
	{
		UtilitiesInput.StopVibration();
		if (this.fish && breakFish && this.fish.storedBait.fishingHands == this.currentHands)
		{
			this.fish.BreakFree();
			this.fish = null;
			this.pullForce = 0f;
		}
		if (breakFish)
		{
			this.ResetJunk();
		}
		if (this.currentHands.fishingFloat)
		{
			this.currentHands.fishingFloat.ResetFloat();
		}
		this.currentHands.bait.ResetBait();
		this.currentHands.bait.HasAnyBaitParts();
		if (this.currentHands.biteIndicator)
		{
			this.currentHands.biteIndicator.StopIndicating();
		}
		this.currentHands.ChangeRopesRadius(false);
		this.currentHands.ResetJerkBlend();
		this.currentHands.fishingRod.UpdateBaitDistance();
		this.currentHands.fishingRod.ChangeBendWarpPosition(true);
		this.currentHands.reel.ResetReel();
		this.currentHands.spinningMethodController.Reset();
		this.currentHands.isThrowing = false;
		this.currentHands.isThrowingNear = false;
		this.currentHands.ResetThrowFakeObject();
		if (this.currentHands.biteIndicator)
		{
			this.currentHands.biteIndicator.StopFishing();
		}
		if (this.currentHands.feeder)
		{
			this.currentHands.feeder.PulledFromWater();
		}
		this.fishingController.throwStrength = this.fishingController.throwStrengthMin;
		this.fishingController.fishCanBePulled = false;
		this.fishingController.fishCanBePulledNet = false;
		this.fishingController.isCastingFly = false;
		this.fishingController.fishInNetAreaTimer = 0f;
		this.gameController.ShowFishEscapeColliders(true);
		this.gameController.hudManager.hudFishing.UpdateThrowStrength(0f);
		this.gameController.hudManager.hudFishing.ShowThrowSlider(false);
		this.gameController.hudManager.hudFishing.ShowFlyCastSlider(false);
		this.gameController.hudManager.hudFishing.fishDurability.gameObject.SetActive(false);
		this.gameController.hudManager.hudFishing.StartTensionAlarm(false);
		this.gameController.hudManager.ShowPullFishCursor(false);
		this.gameController.hudManager.ShowNetFishCursor(false);
		if (!this.gameController.iceLevel)
		{
			this.gameController.hudManager.ShowHudFishing(false);
		}
		if (!this.gameController.iceLevel)
		{
			LeanTween.value(0f, 1f, (!this.currentHands.baitWasThrown) ? 0f : 0.25f).setOnComplete(delegate
			{
				this.currentHands.fishingRod.ShowReelLine(false);
			});
			this.ufpsInput.AllowGameplayInput = true;
			this.BlockMouseLook(false, 4f);
			this.AllowInteraction(true);
			this.TurnOnSpring(true, -1f);
			this.currentHands.baitWasThrown = false;
			if (this.fishingPlayerRemote)
			{
				this.fishingPlayerRemote.StateChanged();
			}
			this.allowRun = true;
			this.ufpsController.MotorAcceleration = (this.currentMotorAcceleration = this.motorAccelerationValues.x);
		}
		this.SetHorizontalRotationLimit(-360f, 360f);
		if (!this.gameController.iceLevel)
		{
			if (this.currentHands.fishingFloat)
			{
				global::Utilities.SetLayerRecursively(this.currentHands.fishingFloat.gameObject, "Weapon");
			}
			if (this.currentHands.feeder)
			{
				global::Utilities.SetLayerRecursively(this.currentHands.feeder.gameObject, "Weapon");
			}
			this.currentHands.bait.UpdateLayers("Weapon", false);
		}
		this.gameController.junkManager.ResetJunk();
		base.StartCoroutine(this.underwaterCamera.TurnOn(false, true));
		this.currentHands.currentRope.rate = 0f;
		this.currentHands.currentRope.transform.parent = GameController.Instance.transform;
		if (this.gameController.iceLevel)
		{
			this.currentHands.currentRope.getSegmentProperties().length = 0.2f;
		}
		this.currentHands.currentRope.regenerateRope(false);
		if (this.currentHands.isFlyRig)
		{
			LeanTween.delayedCall(0.1f, delegate
			{
				this.currentHands.currentRope.regenerateRope(false);
			});
		}
		if (this.currentHands.fishingFloat)
		{
			this.currentHands.floatRope.transform.parent = GameController.Instance.transform;
			this.currentHands.floatRope.regenerateRope(false);
			this.currentHands.fishingPlayer.floatCamera.TurnOn(false);
		}
		LeanTween.delayedCall(1f, delegate
		{
		});
		this.currentHands.fishingLine.tryPullTension = 0f;
		this.currentHands.fishingLine.currentTryReelTension = 0f;
		this.currentHands.fishingLine.pumpTension = 0f;
		this.currentHands.fishingLine.currentPumpTension = 0f;
		this.currentHands.fishingLine.breakTensionTimer = 3f;
		if (this.boatSimulator)
		{
			this.boatSimulator.PauseFloating(false);
		}
		if (VRManager.Instance.IsVRHoldRod())
		{
			this.currentHands.HideLeftArm();
		}
		this.currentHands.fishingLine.UpdateSegmentsDamping();
		this.gameController.hudManager.UpdateControls();
	}

	// Token: 0x06002B71 RID: 11121 RVA: 0x000FD987 File Offset: 0x000FBB87
	[Button]
	public void LineBreak()
	{
		GameController.Instance.fishingPlayer.LineBreak(global::Utilities.GetTranslation("HUD_MESSAGE/LINE_BROKE_TENSION", false), 0f);
	}

	// Token: 0x06002B72 RID: 11122 RVA: 0x000FD9A8 File Offset: 0x000FBBA8
	public void LineBreak(string msg, float loseBaitChance)
	{
		if (this.gameController.fishingPlayer.currentHands.fishingNet.gameObject.activeInHierarchy)
		{
			Debug.LogError("Line NOT Break NET msg: " + msg);
			return;
		}
		if (this.currentState == FishingPlayer.PlayerState.WATCH_FISH || (this.fish && this.fish.isWatchingFish))
		{
			this.currentHands.currentRope.regenerateRope(true);
			Debug.LogError("Line NOT Break WATCH_FISH msg: " + msg);
			return;
		}
		if (this.fish && this.fish.transform.parent == null)
		{
			return;
		}
		if (this.junk && this.junk.transform.parent == null)
		{
			return;
		}
		bool flag = false;
		this.gameController.hudManager.ShowMessage(string.Empty + msg + ((!flag) ? string.Empty : ("\n" + global::Utilities.GetTranslation("HUD_MESSAGE/BAIT_LOST", false))), 3.5f);
		if (this.gameController.iceLevel)
		{
			this.ResetFishing(true);
		}
		else
		{
			this.ChangeState(FishingPlayer.PlayerState.NORMAL);
		}
	}

	// Token: 0x06002B73 RID: 11123 RVA: 0x000FDAFC File Offset: 0x000FBCFC
	public void OnCatchBait(Fish f, FishingHands hands)
	{
		if (f != null && this.fish != null)
		{
			Debug.LogError("OnCatchBait 2 fish");
			return;
		}
		this.fish = f;
		if (this.fishingPlayerRemote)
		{
			this.fishingPlayerRemote.StateChanged();
		}
		hands.bait.baitAnimation.ResetMovingParts(true);
		if (!hands.fishingFloat && !hands.isGroundRig)
		{
			hands.bait.SetFish(this.fish);
		}
		if (hands.biteIndicator)
		{
			hands.biteIndicator.StartIndicating();
		}
		if ((hands.isGroundRig || hands.fishingRod.isOnRodStand) && !this.boatSimulator)
		{
			hands.fishingRod.ChangeBendWarpPosition(false);
		}
		hands.bait.rigidbody.isKinematic = true;
		hands.bait.rigidbody.useGravity = false;
		hands.bait.baitAnimation.StopAnimation();
		if (!VRManager.Instance.IsVRReeling() && !hands.fishingFloat && !hands.isGroundRig && hands.currentUserReelSpeed < 0.5f)
		{
			hands.currentUserReelSpeed = 0.5f;
		}
		if (hands.fishingFloat)
		{
			hands.bait.transform.parent = GameController.Instance.transform;
			hands.floatRope.transform.parent = GameController.Instance.transform;
		}
		if (this.gameController.iceLevel)
		{
			hands.currentRope.getSegmentProperties().length = 0.75f;
			hands.currentRope.regenerateRope(true);
		}
		this.pullForce = 0f;
		if (!hands.fishingFloat)
		{
		}
		if (!GlobalSettings.Instance || GlobalSettings.Instance.turnOnMyCheats)
		{
			this.gameController.hudManager.hudFishing.fishDurability.gameObject.SetActive(true);
			this.gameController.hudManager.hudFishing.strengthDifferenceSlider.gameObject.SetActive(true);
		}
		this.gameController.hudManager.hudFishing.InitStrengthDifference(this.fish);
		hands.spinningMethodController.Reset();
		if (!this.underwaterCamera.isTurnedOn || !hands.fishingFloat)
		{
		}
		float num = Mathf.Clamp(this.fish.Length, 0f, 3f);
		if (hands.fishingFloat || hands.isGroundRig)
		{
			this.underwaterCamera.SetDistanceAway(num * 0.6f, 2f);
		}
		else
		{
			this.underwaterCamera.SetDistanceAway(num * 1.5f, 1f);
		}
		TutorialManager.Instance.ShowTutorial(TutorialManager.TutorialsId.STRIKE_01, 1.5f);
	}

	// Token: 0x06002B74 RID: 11124 RVA: 0x000FDE08 File Offset: 0x000FC008
	public void OnCatchJunk(Junk newJunk)
	{
		this.junk = newJunk;
		this.junk.transform.parent = this.currentHands.bait.catchPosition;
		this.junk.transform.localEulerAngles = new Vector3(-90f, 0f, 0f);
		this.junk.transform.localPosition = new Vector3(-this.junk.hookPosition.localPosition.x, -this.junk.hookPosition.localPosition.z, this.junk.hookPosition.localPosition.y);
		this.currentHands.bait.baitAnimation.ResetMovingParts(true);
		this.currentHands.bait.SetJunk(this.junk);
		this.currentHands.bait.baitAnimation.StopAnimation();
		this.currentHands.spinningMethodController.Reset();
	}

	// Token: 0x06002B75 RID: 11125 RVA: 0x000FDF10 File Offset: 0x000FC110
	public void FishCatch(Fish netFish = null)
	{
		if (this.fish == null)
		{
			Debug.LogError("FishCatch fish == null");
			return;
		}
		if (this.fish.isWatchingFish && !this.gameController.iceLevel)
		{
			return;
		}
		this.fish.isWatchingFish = true;
		this.fish.CheckBehaviorDistance();
		this.fish.isInNetArea = false;
		this.fish.isOnGround = false;
		this.fish.particleSwim.Stop();
		this.fish.animController.ChangeAnimation(FishAnimationController.AnimType.WATCH);
		this.fish.stateIndicator.gameObject.SetActive(false);
		this.fish.DisableAI();
		this.fish.pullOutPosition = this.fish.transform.position;
		if (netFish && this.fish != netFish)
		{
			Debug.LogError("Wrong fish?");
		}
		if (netFish && !this.fish)
		{
			this.fish = netFish;
		}
		Debug.Log("!!! FISH CATCH !!! " + this.fish.fishName);
		if (this.gameController.isMultiplayer)
		{
			MultiplayerManager.Instance.multiplayerChat.SendFish(this.fish);
		}
		if (this.gameController.isTournament)
		{
			this.tournamentPlayer.CatchFish(this.fish);
		}
		if (GlobalTournamentManager.Instance)
		{
			GlobalTournamentManager.Instance.FishCaught(this.fish);
		}
		base.StartCoroutine(this.ShowCatchObject());
	}

	// Token: 0x06002B76 RID: 11126 RVA: 0x000FE0BC File Offset: 0x000FC2BC
	public IEnumerator ShowCatchObject()
	{
		GameObject showObject = ((!this.fish) ? this.junk.gameObject : this.fish.gameObject);
		if (showObject == null || this.currentHands == null)
		{
			yield break;
		}
		if (showObject.transform.parent == this.currentHands.transform)
		{
			yield break;
		}
		if (showObject.transform.parent == null)
		{
			yield break;
		}
		if (showObject.transform.parent == this.currentHands.fishingNet.transform)
		{
			yield break;
		}
		if (this.currentHands.animator.GetBool("WatchFish"))
		{
			yield break;
		}
		if (VRManager.IsVROn())
		{
			HUDManager.Instance.ShowHudFishing(false);
		}
		if (this.gameController.iceLevel)
		{
			this.currentHands.transform.localPosition = Vector3.zero;
			this.currentHands.transform.localRotation = Quaternion.identity;
			Vector3 position = this.currentHands.watchFishPosition.position;
			position.x = this.drillingController.currentHole.Data.Position.x;
			position.z = this.drillingController.currentHole.Data.Position.z;
			this.currentHands.watchFishPosition.position = position;
		}
		if (this.fish.watchStyle == Fish.WatchStyle.BOAT)
		{
			if (this.boatSimulator)
			{
				if (this.underwaterCamera.isTurnedOn)
				{
					base.StartCoroutine(this.underwaterCamera.TurnOn(false, false));
				}
				else
				{
					this.gameController.hudManager.FadeDark(1f, 0.3f, false);
				}
				yield return new WaitForSeconds(0.3f);
			}
			else
			{
				this.fish.watchStyle = Fish.WatchStyle.HANDS;
			}
		}
		if (this.fish.watchStyle == Fish.WatchStyle.HANDS && VRManager.Instance.IsVRHoldRod())
		{
			this.gameController.hudManager.FadeDark(1f, 0.3f, false);
			yield return new WaitForSeconds(0.3f);
		}
		Debug.Log("ShowCatchObject " + this.fish.watchStyle.ToString());
		if (this.fish)
		{
			this.fish.particleSwim.Stop();
		}
		this.currentWatchStyle = ((!this.fish) ? Fish.WatchStyle.HOOK_LIGHT : this.fish.watchStyle);
		showObject.transform.SetParent(null);
		if (this.currentWatchStyle != Fish.WatchStyle.HOOK_LIGHT)
		{
			if (this.currentWatchStyle == Fish.WatchStyle.HANDS)
			{
				this.currentHands.fishingNet.MoveFishToCatchPosition(showObject);
				this.currentHands.bait.ResetBait();
				this.currentHands.bait.rigidbody.isKinematic = true;
			}
		}
		if (!VRManager.Instance.IsControllersInput())
		{
			this.currentHands.animator.SetBool("WatchFish", true);
		}
		this.gameController.hudManager.hudWatchFish.UpdateWatchFishPosition(this.currentWatchStyle);
		if (this.currentWatchStyle != Fish.WatchStyle.BOAT)
		{
			base.StartCoroutine(this.underwaterCamera.TurnOn(false, false));
		}
		if (this.currentWatchStyle == Fish.WatchStyle.HOOK_LIGHT)
		{
			this.currentHands.ShowWatchFishRopes();
		}
		else
		{
			this.currentHands.ShowRopes(false);
		}
		if (this.gameController.iceLevel)
		{
			LeanTween.delayedCall(0f, new Action(this.currentHands.HideLeftArm));
		}
		if (this.currentWatchStyle == Fish.WatchStyle.HOOK_LIGHT)
		{
			if (!this.gameController.iceLevel)
			{
				this.ufpsCamera.SetRotation(this.ufpsCamera.Transform.eulerAngles, false);
				Quaternion quaternion = Quaternion.LookRotation(showObject.transform.position - this.ufpsCamera.transform.position);
				Vector2 vector = new Vector2(quaternion.eulerAngles.x, quaternion.eulerAngles.y);
				if (this.currentWatchStyle == Fish.WatchStyle.HANDS)
				{
					vector.x = 30f;
				}
				else
				{
					vector.x = 0f;
				}
				LeanTween.value(base.gameObject, delegate(Vector2 v)
				{
					this.ufpsCamera.Angle = v;
				}, this.ufpsCamera.Angle, vector, 0.3f).setEase(LeanTweenType.easeInOutQuad);
			}
			yield return new WaitForSeconds(0.2f);
			showObject.transform.SetParent(this.currentHands.transform);
		}
		if (this.fish)
		{
			this.fish.particleSwim.Stop();
		}
		if (!VRManager.Instance.IsVRHoldRod())
		{
			this.currentHands.animator.SetInteger("WatchStyle", (int)this.currentWatchStyle);
		}
		else
		{
			this.currentHands.leftArm.SetActive(false);
		}
		this.gameController.hudManager.ChangeGameState(HUDManager.GameState.WATCH_FISH);
		this.ChangeState(FishingPlayer.PlayerState.WATCH_FISH);
		if (this.currentWatchStyle == Fish.WatchStyle.HANDS)
		{
			this.currentHands.HideRod();
			this.currentHands.ActivateRopes(false);
		}
		this.currentHands.bait.ropeRigidbody.transform.localEulerAngles = Vector3.zero;
		this.pullForce = 0f;
		LeanTween.delayedCall(0.1f, delegate
		{
			this.currentHands.fishingRod.ResetBend();
			if (this.currentWatchStyle == Fish.WatchStyle.HANDS && VRManager.Instance.IsControllersInput())
			{
				this.currentHands.HideRightArm();
			}
		});
		this.currentHands.currentRope.regenerateRope(false);
		if (this.fish)
		{
			this.fish.animController.squirmTimer = global::UnityEngine.Random.Range(5f, 8f);
		}
		AudioController.Play("SplashOut_01", showObject.transform.position, null);
		this.WaterSplash(1f, 0f);
		if (Vector3.Distance(this.currentHands.leftArmReelParent.transform.localPosition, this.currentHands.leftArmReelParentStartPos) > 0.05f)
		{
			this.currentHands.leftArmReelParent.transform.localPosition = this.currentHands.leftArmReelParentStartPos;
			Debug.Log("Wrong ReelArmParent pos 1");
		}
		if (this.currentWatchStyle == Fish.WatchStyle.BOAT && this.fish && this.boatSimulator)
		{
			if (this.boatSimulator)
			{
				this.fish.animController.ChangeAnimation(FishAnimationController.AnimType.WATCH);
				this.boatSimulator.PauseFloating(true);
				if (this.boatSimulator.keepMovingForward)
				{
					this.boatSimulator.keepMovingForward = false;
					this.boatSimulator.StopBoat();
					this.transform.parent = this.boatSimulator.enterPosition;
				}
				if (this.boatSimulator.watchFishOnSide && Mathf.Sign(this.boatSimulator.watchFishPosition.localPosition.x) != Mathf.Sign(this.transform.localPosition.x))
				{
					this.boatSimulator.watchFishPosition.localPosition = new Vector3(-this.boatSimulator.watchFishPosition.localPosition.x, this.boatSimulator.watchFishPosition.localPosition.y, this.boatSimulator.watchFishPosition.localPosition.z);
					this.boatSimulator.watchFishPosition.localEulerAngles += Vector3.up * 180f;
					this.boatSimulator.watchPlayerPosition.localPosition = new Vector3(-this.boatSimulator.watchPlayerPosition.localPosition.x, this.boatSimulator.watchPlayerPosition.localPosition.y, this.boatSimulator.watchPlayerPosition.localPosition.z);
					this.boatSimulator.watchPlayerPosition.localEulerAngles += Vector3.up * 180f;
					Debug.LogError("watchFishOnSide change");
				}
				if (this.fish.animController.fishAnimSwim.useXAxisForSwim)
				{
					showObject.transform.localEulerAngles = this.boatSimulator.watchFishPosition.eulerAngles + new Vector3(0f, 0f, -100f);
				}
				else
				{
					showObject.transform.eulerAngles = this.boatSimulator.watchFishPosition.eulerAngles;
				}
				Vector3 newPos = this.boatSimulator.watchFishPosition.position;
				newPos += this.fish.transform.forward * (this.fish.Length * 0.3f);
				newPos.y = -0.2f;
				showObject.transform.position = newPos;
				this.characteController.enabled = false;
				this.ufpsController.enabled = false;
				this.ufpsCamera.enabled = false;
				this.SetPosition(this.boatSimulator.watchPlayerPosition.position);
				this.SetRotation(this.boatSimulator.watchPlayerPosition.eulerAngles);
				this.ufpsCamera.transform.eulerAngles = new Vector3(45f, this.ufpsCamera.transform.eulerAngles.y, this.ufpsCamera.transform.eulerAngles.z);
				this.Zoom(true, 0.01f, Mathf.Lerp(0.7f, 1f, Mathf.InverseLerp(1.5f, 5.5f, this.fish.Length)));
				this.currentHands.HideHandsCamera(true, false);
				if (VRManager.IsVROn())
				{
					this.currentHands.HideRod();
				}
				this.gameController.weatherLevelManager.UpdateWaterProfiles();
				yield return new WaitForSeconds(0.3f);
				this.gameController.hudManager.FadeDark(0f, 0.3f, false);
			}
		}
		else if (this.currentWatchStyle == Fish.WatchStyle.HANDS)
		{
			this.fish.animController.ChangeAnimation(FishAnimationController.AnimType.WATCH);
			if (this.fish.animController.fishWatchAnims.Count > 0)
			{
				this.fish.animController.fishWatchAnims[0].enabled = false;
			}
			yield return new WaitForSeconds(1f);
			if (this.fish)
			{
				this.fish.particleSwim.Stop();
			}
			if (this.fish.Length > 1.5f)
			{
				showObject.transform.parent = this.currentHands.watchBigHandsPosition.transform;
				this.currentHands.ShowArmRenderers(false);
			}
			else
			{
				showObject.transform.parent = this.currentHands.watchHandsPosition.transform;
			}
			this.SetShowObjectPosition();
			if (VRManager.Instance.IsVRHoldRod())
			{
				yield return new WaitForSeconds(0.3f);
				this.vrWatchHandsCameraStartAngle = this.ufpsCamera.transform.localEulerAngles.y;
				this.gameController.hudManager.FadeDark(0f, 0.3f, false);
			}
		}
		else if (this.currentWatchStyle == Fish.WatchStyle.HOOK_LIGHT && this.fish)
		{
			Transform parent = this.fish.mouth.parent;
			this.fish.mouth.parent = this.fish.transform;
			Vector3 vector2 = this.currentHands.watchFishPosition.localPosition - new Vector3(0f, 0f, 0f);
			vector2.y -= this.fish.mouth.localPosition.z * this.fish.transform.localScale.z;
			if (this.gameController.iceLevel)
			{
				showObject.transform.localEulerAngles = this.currentHands.watchFishPosition.localEulerAngles;
				showObject.transform.position = this.drillingController.currentHole.centerDown;
				LeanTween.moveLocal(showObject, vector2, 0.75f).setOnComplete(delegate
				{
					this.fish.isWatchingFishReady = true;
				});
			}
			else
			{
				float num = Vector3.Distance(showObject.transform.localPosition, vector2) / this.currentHands.watchFishFlySpeed;
				LeanTween.rotateLocal(showObject, this.currentHands.watchFishPosition.localEulerAngles, num * 0.6f);
				if (VRManager.Instance.IsVRHoldRod())
				{
					num *= 0.4f;
					vector2 = this.currentHands.fishingRod.GetVRBaitHoldPosition(true);
					LeanTween.move(showObject, vector2, num).setOnComplete(delegate
					{
						this.fish.isWatchingFishReady = true;
					});
				}
				else
				{
					LeanTween.moveLocal(showObject, vector2, num).setOnComplete(delegate
					{
						this.fish.isWatchingFishReady = true;
					});
				}
			}
			this.fish.mouth.parent = parent;
		}
		else if (this.currentWatchStyle == Fish.WatchStyle.HOOK_LIGHT && this.junk)
		{
			this.currentHands.watchFishLine.gameObject.SetActive(true);
			float num2 = Vector3.Distance(showObject.transform.localPosition, this.currentHands.watchJunkPosition.localPosition) / this.currentHands.watchFishJunkSpeed;
			Debug.Log("Show Object flyTime: " + num2);
			LeanTween.rotateLocal(showObject, this.junk.watchRotation, num2 * 0.6f);
			LeanTween.moveLocal(showObject, this.currentHands.watchJunkPosition.localPosition - new Vector3(0f, 0f, 0f), num2);
		}
		if (this.fish)
		{
			this.fish.ChangeMaterial(true);
			if (this.fish.watchStyle != Fish.WatchStyle.BOAT)
			{
				LeanTween.delayedCall((!this.gameController.iceLevel) ? 0f : 0.5f, delegate
				{
					global::Utilities.SetLayerRecursively(showObject.gameObject, "Weapon");
				});
			}
		}
		else if (this.junk)
		{
			global::Utilities.SetLayerRecursively(showObject.gameObject, "Default");
		}
		if (this.fish)
		{
			TutorialManager.Instance.ShowTutorial(TutorialManager.TutorialsId.WATCH_FISH_01, (!VRManager.IsVROn()) ? 1f : 0.2f);
			this.fish.particleSwim.Stop();
		}
		LeanTween.delayedCall(0.1f, delegate
		{
			this.currentHands.fishingRod.ResetBend();
		});
		LeanTween.delayedCall(2f, delegate
		{
			if (Vector3.Distance(this.currentHands.leftArmReelParent.transform.localPosition, this.currentHands.leftArmReelParentStartPos) > 0.05f)
			{
				this.currentHands.leftArmReelParent.transform.localPosition = this.currentHands.leftArmReelParentStartPos;
			}
		});
		yield break;
	}

	// Token: 0x06002B77 RID: 11127 RVA: 0x000FE0D8 File Offset: 0x000FC2D8
	private void SetShowObjectPosition()
	{
		GameObject gameObject = ((!this.fish) ? this.junk.gameObject : this.fish.gameObject);
		if (this.fish == null)
		{
			Debug.LogError("SetShowObjectPosition fish == null");
			return;
		}
		if (this.currentWatchStyle == Fish.WatchStyle.HOOK_LIGHT)
		{
			if (this.fish.isWatchingFishReady)
			{
				if (!VRManager.Instance.IsVRHoldRod())
				{
					Transform parent = this.fish.mouth.parent;
					this.fish.mouth.parent = this.fish.transform;
					gameObject.transform.localPosition = this.currentHands.watchFishPosition.localPosition - new Vector3(0f, this.fish.mouth.localPosition.z * this.fish.transform.localScale.z, 0f);
					this.fish.mouth.parent = parent;
				}
			}
		}
		else if (this.fish.watchStyle == Fish.WatchStyle.HANDS)
		{
			if (VRManager.Instance.IsVRHoldRod())
			{
				this.fish.transform.parent = this.transform;
				if (this.fish.animController.fishAnimSwim.useXAxisForSwim)
				{
					this.fish.transform.localEulerAngles = new Vector3(0f, -90f, 10f);
				}
				else
				{
					this.fish.transform.localEulerAngles = new Vector3(0f, -90f, -60f);
				}
				this.fish.transform.localEulerAngles += new Vector3(0f, this.vrWatchHandsCameraStartAngle, 0f);
				this.fish.transform.position = VRControllersManager.Instance.GetVRController(OVRInput.Controller.RTouch).transform.position;
				this.fish.transform.position += this.fish.transform.forward * (this.fish.Length * 0.5f + 0.3f);
				this.fish.transform.localPosition += new Vector3(0f, -0.1f, 0.1f);
			}
			else
			{
				if (this.fish.animController.fishAnimSwim.useXAxisForSwim)
				{
					gameObject.transform.localEulerAngles = new Vector3(0f, 0f, 70f);
				}
				else
				{
					gameObject.transform.localEulerAngles = Vector3.zero;
				}
				if (this.fish.Length > 1.5f)
				{
					gameObject.transform.localPosition = Vector3.zero + new Vector3(0f, this.fish.boxCollider.size.y * this.fish.transform.localScale.x * 0.25f, this.fish.Length * 0.4f);
				}
				else
				{
					gameObject.transform.localPosition = Vector3.zero + new Vector3(-this.fish.boxCollider.size.x * this.fish.transform.localScale.x * 0.3f, 0f, this.fish.Length * 0.4f);
				}
			}
		}
	}

	// Token: 0x06002B78 RID: 11128 RVA: 0x000FE4A8 File Offset: 0x000FC6A8
	[Button]
	public void RestWatchFish()
	{
		this.currentHands.bait.transform.position = this.currentHands.watchFishPosition.position;
		if (this.currentHands.fishingFloat)
		{
			this.currentHands.bait.transform.position += Vector3.up * 0.7f;
		}
		Debug.LogError("currentHands.bait.transform.position " + this.currentHands.bait.transform.position);
		Debug.LogError("currentHands.watchFishPosition.position " + this.currentHands.watchFishPosition.position);
		this.currentHands.currentRope.regenerateRope(false);
	}

	// Token: 0x06002B79 RID: 11129 RVA: 0x000FE57C File Offset: 0x000FC77C
	public bool IsSomethingOnBait()
	{
		return this.HasFishOnCurrentRod();
	}

	// Token: 0x06002B7A RID: 11130 RVA: 0x000FE584 File Offset: 0x000FC784
	public void WatchFishDecision(int decision)
	{
		Debug.Log("WatchFishDecision " + decision);
		if (this.fish && GlobalSettings.Instance && !this.gameController.fisheryEditorGame)
		{
			GlobalSettings.Instance.fishManager.AddSpeciesLevel(this.fish);
		}
		if (this.fish)
		{
			this.fish.ChangeMaterial(false);
		}
		if (this.fish && this.fish.watchStyle == Fish.WatchStyle.BOAT)
		{
			this.currentHands.HideHandsCamera(false, true);
			this.Zoom(false, 0f, 0.75f);
			if (this.boatSimulator)
			{
				this.boatSimulator.PauseFloating(false);
			}
		}
		if (decision == 1)
		{
			this.fish.ReleaseFish(true, Vector3.zero, Vector3.zero);
			this.fish = null;
		}
		else if (decision == 5)
		{
			if (GlobalSettings.Instance)
			{
				GlobalSettings.Instance.fishManager.KeepFish(this.fish, GlobalSettings.Instance.levelsManager.GetCurrentFishery().name);
			}
			this.fish.ReleaseFish(true, Vector3.zero, Vector3.zero);
			this.fish = null;
		}
		else if (decision == 2)
		{
			if (GlobalSettings.Instance)
			{
			}
			Vector3 vector = this.fish.pullOutPosition - this.transform.position;
			if (this.gameController.iceLevel)
			{
				this.fish.ReleaseFish(true, Vector3.zero, Vector3.zero);
			}
			else
			{
				this.fish.ReleaseFish(false, this.fish.pullOutPosition + new Vector3(0f, 1f, 0f), new Vector3(60f, this.transform.eulerAngles.y, 0f));
			}
			this.fish = null;
		}
		if (this.junk && !this.junk.isExploding)
		{
			this.ResetJunk();
		}
		this.currentHands.animator.SetBool("WatchFish", false);
		this.currentHands.watchFishLine.gameObject.SetActive(false);
		if (VRManager.IsVROn())
		{
			this.currentHands.ShowRightArm();
		}
		this.currentHands.ActivateRopes(true);
		this.currentHands.ShowRod();
		this.LeanForward(0.1f);
		this.characteController.enabled = true;
		this.ufpsController.enabled = true;
		if (!VRManager.IsVROn())
		{
			this.ufpsCamera.enabled = true;
		}
		if (this.gameController.iceLevel)
		{
			this.currentHands.ShowRightArm();
			this.currentHands.ShowLeftArm();
			this.ChangeState(FishingPlayer.PlayerState.ICE_FISHING);
		}
		else
		{
			this.ChangeState(FishingPlayer.PlayerState.NORMAL);
			this.TurnOnSpring(true, -1f);
			LeanTween.value(0f, 1f, 1f).setOnComplete(delegate
			{
				if (this.currentHands)
				{
					this.currentHands.HideLeftArm();
				}
				else
				{
					Debug.LogError("WatchFishDecision LeanTween currentHands == null");
				}
			});
		}
		this.currentHands.ShowRopes(true);
		this.currentHands.ShowArmRenderers(true);
		TutorialManager.Instance.ShowTutorial(TutorialManager.TutorialsId.HOOKS_01, 1f);
		if (this.gameController.isMultiplayer && MultiplayerManager.Instance)
		{
			MultiplayerManager.Instance.UpdateMyStats();
		}
		if (this.gameController.fisheryEditorGame == null)
		{
			if (GlobalSettings.Instance)
			{
				GlobalSettings.Instance.playerSettings.Save();
			}
			if (LeaderboardsManager.Instance)
			{
				LeaderboardsManager.Instance.UploadAllScores(".MAIN.");
				LeaderboardsManager.Instance.UploadAllScores(GlobalSettings.Instance.levelsManager.GetCurrentFishery().leaderboardName);
			}
			if (SteamStatsManager.Instance)
			{
				SteamStatsManager.Instance.StoreStats();
			}
		}
	}

	// Token: 0x06002B7B RID: 11131 RVA: 0x000FE9A4 File Offset: 0x000FCBA4
	public void ToggleFlashlight()
	{
		AudioController.Play((!this.currentHands.flashLight.enabled) ? "Flashlight_ON_01" : "Flashlight_OFF_01");
		LeanTween.delayedCall(0.1f, delegate
		{
			this.currentHands.flashLight.enabled = !this.currentHands.flashLight.enabled;
		});
	}

	// Token: 0x06002B7C RID: 11132 RVA: 0x000FE9F4 File Offset: 0x000FCBF4
	public void TurnOnHunterVision()
	{
		if (!this.isHunterVisionOn && this.currentState != FishingPlayer.PlayerState.WATCH_FISH && this.currentState != FishingPlayer.PlayerState.DEATH && !this.underwaterCamera.isTurnedOn && (!GlobalSettings.Instance || GlobalSettings.Instance.skillsManager.GetSkill(SkillsManager.SkillType.HUNTER_VISION_1).isUnlocked || GlobalSettings.Instance.skillsManager.GetSkill(SkillsManager.SkillType.HUNTER_VISION_2).isUnlocked || GlobalSettings.Instance.skillsManager.GetSkill(SkillsManager.SkillType.HUNTER_VISION_3).isUnlocked))
		{
			if (!GlobalSettings.Instance || GlobalSettings.Instance.playerSettings.IsCasual())
			{
				base.StartCoroutine(this.HunterVision());
			}
			return;
		}
	}

	// Token: 0x06002B7D RID: 11133 RVA: 0x000FEAD4 File Offset: 0x000FCCD4
	public IEnumerator EatIceFishing()
	{
		this.ChangeState(FishingPlayer.PlayerState.NORMAL);
		yield return new WaitForSeconds(2f);
		this.currentHands.Eat();
		yield return new WaitForSeconds(6f);
		this.currentHands.ShowLeftArm();
		this.ChangeState(FishingPlayer.PlayerState.ICE_FISHING);
		yield break;
	}

	// Token: 0x06002B7E RID: 11134 RVA: 0x000FEAEF File Offset: 0x000FCCEF
	public void SetTournamentPlayer(TournamentPlayer tournamentPl)
	{
		this.tournamentPlayer = tournamentPl;
		this.tournamentPlayer.isAi = false;
		this.tournamentPlayer.isLocalPlayer = true;
		this.UpdateTournamentPlayerName();
	}

	// Token: 0x06002B7F RID: 11135 RVA: 0x000FEB18 File Offset: 0x000FCD18
	public void UpdateTournamentPlayerName()
	{
		if (this.tournamentPlayer == null)
		{
			return;
		}
		if (MultiplayerManager.Instance && PhotonNetwork.connected)
		{
			this.tournamentPlayer.playerName = PhotonNetwork.playerName;
		}
		else if (GlobalSettings.Instance)
		{
			this.tournamentPlayer.playerName = GlobalSettings.Instance.playerSettings.playersName;
		}
		else
		{
			this.tournamentPlayer.playerName = "Editor Player";
		}
	}

	// Token: 0x06002B80 RID: 11136 RVA: 0x000FEBA3 File Offset: 0x000FCDA3
	public void ExitBoatCoroutine()
	{
		base.StartCoroutine(this.EnterBoat(false, this.boatSimulator));
	}

	// Token: 0x06002B81 RID: 11137 RVA: 0x000FEBBC File Offset: 0x000FCDBC
	public IEnumerator EnterBoat(bool enter, BoatSimulator boat)
	{
		if (this.gameController.hudManager.fadeDark.color.a > 0f)
		{
			yield break;
		}
		this.allowFishing = false;
		this.gameController.resetingPlayer = true;
		this.gameController.hudManager.FadeDark(1f, 0.4f, false);
		yield return new WaitForSeconds(0.4f);
		this.EnterBoatInstant(enter, boat);
		yield return new WaitForSeconds((!this.gameController.newSpawnersDeactivateMethod || enter) ? 0.2f : 2f);
		this.gameController.hudManager.FadeDark(0f, (!this.gameController.newSpawnersDeactivateMethod || enter) ? 0.4f : 2f, false);
		this.allowFishing = true;
		this.gameController.hudManager.UpdateControls();
		if (enter)
		{
			boat.PlayEngineStartSound(true);
		}
		this.gameController.resetingPlayer = false;
		TutorialManager.Instance.ShowTutorial(TutorialManager.TutorialsId.BOAT_01, 0.9f);
		yield break;
	}

	// Token: 0x06002B82 RID: 11138 RVA: 0x000FEBE8 File Offset: 0x000FCDE8
	public void EnterBoatInstant(bool enter, BoatSimulator boat)
	{
		SphereCollider component = base.GetComponent<SphereCollider>();
		if (component)
		{
			if (enter)
			{
				component.center = Vector3.up * 1.45f;
			}
			else
			{
				component.center = Vector3.up * 0.85f;
			}
		}
		if (!enter)
		{
			this.DriveBoatInstant(false);
			this.characteController.enabled = false;
		}
		if (FishingHands.rodStand)
		{
			FishingHands.rodStand.TakeStand();
		}
		FishingHands.rodStand = ((!enter) ? FishingHands.rodStandFromEquipment : boat.rodStand);
		boat.EnterBoat(enter);
		this.ChangeState(FishingPlayer.PlayerState.NORMAL);
		this.dustParticles.gameObject.SetActive(!enter && !this.gameController.iceLevel);
		this.waterInteractive.enabled = !enter;
		if (boat.changePlayerParent)
		{
			this.transform.parent = ((!enter) ? null : boat.enterPosition);
			this.transform.localPosition = Vector3.zero;
			this.transform.localEulerAngles = Vector3.zero;
		}
		this.SetPosition((!enter) ? boat.exitPosition.position : boat.enterPosition.position);
		if (!FishingPlayer.playerWasInBoat)
		{
			this.SetRotation((!enter) ? boat.exitPosition.eulerAngles : boat.enterPosition.eulerAngles);
		}
		if (!enter)
		{
			boat.PlayEngineStartSound(false);
			base.StartCoroutine(this.ExitBoat());
		}
		if (enter)
		{
			this.boatSimulator = boat;
			this.DriveBoatInstant(true);
			if (this.boatSimulator.isKayak)
			{
				this.ufpsController.AllowMoveInput = false;
				this.SitDown(this.boatSimulator.kayakSitHeight, false);
				this.ufpsCamera.transform.localPosition = new Vector3(this.ufpsCamera.transform.localPosition.x, this.boatSimulator.kayakSitHeight, this.ufpsCamera.transform.localPosition.z);
			}
		}
	}

	// Token: 0x06002B83 RID: 11139 RVA: 0x000FEE20 File Offset: 0x000FD020
	public IEnumerator ExitBoat()
	{
		if (!this.boatSimulator)
		{
			yield break;
		}
		yield return null;
		this.gameController.fisheryExitCounter = 0;
		this.characteController.enabled = true;
		this.characteController.SimpleMove(Vector3.zero);
		this.ufpsController.Stop();
		this.boatSimulator.StopBoat();
		this.boatSimulator = null;
		this.ufpsController.AllowMoveInput = true;
		this.SitDown(-1f, false);
		this.gameController.hudManager.UpdateControls();
		yield break;
	}

	// Token: 0x06002B84 RID: 11140 RVA: 0x000FEE3C File Offset: 0x000FD03C
	public IEnumerator DriveBoat(bool start)
	{
		if (!this.boatSimulator)
		{
			yield break;
		}
		if (this.gameController.hudManager.fadeDark.color.a > 0f)
		{
			yield break;
		}
		if (start && this.boatSimulator.engineIdleAudioObject != null && !this.boatSimulator.engineIdleAudioObject.isFadeOutComplete)
		{
			yield return null;
		}
		this.gameController.hudManager.FadeDark(1f, 0.2f, false);
		if (start)
		{
			yield return new WaitForSeconds(1f);
		}
		else
		{
			yield return new WaitForSeconds(0.2f);
		}
		this.DriveBoatInstant(start);
		yield return new WaitForSeconds(0.1f);
		this.gameController.hudManager.FadeDark(0f, 0.2f, false);
		this.boatSimulator.ResetRotations();
		yield break;
	}

	// Token: 0x06002B85 RID: 11141 RVA: 0x000FEE60 File Offset: 0x000FD060
	public void DriveBoatInstant(bool start)
	{
		if (!this.boatSimulator)
		{
			return;
		}
		if (start)
		{
			this.boatSimulator.ChangeState(BoatSimulator.BoatState.PLAYER_DRIVING);
			this.ChangeState(FishingPlayer.PlayerState.DRIVING_BOAT);
			if (this.isHandsCameraVisible)
			{
				if (VRManager.IsVROn())
				{
					this.ufpsCameraCamera.cullingMask ^= 1 << LayerMask.NameToLayer("Weapon");
				}
				else
				{
					this.ufpsWeaponCamera.cullingMask ^= 1 << LayerMask.NameToLayer("Weapon");
				}
			}
			if (this.boatSimulator.isKayak)
			{
				global::Utilities.SetLayerRecursively(this.currentHands.kayakHandsParent, LayerMask.NameToLayer("Default"));
				this.currentHands.kayakHandsParent.SetActive(true);
				if (this.boatSimulator.paddles)
				{
					this.boatSimulator.paddles.transform.parent.gameObject.SetActive(false);
				}
				this.currentHands.kayakHandsAnimation.parent = this.transform;
				this.SitDown(this.boatSimulator.kayakSitHeight, false);
				this.ufpsCamera.transform.localPosition = new Vector3(this.ufpsCamera.transform.localPosition.x, this.boatSimulator.kayakSitHeight, this.ufpsCamera.transform.localPosition.z);
			}
			this.ufpsCamera.enabled = false;
			this.transform.parent = this.boatSimulator.enterPosition;
			this.SetPosition(this.boatSimulator.enterPosition.position);
			if (!FishingPlayer.playerWasInBoat || VRManager.IsVROn())
			{
				this.SetRotation(this.boatSimulator.enterPosition.eulerAngles);
			}
			this.ufpsCamera.transform.eulerAngles = new Vector3(0f, this.ufpsCamera.transform.eulerAngles.y, this.ufpsCamera.transform.eulerAngles.z);
			this.ufpsCamera.transform.localPosition = new Vector3(this.ufpsCamera.transform.localPosition.x, this.playerHeight, this.ufpsCamera.transform.localPosition.z);
			this.gameController.hudManager.ShowRadar(!this.boatSimulator.isKayak);
			this.gameController.weatherLevelManager.UpdateWaterProfiles();
		}
		else
		{
			base.StartCoroutine(this.StopDriving());
			this.transform.parent = ((!this.boatSimulator.keepMovingForward) ? this.boatSimulator.enterPosition : null);
		}
		this.characteController.enabled = !start;
	}

	// Token: 0x06002B86 RID: 11142 RVA: 0x000FF150 File Offset: 0x000FD350
	public IEnumerator StopDriving()
	{
		if (!this.boatSimulator)
		{
			yield break;
		}
		this.boatSimulator.ChangeState(BoatSimulator.BoatState.PLAYER_FISHING);
		this.ChangeState(FishingPlayer.PlayerState.NORMAL);
		if (this.isHandsCameraVisible)
		{
			if (VRManager.IsVROn())
			{
				this.ufpsCameraCamera.cullingMask |= 1 << LayerMask.NameToLayer("Weapon");
			}
			else
			{
				this.ufpsWeaponCamera.cullingMask |= 1 << LayerMask.NameToLayer("Weapon");
			}
		}
		if (this.boatSimulator.isKayak)
		{
			this.currentHands.kayakHandsAnimation.parent = this.currentHands.kayakHandsParent.transform;
			this.currentHands.kayakHandsParent.SetActive(false);
			if (this.boatSimulator.paddles)
			{
				this.boatSimulator.paddles.transform.parent.gameObject.SetActive(true);
			}
		}
		if (!VRManager.IsVROn())
		{
			this.ufpsCamera.enabled = true;
		}
		if (this.boatSimulator.fishingPosition)
		{
			this.SetPosition(this.boatSimulator.fishingPosition.position);
			this.SetRotation(this.boatSimulator.fishingPosition.eulerAngles);
		}
		this.gameController.hudManager.ShowRadar(false);
		yield return null;
		this.gameController.weatherLevelManager.UpdateWaterProfiles();
		yield break;
	}

	// Token: 0x06002B87 RID: 11143 RVA: 0x000FF16C File Offset: 0x000FD36C
	public void SetPosition(Vector3 newPosition)
	{
		this.ufpsController.SetPosition(newPosition);
		this.ufpsController.Stop();
		if (this.currentHands && !this.currentHands.baitWasThrown)
		{
			if (this.currentHands.fishingFloat)
			{
				this.currentHands.fishingFloat.ResetFloat();
			}
			this.currentHands.bait.ResetBait();
		}
	}

	// Token: 0x06002B88 RID: 11144 RVA: 0x000FF1E8 File Offset: 0x000FD3E8
	public IEnumerator SetPositionFade(Vector3 newPosition, float fadeDuration)
	{
		if (!HUDManager.Instance)
		{
			this.SetPosition(newPosition);
			yield break;
		}
		HUDManager.Instance.FadeDark(1f, fadeDuration, false);
		yield return new WaitForSeconds(fadeDuration);
		this.SetPosition(newPosition);
		yield return new WaitForSeconds(0.05f);
		HUDManager.Instance.FadeDark(0f, fadeDuration, false);
		yield break;
	}

	// Token: 0x06002B89 RID: 11145 RVA: 0x000FF211 File Offset: 0x000FD411
	public void SetRotation(Vector3 eulerAngles)
	{
		if (this.ufpsCamera.enabled)
		{
			this.ufpsCamera.SetRotation(eulerAngles);
		}
		else
		{
			this.transform.eulerAngles = eulerAngles;
		}
	}

	// Token: 0x06002B8A RID: 11146 RVA: 0x000FF248 File Offset: 0x000FD448
	public IEnumerator SetRotationFade(Vector3 eulerAngles, float fadeDuration)
	{
		if (!HUDManager.Instance)
		{
			this.SetRotation(eulerAngles);
			yield break;
		}
		HUDManager.Instance.FadeDark(1f, fadeDuration, false);
		yield return new WaitForSeconds(fadeDuration);
		this.SetRotation(eulerAngles);
		yield return new WaitForSeconds(0.05f);
		HUDManager.Instance.FadeDark(0f, fadeDuration, false);
		yield break;
	}

	// Token: 0x06002B8B RID: 11147 RVA: 0x000FF271 File Offset: 0x000FD471
	public void SetPositionAndRotation(Vector3 newPosition, Vector3 eulerAngles)
	{
		this.SetPosition(newPosition);
		this.SetRotation(eulerAngles);
	}

	// Token: 0x06002B8C RID: 11148 RVA: 0x000FF284 File Offset: 0x000FD484
	public IEnumerator SetPositionAndRotationFade(Vector3 newPosition, Vector3 eulerAngles, float fadeDuration)
	{
		if (!HUDManager.Instance)
		{
			this.SetPosition(newPosition);
			yield break;
		}
		HUDManager.Instance.FadeDark(1f, fadeDuration, false);
		yield return new WaitForSeconds(fadeDuration);
		this.SetPosition(newPosition);
		this.SetRotation(eulerAngles);
		yield return new WaitForSeconds(0.05f);
		HUDManager.Instance.FadeDark(0f, fadeDuration, false);
		yield break;
	}

	// Token: 0x06002B8D RID: 11149 RVA: 0x000FF2B4 File Offset: 0x000FD4B4
	public void CameraLookAt(Vector3 goal, Vector3 offset, float tweenTime)
	{
		if (VRManager.IsVROn())
		{
			return;
		}
		this.ufpsCamera.SetRotation(this.ufpsCamera.Transform.eulerAngles, false);
		Quaternion quaternion = Quaternion.LookRotation(goal + offset - this.ufpsCamera.transform.position);
		Vector2 vector = new Vector2(quaternion.eulerAngles.x, quaternion.eulerAngles.y);
		if (vector.x > 180f)
		{
			vector.x -= 360f;
		}
		if (tweenTime == 0f)
		{
			this.ufpsCamera.Angle = vector;
		}
		else
		{
			LeanTween.value(base.gameObject, delegate(Vector2 v)
			{
				this.ufpsCamera.Angle = v;
			}, this.ufpsCamera.Angle, vector, tweenTime).setEase(LeanTweenType.easeInOutQuad);
		}
	}

	// Token: 0x06002B8E RID: 11150 RVA: 0x000FF39F File Offset: 0x000FD59F
	public void CameraLookAt(Transform goal, Vector3 offset, float tweenTime)
	{
		this.CameraLookAt(goal.position, offset, tweenTime);
	}

	// Token: 0x06002B8F RID: 11151 RVA: 0x00007702 File Offset: 0x00005902
	public void SetHorizontalRotationLimit(float left, float right)
	{
	}

	// Token: 0x06002B90 RID: 11152 RVA: 0x000FF3B0 File Offset: 0x000FD5B0
	public void BlockMouseLook(bool block, float normalSensitivity = 4f)
	{
		this.ufpsInput.MouseLookSensitivity = new Vector2((!block) ? normalSensitivity : 0f, (!block) ? normalSensitivity : 0f);
		this.CurrentMouseLookSensitivity = this.ufpsInput.MouseLookSensitivity;
		this.RefreshInputSettings();
	}

	// Token: 0x06002B91 RID: 11153 RVA: 0x000FF406 File Offset: 0x000FD606
	public void BlockVerticalMouseLook(bool block, float normalSensitivity = 4f)
	{
		this.ufpsInput.MouseLookSensitivity = new Vector2(normalSensitivity, (!block) ? normalSensitivity : 0f);
		this.CurrentMouseLookSensitivity = this.ufpsInput.MouseLookSensitivity;
		this.RefreshInputSettings();
	}

	// Token: 0x06002B92 RID: 11154 RVA: 0x000FF444 File Offset: 0x000FD644
	public void RefreshInputSettings()
	{
		this.ufpsInput.MouseLookSensitivity = this.CurrentMouseLookSensitivity;
		if (this.underwaterCamera.isTurnedOn)
		{
			this.ufpsInput.MouseLookSensitivity *= 0.4f;
		}
		if (GlobalSettings.Instance)
		{
			this.ufpsInput.MouseLookSensitivity *= GlobalSettings.Instance.playerSettings.mouseSensitivity;
			this.ufpsInput.MouseLookInvert = GlobalSettings.Instance.playerSettings.invertYAxis < 0f;
		}
	}

	// Token: 0x06002B93 RID: 11155 RVA: 0x000FF4E2 File Offset: 0x000FD6E2
	public void AllowInteraction(bool allow)
	{
		this.ufpsInteractiveManage.MaxInteractDistance = ((!allow) ? (-1f) : 25f);
	}

	// Token: 0x06002B94 RID: 11156 RVA: 0x000FF504 File Offset: 0x000FD704
	public void Zoom(bool zoom, float duration, float zoomValue = 0.75f)
	{
		if ((zoom && this.ufpsWeaponCamera.fieldOfView < this.CurrentFieldOfView * this.ZoomFieldOfView) || (!zoom && this.ufpsWeaponCamera.fieldOfView == this.CurrentFieldOfView))
		{
			return;
		}
		LeanTween.value(base.gameObject, new Action<float>(this.UpdateZoom), (!zoom) ? zoomValue : 1f, (!zoom) ? 1f : zoomValue, duration);
	}

	// Token: 0x06002B95 RID: 11157 RVA: 0x000FF58C File Offset: 0x000FD78C
	public void UpdateZoom(float zoomValue)
	{
		this.ZoomFieldOfView = zoomValue;
	}

	// Token: 0x06002B96 RID: 11158 RVA: 0x000FF595 File Offset: 0x000FD795
	[Button]
	public void LeanForwardTest()
	{
		this.LeanForward(global::UnityEngine.Random.Range(0.1f, 1.5f));
	}

	// Token: 0x06002B97 RID: 11159 RVA: 0x000FF5AC File Offset: 0x000FD7AC
	public void LeanForward(float distance = 0.1f)
	{
		if (VRManager.IsVROn())
		{
			return;
		}
		if (this.ufpsCamera.PositionOffset.z == distance)
		{
			return;
		}
		Debug.Log("LeanForward: " + distance);
		this.ufpsCamera.PositionOffset.z = distance;
		this.ufpsCamera.Refresh();
	}

	// Token: 0x06002B98 RID: 11160 RVA: 0x000FF60C File Offset: 0x000FD80C
	public bool CanChangeEquipment()
	{
		if (this.underwaterCamera.isTurnedOn || this.underwaterCamera.isChanging)
		{
			return false;
		}
		if (this.gameController.iceLevel)
		{
			return this.currentState == FishingPlayer.PlayerState.NORMAL || (this.currentState == FishingPlayer.PlayerState.ICE_FISHING && !this.fish);
		}
		return this.currentState == FishingPlayer.PlayerState.NORMAL || (this.currentState == FishingPlayer.PlayerState.FISHING && !this.currentHands.baitWasThrown && !this.currentHands.isThrowingNear && !this.currentHands.isThrowing);
	}

	// Token: 0x06002B99 RID: 11161 RVA: 0x000FF6C4 File Offset: 0x000FD8C4
	public void ShowRod(bool show)
	{
		if (show)
		{
			LeanTween.rotateLocal(this.currentHands.rightArm, new Vector3(0f, 0f, 0f), 1f);
			AudioController.Play("ArmShow_01", this.transform);
		}
		else
		{
			LeanTween.rotateLocal(this.currentHands.rightArm, new Vector3(0f, -90f, 40f), 1f);
			AudioController.Play("ArmHide_01", this.transform);
		}
	}

	// Token: 0x06002B9A RID: 11162 RVA: 0x000FF754 File Offset: 0x000FD954
	public void SitDown(float sitDownHeight, bool drilling = true)
	{
		Debug.Log("SitDown: " + ((sitDownHeight <= 0f) ? this.playerHeight : sitDownHeight));
		if (sitDownHeight > 0f && drilling)
		{
			float num = this.defaultHoleDistance + this.currentHands.fishingRod.rodLength;
			float num2 = Vector3.Distance(this.transform.position, this.drillingController.currentHole.holeCenter.position);
			Vector3 vector = this.drillingController.currentHole.holeCenter.position + (this.transform.position - this.drillingController.currentHole.holeCenter.position).normalized * num;
			vector.y = this.transform.position.y;
			Debug.Log(string.Concat(new object[]
			{
				"Ice Sit currentDistance: ",
				num2,
				" newDistance: ",
				num,
				" difference: ",
				num2 - num
			}));
			if (!VRManager.IsVROn())
			{
				LeanTween.move(base.gameObject, vector, 0.3f).setOnComplete(delegate
				{
					LeanTween.delayedCall(0.3f, delegate
					{
					});
					this.CameraLookAt(this.drillingController.currentHole.transform, new Vector3(0f, this.fishingController.iceFishingLookHeight, 0f), 0.3f);
				});
			}
		}
		else
		{
			this.ufpsCamera.PositionOffset = new Vector3(this.ufpsCamera.PositionOffset.x, (sitDownHeight <= 0f) ? this.playerHeight : sitDownHeight, this.ufpsCamera.PositionOffset.z);
			this.ufpsCamera.Refresh();
		}
		if (VRManager.IsVROn())
		{
			this.ChangeVRCameraHeight(VRManager.Instance.cameraHeight);
		}
		this.isDuringAnimation = true;
		LeanTween.delayedCall(1.5f, delegate
		{
			this.isDuringAnimation = false;
			if (sitDownHeight < 0f && drilling && this.gameController.iceLevel)
			{
				this.currentHands.ShowWalkingDriller(true);
			}
		});
	}

	// Token: 0x06002B9B RID: 11163 RVA: 0x000FF97F File Offset: 0x000FDB7F
	public void ShowHands(bool show)
	{
		if (this.currentHands)
		{
			this.currentHands.gameObject.SetActive(show);
		}
	}

	// Token: 0x06002B9C RID: 11164 RVA: 0x000FF9A4 File Offset: 0x000FDBA4
	public void TurnOnSpring(bool turnOn, float value = -1f)
	{
		if (VRManager.IsVROn())
		{
			return;
		}
		this.ufpsWeapon.RotationLookSway = ((!turnOn) ? Vector3.zero : new Vector3(1f, 1f, -2.5f));
		this.ufpsWeapon.Refresh();
	}

	// Token: 0x06002B9D RID: 11165 RVA: 0x000FF9F6 File Offset: 0x000FDBF6
	public void EnterChat(bool enter)
	{
		if (enter)
		{
			this.ufpsController.Stop();
		}
	}

	// Token: 0x06002B9E RID: 11166 RVA: 0x000FFA0C File Offset: 0x000FDC0C
	public void WaterSplash(float strength, float delay = 0f)
	{
		if (GlobalSettings.Instance && !GlobalSettings.Instance.renderSettings.GetQualityDefinition().useCameraWaterDrops)
		{
			return;
		}
		if (this.waterDropsRainMy)
		{
			this.waterDropsRainMy.SpawnRandom((int)Mathf.Lerp(10f, 30f, strength));
			AudioController.Play("SplashCamera_01");
		}
	}

	// Token: 0x06002B9F RID: 11167 RVA: 0x000FFA79 File Offset: 0x000FDC79
	public float GetFishDistanceAnimation()
	{
		if (GlobalSettings.Instance)
		{
			return this.fishDistanceAnimation * GlobalSettings.Instance.renderSettings.GetQualityDefinition().fishDistanceAnimationMultiplier;
		}
		return this.fishDistanceAnimation;
	}

	// Token: 0x06002BA0 RID: 11168 RVA: 0x000FFAAC File Offset: 0x000FDCAC
	public void CalculateFishDistanceBehavior()
	{
		if (this.fish)
		{
			this.currentFishDistanceBehavior = 30f;
		}
		else if (this.underwaterCamera.isTurnedOn)
		{
			this.currentFishDistanceBehavior = 30f;
		}
		else if (this.currentHands.bait.isOnWater)
		{
			this.currentFishDistanceBehavior = 30f;
		}
		else if (this.currentHands.baitWasThrown)
		{
			this.currentFishDistanceBehavior = 60f;
		}
		else if (GlobalSettings.Instance)
		{
			this.currentFishDistanceBehavior = this.fishDistanceBehavior * GlobalSettings.Instance.renderSettings.GetQualityDefinition().fishDistanceBehaviorMultiplier;
		}
		else
		{
			this.currentFishDistanceBehavior = this.fishDistanceBehavior;
		}
		if (this.fishDistanceBehaviorSphere.gameObject.activeSelf)
		{
			this.fishDistanceBehaviorSphere.localScale = Vector3.one * this.currentFishDistanceBehavior * 2f;
			this.fishDistanceBehaviorSphere.parent = ((!this.underwaterCamera.isTurnedOn) ? this.transform : this.underwaterCamera.transform);
			this.fishDistanceBehaviorSphere.localPosition = Vector3.zero;
			this.fishDistanceBehaviorSphere.localRotation = Quaternion.identity;
		}
	}

	// Token: 0x06002BA1 RID: 11169 RVA: 0x000FD002 File Offset: 0x000FB202
	public bool HasFishOnCurrentRod()
	{
		return this.fish && this.fish.storedBait.fishingHands == this.currentHands;
	}

	// Token: 0x06002BA2 RID: 11170 RVA: 0x000FFC0C File Offset: 0x000FDE0C
	public IEnumerator Die()
	{
		if (this.junk)
		{
			this.gameController.hudManager.deathInfoBtn.text = "Pay -" + this.junk.cost + " $";
		}
		this.currentHands.animator.SetBool("WatchFish", false);
		this.currentHands.watchFishLine.gameObject.SetActive(false);
		this.ChangeState(FishingPlayer.PlayerState.DEATH);
		AudioController.Play("Death_01", this.transform);
		this.CameraLookAt(this.transform.position + this.transform.forward * 1f, new Vector3(0f, 4f, 0f), 0.7f);
		this.SitDown(0.2f, false);
		this.fxBlood1.enabled = true;
		this.gaussianBlur.enabled = true;
		this.gameController.hudManager.ChangeGameState(HUDManager.GameState.EMPTY);
		yield return new WaitForSeconds(2.5f);
		this.gameController.hudManager.ChangeGameState(HUDManager.GameState.DEATH);
		yield break;
	}

	// Token: 0x06002BA3 RID: 11171 RVA: 0x000FFC27 File Offset: 0x000FDE27
	public void RecoverAfterDeath()
	{
		if (GlobalSettings.Instance)
		{
			GlobalSettings.Instance.playerSettings.AddMoney(-(int)this.junk.cost);
		}
		base.StartCoroutine(this.gameController.ResetPlayer(true));
	}

	// Token: 0x06002BA4 RID: 11172 RVA: 0x000FFC68 File Offset: 0x000FDE68
	public IEnumerator PooPissLimit(bool poo)
	{
		this.gameController.hudManager.FadeDark(1f, 0.5f, false);
		yield return new WaitForSeconds(1.5f);
		base.StartCoroutine(this.gameController.ResetPlayer(false));
		this.gameController.hudManager.ChangeGameState(HUDManager.GameState.EMPTY);
		this.gameController.hudManager.pooPissInfo.text = string.Concat(new object[]
		{
			global::Utilities.GetTranslation("You didn't piss on time.\nBill from laundry", false),
			": -",
			50f,
			" $"
		});
		this.ufpsInput.AllowGameplayInput = false;
		yield return new WaitForSeconds(1f);
		this.gameController.hudManager.ChangeGameState(HUDManager.GameState.AFTER_POO_PISS);
		this.gameController.hudManager.FadeDark(0f, 0.5f, false);
		yield break;
	}

	// Token: 0x06002BA5 RID: 11173 RVA: 0x000FFC83 File Offset: 0x000FDE83
	public void UpdateParams()
	{
		this.currentStrengthLevel = 1f;
	}

	// Token: 0x06002BA6 RID: 11174 RVA: 0x000FFC90 File Offset: 0x000FDE90
	public float GetCurrentStrength()
	{
		return 1f;
	}

	// Token: 0x06002BA7 RID: 11175 RVA: 0x00007702 File Offset: 0x00005902
	public void AddStrength(float value)
	{
	}

	// Token: 0x06002BA8 RID: 11176 RVA: 0x00007702 File Offset: 0x00005902
	public void CheckStrength()
	{
	}

	// Token: 0x06002BA9 RID: 11177 RVA: 0x000FFC98 File Offset: 0x000FDE98
	public void SetDrunkLevel(float level, bool instant)
	{
		if (this.fxDrunk)
		{
			this.fxDrunk.enabled = level > 0f;
		}
		if (instant)
		{
			this.SetDrunkSettings(level, true);
		}
		else
		{
			LeanTween.delayedCall(base.gameObject, 2f, delegate
			{
				this.SetDrunkSettings(level, true);
			});
			LeanTween.value(base.gameObject, this.drunkLevel, level, 2f).setOnUpdate(delegate(float value)
			{
				this.SetDrunkSettings(value, false);
			});
		}
	}

	// Token: 0x06002BAA RID: 11178 RVA: 0x000FFD44 File Offset: 0x000FDF44
	private void SetDrunkSettings(float value, bool instant)
	{
		float num = value * 0.5f;
		if (this.fxDrunk)
		{
			this.fxDrunk.Wavy = num * 0.8f;
			this.fxDrunk.DistortionWave = num * 0.02f;
		}
		this.ufpsCamera.RenderingFieldOfView = 60f + num * num * 40f;
		if (instant)
		{
			this.ufpsCamera.ShakeSpeed = 0.15f + num * 0.15f;
			this.ufpsCamera.ShakeAmplitude = new Vector3(10f + num * 70f, 10f + num * 70f, 0f);
		}
		this.ufpsCamera.BobAmplitude = new Vector4(num, 1f + num, num, 1f + num);
		this.ufpsController.MotorAcceleration = this.currentMotorAcceleration - num * 0.15f;
		this.ufpsCamera.RotationStrafeRoll = 0.01f - num * 2f;
		this.ufpsCamera.RefreshZoom();
		this.drunkLevel = value;
	}

	// Token: 0x06002BAB RID: 11179 RVA: 0x000FFE58 File Offset: 0x000FE058
	public IEnumerator HunterVision()
	{
		if (this.isHunterVisionOn)
		{
			yield break;
		}
		this.isHunterVisionOn = true;
		float duration = 0f;
		if (GlobalSettings.Instance)
		{
			if (GlobalSettings.Instance.skillsManager.GetSkill(SkillsManager.SkillType.HUNTER_VISION_1).isUnlocked)
			{
				duration = GlobalSettings.Instance.skillsManager.GetSkill(SkillsManager.SkillType.HUNTER_VISION_1).param_1;
			}
			if (GlobalSettings.Instance.skillsManager.GetSkill(SkillsManager.SkillType.HUNTER_VISION_2).isUnlocked)
			{
				duration += GlobalSettings.Instance.skillsManager.GetSkill(SkillsManager.SkillType.HUNTER_VISION_2).param_1;
			}
			if (GlobalSettings.Instance.skillsManager.GetSkill(SkillsManager.SkillType.HUNTER_VISION_3).isUnlocked)
			{
				duration += GlobalSettings.Instance.skillsManager.GetSkill(SkillsManager.SkillType.HUNTER_VISION_3).param_1;
			}
		}
		else
		{
			duration = 3f;
		}
		float tweenTime = 2f;
		this.grayscale.Amount = 0f;
		this.grayscale.enabled = true;
		this.gameController.UpdateHunterFishMaterials(true, tweenTime);
		this.hunterAudio = AudioController.Play("HunterVision_01", this.transform);
		if (this.hunterAudio)
		{
			this.hunterAudio.volume = 0f;
		}
		LeanTween.value(0f, 1f, tweenTime).setOnUpdate(delegate(float value)
		{
			this.grayscale.Amount = value;
			if (this.hunterAudio)
			{
				this.hunterAudio.volume = value * this.hunterAudio.audioItem.Volume * this.hunterAudio.subItem.Volume * this.hunterAudio.category.VolumeTotal;
			}
		});
		this.gameController.hudManager.UpdateControls();
		yield return new WaitForSeconds(tweenTime);
		yield return new WaitForSeconds(duration);
		LeanTween.value(1f, 0f, tweenTime).setOnUpdate(delegate(float value)
		{
			this.grayscale.Amount = value;
			if (this.hunterAudio)
			{
				this.hunterAudio.volume = value * this.hunterAudio.audioItem.Volume * this.hunterAudio.subItem.Volume * this.hunterAudio.category.VolumeTotal;
			}
		});
		this.gameController.UpdateHunterFishMaterials(false, tweenTime);
		yield return new WaitForSeconds(tweenTime);
		this.grayscale.Amount = 0f;
		this.grayscale.enabled = false;
		if (this.hunterAudio)
		{
			this.hunterAudio.Stop();
		}
		this.hunterAudio = null;
		this.gameController.hudManager.UpdateControls();
		if (GlobalSettings.Instance)
		{
			yield return new WaitForSeconds(12f);
		}
		this.isHunterVisionOn = false;
		this.gameController.hudManager.UpdateControls();
		yield break;
	}

	// Token: 0x06002BAC RID: 11180 RVA: 0x000FFE74 File Offset: 0x000FE074
	public void QuickStopHunterVision()
	{
		this.gameController.UpdateHunterFishMaterials(false, 0f);
		this.gameController.ResetHunterFishMaterials();
		this.grayscale.Amount = 0f;
		this.grayscale.enabled = false;
		if (this.hunterAudio)
		{
			this.hunterAudio.Stop();
		}
		this.hunterAudio = null;
		this.isHunterVisionOn = false;
		this.gameController.hudManager.UpdateControls();
	}

	// Token: 0x06002BAD RID: 11181 RVA: 0x000FFEF4 File Offset: 0x000FE0F4
	public void UpdateFOV(bool force)
	{
		if (VRManager.IsVROn())
		{
			return;
		}
		if (this.currentHands.transform.parent == null)
		{
			return;
		}
		if (this.ufpsCameraCamera.fieldOfView != this.ufpsWeaponCamera.fieldOfView || force)
		{
		}
		if (this.postProcessingBehaviour_1 && this.postProcessingBehaviour_1.profile.antialiasing.settings.method != AntialiasingModel.Method.Taa)
		{
			Debug.LogWarning("Wrong Antialiasing Method");
		}
		if (this.gameController.iceLevel)
		{
			return;
		}
		Vector3 localPosition = this.currentHands.transform.parent.localPosition;
		if (this.currentState == FishingPlayer.PlayerState.WATCH_FISH)
		{
			localPosition.z = Mathf.Lerp(this.currentHands.handsFovPositionWatch.x, this.currentHands.handsFovPositionWatch.y, Mathf.InverseLerp(50f, 100f, this.CurrentFieldOfView));
		}
		else if (this.currentHands.baitWasThrown)
		{
			localPosition.z = Mathf.Lerp(this.currentHands.handsFovPositionFishing.x, this.currentHands.handsFovPositionFishing.y, Mathf.InverseLerp(50f, 100f, this.CurrentFieldOfView));
		}
		else
		{
			localPosition.z = Mathf.Lerp(this.currentHands.handsFovPosition.x, this.currentHands.handsFovPosition.y, Mathf.InverseLerp(50f, 100f, this.CurrentFieldOfView));
		}
		this.currentHands.transform.parent.localPosition = localPosition;
	}

	// Token: 0x06002BAE RID: 11182 RVA: 0x001000AC File Offset: 0x000FE2AC
	public void CheckVRCameraHeight()
	{
		if (this.vrWasCameraUnderwater)
		{
			this.vrWasCameraUnderwater = false;
			Transform transform = this.ovrPlayerController.transform;
			Vector3 vector = new Vector3(0f, VRManager.Instance.cameraHeight, 0f);
			this.ufpsCamera.transform.parent.localPosition = vector;
			transform.localPosition = vector;
		}
		if (this.ufpsCamera.transform.position.y < this.vrMinCameraHeight)
		{
			float num = this.ufpsCamera.transform.position.y - this.vrMinCameraHeight;
			Transform transform2 = this.ovrPlayerController.transform;
			Vector3 vector2 = new Vector3(0f, this.ufpsCamera.transform.parent.localPosition.y - num, 0f);
			this.ufpsCamera.transform.parent.localPosition = vector2;
			transform2.localPosition = vector2;
			this.vrWasCameraUnderwater = true;
		}
	}

	// Token: 0x06002BAF RID: 11183 RVA: 0x001001B0 File Offset: 0x000FE3B0
	public void ChangeVRCameraHeight(Vector3 cameraPos)
	{
		Transform transform = this.ovrPlayerController.transform;
		this.ufpsCamera.transform.parent.localPosition = cameraPos;
		transform.localPosition = cameraPos;
	}

	// Token: 0x06002BB0 RID: 11184 RVA: 0x001001E8 File Offset: 0x000FE3E8
	public void ChangeVRCameraHeight(float cameraHeight)
	{
		if (!VRManager.IsVROn())
		{
			return;
		}
		float num = cameraHeight + this.ufpsCamera.PositionOffset.y - this.playerHeight;
		float num2 = this.ufpsCamera.transform.position.y - this.transform.position.y;
		if (num2 < 1.3f)
		{
		}
		Transform transform = this.ovrPlayerController.transform;
		Vector3 vector = new Vector3(0f, num, 0f);
		this.ufpsCamera.transform.parent.localPosition = vector;
		transform.localPosition = vector;
	}

	// Token: 0x06002BB1 RID: 11185 RVA: 0x00100290 File Offset: 0x000FE490
	public IEnumerator ChangeEquipmentSet(int setId)
	{
		if (!GlobalSettings.Instance)
		{
			yield break;
		}
		if (setId == GlobalSettings.Instance.equipmentManager.currentEquipmentSetId)
		{
			yield break;
		}
		if (!this.CanChangeEquipment() && (!this.currentHands.fishingRod || !this.currentHands.fishingRod.isOnRodStand))
		{
			yield break;
		}
		if (this.IsSetOnStand(setId))
		{
			yield break;
		}
		Debug.Log("ChangeEquipmentSet " + setId);
		this.gameController.hudManager.FadeDark(1f, 0.2f, false);
		yield return new WaitForSeconds(0.4f);
		GlobalSettings.Instance.equipmentManager.ChangeCurrentEquipmentSet(setId);
		this.ChangeHands(setId);
		this.currentHands.equipmentSetId = setId;
		this.ResetFishing(true);
		GlobalSettings.Instance.equipmentManager.EquipmentChanged(EquipmentObject.EquipmentType.COUNT);
		yield return null;
		yield return null;
		base.StartCoroutine(this.currentHands.ChangeEquipment());
		this.gameController.hudManager.FadeDark(0f, 0.3f, false);
		yield break;
	}

	// Token: 0x06002BB2 RID: 11186 RVA: 0x001002B4 File Offset: 0x000FE4B4
	public bool IsSetOnStand(int setId)
	{
		FishingHands fishingHands = this.allFishingHands[setId];
		return fishingHands.fishingRod && fishingHands.fishingRod.isOnRodStand;
	}

	// Token: 0x06002BB3 RID: 11187 RVA: 0x001002F1 File Offset: 0x000FE4F1
	public bool IsCurrentSetOnStand()
	{
		return this.IsSetOnStand(GlobalSettings.Instance.equipmentManager.currentEquipmentSetId);
	}

	// Token: 0x06002BB4 RID: 11188 RVA: 0x00100308 File Offset: 0x000FE508
	public void ChangeFreeCamera(bool turnOn)
	{
		this.freeCamera.enabled = turnOn;
		this.characteController.enabled = !turnOn;
		this.ufpsController.enabled = !turnOn;
		this.ufpsInput.enabled = !turnOn;
		if (!VRManager.IsVROn())
		{
			this.ufpsCamera.enabled = !turnOn;
		}
		this.currentHands.gameObject.SetActive(!turnOn);
		this.ufpsCamera.GetComponent<UnderwaterIME>().EffectEnabled = turnOn;
		base.enabled = !turnOn;
		if (turnOn)
		{
			this.currentHands.ShowRopes(false);
		}
		else
		{
			this.currentHands.ShowRopes(true);
			base.StartCoroutine(this.gameController.ResetPlayer(false));
		}
	}

	// Token: 0x06002BB5 RID: 11189 RVA: 0x001003CC File Offset: 0x000FE5CC
	[Button]
	public void MoveToSpawnPoint()
	{
		GameObject gameObject = MultiTags.FindGameObjectsWithMultiTag("SPAWN_POINT")[0];
		this.transform.position = gameObject.transform.position;
		this.transform.rotation = gameObject.transform.rotation;
	}

	// Token: 0x06002BB6 RID: 11190 RVA: 0x00100412 File Offset: 0x000FE612
	[Button]
	public void SitDownKayak()
	{
		this.SitDown(1.4f, false);
	}

	// Token: 0x06002BB7 RID: 11191 RVA: 0x00100420 File Offset: 0x000FE620
	public bool IsTrolling()
	{
		return this.currentState == FishingPlayer.PlayerState.DRIVING_BOAT || (this.boatSimulator && this.boatSimulator.keepMovingForward);
	}

	// Token: 0x06002BB8 RID: 11192 RVA: 0x00100454 File Offset: 0x000FE654
	public void UpdateVRHands()
	{
		if (VRManager.IsVROn())
		{
			if (VRManager.Instance.isOculusDashboardOn)
			{
				return;
			}
			if (HUDManager.Instance.currentHudState == HUDManager.HUDState.PAUSE)
			{
				return;
			}
			if (this.vrHandsParent.parent == this.transform && VRManager.Instance.IsVRHoldRod() && VRControllersManager.Instance.GetVRController(VRControllersManager.Instance.GetPrimaryController()))
			{
				if (this.currentHands.fishingRod && this.currentHands.fishingRod.isOnRodStand)
				{
					return;
				}
				if (this.vrChangeRopeParent)
				{
					this.currentHands.currentRope.transform.parent = null;
				}
				this.vrHandsParent.parent = VRControllersManager.Instance.GetVRController(VRControllersManager.Instance.GetPrimaryController()).rodHoldTransform;
				this.vrHandsParent.localScale = Vector3.one * this.vrHandsScale;
				this.vrHandsParent.localPosition = new Vector3(0.134f, 0.376f, 0.128f);
				this.vrHandsParent.localEulerAngles = new Vector3(20f, 180f, 0f);
				this.vrHandsParent.parent = this.transform;
				if (this.vrChangeRopeParent)
				{
					this.currentHands.currentRope.transform.parent = this.currentHands.transform;
					if (this.vrUpdateRopePosition)
					{
						this.currentHands.currentRope.transform.position = this.currentHands.fishingRod.rodMegaAttachPosition.position;
					}
				}
				if (!GameController.Instance.iceLevel)
				{
					this.currentHands.fishingRod.transform.localScale = new Vector3((!VRControllersManager.Instance.IsLeftHanded()) ? this.currentHands.fishingRod.transform.localScale.y : (-this.currentHands.fishingRod.transform.localScale.y), this.currentHands.fishingRod.transform.localScale.y, this.currentHands.fishingRod.transform.localScale.z);
					this.currentHands.fishingRod.reelMountPosition.localScale = new Vector3(1f, 1f, (!VRControllersManager.Instance.IsLeftHanded()) ? 1f : (-1f));
				}
			}
			else if (this.vrHandsParent.parent != this.transform && !VRManager.Instance.vrHoldRod)
			{
				this.vrHandsParent.parent = this.transform;
				this.vrHandsParent.localScale = Vector3.one;
				this.vrHandsParent.localPosition = new Vector3(0f, 1.5f, 0f);
				this.vrHandsParent.localEulerAngles = new Vector3(0f, 0f, 0f);
			}
			else if (!VRManager.Instance.IsControllersInput())
			{
				this.vrHandsParent.localScale = Vector3.one * this.vrHandsScale;
				if (this.gameController.iceLevel)
				{
					if (this.currentState == FishingPlayer.PlayerState.ICE_FISHING)
					{
						this.vrHandsParent.localPosition = new Vector3(0f, 1f, -0.35f);
					}
					else
					{
						this.vrHandsParent.localPosition = new Vector3(0f, 1.5f, 0f);
					}
				}
				else
				{
					this.vrHandsParent.localPosition = new Vector3(0f, 1.5f, -0.1f);
				}
				this.vrHandsParent.position = new Vector3(this.ufpsCamera.transform.position.x, this.vrHandsParent.position.y, this.ufpsCamera.transform.position.z);
			}
		}
		this.currentHands.currentRope.transform.localScale = Vector3.one;
	}

	// Token: 0x06002BB9 RID: 11193 RVA: 0x001008B8 File Offset: 0x000FEAB8
	public void UpdateVRHUD()
	{
		if (VRManager.IsVROn() && this.ufpsCamera)
		{
			HUDManager.Instance.transform.localPosition = this.vrHUDOffset;
			this.vrHUDParent.position = this.ufpsCamera.transform.position;
			if (VRManager.Instance.hudRotateStyle != VRManager.HUDRotateStyle.NONE)
			{
				if (VRManager.Instance.hudRotateStyle == VRManager.HUDRotateStyle.FREE)
				{
					this.vrHUDParent.eulerAngles = new Vector3(0f, this.ufpsCamera.transform.eulerAngles.y, 0f);
				}
				else if (VRManager.Instance.hudRotateStyle == VRManager.HUDRotateStyle.STEP && this.vrHUDRotationTween == null)
				{
					float num = Mathf.Abs(Mathf.DeltaAngle(this.vrHUDParent.eulerAngles.y, this.ufpsCamera.transform.eulerAngles.y));
					this.vrHUDRotationThreshold = Mathf.Lerp(50f, 70f, Mathf.InverseLerp(VRManager.Instance.hudSizeMinMax.x, VRManager.Instance.hudSizeMinMax.y, VRManager.Instance.hudSize));
					if (num > this.vrHUDRotationThreshold || this.vrHUDFreeIsRotating)
					{
						this.vrHUDParent.rotation = Quaternion.RotateTowards(this.vrHUDParent.rotation, this.ufpsCamera.transform.rotation, this.vrHUDRotationSpeed * num * Time.deltaTime);
						this.vrHUDParent.eulerAngles = new Vector3(0f, this.vrHUDParent.eulerAngles.y, 0f);
						this.vrHUDFreeIsRotating = true;
						if (num < 10f)
						{
							this.vrHUDFreeIsRotating = false;
						}
					}
				}
			}
		}
	}

	// Token: 0x06002BBA RID: 11194 RVA: 0x00100A94 File Offset: 0x000FEC94
	[Button]
	public void GetForwardVectorTEST()
	{
		Debug.LogError("GetForwardVectorTEST: " + this.GetForwardVector());
		Debug.DrawLine(this.ufpsCamera.transform.position, this.ufpsCamera.transform.position + this.GetForwardVector(), Color.red, 8f);
	}

	// Token: 0x06002BBB RID: 11195 RVA: 0x00100AF8 File Offset: 0x000FECF8
	public Vector3 GetForwardVector()
	{
		if (VRManager.IsVROn())
		{
			Vector3 vector = new Vector3(this.ufpsCamera.transform.forward.x, 0f, this.ufpsCamera.transform.forward.z);
			return vector.normalized;
		}
		return this.transform.forward;
	}

	// Token: 0x06002BBC RID: 11196 RVA: 0x00100B60 File Offset: 0x000FED60
	public void UpdateRoomscale()
	{
	}

	// Token: 0x040030BC RID: 12476
	[HideInInspector]
	public CharacterController characteController;

	// Token: 0x040030BD RID: 12477
	[HideInInspector]
	public vp_FPController ufpsController;

	// Token: 0x040030BE RID: 12478
	[HideInInspector]
	public vp_FPInput ufpsInput;

	// Token: 0x040030BF RID: 12479
	[HideInInspector]
	public vp_FPWeaponHandler ufpsWeaponHandler;

	// Token: 0x040030C0 RID: 12480
	[HideInInspector]
	public vp_FPWeapon ufpsWeapon;

	// Token: 0x040030C1 RID: 12481
	[HideInInspector]
	public vp_FPPlayerEventHandler ufpsPlayerEventHandler;

	// Token: 0x040030C2 RID: 12482
	[HideInInspector]
	public vp_SimpleCrosshair ufpsSimpleCrosshair;

	// Token: 0x040030C3 RID: 12483
	[HideInInspector]
	public vp_FPCamera ufpsCamera;

	// Token: 0x040030C4 RID: 12484
	[HideInInspector]
	public Camera ufpsCameraCamera;

	// Token: 0x040030C5 RID: 12485
	[HideInInspector]
	public vp_FPInteractManager ufpsInteractiveManage;

	// Token: 0x040030C6 RID: 12486
	[HideInInspector]
	public vp_FootstepManager ufpsFootstepManager;

	// Token: 0x040030C7 RID: 12487
	public Camera ufpsWeaponCamera;

	// Token: 0x040030C8 RID: 12488
	public MaskCamera maskCamera;

	// Token: 0x040030C9 RID: 12489
	public vp_FPBodyAnimator ufpsBodyAnimator;

	// Token: 0x040030CA RID: 12490
	[HideInInspector]
	public MyFreeCamera freeCamera;

	// Token: 0x040030CB RID: 12491
	public UnderwaterCamera underwaterCamera;

	// Token: 0x040030CC RID: 12492
	public FloatCamera floatCamera;

	// Token: 0x040030CD RID: 12493
	public Camera baitIndicatorCamera;

	// Token: 0x040030CE RID: 12494
	public FishingPlayerRemote fishingPlayerRemote;

	// Token: 0x040030CF RID: 12495
	[ReadOnly]
	public BaitIndicator baitIndicator;

	// Token: 0x040030D0 RID: 12496
	public BaitIndicator baitIndicatorPrefab;

	// Token: 0x040030D1 RID: 12497
	[HideInInspector]
	public CameraFilterPack_FX_Drunk fxDrunk;

	// Token: 0x040030D2 RID: 12498
	[HideInInspector]
	public CameraFilterPack_AAA_Blood_Hit fxBlood1;

	// Token: 0x040030D3 RID: 12499
	[HideInInspector]
	public VolumetricFog volumetricFog;

	// Token: 0x040030D4 RID: 12500
	[HideInInspector]
	public HueFocus hueFocus;

	// Token: 0x040030D5 RID: 12501
	[HideInInspector]
	public global::Colorful.Grayscale grayscale;

	// Token: 0x040030D6 RID: 12502
	[HideInInspector]
	public VintageFast vintageFast;

	// Token: 0x040030D7 RID: 12503
	[HideInInspector]
	public SunShafts sunShafts;

	// Token: 0x040030D8 RID: 12504
	[HideInInspector]
	public CameraFilterPack_AAA_WaterDropPro currentWaterDropPro;

	// Token: 0x040030D9 RID: 12505
	[HideInInspector]
	public CameraFilterPack_AAA_WaterDropPro waterDropPro;

	// Token: 0x040030DA RID: 12506
	[HideInInspector]
	public CameraFilterPack_AAA_WaterDropPro waterDropProUnderwater;

	// Token: 0x040030DB RID: 12507
	[HideInInspector]
	public CameraFilterPack_Colors_Brightness hunterBrightness;

	// Token: 0x040030DC RID: 12508
	[HideInInspector]
	public UnityStandardAssets.ImageEffects.DepthOfField depthOfField;

	// Token: 0x040030DD RID: 12509
	[HideInInspector]
	public WaterCamera waterCamera;

	// Token: 0x040030DE RID: 12510
	[HideInInspector]
	public WaterCamera underwaterWaterCamera;

	// Token: 0x040030DF RID: 12511
	[HideInInspector]
	public WaterRaindropsIME waterRaindropsIME;

	// Token: 0x040030E0 RID: 12512
	[HideInInspector]
	public WaterDropsRainMy waterDropsRainMy;

	// Token: 0x040030E1 RID: 12513
	[HideInInspector]
	public PostProcessingBehaviour postProcessingBehaviour_1;

	// Token: 0x040030E2 RID: 12514
	[HideInInspector]
	public PostProcessingBehaviour postProcessingBehaviour_2;

	// Token: 0x040030E3 RID: 12515
	[HideInInspector]
	public PostProcessLayer postProcessLayer;

	// Token: 0x040030E4 RID: 12516
	[HideInInspector]
	public Beautify beautify;

	// Token: 0x040030E5 RID: 12517
	[HideInInspector]
	public AmplifyMotionEffect amplifyMotionEffect;

	// Token: 0x040030E6 RID: 12518
	[HideInInspector]
	public AmplifyOcclusionEffect amplifyOcclusion;

	// Token: 0x040030E7 RID: 12519
	[HideInInspector]
	public AmplifyOcclusionEffect amplifyOcclusionUnderwater;

	// Token: 0x040030E8 RID: 12520
	[HideInInspector]
	public UNSeeker unSeeker;

	// Token: 0x040030E9 RID: 12521
	[ReadOnly]
	public bool isHandsCameraVisible = true;

	// Token: 0x040030EA RID: 12522
	[ReadOnly]
	public bool isOnIce;

	// Token: 0x040030EB RID: 12523
	private Texture defaultCrosshair;

	// Token: 0x040030EC RID: 12524
	public float resetPlayerInWater = -2f;

	// Token: 0x040030ED RID: 12525
	public float waterMaxClipRange = 1000f;

	// Token: 0x040030EE RID: 12526
	public float playerHeight = 1.7f;

	// Token: 0x040030EF RID: 12527
	public bool canFallInWater;

	// Token: 0x040030F0 RID: 12528
	[HideInInspector]
	public GameController gameController;

	// Token: 0x040030F1 RID: 12529
	public FishingController fishingController;

	// Token: 0x040030F2 RID: 12530
	[HideInInspector]
	public Vector3 prevPosition = Vector3.zero;

	// Token: 0x040030F3 RID: 12531
	[ReadOnly]
	public Vector3 fishingStartPosition = Vector3.zero;

	// Token: 0x040030F4 RID: 12532
	[HideInInspector]
	public Transform startTransform;

	// Token: 0x040030F5 RID: 12533
	public FishingPlayer.PlayerState currentState;

	// Token: 0x040030F6 RID: 12534
	public Vector2 verticalMaxRotation = new Vector2(89f, -45f);

	// Token: 0x040030F7 RID: 12535
	public Vector2 verticalMaxRotationIce = new Vector2(55f, -55f);

	// Token: 0x040030F8 RID: 12536
	[Space(10f)]
	public FishingHands currentHands;

	// Token: 0x040030F9 RID: 12537
	public List<FishingHands> allFishingHands = new List<FishingHands>();

	// Token: 0x040030FA RID: 12538
	[HideInInspector]
	public DrillingController drillingController;

	// Token: 0x040030FB RID: 12539
	[HideInInspector]
	public TournamentPlayer tournamentPlayer;

	// Token: 0x040030FC RID: 12540
	public LayerMask checkWaterMask = 1;

	// Token: 0x040030FD RID: 12541
	[ReadOnly]
	public float currentMotorAcceleration = 0.18f;

	// Token: 0x040030FE RID: 12542
	public Vector2 motorAccelerationValues = new Vector2(0.18f, 0.09f);

	// Token: 0x040030FF RID: 12543
	[HideInInspector]
	public BoatSimulator boatSimulator;

	// Token: 0x04003100 RID: 12544
	[Space(10f)]
	public GameObject unistormCamera;

	// Token: 0x04003101 RID: 12545
	public ParticleSystem rain;

	// Token: 0x04003102 RID: 12546
	public ParticleSystem snow;

	// Token: 0x04003103 RID: 12547
	public ParticleSystem lightningBugs;

	// Token: 0x04003104 RID: 12548
	public ParticleSystem rainMist;

	// Token: 0x04003105 RID: 12549
	public ParticleSystem snowDust;

	// Token: 0x04003106 RID: 12550
	public GameObject rainStreaks;

	// Token: 0x04003107 RID: 12551
	public ParticleSystem windyLeaves;

	// Token: 0x04003108 RID: 12552
	public GameObject lightningBolt1;

	// Token: 0x04003109 RID: 12553
	public ParticleSystem rainSplash;

	// Token: 0x0400310A RID: 12554
	public Transform lightningPosition;

	// Token: 0x0400310B RID: 12555
	[Space(10f)]
	[ReadOnly]
	public Fish fish;

	// Token: 0x0400310C RID: 12556
	[ReadOnly]
	public Junk junk;

	// Token: 0x0400310D RID: 12557
	public float xOffset;

	// Token: 0x0400310E RID: 12558
	public float xOffsetMax = 1f;

	// Token: 0x0400310F RID: 12559
	public Vector3 rodStartPos = Vector3.zero;

	// Token: 0x04003110 RID: 12560
	public Vector3 baitStartPos = Vector3.zero;

	// Token: 0x04003111 RID: 12561
	public Vector3 pullDirection = Vector3.zero;

	// Token: 0x04003112 RID: 12562
	public float pullForce;

	// Token: 0x04003113 RID: 12563
	public float pullSpeed = 1f;

	// Token: 0x04003114 RID: 12564
	public float maxPullForce = 3f;

	// Token: 0x04003115 RID: 12565
	public float pullRodSpeed = 5f;

	// Token: 0x04003116 RID: 12566
	[Space(10f)]
	public float checkWaterDistance = 4f;

	// Token: 0x04003117 RID: 12567
	public float sitHeight = 0.6f;

	// Token: 0x04003118 RID: 12568
	public float defaultHoleDistance = 1f;

	// Token: 0x04003119 RID: 12569
	public float fishDistanceAnimation = 2f;

	// Token: 0x0400311A RID: 12570
	public float fishDistanceBehavior = 100f;

	// Token: 0x0400311B RID: 12571
	public float fishDistanceBoatBehavior = 350f;

	// Token: 0x0400311C RID: 12572
	[ReadOnly]
	public float currentFishDistanceBehavior;

	// Token: 0x0400311D RID: 12573
	public Transform fishDistanceBehaviorSphere;

	// Token: 0x0400311E RID: 12574
	[ReadOnly]
	public float distanceToBait;

	// Token: 0x0400311F RID: 12575
	[HideInInspector]
	public Fish.WatchStyle currentWatchStyle;

	// Token: 0x04003120 RID: 12576
	[HideInInspector]
	public GaussianBlur gaussianBlur;

	// Token: 0x04003121 RID: 12577
	public static bool playerWasInBoat;

	// Token: 0x04003122 RID: 12578
	public Fish tempFishPrefab;

	// Token: 0x04003123 RID: 12579
	private bool AllowGameplayInput = true;

	// Token: 0x04003124 RID: 12580
	[ReadOnly]
	public Vector2 MouseLookSensitivity = new Vector2(-1f, -1f);

	// Token: 0x04003125 RID: 12581
	[ReadOnly]
	public Vector2 CurrentMouseLookSensitivity = new Vector2(-1f, -1f);

	// Token: 0x04003126 RID: 12582
	public float CurrentFieldOfView = 60f;

	// Token: 0x04003127 RID: 12583
	public float ZoomFieldOfView = 1f;

	// Token: 0x04003128 RID: 12584
	public float playersZoom = 0.75f;

	// Token: 0x04003129 RID: 12585
	[HideInInspector]
	public float RotationSpringStiffness = 0.02f;

	// Token: 0x0400312A RID: 12586
	[HideInInspector]
	public float RotationSpringDamping = 0.37f;

	// Token: 0x0400312B RID: 12587
	public Foot leftFoot;

	// Token: 0x0400312C RID: 12588
	public Foot rightFoot;

	// Token: 0x0400312D RID: 12589
	[ReadOnly]
	public bool baitCanBeThrown;

	// Token: 0x0400312E RID: 12590
	[HideInInspector]
	public bool isIceBaitReseted = true;

	// Token: 0x0400312F RID: 12591
	[HideInInspector]
	public bool isDrillerReady;

	// Token: 0x04003130 RID: 12592
	[HideInInspector]
	public bool isDrinking;

	// Token: 0x04003131 RID: 12593
	[HideInInspector]
	public bool isEating;

	// Token: 0x04003132 RID: 12594
	[HideInInspector]
	public bool isPissing;

	// Token: 0x04003133 RID: 12595
	[HideInInspector]
	public bool isPooing;

	// Token: 0x04003134 RID: 12596
	[ReadOnly]
	public bool isThrowingExplosive;

	// Token: 0x04003135 RID: 12597
	[HideInInspector]
	public bool isJerkOn;

	// Token: 0x04003136 RID: 12598
	[HideInInspector]
	public bool wasJustJerked;

	// Token: 0x04003137 RID: 12599
	[HideInInspector]
	public bool isHunterVisionOn;

	// Token: 0x04003138 RID: 12600
	[HideInInspector]
	public bool isDuringAnimation;

	// Token: 0x04003139 RID: 12601
	[HideInInspector]
	public bool wasLateInitialized;

	// Token: 0x0400313A RID: 12602
	public Camera hunterCamera;

	// Token: 0x0400313B RID: 12603
	[ReadOnly]
	public AudioObject hunterAudio;

	// Token: 0x0400313C RID: 12604
	public WaterInteractive waterInteractive;

	// Token: 0x0400313D RID: 12605
	public WaterSimulationArea waterSimulationSpace;

	// Token: 0x0400313E RID: 12606
	[ReadOnly]
	public bool isInWater;

	// Token: 0x0400313F RID: 12607
	[ReadOnly]
	public bool allowRun = true;

	// Token: 0x04003140 RID: 12608
	[ReadOnly]
	public bool allowFishing = true;

	// Token: 0x04003141 RID: 12609
	public float hookLoseChance = 0.3f;

	// Token: 0x04003142 RID: 12610
	public float strengthLevel;

	// Token: 0x04003143 RID: 12611
	[ReadOnly]
	public float currentStrengthLevel;

	// Token: 0x04003144 RID: 12612
	public float foodLevel;

	// Token: 0x04003145 RID: 12613
	public float drunkLevel;

	// Token: 0x04003146 RID: 12614
	public float pissingLevel;

	// Token: 0x04003147 RID: 12615
	public float pissingIncFactor = 0.001f;

	// Token: 0x04003148 RID: 12616
	public bool pissingLevelOverLimit;

	// Token: 0x04003149 RID: 12617
	public ParticleSystem pissingParticles;

	// Token: 0x0400314A RID: 12618
	public ParticleEmitter pissingParticlesOld;

	// Token: 0x0400314B RID: 12619
	public float pooLevel;

	// Token: 0x0400314C RID: 12620
	public float pooIncFactor = 0.001f;

	// Token: 0x0400314D RID: 12621
	public GameObject pooPrefab;

	// Token: 0x0400314E RID: 12622
	public ParticleSystem dustParticles;

	// Token: 0x0400314F RID: 12623
	[ReadOnly]
	public float PlayerFishStrengthDiff;

	// Token: 0x04003150 RID: 12624
	[ReadOnly]
	public float DragFishStrengthDiff;

	// Token: 0x04003151 RID: 12625
	[Header("VR")]
	public Image vrFadeImage;

	// Token: 0x04003152 RID: 12626
	public Transform vrHandsParent;

	// Token: 0x04003153 RID: 12627
	public Transform vrHUDParent;

	// Token: 0x04003154 RID: 12628
	public VRPlayertUIInput vrPlayertUIInput;

	// Token: 0x04003155 RID: 12629
	public OVRPlayerController ovrPlayerController;

	// Token: 0x04003156 RID: 12630
	public AFPSCounter afpsCounter;

	// Token: 0x04003157 RID: 12631
	public float vrHUDHeight;

	// Token: 0x04003158 RID: 12632
	public Vector3 vrHUDOffset = new Vector3(0f, 0f, 0.9f);

	// Token: 0x04003159 RID: 12633
	private bool vrWasLeftHanded;

	// Token: 0x0400315A RID: 12634
	public bool vrLateUpdateHands;

	// Token: 0x0400315B RID: 12635
	[ReadOnly]
	public Vector3 vrWheelMoveLastPosition = Vector3.zero;

	// Token: 0x0400315C RID: 12636
	public float vrWatchHandsCameraStartAngle;

	// Token: 0x0400315D RID: 12637
	public bool newFloatRopeRegenerate;

	// Token: 0x0400315E RID: 12638
	[HideInInspector]
	public new Transform transform;

	// Token: 0x0400315F RID: 12639
	public bool isTeleporting;

	// Token: 0x04003160 RID: 12640
	private float lastFloatDepthInput;

	// Token: 0x04003161 RID: 12641
	private float floatDepthInputDelay = 0.06f;

	// Token: 0x04003162 RID: 12642
	private float lastFloatDepthInputDown;

	// Token: 0x04003163 RID: 12643
	private float fastFloatDepthIncrese = 5f;

	// Token: 0x04003164 RID: 12644
	private float fastFloatDepthDelay = 3f;

	// Token: 0x04003165 RID: 12645
	public float checkBoatHeight = 0.1f;

	// Token: 0x04003166 RID: 12646
	public float vrMinCameraHeight = 0.5f;

	// Token: 0x04003167 RID: 12647
	private bool vrWasCameraUnderwater;

	// Token: 0x04003168 RID: 12648
	public bool vrChangeRopeParent = true;

	// Token: 0x04003169 RID: 12649
	public bool vrUpdateRopePosition = true;

	// Token: 0x0400316A RID: 12650
	public float vrHandsScale = 0.8f;

	// Token: 0x0400316B RID: 12651
	[ReadOnly]
	public Vector3 vrHUDTargetRotation = Vector3.zero;

	// Token: 0x0400316C RID: 12652
	public float vrHUDRotationSpeed = 0.2f;

	// Token: 0x0400316D RID: 12653
	[ReadOnly]
	public float vrHUDRotationThreshold = 20f;

	// Token: 0x0400316E RID: 12654
	public bool vrHUDFreeIsRotating;

	// Token: 0x0400316F RID: 12655
	private LTDescr vrHUDRotationTween;

	// Token: 0x02000745 RID: 1861
	public enum PlayerState
	{
		// Token: 0x04003172 RID: 12658
		NORMAL,
		// Token: 0x04003173 RID: 12659
		FISHING,
		// Token: 0x04003174 RID: 12660
		FISHING_NET,
		// Token: 0x04003175 RID: 12661
		DRIVING_BOAT,
		// Token: 0x04003176 RID: 12662
		WATCH_FISH,
		// Token: 0x04003177 RID: 12663
		DRILLING,
		// Token: 0x04003178 RID: 12664
		ICE_FISHING,
		// Token: 0x04003179 RID: 12665
		RPG,
		// Token: 0x0400317A RID: 12666
		ATTRACTOR,
		// Token: 0x0400317B RID: 12667
		DEATH
	}
}
