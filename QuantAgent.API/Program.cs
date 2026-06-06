using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using QuantAgent.API.Data;
using QuantAgent.API.Jobs;
using QuantAgent.API.Services;
using QuantAgent.API.Services.Telegram;
using QuantAgent.API.Services.Inference;
using QuantAgent.API.Services.Scraping;

using Telegram.Bot;

var builder = WebApplication.CreateBuilder(args);

// --- Services ----------------------------------------------------------------
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddCors(options =>
    options.AddPolicy("AllowNextJs", policy =>
        policy.WithOrigins("http://localhost:3000", "http://localhost:4000", "http://100.78.144.4:4000")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials()));
builder.Services.AddSwaggerGen();

// Database (PostgreSQL via Npgsql + EF Core)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");
builder.Services.AddDbContext<QuantDbContext>(options =>
    options.UseNpgsql(
        connectionString,
        npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history")));

// Hangfire (PostgreSQL-backed job storage)
builder.Services.AddHangfire(cfg => cfg
    .UsePostgreSqlStorage(
        bootstrapper => bootstrapper.UseNpgsqlConnection(connectionString, _ => { }),
        new PostgreSqlStorageOptions())
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings());
builder.Services.AddHangfireServer();

// Background jobs (resolved per-execution by Hangfire from DI)
builder.Services.AddScoped<MatchIngestionJob>();
builder.Services.AddScoped<PostMatchVerificationJob>();
builder.Services.AddScoped<ValueBetDetectionJob>();
builder.Services.AddScoped<SelfReflectionJob>();
builder.Services.AddScoped<KeyHealthCheckJob>();

// Typed HTTP client for API-Football v3. The API key is now resolved
// dynamically via IKeyRotationService (Order #34), so the
// x-apisports-key header is set per-request, not globally.
builder.Services.AddHttpClient<FootballApiService>(client =>
{
    client.BaseAddress = new Uri("https://v3.football.api-sports.io/");
});
builder.Services.AddSingleton<IKeyRotationService, KeyRotationService>();
builder.Services.AddSingleton<IBankrollManagementService, BankrollManagementService>();

// Telegram bot (singleton — wraps a long-lived HttpClient)
builder.Services.AddSingleton<ITelegramBotClient>(sp =>
{
    var token = builder.Configuration["TelegramBot:Token"]
        ?? throw new InvalidOperationException("TelegramBot:Token is required.");
    return new TelegramBotClient(token);
});
builder.Services.AddSingleton<ITelegramNotificationService, TelegramNotificationService>();
builder.Services.AddHostedService<TelegramListenerService>();

// SignalR hub for real-time dashboard telemetry (Order #36)
builder.Services.AddSignalR();
builder.Services.AddSingleton<ITelemetryService, TelemetryService>();

// Inference: local Ollama (llama3.2). Read endpoint from configuration,
// defaulting to http://127.0.0.1:11434/ for local dev. In production Docker,
// set Ollama__Endpoint=http://<tailscale-ip>:11434 to reach the GPU host.
var ollamaEndpoint = builder.Configuration["Ollama:Endpoint"]
    ?? "http://127.0.0.1:11434/";
builder.Services.AddHttpClient<OllamaApiClient>(client =>
{
    client.BaseAddress = new Uri(ollamaEndpoint);
    client.Timeout = TimeSpan.FromMinutes(2);
});
builder.Services.AddScoped<IOllamaInferenceService, OllamaInferenceService>();

// Scraper: SoccerStats.com — real-time team stats used as RAG context for Ollama.
// Registered as Singleton so the per-league cache lives process-wide (one
// 3-URL fan-out per league, hit on every subsequent team lookup).
// The HttpClient is configured with a realistic browser User-Agent inside
// the service's constructor to avoid 403/Cloudflare blocks.
builder.Services.AddSingleton<ISoccerStatsScraperService, SoccerStatsScraperService>(sp =>
{
    var client = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(30),
    };
    var logger = sp.GetRequiredService<ILogger<SoccerStatsScraperService>>();
    return new SoccerStatsScraperService(client, logger);
});

// Order #37: Multi-source odds pipeline — TheOddsAPI as secondary provider,
// odds comparator for cross-source validation, and data aggregator coordinator.
builder.Services.AddHttpClient<IAlternativeOddsService, AlternativeOddsService>(client =>
{
    client.BaseAddress = new Uri("https://api.the-odds-api.com/");
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddSingleton<OddsComparatorService>();
builder.Services.AddScoped<IDataAggregatorService, DataAggregatorService>();

// Order #37: Playwright-based sentiment scraper + Ollama sentiment analysis.
builder.Services.AddScoped<ISentimentScraperService, SentimentScraperService>();
builder.Services.AddScoped<ISentimentAnalysisService, SentimentAnalysisService>();

// Order #38: circuit breaker + heartbeat + discrepancy detection
builder.Services.AddSingleton<ISafetyValveService, SafetyValveService>();
builder.Services.AddHostedService<HeartbeatService>();

var app = builder.Build();

// --- Pipeline ----------------------------------------------------------------
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowNextJs");
app.UseAuthorization();
app.MapControllers();
app.MapHub<QuantAgent.API.Hubs.LoggingHub>(QuantAgent.API.Hubs.LoggingHub.Route);

// Hangfire dashboard (server is started via AddHangfireServer above)
app.MapHangfireDashboard("/hangfire");

// Recurring ingestion — every day at 08:00 UTC
RecurringJob.AddOrUpdate<MatchIngestionJob>(
    "daily-match-ingestion",
    x => x.ExecuteAsync(default),
    "0 8 * * *",
    new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

// Recurring key health check — every 24 hours at 12:00 UTC
// Tests limited API keys and reactivates them when the rate window resets.
RecurringJob.AddOrUpdate<KeyHealthCheckJob>(
    "key-health-check",
    x => x.ExecuteAsync(CancellationToken.None),
    "0 12 * * *",
    new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

// E2E test trigger: fire the recurring job immediately on startup so the full pipeline
// (ingestion → analysis → Ollama inference → Telegram) can be observed without waiting
// for the next 08:00 UTC slot. Remove (or guard with env var) before production.
        // Uncommented for Order #12 E2E test — will re-comment after.
        // RecurringJob.TriggerJob("daily-match-ingestion");


// Health check endpoint — Coolify zero-downtime + container orchestration
app.MapGet("/health", async (QuantDbContext db) =>
{
    try
    {
        await db.Database.CanConnectAsync();
        return Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }
    catch (Exception ex)
    {
        return Results.Problem(
            detail: ex.Message,
            statusCode: 503,
            title: "Database connection failed");
    }
});

app.Run();
