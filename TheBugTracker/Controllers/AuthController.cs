using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace TheBugTracker.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private static readonly HashSet<string> SelfServiceRoles = new(StringComparer.OrdinalIgnoreCase)
        {
            "Tester", "Guest"
        };

        private readonly BugTrackerContext _context;
        private readonly IConfiguration _configuration;
        private readonly IPasswordHasher<User> _passwordHasher;

        public AuthController(
            BugTrackerContext context,
            IConfiguration configuration,
            IPasswordHasher<User> passwordHasher)
        {
            _context = context;
            _configuration = configuration;
            _passwordHasher = passwordHasher;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.UserName == request.UserName);

            if (user == null)
                return Unauthorized(new { message = "Invalid credentials" });

            var verify = _passwordHasher.VerifyHashedPassword(user, user.Password, request.Password);
            if (verify == PasswordVerificationResult.Failed)
            {
                // One-time upgrade path for legacy plaintext rows not yet migrated
                if (!user.Password.StartsWith("AQAAAA", StringComparison.Ordinal)
                    && user.Password == request.Password)
                {
                    user.Password = _passwordHasher.HashPassword(user, request.Password);
                    await _context.SaveChangesAsync();
                }
                else
                {
                    return Unauthorized(new { message = "Invalid credentials" });
                }
            }
            else if (verify == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.Password = _passwordHasher.HashPassword(user, request.Password);
                await _context.SaveChangesAsync();
            }

            var token = GenerateJwtToken(user);

            return Ok(new
            {
                token,
                user = new
                {
                    user.Id,
                    user.UserName,
                    user.Email,
                    user.Role
                }
            });
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            if (await _context.Users.AnyAsync(u => u.UserName == request.UserName))
                return BadRequest(new { message = "Username already exists" });

            // Never trust client-supplied privileged roles (e.g. Admin / Dev)
            var role = string.IsNullOrWhiteSpace(request.Role) ? "Tester" : request.Role.Trim();
            if (!SelfServiceRoles.Contains(role))
                return BadRequest(new { message = "Invalid role. Self-registration allows Tester or Guest only." });

            var user = new User
            {
                UserName = request.UserName.Trim(),
                Email = request.Email.Trim(),
                Role = role
            };
            user.Password = _passwordHasher.HashPassword(user, request.Password);

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return Ok(new { message = "User registered successfully" });
        }

        private string GenerateJwtToken(User user)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var jwtSettings = _configuration.GetSection("JwtSettings");
            var secretKey = jwtSettings["SecretKey"];
            if (string.IsNullOrWhiteSpace(secretKey) || secretKey.Length < 32)
            {
                secretKey = "DevOnly_ChangeMe_BugTrackerSecretKey_Min32Chars!";
            }

            var key = Encoding.UTF8.GetBytes(secretKey);
            var issuer = jwtSettings["Issuer"] ?? "TheBugTracker";
            var audience = jwtSettings["Audience"] ?? "TheBugTrackerUsers";
            var lifetimeHours = int.TryParse(jwtSettings["TokenLifetimeHours"], out var hours) ? hours : 8;

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Name, user.UserName),
                    new Claim(ClaimTypes.Role, user.Role)
                }),
                Expires = DateTime.UtcNow.AddHours(lifetimeHours),
                Issuer = issuer,
                Audience = audience,
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(key),
                    SecurityAlgorithms.HmacSha256Signature)
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }
    }

    public class LoginRequest
    {
        [Required, MaxLength(64)]
        public string UserName { get; set; } = string.Empty;

        [Required, MaxLength(128)]
        public string Password { get; set; } = string.Empty;
    }

    public class RegisterRequest
    {
        [Required, MaxLength(64)]
        [RegularExpression(@"^[a-zA-Z0-9._-]{3,64}$", ErrorMessage = "Username must be 3-64 chars: letters, digits, . _ -")]
        public string UserName { get; set; } = string.Empty;

        [Required, MinLength(8), MaxLength(128)]
        public string Password { get; set; } = string.Empty;

        [Required, EmailAddress, MaxLength(256)]
        public string Email { get; set; } = string.Empty;

        [MaxLength(32)]
        public string? Role { get; set; }
    }
}
