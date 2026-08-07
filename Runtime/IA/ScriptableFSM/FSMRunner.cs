using UnityEngine;

namespace NachoTools.IA.ScriptableFSM
{
    /// <summary>
    /// Component that runs the ScriptableObject-based FSM.
    /// Attach this to a GameObject and assign an initial State asset in the inspector.
    /// </summary>
    public class FSMRunner : MonoBehaviour
    {
        [Tooltip("The state the FSM will start in.")]
        [SerializeField] private BaseStateSO initialState;
        
        [Header("Debug")]
        [SerializeField] private bool isDebugging;
        
        private BaseStateSO currentState;

        public BaseStateSO CurrentState => currentState;

        private void Start()
        {
            if (initialState != null)
            {
                ChangeState(initialState);
            }
            else if (isDebugging)
            {
                Debug.LogWarning("FSMRunner has no initial state assigned.", this);
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
        /// Transitions the FSM to a new state.
        /// </summary>
        public void ChangeState(BaseStateSO nextState)
        {
            if (nextState == null)
            {
                if (isDebugging) Debug.LogError("Cannot change to a null state.", this);
                return;
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
