using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class StateConfigurer<T>
{
	StateE<T> instance;
	Dictionary<T, Transition<T>> transitions = new Dictionary<T, Transition<T>>();

	public StateConfigurer(StateE<T> state)
	{
		instance = state;
	}

	public StateConfigurer<T> SetTransition(T input, StateE<T> target)
	{
		transitions.Add(input, new Transition<T>(input, target));
		return this;
	}

	public void Done()
	{
		
		instance.Configure(transitions);
	}
}

public static class StateConfigurer
{
	public static StateConfigurer<T> Create<T>(StateE<T> state)
	{
		return new StateConfigurer<T>(state);
	}
}