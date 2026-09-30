#nullable disable
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace EngiSentry.AutoCAD2021
{
    // Shared presentation primitives for the engineer-facing WinForms surfaces hosted in the AutoCAD
    // palette (the accepted project-engineer review surface and the equipment placement resolution
    // surface). They were extracted verbatim from the nested helpers of ProjectEngineerReviewControl
    // so both surfaces compose the same bordered cards, field pairs, buttons, status pills and
    // message lines instead of each declaring its own copy.
    //
    // Presentation only: nothing here reads or writes review, disposition, resolution, governance or
    // persistence state. The palette and fonts come from ReviewUi; these types only lay them out.

    /// <summary>Panel with a single hairline rule on its top and/or bottom edge, used to separate
    /// the pinned header and footer from the scrolling card column.</summary>
    internal sealed class BorderedPanel : Panel
    {
        public bool TopLine { get; set; }
        public bool BottomLine { get; set; }

        public BorderedPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(ReviewUi.Border, 1))
            {
                if (TopLine) e.Graphics.DrawLine(pen, 0, 0, Width - 1, 0);
                if (BottomLine) e.Graphics.DrawLine(pen, 0, Height - 1, Width - 1, Height - 1);
            }
        }
    }

    /// <summary>Bordered card with an accent bar, an uppercase caption, and an optional right chip.</summary>
    internal sealed class Card : Panel
    {
        internal const int HeaderHeight = 32;
        internal const int PadTop = 6;
        internal const int PadBottom = 12;

        private readonly Panel accent = new Panel { Width = 3, BackColor = ReviewUi.Accent };
        private readonly Label caption = new Label();
        public Panel Body { get; }

        public string Caption => caption.Text;

        public Card(string title, Control chip)
        {
            BackColor = ReviewUi.Surface;
            Padding = new Padding(1);
            Margin = new Padding(12, 0, 12, 12);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            var head = new BorderedPanel { Dock = DockStyle.Top, Height = HeaderHeight, BackColor = ReviewUi.CardHead, BottomLine = true };
            accent.Dock = DockStyle.Left;
            accent.Margin = new Padding(0, 8, 10, 8);
            caption.Text = title.ToUpperInvariant();
            caption.Dock = DockStyle.Fill;
            caption.Font = ReviewUi.SectionFont;
            caption.ForeColor = ReviewUi.Section;
            caption.BackColor = ReviewUi.CardHead;
            caption.TextAlign = ContentAlignment.MiddleLeft;
            caption.AutoEllipsis = true;
            if (chip != null)
            {
                chip.Dock = DockStyle.Right;
                // a chip on the header band shares the band color so only its text reads as a marker
                chip.BackColor = ReviewUi.CardHead;
            }
            // add order sets dock order (reverse of add): the caption fills whatever remains
            // after the accent claims the left edge and the chip claims the right edge
            head.Controls.Add(caption);
            if (chip != null) head.Controls.Add(chip);
            head.Controls.Add(accent);

            Body = new Panel { Dock = DockStyle.Fill, BackColor = ReviewUi.Surface, Padding = new Padding(14, PadTop, 14, PadBottom) };
            Controls.Add(Body);
            Controls.Add(head);
        }

        // total height from the height of the card's content
        public void Fit(int contentHeight)
        {
            Height = 2 + HeaderHeight + PadTop + contentHeight + PadBottom;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(ReviewUi.Border, 1))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }
    }

    /// <summary>Rounded status pill: tinted background with matching foreground, drawn so the badge
    /// reads as a colored tag rather than a bordered label.</summary>
    internal sealed class BadgeLabel : Label
    {
        public Color Tint { get; set; } = ReviewUi.SurfaceAlt;

        public BadgeLabel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            using (var path = ReviewStyle.RoundedPath(r, 8))
            using (var brush = new SolidBrush(Tint)) g.FillPath(brush, path);
            TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
            if (RightToLeft == RightToLeft.Yes) flags |= TextFormatFlags.RightToLeft;
            TextRenderer.DrawText(g, Text, Font, r, ForeColor, flags);
        }
    }

    /// <summary>Message line that prefixes a colored severity glyph while keeping its Text pure, so
    /// the inline validation reason stays machine-readable and copy-safe.</summary>
    internal sealed class SeverityMessage : Label
    {
        public const int None = 0;
        public const int Info = 1;
        public const int Success = 2;
        public const int Warning = 3;
        public const int Error = 4;

        private const int GlyphBox = 20;
        private static Font GlyphFont => ReviewUi.GetFont(ReviewLocalization.UiFontFamily, 8F, FontStyle.Bold);

        private int severity = None;

        public int Severity
        {
            get => severity;
            set { severity = value; Invalidate(); }
        }

        public SeverityMessage()
        {
            AutoSize = false;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var back = new SolidBrush(BackColor)) g.FillRectangle(back, e.ClipRectangle);
            bool rtl = RightToLeft == RightToLeft.Yes;
            Rectangle text = new Rectangle(rtl ? 0 : 2, 0, Math.Max(0, Width - 4), Height);
            if (severity == None)
            {
                TextFormatFlags noneFlags = (rtl ? TextFormatFlags.Right : TextFormatFlags.Left) | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
                if (rtl) noneFlags |= TextFormatFlags.RightToLeft;
                TextRenderer.DrawText(g, Text, Font, text, ForeColor, noneFlags);
                return;
            }
            Color color = severity == Success ? ReviewUi.Completed : severity == Warning ? ReviewUi.Required : severity == Error ? ReviewUi.Invalid : ReviewUi.Accent;
            string glyph = severity == Success ? "✓" : severity == Warning ? "!" : severity == Error ? "✕" : "●";
            // the severity dot sits at the leading edge so the message text follows it in the
            // current reading direction
            Rectangle dot = rtl
                ? new Rectangle(Width - 17, (Height - 15) / 2, 15, 15)
                : new Rectangle(2, (Height - 15) / 2, 15, 15);
            using (var brush = new SolidBrush(color)) g.FillEllipse(brush, dot);
            TextRenderer.DrawText(g, glyph, GlyphFont, dot, Color.FromArgb(28, 30, 34),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            text = rtl
                ? new Rectangle(0, 0, Math.Max(0, Width - GlyphBox - 2), Height)
                : new Rectangle(GlyphBox, 0, Math.Max(0, Width - GlyphBox - 2), Height);
            TextFormatFlags msgFlags = (rtl ? TextFormatFlags.Right : TextFormatFlags.Left) | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            if (rtl) msgFlags |= TextFormatFlags.RightToLeft;
            TextRenderer.DrawText(g, Text, Font, text, ForeColor, msgFlags);
        }
    }

    /// <summary>Action button whose disabled caption stays clearly readable. A plain WinForms
    /// <see cref="Button"/> paints its disabled caption through <c>ControlPaint.DrawStringDisabled</c>,
    /// which ignores <c>ForeColor</c> and blends the text toward the surface — on the dark card that
    /// washes the caption out (the reported <c>ثبت تصمیم</c> case). This button draws its own disabled
    /// face (card surface + hairline edge + the readable disabled caption colour) while leaving the
    /// enabled face to the base button, so hover/press feedback and the accent surface are unchanged.
    /// Presentation only: it carries no behaviour and reuses the shared ReviewUi palette.</summary>
    internal sealed class ReviewActionButton : Button
    {
        // the exact colour the disabled caption is painted with; the enabled caption is the base
        // button's own (white on the accent surface)
        internal Color DisabledCaptionColor => ReviewUi.DisabledText;

        public ReviewActionButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            if (Enabled)
            {
                // enabled rendering (accent surface, border, hover/press, focus) is the base button's
                base.OnPaint(pevent);
                return;
            }

            // disabled: keep the button reading as a disabled action (no accent surface) but paint the
            // caption with the readable disabled colour instead of the faded DrawStringDisabled blend
            Graphics g = pevent.Graphics;
            var bounds = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            using (var back = new SolidBrush(ReviewUi.Card)) g.FillRectangle(back, bounds);
            using (var edge = new Pen(ReviewUi.Border)) g.DrawRectangle(edge, bounds);
            TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            if (RightToLeft == RightToLeft.Yes) flags |= TextFormatFlags.RightToLeft;
            TextRenderer.DrawText(g, Text, Font, bounds, DisabledCaptionColor, flags);
        }
    }

    /// <summary>Hosts a card's form content and gives it a reasonable responsive maximum width: on a
    /// wide window the content stops growing after the shared maximum and the leftover space stays
    /// empty, while on a narrow window the content shrinks with the card so no required field is ever
    /// clipped. The content always hugs the leading edge (left in LTR, right in RTL) so the form stays
    /// grouped with its captions. Presentation only: it only positions its single child.</summary>
    internal sealed class FormContentHost : Panel
    {
        private readonly int maxWidth;
        private bool laying;

        internal FormContentHost(int maxWidth)
        {
            this.maxWidth = maxWidth;
            Dock = DockStyle.Fill;
            BackColor = ReviewUi.Surface;
        }

        // the responsive maximum content width (device pixels) this host enforces
        internal int MaxWidth => maxWidth;

        protected override void OnLayout(LayoutEventArgs levent)
        {
            if (laying)
            {
                base.OnLayout(levent);
                return;
            }
            laying = true;
            try
            {
                base.OnLayout(levent);
                if (Controls.Count == 0) return;
                Control content = Controls[0];
                int available = ClientSize.Width;
                int width = maxWidth > 0 ? Math.Min(available, maxWidth) : available;
                width = Math.Max(0, width);
                int x = RightToLeft == RightToLeft.Yes ? Math.Max(0, available - width) : 0;
                content.Bounds = new Rectangle(x, 0, width, ClientSize.Height);
            }
            finally { laying = false; }
        }
    }

    /// <summary>Stateless layout and control-styling helpers shared by the engineer-facing surfaces.</summary>
    internal static class ReviewStyle
    {
        // A reasonable responsive maximum content width for a card's form: a wide window stops the
        // fields from stretching across the whole card, while a narrower card still shrinks them. The
        // value is a base (96 DPI) metric; callers pass it through the surface's Scale() so the form
        // keeps its proportions on a high-DPI display.
        internal const int FormContentMaxWidthBase = 600;

        // Wraps a card's form grid in the shared bounded host so its fields stay grouped near their
        // captions and stop growing past the shared maximum width. The host keeps the content at the
        // leading edge and shrinks it with the card, so a narrow window never clips a required field.
        internal static Control BoundedContent(Control content, int maxWidth)
        {
            if (content == null) return null;
            content.Dock = DockStyle.None;
            var host = new FormContentHost(maxWidth);
            host.Controls.Add(content);
            return host;
        }

        // A labelled field: an always-visible caption above a full-width input, with an optional
        // right-aligned chip (required marker, character count) on the caption row. The caption is
        // what makes an otherwise ambiguous control self-describing, so it is never omitted.
        internal static Panel FieldPair(string caption, Control input, Control chip = null, bool upperCaseCaption = true)
        {
            var pair = new Panel { Dock = DockStyle.Fill, BackColor = ReviewUi.Surface, Margin = new Padding(0, 0, 16, 0) };
            var captionHost = new Panel { Dock = DockStyle.Top, Height = 16, BackColor = ReviewUi.Surface };
            var label = new Label
            {
                Text = upperCaseCaption ? caption.ToUpperInvariant() : caption, Dock = DockStyle.Fill,
                ForeColor = ReviewUi.Caption, Font = ReviewUi.CaptionFont, BackColor = ReviewUi.Surface,
                TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true
            };
            captionHost.Controls.Add(label);
            if (chip != null)
            {
                chip.Dock = DockStyle.Right;
                captionHost.Controls.Add(chip);
            }
            var fieldHost = new Panel { Dock = DockStyle.Fill, BackColor = ReviewUi.Surface };
            fieldHost.Controls.Add(input);
            pair.Controls.Add(fieldHost);
            pair.Controls.Add(captionHost);
            return pair;
        }

        internal static Label Chip(string text, Color color)
        {
            return new Label
            {
                Text = text, Width = 140, AutoEllipsis = true,
                Font = ReviewUi.ChipFont, ForeColor = color,
                TextAlign = ContentAlignment.MiddleRight, BackColor = ReviewUi.Surface,
                Margin = new Padding(0, 0, 8, 0)
            };
        }

        // Seats a live counter chip on a card header band, sized for its longest text so the
        // header caption keeps its full width and the counter never clips mid-update.
        internal static Label ConfigureChip(Label chip, int width)
        {
            chip.Width = width;
            chip.Font = ReviewUi.ChipFont;
            chip.TextAlign = ContentAlignment.MiddleRight;
            chip.AutoEllipsis = true;
            chip.Margin = new Padding(0, 0, 8, 0);
            return chip;
        }

        // Applies the one shared button styling to any Button. A button that can be disabled should be
        // created as a ReviewActionButton so its disabled caption is drawn with the readable disabled
        // colour instead of the base button's faded DrawStringDisabled blend; an ordinary Button is
        // still styled correctly for the enabled state.
        internal static void StyleButton(Button button, bool primary)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.Font = primary ? ReviewUi.PrimaryButtonFont : ReviewUi.ButtonFont;
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.FlatAppearance.BorderColor = primary ? ReviewUi.PrimaryEdge : ReviewUi.Border;
            button.FlatAppearance.BorderSize = 1;
            ApplyButtonState(button, primary);
            button.EnabledChanged += (sender, args) => ApplyButtonState(button, primary);
        }

        internal static void ApplyButtonState(Button button, bool primary)
        {
            if (button.Enabled)
            {
                button.BackColor = primary ? ReviewUi.Primary : ReviewUi.Card;
                button.ForeColor = Color.White;
            }
            else
            {
                button.BackColor = ReviewUi.Card;
                button.ForeColor = ReviewUi.DisabledText;
            }
        }

        // Direction-aware field association for a labelled value. The caption occupies the leading
        // cell of the row, so the value hugs the leading edge in LTR and the trailing edge in RTL and
        // the pair never drifts to opposite card edges. The value itself stays LTR (RightToLeft.No);
        // only the alignment mirrors, so a technical value is never reordered.
        internal static HorizontalAlignment LabelSideValueAlignment =>
            ReviewLocalization.IsPersian ? HorizontalAlignment.Right : HorizontalAlignment.Left;

        // A caption that describes a control placed after it hugs that control's edge. Captions mirror
        // under RightToLeft, so a trailing-aligned caption sits at the right of its cell in LTR (the
        // control is to its right) and at the left of its cell in RTL (the control is to its left):
        // the caption and its control stay visually adjacent in both languages.
        internal static ContentAlignment ControlSideLabelAlignment => ContentAlignment.MiddleRight;

        // Dark-theme input styling: read-only and editable fields share one readable palette so a
        // read-only evidence pane is never a white block inside the dark surface.
        internal static void StyleTextBox(TextBoxBase box, Font font, bool multiline)
        {
            box.Font = font;
            box.Multiline = multiline;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.BackColor = box.ReadOnly ? ReviewUi.SurfaceAlt : ReviewUi.InputBack;
            box.ForeColor = box.ReadOnly ? ReviewUi.Value : ReviewUi.InputText;
        }

        // Drop-down styling with owner-drawn items: the item objects stay canonical (so selection,
        // capture and persistence are byte-identical); only the painted entry runs through the
        // display layer. This is what keeps the closed combo and its drop-down list readable on the
        // dark surface instead of falling back to the system light list.
        internal static void StyleCombo(ComboBox combo, Font font)
        {
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.FlatStyle = FlatStyle.Flat;
            combo.Font = font;
            combo.BackColor = ReviewUi.InputBack;
            combo.ForeColor = ReviewUi.InputText;
            combo.DrawMode = DrawMode.OwnerDrawFixed;
            combo.ItemHeight = Math.Max(16, (int)Math.Ceiling(font.GetHeight()) + 4);
            combo.DrawItem += DrawComboItem;
        }

        private static void DrawComboItem(object sender, DrawItemEventArgs e)
        {
            ComboBox combo = (ComboBox)sender;
            if (e.Index < 0 || e.Index >= combo.Items.Count) return;
            PaintComboItem(combo, e, combo.Items[e.Index] as string);
        }

        internal static void PaintComboItem(ComboBox combo, DrawItemEventArgs e, string display)
        {
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using (var back = new SolidBrush(selected ? ReviewUi.SurfaceAlt : combo.BackColor))
                e.Graphics.FillRectangle(back, e.Bounds);
            Color fore = selected ? ReviewUi.Value : combo.ForeColor;
            if (string.IsNullOrEmpty(display)) { e.DrawFocusRectangle(); return; }
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
            flags |= combo.RightToLeft == RightToLeft.Yes
                ? TextFormatFlags.Right | TextFormatFlags.RightToLeft
                : TextFormatFlags.Left;
            TextRenderer.DrawText(e.Graphics, display, combo.Font, e.Bounds, fore, flags);
            e.DrawFocusRectangle();
        }

        internal static GraphicsPath RoundedPath(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
