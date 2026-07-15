using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[System.Serializable]
public abstract class BaseState
{
    public FSM fsm;
    
    public abstract void OnExit();

    public abstract void OnEnter();

    public abstract void Update();

    public abstract void FixedUpdate();

    public abstract void Transitions();
}

public enum FSMStates
{
    Idle,
    GettingFood,
    Dead,
    Eating,
    Hungry,
    Entering,
    Queue,
}
