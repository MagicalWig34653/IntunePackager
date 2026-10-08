<p align="center">
  <img src="assets/icon-256.png" width="128" alt="Intune Package Builder">
</p>

<h1 align="center">Intune Package Builder</h1>

<p align="center">
  From installer to Intune package.<br>
  <a href="README.md">Deutsch</a> · <a href="https://magicalwig34653.github.io/IntunePackager/">Website</a> · <a href="docs/SPEC.md">Specification (German)</a> · <a href="docs/PLANUNG.md">Planning (German)</a>
</p>

<p align="center">
  <a href="https://github.com/MagicalWig34653/IntunePackager/actions/workflows/ci.yml"><img src="https://github.com/MagicalWig34653/IntunePackager/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <img src="https://img.shields.io/badge/Status-Analysis%20done%2C%20no%20UI%20yet-orange" alt="Status: analysis done, no UI yet">
  <img src="https://img.shields.io/badge/Platform-Windows%2011%20%7C%20Server%202022-blue" alt="Platform">
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-AGPL--3.0-green" alt="License AGPL-3.0"></a>
</p>

> **Project status:** the project scaffold, the core (projects, versions, locks, settings) and the source analysis (MSI, EXE, manifest, safe import) are in place and tested by Windows CI (M0 to M2 done). The application has no user interface and no usable features yet, and there is no release. The images below are **design mockups**, not screenshots of a working program.

Intune Package Builder is a Windows desktop application that turns an MSI, an EXE or a vendor folder into a complete Microsoft Intune Win32 package:

> Drop the installer → check the details → build the package → copy file and settings into Intune.

The result is a `.intunewin` file, a standalone detection script and a setup guide that matches that exact build.

<p align="center">
  <img src="site/img/mockup-start-en.png" alt="Design mockup of the start page" width="720">
</p>

## Planned features

- **Standard mode** with file picker and drag and drop. An MSI needs no technical input; for an EXE the vendor's parameters are asked for, never guessed.
- **Advanced mode** for vendor folders, MSI properties, apps that must be closed, timeout and return codes.
- **Projects and versions** in a base folder of your choice, with notes and reuse of settings for updates.
- **Real detection** of the installed state (product code or file version), independent of the Intune cache.
- **Safe behavior on the target device:** LocalSystem, native 64-bit PowerShell, progress window, no forced process termination or restarts.
- **Traceable builds** with a configuration snapshot, source checksums and a build manifest.
- **German and English:** the interface, the Intune guide and user notices follow the program language.

<p align="center">
  <img src="site/img/mockup-form-en.png" alt="Design mockup: standard mode for an MSI" width="48%">
  <img src="site/img/mockup-result-en.png" alt="Design mockup: result page" width="48%">
</p>

Out of scope for version 1: direct Intune upload, Graph sign-in, group assignments, MSIX/Store, driver packages, per-user installs, license activation and arbitrary multi-step install scripts. See the [specification](docs/SPEC.md) (German) for details.

## Roadmap

| M | Content | State |
|---|---|---|
| M0 | Solution scaffold, Windows CI, logging foundation, Pester scaffold, format check | done |
| M1 | Core: project model, atomic saving, migration, locks, settings | done |
| M2 | Source analysis: MSI reader, EXE metadata, manifests, safe import | done |
| M3 | Generators: detection script, Intune guide, JSON/CSV | next |
| M4 | Build pipeline with the Content Prep Tool | planned |
| M5 | Client runtime: wrapper, return codes, user interaction | planned |
| M6 | UI: standard mode, drag and drop, results | planned |
| M7 | Advanced mode, project view, update flow | planned |
| M8 | Distribution, documentation, device test protocol | planned |
| M9 | Optional: Intune upload via Microsoft Graph (not part of version 1) | noted |

The matching acceptance checks A01–A18 are listed in the [acceptance matrix](docs/ABNAHME-MATRIX.md) (German).

## Repository

```text
src/      Authoring tool (C# / WPF, .NET Framework 4.8) in separate modules
deploy/   Client runtime: wrapper templates and pinned third-party library (planned, M5)
tools/    Win32 Content Prep Tool with provenance and checksum manifest (planned, M4)
tests/    xUnit exists; Pester and UI tests planned
docs/     Specification, planning, acceptance matrix, data format, work status (German)
assets/   App icon (SVG, PNG, ICO)
design/   Sources of the design mockups
site/     GitHub Pages website
```

User projects and vendor installers do not belong in this repository (see `.gitignore`). Third-party components are listed in [THIRD-PARTY.md](THIRD-PARTY.md).

## Development

- **Source code is English throughout:** identifiers, comments, log messages, configuration and workflows. User-visible text comes only from resource files (German and English). The documentation under `docs/` is in German.
- Build and test on Windows (`dotnet build IntunePackageBuilder.sln`, `dotnet test IntunePackageBuilder.sln`). CI runs on `windows-latest` and `windows-2022`.
- The website lives in `site/` and is published by GitHub Actions when `site/` changes on `main`.

## License

[GNU Affero General Public License v3.0](LICENSE). This project is not affiliated with Microsoft. "Intune" and "Windows" are trademarks of Microsoft Corporation.
