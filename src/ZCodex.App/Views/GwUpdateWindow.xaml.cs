using System.Windows;

namespace ZCodex.App.Views;

public partial class GwUpdateWindow : Window
{
    public bool ShouldUpdate { get; private set; }
    public bool IgnoreChecked => IgnoreBox.IsChecked == true;

    public GwUpdateWindow(string message)
    {
        InitializeComponent();
        MessageText.Text = message;
    }

    private void Yes_Click(object sender, RoutedEventArgs e)
    {
        ShouldUpdate = true;
        DialogResult = true;
    }

    private void No_Click(object sender, RoutedEventArgs e)
    {
        ShouldUpdate = false;
        DialogResult = false;
    }
}
