using UnityEngine;

namespace NachoTools.IA.ScriptableFSM
{
    /// <summary>
    /// Base class for all states in the ScriptableObject-based FSM.
    /// Inherit from this class and create assets to define different states.
    /// Keep in mind that ScriptableObjects are shared across all instances,
    /// so do not store instance-specific data (like timers or health) in this class.
    /// Store that data in components attached to the FSMRunner instead.
    /// </summary>
    public abstract class BaseStateSO : ScriptableObject
    {
        public virtual void OnEnter(FSMRunner runner) { }
        public virtual void OnUpdate(FSMRunner runner) { }
        public virtual void OnFixedUpdate(FSMRunner runner) { }
        public virtual void OnExit(FSMRunner runner) { }
    }
}
