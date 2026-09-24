using System.Text.RegularExpressions;
using Avallo.Connectors.Abstractions;

namespace Avallo.Web.Features.Connectors;

public sealed partial class ConnectorRegistry
{
    private readonly IReadOnlyDictionary<string, IMarketplaceConnector> _connectors;

    public ConnectorRegistry(IEnumerable<IMarketplaceConnector> connectors)
    {
        var list = connectors.ToArray();
        foreach (var connector in list)
            if (!NamePattern().IsMatch(connector.Descriptor.Name))
                throw new InvalidOperationException($"Connector name '{connector.Descriptor.Name}' must use lowercase letters, numbers or hyphens.");
        _connectors = list.ToDictionary(x => x.Descriptor.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<ConnectorDescriptor> Descriptors => _connectors.Values.Select(x => x.Descriptor).ToArray();

    public IMarketplaceConnector Get(string name) => _connectors.TryGetValue(name, out var connector)
        ? connector
        : throw new ConnectorNotFoundException(name);

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex NamePattern();
}

public sealed class ConnectorNotFoundException(string name)
    : Exception($"Connector '{name}' is not installed.");
