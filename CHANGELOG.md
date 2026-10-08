# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

Nothing yet.

## [1.0.0] - 2026-10-07

First tagged release. The application and installer are unchanged in behaviour from the preceding work; this release establishes a versioned baseline after the repository was de-branded, documented, and republished.

### Added

- Repository documentation: root `README.md`, `FAQ.md`, `CONTRIBUTING.md`, `SECURITY.md`, `CODE_OF_CONDUCT.md`, MIT `LICENSE`, issue and pull request templates, and a CI workflow.

### Changed

- Converted `INSTALLATION_GUIDE` and `CONNECTION_STRING_GUIDE` from `.docx` to Markdown so they render and diff on GitHub.
- Installer now targets `%ProgramFiles%\CerberusDashboard` instead of a vendor subdirectory, and the MSI `Manufacturer` is `Cerberus Dashboard`.
- Data-protection keys now live in `%ProgramData%\CerberusDashboard\DataProtection`.
- `appsettings.json` and `appsettings.json.example` reduced to a single credential-free local estate.
- Aligned the `launchSettings.json` profile port with the configured endpoint.

### Removed

- All vendor branding: page title, header logo, logo assets, installer manufacturer and company folder.
- Previously committed SQL credentials, server addresses, and internal instance names from configuration and documentation.
- The legacy Visual Studio Installer project (`.vdproj`), superseded by the WiX 7 project.
- Stale .NET Upgrade Assistant artifacts under `.github/upgrades/`.

### Security

- Repository history was flattened to a single commit to purge previously committed credentials. Any credential that appeared in the prior history should be considered exposed and rotated.

---

## Release history

Versions before this repository's history was flattened are not itemised here. The project was migrated from .NET Framework 4.8 to .NET 8, then to .NET 10, and its installer moved from a Visual Studio Installer project to WiX 7 over that period.

Installer versions are passed at build time (`-p:InstallerVersion=x.y.z`). Use exactly three numeric fields; major and minor are capped at 255 and build at 65535. The MSI shipped with a release carries that release's version.

[Unreleased]: https://github.com/lucduguaysita/CerberusDashboard/compare/1.0.0...HEAD
[1.0.0]: https://github.com/lucduguaysita/CerberusDashboard/releases/tag/1.0.0
