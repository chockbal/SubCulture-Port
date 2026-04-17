using System;
using System.Collections.Generic;
using UniRx;
using UnityEngine;

/// <summary>
/// 캐릭터 데이터 클래스.
/// </summary>
public class SCharacterData : ICharacterData, ISaveData
{
	private int idx;
	private string name;
	private string resources;
	private ReactiveProperty<Vector2> weaponData = new ReactiveProperty<Vector2>(new Vector2(1f, 1f));
	private ReactiveProperty<Vector3> status = new ReactiveProperty<Vector3>(Vector3.zero);
	private ReactiveProperty<Vector2> hp = new ReactiveProperty<Vector2>(Vector3.zero);
	private IntReactiveProperty remainPoint = new IntReactiveProperty(0);
	private LongReactiveProperty topEquipUID = new LongReactiveProperty(0);
	private LongReactiveProperty middleEquipUID = new LongReactiveProperty(0);
	private LongReactiveProperty bottomEquipUID = new LongReactiveProperty(0);
	private Subject<Vector3> onStatusChanged = new Subject<Vector3>();
	public int Idx { get => idx; }
	public bool IsMaked { get => idx > 0; }
	public int RemainPoint => remainPoint.Value;
	public Vector2 HP => hp.Value;
	public Vector3 Status => status.Value;
	public IObservable<Vector2> WeaponDataObservable => weaponData;
	public IObservable<Vector2> HPObservable => hp;
	public IObservable<int> RemainPointObservable => remainPoint;
	public IObservable<Vector3> OnStatusChanged => onStatusChanged;
	public long TopEquipUID => topEquipUID.Value;
	public long MiddleEquipUID => middleEquipUID.Value;
	public long BottomEquipUID => bottomEquipUID.Value;
	public bool IsMaxLevel { get => Level == (GameDefine.PointMax * 3) + 1; }

	// UniRx의 ReactiveProperty와 Subject를 사용하여 데이터와 UI/로직 간의 의존성을 낮췄습니다.

	#region Constructors

	/// <summary>
	/// 기본 생성자: 기본 데이터로 초기화
	/// </summary>
	public SCharacterData()
	{
		Initialize(new CharacterDataJson());
	}

	/// <summary>
	/// JSON 문자열로부터 생성
	/// </summary>
	public SCharacterData(string _json) : this() // 기본 생성자 먼저 호출
	{
		if (string.IsNullOrEmpty(_json)) return;
		try
		{
			CharacterDataJson _data = JsonUtility.FromJson<CharacterDataJson>(_json);
			Initialize(_data);
			UpdateTotalStatus();
		}
		catch (Exception e)
		{
			Debug.LogException(e);
		}
	}

	/// <summary>
	/// 데이터 객체로부터 생성
	/// </summary>
	public SCharacterData(CharacterDataJson _data)
	{
		Initialize(_data);
	}

	/// <summary>
	/// 테이블 데이터로부터 신규 캐릭터 생성
	/// </summary>
	public SCharacterData(JCharacter _table)
	{
		JCostume _defaultCostume = JTableManager.Instance.GetCostume(_one => _one.id == _table.defaultCostumeIdx);

		CharacterDataJson _data = new CharacterDataJson
		{
			idx = _table.id,
			name = _table.name,
			resources = _defaultCostume.address,
			weaponId = _table.defaultWeaponId,
			weaponLevel = 1,
			hp = _table.defaultHP
			// 나머지 필드는 CharacterDataJson 기본값 사용
		};

		Initialize(_data);
		// 신규 캐릭터이므로 HP를 최대치로 설정
		hp.Value = new Vector2(_table.defaultHP, _table.defaultHP);
	}
	#endregion
	/// <summary>
	/// 모든 생성자가 공유하는 핵심 초기화 로직
	/// </summary>
	private void Initialize(CharacterDataJson _data)
	{
		if (_data == null) return;

		idx = _data.idx;
		name = _data.name;
		resources = _data.resources;

		// ReactiveProperty 값 할당
		status.Value = new Vector3(_data.atk, _data.hpPlus, _data.spd);
		remainPoint.Value = _data.remainPoint;
		topEquipUID.Value = _data.topEquipUID;
		middleEquipUID.Value = _data.middleEquipUID;
		bottomEquipUID.Value = _data.bottomEquipUID;
		weaponData.Value = new Vector2(_data.weaponId, _data.weaponLevel);

		// HP 설정 (테이블 데이터를 참조해야 하므로 별도 처리)
		JCharacter _table = JTableManager.Instance.GetCharacter(_one => _one.id == idx);
		float _maxHP = (_table != null ? _table.defaultHP : 0) + _data.hpPlus;
		hp.Value = new Vector2(Mathf.Min(_data.hp, _maxHP), _maxHP);
	}

	public void AddWeaponLevel(int _idx)
	{
		if ((int)weaponData.Value.x != _idx) return;
		Vector2 _temp = weaponData.Value + new Vector2(0f, 1f);
		if (_temp.y > 3f) _temp.y = 3f;
		weaponData.Value = _temp;
		SaveData();
	}
	public Vector2 GetWeaponData() => weaponData.Value;
	public void ChangeWeapon(int _idx)
	{
		weaponData.Value = new Vector2(_idx, 1f);
		SaveData();
	}
	public void AddPoint()
	{
		if (!IsMaxLevel)
			remainPoint.Value = remainPoint.Value + 1;
				SaveData();
	}
	/// <summary>
	/// Equip Item
	/// </summary>
	/// <param name="_uid"></param>
	public void SetEquip(long _uid)
	{
		var _data = DataOne.Instance.GetEquipData(_uid);
		if (_data == null)
			return;

		switch (_data.EquipType)
		{
			case SEquipType.Top:
				{
					topEquipUID.Value = _data.uid;
					break;
				}
			case SEquipType.Middle:
				{
					middleEquipUID.Value = _data.uid;
					break;
				}
			case SEquipType.Bottom:
				{
					bottomEquipUID.Value = _data.uid;
					break;
				}
		}

		UpdateTotalStatus();
		SaveData();
	}
	/// <summary>
	/// UnEquip Item
	/// </summary>
	/// <param name="_type"></param>
	public void UnEquip(SEquipType _type)
	{
		switch (_type)
		{
			case SEquipType.Top:
				{
					topEquipUID.Value = 0;
					break;
				}
			case SEquipType.Middle:
				{
					middleEquipUID.Value = 0;
					break;
				}
			case SEquipType.Bottom:
				{
					bottomEquipUID.Value = 0;
					break;
				}
		}

		UpdateTotalStatus();
		SaveData();
	}



	public void SetIdx(int _idx)
	{
		idx = _idx;
		name = "";
		resources = "";
	}
	public void AddHP(int _value)
	{
		Vector2 _hp = this.hp.Value;
		_hp.x = Mathf.Min(_hp.x + _value, _hp.y);
		this.hp.Value = _hp;

		SaveData();
	}
	public void RemoveHP(int _value)
	{
		Vector2 _hp = this.hp.Value;
		_hp.x = Mathf.Max(_hp.x - _value, 0f);
		this.hp.Value = _hp;

		SaveData();
	}
	public void AddATK()
	{
		if (remainPoint.Value <= 0 || status.Value.x >= GameDefine.PointMax) return;
		remainPoint.Value -= 1;
		Vector3 _plus = new Vector3(1f, 0f, 0f);
		Vector3 _status = status.Value;
		_status = _status + _plus;
		if (_status.x > GameDefine.PointMax) _status.x = GameDefine.PointMax;
		SetStatus(_status);
	}
	public void AddHPPlus()
	{
		if (remainPoint.Value <= 0 || status.Value.y >= GameDefine.PointMax) return;
		remainPoint.Value -= 1;
		Vector3 _plus = new Vector3(0f, 1f, 0f);
		Vector3 _status = status.Value;
		_status = _status + _plus;
		if (_status.y > GameDefine.PointMax) _status.y = GameDefine.PointMax;
		SetStatus(_status);
	}
	public void AddSPD()
	{
		if (remainPoint.Value <= 0 || status.Value.z >= GameDefine.PointMax) return;
		remainPoint.Value -= 1;
		Vector3 _plus = new Vector3(0f, 0f, 1f);
		Vector3 _status = status.Value;
		_status = _status + _plus;
		if (_status.z > GameDefine.PointMax) _status.z = GameDefine.PointMax;
		SetStatus(_status);
	}
	public void SetStatus(Vector3 _status)
	{
		_status.x = _status.x > GameDefine.PointMax ? GameDefine.PointMax : _status.x;
		_status.y = _status.y > GameDefine.PointMax ? GameDefine.PointMax : _status.y;
		_status.z = _status.z > GameDefine.PointMax ? GameDefine.PointMax : _status.z;
		status.Value = _status;

		UpdateTotalStatus();

		SaveData();
	}
	/// <summary>
	/// Update Total Status (Character Status + Equip Bonus Status)
	/// 캐릭터와 장비의 스테이터스를 분리.
	/// </summary>
	public void UpdateTotalStatus()
	{
		JCharacter _characterTable = JTableManager.Instance.GetCharacter(_one => _one.id == this.Idx);
		Vector3 _totalStatus = GetTotalStatus();
		// Atk Changed


		// Hp Changed
		Vector2 _hp = hp.Value;
		hp.Value = new Vector2(_hp.x, _characterTable.defaultHP + _totalStatus.y);

		// Spd Changed

		onStatusChanged?.OnNext(_totalStatus);
	}
	public Vector3 GetEquipStatus()
	{
		Vector3 _result = Vector3.zero;
		var _top = DataOne.Instance.GetEquipData(topEquipUID.Value);
		var _middle = DataOne.Instance.GetEquipData(middleEquipUID.Value);
		var _bottom = DataOne.Instance.GetEquipData(bottomEquipUID.Value);
		if ( _top != null ) _result += new Vector3(_top.atk, _top.hp, _top.spd);
		if (_middle != null) _result += new Vector3(_middle.atk, _middle.hp, _middle.spd);
		if (_bottom != null) _result += new Vector3(_bottom.atk, _bottom.hp, _bottom.spd);
		return _result;
	}
	public Vector3 GetTotalStatus() => status.Value + GetEquipStatus();
	


	public void SaveData()
	{
		CharacterDataJson _json = new CharacterDataJson();
		_json.idx = idx;
		_json.name = name;
		_json.resources = resources;
		_json.atk = (int)status.Value.x;
		_json.hpPlus = (int)status.Value.y;
		_json.spd = (int)status.Value.z;
		_json.hp = (int)this.hp.Value.x;
		_json.topEquipUID = this.topEquipUID.Value;
		_json.middleEquipUID = this.middleEquipUID.Value;
		_json.bottomEquipUID = this.bottomEquipUID.Value;
		_json.weaponId = (int)this.weaponData.Value.x;
		_json.weaponLevel = (int)this.weaponData.Value.y;
		PlayerPrefs.SetString(GameDefine.KeyCharacterData, JsonUtility.ToJson(_json));
		PlayerPrefs.Save();
	}
	public void Dispose()
	{
		status.Dispose();
		hp.Dispose();
		remainPoint.Dispose();
		topEquipUID.Dispose();
		middleEquipUID.Dispose();
		bottomEquipUID.Dispose();
		weaponData.Dispose();
	}
	public int Level => 1 + (int)status.Value.x + (int)status.Value.y + (int)status.Value.z;
	public int CostumeId
	{
		get
		{
			int _level = Level;
			int _costumeIdx = 0;
			List <JCostume> _tables = JTableManager.Instance.GetCostumes(_one => _one.characterIdx == idx);
			if (_level <= _tables[0].id * 10)
				_costumeIdx = _tables[0].id;
			else if (_level <= _tables[1].id * 10)
				_costumeIdx = _tables[1].id;
			else if (_level <= _tables[2].id * 10)
				_costumeIdx = _tables[2].id;
			else if (_level <= _tables[3].id * 10)
				_costumeIdx = _tables[3].id;
			return _costumeIdx;
		}
	}
}

[Serializable]
public class CharacterDataJson
{
	public int idx;
	public string name;
	public string resources;
	public int hp;
	public int atk;
	public int hpPlus;
	public int spd;
	public int boomCount;
	public int remainPoint;
	public long topEquipUID;
	public long middleEquipUID;
	public long bottomEquipUID;
	public int weaponId;
	public int weaponLevel;
	public CharacterDataJson()
	{
		idx = 0;
		name = string.Empty;
		resources = string.Empty;
		atk = 0;
		hp = 0;
		hpPlus = 0;
		spd = 0;
		boomCount = 0;
		remainPoint = 0;
		topEquipUID = 0;
		middleEquipUID = 0;
		bottomEquipUID = 0;
		weaponId = 1;
		weaponLevel = 1;
	}
	public CharacterDataJson(CharacterDataJson _origin)
	{
		idx = _origin.idx;
		name = _origin.name;
		resources = _origin.resources;
		hp = _origin.hp;
		atk = _origin.atk;
		hpPlus = _origin.hpPlus;
		spd = _origin.spd;
		boomCount = _origin.boomCount;
		remainPoint = _origin.remainPoint;
		topEquipUID = _origin.topEquipUID;
		middleEquipUID = _origin.middleEquipUID;
		bottomEquipUID = _origin.bottomEquipUID;
		weaponId = _origin.weaponId;
		weaponLevel = _origin.weaponLevel;
	}
}
