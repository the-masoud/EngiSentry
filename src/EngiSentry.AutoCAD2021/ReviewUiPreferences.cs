#nullable disable
using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace EngiSentry.AutoCAD2021
{
    /// <summary>
    /// Per-Windows-user UI preference store for the engineer-facing surfaces. The only preference
    /// today is the selected interface language ("en" or "fa"); it is stored as exactly
    /// <c>{"language":"en"}</c> or <c>{"language":"fa"}</c> under
    /// <c>%LOCALAPPDATA%\EngiSentry\settings\ui-preferences.json</c>.
    ///
    /// Presentation only: this store never touches review, disposition, resolution, governance or
    /// authority state, and it never uses the registry. Reading never throws — a missing, unreadable,
    /// malformed or unsupported file falls back to English so the surface always opens. Writing
    /// happens only when the stored value actually changes, and a write failure is swallowed so it
    /// can never prevent the UI from opening.
    /// </summary>
    internal static class ReviewUiPreferences
    {
        // Tests point this at an isolated directory; production never sets it. This keeps the real
        // per-user preference file untouched when the suite exercises the store.
        internal const string RootOverrideVariable = "ENGISENTRY_UI_PREFERENCES_ROOT";

        internal const string EnglishCode = "en";
        internal const string PersianCode = "fa";

        internal static string DefaultSettingsRoot =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EngiSentry", "settings");

        internal static string SettingsRoot
        {
            get
            {
                string over = Environment.GetEnvironmentVariable(RootOverrideVariable);
                return string.IsNullOrWhiteSpace(over) ? DefaultSettingsRoot : over;
            }
        }

        internal static string PreferencesPath => Path.Combine(SettingsRoot, "ui-preferences.json");

        // The exact persisted shape: a single "language" member holding "en" or "fa".
        [DataContract]
        private sealed class Document
        {
            [DataMember(Name = "language", Order = 1)] public string Language { get; set; }
        }

        // Returns the persisted language, or English when nothing valid is stored. Never throws.
        internal static CultureInfo Load()
        {
            string code = ReadCode();
            return string.Equals(code, PersianCode, StringComparison.OrdinalIgnoreCase)
                ? CultureInfo.GetCultureInfo(PersianCode)
                : CultureInfo.GetCultureInfo(EnglishCode);
        }

        // Persists the selected language only when it differs from what is already stored. A culture
        // other than Persian is stored as English, so only the two supported values can ever land on
        // disk. Never throws.
        internal static void Save(CultureInfo culture)
        {
            string code = culture != null && string.Equals(culture.Name, PersianCode, StringComparison.OrdinalIgnoreCase)
                ? PersianCode
                : EnglishCode;
            if (string.Equals(ReadCode(), code, StringComparison.Ordinal)) return;
            try
            {
                string path = PreferencesPath;
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                using (FileStream stream = File.Create(path))
                {
                    new DataContractJsonSerializer(typeof(Document)).WriteObject(stream, new Document { Language = code });
                }
            }
            catch
            {
                // A preference write must never prevent the surface from opening.
            }
        }

        // Reads the stored language code, or null when the file is absent or unusable. Never throws.
        private static string ReadCode()
        {
            try
            {
                string path = PreferencesPath;
                if (!File.Exists(path)) return null;
                using (FileStream stream = File.OpenRead(path))
                {
                    var document = new DataContractJsonSerializer(typeof(Document)).ReadObject(stream) as Document;
                    return document?.Language;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
