using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System;
using System.Threading;
using UniRx;
using UnityEngine;

/// <summary>
/// 원거리 캐릭터의 무기 클래스.
/// </summary>
public class Gun : MonoBehaviour
{
    private float fireRate;
    private CompositeDisposable disposables;
    private Transform mTransform;
    
    private float scale;
    private bool fire;
    private bool moving;
    private float fireDuratin;

	private int weaponLevel;
	private string weaponAddress;
    private int weaponDamege;
    private int atk;
    private string hitSound;
    private EffectParameter effect;
    private SPoolManager poolManager;
	private readonly Dictionary<int, List<Vector3>> WeaponPosition = new Dictionary<int, List<Vector3>>()
	{
		{ 1, new List<Vector3>(){ Vector3.zero } },
		{ 2, new List<Vector3>(){ new Vector3(-0.6f, 0f, 0f), new Vector3(0.6f, 0f, 0f) } },
		{ 3, new List<Vector3>(){ new Vector3(-0.8f, 0f, 0f), Vector3.zero, new Vector3(0.8f, 0f, 0f) } },
	};

	void OnEnable()
    {
		if (poolManager == null)
			poolManager = SPoolManager.Instance;
		disposables = new CompositeDisposable();
        mTransform = transform;
        fire = false;
    }
    void OnDisable()
    {
		poolManager = null;
		disposables?.Clear();
    }
    public void Set(Vector2 _weapon)
    {
        JWeapon _table = JTableManager.Instance.GetWeapon(_one => _one.id == (int)_weapon.x);
		weaponLevel = (int)_weapon.y;

		weaponAddress = _table.address;
        weaponDamege = _table.defaultAtk;
        scale = _table.scale;

		JEffect _effectTable = JTableManager.Instance.GetEffect(_one => _one.id == _table.effect);

		SetEffect(new EffectParameter(
			_effectTable != null ? (CharacterState)_effectTable.type : CharacterState.None,
			_effectTable != null ? _effectTable.icon : string.Empty,
			_effectTable != null ? _effectTable.value: 0f,
			_effectTable != null ? _effectTable.duration : 0));
	}
    public void SetATK(int _atk) => atk = _atk;
	public void SetFireRate(float _rate) => fireRate = _rate;
	public void SetEffect(EffectParameter _parameter) => this.effect = _parameter;

    public void SetFire(bool _value) => fire = _value;
    public async UniTask UpdateFireAsync(CancellationToken _token)
    {
		try
        {
			while (!_token.IsCancellationRequested)
			{
				bool _onFire = fire && !moving;
				if (fire && !moving)
				{
					if (fireDuratin <= 0f && poolManager != null)
					{
						Vector3 _fireDirection = mTransform.forward;
						for (int i = 0; i < weaponLevel; ++i)
						{
							GameObject _go = await poolManager.Spawn(weaponAddress, _token);
							SpawnedObject _so = _go.GetComponent<SpawnedObject>();
							HS_ProjectileMover _bullet = _go.GetComponent<HS_ProjectileMover>();
							if (_so == null) _so = _go.AddComponent<SpawnedObject>();
							if (_bullet == null) _bullet = _go.AddComponent<HS_ProjectileMover>();

							_bullet.SetHitSound(hitSound);
							_bullet.SetDirection(_fireDirection);
							_bullet.SetPosition(mTransform, WeaponPosition[weaponLevel][i]);
							_so.cachedTransform.localScale = Vector3.one * scale;
							_bullet.SetDamege(new DamegeParameter(weaponDamege + (weaponDamege * atk), 100f)); 
                            _bullet.SetEffect(effect);
                            _bullet.enabled = true;
							_bullet.DoAutoDisable();
						}

						fireDuratin = fireRate;
						if (!string.IsNullOrEmpty(hitSound))
							SoundManager.Instance.PlaySFXAsync(hitSound, 0.1f);
					}
				}

				fireDuratin = Mathf.Max(fireDuratin - Time.unscaledDeltaTime, 0f);
				await UniTask.Yield(cancellationToken: _token);
			}
		}
        catch(OperationCanceledException)
        {
            throw;
        }
    }
    public int GetWeaponLevel() => weaponLevel;
    public void SetWeaponLevel(int _level) => weaponLevel = _level;
    public void SetHitSound(string _address) => hitSound = _address;

}
