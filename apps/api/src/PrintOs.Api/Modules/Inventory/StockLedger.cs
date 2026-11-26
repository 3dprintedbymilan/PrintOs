using PrintOs.Api.Infrastructure.Persistence;

namespace PrintOs.Api.Modules.Inventory;

public static class StockLedger
{
    public static bool TryApply(
        PrintOsDbContext dbContext,
        FilamentSpool spool,
        MovementKind kind,
        int deltaGrams,
        string reason,
        Guid? printJobId = null)
    {
        var balance = spool.RemainingWeightGrams + deltaGrams;
        if (balance < 0)
        {
            return false;
        }

        spool.RemainingWeightGrams = balance;
        dbContext.Set<MaterialMovement>().Add(new MaterialMovement
        {
            SpoolId = spool.Id,
            Kind = kind,
            DeltaGrams = deltaGrams,
            BalanceAfterGrams = balance,
            Reason = reason,
            PrintJobId = printJobId,
        });
        return true;
    }
}
