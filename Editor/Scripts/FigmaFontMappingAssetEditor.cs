using UnityEditor;
using UnityEngine;
using static FigmaUiImporter.Editor.FigmaLocalization;

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
            FigmaLocalization.LanguageChanged += Repaint;
        }

        private void OnDisable()
        {
            FigmaLocalization.LanguageChanged -= Repaint;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(_defaultFontProperty, new GUIContent(L("默认 TMP 字体", "Default TMP Font")));
            EditorGUILayout.Space(4f);
            EditorGUILayout.PropertyField(_entriesProperty, new GUIContent(L("映射条目", "Mapping Entries")), true);
            serializedObject.ApplyModifiedProperties();
        }
    }
}
