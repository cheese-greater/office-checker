using System;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("Office Lock Checker")]
[assembly: AssemblyDescription("Shows who has Office documents locked by decoding their lock files.")]
[assembly: AssemblyProduct("Office Lock Checker")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace OfficeLockChecker
{
    internal static class Program
    {
        // Paths dropped onto the exe icon in Explorer arrive as arguments and are checked on startup.
        [STAThread]
        private static void Main(string[] arguments)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            ToolStripManager.Renderer = new XpToolStripRenderer();
            Application.Run(new MainForm(arguments));
        }
    }
}
