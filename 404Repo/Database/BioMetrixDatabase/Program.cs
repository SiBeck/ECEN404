using BioMetrixDatabase.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── Services ──────────────────────────────────────────────────────────────────

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "BioMetrix API", Version = "v1",
        Description = "REST API for BioMetrix patient and imaging data (SQLite backend)" });
});

// Register SQLite database context
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=biometrix.db";

builder.Services.AddDbContext<BioMetrixDbContext>(options =>
    options.UseSqlite(connectionString));

// Allow desktop app and web portal (localhost) to call the API without CORS errors
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost", "https://localhost",
                           "http://localhost:5168", "http://localhost:7000")
              .AllowAnyMethod()
              .AllowAnyHeader());
});

// ── Pipeline ──────────────────────────────────────────────────────────────────

var app = builder.Build();

// Ensure the SQLite database and schema exist on every startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BioMetrixDbContext>();
    db.Database.EnsureCreated();
}

app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "BioMetrix API v1"));

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseCors();
app.UseAuthorization();
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
