# Intune Package Builder

Windows desktop tool (C# / WPF, .NET Framework 4.8) that turns an MSI, EXE or vendor folder into a Microsoft Intune Win32 package (`.intunewin`, standalone detection script, Intune guide). The product specification is `docs/SPEC.md` (German, normative). Architecture, milestones and test strategy are in `docs/PLANUNG.md`; acceptance checks A01-A18 are tracked in `docs/ABNAHME-MATRIX.md`.

**Status:** M0 done (scaffold, Windows CI, logging, Pester, format check). M1 (Core) in progress; current slice and next steps are in `docs/ARBEITSSTAND.md`. Never describe planned behavior as existing.

## Sessions are disposable

A session can end at any moment (container reclaimed, context lost, user switches device). Nothing may live only in a session.

- **The repository is the memory.** Decisions, status, open questions and next steps are written to files in the same change that causes them, never "at the end".
- **Document in parallel with code.** Every PR that changes behavior also updates the matching documentation (`docs/DATENFORMAT.md` for formats, `docs/ABNAHME-MATRIX.md` for check status, `docs/PLANUNG.md` for decisions) and `docs/ARBEITSSTAND.md`.
- **`docs/ARBEITSSTAND.md` is the handoff file.** It must always let a fresh session continue without asking: current milestone and step, what is done and verified, what is in progress, next steps in order, open questions, and how to resume. Update it in every PR; read it first at the start of every session.
- **Commit and push small and often.** Work in slices that can be merged on their own (one PR per slice). Unpushed work counts as lost. Push after each green local check; do not batch a milestone into one PR.
- **State what is unverified** in the PR and in `docs/ARBEITSSTAND.md`; verified means CI evidence, not local belief.
- Chat is not documentation. Anything the user decided in conversation goes into `docs/PLANUNG.md` section 8 (decisions) before the work continues.

## Language rules

- **All source code is English:** identifiers, comments, log messages, commit messages, config files, workflows, scripts.
- **User-visible text is never hard-coded.** It comes from `.resx` resources in German and English (program language decides). Every neutral/English `.resx` needs a `.de.resx` twin with identical keys.
- **Documentation under `docs/` is German.** `README.md` is German, `README.en.md` is English; keep both in sync.
- The website dictionary in `site/index.html` and the mockup texts in `design/mockups/` carry German on purpose.
- A PostToolUse hook (`.claude/hooks/lint_edit.py`) and the `check-i18n` skill enforce this.

## Layout

```text
src/      IntunePackageBuilder.{App,Core,Analysis,Build,Generation}   (App -> Build -> Analysis/Generation -> Core)
tests/    xUnit per module; Pester for PowerShell; FlaUI UI tests (later)
deploy/   client runtime templates and pinned vendor libraries (target device, PowerShell 5.1)
tools/    Win32 Content Prep Tool with provenance and checksum
docs/     SPEC, PLANUNG, ABNAHME-MATRIX (German)
assets/   app icon        design/  mockup sources        site/  GitHub Pages website
.claude/  agents, skills, hooks, settings for this repository
```

Dependency direction is enforced by project references; do not add `Core -> anything` references. The Core must not know WPF.

## Build and test

- Build: `dotnet build IntunePackageBuilder.sln -c Release`; test: `dotnet test IntunePackageBuilder.sln -c Release`.
- **Cloud sessions run on Linux without a .NET SDK, PowerShell or Windows APIs.** WPF, `msi.dll`, Content Prep Tool and Pester cannot run there. The Windows CI (`.github/workflows/ci.yml`) is the source of truth. Code written in such a session is unverified until CI is green; say so in the PR.
- Do not claim a test passed unless you saw it pass. Local packaging tests never count as proof of an Intune rollout.

## Spec invariants (do not break)

- The authoring tool never executes imported installers (analysis reads metadata only: MSI database via `msi.dll`, EXE via file version info).
- No invented EXE parameters: silent switches, uninstall path and detection file come from the user.
- Detection checks the real installed state, is self-contained (no Intune cache files), and never relies on a marker file or tool version.
- Client scripts: Windows PowerShell 5.1, 64-bit, UTF-8 **with BOM**, no PowerShell 7 syntax. LocalSystem context. No forced process kills, no forced restart. Return codes 0, 3010, 1618 by default; 1641 is never a quiet success.
- Generated JSON, PowerShell, HTML and CSV escape their content per format; external processes get an argument list, never a shell string.
- Cleanup deletes only directories the build created (marker file). User originals are never deleted. Stored sources are never overwritten silently.
- Software version (target state) and build ID are different things; every build keeps an immutable configuration snapshot and source manifest.
- No telemetry, no tenant access, no secrets in configuration. Vendor installers and user projects never enter the repository.

## Working conventions

- Read `docs/ARBEITSSTAND.md` first, then work in small steps along the milestones M0-M8. A milestone is done only when its automated checks pass in CI.
- When a milestone changes, update `docs/ABNAHME-MATRIX.md`, the roadmap tables in both READMEs and in `site/index.html` (`milestone` skill).
- Third-party components are added to `THIRD-PARTY.md` with version, source, license and SHA-256 before they are used.
- Assets (icons, mockup PNGs) are generated; regenerate with the `render-assets` skill, do not hand-edit PNGs. Mockups must stay labeled as design mockups until real screenshots exist.

## Git and pull requests

- Develop on the branch the session assigns. The repository owner authorized Claude to create, drive and merge pull requests here (session of 2026-10-08), using the `pr-flow` skill. Merge only when CI is green and the PR contains nothing unverified that is described as verified.
- Commit messages are English, imperative, short subject.
- Never force-push to `main`; never commit `.intunewin`, MSI or EXE files outside `tools/`.

## Agents and skills in this repository

| Name | Kind | Model | Use for |
|---|---|---|---|
| `spec-reviewer` | agent | sonnet | Review a change against the SPEC MUST requirements |
| `powershell-reviewer` | agent | haiku | PowerShell 5.1 compatibility, BOM, escaping, return codes |
| `docs-sync-checker` | agent | haiku | READMEs, site, roadmap and acceptance matrix stay consistent |
| `check-i18n` | skill | - | Resource parity, hard-coded text, German in code |
| `render-assets` | skill | - | Regenerate icons and mockups |
| `milestone` | skill | - | Close or advance a milestone consistently |
| `pr-flow` | skill | - | Open a PR, watch CI, fix, merge |

Prefer the cheap haiku agents for mechanical checks and run independent reviewers in parallel.
