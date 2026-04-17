/* README
 전투용 캐릭터 클래스.
 - 본 클래스는 캐릭터의 이동, 전투, 상태 이상 및 애니메이션을 총괄하는 핵심 Actor 클래스입니다.

1. 비동기 초기화 및 리소스 관리: Initialize 메서드에서 어드레서블과 풀링 시스템을 활용한 초기화 프로세스 구현.
2. 반응형 상태 제어: UniRx를 활용하여 UI 입력 및 상태 이상(Freeze, Poison) 변화를 실시간으로 반영.
3. 비동기 루프 연출: UpdateAnimationAsync를 통해 Update() 호출 없이도 효율적인 애니메이션 상태 업데이트 구현.
4. 저스트 회피 시스템: DashCharacter와 AddJustDashArea를 통한 타임 슬로우 및 연출 로직 구현.

 */

using Cysharp.Threading.Tasks;
using DG.Tweening;
using System;
using System.Collections.Generic;
using System.Threading;
using UniRx;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AI;

/// <summary>
/// 전투용 캐릭터 클래스.
/// </summary>

public class Character : MonoBehaviour, ITargetable, ICharacter
{
	protected Collider mCollider;
	protected Rigidbody rigidBody;
	protected NavMeshAgent nav;
	protected CompositeDisposable disposables;
	protected ICharacterData characterData;
	protected CharacterStatusControll characterState = new CharacterStatusControll();
	protected Dictionary<CharacterState, CharacterStateBall> stateBallBuffer
		= new Dictionary<CharacterState, CharacterStateBall>();
	protected StateController stateController;
	protected float moveSpeed;
	protected int characterIdx;
	protected int justDashAreaCount;

	private VFXCallbacks boomEffect;
	private Animator animator;
	protected AnimationCurve dashCurve;

	protected Transform mTransform;
	protected GameObject cachedGameObject;

	protected ISpawner hitEffectSpanwer;
	private ISpawner dashEffectSpawner;
	
	private Gun mGun;
	private int animationIdx;
	protected bool running;
	protected bool isDashing;
	protected bool attacking;
	protected float hitDuration;
	protected bool boom;
	protected bool victory;
	protected Vector3 moveVector;
	protected Vector3 dashVector;
	protected CancellationToken token;
	private AimAssistScanner animAssistantScanner = new AimAssistScanner();

	private float duration;
	private const int HealHP = 1;
	private const float DefaultAnimationDuration = 1f;
	private const string AnimationKey = "animation";
	private const string BoomAddress = "Assets/Polygon Arsenal/Prefabs/Combat/Orbital Beam/OrbitalBeamPink.prefab";
	private const string BoomSoundFirst = "Assets/voice_battle/tsukaeru_battle_female/audio/mp3/female_10/ganbatte_go_for_it.mp3";
	private const string BoomSoundEnd = "Assets/voice_battle/tsukaeru_battle_female/audio/mp3/female_10/good.mp3";
	private const string BoomCameraTrigger = "Boom";

	protected const int DashDelay = 500;
	protected const int JustDashDelay1 = 100;
	protected const int JustDashDelay2 = 1000;
	protected const float DefaultSpeed = 3.5f;
	private const float searchRadius = 8f;
	private const float searchAngle = 60f;

	public bool IsAnim(CharacterRikoType _type) => animationIdx == (int)_type && duration > 0.0f;

	public int CharacterIdx
	{
		get
		{
			return characterIdx;
		}
		set
		{
			characterIdx = value;
		}
	}
	public void SetActive(bool _value)
	{
		if (cachedGameObject == null)
			cachedGameObject = gameObject;
		cachedGameObject.SetActive(_value);
	}
	#region CALL_BATTLE_BEFORE
	public virtual async UniTask Initialize(CancellationToken _token)
    {
		try
		{
			victory = false;
			running = false;
			attacking = false;
			moveVector = Vector3.zero;
			token = _token;
			disposables = new CompositeDisposable();

			if (animator == null)
				animator = GetComponent<Animator>();
			if (mTransform == null)
				mTransform = transform;

			if (nav == null)
			{
				nav = GetComponent<NavMeshAgent>();
				if (nav == null)
					nav = gameObject.AddComponent<NavMeshAgent>();
			}

			if (mGun == null)
			{
				mGun = GetComponent<Gun>();
				if (mGun == null)
					mGun = gameObject.AddComponent<Gun>();
			}

			rigidBody = GetComponent<Rigidbody>();
			if (rigidBody == null)
				rigidBody = gameObject.AddComponent<Rigidbody>();
			mCollider = GetComponent<Collider>();

			stateController = GetComponent<StateController>();
			if (stateController == null)
				stateController = gameObject.AddComponent<StateController>();

			characterData = DataOne.Instance.CharacterData;

			Vector3 _status = characterData.GetTotalStatus();
			Vector2 _weaponData = characterData.GetWeaponData();
			JWeapon _weaponTable = JTableManager.Instance.GetWeapon(_one => _one.id == (int)_weaponData.x);
			JEffect _effectTable = JTableManager.Instance.GetEffect(_one => _one.id == _weaponTable.effect);

			moveSpeed = DefaultSpeed * (1f + _status.z * 0.1f);
			nav.enabled = true;
			nav.baseOffset = -0.1f;
			nav.speed = moveSpeed;
			nav.avoidancePriority = 10;
			nav.radius = 0.1f;
			rigidBody.isKinematic = true;
			mGun.UpdateFireAsync(_token);
			mGun.SetFireRate(0.5f);
			mGun.Set(_weaponData);
			mGun.SetATK((int)_status.x);
			mGun.SetHitSound("HitMonster");
			characterState.Initialize(mTransform);

			stateController.Set(_token);
			stateController.OnPriorityState.Subscribe(_value =>
			{
				OnPirorityState(_value);
				characterState.ChangeState(_value);
			}).AddTo(disposables);
			stateController.OnCurrentState.Subscribe(_bitmask =>
			{
				OnCurrentState(_bitmask);
			}).AddTo(disposables);

			GameObject _boom = await Addressables.InstantiateAsync(BoomAddress, mTransform).WithCancellation(_token);
			boomEffect = _boom.GetComponent<VFXCallbacks>();
			if (boomEffect == null)
				boomEffect = _boom.AddComponent<VFXCallbacks>();
			boomEffect.SetActive(false);

			Transform _transform = boomEffect.transform;
			_transform.localPosition = new Vector3(-13.5f, 0f, -29f);
			_transform.localRotation = Quaternion.Euler(-90f, 0f, -155f);
			_transform.localScale = new Vector3(25f, 25f, 10f);

			await SPoolManager.Instance.Preload(GameDefine.FreezeBallAddress, _token);
			await SPoolManager.Instance.Preload(GameDefine.PoisionBallAddress, _token);
			List<JWeapon> _weapons = JTableManager.Instance.GetWeapons(_one => _one.characterIdx == characterData.Idx);
			for (int i = 0; i < _weapons.Count; i++)
			{
				await SPoolManager.Instance.Preload(_weapons[i].address, _token);
				await SPoolManager.Instance.Preload(_weapons[i].UpAddress, _token);
			}

			gameObject.SetActive(false);
		}
		catch(OperationCanceledException)
		{
			throw;
		}
	}
	/// <summary>
	/// 연출 전 캐릭터 나옴.
	/// 캐릭터 연출용 이펙트 포함.
	/// </summary>
	/// <returns></returns>
	public void OnSpawned()
	{
		SetActive(true);
		mTransform.localScale = Vector3.zero;
		mTransform.DOScale(Vector3.one, 0.15f);
		PlayChangeVFX(false);
	}

	/// <summary>
	/// 게임 시작 전 3,2,1 후에 호출.
	/// </summary>
	/// <returns></returns>
	public virtual async UniTask ShowIntro()
	{
		SetActive(true);
		PlayAnimation((int)CharacterRikoType.IdleC);
		UpdateAnimationDurationAsync(token);
		UpdateAnimationAsync(token);
	}
	public void Disable()
	{
		DisableNavmeshAgent();
		if (animator != null)
		{
			animator.Rebind();
		}

		disposables?.Clear();
		if (boomEffect != null && boomEffect.gameObject != null)
		{
			Addressables.ReleaseInstance(boomEffect.gameObject);
			boomEffect = null;
		}

		mTransform.localPosition = Vector3.zero;
		mTransform.localScale = Vector3.one;
		gameObject.SetActive(false);
	}
	/// <summary>
	/// 이동 UI 구독.
	/// </summary>
	/// <param name="_vectorObservable"></param>
	public void Set(IObservable<Vector3> _vectorObservable)
	{
		_vectorObservable.Subscribe(_v =>
		{
			if (attacking || IsAnim(CharacterRikoType.CuteA) || victory)
				_v = Vector3.zero;

			moveVector = _v;
			running = moveVector != Vector3.zero;
			mTransform.LookAt(mTransform.position + _v);
		}).AddTo(disposables);
	}
	/// <summary>
	/// 캐릭터1(원거리) 공격 버튼 구독.
	/// </summary>
	/// <param name="_attackObservable"></param>
	public virtual void SetButton1Observable(IObservable<bool> _attackObservable)
	{
		if (mGun == null)
			mGun = GetComponent<Gun>();

		if (_attackObservable != null)
		{
			_attackObservable.Subscribe(_attacking =>
			{
				attacking = _attacking;
				mGun.SetFire(attacking);
				if (attacking)
				{
					Transform _target = GetAnimedTarget();
					if (_target != null)
						mTransform.LookAt(_target.position);
				}
			}).AddTo(disposables);
		}
	}
	/// <summary>
	/// 자동 조준 타겟 출력.
	/// </summary>
	/// <returns></returns>
	private Transform GetAnimedTarget()
	{
		Vector3 _result = mTransform.forward;
		LayerMask _monsterLayer = LayerMask.GetMask(GameDefine.LABEL_MONSTER);
		int _count = this.animAssistantScanner.Scan(_result, searchRadius, _monsterLayer);
		return AimAssistUtility.FindBestTarget(this.animAssistantScanner, _count, mTransform.position, _result, searchRadius, searchAngle);
	}
	public void SetHitEffectSpawner(ISpawner _hitEffectSpawner) => hitEffectSpanwer = _hitEffectSpawner;
	public void SetSpawnedEffectPool(ISpawner _spawningEffectPool) => dashEffectSpawner = _spawningEffectPool;
	public void SetDashCurve(AnimationCurve _dashCurve) => this.dashCurve = _dashCurve;
	#endregion

	#region WEAPON
	public virtual void SetWeaponLevel(int level) => mGun.SetWeaponLevel(level);
	public int GetWeaponLevel() => this.mGun.GetWeaponLevel();
	public virtual void SetWeapon(JWeapon _table)
	{
		if (mGun != null) mGun.Set(DataOne.Instance.CharacterData.GetWeaponData());
	}
	public int GetWeaponId()
	{
		Vector2 _weapon = DataOne.Instance.CharacterData.GetWeaponData();
		int _id = (int)_weapon.x;
		return _id;
	}
	#endregion

	#region ON_BATTLE

	/// <summary>
	/// 대쉬.
	/// </summary>
	public void OnDash()
	{
		if(token.IsCancellationRequested) return;
		DashCharacter();
	}
	/// <summary>
	/// 대쉬 연출.
	/// </summary>
	/// <returns></returns>
	public virtual async UniTask DashCharacter()
	{
		try
		{
			isDashing = true;
			mCollider.enabled = false;
			dashVector = moveVector != Vector3.zero ? moveVector : mTransform.forward * -1f;

			DisableNavmeshAgent();
			if (animator != null)
			{
				//animator.Rebind();
				PlayAnimation((int)CharacterRikoType.IdleC);
			}

			int _justDash = justDashAreaCount;
			justDashAreaCount = 0;
			if (_justDash > 0)
			{
				Time.timeScale = 0.1f;
				MessageBroker.Default.Publish(Messages.JustDash);
			}


			PlayChangeVFX();
			SetActive(true);
			mTransform.DOKill();
			mTransform.localScale = Vector3.zero;
			mTransform.DOMove(mTransform.position + dashVector * 2.5f, 0.5f).SetEase(dashCurve).SetUpdate(true);
			await UniTask.Delay(DashDelay, ignoreTimeScale: true, cancellationToken: token);

			mTransform.DOScale(Vector3.one, 0.1f).SetUpdate(true);
			DirectPosition();
			isDashing = false;

			PlayChangeVFX();

			if (_justDash > 0)
			{
				await UniTask.Delay(JustDashDelay1, ignoreTimeScale: true, cancellationToken: token);
				Time.timeScale = 1.0f;

				await UniTask.Delay(JustDashDelay2, ignoreTimeScale: true, cancellationToken: token);
				mCollider.enabled = true;
			}
			else
			{
				mCollider.enabled = true;
			}
		}
		catch (OperationCanceledException)
		{
			throw;
		}
	}
	/// <summary>
	/// 저스트 회피 히트박스에 닿임.
	/// </summary>
	public void AddJustDashArea()
	{
		if (isDashing)
		{
			justDashAreaCount = 0;
			return;
		}

		justDashAreaCount = Mathf.Min(1000000, justDashAreaCount + 1);
	}
	/// <summary>
	/// 저스트 회피 히트박스에서 벗어남.
	/// </summary>
	public void RemoveJustDasharea()
	{
		if (isDashing)
		{
			justDashAreaCount = 0;
			return;
		}

		justDashAreaCount = Mathf.Max(0, justDashAreaCount - 1);
	}
	/// <summary>
	/// 피격 코드.
	/// </summary>
	/// <param name="_parameter"> 데미지 </param>
	/// <param name="_effectParameter"> 공격 특수 효과(빙결, 독 등)</param>
	public virtual void OnHit(DamegeParameter _parameter, EffectParameter _effectParameter = default)
	{
		characterData.RemoveHP(_parameter.damege);

		// hit effect
		var _go = hitEffectSpanwer.Spawn();

		SpawnedObject _so = _go.GetComponent<SpawnedObject>();
		if (_so == null)
			_so = _go.AddComponent<SpawnedObject>();
		_so.Set(hitEffectSpanwer);

		AutoDisable _auto = _go.GetComponent<AutoDisable>();
		if (_auto == null)
			_auto = _go.AddComponent<AutoDisable>();
		_auto.DoDisable(1.0f);

		Transform _t = _go.transform;
		_t.position = mTransform.position + Vector3.up;
		_t.localScale = Vector3.one * 0.1f;

		MessageBroker.Default.Publish(Messages.PlayerHit);
		MessageBroker.Default.Publish(new DamegeUIParameter(_parameter.damege, true, GetPosition()));

		//
		if (characterData.HP.x <= 0f)
		{
			//die
			OnDie();
		}
		else
		{
			hitDuration = 0.5f;
			stateController.Set(_effectParameter);
			SoundManager.Instance.PlaySFXAsync(SoundManager.GetHitSFXAddress(this.CharacterIdx));
		}
	}
	protected virtual void OnDie()
	{
		characterState.Release();
		PlayAnimation((int)CharacterRikoType.DieA);
		SoundManager.Instance.PlaySFXAsync(SoundManager.GameOver);
		Collider _collider = GetComponent<Collider>();
		_collider.enabled = false;

		DisableNavmeshAgent();

		disposables?.Clear();

		MessageBroker.Default.Publish(Messages.PlayerDie);
	}
	/// <summary>
	/// 전체 공격 실행.
	/// </summary>
	/// <param name="_token"></param>
	/// <returns></returns>
	public virtual async UniTask OnBoomEnable(CancellationToken _token = default)
	{
		mCollider.enabled = false;
		SoundManager.Instance.PlaySFXAsync(BoomSoundFirst, 0.5f);
		SetBoom(true);
		mTransform.localRotation = Quaternion.identity;
		boomEffect.SetActive(true);

		try
		{
			SoundManager.Instance.PlaySFXAsync(SoundManager.BOOM);
			await UniTask.Delay(2000, cancellationToken: _token);
			SoundManager.Instance.PlaySFXAsync(SoundManager.BOOM2);
			await UniTask.Delay(2000, cancellationToken: _token);
			SoundManager.Instance.PlaySFXAsync(BoomSoundEnd, 0.5f);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
	}
	/// <summary>
	/// 전체 공격 종료.
	/// </summary>
	public virtual void OnBoomDisable()
	{
		SetBoom(false);
		mCollider.enabled = true;
	}
	protected virtual void EnableNavmeshAgent()
	{
		nav.enabled = true;
		nav.TryResume();
		nav.TryResetPath();
	}
	protected virtual void DisableNavmeshAgent()
	{
		nav.TryStop();
		nav.TryResetPath();
		nav.enabled = false;
	}
	protected virtual void DirectPosition()
	{
		if (UnityEngine.AI.NavMesh.SamplePosition(mTransform.position, out var hit, 2f, UnityEngine.AI.NavMesh.AllAreas))
		{
			nav.enabled = true;
			nav.Warp(hit.position);
			nav.TryResume();
			nav.TryResetPath();
		}
	}
	/// <summary>
	/// 전체 공격 카메라 애니메이션 트리거.
	/// </summary>
	/// <returns></returns>
	public virtual string GetCameraTriggerName() => BoomCameraTrigger;
	public void DoHeal() => characterData.AddHP(HealHP);
	public Vector2 GetHP() => characterData.HP;
	public Vector3 GetPosition() => mTransform.position;
	#endregion

	#region ANIMATION
	public virtual void PlayAnimation(int _idx)
	{
		if (animationIdx != _idx)
			animator.SetInteger(AnimationKey, _idx);
		animationIdx = _idx;
		if (_idx != (int)CharacterRikoType.IdleC)
			duration = DefaultAnimationDuration;
	}
	/// <summary>
	/// 애니메이션 진행 시간 체크.
	/// 애니메이션 끝나면 Idle로 복귀.
	/// </summary>
	/// <param name="_token"></param>
	/// <returns></returns>
	private async UniTask UpdateAnimationDurationAsync(CancellationToken _token)
	{
		try
		{
			while (true)
			{
				if (_token.IsCancellationRequested)
					break;
				if (duration > 0.0f)
				{
					duration -= Time.unscaledDeltaTime;
					if (duration <= 0.0f)
					{
						PlayAnimation((int)CharacterRikoType.IdleC);
						duration = 0.0f;
					}
				}

				await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken:_token);
			}
		}
		catch (OperationCanceledException)
		{
			throw;
		}
	}
	/// <summary>
	/// 상태에 따른 애니메이션 실행.
	/// </summary>
	/// <param name="_token"></param>
	/// <returns></returns>
	protected async UniTask UpdateAnimationAsync(CancellationToken _token)
	{
		try
		{
			while (true)
			{
				CharacterRikoType _targetAnim = GetCurrentPriorityAnimation();
				PlayAnimation((int)_targetAnim);

				if(_targetAnim == CharacterRikoType.Run)
					nav.Move(moveVector * nav.speed * Time.unscaledDeltaTime);
				if (hitDuration > 0.0f)
					hitDuration = Mathf.Max(hitDuration - Time.unscaledDeltaTime, 0f);

				await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken: _token);
			}
		}
		catch (OperationCanceledException)
		{
			throw;
		}
	}
	private CharacterRikoType GetCurrentPriorityAnimation()
	{
		if (victory) return CharacterRikoType.CuteA;
		if (boom) return CharacterRikoType.Bye;
		if (stateController.IsState(CharacterState.Dizzy)) return CharacterRikoType.Cry;
		if (hitDuration > 0.0f) return CharacterRikoType.Damaged;
		if (isDashing) return CharacterRikoType.IdleC;
		if (running) return CharacterRikoType.Run;
		if (attacking) return CharacterRikoType.CuteA;

		return CharacterRikoType.IdleC;
	}
	/// <summary>
	/// 승리 모션.
	/// </summary>
	/// <param name="_value"></param>
	public virtual void SetVictory(bool _value)
	{
		victory = _value;
		if (_value)
			mTransform.rotation = Quaternion.Euler(0f, 180f, 0f);
	}
	/// <summary>
	/// 전체 공격 실행.
	/// </summary>
	/// <param name="_value"></param>
	public void SetBoom(bool _value) => this.boom = _value;
	#endregion

	#region CHARACTER_EFFECT_STATE
	/// <summary>
	/// 현재 진행중인 상태이상.
	/// </summary>
	/// <param name="_bitValue"></param>
	protected void OnCurrentState(int _bitValue)
	{
		// 기절
		if ((_bitValue & (int)CharacterState.Dizzy) != 0)
		{
		}
		else
		{
		}

		// 빙결
		if ((_bitValue & (int)CharacterState.Freeze) != 0)
		{
			OnFreeze(true);
		}
		else
		{
			OnFreeze(false);
		}

		// 독
		if ((_bitValue & (int)CharacterState.Poison) != 0)
		{
			OnPoison(true);
		}
		else
		{
			OnPoison(false);
		}
	}
	
	/// <summary>
	/// 상태이상 구독할수 있게 Observable
	/// </summary>
	/// <param name="_state"></param>
	/// <returns></returns>
	public IObservable<EffectParameter> GetStateObservable(CharacterState _state) => stateController.GetObservable(_state);
	
	/// <summary>
	/// 상태이상 이펙트 추가.
	/// </summary>
	/// <param name="_state"></param>
	/// <returns></returns>
	private async UniTask AddBall(CharacterState _state)
	{
		if (_state == CharacterState.None || _state == CharacterState.Dizzy) return;
		if (stateBallBuffer.ContainsKey(_state)) return;
		string _address = _state == CharacterState.Freeze ? GameDefine.FreezeBallAddress : GameDefine.PoisionBallAddress;
		GameObject _go = await SPoolManager.Instance.Spawn(_address, token);
		CharacterStateBall _ball = _go.GetComponent<CharacterStateBall>();
		_ball.SetActive(true);
		stateBallBuffer.Add(_state, _ball);

		SpawnedObject _sp = _go.GetComponent<SpawnedObject>();
		_sp.cachedTransform.parent = mTransform;
		_sp.cachedTransform.localPosition = Vector3.zero;
		_sp.cachedTransform.localScale = Vector3.one;
		_sp.cachedTransform.localRotation = Quaternion.identity;
	}

	/// <summary>
	/// 상태이상 이펙트 제거.
	/// </summary>
	/// <param name="_state"></param>
	private void RemoveBall(CharacterState _state)
	{
		if (_state == CharacterState.None || _state == CharacterState.Dizzy) return;
		if (!stateBallBuffer.ContainsKey(_state)) return;
		stateBallBuffer[_state].SetActive(false);
		SpawnedObject _sp = stateBallBuffer[_state].GetComponent<SpawnedObject>();
		stateBallBuffer.Remove(_state);
		_sp.Despawn();
	}

	/// <summary>
	/// 우선 표시할 상태이상.
	/// </summary>
	/// <param name="_state"></param>
	protected void OnPirorityState(CharacterState _state)
	{
		if(_state == CharacterState.None)
		{
			if(stateBallBuffer.Count > 0)
			{
				RemoveBall(CharacterState.Freeze);
				RemoveBall(CharacterState.Poison);
				RemoveBall(CharacterState.Dizzy);
			}
		}
		else if(_state == CharacterState.Freeze)
		{
			AddBall(_state);
			RemoveBall(CharacterState.Poison);
			RemoveBall(CharacterState.Dizzy);
		}
		else if (_state == CharacterState.Poison)
		{
			AddBall(_state);
			RemoveBall(CharacterState.Freeze);
			RemoveBall(CharacterState.Dizzy);
		}
	}
	/// <summary>
	/// 기절 중
	/// </summary>
	/// <param name="_value"></param>
	private void OnDizzy(bool _value) { }

	/// <summary>
	/// 중독 걸림.
	/// </summary>
	/// <param name="_value"></param>
	private void OnPoison(bool _value)
	{
		if(_value) characterData.RemoveHP(1);
	}
	/// <summary>
	/// 빙결걸림.
	/// </summary>
	/// <param name="_value"></param>
	private void OnFreeze(bool _value) => this.nav.speed = moveSpeed * (_value ? 0.5f : 1f);
	#endregion

	#region ETC
	/// <summary>
	/// 대쉬, 캐릭터 나타나는 이펙트.
	/// </summary>
	/// <param name="_sound"></param>
	public void PlayChangeVFX(bool _sound = true)
	{
		if (_sound) SoundManager.Instance.PlaySFXAsync(SoundManager.TRANSFORM_TWINCLE, 0.2f);
		Vector3 _position = GetPosition() + Vector3.up * 0.9f;
		Vector3 _scale = Vector3.one * 0.2f;
		GameObject _go = dashEffectSpawner.Spawn();
		_go.transform.position = _position;
		_go.transform.localScale = _scale;

		ParticleSystem _ps = _go.GetComponent<ParticleSystem>();
		_ps.Play();
	}
	public virtual void OnButtonAttack()
	{
	}
	public int GetSlotIdx() => 0;
	#endregion
}
