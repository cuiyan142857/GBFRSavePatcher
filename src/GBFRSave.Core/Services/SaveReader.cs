using GBFRSave.Core.Abstractions;
using GBFRSave.Core.Patching;

namespace GBFRSave.Core.Services;

public sealed class SaveReader : ISaveReader
{
    private readonly SavePatchEngine _engine = new();
    public object Load(string path) => _engine.Load(path);
}
