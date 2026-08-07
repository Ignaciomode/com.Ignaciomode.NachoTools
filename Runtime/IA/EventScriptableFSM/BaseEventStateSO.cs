using System.Collections.Generic;
using UnityEngine;

namespace NachoTools.IA.EventScriptableFSM
{
    /// <summary>
    /// Base class for all states in the EventScriptableFSM.
    /// Inherit from this class and create assets to define different states.
    /// </summary>
    public abstract class BaseEventStateSO : ScriptableObject
    {
        [Tooltip("List of states this state is allowed to transition to.")]
        public List<BaseEventStateSO> AllowedTransitions = new List<BaseEventStateSO>();

        public virtual void OnEnter(EventScriptableFSM runner) { }
        public virtual void OnUpdate(EventScriptableFSM runner) { }
        public virtual void OnFixedUpdate(EventScriptableFSM runner) { }
        public virtual void OnExit(EventScriptableFSM runner) { }
    }
}
