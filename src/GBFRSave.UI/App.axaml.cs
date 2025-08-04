// App.axaml.cs
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using GBFRSave.UI.ViewModels;
using GBFRSave.Core.Services; // SaveReader, TicketCalculator
using GBFRSave.UI.Views;

namespace GBFRSave.UI;

public partial class App : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainWindowViewModel(new SaveReader(), new TicketCalculator(), new PatchService());
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
