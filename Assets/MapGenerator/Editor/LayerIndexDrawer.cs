using TacticalMaps.Construction;
using UnityEditor;
using UnityEngine;

namespace TacticalMaps.Editor
{
    [CustomPropertyDrawer(typeof(LayerIndexAttribute))]
    internal sealed class LayerIndexDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect rect, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(rect, label, property);
            property.intValue = EditorGUI.LayerField(rect, label, property.intValue);
            EditorGUI.EndProperty();
        }
    }
}
