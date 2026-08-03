using System.Net;
using Google.Apis.Auth.OAuth2.Responses;
using HealthMetrics.Application.Models;
using HealthMetrics.Infrastructure.Clients;
using HealthMetrics.Infrastructure.Options;
using HealthMetrics.Infrastructure.Persistence;
using HealthMetrics.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HealthMetrics.Tests.Services;

public sealed class GoogleHealthAuthorizationServiceTests : IAsyncLifetime
{
    private const string SleepReadScope = "https://www.googleapis.com/auth/googlehealth.sleep.readonly";

    private SqliteConnection _connection = null!;
    private HealthMetricsDbContext _dbContext = null!;
    private IDataProtectionProvider _dp = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        _dbContext = new HealthMetricsDbContext(
            new DbContextOptionsBuilder<HealthMetricsDbContext>().UseSqlite(_connection).Options);
        await _dbContext.Database.EnsureCreatedAsync();
        _dp = new EphemeralDataProtectionProvider();
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ── GetConnectionStatusAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetConnectionStatusAsync_WhenNotConnected_ReturnsDisconnected()
    {
        var svc = CreateService();

        var status = await svc.GetConnectionStatusAsync();

        Assert.False(status.IsConnected);
        Assert.Null(status.GoogleEmail);
        Assert.Null(status.GoogleUserId);
        Assert.False(status.RequiresReconnect);
    }

    [Fact]
    public async Task GetConnectionStatusAsync_WhenConnected_ReturnsFullDetails()
    {
        var protector = _dp.CreateProtector("HealthMetrics.GoogleTokens.v1");
        _dbContext.HealthConnections.Add(new HealthConnection
        {
            GoogleUserId = "user-123",
            GoogleEmail = "user@example.com",
            AccessToken = protector.Protect("at"),
            RefreshToken = protector.Protect("rt"),
            Scope = "openid",
            AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            LastSuccessfulSyncAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
        });
        await _dbContext.SaveChangesAsync();

        var status = await CreateService().GetConnectionStatusAsync();

        Assert.True(status.IsConnected);
        Assert.Equal("user-123", status.GoogleUserId);
        Assert.Equal("user@example.com", status.GoogleEmail);
        Assert.NotNull(status.LastSuccessfulSyncAtUtc);
        Assert.True(status.RequiresReconnect);
    }

    [Fact]
    public async Task GetConnectionStatusAsync_WhenSleepScopeIsGranted_DoesNotRequireReconnect()
    {
        var protector = _dp.CreateProtector("HealthMetrics.GoogleTokens.v1");
        _dbContext.HealthConnections.Add(new HealthConnection
        {
            GoogleUserId = "user-123",
            AccessToken = protector.Protect("at"),
            RefreshToken = protector.Protect("rt"),
            Scope = $"openid email {SleepReadScope}",
            AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        });
        await _dbContext.SaveChangesAsync();

        var status = await CreateService().GetConnectionStatusAsync();

        Assert.True(status.IsConnected);
        Assert.False(status.RequiresReconnect);
    }

    // ── GetValidAccessTokenAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetValidAccessTokenAsync_WhenNotConnected_Throws()
    {
        var svc = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.GetValidAccessTokenAsync());
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_WhenTokenValid_ReturnsDecryptedToken()
    {
        var protector = _dp.CreateProtector("HealthMetrics.GoogleTokens.v1");
        _dbContext.HealthConnections.Add(new HealthConnection
        {
            GoogleUserId = "user-123",
            AccessToken = protector.Protect("my-access-token"),
            RefreshToken = protector.Protect("my-refresh-token"),
            Scope = "openid",
            AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        });
        await _dbContext.SaveChangesAsync();

        var token = await CreateService().GetValidAccessTokenAsync();

        Assert.Equal("my-access-token", token);
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_WhenTokenNearExpiry_RefreshesAndPersistsNewToken()
    {
        var protector = _dp.CreateProtector("HealthMetrics.GoogleTokens.v1");
        _dbContext.HealthConnections.Add(new HealthConnection
        {
            GoogleUserId = "user-123",
            AccessToken = protector.Protect("old-access-token"),
            RefreshToken = protector.Protect("my-refresh-token"),
            Scope = "openid",
            AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(1) // within 2-minute skew
        });
        await _dbContext.SaveChangesAsync();

        var fakeAdapter = new FakeGoogleAuthAdapter(
            refreshResponse: new TokenResponse { AccessToken = "new-access-token", ExpiresInSeconds = 3600 });
        var token = await CreateService(fakeAdapter).GetValidAccessTokenAsync();

        Assert.Equal("new-access-token", token);
        var connection = await _dbContext.HealthConnections.SingleAsync();
        Assert.Equal("new-access-token", protector.Unprotect(connection.AccessToken));
        Assert.Equal("my-refresh-token", fakeAdapter.LastRefreshTokenReceived);
    }

    // ── HandleAuthorizationCodeAsync ─────────────────────────────────────────

    [Fact]
    public async Task HandleAuthorizationCodeAsync_NewConnection_CreatesRow()
    {
        var fakeAdapter = new FakeGoogleAuthAdapter(
            exchangeResponse: new TokenResponse
            {
                AccessToken = "new-at",
                RefreshToken = "new-rt",
                ExpiresInSeconds = 3600,
                Scope = "openid email"
            });
        var svc = CreateService(fakeAdapter, googleUserId: "gid-1", googleEmail: "a@b.com");

        await svc.HandleAuthorizationCodeAsync("auth-code");

        var conn = await _dbContext.HealthConnections.SingleAsync();
        Assert.Equal("gid-1", conn.GoogleUserId);
        Assert.Equal("a@b.com", conn.GoogleEmail);
        Assert.Equal("openid email", conn.Scope);
    }

    [Fact]
    public async Task HandleAuthorizationCodeAsync_ExistingConnection_UpdatesTokens()
    {
        var protector = _dp.CreateProtector("HealthMetrics.GoogleTokens.v1");
        _dbContext.HealthConnections.Add(new HealthConnection
        {
            GoogleUserId = "old-user",
            AccessToken = protector.Protect("old-at"),
            RefreshToken = protector.Protect("old-rt"),
            Scope = "openid",
            AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        });
        await _dbContext.SaveChangesAsync();

        var fakeAdapter = new FakeGoogleAuthAdapter(
            exchangeResponse: new TokenResponse
            {
                AccessToken = "new-at",
                RefreshToken = "new-rt",
                ExpiresInSeconds = 3600,
                Scope = "openid email"
            });
        await CreateService(fakeAdapter, googleUserId: "new-user", googleEmail: "new@b.com")
            .HandleAuthorizationCodeAsync("code");

        Assert.Equal(1, await _dbContext.HealthConnections.CountAsync());
        var conn = await _dbContext.HealthConnections.SingleAsync();
        Assert.Equal("new-user", conn.GoogleUserId);
    }

    [Fact]
    public async Task HandleAuthorizationCodeAsync_WhenTokenScopeMissing_PreservesExistingScope()
    {
        var protector = _dp.CreateProtector("HealthMetrics.GoogleTokens.v1");
        _dbContext.HealthConnections.Add(new HealthConnection
        {
            GoogleUserId = "old-user",
            AccessToken = protector.Protect("old-at"),
            RefreshToken = protector.Protect("old-rt"),
            Scope = $"openid {SleepReadScope}",
            AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        });
        await _dbContext.SaveChangesAsync();

        var fakeAdapter = new FakeGoogleAuthAdapter(
            exchangeResponse: new TokenResponse
            {
                AccessToken = "new-at",
                RefreshToken = "new-rt",
                ExpiresInSeconds = 3600,
                Scope = null
            });

        await CreateService(fakeAdapter).HandleAuthorizationCodeAsync("code");

        var conn = await _dbContext.HealthConnections.SingleAsync();
        Assert.Equal($"openid {SleepReadScope}", conn.Scope);
    }

    [Fact]
    public async Task HandleAuthorizationCodeAsync_WhenTokenScopeMissingForNewConnection_UsesConfiguredScopes()
    {
        var fakeAdapter = new FakeGoogleAuthAdapter(
            exchangeResponse: new TokenResponse
            {
                AccessToken = "new-at",
                RefreshToken = "new-rt",
                ExpiresInSeconds = 3600,
                Scope = null
            });

        var configuredScopes = new[] { "openid", "email", SleepReadScope };
        await CreateService(fakeAdapter, scopes: configuredScopes).HandleAuthorizationCodeAsync("code");

        var conn = await _dbContext.HealthConnections.SingleAsync();
        Assert.Equal(string.Join(' ', configuredScopes), conn.Scope);
    }

    [Fact]
    public async Task HandleAuthorizationCodeAsync_MissingRefreshToken_Throws()
    {
        var fakeAdapter = new FakeGoogleAuthAdapter(
            exchangeResponse: new TokenResponse { AccessToken = "at", ExpiresInSeconds = 3600 });
        var svc = CreateService(fakeAdapter);

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.HandleAuthorizationCodeAsync("code"));
    }

    // ── DisconnectAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task DisconnectAsync_WhenNotConnected_ReturnsWithoutError()
    {
        var svc = CreateService();

        await svc.DisconnectAsync(); // should not throw

        Assert.Equal(0, await _dbContext.HealthConnections.CountAsync());
    }

    [Fact]
    public async Task DisconnectAsync_WhenConnected_RemovesRow()
    {
        var protector = _dp.CreateProtector("HealthMetrics.GoogleTokens.v1");
        _dbContext.HealthConnections.Add(new HealthConnection
        {
            GoogleUserId = "user-123",
            AccessToken = protector.Protect("at"),
            RefreshToken = protector.Protect("rt"),
            Scope = "openid",
            AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        });
        await _dbContext.SaveChangesAsync();

        var fakeAdapter = new FakeGoogleAuthAdapter();
        await CreateService(fakeAdapter).DisconnectAsync();

        Assert.Equal(0, await _dbContext.HealthConnections.CountAsync());
        Assert.Equal("rt", fakeAdapter.LastRevokedToken);
    }

    [Fact]
    public async Task DisconnectAsync_WhenRevokeFails_StillRemovesRow()
    {
        var protector = _dp.CreateProtector("HealthMetrics.GoogleTokens.v1");
        _dbContext.HealthConnections.Add(new HealthConnection
        {
            GoogleUserId = "user-123",
            AccessToken = protector.Protect("at"),
            RefreshToken = protector.Protect("rt"),
            Scope = "openid",
            AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        });
        await _dbContext.SaveChangesAsync();

        var failingAdapter = new FakeGoogleAuthAdapter(revokeThrows: true);
        await CreateService(failingAdapter).DisconnectAsync(); // should not throw

        Assert.Equal(0, await _dbContext.HealthConnections.CountAsync());
    }

    [Fact]
    public async Task DisconnectAsync_WhenStoredRefreshTokenCannotBeDecrypted_StillRemovesRow()
    {
        var foreignProtector = new EphemeralDataProtectionProvider()
            .CreateProtector("HealthMetrics.GoogleTokens.v1");
        _dbContext.HealthConnections.Add(new HealthConnection
        {
            GoogleUserId = "user-123",
            AccessToken = foreignProtector.Protect("at"),
            RefreshToken = foreignProtector.Protect("rt"),
            Scope = "openid",
            AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        });
        await _dbContext.SaveChangesAsync();

        await CreateService().DisconnectAsync();

        Assert.Equal(0, await _dbContext.HealthConnections.CountAsync());
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_WhenStoredAccessTokenCannotBeDecrypted_ThrowsInvalidOperationException()
    {
        var foreignProtector = new EphemeralDataProtectionProvider()
            .CreateProtector("HealthMetrics.GoogleTokens.v1");
        _dbContext.HealthConnections.Add(new HealthConnection
        {
            GoogleUserId = "user-123",
            AccessToken = foreignProtector.Protect("at"),
            RefreshToken = foreignProtector.Protect("rt"),
            Scope = "openid",
            AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        });
        await _dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService().GetValidAccessTokenAsync());
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private GoogleHealthAuthorizationService CreateService(
        FakeGoogleAuthAdapter? adapter = null,
        string? googleUserId = null,
        string? googleEmail = null,
        string[]? scopes = null)
    {
        adapter ??= new FakeGoogleAuthAdapter();

        var identityHandler = new StubHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$$"""{"healthUserId":"{{{googleUserId ?? "user-123"}}}"}""",
                    System.Text.Encoding.UTF8, "application/json")
            });
        var apiHttpClient = new HttpClient(identityHandler)
        {
            BaseAddress = new Uri("https://health.googleapis.com/v4/")
        };
        var apiClient = new GoogleHealthApiClient(
            apiHttpClient,
            Options.Create(new GoogleHealthHttpLoggingOptions()),
            NullLogger<GoogleHealthApiClient>.Instance);

        var emailHandler = new StubHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$$"""{"email":"{{{googleEmail ?? "user@example.com"}}}"}""",
                    System.Text.Encoding.UTF8, "application/json")
            });
        var accountClient = new GoogleAccountApiClient(new HttpClient(emailHandler)
        {
            BaseAddress = new Uri("https://openidconnect.googleapis.com/")
        });
        var options = Options.Create(new GoogleHealthApiOptions
        {
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RedirectUri = "https://localhost/callback",
            Scopes = scopes ?? ["openid", "email", SleepReadScope]
        });

        return new GoogleHealthAuthorizationService(
            _dbContext,
            apiClient,
            accountClient,
            adapter,
            _dp,
            options,
            NullLogger<GoogleHealthAuthorizationService>.Instance);
    }

    private sealed class FakeGoogleAuthAdapter(
        TokenResponse? exchangeResponse = null,
        TokenResponse? refreshResponse = null,
        bool revokeThrows = false) : IGoogleAuthAdapter
    {
        public string? LastRefreshTokenReceived { get; private set; }
        public string? LastRevokedToken { get; private set; }

        public Task<Uri> BuildAuthorizationUriAsync(string state, CancellationToken cancellationToken)
            => Task.FromResult(new Uri($"https://accounts.google.com/auth?state={state}"));

        public Task<TokenResponse> ExchangeCodeForTokenAsync(string code, CancellationToken cancellationToken)
            => Task.FromResult(exchangeResponse ?? throw new InvalidOperationException("No exchange response configured."));

        public Task<TokenResponse> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
        {
            LastRefreshTokenReceived = refreshToken;
            return Task.FromResult(refreshResponse ?? throw new InvalidOperationException("No refresh response configured."));
        }

        public Task RevokeTokenAsync(string token, CancellationToken cancellationToken)
        {
            if (revokeThrows)
                throw new HttpRequestException("Revoke failed.");
            LastRevokedToken = token;
            return Task.CompletedTask;
        }
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
