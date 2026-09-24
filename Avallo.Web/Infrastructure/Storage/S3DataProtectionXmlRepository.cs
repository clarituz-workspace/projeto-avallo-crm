using System.Xml.Linq;
using Amazon.S3.Model;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace Avallo.Web.Features.Expenses;

/// <summary>
/// Persiste o key ring do Data Protection no object storage S3-compativel (MinIO local,
/// S3 em producao auto-hospedada). Um objeto por chave — nao ha read-modify-write em um
/// arquivo unico, entao escritas concorrentes de replicas diferentes nao se perdem.
/// Equivale ao PersistKeysToAzureBlobStorage usado quando o provider e "azure".
/// </summary>
internal sealed class S3DataProtectionXmlRepository(S3ObjectStorage storage) : IXmlRepository
{
    private const string Prefix = "system/dataprotection/";

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        var keys = storage.ListKeysAsync(Prefix).GetAwaiter().GetResult();
        var elements = new List<XElement>(keys.Count);
        foreach (var key in keys)
        {
            var content = storage.GetAsync(key, CancellationToken.None).GetAwaiter().GetResult();
            if (content is { Length: > 0 })
                elements.Add(XElement.Parse(System.Text.Encoding.UTF8.GetString(content)));
        }
        return elements;
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        var fileName = friendlyName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
            ? friendlyName
            : friendlyName + ".xml";
        foreach (var character in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(character, '-');
        using var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(element.ToString(SaveOptions.DisableFormatting)));
        storage.PutAsync(Prefix + fileName, content, "application/xml", CancellationToken.None).GetAwaiter().GetResult();
    }
}
