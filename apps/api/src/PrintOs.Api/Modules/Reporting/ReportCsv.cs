using System.Globalization;
using System.Text;

namespace PrintOs.Api.Modules.Reporting;

public static class ReportCsv
{
    private static readonly char[] FormulaStarts = ['=', '+', '-', '@', '\t', '\r'];

    public static string Text(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var safe = FormulaStarts.Contains(value[0]) ? "'" + value : value;
        return Escape(safe);
    }

    public static string Number(decimal? value, int decimals = 2) =>
        value is null ? string.Empty : Math.Round(value.Value, decimals, MidpointRounding.AwayFromZero).ToString("F" + decimals, CultureInfo.InvariantCulture);

    public static string Whole(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    public static string Escape(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;

    public static byte[] Build(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> rows)
    {
        var builder = new StringBuilder();
        builder.Append(string.Join(',', header.Select(Escape))).Append("\r\n");
        foreach (var row in rows)
        {
            builder.Append(string.Join(',', row)).Append("\r\n");
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(builder.ToString())];
    }

    public static byte[] Margin(MarginReportResponse report)
    {
        static IReadOnlyList<string> Row(string scope, string name, string? sku, MarginFigures figures) =>
        [
            scope,
            Text(name),
            Text(sku),
            Whole(figures.Units),
            Number(figures.Revenue),
            Number(figures.Cost),
            Number(figures.GrossProfit),
            Number(figures.MarginPercent, 1),
            Number(figures.StandCosts),
            Number(figures.NetProfit),
            figures.CostComplete ? "yes" : "no",
        ];

        var rows = new List<IReadOnlyList<string>> { Row("total", "Total", null, report.Current) };
        rows.AddRange(report.Channels.Select(row => Row("channel", row.Label, null, row.Figures)));
        rows.AddRange(report.Markets.Select(row => Row("market", row.Label, null, row.Figures)));
        rows.AddRange(report.Products.Select(row => Row("product", row.Label, null, row.Figures)));
        rows.AddRange(report.Variants.Select(row => Row("variant", row.Label, row.Sku, row.Figures)));
        return Build(["scope", "name", "sku", "units", "revenue", "cost", "gross_profit", "margin_percent", "stand_costs", "net_profit", "cost_complete"], rows);
    }

    public static byte[] MarginTrend(MarginReportResponse report) =>
        Build(
            ["period_start", "revenue", "cost", "gross_profit", "margin_percent"],
            report.Trend.Select(point => (IReadOnlyList<string>)
            [
                point.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Number(point.Revenue),
                Number(point.Cost),
                Number(point.GrossProfit),
                Number(point.MarginPercent, 1),
            ]));

    public static byte[] Bestsellers(IReadOnlyList<RankedItem> items) =>
        Build(
            ["rank", "name", "sku", "units", "revenue", "profit", "margin_percent", "units_share_percent", "revenue_share_percent", "profit_share_percent"],
            items.Select(item => (IReadOnlyList<string>)
            [
                Whole(item.Rank),
                Text(item.Label),
                Text(item.Sku),
                Whole(item.Units),
                Number(item.Revenue),
                Number(item.Profit),
                Number(item.MarginPercent, 1),
                Number(item.UnitsSharePercent, 1),
                Number(item.RevenueSharePercent, 1),
                Number(item.ProfitSharePercent, 1),
            ]));

    public static byte[] SlowMovers(IReadOnlyList<SlowMoverRow> rows) =>
        Build(
            ["name", "sku", "on_hand", "stock_value", "last_sold_at", "days_since_last_sale"],
            rows.Select(row => (IReadOnlyList<string>)
            [
                Text(row.Label),
                Text(row.Sku),
                Whole(row.OnHand),
                Number(row.StockValue),
                row.LastSoldAt?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) ?? string.Empty,
                Whole(row.DaysSinceLastSale),
            ]));
}
