using UnityEngine;

namespace NachoTools.Runtime.Tools
{
    public class CustomCategoryExample : MonoBehaviour
    {
        [Header("Instead of an enum, drag your CustomCategory ScriptableObjects here!")]
        
        [SerializeField] 
        private CustomCategory primaryCategory;
        
        [SerializeField] 
        private CustomCategory secondaryCategory;

        public void CheckCategory(CustomCategory categoryToCheck)
        {
            // You can compare them just like an enum!
            if (primaryCategory == categoryToCheck)
            {
                Debug.Log($"This matches the primary category! It's colored {primaryCategory.categoryColor}");
            }
            else
            {
                Debug.Log("This does NOT match the primary category.");
            }
        }
    }
}
