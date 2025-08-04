// MainWindow.axaml.cs
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Linq;
using GBFRSave.UI.ViewModels;

namespace GBFRSave.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select SaveData1.dat",
            AllowMultiple = false,
            Filters =
            {
                new FileDialogFilter { Name = "GBFR Save", Extensions = { "dat" } },
                new FileDialogFilter { Name = "All files", Extensions = { "*" } }
            }
        };

        var result = await dlg.ShowAsync(this);
        if (result != null && result.Length > 0)
        {
            if (DataContext is MainWindowViewModel vm)
                vm.SavePath = result.First();
        }
    }
}
