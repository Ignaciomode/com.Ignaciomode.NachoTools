using System;


public class Transition<T>
{
	public event Action<T> OnTransition = delegate { };
	public T Input { get { return input; } }
	public StateE<T> TargetState { get { return targetState; } }

	T input;
	StateE<T> targetState;


	public void OnTransitionExecute(T input)
	{
		OnTransition(input);
	}

	public Transition(T input, StateE<T> targetState)
	{
		this.input = input;
		this.targetState = targetState;
	}
}