using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using PatientPortal.Data;
using PatientPortal.Services;

// Explicitly qualify the method call to resolve ambiguity
using SeedDataNamespace = PatientPortal.Data.SeedData;

var builder = WebApplication.CreateBuilder(args);

// ── BioMetrixDatabase integration ─────────────────────────────────────────────
// Registers a typed HttpClient for BioMetrixDatabaseService. The client uses
// a service-account JWT (obtained at first use) for server-to-server calls.
// Set BioMetrixDatabase:BaseUrl in appsettings to point at the running API.
builder.Services.AddHttpClient<IBioMetrixDatabaseService, BioMetrixDatabaseService>(client =>
{
    var baseUrl = builder.Configuration["BioMetrixDatabase:BaseUrl"] ?? "https://localhost:5001";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    // Allow self-signed certs in dev; enforce valid certs in production
    ServerCertificateCustomValidationCallback =
        builder.Environment.IsDevelopment()
            ? HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            : null
});

// Add services
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

// Database - points to the same database as the API
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();

// Application Services
builder.Services.AddScoped<IPatientAuthService, PatientAuthService>();
builder.Services.AddScoped<IPatientFileService, PatientFileService>();
builder.Services.AddScoped<IStorageService, FileSystemStorageService>();
builder.Services.AddSingleton<IEncryptionService, EncryptionService>();

// Session
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddHttpContextAccessor();

var app = builder.Build();

// Middleware pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseSession();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        // Ensure migrations are applied (optional; remove if you manage migrations separately)
        await db.Database.MigrateAsync();
        // Run the seed routine you added earlier
        await SeedDataNamespace.EnsureSeedDataAsync(db);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
    }
}

app.Run();
