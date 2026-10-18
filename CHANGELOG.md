# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-10-18

### Added

- Portable WinForms GUI that diagnoses and repairs Microsoft Defender after
  dControl or similar tools disabled it.
- Checks for services, drivers, policies (including nested subkeys), real
  config flags, Security Center providers, IFEO hijacks, registry ACL denies,
  Tamper Protection state, Defender engine status, Windows Security app
  registration, platform binaries, signatures, dControl leftovers, and firewall
  profiles.
- Ordered repair: policies -> config -> ACL -> providers -> services ->
  drivers -> IFEO -> exclusions -> app registration -> firewall -> start.
- `WinDefend` `ImagePath` repair and service start-type fallback through the
  registry.
- Service diagnostics in the report: `Start` value, dependencies, running
  state, `sc.exe qc` output on failure, and Win32 error codes (including 1058).
- One-click mode: `--repair --yes`, plus `one-click-repair.bat`.
- CLI report mode: `--report <path>`.
- Optional restore point, `MpCmdRun.exe -RestoreDefaults`, and signature update.
- Japanese and English documentation, MIT license, build script, GitHub Actions
  build workflow.

### Notes

- This tool **never disables** real-time protection, cloud protection, sample
  submission, or Tamper Protection.
- Tamper Protection cannot be disabled programmatically; the tool only reports
  when Windows blocks a change.

[1.0.0]: https://github.com/4uthen7/defender-restore/releases/tag/v1.0.0
