using System.Windows;

namespace ReliosExport;

/// <summary>Relios temalı evet/hayır penceresi (sistem MessageBox'ı yerine).</summary>
public partial class ConfirmDialog : Window
{
    private ConfirmDialog()
    {
        InitializeComponent();
    }

    /// <summary>Soruyu sorar; "evet" düğmesine basılırsa true döner. Varsayılan seçim "hayır"dır.</summary>
    public static bool Ask(Window owner, string title, string message, string yes, string no)
    {
        var dlg = new ConfirmDialog { Owner = owner };
        dlg.TitleText.Text = title;
        dlg.MessageText.Text = message;
        dlg.BtnYes.Content = yes;
        dlg.BtnNo.Content = no;
        return dlg.ShowDialog() == true;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowTheme.Apply(this);
    }

    private void OnYes(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnNo(object sender, RoutedEventArgs e) => DialogResult = false;
}
