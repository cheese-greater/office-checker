using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OfficeLockChecker
{
    // A push button painted like XP Luna: beige gradient face, navy border, orange glow on hover and blue glow on focus.
    internal sealed class XpButton : Button
    {
        private static readonly Color Border = XpTheme.Hex("#003c74");
        private static readonly Color DisabledBorder = XpTheme.Hex("#c9c7ba");
        private static readonly Color DisabledFace = XpTheme.Hex("#f5f4ea");
        private static readonly Color[] FaceColors = { Color.White, XpTheme.Hex("#ecebe6"), XpTheme.Hex("#d6d0c5") };
        private static readonly float[] FaceStops = { 0F, 0.86F, 1F };
        private static readonly Color[] PressedFaceColors =
        {
            XpTheme.Hex("#cdcac3"), XpTheme.Hex("#e3e3db"), XpTheme.Hex("#e5e5de"), XpTheme.Hex("#f2f2f1")
        };
        private static readonly float[] PressedFaceStops = { 0F, 0.08F, 0.94F, 1F };

        private bool hovered;
        private bool pressed;

        public XpButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
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

        protected override void OnMouseDown(MouseEventArgs eventArgs)
        {
            base.OnMouseDown(eventArgs);
            if (eventArgs.Button == MouseButtons.Left)
            {
                pressed = true;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs eventArgs)
        {
            base.OnMouseUp(eventArgs);
            pressed = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            Graphics graphics = eventArgs.Graphics;
            graphics.Clear(Parent != null ? Parent.BackColor : XpTheme.WindowBackground);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            bool showPressed = Enabled && pressed && hovered;
            float borderWidth = Math.Max(1F, XpTheme.DpiScale);
            float radius = 3F * XpTheme.DpiScale;
            RectangleF outline = new RectangleF(borderWidth / 2, borderWidth / 2, Width - borderWidth, Height - borderWidth);
            using (GraphicsPath outlinePath = XpTheme.CreateRoundedRectangle(outline, radius))
            {
                if (!Enabled)
                {
                    using (SolidBrush faceBrush = new SolidBrush(DisabledFace))
                    {
                        graphics.FillPath(faceBrush, outlinePath);
                    }
                }
                else
                {
                    Color[] colors = showPressed ? PressedFaceColors : FaceColors;
                    float[] stops = showPressed ? PressedFaceStops : FaceStops;
                    using (LinearGradientBrush faceBrush = XpTheme.CreateVerticalBlendBrush(ClientRectangle, colors, stops))
                    {
                        graphics.FillPath(faceBrush, outlinePath);
                    }
                    if (hovered && !showPressed)
                    {
                        DrawInnerGlow(graphics, outline, radius, borderWidth, XpTheme.Hex("#fff0cf"), XpTheme.Hex("#e5a01a"));
                    }
                    else if (Focused && !showPressed)
                    {
                        DrawInnerGlow(graphics, outline, radius, borderWidth, XpTheme.Hex("#cee7ff"), XpTheme.Hex("#89ade4"));
                    }
                }
                using (Pen borderPen = new Pen(Enabled ? Border : DisabledBorder, borderWidth))
                {
                    graphics.DrawPath(borderPen, outlinePath);
                }
            }

            TextFormatFlags textFormat = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
            if (!ShowKeyboardCues)
            {
                textFormat |= TextFormatFlags.HidePrefix;
            }
            Rectangle textBounds = ClientRectangle;
            if (showPressed)
            {
                textBounds.Offset(1, 1);
            }
            TextRenderer.DrawText(graphics, Text, Font, textBounds, Enabled ? Color.Black : XpTheme.DisabledText, textFormat);
            if (Focused && ShowFocusCues)
            {
                int inset = XpTheme.Scaled(4);
                ControlPaint.DrawFocusRectangle(graphics, Rectangle.Inflate(ClientRectangle, -inset, -inset));
            }
        }

        private static void DrawInnerGlow(
            Graphics graphics, RectangleF outline, float radius, float borderWidth, Color topColor, Color bottomColor)
        {
            RectangleF glowBounds = RectangleF.Inflate(outline, -borderWidth, -borderWidth);
            if (glowBounds.Width <= 0 || glowBounds.Height <= 0)
            {
                return;
            }
            using (GraphicsPath glowPath = XpTheme.CreateRoundedRectangle(glowBounds, Math.Max(1F, radius - borderWidth)))
            using (LinearGradientBrush glowBrush = new LinearGradientBrush(glowBounds, topColor, bottomColor, LinearGradientMode.Vertical))
            using (Pen glowPen = new Pen(glowBrush, borderWidth * 2))
            {
                graphics.DrawPath(glowPen, glowPath);
            }
        }
    }
}
