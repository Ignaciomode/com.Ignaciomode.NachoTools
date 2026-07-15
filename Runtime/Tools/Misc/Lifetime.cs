using UnityEngine;

namespace BaseScripts.Tools.Misc
{
    public class Lifetime : MonoBehaviour
    {
        [SerializeField] private float lifetime;
        private void Start()
        {
            Destroy(gameObject, lifetime);
        }
    }
}
