using System.Reflection;
using System.Runtime.Loader;
using Avallo.Connectors.Abstractions;

namespace Avallo.Web.Features.Connectors;

public static class ConnectorPluginExtensions
{
    /// <summary>
    /// Descobre e registra os modulos de marketplace publicados em <c>Connectors:PluginPath</c>.
    /// O Core nao referencia nenhum conector em tempo de compilacao: adicionar um marketplace
    /// significa publicar um DLL na pasta, nunca recompilar este projeto.
    /// </summary>
    public static IServiceCollection AddConnectorLayer(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger? logger = null)
    {
        var configuredPath = configuration["Connectors:PluginPath"] ?? "connectors";
        var pluginPath = Path.GetFullPath(Path.Combine(environment.ContentRootPath, configuredPath));
        var loadPlugins = configuration.GetValue("Connectors:LoadPlugins", true);

        if (loadPlugins)
            LoadPlugins(services, configuration, pluginPath, environment, logger);

        services.AddScoped<ConnectorRegistry>();
        services.AddScoped<ConnectorGateway>();
        services.AddScoped<ConnectorSyncService>();
        services.AddScoped<ConnectorOAuthService>();
        return services;
    }

    private static void LoadPlugins(
        IServiceCollection services,
        IConfiguration configuration,
        string pluginPath,
        IHostEnvironment environment,
        ILogger? logger)
    {
        if (!Directory.Exists(pluginPath))
        {
            Fail(environment, logger,
                $"A pasta de conectores '{pluginPath}' nao existe. Nenhum marketplace foi carregado.");
            return;
        }

        // O contrato (Avallo.Connectors.Abstractions) nao casa com este padrao de nome
        // e deve permanecer fora da pasta: ele vem sempre do host.
        var assemblyPaths = Directory
            .EnumerateFiles(pluginPath, "Avallo.Connector.*.dll", SearchOption.TopDirectoryOnly)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var registered = 0;
        foreach (var assemblyPath in assemblyPaths)
        {
            try
            {
                var context = new PluginLoadContext(assemblyPath);
                var assembly = context.LoadFromAssemblyPath(assemblyPath);
                var moduleTypes = assembly.GetTypes().Where(x =>
                    typeof(IConnectorModule).IsAssignableFrom(x) && x is { IsAbstract: false, IsInterface: false });

                var found = 0;
                foreach (var moduleType in moduleTypes)
                {
                    var module = (IConnectorModule)Activator.CreateInstance(moduleType)!;
                    module.Register(services, configuration);
                    found++;
                    registered++;
                }

                if (found == 0)
                    logger?.LogWarning(
                        "O assembly '{Assembly}' esta na pasta de conectores mas nao expoe nenhum IConnectorModule. " +
                        "Se ele foi compilado contra outra versao do contrato, republique o plugin.",
                        Path.GetFileName(assemblyPath));
                else
                    logger?.LogInformation("Conector '{Assembly}' carregado ({Modules} modulo(s)).",
                        Path.GetFileName(assemblyPath), found);
            }
            catch (Exception exception) when (exception is ReflectionTypeLoadException or BadImageFormatException
                                                  or FileLoadException or MissingMethodException or TypeLoadException)
            {
                Fail(environment, logger,
                    $"Falha ao carregar o conector '{Path.GetFileName(assemblyPath)}'. " +
                    "A causa mais provavel e um DLL desatualizado, compilado contra outra versao de " +
                    "Avallo.Connectors.Abstractions. Apague a pasta de conectores e recompile a solucao.",
                    exception);
            }
        }

        if (registered == 0)
            Fail(environment, logger,
                $"Nenhum modulo de marketplace foi registrado a partir de '{pluginPath}' " +
                $"({assemblyPaths.Length} assembly(ies) inspecionado(s)). A aplicacao subiria sem conectores.");
    }

    /// <summary>
    /// Em desenvolvimento a falha e imediata: um painel de conectores vazio por erro de build
    /// e muito mais caro de diagnosticar depois. Em producao apenas registra, para nao derrubar
    /// um deploy inteiro por causa da camada de integracao.
    /// </summary>
    private static void Fail(IHostEnvironment environment, ILogger? logger, string message, Exception? exception = null)
    {
        if (environment.IsDevelopment())
            throw new InvalidOperationException(message, exception);
        logger?.LogCritical(exception, "{Message}", message);
    }

    /// <summary>
    /// Contexto isolado por plugin. Tipos compartilhados — o contrato, DI, configuracao e
    /// qualquer pacote que o host ja possua — sao resolvidos primeiro pelo contexto padrao.
    /// Sem isso, o plugin carregaria a sua propria copia de IServiceCollection e o tipo
    /// deixaria de implementar IConnectorModule aos olhos do host.
    /// </summary>
    private sealed class PluginLoadContext(string pluginPath) : AssemblyLoadContext(isCollectible: false)
    {
        private readonly AssemblyDependencyResolver _resolver = new(pluginPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            try
            {
                return Default.LoadFromAssemblyName(assemblyName);
            }
            catch (FileNotFoundException)
            {
                // O host nao tem esta dependencia: o plugin precisa trazer a propria copia.
            }

            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }
    }
}
