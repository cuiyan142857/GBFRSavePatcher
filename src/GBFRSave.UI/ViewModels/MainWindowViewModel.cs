using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GBFRSave.Core.Abstractions;
using GBFRSave.Core.Models;

namespace GBFRSave.UI.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly ISaveReader _reader;
    private readonly ITicketCalculator _calc;
    private readonly IPatchService _patch;

    [ObservableProperty] private string? _savePath;
    [ObservableProperty] private long _oldTickets;
    [ObservableProperty] private int _sigilTickets;
    [ObservableProperty] private int _wrightstoneTickets;
    [ObservableProperty] private int _sigilCount;
    [ObservableProperty] private int _removedSigilCount;
    [ObservableProperty] private int _wrightstoneCount;
    [ObservableProperty] private int _removedWrightstoneCount;
    [ObservableProperty] private long _newTickets;
    [ObservableProperty] private long _oldTrans;
    [ObservableProperty] private long _newTrans;
    [ObservableProperty] private string? _status;
    [ObservableProperty] private string? _outputPath;
    [ObservableProperty] private string? _hashStatus;

    public MainWindowViewModel(ISaveReader reader, ITicketCalculator calc, IPatchService patch)
    {
        _reader = reader;
        _calc = calc;
        _patch = patch;
    }

    [RelayCommand]
    private void Browse()
    {
        // You can replace this with a real FilePicker service; hard-code for the first slice if needed.
        // For now, set SavePath from a dialog service later.
    }

    [RelayCommand]
    private void Scan()
    {
        if (string.IsNullOrWhiteSpace(SavePath)) { Status = "Pick a save file."; return; }

        var save = _reader.Load(SavePath);
        TicketSummary s = _calc.Compute(save);

        SigilTickets = s.sigilTickets;
        WrightstoneTickets = s.wrightstoneTickets;
        NewTickets = s.newTickets;
        OldTickets = s.oldTickets;
        NewTrans = s.newTrans;
        OldTrans = s.oldTrans;
        SigilCount = s.sigilCount;
        RemovedSigilCount = s.removedSigilCount;
        WrightstoneCount = s.wrightstoneCount;
        RemovedWrightstoneCount = s.removedWrightstoneCount;
        Status = "Scan complete.✅";
    }

    [RelayCommand]
    private void ApplyPatch()
    {
        if (string.IsNullOrWhiteSpace(SavePath)) { Status = "Pick a save file."; return; }

        PatchSummary s = _patch.Apply(SavePath);
        OutputPath = $"Output file path: {s.outputPath}";
        if (s.hashStatus)
        {
            HashStatus = "Hash verification successful.✅";
        }
        else
        {
            HashStatus = "Hash verification failed.❌";
        }
        Status = "ApplyPatch complete.✅";
    }

    [RelayCommand]
    private void ConvertTicketsToTransmarvelPoints()
    {
        if (string.IsNullOrWhiteSpace(SavePath)) { Status = "Pick a save file."; return; }

        PatchSummary s = _patch.ConvertTicketsToTransmarvelPoints(SavePath);
        OutputPath = $"Output file path: {s.outputPath}";
        if (s.hashStatus)
        {
            HashStatus = "Hash verification successful.✅";
        }
        else
        {
            HashStatus = "Hash verification failed.❌";
        }
        Status = "Convert complete.✅";
    }

    [RelayCommand]
    private void BackupFile()
    {
        if (string.IsNullOrWhiteSpace(SavePath)) { Status = "Pick a save file."; return; }
        string BackupPath = _patch.BackupFile(SavePath);
        OutputPath = $"Backup file path: {BackupPath}";
        Status = "Backup complete.✅";
    }
}
