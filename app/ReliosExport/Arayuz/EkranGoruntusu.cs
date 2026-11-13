using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ReliosExport.Arayuz;

/// <summary>
/// Yalnızca GitHub'daki pencere kontrolü için: pencerelerin görüntüsünü PNG olarak kaydeder
/// (arayüz derlemeden sonra gözle de kontrol edilebilsin).
/// </summary>
public static class EkranGoruntusu
{
    /// <summary>Bekleyen yerleşim ve çizim işlerinin bitmesini bekler.</summary>
    public static void Bekle(Dispatcher d)
    {
        d.Invoke(() => { }, DispatcherPriority.ContextIdle);
        d.Invoke(() => { }, DispatcherPriority.ContextIdle);
    }

    /// <summary>Pencerenin iç alanı (başlık çubuğu hariç), pencerenin arka planıyla.</summary>
    public static void Pencere(Window w, string dosya)
    {
        w.UpdateLayout();
        if (VisualTreeHelper.GetChildrenCount(w) == 0 || VisualTreeHelper.GetChild(w, 0) is not FrameworkElement kok)
        {
            return;
        }

        Oge(kok, w.Background, dosya);
    }

    /// <summary>Bir öğenin tamamı (kaydırma alanında görünmeyen kısmı dahil).</summary>
    public static void Oge(FrameworkElement oge, Brush? arka, string dosya)
    {
        var en = Math.Max(1, (int)Math.Ceiling(oge.ActualWidth));
        var boy = Math.Max(1, (int)Math.Ceiling(oge.ActualHeight));
        var alan = new Rect(0, 0, en, boy);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            if (arka is not null)
            {
                dc.DrawRectangle(arka, null, alan);
            }

            dc.DrawRectangle(
                new VisualBrush(oge)
                {
                    Stretch = Stretch.None,
                    AlignmentX = AlignmentX.Left,
                    AlignmentY = AlignmentY.Top,
                    ViewboxUnits = BrushMappingMode.Absolute,
                    Viewbox = new Rect(0, 0, oge.ActualWidth, oge.ActualHeight),
                },
                null,
                alan);
        }

        var rtb = new RenderTargetBitmap(en, boy, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        Directory.CreateDirectory(Path.GetDirectoryName(dosya)!);
        using var fs = File.Create(dosya);
        enc.Save(fs);
    }

    /// <summary>Penceredeki sekmelerin her birinin görüntüsü (sekme yoksa pencerenin kendisi).</summary>
    public static int Sekmeler(Window w, string klasor, string ad)
    {
        var tc = Bul<TabControl>(w);
        if (tc is null)
        {
            Pencere(w, Path.Combine(klasor, ad + ".png"));
            return 1;
        }

        for (var i = 0; i < tc.Items.Count; i++)
        {
            tc.SelectedIndex = i;
            Bekle(w.Dispatcher);
            var baslik = (tc.Items[i] as TabItem)?.Header?.ToString() ?? i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Pencere(w, Path.Combine(klasor, $"{ad}-{i + 1}-{DosyaAdi(baslik)}.png"));
        }

        tc.SelectedIndex = 0;
        return tc.Items.Count;
    }

    private static string DosyaAdi(string s)
    {
        var tr = s.ToLowerInvariant()
            .Replace('ı', 'i').Replace('ş', 's').Replace('ğ', 'g').Replace('ü', 'u').Replace('ö', 'o').Replace('ç', 'c');
        return new string(tr.Where(char.IsLetterOrDigit).ToArray());
    }

    public static T? Bul<T>(DependencyObject kok)
        where T : DependencyObject
    {
        foreach (var c in LogicalTreeHelper.GetChildren(kok).OfType<DependencyObject>())
        {
            if (c is T t)
            {
                return t;
            }

            if (Bul<T>(c) is { } alt)
            {
                return alt;
            }
        }

        return null;
    }
}
