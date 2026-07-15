using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class FSM
{
    public BaseState _currentState;
    public FSMStates _currentStateName;
    public Dictionary<FSMStates, BaseState> _allStates= new Dictionary<FSMStates, BaseState>();

    public bool IsDebugging;

    public void AddState(FSMStates key, BaseState state)
    {
        if (state == null)
        {
            if(IsDebugging) Debug.Log("No state added");
            return;
        }
        
        _allStates.Add(key, state);
        state.fsm = this;

    }


    public IEnumerator ChangeStateAfterTime(FSMStates newState, float timeUntilChange)
    {
        
        if (!_allStates.ContainsKey(newState))
        {
            if(IsDebugging) Debug.Log($"The {newState} state doesn't exist in Fsm");
            yield break;
        }

        yield return new WaitForSeconds(timeUntilChange);
        if (_currentState != null)
        {
            _currentState.OnExit();
        }

        _currentState = _allStates[newState];
        _currentStateName = newState;
        if(IsDebugging) Debug.Log($"State changed to {newState}");
        _currentState.OnEnter();


    }


    public void ChangeState(FSMStates newState)
    {
        if (!_allStates.ContainsKey(newState))
        {
            if(IsDebugging) Debug.Log($"The {newState} state doesn't exist in Fsm");
            return;
        }

        if (_currentState != null)
        {
            _currentState.OnExit();
        }

        _currentState = _allStates[newState];
        _currentStateName = newState;
        if(IsDebugging) Debug.Log($"State changed to {newState}");
        _currentState.OnEnter();
    }

    public void Update()
    {
        _currentState.Update();
        if(IsDebugging) Debug.Log($"{_currentState} updated");
    }

    public void FixedUpdate()
    {
        _currentState.FixedUpdate();
    }



}


