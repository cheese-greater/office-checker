using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace OfficeLockChecker
{
    // Windows XP Luna (blue) colors, fonts and drawing helpers shared by the custom-drawn controls.
    internal static class XpTheme
    {
        public static readonly Color WindowBackground = Hex("#ece9d8");
        public static readonly Color ControlBorder = Hex("#7f9db9");
        public static readonly Color MenuBorder = Hex("#aca899");
        public static readonly Color Selection = Hex("#316ac5");
        public static readonly Color DisabledText = Hex("#aca899");
        public static readonly Color TaskPaneText = Hex("#215dc6");
        public static readonly Color TaskPaneHoverText = Hex("#428eff");

        public static readonly Font UiFont = new Font("Tahoma", 8.25F);
        public static readonly Font BoldUiFont = new Font("Tahoma", 8.25F, FontStyle.Bold);
        public static readonly Font CaptionFont = new Font("Trebuchet MS", 10F, FontStyle.Bold);

        // The app is system DPI aware, so one scale factor applies to every window it shows.
        public static readonly float DpiScale = ReadScreenDpiScale();

        private static readonly Color HeaderBackground = Hex("#ebeadb");
        private static readonly Color HeaderSeparator = Hex("#c7c5b2");
        // List header bottom edge, top line first; hot headers swap the beige shadow for orange.
        private static readonly Color[] HeaderLines = { Hex("#e2decd"), Hex("#d6d2c2"), Hex("#cbc7b8") };
        private static readonly Color[] HotHeaderLines = { Hex("#ffc83c"), Hex("#f9b119"), Hex("#e89b00") };

        public static int Scaled(int pixels)
        {
            return (int)Math.Round(pixels * DpiScale);
        }

        public static Color Hex(string htmlColor)
        {
            return ColorTranslator.FromHtml(htmlColor);
        }

        public static GraphicsPath CreateRoundedRectangle(RectangleF bounds, float radius)
        {
            float diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static GraphicsPath CreateTopRoundedRectangle(RectangleF bounds, float radius)
        {
            float diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddLine(bounds.Right, bounds.Top + radius, bounds.Right, bounds.Bottom);
            path.AddLine(bounds.Right, bounds.Bottom, bounds.Left, bounds.Bottom);
            path.CloseFigure();
            return path;
        }

        public static LinearGradientBrush CreateVerticalBlendBrush(Rectangle bounds, Color[] colors, float[] positions)
        {
            // One pixel of slack on each end stops GDI+ wrapping the last color onto the first row.
            Rectangle brushBounds = new Rectangle(bounds.X, bounds.Y - 1, bounds.Width, bounds.Height + 2);
            LinearGradientBrush brush = new LinearGradientBrush(brushBounds, colors[0], colors[colors.Length - 1], LinearGradientMode.Vertical);
            brush.InterpolationColors = new ColorBlend { Colors = colors, Positions = positions };
            return brush;
        }

        public static void DrawListHeader(Graphics graphics, Rectangle bounds, bool isHot, bool drawSeparator)
        {
            using (SolidBrush backgroundBrush = new SolidBrush(HeaderBackground))
            {
                graphics.FillRectangle(backgroundBrush, bounds);
            }
            int lineThickness = Math.Max(1, Scaled(1));
            Color[] bottomLines = isHot ? HotHeaderLines : HeaderLines;
            for (int index = 0; index < bottomLines.Length; index++)
            {
                using (SolidBrush lineBrush = new SolidBrush(bottomLines[index]))
                {
                    int lineTop = bounds.Bottom - (bottomLines.Length - index) * lineThickness;
                    graphics.FillRectangle(lineBrush, bounds.Left, lineTop, bounds.Width, lineThickness);
                }
            }
            if (drawSeparator)
            {
                int inset = Scaled(4);
                int separatorHeight = bounds.Height - inset * 2;
                using (SolidBrush separatorBrush = new SolidBrush(HeaderSeparator))
                {
                    graphics.FillRectangle(separatorBrush, bounds.Right - lineThickness * 2, bounds.Top + inset, lineThickness, separatorHeight);
                }
                graphics.FillRectangle(Brushes.White, bounds.Right - lineThickness, bounds.Top + inset, lineThickness, separatorHeight);
            }
        }

        private static float ReadScreenDpiScale()
        {
            using (Graphics screenGraphics = Graphics.FromHwnd(IntPtr.Zero))
            {
                return screenGraphics.DpiX / 96F;
            }
        }
    }
}
