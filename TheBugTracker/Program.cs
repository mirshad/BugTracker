using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Text;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);


// Add services
builder.Services.AddControllers().AddJsonOptions(x =>
   x.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.Preserve); ;
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure SQLite
builder.Services.AddDbContext<BugTrackerContext>(options =>
    options.UseSqlite("Data Source=bugtracker.db"));

// Configure JWT Authentication
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var key = Encoding.ASCII.GetBytes(jwtSettings["SecretKey"] ?? "YourSuperSecretKeyForBugTrackerApp123456789");

builder.Services.AddAuthentication(x =>
{
    x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(x =>
{
    x.RequireHttpsMetadata = false;
    x.SaveToken = true;
    x.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = false,
        ValidateAudience = false
    };
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        builder => builder.AllowAnyOrigin()
                         .AllowAnyMethod()
                         .AllowAnyHeader());
});

var app = builder.Build();

// Initialize database
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<BugTrackerContext>();
    context.Database.EnsureCreated();
    DatabaseInitializer.Initialize(context);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();
app.UseStaticFiles();
app.MapControllers();

app.Run();

// Database Context
public class BugTrackerContext : DbContext
{
    public BugTrackerContext(DbContextOptions<BugTrackerContext> options) : base(options) { }

    public DbSet<User> Users { get; set; }
    public DbSet<Bug> Bugs { get; set; }
    public DbSet<BugScreenshot> BugScreenshots { get; set; }
    public DbSet<DevComment> DevComments { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Bug>()
            .HasMany(b => b.Screenshots)
            .WithOne(s => s.Bug)
            .HasForeignKey(s => s.BugId);

        modelBuilder.Entity<Bug>()
            .HasMany(b => b.Comments)
            .WithOne(c => c.Bug)
            .HasForeignKey(c => c.BugId);
    }
}

// Models
public class User
{
    public int Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = "Guest"; // Admin, Tester, Dev, Guest
}

public class Bug
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string WebPage { get; set; } = string.Empty;
    public string Severity { get; set; } = "Low"; // Low, Medium, High, Critical
    public string Status { get; set; } = "Open"; // Open, In Progress, Resolved, Closed
    public DateTime DateReported { get; set; } = DateTime.Now;
    public DateTime? DateResolved { get; set; }
    public string AssignedTo { get; set; } = string.Empty;
    public int ETA { get; set; } // in hours
    public List<BugScreenshot> Screenshots { get; set; } = new();
    public List<DevComment> Comments { get; set; } = new();
}

public class BugScreenshot
{
    public int Id { get; set; }
    public int BugId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; } = DateTime.Now;
    public Bug Bug { get; set; } = null!;
}

public class DevComment
{
    public int Id { get; set; }
    public int BugId { get; set; }
    public string Comment { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public Bug Bug { get; set; } = null!;
}

// Database Initializer
public static class DatabaseInitializer
{
    public static void Initialize(BugTrackerContext context)
    {
        if (!context.Users.Any())
        {
            context.Users.AddRange(
                new User { UserName = "admin", Password = "admin123", Email = "admin@bugtracker.com", Role = "Admin" },
                new User { UserName = "tester", Password = "tester123", Email = "tester@bugtracker.com", Role = "Tester" },
                new User { UserName = "developer", Password = "dev123", Email = "dev@bugtracker.com", Role = "Dev" },
                new User { UserName = "guest", Password = "guest123", Email = "guest@bugtracker.com", Role = "Guest" }
            );
            context.SaveChanges();
        }
    }
}