namespace Avallo.Client.Models;

public sealed record FiscalOverviewModel(TaxProfileModel[] Profiles, TaxRuleModel[] Rules, TaxIssueModel[] Issues);
public sealed record CnpjLookupModel(
    string Cnpj, string LegalName, string? TradeName, string RegistrationStatus, string CompanySize,
    string AddressSummary, string MainCnaeCode, string MainCnaeDescription,
    CnaeModel[] SecondaryCnaes, BrasilApiRegimeModel[] TaxRegimeHistory);
public sealed record CnaeModel(string Code, string Description);
public sealed record BrasilApiRegimeModel(int Year, string TaxationForm);
public sealed record TaxProfileModel(
    Guid Id, int Version, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo, string Cnpj,
    string LegalName, string? TradeName, string RegistrationStatus, string CompanySize,
    string AddressSummary, string MainCnaeCode, string MainCnaeDescription, string TaxRegime,
    DateTimeOffset SourceLookedUpAt, CnaeModel[] SecondaryCnaes);
public sealed record TaxRuleModel(
    Guid Id, Guid TaxProfileId, int Version, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo,
    string TaxCode, string TaxName, decimal Rate, string Status, string? ReviewNotes,
    DateTimeOffset CreatedAt, DateTimeOffset? ReviewedAt);
public sealed record TaxIssueModel(
    Guid Id, string Type, string Details, string OrderId, string Platform, DateTimeOffset CreatedAt);
public sealed record TaxSimulationModel(decimal Amount, TaxSimulationLineModel[] Lines);
public sealed record TaxSimulationLineModel(
    string TaxCode, string TaxName, decimal TaxableBase, decimal Rate, decimal TaxAmount);
public sealed record ReprocessTaxModel(int ProcessedOrders);
