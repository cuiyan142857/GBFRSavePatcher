using GBFRSave.Core.Abstractions;
using GBFRSave.Core.Models;
using GBFRSave.Core.Patching;

namespace GBFRSave.Core.Services;

public sealed class TicketCalculator : ITicketCalculator
{
    private readonly SavePatchEngine _engine = new();
    public TicketSummary Compute(object saveObject)
    {
        var (sigilCount, removedSigilCount,
            wrightstoneCount, removedWrightstoneCount,
            oldTickets, sigilTickets, wrightstoneTickets, newTickets, oldTrans, newTrans) = _engine.ComputeTickets(saveObject);
        return new TicketSummary(sigilCount, removedSigilCount,
            wrightstoneCount, removedWrightstoneCount,
            oldTickets, sigilTickets, wrightstoneTickets, newTickets, oldTrans, newTrans);
    }
}
