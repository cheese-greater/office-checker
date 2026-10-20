using System.Drawing;
using System.Windows.Forms;

namespace OfficeLockChecker
{
    // Flat XP menus (white with a gray border and blue selection) and a beige status bar with the dotted size grip.
    internal sealed class XpToolStripRenderer : ToolStripProfessionalRenderer
    {
        public XpToolStripRenderer()
            : base(new XpColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs eventArgs)
        {
            if (eventArgs.Item.Selected && eventArgs.Item.Enabled)
            {
                using (SolidBrush selectionBrush = new SolidBrush(XpTheme.Selection))
                {
                    eventArgs.Graphics.FillRectangle(selectionBrush, new Rectangle(Point.Empty, eventArgs.Item.Size));
                }
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs eventArgs)
        {
            if (eventArgs.Item is ToolStripMenuItem)
            {
                eventArgs.TextColor = !eventArgs.Item.Enabled ? XpTheme.DisabledText : eventArgs.Item.Selected ? Color.White : Color.Black;
            }
            base.OnRenderItemText(eventArgs);
        }

        private sealed class XpColorTable : ProfessionalColorTable
        {
            public override Color MenuBorder { get { return XpTheme.MenuBorder; } }
            public override Color MenuItemBorder { get { return XpTheme.Selection; } }
            public override Color MenuItemSelected { get { return XpTheme.Selection; } }
            public override Color ToolStripDropDownBackground { get { return Color.White; } }
            public override Color ImageMarginGradientBegin { get { return Color.White; } }
            public override Color ImageMarginGradientMiddle { get { return Color.White; } }
            public override Color ImageMarginGradientEnd { get { return Color.White; } }
            public override Color SeparatorDark { get { return XpTheme.MenuBorder; } }
            public override Color SeparatorLight { get { return Color.White; } }
            public override Color StatusStripGradientBegin { get { return XpTheme.WindowBackground; } }
            public override Color StatusStripGradientEnd { get { return XpTheme.WindowBackground; } }
            public override Color GripDark { get { return XpTheme.MenuBorder; } }
            public override Color GripLight { get { return Color.White; } }
        }
    }
}
