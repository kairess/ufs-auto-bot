using System;
using BitStrap;
using UnityEngine;

// Token: 0x0200072F RID: 1839
public class FishingFloat : MonoBehaviour
{
	// Token: 0x06002A48 RID: 10824 RVA: 0x000EC8D0 File Offset: 0x000EAAD0
	private void Awake()
	{
		this.modelResizeFactor = this.resizeParent.localScale.x;
	}

	// Token: 0x06002A49 RID: 10825 RVA: 0x000EC8F8 File Offset: 0x000EAAF8
	private void Start()
	{
		if (base.transform.parent == null)
		{
			base.gameObject.SetActive(false);
		}
		if (GameController.Instance.iceLevel)
		{
			this.particleHit = global::UnityEngine.Object.Instantiate<ParticleSystem>(GameController.Instance.waterEffectsManager.GetSplashParticlePrefab(WaterEffectsManager.SplashSize.VERY_SMALL));
		}
		else
		{
			this.particleHit = global::UnityEngine.Object.Instantiate<ParticleSystem>(GameController.Instance.waterEffectsManager.GetSplashParticlePrefab(WaterEffectsManager.SplashSize.SMALL));
		}
		this.particleHit.transform.parent = null;
		GameController.Instance.waterEffectsManager.InitParticleDisplacement(this.particleHit);
		this.setRopePosition = true;
		this.fishTriesForce.x = 2000f;
		this.fishTriesForce.y = 2000f;
		this.fishTriesForceMenuMultiplier = 4f;
		this.floatingSpeed = 0.01f;
		this.floatingMax = 0.01f;
	}

	// Token: 0x06002A4A RID: 10826 RVA: 0x000EC9DF File Offset: 0x000EABDF
	public void Initialize()
	{
		this.rigidbody = base.GetComponent<Rigidbody>();
		this.collider = base.GetComponent<Collider>();
		this.bait = this.fishingHands.bait;
	}

	// Token: 0x06002A4B RID: 10827 RVA: 0x000ECA0C File Offset: 0x000EAC0C
	private void Update()
	{
		if (Time.timeScale == 0f)
		{
			return;
		}
		if (!this.fishingHands.fishingPlayer)
		{
			return;
		}
		if (!this.fishingHands.baitWasThrown)
		{
			this.rigidbody.useGravity = true;
			this.rigidbody.drag = 1f;
			this.rigidbody.angularDrag = 1f;
			return;
		}
		this.isLoose = !this.bait.fish || this.fishingHands.fishingLine.isLoose;
		if (!this.isOnWater && base.transform.position.y <= 0f && this.canHitWater)
		{
			this.HitWater();
		}
		this.currentStandardOffset = this.standardOffset;
		if (!this.fishingHands.fishingPlayer.underwaterCamera.isTurnedOn)
		{
			this.currentStandardOffset *= this.fishingHands.floatSizeMultiplier;
		}
		this.burdenFactor = Mathf.Lerp(-1f, 1f, (this.optimalBurden - this.currentBurden) / (this.optimalBurden * 2f) + 0.5f);
		if (this.burdenFactor <= 0f)
		{
			this.burdenOffset = Mathf.Lerp(0f, this.burdenOffsetMinMax.x, -this.burdenFactor);
		}
		else if (this.burdenFactor == 1f)
		{
			this.burdenOffset = -this.currentStandardOffset * 1.2f;
		}
		else if (this.burdenFactor > 0f)
		{
			this.burdenOffset = Mathf.Lerp(0f, -this.currentStandardOffset, this.burdenFactor);
		}
		else
		{
			this.burdenOffset = -this.currentStandardOffset;
		}
		this.UpdateFloatingOffset();
		float num = this.currentStandardOffset + this.burdenOffset + this.floatingEasedOffset;
		if (base.transform.position.y < num)
		{
			this.rigidbody.mass = this.mass;
			if (this.isLoose)
			{
				if (this.burdenFactor > -1f)
				{
					base.transform.position = Vector3.MoveTowards(base.transform.position, new Vector3(base.transform.position.x, num, base.transform.position.z), FishingFloat.moveSurfaceSpeed * Time.deltaTime);
					this.rigidbody.velocity = Vector3.zero;
					this.rigidbody.angularVelocity = Vector3.zero;
					this.rigidbody.useGravity = false;
					this.rigidbody.drag = 5f;
					this.rigidbody.angularDrag = 5f;
				}
				else
				{
					this.rigidbody.useGravity = true;
					this.rigidbody.drag = 15f;
					this.rigidbody.angularDrag = 1f;
				}
			}
			else
			{
				this.rigidbody.useGravity = false;
				this.rigidbody.drag = 15f;
				this.rigidbody.angularDrag = 20f;
			}
		}
		else
		{
			this.rigidbody.useGravity = true;
			this.rigidbody.drag = 1f;
			this.rigidbody.angularDrag = 1f;
		}
		if (this.fishingHands.fishingPlayer.fish && this.fishingHands.fishingPlayer.fish.storedBait != this.bait)
		{
			this.isTryAnimation = false;
		}
		else
		{
			this.isTryAnimation = (this.fishingHands.fishingPlayer.fish && this.fishingHands.fishingPlayer.fish.isTryingBait) || (this.isOnWater && this.fishTriesTimer >= 0f && (!this.fishingHands.fishingPlayer.fish || this.fishingHands.fishingPlayer.fish.isTryingBait));
		}
		if (this.isTryAnimation)
		{
			this.FishTriesBaitAnimation();
		}
	}

	// Token: 0x06002A4C RID: 10828 RVA: 0x000ECE70 File Offset: 0x000EB070
	private void FixedUpdate()
	{
		if (Time.timeScale == 0f)
		{
			return;
		}
		if (!this.fishingHands || !this.fishingHands.fishingPlayer)
		{
			return;
		}
		if (this.setRopePosition && !this.fishingHands.fishingPlayer.fish)
		{
			this.fishingHands.floatRope.transform.position = base.transform.position;
		}
	}

	// Token: 0x06002A4D RID: 10829 RVA: 0x000ECEF8 File Offset: 0x000EB0F8
	private void LateUpdate()
	{
		if (Time.timeScale == 0f)
		{
			return;
		}
		if (!this.fishingHands || !this.fishingHands.fishingPlayer)
		{
			return;
		}
		if (this.resizeParent)
		{
			if (this.fishingHands.baitWasThrown && !this.fishingHands.fishingPlayer.underwaterCamera.isTurnedOn)
			{
				this.resizeParent.localScale = Vector3.one * this.modelResizeFactor * this.fishingHands.floatSizeMultiplier;
			}
			else
			{
				this.resizeParent.localScale = Vector3.one * this.modelResizeFactor;
			}
		}
		if (!this.fishingHands.baitWasThrown || !this.isOnWater)
		{
			return;
		}
		if (this.setRopePosition && !this.fishingHands.fishingPlayer.fish)
		{
			this.fishingHands.floatRope.transform.position = base.transform.position;
		}
		Vector3 vector = Utilities.GetVectorAngles180Range(base.transform.eulerAngles);
		if (this.isTryAnimation)
		{
			vector.x = Mathf.Clamp(vector.x, -5f, 5f);
			vector.z = Mathf.Clamp(vector.z, -5f, 5f);
			base.transform.eulerAngles = vector;
		}
		else if (!this.newPhysics && this.fishingHands.fishingLine.isLoose)
		{
			if (this.burdenFactor <= 0f)
			{
				vector.x = Mathf.Clamp(vector.x, -5f, 5f);
				vector.z = Mathf.Clamp(vector.z, -5f, 5f);
			}
			else
			{
				vector.x = Mathf.Clamp(vector.x, -90f, 90f);
				vector.z = Mathf.Clamp(vector.z, -90f, 90f);
			}
			base.transform.eulerAngles = Vector3.MoveTowards(Utilities.GetVectorAngles180Range(base.transform.eulerAngles), vector, FishingFloat.rotateSurfaceSpeed * Time.deltaTime);
			bool flag = true;
			if (this.fishingHands.fishingPlayer.fish || this.fishingHands.fishingPlayer.junk)
			{
				flag = false;
			}
			if (this.burdenFactor <= 0f)
			{
				flag = false;
			}
			if (this.rigidbody.velocity.sqrMagnitude > 1f)
			{
				flag = false;
			}
			if (flag)
			{
				vector = Utilities.GetVectorAngles180Range(this.model.transform.localEulerAngles);
				this.burdenRotation = Mathf.Lerp(0f, 89f, Mathf.Pow(this.burdenFactor, 2f));
				vector.x = Mathf.MoveTowards(vector.x, this.burdenRotation, FishingFloat.rotateSurfaceSpeed * Time.deltaTime * 3f);
				vector.y = 180f;
				vector.z = 0f;
				this.model.transform.localEulerAngles = vector;
			}
		}
		vector = Utilities.GetVectorAngles180Range(this.model.transform.eulerAngles);
		vector.x = Mathf.MoveTowards(vector.x, Mathf.Clamp(vector.x, -89f, 89f), FishingFloat.rotateSurfaceSpeed * Time.deltaTime * 10f);
		this.model.transform.eulerAngles = vector;
	}

	// Token: 0x06002A4E RID: 10830 RVA: 0x000ED2BC File Offset: 0x000EB4BC
	public void UpdateFloatingOffset()
	{
		this.floatingOffset += Time.deltaTime * this.floatingSpeed * ((this.floatingTarget <= 0f) ? (-1f) : 1f);
		if ((this.floatingTarget < 0f && this.floatingOffset <= this.floatingTarget) || (this.floatingTarget > 0f && this.floatingOffset >= this.floatingTarget))
		{
			this.floatingPrevTarget = this.floatingTarget;
			this.floatingTarget = global::UnityEngine.Random.Range(0.01f, (this.floatingTarget >= 0f) ? (this.floatingMax * 2f) : this.floatingMax) * ((this.floatingTarget <= 0f) ? 1f : (-1f));
		}
		float num = Mathf.Abs(this.floatingPrevTarget - this.floatingTarget);
		float num2 = Mathf.Abs(this.floatingOffset - this.floatingTarget);
		this.floatingEasedOffset = Mathf.Lerp(this.floatingPrevTarget, this.floatingTarget, this.floatingCurve.Evaluate((num - num2) / num));
	}

	// Token: 0x06002A4F RID: 10831 RVA: 0x000ED3F4 File Offset: 0x000EB5F4
	public float GetDepth()
	{
		return base.transform.position.y;
	}

	// Token: 0x06002A50 RID: 10832 RVA: 0x000ED414 File Offset: 0x000EB614
	private void OnCollisionEnter(Collision col)
	{
		if (!this.isOnWater)
		{
			this.fishingHands.fishingRod.fishingPlayer.LineBreak(Utilities.GetTranslation("HUD_MESSAGE/BAIT_HIT_OBSTACLE", false), 0f);
			this.fishingHands.fishingPlayer.BlockMouseLook(false, 4f);
			Debug.Log(string.Concat(new object[]
			{
				"Float collide with: ",
				col.gameObject.name,
				" at pos: ",
				base.transform.position.y
			}));
		}
	}

	// Token: 0x06002A51 RID: 10833 RVA: 0x000ED4B0 File Offset: 0x000EB6B0
	public void ResetFloat()
	{
		base.transform.parent = GameController.Instance.transform;
		float num = this.fishingHands.baitStartPosition.y;
		if (this.fishingHands.floatRopeDepth > 1.5f)
		{
			num -= this.fishingHands.floatRopeDepth - 1.5f;
		}
		base.transform.position = this.fishingHands.fishingRod.rodEndPosition.position + new Vector3(0f, -num, 0f);
		this.rigidbody.isKinematic = false;
		this.isOnWater = false;
		this.canHitWater = false;
		this.rigidbody.mass = 10f;
		this.rigidbody.drag = 1f;
		this.rigidbody.angularDrag = 1f;
		this.rigidbody.velocity = Vector3.zero;
		this.rigidbody.angularVelocity = Vector3.zero;
		this.rigidbody.constraints = RigidbodyConstraints.None;
		this.floatingOffset = 0f;
		this.floatingTarget = -this.floatingMax;
		this.floatingPrevTarget = 0f;
		this.fishTriesTimer = -1f;
		this.fishTriesForceTimer = -1f;
		this.fishTriesPullTimer = 0f;
		this.fishTriesRippleTimer = -1f;
	}

	// Token: 0x06002A52 RID: 10834 RVA: 0x000ED607 File Offset: 0x000EB807
	public void StartThrowFloat()
	{
		this.rigidbody.mass = 1f;
		this.rigidbody.drag = 0f;
	}

	// Token: 0x06002A53 RID: 10835 RVA: 0x000ED62C File Offset: 0x000EB82C
	public void ThrowFloat()
	{
		this.fishingHands.fishingFloat.rigidbody.isKinematic = true;
		this.fishingHands.fishingFloat.collider.isTrigger = false;
		LeanTween.delayedCall((!VRManager.Instance.IsVRHoldRod()) ? 1.5f : 0.2f, delegate
		{
			this.canHitWater = true;
		});
	}

	// Token: 0x06002A54 RID: 10836 RVA: 0x000ED698 File Offset: 0x000EB898
	public void HitWater()
	{
		if (this.isOnWater)
		{
			return;
		}
		AudioController.Play("SplashBait_01", base.transform.position, base.transform, 1f, 0f, 0f);
		this.isOnWater = true;
		this.Splash();
		this.rigidbody.isKinematic = false;
		this.fishingHands.ThrowObjectHitWater();
		this.fishingHands.fishingPlayer.floatCamera.TurnOn(true);
		if (GlobalSettings.Instance && GlobalSettings.Instance.playerSettings.IsCasual())
		{
			if (this.currentBurden >= this.optimalBurden * 2f)
			{
				HUDManager.Instance.ShowMessage(Utilities.GetTranslation("HUD_MESSAGE/FLOAT_TOO_HEAVY", false), 7f);
			}
			else if (this.currentBurden == 0f)
			{
				HUDManager.Instance.ShowMessage(Utilities.GetTranslation("HUD_MESSAGE/FLOAT_NO_WEIGHT", false), 7f);
			}
		}
	}

	// Token: 0x06002A55 RID: 10837 RVA: 0x000ED799 File Offset: 0x000EB999
	public float GetBaitFloatDistance()
	{
		return Vector3.Distance(this.ropeBaitRigidbody.transform.position, this.bait.transform.position);
	}

	// Token: 0x06002A56 RID: 10838 RVA: 0x000ED7C0 File Offset: 0x000EB9C0
	public void Splash()
	{
		this.particleHit.transform.position = new Vector3(base.transform.position.x, (!GameController.Instance.iceLevel) ? 0.3f : 0f, base.transform.position.z);
		this.particleHit.transform.eulerAngles = new Vector3(-90f, 0f, 0f);
		this.particleHit.Play(true);
		GameController.Instance.waterEffectsManager.SplashWakeEffect(this.particleHit.transform.position, 3f);
	}

	// Token: 0x06002A57 RID: 10839 RVA: 0x000ED87C File Offset: 0x000EBA7C
	public void FishTriesBaitAnimation()
	{
		this.fishTriesTimer += Time.deltaTime;
		this.fishTriesRippleTimer -= Time.deltaTime;
		this.fishTriesForceTimer -= Time.deltaTime;
		float num = Mathf.InverseLerp(0f, 4f, this.fishTriesTimer);
		if (this.fishTriesPullTimer > 0f)
		{
			this.fishTriesPullTimer -= Time.deltaTime;
			Vector3 vector = Vector3.down * this.fishTriesCurrentForce * num * FishingFloat.moveSurfaceSpeed;
			if (GlobalSettings.Instance)
			{
				vector *= this.fishTriesForceMenuMultiplier;
			}
			vector *= 1f - Mathf.Abs(this.burdenFactor);
			this.rigidbody.AddForce(vector);
			if (this.fishTriesRippleTimer <= 0f)
			{
				GameController.Instance.PlayWaterRipple(base.transform.position, Mathf.Lerp(10f, 50f, num), Mathf.Lerp(0.6f, 1f, num));
				this.fishTriesRippleTimer = global::UnityEngine.Random.Range(2f, 2.5f) - num;
			}
		}
		else if (this.fishTriesForceTimer < 0f)
		{
			this.fishTriesForceTimer = global::UnityEngine.Random.Range(0.8f, 1.2f);
			this.fishTriesPullTimer = global::UnityEngine.Random.Range(0.1f, 0.2f);
			this.fishTriesCurrentForce = global::UnityEngine.Random.Range(this.fishTriesForce.x, this.fishTriesForce.y);
		}
	}

	// Token: 0x06002A58 RID: 10840 RVA: 0x000EDA14 File Offset: 0x000EBC14
	[Button]
	public void StartTriesAnim()
	{
		this.fishTriesTimer = 0f;
		this.fishTriesRippleTimer = 3f;
	}

	// Token: 0x04002F17 RID: 12055
	public FishingFloat.Shape shape;

	// Token: 0x04002F18 RID: 12056
	public bool setRopePosition;

	// Token: 0x04002F19 RID: 12057
	public GameObject ropeRigidbody;

	// Token: 0x04002F1A RID: 12058
	public GameObject ropeBaitRigidbody;

	// Token: 0x04002F1B RID: 12059
	[ReadOnly]
	public Collider collider;

	// Token: 0x04002F1C RID: 12060
	[Space(10f)]
	public Transform model;

	// Token: 0x04002F1D RID: 12061
	public Transform resizeParent;

	// Token: 0x04002F1E RID: 12062
	[ReadOnly]
	public float modelResizeFactor = 1f;

	// Token: 0x04002F1F RID: 12063
	public float mainRopeOffset;

	// Token: 0x04002F20 RID: 12064
	public float standardOffset;

	// Token: 0x04002F21 RID: 12065
	[ReadOnly]
	public float currentStandardOffset;

	// Token: 0x04002F22 RID: 12066
	[Header("Burden")]
	public float length = 20f;

	// Token: 0x04002F23 RID: 12067
	public float realMass = 2.5f;

	// Token: 0x04002F24 RID: 12068
	public float mass = 30f;

	// Token: 0x04002F25 RID: 12069
	public float currentBurden = 1f;

	// Token: 0x04002F26 RID: 12070
	public float optimalBurden = 1f;

	// Token: 0x04002F27 RID: 12071
	[ReadOnly]
	public float burdenFactor;

	// Token: 0x04002F28 RID: 12072
	[ReadOnly]
	public float burdenOffset;

	// Token: 0x04002F29 RID: 12073
	[ReadOnly]
	public float burdenRotation;

	// Token: 0x04002F2A RID: 12074
	public Vector2 burdenOffsetMinMax = Vector2.zero;

	// Token: 0x04002F2B RID: 12075
	[ReadOnly]
	[Header("Floating")]
	public float floatingTimer;

	// Token: 0x04002F2C RID: 12076
	[ReadOnly]
	public float floatingOffset;

	// Token: 0x04002F2D RID: 12077
	[ReadOnly]
	public float floatingEasedOffset;

	// Token: 0x04002F2E RID: 12078
	public float floatingSpeed = 0.04f;

	// Token: 0x04002F2F RID: 12079
	public float floatingMax = 0.03f;

	// Token: 0x04002F30 RID: 12080
	[ReadOnly]
	public float floatingTarget;

	// Token: 0x04002F31 RID: 12081
	[ReadOnly]
	public float floatingPrevTarget;

	// Token: 0x04002F32 RID: 12082
	public AnimationCurve floatingCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

	// Token: 0x04002F33 RID: 12083
	[ReadOnly]
	[Header("FishTriesBait")]
	public float fishTriesTimer;

	// Token: 0x04002F34 RID: 12084
	[ReadOnly]
	public float fishTriesForceTimer;

	// Token: 0x04002F35 RID: 12085
	[ReadOnly]
	public float fishTriesPullTimer;

	// Token: 0x04002F36 RID: 12086
	[ReadOnly]
	public float fishTriesRippleTimer;

	// Token: 0x04002F37 RID: 12087
	public Vector2 fishTriesForce = Vector2.one;

	// Token: 0x04002F38 RID: 12088
	[ReadOnly]
	public float fishTriesCurrentForce;

	// Token: 0x04002F39 RID: 12089
	public float fishTriesForceMenuMultiplier = 2f;

	// Token: 0x04002F3A RID: 12090
	[Space(10f)]
	[HideInInspector]
	public Bait bait;

	// Token: 0x04002F3B RID: 12091
	[HideInInspector]
	public FishingHands fishingHands;

	// Token: 0x04002F3C RID: 12092
	[HideInInspector]
	public bool isOnWater;

	// Token: 0x04002F3D RID: 12093
	[HideInInspector]
	public Rigidbody rigidbody;

	// Token: 0x04002F3E RID: 12094
	[HideInInspector]
	public ParticleSystem particleHit;

	// Token: 0x04002F3F RID: 12095
	public static float moveSurfaceSpeed = 3f;

	// Token: 0x04002F40 RID: 12096
	public static float rotateSurfaceSpeed = 30f;

	// Token: 0x04002F41 RID: 12097
	[HideInInspector]
	public bool newPhysics;

	// Token: 0x04002F42 RID: 12098
	[ReadOnly]
	public bool isLoose = true;

	// Token: 0x04002F43 RID: 12099
	[ReadOnly]
	public bool isTryAnimation;

	// Token: 0x04002F44 RID: 12100
	[ReadOnly]
	public bool canHitWater;

	// Token: 0x02000730 RID: 1840
	public enum Shape
	{
		// Token: 0x04002F46 RID: 12102
		OVAL
	}
}
