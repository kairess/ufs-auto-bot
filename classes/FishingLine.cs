using System;
using System.Collections.Generic;
using BitStrap;
using Moonlit.IceFishing;
using PhysicsTools;
using UnityEngine;

// Token: 0x02000738 RID: 1848
public class FishingLine : MonoBehaviour
{
	// Token: 0x06002B06 RID: 11014 RVA: 0x000F4701 File Offset: 0x000F2901
	private void Awake()
	{
		if (this.model)
		{
			this.model.SetActive(GameController.Instance == null);
		}
		this.CalculateWeightDurability();
	}

	// Token: 0x06002B07 RID: 11015 RVA: 0x000F4730 File Offset: 0x000F2930
	public void ResetMaterial()
	{
		this.lineMaterial.color = Color.white;
		this.lineMaterial.SetColor("_EmissionColor", Color.black);
		this.lineMaterialOpaque.color = Color.white;
		this.lineMaterialOpaque.SetColor("_EmissionColor", Color.black);
	}

	// Token: 0x06002B08 RID: 11016 RVA: 0x000F4787 File Offset: 0x000F2987
	[Button]
	public void UpdateModelViewer()
	{
		this.lineModel.material.color = this.lineColor;
	}

	// Token: 0x06002B09 RID: 11017 RVA: 0x000F47A0 File Offset: 0x000F29A0
	[Button]
	public void UpdateThickness()
	{
		this.thikness = Mathf.Lerp(10f, 70f, this.durability);
		if (this.lineType == FishingLine.LineType.BRAID)
		{
			this.thikness *= 0.65f;
		}
		else if (this.lineType == FishingLine.LineType.FLY)
		{
			this.thikness *= 3f;
		}
		this.thikness = (float)Mathf.RoundToInt(this.thikness);
		this.thikness *= 0.01f;
	}

	// Token: 0x06002B0A RID: 11018 RVA: 0x000F4830 File Offset: 0x000F2A30
	public void MakeUpdate()
	{
		if (!this.fishingHands.baitWasThrown)
		{
			return;
		}
		if (this.dontUpdate)
		{
			this.dontUpdate = false;
			return;
		}
		this.fishingReel.UpdateLineAmount(1f - this.currentRope.getLength() / this.fishingReel.maxLineLength);
		if (this.gameController == null)
		{
			this.gameController = GameController.Instance;
		}
		this.lineState = FishingLine.LineState.IDLE;
		float num = 0f;
		this.CalculateLooseLengths();
		this.UpdateLooseFactor();
		if (!this.gameController.iceLevel && !this.fishingHands.ThrowObjectOnWater())
		{
			this.fishingHands.throwObject.transform.position = this.fishingHands.throwFakeObject.transform.position;
			if (this.fishingHands.fishingFloat)
			{
				this.fishingHands.fishingFloat.transform.LookAt(this.fishingHands.fishingPlayer.transform.position);
			}
			this.currentRope.rate = 1f * this.FLC.flyRateFactor;
			this.flyRegenerateTimer -= Time.deltaTime;
			if (this.flyRegenerateTimer <= 0f)
			{
				this.currentRope.regenerateRope(true);
				this.flyRegenerateTimer = Mathf.Lerp(0f, 0.5f, this.currentRope.getLength() / 50f);
				if (this.fishingHands.isThrowingNear)
				{
					this.flyRegenerateTimer = 0f;
				}
			}
			this.lineState = FishingLine.LineState.FLY;
			return;
		}
		if (this.fishingRod.GetThrowObjectDistance() >= this.fishingReel.maxLineLength && this.fishingHands.fishingPlayer.fish && !this.fishingRod.isOnRodStand)
		{
			this.fishingRod.fishingPlayer.LineBreak(Utilities.GetTranslation("HUD_MESSAGE/FISH_ESCAPED", false) + "\n" + Utilities.GetTranslation("HUD_MESSAGE/NO_LINE", false), 1f);
			return;
		}
		this.UpdateTension();
		this.UpdateCanReelOut();
		this.canUseProtection = true;
		if (this.isReeling != 0f)
		{
			if (!this.gameController.iceLevel)
			{
				num = Mathf.Lerp(this.fishingHands.ropeReelInSpeed.x, this.fishingHands.ropeReelInSpeed.y, (this.isReeling >= 0f) ? this.fishingHands.currentReelSpeed : this.fishingHands.currentUserReelSpeed);
			}
			else
			{
				num = Mathf.Lerp(this.fishingHands.ropeReelInSpeed.z, this.fishingHands.ropeReelInSpeed.w, (this.isReeling >= 0f) ? this.fishingHands.currentReelSpeed : this.fishingHands.currentUserReelSpeed);
			}
			if (VRManager.Instance.IsVRReeling())
			{
				num *= VRControllersManager.Instance.handLineSpeedMultiplier;
			}
			if (this.fishingHands.isFlyRig)
			{
				num *= 2f;
			}
			num *= -this.isReeling;
			if (this.isReeling < 0f)
			{
				num *= ((!this.gameController.iceLevel) ? 0.4f : 1f);
			}
		}
		if (this.fishingRod.fishingPlayer.fish && !this.fishingRod.fishingPlayer.fish.isBaitUpdate && this.isReeling == 0f && this.stretchToDistance > Mathf.Lerp(0.001f, 0.2f, this.fishingReel.GetDragForce()) && this.fishingRod.fishingPlayer.DragFishStrengthDiff < 0f)
		{
			num = this.fishingRod.fishingPlayer.fish.currentSpeed;
			num *= ((!this.fishingHands.fishingFloat) ? this.FLC.ropeFishIncMargin : this.FLC.ropeFishFloatIncMargin);
			if (this.fishingReel.GetDragForce() == 0f)
			{
				num *= this.stretchToDistance * 50f;
			}
			else
			{
				num *= this.stretchToDistance * Mathf.Lerp(20f, 1f, this.fishingReel.GetDragForce());
			}
			if (!this.fishingRod.fishingPlayer.fish.isJerked)
			{
			}
			this.lineState = FishingLine.LineState.REEL_OUT;
		}
		float num2 = num;
		if (this.isReeling > 0f)
		{
			this.lineState = FishingLine.LineState.REEL_IN;
		}
		else if (this.isReeling < 0f)
		{
			this.lineState = FishingLine.LineState.REEL_OUT;
		}
		if (num2 < 0f && !this.CanReelIn())
		{
			num2 = 0f;
		}
		this.currentRope.rate = num2;
		if (this.fishingRod.fishingPlayer.fish == null || this.fishingRod.fishingPlayer.fish.isBaitUpdate || this.fishingRod.fishingPlayer.fish.isJerked)
		{
			this.canUseProtection = false;
		}
		if (this.isReeling > 0f)
		{
			this.canUseProtection = false;
		}
		this.isReeling = 0f;
	}

	// Token: 0x06002B0B RID: 11019 RVA: 0x000F4DB4 File Offset: 0x000F2FB4
	public void UpdateTension()
	{
		float num = ((!this.fishingRod.fishingPlayer.isJerkOn) ? 0f : 0.2f);
		float num2 = this.currentTension;
		this.currentTension = 0f;
		if (this.fishingRod.fishingPlayer.currentState == FishingPlayer.PlayerState.WATCH_FISH)
		{
			return;
		}
		this.currentTension = this.stretchToDistance / this.FLC.ropeStretchBreakThreshold;
		if (this.fishingRod.fishingPlayer.fish)
		{
			if (this.fishingRod.fishingPlayer.fishingController.fishCanBePulled)
			{
				this.currentTryReelTension = 0f;
				this.pumpTension = 0f;
			}
			if (this.currentTryReelTension > 0f)
			{
				this.tryPullTension += this.currentTryReelTension * Time.deltaTime;
			}
			else
			{
				this.tryPullTension -= 2f * Time.deltaTime;
			}
			this.tryPullTension = Mathf.Clamp(this.tryPullTension, 0f, 1f);
			this.currentTension += this.tryPullTension;
			this.pumpTension -= 0.08f * Time.deltaTime;
			this.pumpTension = Mathf.Clamp(this.pumpTension, 0f, 1f);
			this.currentTension += this.pumpTension;
		}
		if (this.fishingHands.GetEquipmentDurability() <= 1f)
		{
			this.currentTension /= Mathf.Lerp(0.5f, 1.5f, this.fishingHands.GetEquipmentDurability());
		}
		else
		{
			this.currentTension /= Mathf.Lerp(1.5f, 3f, this.fishingHands.GetEquipmentDurability() - 1f);
		}
		if (!this.fishingRod.fishingPlayer.IsSomethingOnBait())
		{
			this.currentTension = 0f;
		}
		bool flag = this.fishingRod.fishingPlayer.boatSimulator && this.fishingRod.isOnRodStand;
		if (this.stretchToDistance > this.FLC.ropeStretchBreakThreshold * 1f && !flag && this.fishingRod.fishingPlayer.fish && !this.fishingRod.fishingPlayer.fish.isInNetArea && !this.fishingRod.fishingPlayer.fish.isWatchingFish)
		{
			this.currentTension = 1f;
			if (this.stretchToDistance > this.FLC.ropeStretchInstantBreakThreshold)
			{
				Debug.LogError(string.Concat(new object[]
				{
					"Line break quick stretchTimer",
					this.stretchToDistance,
					" ",
					this.currentTension,
					" ",
					this.fishingHands.reel.currentDrag
				}));
				this.stretchTimer += Time.deltaTime;
				if (!this.gameController.iceLevel && this.stretchTimer > 1f && this.fishingHands.reel.currentDrag > 0f)
				{
					this.fishingRod.fishingPlayer.LineBreak(Utilities.GetTranslation("HUD_MESSAGE/LINE_BROKE_TENSION", false), 1f);
					Debug.LogError(string.Concat(new object[]
					{
						"Line BREAK quick ",
						this.stretchToDistance,
						" ",
						this.currentTension,
						" ",
						this.fishingHands.reel.currentDrag
					}));
					this.currentRope.regenerateRope(true);
					return;
				}
			}
			else
			{
				this.stretchTimer = 0f;
			}
		}
		else
		{
			this.stretchTimer = 0f;
		}
		this.currentTension = Mathf.Clamp(this.currentTension, 0f, 1f);
		if (flag)
		{
			this.currentTension = 0f;
		}
		if (this.fishingHands.fishingPlayer.fish && this.fishingHands.fishingPlayer.fish.isInNetArea)
		{
			this.currentTension = 0f;
		}
		if (this.fishingHands.fishingPlayer.fish && this.fishingHands.fishingPlayer.fish.isTryingBait)
		{
			this.currentTension = 0f;
		}
		if (this.fishingHands.fishingPlayer.fish && this.fishingHands.fishingPlayer.fish.IsItTinyFish())
		{
			this.currentTension *= 0.2f;
		}
		if (this.fishingHands.fishingPlayer.fish && this.fishingHands.fishingPlayer.fish.isOnGround)
		{
			this.currentTension = 1f;
			if (this.breakTensionTimer > 0.1f)
			{
				Debug.LogError("Fish isOnGround break");
				this.breakTensionTimer = 0.1f;
			}
		}
		if (this.currentTension >= 1f)
		{
			this.currentTension = 1f;
			this.breakTensionTimer -= Time.deltaTime;
			if (this.breakTensionTimer <= 0f && (!this.gameController.fightTest || (this.fishingHands.fishingPlayer.fish && this.fishingHands.fishingPlayer.fish.isOnGround)))
			{
				this.fishingRod.fishingPlayer.LineBreak(Utilities.GetTranslation("HUD_MESSAGE/LINE_BROKE_TENSION", false), 1f);
				Debug.LogError(string.Concat(new object[] { "Line break tension ", this.stretchToDistance, " ", this.currentTension, " " }));
				return;
			}
		}
		if (this.fishingHands.fishingPlayer.pullForce == 0f && (this.fishingHands.reel.currentDrag == 0f || !this.fishingHands.reel.isDragActive))
		{
			this.currentTension = 0f;
		}
		if (num2 < 1f && this.currentTension >= 1f)
		{
			this.gameController.hudManager.hudFishing.StartTensionAlarm(true);
		}
		else if (num2 >= 1f && this.currentTension < 1f)
		{
			this.gameController.hudManager.hudFishing.StartTensionAlarm(false);
			this.breakTensionTimer = 1.5f;
		}
		if (this.currentTension > 0.5f)
		{
			UtilitiesInput.SetVibration(0, true, Mathf.Lerp(0.1f, 0.55f, Mathf.InverseLerp(0.5f, 1f, this.currentTension)), -1f);
		}
		else if (UtilitiesInput.GetVibration(0) > 0f)
		{
			UtilitiesInput.StopVibration(true);
		}
		num2 = this.currentTension;
	}

	// Token: 0x06002B0C RID: 11020 RVA: 0x000F5526 File Offset: 0x000F3726
	public void ReelLine(float direction)
	{
		this.isReeling = direction;
	}

	// Token: 0x06002B0D RID: 11021 RVA: 0x000F5530 File Offset: 0x000F3730
	public void CalculateLooseLengths()
	{
		if (this.stretchToDistance > 0.1f)
		{
			this.looseLength = 0f;
			this.looseLengthColliders = 0f;
		}
		else
		{
			this.looseLength = this.currentRope.getLength() - this.fishingRod.GetThrowObjectDistance();
			this.looseLengthColliders = this.currentRope.getLengthColliders() - this.fishingRod.GetThrowObjectDistance();
		}
		if (this.fishingHands.fishingFloat)
		{
			this.looseLengthFloat = this.floatRope.getLength() - this.fishingRod.GetBaitFloatDistance();
		}
	}

	// Token: 0x06002B0E RID: 11022 RVA: 0x000F55D4 File Offset: 0x000F37D4
	public void UpdateLooseFactor()
	{
		bool flag = this.isLoose;
		this.stretchFactor = this.currentRope.getLength() - this.currentRope.getLengthColliders();
		this.stretchToDistance = this.stretchFactor / Mathf.Clamp(this.currentRope.getLength(), 0.1f, 666f);
		if (this.fishingHands.fishingFloat)
		{
			this.CheckFloatRopeTension();
		}
		this.looseTensionFactor = this.looseLength;
		this.looseTensionFactor = Mathf.InverseLerp(0f, this.FLC.fishingLineLooseThreshold, 1f - Mathf.Clamp01(this.looseTensionFactor));
		this.isLoose = this.looseTensionFactor < 0.95f;
		this.isLooseCanReel = this.isLoose;
		this.looseTensionFactorColliders = this.looseLengthColliders;
		this.looseTensionFactorColliders = Mathf.InverseLerp(0f, this.FLC.fishingLineLooseThreshold, 1f - Mathf.Clamp01(this.looseTensionFactorColliders));
		this.isLooseColliders = this.looseTensionFactorColliders < 0.95f;
		if (this.fishingHands.fishingFloat && this.fishingHands.fishingPlayer.fish && UtilitiesInput.isReelingIn)
		{
			float num = this.fishingRod.GetThrowObjectDistance() + this.fishingRod.GetBaitFloatDistance() - this.fishingRod.GetBaitDistance();
			if (this.fishingRod.GetBaitDistance() < this.fishingRod.GetThrowObjectDistance() || num > 0.1f || this.looseLengthFloat > 0.05f)
			{
				this.isLoose = true;
				this.isLooseColliders = true;
				this.looseTensionFactor = 0f;
			}
		}
		if (!this.fishingHands.fishingPlayer.gameController.iceLevel)
		{
			this.UpdateSegmentsDamping();
		}
	}

	// Token: 0x06002B0F RID: 11023 RVA: 0x000F57B8 File Offset: 0x000F39B8
	public void LateUpdate()
	{
		if (!this.fishingHands || !this.fishingHands.fishingPlayer)
		{
			return;
		}
		if (Time.timeScale == 0f)
		{
			return;
		}
		if (this.fishingHands.fishingPlayer.gameController.iceLevel)
		{
			this.UpdateIceCollision(this.fishingHands.fishingPlayer.drillingController.currentHole);
		}
		if (this.lineType == FishingLine.LineType.FLY)
		{
			this.UpdateFlyLine();
		}
		this.UpdateFakeStraightLine();
		this.UpdateFakeFloatLine();
		this.UpdateReelLine();
	}

	// Token: 0x06002B10 RID: 11024 RVA: 0x000F5854 File Offset: 0x000F3A54
	private void FixedUpdate()
	{
		if (!this.fishingHands || !this.fishingHands.fishingPlayer)
		{
			return;
		}
		if (this.lineType == FishingLine.LineType.FLY)
		{
			this.UpdateFlyLine();
		}
	}

	// Token: 0x06002B11 RID: 11025 RVA: 0x000F5890 File Offset: 0x000F3A90
	public void CheckFloatRopeTension()
	{
		float num = this.floatRope.getLength() - this.floatRope.getLengthColliders();
		float num2 = num / Mathf.Clamp(this.floatRope.getLength(), 0.1f, 666f);
		if (num2 > 0.3f && this.fishingHands.bait.isOnWater)
		{
			string text = string.Concat(new object[]
			{
				"stretchFactorFloat ",
				num,
				" stretchToDistanceFloat: ",
				num2,
				" floatPos ",
				this.fishingHands.fishingFloat.transform.position
			});
			if (this.fishingHands.fishingPlayer.newFloatRopeRegenerate)
			{
				string text2 = text;
				text = string.Concat(new object[]
				{
					text2,
					" FloatRope getLength: ",
					this.floatRope.getLength(),
					" getLengthColliders: ",
					this.floatRope.getLengthColliders()
				});
				Vector3 vector = this.fishingHands.bait.transform.position - this.fishingHands.fishingFloat.transform.position;
				Vector3 vector2 = this.fishingHands.fishingFloat.transform.position - this.fishingHands.bait.transform.position;
				vector = vector.normalized * num;
				vector2 = vector2.normalized * this.fishingHands.floatRopeDepth;
				this.fishingHands.fishingFloat.transform.position = this.fishingHands.bait.transform.position + vector2;
				text2 = text;
				text = string.Concat(new object[]
				{
					text2,
					" floatPos: ",
					this.fishingHands.fishingFloat.transform.position,
					" baitToFloat: ",
					vector2
				});
				this.floatRope.CalculateTotalLength();
				this.currentRope.CalculateTotalLength();
				text2 = text;
				text = string.Concat(new object[]
				{
					text2,
					" FloatRope 2 getLength: ",
					this.floatRope.getLength(),
					" getLengthColliders: ",
					this.floatRope.getLengthColliders()
				});
			}
			else if (this.floatRope.getLength() > this.fishingHands.maxFloatDistFight)
			{
				this.FixFloatDistance();
			}
			else
			{
				string text2 = text;
				text = string.Concat(new object[]
				{
					text2,
					" FloatRope getLength: ",
					this.floatRope.getLength(),
					" getLengthColliders: ",
					this.floatRope.getLengthColliders()
				});
				this.floatRope.regenerateRope(true);
			}
			Debug.LogError(text);
		}
	}

	// Token: 0x06002B12 RID: 11026 RVA: 0x000F5B8C File Offset: 0x000F3D8C
	public void FixFloatDistance()
	{
		if (this.fishingHands.fishingPlayer.fish && this.fishingHands.fishingPlayer.fish.isWatchingFish)
		{
			Debug.LogError("FixFloatDistance isWatchingFish");
			return;
		}
		if (this.floatRope.getLength() > this.fishingHands.maxFloatDistFight)
		{
			Debug.LogError(string.Concat(new object[]
			{
				"FloatRope getLength: ",
				this.floatRope.getLength(),
				" getLengthColliders: ",
				this.floatRope.getLengthColliders()
			}));
			Vector3 vector = (this.fishingHands.fishingFloat.transform.position - this.fishingHands.bait.transform.position).normalized * this.fishingHands.maxFloatDistFight;
			this.fishingHands.fishingFloat.transform.position = this.fishingHands.bait.transform.position + vector;
			this.floatRope.regenerateRope(true);
			this.floatRope.CalculateTotalLength();
			this.currentRope.CalculateTotalLength();
		}
	}

	// Token: 0x06002B13 RID: 11027 RVA: 0x000F5CD4 File Offset: 0x000F3ED4
	public void UpdateFakeStraightLine()
	{
		if (!this.gameController)
		{
			return;
		}
		if (this.fishingHands.fishingRod.isOnRodStand)
		{
			if (!this.fishingHands.fakeStraightLine.enabled)
			{
				this.fishingHands.fakeStraightLine.enabled = true;
				this.currentRope.meshRenderer.enabled = false;
			}
		}
		else if (this.fishingHands.fishingPlayer.currentState == FishingPlayer.PlayerState.WATCH_FISH)
		{
			this.fishingHands.fakeStraightLine.enabled = true;
			this.currentRope.meshRenderer.enabled = false;
		}
		else if ((this.fishingHands.fishingPlayer.fish || this.fishingHands.fishingPlayer.junk || GameController.Instance.stopBaitOnWater) && this.fishingHands.fishingPlayer.currentState != FishingPlayer.PlayerState.WATCH_FISH)
		{
			if (this.fishingHands.fishingRod.bendAngle < this.fishingHands.fakeStraightLineThreshold && this.currentRope.totalLength < 100f && !this.gameController.iceLevel)
			{
				if (!this.currentRope.meshRenderer.enabled)
				{
					this.fishingHands.fakeStraightLine.enabled = false;
					this.currentRope.meshRenderer.enabled = true;
					this.currentRope.generateOverallMesh();
				}
			}
			else if (!this.fishingHands.fakeStraightLine.enabled)
			{
				this.fishingHands.fakeStraightLine.enabled = true;
				this.currentRope.meshRenderer.enabled = false;
			}
		}
		else if (this.lineState == FishingLine.LineState.FLY)
		{
			this.fishingHands.fakeStraightLine.enabled = true;
			this.currentRope.meshRenderer.enabled = false;
		}
		else if (!this.fishingHands.baitWasThrown)
		{
			if (!this.gameController.iceLevel)
			{
				this.fishingHands.fakeStraightLine.enabled = true;
				this.currentRope.meshRenderer.enabled = false;
			}
		}
		else if (!this.currentRope.meshRenderer.enabled)
		{
			this.fishingHands.fakeStraightLine.enabled = false;
			this.currentRope.meshRenderer.enabled = true;
			this.currentRope.generateOverallMesh();
		}
		if (this.gameController.iceLevel)
		{
			this.fishingHands.fakeStraightLine.enabled = true;
			this.fishingHands.fakeIceLine.enabled = this.fishingHands.fakeStraightLine.enabled && !this.fishingHands.fishingPlayer.fish;
			this.currentRope.meshRenderer.enabled = false;
		}
		if (this.lineType == FishingLine.LineType.FLY)
		{
			this.fishingHands.fakeLeaderLine.enabled = this.fishingHands.fakeStraightLine.enabled;
		}
		if (this.fishingHands.fakeStraightLine.enabled)
		{
			Transform transform = ((!this.gameController.iceLevel) ? this.currentRope.controlPoints[1].obj.transform : this.fishingHands.fakeLineIceTarget);
			if (this.fishingHands.fishingPlayer.currentState == FishingPlayer.PlayerState.WATCH_FISH)
			{
				transform = this.fishingHands.bait.ropeRigidbody.transform;
			}
			float num = Vector3.Distance(this.fishingHands.fakeStraightLine.transform.parent.position, transform.position);
			num /= this.fishingHands.fishingPlayer.vrHandsParent.localScale.y;
			if (this.lineType == FishingLine.LineType.FLY && !this.gameController.iceLevel)
			{
				this.fishingHands.fakeLeaderLine.transform.localScale = new Vector3(this.fishingHands.fakeLeaderLine.transform.localScale.x, num, this.fishingHands.fakeLeaderLine.transform.localScale.z);
				num -= this.fishingHands.lineLeaderLength;
				this.fishingHands.swivel.localPosition = Vector3.forward * (num - 0.003f);
			}
			this.fishingHands.fakeStraightLine.transform.parent.LookAt(transform);
			float num2 = ((!this.fishingHands.baitWasThrown || this.fishingHands.fishingPlayer.currentState == FishingPlayer.PlayerState.WATCH_FISH) ? this.fishingHands.fakeStraightLineThickness.x : this.fishingHands.fakeStraightLineThickness.y);
			this.fishingHands.fakeStraightLine.transform.localScale = new Vector3(num2, num, num2);
			if (this.gameController.iceLevel)
			{
				transform = this.currentRope.controlPoints[1].obj.transform;
				this.fishingHands.fakeIceLine.transform.parent.LookAt(transform);
				this.fishingHands.fakeIceLine.transform.localScale = new Vector3(this.fishingHands.fakeIceLine.transform.localScale.x, Vector3.Distance(this.fishingHands.fakeIceLine.transform.parent.position, transform.position), this.fishingHands.fakeIceLine.transform.localScale.z);
			}
			if (this.lineType == FishingLine.LineType.FLY && this.fishingHands.fishingPlayer.currentState == FishingPlayer.PlayerState.FISHING_NET)
			{
				this.fishingHands.fakeStraightLine.enabled = false;
			}
		}
	}

	// Token: 0x06002B14 RID: 11028 RVA: 0x000F62DC File Offset: 0x000F44DC
	public void UpdateFakeFloatLine()
	{
		if (this.fishingHands.fishingFloat && !this.fishingHands.baitWasThrown)
		{
			this.fishingHands.fakeFloatLine.enabled = true;
			this.floatRope.meshRenderer.enabled = false;
			this.fishingHands.fakeFloatLine.transform.parent.position = this.floatRope.controlPoints[1].obj.transform.position;
			this.fishingHands.fakeFloatLine.transform.parent.LookAt(this.floatRope.controlPoints[0].obj.transform);
			this.fishingHands.fakeFloatLine.transform.localScale = new Vector3(this.fishingHands.fakeFloatLine.transform.localScale.x, Vector3.Distance(this.fishingHands.fakeFloatLine.transform.parent.position, this.floatRope.controlPoints[0].obj.transform.position) / this.fishingHands.fishingPlayer.vrHandsParent.localScale.y, this.fishingHands.fakeFloatLine.transform.localScale.z);
		}
		else
		{
			this.fishingHands.fakeFloatLine.enabled = false;
			if (this.fishingHands.fishingFloat)
			{
				this.floatRope.meshRenderer.enabled = true;
			}
		}
	}

	// Token: 0x06002B15 RID: 11029 RVA: 0x000F648C File Offset: 0x000F468C
	public float GetTension()
	{
		return this.currentTension;
	}

	// Token: 0x06002B16 RID: 11030 RVA: 0x000F6494 File Offset: 0x000F4694
	public float GetDynamicRopeLength()
	{
		return this.currentRope.getLength();
	}

	// Token: 0x06002B17 RID: 11031 RVA: 0x000F64A1 File Offset: 0x000F46A1
	public bool isMaxLength()
	{
		return this.GetDynamicRopeLength() >= this.fishingReel.maxLineLength;
	}

	// Token: 0x06002B18 RID: 11032 RVA: 0x000F64B9 File Offset: 0x000F46B9
	public bool CanReelIn()
	{
		if (this.gameController.iceLevel)
		{
			return this.currentRope.getLength() > 0.13f;
		}
		return this.currentRope.getLength() > 0.3f;
	}

	// Token: 0x06002B19 RID: 11033 RVA: 0x000F64F0 File Offset: 0x000F46F0
	public bool CanReelOut()
	{
		return this.canReelOut;
	}

	// Token: 0x06002B1A RID: 11034 RVA: 0x000F64F8 File Offset: 0x000F46F8
	public void UpdateCanReelOut()
	{
		if (this.gameController.iceLevel)
		{
			this.canReelOut = true;
		}
		else if (this.looseLength > 1.5f && this.canReelOut)
		{
			this.canReelOut = false;
		}
		else if (this.looseLength < 1.3f && !this.canReelOut)
		{
			this.canReelOut = true;
		}
	}

	// Token: 0x06002B1B RID: 11035 RVA: 0x000F656C File Offset: 0x000F476C
	public void UpdateSegmentsDamping()
	{
		SoftJointLimit softJointLimit = default(SoftJointLimit);
		this.currentRope.ropeStretchThreshold = this.FLC.ropeStretchThreshold;
		for (int i = 0; i < this.currentRope.lstSegments.Count; i++)
		{
			FishingLineController.SegmentParameters segmentParameters;
			if (!this.fishingHands.baitWasThrown && !this.fishingHands.isFlyRig)
			{
				segmentParameters = this.FLC.idle;
			}
			else if (!this.fishingHands.baitWasThrown && this.fishingHands.isFlyRig)
			{
				segmentParameters = this.FLC.air;
			}
			else if (this.currentRope.getLength() < 3.2f)
			{
				segmentParameters = this.FLC.near;
			}
			else
			{
				segmentParameters = this.FLC.GetSegmentParameters(this.currentRope.lstSegments[i], i, this.looseTensionFactorColliders, this.currentRope.getLength(), !this.fishingHands.ThrowObjectOnWater());
			}
			this.currentRope.lstSegments[i].body.useGravity = segmentParameters.gravity;
			this.currentRope.lstSegments[i].body.drag = segmentParameters.drag;
			this.currentRope.lstSegments[i].body.angularDrag = segmentParameters.angularDrag;
			float num = ((!this.fishingHands.fishingFloat) ? (segmentParameters.massNormal * this.currentRope.getSegmentProperties().length) : segmentParameters.massFloat);
			this.currentRope.lstSegments[i].body.mass = num;
			softJointLimit.limit = segmentParameters.swingLimit;
			if (this.currentRope.lstSegments[i].prev != null)
			{
				this.currentRope.lstSegments[i].prev.joint.GetComponent<ConfigurableJoint>().angularYLimit = softJointLimit;
				this.currentRope.lstSegments[i].prev.joint.GetComponent<ConfigurableJoint>().angularZLimit = softJointLimit;
			}
			if (this.currentRope.lstSegments[i].next != null)
			{
				this.currentRope.lstSegments[i].next.joint.GetComponent<ConfigurableJoint>().angularYLimit = softJointLimit;
				this.currentRope.lstSegments[i].next.joint.GetComponent<ConfigurableJoint>().angularZLimit = softJointLimit;
			}
		}
		if (this.floatRope == null)
		{
			return;
		}
		for (int j = 0; j < this.floatRope.lstSegments.Count; j++)
		{
			FishingLineController.SegmentParameters segmentParameters;
			if (!this.fishingHands.baitWasThrown)
			{
				segmentParameters = this.FLC.idle;
			}
			else
			{
				segmentParameters = this.FLC.GetFloatSegmentParameters(this.floatRope.lstSegments[j], this.looseTensionFactorColliders, !this.fishingHands.ThrowObjectOnWater());
			}
			this.floatRope.lstSegments[j].body.useGravity = segmentParameters.gravity;
			this.floatRope.lstSegments[j].body.drag = segmentParameters.drag;
			this.floatRope.lstSegments[j].body.angularDrag = segmentParameters.angularDrag;
			float num2 = segmentParameters.massFloat * this.floatRope.getSegmentProperties().length;
			this.floatRope.lstSegments[j].body.mass = num2;
			softJointLimit.limit = segmentParameters.swingLimit;
			if (this.floatRope.lstSegments[j].prev != null)
			{
				this.floatRope.lstSegments[j].prev.joint.GetComponent<ConfigurableJoint>().angularYLimit = softJointLimit;
				this.floatRope.lstSegments[j].prev.joint.GetComponent<ConfigurableJoint>().angularZLimit = softJointLimit;
			}
			if (this.floatRope.lstSegments[j].next != null)
			{
				this.floatRope.lstSegments[j].next.joint.GetComponent<ConfigurableJoint>().angularYLimit = softJointLimit;
				this.floatRope.lstSegments[j].next.joint.GetComponent<ConfigurableJoint>().angularZLimit = softJointLimit;
			}
		}
	}

	// Token: 0x06002B1C RID: 11036 RVA: 0x000F6A1C File Offset: 0x000F4C1C
	public void UpdateIceCollision(Hole hole)
	{
		if (!this.fishingHands.bait.isOnWater)
		{
			this.fishingHands.fakeLineIceTarget.position = this.currentRope.controlPoints[1].obj.transform.position;
			return;
		}
		if (hole == null)
		{
			return;
		}
		Vector3 position = hole.Data.Position;
		float num = hole.Data.Radius * 0.4f;
		float depth = hole.Data.Depth;
		if (this.fishingHands.fakeStraightLine.enabled)
		{
			num = hole.Data.Radius * 0.52f;
			Vector3 vector = position;
			Vector3 position2 = this.fishingHands.fakeStraightLine.transform.position;
			Vector3 vector2 = this.currentRope.controlPoints[1].obj.transform.position - position2;
			Vector3 vector3 = hole.Data.Position;
			RaycastHit raycastHit = default(RaycastHit);
			if (Physics.Raycast(position2, vector2.normalized, out raycastHit, 10f, LayerMask.GetMask(new string[] { "Ice", "Terrain" })))
			{
				vector3 = raycastHit.point;
			}
			vector3.y = hole.Data.Position.y;
			Vector3 vector4 = vector3 - position;
			float num2 = vector4.magnitude - num;
			if (num2 < 0f)
			{
				vector = vector3;
			}
			else
			{
				vector = hole.Data.Position + vector4.normalized * num;
			}
			if (!this.fishingHands.fishingPlayer.fish)
			{
				vector.y += 0.05f;
			}
			this.fishingHands.fakeLineIceTarget.position = vector;
		}
		if (!this.FLC.updateIceCollision)
		{
			return;
		}
		if (!this.fishingHands.bait.isOnWater)
		{
			return;
		}
		if (!this.fishingHands.fishingPlayer.fish)
		{
			List<int> list = new List<int>();
			Vector3 vector5 = Vector3.zero;
			Vector3 vector6 = position;
			Vector3 vector7 = Vector3.zero;
			Vector3 vector8 = Vector3.zero;
			for (int i = 0; i < this.currentRope.lstSegments.Count; i++)
			{
				vector5 = this.currentRope.lstSegments[i].seg.transform.position;
				vector6.y = vector5.y;
				vector8 = vector6 - vector5;
				float magnitude = vector8.magnitude;
				if (vector5.y < position.y + 0.05f && vector5.y > position.y - depth - 0.05f)
				{
					if (magnitude > num)
					{
						vector7 = vector6 + (vector5 - vector6).normalized * num;
						this.currentRope.lstSegments[i].body.AddForce(vector8 * this.FLC.iceCollisionForce, this.FLC.iceCollisionForceMode);
						this.currentRope.lstSegments[i].seg.transform.position = vector7;
						if (list.Count != 0 || i > 0)
						{
						}
					}
					list.Add(i);
				}
			}
			this.currentRope.generateOverallMesh();
		}
	}

	// Token: 0x06002B1D RID: 11037 RVA: 0x000F6DB8 File Offset: 0x000F4FB8
	public void UpdateFlyLine()
	{
		if (this.flyLineType == FishingLine.FlyLineType.SINKING)
		{
			return;
		}
		if (!this.fishingHands.baitWasThrown)
		{
			return;
		}
		if (this.fishingHands.fishingPlayer.fish)
		{
			return;
		}
		if (this.currentRope.lstSegments.Count <= 2)
		{
			return;
		}
		int num = ((this.fishingHands.bait.flyType != Bait.FlyType.DRY) ? 2 : 1);
		if (this.flyLineType == FishingLine.FlyLineType.SINK_TIP)
		{
			num = 3;
		}
		Vector3 vector = Vector3.zero;
		for (int i = 0; i < this.currentRope.lstSegments.Count - num; i++)
		{
			vector = this.currentRope.lstSegments[i].seg.transform.position;
			if (vector.y < 0f)
			{
				vector.y = 0f;
				this.currentRope.lstSegments[i].seg.transform.position = vector;
			}
		}
		this.currentRope.generateOverallMesh();
	}

	// Token: 0x06002B1E RID: 11038 RVA: 0x000F6ED8 File Offset: 0x000F50D8
	public void UpdateReelLine()
	{
		if (this.fishingHands.reelLineParent && this.fishingHands.reel.reelLineFinish)
		{
			if (this.fishingHands.updateLeftArmFly && !this.fishingHands.fishingRod.isOnRodStand)
			{
				this.fishingHands.reelLineParent.position = this.fishingHands.flyReelLineParent.transform.position;
				this.fishingHands.reelLine.transform.localScale = new Vector3(this.fishingHands.reelLine.transform.localScale.x, Vector3.Distance(this.fishingHands.fishingRod.reelLineAttach.position, this.fishingHands.reelLine.transform.position) / this.fishingHands.fishingPlayer.vrHandsParent.localScale.y, this.fishingHands.reelLine.transform.localScale.z);
				this.fishingHands.reelLineParent.LookAt(this.fishingHands.fishingRod.reelLineAttach.position, base.transform.right);
			}
			else
			{
				this.fishingHands.reelLine.transform.localScale = new Vector3(this.fishingHands.reelLine.transform.localScale.x, Vector3.Distance(this.fishingHands.fishingRod.reelLineAttach.position, this.fishingHands.reel.reelLineFinish.position) / this.fishingHands.fishingPlayer.vrHandsParent.localScale.y, this.fishingHands.reelLine.transform.localScale.z);
				this.fishingHands.reelLineParent.position = this.fishingHands.reel.reelLineFinish.position;
				this.fishingHands.reelLineParent.LookAt(this.fishingHands.fishingRod.reelLineAttach.position, base.transform.right);
			}
		}
	}

	// Token: 0x06002B1F RID: 11039 RVA: 0x000F712C File Offset: 0x000F532C
	[Button]
	public void CalculateWeightDurability()
	{
		if (this.durability <= 1f)
		{
			this.weightDurability = Mathf.Lerp(0f, 40f, Mathf.Pow(this.durability, 2f));
		}
		else
		{
			this.weightDurability = Mathf.Lerp(45f, 85f, Mathf.Pow(this.durability - 1f, 2f));
		}
	}

	// Token: 0x06002B20 RID: 11040 RVA: 0x000F71A0 File Offset: 0x000F53A0
	public void RemoveLineOverhead(float addPercent)
	{
		this.CalculateLooseLengths();
		if (this.looseLengthColliders < 0f)
		{
			return;
		}
		float num = Mathf.Max(this.looseLength, this.looseLengthColliders);
		if (num > 0.005f)
		{
			num += this.currentRope.getLength() * addPercent;
		}
		num = Mathf.Clamp(num, 0f, 666f);
		Debug.Log(string.Concat(new object[] { "lineOverhead: ", num, " looseLength ", this.looseLength, " looseLengthColliders ", this.looseLengthColliders }));
		this.currentRope.changeLength(-num);
	}

	// Token: 0x06002B21 RID: 11041 RVA: 0x000F725C File Offset: 0x000F545C
	[Button]
	public void RemoveLineOverheadZero()
	{
		this.RemoveLineOverhead(0f);
	}

	// Token: 0x06002B22 RID: 11042 RVA: 0x000F7269 File Offset: 0x000F5469
	[Button]
	public void RemoveLineOverheadOne()
	{
		this.RemoveLineOverhead(0.1f);
	}

	// Token: 0x06002B23 RID: 11043 RVA: 0x000F7276 File Offset: 0x000F5476
	[Button]
	public void SetLengthWithOutTensionTest()
	{
		this.SetLengthWithTension(0f);
	}

	// Token: 0x06002B24 RID: 11044 RVA: 0x000F7283 File Offset: 0x000F5483
	[Button]
	public void SetLengthWithTensionTest()
	{
		this.SetLengthWithTension(0.1f);
	}

	// Token: 0x06002B25 RID: 11045 RVA: 0x000F7290 File Offset: 0x000F5490
	public void SetLengthWithTension(float tension)
	{
		float num = this.fishingRod.GetThrowObjectDistance();
		num *= 1f - tension;
		this.currentRope.setLength(num);
	}

	// Token: 0x06002B26 RID: 11046 RVA: 0x000F72BF File Offset: 0x000F54BF
	[Button]
	public void FixLooseColliders()
	{
		if (this.stretchFactor > 0f)
		{
			this.currentRope.changeLength(this.stretchFactor);
		}
	}

	// Token: 0x06002B27 RID: 11047 RVA: 0x00007702 File Offset: 0x00005902
	[Button]
	public void SetLineLength()
	{
	}

	// Token: 0x06002B28 RID: 11048 RVA: 0x000F72E2 File Offset: 0x000F54E2
	[Button]
	public void AddLineLength()
	{
		this.currentRope.changeLength(0.1f);
	}

	// Token: 0x04003030 RID: 12336
	public FishingLine.LineType lineType;

	// Token: 0x04003031 RID: 12337
	public FishingLine.LineColor colorType;

	// Token: 0x04003032 RID: 12338
	public FishingLine.FlyLineType flyLineType;

	// Token: 0x04003033 RID: 12339
	public GameObject model;

	// Token: 0x04003034 RID: 12340
	public MeshRenderer lineModel;

	// Token: 0x04003035 RID: 12341
	public Material lineMaterial;

	// Token: 0x04003036 RID: 12342
	public Material lineMaterialOpaque;

	// Token: 0x04003037 RID: 12343
	public Color lineColor = Color.white;

	// Token: 0x04003038 RID: 12344
	public Color emissionColor = Color.white;

	// Token: 0x04003039 RID: 12345
	public float thikness = 0.3f;

	// Token: 0x0400303A RID: 12346
	public float thiknessDynamic = 0.09f;

	// Token: 0x0400303B RID: 12347
	public float thiknessStatic = 0.02f;

	// Token: 0x0400303C RID: 12348
	public float durability = 0.5f;

	// Token: 0x0400303D RID: 12349
	public float weightDurability;

	// Token: 0x0400303E RID: 12350
	public float scareFactor;

	// Token: 0x0400303F RID: 12351
	public bool useDragTension = true;

	// Token: 0x04003040 RID: 12352
	private float reelInFactor = 1.7f;

	// Token: 0x04003041 RID: 12353
	[ReadOnly]
	[Space(10f)]
	public float currentTension;

	// Token: 0x04003042 RID: 12354
	[HideInInspector]
	public float prevTension;

	// Token: 0x04003043 RID: 12355
	[ReadOnly]
	public float breakTensionTimer = 3f;

	// Token: 0x04003044 RID: 12356
	[ReadOnly]
	public float currentTryReelTension;

	// Token: 0x04003045 RID: 12357
	[ReadOnly]
	public float currentPumpTension;

	// Token: 0x04003046 RID: 12358
	[ReadOnly]
	public float tryPullTension;

	// Token: 0x04003047 RID: 12359
	[ReadOnly]
	public float pumpTension;

	// Token: 0x04003048 RID: 12360
	[HideInInspector]
	public FishingRod fishingRod;

	// Token: 0x04003049 RID: 12361
	[HideInInspector]
	public FishingReel fishingReel;

	// Token: 0x0400304A RID: 12362
	[HideInInspector]
	public FishingHands fishingHands;

	// Token: 0x0400304B RID: 12363
	[HideInInspector]
	public FishingLineController FLC;

	// Token: 0x0400304C RID: 12364
	[HideInInspector]
	public Rope currentRope;

	// Token: 0x0400304D RID: 12365
	[HideInInspector]
	public Rope floatRope;

	// Token: 0x0400304E RID: 12366
	[HideInInspector]
	public float isReeling;

	// Token: 0x0400304F RID: 12367
	[ReadOnly]
	public bool isLoose = true;

	// Token: 0x04003050 RID: 12368
	[ReadOnly]
	public bool isLooseColliders = true;

	// Token: 0x04003051 RID: 12369
	[ReadOnly]
	public bool isLooseCanReel = true;

	// Token: 0x04003052 RID: 12370
	[ReadOnly]
	public float looseTensionFactor;

	// Token: 0x04003053 RID: 12371
	[ReadOnly]
	public float looseTensionFactorColliders;

	// Token: 0x04003054 RID: 12372
	[ReadOnly]
	public float stretchFactor;

	// Token: 0x04003055 RID: 12373
	[ReadOnly]
	public float stretchToDistance;

	// Token: 0x04003056 RID: 12374
	[ReadOnly]
	public float stretchTimer;

	// Token: 0x04003057 RID: 12375
	[ReadOnly]
	public float looseLength;

	// Token: 0x04003058 RID: 12376
	[ReadOnly]
	public float looseLengthColliders;

	// Token: 0x04003059 RID: 12377
	[ReadOnly]
	public float looseLengthFloat;

	// Token: 0x0400305A RID: 12378
	[ReadOnly]
	public bool canReelOut = true;

	// Token: 0x0400305B RID: 12379
	[ReadOnly]
	public float currentThrowStrength01;

	// Token: 0x0400305C RID: 12380
	[ReadOnly]
	public float flyRegenerateTimer;

	// Token: 0x0400305D RID: 12381
	[ReadOnly]
	public float fakeStraightLineTimer;

	// Token: 0x0400305E RID: 12382
	[HideInInspector]
	public GameController gameController;

	// Token: 0x0400305F RID: 12383
	[HideInInspector]
	public bool dontUpdate;

	// Token: 0x04003060 RID: 12384
	[HideInInspector]
	public bool canUseProtection = true;

	// Token: 0x04003061 RID: 12385
	[ReadOnly]
	public FishingLine.LineState lineState;

	// Token: 0x02000739 RID: 1849
	public enum LineType
	{
		// Token: 0x04003063 RID: 12387
		MONO,
		// Token: 0x04003064 RID: 12388
		BRAID,
		// Token: 0x04003065 RID: 12389
		FLUORO,
		// Token: 0x04003066 RID: 12390
		FLY
	}

	// Token: 0x0200073A RID: 1850
	public enum LineColor
	{
		// Token: 0x04003068 RID: 12392
		WHITE,
		// Token: 0x04003069 RID: 12393
		YELLOW,
		// Token: 0x0400306A RID: 12394
		RED,
		// Token: 0x0400306B RID: 12395
		GREEN,
		// Token: 0x0400306C RID: 12396
		PURPLE,
		// Token: 0x0400306D RID: 12397
		BLACK
	}

	// Token: 0x0200073B RID: 1851
	public enum FlyLineType
	{
		// Token: 0x0400306F RID: 12399
		FLOATING,
		// Token: 0x04003070 RID: 12400
		SINKING,
		// Token: 0x04003071 RID: 12401
		SINK_TIP
	}

	// Token: 0x0200073C RID: 1852
	public enum LineState
	{
		// Token: 0x04003073 RID: 12403
		IDLE,
		// Token: 0x04003074 RID: 12404
		FLY,
		// Token: 0x04003075 RID: 12405
		REEL_IN,
		// Token: 0x04003076 RID: 12406
		REEL_OUT,
		// Token: 0x04003077 RID: 12407
		NEAR_COAST
	}
}
