using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(UseConstantValue))]
public class UseConstantValueDrawer : PropertyDrawer
{
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return 36f;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType == SerializedPropertyType.Float)
        {
            EditorGUI.BeginProperty(position, label, property);
            Rect buttonPosition = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            Rect propertyPosition = new Rect(position.x, position.y + buttonPosition.height, position.width,
                EditorGUIUtility.singleLineHeight);
            UseConstantValue useConstantValueAttribute = (UseConstantValue)attribute;
            if (GUI.Button(buttonPosition, "Toggle Constant"))
            {
                useConstantValueAttribute.choice = !useConstantValueAttribute.choice;
            }

            if (useConstantValueAttribute.choice)
            {
                EditorGUI.LabelField(propertyPosition, label,
                    new GUIContent(useConstantValueAttribute.constantValue.ToString()));
                property.floatValue = useConstantValueAttribute.constantValue;
            }
            else
            {
                EditorGUI.PropertyField(propertyPosition, property, label);
            }

            EditorGUI.EndProperty();
        }
        else
        {
            EditorGUI.LabelField(position, "Use constant value Attribute only works on float values");
        }
    }
}
