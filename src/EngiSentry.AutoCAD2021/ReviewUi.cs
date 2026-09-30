#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

namespace EngiSentry.AutoCAD2021
{
    /// <summary>
    /// Shared EngiSentry dark visual language for the engineer-facing WinForms surfaces hosted in
    /// the AutoCAD palette. The palette is deliberately self-contained (dark surfaces with light
    /// text) so it stays readable regardless of whether AutoCAD is running its light or dark theme;
    /// it is never inherited from ambient WinForms defaults.
    ///
    /// This is the single palette used by the accepted Results UI, the project-engineer review
    /// surface and the equipment placement resolution surface. Extracted verbatim from the nested
    /// <c>ProjectEngineerReviewControl.ReviewUi</c> class so the placement surface reuses it instead
    /// of declaring a parallel palette. Presentation only: it carries no behaviour.
    /// </summary>
    internal static class ReviewUi
    {
        // shared EngiSentry dark palette, kept consistent with the accepted Results UI
        internal static readonly Color Background = Color.FromArgb(30, 31, 34);
        internal static readonly Color Surface = Color.FromArgb(38, 40, 44);
        internal static readonly Color SurfaceAlt = Color.FromArgb(46, 49, 55);
        internal static readonly Color EvidenceAlt = Color.FromArgb(45, 48, 54);
        internal static readonly Color Card = Color.FromArgb(43, 45, 50);
        internal static readonly Color CardHead = Color.FromArgb(44, 47, 52);
        internal static readonly Color Text = Color.FromArgb(235, 237, 240);
        internal static readonly Color Value = Color.FromArgb(225, 228, 232);
        internal static readonly Color Muted = Color.FromArgb(160, 166, 175);
        internal static readonly Color Caption = Color.FromArgb(120, 127, 138);
        internal static readonly Color Section = Color.FromArgb(170, 176, 186);
        internal static readonly Color Border = Color.FromArgb(74, 78, 86);
        internal static readonly Color Accent = Color.FromArgb(0, 120, 200);
        internal static readonly Color Primary = Color.FromArgb(0, 118, 214);
        internal static readonly Color PrimaryEdge = Color.FromArgb(0, 140, 250);
        internal static readonly Color Pending = Color.FromArgb(255, 190, 92);
        internal static readonly Color PendingTint = Color.FromArgb(58, 48, 26);
        internal static readonly Color Completed = Color.FromArgb(108, 220, 155);
        internal static readonly Color CompletedTint = Color.FromArgb(24, 46, 34);
        internal static readonly Color Invalid = Color.FromArgb(255, 112, 112);
        internal static readonly Color InvalidTint = Color.FromArgb(58, 30, 32);
        internal static readonly Color Required = Color.FromArgb(255, 176, 59);
        internal static readonly Color Optional = Color.FromArgb(96, 103, 113);
        internal static readonly Color InputBack = Color.FromArgb(24, 26, 29);
        internal static readonly Color InputText = Color.FromArgb(235, 237, 240);
        internal static readonly Color ErrorBack = Color.FromArgb(58, 30, 32);
        // Disabled actions keep a clearly readable caption instead of fading into the card; the
        // disabled state stays unmistakable because the accent surface is dropped. Tuned for both the
        // Latin (Segoe UI) and the Persian (Tahoma) faces.
        internal static readonly Color DisabledText = Color.FromArgb(196, 202, 210);

        // Fonts are resolved per UI language so Persian renders in a Persian-capable system
        // font (Tahoma) while the Latin surface keeps Segoe UI. Instances are cached per
        // culture; hash/ID values always render in the LTR monospace face.
        private static readonly Dictionary<string, Font> fontCache = new Dictionary<string, Font>(StringComparer.Ordinal);

        internal static Font GetFont(string family, float size, FontStyle style)
        {
            string key = (ReviewLocalization.IsPersian ? "fa|" : "en|") + family + "|" + size.ToString(CultureInfo.InvariantCulture) + "|" + (int)style;
            if (!fontCache.TryGetValue(key, out Font font))
            {
                font = new Font(family, size, style, GraphicsUnit.Point);
                fontCache[key] = font;
            }
            return font;
        }

        internal static Font TitleFont => GetFont(ReviewLocalization.UiFontFamilySemibold, 14.5F, FontStyle.Bold);
        internal static Font SubtitleFont => GetFont(ReviewLocalization.UiFontFamily, 8.5F, FontStyle.Regular);
        internal static Font BadgeFont => GetFont(ReviewLocalization.UiFontFamilySemibold, 9.5F, FontStyle.Bold);
        internal static Font CounterFont => GetFont(ReviewLocalization.UiFontFamilySemibold, 9.5F, FontStyle.Bold);
        internal static Font SectionFont => GetFont(ReviewLocalization.UiFontFamilySemibold, 8.5F, FontStyle.Bold);
        internal static Font CaptionFont => GetFont(ReviewLocalization.UiFontFamily, 8F, FontStyle.Regular);
        internal static Font ChipFont => GetFont(ReviewLocalization.UiFontFamily, 8F, FontStyle.Regular);
        internal static Font BodyFont => GetFont(ReviewLocalization.UiFontFamily, 9F, FontStyle.Regular);
        internal static Font ButtonFont => GetFont(ReviewLocalization.UiFontFamily, 9F, FontStyle.Regular);
        internal static Font PrimaryButtonFont => GetFont(ReviewLocalization.UiFontFamilySemibold, 9.5F, FontStyle.Bold);
        internal static Font ValueFont => GetFont(ReviewLocalization.UiFontFamilySemibold, 9.5F, FontStyle.Regular);
        internal static Font MarkerFont => GetFont(ReviewLocalization.UiFontFamily, 10F, FontStyle.Regular);
        internal static Font MonoFont => GetFont(ReviewLocalization.MonoFontFamily, 9F, FontStyle.Regular);
    }
}
