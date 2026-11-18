using System;
using UnityEngine;
using UnityEngine.UI;

namespace ARVisualizer
{
    /// <summary>
    /// Gives every Text under this canvas the iOS system typeface. Assign SF Pro (from
    /// developer.apple.com/fonts) to <see cref="font"/> for an exact match everywhere; otherwise the
    /// first installed font in <see cref="systemFonts"/> is used. Text with a custom font keeps it.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("AR Visualizer/HUD Typeface")]
    public sealed class HUDTypeface : MonoBehaviour
    {
        [Tooltip("Optional font asset, for example SF-Pro.ttf imported into the project. Empty: use the system font.")]
        [SerializeField] Font font;
        [Tooltip("Installed fonts to try, in order, when no font asset is assigned.")]
        [SerializeField] string[] systemFonts =
        {
            "SF Pro Text", "SF Pro", "SF Pro Display", ".SF UI Text", ".SFUI-Regular", "System Font",
            ".AppleSystemUIFont", "Helvetica Neue", "Segoe UI", "Arial"
        };
        [SerializeField, HideInInspector] int styleVersion;

        public int StyleVersion => styleVersion;
        public Font FontAsset => font;
        /// <summary>The installed font chosen when no font asset is assigned, or null.</summary>
        public static string SystemFontName { get; private set; }

        static Font systemFont;
        static bool resolved;

        void Awake() => Apply();

        /// <summary>Replaces the built-in font on this canvas, including inactive and template objects.</summary>
        public Font Apply()
        {
            var chosen = font != null ? font : SystemFont(systemFonts);
            if (chosen == null) return null;
            foreach (var text in GetComponentsInChildren<Text>(true))
                if (text.font == null || IsBuiltIn(text.font)) text.font = chosen;
            return chosen;
        }

        static readonly string[] SmallWords = { "and", "or", "to", "of", "a", "the", "in", "on" };
        static readonly string[] Acronyms = { "HUD", "AR", "UI", "UDP", "MAC" };

        /// <summary>"POINTER  ON" becomes "Pointer On", as iOS buttons are written. Acronyms stay upper case.</summary>
        public static string TitleCase(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            var words = string.Join(" ", value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)).Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                if (Array.IndexOf(Acronyms, words[i]) >= 0) continue;
                string lower = words[i].ToLowerInvariant();
                words[i] = i > 0 && Array.IndexOf(SmallWords, lower) >= 0 ? lower
                    : System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(lower);
            }
            return string.Join(" ", words);
        }

        public static bool IsBuiltIn(Font candidate) => candidate != null
            && (candidate.name == "LegacyRuntime" || candidate.name == "Arial");

        static Font SystemFont(string[] names)
        {
            if (resolved) return systemFont;
            resolved = true;
            string[] installed;
            try { installed = Font.GetOSInstalledFontNames() ?? Array.Empty<string>(); }
            catch (Exception) { installed = Array.Empty<string>(); }
            foreach (var name in names ?? Array.Empty<string>())
            {
                string match = Array.Find(installed, candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase));
                if (match == null) continue;
                systemFont = Font.CreateDynamicFontFromOSFont(match, 32);
                if (systemFont == null) continue;
                SystemFontName = match;
                break;
            }
            Debug.Log("[HUD] Typeface: " + (SystemFontName ?? "built-in (no listed font is installed)"));
            return systemFont;
        }
    }
}
