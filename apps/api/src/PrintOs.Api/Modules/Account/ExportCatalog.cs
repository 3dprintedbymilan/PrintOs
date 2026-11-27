using PrintOs.Api.Modules.Audit;
using PrintOs.Api.Modules.Bambu;
using PrintOs.Api.Modules.Costing;
using PrintOs.Api.Modules.FinishedStock;
using PrintOs.Api.Modules.Inventory;
using PrintOs.Api.Modules.Licensing;
using PrintOs.Api.Modules.Markets;
using PrintOs.Api.Modules.ModelLibrary;
using PrintOs.Api.Modules.Production;
using PrintOs.Api.Modules.Products;
using PrintOs.Api.Modules.Sales;
using PrintOs.Api.Modules.Purchases;

namespace PrintOs.Api.Modules.Account;

public static class ExportCatalog
{
    public static readonly IReadOnlyDictionary<Type, string> Descriptions = new Dictionary<Type, string>
    {
        [typeof(AuditEntry)] = "Audit log: who changed which record and when (append-only history of changes).",
        [typeof(BambuConnection)] = "Bambu Lab cloud connection. Never exported: see bambu.json (connected yes/no only).",
        [typeof(BambuDevice)] = "Printers discovered in the Bambu Lab account (serial, name, model, last known live status).",
        [typeof(BambuTaskImport)] = "Which Bambu Lab print tasks were already imported as print jobs.",
        [typeof(CostSnapshot)] = "Cost breakdown frozen at the time a print job was completed.",
        [typeof(Supplier)] = "Filament suppliers.",
        [typeof(Material)] = "Filament materials (type, brand, colour).",
        [typeof(FilamentSpool)] = "Individual filament spools with remaining weight and price.",
        [typeof(MaterialMovement)] = "Stock movements of spools (initial, adjustment, consumption).",
        [typeof(Purchase)] = "Supplier purchases with invoice reference, shipping, discount and status (receipt files under files/Purchase).",
        [typeof(PurchaseLine)] = "Lines of a purchase: material, colour, quantity, weight and price.",
        [typeof(License)] = "Model licenses, attribution texts and proof of purchase (files under files/License).",
        [typeof(PrintModel)] = "Models in the library (thumbnails under files/PrintModel).",
        [typeof(PrintModelVersion)] = "Versions of a model with the uploaded model file (files under files/PrintModelVersion).",
        [typeof(Printer)] = "Your printers.",
        [typeof(PrintJob)] = "Print jobs with times, results and costs.",
        [typeof(PrintJobFilament)] = "Filament used per print job.",
        [typeof(Product)] = "Products you sell.",
        [typeof(ProductVariant)] = "Variants of a product with price.",
        [typeof(BomLine)] = "Bill of materials lines of a variant.",
        [typeof(Customer)] = "Customers: contact details, VAT number and notes.",
        [typeof(Order)] = "Orders with status, totals, payment status, due date and notes.",
        [typeof(OrderLine)] = "Order lines with the price and cost frozen at the time of the order.",
        [typeof(OrderPayment)] = "Payments recorded against orders.",
        [typeof(FinishedStockLevel)] = "Finished goods on hand and reserved per product variant, with the average unit cost and low-stock threshold.",
        [typeof(FinishedStockMovement)] = "Finished goods ledger (produced, sold, reserved, released, adjustment, returned) with unit cost and links to print jobs and orders.",
        [typeof(StockReservation)] = "Finished goods reserved for order lines.",
        [typeof(Market)] = "Markets and events where you sell: dates, location, stand cost, notes and open/closed status.",
        [typeof(MarketSale)] = "Market (point of sale) sales: receipt number, payment method, totals with the cost snapshot, amount tendered and change, void status.",
        [typeof(MarketDayClosing)] = "Immutable daily closing records of a market: totals by payment method, expected and counted cash, difference and notes.",
        [typeof(MarketSaleLine)] = "Lines of a market sale with the price and cost frozen at the time of the sale.",
        [typeof(VariantCostSnapshot)] = "Calculated cost and margin snapshots of variants.",
    };
}
