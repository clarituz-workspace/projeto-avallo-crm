namespace Avallo.Web.Domain;

public static class Roles
{
    public const string Admin = nameof(Admin);
    public const string Seller = "Vendedor";
    public const string Accountant = "Contador";
    public const string BpoOperator = "OperadorBPO";
    public const string BpoAdmin = "AdministradorBPO";

    public static readonly string[] All = [Admin, Seller, Accountant];
    public static readonly string[] Seeded = [Admin, Seller, Accountant, BpoOperator, BpoAdmin];
    public static readonly string[] Writers = [Admin, Seller];
    public static readonly string[] AccountingManagers = [Admin, Accountant];
}

public static class Policies
{
    public const string TenantMember = nameof(TenantMember);
    public const string CanWrite = nameof(CanWrite);
    public const string CanManageUsers = nameof(CanManageUsers);
    public const string CanReviewAccounting = nameof(CanReviewAccounting);
    public const string CanOperateBpo = nameof(CanOperateBpo);
    public const string CanManageBpo = nameof(CanManageBpo);
}
