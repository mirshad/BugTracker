# Security findings and remediations

This document summarizes the security review of **TheBugTracker** and the changes applied in this branch.

## Critical findings (fixed)

| Issue | Risk | Remediation |
| --- | --- | --- |
| Passwords stored and compared in plaintext | Credential theft if DB leaks | Hash with ASP.NET Identity `PasswordHasher`; migrate legacy plaintext on startup |
| Open registration accepted any `Role` (including `Admin`) | Privilege escalation | Self-registration limited to `Tester` / `Guest` |
| Hardcoded JWT secret + disabled issuer/audience validation | Token forgery | Require strong key; validate issuer, audience, lifetime |
| Unrestricted file uploads using client filenames under `wwwroot` | Path traversal, XSS via SVG/HTML, DoS | Extension/content-type whitelist, size limits, GUID filenames, path containment checks |
| DOM XSS via `innerHTML` with bug/comment fields | Stored XSS | HTML-escape output; build screenshot nodes with DOM APIs |
| Overly permissive CORS (`AllowAnyOrigin`) | Cross-origin API abuse | Explicit `Cors:AllowedOrigins` allowlist |
| `Credential.txt` committed with demo passwords | Secret exposure | Removed from tree; ignored going forward |
| Guests could delete screenshots / add comments (API) | Authorization bypass | Role attributes on mutating endpoints |
| Vulnerable transitive `SQLitePCLRaw.lib.e_sqlite3` (GHSA-2m69-gcr7-jv3q) | Native SQLite memory corruption | Pin `SQLitePCLRaw.bundle_e_sqlite3` 3.0.5 |

## High / medium (fixed or mitigated)

- JWT lifetime reduced from 7 days to 8 hours (configurable).
- `RequireHttpsMetadata` enabled outside Development; HTTPS redirection + HSTS in non-Development.
- Security response headers: `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, CSP.
- Uploaded static files forced to non-executable handling for dangerous extensions.
- Input validation on auth and bug payloads (length, enums, dates).
- Duplicate ambiguous `GET /api/bugs` health route moved to `/api/bugs/health`.
- SQLite DB and credential files added to `.gitignore`.

## Remaining recommendations (not fully automated)

1. **Rotate demo credentials** before any shared/staging deploy; seed passwords are still well-known defaults (`admin`/`admin123`, etc.).
2. **Do not commit production secrets** — set `JwtSettings:SecretKey` via environment variables or user secrets; refuse weak keys in Production (already enforced).
3. **Add login rate limiting** (e.g. ASP.NET Core rate limiter) to slow credential stuffing.
4. **Prefer HttpOnly secure cookies** (or BFF pattern) over `localStorage` JWT storage to reduce XSS impact.
5. **Serve uploads outside the web root** or behind an authenticated download endpoint.
6. **Remove committed `bugtracker.db` history** if the repo is public (passwords may exist in older commits as plaintext).
7. **Add automated security tests** covering registration role rejection, Guest forbid on writes, and upload rejection of `.html`/`.svg`/oversized files.
8. **Consider account lockout and password complexity policy** beyond the basic registration length check.

## Quick verification checklist

- [ ] Login still works with seeded users after password migration.
- [ ] `POST /api/auth/register` with `Role: Admin` returns 400.
- [ ] Guest JWT cannot create bugs, add comments, or delete screenshots.
- [ ] Uploading `evil.html` / path-traversal filenames is rejected.
- [ ] Bug title containing `<script>` renders as text in the table, not as script.
