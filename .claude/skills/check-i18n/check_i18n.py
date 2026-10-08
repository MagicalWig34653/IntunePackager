#!/usr/bin/env python3
"""Checks localization hygiene for the repository.

1. Every neutral/English .resx has a German twin with the same keys, and vice versa.
2. XAML does not contain hard-coded user-visible text (Content/Text/Header/Title/ToolTip literals).
3. Source/config files outside docs/ site/ design/ contain no German characters.

Usage: python3 -I check_i18n.py [repo_root]
Exit code 1 when problems are found.
"""
import os
import re
import sys
import xml.etree.ElementTree as ET

ALLOWED_LITERALS = {"Intune Package Builder"}  # product name is not translated
XAML_ATTR = re.compile(r'\b(Content|Text|Header|Title|ToolTip|Watermark|PlaceholderText)="([^"{][^"]*)"')
UMLAUTS = re.compile("[\u00e4\u00f6\u00fc\u00c4\u00d6\u00dc\u00df]")
SKIP_DIRS = {".git", "bin", "obj", "docs", "site", "design", "node_modules"}
CODE_SUFFIXES = (".cs", ".xaml", ".csproj", ".props", ".ps1", ".psm1", ".yml", ".yaml", ".json", ".cmd")


def walk(root):
    for base, dirs, files in os.walk(root):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for name in files:
            yield os.path.join(base, name)


def resx_keys(path):
    return {d.get("name") for d in ET.parse(path).getroot().findall("data")}


def main():
    root = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else ".")
    problems = []

    resx = [p for p in walk(root) if p.endswith(".resx")]
    neutral = [p for p in resx if not re.search(r"\.(de|en)\.resx$", p)]
    for base in neutral:
        stem = base[:-len(".resx")]
        de = stem + ".de.resx"
        if not os.path.isfile(de):
            problems.append("%s: missing German twin %s" % (rel(root, base), rel(root, de)))
            continue
        a, b = resx_keys(base), resx_keys(de)
        for key in sorted(a - b):
            problems.append("%s: key '%s' missing in German resource" % (rel(root, de), key))
        for key in sorted(b - a):
            problems.append("%s: key '%s' missing in neutral/English resource" % (rel(root, base), key))
    for de in [p for p in resx if p.endswith(".de.resx")]:
        if not os.path.isfile(de[:-len(".de.resx")] + ".resx"):
            problems.append("%s: German resource without neutral resource" % rel(root, de))

    for path in walk(root):
        if path.endswith(".xaml"):
            with open(path, encoding="utf-8-sig") as handle:
                for number, line in enumerate(handle, 1):
                    for match in XAML_ATTR.finditer(line):
                        if match.group(2) not in ALLOWED_LITERALS:
                            problems.append("%s:%d hard-coded text '%s' (use a resource)" % (rel(root, path), number, match.group(2)))
        if path.endswith(CODE_SUFFIXES):
            with open(path, encoding="utf-8-sig", errors="replace") as handle:
                for number, line in enumerate(handle, 1):
                    if UMLAUTS.search(line):
                        problems.append("%s:%d German characters in code/config" % (rel(root, path), number))

    for problem in problems:
        print(problem)
    print("i18n check: %d problem(s)" % len(problems))
    return 1 if problems else 0


def rel(root, path):
    return os.path.relpath(path, root).replace(os.sep, "/")


if __name__ == "__main__":
    sys.exit(main())
