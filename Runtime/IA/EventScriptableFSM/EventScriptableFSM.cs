using System.Collections.Generic;
using UnityEngine;

namespace NachoTools.IA.EventScriptableFSM
{
    /// <summary>
    /// Component that runs the EventScriptableFSM.
    /// Attach this to a GameObject and assign an initial State asset in the inspector.
    /// </summary>
    public class EventScriptableFSM : MonoBehaviour
    {
        [Tooltip("The state the FSM will start in.")]
        [SerializeField] private BaseEventStateSO initialState;
        
        [Tooltip("States that can be transitioned to from ANY state (e.g., DeathState).")]
        [SerializeField] private List<BaseEventStateSO> anyStateTransitions = new List<BaseEventStateSO>();
        
        [Header("Debug")]
        [SerializeField] private bool isDebugging;
        
        private BaseEventStateSO currentState;

        public BaseEventStateSO CurrentState => currentState;

        private void Start()
        {
            if (initialState != null)
            {
                // Force transition to initial state bypassing checks
                ChangeState(initialState, force: true);
            }
            else if (isDebugging)
            {
                Debug.LogWarning("EventScriptableFSM has no initial state assigned.", this);
            }
        }

        private void Update()
        {
            if (currentState != null)
            {
                currentState.OnUpdate(this);
            }
        }

        private void FixedUpdate()
        {
            if (currentState != null)
            {
                currentState.OnFixedUpdate(this);
            }
        }

        /// <summary>
        /// Transitions the FSM to a new state if the transition is allowed.
        /// </summary>
        public void ChangeState(BaseEventStateSO nextState, bool force = false)
        {
            if (nextState == null)
            {
                if (isDebugging) Debug.LogError("Cannot change to a null state.", this);
                return;
            }

            // Validate transition
            if (!force)
            {
                if (currentState != null && !currentState.AllowedTransitions.Contains(nextState) && !anyStateTransitions.Contains(nextState))
                {
                    if (isDebugging) Debug.LogWarning($"Transition from {currentState.name} to {nextState.name} is not allowed!", this);
                    return;
                }
            }

            if (currentState != null)
            {
                currentState.OnExit(this);
            }

            currentState = nextState;
            
            if (isDebugging) Debug.Log($"FSM changed to state: {currentState.name}", this);
            
            currentState.OnEnter(this);
        }
    }
}
