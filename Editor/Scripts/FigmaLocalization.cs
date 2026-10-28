using System;
using UnityEditor;

namespace FigmaUiImporter.Editor
{
    internal enum FigmaLanguage
    {
        English,
        Chinese
    }

    /// <summary>
    /// Lightweight editor localization. Chinese source strings remain in code; English is provided at each call site.
    /// </summary>
    internal static class FigmaLocalization
    {
        private const string PrefKey = "FigmaUiImporter.Language";
        private static FigmaLanguage _language;

        public static event Action LanguageChanged;

        static FigmaLocalization()
        {
            int saved = EditorPrefs.GetInt(PrefKey, (int)FigmaLanguage.English);
            _language = saved == (int)FigmaLanguage.Chinese ? FigmaLanguage.Chinese : FigmaLanguage.English;
        }

        public static FigmaLanguage CurrentLanguage
        {
            get { return _language; }
        }

        public static bool IsEnglish
        {
            get { return _language == FigmaLanguage.English; }
        }

        public static void SetLanguage(FigmaLanguage language)
        {
            if (_language == language)
            {
                return;
            }

            _language = language;
            EditorPrefs.SetInt(PrefKey, (int)language);
            Action handler = LanguageChanged;
            if (handler != null)
            {
                handler();
            }
        }

        public static void ToggleLanguage()
        {
            SetLanguage(IsEnglish ? FigmaLanguage.Chinese : FigmaLanguage.English);
        }

        public static string L(string chinese, string english)
        {
            return IsEnglish ? english : chinese;
        }

        public static string F(string chineseFormat, string englishFormat, params object[] args)
        {
            string format = IsEnglish ? englishFormat : chineseFormat;
            return args == null || args.Length == 0 ? format : string.Format(format, args);
        }
    }
}
