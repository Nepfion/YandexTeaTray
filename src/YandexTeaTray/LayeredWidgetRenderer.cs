using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace YandexTeaTray;

internal static class LayeredWidgetRenderer
{
    internal static int MeasureWidth(Font font, string text, float scale)
    {
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        using var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap };
        return (int)Math.Ceiling(graphics.MeasureString(text, font, int.MaxValue, format).Width + 32 * scale);
    }

    internal static Bitmap Draw(Size size, Font font, string text, Color foreground, Color indicator, bool hover, float scale)
    {
        var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(bitmap);
        // One alpha level keeps the entire label clickable, without an opaque background panel.
        graphics.Clear(Color.FromArgb(1, 0, 0, 0));
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var dot = new SolidBrush(indicator);
        var diameter = 6 * scale;
        graphics.FillEllipse(dot, 12 * scale, (size.Height - diameter) / 2, diameter, diameter);
        using var brush = new SolidBrush(foreground);
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap
        };
        graphics.DrawString(text, font, brush,
            new RectangleF(25 * scale, 0, size.Width - 29 * scale, size.Height), format);
        if (hover)
        {
            using var pen = new Pen(Color.FromArgb(100, foreground));
            graphics.DrawRectangle(pen, 0, 0, size.Width - 1, size.Height - 1);
        }
        return bitmap;
    }

    public static bool Present(IntPtr window, Point location, Bitmap bitmap)
    {
        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        if (screen == IntPtr.Zero || memory == IntPtr.Zero)
        {
            if (memory != IntPtr.Zero) DeleteDC(memory);
            if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
            return false;
        }
        var handle = IntPtr.Zero;
        var previous = IntPtr.Zero;
        try
        {
            handle = bitmap.GetHbitmap(Color.FromArgb(0));
            previous = SelectObject(memory, handle);
            var destination = new NativePoint { X = location.X, Y = location.Y };
            var source = new NativePoint();
            var size = new NativeSize { Width = bitmap.Width, Height = bitmap.Height };
            var blend = new BlendFunction { SourceConstantAlpha = 255, AlphaFormat = 1 };
            return UpdateLayeredWindow(window, screen, ref destination, ref size, memory, ref source, 0, ref blend, 2);
        }
        finally
        {
            if (previous != IntPtr.Zero) SelectObject(memory, previous);
            if (handle != IntPtr.Zero) DeleteObject(handle);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize { public int Width, Height; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr item);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr item);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destinationDC, ref NativePoint destination,
        ref NativeSize size, IntPtr sourceDC, ref NativePoint source, uint colorKey, ref BlendFunction blend, uint flags);
}
