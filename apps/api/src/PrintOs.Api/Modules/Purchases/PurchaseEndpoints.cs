using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrintOs.Api.Infrastructure.Persistence;
using PrintOs.Api.Infrastructure.Storage;
using PrintOs.Api.Modules.Identity;
using PrintOs.Api.Modules.Inventory;
using PrintOs.Api.Modules.ModelLibrary;

namespace PrintOs.Api.Modules.Purchases;

public static class PurchaseEndpoints
{
    private const long MaxReceiptBytes = 10L * 1024 * 1024;
    private const int MaxLines = 50;
    private static readonly TimeSpan ReceiptUrlLifetime = TimeSpan.FromMinutes(5);

    public static void MapPurchaseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/purchases")
            .WithTags("Purchases")
            .RequireAuthorization(AuthPolicies.RequireWorkspace)
            .DisableAntiforgery();

        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);
        group.MapPost("/{id:guid}/receive", ReceiveAsync);
        group.MapPost("/{id:guid}/cancel", CancelAsync);
        group.MapPost("/{id:guid}/receipt", UploadReceiptAsync);
        group.MapDelete("/{id:guid}/receipt", DeleteReceiptAsync);
        group.MapGet("/{id:guid}/receipt-url", GetReceiptUrlAsync);

        endpoints.MapGet("/api/purchase-files/{token}", DownloadReceiptAsync).WithTags("Purchases").AllowAnonymous();
    }

    internal static async Task<string> LoadCurrencyAsync(ICurrentWorkspace currentWorkspace, PrintOsDbContext dbContext, CancellationToken cancellationToken)
    {
        var workspaceId = currentWorkspace.WorkspaceId;
        return await dbContext.Set<Workspace>()
            .Where(workspace => workspace.Id == workspaceId)
            .Select(workspace => workspace.Currency)
            .FirstOrDefaultAsync(cancellationToken) ?? WorkspaceSettingsDefaults.Currency;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static decimal Total(Purchase purchase) =>
        PurchaseCosting.Total(purchase.Lines.Select(line => new PurchaseLineAmount(line.Quantity, line.UnitPrice)), purchase.ShippingCost, purchase.Discount);

    private static PurchaseSummaryResponse ToSummary(Purchase purchase, string supplierName, string currency) =>
        new(purchase.Id, purchase.SupplierId, supplierName, purchase.PurchasedOn, purchase.InvoiceReference, purchase.Status.ToString(),
            Total(purchase), currency, purchase.Lines.Count, purchase.Lines.Sum(line => line.Quantity), purchase.Lines.Sum(line => line.ReceivedQuantity));

    private static PurchaseDetailResponse ToDetail(Purchase purchase, string supplierName, IReadOnlyDictionary<Guid, string> materialNames, string currency) =>
        new(purchase.Id, purchase.SupplierId, supplierName, purchase.PurchasedOn, purchase.InvoiceReference, purchase.Notes, purchase.Status.ToString(), currency,
            PurchaseCosting.Subtotal(purchase.Lines.Select(line => new PurchaseLineAmount(line.Quantity, line.UnitPrice))),
            purchase.ShippingCost, purchase.Discount, Total(purchase), purchase.ReceiptFileKey is not null, purchase.ReceiptFileName,
            purchase.Lines.OrderBy(line => materialNames.GetValueOrDefault(line.MaterialId) ?? string.Empty).ThenBy(line => line.ColorName).ThenBy(line => line.Id)
                .Select(line => new PurchaseLineResponse(line.Id, line.MaterialId, materialNames.GetValueOrDefault(line.MaterialId) ?? string.Empty, line.ColorName, line.ColorHex,
                    line.Quantity, line.ReceivedQuantity, line.WeightGramsPerSpool, line.UnitPrice, line.LandedUnitCost,
                    PurchaseCosting.CostPerKilogram(line.LandedUnitCost, line.WeightGramsPerSpool), line.Quantity * line.UnitPrice)).ToList(),
            purchase.CreatedAt);

    private static async Task<PurchaseDetailResponse> BuildDetailAsync(Purchase purchase, PrintOsDbContext dbContext, ICurrentWorkspace currentWorkspace, CancellationToken cancellationToken)
    {
        var supplierName = await dbContext.Set<Supplier>().Where(supplier => supplier.Id == purchase.SupplierId).Select(supplier => supplier.Name).FirstAsync(cancellationToken);
        var materialIds = purchase.Lines.Select(line => line.MaterialId).Distinct().ToList();
        var materialNames = await dbContext.Set<Material>().Where(material => materialIds.Contains(material.Id)).ToDictionaryAsync(material => material.Id, material => material.Name, cancellationToken);
        return ToDetail(purchase, supplierName, materialNames, await LoadCurrencyAsync(currentWorkspace, dbContext, cancellationToken));
    }

    private static async Task<IResult> ListAsync(PrintOsDbContext dbContext, ICurrentWorkspace currentWorkspace, string? status, Guid? supplierId, CancellationToken cancellationToken)
    {
        var query = dbContext.Set<Purchase>().Include(purchase => purchase.Lines).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<PurchaseStatus>(status, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed) || int.TryParse(status, out _))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Choose Ordered, PartiallyReceived, Received or Cancelled."] });
            }

            query = query.Where(purchase => purchase.Status == parsed);
        }

        if (supplierId is { } supplier)
        {
            query = query.Where(purchase => purchase.SupplierId == supplier);
        }

        var purchases = await query.OrderByDescending(purchase => purchase.PurchasedOn).ThenByDescending(purchase => purchase.CreatedAt).ToListAsync(cancellationToken);
        var supplierNames = await dbContext.Set<Supplier>().ToDictionaryAsync(candidate => candidate.Id, candidate => candidate.Name, cancellationToken);
        var currency = await LoadCurrencyAsync(currentWorkspace, dbContext, cancellationToken);
        return TypedResults.Ok(purchases.Select(purchase => ToSummary(purchase, supplierNames.GetValueOrDefault(purchase.SupplierId) ?? string.Empty, currency)).ToList());
    }

    private static async Task<IResult> GetAsync(Guid id, PrintOsDbContext dbContext, ICurrentWorkspace currentWorkspace, CancellationToken cancellationToken)
    {
        var purchase = await dbContext.Set<Purchase>().Include(candidate => candidate.Lines).FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        return purchase is null ? TypedResults.NotFound() : TypedResults.Ok(await BuildDetailAsync(purchase, dbContext, currentWorkspace, cancellationToken));
    }

    private static async Task<Dictionary<string, string[]>> ValidateAsync(PurchaseRequest request, PrintOsDbContext dbContext, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (!await dbContext.Set<Supplier>().AnyAsync(supplier => supplier.Id == request.SupplierId, cancellationToken))
        {
            errors["supplierId"] = ["Choose an existing supplier."];
        }

        if (request.PurchasedOn is { } date && (date < new DateOnly(2000, 1, 1) || date > DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1)))
        {
            errors["purchasedOn"] = ["The date cannot be in the future or before 2000."];
        }

        if (request.InvoiceReference is { Length: > 100 })
        {
            errors["invoiceReference"] = ["The invoice reference must be 100 characters or fewer."];
        }

        if (request.Notes is { Length: > 1000 })
        {
            errors["notes"] = ["The notes must be 1000 characters or fewer."];
        }

        if (request.ShippingCost is < 0 or > 100_000)
        {
            errors["shippingCost"] = ["The shipping cost must be between 0 and 100000."];
        }

        if (request.Discount is < 0 or > 100_000)
        {
            errors["discount"] = ["The discount must be between 0 and 100000."];
        }

        var lines = request.Lines ?? [];
        if (lines.Count is 0 or > MaxLines)
        {
            errors["lines"] = [$"Add between 1 and {MaxLines} lines."];
            return errors;
        }

        var materialIds = await dbContext.Set<Material>().Select(material => material.Id).ToListAsync(cancellationToken);
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var key = $"lines[{index}]";
            if (!materialIds.Contains(line.MaterialId))
            {
                errors[$"{key}.materialId"] = ["Choose an existing material."];
            }

            var colorName = line.ColorName?.Trim() ?? string.Empty;
            if (colorName.Length is 0 or > 60)
            {
                errors[$"{key}.colorName"] = ["The colour is required and must be 60 characters or fewer."];
            }

            if (line.ColorHex is not null && !InventoryEndpoints.HexColor().IsMatch(line.ColorHex))
            {
                errors[$"{key}.colorHex"] = ["The colour must look like #1A2B3C."];
            }

            if (line.Quantity is < 1 or > 1000)
            {
                errors[$"{key}.quantity"] = ["The quantity must be between 1 and 1000."];
            }

            if (line.WeightGramsPerSpool is < 1 or > 100_000)
            {
                errors[$"{key}.weightGramsPerSpool"] = ["The weight must be between 1 and 100000 grams."];
            }

            if (line.UnitPrice is < 0 or > 100_000)
            {
                errors[$"{key}.unitPrice"] = ["The price must be between 0 and 100000."];
            }
        }

        if (errors.Count == 0 && PurchaseCosting.Total(lines.Select(line => new PurchaseLineAmount(line.Quantity, line.UnitPrice)), request.ShippingCost, request.Discount) < 0)
        {
            errors["discount"] = ["The discount cannot be higher than the purchase total."];
        }

        return errors;
    }

    private static async Task<IResult> CreateAsync(PurchaseRequest request, PrintOsDbContext dbContext, ICurrentWorkspace currentWorkspace, CancellationToken cancellationToken)
    {
        var errors = await ValidateAsync(request, dbContext, cancellationToken);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var lines = request.Lines!;
        var landed = PurchaseCosting.LandedUnitCosts(lines.Select(line => new PurchaseLineAmount(line.Quantity, line.UnitPrice)).ToList(), request.ShippingCost, request.Discount);
        var purchase = new Purchase
        {
            SupplierId = request.SupplierId,
            PurchasedOn = request.PurchasedOn ?? DateOnly.FromDateTime(DateTime.UtcNow),
            InvoiceReference = Clean(request.InvoiceReference),
            Notes = Clean(request.Notes),
            ShippingCost = request.ShippingCost,
            Discount = request.Discount,
            Lines = lines.Select((line, index) => new PurchaseLine
            {
                MaterialId = line.MaterialId,
                ColorName = line.ColorName.Trim(),
                ColorHex = line.ColorHex,
                Quantity = line.Quantity,
                WeightGramsPerSpool = line.WeightGramsPerSpool,
                UnitPrice = line.UnitPrice,
                LandedUnitCost = landed[index],
            }).ToList(),
        };
        dbContext.Set<Purchase>().Add(purchase);
        if (request.ReceiveImmediately)
        {
            ReceiveLines(dbContext, purchase, purchase.Lines.Select(line => (line, line.Quantity)).ToList());
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/purchases/{purchase.Id}", await BuildDetailAsync(purchase, dbContext, currentWorkspace, cancellationToken));
    }

    private static string Reason(Purchase purchase) =>
        purchase.InvoiceReference is { } invoice ? $"Purchase {invoice}" : $"Purchase of {purchase.PurchasedOn:yyyy-MM-dd}";

    private static void ReceiveLines(PrintOsDbContext dbContext, Purchase purchase, IReadOnlyList<(PurchaseLine Line, int Quantity)> received)
    {
        foreach (var (line, quantity) in received)
        {
            for (var copy = 0; copy < quantity; copy++)
            {
                StockLedger.AddSpool(dbContext, new FilamentSpool
                {
                    MaterialId = line.MaterialId,
                    SupplierId = purchase.SupplierId,
                    PurchaseLineId = line.Id,
                    ColorName = line.ColorName,
                    ColorHex = line.ColorHex,
                    Cost = Math.Round(line.LandedUnitCost, 2),
                    InitialWeightGrams = line.WeightGramsPerSpool,
                }, Reason(purchase));
            }

            line.ReceivedQuantity += quantity;
        }

        purchase.Status = purchase.Lines.All(line => line.ReceivedQuantity >= line.Quantity) ? PurchaseStatus.Received : PurchaseStatus.PartiallyReceived;
        purchase.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static async Task<IResult> ReceiveAsync(Guid id, ReceivePurchaseRequest? request, PrintOsDbContext dbContext, ICurrentWorkspace currentWorkspace, CancellationToken cancellationToken)
    {
        var purchase = await dbContext.Set<Purchase>().Include(candidate => candidate.Lines).FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (purchase is null)
        {
            return TypedResults.NotFound();
        }

        if (purchase.Status is PurchaseStatus.Received or PurchaseStatus.Cancelled)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["status"] = [$"A {purchase.Status.ToString().ToLowerInvariant()} purchase cannot receive more spools."] });
        }

        var requested = request?.Lines is { Count: > 0 } lines
            ? lines
            : purchase.Lines.Where(line => line.Quantity > line.ReceivedQuantity).Select(line => new ReceiveLineRequest(line.Id, line.Quantity - line.ReceivedQuantity)).ToList();

        var errors = new Dictionary<string, string[]>();
        var resolved = new List<(PurchaseLine Line, int Quantity)>();
        for (var index = 0; index < requested.Count; index++)
        {
            var item = requested[index];
            var line = purchase.Lines.FirstOrDefault(candidate => candidate.Id == item.LineId);
            if (line is null)
            {
                errors[$"lines[{index}].lineId"] = ["This line does not belong to the purchase."];
            }
            else if (resolved.Any(entry => entry.Line.Id == line.Id))
            {
                errors[$"lines[{index}].lineId"] = ["This line is listed more than once."];
            }
            else if (item.Quantity < 1 || item.Quantity > line.Quantity - line.ReceivedQuantity)
            {
                errors[$"lines[{index}].quantity"] = [$"Receive between 1 and {line.Quantity - line.ReceivedQuantity} spools."];
            }
            else
            {
                resolved.Add((line, item.Quantity));
            }
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        ReceiveLines(dbContext, purchase, resolved);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TypedResults.Conflict();
        }

        return TypedResults.Ok(await BuildDetailAsync(purchase, dbContext, currentWorkspace, cancellationToken));
    }

    private static async Task<IResult> CancelAsync(Guid id, PrintOsDbContext dbContext, ICurrentWorkspace currentWorkspace, CancellationToken cancellationToken)
    {
        var purchase = await dbContext.Set<Purchase>().Include(candidate => candidate.Lines).FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (purchase is null)
        {
            return TypedResults.NotFound();
        }

        if (purchase.Status != PurchaseStatus.Ordered)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Only a purchase without received spools can be cancelled. Adjust or archive the received spools instead."] });
        }

        purchase.Status = PurchaseStatus.Cancelled;
        purchase.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await BuildDetailAsync(purchase, dbContext, currentWorkspace, cancellationToken));
    }

    private static async Task<IResult> DeleteAsync(Guid id, PrintOsDbContext dbContext, IFileStorage storage, CancellationToken cancellationToken)
    {
        var purchase = await dbContext.Set<Purchase>().FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (purchase is null)
        {
            return TypedResults.NotFound();
        }

        if (purchase.Status is PurchaseStatus.PartiallyReceived or PurchaseStatus.Received)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["A purchase with received spools cannot be deleted."] });
        }

        var key = purchase.ReceiptFileKey;
        dbContext.Set<Purchase>().Remove(purchase);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (key is not null)
        {
            await DeleteQuietlyAsync(storage, key);
        }

        return TypedResults.NoContent();
    }

    private static async Task DeleteQuietlyAsync(IFileStorage storage, string key)
    {
        try
        {
            await storage.DeleteAsync(key, CancellationToken.None);
        }
        catch (Exception)
        {
        }
    }

    private static (string? ContentType, string? Extension) InspectReceipt(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 5 && header[..5].SequenceEqual("%PDF-"u8))
        {
            return ("application/pdf", ".pdf");
        }

        var image = ModelFileRules.ImageContentType(header);
        return image is null ? (null, null) : (image, ModelFileRules.ImageExtension(image));
    }

    private static async Task<IResult> UploadReceiptAsync(
        Guid id,
        [FromForm] PurchaseReceiptForm form,
        PrintOsDbContext dbContext,
        ICurrentWorkspace currentWorkspace,
        IFileStorage storage,
        CancellationToken cancellationToken)
    {
        var purchase = await dbContext.Set<Purchase>().Include(candidate => candidate.Lines).FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (purchase is null)
        {
            return TypedResults.NotFound();
        }

        if (form.File is null || form.File.Length == 0 || form.File.Length > MaxReceiptBytes)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [$"Attach a PDF or image of at most {MaxReceiptBytes / (1024 * 1024)} MB."] });
        }

        await using var stream = form.File.OpenReadStream();
        var header = new byte[16];
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        var (contentType, extension) = InspectReceipt(header.AsSpan(0, read));
        if (contentType is null || extension is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["The receipt must be a PDF, PNG, JPEG or WebP file."] });
        }

        stream.Position = 0;
        var key = $"{purchase.WorkspaceId:N}/purchases/{purchase.Id:N}/receipt-{Guid.NewGuid():N}{extension}";
        await storage.SaveAsync(key, stream, contentType, cancellationToken);
        var previousKey = purchase.ReceiptFileKey;
        try
        {
            var fileName = Path.GetFileName(form.File.FileName);
            purchase.ReceiptFileKey = key;
            purchase.ReceiptContentType = contentType;
            purchase.ReceiptFileName = fileName.Length > 200 ? fileName[^200..] : fileName;
            purchase.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await DeleteQuietlyAsync(storage, key);
            throw;
        }

        if (previousKey is not null)
        {
            await DeleteQuietlyAsync(storage, previousKey);
        }

        return TypedResults.Ok(await BuildDetailAsync(purchase, dbContext, currentWorkspace, cancellationToken));
    }

    private static async Task<IResult> DeleteReceiptAsync(Guid id, PrintOsDbContext dbContext, ICurrentWorkspace currentWorkspace, IFileStorage storage, CancellationToken cancellationToken)
    {
        var purchase = await dbContext.Set<Purchase>().Include(candidate => candidate.Lines).FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (purchase is null)
        {
            return TypedResults.NotFound();
        }

        var key = purchase.ReceiptFileKey;
        purchase.ReceiptFileKey = null;
        purchase.ReceiptFileName = null;
        purchase.ReceiptContentType = null;
        purchase.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        if (key is not null)
        {
            await DeleteQuietlyAsync(storage, key);
        }

        return TypedResults.Ok(await BuildDetailAsync(purchase, dbContext, currentWorkspace, cancellationToken));
    }

    private static async Task<IResult> GetReceiptUrlAsync(Guid id, PrintOsDbContext dbContext, ModelFileSigner signer, CancellationToken cancellationToken)
    {
        var purchase = await dbContext.Set<Purchase>().FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (purchase?.ReceiptFileKey is null)
        {
            return TypedResults.NotFound();
        }

        var token = signer.Sign(SignedFileKind.PurchaseReceipt, purchase.WorkspaceId, purchase.Id, ReceiptUrlLifetime);
        return TypedResults.Ok(new ReceiptUrlResponse($"/api/purchase-files/{token}", DateTimeOffset.UtcNow.Add(ReceiptUrlLifetime), purchase.ReceiptFileName ?? "receipt"));
    }

    private static async Task<IResult> DownloadReceiptAsync(string token, PrintOsDbContext dbContext, IFileStorage storage, ModelFileSigner signer, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var claim = signer.Verify(token);
        if (claim is null || claim.Kind != SignedFileKind.PurchaseReceipt)
        {
            return TypedResults.NotFound();
        }

        var purchase = await dbContext.Set<Purchase>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(candidate => candidate.Id == claim.Id && candidate.WorkspaceId == claim.WorkspaceId, cancellationToken);
        if (purchase?.ReceiptFileKey is null)
        {
            return TypedResults.NotFound();
        }

        var stream = await storage.OpenReadAsync(purchase.ReceiptFileKey, cancellationToken);
        if (stream is null)
        {
            return TypedResults.NotFound();
        }

        httpContext.Response.Headers.CacheControl = "private, no-store";
        httpContext.Response.Headers.XContentTypeOptions = "nosniff";
        return TypedResults.File(stream, purchase.ReceiptContentType ?? "application/octet-stream", purchase.ReceiptFileName);
    }
}
