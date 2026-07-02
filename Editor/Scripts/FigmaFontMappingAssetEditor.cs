using UnityEditor;
using UnityEngine;

namespace FigmaUiImporter.Editor
{
    [CustomEditor(typeof(FigmaFontMappingAsset))]
    public sealed class FigmaFontMappingAssetEditor : UnityEditor.Editor
    {
        private SerializedProperty _defaultFontProperty;
        private SerializedProperty _entriesProperty;

        private void OnEnable()
        {
            _defaultFontProperty = serializedObject.FindProperty("DefaultFont");
            _entriesProperty = serializedObject.FindProperty("Entries");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(_defaultFontProperty, new GUIContent("默认 TMP 字体"));
            EditorGUILayout.Space(4f);
            EditorGUILayout.PropertyField(_entriesProperty, new GUIContent("映射条目"), true);
            serializedObject.ApplyModifiedProperties();
        }
    }
}
