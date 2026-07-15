using UnityEngine;

namespace BaseScripts.Tools.Misc
{
    public class RagdollController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform root;
        private Rigidbody[] _rigidbodies;
        private CharacterJoint[] _joints;
        private Collider[] _colliders;
        [SerializeField] private Collider mainCollider;
        [SerializeField] private Animator _animator;
    
        public void EnableRagdoll()
        {
            _animator.enabled = false;
            foreach (var joint in _joints) joint.enableCollision = true;
            foreach (var col in _colliders) col.enabled = true;
            foreach (var currentRigidbody in _rigidbodies)
            {
                currentRigidbody.isKinematic = false;
                currentRigidbody.linearVelocity = Vector3.zero;
            }
            mainCollider.enabled = false;
        }

        public void EnableAnimator()
        {
            _animator.enabled = true;
            foreach (var joint in _joints) joint.enableCollision = false;
            foreach (var col in _colliders) col.enabled = false;
            foreach (var currentRigidbody in _rigidbodies) currentRigidbody.isKinematic = true;
            mainCollider.enabled = true;

        }

        public void AddImpulseToRagdoll(Vector3 dir, float force, Vector3 offset = new())
        {
            foreach (var currentRigidbody in _rigidbodies)
            {
                currentRigidbody.AddForce((dir + offset) * force, ForceMode.Impulse);
            }
        }

    }
}
