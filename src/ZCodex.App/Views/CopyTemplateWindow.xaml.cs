using System.Windows;

namespace ZCodex.App.Views;

public partial class CopyTemplateWindow : Window
{
    private readonly string _code;
    private readonly string _chatCode;

    public CopyTemplateWindow(string code, string chatCode)
    {
        InitializeComponent();
        _code     = code;
        _chatCode = chatCode;
        Loaded += (_, _) =>
        {
            CodeBox.Text = _code;
            ChatBox.Text = _chatCode;
        };
    }

    private void CopyCode_Click(object sender, RoutedEventArgs e) => SafeClipboard.SetText(_code);

    private void CopyChat_Click(object sender, RoutedEventArgs e) => SafeClipboard.SetText(_chatCode);
}
