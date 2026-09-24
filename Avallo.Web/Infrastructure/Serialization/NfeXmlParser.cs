using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Avallo.Web.Features.Inventory;

public sealed record ParsedNfeInvoice(
    string AccessKey,
    string InvoiceNumber,
    string Series,
    DateTimeOffset IssuedAt,
    string SupplierTaxId,
    string SupplierName,
    decimal Total,
    IReadOnlyList<ParsedNfeItem> Items);

public sealed record ParsedNfeItem(
    string SupplierSku,
    string? Barcode,
    string Name,
    decimal Quantity,
    decimal UnitCost,
    decimal Total);

public interface INfeXmlParser
{
    ParsedNfeInvoice Parse(Stream xml);
}

public sealed class NfeXmlParser : INfeXmlParser
{
    public ParsedNfeInvoice Parse(Stream xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        try
        {
            using var reader = XmlReader.Create(xml, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                CloseInput = false
            });
            var document = XDocument.Load(reader, LoadOptions.None);
            var infNfe = Single(document.Descendants(), "infNFe");
            var accessKey = RequiredAttribute(infNfe, "Id");
            if (accessKey.StartsWith("NFe", StringComparison.OrdinalIgnoreCase))
                accessKey = accessKey[3..];
            if (accessKey.Length != 44 || accessKey.Any(x => !char.IsAsciiDigit(x)))
                throw Invalid("infNFe Id must contain a 44-digit access key.");

            var ide = RequiredSingleChild(infNfe, "ide");
            var emit = RequiredSingleChild(infNfe, "emit");
            var issueDateText = OptionalSingleChildValue(ide, "dhEmi") ?? RequiredSingleChildValue(ide, "dEmi");
            if (!DateTimeOffset.TryParse(issueDateText, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var issuedAt))
                throw Invalid("NF-e issue date is invalid.");

            var parsedItems = DirectChildren(infNfe, "det").Select(ParseItem).ToArray();
            if (parsedItems.Length == 0)
                throw Invalid("NF-e must contain at least one det item.");
            var total = Single(infNfe.Descendants(), "vNF");
            return new ParsedNfeInvoice(
                accessKey,
                RequiredSingleChildValue(ide, "nNF"),
                RequiredSingleChildValue(ide, "serie"),
                issuedAt,
                OptionalSingleChildValue(emit, "CNPJ")
                    ?? OptionalSingleChildValue(emit, "CPF")
                    ?? throw Invalid("NF-e supplier CNPJ or CPF is required."),
                RequiredSingleChildValue(emit, "xNome"),
                Decimal(total.Value, "vNF", positive: false),
                parsedItems);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException or FormatException or OverflowException)
        {
            throw Invalid("Malformed NF-e XML.", exception);
        }
    }

    private static ParsedNfeItem ParseItem(XElement det)
    {
        var product = RequiredSingleChild(det, "prod");
        var barcode = OptionalSingleChildValue(product, "cEAN");
        if (string.Equals(barcode, "SEM GTIN", StringComparison.OrdinalIgnoreCase))
            barcode = null;
        return new ParsedNfeItem(
            RequiredSingleChildValue(product, "cProd"),
            barcode,
            RequiredSingleChildValue(product, "xProd"),
            Decimal(RequiredSingleChildValue(product, "qCom"), "qCom", positive: true),
            Decimal(RequiredSingleChildValue(product, "vUnCom"), "vUnCom", positive: false),
            Decimal(RequiredSingleChildValue(product, "vProd"), "vProd", positive: false));
    }

    private static decimal Decimal(string value, string name, bool positive)
    {
        if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var result) ||
            (positive ? result <= 0 : result < 0))
            throw Invalid($"NF-e {name} is invalid.");
        return result;
    }

    private static XElement Single(IEnumerable<XElement> elements, string localName)
    {
        var matches = elements.Where(x => x.Name.LocalName == localName).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : throw Invalid($"NF-e must contain exactly one {localName}.");
    }

    private static IEnumerable<XElement> DirectChildren(XElement parent, string localName) =>
        parent.Elements().Where(x => x.Name.LocalName == localName);

    private static XElement RequiredSingleChild(XElement parent, string localName) =>
        Single(DirectChildren(parent, localName), localName);

    private static string RequiredSingleChildValue(XElement parent, string localName)
    {
        var value = RequiredSingleChild(parent, localName).Value.Trim();
        return value.Length > 0 ? value : throw Invalid($"NF-e {localName} is required.");
    }

    private static string? OptionalSingleChildValue(XElement parent, string localName)
    {
        var matches = DirectChildren(parent, localName).Take(2).ToArray();
        if (matches.Length > 1)
            throw Invalid($"NF-e contains duplicate {localName}.");
        return matches.Length == 0 || string.IsNullOrWhiteSpace(matches[0].Value) ? null : matches[0].Value.Trim();
    }

    private static string RequiredAttribute(XElement element, string localName)
    {
        var matches = element.Attributes().Where(x => x.Name.LocalName == localName).Take(2).ToArray();
        return matches.Length == 1 && !string.IsNullOrWhiteSpace(matches[0].Value)
            ? matches[0].Value.Trim()
            : throw Invalid($"NF-e {localName} is required.");
    }

    private static InvalidDataException Invalid(string message, Exception? inner = null) => new(message, inner);
}
