using UnityEngine;

namespace BaseScripts.Tools.Misc
{
    public class DevOnly : MonoBehaviour
    {
        private void Awake()
        {
            if (!Application.isEditor && !Debug.isDebugBuild)
            {
                gameObject.SetActive(false);
            }
        }
    }
}
