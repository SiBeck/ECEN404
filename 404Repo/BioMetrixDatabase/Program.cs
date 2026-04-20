using MedicalImagingAPI.Database;
using MedicalImagingAPI.Models;
using MedicalImagingAPI.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Configure Settings
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("JwtSettings"));
builder.Services.Configure<StorageSettings>(builder.Configuration.GetSection("Storage"));

// Add DbContext with SQLite
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// Register Services (EACH SERVICE ONLY ONCE)
builder.Services.AddScoped<IUserAuthenticationService, UserAuthenticationService>();
builder.Services.AddScoped<IPatientService, PatientService>();
builder.Services.AddScoped<IPatientFileService, PatientFileService>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IStorageService, FileSystemStorageService>(); // FIXED
builder.Services.AddSingleton<IEncryptionService, EncryptionService>();
builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddHttpContextAccessor();

// Add Authentication
var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>();
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
        ValidIssuer = jwtSettings.Issuer,
        ValidAudience = jwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
        ClockSkew = TimeSpan.Zero
    };
});

// Add Authorization
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdmin", policy => policy.RequireRole("Admin"));
    options.AddPolicy("RequirePhysician", policy => policy.RequireRole("Admin", "Physician"));
    options.AddPolicy("RequireMedicalStaff", policy => policy.RequireRole("Admin", "Physician", "Nurse", "Technician"));
    options.AddPolicy("RequireAnyUser", policy => policy.RequireAuthenticatedUser());
});

// Allow PatientPortal and DesktopApp to reach this API.
// Origins are read from configuration so production values can be set via
// environment variables without code changes.
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? new[] { "https://localhost:7000", "http://localhost:5100" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("MedicalSystemPolicy", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Medical Imaging API",
        Version = "v1"
    });
});

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 524288000; // 500 MB
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 524288000; // 500 MB
});

var app = builder.Build();

// Seed database with initial data
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();

    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();

        // Create database if it doesn't exist
        logger.LogInformation("Ensuring database is created...");
        context.Database.EnsureCreated();

        // Seed data
        logger.LogInformation("Seeding database...");
        await DatabaseSeeder.SeedAsync(context, services);

        logger.LogInformation("Database seeding completed successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while seeding the database: {Message}", ex.Message);

        // Print inner exception for more detail
        if (ex.InnerException != null)
        {
            logger.LogError(ex.InnerException, "Inner exception: {Message}", ex.InnerException.Message);
        }
    }
}
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("MedicalSystemPolicy");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();