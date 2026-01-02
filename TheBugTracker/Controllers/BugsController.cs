using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace TheBugTracker.Controllers
{
    
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class BugsController : ControllerBase
    {
        private readonly BugTrackerContext _context;
        private readonly IWebHostEnvironment _environment;

        public BugsController(BugTrackerContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        [HttpGet]
        public async Task<IActionResult> GetBugs([FromQuery] string? search, [FromQuery] string? severity,
            [FromQuery] string? status, [FromQuery] string? sortBy, [FromQuery] bool desc = false)
        {
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
                query = query.Where(b => b.Severity == severity);

            if (!string.IsNullOrEmpty(status))
                query = query.Where(b => b.Status == status);

            // Sorting
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

        [HttpGet("{id}")]
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
        public async Task<IActionResult> CreateBug([FromForm] BugRequest request)
        {
            var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            if (userRole == "Guest")
                return Forbid();

            var bug = new Bug
            {
                Title = request.Title,
                Description = request.Description,
                Module = request.Module,
                WebPage = request.WebPage,
                Severity = request.Severity,
                Status = request.Status,
                DateReported = DateTime.Parse(request.DateReported),
                DateResolved = string.IsNullOrEmpty(request.DateResolved) ? null : DateTime.Parse(request.DateResolved),
                AssignedTo = request.AssignedTo,
                ETA = request.ETA
            };

            _context.Bugs.Add(bug);
            await _context.SaveChangesAsync();

            // Handle file uploads
            if (request.Screenshots != null && request.Screenshots.Count > 0)
            {
                await SaveScreenshots(bug.Id, request.Screenshots);
            }

            return CreatedAtAction(nameof(GetBug), new { id = bug.Id }, bug);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateBug(int id, [FromForm] BugRequest request)
        {
            var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            if (userRole == "Guest")
                return Forbid();

            var bug = await _context.Bugs
                .Include(b => b.Screenshots)
                .Include(b => b.Comments)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (bug == null)
                return NotFound();

            bug.Title = request.Title;
            bug.Description = request.Description;
            bug.Module = request.Module;
            bug.WebPage = request.WebPage;
            bug.Severity = request.Severity;
            bug.Status = request.Status;
            bug.DateReported = DateTime.Parse(request.DateReported);
            bug.DateResolved = string.IsNullOrEmpty(request.DateResolved) ? null : DateTime.Parse(request.DateResolved);
            bug.AssignedTo = request.AssignedTo;
            bug.ETA = request.ETA;

            // Handle new screenshots
            if (request.Screenshots != null && request.Screenshots.Count > 0)
            {
                await SaveScreenshots(bug.Id, request.Screenshots);
            }

            await _context.SaveChangesAsync();
            return Ok(bug);
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteBug(int id)
        {
            var bug = await _context.Bugs
                .Include(b => b.Screenshots)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (bug == null)
                return NotFound();

            // Delete screenshot files
            foreach (var screenshot in bug.Screenshots)
            {
                var filePath = Path.Combine(_environment.WebRootPath, screenshot.FilePath);
                if (System.IO.File.Exists(filePath))
                    System.IO.File.Delete(filePath);
            }

            _context.Bugs.Remove(bug);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        [HttpPost("{id}/comments")]
        public async Task<IActionResult> AddComment(int id, [FromBody] CommentRequest request)
        {
            var bug = await _context.Bugs.FindAsync(id);
            if (bug == null)
                return NotFound();

            var userName = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "Unknown";

            var comment = new DevComment
            {
                BugId = id,
                Comment = request.Comment,
                CreatedBy = userName,
                CreatedAt = DateTime.Now
            };

            _context.DevComments.Add(comment);
            await _context.SaveChangesAsync();

            return Ok(comment);
        }

        [HttpDelete("screenshots/{id}")]
        public async Task<IActionResult> DeleteScreenshot(int id)
        {
            var screenshot = await _context.BugScreenshots.FindAsync(id);
            if (screenshot == null)
                return NotFound();

            var filePath = Path.Combine(_environment.WebRootPath, screenshot.FilePath);
            if (System.IO.File.Exists(filePath))
                System.IO.File.Delete(filePath);

            _context.BugScreenshots.Remove(screenshot);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private async Task SaveScreenshots(int bugId, List<IFormFile> files)
        {
            var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads");
            if (!Directory.Exists(uploadsFolder))
                Directory.CreateDirectory(uploadsFolder);

            foreach (var file in files)
            {
                if (file.Length > 0)
                {
                    var fileName = $"{Guid.NewGuid()}_{file.FileName}";
                    var filePath = Path.Combine(uploadsFolder, fileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }

                    var screenshot = new BugScreenshot
                    {
                        BugId = bugId,
                        FileName = file.FileName,
                        FilePath = $"uploads/{fileName}"
                    };

                    _context.BugScreenshots.Add(screenshot);
                }
            }

            await _context.SaveChangesAsync();
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
        public string Comment { get; set; } = string.Empty;
    }
}
