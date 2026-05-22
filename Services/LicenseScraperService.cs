using System.IO;
using System.Net.Http;
using ClosedXML.Excel;
using DeploymentApp.Models;
using DeploymentApp.Services.Interfaces;

namespace DeploymentApp.Services;

public sealed class LicenseScraperService : ILicenseScraperService
{
    private readonly HttpClient _httpClient;
    private readonly ILogService _log;

    // TODO: Update to the real Excel download URL on the Passepartout portal
    private const string LicenseExcelUrl = "https://www.passepartout.net/area-riservata/licenze.xlsx";

    public LicenseScraperService(HttpClient httpClient, ILogService log)
    {
        _httpClient = httpClient;
        _log = log;
    }

    public async Task<IReadOnlyList<LicenseEntry>> FetchLicensesAsync(CancellationToken ct = default)
    {
        _log.Info("Downloading license Excel from Passepartout portal...");

        var bytes = await _httpClient.GetByteArrayAsync(LicenseExcelUrl, ct);
        _log.Info($"Excel downloaded ({bytes.Length} bytes). Parsing...");

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.First();

        var headers = sheet.Row(1).Cells()
            .Select(c => c.GetString().Trim())
            .ToList();

        var entries = new List<LicenseEntry>();

        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var entry = new LicenseEntry();
            for (int i = 0; i < headers.Count; i++)
            {
                var value = row.Cell(i + 1).GetString().Trim();
                switch (headers[i].ToLowerInvariant())
                {
                    case "customer": case "cliente": entry.CustomerName = value; break;
                    case "product": case "prodotto": entry.ProductName = value; break;
                    case "licensekey": case "license": case "licenza": entry.LicenseKey = value; break;
                    case "expiry": case "scadenza": entry.ExpiryDate = value; break;
                    default: entry.ExtraFields[headers[i]] = value; break;
                }
            }
            entries.Add(entry);
        }

        _log.Info($"Parsed {entries.Count} license entries.");
        return entries;
    }

    public string? ExtractLicenseKey(IReadOnlyList<LicenseEntry> licenses, string productName, string customerName)
    {
        return licenses.FirstOrDefault(l =>
            l.ProductName.Contains(productName, StringComparison.OrdinalIgnoreCase) &&
            l.CustomerName.Contains(customerName, StringComparison.OrdinalIgnoreCase))
            ?.LicenseKey;
    }
}
