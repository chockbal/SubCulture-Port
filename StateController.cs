/* README
상태이상 컨트롤 클래스.
 - 비트마스크 및 Observable 기반 상태 이상 관리 시스템

1. 중첩 상태 관리: 다중 상태 이상을 비트 연산으로 통합 관리하여 로직 연산 부하를 최소화했습니다.
2. 비동기 시간 제어: UniTask.Delay를 활용한 정밀한 1초 단위 업데이트 루프를 구현, 게임 틱(Tick) 베이스의 상태 감쇄 로직을 설계했습니다.
3. 구독 기반 통보: 개별 상태 이상 유형에 대해 전용 Observable을 제공하여, 필요한 컴포넌트만 이벤트를 수신하는 느슨한 결합(Loose Coupling)을 실현했습니다.

 */



using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;
using UniRx;
using UnityEngine;

/// <summary>
/// 상태이상 컨트롤 클래스.
/// </summary>

public class StateController : MonoBehaviour
{
	private List<EffectParameter> effects = new List<EffectParameter>();
	private Dictionary<CharacterState, EffectParameter> effectCollect = new Dictionary<CharacterState, EffectParameter>();

	private Subject<CharacterState> onPriorityState = new Subject<CharacterState>();
	private Subject<int> onCurrentState = new Subject<int>();
	private Dictionary<CharacterState, Subject<EffectParameter>> onStateChanged = new Dictionary<CharacterState, Subject<EffectParameter>>();

	private CancellationToken token;
	protected const int SECOND = 1000;

	public Subject<CharacterState> OnPriorityState => onPriorityState;
	public IObservable<int> OnCurrentState => onCurrentState;

	private void OnDestroy()
	{
		onPriorityState.Dispose();
		onCurrentState.Dispose();
		if(onStateChanged.Count > 0)
		{
			foreach (var effect in onStateChanged.Values)
				effect.Dispose();
		}

		onStateChanged.Clear();

		onPriorityState = null;
		onCurrentState = null;
	}

	private async UniTask UpdateEffectsAsync()
    {
        try
        {
			while (!token.IsCancellationRequested)
			{
				UpdateEffectOnce(true);
				await UniTask.Delay(SECOND, cancellationToken: token);
			}
		}
        catch(OperationCanceledException)
        {
			throw;
        }
    }
	/// <summary>
	/// 현재 상태이상 체크.
	/// </summary>
	/// <param name="_timeReduce"></param>
	private void UpdateEffectOnce(bool _timeReduce)
	{
		int _current = 0; // 비트마스크.
		CharacterState _state = CharacterState.None;
		int _duration = 0;
		for (int i = 0; i < effects.Count; i++)
		{
			if (effects[i].duration <= 0) continue;

			EffectParameter _value = effects[i];
			_value.duration = _timeReduce ? Mathf.Max(effects[i].duration - 1, 0) : _value.duration;

			effects[i] = _value;
			effectCollect[_value.type] = _value;
			_current = (_value.duration > 0) ? _current | (int)effects[i].type : _current;

			if (_value.duration > _duration)
			{
				_duration = _value.duration;
				_state = _value.type;
			}

			if (!onStateChanged.ContainsKey(_value.type))
				onStateChanged.Add(_value.type, new Subject<EffectParameter>());
			onStateChanged[_value.type]?.OnNext(_value);
		}

		onPriorityState?.OnNext(_state);
		onCurrentState?.OnNext(_current);
	}
	public void Set(CancellationToken _token)
	{
		token = _token;
		UpdateEffectsAsync().Forget();
	}
	public void Set(EffectParameter _effect)
    {
		if (token.IsCancellationRequested) return;
        if (_effect.type == CharacterState.None || _effect.duration <= 0) return;
        int _idx = this.effects.FindIndex(_one => _one.type == _effect.type);
		if (_idx < 0) this.effects.Add(_effect);
		else this.effects[_idx] = _effect;
		if (!effectCollect.ContainsKey(_effect.type))
			effectCollect.Add(_effect.type, new EffectParameter());
		effectCollect[_effect.type] = _effect;

		// Observer
		UpdateEffectOnce(false);
	}
	public IObservable<EffectParameter> GetObservable(CharacterState _state)
	{
		if (!onStateChanged.ContainsKey(_state))
			onStateChanged.Add(_state, new Subject<EffectParameter>());
		return onStateChanged[_state];
	}
	public bool IsState(CharacterState _state)
	{
		if (effectCollect.TryGetValue(_state, out var _param))
			return _param.duration > 0;
		return false;
	}
}
