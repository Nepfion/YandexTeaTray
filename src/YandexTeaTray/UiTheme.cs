using System.Runtime.InteropServices;

namespace YandexTeaTray;

internal static class UiTheme
{
    public static readonly Color Background = Color.FromArgb(12, 12, 14);
    public static readonly Color Ink = Color.FromArgb(244, 244, 245);
    public static readonly Color Muted = Color.FromArgb(161, 161, 170);
    public static readonly Color Accent = Color.FromArgb(240, 193, 126);
    public static readonly Color Warm = Color.FromArgb(255, 166, 93);
    public static readonly Color Surface = Color.FromArgb(25, 25, 29);
    public static readonly Color Border = Color.FromArgb(50, 50, 56);
    public static readonly Color Danger = Color.FromArgb(248, 88, 88);

    public static void InitializeNativeDarkMode()
    {
        // .NET 10 supplies dark rendering for native controls, including UpDown buttons.
#pragma warning disable WFO5001
        Application.SetColorMode(SystemColorMode.Dark);
#pragma warning restore WFO5001
    }

    public static void Apply(Form form, string title, Size size)
    {
        form.Text = title;
        form.Font = new Font("Segoe UI", 10);
        form.BackColor = Background;
        form.ForeColor = Ink;
        form.ClientSize = size;
        form.MinimumSize = new Size(size.Width, size.Height);
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.StartPosition = FormStartPosition.CenterScreen;
        form.Icon = SystemIcons.Information;
        form.HandleCreated += (_, _) =>
        {
            var enabled = 1;
            _ = DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int));
            var caption = Background.R | (Background.G << 8) | (Background.B << 16);
            _ = DwmSetWindowAttribute(form.Handle, 35, ref caption, sizeof(int));
            var foreground = Ink.R | (Ink.G << 8) | (Ink.B << 16);
            _ = DwmSetWindowAttribute(form.Handle, 36, ref foreground, sizeof(int));
        };
    }

    public static void StyleControls(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            if (control is TextBox or ComboBox or NumericUpDown)
            {
                control.BackColor = Surface;
                control.ForeColor = Ink;
            }
            if (control is ComboBox combo) combo.FlatStyle = FlatStyle.Flat;
            if (control is TextBox text) text.BorderStyle = BorderStyle.FixedSingle;
            StyleControls(control);
        }
    }

    public static Label Label(string text = "", float size = 10, Color? color = null, bool bold = false) => new()
    {
        Text = text, AutoSize = true, Dock = DockStyle.Fill,
        Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
        ForeColor = color ?? Ink, Margin = new Padding(0, 5, 0, 5)
    };

    public static Button Button(string text, bool primary = false)
    {
        var button = new Button
        {
            Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(96, 36),
            Padding = new Padding(12, 5, 12, 5), FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Accent : Surface, ForeColor = primary ? Background : Ink,
            Cursor = Cursors.Hand, Margin = new Padding(0, 0, 8, 0)
        };
        button.FlatAppearance.BorderColor = primary ? Accent : Border;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(255, 211, 153) : Color.FromArgb(40, 40, 45);
        return button;
    }

    public static ContextMenuStrip Menu()
    {
        var menu = new ContextMenuStrip { BackColor = Surface, ForeColor = Ink, ShowImageMargin = false };
        menu.Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors());
        return menu;
    }

    private sealed class DarkMenuColors : ProfessionalColorTable
    {
        public override Color MenuItemSelected => Border;
        public override Color MenuItemBorder => Border;
        public override Color ToolStripDropDownBackground => Surface;
        public override Color MenuBorder => Border;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Border;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
