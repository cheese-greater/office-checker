using System;
using System.Drawing;
using System.Drawing.Drawing2D;
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

        private static readonly CaptionButton[] CaptionButtons = { CaptionButton.Minimize, CaptionButton.Maximize, CaptionButton.Close };

        // Title bar gradient stops taken from XP.css; the inactive colors are the same bands washed out.
        private static readonly float[] TitleStops = { 0F, 0.08F, 0.40F, 0.88F, 0.93F, 0.95F, 0.96F, 1F };
        private static readonly Color[] ActiveTitleColors =
        {
            XpTheme.Hex("#0997ff"), XpTheme.Hex("#0053ee"), XpTheme.Hex("#0050ee"), XpTheme.Hex("#0066ff"),
            XpTheme.Hex("#0066ff"), XpTheme.Hex("#005bff"), XpTheme.Hex("#003dd7"), XpTheme.Hex("#003dd7")
        };
        private static readonly Color[] InactiveTitleColors =
        {
            XpTheme.Hex("#b6cdf7"), XpTheme.Hex("#8da9ea"), XpTheme.Hex("#7e9de5"), XpTheme.Hex("#94b4f2"),
            XpTheme.Hex("#94b4f2"), XpTheme.Hex("#87a6ee"), XpTheme.Hex("#6f8cd8"), XpTheme.Hex("#6f8cd8")
        };
        private static readonly Color ActiveTitleShadow = XpTheme.Hex("#0f1089");
        private static readonly Color InactiveTitleText = XpTheme.Hex("#d8e4f8");

        // Frame bands from the outer edge inwards: light on the left, dark on the right and bottom.
        private static readonly Color[] ActiveFrameLight =
        {
            XpTheme.Hex("#0831d9"), XpTheme.Hex("#166aee"), XpTheme.Hex("#0855dd"), XpTheme.Hex("#0855dd")
        };
        private static readonly Color[] ActiveFrameDark =
        {
            XpTheme.Hex("#00138c"), XpTheme.Hex("#001ea0"), XpTheme.Hex("#003bda"), XpTheme.Hex("#0855dd")
        };
        private static readonly Color[] InactiveFrameLight =
        {
            XpTheme.Hex("#6582d4"), XpTheme.Hex("#8da9ea"), XpTheme.Hex("#7a96df"), XpTheme.Hex("#7a96df")
        };
        private static readonly Color[] InactiveFrameDark =
        {
            XpTheme.Hex("#4b64b8"), XpTheme.Hex("#5a73c3"), XpTheme.Hex("#7088d8"), XpTheme.Hex("#7a96df")
        };

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

        // Rounded top corners and square bottom corners, as on every XP window.
        private Region CreateWindowRegion()
        {
            using (GraphicsPath outline = XpTheme.CreateTopRoundedRectangle(new RectangleF(0, 0, Width, Height), XpTheme.Scaled(8)))
            {
                return new Region(outline);
            }
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
            int size = XpTheme.Scaled(21);
            int spacing = XpTheme.Scaled(2);
            int right = ClientSize.Width - FrameWidth - XpTheme.Scaled(1);
            int top = FrameWidth + (CaptionHeight - size) / 2;
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
            Rectangle titleBounds = new Rectangle(0, 0, ClientSize.Width, TitleBarHeight);
            Color[] titleColors = isActive ? ActiveTitleColors : InactiveTitleColors;
            using (LinearGradientBrush titleBrush = XpTheme.CreateVerticalBlendBrush(titleBounds, titleColors, TitleStops))
            {
                graphics.FillRectangle(titleBrush, titleBounds);
            }

            int captionTop = FrameWidth;
            int iconSize = XpTheme.Scaled(16);
            int iconLeft = FrameWidth + XpTheme.Scaled(3);
            Icon icon = GetCaptionIcon(iconSize);
            if (icon != null)
            {
                graphics.DrawIcon(icon, new Rectangle(iconLeft, captionTop + (CaptionHeight - iconSize) / 2, iconSize, iconSize));
            }

            int textLeft = iconLeft + iconSize + XpTheme.Scaled(4);
            int textRight = GetCaptionButtonBounds(CaptionButton.Minimize).Left - XpTheme.Scaled(4);
            Rectangle textBounds = new Rectangle(textLeft, captionTop, Math.Max(0, textRight - textLeft), CaptionHeight);
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

        private void DrawCaptionButton(Graphics graphics, CaptionButton button)
        {
            Rectangle bounds = GetCaptionButtonBounds(button);
            bool hovered = hoveredButton == button;
            bool pressed = hovered && pressedButton == button;
            Color shadeColor;
            Color glowColor;
            if (button == CaptionButton.Close)
            {
                shadeColor = XpTheme.Hex(pressed ? "#a83418" : hovered ? "#e1603a" : "#c94a26");
                glowColor = XpTheme.Hex(pressed ? "#cf6440" : hovered ? "#ffb393" : "#f09272");
            }
            else
            {
                shadeColor = XpTheme.Hex(pressed ? "#1747c4" : hovered ? "#3a7bff" : "#2361ea");
                glowColor = XpTheme.Hex(pressed ? "#3b70db" : hovered ? "#9ccaff" : "#6ba6ff");
            }

            SmoothingMode previousSmoothing = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float borderWidth = Math.Max(1F, XpTheme.DpiScale);
            RectangleF faceBounds = RectangleF.Inflate(bounds, -borderWidth / 2, -borderWidth / 2);
            using (GraphicsPath face = XpTheme.CreateRoundedRectangle(faceBounds, 3F * XpTheme.DpiScale))
            using (LinearGradientBrush faceBrush = new LinearGradientBrush(bounds, shadeColor, glowColor, 45F))
            using (Pen borderPen = new Pen(Color.White, borderWidth))
            {
                graphics.FillPath(faceBrush, face);
                graphics.DrawPath(borderPen, face);
                DrawCaptionGlyph(graphics, button, bounds, faceBrush);
                if (!isActive)
                {
                    using (SolidBrush washBrush = new SolidBrush(Color.FromArgb(100, Color.White)))
                    {
                        graphics.FillPath(washBrush, face);
                    }
                }
            }
            graphics.SmoothingMode = previousSmoothing;
        }

        // Glyphs are laid out on the 21-pixel grid of the original XP buttons and scaled to the button size.
        private void DrawCaptionGlyph(Graphics graphics, CaptionButton button, Rectangle bounds, Brush faceBrush)
        {
            float unit = bounds.Width / 21F;
            switch (button)
            {
                case CaptionButton.Minimize:
                    graphics.FillRectangle(Brushes.White, GridRectangle(bounds, unit, 5, 13, 7, 3));
                    break;
                case CaptionButton.Maximize:
                    if (IsMaximized)
                    {
                        DrawWindowGlyph(graphics, GridRectangle(bounds, unit, 8, 4, 9, 9), unit, 2);
                        Rectangle frontWindow = GridRectangle(bounds, unit, 4, 8, 9, 9);
                        graphics.FillRectangle(faceBrush, frontWindow);
                        DrawWindowGlyph(graphics, frontWindow, unit, 2);
                    }
                    else
                    {
                        DrawWindowGlyph(graphics, GridRectangle(bounds, unit, 5, 5, 11, 11), unit, 3);
                    }
                    break;
                case CaptionButton.Close:
                    using (Pen crossPen = new Pen(Color.White, 2.4F * unit))
                    {
                        crossPen.StartCap = LineCap.Square;
                        crossPen.EndCap = LineCap.Square;
                        float near = 6.5F * unit;
                        float far = 14.5F * unit;
                        graphics.DrawLine(crossPen, bounds.X + near, bounds.Y + near, bounds.X + far, bounds.Y + far);
                        graphics.DrawLine(crossPen, bounds.X + far, bounds.Y + near, bounds.X + near, bounds.Y + far);
                    }
                    break;
            }
        }

        // An outlined window with a thick title edge, as in the XP maximize and restore glyphs.
        private static void DrawWindowGlyph(Graphics graphics, Rectangle bounds, float unit, int titleUnits)
        {
            int line = Math.Max(1, (int)Math.Round(unit));
            int titleThickness = Math.Max(line + 1, (int)Math.Round(titleUnits * unit));
            graphics.FillRectangle(Brushes.White, bounds.X, bounds.Y, bounds.Width, titleThickness);
            graphics.FillRectangle(Brushes.White, bounds.X, bounds.Y, line, bounds.Height);
            graphics.FillRectangle(Brushes.White, bounds.Right - line, bounds.Y, line, bounds.Height);
            graphics.FillRectangle(Brushes.White, bounds.X, bounds.Bottom - line, bounds.Width, line);
        }

        private static Rectangle GridRectangle(Rectangle bounds, float unit, float left, float top, float width, float height)
        {
            return new Rectangle(
                bounds.X + (int)Math.Round(left * unit),
                bounds.Y + (int)Math.Round(top * unit),
                (int)Math.Round(width * unit),
                (int)Math.Round(height * unit));
        }
    }
}
