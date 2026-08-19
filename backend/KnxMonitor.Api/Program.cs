using System.Globalization;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using KnxMonitor.Infrastructure.Data;
using KnxMonitor.Core.Interfaces;
using KnxMonitor.Core.Models;
using KnxMonitor.Core.Services;
using KnxMonitor.Infrastructure.Repositories;
using KnxMonitor.Infrastructure.Services;
using KnxMonitor.Infrastructure.KnxConnection;
using KnxMonitor.Infrastructure;
using KnxMonitor.Infrastructure.Logging;
using KnxMonitor.Api.Hubs;
using KnxMonitor.Api.Services;
using KnxMonitor.Api.Logging;
using Scalar.AspNetCore;
using Serilog;

// Pin the process culture to English so Falcon's DPT enum labels (On/Off, Open/Close,
// Active/Inactive …) and number formatting are language-stable regardless of host locale.
// Must run before any Falcon master-data access (it may cache the language on first use).
var enCulture = CultureInfo.GetCultureInfo("en");
CultureInfo.DefaultThreadCurrentCulture = enCulture;
CultureInfo.DefaultThreadCurrentUICulture = enCulture;
CultureInfo.CurrentCulture = enCulture;
CultureInfo.CurrentUICulture = enCulture;

// Resolve the data directory relative to the executable (not the cwd), then ensure it exists.
AppPaths.EnsureDirectories();

// Log level controllable at runtime via KNX_LOG_LEVEL (Verbose/Debug/Information/Warning/Error/Fatal).
var logLevelSwitch = new Serilog.Core.LoggingLevelSwitch(ParseLogLevel(Environment.GetEnvironmentVariable("KNX_LOG_LEVEL")));

// In-memory ring buffer feeding the live in-app log viewer (also registered in DI below).
var logBuffer = new LogBuffer();

// Configure Serilog: console + rolling file (./data/logs) + in-app buffer sink.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.ControlledBy(logLevelSwitch)
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        Path.Combine(AppPaths.LogsDir, "knxmonitor-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        fileSizeLimitBytes: 50_000_000,
        rollOnFileSizeLimit: true,
        shared: true,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .WriteTo.Sink(new BufferSink(logBuffer))
    .CreateLogger();

// Versions 0.8.0 - 0.8.2 wrote into the single-file bundle's extraction directory instead of the
// data directory next to the executable (see AppPaths). Report a database stranded there
// unconditionally: it is just as relevant when the data directory already holds an older database,
// because that is the case where the newer history would otherwise be lost without a word.
if (AppPaths.FindStrandedDbPath() is { } strandedDb)
{
    Log.Warning(
        "A database from an older version is still present at {StrandedPath} and is NOT being used "
        + "- the data directory is {DataDir}. Versions 0.8.0-0.8.2 wrote there by mistake. To keep "
        + "that history: stop the app, back up {DataDir}, then move the file (plus any -wal/-shm "
        + "files and the archive folder next to it) into it.",
        strandedDb, AppPaths.DataDir);
}

// Anchor the content root to the executable too. It defaults to the current working directory and
// drives both appsettings.json discovery and the wwwroot lookup — so launching the portable binary
// from anywhere but its own folder would silently serve no frontend and fall back to default
// configuration (the Jwt section among it).
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppPaths.AppDirectory
});

// Use Serilog for logging
builder.Host.UseSerilog();

// Generate or load JWT secret (stored next to the executable under ./data).
var jwtSecret = KnxMonitor.Infrastructure.Services.JwtSecretManager.GetOrGenerateSecret(AppPaths.DataDir);
Log.Information("JWT secret loaded/generated successfully");

// Override JWT Secret in configuration
builder.Configuration["Jwt:Secret"] = jwtSecret;

// Anchor the SQLite DB to the executable-relative data dir (not the cwd) so the portable
// binary always opens the same database regardless of launch directory.
builder.Configuration["ConnectionStrings:DefaultConnection"] = $"Data Source={AppPaths.DbPath}";

// In-app log viewer buffer (fed by the Serilog BufferSink configured above).
builder.Services.AddSingleton(logBuffer);

// Bound request body size (project uploads) to avoid memory exhaustion.
const long MaxUploadBytes = 200L * 1024 * 1024; // 200 MB
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = MaxUploadBytes;
});
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = MaxUploadBytes);

// Standard-Adresse 0.0.0.0:8080, aber nur wenn wirklich nichts anderes eine Adresse setzt.
// Sie darf nicht in appsettings.json stehen: ein dort konfigurierter Kestrel-Endpunkt sticht die
// Adressliste und machte ASPNETCORE_URLS damit wirkungslos (#9). Muss vor builder.Build() laufen —
// app.Urls.Add() danach wirft im OpenAPI-Generator (genau dafür gibt es BoundUrls()), und
// ConfigureKestrel(o => o.ListenAnyIP(...)) ließe app.Urls leer, womit Startmeldung und
// Browser-Start still nichts mehr täten.
if (HostingUrls.ResolveFallbackListenUrl(builder.Configuration) is { } fallbackUrl)
{
    builder.WebHost.UseUrls(fallbackUrl);
}

// Rate limiting — throttle the anonymous auth endpoints against brute force.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("auth", o =>
    {
        o.PermitLimit = 10;
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueLimit = 0;
    });
});

// Add services to the container.
// Database
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlite(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqliteOptions =>
        {
            // Use split queries for better performance when loading multiple collections
            sqliteOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
        });
    // Enable WAL (+ synchronous=NORMAL) on every opened connection.
    options.AddInterceptors(new SqliteWalConnectionInterceptor());
});

// JWT Settings
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>();

// Authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings?.Issuer,
        ValidAudience = jwtSettings?.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtSettings?.Secret ?? throw new InvalidOperationException("JWT Secret not configured"))),
        // Default sind 5 Minuten: der Server akzeptiert einen 15-Minuten-Token dann
        // faktisch 20, während das Frontend ihn nach 15 für abgelaufen hält. Beide
        // Seiten sollen dieselbe Grenze sehen; 30 s bleiben für Uhrendrift.
        ClockSkew = TimeSpan.FromSeconds(30)
    };

    // Allow JWT token in query string for SignalR (WebSockets don't support Authorization header)
    options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];

            // If the request is for our hub...
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                // Read the token out of the query string
                context.Token = accessToken;
            }

            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

// CORS — only needed for `ng serve` against the API in Development.
// In Production the frontend is served from the same origin (wwwroot), so no CORS is required.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowFrontend", policy =>
        {
            policy.WithOrigins("http://localhost:4200")
                  .AllowAnyHeader()
                  .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
                  .AllowCredentials();
        });
    });
}

// Repositories
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ITelegramRepository, TelegramRepository>();
builder.Services.AddScoped<IGroupAddressRepository, GroupAddressRepository>();
builder.Services.AddScoped<IProjectRepository, ProjectRepository>();
builder.Services.AddScoped<IKnxConfigurationRepository, KnxConfigurationRepository>();
builder.Services.AddScoped<IRecordingSettingsRepository, RecordingSettingsRepository>();
builder.Services.AddScoped<IMonitorHeartbeatRepository, MonitorHeartbeatRepository>();

// Live-applied recording settings (cached snapshot, single source of truth)
builder.Services.AddSingleton<IRecordingSettingsProvider, RecordingSettingsProvider>();

// Services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IProjectService, ProjectService>();

// Parser Library Services (Singleton for performance)
builder.Services.AddSingleton<KnxMonitor.ProjectParser.Core.Interfaces.IFeatureDetector,
    KnxMonitor.ProjectParser.Services.FeatureDetector>();

// Register all ETS version loaders
builder.Services.AddSingleton<KnxMonitor.ProjectParser.Core.Interfaces.IProjectLoader,
    KnxMonitor.ProjectParser.Loaders.Ets4ProjectLoader>();

builder.Services.AddSingleton<KnxMonitor.ProjectParser.Core.Interfaces.IProjectLoader,
    KnxMonitor.ProjectParser.Loaders.Ets5ProjectLoader>();

builder.Services.AddSingleton<KnxMonitor.ProjectParser.Core.Interfaces.IProjectLoader,
    KnxMonitor.ProjectParser.Loaders.Ets6ProjectLoader>();

builder.Services.AddSingleton<KnxMonitor.ProjectParser.Core.Interfaces.IKeyringReader,
    KnxMonitor.ProjectParser.Services.KeyringReader>();

builder.Services.AddSingleton<KnxMonitor.ProjectParser.Core.Interfaces.IProjectParser,
    KnxMonitor.ProjectParser.Services.ProjectParser>();

// Infrastructure Adapter (uses Library)
builder.Services.AddScoped<IKnxProjectParserService, KnxProjectParserService>();

builder.Services.AddSingleton<IProjectCacheService, ProjectCacheService>();
builder.Services.AddSingleton<IKnxConnectionService, KnxConnectionService>();

// Keeps the bus link up automatically (startup + reconnect on loss).
builder.Services.AddHostedService<KnxAutoConnectWorker>();

// Telegram persistence: one bounded channel, drop-oldest under burst.
// The same instance backs the queue (writer) and the hosted worker (reader).
builder.Services.AddSingleton<TelegramPersistenceService>();
builder.Services.AddSingleton<ITelegramQueue>(sp => sp.GetRequiredService<TelegramPersistenceService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<TelegramPersistenceService>());

builder.Services.AddHostedService<TelegramBroadcastService>();

// Liveness record: one beat per minute so gaps in the telegram history can be explained
// afterwards (quiet bus vs. lost link vs. process not running).
builder.Services.AddHostedService<MonitorHeartbeatWorker>();

// Pushes log entries from the buffer to the LogHub for the live in-app viewer.
builder.Services.AddHostedService<LogBroadcastWorker>();

// Cold-tier archive: tees off the same telegram event, writes NDJSON+gzip day-files (opt-in).
builder.Services.AddSingleton<TelegramArchiveService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TelegramArchiveService>());

// Import Services
builder.Services.AddSingleton<IImportJobManager, ImportJobManager>();
builder.Services.AddHostedService<ImportJobCleanupService>();
builder.Services.AddHostedService<RefreshTokenCleanupService>();
builder.Services.AddScoped<IProjectFeatureDetector, ProjectFeatureDetector>();
builder.Services.AddScoped<ProjectImportService>();

// SignalR
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi(options =>
{
    // Ohne dieses Schema kennt die API-Referenz keinen Weg, einen Token zu setzen —
    // jeder geschützte Aufruf käme dort als 401 zurück.
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Components ??= new Microsoft.OpenApi.Models.OpenApiComponents();
        document.Components.SecuritySchemes["Bearer"] = new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        {
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = Microsoft.OpenApi.Models.ParameterLocation.Header,
            Description = "Access-Token aus POST /api/Auth/login (ohne \"Bearer \"-Präfix einfügen)."
        };
        return Task.CompletedTask;
    });

    // Beschreibungen kommen aus den XML-Doku-Kommentaren an den Controller-Methoden;
    // damit ist der Code die einzige Quelle und es gibt keine zweite, separat
    // gepflegte API-Doku, die still veralten kann.
    options.AddOperationTransformer<KnxMonitor.Api.OpenApi.XmlDocumentationTransformer>();

    // Nur die Endpunkte als geschützt ausweisen, die es wirklich sind — sonst
    // erscheinen Login und Health fälschlich als anmeldepflichtig.
    options.AddOperationTransformer((operation, context, _) =>
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        var anonymous = metadata.OfType<Microsoft.AspNetCore.Authorization.IAllowAnonymous>().Any();
        var authorized = metadata.OfType<Microsoft.AspNetCore.Authorization.IAuthorizeData>().Any();

        if (authorized && !anonymous)
        {
            operation.Security = new List<Microsoft.OpenApi.Models.OpenApiSecurityRequirement>
            {
                new()
                {
                    [new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                    {
                        Reference = new Microsoft.OpenApi.Models.OpenApiReference
                        {
                            Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    }] = Array.Empty<string>()
                }
            };
        }
        return Task.CompletedTask;
    });
});

// Controllers with JSON configuration
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Serialize enums as strings instead of numbers
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

var app = builder.Build();

// Database initialization and seeding
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        await KnxMonitor.Infrastructure.Data.DbInitializer.InitializeAsync(context);

        // Initialize group address cache
        var cacheService = app.Services.GetRequiredService<IProjectCacheService>();
        await cacheService.InitializeAsync();

        // Warm the recording-settings snapshot before the persistence worker's first retention pass.
        var recordingSettings = app.Services.GetRequiredService<IRecordingSettingsProvider>();
        await recordingSettings.InitializeAsync();
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogCritical(ex, "Fatal error during initialization — aborting startup.");
        // Fail fast: do NOT start serving with a broken/unmigrated database.
        throw;
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Lesbare API-Referenz über dem generierten Dokument: /scalar/v1.
    // Nur in Development — in Produktion liegt unter "/" das Angular-Frontend,
    // und die Endpunktliste gehört dort nicht ungefragt ins Netz.
    app.MapScalarApiReference(options =>
    {
        options.Title = "KNX-NG-Monitor API";
        // Die API ist JWT-geschützt; so kann man den Token in der Oberfläche
        // hinterlegen und Aufrufe direkt ausprobieren.
        options.AddPreferredSecuritySchemes("Bearer");
    });
}

// Serve static files (Angular frontend) in Production
if (app.Environment.IsProduction())
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

// Gebundene Adressen — leer, wenn kein echter Server dahintersteht. Genau das ist
// beim OpenAPI-Generator der Fall, der die Anwendung nur aufbaut, um das Dokument
// abzugreifen; ein ungeschützter Zugriff auf app.Urls lässt ihn scheitern.
List<string> BoundUrls()
{
    try { return app.Urls.ToList(); }
    catch (InvalidOperationException) { return new List<string>(); }
}

// Only use HTTPS redirection if HTTPS is configured
var httpsPort = builder.Configuration["HTTPS_PORT"];
if (!string.IsNullOrEmpty(httpsPort) || BoundUrls().Any(u => u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
{
    app.UseHttpsRedirection();
}

if (app.Environment.IsDevelopment())
{
    app.UseCors("AllowFrontend");
}

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

// Health check that actually probes the database (so a broken DB reports unhealthy).
app.MapGet("/healthz", async (ApplicationDbContext db) =>
    await db.Database.CanConnectAsync()
        ? Results.Ok("ok")
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable))
    .AllowAnonymous();

app.MapControllers();
app.MapHub<TelegramHub>("/hubs/telegram");
app.MapHub<LogHub>("/hubs/logs");

// Fallback to index.html for Angular routing (SPA) in Production
if (app.Environment.IsProduction())
{
    app.MapFallbackToFile("index.html");
}

try
{
    Log.Information("Starting KNX Monitor API");

    // Die Startmeldung hängt am ApplicationStarted-Ereignis statt an einem
    // Hintergrund-Task mit fester Wartezeit: Erst dort stehen die tatsächlich
    // gebundenen Adressen fest, und `app.Run()` bleibt der reguläre, synchrone
    // Aufruf. Letzteres braucht der OpenAPI-Generator, der die Anwendung nur baut
    // und den Start unterbindet — mit `Task.Run(() => app.Run())` lief er ins Leere.
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        // Ohne echten Server gibt es keine gebundenen Adressen — dann ist auch
        // nichts zu melden.
        var urls = BoundUrls();
        if (urls.Count == 0)
        {
            return;
        }

        var primaryUrl = urls[0];

        // Platzhalter-Host auf localhost drehen, der Browser kann mit 0.0.0.0 nichts anfangen.
        // Seit #9 wirkt ASPNETCORE_URLS auch im Container, und dessen http://+:8080 taucht hier
        // als gebundenes http://[::]:8080 auf — das traf das frühere Replace("0.0.0.0", …) nicht.
        var displayUrl = HostingUrls.ToBrowsableUrl(primaryUrl);

        // Log the URL(s) - modern terminals will make these clickable
        Log.Information("====================================");
        Log.Information("KNX Monitor API is running!");
        Log.Information("Server listening on: " + primaryUrl);

        if (app.Environment.IsDevelopment())
        {
            Log.Information("Backend API: {Url}", displayUrl);
            Log.Information("Frontend Dev Server: http://localhost:4200");
            Log.Information("Note: In Development, start the frontend separately with 'ng serve'");
        }
        else
        {
            Log.Information("Access the application at: {Url}", displayUrl);
            foreach (var url in urls.Skip(1))
            {
                var altDisplayUrl = HostingUrls.ToBrowsableUrl(url);
                Log.Information("Alternative URL: {Url}", altDisplayUrl);
            }
        }

        Log.Information("====================================");

        // Check if we should open the browser (only in Production)
        if (ShouldOpenBrowser(app.Environment))
        {
            Log.Information("Opening browser...");
            OpenBrowser(primaryUrl);
        }
    });

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

static Serilog.Events.LogEventLevel ParseLogLevel(string? value) =>
    Enum.TryParse<Serilog.Events.LogEventLevel>(value, ignoreCase: true, out var level)
        ? level
        : Serilog.Events.LogEventLevel.Information;

static bool ShouldOpenBrowser(IWebHostEnvironment environment)
{
    // Only open browser in Production (where frontend is served by backend)
    if (environment.IsDevelopment())
    {
        return false;
    }

    // Don't open browser in Docker
    if (IsRunningInDocker())
    {
        return false;
    }

    // Don't open browser if not running interactively
    if (!Environment.UserInteractive)
    {
        return false;
    }

    // Don't open browser if output is redirected (piped to file, etc.)
    if (Console.IsOutputRedirected)
    {
        return false;
    }

    return true;
}

static bool IsRunningInDocker()
{
    // Check for Docker environment indicator
    if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
    {
        return true;
    }

    // Alternative check: Look for .dockerenv file (Linux containers)
    if (File.Exists("/.dockerenv"))
    {
        return true;
    }

    // Alternative check: Look for docker in cgroup (Linux)
    try
    {
        if (File.Exists("/proc/1/cgroup"))
        {
            var cgroup = File.ReadAllText("/proc/1/cgroup");
            if (cgroup.Contains("docker") || cgroup.Contains("containerd"))
            {
                return true;
            }
        }
    }
    catch
    {
        // Ignore errors reading cgroup
    }

    return false;
}

static void OpenBrowser(string url)
{
    try
    {
        // Platzhalter-Host (0.0.0.0, [::], +, *) auf localhost drehen — sonst öffnet der Browser
        // eine Adresse, die er nicht auflösen kann.
        url = HostingUrls.ToBrowsableUrl(url);

        if (OperatingSystem.IsWindows())
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        else if (OperatingSystem.IsLinux())
        {
            System.Diagnostics.Process.Start("xdg-open", url);
        }
        else if (OperatingSystem.IsMacOS())
        {
            System.Diagnostics.Process.Start("open", url);
        }
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Could not open browser automatically");
    }
}
