# Bug Tracker

ASP.NET Core bug tracking sample with JWT auth and a static frontend.

## InkMark — PDF Document Signer

A Node.js full-stack PDF signing app lives in [`pdf-signer/`](pdf-signer/README.md):

```bash
cd pdf-signer
npm install
npm run seed
npm start
```

Then open http://localhost:3000 to upload a PDF, place predefined or hand-drawn signatures, and download the signed file.

## Security

See [TheBugTracker/SECURITY.md](TheBugTracker/SECURITY.md) for findings and hardening notes.

## Configuration

Set a strong `JwtSettings:SecretKey` (32+ characters) via environment variables or user secrets before deploying outside Development:

```bash
dotnet user-secrets set "JwtSettings:SecretKey" "<long-random-secret>"
```

Restrict `Cors:AllowedOrigins` and `AllowedHosts` for your real hostnames.
