namespace Avallo.Client.Models;

public sealed record ConnectorCredentialFieldModel(string Name, string Label, bool Secret, bool Required);

/// <summary>Espelho de <c>ConnectorPresentation</c>: a UI so conhece o contrato, nunca a plataforma.</summary>
public sealed record ConnectorPresentationModel(
    string Tagline, string? LogoUrl = null, string? BannerLogoUrl = null,
    string? BannerHeadline = null, string? BannerSubtitle = null,
    string? AccentFrom = null, string? AccentTo = null, string? SoundUrl = null);

public sealed record ConnectorDescriptorModel(
    string Name, string DisplayName, string Version, bool SupportsInvoices,
    ConnectorCredentialFieldModel[]? CredentialFields, bool UsesOAuth,
    ConnectorPresentationModel? Presentation = null,
    bool IsConfigured = true,
    string? ConfigurationHint = null);
public sealed record MarketplaceConnectionModel(
    Guid Id, string ConnectorName, string ExternalAccountId, string? AccountDisplayName,
    string Status, string? StatusMessage, DateTimeOffset TokenExpiresAt, DateTimeOffset? LastSyncAt);
public sealed record SyncResultModel(int ProcessedOrders, DateTimeOffset CompletedAt, bool Queued = false);
public sealed record OAuthStartModel(string AuthorizationUrl);
