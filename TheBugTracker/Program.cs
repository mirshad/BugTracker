using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(x =>
    x.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.Preserve);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

builder.Services.AddDbContext<BugTrackerContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Data Source=bugtracker.db"));

var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings["SecretKey"];
if (string.IsNullOrWhiteSpace(secretKey) || secretKey.Length < 32)
{
    if (builder.Environment.IsDevelopment())
    {
        secretKey = "DevOnly_ChangeMe_BugTrackerSecretKey_Min32Chars!";
    }
    else
    {
        throw new InvalidOperationException(
            "JwtSettings:SecretKey must be configured (min 32 characters) outside Development.");
    }
}

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
var jwtIssuer = jwtSettings["Issuer"] ?? "TheBugTracker";
var jwtAudience = jwtSettings["Audience"] ?? "TheBugTrackerUsers";

builder.Services.AddAuthentication(x =>
{
    x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(x =>
{
    x.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    x.SaveToken = true;
    x.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = signingKey,
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1)
    };
});

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "https://localhost:44347", "https://localhost:7035", "http://localhost:5019", "http://localhost:62843" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("AppCors", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader());
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<BugTrackerContext>();
    var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
    context.Database.EnsureCreated();
    DatabaseInitializer.Initialize(context, passwordHasher);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; script-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    await next();
});

app.UseCors("AppCors");
app.UseAuthentication();
app.UseAuthorization();

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Uploaded content must never execute as script/HTML in the browser
        if (ctx.File.PhysicalPath?.Contains($"{Path.DirectorySeparatorChar}uploads{Path.DirectorySeparatorChar}") == true)
        {
            ctx.Context.Response.Headers["Content-Disposition"] = "inline";
            ctx.Context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            var ext = Path.GetExtension(ctx.File.Name).ToLowerInvariant();
            if (ext is ".svg" or ".html" or ".htm" or ".js" or ".xml")
            {
                ctx.Context.Response.Headers["Content-Disposition"] = "attachment";
                ctx.Context.Response.ContentType = "application/octet-stream";
            }
        }
    }
});

app.MapControllers();
app.Run();

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

        modelBuilder.Entity<User>()
            .HasIndex(u => u.UserName)
            .IsUnique();
    }
}

public class User
{
    public int Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    /// <summary>Password hash only — never store plaintext.</summary>
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
    public string Severity { get; set; } = "Low";
    public string Status { get; set; } = "Open";
    public DateTime DateReported { get; set; } = DateTime.UtcNow;
    public DateTime? DateResolved { get; set; }
    public string AssignedTo { get; set; } = string.Empty;
    public int ETA { get; set; }
    public List<BugScreenshot> Screenshots { get; set; } = new();
    public List<DevComment> Comments { get; set; } = new();
}

public class BugScreenshot
{
    public int Id { get; set; }
    public int BugId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public Bug Bug { get; set; } = null!;
}

public class DevComment
{
    public int Id { get; set; }
    public int BugId { get; set; }
    public string Comment { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Bug Bug { get; set; } = null!;
}

public static class DatabaseInitializer
{
    private static readonly string[] AllowedSeedRoles = ["Admin", "Tester", "Dev", "Guest"];

    public static void Initialize(BugTrackerContext context, IPasswordHasher<User> passwordHasher)
    {
        // Migrate any legacy plaintext passwords to hashes
        var dirty = false;
        foreach (var user in context.Users)
        {
            if (!LooksLikePasswordHash(user.Password))
            {
                user.Password = passwordHasher.HashPassword(user, user.Password);
                dirty = true;
            }
        }
        if (dirty)
            context.SaveChanges();

        if (!context.Users.Any())
        {
            var seeds = new[]
            {
                new { UserName = "admin", Password = "admin123", Email = "admin@bugtracker.com", Role = "Admin" },
                new { UserName = "tester", Password = "tester123", Email = "tester@bugtracker.com", Role = "Tester" },
                new { UserName = "developer", Password = "dev123", Email = "dev@bugtracker.com", Role = "Dev" },
                new { UserName = "guest", Password = "guest123", Email = "guest@bugtracker.com", Role = "Guest" }
            };

            foreach (var seed in seeds)
            {
                if (!AllowedSeedRoles.Contains(seed.Role))
                    continue;

                var user = new User
                {
                    UserName = seed.UserName,
                    Email = seed.Email,
                    Role = seed.Role
                };
                user.Password = passwordHasher.HashPassword(user, seed.Password);
                context.Users.Add(user);
            }

            context.SaveChanges();
        }
    }

    /// <summary>
    /// ASP.NET Identity V3 hashes are base64 and typically start with "AQAAAA".
    /// Plaintext demo passwords are short and will not match this pattern.
    /// </summary>
    private static bool LooksLikePasswordHash(string value)
        => !string.IsNullOrEmpty(value) && value.StartsWith("AQAAAA", StringComparison.Ordinal);
}
