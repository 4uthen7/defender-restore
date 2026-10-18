# Security Policy

## What this project does

DefenderRestore **enables and repairs** Microsoft Defender. It does not contain
code to disable real-time protection, cloud protection, sample submission, or
Tamper Protection, and it does not try to bypass Tamper Protection.

## Supported versions

| Version | Supported |
|---------|-----------|
| 1.0.x   | Yes       |

## Reporting a vulnerability

Please use GitHub's **Security advisories** (Security → Report a vulnerability)
for this repository rather than opening a public issue.

Include:

- Windows version and build,
- the exact steps to reproduce,
- the diagnostic report if possible (remove any personal paths),
- the expected and actual behaviour.

## Scope

The following are **not** considered vulnerabilities for this project:

- the tool cannot disable Defender protections (by design),
- the tool cannot disable Tamper Protection (by design),
- the tool requires administrator rights for repair,
- the binary is unsigned and may trigger SmartScreen.

## Verifying a download

Each release publishes a SHA-256 hash. Verify it before running:

```powershell
Get-FileHash .\DefenderRestore.exe -Algorithm SHA256
```

Compare it with the value in the release notes / `SHA256.txt`.
