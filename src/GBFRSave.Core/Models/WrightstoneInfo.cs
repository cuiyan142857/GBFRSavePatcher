namespace GBFRSave.Core.Models;

public record WrightstoneInfo(int UnitId, (int L1, int L2, int L3) Levels, int Tickets);
