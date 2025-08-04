namespace GBFRSave.Core.Models;

public sealed class PatchOptions
{
    public bool   ClearAllFactors { get; init; } = true;
    public bool   ClearAllWrightstones { get; init; } = true;
    public bool   ConvertVouchersToTransmarvelPoints { get; init; } = true;
    public double? TransmarvelPointRate { get; init; } = null;
}
