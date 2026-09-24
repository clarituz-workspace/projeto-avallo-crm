using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Avallo.Web.Domain;
using Avallo.Web.Features.Fiscal;
using Avallo.Web.Infrastructure;
using Xunit;

namespace Avallo.Tests.Features;

public sealed class FiscalFoundationTests
{
    [Fact]
    public async Task Brasil_api_client_caches_successful_lookup_by_cnpj()
    {
        const string json = """{"cnpj":"19131243000197","razao_social":"Cached LTDA","nome_fantasia":null,"descricao_situacao_cadastral":"ATIVA","porte":"ME","logradouro":"Rua A","numero":"1","complemento":null,"bairro":"Centro","municipio":"Sao Paulo","uf":"SP","cep":"01001000","cnae_fiscal":4781400,"cnae_fiscal_descricao":"Comercio","cnaes_secundarios":[],"regime_tributario":[]}""";
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var client = new BrasilApiCnpjClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://brasilapi.com.br/")
        }, cache);

        await client.LookupAsync("19.131.243/0001-97", TestContext.Current.CancellationToken);
        await client.LookupAsync("19131243000197", TestContext.Current.CancellationToken);

        Assert.Equal(1, handler.RequestCount);
    }

    private static readonly DateTimeOffset DeliveredAt = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("19.131.243/0001-97", "19131243000197")]
    [InlineData("11.222.333/0001-81", "11222333000181")]
    public void Cnpj_validation_accepts_formatting_and_valid_check_digits(string value, string expected) =>
        Assert.Equal(expected, BrasilApiCnpjClient.ValidateAndNormalizeCnpj(value));

    [Theory]
    [InlineData("11.111.111/1111-11")]
    [InlineData("19.131.243/0001-98")]
    [InlineData("123")]
    [InlineData("abc19.131.243/0001-97")]
    public void Cnpj_validation_rejects_invalid_values(string value) =>
        Assert.Throws<ArgumentException>(() => BrasilApiCnpjClient.ValidateAndNormalizeCnpj(value));

    [Fact]
    public async Task Brasil_api_client_maps_snake_case_profile_data_without_qsa()
    {
        const string json = """
            {
              "cnpj":"19131243000197",
              "razao_social":"Fiscal Comercio LTDA",
              "nome_fantasia":"Fiscal Loja",
              "descricao_situacao_cadastral":"ATIVA",
              "porte":"MICRO EMPRESA",
              "logradouro":"Rua Um",
              "numero":"10",
              "complemento":"Sala 2",
              "bairro":"Centro",
              "municipio":"Sao Paulo",
              "uf":"SP",
              "cep":"01001000",
              "cnae_fiscal":4781400,
              "cnae_fiscal_descricao":"Comercio varejista",
              "cnaes_secundarios":[{"codigo":4751201,"descricao":"Comercio de equipamentos"}],
              "regime_tributario":[{"ano":2026,"forma_de_tributacao":"SIMPLES NACIONAL"}],
              "qsa":[{"nome_socio":"Must not be persisted"}]
            }
            """;
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        var client = new BrasilApiCnpjClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://brasilapi.com.br/")
        });

        var result = await client.LookupAsync("19.131.243/0001-97", TestContext.Current.CancellationToken);

        Assert.Equal("Fiscal Comercio LTDA", result.LegalName);
        Assert.Equal(4781400, result.MainCnaeCode);
        Assert.Equal("Comercio de equipamentos", Assert.Single(result.SecondaryCnaes).Description);
        Assert.Contains("Sao Paulo/SP", result.AddressSummary);
        Assert.Equal("SIMPLES NACIONAL", Assert.Single(result.TaxRegimeHistory!).TaxationForm);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Brasil_api_client_retries_transient_then_falls_back_to_cnpj_ws()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var client = new BrasilApiCnpjClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://brasilapi.com.br/")
        });

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.LookupAsync("19.131.243/0001-97", TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Equal(4, handler.RequestCount);
        Assert.Equal(3, handler.RequestUris.Count(uri => uri?.Host == "brasilapi.com.br"));
        Assert.Single(handler.RequestUris.Where(uri => uri?.Host == "publica.cnpj.ws"));
    }

    [Fact]
    public void Effective_rule_selection_uses_approved_latest_version_for_each_tax_code()
    {
        var profile = Profile(Guid.NewGuid());
        var oldRule = Rule(profile, "ICMS", 4, 1, TaxRuleStatuses.Approved, DeliveredAt.AddYears(-1));
        var currentRule = Rule(profile, "ICMS", 7.25m, 2, TaxRuleStatuses.Approved, DeliveredAt.AddDays(-1));
        var draftRule = Rule(profile, "PIS", 1.65m, 1, TaxRuleStatuses.Draft, DeliveredAt.AddDays(-1));
        var futureRule = Rule(profile, "COFINS", 3, 1, TaxRuleStatuses.Approved, DeliveredAt.AddDays(1));

        var selected = TaxEngine.SelectEffectiveRules(profile,
            [oldRule, currentRule, draftRule, futureRule], DeliveredAt).ToList();

        var rule = Assert.Single(selected);
        Assert.Equal(currentRule.Id, rule.Id);
    }

    [Fact]
    public async Task Delivered_order_posts_rounded_balanced_tax_once()
    {
        await using var fixture = await FiscalFixture.CreateAsync(100.05m, withConfiguration: true);

        await fixture.Engine.ProcessOrderAsync(fixture.Order.Id, TestContext.Current.CancellationToken);
        await fixture.Engine.ProcessOrderAsync(fixture.Order.Id, TestContext.Current.CancellationToken);

        var assessment = await fixture.Db.TaxAssessments.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(100.05m, assessment.TaxableBase);
        Assert.Equal(10.01m, assessment.TaxAmount);
        var ledger = await fixture.Db.AccountingEntries.Include(x => x.Postings)
            .SingleAsync(x => x.Type == AccountingEntryTypes.TaxAssessment, TestContext.Current.CancellationToken);
        Assert.Equal(10.01m, ledger.Postings.Single(x => x.AccountCode == AccountingAccounts.TaxOnSales).Debit);
        Assert.Equal(10.01m, ledger.Postings.Single(x => x.AccountCode == AccountingAccounts.TaxesPayable).Credit);
        Assert.Equal(ledger.Postings.Sum(x => x.Debit), ledger.Postings.Sum(x => x.Credit));
    }

    [Fact]
    public async Task Returned_order_reverses_exact_assessment_once()
    {
        await using var fixture = await FiscalFixture.CreateAsync(100m, withConfiguration: true);
        await fixture.Engine.ProcessOrderAsync(fixture.Order.Id, TestContext.Current.CancellationToken);
        fixture.Order.FulfillmentStatus = "Returned";
        await fixture.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await fixture.Engine.ProcessOrderAsync(fixture.Order.Id, TestContext.Current.CancellationToken);
        await fixture.Engine.ProcessOrderAsync(fixture.Order.Id, TestContext.Current.CancellationToken);

        var assessments = await fixture.Db.TaxAssessments.OrderBy(x => x.AssessedAt)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, assessments.Count);
        var original = assessments.Single(x => x.Type == TaxAssessmentTypes.Assessment);
        var reversal = assessments.Single(x => x.Type == TaxAssessmentTypes.Reversal);
        Assert.Equal(original.Id, reversal.ReversesAssessmentId);
        Assert.Equal(-original.TaxAmount, reversal.TaxAmount);
        var ledger = await fixture.Db.AccountingEntries.Include(x => x.Postings)
            .SingleAsync(x => x.Type == AccountingEntryTypes.TaxReversal, TestContext.Current.CancellationToken);
        Assert.Equal(original.TaxAmount, ledger.Postings.Single(x => x.AccountCode == AccountingAccounts.TaxesPayable).Debit);
        Assert.Equal(original.TaxAmount, ledger.Postings.Single(x => x.AccountCode == AccountingAccounts.TaxOnSales).Credit);
    }

    [Fact]
    public async Task Missing_configuration_creates_one_issue_and_does_not_post()
    {
        await using var fixture = await FiscalFixture.CreateAsync(100m, withConfiguration: false);

        var first = await fixture.Engine.ProcessOrderAsync(fixture.Order.Id, TestContext.Current.CancellationToken);
        var second = await fixture.Engine.ProcessOrderAsync(fixture.Order.Id, TestContext.Current.CancellationToken);

        Assert.Equal(TaxReconciliationIssueTypes.MissingProfile, first.Issue?.Type);
        Assert.Equal(first.Issue?.Id, second.Issue?.Id);
        Assert.Equal(1, await fixture.Db.TaxReconciliationIssues.CountAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await fixture.Db.TaxAssessments.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await fixture.Db.AccountingEntries.ToListAsync(TestContext.Current.CancellationToken));
    }

    private static TaxProfile Profile(Guid tenantId) => new()
    {
        TenantId = tenantId,
        Version = 1,
        EffectiveFrom = DeliveredAt.AddYears(-1),
        Cnpj = "19131243000197",
        LegalName = "Fiscal Comercio LTDA",
        RegistrationStatus = "ATIVA",
        CompanySize = "ME",
        AddressSummary = "Sao Paulo/SP",
        MainCnaeCode = "4781400",
        MainCnaeDescription = "Comercio varejista",
        TaxRegime = TaxRegime.SimplesNacional,
        SourceLookedUpAt = DeliveredAt.AddYears(-1)
    };

    private static TaxRule Rule(
        TaxProfile profile,
        string code,
        decimal rate,
        int version,
        string status,
        DateTimeOffset effectiveFrom) => new()
    {
        TenantId = profile.TenantId,
        TaxProfileId = profile.Id,
        Version = version,
        EffectiveFrom = effectiveFrom,
        TaxCode = code,
        TaxName = code,
        Rate = rate,
        Status = status,
        CreatedByUserId = Guid.NewGuid()
    };

    private sealed class FiscalFixture : IAsyncDisposable
    {
        private FiscalFixture(AppDbContext db, TaxEngine engine, MarketplaceOrder order)
        {
            Db = db;
            Engine = engine;
            Order = order;
        }

        public AppDbContext Db { get; }
        public TaxEngine Engine { get; }
        public MarketplaceOrder Order { get; }

        public static async Task<FiscalFixture> CreateAsync(decimal grossValue, bool withConfiguration)
        {
            var tenantId = Guid.NewGuid();
            var tenant = new StubTenantContext(tenantId);
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant);
            var order = new MarketplaceOrder
            {
                TenantId = tenantId,
                ConnectionId = Guid.NewGuid(),
                OrderId = "ORDER-TAX",
                Platform = "test-marketplace",
                SaleDate = DeliveredAt.AddDays(-1),
                GrossValue = grossValue,
                NetValue = grossValue,
                PaymentMethod = "Pix",
                Status = "Paid",
                FulfillmentStatus = "Delivered",
                DeliveredAt = DeliveredAt,
                BuyerName = "Buyer"
            };
            db.MarketplaceOrders.Add(order);
            if (withConfiguration)
            {
                var profile = Profile(tenantId);
                db.TaxProfiles.Add(profile);
                db.TaxRules.Add(Rule(profile, "DAS", 10, 1, TaxRuleStatuses.Approved, DeliveredAt.AddMonths(-1)));
            }
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            var time = new FixedTimeProvider(DeliveredAt.AddDays(1));
            return new FiscalFixture(db, new TaxEngine(db, tenant, time), order);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed record StubTenantContext(Guid? TenantId) : ITenantContext;

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public List<Uri?> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestUris.Add(request.RequestUri);
            return Task.FromResult(response(request));
        }
    }
}
