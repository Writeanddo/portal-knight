using System;
using System.Collections;
using System.Collections.Generic;
using NaughtyAttributes;
using Thuleanx.AI.FSM;
using Thuleanx.Combat3D;
using Thuleanx.PrettyPatterns;
using Thuleanx.Utils;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using FMODUnity;
using DG.Tweening;
#if UNITY_EDITOR
using UnityEditor;
#endif

using Yarn.Unity;
using Thuleanx.PortalKnight.Dialogue;
using Thuleanx.PortalKnight.Effects;
using Thuleanx.Audio;

namespace Thuleanx.PortalKnight {
	public partial class Player {
		public enum State {
			Neutral,
			Attack,
			Dash,
			Special,
			Dead
		};

		public enum ActionType {
			Attack = 0,
			Shoot = 1,
			Dash = 2,
			Special = 3
		};
	}

	[RequireComponent(typeof(PlayerInputChain))]
	[RequireComponent(typeof(CharacterController))]
	public partial class Player : Animated {
		#region Components
		public StateMachine<Player> StateMachine {get; private set;}
		public CharacterController Controller {get; private set; }
		public PlayerInputChain Input {get; private set; }
		public Status Status => Puppet.Status;
		public List<Renderer> renderers {get; private set; }
		#endregion

		#region Animations
		[HorizontalLine(color:EColor.Red)]
		[BoxGroup("Animations"), AnimatorParam("Anim"), SerializeField] string speedVariable;
		[BoxGroup("Animations"), AnimatorParam("Anim"), SerializeField] string attackTrigger;
		[BoxGroup("Animations"), AnimatorParam("Anim"), SerializeField] string attack2Trigger;
		[BoxGroup("Animations"), AnimatorParam("Anim"), SerializeField] string novaTrigger;
		[BoxGroup("Animations"), AnimatorParam("Anim"), SerializeField] string dashTrigger;
		[BoxGroup("Animations"), AnimatorParam("Anim"), SerializeField] string neutralTrigger;
		[BoxGroup("Animations"), AnimatorParam("Anim"), SerializeField] string deathTrigger;
		#endregion

		#region Movement
		[HorizontalLine(color:EColor.Blue)]
		[BoxGroup("Movement"), Range(0, 10), SerializeField] float speed = 4;
		[BoxGroup("Movement"), Range(0, 64), SerializeField] float accelerationAlpha = 24;
		[BoxGroup("Movement"), Range(0, 64), SerializeField] float deccelerationAlpha = 12;
		[BoxGroup("Movement"), Range(0, 720), SerializeField] float turnSpeed = 24;

		[HorizontalLine(color:EColor.Violet)]
		[BoxGroup("Dash"), Range(0, 4), SerializeField] float dashIframes = 1;
		[BoxGroup("Dash"), Range(0, 4), SerializeField] float dashCooldown = 1;
		[BoxGroup("Dash"), Range(0, 100), SerializeField] float dashSpeed = 10;
		[BoxGroup("Dash"), Range(0, 1), Tooltip("Dash duration in seconds"), SerializeField] float dashDuration = 1;
		[BoxGroup("Dash"), Range(0, 64), SerializeField] float dashDrag;
		[BoxGroup("Dash"), SerializeField] Material dashMaterial;
		[BoxGroup("Dash"), SerializeField] FlashMaterialEffect flashEffect;
		[BoxGroup("Dash"), SerializeField, Space] UnityEvent OnDash;
		#endregion
		
		#region Combat
		[HorizontalLine(color:EColor.Red)]
		[BoxGroup("General Combat"), Range(0, 4), SerializeField] float hitIframes = 1;
		[BoxGroup("General Combat"), Range(0, 2), SerializeField] float hurtSFXMuteDuration = 1;
		[BoxGroup("General Combat"), Range(0, 1), SerializeField] float damageFlashDuration = 0.2f;
		[BoxGroup("General Combat"), SerializeField] Material damageFlashMaterial;
		[BoxGroup("General Combat"), SerializeField] EventReference swordEquipSfx;

		[BoxGroup("Attack"), Range(0, 100), SerializeField] float nudgeSpeed = 40;
		[BoxGroup("Attack"), Range(0, 720), SerializeField] float attackTurnSpeed = 24;
		[BoxGroup("Attack"), Range(0, 10), SerializeField] int attackDamage = 1;
		[BoxGroup("Attack"), Range(0, 300), SerializeField] float attackKnockback = 20;
		[BoxGroup("Attack"), Range(0, 1), SerializeField] float attackCooldown = 0.5f;
		[BoxGroup("Attack"), Range(0,64), SerializeField] float attackDrag = 8f;
		[BoxGroup("Attack"), Required, SerializeField] Hitbox3D attackHitbox;
		[BoxGroup("Attack"), SerializeField] EventReference HitSound;
		[BoxGroup("Attack"), SerializeField, Space] UnityEvent OnAttack1;
		[BoxGroup("Attack"), SerializeField, Space] UnityEvent OnAttack2;
		[BoxGroup("Attack"), SerializeField, Space] UnityEvent<Vector3> OnAttackHit;
		[BoxGroup("Attack"), SerializeField, Required] GameObject Sword;
		[BoxGroup("Attack"), SerializeField] bool swordEquippedDefault = false;
		#endregion

		#region Spell Casting
		[BoxGroup("Spell"), Range(0, 720), SerializeField] float spellTurnSpeed = 24;
		[BoxGroup("Spell"), Range(1, 5), SerializeField] int manaOrbDamage;
		[BoxGroup("Spell"), Range(0, 10), SerializeField] float manaOrbMouseRange = 3;
		[BoxGroup("Spell"), Range(0, 10), SerializeField] float novaTrackingRange = 3;
		[BoxGroup("Spell"), Range(1, 5), SerializeField] int maxMana = 2;
		[BoxGroup("Spell"), Range(0, 1), SerializeField] float manaOnHit;
		[BoxGroup("Spell"), SerializeField, Required] BubblePool manaOrbPool;
		[BoxGroup("Spell"), SerializeField, Required] BubblePool novaPool;
		[BoxGroup("Spell"), SerializeField, Required] Transform manaOrbFiringSource;

		public float MaxMana => maxMana;
		public bool Interactible = true;

		float _mana;
		public float Mana {get => _mana; private set {
			value = Mathf.Clamp(value, 0, MaxMana);
			bool manaGained = value > _mana;
			bool manaUsed = value < _mana; 
			_mana = value;
			if (manaGained) OnManaGained?.Invoke();
			if (manaUsed) OnManaUse?.Invoke();
		}}
		public bool SwordEquipped {
			get => Sword.activeInHierarchy;
			private set => Sword.SetActive(value);
		}
		#endregion

		#region Events
		public UnityEvent OnManaGained;
		public UnityEvent OnManaUse;
		public UnityEvent OnManaInsufficient;
		#endregion

		public override void Awake() {
			base.Awake();
			StateMachine = GetComponent<StateMachine<Player>>();
			Controller = GetComponent<CharacterController>();
			Input = GetComponent<PlayerInputChain>();
			SwordEquipped = swordEquippedDefault;
			StateMachine.Construct();
		}

		public override void Start() {
			base.Start();
			StateMachine.Init();
			// Mana = MaxMana;

			var storage = App.instance.GetComponentInChildren<VariableStorage>();
			if (storage && storage.GetDeathCount() > 0) 
				SetPosition(GameObject.FindWithTag("Death Anchor").transform.position);
			Puppet.OnHit.AddListener(onHit);
			Debug.Log(Thuleanx.Audio.AudioManager.instance.gameObject);
		}

		protected override void Update() {
			transform.position = FindClosestNavPoint(transform.position);
			Input.ProcessInputs();
			StateMachine.RunUpdate();
			base.Update();
		}

		void LateUpdate() {
			Shader.SetGlobalVector("_Player_Position", transform.position + Vector3.up * Controller.height / 2);
			Anim?.SetFloat(speedVariable, Velocity.magnitude);
			FMODUnity.RuntimeManager.StudioSystem.setParameterByName("Health", Puppet.Status.Health);
		}

		void FixedUpdate() {
			StateMachine.RunFixUpdate();
		}

		Tween hitLowpass;
		void onHit(Puppet p) {
			p.GiveIframes(hitIframes);
			flashEffect.Flash(damageFlashMaterial, damageFlashDuration);
			hitLowpass?.Kill();
			hitLowpass = DOVirtual.Float(1, 0, hurtSFXMuteDuration, (x) => {
				FMODUnity.RuntimeManager.StudioSystem.setParameterByName("Hurt", x);
			}).OnKill(() => {
				FMODUnity.RuntimeManager.StudioSystem.setParameterByName("Hurt", 0);
			});
		}

		protected override void Move(Vector3 displacement) {
			if (displacement.sqrMagnitude > 0)  {
				displacement = AdjustVelocityToSlope(displacement, Controller.slopeLimit);
				Vector3 nxtPos = displacement + transform.position;
				if (FindClosestNavPoint(nxtPos, out Vector3 adjustedNxtPos)) 
					Controller.Move(adjustedNxtPos - transform.position);
				else Controller.Move(Physics.gravity * Time.deltaTime);
			}
		}
		protected override void OnDeath(Puppet puppet) {
			StateMachine.SetState((int)State.Dead);
		}

		public override void Reanimated() {}
		public override void Vanquish() {}

		public void SetPosition(Vector3 pos) {
			Controller.enabled = false;
			transform.position = pos;
			Controller.enabled = true;
		}

		IEnumerator iWaitForAnimationWhileTurn(Vector3 desiredRotation, float turnSpeed) {
			StartAnimationWait();
			while (WaitingForTrigger) {
				TurnToFace(desiredRotation, turnSpeed);
				yield return null;
			}
		}

		public void _Nudge() {
			Vector3 nudge = transform.forward * nudgeSpeed;
			Velocity += nudge;
		}

		#if UNITY_EDITOR
		[MenuItem("Thuleanx/Player/EquipSword")]
		#endif
		[YarnCommand("equip_sword")]
		static void yarn_SwordEquip() {
			Player player = GameObject.FindObjectOfType<Player>();
			player.SwordEquipped = true;
			AudioManager.instance?.PlayOneShot(player.swordEquipSfx);
		}
	}
}
