using System.Text;
using HealUp.Api.Data;
using HealUp.Api.Hubs;
using HealUp.Api.Models;
using HealUp.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// When ASPNETCORE_URLS is unset, avoid Kestrel's default http://localhost:5000 (often already in use).
// `dotnet run` normally sets this from Properties/launchSettings.json to http://127.0.0.1:8000.
if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls("http://127.0.0.1:8000");
}

var configuration = builder.Configuration;

// Database (SQL Server)
builder.Services.AddDbContext<HealUpDbContext>(options =>
    options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

// JWT
var jwtSection = configuration.GetSection("Jwt");
var key = Encoding.UTF8.GetBytes(jwtSection["Key"] ?? throw new InvalidOperationException("Jwt:Key missing"));

builder.Services
    .AddAuthentication(options =>
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
            ValidIssuer = jwtSection["Issuer"],
            ValidAudience = jwtSection["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(key),
            // Avoid rejecting valid tokens when server/client clocks differ slightly.
            ClockSkew = TimeSpan.FromMinutes(10),
        };

        // Allow JWT auth for SignalR websocket connections
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var accessToken = ctx.Request.Query["access_token"];
                var path = ctx.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/notifications"))
                {
                    ctx.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// Domain services
builder.Services.AddScoped<CloudinaryService>();
builder.Services.AddScoped<GoogleMapsService>();
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<NotificationService>();

builder.Services.AddSignalR();

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        // Always return validation errors in a consistent HealUp format
        options.InvalidModelStateResponseFactory = ctx =>
        {
            var errors = ctx.ModelState
                .Where(x => x.Value?.Errors.Count > 0)
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
                );

            return new UnprocessableEntityObjectResult(new
            {
                message = "HealUp: Validation failed.",
                errors
            });
        };
    });

// CORS for Next.js frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        var origins = configuration.GetSection("Frontend:Origins").Get<string[]>()
            ?? (configuration["Frontend:Origin"] is { Length: > 0 } single ? new[] { single } : new[] { "http://localhost:3000", "http://localhost:3001" });

        policy.WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// Swagger / OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "HealUp API",
        Version = "v1",
        Description = "HealUp Medicine Request Platform – .NET 8 Web API"
    });

    var securityScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "HealUp JWT Bearer token. Example: \"Bearer {token}\""
    };
    c.AddSecurityDefinition("Bearer", securityScheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            securityScheme,
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Apply EF migrations (creates tables on first run against an empty DB — required for MonsterASP / new servers).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<HealUpDbContext>();
    await db.Database.MigrateAsync();

    await db.ExpireOldRequestsAsync();

    // Dev/testing admin bootstrap for clean databases.
    var adminEmail = configuration["AdminSeed:Email"];
    var adminPassword = configuration["AdminSeed:Password"];
    var adminName = configuration["AdminSeed:Name"] ?? "HealUp Admin";
    if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
    {
        var exists = await db.Admins.AnyAsync(a => a.Email == adminEmail);
        if (!exists)
        {
            db.Admins.Add(new Admin
            {
                Name = adminName,
                Email = adminEmail,
                PasswordHash = PasswordHasher.HashPassword(adminPassword)
            });
            await db.SaveChangesAsync();
        }
    }

    await DemoDataSeeder.SeedAsync(db, configuration);
}

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "HealUp API v1");
    c.DocumentTitle = "HealUp API Documentation";
});

app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapHub<NotificationHub>("/hubs/notifications");

// One-time hosted DB fill (same data as local DemoSeed). Configure DemoSeed:SetupKey (12+ chars), then POST with header X-HealUp-Setup-Key. Remove SetupKey after use.
app.MapPost(
        "/api/setup/seed-demo-data",
        async (HttpContext http, IConfiguration cfg, IServiceProvider services) =>
        {
            var setupKey = cfg["DemoSeed:SetupKey"];
            if (string.IsNullOrWhiteSpace(setupKey) || setupKey.Length < 12)
                return Results.NotFound();

            if (!http.Request.Headers.TryGetValue("X-HealUp-Setup-Key", out var sent) ||
                !string.Equals(sent.ToString(), setupKey, StringComparison.Ordinal))
                return Results.Unauthorized();

            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HealUpDbContext>();
            var password = cfg["DemoSeed:Password"] ?? "Demo@2026";
            var (inserted, detail) = await DemoDataSeeder.TrySeedDemoDataAsync(db, password, http.RequestAborted);
            return Results.Ok(new
            {
                inserted,
                detail,
                logins = new
                {
                    patients = "patient1@demo.healup.local … patient5@demo.healup.local",
                    pharmacies = "pharmacy1@demo.healup.local … pharmacy5@demo.healup.local",
                    note = "Password is DemoSeed:Password from server config (see frontend README; default Demo@2026)."
                }
            });
        })
    .AllowAnonymous();

app.Run();

