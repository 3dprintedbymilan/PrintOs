namespace PrintOs.Api.Modules.FinishedStock;

public enum StockRejection
{
    None = 0,
    InsufficientOnHand = 1,
    InsufficientAvailable = 2,
    ReservedUnderflow = 3,
}

public readonly record struct StockState(int OnHand, int Reserved, decimal? AverageUnitCost)
{
    public int Available => OnHand - Reserved;
}

public readonly record struct StockOutcome(StockState State, StockRejection Rejection)
{
    public bool Accepted => Rejection == StockRejection.None;
}

public static class FinishedStockMath
{
    public static StockOutcome Apply(StockState state, int deltaOnHand, int deltaReserved, bool requireAvailable, decimal? receiptUnitCost)
    {
        var onHand = state.OnHand + deltaOnHand;
        var reserved = state.Reserved + deltaReserved;

        if (onHand < 0)
        {
            return new StockOutcome(state, StockRejection.InsufficientOnHand);
        }

        if (reserved < 0)
        {
            return new StockOutcome(state, StockRejection.ReservedUnderflow);
        }

        var available = onHand - reserved;
        if (requireAvailable && available < 0 && available < state.Available)
        {
            return new StockOutcome(state, StockRejection.InsufficientAvailable);
        }

        return new StockOutcome(new StockState(onHand, reserved, NextAverageCost(state, deltaOnHand, receiptUnitCost)), StockRejection.None);
    }

    public static decimal? NextAverageCost(StockState state, int deltaOnHand, decimal? receiptUnitCost)
    {
        if (deltaOnHand <= 0 || receiptUnitCost is not { } cost)
        {
            return state.AverageUnitCost;
        }

        if (state.OnHand <= 0 || state.AverageUnitCost is not { } average)
        {
            return Math.Round(cost, 4, MidpointRounding.AwayFromZero);
        }

        var total = (state.OnHand * average) + (deltaOnHand * cost);
        return Math.Round(total / (state.OnHand + deltaOnHand), 4, MidpointRounding.AwayFromZero);
    }

    public static bool IsLow(int available, int? threshold) => threshold is { } limit && available <= limit;
}
