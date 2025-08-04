namespace GBFRSave.Core.Models;
public record TicketSummary(int sigilCount, int removedSigilCount,
                            int wrightstoneCount, int removedWrightstoneCount,
                            long oldTickets, int sigilTickets, int wrightstoneTickets, long newTickets, long oldTrans, long newTrans);
