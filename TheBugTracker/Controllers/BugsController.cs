using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace TheBugTracker.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class BugsController : ControllerBase
    {
        private static readonly HashSet<string> AllowedSeverities = new(StringComparer.OrdinalIgnoreCase)
        {
            "Low", "Medium", "High", "Critical"
        };

        private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
        {
            "Open", "In Progress", "Resolved", "Closed"
        };

        private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".webp"
        };

        private const long MaxUploadBytes = 5 * 1024 * 1024; // 5 MB per file
        private const int MaxUploadsPerRequest = 5;

        private readonly BugTrackerContext _context;
        private readonly IWebHostEnvironment _environment;

        public BugsController(BugTrackerContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        [HttpGet("health")]
        [AllowAnonymous]
        public IActionResult GetHealth()
        {
            return Ok(new { status = "API is Running..." });
        }

        [HttpGet]
        public async Task<IActionResult> GetBugs([FromQuery] string? search, [FromQuery] string? severity,
            [FromQuery] string? status, [FromQuery] string? sortBy, [FromQuery] bool desc = false)
        {
            if (search is { Length: > 200 })
                return BadRequest(new { message = "Search query is too long." });

            var query = _context.Bugs
                .Include(b => b.Screenshots)
                .Include(b => b.Comments)
                .AsQueryable();

            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(b =>
                    b.Title.Contains(search) ||
                    b.Description.Contains(search) ||
                    b.Module.Contains(search));
            }

            if (!string.IsNullOrEmpty(severity))
            {
                if (!AllowedSeverities.Contains(severity))
                    return BadRequest(new { message = "Invalid severity filter." });
                query = query.Where(b => b.Severity == severity);
            }

            if (!string.IsNullOrEmpty(status))
            {
                if (!AllowedStatuses.Contains(status))
                    return BadRequest(new { message = "Invalid status filter." });
                query = query.Where(b => b.Status == status);
            }

            query = sortBy switch
            {
                "title" => desc ? query.OrderByDescending(b => b.Title) : query.OrderBy(b => b.Title),
                "severity" => desc ? query.OrderByDescending(b => b.Severity) : query.OrderBy(b => b.Severity),
                "status" => desc ? query.OrderByDescending(b => b.Status) : query.OrderBy(b => b.Status),
                "dateReported" => desc ? query.OrderByDescending(b => b.DateReported) : query.OrderBy(b => b.DateReported),
                "eta" => desc ? query.OrderByDescending(b => b.ETA) : query.OrderBy(b => b.ETA),
                _ => query.OrderByDescending(b => b.Id)
            };

            var bugs = await query.ToListAsync();
            return Ok(bugs);
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetBug(int id)
        {
            var bug = await _context.Bugs
                .Include(b => b.Screenshots)
                .Include(b => b.Comments)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (bug == null)
                return NotFound();

            return Ok(bug);
        }

        [HttpPost]
        [Authorize(Roles = "Admin,Tester,Dev")]
        public async Task<IActionResult> CreateBug([FromForm] BugRequest request)
        {
            var validationError = ValidateBugRequest(request);
            if (validationError != null)
                return BadRequest(new { message = validationError });

            if (!TryParseDates(request, out var dateReported, out var dateResolved, out var dateError))
                return BadRequest(new { message = dateError });

            var bug = new Bug
            {
                Title = request.Title.Trim(),
                Description = request.Description.Trim(),
                Module = request.Module.Trim(),
                WebPage = request.WebPage?.Trim() ?? string.Empty,
                Severity = NormalizeSeverity(request.Severity),
                Status = NormalizeStatus(request.Status),
                DateReported = dateReported,
                DateResolved = dateResolved,
                AssignedTo = request.AssignedTo?.Trim() ?? string.Empty,
                ETA = request.ETA
            };

            if (request.Screenshots != null && request.Screenshots.Count > 0)
            {
                var precheck = ValidateScreenshots(request.Screenshots);
                if (precheck != null)
                    return BadRequest(new { message = precheck });
            }

            _context.Bugs.Add(bug);
            await _context.SaveChangesAsync();

            if (request.Screenshots != null && request.Screenshots.Count > 0)
            {
                var uploadError = await SaveScreenshots(bug.Id, request.Screenshots);
                if (uploadError != null)
                {
                    _context.Bugs.Remove(bug);
                    await _context.SaveChangesAsync();
                    return BadRequest(new { message = uploadError });
                }
            }

            return CreatedAtAction(nameof(GetBug), new { id = bug.Id }, bug);
        }

        [HttpPut("{id:int}")]
        [Authorize(Roles = "Admin,Tester,Dev")]
        public async Task<IActionResult> UpdateBug(int id, [FromForm] BugRequest request)
        {
            var validationError = ValidateBugRequest(request);
            if (validationError != null)
                return BadRequest(new { message = validationError });

            if (!TryParseDates(request, out var dateReported, out var dateResolved, out var dateError))
                return BadRequest(new { message = dateError });

            var bug = await _context.Bugs
                .Include(b => b.Screenshots)
                .Include(b => b.Comments)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (bug == null)
                return NotFound();

            bug.Title = request.Title.Trim();
            bug.Description = request.Description.Trim();
            bug.Module = request.Module.Trim();
            bug.WebPage = request.WebPage?.Trim() ?? string.Empty;
            bug.Severity = NormalizeSeverity(request.Severity);
            bug.Status = NormalizeStatus(request.Status);
            bug.DateReported = dateReported;
            bug.DateResolved = dateResolved;
            bug.AssignedTo = request.AssignedTo?.Trim() ?? string.Empty;
            bug.ETA = request.ETA;

            if (request.Screenshots != null && request.Screenshots.Count > 0)
            {
                var precheck = ValidateScreenshots(request.Screenshots);
                if (precheck != null)
                    return BadRequest(new { message = precheck });

                var uploadError = await SaveScreenshots(bug.Id, request.Screenshots);
                if (uploadError != null)
                    return BadRequest(new { message = uploadError });
            }

            await _context.SaveChangesAsync();
            return Ok(bug);
        }

        [HttpDelete("{id:int}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteBug(int id)
        {
            var bug = await _context.Bugs
                .Include(b => b.Screenshots)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (bug == null)
                return NotFound();

            foreach (var screenshot in bug.Screenshots)
            {
                TryDeleteUploadFile(screenshot.FilePath);
            }

            _context.Bugs.Remove(bug);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        [HttpPost("{id:int}/comments")]
        [Authorize(Roles = "Admin,Tester,Dev")]
        public async Task<IActionResult> AddComment(int id, [FromBody] CommentRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            var bug = await _context.Bugs.FindAsync(id);
            if (bug == null)
                return NotFound();

            var userName = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "Unknown";

            var comment = new DevComment
            {
                BugId = id,
                Comment = request.Comment.Trim(),
                CreatedBy = userName,
                CreatedAt = DateTime.UtcNow
            };

            _context.DevComments.Add(comment);
            await _context.SaveChangesAsync();

            return Ok(comment);
        }

        [HttpDelete("screenshots/{id:int}")]
        [Authorize(Roles = "Admin,Tester,Dev")]
        public async Task<IActionResult> DeleteScreenshot(int id)
        {
            var screenshot = await _context.BugScreenshots.FindAsync(id);
            if (screenshot == null)
                return NotFound();

            TryDeleteUploadFile(screenshot.FilePath);

            _context.BugScreenshots.Remove(screenshot);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private string? ValidateBugRequest(BugRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200)
                return "Title is required (max 200 characters).";
            if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Length > 4000)
                return "Description is required (max 4000 characters).";
            if (string.IsNullOrWhiteSpace(request.Module) || request.Module.Length > 100)
                return "Module is required (max 100 characters).";
            if (request.WebPage is { Length: > 200 })
                return "Web page must be at most 200 characters.";
            if (request.AssignedTo is { Length: > 100 })
                return "Assigned To must be at most 100 characters.";
            if (request.ETA < 0 || request.ETA > 10000)
                return "ETA must be between 0 and 10000.";
            if (!AllowedSeverities.Contains(request.Severity))
                return "Invalid severity.";
            if (!AllowedStatuses.Contains(request.Status))
                return "Invalid status.";
            return null;
        }

        private static bool TryParseDates(BugRequest request, out DateTime dateReported, out DateTime? dateResolved, out string? error)
        {
            dateReported = default;
            dateResolved = null;
            error = null;

            if (!DateTime.TryParse(request.DateReported, out dateReported))
            {
                error = "Invalid date reported.";
                return false;
            }

            if (!string.IsNullOrEmpty(request.DateResolved))
            {
                if (!DateTime.TryParse(request.DateResolved, out var resolved))
                {
                    error = "Invalid date resolved.";
                    return false;
                }
                dateResolved = resolved;
            }

            return true;
        }

        private static string NormalizeSeverity(string severity)
            => AllowedSeverities.First(s => s.Equals(severity, StringComparison.OrdinalIgnoreCase));

        private static string NormalizeStatus(string status)
            => AllowedStatuses.First(s => s.Equals(status, StringComparison.OrdinalIgnoreCase));

        private void TryDeleteUploadFile(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || relativePath.Contains("..", StringComparison.Ordinal))
                return;

            var uploadsRoot = Path.GetFullPath(Path.Combine(_environment.WebRootPath, "uploads"));
            var filePath = Path.GetFullPath(Path.Combine(_environment.WebRootPath, relativePath));
            if (!filePath.StartsWith(uploadsRoot, StringComparison.OrdinalIgnoreCase))
                return;

            if (System.IO.File.Exists(filePath))
                System.IO.File.Delete(filePath);
        }

        private string? ValidateScreenshots(List<IFormFile> files)
        {
            if (files.Count > MaxUploadsPerRequest)
                return $"A maximum of {MaxUploadsPerRequest} screenshots can be uploaded at once.";

            foreach (var file in files)
            {
                if (file.Length <= 0)
                    continue;

                if (file.Length > MaxUploadBytes)
                    return $"File '{Path.GetFileName(file.FileName)}' exceeds the {MaxUploadBytes / (1024 * 1024)} MB limit.";

                var extension = Path.GetExtension(file.FileName);
                if (string.IsNullOrEmpty(extension) || !AllowedImageExtensions.Contains(extension))
                    return "Only image uploads are allowed (.jpg, .jpeg, .png, .gif, .webp).";

                if (!string.IsNullOrEmpty(file.ContentType)
                    && !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    return "Invalid content type for screenshot upload.";
            }

            return null;
        }

        private async Task<string?> SaveScreenshots(int bugId, List<IFormFile> files)
        {
            var validation = ValidateScreenshots(files);
            if (validation != null)
                return validation;

            var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads");
            Directory.CreateDirectory(uploadsFolder);
            var uploadsRoot = Path.GetFullPath(uploadsFolder);

            foreach (var file in files)
            {
                if (file.Length <= 0)
                    continue;

                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                // Never trust client filename for the stored path (path traversal / overwrite)
                var safeOriginalName = Path.GetFileName(file.FileName);
                var storedName = $"{Guid.NewGuid():N}{extension}";
                var filePath = Path.GetFullPath(Path.Combine(uploadsFolder, storedName));
                if (!filePath.StartsWith(uploadsRoot, StringComparison.OrdinalIgnoreCase))
                    return "Invalid upload path.";

                await using (var stream = new FileStream(filePath, FileMode.CreateNew))
                {
                    await file.CopyToAsync(stream);
                }

                _context.BugScreenshots.Add(new BugScreenshot
                {
                    BugId = bugId,
                    FileName = safeOriginalName,
                    FilePath = $"uploads/{storedName}"
                });
            }

            await _context.SaveChangesAsync();
            return null;
        }
    }

    public class BugRequest
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Module { get; set; } = string.Empty;
        public string WebPage { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string DateReported { get; set; } = string.Empty;
        public string? DateResolved { get; set; }
        public string AssignedTo { get; set; } = string.Empty;
        public int ETA { get; set; }
        public List<IFormFile>? Screenshots { get; set; }
    }

    public class CommentRequest
    {
        [Required, MaxLength(2000)]
        public string Comment { get; set; } = string.Empty;
    }
}
