using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Modules.Identity;

namespace PrintOs.Api.Modules.Markets;

public sealed class MarketDayClosing : IWorkspaceScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public Guid MarketId { get; set; }

    public DateOnly BusinessDate { get; set; }

    public string Currency { get; set; } = string.Empty;

    public int SaleCount { get; set; }

    public int VoidedCount { get; set; }

    public int ItemCount { get; set; }

    public decimal Subtotal { get; set; }

    public decimal Discounts { get; set; }

    public decimal Revenue { get; set; }

    public decimal Cost { get; set; }

    public decimal CashTotal { get; set; }

    public decimal CardTotal { get; set; }

    public decimal OtherTotal { get; set; }

    public decimal OpeningFloat { get; set; }

    public decimal ExpectedCash { get; set; }

    public decimal CountedCash { get; set; }

    public decimal Difference { get; set; }

    public decimal StandCost { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset ClosedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MarketDayClosingConfiguration : IEntityTypeConfiguration<MarketDayClosing>
{
    public void Configure(EntityTypeBuilder<MarketDayClosing> builder)
    {
        builder.ToTable("MarketDayClosings");
        builder.HasKey(closing => closing.Id);
        builder.Property(closing => closing.Currency).HasMaxLength(3).IsRequired();
        foreach (var money in new[] { nameof(MarketDayClosing.Subtotal), nameof(MarketDayClosing.Discounts), nameof(MarketDayClosing.Revenue), nameof(MarketDayClosing.Cost), nameof(MarketDayClosing.CashTotal), nameof(MarketDayClosing.CardTotal), nameof(MarketDayClosing.OtherTotal), nameof(MarketDayClosing.OpeningFloat), nameof(MarketDayClosing.ExpectedCash), nameof(MarketDayClosing.CountedCash), nameof(MarketDayClosing.Difference), nameof(MarketDayClosing.StandCost) })
        {
            builder.Property(money).HasPrecision(12, 2);
        }

        builder.Property(closing => closing.Notes).HasMaxLength(MarketRules.NotesMaxLength);
        builder.HasIndex(closing => new { closing.WorkspaceId, closing.MarketId, closing.BusinessDate }).IsUnique();
        builder.HasOne<Market>().WithMany().HasForeignKey(closing => closing.MarketId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Workspace>().WithMany().HasForeignKey(closing => closing.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed record MarketClosingRequest(DateOnly? Date, decimal? OpeningFloat, decimal? CountedCash, string? Notes);

public sealed record MarketClosingResponse(
    Guid Id,
    Guid MarketId,
    string MarketName,
    DateOnly Date,
    string Currency,
    int SaleCount,
    int VoidedCount,
    int ItemCount,
    decimal Subtotal,
    decimal Discounts,
    decimal Revenue,
    decimal Cost,
    decimal GrossProfit,
    decimal CashTotal,
    decimal CardTotal,
    decimal OtherTotal,
    decimal OpeningFloat,
    decimal ExpectedCash,
    decimal CountedCash,
    decimal Difference,
    decimal StandCost,
    string? Notes,
    DateTimeOffset ClosedAt,
    List<MarketItemTotalResponse> Items);

public static class MarketClosingRules
{
    public static decimal ExpectedCash(decimal openingFloat, decimal cashSales) => MarketRules.Round(openingFloat + cashSales);

    public static decimal Difference(decimal counted, decimal expected) => MarketRules.Round(counted - expected);

    public static Task<bool> IsClosedAsync(PrintOsDbContext dbContext, Guid marketId, DateOnly date) =>
        dbContext.Set<MarketDayClosing>().AnyAsync(closing => closing.MarketId == marketId && closing.BusinessDate == date);
}

public static class MarketClosingEndpoints
{
    public static void MapMarketClosingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/markets/{marketId:guid}/closings")
            .WithTags("Market closings")
            .RequireAuthorization(AuthPolicies.RequireWorkspace);
        group.MapGet("", ListAsync);
        group.MapPost("", CloseAsync);
        group.MapGet("/{date}", GetAsync);
    }

    private static async Task<IResult> ListAsync(Guid marketId, PrintOsDbContext dbContext)
    {
        var market = await dbContext.Set<Market>().SingleOrDefaultAsync(candidate => candidate.Id == marketId);
        if (market is null)
        {
            return TypedResults.NotFound();
        }

        var closings = await dbContext.Set<MarketDayClosing>().Where(closing => closing.MarketId == marketId).OrderByDescending(closing => closing.BusinessDate).ToListAsync();
        var responses = new List<MarketClosingResponse>();
        foreach (var closing in closings)
        {
            responses.Add(ToResponse(closing, market, []));
        }

        return TypedResults.Ok(responses);
    }

    private static async Task<IResult> GetAsync(Guid marketId, string date, PrintOsDbContext dbContext)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var day))
        {
            return TypedResults.NotFound();
        }

        var market = await dbContext.Set<Market>().SingleOrDefaultAsync(candidate => candidate.Id == marketId);
        var closing = market is null ? null : await dbContext.Set<MarketDayClosing>().SingleOrDefaultAsync(candidate => candidate.MarketId == marketId && candidate.BusinessDate == day);
        if (market is null || closing is null)
        {
            return TypedResults.NotFound();
        }

        var summary = await MarketSummaryBuilder.BuildAsync(dbContext, market, day);
        return TypedResults.Ok(ToResponse(closing, market, summary.Items));
    }

    private static async Task<IResult> CloseAsync(Guid marketId, MarketClosingRequest request, PrintOsDbContext dbContext)
    {
        var market = await dbContext.Set<Market>().SingleOrDefaultAsync(candidate => candidate.Id == marketId);
        if (market is null)
        {
            return TypedResults.NotFound();
        }

        var errors = new Dictionary<string, string[]>();
        var notes = MarketEndpoints.Clean(request.Notes);
        if (request.Date is null)
        {
            errors["date"] = ["The date is required."];
        }
        else if (request.Date.Value > DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1))
        {
            errors["date"] = ["The date cannot be in the future."];
        }

        if (request.CountedCash is null or < 0m or > MarketRules.MaximumMoney)
        {
            errors["countedCash"] = ["Enter the counted cash, between 0 and 1000000."];
        }

        if (request.OpeningFloat is < 0m or > MarketRules.MaximumMoney)
        {
            errors["openingFloat"] = ["The opening float must be between 0 and 1000000."];
        }

        if (notes is { Length: > MarketRules.NotesMaxLength })
        {
            errors["notes"] = [$"The notes must be {MarketRules.NotesMaxLength} characters or fewer."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var day = request.Date!.Value;
        if (await MarketClosingRules.IsClosedAsync(dbContext, marketId, day))
        {
            return TypedResults.Problem("This day is already closed.", statusCode: StatusCodes.Status409Conflict);
        }

        var summary = await MarketSummaryBuilder.BuildAsync(dbContext, market, day);
        var cash = summary.ByPayment.Single(entry => entry.Method == MarketPaymentMethods.Cash).Amount;
        var openingFloat = MarketRules.Round(request.OpeningFloat ?? 0m);
        var expected = MarketClosingRules.ExpectedCash(openingFloat, cash);
        var counted = MarketRules.Round(request.CountedCash!.Value);
        var closing = new MarketDayClosing
        {
            MarketId = marketId,
            BusinessDate = day,
            Currency = market.Currency,
            SaleCount = summary.SaleCount,
            VoidedCount = summary.VoidedCount,
            ItemCount = summary.ItemCount,
            Subtotal = summary.Subtotal,
            Discounts = summary.Discounts,
            Revenue = summary.Revenue,
            Cost = MarketRules.Round(summary.Cost),
            CashTotal = cash,
            CardTotal = summary.ByPayment.Single(entry => entry.Method == MarketPaymentMethods.Card).Amount,
            OtherTotal = summary.ByPayment.Single(entry => entry.Method == MarketPaymentMethods.Other).Amount,
            OpeningFloat = openingFloat,
            ExpectedCash = expected,
            CountedCash = counted,
            Difference = MarketClosingRules.Difference(counted, expected),
            StandCost = market.StandCost,
            Notes = notes,
        };
        dbContext.Set<MarketDayClosing>().Add(closing);
        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return TypedResults.Problem("This day is already closed.", statusCode: StatusCodes.Status409Conflict);
        }

        return TypedResults.Created($"/api/markets/{marketId}/closings/{day:yyyy-MM-dd}", ToResponse(closing, market, summary.Items));
    }

    private static MarketClosingResponse ToResponse(MarketDayClosing closing, Market market, List<MarketItemTotalResponse> items) => new(
        closing.Id,
        closing.MarketId,
        market.Name,
        closing.BusinessDate,
        closing.Currency,
        closing.SaleCount,
        closing.VoidedCount,
        closing.ItemCount,
        closing.Subtotal,
        closing.Discounts,
        closing.Revenue,
        closing.Cost,
        closing.Revenue - closing.Cost,
        closing.CashTotal,
        closing.CardTotal,
        closing.OtherTotal,
        closing.OpeningFloat,
        closing.ExpectedCash,
        closing.CountedCash,
        closing.Difference,
        closing.StandCost,
        closing.Notes,
        closing.ClosedAt,
        items);
}
