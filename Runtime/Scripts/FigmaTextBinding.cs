using UnityEngine;

namespace FigmaUiImporter
{
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class FigmaTextBinding : MonoBehaviour
    {
        [SerializeField] private string characters;
        [SerializeField] private string fontFamily;
        [SerializeField] private string fontStyleName;
        [SerializeField] private string fontPostScriptName;
        [SerializeField] private int fontWeight;

        public string Characters { get { return characters; } }
        public string FontFamily { get { return fontFamily; } }
        public string FontStyleName { get { return fontStyleName; } }
        public string FontPostScriptName { get { return fontPostScriptName; } }
        public int FontWeight { get { return fontWeight; } }

        public void SetTextInfo(
            string textCharacters,
            string textFontFamily,
            string textFontStyleName,
            string textFontPostScriptName,
            int textFontWeight)
        {
            characters = textCharacters ?? string.Empty;
            fontFamily = textFontFamily ?? string.Empty;
            fontStyleName = textFontStyleName ?? string.Empty;
            fontPostScriptName = textFontPostScriptName ?? string.Empty;
            fontWeight = textFontWeight;
        }
    }
}
