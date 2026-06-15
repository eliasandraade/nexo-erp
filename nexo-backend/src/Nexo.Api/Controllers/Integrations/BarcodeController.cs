using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.Options;

namespace Nexo.Api.Controllers.Integrations;

[ApiController]
[Route("api/integrations")]
[Authorize]
public class BarcodeController : ControllerBase
{
    private readonly IBarcodeProductLookupProvider _provider;
    private readonly IIntegrationFeatureFlags      _flags;
    private readonly ILogger<BarcodeController>    _logger;

    public BarcodeController(
        IBarcodeProductLookupProvider provider,
        IIntegrationFeatureFlags flags,
        ILogger<BarcodeController> logger)
    {
        _provider = provider;
        _flags    = flags;
        _logger   = logger;
    }

    /// <summary>
    /// Lookup product data by barcode from Open Food Facts.
    /// Returns 404 when OpenFoodFactsEnabled feature flag is false.
    /// </summary>
    [HttpGet("barcode/{barcode}")]
    public async Task<IActionResult> LookupBarcode(string barcode, CancellationToken ct)
    {
        if (!_flags.OpenFoodFactsEnabled)
            return NotFound(new { error = "Consulta por código de barras não está habilitada." });

        // Normalize: digits only
        var normalized = Regex.Replace(barcode, @"\D", "");

        if (normalized.Length < 8 || normalized.Length > 14)
            return BadRequest(new { error = "Código de barras deve ter entre 8 e 14 dígitos." });

        _logger.LogInformation("[Barcode] Lookup — barcode={Barcode}", normalized);

        try
        {
            var result = await _provider.LookupAsync(normalized, ct);

            if (result == null)
                return Ok(new { found = false, data = (object?)null });

            return Ok(new { found = true, data = result });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Barcode] Lookup failed for barcode={BarcodePrefix}...", normalized[..4]);
            return Ok(new { found = false, data = (object?)null, unavailable = true });
        }
    }
}
