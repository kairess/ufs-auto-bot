using System;
using UnityEngine;
using UnityEngine.UI;

// Token: 0x020007D4 RID: 2004
public class HUDWatchFish : MonoBehaviour
{
	// Token: 0x06002F12 RID: 12050 RVA: 0x001218A7 File Offset: 0x0011FAA7
	private void Awake()
	{
		this.rectTransform = base.GetComponent<RectTransform>();
		if (GlobalSettings.Instance)
		{
			this.playerSettings = GlobalSettings.Instance.playerSettings;
		}
	}

	// Token: 0x06002F13 RID: 12051 RVA: 0x001218D4 File Offset: 0x0011FAD4
	private void Start()
	{
		LeanTween.value(base.gameObject, this.newRecordStartColor, this.newRecordFinishColor, 1f).setLoopPingPong().setIgnoreTimeScale(true)
			.setEaseInOutQuad()
			.setOnUpdate(delegate(Color val)
			{
				this.newRecordText.color = val;
			});
	}

	// Token: 0x06002F14 RID: 12052 RVA: 0x00121914 File Offset: 0x0011FB14
	public void PlayerCaughtFish(Fish fish)
	{
		this.infoFishParams.gameObject.SetActive(false);
		this.watchSellBtn.interactable = true;
		this.watchReleaseBtn.interactable = true;
		int expPrize = fish.GetExpPrize();
		int moneyPrize = fish.GetMoneyPrize();
		if (this.playerSettings)
		{
			this.currentMoneyAmount = this.playerSettings.playersMoney;
			this.currentExpAmount = this.playerSettings.playersExperience;
			this.currentLevel = this.playerSettings.playersLevel;
			this.playerExperienceWidget.expEnergyBar.SetValueMin(this.playerSettings.GetThresholdExperience(this.playerSettings.playersLevel - 1));
			this.playerExperienceWidget.expEnergyBar.SetValueMax(this.playerSettings.GetThresholdExperience(this.playerSettings.playersLevel));
		}
		else
		{
			this.currentMoneyAmount = 666;
			this.currentExpAmount = 150;
			this.currentLevel = 9;
			this.playerExperienceWidget.expEnergyBar.SetValueMin(100);
			this.playerExperienceWidget.expEnergyBar.SetValueMax(200);
		}
		this.playerExperienceWidget.expEnergyBar.SetValueCurrent(this.currentExpAmount);
		this.playerExperienceWidget.levelValueText.text = this.currentLevel.ToString();
		this.fishNameText.text = ((!fish.isYoung) ? string.Empty : (string.Empty + Utilities.GetTranslation("HUD_WATCH_FISH/YOUNG", false) + " ")) + Utilities.GetTranslation(fish.fishName, false);
		this.fishWeigthText.text = UtilitiesUnits.GetWeightString(fish.Weight, "F2");
		this.fishLengthText.text = UtilitiesUnits.GetLengthString(fish.Length, "F2", true);
		this.fishExpText.text = string.Concat(new object[]
		{
			"+ ",
			(!this.playerSettings) ? expPrize : this.playerSettings.GetExpUpdated(expPrize),
			" ",
			Utilities.GetTranslation("HUD_WATCH_FISH/EXP", false)
		});
		this.playerMoneyText.text = this.currentMoneyAmount.ToString() + " $";
		this.watchSellBtnText.text = string.Concat(new object[]
		{
			Utilities.GetTranslation("HUD_WATCH_FISH/SELL", false),
			" +",
			moneyPrize,
			" $"
		});
		this.watchReleaseBtnText.text = string.Concat(new object[]
		{
			Utilities.GetTranslation("HUD_WATCH_FISH/RELEASE", false),
			" +",
			(!this.playerSettings) ? expPrize : this.playerSettings.GetExpUpdated(Mathf.RoundToInt((float)expPrize * 0.2f)),
			" ",
			Utilities.GetTranslation("HUD_WATCH_FISH/EXP", false)
		});
		this.watchYoungBaitBtn.gameObject.SetActive(false);
		this.watchFilletBtn.gameObject.SetActive(false);
		if (GlobalSettings.Instance)
		{
			this.newRecordText.enabled = GlobalSettings.Instance.fishManager.IsFishBigger(fish);
			this.playerSettings.AddScore(expPrize);
			this.playerSettings.AddExperience(this.playerSettings.GetExpUpdated(expPrize));
			GlobalSettings.Instance.fishManager.ChangeFishBigger(fish, GlobalSettings.Instance.levelsManager.GetCurrentFishery().name);
			GlobalSettings.Instance.levelsManager.PlayerCaughtFish(fish);
		}
	}

	// Token: 0x06002F15 RID: 12053 RVA: 0x00121CE4 File Offset: 0x0011FEE4
	public void OnDecision(int decision, float delay)
	{
		this.watchSellBtn.interactable = false;
		this.watchReleaseBtn.interactable = false;
		if (decision == 1)
		{
			LeanTween.value((float)this.currentMoneyAmount, (!this.playerSettings) ? 777f : ((float)this.playerSettings.playersMoney), delay).setOnUpdate(delegate(float value)
			{
				this.playerMoneyText.text = ((int)value).ToString() + " $";
			});
		}
		else if (decision == 2)
		{
			this.UpdateExpBer(delay, 250f);
		}
	}

	// Token: 0x06002F16 RID: 12054 RVA: 0x00121D74 File Offset: 0x0011FF74
	public void UpdateExpBer(float delay, float fakeValue = 250f)
	{
		LeanTween.value((float)this.currentExpAmount, (!this.playerSettings) ? fakeValue : ((float)this.playerSettings.playersExperience), delay).setOnUpdate(delegate(float value)
		{
			if (this.playerExperienceWidget.expEnergyBar.valueCurrent >= this.playerExperienceWidget.expEnergyBar.valueMax && this.currentLevel < ((!this.playerSettings) ? 25 : this.playerSettings.maxPlayerLevel))
			{
				this.currentLevel++;
				this.playerExperienceWidget.levelValueText.text = this.currentLevel.ToString();
				LeanTween.scale(this.playerExperienceWidget.levelValueText.rectTransform, Vector3.one * 1.4f, 0.2f).setLoopPingPong(1);
				AudioController.Play("level_reached");
				if (this.playerSettings)
				{
					if (this.currentLevel == this.playerSettings.maxPlayerLevel)
					{
						this.playerExperienceWidget.expEnergyBar.SetValueMin(0);
						this.playerExperienceWidget.expEnergyBar.SetValueMax(1);
						this.playerExperienceWidget.expEnergyBar.SetValueCurrent(1);
					}
					else
					{
						this.playerExperienceWidget.expEnergyBar.SetValueMin(this.playerSettings.GetThresholdExperience(this.currentLevel - 1));
						this.playerExperienceWidget.expEnergyBar.SetValueMax(this.playerSettings.GetThresholdExperience(this.currentLevel));
					}
				}
				else
				{
					this.playerExperienceWidget.expEnergyBar.SetValueMin(200);
					this.playerExperienceWidget.expEnergyBar.SetValueMax(300);
				}
			}
			this.playerExperienceWidget.expEnergyBar.SetValueCurrent((int)value);
		});
		LeanTween.scale(this.playerExperienceWidget.gameObject, this.playerExperienceWidget.gameObject.transform.localScale * 1.1f, delay / 2f).setLoopPingPong(1);
	}

	// Token: 0x06002F17 RID: 12055 RVA: 0x00007702 File Offset: 0x00005902
	public void PlayerCaughtJunk(Junk junk)
	{
	}

	// Token: 0x06002F18 RID: 12056 RVA: 0x00121E04 File Offset: 0x00120004
	public void UpdateWatchFishPosition(Fish.WatchStyle watchStyle)
	{
		if (VRManager.IsVROn())
		{
			this.rectTransform.anchoredPosition = this.watchFishInfoPosHands;
		}
		else if (watchStyle == Fish.WatchStyle.HOOK_LIGHT)
		{
			this.rectTransform.anchoredPosition = this.watchFishInfoPosHook;
		}
		else if (watchStyle == Fish.WatchStyle.HANDS)
		{
			this.rectTransform.anchoredPosition = this.watchFishInfoPosHands;
		}
		else if (watchStyle == Fish.WatchStyle.BOAT)
		{
			this.rectTransform.anchoredPosition = this.watchFishInfoPosBoat;
		}
	}

	// Token: 0x040036AE RID: 13998
	public Text fishNameText;

	// Token: 0x040036AF RID: 13999
	public Text fishWeigthText;

	// Token: 0x040036B0 RID: 14000
	public Text fishLengthText;

	// Token: 0x040036B1 RID: 14001
	public Text fishExpText;

	// Token: 0x040036B2 RID: 14002
	public Text playerMoneyText;

	// Token: 0x040036B3 RID: 14003
	public Text newRecordText;

	// Token: 0x040036B4 RID: 14004
	public Color newRecordStartColor = Color.white;

	// Token: 0x040036B5 RID: 14005
	public Color newRecordFinishColor = Color.yellow;

	// Token: 0x040036B6 RID: 14006
	public PlayerExperienceWidget playerExperienceWidget;

	// Token: 0x040036B7 RID: 14007
	[Space(10f)]
	public Text infoFishParams;

	// Token: 0x040036B8 RID: 14008
	public Text infoJunkParams;

	// Token: 0x040036B9 RID: 14009
	public Button watchTakeBtn;

	// Token: 0x040036BA RID: 14010
	public Button watchSellBtn;

	// Token: 0x040036BB RID: 14011
	public Button watchReleaseBtn;

	// Token: 0x040036BC RID: 14012
	public Text watchSellBtnText;

	// Token: 0x040036BD RID: 14013
	public Text watchReleaseBtnText;

	// Token: 0x040036BE RID: 14014
	public Button watchFilletBtn;

	// Token: 0x040036BF RID: 14015
	public Button watchYoungBaitBtn;

	// Token: 0x040036C0 RID: 14016
	public Button watchMakeHookBtn;

	// Token: 0x040036C1 RID: 14017
	[Space(10f)]
	public Vector3 watchFishInfoPosHook = new Vector3(0f, -200f, 0f);

	// Token: 0x040036C2 RID: 14018
	public Vector3 watchFishInfoPosHands = new Vector3(0f, 0f, 0f);

	// Token: 0x040036C3 RID: 14019
	public Vector3 watchFishInfoPosBoat = new Vector3(0f, 0f, 0f);

	// Token: 0x040036C4 RID: 14020
	private RectTransform rectTransform;

	// Token: 0x040036C5 RID: 14021
	private int currentMoneyAmount;

	// Token: 0x040036C6 RID: 14022
	private int currentExpAmount;

	// Token: 0x040036C7 RID: 14023
	private int currentLevel;

	// Token: 0x040036C8 RID: 14024
	private PlayerSettingsMy playerSettings;
}
