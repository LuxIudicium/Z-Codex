using System.Windows;
using System.Windows.Input;

namespace ZCodex.App.Views;

public partial class ImportTemplateWindow : Window
{
    public string TemplateCode { get; private set; } = string.Empty;

    public ImportTemplateWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => CodeBox.Focus();
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var code = CodeBox.Text.Trim();
        if (string.IsNullOrEmpty(code)) return;
        TemplateCode = code;
        DialogResult = true;
    }

    private void CodeBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Import_Click(sender, new RoutedEventArgs());
    }
}
