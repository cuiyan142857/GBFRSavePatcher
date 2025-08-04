// IPatchService.cs
using GBFRSave.Core.Models;
namespace GBFRSave.Core.Abstractions;

public interface IPatchService
{
    PatchSummary Apply(string SavePath);
    PatchSummary ConvertTicketsToTransmarvelPoints(string SavePath);
    string BackupFile(string SavePath);
}
