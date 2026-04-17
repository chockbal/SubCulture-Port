/* README
 * 오브젝트 풀 매니저.
 * 1. 대량으로 사용하는 오브젝트들을 사전에 생성해 인스턴스화 오버헤드를 방지.
 * 2. 비동기 호출로 리소스 로딩 대기 시간동안 게임 루프가 멈추지 않도록 설계.
 */

using UnityEngine;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using System.Threading;
using System;

/// <summary>
/// 오브젝트 풀 매니저.
/// </summary>
public class SPoolManager : MonoSingleton<SPoolManager>
{
	[SerializeField] private GameObject poolPrefab;
	private Dictionary<string, SinglePool> pool = new Dictionary<string, SinglePool>();

	/// <summary>
	/// 사전에 50개 정도 오브젝트 생성.
	/// </summary>
	/// <param name="_key"></param>
	/// <param name="_token"></param>
	/// <returns></returns>
	public async UniTask Preload(string _key, CancellationToken _token = default)
	{
		if (!pool.ContainsKey(_key))
		{
			GameObject _resource = await InstanceResourceAsync(_key, _token);
			GameObject _go = GameObject.Instantiate(poolPrefab, transform);
			SinglePool _pool = _go.GetComponent<SinglePool>();
			_pool.Set(_resource);
			_pool.DoInstaneced();
			pool.Add(_key, _pool);
		}

		List<SpawnedObject> _temp = new List<SpawnedObject>(50);
		for (int i = 0; i < 50; i++)
		{
			var _obj = pool[_key].Spawn().GetComponent<SpawnedObject>();
			if (_obj != null) _temp.Add(_obj);
		}

		foreach (var _sp in _temp)
			_sp.Despawn();
	}
	public async UniTask<GameObject> Spawn(string _key, CancellationToken _token)
	{
		GameObject _result = null;
		if (pool.ContainsKey(_key))
		{
			_result = pool[_key].Spawn();
		}
		else
		{
			try
			{
				GameObject _resource = await InstanceResourceAsync(_key, _token);
				GameObject _go = GameObject.Instantiate(poolPrefab, transform);
				SinglePool _pool = _go.GetComponent<SinglePool>();
				_pool.Set(_resource);
				_pool.DoInstaneced();
				pool.Add(_key, _pool);
				_result = _pool.Spawn();
			}
			catch(OperationCanceledException)
			{
				throw;
			}
		}

		return _result;
	}
	private async UniTask<GameObject> InstanceResourceAsync(string _key, CancellationToken _token = default)
	{
		GameObject _result = null;
		try
		{
			_result = await Addressables.InstantiateAsync(_key, transform).WithCancellation(_token);
			return _result;
		}
		catch(OperationCanceledException)
		{
			throw;
		}
	}
}
