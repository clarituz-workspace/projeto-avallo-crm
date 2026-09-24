using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using MudBlazor.Services;
using Avallo.Web.Components;
using Avallo.Web.Domain;
using Avallo.Web.Application;
using Avallo.Web.Infrastructure;
using Avallo.Web.Features.Auth;
using QuestPDF.Infrastructure;
using Avallo.Web.Features.Notifications;
using Avallo.Web.Features.Connectors;
using Avallo.Client.Services;
using Avallo.Web.Hosting;
using Avallo.Web.Features.Expenses;
using Avallo.Web.Features.Reports;
using Avallo.Web.Features.Deployment;
using Avallo.Web.Features.AI;
using Azure.Storage.Blobs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMudServices();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    options.ForwardLimit = 1;
    var knownProxies = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
    foreach (var address in knownProxies)
    {
        if (System.Net.IPAddress.TryParse(address, out var ip))
            options.KnownProxies.Add(ip);
    }
    // TrustAnyProxy honors X-Forwarded-* from any upstream: appropriate on Azure Container
    // Apps, where the app is only reachable through the ingress. Without forwarded headers,
    // RemoteIpAddress is always the proxy IP and per-IP rate limiting becomes a global bucket.
    // An explicit KnownProxies list takes precedence and stays restricted to those IPs.
    var trustAnyProxy = builder.Configuration.GetValue<bool>("ForwardedHeaders:TrustAnyProxy");
    if (trustAnyProxy && options.KnownProxies.Count == 0)
    {
        options.KnownIPNetworks.Add(new System.Net.IPNetwork(System.Net.IPAddress.Any, 0));
        options.KnownIPNetworks.Add(new System.Net.IPNetwork(System.Net.IPAddress.IPv6Any, 0));
    }
    options.ForwardedHeaders = options.KnownProxies.Count > 0 || trustAnyProxy
        ? ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        : ForwardedHeaders.None;
});
// Dynamic HttpClient for server-side prerendering with cookie forwarding.
builder.Services.AddScoped(sp =>
{
    var accessor = sp.GetRequiredService<IHttpContextAccessor>();
    var context = accessor.HttpContext;
    var httpClient = new HttpClient();
    if (context is not null)
    {
        var request = context.Request;
        httpClient.BaseAddress = new Uri($"{request.Scheme}://{request.Host}{request.PathBase}/");
        if (request.Headers.TryGetValue("Cookie", out var cookies))
        {
            httpClient.DefaultRequestHeaders.Add("Cookie", cookies.ToString());
        }
    }
    return httpClient;
});

// Register all client-side services in the server DI container for Prerendering
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<UserAccessClient>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<ConnectorService>();
builder.Services.AddScoped<ChatService>();
builder.Services.AddScoped<AccountingService>();
builder.Services.AddScoped<ExpenseService>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<FiscalService>();
builder.Services.AddScoped<GeoLocationService>();
builder.Services.AddScoped<PeriodClosingClient>();
builder.Services.AddScoped<ReconciliationClient>();
builder.Services.AddScoped<WebPushNotificationService>();
builder.Services.AddScoped<DeploymentRealtimeService>();
builder.Services.AddOptions<AzureAiChatOptions>().BindConfiguration(AzureAiChatOptions.SectionName);
builder.Services.AddScoped<AzureAiChatService>();
builder.Services.AddSingleton<AppLocalizer>();
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
    options.Limits.MaxRequestBodySize = 12 * 1024 * 1024;
});
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/wasm"]);
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
    options.Level = System.IO.Compression.CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(options =>
    options.Level = System.IO.Compression.CompressionLevel.Fastest);
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("Avallo");
var objectStorageSection = builder.Configuration.GetSection(ObjectStorageOptions.SectionName);
var dataProtectionContainer = objectStorageSection["ContainerName"];
if (string.Equals(objectStorageSection["Provider"], "s3", StringComparison.OrdinalIgnoreCase))
{
    var s3Storage = new S3ObjectStorage(Microsoft.Extensions.Options.Options.Create(
        objectStorageSection.Get<ObjectStorageOptions>() ?? new ObjectStorageOptions()));
    if (s3Storage.IsEnabled && !string.IsNullOrWhiteSpace(dataProtectionContainer))
    {
        s3Storage.EnsureBucketAsync().GetAwaiter().GetResult();
        builder.Services.Configure<KeyManagementOptions>(options =>
            options.XmlRepository = new S3DataProtectionXmlRepository(s3Storage));
    }
}
else
{
    var dataProtectionConnection = objectStorageSection["ConnectionString"];
    if (!string.IsNullOrWhiteSpace(dataProtectionConnection) &&
        !string.IsNullOrWhiteSpace(dataProtectionContainer))
    {
        new BlobContainerClient(dataProtectionConnection, dataProtectionContainer)
            .CreateIfNotExists();
        dataProtection.PersistKeysToAzureBlobStorage(
            dataProtectionConnection,
            dataProtectionContainer,
            "system/dataprotection-keys.xml");
    }
}
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
});
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();
// Os conectores sao descobertos em runtime na pasta Connectors:PluginPath.
// Nenhum marketplace e citado aqui: adicionar um canal e publicar um DLL, nao recompilar o Core.
using (var connectorLoggerFactory = LoggerFactory.Create(logging => logging.AddConsole()))
{
    builder.Services.AddConnectorLayer(
        builder.Configuration,
        builder.Environment,
        connectorLoggerFactory.CreateLogger("Avallo.Connectors.Plugins"));
}

// Clean Architecture Layer Registrations
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddApplicationServices();
var redisConnection = builder.Configuration["AzureRedis:ConnectionString"];
if (!string.IsNullOrWhiteSpace(redisConnection))
{
    builder.Services.AddStackExchangeRedisCache(options => options.Configuration = redisConnection);
    builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(_ => StackExchange.Redis.ConnectionMultiplexer.Connect(redisConnection));
}
else
{
    builder.Services.AddDistributedMemoryCache();
}
builder.Services.AddSingleton<ReportCacheLock>();
var signalR = builder.Services.AddSignalR();
var azureSignalRConnection = builder.Configuration["AzureSignalR:ConnectionString"];
if (!string.IsNullOrWhiteSpace(azureSignalRConnection))
    signalR.AddAzureSignalR(azureSignalRConnection);
else if (!string.IsNullOrWhiteSpace(redisConnection))
    signalR.AddStackExchangeRedis(redisConnection);
builder.Services.AddSingleton<DeploymentNotificationService>();

builder.Services.AddScoped<NotificationDispatchService>();
    builder.Services.AddScoped<NotificationScheduler>();
    builder.Services.AddSingleton<MarketplaceSyncQueue>();
    builder.Services.AddOptions<ServiceBusOptions>().BindConfiguration(ServiceBusOptions.SectionName);
    builder.Services.AddOptions<MarketplaceWebhookOptions>().BindConfiguration(MarketplaceWebhookOptions.SectionName);
    builder.Services.AddOptions<MarketplaceSyncSchedulerOptions>().BindConfiguration(MarketplaceSyncSchedulerOptions.SectionName);
    builder.Services.AddHostedService<MarketplaceSyncWorker>();
    builder.Services.AddHostedService<MarketplaceSyncSchedulerWorker>();
    builder.Services.AddScoped<AzureCommunicationEmailSender>();
builder.Services.AddHostedService<NotificationWorker>();
QuestPDF.Settings.License = Enum.TryParse<LicenseType>(
    builder.Configuration["Reports:QuestPdfLicense"], ignoreCase: true, out var questPdfLicense)
    ? questPdfLicense
    : LicenseType.Community;
    builder.Services.AddOptions<EmailOptions>()
    .BindConfiguration(EmailOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate(options => !options.Enabled ||
        (!string.IsNullOrWhiteSpace(options.Host) && !string.IsNullOrWhiteSpace(options.FromEmail)),
        "Email host and sender are required when email delivery is enabled.")
        .ValidateOnStart();
    builder.Services.AddOptions<AzureCommunicationEmailOptions>()
        .BindConfiguration(AzureCommunicationEmailOptions.SectionName);
builder.Services.AddOptions<NotificationWorkerOptions>()
    .BindConfiguration(NotificationWorkerOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<ObjectStorageOptions>()
    .BindConfiguration(ObjectStorageOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate(options => !options.Enabled ||
        ((options.IsS3
            ? !string.IsNullOrWhiteSpace(options.ServiceUrl) &&
              !string.IsNullOrWhiteSpace(options.AccessKey) &&
              !string.IsNullOrWhiteSpace(options.SecretKey)
            : string.Equals(options.Provider, "azure", StringComparison.OrdinalIgnoreCase) &&
              !string.IsNullOrWhiteSpace(options.ConnectionString)) &&
         !string.IsNullOrWhiteSpace(options.ContainerName)),
        "ObjectStorage:Provider must be 'azure' (ConnectionString required) or 's3' (ServiceUrl, AccessKey and SecretKey required); ContainerName/bucket is always required.")
    .ValidateOnStart();

builder.Services.AddOptions<JwtOptions>()
    .BindConfiguration(JwtOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("JWT configuration is required.");

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

// Valida o security_stamp do JWT via IDistributedCache, sem query por request.
// A revogacao pode atrasar ate o TTL do cache (30s); ver CachedSecurityStampValidator.
builder.Services.AddScoped<CachedSecurityStampValidator>();

var authentication = builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "name",
            RoleClaimType = ClaimTypes.Role
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) &&
                    (context.HttpContext.Request.Path.StartsWithSegments("/hubs/deployment") ||
                     context.HttpContext.Request.Path.StartsWithSegments("/updateHub")))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                var stamp = context.Principal?.FindFirstValue("security_stamp");
                var validator = context.HttpContext.RequestServices.GetRequiredService<CachedSecurityStampValidator>();
                if (!await validator.IsValidAsync(userId, stamp, context.HttpContext.RequestAborted))
                    context.Fail("Token is no longer valid.");
            }
        };
    })
    .AddCookie(IdentityConstants.ExternalScheme, options =>
    {
        options.Cookie.Name = "nucleo-external-auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
    });

var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
if (!string.IsNullOrWhiteSpace(googleClientId) &&
    !string.IsNullOrWhiteSpace(googleClientSecret))
{
    authentication.AddGoogle(options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
        options.SignInScheme = IdentityConstants.ExternalScheme;
        options.SaveTokens = false;
    });
}

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.TenantMember, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("tenant_id")
        .RequireClaim("password_change_required", "false")
        .RequireRole(Roles.All))
    .AddPolicy(Policies.CanWrite, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("tenant_id")
        .RequireClaim("password_change_required", "false")
        .RequireRole(Roles.Writers))
    .AddPolicy(Policies.CanManageUsers, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("tenant_id")
        .RequireClaim("password_change_required", "false")
        .RequireRole(Roles.Admin))
    .AddPolicy(Policies.CanReviewAccounting, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("tenant_id")
        .RequireClaim("password_change_required", "false")
        .RequireRole(Roles.AccountingManagers));
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.CanOperateBpo, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("tenant_id")
        .RequireClaim("password_change_required", "false")
        .RequireRole(Roles.BpoOperator, Roles.BpoAdmin));
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.CanManageBpo, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("tenant_id")
        .RequireClaim("password_change_required", "false")
        .RequireRole(Roles.BpoAdmin));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    var authPermitLimit = builder.Configuration.GetValue("RateLimiting:AuthPermitLimit", 10);
    var sessionPermitLimit = builder.Configuration.GetValue("RateLimiting:SessionPermitLimit", 120);
    options.AddPolicy("authentication", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("authentication-session", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = sessionPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 2,
                AutoReplenishment = true
            }));
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("auth-v1", new OpenApiInfo
    {
        Title = "Avallo.Web Authentication API",
        Version = "v1",
        Description = "Cadastro, login, OAuth Google, renovacao de sessao e usuarios multi-tenant."
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Informe somente o access token JWT."
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});

var app = builder.Build();

app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
    app.UseSwagger(options => options.RouteTemplate = "swagger/{documentName}/swagger.json");
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/auth-v1/swagger.json", "Authentication API v1");
        options.RoutePrefix = "swagger";
        options.DocumentTitle = "Authentication API";
        options.DisplayRequestDuration();
        options.EnableTryItOutByDefault();
    });
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
// Keep API status codes and response bodies intact. Re-executing an API 401/403
// through the Razor not-found page can turn the original response into a 400/500.
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api"),
    branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));

app.UseStaticFiles();
app.UseHttpsRedirection();
if (!app.Environment.IsDevelopment())
{
    app.UseResponseCompression();
}
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/health", async (AppDbContext db, CancellationToken cancellationToken) =>
{
    try
    {
        if (!await db.Database.CanConnectAsync(cancellationToken))
            return Results.Json(new { status = "unhealthy", service = "Avallo-web", dependency = "database" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        return Results.Ok(new { status = "healthy", service = "Avallo-web" });
    }
    catch (Exception)
    {
        return Results.Json(new { status = "unhealthy", service = "Avallo-web", dependency = "database" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).AllowAnonymous();

app.MapGet("/live", () => Results.Ok(new { status = "live", service = "Avallo-web" })).AllowAnonymous();
app.MapGet("/ready", async (AppDbContext db, [FromServices] StackExchange.Redis.IConnectionMultiplexer? redis, CancellationToken cancellationToken) =>
{
    try
    {
        if (!await db.Database.CanConnectAsync(cancellationToken))
            return Results.Json(new { status = "not_ready", dependency = "database" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        if (redis is not null)
            await redis.GetDatabase().PingAsync();
        return Results.Ok(new { status = "ready", service = "Avallo-web" });
    }
    catch (Exception)
    {
        return Results.Json(new { status = "not_ready", dependency = "infrastructure" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).AllowAnonymous();

app.MapGet("/_framework/blazor.web.js", (IWebHostEnvironment environment) =>
    Results.File(
        Path.Combine(environment.WebRootPath, "_framework", "blazor.web.js"),
        "text/javascript"))
    .AllowAnonymous();

app.MapStaticAssets();
app.MapBusinessModules();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Avallo.Client._Imports).Assembly);

await using (var scope = app.Services.CreateAsyncScope())
{
    var services = scope.ServiceProvider;
    if (builder.Configuration.GetValue("Database:ApplyMigrations", false))
    {
        // Migrations e DDL usam a credencial dona do schema, nao o role da aplicacao.
        // O role da aplicacao nao tem DDL de proposito: e ele que fica sujeito as
        // policies de Row Level Security. Sem MigrationConnection, cai no comportamento
        // anterior e usa a conexao padrao.
        var migrationConnection = builder.Configuration.GetConnectionString("MigrationConnection");
        await using AppDbContext? ownerDb = string.IsNullOrWhiteSpace(migrationConnection)
            ? null
            : new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(migrationConnection).Options,
                new NullTenantContext());
        var db = ownerDb ?? services.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        try
        {
            if (db.Database.IsRelational())
            {
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_AccountingPostings_AccountingEntryId\" ON \"AccountingPostings\" (\"AccountingEntryId\");");
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_InventoryReconciliationIssues_MarketplaceOrderId\" ON \"InventoryReconciliationIssues\" (\"MarketplaceOrderId\");");
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_InventoryReconciliationIssues_MarketplaceOrderItemId\" ON \"InventoryReconciliationIssues\" (\"MarketplaceOrderItemId\");");
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_TaxReconciliationIssues_MarketplaceOrderId\" ON \"TaxReconciliationIssues\" (\"MarketplaceOrderId\");");
            }
        }
        catch (Exception ex)
        {
            var logger = services.GetRequiredService<ILogger<AppDbContext>>();
            logger.LogWarning(ex, "Failed to dynamically ensure indexes exist on database startup.");
        }
    }

    var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    foreach (var role in Roles.Seeded)
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole<Guid>(role));
}

app.Run();
