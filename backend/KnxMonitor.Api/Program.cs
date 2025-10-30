using System.Text;
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
using KnxMonitor.Api.Hubs;
using KnxMonitor.Api.Services;
using Serilog;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

// Use Serilog for logging
builder.Host.UseSerilog();

// Generate or load JWT secret
var jwtSecret = KnxMonitor.Infrastructure.Services.JwtSecretManager.GetOrGenerateSecret();
Log.Information("JWT secret loaded/generated successfully");

// Override JWT Secret in configuration
builder.Configuration["Jwt:Secret"] = jwtSecret;

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
            Encoding.UTF8.GetBytes(jwtSettings?.Secret ?? throw new InvalidOperationException("JWT Secret not configured")))
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

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Repositories
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ITelegramRepository, TelegramRepository>();
builder.Services.AddScoped<IGroupAddressRepository, GroupAddressRepository>();
builder.Services.AddScoped<IProjectRepository, ProjectRepository>();
builder.Services.AddScoped<IKnxConfigurationRepository, KnxConfigurationRepository>();

// Services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<IKnxProjectParserService, KnxProjectParserService>();
builder.Services.AddSingleton<IGroupAddressCacheService, GroupAddressCacheService>();
builder.Services.AddSingleton<IKnxConnectionService, KnxConnectionService>();
builder.Services.AddHostedService<TelegramBroadcastService>();

// Import Services
builder.Services.AddSingleton<IImportJobManager, ImportJobManager>();
builder.Services.AddScoped<IProjectFeatureDetector, ProjectFeatureDetector>();
builder.Services.AddScoped<IKnxSecureService, KnxSecureService>();
builder.Services.AddScoped<ProjectImportService>();

// SignalR
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

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
        var cacheService = app.Services.GetRequiredService<IGroupAddressCacheService>();
        await cacheService.InitializeAsync();
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred during initialization.");
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Serve static files (Angular frontend) in Production
if (app.Environment.IsProduction())
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

// Only use HTTPS redirection if HTTPS is configured
var httpsPort = builder.Configuration["HTTPS_PORT"];
if (!string.IsNullOrEmpty(httpsPort) || app.Urls.Any(u => u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
{
    app.UseHttpsRedirection();
}

app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<TelegramHub>("/hubs/telegram");

// Fallback to index.html for Angular routing (SPA) in Production
if (app.Environment.IsProduction())
{
    app.MapFallbackToFile("index.html");
}

try
{
    Log.Information("Starting KNX Monitor API");

    // Start the application in a background task
    var runTask = Task.Run(() => app.Run());

    // Wait a moment for Kestrel to start
    await Task.Delay(500);

    // Get the configured URLs and make them browser-friendly
    var urls = app.Urls;
    var primaryUrl = urls.FirstOrDefault() ?? "http://localhost:8080";

    // Convert 0.0.0.0 to localhost for display (0.0.0.0 doesn't work in browsers)
    var displayUrl = primaryUrl.Replace("0.0.0.0", "localhost");

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
        if (urls.Count > 1)
        {
            foreach (var url in urls.Skip(1))
            {
                var altDisplayUrl = url.Replace("0.0.0.0", "localhost");
                Log.Information("Alternative URL: {Url}", altDisplayUrl);
            }
        }
    }

    Log.Information("====================================");

    // Check if we should open the browser (only in Production)
    if (ShouldOpenBrowser(app.Environment))
    {
        Log.Information("Opening browser...");
        OpenBrowser(primaryUrl);
    }

    await runTask;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

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
        // Convert 0.0.0.0 to localhost for browser
        url = url.Replace("0.0.0.0", "localhost");

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
