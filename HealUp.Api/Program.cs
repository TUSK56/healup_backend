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
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

QuestPDF.Settings.License = LicenseType.Community;

// For local development only: when no URL is configured, use a stable local port.
// In IIS/ANCM hosting (e.g., MonsterASP), do not force UseUrls because ANCM manages the port.
var hasAspNetCorePort = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_PORT"));
if (builder.Environment.IsDevelopment()
    && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS"))
    && !hasAspNetCorePort)
{
    builder.WebHost.UseUrls("http://127.0.0.1:8000");
}

var configuration = builder.Configuration;

// Database (SQL Server)
builder.Services.AddDbContext<HealUpDbContext>(options =>
    options.UseSqlServer(
        configuration.GetConnectionString("DefaultConnection"),
        sql =>
        {
            sql.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(5),
                errorNumbersToAdd: null);
            sql.CommandTimeout(30);
        }));

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

    // Nested controller DTOs share short names (e.g. ChangePasswordDto); default schema ids collide and break /swagger/v1/swagger.json.
    c.CustomSchemaIds(type => type.FullName?.Replace('+', '.') ?? type.Name);

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
var runStartupMaintenance = builder.Environment.IsDevelopment() || builder.Configuration.GetValue("StartupMaintenance:Enabled", false);
using (var scope = app.Services.CreateScope())
{
    if (runStartupMaintenance)
    {
        try
        {
            var db = scope.ServiceProvider.GetRequiredService<HealUpDbContext>();

            try
            {
                await db.Database.MigrateAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"HealUp: database migrate skipped: {ex.Message}");
            }

            // Align DB with model when EF migration wasn't applied yet (avoids failures on POST /api/orders).
            if ((db.Database.ProviderName ?? "").Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await db.Database.ExecuteSqlRawAsync(
                        """
                                        IF COL_LENGTH(N'dbo.requests', N'NotifiedPharmacyCount') IS NULL
                                            ALTER TABLE dbo.requests ADD NotifiedPharmacyCount int NOT NULL CONSTRAINT DF_requests_NotifiedPharmacyCount DEFAULT(0);

                                        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_requests_PatientId_CreatedAt' AND object_id = OBJECT_ID(N'dbo.requests'))
                                            CREATE INDEX IX_requests_PatientId_CreatedAt ON dbo.requests (PatientId, CreatedAt DESC);
                                        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_requests_Status_ExpiresAt' AND object_id = OBJECT_ID(N'dbo.requests'))
                                            CREATE INDEX IX_requests_Status_ExpiresAt ON dbo.requests (Status, ExpiresAt);
                                        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_pharmacy_responses_RequestId_CreatedAt' AND object_id = OBJECT_ID(N'dbo.pharmacy_responses'))
                                            CREATE INDEX IX_pharmacy_responses_RequestId_CreatedAt ON dbo.pharmacy_responses (RequestId, CreatedAt DESC);
                                        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_pharmacy_responses_PharmacyId_RequestId' AND object_id = OBJECT_ID(N'dbo.pharmacy_responses'))
                                            CREATE INDEX IX_pharmacy_responses_PharmacyId_RequestId ON dbo.pharmacy_responses (PharmacyId, RequestId);
                                        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_orders_PatientId_CreatedAt' AND object_id = OBJECT_ID(N'dbo.orders'))
                                            CREATE INDEX IX_orders_PatientId_CreatedAt ON dbo.orders (PatientId, CreatedAt DESC);
                                        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_orders_PharmacyId_CreatedAt' AND object_id = OBJECT_ID(N'dbo.orders'))
                                            CREATE INDEX IX_orders_PharmacyId_CreatedAt ON dbo.orders (PharmacyId, CreatedAt DESC);
                                        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_orders_RequestId_CreatedAt' AND object_id = OBJECT_ID(N'dbo.orders'))
                                            CREATE INDEX IX_orders_RequestId_CreatedAt ON dbo.orders (RequestId, CreatedAt DESC);
                                        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_notifications_PatientId_IsRead_CreatedAt' AND object_id = OBJECT_ID(N'dbo.notifications'))
                                            CREATE INDEX IX_notifications_PatientId_IsRead_CreatedAt ON dbo.notifications (PatientId, IsRead, CreatedAt DESC);
                                        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_notifications_PharmacyId_IsRead_CreatedAt' AND object_id = OBJECT_ID(N'dbo.notifications'))
                                            CREATE INDEX IX_notifications_PharmacyId_IsRead_CreatedAt ON dbo.notifications (PharmacyId, IsRead, CreatedAt DESC);

                    IF COL_LENGTH(N'dbo.orders', N'PreparingAt') IS NULL
                      ALTER TABLE dbo.orders ADD PreparingAt datetime2 NULL;
                    IF COL_LENGTH(N'dbo.orders', N'PaymentMethod') IS NULL
                      ALTER TABLE dbo.orders ADD PaymentMethod nvarchar(256) NULL;
                    ELSE
                      ALTER TABLE dbo.orders ALTER COLUMN PaymentMethod nvarchar(256) NULL;
                    IF COL_LENGTH(N'dbo.orders', N'DeliveryAddressSnapshot') IS NULL
                      ALTER TABLE dbo.orders ADD DeliveryAddressSnapshot nvarchar(500) NULL;
                                        IF COL_LENGTH(N'dbo.orders', N'CouponCode') IS NULL
                                            ALTER TABLE dbo.orders ADD CouponCode nvarchar(50) NULL;
                                        ELSE
                                            ALTER TABLE dbo.orders ALTER COLUMN CouponCode nvarchar(50) NULL;
                                        IF COL_LENGTH(N'dbo.orders', N'CouponPercent') IS NULL
                                            ALTER TABLE dbo.orders ADD CouponPercent decimal(5,2) NULL;
                                        ELSE
                                            ALTER TABLE dbo.orders ALTER COLUMN CouponPercent decimal(5,2) NULL;

                                        IF COL_LENGTH(N'dbo.notifications', N'AdminId') IS NULL
                                            ALTER TABLE dbo.notifications ADD AdminId int NULL;
                        """);

                    await db.Database.ExecuteSqlRawAsync(
                        """
                    IF COL_LENGTH(N'dbo.notifications', N'AdminId') IS NOT NULL
                    AND NOT EXISTS (
                        SELECT 1
                        FROM sys.foreign_keys
                        WHERE name = N'FK_notifications_admins_AdminId'
                    )
                    BEGIN
                      ALTER TABLE dbo.notifications WITH CHECK
                      ADD CONSTRAINT FK_notifications_admins_AdminId
                      FOREIGN KEY (AdminId) REFERENCES dbo.admins (Id);
                    END
                        """);

                    await db.Database.ExecuteSqlRawAsync(
                        """
                    IF OBJECT_ID(N'dbo.pharmacy_declined_requests', N'U') IS NULL
                    BEGIN
                      CREATE TABLE dbo.pharmacy_declined_requests (
                        Id int NOT NULL IDENTITY(1,1),
                        PharmacyId int NOT NULL,
                        RequestId int NOT NULL,
                        CreatedAt datetime2 NOT NULL,
                        CONSTRAINT PK_pharmacy_declined_requests PRIMARY KEY (Id),
                        CONSTRAINT FK_pharmacy_declined_requests_pharmacies_PharmacyId FOREIGN KEY (PharmacyId) REFERENCES dbo.pharmacies (Id) ON DELETE CASCADE,
                        CONSTRAINT FK_pharmacy_declined_requests_requests_RequestId FOREIGN KEY (RequestId) REFERENCES dbo.requests (Id) ON DELETE CASCADE
                      );
                      CREATE UNIQUE INDEX IX_pharmacy_declined_requests_PharmacyId_RequestId ON dbo.pharmacy_declined_requests (PharmacyId, RequestId);
                    END
                        """);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"HealUp: optional schema patch skipped: {ex.Message}");
                }
            }

            try
            {
                await db.ExpireOldRequestsAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"HealUp: request expiry skipped: {ex.Message}");
            }

            // Dev/testing admin bootstrap for clean databases.
            var adminEmail = configuration["AdminSeed:Email"];
            var adminPassword = configuration["AdminSeed:Password"];
            var adminName = configuration["AdminSeed:Name"] ?? "HealUp Admin";
            if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
            {
                try
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
                catch (Exception ex)
                {
                    Console.WriteLine($"HealUp: admin seed skipped: {ex.Message}");
                }
            }

            try
            {
                await DemoDataSeeder.SeedAsync(db, configuration);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"HealUp: demo seed skipped: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"HealUp: startup db init skipped: {ex.Message}");
        }
    }
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

app.MapGet("/", () => Results.Ok(new
{
    app = "HealUp.Api",
    status = "running",
    environment = app.Environment.EnvironmentName
})).AllowAnonymous();

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    timeUtc = DateTime.UtcNow
})).AllowAnonymous();

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
