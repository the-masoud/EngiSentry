using System.Collections.Generic;
using System.Globalization;
using System.Resources;

namespace EngiSentry.AutoCAD2021
{
    // Resource-backed localization for the ES_ENGINEERREVIEW review surface ONLY.
    //
    // English is the neutral/default language and Persian (fa) is the selectable alternative. All
    // UI prose is read from ReviewStrings.resx (neutral) / ReviewStrings.fa.resx; no UI text is
    // duplicated in code. The selected culture is session-scoped: it is never persisted and never
    // touches review requests/responses, questions, answer vocabularies, classifications,
    // governance, or authority behavior. Governed vocabulary tokens (status words, classifications,
    // answer values, backend validation reasons) are deliberately NOT translated and keep their
    // exact upstream values so hashes, IDs, and persisted semantics stay LTR and unchanged.
    internal static class ReviewStrings
    {
        private static readonly ResourceManager Manager =
            new ResourceManager("EngiSentry.AutoCAD2021.ReviewStrings", typeof(ReviewStrings).Assembly);

        // English is always the default culture of the review surface.
        private static CultureInfo culture = CultureInfo.GetCultureInfo("en");

        internal static CultureInfo Culture
        {
            get => culture;
            set => culture = value ?? CultureInfo.GetCultureInfo("en");
        }

        internal static bool IsPersian
            => culture != null
               && (string.Equals(culture.Name, "fa", System.StringComparison.OrdinalIgnoreCase)
                   || culture.Name.StartsWith("fa-", System.StringComparison.OrdinalIgnoreCase));

        // Returns the localized value, falling back to the key name so a missing entry is visible
        // instead of crashing the surface.
        internal static string Get(string name)
        {
            string value = Manager.GetString(name, culture);
            return string.IsNullOrEmpty(value) ? name : value;
        }

        // Format-string lookup with invariant numeric formatting so IDs/counts stay LTR and stable.
        internal static string Format(string name, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, Get(name), args);
        }
    }

    // Language selection support for the review surface.
    internal static class ReviewLocalization
    {
        internal static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");
        internal static readonly CultureInfo Persian = CultureInfo.GetCultureInfo("fa");

        internal static readonly IReadOnlyList<CultureInfo> SupportedCultures =
            new[] { English, Persian };

        internal static CultureInfo Current => ReviewStrings.Culture;

        internal static bool IsPersian => ReviewStrings.IsPersian;

        // Applies a supported culture; unsupported values fall back to English.
        internal static void Apply(CultureInfo culture)
        {
            ReviewStrings.Culture = Supports(culture) ? culture : English;
        }

        // Toggles between the two supported UI languages.
        internal static CultureInfo Toggle()
        {
            CultureInfo next = ReviewStrings.IsPersian ? English : Persian;
            ReviewStrings.Culture = next;
            return next;
        }

        // The link always names the language the engineer can switch TO.
        internal static string SwitchLabel => ReviewStrings.IsPersian ? "English" : "فارسی";

        internal static bool Supports(CultureInfo culture)
        {
            if (culture == null) return false;
            return string.Equals(culture.Name, English.Name, System.StringComparison.OrdinalIgnoreCase)
                   || string.Equals(culture.Name, Persian.Name, System.StringComparison.OrdinalIgnoreCase)
                   || culture.Name.StartsWith(English.Name + "-", System.StringComparison.OrdinalIgnoreCase)
                   || culture.Name.StartsWith(Persian.Name + "-", System.StringComparison.OrdinalIgnoreCase);
        }

        // Persian text needs a Persian-capable system font. Tahoma ships with every supported
        // Windows release and covers the Persian/Arabic script, so it is the system-safe choice;
        // the Latin surface keeps Segoe UI. Hashes/IDs stay in the LTR monospace face.
        internal static string UiFontFamily => ReviewStrings.IsPersian ? "Tahoma" : "Segoe UI";
        internal static string UiFontFamilySemibold => ReviewStrings.IsPersian ? "Tahoma" : "Segoe UI Semibold";
        internal static string MonoFontFamily => "Consolas";
    }
}
