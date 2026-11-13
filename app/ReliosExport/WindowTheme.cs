using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ReliosExport;

/// <summary>
/// Windows başlık çubuğunu koyu yapar (Relios Audio ile aynı yöntem: DwmSetWindowAttribute).
/// Windows 11'de başlık çubuğu rengi üst çubukla aynı laciverte boyanır; Windows 10'da yalnızca koyu mod uygulanır.
/// </summary>
internal static class WindowTheme
{
    private const int DwmwaUseImmersiveDarkModeOld = 19; // Windows 10 1809–1909
    private const int DwmwaUseImmersiveDarkMode = 20;    // Windows 10 2004 ve sonrası
    private const int DwmwaBorderColor = 34;             // Windows 11
    private const int DwmwaCaptionColor = 35;            // Windows 11
    private const int DwmwaTextColor = 36;               // Windows 11

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var on = 1;
            if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref on, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeOld, ref on, sizeof(int));
            }

            // Desteklenmeyen sürümlerde bu çağrılar hata kodu döner ve etkisizdir.
            var caption = ColorRef(0x15, 0x1F, 0x2B); // BgSurface
            var text = ColorRef(0xE6, 0xED, 0xF5);    // TextPrimary
            var border = ColorRef(0x26, 0x34, 0x45);  // BorderSubtle
            DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref caption, sizeof(int));
            DwmSetWindowAttribute(hwnd, DwmwaTextColor, ref text, sizeof(int));
            DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref border, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Çok eski Windows: varsayılan başlık çubuğu kalır.
        }
    }

    private static int ColorRef(byte r, byte g, byte b) => r | (g << 8) | (b << 16);
}
