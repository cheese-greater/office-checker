using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OfficeLockChecker
{
    // A check box painted like XP Luna: navy-bordered box with a soft gradient, orange glow on hover and a green tick.
    internal sealed class XpCheckBox : CheckBox
    {
        private static readonly Color BoxBorder = XpTheme.Hex("#1c5180");
        private static readonly Color DisabledBoxBorder = XpTheme.Hex("#cac8bb");
        private static readonly Color TickColor = XpTheme.Hex("#21a121");

        private bool hovered;
        private bool pressed;

        public XpCheckBox()
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
            pressed = eventArgs.Button == MouseButtons.Left;
            Invalidate();
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

            int boxSize = XpTheme.Scaled(13);
            Rectangle box = new Rectangle(0, (Height - boxSize) / 2, boxSize, boxSize);
            Color topLeftFill = !Enabled ? XpTheme.Hex("#f4f3ee") : pressed && hovered ? XpTheme.Hex("#b0b0a7") : XpTheme.Hex("#dcdcd7");
            Color bottomRightFill = !Enabled ? XpTheme.Hex("#f4f3ee") : pressed && hovered ? XpTheme.Hex("#e3e1d2") : Color.White;
            using (LinearGradientBrush fillBrush = new LinearGradientBrush(box, topLeftFill, bottomRightFill, 45F))
            {
                graphics.FillRectangle(fillBrush, box);
            }
            int line = Math.Max(1, XpTheme.Scaled(1));
            if (Enabled && hovered && !pressed)
            {
                Rectangle glowBounds = Rectangle.Inflate(box, -line, -line);
                using (LinearGradientBrush glowBrush = new LinearGradientBrush(glowBounds, XpTheme.Hex("#fff0cf"), XpTheme.Hex("#f8b636"), 45F))
                using (Pen glowPen = new Pen(glowBrush, line * 2))
                {
                    glowPen.Alignment = PenAlignment.Inset;
                    graphics.DrawRectangle(glowPen, glowBounds);
                }
            }
            using (Pen borderPen = new Pen(Enabled ? BoxBorder : DisabledBoxBorder, line))
            {
                borderPen.Alignment = PenAlignment.Inset;
                graphics.DrawRectangle(borderPen, box);
            }
            if (Checked)
            {
                DrawTick(graphics, box);
            }

            int gap = XpTheme.Scaled(5);
            Rectangle textBounds = new Rectangle(box.Right + gap, 0, Math.Max(0, Width - box.Right - gap), Height);
            TextFormatFlags textFormat = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
            if (!ShowKeyboardCues)
            {
                textFormat |= TextFormatFlags.HidePrefix;
            }
            TextRenderer.DrawText(graphics, Text, Font, textBounds, Enabled ? ForeColor : XpTheme.DisabledText, textFormat);
            if (Focused && ShowFocusCues)
            {
                Size textSize = TextRenderer.MeasureText(graphics, Text, Font, textBounds.Size, textFormat);
                Rectangle focusBounds = new Rectangle(textBounds.X - 1, (Height - textSize.Height) / 2, textSize.Width + 2, textSize.Height);
                ControlPaint.DrawFocusRectangle(graphics, focusBounds);
            }
        }

        private void DrawTick(Graphics graphics, Rectangle box)
        {
            float unit = box.Width / 13F;
            SmoothingMode previousSmoothing = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen tickPen = new Pen(Enabled ? TickColor : XpTheme.DisabledText, 2.2F * unit))
            {
                PointF[] tick =
                {
                    new PointF(box.X + 3.3F * unit, box.Y + 6.2F * unit),
                    new PointF(box.X + 5.5F * unit, box.Y + 8.6F * unit),
                    new PointF(box.X + 9.8F * unit, box.Y + 3.9F * unit)
                };
                graphics.DrawLines(tickPen, tick);
            }
            graphics.SmoothingMode = previousSmoothing;
        }
    }
}
