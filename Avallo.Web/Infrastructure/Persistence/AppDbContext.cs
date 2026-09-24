using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Avallo.Web.Domain;

namespace Avallo.Web.Infrastructure;

public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options,
    ITenantContext tenantContext)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<FinancialEntry> FinancialEntries => Set<FinancialEntry>();
    public DbSet<UserNotification> Notifications => Set<UserNotification>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<EmailOutbox> EmailOutbox => Set<EmailOutbox>();
    public DbSet<MarketplaceConnection> MarketplaceConnections => Set<MarketplaceConnection>();
    public DbSet<MarketplaceOrder> MarketplaceOrders => Set<MarketplaceOrder>();
    public DbSet<MarketplaceOrderItem> MarketplaceOrderItems => Set<MarketplaceOrderItem>();
    public DbSet<MarketplacePayment> MarketplacePayments => Set<MarketplacePayment>();
    public DbSet<MarketplaceFee> MarketplaceFees => Set<MarketplaceFee>();
    public DbSet<AccountingEntry> AccountingEntries => Set<AccountingEntry>();
    public DbSet<AccountingPosting> AccountingPostings => Set<AccountingPosting>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<ExpenseAttachment> ExpenseAttachments => Set<ExpenseAttachment>();
    public DbSet<CustomExpenseCategory> CustomExpenseCategories => Set<CustomExpenseCategory>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<MarketplaceSkuMapping> MarketplaceSkuMappings => Set<MarketplaceSkuMapping>();
    public DbSet<SupplierInvoice> SupplierInvoices => Set<SupplierInvoice>();
    public DbSet<SupplierInvoiceItem> SupplierInvoiceItems => Set<SupplierInvoiceItem>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<InventoryReconciliationIssue> InventoryReconciliationIssues => Set<InventoryReconciliationIssue>();
    public DbSet<TaxProfile> TaxProfiles => Set<TaxProfile>();
    public DbSet<TaxProfileSecondaryCnae> TaxProfileSecondaryCnaes => Set<TaxProfileSecondaryCnae>();
    public DbSet<TaxRule> TaxRules => Set<TaxRule>();
    public DbSet<TaxAssessment> TaxAssessments => Set<TaxAssessment>();
    public DbSet<TaxReconciliationIssue> TaxReconciliationIssues => Set<TaxReconciliationIssue>();
    public DbSet<AccountingPeriod> AccountingPeriods => Set<AccountingPeriod>();
    public DbSet<AccountingPeriodCheck> AccountingPeriodChecks => Set<AccountingPeriodCheck>();
    public DbSet<DreSnapshot> DreSnapshots => Set<DreSnapshot>();
    public DbSet<ProfitDistributionAuthorization> ProfitDistributionAuthorizations => Set<ProfitDistributionAuthorization>();
    public DbSet<BpoTenantAssignment> BpoTenantAssignments => Set<BpoTenantAssignment>();
    public DbSet<ReconciliationImport> ReconciliationImports => Set<ReconciliationImport>();
    public DbSet<ReconciliationTransaction> ReconciliationTransactions => Set<ReconciliationTransaction>();
    public DbSet<ReconciliationAllocation> ReconciliationAllocations => Set<ReconciliationAllocation>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Tenant>(entity =>
        {
            entity.ToTable("Tenants");
            entity.Property(x => x.Name).HasMaxLength(160);
        });

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(x => x.DisplayName).HasMaxLength(160);
            entity.HasIndex(x => x.TenantId);
            entity.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("RefreshTokens");
            entity.Property(x => x.TokenHash).HasMaxLength(64).IsFixedLength();
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.UserId });
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<FinancialEntry>(entity =>
        {
            entity.ToTable("FinancialEntries");
            entity.Property(x => x.ExternalId).HasMaxLength(160);
            entity.Property(x => x.Description).HasMaxLength(300);
            entity.Property(x => x.Marketplace).HasMaxLength(80);
            entity.Property(x => x.PaymentMethod).HasMaxLength(80);
            entity.Property(x => x.Status).HasMaxLength(50);
            entity.Property(x => x.GrossAmount).HasPrecision(18, 2);
            entity.Property(x => x.ReceivedAmount).HasPrecision(18, 2);
            entity.Property(x => x.FeeAmount).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.TenantId, x.Marketplace, x.ExternalId }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.OccurredAt });
            entity.HasIndex(x => new { x.TenantId, x.Status });
            entity.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<UserNotification>(entity =>
        {
            entity.ToTable("Notifications");
            entity.Property(x => x.Type).HasMaxLength(60);
            entity.Property(x => x.EventKey).HasMaxLength(200);
            entity.Property(x => x.Title).HasMaxLength(180);
            entity.Property(x => x.Message).HasMaxLength(600);
            entity.Property(x => x.Link).HasMaxLength(300);
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.EventKey }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.IsRead, x.CreatedAt });
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<NotificationPreference>(entity =>
        {
            entity.ToTable("NotificationPreferences");
            entity.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
            entity.HasOne<ApplicationUser>().WithOne().HasForeignKey<NotificationPreference>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<EmailOutbox>(entity =>
        {
            entity.ToTable("EmailOutbox");
            entity.Property(x => x.EventKey).HasMaxLength(200);
            entity.Property(x => x.Recipient).HasMaxLength(320);
            entity.Property(x => x.Subject).HasMaxLength(200);
            entity.Property(x => x.AttachmentName).HasMaxLength(240);
            entity.Property(x => x.AttachmentContentType).HasMaxLength(120);
            entity.Property(x => x.AttachmentObjectKey).HasMaxLength(500);
            entity.Property(x => x.LastError).HasMaxLength(1000);
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.EventKey }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.SentAt, x.NextAttemptAt });
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<MarketplaceConnection>(entity =>
        {
            entity.ToTable("MarketplaceConnections");
            entity.Property(x => x.ConnectorName).HasMaxLength(80);
            entity.Property(x => x.ExternalAccountId).HasMaxLength(160);
            entity.Property(x => x.AccountDisplayName).HasMaxLength(160);
            entity.Property(x => x.EncryptedAccessToken).HasMaxLength(4000);
            entity.Property(x => x.EncryptedRefreshToken).HasMaxLength(4000);
            entity.Property(x => x.Status).HasMaxLength(30);
            entity.Property(x => x.StatusMessage).HasMaxLength(500);
            entity.HasIndex(x => new { x.TenantId, x.ConnectorName, x.ExternalAccountId }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.Status });
            entity.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<MarketplaceOrder>(entity =>
        {
            entity.ToTable("MarketplaceOrders");
            entity.Property(x => x.OrderId).HasMaxLength(160);
            entity.Property(x => x.Platform).HasMaxLength(80);
            entity.Property(x => x.PaymentMethod).HasMaxLength(80);
            entity.Property(x => x.Status).HasMaxLength(30);
            entity.Property(x => x.FulfillmentStatus).HasMaxLength(30);
            entity.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            entity.Property(x => x.BuyerName).HasMaxLength(200);
            entity.Property(x => x.InvoiceNumber).HasMaxLength(100);
            entity.Property(x => x.GrossValue).HasPrecision(18, 2);
            entity.Property(x => x.PlatformFee).HasPrecision(18, 2);
            entity.Property(x => x.NetValue).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.TenantId, x.Platform, x.OrderId }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.SaleDate });
            entity.HasIndex(x => new { x.TenantId, x.ConnectionId, x.OrderId });
            entity.HasOne<MarketplaceConnection>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<MarketplaceOrderItem>(entity =>
        {
            entity.ToTable("MarketplaceOrderItems");
            entity.Property(x => x.Sku).HasMaxLength(120);
            entity.Property(x => x.Title).HasMaxLength(300);
            entity.Property(x => x.UnitValue).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.TenantId, x.MarketplaceOrderId });
            entity.HasOne<MarketplaceOrder>().WithMany(x => x.Items).HasForeignKey(x => x.MarketplaceOrderId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<MarketplacePayment>(entity =>
        {
            entity.ToTable("MarketplacePayments");
            entity.Property(x => x.PaymentId).HasMaxLength(160);
            entity.Property(x => x.Method).HasMaxLength(80);
            entity.Property(x => x.Status).HasMaxLength(30);
            entity.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            entity.Property(x => x.GrossValue).HasPrecision(18, 2);
            entity.Property(x => x.NetValue).HasPrecision(18, 2);
            entity.Property(x => x.PaymentFee).HasPrecision(18, 2);
            entity.Property(x => x.PlatformFee).HasPrecision(18, 2);
            entity.Property(x => x.ShippingCost).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.TenantId, x.MarketplaceOrderId, x.PaymentId }).IsUnique();
            entity.HasOne<MarketplaceOrder>().WithMany().HasForeignKey(x => x.MarketplaceOrderId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<MarketplaceFee>(entity =>
        {
            entity.ToTable("MarketplaceFees");
            entity.Property(x => x.ExternalKey).HasMaxLength(200);
            entity.Property(x => x.Type).HasMaxLength(80);
            entity.Property(x => x.Category).HasMaxLength(50);
            entity.Property(x => x.Description).HasMaxLength(300);
            entity.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.TenantId, x.MarketplaceOrderId, x.ExternalKey }).IsUnique();
            entity.HasOne<MarketplaceOrder>().WithMany().HasForeignKey(x => x.MarketplaceOrderId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AccountingEntry>(entity =>
        {
            entity.ToTable("AccountingEntries");
            entity.Property(x => x.EventKey).HasMaxLength(240);
            entity.Property(x => x.Type).HasMaxLength(50);
            entity.Property(x => x.SourceType).HasMaxLength(50);
            entity.Property(x => x.SourceId).HasMaxLength(160);
            entity.Property(x => x.Description).HasMaxLength(400);
            entity.HasIndex(x => new { x.TenantId, x.EventKey }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.OccurredAt });
            entity.HasOne<AccountingEntry>().WithMany().HasForeignKey(x => x.ReversesEntryId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<AccountingPosting>(entity =>
        {
            entity.ToTable("AccountingPostings");
            entity.Property(x => x.AccountCode).HasMaxLength(30);
            entity.Property(x => x.AccountName).HasMaxLength(160);
            entity.Property(x => x.Marketplace).HasMaxLength(80);
            entity.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            entity.Property(x => x.Debit).HasPrecision(18, 2);
            entity.Property(x => x.Credit).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.TenantId, x.AccountCode, x.AccountingEntryId });
            entity.HasIndex(x => x.AccountingEntryId);
            entity.HasOne<AccountingEntry>().WithMany(x => x.Postings).HasForeignKey(x => x.AccountingEntryId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Expense>(entity =>
        {
            entity.ToTable("Expenses");
            entity.Property(x => x.Description).HasMaxLength(300);
            entity.Property(x => x.Category).HasMaxLength(50);
            entity.Property(x => x.Supplier).HasMaxLength(200);
            entity.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            entity.Property(x => x.Status).HasMaxLength(30);
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.Property(x => x.RejectionReason).HasMaxLength(600);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.TenantId, x.CompetenceDate });
            entity.HasIndex(x => new { x.TenantId, x.Status });
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ReviewedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ExpenseAttachment>(entity =>
        {
            entity.ToTable("ExpenseAttachments");
            entity.Property(x => x.ObjectKey).HasMaxLength(500);
            entity.Property(x => x.FileName).HasMaxLength(240);
            entity.Property(x => x.ContentType).HasMaxLength(100);
            entity.Property(x => x.Sha256).HasMaxLength(64).IsFixedLength();
            entity.HasIndex(x => new { x.TenantId, x.ExpenseId });
            entity.HasIndex(x => new { x.TenantId, x.ObjectKey }).IsUnique();
            entity.HasOne<Expense>().WithMany(x => x.Attachments).HasForeignKey(x => x.ExpenseId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<CustomExpenseCategory>(entity =>
        {
            entity.ToTable("CustomExpenseCategories");
            entity.Property(x => x.Name).HasMaxLength(50);
            entity.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            entity.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<InventoryItem>(entity =>
        {
            entity.ToTable("InventoryItems");
            entity.Property(x => x.Sku).HasMaxLength(120);
            entity.Property(x => x.Name).HasMaxLength(300);
            entity.Property(x => x.QuantityOnHand).HasPrecision(18, 4);
            entity.Property(x => x.AverageUnitCost).HasPrecision(18, 6);
            entity.HasIndex(x => new { x.TenantId, x.Sku }).IsUnique();
            entity.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<MarketplaceSkuMapping>(entity =>
        {
            entity.ToTable("MarketplaceSkuMappings");
            entity.Property(x => x.Platform).HasMaxLength(80);
            entity.Property(x => x.ExternalSku).HasMaxLength(120);
            entity.HasIndex(x => new { x.TenantId, x.Platform, x.ExternalSku }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.InventoryItemId });
            entity.HasOne(x => x.InventoryItem).WithMany().HasForeignKey(x => x.InventoryItemId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<SupplierInvoice>(entity =>
        {
            entity.ToTable("SupplierInvoices");
            entity.Property(x => x.AccessKey).HasMaxLength(44).IsFixedLength();
            entity.Property(x => x.InvoiceNumber).HasMaxLength(20);
            entity.Property(x => x.Series).HasMaxLength(10);
            entity.Property(x => x.SupplierTaxId).HasMaxLength(20);
            entity.Property(x => x.SupplierName).HasMaxLength(300);
            entity.Property(x => x.Total).HasPrecision(18, 2);
            entity.Property(x => x.XmlObjectKey).HasMaxLength(500);
            entity.Property(x => x.XmlSha256).HasMaxLength(64).IsFixedLength();
            entity.HasIndex(x => new { x.TenantId, x.AccessKey }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.IssuedAt });
            entity.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SupplierInvoiceItem>(entity =>
        {
            entity.ToTable("SupplierInvoiceItems");
            entity.Property(x => x.SupplierSku).HasMaxLength(120);
            entity.Property(x => x.Barcode).HasMaxLength(32);
            entity.Property(x => x.Name).HasMaxLength(300);
            entity.Property(x => x.Quantity).HasPrecision(18, 4);
            entity.Property(x => x.UnitCost).HasPrecision(18, 6);
            entity.Property(x => x.Total).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.TenantId, x.SupplierInvoiceId });
            entity.HasIndex(x => new { x.TenantId, x.InventoryItemId });
            entity.HasOne<SupplierInvoice>().WithMany(x => x.Items).HasForeignKey(x => x.SupplierInvoiceId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.InventoryItemId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<InventoryMovement>(entity =>
        {
            entity.ToTable("InventoryMovements");
            entity.Property(x => x.Type).HasMaxLength(30);
            entity.Property(x => x.EventKey).HasMaxLength(240);
            entity.Property(x => x.Quantity).HasPrecision(18, 4);
            entity.Property(x => x.UnitCost).HasPrecision(18, 6);
            entity.Property(x => x.Total).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.TenantId, x.EventKey }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.InventoryItemId, x.OccurredAt });
            entity.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.InventoryItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<SupplierInvoiceItem>().WithMany().HasForeignKey(x => x.SupplierInvoiceItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<MarketplaceOrderItem>().WithMany().HasForeignKey(x => x.MarketplaceOrderItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<InventoryMovement>().WithMany().HasForeignKey(x => x.ReversesMovementId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<InventoryReconciliationIssue>(entity =>
        {
            entity.ToTable("InventoryReconciliationIssues");
            entity.Property(x => x.EventKey).HasMaxLength(240);
            entity.Property(x => x.Type).HasMaxLength(40);
            entity.Property(x => x.Details).HasMaxLength(600);
            entity.HasIndex(x => new { x.TenantId, x.EventKey }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.ResolvedAt, x.CreatedAt });
            entity.HasIndex(x => x.MarketplaceOrderId);
            entity.HasIndex(x => x.MarketplaceOrderItemId);
            entity.HasOne<MarketplaceOrder>().WithMany().HasForeignKey(x => x.MarketplaceOrderId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<MarketplaceOrderItem>().WithMany().HasForeignKey(x => x.MarketplaceOrderItemId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TaxProfile>(entity =>
        {
            entity.ToTable("TaxProfiles");
            entity.Property(x => x.Cnpj).HasMaxLength(14).IsFixedLength();
            entity.Property(x => x.LegalName).HasMaxLength(300);
            entity.Property(x => x.TradeName).HasMaxLength(300);
            entity.Property(x => x.RegistrationStatus).HasMaxLength(80);
            entity.Property(x => x.CompanySize).HasMaxLength(80);
            entity.Property(x => x.AddressSummary).HasMaxLength(600);
            entity.Property(x => x.MainCnaeCode).HasMaxLength(10);
            entity.Property(x => x.MainCnaeDescription).HasMaxLength(300);
            entity.Property(x => x.TaxRegime).HasConversion<string>().HasMaxLength(30);
            entity.HasIndex(x => new { x.TenantId, x.Cnpj, x.Version }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.EffectiveFrom, x.EffectiveTo });
            entity.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TaxProfileSecondaryCnae>(entity =>
        {
            entity.ToTable("TaxProfileSecondaryCnaes");
            entity.Property(x => x.Code).HasMaxLength(10);
            entity.Property(x => x.Description).HasMaxLength(300);
            entity.HasIndex(x => new { x.TenantId, x.TaxProfileId, x.Code }).IsUnique();
            entity.HasOne<TaxProfile>().WithMany(x => x.SecondaryCnaes).HasForeignKey(x => x.TaxProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TaxRule>(entity =>
        {
            entity.ToTable("TaxRules");
            entity.Property(x => x.TaxCode).HasMaxLength(40);
            entity.Property(x => x.TaxName).HasMaxLength(160);
            entity.Property(x => x.Rate).HasPrecision(9, 6);
            entity.Property(x => x.Status).HasMaxLength(30);
            entity.Property(x => x.ReviewNotes).HasMaxLength(600);
            entity.HasIndex(x => new { x.TenantId, x.TaxProfileId, x.TaxCode, x.Version }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.Status, x.EffectiveFrom, x.EffectiveTo });
            entity.HasOne<TaxProfile>().WithMany().HasForeignKey(x => x.TaxProfileId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ReviewedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<TaxAssessment>(entity =>
        {
            entity.ToTable("TaxAssessments");
            entity.Property(x => x.Type).HasMaxLength(30);
            entity.Property(x => x.TaxableBase).HasPrecision(18, 2);
            entity.Property(x => x.Rate).HasPrecision(9, 6);
            entity.Property(x => x.TaxAmount).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.TenantId, x.MarketplaceOrderId, x.TaxRuleId, x.Type }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.AssessedAt });
            entity.HasIndex(x => x.ReversesAssessmentId).IsUnique();
            entity.HasOne<MarketplaceOrder>().WithMany().HasForeignKey(x => x.MarketplaceOrderId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TaxRule>().WithMany().HasForeignKey(x => x.TaxRuleId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TaxAssessment>().WithMany().HasForeignKey(x => x.ReversesAssessmentId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<TaxReconciliationIssue>(entity =>
        {
            entity.ToTable("TaxReconciliationIssues");
            entity.Property(x => x.EventKey).HasMaxLength(240);
            entity.Property(x => x.Type).HasMaxLength(40);
            entity.Property(x => x.Details).HasMaxLength(600);
            entity.HasIndex(x => new { x.TenantId, x.EventKey }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.ResolvedAt, x.CreatedAt });
            entity.HasIndex(x => x.MarketplaceOrderId);
            entity.HasOne<MarketplaceOrder>().WithMany().HasForeignKey(x => x.MarketplaceOrderId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AccountingPeriod>(entity =>
        {
            entity.ToTable("AccountingPeriods");
            entity.Property(x => x.Status).HasMaxLength(30);
            entity.Property(x => x.ReopenReason).HasMaxLength(1000);
            entity.HasIndex(x => new { x.TenantId, x.Year, x.Month }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.Status });
            entity.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AccountingPeriodCheck>(entity =>
        {
            entity.ToTable("AccountingPeriodChecks");
            entity.Property(x => x.Code).HasMaxLength(80);
            entity.Property(x => x.Description).HasMaxLength(300);
            entity.Property(x => x.BlockerDetails).HasColumnType("jsonb");
            entity.HasIndex(x => new { x.TenantId, x.AccountingPeriodId, x.ValidationRunId, x.Code }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.AccountingPeriodId, x.CheckedAt });
            entity.HasOne<AccountingPeriod>().WithMany(x => x.Checks).HasForeignKey(x => x.AccountingPeriodId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<DreSnapshot>(entity =>
        {
            entity.ToTable("DreSnapshots");
            entity.Property(x => x.CanonicalJson).HasColumnType("jsonb");
            entity.Property(x => x.CanonicalJsonSha256).HasMaxLength(64).IsFixedLength();
            entity.Property(x => x.PdfObjectKey).HasMaxLength(500);
            entity.Property(x => x.PdfSha256).HasMaxLength(64).IsFixedLength();
            foreach (var property in new[] { nameof(DreSnapshot.GrossRevenue), nameof(DreSnapshot.Deductions),
                         nameof(DreSnapshot.Taxes), nameof(DreSnapshot.NetRevenue), nameof(DreSnapshot.Cmv),
                         nameof(DreSnapshot.GrossProfit), nameof(DreSnapshot.SellingExpense),
                         nameof(DreSnapshot.OperatingExpense), nameof(DreSnapshot.Result) })
                entity.Property(property).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.TenantId, x.AccountingPeriodId, x.Revision }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.PdfObjectKey }).IsUnique();
            entity.HasOne<AccountingPeriod>().WithMany(x => x.Snapshots).HasForeignKey(x => x.AccountingPeriodId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ProfitDistributionAuthorization>(entity =>
        {
            entity.ToTable("ProfitDistributionAuthorizations");
            entity.Property(x => x.BeneficiaryName).HasMaxLength(200);
            entity.Property(x => x.BeneficiaryTaxId).HasMaxLength(20);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.TaxTreatment).HasMaxLength(40);
            entity.Property(x => x.LegalBasis).HasMaxLength(2000);
            entity.HasIndex(x => new { x.TenantId, x.AccountingPeriodId, x.AuthorizedAt });
            entity.HasOne<AccountingPeriod>().WithMany(x => x.ProfitDistributions)
                .HasForeignKey(x => x.AccountingPeriodId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DreSnapshot>().WithMany().HasForeignKey(x => x.DreSnapshotId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AuthorizedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<BpoTenantAssignment>(entity =>
        {
            entity.ToTable("BpoTenantAssignments");
            entity.HasIndex(x => new { x.TenantId, x.OperatorUserId, x.TargetTenantId }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.OperatorUserId, x.RevokedAt });
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.OperatorUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TargetTenantId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AssignedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ReconciliationImport>(entity =>
        {
            entity.ToTable("ReconciliationImports");
            entity.Property(x => x.Source).HasMaxLength(20);
            entity.Property(x => x.OriginalFileName).HasMaxLength(240);
            entity.Property(x => x.ObjectKey).HasMaxLength(500);
            entity.Property(x => x.Sha256).HasMaxLength(64).IsFixedLength();
            entity.Property(x => x.AccountReference).HasMaxLength(120);
            entity.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            entity.HasIndex(x => new { x.TenantId, x.Sha256 }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.PeriodStart, x.PeriodEnd });
            entity.HasIndex(x => new { x.TenantId, x.ImportedAt });
            entity.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ImportedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ReconciliationTransaction>(entity =>
        {
            entity.ToTable("ReconciliationTransactions");
            entity.Property(x => x.ExternalId).HasMaxLength(160);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.Property(x => x.Reference).HasMaxLength(240);
            entity.Property(x => x.Status).HasMaxLength(30);
            entity.Property(x => x.ReviewNote).HasMaxLength(600);
            entity.HasIndex(x => new { x.TenantId, x.ReconciliationImportId, x.ExternalId }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.OccurredAt, x.Status });
            entity.HasIndex(x => new { x.TenantId, x.Amount, x.OccurredAt });
            entity.HasOne<ReconciliationImport>().WithMany(x => x.Transactions)
                .HasForeignKey(x => x.ReconciliationImportId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ReviewedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ReconciliationAllocation>(entity =>
        {
            entity.ToTable("ReconciliationAllocations");
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.MatchMethod).HasMaxLength(30);
            entity.HasIndex(x => new { x.TenantId, x.ReconciliationTransactionId, x.MarketplacePaymentId }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.MarketplacePaymentId });
            entity.HasIndex(x => new { x.TenantId, x.AccountingEntryId });
            entity.HasOne<ReconciliationTransaction>().WithMany(x => x.Allocations)
                .HasForeignKey(x => x.ReconciliationTransactionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<MarketplacePayment>().WithMany().HasForeignKey(x => x.MarketplacePaymentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AccountingEntry>().WithMany().HasForeignKey(x => x.AccountingEntryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ConfirmedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        foreach (var entityType in builder.Model.GetEntityTypes()
                     .Where(x => typeof(ITenantEntity).IsAssignableFrom(x.ClrType)))
        {
            typeof(AppDbContext)
                .GetMethod(nameof(ApplyTenantFilter), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .MakeGenericMethod(entityType.ClrType)
                .Invoke(this, [builder]);
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardTenantChanges();
        GuardClosedPeriods();
        PrepareNewSaleNotifications();
        GuardTenantChanges();
        GuardClosedPeriods();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        GuardTenantChanges();
        await GuardClosedPeriodsAsync(cancellationToken);
        await PrepareNewSaleNotificationsAsync(cancellationToken);
        GuardTenantChanges();
        await GuardClosedPeriodsAsync(cancellationToken);
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void GuardTenantChanges()
    {
        if (ChangeTracker.Entries<AccountingEntry>().Any(x => x.State is EntityState.Modified or EntityState.Deleted) ||
            ChangeTracker.Entries<AccountingPosting>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Accounting entries and postings are append-only.");
        if (ChangeTracker.Entries<ReconciliationAllocation>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Reconciliation allocations are append-only.");
        if (ChangeTracker.Entries<InventoryMovement>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Inventory movements are append-only.");
        if (ChangeTracker.Entries<TaxAssessment>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Tax assessments are append-only.");
        if (ChangeTracker.Entries<DreSnapshot>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("DRE snapshots are append-only.");
        if (ChangeTracker.Entries<AccountingPeriodCheck>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Accounting period checks are append-only.");

        var tenantId = tenantContext.TenantId;
        foreach (var entry in ChangeTracker.Entries<ITenantEntity>()
                     .Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            // Registration and token rotation run before/without a bearer principal and set the tenant explicitly.
            if (tenantId is null)
            {
                if (entry.State != EntityState.Added && entry.Entity is not RefreshToken)
                    throw new UnauthorizedAccessException("A tenant is required to change tenant-owned data.");

                continue;
            }

            if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
                entry.Entity.TenantId = tenantId.Value;

            if (entry.Entity.TenantId != tenantId.Value)
                throw new UnauthorizedAccessException("Cross-tenant data access was blocked.");
        }
    }

    private void GuardClosedPeriods()
    {
        var dates = ChangedAccountingDates();
        if (dates.Count == 0)
            return;

        var closed = AccountingPeriods.AsNoTracking().Where(x => x.Status == AccountingPeriodStatuses.Closed).ToList();
        ThrowIfClosed(dates, closed);
    }

    private async Task GuardClosedPeriodsAsync(CancellationToken cancellationToken)
    {
        var dates = ChangedAccountingDates();
        if (dates.Count == 0)
            return;

        var closed = await AccountingPeriods.AsNoTracking().Where(x => x.Status == AccountingPeriodStatuses.Closed)
            .ToListAsync(cancellationToken);
        ThrowIfClosed(dates, closed);
    }

    private List<(string Entity, DateOnly Date)> ChangedAccountingDates()
    {
        var dates = ChangeTracker.Entries<AccountingEntry>()
            .Where(x => x.State == EntityState.Added)
            .Select(x => ("Accounting entry", DateOnly.FromDateTime(x.Entity.OccurredAt.UtcDateTime)))
            .ToList();

        foreach (var entry in ChangeTracker.Entries<Expense>()
                     .Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            dates.Add(("Expense", entry.Entity.CompetenceDate));
            if (entry.State == EntityState.Modified)
                dates.Add(("Expense", entry.OriginalValues.GetValue<DateOnly>(nameof(Expense.CompetenceDate))));
        }

        foreach (var entry in ChangeTracker.Entries<ReconciliationTransaction>()
                     .Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            dates.Add(("Reconciliation transaction", DateOnly.FromDateTime(entry.Entity.OccurredAt.UtcDateTime)));

        return dates.Distinct().ToList();
    }

    private static void ThrowIfClosed(
        IEnumerable<(string Entity, DateOnly Date)> dates,
        IEnumerable<AccountingPeriod> persistedClosedPeriods)
    {
        foreach (var change in dates)
        {
            if (persistedClosedPeriods.Any(x => change.Date >= x.StartDate && change.Date <= x.EndDate))
                throw new InvalidOperationException($"{change.Entity} cannot be changed in closed accounting period {change.Date:yyyy-MM}.");
        }
    }

    private void ApplyTenantFilter<TEntity>(ModelBuilder builder) where TEntity : class, ITenantEntity =>
        builder.Entity<TEntity>().HasQueryFilter(
            entity => tenantContext.TenantId.HasValue && entity.TenantId == tenantContext.TenantId.Value);

    private void PrepareNewSaleNotifications()
    {
        var entries = ChangeTracker.Entries<FinancialEntry>()
            .Where(x => x.State == EntityState.Added).Select(x => x.Entity).ToArray();
        if (entries.Length == 0 || tenantContext.TenantId is not { } tenantId)
            return;
        var recipients = NewSaleRecipients(tenantId).ToList();
        var existing = ExistingNotificationKeys(entries, recipients).ToHashSet();
        AddNewSaleNotifications(entries, recipients, existing);
    }

    private async Task PrepareNewSaleNotificationsAsync(CancellationToken cancellationToken)
    {
        var entries = ChangeTracker.Entries<FinancialEntry>()
            .Where(x => x.State == EntityState.Added).Select(x => x.Entity).ToArray();
        if (entries.Length == 0 || tenantContext.TenantId is not { } tenantId)
            return;
        var recipients = await NewSaleRecipients(tenantId).ToListAsync(cancellationToken);
        var existing = (await ExistingNotificationKeys(entries, recipients).ToListAsync(cancellationToken)).ToHashSet();
        AddNewSaleNotifications(entries, recipients, existing);
    }

    private IQueryable<NewSaleRecipient> NewSaleRecipients(Guid tenantId) =>
        from user in Users
        join userRole in UserRoles on user.Id equals userRole.UserId
        join role in Roles on userRole.RoleId equals role.Id
        join preference in NotificationPreferences on user.Id equals preference.UserId
        where user.TenantId == tenantId && user.IsActive && preference.NewSaleNotification &&
              (role.Name == Domain.Roles.Admin || role.Name == Domain.Roles.Seller)
        select new NewSaleRecipient(user.Id, user.Email!);

    private void AddNewSaleNotifications(
        IEnumerable<FinancialEntry> entries,
        IEnumerable<NewSaleRecipient> recipients,
        IReadOnlySet<NotificationKey> existing)
    {
        var tenantId = tenantContext.TenantId!.Value;
        foreach (var entry in entries)
        foreach (var recipient in recipients.Distinct())
        {
            var eventKey = $"sale:{entry.Marketplace}:{entry.ExternalId}";
            if (Notifications.Local.Any(x => x.UserId == recipient.Id && x.EventKey == eventKey) ||
                existing.Contains(new NotificationKey(recipient.Id, eventKey)))
                continue;
            var title = $"Nova venda em {entry.Marketplace}";
            var message = $"{entry.Description} no valor de {entry.GrossAmount:C}.";
            Notifications.Add(new UserNotification
            {
                TenantId = tenantId, UserId = recipient.Id, Type = NotificationTypes.NewSale,
                EventKey = eventKey, Title = title, Message = message, Link = "/dashboard"
            });
            EmailOutbox.Add(new EmailOutbox
            {
                TenantId = tenantId, UserId = recipient.Id, EventKey = eventKey,
                Recipient = recipient.Email, Subject = title,
                HtmlBody = $"<h1>{System.Text.Encodings.Web.HtmlEncoder.Default.Encode(title)}</h1><p>{System.Text.Encodings.Web.HtmlEncoder.Default.Encode(message)}</p>"
            });
        }
    }

    private IQueryable<NotificationKey> ExistingNotificationKeys(
        IReadOnlyCollection<FinancialEntry> entries,
        IReadOnlyCollection<NewSaleRecipient> recipients)
    {
        var eventKeys = entries.Select(x => $"sale:{x.Marketplace}:{x.ExternalId}").ToArray();
        var userIds = recipients.Select(x => x.Id).ToArray();
        return Notifications.Where(x => eventKeys.Contains(x.EventKey) && userIds.Contains(x.UserId))
            .Select(x => new NotificationKey(x.UserId, x.EventKey));
    }

    private sealed record NewSaleRecipient(Guid Id, string Email);
    private sealed record NotificationKey(Guid UserId, string EventKey);
}
