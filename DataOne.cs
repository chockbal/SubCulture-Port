using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine.UI;
using UnityEngine;
using System;


/// <summary>
/// 데이터 관리 및 로딩 클래스
/// </summary>
public class DataOne : MonoSingleton<DataOne>
{
    private SCharacterData	characterData;
	private SEquipDatas		equipData;
	private SGameData		gameData;
	private bool isEasy;
	private List<ISaveData> saveDatas = new List<ISaveData>();
    public bool IsLoaded {get;set;}
	public SGameData GameData => gameData;
	public SEquipDatas EquipItems => equipData;
	public ICharacterData CharacterData => characterData;


	protected override void OnDestroy()
	{
        characterData?.Dispose();
		base.OnDestroy();
	}

	#region LOADING
	/// <summary>
	/// Load Data and Resources
	/// </summary>
	/// <param name="progress"></param>
	/// <param name="_token"></param>
	/// <returns></returns>
	public async UniTask LoadAwake(Slider progress, CancellationToken _token)
	{
		if (IsLoaded) return;

		QualitySettings.vSyncCount = 0;
		Application.targetFrameRate = 60; // 사실상 무시됨

		isEasy = false;
		IsLoaded = false;

		// Resource Loading
		if(!SUIManager.HasInstance)
		{
			try
			{
				IProgress<float> _progress = progress != null ? new Progress<float>(_v => { progress.value = _v; }) : null;
				var steps = new List<Func<CancellationToken, UniTask>>
				{
					t => Addressables.InstantiateAsync(SUIManager.AddressUIManager, null).WithCancellation(_token),
					t => JTableManager.Instance.LoadTableCharacter(_token),
					t => JTableManager.Instance.LoadTableCostume(_token),
					t => JTableManager.Instance.LoadTableEquip(_token),
					t => JTableManager.Instance.LoadTableMonster(_token),
					t => JTableManager.Instance.LoadTableStage(_token),
					t => JTableManager.Instance.LoadTableWeapon(_token),
					t => JTableManager.Instance.LoadTableEffect(_token),
					t => AtlasManager.Instance.LoadAllAsync(_token: t),
				};

				await RunLoadingAsync(steps, _progress: _progress, _token: _token);
			}
			catch(OperationCanceledException)
			{
				throw;
			}
		}

		LoadData();
		IsLoaded = true;

		SceneManager.LoadSceneAsync(GameDefine.MAIN_SCENE_IDX);
	}
	/// <summary>
	/// Do Loading And Show Loading UI
	/// </summary>
	/// <param name="_step"></param>
	/// <param name="_progress"></param>
	/// <param name="_token"></param>
	/// <returns></returns>
	private async UniTask RunLoadingAsync(IReadOnlyList<Func<CancellationToken, UniTask>> _step, IProgress<float> _progress = null, CancellationToken _token = default)
	{
		if (_step == null || _step.Count == 0)
		{
			_progress?.Report(1.0f);
			return;
		}

		int _total = _step.Count;

		_progress?.Report(0.0f);

		try
		{
			for (int i = 0; i < _total; i++)
			{
				_token.ThrowIfCancellationRequested();
				await _step[i](_token);

				_progress?.Report((i + 1) / (float)_total);
			}
		}
		catch (OperationCanceledException)
		{
			Debug.LogFormat("RunLoadingAsync, cancelled");
			throw;
		}
	}
	/// <summary>
	/// Set Local User Data
	/// </summary>
	private void LoadData()
	{
		string _json = "";
		_json = PlayerPrefs.GetString(GameDefine.KeyEquipData);
		if (!string.IsNullOrEmpty(_json))
		{
			equipData = JsonUtility.FromJson<SEquipDatas>(_json);
			saveDatas.Add(equipData);
		}

		_json = PlayerPrefs.GetString(GameDefine.KeyCharacterData);
		if (!string.IsNullOrEmpty(_json))
		{
			var _data = JsonUtility.FromJson<CharacterDataJson>(_json);
			characterData = new SCharacterData(_data);
			saveDatas.Add(characterData);
		}

		_json = PlayerPrefs.GetString(GameDefine.KeyGameData);
		if (!string.IsNullOrEmpty(_json))
		{
			gameData = new SGameData();
			gameData.Set(_json);
			saveDatas.Add(gameData);
		}

		if (CharacterData != null)
			CharacterData.UpdateTotalStatus();
	}
	#endregion

	#region COMPOSING
	/// <summary>
	/// Compose Equips
	/// </summary>
	/// <param name="_cost"></param>
	/// <returns></returns>
	public long TryCompose(List<long> _cost)
	{
		if (_cost.Count == 0)
			return 0;

		int _rate = 0;
		int _targetGrade = 1;
		for(int i=0;i< _cost.Count;++i)
		{
			SEquipData _item = GetEquipData(_cost[i]);
			if (_item.grade == 3) _rate += GameDefine.ComposeRateGrade3;
			else if (_item.grade == 2) _rate += GameDefine.ComposeRateGrade2;
			else if (_item.grade == 1) _rate += GameDefine.ComposeRateGrade1;
			if(_targetGrade < _item.grade) _targetGrade = _item.grade;
		}

		_rate = (_rate > 100) ? 100 : _rate;
		_rate = (_cost.Count <= 1) ? (int)(_rate * 0.5f) : _rate;
		equipData.DeleteItem(_cost);

		int _r = UnityEngine.Random.Range(1, 101);

		bool _sucess = _r <= _rate;
		if(_sucess)
		{
			bool _upgrade = _r <= (int)(_rate*0.5f);
			if(_upgrade) _targetGrade = Mathf.Min(_targetGrade + 1, 3);

			List<JEquip> _tables = JTableManager.Instance.GetEquips(_one => _one.grade == _targetGrade);
			_r = UnityEngine.Random.Range(0, _tables.Count);
			var _result = this.equipData.Create(_tables[_r]);
			this.equipData.data.Add(_result);
			this.equipData.SaveData();
			return _result.uid;
		}
		else
		{
			this.equipData.SaveData();
			return 0;
		}
	}
	public float GetComposeRate(List<long> _cost)
	{
		float _rate = 0;
		for (int i = 0; i < _cost.Count; ++i)
		{
			SEquipData _item = GetEquipData(_cost[i]);
			if (_item.grade == 3) _rate += GameDefine.ComposeRateGrade3;
			else if (_item.grade == 2) _rate += GameDefine.ComposeRateGrade2;
			else if (_item.grade == 1) _rate += GameDefine.ComposeRateGrade1;
		}

		_rate = (_rate > 100f) ? 100f : _rate;
		_rate = (_cost.Count <= 1) ? (float)(_rate * 0.5f) : _rate;
		return _rate;
	}
	#endregion

	#region ETC
	/// <summary>
	/// Call At GameOver
	/// </summary>
	public void Initialize()
	{
		IsLoaded = false;
		PlayerPrefs.DeleteAll();

		saveDatas.Clear();
		EquipUidGenerator.DeleteUIDData();

		characterData = null;
		equipData = null;
		gameData = null;
	}
	public SEquipData GetEquipData(long _uid) => this.equipData.data.Find(one => one.uid == _uid);
	public void AddATK()
	{
		characterData.AddATK();
	}
	public void AddHP()
	{
		characterData.AddHPPlus();
	}
	public void AddSPD()
	{
		characterData.AddSPD();
	}
	public void SetCharacter(int _idx)
	{
		characterData = null;
		characterData = new SCharacterData(JTableManager.Instance.GetCharacter(_one => _one.id == _idx));
		characterData.SaveData();

		equipData = new SEquipDatas();
		equipData.MakeDefault();

		gameData = new SGameData();
		gameData.Initialize();
		//gameData.SaveData();

		saveDatas.Add(characterData);
		saveDatas.Add(equipData);
		saveDatas.Add(gameData);

		foreach (var one in saveDatas)
			one.SaveData();
	}

	public void ToggleEasy() => isEasy = !isEasy;
	public bool IsEasy() => isEasy;
	public bool TryUseCost(long _cost, bool _save = false)
	{
		bool _result = false;
		if (IsEnoughCost(_cost))
		{
			this.gameData.RemoveMoney(_cost, _save);
			_result = true;
		}

		return _result;
	}
	public bool IsEnoughCost(long _cost) => this.gameData.Money >= _cost;
	#endregion
}
