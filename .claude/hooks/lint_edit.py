#!/usr/bin/env python3
"""PostToolUse hook for Edit/Write: enforces repository conventions on the edited file.

Rules (see CLAUDE.md):
  * Source, config, scripts and workflows are English only (no German umlauts/eszett).
  * Own PowerShell files are UTF-8 with BOM.
Exit code 2 sends the message on stderr back to Claude; exit 0 stays silent.
"""
import json
import os
import re
import sys

ENGLISH_ONLY_DIRS = ("src/", "tests/", "deploy/", "tools/", ".github/", ".claude/")
# German text is expected here: documentation, the website dictionary, design sources, German resources.
EXEMPT_PREFIXES = ("docs/", "site/", "design/")
EXEMPT_SUFFIXES = (".md", ".de.resx", ".png", ".ico", ".svg")
UMLAUTS = re.compile("[\u00e4\u00f6\u00fc\u00c4\u00d6\u00dc\u00df]")
BOM = b"\xef\xbb\xbf"


def main():
    try:
        payload = json.load(sys.stdin)
    except ValueError:
        return 0
    path = (payload.get("tool_input") or {}).get("file_path")
    if not path or not os.path.isfile(path):
        return 0

    root = os.environ.get("CLAUDE_PROJECT_DIR") or os.getcwd()
    rel = os.path.relpath(os.path.abspath(path), os.path.abspath(root)).replace(os.sep, "/")
    if rel.startswith(".."):
        return 0

    problems = []

    if rel.lower().endswith((".ps1", ".psm1", ".psd1")) and rel.startswith(("deploy/", "src/", "tests/", "tools/")):
        with open(path, "rb") as handle:
            if handle.read(3) != BOM:
                problems.append("PowerShell file must be UTF-8 with BOM (SPEC section 4). Re-save it with a BOM.")

    english_only = rel.startswith(ENGLISH_ONLY_DIRS) and not rel.startswith(EXEMPT_PREFIXES) \
        and not rel.lower().endswith(EXEMPT_SUFFIXES)
    if english_only:
        with open(path, "r", encoding="utf-8-sig", errors="replace") as handle:
            for number, line in enumerate(handle, 1):
                if UMLAUTS.search(line):
                    problems.append("line %d contains German characters; code, comments and config must be English "
                                    "(user-visible text belongs in .resx resources): %s" % (number, line.strip()[:100]))
                    if len(problems) >= 5:
                        break

    if problems:
        sys.stderr.write("Convention check failed for %s:\n- %s\n" % (rel, "\n- ".join(problems)))
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
