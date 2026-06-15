using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.DTOs;

namespace Nexo.Api.Controllers.Integrations;

[ApiController]
[Route("api/integrations")]
[Authorize]
public class LookupController : ControllerBase
{
    private readonly ICepLookupProvider _cepProvider;
    private readonly ICnpjLookupProvider _cnpjProvider;
    private readonly ILogger<LookupController> _logger;

    public LookupController(
        ICepLookupProvider cepProvider,
        ICnpjLookupProvider cnpjProvider,
        ILogger<LookupController> logger)
    {
        _cepProvider = cepProvider;
        _cnpjProvider = cnpjProvider;
        _logger = logger;
    }

    /// <summary>
    /// Lookup CEP address data from external providers.
    /// </summary>
    /// <param name="cep">CEP string (8 digits, with or without formatting)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>
    /// Ok with { found: true, data: CepLookupResult } if found.
    /// Ok with { found: false, data: null } if not found.
    /// Ok with { found: false, data: null, unavailable: true } if provider error.
    /// BadRequest if invalid format.
    /// </returns>
    [HttpGet("cep/{cep}")]
    public async Task<IActionResult> LookupCep(string cep, CancellationToken ct)
    {
        // Normalize: strip all non-digit characters
        var normalizedCep = Regex.Replace(cep, @"\D", "");

        // Log normalization if different
        if (normalizedCep != cep)
        {
            _logger.LogInformation("[Lookup] CEP normalized {Input} → {Normalized}", cep, normalizedCep);
        }

        // Validate format: must be exactly 8 digits
        if (normalizedCep.Length != 8)
        {
            return BadRequest(new { error = "CEP deve conter 8 dígitos." });
        }

        try
        {
            // Lookup from provider
            var result = await _cepProvider.LookupAsync(normalizedCep, ct);

            if (result == null)
            {
                return Ok(new { found = false, data = (object?)null });
            }

            return Ok(new { found = true, data = result });
        }
        catch (Exception ex)
        {
            // Log error without exposing raw CEP (PII)
            _logger.LogError(ex, "CEP lookup failed for {CepPrefix}: {Message}", normalizedCep[..4] + "...", ex.Message);

            // Return graceful degradation with unavailable flag
            return Ok(new { found = false, data = (object?)null, unavailable = true });
        }
    }

    /// <summary>
    /// Lookup CNPJ company data from external providers.
    /// </summary>
    /// <param name="cnpj">CNPJ string (14 digits, with or without formatting)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>
    /// Ok with { found: true, data: CnpjLookupResult } if found.
    /// Ok with { found: false, data: null } if not found.
    /// Ok with { found: false, data: null, unavailable: true } if provider error.
    /// BadRequest if invalid format.
    /// </returns>
    [HttpGet("cnpj/{cnpj}")]
    public async Task<IActionResult> LookupCnpj(string cnpj, CancellationToken ct)
    {
        // Normalize: strip all non-digit characters
        var normalizedCnpj = Regex.Replace(cnpj, @"\D", "");

        // Log normalization if different
        if (normalizedCnpj != cnpj)
        {
            _logger.LogInformation("[Lookup] CNPJ normalized {Input} → {Normalized}", cnpj, normalizedCnpj);
        }

        // Validate format: must be exactly 14 digits
        if (normalizedCnpj.Length != 14)
        {
            return BadRequest(new { error = "CNPJ deve conter 14 dígitos." });
        }

        try
        {
            // Lookup from provider
            var result = await _cnpjProvider.LookupAsync(normalizedCnpj, ct);

            if (result == null)
            {
                return Ok(new { found = false, data = (object?)null });
            }

            return Ok(new { found = true, data = result });
        }
        catch (Exception ex)
        {
            // Log error without exposing raw CNPJ (PII)
            _logger.LogError(ex, "CNPJ lookup failed for {CnpjPrefix}: {Message}", normalizedCnpj[..4] + "...", ex.Message);

            // Return graceful degradation with unavailable flag
            return Ok(new { found = false, data = (object?)null, unavailable = true });
        }
    }
}
