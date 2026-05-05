using System;
using System.Collections;
using BitStrap;
using Colorful;
using UnityEngine;
using UnityEngine.UI;

// Token: 0x020007C9 RID: 1993
public class HUDManager : MonoBehaviour
{
	// Token: 0x06002EA7 RID: 11943 RVA: 0x0011CCE9 File Offset: 0x0011AEE9
	private HUDManager()
	{
	}

	// Token: 0x17000467 RID: 1127
	// (get) Token: 0x06002EA8 RID: 11944 RVA: 0x0011CD0A File Offset: 0x0011AF0A
	public static HUDManager Instance
	{
		get
		{
			return HUDManager.instance;
		}
	}

	// Token: 0x06002EA9 RID: 11945 RVA: 0x0011CD14 File Offset: 0x0011AF14
	private void Awake()
	{
		if (HUDManager.instance == null)
		{
			HUDManager.instance = this;
		}
		this.kgfMapSystem = global::UnityEngine.Object.FindObjectOfType<KGFMapSystem>();
		base.transform.position = Vector3.zero;
		this.fishingMessageText.transform.parent.gameObject.SetActive(false);
		this.fishingMessageText.text = string.Empty;
		this.ShowMessageWindow(false, string.Empty);
		this.ShowVRQuickMenu(false);
		if (VRManager.IsVROn())
		{
			if (GlobalSettings.Instance)
			{
				this.fadeDark = ((!GlobalSettings.Instance.levelsManager.GetCurrentFishery().iceLevel) ? GameController.Instance.normalPlayerVR.vrFadeImage : GameController.Instance.icePlayerVR.vrFadeImage);
			}
			else
			{
				this.fadeDark = ((!GameController.Instance.iceLevel) ? GameController.Instance.normalPlayerVR.vrFadeImage : GameController.Instance.icePlayerVR.vrFadeImage);
			}
		}
		this.FadeDark(1f, 0f, false);
	}

	// Token: 0x06002EAA RID: 11946 RVA: 0x0011CE3C File Offset: 0x0011B03C
	private void Start()
	{
		this.gameController = GameController.Instance;
		this.ShowRadar(false);
		if (this.gameController.iceLevel)
		{
			this.hudFishing.ShowThrowSlider(false);
			this.hudFishing.ShowThrowSlider(false);
		}
		if (GlobalSettings.Instance)
		{
			this.ShowControls(GlobalSettings.Instance.playerSettings.showIngameControls);
		}
		else
		{
			this.ShowControls(false);
		}
	}

	// Token: 0x06002EAB RID: 11947 RVA: 0x0011CEB4 File Offset: 0x0011B0B4
	public void ResetHUD()
	{
		this.ChangeState(HUDManager.HUDState.GAME);
		this.ChangeSubState(HUDManager.PauseState.DEFAULT);
		this.ChangeSubState(HUDManager.GameState.NORMAL);
		this.infoFisheryExit.SetActive(false);
		this.portButton.SetActive(this.gameController.oceanLevel);
		this.cursorDrilling.SetActive(false);
		this.cursorEnterBoat.SetActive(false);
		this.cursorChooseHole.SetActive(false);
		this.cursorBoat.gameObject.SetActive(false);
		this.cursorRodStand.SetActive(false);
		this.ShowEnterBoatCursor(false);
		this.ShowPullFishCursor(false);
		this.ShowNetFishCursor(false);
		this.ShowHudFishing(false);
	}

	// Token: 0x06002EAC RID: 11948 RVA: 0x0011CF58 File Offset: 0x0011B158
	private void Update()
	{
		if (this.gameController == null || !this.gameController.isInitialized)
		{
			return;
		}
		if (BugReporter.Instance && BugReporter.Instance.isVisible)
		{
			return;
		}
		if (TutorialManager.Instance && TutorialManager.Instance.GetCurrentTutorial())
		{
			return;
		}
		if (this.firstUpdate)
		{
			this.firstUpdate = false;
			this.ChangeState(HUDManager.HUDState.GAME);
		}
		if (UtilitiesInput.GetButtonDown("PAUSE") && this.CanPause())
		{
			if (this.currentHudState == HUDManager.HUDState.GAME)
			{
				this.PauseGame(true);
			}
			else if (this.currentHudState == HUDManager.HUDState.PAUSE)
			{
				if (this.currentPauseState == HUDManager.PauseState.DEFAULT)
				{
					this.PauseGame(false);
				}
				else if (this.currentPauseState == HUDManager.PauseState.OPTIONS)
				{
					this.ShowOptions(false);
				}
			}
		}
		if (this.tournamentFinishCanvas.gameObject.activeSelf && VRManager.IsVROn())
		{
			if (OVRInput.GetDown(OVRInput.Button.Any, OVRInput.Controller.Active) || OVRInput.GamepadGetAnyButtonDown())
			{
				this.ShowTournamentFinishScreen(false);
			}
			return;
		}
		if (this.currentHudState == HUDManager.HUDState.GAME && VRManager.IsVROn() && !VRManager.Instance.keyboardManager.gameObject.activeSelf && UtilitiesInput.GetButtonDown("VR_QUICK_MENU"))
		{
			this.ShowVRQuickMenu(!this.vrQuickMenuCanvas.gameObject.activeSelf);
		}
		if (!this.hudMultiplayer.isInInputMode)
		{
			if (Input.GetKeyDown(KeyCode.I))
			{
			}
			if (Input.GetKeyDown(KeyCode.O))
			{
				this.ShowControls(!this.showInfo);
			}
			if (this.infoWatchFish.activeSelf && VRManager.IsVROn())
			{
				if (UtilitiesInput.GetButtonDown("TEMP_SELL"))
				{
					this.WatchFishDecision(1);
				}
				else if (UtilitiesInput.GetButtonDown("TEMP_RELEASE"))
				{
					this.WatchFishDecision(2);
				}
				else if (UtilitiesInput.GetButtonDown("TEMP_KEEP"))
				{
					this.WatchFishDecision(5);
				}
			}
		}
		this.UpdateRadar();
	}

	// Token: 0x06002EAD RID: 11949 RVA: 0x0011D184 File Offset: 0x0011B384
	public void ChangeState(HUDManager.HUDState newState)
	{
		if (this.fishingPlayer.postProcessingBehaviour_1 && !VRManager.IsVROn())
		{
			this.fishingPlayer.postProcessingBehaviour_1.enabled = newState == HUDManager.HUDState.GAME;
		}
		if (!this.gameController.isMultiplayer || !this.gameController.isTournament)
		{
			Time.timeScale = ((newState != HUDManager.HUDState.GAME) ? 0f : 1f);
		}
		this.fishingPlayer.Pause(newState == HUDManager.HUDState.PAUSE);
		this.pauseCommonCanvas.gameObject.SetActive(newState == HUDManager.HUDState.PAUSE);
		this.pauseCanvas.gameObject.SetActive(newState == HUDManager.HUDState.PAUSE);
		this.hudCanvas.gameObject.SetActive(newState == HUDManager.HUDState.GAME);
		this.infoCanvas.gameObject.SetActive(newState == HUDManager.HUDState.GAME);
		this.fishingCanvas.gameObject.SetActive(newState == HUDManager.HUDState.GAME && this.fishingPlayer.currentHands && this.fishingPlayer.currentHands.baitWasThrown && !this.fishingPlayer.currentHands.fishingRod.isOnRodStand);
		this.hudMultiplayer.gameObject.SetActive(this.gameController.isMultiplayer && newState == HUDManager.HUDState.GAME);
		this.tournamentCanvas.gameObject.SetActive(newState == HUDManager.HUDState.GAME && this.gameController.isTournament);
		if (this.kgfMapSystem)
		{
			this.kgfMapSystem.gameObject.SetActive(newState == HUDManager.HUDState.GAME);
		}
		if (newState == HUDManager.HUDState.GAME)
		{
			if (MapController.Instance && MapController.Instance.initialized)
			{
				MapController.Instance.ShowMap(false, false);
			}
			if (RadioManager.Instance)
			{
				RadioManager.Instance.ShowRadioWindow(false);
			}
		}
		if (newState == HUDManager.HUDState.PAUSE)
		{
			this.ShowVRQuickMenu(false);
		}
		if (MenuManager.Instance)
		{
			this.pauseCommonCanvas.gameObject.SetActive(false);
			this.pauseCanvas.gameObject.SetActive(false);
			MenuManager.Instance.ShowDuringGame(newState == HUDManager.HUDState.PAUSE);
			if (newState == HUDManager.HUDState.GAME)
			{
			}
		}
		else if (newState != HUDManager.HUDState.GAME)
		{
			if (newState == HUDManager.HUDState.PAUSE)
			{
				this.ChangeSubState(HUDManager.PauseState.DEFAULT);
			}
		}
		if ((this.fishingPlayer.currentState == FishingPlayer.PlayerState.DRILLING && this.fishingPlayer.drillingController.Drilling) || this.fishingPlayer.currentState == FishingPlayer.PlayerState.WATCH_FISH)
		{
			Cursor.visible = true;
			Cursor.lockState = CursorLockMode.None;
		}
		else
		{
			Cursor.visible = newState != HUDManager.HUDState.GAME;
			Cursor.lockState = ((newState == HUDManager.HUDState.GAME) ? CursorLockMode.Locked : CursorLockMode.None);
		}
		if (this.fishingPlayer.gaussianBlur)
		{
			this.fishingPlayer.gaussianBlur.enabled = newState == HUDManager.HUDState.PAUSE;
		}
		this.guiCamera.GetComponent<ChromaticAberration>().enabled = newState == HUDManager.HUDState.PAUSE;
		AudioController.SetCategoryVolume("Gameplay", (newState != HUDManager.HUDState.GAME) ? 0f : 1f);
		this.prevHudState = this.currentHudState;
		this.currentHudState = newState;
		if (newState != HUDManager.HUDState.PAUSE)
		{
			this.UpdateControls();
		}
	}

	// Token: 0x06002EAE RID: 11950 RVA: 0x0011D4C4 File Offset: 0x0011B6C4
	public void ChangeSubState(HUDManager.GameState newState)
	{
		if (newState != HUDManager.GameState.NORMAL)
		{
			if (newState == HUDManager.GameState.FISHING)
			{
				this.hudFishing.RefreshSetting();
			}
			else if (newState != HUDManager.GameState.EMPTY)
			{
				if (newState == HUDManager.GameState.AFTER_POO_PISS)
				{
					Cursor.visible = true;
					Cursor.lockState = CursorLockMode.None;
				}
			}
		}
		this.prevGameState = this.currentGameState;
		this.currentGameState = newState;
		this.hudFishing.RefreshSetting();
		this.UpdateInfo();
		this.UpdateControls();
	}

	// Token: 0x06002EAF RID: 11951 RVA: 0x0011D540 File Offset: 0x0011B740
	public void ChangeSubState(HUDManager.PauseState newState)
	{
		if (newState == HUDManager.PauseState.DEFAULT)
		{
			this.pauseCanvas.gameObject.SetActive(true);
			this.pauseCanvas.GetComponent<Animator>().SetBool("Open", true);
		}
		else if (newState == HUDManager.PauseState.OPTIONS)
		{
			this.optionsCanvas.gameObject.SetActive(true);
			this.optionsCanvas.GetComponent<Animator>().SetBool("Open", true);
		}
		else if (newState == HUDManager.PauseState.EQUIPMENT)
		{
			this.equipmentCanvas.gameObject.SetActive(true);
			this.equipmentCanvas.GetComponent<Animator>().SetBool("Open", true);
		}
		else if (newState == HUDManager.PauseState.TOURNAMENT_FINISH)
		{
			this.tournamentFinishCanvas.gameObject.SetActive(true);
			this.tournamentFinishCanvas.GetComponent<Animator>().SetBool("Open", true);
		}
		if (this.currentPauseState != newState)
		{
			if (this.currentPauseState == HUDManager.PauseState.DEFAULT)
			{
				this.pauseCanvas.GetComponent<Animator>().SetBool("Open", false);
				base.StartCoroutine(MenuManager.HideState(this.pauseCanvas.GetComponent<Animator>()));
			}
			else if (this.currentPauseState == HUDManager.PauseState.OPTIONS)
			{
				this.optionsCanvas.GetComponent<Animator>().SetBool("Open", false);
				base.StartCoroutine(MenuManager.HideState(this.optionsCanvas.GetComponent<Animator>()));
			}
			else if (this.currentPauseState == HUDManager.PauseState.EQUIPMENT)
			{
				this.equipmentCanvas.GetComponent<Animator>().SetBool("Open", false);
				base.StartCoroutine(MenuManager.HideState(this.equipmentCanvas.GetComponent<Animator>()));
			}
			else if (this.currentPauseState == HUDManager.PauseState.TOURNAMENT_FINISH)
			{
				this.tournamentFinishCanvas.GetComponent<Animator>().SetBool("Open", false);
				base.StartCoroutine(MenuManager.HideState(this.tournamentFinishCanvas.GetComponent<Animator>()));
			}
		}
		this.prevPauseState = this.currentPauseState;
		this.currentPauseState = newState;
	}

	// Token: 0x06002EB0 RID: 11952 RVA: 0x0011D724 File Offset: 0x0011B924
	public void ShowHudFishing(bool show)
	{
		this.fishingCanvas.gameObject.SetActive(show);
		if (VRManager.Instance.IsVRReeling() && this.fishingPlayer && this.fishingPlayer.currentHands)
		{
			this.fishingPlayer.currentHands.currentUserReelSpeed = 0f;
			this.hudFishing.UpdateReelSpeedNew(this.fishingPlayer.currentHands.currentUserReelSpeed);
			UtilitiesInput.StopVibration(false);
		}
	}

	// Token: 0x06002EB1 RID: 11953 RVA: 0x0011D7AC File Offset: 0x0011B9AC
	public void ShowControls(bool show)
	{
		if (VRManager.IsVROn())
		{
			show = false;
		}
		this.showInfo = show;
		this.infoAll.SetActive(this.showInfo);
		if (GlobalSettings.Instance)
		{
			GlobalSettings.Instance.playerSettings.showIngameControls = this.showInfo;
		}
		this.infoHide.text = "O - " + ((!this.showInfo) ? Utilities.GetTranslation("HUD/SHOW_INFORMATION", false).ToUpper() : Utilities.GetTranslation("HUD/HIDE_INFORMATION", false).ToUpper());
		this.UpdateControls();
	}

	// Token: 0x06002EB2 RID: 11954 RVA: 0x0011D850 File Offset: 0x0011BA50
	public void UpdateInfo()
	{
		if (this.currentHudState == HUDManager.HUDState.GAME)
		{
			this.infoWatchFish.SetActive(this.currentGameState == HUDManager.GameState.WATCH_FISH && this.fishingPlayer.fish);
			this.infoWatchJunk.SetActive(this.currentGameState == HUDManager.GameState.WATCH_FISH && this.fishingPlayer.junk);
			this.cursorDrilling.SetActive(this.currentGameState == HUDManager.GameState.DRILLING);
			this.infoDeath.SetActive(this.currentGameState == HUDManager.GameState.DEATH);
			this.infoPooPiss.SetActive(this.currentGameState == HUDManager.GameState.AFTER_POO_PISS);
		}
	}

	// Token: 0x06002EB3 RID: 11955 RVA: 0x0011D8F9 File Offset: 0x0011BAF9
	public void PauseGame(bool pause)
	{
		if (pause)
		{
			this.Pause();
		}
		else
		{
			this.Resume();
		}
	}

	// Token: 0x06002EB4 RID: 11956 RVA: 0x0011D912 File Offset: 0x0011BB12
	public void Pause()
	{
		this.ChangeState(HUDManager.HUDState.PAUSE);
	}

	// Token: 0x06002EB5 RID: 11957 RVA: 0x0011D91B File Offset: 0x0011BB1B
	public void Resume()
	{
		this.ChangeState(HUDManager.HUDState.GAME);
	}

	// Token: 0x06002EB6 RID: 11958 RVA: 0x0011D924 File Offset: 0x0011BB24
	public bool CanPause()
	{
		return !this.tournamentFinishCanvas.gameObject.activeSelf && !VRManager.Instance.keyboardManager.gameObject.activeSelf && !this.infoWatchFish.activeSelf;
	}

	// Token: 0x06002EB7 RID: 11959 RVA: 0x0011D970 File Offset: 0x0011BB70
	public void ShowInventory(bool show)
	{
		if (show)
		{
			this.PauseGame(true);
			if (MenuManager.Instance)
			{
				MenuManager.Instance.ChangeState(MenuManager.MenuState.INVENTORY);
			}
		}
		else
		{
			this.PauseGame(false);
		}
	}

	// Token: 0x06002EB8 RID: 11960 RVA: 0x0011D9A5 File Offset: 0x0011BBA5
	public void ShowOptions(bool show)
	{
		this.ChangeSubState((!show) ? HUDManager.PauseState.DEFAULT : HUDManager.PauseState.OPTIONS);
	}

	// Token: 0x06002EB9 RID: 11961 RVA: 0x0011D9BA File Offset: 0x0011BBBA
	public void ShowEquipment(bool show)
	{
		this.ChangeSubState((!show) ? HUDManager.PauseState.DEFAULT : HUDManager.PauseState.EQUIPMENT);
	}

	// Token: 0x06002EBA RID: 11962 RVA: 0x0011D9D0 File Offset: 0x0011BBD0
	public void ShowTournamentFinishScreen(bool show)
	{
		if (show && this.currentHudState == HUDManager.HUDState.PAUSE)
		{
			this.ChangeState(HUDManager.HUDState.GAME);
		}
		this.tournamentFinishCanvas.gameObject.SetActive(show);
		this.tournamentFinishCanvas.GetComponent<Animator>().SetBool("Open", show);
		this.hudTournament.gameObject.SetActive(false);
		if (show || this.fishingPlayer.currentState != FishingPlayer.PlayerState.WATCH_FISH)
		{
			this.fishingPlayer.Pause(show);
			Cursor.visible = show;
			Cursor.lockState = ((!show) ? CursorLockMode.Locked : CursorLockMode.None);
		}
		Time.timeScale = ((!show) ? 1f : 0f);
		if (VRManager.IsVROn())
		{
			this.tournamentFinishCanvas.GetComponent<RectTransform>().localScale = Vector3.one * 0.35f;
			this.tournamentFinishCanvas.GetComponent<RectTransform>().localPosition = new Vector3(0f, 0f, -195f);
		}
		Debug.Log("ShowTournamentFinishScreen: " + show);
	}

	// Token: 0x06002EBB RID: 11963 RVA: 0x0011DAE5 File Offset: 0x0011BCE5
	public void ShowEnterBoatCursor(bool show)
	{
		this.cursorEnterBoat.gameObject.SetActive(show);
	}

	// Token: 0x06002EBC RID: 11964 RVA: 0x0011DAF8 File Offset: 0x0011BCF8
	public void ShowPullFishCursor(bool show)
	{
		this.cursorPullFish.gameObject.SetActive(show);
	}

	// Token: 0x06002EBD RID: 11965 RVA: 0x0011DB0B File Offset: 0x0011BD0B
	public void ShowNetFishCursor(bool show)
	{
		this.cursorNetFish.gameObject.SetActive(show);
	}

	// Token: 0x06002EBE RID: 11966 RVA: 0x0011DB1E File Offset: 0x0011BD1E
	public void ShowRodStandCursor(bool show)
	{
		this.cursorRodStand.gameObject.SetActive(show);
	}

	// Token: 0x06002EBF RID: 11967 RVA: 0x00007702 File Offset: 0x00005902
	public void ShowRemotePlayerInfoCursor(bool show)
	{
	}

	// Token: 0x06002EC0 RID: 11968 RVA: 0x0011DB31 File Offset: 0x0011BD31
	public void ChangeGameState(HUDManager.GameState newState)
	{
		if (this.currentHudState == HUDManager.HUDState.PAUSE)
		{
			return;
		}
		this.ChangeSubState(newState);
	}

	// Token: 0x06002EC1 RID: 11969 RVA: 0x0011DB48 File Offset: 0x0011BD48
	public void ExitLevel()
	{
		Cursor.visible = true;
		Cursor.lockState = CursorLockMode.None;
		Time.timeScale = 1f;
		if (this.fishingPlayer.currentHands.bait)
		{
			this.fishingPlayer.currentHands.bait.ReturnToEquipment();
		}
		GlobalSettings.Instance.levelsManager.Save();
		if (this.gameController.fisheryEditorGame)
		{
			FisheryEditor.m_isComingBackFromGame = true;
			LoadingManager.LoadScene("FisheryEditor");
		}
		else
		{
			LoadingManager.LoadScene("MainMenu");
		}
	}

	// Token: 0x06002EC2 RID: 11970 RVA: 0x0011DBDD File Offset: 0x0011BDDD
	public void RecoverAfterDeath()
	{
		this.gameController.fishingPlayer.RecoverAfterDeath();
		this.ChangeGameState(HUDManager.GameState.EMPTY);
	}

	// Token: 0x06002EC3 RID: 11971 RVA: 0x0011DBF7 File Offset: 0x0011BDF7
	public void RecoverAfterPooPiss()
	{
		this.gameController.fishingPlayer.ufpsInput.AllowGameplayInput = true;
		this.gameController.fishingPlayer.pissingLevelOverLimit = false;
		Cursor.visible = false;
		Cursor.lockState = CursorLockMode.Locked;
		this.ChangeGameState(HUDManager.GameState.EMPTY);
	}

	// Token: 0x06002EC4 RID: 11972 RVA: 0x0011DC34 File Offset: 0x0011BE34
	public void PortReturn()
	{
		Cursor.visible = true;
		Cursor.lockState = CursorLockMode.None;
		Time.timeScale = 1f;
		LoadingManager.LoadScene(this.gameController.harbourScene);
	}

	// Token: 0x06002EC5 RID: 11973 RVA: 0x0011DC5C File Offset: 0x0011BE5C
	public void PlayerCaughtFish(Fish fish)
	{
		this.hudWatchFish.PlayerCaughtFish(fish);
		if (AchievementManager.Instance)
		{
			AchievementManager.Instance.UpdateAchievement(AchievementManager.AchievementId.FISH_AMOUNT_01, 1);
			AchievementManager.Instance.UpdateAchievement(AchievementManager.AchievementId.FISH_AMOUNT_02, 1);
			AchievementManager.Instance.UpdateAchievement(AchievementManager.AchievementId.FISH_AMOUNT_03, 1);
			AchievementManager.Instance.UpdateAchievement(AchievementManager.AchievementId.FISH_AMOUNT_04, 1);
			AchievementManager.Instance.UpdateAchievement(AchievementManager.AchievementId.FISH_AMOUNT_05, 1);
			AchievementManager.Instance.UpdateAchievement(AchievementManager.FishToAchievementID(fish.species), 1);
		}
	}

	// Token: 0x06002EC6 RID: 11974 RVA: 0x0011DCDC File Offset: 0x0011BEDC
	public void PlayerCaughtJunk(Junk junk)
	{
		this.hudWatchFish.PlayerCaughtJunk(junk);
		if (AchievementManager.Instance)
		{
			AchievementManager.Instance.UpdateAchievement(AchievementManager.AchievementId.JUNK_AMOUNT_01, 1);
			AchievementManager.Instance.UpdateAchievement(AchievementManager.AchievementId.JUNK_AMOUNT_02, 1);
			AchievementManager.Instance.UpdateAchievement(AchievementManager.AchievementId.JUNK_AMOUNT_03, 1);
		}
	}

	// Token: 0x06002EC7 RID: 11975 RVA: 0x0011DD2C File Offset: 0x0011BF2C
	public void FadeDark(float value, float time, bool startInput = false)
	{
		if (time == 0f)
		{
			this.fadeDark.color = new Color(0f, 0f, 0f, value);
		}
		else if (startInput)
		{
			LeanTween.alpha(this.fadeDark.rectTransform, value, time).setOnComplete(delegate
			{
				this.gameController.fishingPlayer.ufpsInput.MouseLookSensitivity = new Vector2(5f, 5f);
				this.gameController.fishingPlayer.RefreshInputSettings();
			}).setIgnoreTimeScale(true);
		}
		else
		{
			LeanTween.alpha(this.fadeDark.rectTransform, value, time).setIgnoreTimeScale(true);
		}
	}

	// Token: 0x06002EC8 RID: 11976 RVA: 0x0011DDB8 File Offset: 0x0011BFB8
	public IEnumerator FadeOutIn(float time, float delay)
	{
		this.FadeDark(1f, time, false);
		yield return new WaitForSeconds(delay);
		this.FadeDark(0f, time, false);
		yield break;
	}

	// Token: 0x06002EC9 RID: 11977 RVA: 0x0011DDE4 File Offset: 0x0011BFE4
	public void WatchFishDecision(int decision)
	{
		if (this.fishingPlayer.fish == null)
		{
			Debug.LogError("WatchFishDecision fishingPlayer.fish == null");
		}
		if (decision == 1)
		{
			if (GlobalSettings.Instance && this.fishingPlayer.fish)
			{
				GlobalSettings.Instance.playerSettings.AddMoney(this.fishingPlayer.fish.GetMoneyPrize());
			}
		}
		else if ((decision == 2 || decision == 5) && GlobalSettings.Instance && this.fishingPlayer.fish)
		{
			GlobalSettings.Instance.playerSettings.AddExperience(GlobalSettings.Instance.playerSettings.GetExpUpdated(Mathf.RoundToInt((float)this.fishingPlayer.fish.GetExpPrize() * 0.2f)));
		}
		float num = 1f;
		this.gameController.hudManager.hudWatchFish.OnDecision(decision, num);
		LeanTween.value(0f, 1f, num + 0.3f).setOnComplete(delegate
		{
			if (this.fishingPlayer.fish && this.fishingPlayer.fish.watchStyle == Fish.WatchStyle.BOAT)
			{
				this.StartCoroutine(this.FadeOutIn(0.5f, 0.5f));
				LeanTween.delayedCall(0.5f, delegate
				{
					this.gameController.fishingPlayer.WatchFishDecision(decision);
					this.gameController.weatherLevelManager.UpdateWaterProfiles();
				});
			}
			else
			{
				this.gameController.fishingPlayer.WatchFishDecision(decision);
			}
		});
	}

	// Token: 0x06002ECA RID: 11978 RVA: 0x0011DF3C File Offset: 0x0011C13C
	public void ShowMessage(string message, float duration = 3.5f)
	{
		if (this.messageCoroutine != null)
		{
			base.StopCoroutine(this.messageCoroutine);
			this.messageCoroutine = null;
		}
		if (duration < 0f)
		{
			this.ShowEndlessMessage(message);
		}
		else
		{
			this.messageCoroutine = base.StartCoroutine(this.ShowMessageCoroutine(message, duration));
		}
	}

	// Token: 0x06002ECB RID: 11979 RVA: 0x0011DF92 File Offset: 0x0011C192
	private void ShowMessage(string message)
	{
		this.fishingMessageText.transform.parent.gameObject.SetActive(true);
		this.fishingMessageText.text = Utilities.GetTranslation(message, false);
	}

	// Token: 0x06002ECC RID: 11980 RVA: 0x0011DFC4 File Offset: 0x0011C1C4
	private IEnumerator ShowMessageCoroutine(string message, float time)
	{
		Debug.Log("MSG: " + message);
		this.ShowMessage(message);
		yield return new WaitForSeconds(time);
		this.HideMessage();
		yield break;
	}

	// Token: 0x06002ECD RID: 11981 RVA: 0x0011DFF0 File Offset: 0x0011C1F0
	public void HideMessage()
	{
		if (this.messageCoroutine != null)
		{
			base.StopCoroutine(this.messageCoroutine);
			this.messageCoroutine = null;
		}
		this.fishingMessageText.transform.parent.gameObject.SetActive(false);
		this.fishingMessageText.text = string.Empty;
	}

	// Token: 0x06002ECE RID: 11982 RVA: 0x0011E046 File Offset: 0x0011C246
	private void ShowEndlessMessage(string message)
	{
		this.fishingMessageText.transform.parent.gameObject.SetActive(true);
		this.fishingMessageText.text = Utilities.GetTranslation(message, false);
		this.isEndlessMessageActive = true;
	}

	// Token: 0x06002ECF RID: 11983 RVA: 0x0011E07C File Offset: 0x0011C27C
	public void HideEndlessMessage()
	{
		if (!this.isEndlessMessageActive)
		{
			return;
		}
		this.fishingMessageText.transform.parent.gameObject.SetActive(false);
		this.fishingMessageText.text = string.Empty;
		this.isEndlessMessageActive = false;
	}

	// Token: 0x06002ED0 RID: 11984 RVA: 0x0011E0BC File Offset: 0x0011C2BC
	public void ShowMessageWindow(bool show, string message = "")
	{
		this.fishingMessageWindowText.text = Utilities.GetTranslation(message, false);
		this.fishingMessageWindowText.transform.parent.gameObject.SetActive(show);
	}

	// Token: 0x06002ED1 RID: 11985 RVA: 0x0011E0EB File Offset: 0x0011C2EB
	public void UpdateLuckBar(float value)
	{
		this.luckBar.valueCurrent = Mathf.FloorToInt(value * 100f);
	}

	// Token: 0x06002ED2 RID: 11986 RVA: 0x0011E104 File Offset: 0x0011C304
	public void UpdateDrunkBar(float value)
	{
		this.drunkBar.valueCurrent = Mathf.FloorToInt(value * 100f);
	}

	// Token: 0x06002ED3 RID: 11987 RVA: 0x0011E11D File Offset: 0x0011C31D
	public void UpdatePissBar(float value)
	{
		this.pissBar.valueCurrent = Mathf.FloorToInt(value * 100f);
	}

	// Token: 0x06002ED4 RID: 11988 RVA: 0x0011E136 File Offset: 0x0011C336
	public void UpdateFoodBar(float value)
	{
		this.foodBar.valueCurrent = Mathf.FloorToInt(value * 100f);
	}

	// Token: 0x06002ED5 RID: 11989 RVA: 0x0011E14F File Offset: 0x0011C34F
	public void UpdatePooBar(float value)
	{
		this.pooBar.valueCurrent = Mathf.FloorToInt(value * 100f);
	}

	// Token: 0x06002ED6 RID: 11990 RVA: 0x0011E168 File Offset: 0x0011C368
	public void UpdateStrengthBar(float value)
	{
		this.strengthBar.valueCurrent = Mathf.FloorToInt(value * 100f);
	}

	// Token: 0x06002ED7 RID: 11991 RVA: 0x0011E184 File Offset: 0x0011C384
	public void ShowRadar(bool show)
	{
		this.minimapGui.SetActive(show);
		this.kgfMapSystem.gameObject.SetActive(show);
		this.kgfMapSystem.SetMinimapEnabled(show);
		if (show)
		{
			AudioController.Play("RadarShow_01");
		}
		if (this.gameController.isTournament)
		{
			this.hudTournament.ShowPlayersParent(!show);
		}
		if (VRManager.IsVROn())
		{
			this.kgfMapSystem.itsCameraOutput.gameObject.SetActive(false);
		}
	}

	// Token: 0x06002ED8 RID: 11992 RVA: 0x0011E20A File Offset: 0x0011C40A
	public void UpdateRadar()
	{
		this.radarRotatingLine.rectTransform.Rotate(new Vector3(0f, 0f, this.radarRotateSpeed * Time.deltaTime));
	}

	// Token: 0x06002ED9 RID: 11993 RVA: 0x0011E238 File Offset: 0x0011C438
	public bool LineCrossingOver(GameObject EnemyToCheckIfLineIsCrossingOver)
	{
		Transform transform = this.kgfMapSystem.itsDataModuleMinimap.itsGlobalSettings.itsTarget.transform;
		float num = this.radarRotatingLine.transform.localEulerAngles.z - transform.eulerAngles.y + this.radarDetectOffset;
		num %= 360f;
		if (Vector3.Distance(new Vector3(EnemyToCheckIfLineIsCrossingOver.transform.position.x, 0f, EnemyToCheckIfLineIsCrossingOver.transform.position.z), new Vector3(transform.position.x, 0f, transform.position.z)) < 3.5f)
		{
			return true;
		}
		Vector3 normalized = (EnemyToCheckIfLineIsCrossingOver.transform.position - transform.position).normalized;
		Vector3 vector = new Vector3(Mathf.Cos(num / 360f * 6.28318f), 0f, Mathf.Sin(num / 360f * 6.28318f));
		return Vector3.Dot(normalized, vector.normalized) > 0.95f;
	}

	// Token: 0x06002EDA RID: 11994 RVA: 0x0011E36C File Offset: 0x0011C56C
	public void UpdateControlsVR()
	{
		VRControllersManager.Instance.HideAllInfo();
		if (!VRManager.useOculusSDK && VRControllersManager.Instance.GetVRController(OVRInput.Controller.RTouch) == VRControllersManager.Instance.touchRightController)
		{
			VRControllersManager.Instance.UpdateButtonInfo(OVRInput.Controller.RTouch, OVRInput.Button.PrimaryThumbstick, Utilities.GetTranslation("VR/QUICK_MENU", false));
		}
		else
		{
			VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("VR_QUICK_MENU"), Utilities.GetTranslation("VR/QUICK_MENU", false));
		}
		if (this.fishingPlayer.vrPlayertUIInput.isOn)
		{
			VRControllersManager.Instance.UpdateButtonInfo(VRControllersManager.Instance.GetPrimaryController(), OVRInput.Button.PrimaryIndexTrigger, Utilities.GetTranslation("GUI/BTN_CHOOSE", false));
			VRControllersManager.Instance.UpdateButtonInfo(VRControllersManager.Instance.GetSecondaryController(), OVRInput.Button.PrimaryIndexTrigger, Utilities.GetTranslation("GUI/BTN_CHOOSE", false));
			return;
		}
		if ((VRManager.Instance.playerWalkStyle == VRManager.PlayerWalkStyle.GAZE_TELEPORT || VRManager.Instance.playerWalkStyle == VRManager.PlayerWalkStyle.POINTER_TELEPORT) && this.fishingPlayer.currentState != FishingPlayer.PlayerState.WATCH_FISH && this.fishingPlayer.currentState != FishingPlayer.PlayerState.DRILLING && this.fishingPlayer.currentState != FishingPlayer.PlayerState.DRIVING_BOAT)
		{
			VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("VR_TELEPORT_GAZE"), Utilities.GetTranslation("HUD_CONTROLS/TELEPORT", false));
		}
		if (this.currentGameState == HUDManager.GameState.NORMAL)
		{
		}
		if (this.currentGameState == HUDManager.GameState.FISHING && !this.fishingPlayer.currentHands.fishingRod.isOnRodStand)
		{
			if (this.fishingPlayer.currentHands.baitWasThrown)
			{
				VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("RESET_THROW"), Utilities.GetTranslation("HUD_CONTROLS/RESET_ROD", false));
				if (!VRManager.Instance.IsVRReeling())
				{
					VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("REEL_IN"), Utilities.GetTranslation("HUD_CONTROLS/REEL_IN", false));
				}
				else if (this.fishingPlayer.currentHands.isFlyRig)
				{
					VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("VR_REEL_GRIP"), Utilities.GetTranslation("HUD_CONTROLS/HOLD", false));
				}
				else if (VRManager.Instance.IsVRReelingGrip())
				{
					VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("VR_REEL_GRIP"), Utilities.GetTranslation("VR/VR_REEL_GRIP", false));
				}
				VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("REEL_ARCH"), Utilities.GetTranslation("HUD_CONTROLS/REEL_ARCH", false));
				if (!VRManager.Instance.IsVRHoldRod())
				{
				}
			}
			else if (this.fishingPlayer.currentHands.currentBoilie)
			{
				VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("BOILIE"), "(" + Utilities.GetTranslation("HUD_WATCH_FISH/RELEASE", false) + ") " + Utilities.GetTranslation("HUD_CONTROLS/THROW_BOILIE", false));
			}
			else if (this.fishingPlayer.isHandsCameraVisible)
			{
				if (VRManager.Instance.IsVRHoldRod())
				{
					VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("THROW_FAR"), string.Concat(new string[]
					{
						"(",
						Utilities.GetTranslation("HUD_CONTROLS/HOLD", false),
						"/",
						Utilities.GetTranslation("HUD_WATCH_FISH/RELEASE", false),
						") ",
						Utilities.GetTranslation("HUD_CONTROLS/THROW_BAIT", false)
					}));
				}
				else
				{
					VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("THROW_FAR"), Utilities.GetTranslation("HUD_CONTROLS/THROW_BAIT", false));
				}
				if (!GlobalSettings.Instance || GlobalSettings.Instance.equipmentManager.HasBoilieEquiped())
				{
					if (VRManager.Instance.IsVRGroundBait())
					{
						VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("BOILIE"), "(" + Utilities.GetTranslation("HUD_CONTROLS/HOLD", false) + ") " + Utilities.GetTranslation("EQUIPMENT/BOILIE", false));
					}
					else
					{
						VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("BOILIE"), Utilities.GetTranslation("HUD_CONTROLS/THROW_BOILIE", false));
					}
				}
			}
			if (this.fishingPlayer.currentHands.baitWasThrown)
			{
				VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("DRAG_INC"), Utilities.GetTranslation("HUD_CONTROLS/DRAG", false) + "+");
				VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("DRAG_DEC"), Utilities.GetTranslation("HUD_CONTROLS/DRAG", false) + "-");
			}
		}
		if (this.currentGameState == HUDManager.GameState.ICE_FISHING)
		{
			VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("STOP_FISHING"), Utilities.GetTranslation("HUD_CONTROLS/STOP_FISHING", false));
			VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("RESET_THROW"), Utilities.GetTranslation("HUD_CONTROLS/RESET_ROD", false));
			if (!VRManager.Instance.IsVRReeling())
			{
				VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("REEL_IN"), Utilities.GetTranslation("HUD_CONTROLS/REEL_IN", false));
				VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("REEL_OUT"), Utilities.GetTranslation("HUD_CONTROLS/REEL_OUT", false));
			}
			else if (VRManager.Instance.IsVRReelingGrip())
			{
				VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("VR_REEL_GRIP"), Utilities.GetTranslation("VR/VR_REEL_GRIP", false));
			}
			if (!GlobalSettings.Instance || GlobalSettings.Instance.equipmentManager.HasBoilieEquiped())
			{
			}
			VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("DRAG_INC"), Utilities.GetTranslation("HUD_CONTROLS/DRAG", false) + "+");
			VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("DRAG_DEC"), Utilities.GetTranslation("HUD_CONTROLS/DRAG", false) + "-");
		}
		if ((this.currentGameState == HUDManager.GameState.FISHING || this.currentGameState == HUDManager.GameState.ICE_FISHING) && this.fishingPlayer.currentHands.ThrowObjectOnWater() && this.fishingPlayer.underwaterCamera.HasProperHeight() && (!GlobalSettings.Instance || GlobalSettings.Instance.playerSettings.IsCasual()) && !this.fishingPlayer.grayscale.enabled && !this.fishingPlayer.currentHands.isGroundRig && !this.fishingPlayer.currentHands.fishingRod.isOnRodStand)
		{
			VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("UNDERWATER_CAMERA"), Utilities.GetTranslation("HUD_CONTROLS/CHANGE_CAMERA", false));
		}
		if (this.currentGameState == HUDManager.GameState.FISHING_NET)
		{
			VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("RESET_THROW"), Utilities.GetTranslation("HUD_CONTROLS/RESET_ROD", false));
			VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("NET_UP"), Utilities.GetTranslation("HUD_CONTROLS/NET_UP", false));
			VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("NET_DOWN"), Utilities.GetTranslation("HUD_CONTROLS/NET_DOWN", false));
		}
		if (this.currentGameState == HUDManager.GameState.FISHING && this.fishingPlayer.boatSimulator && (!this.fishingPlayer.currentHands.baitWasThrown || this.fishingPlayer.currentHands.fishingRod.isOnRodStand))
		{
			VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("BOAT_DRIVE"), Utilities.GetTranslation("HUD_CONTROLS/DRIVE_BOAT", false));
			if (!this.gameController.oceanLevel)
			{
				VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("BOAT_EXIT"), Utilities.GetTranslation("HUD_CONTROLS/EXIT_BOAT", false));
			}
		}
		if (this.currentGameState == HUDManager.GameState.DRIVING_BOAT)
		{
			VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("BOAT_DRIVE_STOP"), Utilities.GetTranslation("HUD_CONTROLS/START_FISHING", false));
			if (!this.gameController.oceanLevel)
			{
				VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("BOAT_EXIT"), Utilities.GetTranslation("HUD_CONTROLS/EXIT_BOAT", false));
			}
			if (this.fishingPlayer.boatSimulator.HasRodsOnPod())
			{
			}
			VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("VR_BOAT_THROTTLE_UP"), Utilities.GetTranslation("HUD_CONTROLS/FORWARD", false));
			VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("VR_BOAT_THROTTLE_DOWN"), Utilities.GetTranslation("HUD_CONTROLS/BACKWARD", false));
		}
		if ((this.currentGameState == HUDManager.GameState.DRIVING_BOAT && VRManager.Instance.IsVRDriveBoat() && !this.fishingPlayer.boatSimulator.isKayak) || this.currentGameState == HUDManager.GameState.ICE_FISHING)
		{
			VRControllersManager.Instance.UpdateButtonInfo(VRControllersManager.Instance.GetSecondaryController(), OVRInput.Button.PrimaryThumbstick, Utilities.GetTranslation("HUD_CONTROLS/MOVE", false));
			if (this.currentGameState == HUDManager.GameState.DRIVING_BOAT)
			{
				VRControllersManager.Instance.UpdateButtonInfo(VRControllersManager.Instance.GetPrimaryController(), OVRInput.Button.Up, Utilities.GetTranslation("HUD_CONTROLS/UP", false));
				VRControllersManager.Instance.UpdateButtonInfo(VRControllersManager.Instance.GetPrimaryController(), OVRInput.Button.Down, Utilities.GetTranslation("HUD_CONTROLS/DOWN", false));
				VRControllersManager.Instance.UpdateButtonInfo(VRControllersManager.Instance.GetPrimaryController(), OVRInput.Button.PrimaryHandTrigger, Utilities.GetTranslation("HUD_CONTROLS/HOLD", false));
				VRControllersManager.Instance.UpdateButtonInfo(VRControllersManager.Instance.GetSecondaryController(), OVRInput.Button.PrimaryHandTrigger, Utilities.GetTranslation("HUD_CONTROLS/HOLD", false));
			}
		}
		if (this.currentGameState == HUDManager.GameState.NORMAL && this.gameController.iceLevel)
		{
			VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("TRY_DRILL"), Utilities.GetTranslation("HUD_CONTROLS/START_DRILL", false));
		}
		if (this.currentGameState == HUDManager.GameState.DRILLING)
		{
			if (!this.fishingPlayer.drillingController.placeChosen)
			{
				VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("TRY_DRILL"), Utilities.GetTranslation("HUD_CONTROLS/STOP_DRILL", false));
				VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("START_DRILL"), Utilities.GetTranslation("HUD_CONTROLS/START_DRILL", false));
			}
			else
			{
				VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("DRILLING"), Utilities.GetTranslation("HUD_CONTROLS/DRILLING", false));
			}
		}
		if (this.fishingPlayer.currentHands && FishingHands.rodStand && this.currentGameState == HUDManager.GameState.FISHING)
		{
			if (!FishingHands.rodStand.isBoatStand)
			{
				if (!FishingHands.rodStand.gameObject.activeSelf && !this.fishingPlayer.boatSimulator)
				{
					VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("ROD_STAND_PUT"), Utilities.GetTranslation("HUD_CONTROLS/ROD_POD_PUT", false));
				}
				else if (FishingHands.rodStand.gameObject.activeSelf)
				{
					VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("ROD_STAND_TAKE"), Utilities.GetTranslation("HUD_CONTROLS/ROD_POD_TAKE", false));
				}
			}
			if (FishingHands.rodStand.gameObject.activeSelf && this.fishingPlayer.currentHands.baitWasThrown && !this.fishingPlayer.currentHands.fishingRod.isOnRodStand && (!this.fishingPlayer.fish || !this.fishingPlayer.fish.isJerked))
			{
				VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("ROD_STAND_PUT_ROD"), Utilities.GetTranslation("HUD_CONTROLS/ROD_POD_PUT_ROD", false));
			}
		}
		if (this.fishingPlayer.currentState == FishingPlayer.PlayerState.WATCH_FISH || this.fishingPlayer.currentState == FishingPlayer.PlayerState.DEATH || this.fishingPlayer.isHunterVisionOn || this.fishingPlayer.underwaterCamera.isTurnedOn || !GlobalSettings.Instance || !GlobalSettings.Instance.skillsManager.GetSkill(SkillsManager.SkillType.HUNTER_VISION_1).isUnlocked || GlobalSettings.Instance.playerSettings.IsCasual())
		{
		}
		if (this.currentGameState != HUDManager.GameState.ICE_FISHING)
		{
		}
		if (this.currentGameState == HUDManager.GameState.WATCH_FISH)
		{
			VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("TEMP_SELL"), Utilities.GetTranslation("HUD_WATCH_FISH/SELL", false));
			VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("TEMP_RELEASE"), Utilities.GetTranslation("HUD_WATCH_FISH/RELEASE", false));
			VRControllersManager.Instance.UpdateButtonInfo(VRInputManager.Instance.FindAction("TEMP_KEEP"), Utilities.GetTranslation("HUD_WATCH_FISH/KEEP", false));
		}
	}

	// Token: 0x06002EDB RID: 11995 RVA: 0x0011F06C File Offset: 0x0011D26C
	public void UpdateControls()
	{
		if (this.fishingPlayer == null)
		{
			return;
		}
		if (VRManager.IsVROn() && VRControllersManager.Instance)
		{
			if (this.currentHudState == HUDManager.HUDState.GAME)
			{
				this.UpdateControlsVR();
			}
			return;
		}
		string text = string.Empty;
		string text2;
		if (this.currentGameState == HUDManager.GameState.NORMAL)
		{
			text2 = text;
			text = string.Concat(new string[]
			{
				text2,
				UtilitiesInput.GetMoveControls(),
				" - ",
				Utilities.GetTranslation("HUD_CONTROLS/MOVE", false),
				"\n"
			});
		}
		if (this.currentGameState == HUDManager.GameState.FISHING)
		{
			text2 = text;
			text = string.Concat(new string[]
			{
				text2,
				UtilitiesInput.GetMoveControls(),
				" - ",
				Utilities.GetTranslation("HUD_CONTROLS/MOVE", false),
				"\n"
			});
			if (!this.fishingPlayer.currentHands.fishingRod.isOnRodStand)
			{
				text += "\n";
				if (this.fishingPlayer.currentHands.baitWasThrown)
				{
					text2 = text;
					text = string.Concat(new string[]
					{
						text2,
						UtilitiesInput.GetActionKeyName("RESET_THROW", true),
						" - ",
						Utilities.GetTranslation("HUD_CONTROLS/RESET_ROD", false),
						"\n"
					});
					text2 = text;
					text = string.Concat(new string[]
					{
						text2,
						UtilitiesInput.GetActionKeyName("REEL_IN", true),
						" - ",
						Utilities.GetTranslation("HUD_CONTROLS/REEL_IN", false),
						"\n"
					});
					text2 = text;
					text = string.Concat(new string[]
					{
						text2,
						UtilitiesInput.GetActionKeyName("REEL_OUT", true),
						" - ",
						Utilities.GetTranslation("HUD_CONTROLS/REEL_OUT", false),
						"\n"
					});
					text2 = text;
					text = string.Concat(new string[]
					{
						text2,
						UtilitiesInput.GetActionKeyName("REEL_ARCH", true),
						" - ",
						Utilities.GetTranslation("HUD_CONTROLS/REEL_ARCH", false),
						"\n"
					});
					text2 = text;
					text = string.Concat(new string[]
					{
						text2,
						UtilitiesInput.GetActionKeyName("PUMP", true),
						" - ",
						Utilities.GetTranslation("HUD_CONTROLS/JERK", false),
						"/",
						Utilities.GetTranslation("HUD_CONTROLS/PUMP", false),
						"\n"
					});
				}
				else
				{
					text2 = text;
					text = string.Concat(new string[]
					{
						text2,
						UtilitiesInput.GetActionKeyName("THROW_FAR", true),
						" - ",
						Utilities.GetTranslation("HUD_CONTROLS/THROW_BAIT", false),
						"\n"
					});
					if (this.fishingPlayer.fishingController.canThrowNear)
					{
						text2 = text;
						text = string.Concat(new string[]
						{
							text2,
							UtilitiesInput.GetActionKeyName("THROW_NEAR", true),
							" - ",
							Utilities.GetTranslation("HUD_CONTROLS/THROW_NEAR", false),
							"\n"
						});
					}
					if (!GlobalSettings.Instance || GlobalSettings.Instance.equipmentManager.HasBoilieEquiped())
					{
						text2 = text;
						text = string.Concat(new string[]
						{
							text2,
							UtilitiesInput.GetActionKeyName("BOILIE", true),
							" - ",
							Utilities.GetTranslation("HUD_CONTROLS/THROW_BOILIE", false),
							"\n"
						});
					}
				}
				if (this.fishingPlayer.currentHands.baitWasThrown)
				{
					text2 = text;
					text = string.Concat(new string[]
					{
						text2,
						UtilitiesInput.GetActionKeyName("REEL_INC", true),
						"/",
						UtilitiesInput.GetActionKeyName("REEL_DEC", true),
						" - ",
						Utilities.GetTranslation("HUD_CONTROLS/REEL_SPEED", false),
						"\n"
					});
					text2 = text;
					text = string.Concat(new string[]
					{
						text2,
						UtilitiesInput.GetActionKeyName("DRAG_CHANGE", true),
						" - ",
						Utilities.GetTranslation("HUD_CONTROLS/DRAG", false),
						"\n"
					});
				}
			}
		}
		if (this.currentGameState == HUDManager.GameState.ICE_FISHING)
		{
			text2 = text;
			text = string.Concat(new string[]
			{
				text2,
				UtilitiesInput.GetActionKeyName("STOP_FISHING", true),
				" - ",
				Utilities.GetTranslation("HUD_CONTROLS/STOP_FISHING", false),
				"\n"
			});
			text2 = text;
			text = string.Concat(new string[]
			{
				text2,
				UtilitiesInput.GetActionKeyName("RESET_THROW", true),
				" - ",
				Utilities.GetTranslation("HUD_CONTROLS/RESET_ROD", false),
				"\n"
			});
			text2 = text;
			text = string.Concat(new string[]
			{
				text2,
				UtilitiesInput.GetActionKeyName("REEL_IN", true),
				" - ",
				Utilities.GetTranslation("HUD_CONTROLS/REEL_IN", false),
				"\n"
			});
			text2 = text;
			text = string.Concat(new string[]
			{
				text2,
				UtilitiesInput.GetActionKeyName("REEL_OUT", true),
				" - ",
				Utilities.GetTranslation("HUD_CONTROLS/REEL_OUT", false),
				"\n"
			});
			if (!GlobalSettings.Instance || GlobalSettings.Instance.equipmentManager.HasBoilieEquiped())
			{
				text2 = text;
				text = string.Concat(new string[]
				{
					text2,
					UtilitiesInput.GetActionKeyName("BOILIE", true),
					" - ",
					Utilities.GetTranslation("HUD_CONTROLS/THROW_BOILIE", false),
					"\n"
				});
			}
			text2 = text;
			text = string.Concat(new string[]
			{
				text2,
				UtilitiesInput.GetActionKeyName("REEL_INC", true),
				"/",
				UtilitiesInput.GetActionKeyName("REEL_DEC", true),
				" - ",
				Utilities.GetTranslation("HUD_CONTROLS/REEL_SPEED", false),
				"\n"
			});
			text2 = text;
			text = string.Concat(new string[]
			{
				text2,
				UtilitiesInput.GetActionKeyName("DRAG_CHANGE", true),
				" - ",
				Utilities.GetTranslation("HUD_CONTROLS/DRAG", false),
				"\n"
			});
		}
		if ((this.currentGameState == HUDManager.GameState.FISHING || this.currentGameState == HUDManager.GameState.ICE_FISHING) && this.fishingPlayer.currentHands.ThrowObjectOnWater() && this.fishingPlayer.underwaterCamera.HasProperHeight() && (!GlobalSettings.Instance || GlobalSettings.Instance.playerSettings.IsCasual()) && !this.fishingPlayer.grayscale.enabled && !this.fishingPlayer.currentHands.isGroundRig && !this.fishingPlayer.currentHands.fishingRod.isOnRodStand)
		{
			text2 = text;
			text = string.Concat(new string[]
			{
				text2,
				UtilitiesInput.GetActionKeyName("UNDERWATER_CAMERA", true),
				" - ",
				Utilities.GetTranslation("HUD_CONTROLS/CHANGE_CAMERA", false),
				"\n"
			});
		}
		if (this.currentGameState == HUDManager.GameState.FISHING_NET)
		{
			text2 = text;
			text = string.Concat(new string[]
			{
				text2,
				UtilitiesInput.GetMoveControls(),
				" - ",
				Utilities.GetTranslation("HUD_CONTROLS/MOVE_NET", false),
				"\n"
			});
			text2 = text;
			text = string.Concat(new string[]
			{
				text2,
				UtilitiesInput.GetActionKeyName("NET_UP", true),
				"/",
				UtilitiesInput.GetActionKeyName("NET_DOWN", true),
				" - ",
				Utilities.GetTranslation("HUD_CONTROLS/MOVE_NET_UPDOWN", false),
				"\n"
			});
			text += "\n";
		}
		if (this.currentGameState == HUDManager.GameState.FISHING && this.fishingPlayer.boatSimulator && (!this.fishingPlayer.currentHands.baitWasThrown || this.fishingPlayer.currentHands.fishingRod.isOnRodStand))
		{
			text2 = text;
			text = string.Concat(new string[]
			{
				text2,
				UtilitiesInput.GetActionKeyName("BOAT_DRIVE", true),
				" - ",
				Utilities.GetTranslation("HUD_CONTROLS/DRIVE_BOAT", false),
				"\n"
			});
			if (!this.gameController.oceanLevel)
			{
				text2 = text;
				text = string.Concat(new string[]
				{
					text2,
					UtilitiesInput.GetActionKeyName("BOAT_EXIT", true),
					" - ",
					Utilities.GetTranslation("HUD_CONTROLS/EXIT_BOAT", false),
					"\n"
				});
			}
		}
		if (this.currentGameState == HUDManager.GameState.DRIVING_BOAT)
		{
			text2 = text;
			text = string.Concat(new string[]
			{
				text2,
				UtilitiesInput.GetMoveControls(),
				" - ",
				Utilities.GetTranslation("HUD_CONTROLS/DRIVE_BOAT", false),
				"\n"
			});
			text2 = text;
			text = string.Concat(new string[]
			{
				text2,
				UtilitiesInput.GetActionKeyName("BOAT_DRIVE", true),
				" - ",
				Utilities.GetTranslation("HUD_CONTROLS/START_FISHING", false),
				"\n"
			});
			if (!this.gameController.oceanLevel)
			{
				text2 = text;
				text = string.Concat(new string[]
				{
					text2,
					UtilitiesInput.GetActionKeyName("BOAT_EXIT", true),
					" - ",
					Utilities.GetTranslation("HUD_CONTROLS/EXIT_BOAT", false),
					"\n"
				});
			}
			if (this.fishingPlayer.boatSimulator.HasRodsOnPod())
			{
				text2 = text;
				text = string.Concat(new string[]
				{
					text2,
					UtilitiesInput.GetActionKeyName("RUN", true),
					" - ",
					Utilities.GetTranslation("HUD_CONTROLS/SPEED_UP_BOAT", false),
					"\n"
				});
			}
			text += "\n";
		}
		if (this.currentGameState == HUDManager.GameState.NORMAL && this.gameController.iceLevel)
		{
			text2 = text;
			text = string.Concat(new string[]
			{
				text2,
				UtilitiesInput.GetActionKeyName("TRY_DRILL", true),
				" - ",
				Utilities.GetTranslation("HUD_CONTROLS/START_DRILL", false),
				"\n"
			});
		}
		if (this.currentGameState == HUDManager.GameState.DRILLING && !this.fishingPlayer.drillingController.placeChosen)
		{
			text2 = text;
			text = string.Concat(new string[]
			{
				text2,
				UtilitiesInput.GetActionKeyName("TRY_DRILL", true),
				" - ",
				Utilities.GetTranslation("HUD_CONTROLS/STOP_DRILL", false),
				"\n"
			});
		}
		if (this.currentGameState == HUDManager.GameState.ATTRACTOR)
		{
		}
		if (this.fishingPlayer.currentHands && FishingHands.rodStand && this.currentGameState == HUDManager.GameState.FISHING)
		{
			if (!FishingHands.rodStand.isBoatStand)
			{
				text += "\n";
				if (!FishingHands.rodStand.gameObject.activeSelf && !this.fishingPlayer.boatSimulator)
				{
					text2 = text;
					text = string.Concat(new string[]
					{
						text2,
						UtilitiesInput.GetActionKeyName("ROD_STAND_PUT", true),
						" - ",
						Utilities.GetTranslation("HUD_CONTROLS/ROD_POD_PUT", false),
						"\n"
					});
				}
				else if (FishingHands.rodStand.gameObject.activeSelf)
				{
					text2 = text;
					text = string.Concat(new string[]
					{
						text2,
						UtilitiesInput.GetActionKeyName("ROD_STAND_TAKE", true),
						" - ",
						Utilities.GetTranslation("HUD_CONTROLS/ROD_POD_TAKE", false),
						"\n"
					});
				}
			}
			if (FishingHands.rodStand.gameObject.activeSelf && this.fishingPlayer.currentHands.baitWasThrown && !this.fishingPlayer.currentHands.fishingRod.isOnRodStand && (!this.fishingPlayer.fish || !this.fishingPlayer.fish.isJerked))
			{
				if (FishingHands.rodStand.isBoatStand)
				{
					text += "\n";
				}
				text2 = text;
				text = string.Concat(new string[]
				{
					text2,
					UtilitiesInput.GetActionKeyName("ROD_STAND_PUT_ROD", true),
					" - ",
					Utilities.GetTranslation("HUD_CONTROLS/ROD_POD_PUT_ROD", false),
					"\n"
				});
			}
			text += "\n";
		}
		if (this.fishingPlayer.currentState != FishingPlayer.PlayerState.WATCH_FISH && this.fishingPlayer.currentState != FishingPlayer.PlayerState.DEATH && !this.fishingPlayer.isHunterVisionOn && !this.fishingPlayer.underwaterCamera.isTurnedOn && (!GlobalSettings.Instance || (GlobalSettings.Instance.skillsManager.GetSkill(SkillsManager.SkillType.HUNTER_VISION_1).isUnlocked && GlobalSettings.Instance.playerSettings.IsCasual())))
		{
			text2 = text;
			text = string.Concat(new string[]
			{
				text2,
				UtilitiesInput.GetActionKeyName("HUNTER_VISION", true),
				" - ",
				Utilities.GetTranslation("HUD_CONTROLS/HUNTER_VISION", false),
				"\n"
			});
		}
		text2 = text;
		text = string.Concat(new string[]
		{
			text2,
			UtilitiesInput.GetActionKeyName("FLASHLIGHT", true),
			" - ",
			Utilities.GetTranslation("HUD_CONTROLS/FLASHLIGHT", false),
			"\n"
		});
		if (this.currentGameState != HUDManager.GameState.ICE_FISHING)
		{
			text2 = text;
			text = string.Concat(new string[]
			{
				text2,
				UtilitiesInput.GetActionKeyName("ZOOM", true),
				" - ",
				Utilities.GetTranslation("HUD_CONTROLS/ZOOM", false),
				"\n"
			});
		}
		text += "\n";
		if ((this.currentGameState == HUDManager.GameState.FISHING || this.currentGameState == HUDManager.GameState.ICE_FISHING) && !this.fishingPlayer.currentHands.baitWasThrown)
		{
			text = text + UtilitiesInput.GetActionKeyName("EQUIPMENT_SET_1", true) + ", ";
			text = text + UtilitiesInput.GetActionKeyName("EQUIPMENT_SET_2", true) + ", ";
			text = text + UtilitiesInput.GetActionKeyName("EQUIPMENT_SET_3", true) + ", ";
			text = text + UtilitiesInput.GetActionKeyName("EQUIPMENT_SET_4", true) + ", ";
			text += UtilitiesInput.GetActionKeyName("EQUIPMENT_SET_5", true);
			text = text + " - " + Utilities.GetTranslation("GUI_HEADERS/EQUIPMENT", false).ToLower() + "\n";
		}
		if (!this.gameController.isTournament || !this.gameController.tournamentManager.tournamentActive)
		{
			text = text + "Ctrl + </> - " + Utilities.GetTranslation("HUD_CONTROLS/CHANGE_HOUR", false) + "\n";
		}
		if (!this.gameController.isTournament || !this.gameController.tournamentManager.tournamentActive)
		{
			text2 = text;
			text = string.Concat(new string[]
			{
				text2,
				UtilitiesInput.GetActionKeyName("FREEZE_GAME", true),
				" - ",
				Utilities.GetTranslation("GUI_HEADERS/PAUSE", false).ToLower(),
				"\n"
			});
		}
		if (!this.gameController.oceanLevel && !this.fishingPlayer.boatSimulator && (this.currentGameState == HUDManager.GameState.NORMAL || this.currentGameState == HUDManager.GameState.FISHING))
		{
			text = text + "F9 - " + Utilities.GetTranslation("HUD_CONTROLS/RESET_PLAYER", false) + "\n";
		}
		text = text + "F5 - " + Utilities.GetTranslation("GUI/REPORT_BUG", false).ToLower();
		if (GlobalSettings.Instance == null || (GlobalSettings.Instance && GlobalSettings.Instance.turnOnCheats))
		{
			text += "\nF11 - show/hide fish indicators";
		}
		this.infoText.text = text;
		this.UpdateInfoSize();
		LeanTween.delayedCall(0.1f, delegate
		{
			this.UpdateInfoSize();
		});
	}

	// Token: 0x06002EDC RID: 11996 RVA: 0x0012002C File Offset: 0x0011E22C
	public void UpdateInfoSize()
	{
		if (this.infoText == null)
		{
			return;
		}
		this.infoBackground.rectTransform.sizeDelta = new Vector2(this.infoText.preferredWidth + 14f, this.infoText.preferredHeight + 11f);
		this.infoBackgroundHide.rectTransform.sizeDelta = new Vector2(this.infoBackground.rectTransform.sizeDelta.x, this.infoBackgroundHide.rectTransform.sizeDelta.y);
		this.infoHide.rectTransform.sizeDelta = new Vector2(this.infoBackground.rectTransform.sizeDelta.x - 7f, this.infoHide.rectTransform.sizeDelta.y);
	}

	// Token: 0x06002EDD RID: 11997 RVA: 0x00120114 File Offset: 0x0011E314
	public void InitializeVR()
	{
		if (!VRManager.IsVROn())
		{
			return;
		}
		this.guiCamera.gameObject.SetActive(false);
		this.hudCanvas.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 5f;
		this.infoCanvas.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
		this.vrQuickMenuCanvas.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
		this.hudMultiplayer.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
		this.tournamentCanvas.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
		this.fishingCanvas.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
		base.transform.parent = this.fishingPlayer.vrHUDParent;
		base.transform.localPosition = new Vector3(0f, 1.8f, 0.9f);
		base.transform.localRotation = Quaternion.identity;
		base.transform.localScale = Vector3.one * 0.002f;
		this.hudWeather.localScale = Vector3.one * 1.25f;
		this.hudWeather.transform.localPosition = new Vector3(0f, 255f, 0f);
		this.hudWeather.transform.localEulerAngles = new Vector3(-25f, 0f, 0f);
		RectTransform rectTransform = this.hudBaits.GetComponent<RectTransform>();
		RectTransform rectTransform2 = rectTransform;
		Vector2 vector = new Vector2(0.5f, 0.5f);
		rectTransform.anchorMax = vector;
		vector = vector;
		rectTransform.anchorMin = vector;
		rectTransform2.pivot = vector;
		rectTransform.localScale = Vector3.one * 2f;
		rectTransform.localPosition = new Vector3(330f, 240f, -60f);
		rectTransform.localEulerAngles = new Vector3(-15f, 20f, 0f);
		this.hudFloatWidget.transform.parent = this.hudFloatWidget.transform.parent.parent;
		RectTransform rectTransform3 = this.hudFloatWidget;
		vector = new Vector2(0.5f, 0.5f);
		this.hudFloatWidget.anchorMax = vector;
		vector = vector;
		this.hudFloatWidget.anchorMin = vector;
		rectTransform3.pivot = vector;
		this.hudFloatWidget.localScale = Vector3.one;
		this.hudFloatWidget.localPosition = new Vector3(210f, 0f, 0f);
		this.hudFloatWidget.localEulerAngles = new Vector3(0f, 20f, 0f);
		RectTransform rectTransform4 = this.hudFishingWidget;
		vector = new Vector2(0.5f, 0.5f);
		this.hudFishingWidget.anchorMax = vector;
		vector = vector;
		this.hudFishingWidget.anchorMin = vector;
		rectTransform4.pivot = vector;
		rectTransform = this.hudTournament.GetComponent<RectTransform>();
		RectTransform rectTransform5 = rectTransform;
		vector = new Vector2(0.5f, 0.5f);
		rectTransform.anchorMax = vector;
		vector = vector;
		rectTransform.anchorMin = vector;
		rectTransform5.pivot = vector;
		rectTransform.localScale = Vector3.one * 1.3f;
		rectTransform.localPosition = new Vector3(-500f, -40f, -50f);
		rectTransform.localEulerAngles = new Vector3(0f, -35f, 0f);
		rectTransform = this.minimapGui.GetComponent<RectTransform>();
		RectTransform rectTransform6 = rectTransform;
		vector = new Vector2(0.5f, 0.5f);
		rectTransform.anchorMax = vector;
		vector = vector;
		rectTransform.anchorMin = vector;
		rectTransform6.pivot = vector;
		rectTransform.localScale = Vector3.one * 1f;
		rectTransform.localPosition = new Vector3(200f, -50f, 0f);
		rectTransform.localEulerAngles = new Vector3(0f, 15f, 0f);
		rectTransform = this.hudMultiplayer.chatParent.GetComponent<RectTransform>();
		RectTransform rectTransform7 = rectTransform;
		vector = new Vector2(0.5f, 0.5f);
		rectTransform.anchorMax = vector;
		vector = vector;
		rectTransform.anchorMin = vector;
		rectTransform7.pivot = vector;
		rectTransform.localScale = Vector3.one * 1.4f;
		rectTransform.localPosition = new Vector3(-520f, 150f, -200f);
		rectTransform.localEulerAngles = new Vector3(-15f, -45f, 0f);
		rectTransform = this.hudMultiplayer.steamUserWidget.transform.parent.GetComponent<RectTransform>();
		RectTransform rectTransform8 = rectTransform;
		vector = new Vector2(0.5f, 0.5f);
		rectTransform.anchorMax = vector;
		vector = vector;
		rectTransform.anchorMin = vector;
		rectTransform8.pivot = vector;
		rectTransform.localScale = Vector3.one * 0.8f;
		rectTransform.localPosition = new Vector3(75f, -100f, 0f);
		rectTransform.localEulerAngles = new Vector3(0f, 0f, 0f);
		rectTransform = this.hudFishing.throwStrengthBar.transform.parent.GetComponent<RectTransform>();
		RectTransform rectTransform9 = rectTransform;
		vector = new Vector2(0.5f, 0.5f);
		rectTransform.anchorMax = vector;
		vector = vector;
		rectTransform.anchorMin = vector;
		rectTransform9.pivot = vector;
		rectTransform.localScale = Vector3.one;
		rectTransform.localPosition = new Vector3(-0.4f, -270f, -50f);
		rectTransform.localEulerAngles = new Vector3(40f, 0f, 0f);
		rectTransform = this.hudFishing.tensionEnergyBar.transform.parent.GetComponent<RectTransform>();
		RectTransform rectTransform10 = rectTransform;
		vector = new Vector2(0.5f, 0.5f);
		rectTransform.anchorMax = vector;
		vector = vector;
		rectTransform.anchorMin = vector;
		rectTransform10.pivot = vector;
		rectTransform.localScale = new Vector3(0.52f, 0.77f, 0.62f);
		rectTransform.localPosition = new Vector3(0f, -295f, -72f);
		rectTransform.localEulerAngles = new Vector3(40f, 0f, 0f);
		rectTransform = this.hudFishing.tensionEnergyBarGauge.transform.parent.GetComponent<RectTransform>();
		RectTransform rectTransform11 = rectTransform;
		vector = new Vector2(0.5f, 0.5f);
		rectTransform.anchorMax = vector;
		vector = vector;
		rectTransform.anchorMin = vector;
		rectTransform11.pivot = vector;
		rectTransform.localScale = Vector3.one * 0.75f;
		rectTransform.localPosition = new Vector3(0f, -298f, -72f);
		rectTransform.localEulerAngles = new Vector3(40f, 0f, 0f);
		rectTransform = this.hudFishing.tensionEnergyBarGaugeShort.transform.parent.GetComponent<RectTransform>();
		RectTransform rectTransform12 = rectTransform;
		vector = new Vector2(0.5f, 0.5f);
		rectTransform.anchorMax = vector;
		vector = vector;
		rectTransform.anchorMin = vector;
		rectTransform12.pivot = vector;
		rectTransform.localScale = Vector3.one * 0.5f;
		rectTransform.localPosition = new Vector3(0f, -283f, -60f);
		rectTransform.localEulerAngles = new Vector3(40f, 0f, 0f);
		rectTransform = this.hudFishingWidget.transform.parent.GetComponent<RectTransform>();
		RectTransform rectTransform13 = rectTransform;
		vector = new Vector2(0.5f, 0.5f);
		rectTransform.anchorMax = vector;
		vector = vector;
		rectTransform.anchorMin = vector;
		rectTransform13.pivot = vector;
		rectTransform.localPosition = new Vector3(0f, -180f, 0f);
		rectTransform = this.hudFishingWidget.GetComponent<RectTransform>();
		RectTransform rectTransform14 = rectTransform;
		vector = new Vector2(0.5f, 0.5f);
		rectTransform.anchorMax = vector;
		vector = vector;
		rectTransform.anchorMin = vector;
		rectTransform14.pivot = vector;
		rectTransform.localScale = Vector3.one * 0.7f;
		rectTransform.localPosition = new Vector3(30f, -55f, -20f);
		rectTransform.localEulerAngles = new Vector3(40f, 0f, 0f);
		rectTransform = this.fishingMessageText.transform.parent.GetComponent<RectTransform>();
		RectTransform rectTransform15 = rectTransform;
		vector = new Vector2(0.5f, 0.5f);
		rectTransform.anchorMax = vector;
		vector = vector;
		rectTransform.anchorMin = vector;
		rectTransform15.pivot = vector;
		rectTransform.localScale = Vector3.one * 1f;
		rectTransform.localPosition = new Vector3(0f, -120f, 0f);
		Canvas[] componentsInChildren = base.GetComponentsInChildren<Canvas>(true);
		for (int i = 0; i < componentsInChildren.Length; i++)
		{
			componentsInChildren[i].renderMode = RenderMode.WorldSpace;
			componentsInChildren[i].worldCamera = VRManager.Instance.eyeCenterTransform.GetComponent<Camera>();
			rectTransform = componentsInChildren[i].GetComponent<RectTransform>();
			rectTransform.anchoredPosition3D = Vector3.zero;
			rectTransform.localRotation = Quaternion.identity;
			OVRRaycaster ovrraycaster = componentsInChildren[i].gameObject.AddComponent<OVRRaycaster>();
			ovrraycaster.pointer = VRManager.Instance.gazeInputParent.GetComponentInChildren<OVRGazePointer>(true).gameObject;
			if (componentsInChildren[i] == this.vrQuickMenuCanvas)
			{
				ovrraycaster.sortOrder = 10;
			}
		}
		if (MapController.Instance)
		{
			Canvas component = MapController.Instance.GetComponent<Canvas>();
			component.renderMode = RenderMode.WorldSpace;
			component.worldCamera = VRManager.Instance.eyeCenterTransform.GetComponent<Camera>();
			rectTransform = component.GetComponent<RectTransform>();
			rectTransform.anchoredPosition3D = Vector3.zero;
			rectTransform.localRotation = Quaternion.identity;
			rectTransform.localScale = Vector3.one * 0.005f;
			component.gameObject.AddComponent<OVRRaycaster>();
		}
		this.infoBackgroundHide.gameObject.SetActive(false);
		VRManager.Instance.SetHUDSize(VRManager.Instance.hudSize, true);
	}

	// Token: 0x06002EDE RID: 11998 RVA: 0x00120A84 File Offset: 0x0011EC84
	public void ShowVRQuickMenu(bool show)
	{
		if (this.fishingPlayer)
		{
			this.fishingPlayer.vrPlayertUIInput.TurnOnUIController(show);
		}
		this.vrQuickMenuCanvas.gameObject.SetActive(show);
	}

	// Token: 0x06002EDF RID: 11999 RVA: 0x00120AB8 File Offset: 0x0011ECB8
	public void RecenterHUD()
	{
		base.transform.parent.eulerAngles = new Vector3(base.transform.parent.eulerAngles.x, this.fishingPlayer.ufpsCamera.transform.eulerAngles.y, base.transform.parent.eulerAngles.z);
	}

	// Token: 0x04003618 RID: 13848
	private static HUDManager instance;

	// Token: 0x04003619 RID: 13849
	[ReadOnly]
	public HUDManager.HUDState currentHudState;

	// Token: 0x0400361A RID: 13850
	[ReadOnly]
	public HUDManager.HUDState prevHudState;

	// Token: 0x0400361B RID: 13851
	[ReadOnly]
	public HUDManager.PauseState currentPauseState;

	// Token: 0x0400361C RID: 13852
	[ReadOnly]
	public HUDManager.PauseState prevPauseState;

	// Token: 0x0400361D RID: 13853
	[ReadOnly]
	public HUDManager.GameState currentGameState;

	// Token: 0x0400361E RID: 13854
	[ReadOnly]
	public HUDManager.GameState prevGameState;

	// Token: 0x0400361F RID: 13855
	[HideInInspector]
	public GameController gameController;

	// Token: 0x04003620 RID: 13856
	[HideInInspector]
	public FishingPlayer fishingPlayer;

	// Token: 0x04003621 RID: 13857
	[Header("Canvas")]
	public Canvas hudCanvas;

	// Token: 0x04003622 RID: 13858
	public Canvas fishingCanvas;

	// Token: 0x04003623 RID: 13859
	public Canvas pauseCanvas;

	// Token: 0x04003624 RID: 13860
	public Canvas optionsCanvas;

	// Token: 0x04003625 RID: 13861
	public Canvas equipmentCanvas;

	// Token: 0x04003626 RID: 13862
	public Canvas pauseCommonCanvas;

	// Token: 0x04003627 RID: 13863
	public Canvas infoCanvas;

	// Token: 0x04003628 RID: 13864
	public Canvas tournamentCanvas;

	// Token: 0x04003629 RID: 13865
	public Canvas tournamentFinishCanvas;

	// Token: 0x0400362A RID: 13866
	public Canvas vrQuickMenuCanvas;

	// Token: 0x0400362B RID: 13867
	[Header("Info")]
	public bool showInfo = true;

	// Token: 0x0400362C RID: 13868
	public Image infoBackground;

	// Token: 0x0400362D RID: 13869
	public Text infoText;

	// Token: 0x0400362E RID: 13870
	public Image infoBackgroundHide;

	// Token: 0x0400362F RID: 13871
	public Text infoHide;

	// Token: 0x04003630 RID: 13872
	public GameObject infoAll;

	// Token: 0x04003631 RID: 13873
	public GameObject infoWatchFish;

	// Token: 0x04003632 RID: 13874
	public GameObject infoWatchJunk;

	// Token: 0x04003633 RID: 13875
	public GameObject infoDeath;

	// Token: 0x04003634 RID: 13876
	public GameObject infoPooPiss;

	// Token: 0x04003635 RID: 13877
	public GameObject cursorDrilling;

	// Token: 0x04003636 RID: 13878
	public GameObject cursorEnterBoat;

	// Token: 0x04003637 RID: 13879
	public GameObject cursorChooseHole;

	// Token: 0x04003638 RID: 13880
	public GameObject cursorPullFish;

	// Token: 0x04003639 RID: 13881
	public GameObject cursorNetFish;

	// Token: 0x0400363A RID: 13882
	public GameObject cursorBoat;

	// Token: 0x0400363B RID: 13883
	public GameObject cursorRodStand;

	// Token: 0x0400363C RID: 13884
	public GameObject cursorRemotePlayerInfo;

	// Token: 0x0400363D RID: 13885
	public HUDWatchFish hudWatchFish;

	// Token: 0x0400363E RID: 13886
	public Text deathInfoBtn;

	// Token: 0x0400363F RID: 13887
	public Text pooPissInfo;

	// Token: 0x04003640 RID: 13888
	public GameObject infoFisheryExit;

	// Token: 0x04003641 RID: 13889
	public Text infoFisheryExitTimer;

	// Token: 0x04003642 RID: 13890
	[Header("Player Params")]
	public EnergyBar foodBar;

	// Token: 0x04003643 RID: 13891
	public EnergyBar drunkBar;

	// Token: 0x04003644 RID: 13892
	public EnergyBar pissBar;

	// Token: 0x04003645 RID: 13893
	public EnergyBar pooBar;

	// Token: 0x04003646 RID: 13894
	public EnergyBar luckBar;

	// Token: 0x04003647 RID: 13895
	public EnergyBar strengthBar;

	// Token: 0x04003648 RID: 13896
	[Header("Objects")]
	public HUDFishing hudFishing;

	// Token: 0x04003649 RID: 13897
	public HUDMultiplayer hudMultiplayer;

	// Token: 0x0400364A RID: 13898
	public HUDTournament hudTournament;

	// Token: 0x0400364B RID: 13899
	public HUDBaits hudBaits;

	// Token: 0x0400364C RID: 13900
	public RectTransform hudWeather;

	// Token: 0x0400364D RID: 13901
	public RectTransform hudFloatWidget;

	// Token: 0x0400364E RID: 13902
	public RectTransform hudFishingWidget;

	// Token: 0x0400364F RID: 13903
	public Camera guiCamera;

	// Token: 0x04003650 RID: 13904
	public GameObject drillingIconStart;

	// Token: 0x04003651 RID: 13905
	public GameObject drillingIconDrill;

	// Token: 0x04003652 RID: 13906
	public GameObject portButton;

	// Token: 0x04003653 RID: 13907
	private bool firstUpdate = true;

	// Token: 0x04003654 RID: 13908
	[Header("Radar")]
	public GameObject minimapGui;

	// Token: 0x04003655 RID: 13909
	[HideInInspector]
	public KGFMapSystem kgfMapSystem;

	// Token: 0x04003656 RID: 13910
	public KGFMapIcon fishMapIconPrefab;

	// Token: 0x04003657 RID: 13911
	public Image radarRotatingLine;

	// Token: 0x04003658 RID: 13912
	public RawImage radarVRBackgroundRaw;

	// Token: 0x04003659 RID: 13913
	public float radarRotateSpeed = 5f;

	// Token: 0x0400365A RID: 13914
	public float radarDetectOffset;

	// Token: 0x0400365B RID: 13915
	[ReadOnly]
	public int radarDetectedCount;

	// Token: 0x0400365C RID: 13916
	[Header("x")]
	public Image fadeDark;

	// Token: 0x0400365D RID: 13917
	public Text fishingMessageText;

	// Token: 0x0400365E RID: 13918
	private Coroutine messageCoroutine;

	// Token: 0x0400365F RID: 13919
	public Text fishingMessageWindowText;

	// Token: 0x04003660 RID: 13920
	public int hookSizeInfoCounter;

	// Token: 0x04003661 RID: 13921
	private bool isEndlessMessageActive;

	// Token: 0x020007CA RID: 1994
	public enum HUDState
	{
		// Token: 0x04003663 RID: 13923
		GAME,
		// Token: 0x04003664 RID: 13924
		PAUSE
	}

	// Token: 0x020007CB RID: 1995
	public enum PauseState
	{
		// Token: 0x04003666 RID: 13926
		DEFAULT,
		// Token: 0x04003667 RID: 13927
		OPTIONS,
		// Token: 0x04003668 RID: 13928
		EQUIPMENT,
		// Token: 0x04003669 RID: 13929
		TOURNAMENT_FINISH
	}

	// Token: 0x020007CC RID: 1996
	public enum GameState
	{
		// Token: 0x0400366B RID: 13931
		NORMAL,
		// Token: 0x0400366C RID: 13932
		FISHING,
		// Token: 0x0400366D RID: 13933
		FISHING_NET,
		// Token: 0x0400366E RID: 13934
		DRIVING_BOAT,
		// Token: 0x0400366F RID: 13935
		WATCH_FISH,
		// Token: 0x04003670 RID: 13936
		DRILLING,
		// Token: 0x04003671 RID: 13937
		ICE_FISHING,
		// Token: 0x04003672 RID: 13938
		ATTRACTOR,
		// Token: 0x04003673 RID: 13939
		DEATH,
		// Token: 0x04003674 RID: 13940
		AFTER_POO_PISS,
		// Token: 0x04003675 RID: 13941
		EMPTY
	}
}
