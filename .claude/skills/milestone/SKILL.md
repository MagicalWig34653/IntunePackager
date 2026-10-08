---
name: milestone
description: Advance or close a project milestone (M0-M8) consistently across the acceptance matrix, both READMEs, the website roadmap and the planning document. Use when a milestone starts, finishes or changes scope.
---

# milestone

Milestones and their acceptance checks are defined in `docs/PLANUNG.md` §4 and `docs/ABNAHME-MATRIX.md`. A milestone is **done only when its automated checks pass in CI**, not when the code exists.

## Steps

1. Read the milestone row in `docs/PLANUNG.md` §4 and the A-IDs it covers in `docs/ABNAHME-MATRIX.md`.
2. Verify the evidence: the named tests exist and the latest CI run on the PR head is green (use the GitHub MCP tools; never infer from a local run in a cloud session without .NET).
3. Update, in one commit (and always `docs/ARBEITSSTAND.md`, sessions are disposable):
   - `docs/ABNAHME-MATRIX.md`: status per A-ID (`offen` -> `bestanden (CI <run id>)` only with evidence; keep `offen` for checks that still need devices).
   - Milestone tables in `README.md` (German: in Arbeit / geplant / fertig), `README.en.md` (in progress / planned / done) and `site/index.html` (`stateProgress`, `statePlanned` tags; add a `stateDone` key in both dictionaries if needed).
   - `docs/PLANUNG.md` §9 next steps.
   - `THIRD-PARTY.md` when the milestone introduced a dependency (version, source, license, SHA-256).
4. Run the `docs-sync-checker` agent and the `check-i18n` skill, fix findings.
5. Open the PR with the `pr-flow` skill. State in the PR body what is verified by CI and what is not.

## Never

- Mark a check passed because the code looks right.
- Describe a planned feature as available in README or on the website.
- Skip the device and usability tests of SPEC §12.2 when a milestone claims release readiness (M8).
