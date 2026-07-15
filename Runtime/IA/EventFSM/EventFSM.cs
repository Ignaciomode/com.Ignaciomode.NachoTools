using System;
using UnityEngine;

public class EventFSM<T>
{
	public StateE<T> Current { get { return current; } }
	private StateE<T> current;

	public EventFSM(StateE<T> initial)
	{
		current = initial;
		current.Enter(default(T));
	}

	public void SendInput(T input)
	{
		StateE<T> newState;
		//
		if (current.CheckInput(input, out newState))
		{
			// Debug.Log("Entro AL SEND INPUT");

			current.Exit(input);
			current = newState;
			current.Enter(input);
		}
	}


	public void Update()
	{
		current.Update();
	}

	public void LateUpdate()
	{
		current.LateUpdate();
	}

	public void FixedUpdate()
	{
		current.FixedUpdate();
	}
}