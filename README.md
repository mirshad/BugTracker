# Bug Tracker

ASP.NET Core bug tracking sample with JWT auth and a static frontend.

## Security

See [TheBugTracker/SECURITY.md](TheBugTracker/SECURITY.md) for findings and hardening notes.

## Configuration

Set a strong `JwtSettings:SecretKey` (32+ characters) via environment variables or user secrets before deploying outside Development:

```bash
dotnet user-secrets set "JwtSettings:SecretKey" "<long-random-secret>"
```

Restrict `Cors:AllowedOrigins` and `AllowedHosts` for your real hostnames.
