using Avallo.Web.Domain;
using Xunit;

namespace Avallo.Tests.Features;

public sealed class RolePermissionTests
{
    [Fact]
    public void Accountant_reviews_accounting_without_general_write_or_user_management_access()
    {
        Assert.Contains(Roles.Accountant, Roles.All);
        Assert.DoesNotContain(Roles.Accountant, Roles.Writers);
        Assert.Contains(Roles.Accountant, Roles.AccountingManagers);
        Assert.Contains(Roles.Admin, Roles.Writers);
        Assert.Contains(Roles.Admin, Roles.AccountingManagers);
    }

    [Fact]
    public void Bpo_operator_is_seeded_but_is_not_a_tenant_member_or_general_writer()
    {
        Assert.Contains(Roles.BpoOperator, Roles.Seeded);
        Assert.Contains(Roles.BpoAdmin, Roles.Seeded);
        Assert.DoesNotContain(Roles.BpoOperator, Roles.All);
        Assert.DoesNotContain(Roles.BpoOperator, Roles.Writers);
        Assert.DoesNotContain(Roles.BpoOperator, Roles.AccountingManagers);
    }
}
