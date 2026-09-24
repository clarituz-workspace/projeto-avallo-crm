namespace Avallo.Client.Models;

public sealed record ReconciliationOverviewModel(DateOnly From, DateOnly To, decimal ReleasedAmount,
    decimal MatchedAmount, decimal UnmatchedAmount, int UnmatchedCount,
    ReconciliationTransactionModel[] Transactions, ReconciliationImportModel[] Imports);
public sealed record ReconciliationTransactionModel(Guid Id, DateTimeOffset OccurredAt, decimal Amount,
    string Currency, string Description, string? Reference, string Status, string? ReviewNote,
    ReconciliationPaymentSuggestionModel[] Suggestions, ReconciliationAllocationModel[] Allocations);
public sealed record ReconciliationPaymentSuggestionModel(Guid MarketplacePaymentId, string PaymentId,
    string OrderId, string Platform, decimal AvailableAmount, string Currency, DateTimeOffset? ReleaseAt, int Score);
public sealed record ReconciliationAllocationModel(Guid Id, Guid MarketplacePaymentId, decimal Amount,
    string MatchMethod, DateTimeOffset ConfirmedAt);
public sealed record ReconciliationImportModel(Guid Id, string Source, string OriginalFileName,
    string? AccountReference, DateOnly PeriodStart, DateOnly PeriodEnd, int TransactionCount, DateTimeOffset ImportedAt);
public sealed record ReconciliationAllocationInput(Guid MarketplacePaymentId, decimal Amount);
public sealed record ReconciliationActionModel(Guid TransactionId, string Status);
