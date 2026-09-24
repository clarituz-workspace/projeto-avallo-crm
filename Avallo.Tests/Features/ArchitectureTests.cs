using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using Avallo.Connector.Amazon;
using Avallo.Connector.MercadoLivre;
using Avallo.Connector.Shopee;
using Avallo.Connectors.Abstractions;
using Avallo.Web.Features.Expenses;
using Avallo.Web.Infrastructure;
using Microsoft.Extensions.Hosting;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Avallo.Tests.Features;

/// <summary>
/// Regras de arquitetura do monolito modular (estilo ArchUnit, via ArchUnitNET).
/// Travam as fronteiras observadas hoje: plugins de conector veem apenas o contrato,
/// o client WASM nao toca servidor/banco, e o dominio nao conhece infraestrutura.
/// </summary>
public sealed class ArchitectureTests
{
    private static readonly Architecture Architecture = new ArchLoader()
        .LoadAssemblies(
            typeof(AppDbContext).Assembly,
            typeof(Avallo.Client.Services.AccountingService).Assembly,
            typeof(IConnectorModule).Assembly,
            typeof(MercadoLivreConnector).Assembly,
            typeof(ShopeeConnector).Assembly,
            typeof(AmazonConnector).Assembly)
        .Build();

    private static IObjectProvider<IType> PersistenceAndInfrastructure() =>
        Types().That().ResideInNamespaceMatching(@"Microsoft\.EntityFrameworkCore.*")
            .Or().ResideInNamespaceMatching(@"Npgsql.*")
            .Or().ResideInNamespaceMatching(@"Azure\..*")
            .Or().ResideInNamespaceMatching(@"Amazon\..*");

    private static IObjectProvider<IType> ServerFrameworks() =>
        Types().That().ResideInNamespaceMatching(@"Microsoft\.AspNetCore.*");

    [Fact]
    public void Architecture_snapshot_loads_all_product_assemblies()
    {
        Assert.True(Architecture.Classes.Count() > 300,
            $"Arquitetura carregou apenas {Architecture.Classes.Count()} classes — regras abaixo seriam vacuas.");
    }

    private static void Check(IArchRule rule)
    {
        var failures = rule.Evaluate(Architecture)
            .Select(result => result.ToString())
            .Where(line => line.Contains("fail"))
            .ToList();
        Assert.True(rule.HasNoViolations(Architecture),
            $"Violacoes de arquitetura:\n{string.Join("\n", failures)}");
    }

    [Fact]
    public void Connector_plugins_only_see_the_contract_assembly()
    {
        Check(Classes().That().ResideInNamespaceMatching(@"Avallo\.Connector\..*")
            .Should().NotDependOnAny(Types().That().ResideInNamespaceMatching(@"Avallo\.Web.*"))
            .AndShould().NotDependOnAny(Types().That().ResideInNamespaceMatching(@"Avallo\.Client.*"))
            .AndShould().NotDependOnAny(PersistenceAndInfrastructure())
            .AndShould().NotDependOnAny(ServerFrameworks()));
    }

    [Fact]
    public void Connector_abstractions_do_not_depend_on_infrastructure()
    {
        Check(Classes().That().ResideInAssembly(typeof(IConnectorModule).Assembly)
            .Should().NotDependOnAny(PersistenceAndInfrastructure())
            .AndShould().NotDependOnAny(ServerFrameworks())
            .AndShould().NotDependOnAny(Types().That().ResideInNamespaceMatching(@"Avallo\.(Web|Client)\..*")));
    }

    [Fact]
    public void Wasm_client_does_not_depend_on_server_or_infrastructure()
    {
        Check(Classes().That().ResideInNamespaceMatching(@"Avallo\.Client\..*")
            .Should().NotDependOnAny(Types().That().ResideInNamespaceMatching(@"Avallo\.Web.*"))
            .AndShould().NotDependOnAny(PersistenceAndInfrastructure())
            .AndShould().NotDependOnAny(ServerFrameworks()));
    }

    [Fact]
    public void Feature_modules_do_not_depend_on_client_code()
    {
        Check(Classes().That().ResideInNamespaceMatching(@"Avallo\.Web\.Features\..*")
            .Should().NotDependOnAny(Types().That().ResideInNamespaceMatching(@"Avallo\.Client\..*")));
    }

    [Fact]
    public void Domain_does_not_depend_on_persistence_or_cloud_sdks()
    {
        Check(Classes().That().ResideInNamespaceMatching(@"Avallo\.Web\.Domain.*")
            .Should().NotDependOnAny(PersistenceAndInfrastructure())
            .AndShould().NotDependOnAny(Types().That().ResideInNamespaceMatching(
                @"Microsoft\.AspNetCore\.(?!Identity).*")));
    }

    [Fact]
    public void Endpoint_classes_are_static_and_live_in_feature_modules()
    {
        Check(Classes().That().HaveNameEndingWith("Endpoints")
            .Should().ResideInNamespaceMatching(@"Avallo\.Web\.Features\..*"));
        // Classe static em IL e abstract+sealed: se nao e abstract, nao e static.
        Check(Classes().That().HaveNameEndingWith("Endpoints").And().AreNotAbstract()
            .Should().NotExist());
    }

    [Fact]
    public void Endpoint_handlers_do_not_return_domain_entities()
    {
        Check(MethodMembers().That()
            .AreDeclaredIn(Classes().That().ResideInNamespaceMatching(@"Avallo\.Web\.Features\..*")
                .And().HaveNameEndingWith("Endpoints"))
            .Should().NotHaveReturnType(
                Types().That().ResideInNamespaceMatching(@"Avallo\.Web\.Domain.*")));
    }

    [Fact]
    public void Domain_types_are_not_named_like_api_contracts()
    {
        Check(Classes().That().ResideInNamespaceMatching(@"Avallo\.Web\.Domain.*")
            .And().HaveNameMatching(@".*(Request|Response|Dto|Contract)$")
            .Should().NotExist());
    }

    [Fact]
    public void Workers_inherit_background_service()
    {
        Check(Classes().That().ResideInNamespaceMatching(@"Avallo\.Web\..*")
            .And().HaveNameEndingWith("Worker")
            .And().AreNotAssignableTo(typeof(BackgroundService))
            .Should().NotExist());
    }

    [Fact]
    public void Services_live_inside_feature_modules()
    {
        Check(Classes().That().ResideInNamespaceMatching(@"Avallo\.Web\..*")
            .And().HaveNameEndingWith("Service")
            .And().DoNotResideInNamespaceMatching(@"Avallo\.Web\.Features\..*")
            .Should().NotExist());
    }

    [Fact]
    public void Storage_implementations_follow_the_storage_suffix()
    {
        Check(Classes().That().ImplementInterface(typeof(IExpenseStorage))
            .And().DoNotHaveNameEndingWith("Storage")
            .Should().NotExist());
    }
}
