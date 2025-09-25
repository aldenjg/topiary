using Avalonia.Controls;
using Avalonia.Interactivity;
using Topiary.App.ViewModels;

namespace Topiary.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void CancelScan_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            // Cancel the current scan
            viewModel.CancelScan();
        }
    }
}