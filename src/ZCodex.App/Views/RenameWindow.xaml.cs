using System.Windows;
using System.Windows.Input;

namespace ZCodex.App.Views;

public partial class RenameWindow : Window
{
    public string NewName { get; private set; }

    public RenameWindow(string currentName)
    {
        InitializeComponent();
        NewName = currentName;
        Loaded += (_, _) =>
        {
            NameBox.Text = currentName;
            NameBox.SelectAll();
            NameBox.Focus();
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;
        NewName = name;
        DialogResult = true;
    }

    private void NameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Ok_Click(sender, new RoutedEventArgs());
    }
}
