using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OfficeLockChecker
{
    // Draws a Windows XP Luna title bar and frame in the client area while Windows keeps move, resize, snap and maximize.
    internal class XpForm : Form
    {
        private enum CaptionButton
        {
            None,
            Minimize,
            Maximize,
            Close
        }

        // The real window keeps these so Windows still offers snap, maximize, minimize and their animations.
        private const int NativeFrameStyles = NativeMethods.WS_CAPTION | NativeMethods.WS_THICKFRAME;

        private const int CaptionButtonSize = 21;

        private static readonly CaptionButton[] CaptionButtons = { CaptionButton.Minimize, CaptionButton.Maximize, CaptionButton.Close };

        // Title bar shading matched to a real XP window: dark top edge, glossy band, flat body, lighter band, dark bottom line.
        private static readonly float[] TitleStops =
        {
            0F, 0.04F, 0.06F, 0.08F, 0.10F, 0.14F, 0.20F, 0.24F, 0.56F, 0.66F, 0.76F, 0.88F, 0.92F, 0.95F, 1F
        };
        private static readonly Color[] ActiveTitleColors =
        {
            XpTheme.Hex("#0058ee"), XpTheme.Hex("#3593ff"), XpTheme.Hex("#288eff"), XpTheme.Hex("#127dff"), XpTheme.Hex("#036ffc"),
            XpTheme.Hex("#0262ee"), XpTheme.Hex("#0057e5"), XpTheme.Hex("#0054e3"), XpTheme.Hex("#0055eb"), XpTheme.Hex("#005bf5"),
            XpTheme.Hex("#026afe"), XpTheme.Hex("#0066fb"), XpTheme.Hex("#0057e5"), XpTheme.Hex("#0040ab"), XpTheme.Hex("#003092")
        };
        private static readonly Color[] InactiveTitleColors = Array.ConvertAll<Color, Color>(ActiveTitleColors, WashOut);
        private static readonly Color ActiveTitleShadow = XpTheme.Hex("#0f1089");
        private static readonly Color InactiveTitleText = XpTheme.Hex("#d8e4f8");

        // The one-pixel outline around the title bar: top and left in the frame's light blue, right in its dark blue.
        private static readonly Color ActiveOutlineLight = XpTheme.Hex("#0831d9");
        private static readonly Color ActiveOutlineDark = XpTheme.Hex("#001ea0");
        private static readonly Color InactiveOutlineLight = WashOut(ActiveOutlineLight);
        private static readonly Color InactiveOutlineDark = WashOut(ActiveOutlineDark);

        // Frame bands from the outer edge inwards: light on the left, dark on the right and bottom.
        private static readonly Color[] ActiveFrameLight =
        {
            XpTheme.Hex("#0831d9"), XpTheme.Hex("#166aee"), XpTheme.Hex("#0855dd"), XpTheme.Hex("#0855dd")
        };
        private static readonly Color[] ActiveFrameDark =
        {
            XpTheme.Hex("#00138c"), XpTheme.Hex("#001ea0"), XpTheme.Hex("#003bda"), XpTheme.Hex("#0855dd")
        };
        private static readonly Color[] InactiveFrameLight = Array.ConvertAll<Color, Color>(ActiveFrameLight, WashOut);
        private static readonly Color[] InactiveFrameDark = Array.ConvertAll<Color, Color>(ActiveFrameDark, WashOut);

        // Caption buttons from XP.css (MIT), traced pixel for pixel from Windows XP; see assets/Convert-XpCaptionButtons.ps1.
        private static readonly Dictionary<string, Bitmap> CaptionImages = LoadCaptionImages();
        // Inactive XP windows show their caption buttons faded.
        private static readonly ImageAttributes InactiveButtonAttributes = CreateFadeAttributes(0.6F);

        private CaptionButton hoveredButton;
        private CaptionButton pressedButton;
        private bool isActive;
        private Icon captionIcon;
        private Icon captionIconSource;
        private Region windowRegion;

        public XpForm()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = XpTheme.WindowBackground;
            SizeGripStyle = SizeGripStyle.Hide;
            UpdateChrome();
        }

        private bool IsMaximized
        {
            get { return IsHandleCreated ? NativeMethods.IsZoomed(Handle) : WindowState == FormWindowState.Maximized; }
        }

        // XP hides the frame when maximized, so the frame width doubles as the title bar's top edge.
        private int FrameWidth
        {
            get { return IsMaximized ? 0 : XpTheme.Scaled(4); }
        }

        private int CaptionHeight
        {
            get { return XpTheme.Scaled(26); }
        }

        private int TitleBarHeight
        {
            get { return FrameWidth + CaptionHeight; }
        }

        // XP rounds the top-left corner slightly more than the top-right.
        private static int LeftCornerRadius
        {
            get { return XpTheme.Scaled(8); }
        }

        private static int RightCornerRadius
        {
            get { return XpTheme.Scaled(7); }
        }

        protected override void OnHandleCreated(EventArgs eventArgs)
        {
            base.OnHandleCreated(eventArgs);
            int style = NativeMethods.GetWindowLong(Handle, NativeMethods.GWL_STYLE);
            NativeMethods.SetWindowLong(Handle, NativeMethods.GWL_STYLE, style | NativeFrameStyles);
            NativeMethods.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_NOMOVE
                | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            // Windows 11 would otherwise round all four corners and outline the window over the Luna frame.
            int cornerPreference = NativeMethods.DWMWCP_DONOTROUND;
            NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));
            int borderColor = NativeMethods.DWMWA_COLOR_NONE;
            NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
            UpdateChrome();
        }

        // WinForms sizes the window by adding the frame these styles describe, such as when restoring from maximized, but the
        // frame here is drawn inside the client area; the styles are hidden from WinForms and put back on the real window.
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.Style &= ~NativeFrameStyles;
                parameters.ExStyle &= ~(NativeMethods.WS_EX_DLGMODALFRAME | NativeMethods.WS_EX_WINDOWEDGE | NativeMethods.WS_EX_CLIENTEDGE);
                return parameters;
            }
        }

        protected override void OnSizeChanged(EventArgs eventArgs)
        {
            UpdateChrome();
            base.OnSizeChanged(eventArgs);
        }

        protected override void OnActivated(EventArgs eventArgs)
        {
            base.OnActivated(eventArgs);
            isActive = true;
            Invalidate();
        }

        protected override void OnDeactivate(EventArgs eventArgs)
        {
            base.OnDeactivate(eventArgs);
            isActive = false;
            Invalidate();
        }

        protected override void OnTextChanged(EventArgs eventArgs)
        {
            base.OnTextChanged(eventArgs);
            InvalidateTitleBar();
        }

        protected override void OnMouseMove(MouseEventArgs eventArgs)
        {
            base.OnMouseMove(eventArgs);
            SetHoveredButton(GetCaptionButtonAt(eventArgs.Location));
        }

        protected override void OnMouseLeave(EventArgs eventArgs)
        {
            base.OnMouseLeave(eventArgs);
            SetHoveredButton(CaptionButton.None);
        }

        protected override void OnMouseDown(MouseEventArgs eventArgs)
        {
            base.OnMouseDown(eventArgs);
            if (eventArgs.Button == MouseButtons.Left)
            {
                pressedButton = GetCaptionButtonAt(eventArgs.Location);
                InvalidateTitleBar();
            }
        }

        protected override void OnMouseUp(MouseEventArgs eventArgs)
        {
            base.OnMouseUp(eventArgs);
            if (eventArgs.Button != MouseButtons.Left)
            {
                return;
            }
            CaptionButton releasedButton = pressedButton;
            pressedButton = CaptionButton.None;
            InvalidateTitleBar();
            if (releasedButton != CaptionButton.None && GetCaptionButtonAt(eventArgs.Location) == releasedButton)
            {
                hoveredButton = CaptionButton.None;
                ClickCaptionButton(releasedButton);
            }
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            base.OnPaint(eventArgs);
            if (FrameWidth > 0)
            {
                DrawFrame(eventArgs.Graphics);
            }
            DrawTitleBar(eventArgs.Graphics);
        }

        protected override void WndProc(ref Message message)
        {
            switch (message.Msg)
            {
                case NativeMethods.WM_NCCALCSIZE:
                    if (NativeMethods.IsZoomed(message.HWnd))
                    {
                        // A maximized window hangs its resize border off screen, so pull the client area back to the work area.
                        NativeMethods.NativeRectangle proposedBounds = (NativeMethods.NativeRectangle)Marshal.PtrToStructure(
                            message.LParam, typeof(NativeMethods.NativeRectangle));
                        Rectangle proposedRectangle = Rectangle.FromLTRB(
                            proposedBounds.Left, proposedBounds.Top, proposedBounds.Right, proposedBounds.Bottom);
                        Rectangle workArea = Screen.FromRectangle(proposedRectangle).WorkingArea;
                        NativeMethods.NativeRectangle clientBounds = new NativeMethods.NativeRectangle
                        {
                            Left = workArea.Left,
                            Top = workArea.Top,
                            Right = workArea.Right,
                            Bottom = workArea.Bottom
                        };
                        Marshal.StructureToPtr(clientBounds, message.LParam, false);
                    }
                    // Returning zero without shrinking the rectangle makes the whole window client area, so nothing native is drawn.
                    message.Result = IntPtr.Zero;
                    return;
                case NativeMethods.WM_STYLECHANGING:
                    if (message.WParam.ToInt64() == NativeMethods.GWL_STYLE)
                    {
                        // WinForms reapplies styles from CreateParams, which leave out the frame styles the real window needs.
                        NativeMethods.StyleChange change = (NativeMethods.StyleChange)Marshal.PtrToStructure(
                            message.LParam, typeof(NativeMethods.StyleChange));
                        change.NewStyle |= NativeFrameStyles;
                        Marshal.StructureToPtr(change, message.LParam, false);
                    }
                    break;
                case NativeMethods.WM_NCHITTEST:
                    message.Result = new IntPtr(HitTest(PointToClient(GetScreenPoint(message.LParam))));
                    return;
                case NativeMethods.WM_NCPAINT:
                case NativeMethods.WM_NCUAHDRAWCAPTION:
                case NativeMethods.WM_NCUAHDRAWFRAME:
                    message.Result = IntPtr.Zero;
                    return;
                case NativeMethods.WM_NCACTIVATE:
                    // Minus one keeps Windows' activation bookkeeping but stops it repainting its own caption.
                    message.LParam = new IntPtr(-1);
                    break;
                case NativeMethods.WM_SETTEXT:
                case NativeMethods.WM_SETICON:
                    // A window with a region gets a classic caption painted on these messages unless it briefly looks hidden.
                    int style = NativeMethods.GetWindowLong(message.HWnd, NativeMethods.GWL_STYLE);
                    NativeMethods.SetWindowLong(message.HWnd, NativeMethods.GWL_STYLE, style & ~NativeMethods.WS_VISIBLE);
                    base.WndProc(ref message);
                    NativeMethods.SetWindowLong(message.HWnd, NativeMethods.GWL_STYLE, style);
                    InvalidateTitleBar();
                    return;
            }
            base.WndProc(ref message);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (captionIcon != null)
                {
                    captionIcon.Dispose();
                }
                if (windowRegion != null)
                {
                    windowRegion.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        private void UpdateChrome()
        {
            if (WindowState == FormWindowState.Minimized)
            {
                return;
            }
            Padding chromePadding = new Padding(FrameWidth, TitleBarHeight, FrameWidth, FrameWidth);
            if (Padding != chromePadding)
            {
                Padding = chromePadding;
            }
            Region previousRegion = windowRegion;
            windowRegion = IsMaximized ? null : CreateWindowRegion();
            Region = windowRegion;
            if (previousRegion != null)
            {
                previousRegion.Dispose();
            }
            Invalidate();
        }

        // Rounded top corners cut row by row, the same staircase the title outline follows; the bottom corners stay square.
        private Region CreateWindowRegion()
        {
            int leftRadius = LeftCornerRadius;
            int rightRadius = RightCornerRadius;
            int cornerRows = Math.Max(leftRadius, rightRadius);
            Region region = new Region(new Rectangle(0, cornerRows, Width, Height - cornerRows));
            for (int row = 0; row < cornerRows; row++)
            {
                int leftInset = CornerInset(leftRadius, row);
                int rightInset = CornerInset(rightRadius, row);
                region.Union(new Rectangle(leftInset, row, Width - leftInset - rightInset, 1));
            }
            return region;
        }

        // Pixels a rounded corner cuts from the start of a row, measured against a circle through the pixel centers.
        private static int CornerInset(int radius, int row)
        {
            if (row < 0 || row >= radius)
            {
                return 0;
            }
            double distanceFromCenter = radius - row - 0.5;
            return (int)Math.Round(radius - Math.Sqrt(radius * radius - distanceFromCenter * distanceFromCenter));
        }

        private int HitTest(Point point)
        {
            if (!IsMaximized)
            {
                Size clientSize = ClientSize;
                int border = XpTheme.Scaled(5);
                int corner = XpTheme.Scaled(16);
                bool nearLeft = point.X < corner;
                bool nearRight = point.X >= clientSize.Width - corner;
                bool nearTop = point.Y < corner;
                bool nearBottom = point.Y >= clientSize.Height - corner;
                if (point.Y < border)
                {
                    return nearLeft ? NativeMethods.HTTOPLEFT : nearRight ? NativeMethods.HTTOPRIGHT : NativeMethods.HTTOP;
                }
                if (point.Y >= clientSize.Height - border)
                {
                    return nearLeft ? NativeMethods.HTBOTTOMLEFT : nearRight ? NativeMethods.HTBOTTOMRIGHT : NativeMethods.HTBOTTOM;
                }
                if (point.X < border)
                {
                    return nearTop ? NativeMethods.HTTOPLEFT : nearBottom ? NativeMethods.HTBOTTOMLEFT : NativeMethods.HTLEFT;
                }
                if (point.X >= clientSize.Width - border)
                {
                    return nearTop ? NativeMethods.HTTOPRIGHT : nearBottom ? NativeMethods.HTBOTTOMRIGHT : NativeMethods.HTRIGHT;
                }
            }
            if (point.Y < TitleBarHeight && GetCaptionButtonAt(point) == CaptionButton.None)
            {
                return NativeMethods.HTCAPTION;
            }
            return NativeMethods.HTCLIENT;
        }

        private static Point GetScreenPoint(IntPtr longParameter)
        {
            long packedPoint = longParameter.ToInt64();
            return new Point((short)(packedPoint & 0xFFFF), (short)((packedPoint >> 16) & 0xFFFF));
        }

        private void ClickCaptionButton(CaptionButton button)
        {
            switch (button)
            {
                case CaptionButton.Minimize:
                    WindowState = FormWindowState.Minimized;
                    break;
                case CaptionButton.Maximize:
                    WindowState = IsMaximized ? FormWindowState.Normal : FormWindowState.Maximized;
                    break;
                case CaptionButton.Close:
                    Close();
                    break;
            }
        }

        private void SetHoveredButton(CaptionButton button)
        {
            if (hoveredButton != button)
            {
                hoveredButton = button;
                InvalidateTitleBar();
            }
        }

        private void InvalidateTitleBar()
        {
            Invalidate(new Rectangle(0, 0, ClientSize.Width, TitleBarHeight));
        }

        // Chrome is laid out in client coordinates: a maximized window is wider than its client area by the off-screen frame.
        private Rectangle GetCaptionButtonBounds(CaptionButton button)
        {
            int size = XpTheme.Scaled(CaptionButtonSize);
            int spacing = XpTheme.Scaled(2);
            int right = ClientSize.Width - FrameWidth - XpTheme.Scaled(1);
            int top = Math.Max(0, FrameWidth + (CaptionHeight - size) / 2 - XpTheme.Scaled(1));
            int positionFromRight = button == CaptionButton.Close ? 0 : button == CaptionButton.Maximize ? 1 : 2;
            return new Rectangle(right - size - positionFromRight * (size + spacing), top, size, size);
        }

        private CaptionButton GetCaptionButtonAt(Point point)
        {
            foreach (CaptionButton button in CaptionButtons)
            {
                if (GetCaptionButtonBounds(button).Contains(point))
                {
                    return button;
                }
            }
            return CaptionButton.None;
        }

        private void DrawFrame(Graphics graphics)
        {
            Color[] lightBands = isActive ? ActiveFrameLight : InactiveFrameLight;
            Color[] darkBands = isActive ? ActiveFrameDark : InactiveFrameDark;
            int frameWidth = FrameWidth;
            int titleBarHeight = TitleBarHeight;
            Size clientSize = ClientSize;
            for (int band = 0; band < lightBands.Length; band++)
            {
                int outer = band * frameWidth / lightBands.Length;
                int inner = (band + 1) * frameWidth / lightBands.Length;
                int thickness = inner - outer;
                int sideHeight = clientSize.Height - titleBarHeight - outer;
                using (SolidBrush lightBrush = new SolidBrush(lightBands[band]))
                using (SolidBrush darkBrush = new SolidBrush(darkBands[band]))
                {
                    graphics.FillRectangle(lightBrush, outer, titleBarHeight, thickness, sideHeight);
                    graphics.FillRectangle(darkBrush, clientSize.Width - inner, titleBarHeight, thickness, sideHeight);
                    graphics.FillRectangle(darkBrush, outer, clientSize.Height - inner, clientSize.Width - outer * 2, thickness);
                }
            }
        }

        private void DrawTitleBar(Graphics graphics)
        {
            int width = ClientSize.Width;
            Rectangle titleBounds = new Rectangle(0, 0, width, TitleBarHeight);
            Color[] titleColors = isActive ? ActiveTitleColors : InactiveTitleColors;
            using (LinearGradientBrush titleBrush = XpTheme.CreateVerticalBlendBrush(titleBounds, titleColors, TitleStops))
            {
                graphics.FillRectangle(titleBrush, titleBounds);
            }
            if (!IsMaximized)
            {
                DrawTitleOutline(graphics, width);
            }

            // The icon and title share the caption buttons' vertical center, as on XP.
            Rectangle closeBounds = GetCaptionButtonBounds(CaptionButton.Close);
            int centerY = closeBounds.Top + closeBounds.Height / 2;
            int iconSize = XpTheme.Scaled(16);
            int iconLeft = FrameWidth + XpTheme.Scaled(3);
            Icon icon = GetCaptionIcon(iconSize);
            if (icon != null)
            {
                graphics.DrawIcon(icon, new Rectangle(iconLeft, centerY - iconSize / 2, iconSize, iconSize));
            }

            int textLeft = iconLeft + iconSize + XpTheme.Scaled(4);
            int textRight = GetCaptionButtonBounds(CaptionButton.Minimize).Left - XpTheme.Scaled(4);
            Rectangle textBounds = new Rectangle(textLeft, centerY - CaptionHeight / 2, Math.Max(0, textRight - textLeft), CaptionHeight);
            const TextFormatFlags titleFormat = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            if (isActive)
            {
                int shadowOffset = Math.Max(1, XpTheme.Scaled(1));
                Rectangle shadowBounds = textBounds;
                shadowBounds.Offset(shadowOffset, shadowOffset);
                TextRenderer.DrawText(graphics, Text, XpTheme.CaptionFont, shadowBounds, ActiveTitleShadow, titleFormat);
            }
            TextRenderer.DrawText(graphics, Text, XpTheme.CaptionFont, textBounds, isActive ? Color.White : InactiveTitleText, titleFormat);

            foreach (CaptionButton button in CaptionButtons)
            {
                DrawCaptionButton(graphics, button);
            }
        }

        private Icon GetCaptionIcon(int size)
        {
            if (Icon == null)
            {
                return null;
            }
            if (captionIconSource != Icon)
            {
                if (captionIcon != null)
                {
                    captionIcon.Dispose();
                }
                captionIcon = new Icon(Icon, size, size);
                captionIconSource = Icon;
            }
            return captionIcon;
        }

        // XP outlines the title bar's top and sides and follows the corner staircase, with a half-tone pixel inside each step.
        private void DrawTitleOutline(Graphics graphics, int width)
        {
            Color lightColor = isActive ? ActiveOutlineLight : InactiveOutlineLight;
            Color darkColor = isActive ? ActiveOutlineDark : InactiveOutlineDark;
            int leftRadius = LeftCornerRadius;
            int rightRadius = RightCornerRadius;
            using (SolidBrush lightBrush = new SolidBrush(lightColor))
            using (SolidBrush lightSoftBrush = new SolidBrush(Color.FromArgb(128, lightColor)))
            using (SolidBrush darkBrush = new SolidBrush(darkColor))
            using (SolidBrush darkSoftBrush = new SolidBrush(Color.FromArgb(128, darkColor)))
            {
                int topLeftInset = CornerInset(leftRadius, 0);
                graphics.FillRectangle(lightBrush, topLeftInset, 0, width - topLeftInset - CornerInset(rightRadius, 0), 1);
                for (int row = 1; row < TitleBarHeight; row++)
                {
                    int leftStart = CornerInset(leftRadius, row);
                    int leftLength = OutlineRunLength(leftRadius, row);
                    graphics.FillRectangle(lightBrush, leftStart, row, leftLength, 1);
                    if (CornerInset(leftRadius, row - 1) > 0)
                    {
                        graphics.FillRectangle(lightSoftBrush, leftStart + leftLength, row, 1, 1);
                    }

                    int rightStart = width - CornerInset(rightRadius, row) - OutlineRunLength(rightRadius, row);
                    graphics.FillRectangle(darkBrush, rightStart, row, OutlineRunLength(rightRadius, row), 1);
                    if (CornerInset(rightRadius, row - 1) > 0)
                    {
                        graphics.FillRectangle(darkSoftBrush, rightStart - 1, row, 1, 1);
                    }
                }
            }
        }

        // A row's outline runs from its own corner inset to where the row above began, so the staircase has no gaps.
        private static int OutlineRunLength(int radius, int row)
        {
            return Math.Max(1, CornerInset(radius, row - 1) - CornerInset(radius, row));
        }

        private void DrawCaptionButton(Graphics graphics, CaptionButton button)
        {
            bool hovered = hoveredButton == button;
            string state = hovered && pressedButton == button ? "pressed" : hovered ? "hover" : "normal";
            string glyph = button == CaptionButton.Close ? "close"
                : button == CaptionButton.Minimize ? "minimize"
                : IsMaximized ? "restore" : "maximize";
            Bitmap image = CaptionImages[glyph + "-" + state];
            Rectangle bounds = GetCaptionButtonBounds(button);

            // The images are pre-scaled to the button size, so nearest neighbour copies them pixel for pixel.
            InterpolationMode previousInterpolation = graphics.InterpolationMode;
            PixelOffsetMode previousPixelOffset = graphics.PixelOffsetMode;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            ImageAttributes attributes = isActive ? null : InactiveButtonAttributes;
            graphics.DrawImage(image, bounds, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
            graphics.InterpolationMode = previousInterpolation;
            graphics.PixelOffsetMode = previousPixelOffset;
        }

        private static Dictionary<string, Bitmap> LoadCaptionImages()
        {
            Dictionary<string, Bitmap> images = new Dictionary<string, Bitmap>();
            int size = XpTheme.Scaled(CaptionButtonSize);
            foreach (string glyph in new[] { "minimize", "maximize", "restore", "close" })
            {
                foreach (string state in new[] { "normal", "hover", "pressed" })
                {
                    string name = glyph + "-" + state;
                    string resourceName = "OfficeLockChecker.caption." + name + ".png";
                    using (Stream imageStream = typeof(XpForm).Assembly.GetManifestResourceStream(resourceName))
                    using (Bitmap original = new Bitmap(imageStream))
                    {
                        images[name] = ScaleCaptionImage(original, size);
                    }
                }
            }
            return images;
        }

        // At 100% the XP bitmaps are used as is; other scales resample smoothly instead of doubling every few pixel rows.
        private static Bitmap ScaleCaptionImage(Bitmap original, int size)
        {
            Bitmap scaled = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(scaled))
            using (ImageAttributes attributes = new ImageAttributes())
            {
                bool isOriginalSize = size == original.Width;
                graphics.InterpolationMode = isOriginalSize ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                Rectangle target = new Rectangle(0, 0, size, size);
                graphics.DrawImage(original, target, 0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
            }
            return scaled;
        }

        private static ImageAttributes CreateFadeAttributes(float opacity)
        {
            ColorMatrix fadeMatrix = new ColorMatrix();
            fadeMatrix.Matrix33 = opacity;
            ImageAttributes attributes = new ImageAttributes();
            attributes.SetColorMatrix(fadeMatrix);
            return attributes;
        }

        // Inactive XP windows show the same caption faded toward a pale periwinkle.
        private static Color WashOut(Color color)
        {
            const float fade = 0.75F;
            return Color.FromArgb(
                (int)(color.R + (150 - color.R) * fade),
                (int)(color.G + (170 - color.G) * fade),
                (int)(color.B + (230 - color.B) * fade));
        }
    }
}
