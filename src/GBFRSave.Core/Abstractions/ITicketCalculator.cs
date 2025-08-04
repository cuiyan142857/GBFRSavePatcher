using GBFRSave.Core.Models;

namespace GBFRSave.Core.Abstractions;

public interface ITicketCalculator
{
    TicketSummary Compute(object saveObject);
}
