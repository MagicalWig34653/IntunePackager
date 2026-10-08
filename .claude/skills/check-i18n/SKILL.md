---
name: check-i18n
description: Check localization hygiene - German/English .resx key parity, hard-coded user-visible text in XAML, and German characters in code or config. Use after adding or changing UI text, resources or source files.
allowed-tools: Bash(python3 -I .claude/skills/check-i18n/check_i18n.py:*)
---

# check-i18n

Run from the repository root:

```bash
python3 -I .claude/skills/check-i18n/check_i18n.py .
```

The script exits 1 and lists every problem as `path:line message`. It checks:

1. Each neutral/English `*.resx` has a `*.de.resx` twin with exactly the same keys.
2. XAML has no literal `Content`, `Text`, `Header`, `Title`, `ToolTip` text (bindings and `{x:Static}` references are fine; the product name "Intune Package Builder" is allowed).
3. Files under `src/`, `tests/`, `deploy/`, `tools/`, `.github/`, `.claude/` contain no German characters. `docs/`, `site/` and `design/` are exempt.

Fix findings by moving text into resources (add the key to both languages) or translating code comments to English. Do not silence the check by exempting paths; if a new exempt location is justified, change the script and `CLAUDE.md` together.
