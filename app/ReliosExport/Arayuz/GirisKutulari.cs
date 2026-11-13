using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace ReliosExport.Arayuz;

/// <summary>
/// Ayar kutularının ortak davranışı: Enter değeri hemen uygular, Esc yazılanı geri alır,
/// kutudan çıkınca ekranda ayarın geçerli (sınırlanmış, düzeltilmiş) değeri görünür.
/// </summary>
public static class GirisKutulari
{
    private static bool _etkin;

    public static void Etkinlestir()
    {
        if (_etkin)
        {
            return;
        }

        _etkin = true;
        EventManager.RegisterClassHandler(typeof(TextBox), UIElement.PreviewKeyDownEvent, new KeyEventHandler(OnKeyDown));
        EventManager.RegisterClassHandler(
            typeof(TextBox), UIElement.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnLostKeyboardFocus));
    }

    /// <summary>Odaktaki kutuya yazılmış ama henüz uygulanmamış değeri uygular (pencere kapanırken, başlatırken).</summary>
    public static void Uygula(DependencyObject kapsam)
    {
        if (FocusManager.GetFocusedElement(kapsam) is TextBox { IsReadOnly: false } tb)
        {
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { IsReadOnly: false } tb || tb.GetBindingExpression(TextBox.TextProperty) is not { } be)
        {
            return;
        }

        if (e.Key == Key.Enter && !tb.AcceptsReturn)
        {
            be.UpdateSource();
            be.UpdateTarget();
            tb.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && be.IsDirty)
        {
            be.UpdateTarget();
            tb.SelectAll();
            e.Handled = true;
        }
    }

    private static void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox { IsReadOnly: false } tb)
        {
            return;
        }

        // Bağlama, değeri odak pencere içinde başka bir denetime geçince yazar; ekran ondan sonra ayarın
        // gerçek (sınırlanmış ya da hatalıysa eski) değerine eşitlenir. Başka pencereye geçildiyse
        // (odak kutuda kalır) yazılan değer korunur.
        tb.Dispatcher.InvokeAsync(() =>
        {
            var kapsam = FocusManager.GetFocusScope(tb);
            if (!tb.IsKeyboardFocusWithin
                && (kapsam is null || !ReferenceEquals(FocusManager.GetFocusedElement(kapsam), tb))
                && tb.GetBindingExpression(TextBox.TextProperty) is { } be)
            {
                be.UpdateTarget();
            }
        }, DispatcherPriority.Input);
    }
}
