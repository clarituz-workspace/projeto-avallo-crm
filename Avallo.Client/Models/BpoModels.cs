namespace Avallo.Client.Models;

public sealed record BpoPeriodItemModel(
    Guid TenantId, string TenantName, Guid PeriodId, int Year, int Month,
    string Status, DateTimeOffset? ValidatedAt, decimal? Result);
public sealed record BpoDashboardModel(
    BpoPeriodItemModel[] Periods, int AwaitingReview, int ReadyToClose);
public sealed record BpoBatchRequestModel(Guid[] PeriodIds, string Action);
public sealed record BpoBatchItemResultModel(Guid PeriodId, bool Succeeded, string? Error);
public sealed record BpoBatchResultModel(BpoBatchItemResultModel[] Items, int Succeeded, int Failed);
