using UnityEngine;

namespace NachoTools.Runtime.Tools
{
    /// <summary>
    /// This is an example of the ScriptableObject "Type Object" pattern.
    /// Instead of a C# Enum, you create an instance of this ScriptableObject in your project for each "enum value" you need.
    /// To add a new category from the Editor, you just Right-Click -> Create -> NachoTools -> Custom Category.
    /// </summary>
    [CreateAssetMenu(fileName = "NewCustomCategory", menuName = "NachoTools/Custom Category")]
    public class CustomCategory : ScriptableObject
    {
        [Tooltip("An optional description for this category type.")]
        [TextArea]
        public string description;
        
        [Tooltip("You can even add colors or other data here, which you can't easily do with a standard enum!")]
        public Color categoryColor = Color.white;
    }
}
