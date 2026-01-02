using BugTracker.Data;
using BugTracker.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddDbContext<BugTrackerContext>(options =>
    options.UseSqlite("Data Source=bugtracker.db"));

builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options => options.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles);


builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BugTrackerContext>();
    db.Database.EnsureCreated();
}

// Create uploads directory
var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "uploads");
if (!Directory.Exists(uploadsPath))
{
    Directory.CreateDirectory(uploadsPath);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAll");
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(app.Environment.ContentRootPath),
    RequestPath = ""
});

// Serve uploaded files
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads"
});

// Bug Endpoints
app.MapGet("/api/bugs", async (BugTrackerContext db) =>
    await db.Bugs.Include(b => b.BugScreenshots).OrderByDescending(b => b.CreatedAt).ToListAsync());

app.MapGet("/api/bugs/{id}", async (int id, BugTrackerContext db) =>
    await db.Bugs.Include(b => b.BugScreenshots).FirstOrDefaultAsync(b => b.Id == id) is Bug bug 
        ? Results.Ok(bug) : Results.NotFound());

app.MapPost("/api/bugs", async (HttpRequest request, BugTrackerContext db) =>
{
    var form = await request.ReadFormAsync();
    
    var bug = new Bug
    {
        Title = form["title"].ToString(),
        Description = form["description"].ToString(),
        Status = form["status"].ToString(),
        Priority = form["priority"].ToString(),
        AssignedTo = form["assignedTo"].ToString(),
        PageUrl = form["pageUrl"].ToString(),
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        BugScreenshots = new List<Screenshot>()
    };

    // Handle file uploads
    var files = form.Files;
    foreach (var file in files)
    {
        if (file.Length > 0)
        {
            var fileName = $"{Guid.NewGuid()}_{file.FileName}";
            var filePath = Path.Combine(uploadsPath, fileName);
            
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            bug.BugScreenshots.Add(new Screenshot
            {
                FileName = file.FileName,
                FilePath = $"/uploads/{fileName}",
                UploadedAt = DateTime.UtcNow
            });
        }
    }

    db.Bugs.Add(bug);
    await db.SaveChangesAsync();
    
    return Results.Created($"/api/bugs/{bug.Id}", bug);
});

app.MapPut("/api/bugs/{id}", async (int id, HttpRequest request, BugTrackerContext db) =>
{
    var bug = await db.Bugs.Include(b => b.BugScreenshots).FirstOrDefaultAsync(b => b.Id == id);
    if (bug is null) return Results.NotFound();

    var form = await request.ReadFormAsync();
    
    bug.Title = form["title"].ToString();
    bug.Description = form["description"].ToString();
    bug.Status = form["status"].ToString();
    bug.Priority = form["priority"].ToString();
    bug.AssignedTo = form["assignedTo"].ToString();
    bug.PageUrl = form["pageUrl"].ToString();
    bug.UpdatedAt = DateTime.UtcNow;

    // Handle new file uploads
    var files = form.Files;
    foreach (var file in files)
    {
        if (file.Length > 0)
        {
            var fileName = $"{Guid.NewGuid()}_{file.FileName}";
            var filePath = Path.Combine(uploadsPath, fileName);
            
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            bug.BugScreenshots.Add(new Screenshot
            {
                FileName = file.FileName,
                FilePath = $"/uploads/{fileName}",
                UploadedAt = DateTime.UtcNow
            });
        }
    }

    await db.SaveChangesAsync();
    return Results.Ok(bug);
});

app.MapDelete("/api/bugs/{id}", async (int id, BugTrackerContext db) =>
{
    var bug = await db.Bugs.Include(b => b.BugScreenshots).FirstOrDefaultAsync(b => b.Id == id);
    if (bug is null) return Results.NotFound();

    // Delete associated files
    foreach (var screenshot in bug.BugScreenshots)
    {
        var fullPath = Path.Combine(Directory.GetCurrentDirectory(), screenshot.FilePath.TrimStart('/'));
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    db.Bugs.Remove(bug);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

app.MapDelete("/api/bugs/{bugId}/screenshots/{screenshotId}", async (int bugId, int screenshotId, BugTrackerContext db) =>
{
    var bug = await db.Bugs.Include(b => b.BugScreenshots).FirstOrDefaultAsync(b => b.Id == bugId);
    if (bug is null) return Results.NotFound();

    var screenshot = bug.BugScreenshots.FirstOrDefault(s => s.Id == screenshotId);
    if (screenshot is null) return Results.NotFound();

    // Delete file
    var fullPath = Path.Combine(Directory.GetCurrentDirectory(), screenshot.FilePath.TrimStart('/'));
    if (File.Exists(fullPath))
    {
        File.Delete(fullPath);
    }

    bug.BugScreenshots.Remove(screenshot);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

app.MapGet("/api/bugs/stats", async (BugTrackerContext db) =>
{
    var total = await db.Bugs.CountAsync();
    var open = await db.Bugs.CountAsync(b => b.Status == "Open");
    var inProgress = await db.Bugs.CountAsync(b => b.Status == "In Progress");
    var resolved = await db.Bugs.CountAsync(b => b.Status == "Resolved");
    var closed = await db.Bugs.CountAsync(b => b.Status == "Closed");

    return Results.Ok(new
    {
        Total = total,
        Open = open,
        InProgress = inProgress,
        Resolved = resolved,
        Closed = closed
    });
});

app.Run();

namespace BugTracker.Models
{
    public class Bug
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = "Open";
        public string Priority { get; set; } = "Medium";
        public string AssignedTo { get; set; } = string.Empty;
        public string PageUrl { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public List<Screenshot> BugScreenshots { get; set; } = new();
    }

    public class Screenshot
    {
        public int Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public DateTime UploadedAt { get; set; }
        public int BugId { get; set; }
        public Bug? Bug { get; set; }
    }
}

namespace BugTracker.Data
{
    public class BugTrackerContext : DbContext
    {
        public BugTrackerContext(DbContextOptions<BugTrackerContext> options)
            : base(options) { }

        public DbSet<Bug> Bugs { get; set; }
        public DbSet<Screenshot> Screenshots { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Bug>()
                .HasKey(b => b.Id);

            modelBuilder.Entity<Bug>()
                .HasMany(b => b.BugScreenshots)
                .WithOne(s => s.Bug)
                .HasForeignKey(s => s.BugId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Screenshot>()
                .HasKey(s => s.Id);
        }
    }
}