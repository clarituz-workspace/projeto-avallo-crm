using System.Net;
using System.Net.Http.Json;
using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;

namespace Avallo.Web.Features.Fiscal;

public sealed class BrasilApiCnpjClient(HttpClient httpClient, IMemoryCache? cache = null)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> LookupLocks = new();

    public static string ValidateAndNormalizeCnpj(string cnpj)
    {
        if (string.IsNullOrWhiteSpace(cnpj) || cnpj.Any(x => !char.IsDigit(x) && x is not '.' and not '/' and not '-' && !char.IsWhiteSpace(x)))
            throw new ArgumentException("CNPJ contains invalid characters.", nameof(cnpj));
        var digits = new string((cnpj ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length != 14 || digits.Distinct().Count() == 1)
            throw new ArgumentException("CNPJ must contain 14 valid digits.", nameof(cnpj));

        var first = CheckDigit(digits.AsSpan(0, 12), [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        var second = CheckDigit(digits.AsSpan(0, 13), [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        if (digits[12] - '0' != first || digits[13] - '0' != second)
            throw new ArgumentException("CNPJ check digits are invalid.", nameof(cnpj));
        return digits;
    }

    public async Task<BrasilApiCnpjResult> LookupAsync(string cnpj, CancellationToken cancellationToken = default)
    {
        var digits = ValidateAndNormalizeCnpj(cnpj);
        var cacheKey = $"brasil-api:cnpj:{digits}";
        if (cache?.TryGetValue(cacheKey, out BrasilApiCnpjResult? cached) == true && cached is not null)
            return cached;

        var lookupLock = LookupLocks.GetOrAdd(digits, _ => new SemaphoreSlim(1, 1));
        await lookupLock.WaitAsync(cancellationToken);
        try
        {
            if (cache?.TryGetValue(cacheKey, out cached) == true && cached is not null)
                return cached;

            try
            {
                var result = await LookupFromBrasilApiAsync(digits, cancellationToken);
                cache?.Set(cacheKey, result, TimeSpan.FromHours(24));
                return result;
            }
            catch (Exception ex) when (ex is not ArgumentException && (ex is not HttpRequestException hre || hre.StatusCode != HttpStatusCode.NotFound))
            {
                try
                {
                    var result = await LookupFromCnpjWsAsync(digits, cancellationToken);
                    cache?.Set(cacheKey, result, TimeSpan.FromHours(24));
                    return result;
                }
                catch
                {
                    // If the fallback also fails, rethrow the original BrasilAPI exception.
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
                    throw;
                }
            }
        }
        finally
        {
            lookupLock.Release();
        }
    }

    private async Task<BrasilApiCnpjResult> LookupFromBrasilApiAsync(string digits, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var response = await httpClient.GetAsync($"api/cnpj/v1/{digits}", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<BrasilApiCnpjResult>(cancellationToken: cancellationToken)
                        ?? throw new InvalidOperationException("Brasil API returned an empty CNPJ response.");
                }
                if (!IsTransient(response.StatusCode) || attempt == 3)
                    throw CreateFailure(response.StatusCode, response.ReasonPhrase);
                await Task.Delay(RetryDelay(response, attempt), cancellationToken);
            }
            catch (HttpRequestException exception) when (attempt < 3 &&
                (exception.StatusCode is null || IsTransient(exception.StatusCode.Value)))
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), cancellationToken);
            }
        }
        throw new HttpRequestException("Brasil API CNPJ lookup failed after 3 attempts.");
    }

    private async Task<BrasilApiCnpjResult> LookupFromCnpjWsAsync(string digits, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"https://publica.cnpj.ws/cnpj/{digits}", cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var wsResult = await response.Content.ReadFromJsonAsync<CnpjWsResponse>(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("CNPJ.ws returned an empty response.");
            return MapToCnpjResult(wsResult);
        }
        throw CreateFailure(response.StatusCode, response.ReasonPhrase);
    }

    private static BrasilApiCnpjResult MapToCnpjResult(CnpjWsResponse ws)
    {
        var est = ws.Estabelecimento;
        long.TryParse(est.AtividadePrincipal.Id, out var mainCnae);

        var secondary = (est.AtividadesSecundarias ?? []).Select(x =>
        {
            long.TryParse(x.Id, out var secCnae);
            return new BrasilApiCnae(secCnae, x.Descricao);
        }).ToList();

        var regimeHistory = new List<BrasilApiTaxRegime>();
        if (ws.Simples is not null && ws.Simples.Optante == "Sim" && !string.IsNullOrEmpty(ws.Simples.DataOpcao))
        {
            if (DateTime.TryParse(ws.Simples.DataOpcao, out var dt))
            {
                regimeHistory.Add(new BrasilApiTaxRegime(dt.Year, "SIMPLES NACIONAL"));
            }
        }

        return new BrasilApiCnpjResult(
            Cnpj: est.Cnpj,
            LegalName: ws.RazaoSocial,
            TradeName: est.NomeFantasia,
            RegistrationStatus: est.SituacaoCadastral.ToUpperInvariant(),
            CompanySize: ws.Porte?.Descricao.ToUpperInvariant() ?? "DEMAIS",
            Street: est.Logradouro,
            Number: est.Numero,
            Complement: est.Complemento,
            District: est.Bairro,
            City: est.Cidade.Nome,
            State: est.Estado.Sigla,
            PostalCode: est.Cep,
            MainCnaeCode: mainCnae,
            MainCnaeDescription: est.AtividadePrincipal.Descricao,
            SecondaryCnaes: secondary,
            TaxRegimeHistory: regimeHistory
        );
    }

    private static HttpRequestException CreateFailure(HttpStatusCode statusCode, string? reason) =>
        statusCode == HttpStatusCode.TooManyRequests
            ? new HttpRequestException("BrasilAPI atingiu o limite de consultas. Aguarde alguns minutos e tente novamente.", null, statusCode)
            : new HttpRequestException($"Brasil API CNPJ lookup failed with HTTP {(int)statusCode} ({reason}).", null, statusCode);

    private static TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
    {
        var delay = response.Headers.RetryAfter?.Delta;
        if (delay is null && response.Headers.RetryAfter?.Date is { } retryAt)
            delay = retryAt - DateTimeOffset.UtcNow;
        if (delay is null || delay <= TimeSpan.Zero)
            delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
        return delay > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : delay.Value;
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)statusCode >= 500;

    private static int CheckDigit(ReadOnlySpan<char> digits, ReadOnlySpan<int> weights)
    {
        var sum = 0;
        for (var index = 0; index < digits.Length; index++)
            sum += (digits[index] - '0') * weights[index];
        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}

public sealed record BrasilApiCnpjResult(
    [property: JsonPropertyName("cnpj")] string Cnpj,
    [property: JsonPropertyName("razao_social")] string LegalName,
    [property: JsonPropertyName("nome_fantasia")] string? TradeName,
    [property: JsonPropertyName("descricao_situacao_cadastral")] string RegistrationStatus,
    [property: JsonPropertyName("porte")] string CompanySize,
    [property: JsonPropertyName("logradouro")] string Street,
    [property: JsonPropertyName("numero")] string Number,
    [property: JsonPropertyName("complemento")] string? Complement,
    [property: JsonPropertyName("bairro")] string District,
    [property: JsonPropertyName("municipio")] string City,
    [property: JsonPropertyName("uf")] string State,
    [property: JsonPropertyName("cep")] string PostalCode,
    [property: JsonPropertyName("cnae_fiscal")] long MainCnaeCode,
    [property: JsonPropertyName("cnae_fiscal_descricao")] string MainCnaeDescription,
    [property: JsonPropertyName("cnaes_secundarios")] IReadOnlyList<BrasilApiCnae> SecondaryCnaes,
    [property: JsonPropertyName("regime_tributario")] IReadOnlyList<BrasilApiTaxRegime>? TaxRegimeHistory)
{
    public string AddressSummary => string.Join(", ", new[]
        { Street, Number, Complement, District, $"{City}/{State}", PostalCode }.Where(x => !string.IsNullOrWhiteSpace(x)));
}

public sealed record BrasilApiCnae(
    [property: JsonPropertyName("codigo")] long Code,
    [property: JsonPropertyName("descricao")] string Description);

public sealed record BrasilApiTaxRegime(
    [property: JsonPropertyName("ano")] int Year,
    [property: JsonPropertyName("forma_de_tributacao")] string TaxationForm);

public sealed class CnpjWsResponse
{
    [JsonPropertyName("razao_social")] public string RazaoSocial { get; set; } = string.Empty;
    [JsonPropertyName("porte")] public CnpjWsPorte? Porte { get; set; }
    [JsonPropertyName("simples")] public CnpjWsSimples? Simples { get; set; }
    [JsonPropertyName("estabelecimento")] public CnpjWsEstabelecimento Estabelecimento { get; set; } = new();
}

public sealed class CnpjWsPorte
{
    [JsonPropertyName("descricao")] public string Descricao { get; set; } = string.Empty;
}

public sealed class CnpjWsSimples
{
    [JsonPropertyName("optante")] public string? Optante { get; set; }
    [JsonPropertyName("data_opcao")] public string? DataOpcao { get; set; }
}

public sealed class CnpjWsEstabelecimento
{
    [JsonPropertyName("cnpj")] public string Cnpj { get; set; } = string.Empty;
    [JsonPropertyName("nome_fantasia")] public string? NomeFantasia { get; set; }
    [JsonPropertyName("situacao_cadastral")] public string SituacaoCadastral { get; set; } = string.Empty;
    [JsonPropertyName("logradouro")] public string Logradouro { get; set; } = string.Empty;
    [JsonPropertyName("numero")] public string Numero { get; set; } = string.Empty;
    [JsonPropertyName("complemento")] public string? Complemento { get; set; }
    [JsonPropertyName("bairro")] public string Bairro { get; set; } = string.Empty;
    [JsonPropertyName("cep")] public string Cep { get; set; } = string.Empty;
    [JsonPropertyName("atividade_principal")] public CnpjWsCnae AtividadePrincipal { get; set; } = new();
    [JsonPropertyName("atividades_secundarias")] public List<CnpjWsCnae> AtividadesSecundarias { get; set; } = [];
    [JsonPropertyName("estado")] public CnpjWsEstado Estado { get; set; } = new();
    [JsonPropertyName("cidade")] public CnpjWsCidade Cidade { get; set; } = new();
}

public sealed class CnpjWsCnae
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("descricao")] public string Descricao { get; set; } = string.Empty;
}

public sealed class CnpjWsEstado
{
    [JsonPropertyName("sigla")] public string Sigla { get; set; } = string.Empty;
}

public sealed class CnpjWsCidade
{
    [JsonPropertyName("nome")] public string Nome { get; set; } = string.Empty;
}
