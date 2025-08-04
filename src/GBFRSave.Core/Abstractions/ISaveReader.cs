namespace GBFRSave.Core.Abstractions;

public interface ISaveReader
{
    // Parse and return an opaque in-memory save object your services can use.
    object Load(string path);
}