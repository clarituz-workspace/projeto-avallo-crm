using System.Security.Claims;

namespace Avallo.Web.Infrastructure;

public interface ITenantContext
{
    Guid? TenantId { get; }
}

public interface ITenantScope : ITenantContext
{
    IDisposable BeginScope(Guid tenantId);
}

public sealed class HttpTenantContext(IHttpContextAccessor accessor) : ITenantScope
{
    private Guid? _scopedTenantId;

    public Guid? TenantId
    {
        get
        {
            if (_scopedTenantId.HasValue)
                return _scopedTenantId;
            var value = accessor.HttpContext?.User.FindFirstValue("tenant_id");
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public IDisposable BeginScope(Guid tenantId)
    {
        if (_scopedTenantId.HasValue)
            throw new InvalidOperationException("A tenant scope is already active.");
        _scopedTenantId = tenantId;
        return new TenantScope(this);
    }

    private sealed class TenantScope(HttpTenantContext context) : IDisposable
    {
        public void Dispose() => context._scopedTenantId = null;
    }
}
