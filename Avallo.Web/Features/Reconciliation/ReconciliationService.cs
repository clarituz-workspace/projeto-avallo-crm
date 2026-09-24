using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Avallo.Web.Domain;
using Avallo.Web.Features.Expenses;
using Avallo.Web.Infrastructure;

namespace Avallo.Web.Features.Reconciliation;

public sealed class ReconciliationService(
    AppDbContext db,
    ITenantContext tenantContext,
    IStatementParser parser,
    IExpenseStorage storage,
    TimeProvider timeProvider)
{
    public async Task<ReconciliationImport> ImportAsync(byte[] content, string fileName, string contentType,
        Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        var sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        if (await db.ReconciliationImports.AnyAsync(x => x.Sha256 == sha256, cancellationToken))
            throw new ReconciliationConflictException("Este extrato ja foi importado.");
        var parsed = parser.Parse(content, fileName);
        var importId = Guid.NewGuid();
        var safeExtension = parsed.Source == ReconciliationSources.Ofx ? ".ofx" : ".csv";
        var objectKey = $"tenants/{tenantId:N}/reconciliation/{parsed.PeriodStart:yyyy-MM}/{importId:N}{safeExtension}";
        var import = new ReconciliationImport
        {
            Id = importId,
            TenantId = tenantId,
            Source = parsed.Source,
            OriginalFileName = Limit(Path.GetFileName(fileName), 240),
            ObjectKey = objectKey,
            Sha256 = sha256,
            AccountReference = parsed.AccountReference is null ? null : Limit(parsed.AccountReference, 120),
            Currency = parsed.Currency,
            PeriodStart = parsed.PeriodStart,
            PeriodEnd = parsed.PeriodEnd,
            ImportedByUserId = actorUserId,
            ImportedAt = timeProvider.GetUtcNow()
        };
        foreach (var source in parsed.Transactions)
            import.Transactions.Add(new ReconciliationTransaction
            {
                TenantId = tenantId,
                ReconciliationImportId = import.Id,
                ExternalId = source.ExternalId,
                OccurredAt = source.OccurredAt,
                Amount = source.Amount,
                Currency = source.Currency,
                Description = source.Description,
                Reference = source.Reference,
                Status = source.Amount > 0 ? ReconciliationTransactionStatuses.Unmatched : ReconciliationTransactionStatuses.Ignored,
                ReviewNote = source.Amount > 0 ? null : "Debito bancario fora do escopo de repasses."
            });
        await using var stream = new MemoryStream(content, writable: false);
        await storage.PutAsync(objectKey, stream, contentType, cancellationToken);
        db.ReconciliationImports.Add(import);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return import;
        }
        catch
        {
            try { await storage.DeleteAsync(objectKey, CancellationToken.None); }
            catch { }
            throw;
        }
    }

    public async Task<ReconciliationOverviewResponse> GetOverviewAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (to < from || to.DayNumber - from.DayNumber > 366)
            throw new ArgumentException("O periodo deve ter no maximo 366 dias.");
        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var transactions = await db.ReconciliationTransactions.AsNoTracking()
            .Where(x => x.OccurredAt >= start && x.OccurredAt < end)
            .OrderByDescending(x => x.OccurredAt).ToArrayAsync(cancellationToken);
        var transactionIds = transactions.Select(x => x.Id).ToArray();
        var allocations = await db.ReconciliationAllocations.AsNoTracking()
            .Where(x => transactionIds.Contains(x.ReconciliationTransactionId)).ToArrayAsync(cancellationToken);
        var allConfirmedByPayment = await db.ReconciliationAllocations.AsNoTracking()
            .GroupBy(x => x.MarketplacePaymentId)
            .Select(x => new { PaymentId = x.Key, Amount = x.Sum(a => a.Amount) })
            .ToDictionaryAsync(x => x.PaymentId, x => x.Amount, cancellationToken);
        var candidateStart = start.AddDays(-7);
        var candidateEnd = end.AddDays(7);
        var payments = await (
            from payment in db.MarketplacePayments.AsNoTracking()
            join order in db.MarketplaceOrders.AsNoTracking() on payment.MarketplaceOrderId equals order.Id
            where payment.ReleaseAt >= candidateStart && payment.ReleaseAt < candidateEnd &&
                  payment.NetValue > 0 && payment.Status == "Paid"
            select new PaymentCandidateData(payment.Id, payment.PaymentId, payment.NetValue, payment.Currency,
                payment.ReleaseAt, order.OrderId, order.Platform)).ToArrayAsync(cancellationToken);
        var rows = transactions.Select(transaction =>
        {
            var transactionAllocations = allocations.Where(x => x.ReconciliationTransactionId == transaction.Id).ToArray();
            var suggestions = transaction.Status == ReconciliationTransactionStatuses.Unmatched && transaction.Amount > 0
                ? Suggestions(transaction, payments, allConfirmedByPayment)
                : [];
            return new ReconciliationTransactionResponse(transaction.Id, transaction.OccurredAt, transaction.Amount,
                transaction.Currency, transaction.Description, transaction.Reference, transaction.Status,
                transaction.ReviewNote, suggestions,
                transactionAllocations.Select(x => new ReconciliationAllocationResponse(
                    x.Id, x.MarketplacePaymentId, x.Amount, x.MatchMethod, x.ConfirmedAt)).ToArray());
        }).ToArray();
        var imports = await db.ReconciliationImports.AsNoTracking()
            .Where(x => x.PeriodEnd >= from && x.PeriodStart <= to).OrderByDescending(x => x.ImportedAt)
            .Select(x => new ReconciliationImportResponse(x.Id, x.Source, x.OriginalFileName, x.AccountReference,
                x.PeriodStart, x.PeriodEnd, x.Transactions.Count, x.ImportedAt)).ToArrayAsync(cancellationToken);
        var released = payments.Where(x => x.ReleaseAt >= start && x.ReleaseAt < end).Sum(x => x.NetValue);
        var matched = transactions.Where(x => x.Status == ReconciliationTransactionStatuses.Matched).Sum(x => x.Amount);
        return new ReconciliationOverviewResponse(from, to, released, matched,
            transactions.Where(x => x.Status == ReconciliationTransactionStatuses.Unmatched && x.Amount > 0).Sum(x => x.Amount),
            rows.Count(x => x.Status == ReconciliationTransactionStatuses.Unmatched && x.Amount > 0), rows, imports);
    }

    public async Task ConfirmAsync(Guid transactionId, IReadOnlyCollection<ReconciliationAllocationRequest> requested,
        Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        if (requested.Count == 0 || requested.Any(x => x.Amount <= 0) ||
            requested.Select(x => x.MarketplacePaymentId).Distinct().Count() != requested.Count)
            throw new ArgumentException("Informe pagamentos distintos com valores positivos.");
        var transaction = await db.ReconciliationTransactions.SingleAsync(x => x.Id == transactionId, cancellationToken);
        if (transaction.Status == ReconciliationTransactionStatuses.Matched)
        {
            var persisted = await db.ReconciliationAllocations.AsNoTracking()
                .Where(x => x.ReconciliationTransactionId == transaction.Id)
                .Select(x => new ReconciliationAllocationRequest(x.MarketplacePaymentId, x.Amount))
                .ToArrayAsync(cancellationToken);
            var sameRequest = persisted.Length == requested.Count && persisted.All(existing =>
                requested.Any(candidate => candidate.MarketplacePaymentId == existing.MarketplacePaymentId &&
                                           candidate.Amount == existing.Amount));
            if (sameRequest)
                return;
            throw new ReconciliationConflictException("Este credito ja foi conciliado com outras alocacoes.");
        }
        if (transaction.Status != ReconciliationTransactionStatuses.Unmatched || transaction.Amount <= 0)
            throw new ReconciliationConflictException("Somente creditos bancarios pendentes podem ser conciliados.");
        if (Math.Abs(requested.Sum(x => x.Amount) - transaction.Amount) > 0.01m)
            throw new ArgumentException("A soma das alocacoes deve ser igual ao credito bancario.");
        var paymentIds = requested.Select(x => x.MarketplacePaymentId).ToArray();
        var payments = await (
            from payment in db.MarketplacePayments
            join order in db.MarketplaceOrders on payment.MarketplaceOrderId equals order.Id
            where paymentIds.Contains(payment.Id)
            select new PaymentCandidateData(payment.Id, payment.PaymentId, payment.NetValue, payment.Currency,
                payment.ReleaseAt, order.OrderId, order.Platform)).ToArrayAsync(cancellationToken);
        if (payments.Length != paymentIds.Length)
            throw new ArgumentException("Um ou mais pagamentos nao foram encontrados.");
        var alreadyAllocated = await db.ReconciliationAllocations.Where(x => paymentIds.Contains(x.MarketplacePaymentId))
            .GroupBy(x => x.MarketplacePaymentId).Select(x => new { Id = x.Key, Amount = x.Sum(a => a.Amount) })
            .ToDictionaryAsync(x => x.Id, x => x.Amount, cancellationToken);
        foreach (var request in requested)
        {
            var payment = payments.Single(x => x.Id == request.MarketplacePaymentId);
            if (!string.Equals(payment.Currency, transaction.Currency, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("A moeda do pagamento difere da moeda do extrato.");
            if (request.Amount - (payment.NetValue - alreadyAllocated.GetValueOrDefault(payment.Id)) > 0.01m)
                throw new ReconciliationConflictException($"O pagamento {payment.PaymentId} nao possui saldo suficiente.");
        }
        var now = timeProvider.GetUtcNow();
        var platforms = payments.Select(x => x.Platform).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var accountingEntry = new AccountingEntry
        {
            TenantId = tenantId,
            EventKey = $"reconciliation:{transaction.Id}:confirmed",
            Type = AccountingEntryTypes.MarketplaceSettlement,
            SourceType = "ReconciliationTransaction",
            SourceId = transaction.Id.ToString("N"),
            Description = $"Conciliacao bancaria: {transaction.Description}",
            OccurredAt = transaction.OccurredAt,
            CreatedAt = now,
            Postings =
            [
                new AccountingPosting { TenantId = tenantId, AccountCode = AccountingAccounts.Bank,
                    AccountName = "Bancos", Marketplace = platforms.Length == 1 ? platforms[0] : "consolidado",
                    Currency = transaction.Currency, Debit = transaction.Amount },
                new AccountingPosting { TenantId = tenantId, AccountCode = AccountingAccounts.MarketplaceReceivable,
                    AccountName = "Valores a receber de marketplaces", Marketplace = platforms.Length == 1 ? platforms[0] : "consolidado",
                    Currency = transaction.Currency, Credit = transaction.Amount }
            ]
        };
        db.AccountingEntries.Add(accountingEntry);
        foreach (var request in requested)
        {
            var payment = payments.Single(x => x.Id == request.MarketplacePaymentId);
            db.ReconciliationAllocations.Add(new ReconciliationAllocation
            {
                TenantId = tenantId,
                ReconciliationTransactionId = transaction.Id,
                MarketplacePaymentId = payment.Id,
                Amount = request.Amount,
                MatchMethod = MatchMethod(transaction, payment, request.Amount),
                ConfirmedByUserId = actorUserId,
                ConfirmedAt = now,
                AccountingEntryId = accountingEntry.Id
            });
        }
        transaction.Status = ReconciliationTransactionStatuses.Matched;
        transaction.ReviewedByUserId = actorUserId;
        transaction.ReviewedAt = now;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task IgnoreAsync(Guid transactionId, string reason, Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Informe o motivo para ignorar a transacao.");
        var transaction = await db.ReconciliationTransactions.SingleAsync(x => x.Id == transactionId, cancellationToken);
        if (transaction.Status != ReconciliationTransactionStatuses.Unmatched)
            throw new ReconciliationConflictException("Somente transacoes pendentes podem ser ignoradas.");
        transaction.Status = ReconciliationTransactionStatuses.Ignored;
        transaction.ReviewNote = reason.Trim();
        transaction.ReviewedByUserId = actorUserId;
        transaction.ReviewedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }

    private static ReconciliationPaymentSuggestionResponse[] Suggestions(ReconciliationTransaction transaction,
        IEnumerable<PaymentCandidateData> payments, IReadOnlyDictionary<Guid, decimal> allocated) => payments
        .Select(payment => new { Payment = payment, Remaining = payment.NetValue - allocated.GetValueOrDefault(payment.Id) })
        .Where(x => x.Remaining > 0 && string.Equals(x.Payment.Currency, transaction.Currency, StringComparison.OrdinalIgnoreCase))
        .Select(x => new { x.Payment, x.Remaining, Score = Score(transaction, x.Payment, x.Remaining) })
        .Where(x => x.Score >= 10).OrderByDescending(x => x.Score)
        .ThenBy(x => Math.Abs(x.Remaining - transaction.Amount)).ThenBy(x => x.Payment.ReleaseAt)
        .Take(5).Select(x => new ReconciliationPaymentSuggestionResponse(x.Payment.Id, x.Payment.PaymentId,
            x.Payment.OrderId, x.Payment.Platform, x.Remaining, x.Payment.Currency, x.Payment.ReleaseAt, x.Score)).ToArray();

    private static int Score(ReconciliationTransaction transaction, PaymentCandidateData payment, decimal remaining)
    {
        var reference = $"{transaction.Reference} {transaction.Description}";
        var exactReference = reference.Contains(payment.PaymentId, StringComparison.OrdinalIgnoreCase) ||
                             reference.Contains(payment.OrderId, StringComparison.OrdinalIgnoreCase);
        var exactAmount = Math.Abs(remaining - transaction.Amount) <= 0.01m;
        var days = payment.ReleaseAt is null ? 99 : Math.Abs((payment.ReleaseAt.Value.Date - transaction.OccurredAt.Date).Days);
        return (exactReference ? 100 : 0) + (exactAmount ? 50 : 0) + (days <= 2 ? 20 : days <= 5 ? 10 : 0);
    }

    private static string MatchMethod(ReconciliationTransaction transaction, PaymentCandidateData payment, decimal amount)
    {
        var reference = $"{transaction.Reference} {transaction.Description}";
        if (reference.Contains(payment.PaymentId, StringComparison.OrdinalIgnoreCase) || reference.Contains(payment.OrderId, StringComparison.OrdinalIgnoreCase))
            return ReconciliationMatchMethods.ExactReference;
        return Math.Abs(payment.NetValue - amount) <= 0.01m ? ReconciliationMatchMethods.AmountAndDate : ReconciliationMatchMethods.Manual;
    }

    private Guid RequireTenant() => tenantContext.TenantId ?? throw new UnauthorizedAccessException("Tenant is required.");
    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];
    private sealed record PaymentCandidateData(Guid Id, string PaymentId, decimal NetValue, string Currency,
        DateTimeOffset? ReleaseAt, string OrderId, string Platform);
}

public sealed class ReconciliationConflictException(string message) : InvalidOperationException(message);
public sealed record ReconciliationAllocationRequest(Guid MarketplacePaymentId, decimal Amount);
public sealed record ReconciliationOverviewResponse(DateOnly From, DateOnly To, decimal ReleasedAmount,
    decimal MatchedAmount, decimal UnmatchedAmount, int UnmatchedCount,
    ReconciliationTransactionResponse[] Transactions, ReconciliationImportResponse[] Imports);
public sealed record ReconciliationTransactionResponse(Guid Id, DateTimeOffset OccurredAt, decimal Amount,
    string Currency, string Description, string? Reference, string Status, string? ReviewNote,
    ReconciliationPaymentSuggestionResponse[] Suggestions, ReconciliationAllocationResponse[] Allocations);
public sealed record ReconciliationPaymentSuggestionResponse(Guid MarketplacePaymentId, string PaymentId,
    string OrderId, string Platform, decimal AvailableAmount, string Currency, DateTimeOffset? ReleaseAt, int Score);
public sealed record ReconciliationAllocationResponse(Guid Id, Guid MarketplacePaymentId, decimal Amount,
    string MatchMethod, DateTimeOffset ConfirmedAt);
public sealed record ReconciliationImportResponse(Guid Id, string Source, string OriginalFileName,
    string? AccountReference, DateOnly PeriodStart, DateOnly PeriodEnd, int TransactionCount, DateTimeOffset ImportedAt);
