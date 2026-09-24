using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Avallo.Web.Domain;

namespace Avallo.Web.Features.Reconciliation;

public interface IStatementParser
{
    ParsedStatement Parse(ReadOnlySpan<byte> content, string fileName);
}

public sealed partial class StatementParser : IStatementParser
{
    private const int MaximumTransactions = 5000;

    public ParsedStatement Parse(ReadOnlySpan<byte> content, string fileName)
    {
        var text = Decode(content);
        if (fileName.EndsWith(".ofx", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("<OFX", StringComparison.OrdinalIgnoreCase))
            return ParseOfx(text);
        return ParseCsv(text);
    }

    private static ParsedStatement ParseOfx(string text)
    {
        var transactions = new List<ParsedStatementTransaction>();
        foreach (Match match in StatementTransactionRegex().Matches(text))
        {
            var block = match.Groups[1].Value;
            var occurredAt = ParseOfxDate(Tag(block, "DTPOSTED"));
            var amount = ParseAmount(Tag(block, "TRNAMT"));
            if (amount == 0)
                continue;
            var fitId = Tag(block, "FITID");
            var reference = FirstNotEmpty(Tag(block, "REFNUM"), fitId);
            var description = FirstNotEmpty(Tag(block, "MEMO"), Tag(block, "NAME"), "Movimentacao bancaria");
            transactions.Add(new ParsedStatementTransaction(
                ExternalId(fitId, occurredAt, amount, description, transactions.Count),
                occurredAt, amount, "BRL", Limit(Clean(description), 500), LimitOrNull(reference, 240)));
            EnsureLimit(transactions.Count);
        }

        if (transactions.Count == 0)
            throw new InvalidDataException("O arquivo OFX nao contem transacoes validas.");
        var account = FirstNotEmpty(Tag(text, "ACCTID"), null);
        var currency = FirstNotEmpty(Tag(text, "CURDEF"), "BRL")!.ToUpperInvariant();
        return Build(ReconciliationSources.Ofx, account, currency, transactions);
    }

    private static ParsedStatement ParseCsv(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2)
            throw new InvalidDataException("O CSV deve conter cabecalho e pelo menos uma transacao.");
        var delimiter = DetectDelimiter(lines[0]);
        var headers = SplitCsvLine(lines[0], delimiter).Select(NormalizeHeader).ToArray();
        var dateIndex = HeaderIndex(headers, "DATA", "DATE", "DTPOSTED", "DATAPOSTAGEM");
        var amountIndex = HeaderIndex(headers, "VALOR", "AMOUNT", "TRNAMT", "CREDITO");
        var descriptionIndex = HeaderIndex(headers, "DESCRICAO", "DESCRIPTION", "HISTORICO", "MEMO", "NAME");
        var idIndex = OptionalHeaderIndex(headers, "ID", "FITID", "DOCUMENTO", "REFERENCIA", "REFERENCE");
        var currencyIndex = OptionalHeaderIndex(headers, "MOEDA", "CURRENCY");
        var transactions = new List<ParsedStatementTransaction>();
        for (var lineIndex = 1; lineIndex < lines.Length; lineIndex++)
        {
            var fields = SplitCsvLine(lines[lineIndex], delimiter);
            if (fields.Length <= Math.Max(dateIndex, Math.Max(amountIndex, descriptionIndex)))
                throw new InvalidDataException($"Linha {lineIndex + 1} do CSV esta incompleta.");
            var occurredAt = ParseCsvDate(fields[dateIndex]);
            var amount = ParseAmount(fields[amountIndex]);
            if (amount == 0)
                continue;
            var description = Limit(Clean(fields[descriptionIndex]), 500);
            var reference = idIndex >= 0 && idIndex < fields.Length ? LimitOrNull(fields[idIndex], 240) : null;
            var currency = currencyIndex >= 0 && currencyIndex < fields.Length
                ? FirstNotEmpty(CleanOrNull(fields[currencyIndex]), "BRL")!.ToUpperInvariant()
                : "BRL";
            transactions.Add(new ParsedStatementTransaction(
                ExternalId(reference, occurredAt, amount, description, lineIndex),
                occurredAt, amount, currency, description, reference));
            EnsureLimit(transactions.Count);
        }

        if (transactions.Count == 0)
            throw new InvalidDataException("O CSV nao contem transacoes com valor diferente de zero.");
        return Build(ReconciliationSources.Csv, null, transactions[0].Currency, transactions);
    }

    private static ParsedStatement Build(string source, string? account, string currency,
        List<ParsedStatementTransaction> transactions) => new(source, CleanOrNull(account), currency,
        transactions.Min(x => DateOnly.FromDateTime(x.OccurredAt.UtcDateTime)),
        transactions.Max(x => DateOnly.FromDateTime(x.OccurredAt.UtcDateTime)), transactions);

    private static string Decode(ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty)
            throw new InvalidDataException("O arquivo esta vazio.");
        var encoding = content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF
            ? Encoding.UTF8
            : DetectEncoding(content);
        return encoding.GetString(content);
    }

    private static Encoding DetectEncoding(ReadOnlySpan<byte> content)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(content) is not null
                ? Encoding.UTF8
                : Encoding.Latin1;
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1;
        }
    }

    private static char DetectDelimiter(string header)
    {
        var candidates = new[] { ';', ',', '\t' };
        return candidates.OrderByDescending(x => header.Count(c => c == x)).First();
    }

    private static string[] SplitCsvLine(string line, char delimiter)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var value = line[index];
            if (value == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else quoted = !quoted;
            }
            else if (value == delimiter && !quoted)
            {
                values.Add(current.ToString().Trim());
                current.Clear();
            }
            else current.Append(value);
        }
        if (quoted)
            throw new InvalidDataException("O CSV contem aspas nao finalizadas.");
        values.Add(current.ToString().Trim());
        return values.ToArray();
    }

    private static DateTimeOffset ParseCsvDate(string value)
    {
        var formats = new[] { "dd/MM/yyyy", "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm", "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssK" };
        foreach (var culture in new[] { CultureInfo.GetCultureInfo("pt-BR"), CultureInfo.InvariantCulture })
            if (DateTimeOffset.TryParseExact(value.Trim(), formats, culture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                return parsed;
        throw new InvalidDataException($"Data bancaria invalida: {value}.");
    }

    private static DateTimeOffset ParseOfxDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 8 ||
            !DateTime.TryParseExact(value[..8], "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
            throw new InvalidDataException("O OFX contem uma data de transacao invalida.");
        return new DateTimeOffset(date, TimeSpan.Zero);
    }

    private static decimal ParseAmount(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException("Uma transacao nao possui valor.");
        var normalized = value.Trim().Replace("R$", string.Empty, StringComparison.OrdinalIgnoreCase).Replace(" ", string.Empty);
        var lastComma = normalized.LastIndexOf(',');
        var lastDot = normalized.LastIndexOf('.');
        if (lastComma >= 0 && lastDot >= 0)
            normalized = lastComma > lastDot ? normalized.Replace(".", string.Empty).Replace(',', '.') : normalized.Replace(",", string.Empty);
        else if (lastComma >= 0)
            normalized = normalized.Replace(".", string.Empty).Replace(',', '.');
        if (!decimal.TryParse(normalized, NumberStyles.Number | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var amount))
            throw new InvalidDataException($"Valor bancario invalido: {value}.");
        return decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
    }

    private static int HeaderIndex(string[] headers, params string[] names)
    {
        var index = OptionalHeaderIndex(headers, names);
        return index >= 0 ? index : throw new InvalidDataException($"Coluna obrigatoria ausente no CSV: {string.Join('/', names)}.");
    }

    private static int OptionalHeaderIndex(string[] headers, params string[] names) =>
        Array.FindIndex(headers, header => names.Contains(header, StringComparer.Ordinal));

    private static string NormalizeHeader(string value)
    {
        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        return new string(normalized.Where(x => CharUnicodeInfo.GetUnicodeCategory(x) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(x)).ToArray()).ToUpperInvariant();
    }

    private static string? Tag(string text, string name)
    {
        var match = Regex.Match(text, $"<{name}>([^<\\r\\n]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string ExternalId(string? sourceId, DateTimeOffset date, decimal amount, string description, int ordinal)
    {
        if (!string.IsNullOrWhiteSpace(sourceId))
            return Limit(Clean(sourceId), 160);
        var canonical = $"{date:O}|{amount:0.00}|{description.Trim()}|{ordinal}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string Clean(string? value) => string.IsNullOrWhiteSpace(value)
        ? "Movimentacao bancaria"
        : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
    private static string? CleanOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : Clean(value);
    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];
    private static string? LimitOrNull(string? value, int length) => CleanOrNull(value) is { } cleaned ? Limit(cleaned, length) : null;
    private static string? FirstNotEmpty(string? first, string? fallback) => string.IsNullOrWhiteSpace(first) ? fallback : first;
    private static string FirstNotEmpty(string? first, string? second, string fallback) =>
        !string.IsNullOrWhiteSpace(first) ? first : !string.IsNullOrWhiteSpace(second) ? second : fallback;
    private static void EnsureLimit(int count)
    {
        if (count > MaximumTransactions)
            throw new InvalidDataException($"O extrato excede o limite de {MaximumTransactions} transacoes.");
    }

    [GeneratedRegex("<STMTTRN>(.*?)(?=<STMTTRN>|</BANKTRANLIST>|</OFX>|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex StatementTransactionRegex();
}

public sealed record ParsedStatement(string Source, string? AccountReference, string Currency,
    DateOnly PeriodStart, DateOnly PeriodEnd, IReadOnlyList<ParsedStatementTransaction> Transactions);
public sealed record ParsedStatementTransaction(string ExternalId, DateTimeOffset OccurredAt,
    decimal Amount, string Currency, string Description, string? Reference);
