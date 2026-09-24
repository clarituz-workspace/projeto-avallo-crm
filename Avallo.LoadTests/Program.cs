using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using NBomber.CSharp;
using NBomber.Contracts;

// Teste de carga Avallo: semeia N tenants (register cria tenant + admin) e cada
// usuario virtual faz login uma vez e navega nos endpoints de leitura.
// Uso: dotnet run --project Avallo.LoadTests -- --url http://localhost:5152 --users 500 --minutes 3
//      --rps N injeta taxa-alvo (modelo aberto) em vez de N usuarios concorrentes.

var baseUrl = Arg("--url", "http://localhost:5152");
var users = int.Parse(Arg("--users", "500"), CultureInfo.InvariantCulture);
var duration = TimeSpan.FromMinutes(double.Parse(Arg("--minutes", "3"), CultureInfo.InvariantCulture));
var ramp = TimeSpan.FromSeconds(double.Parse(Arg("--ramp-seconds", "60"), CultureInfo.InvariantCulture));
var parallelSeed = int.Parse(Arg("--seed-parallel", "20"), CultureInfo.InvariantCulture);
var rpsTarget = int.Parse(Arg("--rps", "0"), CultureInfo.InvariantCulture);

const string password = "LoadTest-2026!abc";
var emails = Enumerable.Range(1, users).Select(i => $"loadtest{i}@avallo.local").ToArray();

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(60) };

Console.WriteLine($"Semeando {users} tenants em {baseUrl} (register cria tenant + admin)...");

var registered = 0;
var seedErrors = 0;
await Parallel.ForEachAsync(emails, new ParallelOptions { MaxDegreeOfParallelism = parallelSeed },
    async (email, ct) =>
    {
        var register = await http.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password,
            displayName = $"Load Test {email}",
            tenantName = $"LoadTest {email}"
        }, ct);
        if (!register.IsSuccessStatusCode)
        {
            // Ja existe de uma corrida anterior — login valida as credenciais.
            var login = await http.PostAsJsonAsync("/api/auth/login", new { email, password }, ct);
            if (!login.IsSuccessStatusCode)
            {
                Interlocked.Increment(ref seedErrors);
                Console.WriteLine($"  FALHA seed {email}: register={register.StatusCode} login={login.StatusCode}");
                return;
            }
        }
        Interlocked.Increment(ref registered);
    });

Console.WriteLine($"Seed pronto: {registered} usuarios ok, {seedErrors} falhas.");
if (seedErrors > 0)
{
    Console.WriteLine("Abortando: usuarios sem seed nao conseguem autenticar.");
    return 1;
}

// Sessao por usuario virtual: token JWT + validade. Re-login sob demanda (401/expirado).
var sessions = new ConcurrentDictionary<int, UserSession>();

async Task<UserSession> EnsureSessionAsync(int instanceNumber, CancellationToken ct)
{
    var session = sessions.GetOrAdd(instanceNumber, n => new UserSession(emails[n % emails.Length]));
    if (session.AccessToken is not null && session.ExpiresAt > DateTimeOffset.UtcNow.AddSeconds(30))
        return session;

    var login = await http.PostAsJsonAsync("/api/auth/login",
        new { email = session.Email, password }, ct);
    if (!login.IsSuccessStatusCode)
        throw new HttpRequestException($"login {session.Email}: {(int)login.StatusCode} {login.StatusCode}");
    var token = await login.Content.ReadFromJsonAsync<TokenResponseDto>(json, ct)
        ?? throw new InvalidOperationException("login sem access_token");
    session.AccessToken = token.AccessToken;
    session.ExpiresAt = token.ExpiresAt;
    return session;
}

async Task<Response<object>> CallAsync(IScenarioContext context, string step, string path, CancellationToken ct)
{
    return await Step.Run(step, context, async () =>
    {
        var session = await EnsureSessionAsync(context.ScenarioInfo.InstanceNumber, ct);
        var response = await SendAuthorizedAsync(session, path, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            session.AccessToken = null;
            session = await EnsureSessionAsync(context.ScenarioInfo.InstanceNumber, ct);
            response = await SendAuthorizedAsync(session, path, ct);
        }
        if (response.IsSuccessStatusCode)
            return Response.Ok(statusCode: ((int)response.StatusCode).ToString());
        var body = await response.Content.ReadAsStringAsync(ct);
        return Response.Fail(statusCode: ((int)response.StatusCode).ToString(),
            message: body.Length > 400 ? body[..400] : body);
    });
}

Task<HttpResponseMessage> SendAuthorizedAsync(UserSession session, string path, CancellationToken ct)
{
    var request = new HttpRequestMessage(HttpMethod.Get, path);
    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.AccessToken);
    return http.SendAsync(request, ct);
}

var random = Random.Shared;
var scenario = Scenario.Create("tenant_browse", async context =>
{
    var pick = random.Next(8);
    return pick switch
    {
        < 3 => await CallAsync(context, "dashboard", "/api/reports/dashboard", CancellationToken.None),
        < 5 => await CallAsync(context, "report_entries", "/api/reports/entries?page=1&pageSize=50&sortBy=date&descending=true", CancellationToken.None),
        < 7 => await CallAsync(context, "expenses", "/api/expenses/", CancellationToken.None),
        _ => await CallAsync(context, "me", "/api/auth/me", CancellationToken.None)
    };
})
    .WithoutWarmUp()
    .WithLoadSimulations(
        rpsTarget > 0
            ? new LoadSimulation[]
            {
                Simulation.RampingInject(rpsTarget, TimeSpan.FromSeconds(1), ramp),
                Simulation.Inject(rpsTarget, TimeSpan.FromSeconds(1), duration)
            }
            : new LoadSimulation[]
            {
                Simulation.RampingConstant(users, ramp),
                Simulation.KeepConstant(users, duration)
            });

NBomberRunner
    .RegisterScenarios(scenario)
    .WithReportFolder("load-test-results")
    .WithReportFileName($"loadtest-{users}users-{DateTime.Now:yyyyMMdd-HHmmss}")
    .Run();

return 0;

static string Arg(string name, string fallback)
{
    var index = Array.IndexOf(Environment.GetCommandLineArgs(), name);
    return index >= 0 && index + 1 < Environment.GetCommandLineArgs().Length
        ? Environment.GetCommandLineArgs()[index + 1]
        : fallback;
}

sealed class UserSession(string email)
{
    public string Email { get; } = email;
    public string? AccessToken { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

// A API responde camelCase (accessToken, expiresAt) — JsonSerializerDefaults.Web ja casa.
sealed record TokenResponseDto(string AccessToken, DateTimeOffset ExpiresAt);
