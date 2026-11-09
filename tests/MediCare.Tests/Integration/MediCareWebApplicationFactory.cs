using System.Security.Claims;
using System.Text.Json;
using System.Text.Encodings.Web;
using MediCare.Data.Context;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediCare.Tests.Integration;

public class MediCareWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = "MediCare_IntegrationTestDb_" + Guid.NewGuid().ToString("N");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=(localdb)\\mssqllocaldb;Database=MediCare_IntegrationHost;Trusted_Connection=True;TrustServerCertificate=True");
        // Avoid production exception re-execution in tests so failures surface with their original stack trace.
        builder.UseEnvironment("Development");
        builder.ConfigureLogging(logging => logging.ClearProviders());

        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=(localdb)\\mssqllocaldb;Database=MediCare_IntegrationHost;Trusted_Connection=True;TrustServerCertificate=True",
                ["Seed:AdminPassword"] = "IntegrationTestAdminPass!2026Secure",
                ["Seed:PharmacistPassword"] = "IntegrationTestPharmPass!2026Secure",
                ["Seed:DefaultPassword"] = "IntegrationTestDefaultPass!2026Secure"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();

            // Remove existing DbContextOptions
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            // Register in-memory DbContext
            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName);
            });

            // Keep integration tests isolated from external payment providers and environment credentials.
            foreach (var paymobDescriptor in services.Where(d => d.ServiceType == typeof(IPaymobCheckoutService)).ToList())
            {
                services.Remove(paymobDescriptor);
            }
            services.AddSingleton<IPaymobCheckoutService, UnconfiguredPaymobCheckoutService>();

            // Register test authentication scheme
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.AuthenticationScheme;
                options.DefaultChallengeScheme = TestAuthHandler.AuthenticationScheme;
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                TestAuthHandler.AuthenticationScheme, _ => { });

            // Replace ClinicClock with controllable TestClinicClock
            var clockDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IClinicClock));
            if (clockDescriptor != null) services.Remove(clockDescriptor);
            services.AddSingleton<TestClinicClock>();
            services.AddSingleton<IClinicClock>(sp => sp.GetRequiredService<TestClinicClock>());

            // Bypass Antiforgery validation during automated integration test execution
            services.AddSingleton<IAntiforgery, FakeAntiforgery>();
        });
    }

    private sealed class UnconfiguredPaymobCheckoutService : IPaymobCheckoutService
    {
        public Task<Result<PaymentCheckoutSessionDto>> CreateCheckoutAsync(
            int appointmentId,
            string userId,
            string? promoCode,
            string? termsVersion = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<PaymentCheckoutSessionDto>.Failure("Paymob is intentionally unavailable in integration tests."));

        public Task<bool> ProcessWebhookAsync(JsonElement payload, string? hmac, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    public TestClinicClock Clock => Services.GetRequiredService<TestClinicClock>();

    public HttpClient CreateAuthenticatedClient(string userId, string role, string? email = null, string? fullName = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        client.DefaultRequestHeaders.Add("X-Test-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-Email", email ?? $"{userId}@test.clinic");
        client.DefaultRequestHeaders.Add("X-Test-Name", fullName ?? $"User {userId}");

        return client;
    }

    public HttpClient CreateAnonymousClient()
    {
        return CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    public async Task SeedAsync(Func<ApplicationDbContext, Task> seeder)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await seeder(db);
    }
}

public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string AuthenticationScheme = "TestAuthScheme";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-UserId", out var userIdValues) ||
            string.IsNullOrWhiteSpace(userIdValues.FirstOrDefault()))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var userId = userIdValues.First()!;
        var role = Request.Headers.TryGetValue("X-Test-Role", out var roleValues) ? roleValues.First() : "Patient";
        var email = Request.Headers.TryGetValue("X-Test-Email", out var emailValues) ? emailValues.First() : $"{userId}@test.clinic";
        var name = Request.Headers.TryGetValue("X-Test-Name", out var nameValues) ? nameValues.First() : "Test User";

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, name!),
            new(ClaimTypes.Email, email!),
            new(ClaimTypes.Role, role!)
        };

        if (Request.Headers.ContainsKey("X-Test-MustChangePassword"))
        {
            claims.Add(new("MustChangePassword", "true"));
        }

        var identity = new ClaimsIdentity(claims, AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, AuthenticationScheme);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public class FakeAntiforgery : IAntiforgery
{
    public AntiforgeryTokenSet GetAndStoreTokens(HttpContext httpContext) =>
        new("fake-req-token", "fake-cookie-token", "__RequestVerificationToken", "X-CSRF-TOKEN");

    public AntiforgeryTokenSet GetTokens(HttpContext httpContext) =>
        new("fake-req-token", "fake-cookie-token", "__RequestVerificationToken", "X-CSRF-TOKEN");

    public Task<bool> IsRequestValidAsync(HttpContext httpContext) => Task.FromResult(true);

    public Task ValidateRequestAsync(HttpContext httpContext) => Task.CompletedTask;

    public void SetCookieTokenAndHeader(HttpContext httpContext) { }
}

public class TestClinicClock : IClinicClock
{
    private DateTime? _fixedNow;

    public void SetNow(DateTime dateTime) => _fixedNow = dateTime;
    public void Reset() => _fixedNow = null;

    public DateTime Now => _fixedNow ?? TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZone);
    public DateTime Today => Now.Date;
    public TimeZoneInfo TimeZone => TimeZoneInfo.Local;
}
