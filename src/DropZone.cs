using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OfficeLockChecker
{
    // A drop target styled as an XP Explorer task pane: gradient header with the control's Text, pale blue body below.
    internal sealed class DropZone : Control
    {
        private const TextFormatFlags SingleLineText = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

        private static readonly Color HeaderEndColor = XpTheme.Hex("#c6d3f7");
        private static readonly Color BodyColor = XpTheme.Hex("#d6dff7");
        private static readonly Color HighlightedBodyColor = XpTheme.Hex("#b9cbf3");

        private bool highlighted;
        private bool hovered;
        private string description = string.Empty;
        private string actionText = string.Empty;
        private Icon glyph;
        private Font underlinedFont;

        public DropZone()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Cursor = Cursors.Hand;
        }

        public string Description
        {
            get { return description; }
            set
            {
                description = value ?? string.Empty;
                Invalidate();
            }
        }

        // Shown as a link-style line under the description.
        public string ActionText
        {
            get { return actionText; }
            set
            {
                actionText = value ?? string.Empty;
                Invalidate();
            }
        }

        public Icon Glyph
        {
            get { return glyph; }
            set
            {
                glyph = value;
                Invalidate();
            }
        }

        public bool Highlighted
        {
            get { return highlighted; }
            set
            {
                if (highlighted != value)
                {
                    highlighted = value;
                    Invalidate();
                }
            }
        }

        protected override void OnTextChanged(EventArgs eventArgs)
        {
            base.OnTextChanged(eventArgs);
            Invalidate();
        }

        protected override void OnFontChanged(EventArgs eventArgs)
        {
            base.OnFontChanged(eventArgs);
            if (underlinedFont != null)
            {
                underlinedFont.Dispose();
                underlinedFont = null;
            }
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs eventArgs)
        {
            base.OnMouseEnter(eventArgs);
            hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs eventArgs)
        {
            base.OnMouseLeave(eventArgs);
            hovered = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            Graphics graphics = eventArgs.Graphics;
            graphics.Clear(Parent != null ? Parent.BackColor : XpTheme.WindowBackground);

            int headerHeight = XpTheme.Scaled(25);
            Rectangle header = new Rectangle(0, 0, Width, headerHeight);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath headerPath = XpTheme.CreateTopRoundedRectangle(header, XpTheme.Scaled(3)))
            using (LinearGradientBrush headerBrush = new LinearGradientBrush(header, Color.White, HeaderEndColor, LinearGradientMode.Horizontal))
            {
                graphics.FillPath(headerBrush, headerPath);
            }
            graphics.SmoothingMode = SmoothingMode.Default;
            Rectangle headerText = Rectangle.FromLTRB(XpTheme.Scaled(12), 0, Width - XpTheme.Scaled(12), headerHeight);
            Color headerColor = hovered || highlighted ? XpTheme.TaskPaneHoverText : XpTheme.TaskPaneText;
            TextRenderer.DrawText(graphics, Text, XpTheme.BoldUiFont, headerText, headerColor, SingleLineText);

            Rectangle body = new Rectangle(0, headerHeight, Width, Height - headerHeight);
            using (SolidBrush bodyBrush = new SolidBrush(highlighted ? HighlightedBodyColor : BodyColor))
            {
                graphics.FillRectangle(bodyBrush, body);
            }
            int line = Math.Max(1, XpTheme.Scaled(1));
            using (Pen edgePen = new Pen(Color.White, line))
            {
                edgePen.Alignment = PenAlignment.Inset;
                graphics.DrawRectangle(edgePen, body.X, body.Y - line, body.Width - line, body.Height);
            }
            if (highlighted)
            {
                using (Pen cuePen = new Pen(XpTheme.Selection, line))
                {
                    cuePen.DashStyle = DashStyle.Dot;
                    int inset = XpTheme.Scaled(4);
                    Rectangle cueBounds = Rectangle.Inflate(body, -inset, -inset);
                    graphics.DrawRectangle(cuePen, cueBounds.X, cueBounds.Y, cueBounds.Width - line, cueBounds.Height - line);
                }
            }

            int contentLeft = XpTheme.Scaled(12);
            if (glyph != null)
            {
                int glyphSize = XpTheme.Scaled(32);
                graphics.DrawIcon(glyph, new Rectangle(contentLeft, body.Y + (body.Height - glyphSize) / 2, glyphSize, glyphSize));
                contentLeft += glyphSize + XpTheme.Scaled(10);
            }
            int lineHeight = TextRenderer.MeasureText(graphics, "Ag", Font).Height;
            int lineGap = XpTheme.Scaled(3);
            int textTop = body.Y + (body.Height - lineHeight * 2 - lineGap) / 2;
            int textWidth = Math.Max(0, Width - contentLeft - XpTheme.Scaled(12));
            Rectangle descriptionBounds = new Rectangle(contentLeft, textTop, textWidth, lineHeight);
            TextRenderer.DrawText(graphics, description, Font, descriptionBounds, Color.Black, SingleLineText);
            if (underlinedFont == null)
            {
                underlinedFont = new Font(Font, FontStyle.Underline);
            }
            Font actionFont = hovered ? underlinedFont : Font;
            Rectangle actionBounds = new Rectangle(contentLeft, textTop + lineHeight + lineGap, textWidth, lineHeight);
            TextRenderer.DrawText(graphics, actionText, actionFont, actionBounds, headerColor, SingleLineText);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && underlinedFont != null)
            {
                underlinedFont.Dispose();
                underlinedFont = null;
            }
            base.Dispose(disposing);
        }
    }
}
