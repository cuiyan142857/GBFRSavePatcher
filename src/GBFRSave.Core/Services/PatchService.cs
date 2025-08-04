using GBFRSave.Core.Abstractions;
using GBFRSave.Core.Models;
using GBFRSave.Core.Patching;

namespace GBFRSave.Core.Services;

public sealed class PatchService : IPatchService
{
    private readonly SavePatchEngine _engine = new();
    public PatchSummary Apply(string SavePath)
    {
        // The engine loads from inputPath because it needs header/footer bytes from disk.
        (string outputPath, bool hashStatus) = _engine.ApplyPatch(SavePath);
        return new PatchSummary(outputPath, hashStatus);
    }

    public PatchSummary ConvertTicketsToTransmarvelPoints(string SavePath)
    {
        // The engine loads from inputPath because it needs header/footer bytes from disk.
        (string outputPath, bool hashStatus) = _engine.ConvertTicketsToTransmarvelPoints(SavePath);
        return new PatchSummary(outputPath, hashStatus);
    }
    public string BackupFile(string SavePath)
    {
        string backupPath = _engine.BackupFile(SavePath);
        return backupPath;
    }
}