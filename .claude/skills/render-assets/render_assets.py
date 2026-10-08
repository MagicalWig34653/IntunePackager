#!/usr/bin/env python3
"""Regenerates icon PNG/ICO files and the design mockup screenshots.

Needs: Python 3 with Pillow, and a Chromium-based browser (CHROME_PATH, a Playwright
Chromium under PLAYWRIGHT_BROWSERS_PATH, or Chrome/Edge in a standard location).

Usage: python3 -I render_assets.py [repo_root]
Outputs: assets/icon-<size>.png, assets/icon.ico, site/img/icon-256.png, site/favicon.svg,
         site/img/mockup-<start|form|result>-<en|de>.png
"""
import glob
import os
import shutil
import subprocess
import sys
import tempfile

from PIL import Image

ICON_SIZES = (16, 32, 48, 64, 128, 256, 512)
SCREENS = ("start", "form", "result")
LANGS = ("en", "de")
MOCKUP_SIZE = (1280, 800)


def find_chrome():
    candidates = [os.environ.get("CHROME_PATH")]
    candidates += glob.glob(os.path.join(os.environ.get("PLAYWRIGHT_BROWSERS_PATH", "/opt/pw-browsers"), "chromium-*", "chrome-linux", "chrome"))
    candidates += [
        r"C:\Program Files\Google\Chrome\Application\chrome.exe",
        r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        shutil.which("chromium"), shutil.which("chrome"), shutil.which("google-chrome"), shutil.which("msedge"),
    ]
    for candidate in candidates:
        if candidate and os.path.isfile(candidate):
            return candidate
    sys.exit("No Chromium-based browser found. Set CHROME_PATH.")


def screenshot(chrome, url, out_path, profile_dir, browser_tmp, transparent=False):
    # The visible viewport is smaller than --window-size, so render large and crop afterwards.
    args = [chrome, "--headless", "--no-sandbox", "--disable-gpu", "--disable-dev-shm-usage",
            "--user-data-dir=" + profile_dir, "--hide-scrollbars", "--window-size=1400,1000",
            "--screenshot=" + out_path, url]
    if transparent:
        args.insert(-1, "--default-background-color=00000000")
    # Chromium crashes (SIGTRAP) when TMPDIR is long because its internal socket path gets too long,
    # so give the browser its own short temp directory.
    env = dict(os.environ, TMPDIR=browser_tmp)
    for attempt in range(3):
        result = subprocess.run(args, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        if result.returncode == 0 and os.path.isfile(out_path):
            return
    sys.exit("Browser failed to render %s (exit code %s)" % (url, result.returncode))


def main():
    root = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else ".")
    chrome = find_chrome()
    tmp = tempfile.mkdtemp(prefix="render-assets-")
    profile = os.path.join(tmp, "profile")
    browser_tmp = tempfile.mkdtemp(prefix="cr-", dir="/tmp" if os.name == "posix" else None)
    try:
        svg_url = "file://" + os.path.join(root, "assets", "icon.svg").replace("\\", "/")
        for size in ICON_SIZES:
            page = os.path.join(tmp, "icon%d.html" % size)
            with open(page, "w", encoding="utf-8") as handle:
                handle.write('<html><body style="margin:0;background:transparent"><img src="%s" width="%d" height="%d" style="display:block"></body></html>' % (svg_url, size, size))
            raw = os.path.join(tmp, "icon%d.png" % size)
            screenshot(chrome, "file://" + page.replace("\\", "/"), raw, profile, browser_tmp, transparent=True)
            Image.open(raw).convert("RGBA").crop((0, 0, size, size)).save(os.path.join(root, "assets", "icon-%d.png" % size))
        Image.open(os.path.join(root, "assets", "icon-256.png")).save(
            os.path.join(root, "assets", "icon.ico"), sizes=[(s, s) for s in ICON_SIZES if s <= 256])
        shutil.copyfile(os.path.join(root, "assets", "icon-256.png"), os.path.join(root, "site", "img", "icon-256.png"))
        shutil.copyfile(os.path.join(root, "assets", "icon.svg"), os.path.join(root, "site", "favicon.svg"))

        mockup = "file://" + os.path.join(root, "design", "mockups", "mockup.html").replace("\\", "/")
        for lang in LANGS:
            for screen in SCREENS:
                raw = os.path.join(tmp, "m-%s-%s.png" % (screen, lang))
                screenshot(chrome, "%s?screen=%s&lang=%s" % (mockup, screen, lang), raw, profile, browser_tmp)
                Image.open(raw).convert("RGB").crop((0, 0) + MOCKUP_SIZE).save(
                    os.path.join(root, "site", "img", "mockup-%s-%s.png" % (screen, lang)), optimize=True)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
        shutil.rmtree(browser_tmp, ignore_errors=True)
    print("Rendered %d icons and %d mockups." % (len(ICON_SIZES), len(SCREENS) * len(LANGS)))


if __name__ == "__main__":
    main()
