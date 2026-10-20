using System;
using System.Drawing;
using System.Windows.Forms;

namespace OfficeLockChecker
{
    // A details list owner-drawn in the XP style: beige column header with an orange hot edge and solid blue selection.
    internal sealed class ResultsListView : ListView
    {
        private const TextFormatFlags CellText = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

        private readonly HeaderFillerPainter headerFillerPainter = new HeaderFillerPainter();
        private ListViewItem hoveredItem;

        public ResultsListView()
        {
            DoubleBuffered = true;
            OwnerDraw = true;
        }

        protected override void OnHandleCreated(EventArgs eventArgs)
        {
            base.OnHandleCreated(eventArgs);
            IntPtr headerHandle = NativeMethods.SendMessage(Handle, NativeMethods.LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
            if (headerHandle != IntPtr.Zero)
            {
                headerFillerPainter.AssignHandle(headerHandle);
            }
        }

        protected override void OnHandleDestroyed(EventArgs eventArgs)
        {
            headerFillerPainter.ReleaseHandle();
            base.OnHandleDestroyed(eventArgs);
        }

        protected override void OnGotFocus(EventArgs eventArgs)
        {
            base.OnGotFocus(eventArgs);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs eventArgs)
        {
            base.OnLostFocus(eventArgs);
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs eventArgs)
        {
            base.OnMouseMove(eventArgs);
            // Owner-drawn rows get a stray item-only repaint on hover that skips the cells, so repaint each row the pointer enters.
            ListViewItem item = GetItemAt(eventArgs.X, eventArgs.Y);
            if (item != hoveredItem)
            {
                if (hoveredItem != null && hoveredItem.ListView == this)
                {
                    Invalidate(hoveredItem.Bounds);
                }
                if (item != null)
                {
                    Invalidate(item.Bounds);
                }
                hoveredItem = item;
            }
        }

        protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs eventArgs)
        {
            bool isHot = (eventArgs.State & ListViewItemStates.Hot) != 0;
            XpTheme.DrawListHeader(eventArgs.Graphics, eventArgs.Bounds, isHot, true);
            Rectangle textBounds = Rectangle.FromLTRB(
                eventArgs.Bounds.Left + XpTheme.Scaled(6),
                eventArgs.Bounds.Top,
                eventArgs.Bounds.Right - XpTheme.Scaled(4),
                eventArgs.Bounds.Bottom - XpTheme.Scaled(2));
            TextRenderer.DrawText(eventArgs.Graphics, eventArgs.Header.Text, Font, textBounds, Color.Black, CellText);
        }

        protected override void OnDrawSubItem(DrawListViewSubItemEventArgs eventArgs)
        {
            Rectangle cellBounds = eventArgs.Bounds;
            if (eventArgs.ColumnIndex == 0)
            {
                // The first cell reports the bounds of the whole row.
                cellBounds.Width = Columns[0].Width;
            }
            bool isSelected = eventArgs.Item.Selected;
            Color background = isSelected ? (Focused ? XpTheme.Selection : XpTheme.WindowBackground) : eventArgs.Item.BackColor;
            Color textColor = isSelected && Focused ? Color.White : Color.Black;
            using (SolidBrush backgroundBrush = new SolidBrush(background))
            {
                eventArgs.Graphics.FillRectangle(backgroundBrush, cellBounds);
            }

            int textLeft = cellBounds.Left + XpTheme.Scaled(4);
            if (eventArgs.ColumnIndex == 0 && SmallImageList != null && eventArgs.Item.ImageIndex >= 0)
            {
                Size imageSize = SmallImageList.ImageSize;
                int imageTop = cellBounds.Top + (cellBounds.Height - imageSize.Height) / 2;
                SmallImageList.Draw(eventArgs.Graphics, textLeft, imageTop, eventArgs.Item.ImageIndex);
                textLeft += imageSize.Width + XpTheme.Scaled(3);
            }
            Rectangle textBounds = Rectangle.FromLTRB(textLeft, cellBounds.Top, cellBounds.Right - XpTheme.Scaled(2), cellBounds.Bottom);
            TextRenderer.DrawText(eventArgs.Graphics, eventArgs.SubItem.Text, Font, textBounds, textColor, CellText);
        }

        // Owner drawing covers the column headers only; this paints the empty strip to their right in the same style.
        private sealed class HeaderFillerPainter : NativeWindow
        {
            protected override void WndProc(ref Message message)
            {
                base.WndProc(ref message);
                if (message.Msg == NativeMethods.WM_PAINT)
                {
                    PaintFiller();
                }
            }

            private void PaintFiller()
            {
                NativeMethods.NativeRectangle clientBounds;
                if (!NativeMethods.GetClientRect(Handle, out clientBounds))
                {
                    return;
                }
                int itemTotal = NativeMethods.SendMessage(Handle, NativeMethods.HDM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt32();
                int fillerLeft = 0;
                if (itemTotal > 0)
                {
                    NativeMethods.NativeRectangle lastItemBounds = new NativeMethods.NativeRectangle();
                    NativeMethods.SendMessage(Handle, NativeMethods.HDM_GETITEMRECT, new IntPtr(itemTotal - 1), ref lastItemBounds);
                    fillerLeft = lastItemBounds.Right;
                }
                if (fillerLeft >= clientBounds.Right)
                {
                    return;
                }
                using (Graphics graphics = Graphics.FromHwnd(Handle))
                {
                    Rectangle fillerBounds = Rectangle.FromLTRB(fillerLeft, clientBounds.Top, clientBounds.Right, clientBounds.Bottom);
                    XpTheme.DrawListHeader(graphics, fillerBounds, false, false);
                }
            }
        }
    }
}
