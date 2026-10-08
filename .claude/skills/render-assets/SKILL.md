---
name: render-assets
description: Regenerate the app icon PNG/ICO sizes and the design mockup screenshots from their sources (assets/icon.svg, design/mockups/mockup.html). Use after changing the icon, the mockup HTML or its texts.
allowed-tools: Bash(python3 -I .claude/skills/render-assets/render_assets.py:*)
---

# render-assets

Sources of truth:

- `assets/icon.svg` - the app icon.
- `design/mockups/mockup.html` - one page that renders the three mockup screens (`?screen=start|form|result&lang=en|de`).

Regenerate everything:

```bash
python3 -I .claude/skills/render-assets/render_assets.py .
```

Outputs: `assets/icon-{16..512}.png`, `assets/icon.ico`, `site/img/icon-256.png`, `site/favicon.svg`, `site/img/mockup-{start,form,result}-{en,de}.png`. The run is deterministic, so unchanged sources give byte-identical files.

Requirements: Python with Pillow and a Chromium-based browser (set `CHROME_PATH` if it is not found automatically). The script gives the browser its own short temp directory because headless Chromium crashes with SIGTRAP when `TMPDIR` is long.

After rendering, look at the PNGs (Read tool) before committing. Mockups must keep the visible "Design mockup, not a real screenshot" label until real application screenshots replace them; when that happens, update README, README.en and `site/index.html` wording as well.
