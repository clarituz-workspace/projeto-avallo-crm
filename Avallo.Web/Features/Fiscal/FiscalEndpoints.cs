using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Avallo.Web.Domain;
using Avallo.Web.Features.Auth;
using Avallo.Web.Infrastructure;

namespace Avallo.Web.Features.Fiscal;

public static class FiscalEndpoints
{
    public static IEndpointRouteBuilder MapFiscalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/fiscal").WithTags("Fiscal")
            .RequireAuthorization(Policies.TenantMember);
        group.MapGet("/overview", GetOverviewAsync);
        group.MapGet("/cnpj/{cnpj}", LookupCnpjAsync).RequireAuthorization(Policies.CanWrite);
        group.MapPost("/profiles", CreateProfileAsync).RequireAuthorization(Policies.CanWrite);
        group.MapPost("/rules", CreateRuleAsync).RequireAuthorization(Policies.CanWrite);
        group.MapPost("/rules/{id:guid}/submit", SubmitRuleAsync).RequireAuthorization(Policies.CanWrite);
        group.MapPost("/rules/{id:guid}/approve", ApproveRuleAsync).RequireAuthorization(Policies.CanReviewAccounting);
        group.MapPost("/rules/{id:guid}/reject", RejectRuleAsync).RequireAuthorization(Policies.CanReviewAccounting);
        group.MapPost("/simulate", SimulateAsync);
        group.MapPost("/reprocess", ReprocessAsync).RequireAuthorization(Policies.CanWrite);
        return endpoints;
    }

    private static async Task<FiscalOverviewResponse> GetOverviewAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var profiles = await db.TaxProfiles.AsNoTracking().Include(x => x.SecondaryCnaes)
            .OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Version)
            .ToArrayAsync(cancellationToken);
        var rules = await db.TaxRules.AsNoTracking().OrderByDescending(x => x.EffectiveFrom)
            .ThenBy(x => x.TaxCode).ToArrayAsync(cancellationToken);
        var issueData = await (
            from issue in db.TaxReconciliationIssues.AsNoTracking()
            join order in db.MarketplaceOrders.AsNoTracking() on issue.MarketplaceOrderId equals order.Id
            where issue.ResolvedAt == null
            orderby issue.CreatedAt descending
            select new { issue.Id, issue.Type, issue.Details, issue.CreatedAt, order.OrderId, order.Platform })
            .ToArrayAsync(cancellationToken);
        return new FiscalOverviewResponse(
            profiles.Select(ProfileResponse).ToArray(), rules.Select(RuleResponse).ToArray(),
            issueData.Select(x => new TaxIssueResponse(
                x.Id, x.Type, x.Details, x.OrderId, x.Platform, x.CreatedAt)).ToArray());
    }

    private static async Task<IResult> LookupCnpjAsync(
        string cnpj, BrasilApiCnpjClient brasilApi, CancellationToken cancellationToken)
    {
        try { return Results.Ok(ToLookupResponse(await brasilApi.LookupAsync(cnpj, cancellationToken))); }
        catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["cnpj"] = [exception.Message] }); }
        catch (HttpRequestException exception) { return Results.Problem(exception.Message, statusCode:
            exception.StatusCode == System.Net.HttpStatusCode.TooManyRequests ? StatusCodes.Status429TooManyRequests : StatusCodes.Status502BadGateway); }
    }

    private static async Task<IResult> CreateProfileAsync(
        CreateTaxProfileRequest request,
        BrasilApiCnpjClient brasilApi,
        AppDbContext db,
        ITenantContext tenantContext,
        TimeProvider timeProvider,
        TaxEngine taxEngine,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<TaxRegime>(request.TaxRegime, true, out var regime))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["taxRegime"] = ["Invalid tax regime."] });
        BrasilApiCnpjResult lookup;
        try { lookup = await brasilApi.LookupAsync(request.Cnpj, cancellationToken); }
        catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["cnpj"] = [exception.Message] }); }
        catch (HttpRequestException exception) { return Results.Problem(exception.Message, statusCode:
            exception.StatusCode == System.Net.HttpStatusCode.TooManyRequests ? StatusCodes.Status429TooManyRequests : StatusCodes.Status502BadGateway); }

        var effectiveFrom = new DateTimeOffset(request.EffectiveFrom.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var latest = await db.TaxProfiles.OrderByDescending(x => x.EffectiveFrom).FirstOrDefaultAsync(cancellationToken);
        if (latest is not null && effectiveFrom <= latest.EffectiveFrom)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["effectiveFrom"] = ["A new profile must start after the latest profile."] });
        if (latest is not null && latest.EffectiveTo is null) latest.EffectiveTo = effectiveFrom;
        var version = (await db.TaxProfiles.MaxAsync(x => (int?)x.Version, cancellationToken) ?? 0) + 1;
        var profile = new TaxProfile
        {
            TenantId = tenantContext.TenantId!.Value,
            Version = version,
            EffectiveFrom = effectiveFrom,
            Cnpj = BrasilApiCnpjClient.ValidateAndNormalizeCnpj(lookup.Cnpj),
            LegalName = lookup.LegalName,
            TradeName = lookup.TradeName,
            RegistrationStatus = lookup.RegistrationStatus,
            CompanySize = lookup.CompanySize,
            AddressSummary = lookup.AddressSummary,
            MainCnaeCode = lookup.MainCnaeCode.ToString(),
            MainCnaeDescription = lookup.MainCnaeDescription,
            TaxRegime = regime,
            SourceLookedUpAt = timeProvider.GetUtcNow()
        };
        foreach (var cnae in lookup.SecondaryCnaes ?? [])
            profile.SecondaryCnaes.Add(new TaxProfileSecondaryCnae
            {
                TenantId = profile.TenantId, TaxProfileId = profile.Id,
                Code = cnae.Code.ToString(), Description = cnae.Description
            });
        db.TaxProfiles.Add(profile);
        await db.SaveChangesAsync(cancellationToken);
        await taxEngine.ReprocessOpenIssuesAsync(cancellationToken);
        return Results.Ok(ProfileResponse(profile));
    }

    private static async Task<IResult> CreateRuleAsync(
        CreateTaxRuleRequest request, ClaimsPrincipal user, AppDbContext db,
        ITenantContext tenantContext, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TaxCode) || string.IsNullOrWhiteSpace(request.TaxName) ||
            request.Rate is <= 0 or > 100)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["rule"] = ["Tax code, name and a rate between 0 and 100 are required."] });
        var profile = await db.TaxProfiles.SingleOrDefaultAsync(x => x.Id == request.TaxProfileId, cancellationToken);
        if (profile is null) return Results.NotFound(new { message = "Tax profile was not found." });
        var effectiveFrom = new DateTimeOffset(request.EffectiveFrom.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        if (effectiveFrom < profile.EffectiveFrom || profile.EffectiveTo is { } end && effectiveFrom >= end)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["effectiveFrom"] = ["Rule date must be inside the profile period."] });
        var code = request.TaxCode.Trim().ToUpperInvariant();
        var version = (await db.TaxRules.Where(x => x.TaxProfileId == profile.Id && x.TaxCode == code)
            .MaxAsync(x => (int?)x.Version, cancellationToken) ?? 0) + 1;
        var rule = new TaxRule
        {
            TenantId = tenantContext.TenantId!.Value, TaxProfileId = profile.Id, Version = version,
            EffectiveFrom = effectiveFrom, TaxCode = code, TaxName = request.TaxName.Trim(),
            Rate = decimal.Round(request.Rate, 6, MidpointRounding.AwayFromZero),
            CreatedByUserId = UserId(user)
        };
        db.TaxRules.Add(rule);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(RuleResponse(rule));
    }

    private static async Task<IResult> SubmitRuleAsync(Guid id, AppDbContext db, CancellationToken cancellationToken)
    {
        var rule = await db.TaxRules.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (rule is null) return Results.NotFound();
        if (rule.Status is not (TaxRuleStatuses.Draft or TaxRuleStatuses.Rejected))
            return Results.Conflict(new { message = "Rule cannot be submitted in its current status." });
        rule.Status = TaxRuleStatuses.PendingReview;
        rule.ReviewNotes = null;
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(RuleResponse(rule));
    }

    private static async Task<IResult> ApproveRuleAsync(
        Guid id, ClaimsPrincipal user, AppDbContext db, TaxEngine taxEngine,
        TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var rule = await db.TaxRules.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (rule is null) return Results.NotFound();
        if (rule.Status != TaxRuleStatuses.PendingReview)
            return Results.Conflict(new { message = "Only rules pending review can be approved." });
        var prior = await db.TaxRules.Where(x => x.TaxProfileId == rule.TaxProfileId && x.TaxCode == rule.TaxCode &&
                                               x.Status == TaxRuleStatuses.Approved && x.EffectiveFrom <= rule.EffectiveFrom &&
                                               x.EffectiveTo == null).ToArrayAsync(cancellationToken);
        foreach (var item in prior) item.EffectiveTo = rule.EffectiveFrom;
        rule.Status = TaxRuleStatuses.Approved;
        rule.ReviewedByUserId = UserId(user);
        rule.ReviewedAt = timeProvider.GetUtcNow();
        rule.ReviewNotes = null;
        await db.SaveChangesAsync(cancellationToken);
        await taxEngine.ReprocessOpenIssuesAsync(cancellationToken);
        return Results.Ok(RuleResponse(rule));
    }

    private static async Task<IResult> RejectRuleAsync(
        Guid id, ReviewTaxRuleRequest request, ClaimsPrincipal user, AppDbContext db,
        TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Notes))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["notes"] = ["Review notes are required."] });
        var rule = await db.TaxRules.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (rule is null) return Results.NotFound();
        if (rule.Status != TaxRuleStatuses.PendingReview)
            return Results.Conflict(new { message = "Only rules pending review can be rejected." });
        rule.Status = TaxRuleStatuses.Rejected;
        rule.ReviewedByUserId = UserId(user);
        rule.ReviewedAt = timeProvider.GetUtcNow();
        rule.ReviewNotes = request.Notes.Trim();
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(RuleResponse(rule));
    }

    private static async Task<IResult> SimulateAsync(
        TaxSimulationRequest request, AppDbContext db, CancellationToken cancellationToken)
    {
        if (request.Amount < 0) return Results.ValidationProblem(new Dictionary<string, string[]> { ["amount"] = ["Amount cannot be negative."] });
        var occurredAt = new DateTimeOffset(request.Date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var profile = await db.TaxProfiles.AsNoTracking().Where(x => x.EffectiveFrom <= occurredAt &&
            (x.EffectiveTo == null || x.EffectiveTo > occurredAt)).OrderByDescending(x => x.Version).FirstOrDefaultAsync(cancellationToken);
        if (profile is null) return Results.NotFound(new { message = "No fiscal profile is effective on this date." });
        var rules = await db.TaxRules.AsNoTracking().Where(x => x.TaxProfileId == profile.Id &&
            x.Status == TaxRuleStatuses.Approved).ToArrayAsync(cancellationToken);
        return Results.Ok(new TaxSimulationResponse(request.Amount,
            TaxEngine.Simulate(request.Amount, occurredAt, profile, rules).Select(x =>
                new TaxSimulationLineResponse(x.TaxCode, x.TaxName, x.TaxableBase, x.Rate, x.TaxAmount)).ToArray()));
    }

    private static async Task<ReprocessTaxResponse> ReprocessAsync(TaxEngine engine, CancellationToken cancellationToken) =>
        new(await engine.ReprocessOpenIssuesAsync(cancellationToken));

    private static Guid UserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static TaxProfileResponse ProfileResponse(TaxProfile profile) => new(
        profile.Id, profile.Version, profile.EffectiveFrom, profile.EffectiveTo, profile.Cnpj,
        profile.LegalName, profile.TradeName, profile.RegistrationStatus, profile.CompanySize,
        profile.AddressSummary, profile.MainCnaeCode, profile.MainCnaeDescription,
        profile.TaxRegime.ToString(), profile.SourceLookedUpAt,
        profile.SecondaryCnaes.Select(x => new CnaeResponse(x.Code, x.Description)).ToArray());
    private static TaxRuleResponse RuleResponse(TaxRule rule) => new(
        rule.Id, rule.TaxProfileId, rule.Version, rule.EffectiveFrom, rule.EffectiveTo,
        rule.TaxCode, rule.TaxName, rule.Rate, rule.Status, rule.ReviewNotes, rule.CreatedAt, rule.ReviewedAt);
    private static CnpjLookupResponse ToLookupResponse(BrasilApiCnpjResult value) => new(
        value.Cnpj, value.LegalName, value.TradeName, value.RegistrationStatus, value.CompanySize,
        value.AddressSummary, value.MainCnaeCode.ToString(), value.MainCnaeDescription,
        (value.SecondaryCnaes ?? []).Select(x => new CnaeResponse(x.Code.ToString(), x.Description)).ToArray(),
        (value.TaxRegimeHistory ?? []).Select(x => new BrasilApiRegimeResponse(x.Year, x.TaxationForm)).ToArray());
}

public sealed record FiscalOverviewResponse(TaxProfileResponse[] Profiles, TaxRuleResponse[] Rules, TaxIssueResponse[] Issues);
public sealed record CnpjLookupResponse(string Cnpj, string LegalName, string? TradeName, string RegistrationStatus,
    string CompanySize, string AddressSummary, string MainCnaeCode, string MainCnaeDescription,
    CnaeResponse[] SecondaryCnaes, BrasilApiRegimeResponse[] TaxRegimeHistory);
public sealed record CnaeResponse(string Code, string Description);
public sealed record BrasilApiRegimeResponse(int Year, string TaxationForm);
public sealed record CreateTaxProfileRequest(string Cnpj, string TaxRegime, DateOnly EffectiveFrom);
public sealed record TaxProfileResponse(Guid Id, int Version, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo,
    string Cnpj, string LegalName, string? TradeName, string RegistrationStatus, string CompanySize,
    string AddressSummary, string MainCnaeCode, string MainCnaeDescription, string TaxRegime,
    DateTimeOffset SourceLookedUpAt, CnaeResponse[] SecondaryCnaes);
public sealed record CreateTaxRuleRequest(Guid TaxProfileId, string TaxCode, string TaxName, decimal Rate, DateOnly EffectiveFrom);
public sealed record ReviewTaxRuleRequest(string Notes);
public sealed record TaxRuleResponse(Guid Id, Guid TaxProfileId, int Version, DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo, string TaxCode, string TaxName, decimal Rate, string Status,
    string? ReviewNotes, DateTimeOffset CreatedAt, DateTimeOffset? ReviewedAt);
public sealed record TaxIssueResponse(Guid Id, string Type, string Details, string OrderId, string Platform, DateTimeOffset CreatedAt);
public sealed record TaxSimulationRequest(decimal Amount, DateOnly Date);
public sealed record TaxSimulationResponse(decimal Amount, TaxSimulationLineResponse[] Lines);
public sealed record TaxSimulationLineResponse(string TaxCode, string TaxName, decimal TaxableBase, decimal Rate, decimal TaxAmount);
public sealed record ReprocessTaxResponse(int ProcessedOrders);
