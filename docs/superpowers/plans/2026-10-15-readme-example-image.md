# README Example Image Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the approved screenshot to the repository and display it in the README screenshot section.

**Architecture:** Keep the original PNG as a repository documentation asset under `docs/images`. Reference it from README with a centered HTML image element and a short Chinese caption; no application or build code changes are required.

**Tech Stack:** PNG, Markdown, Git

## Global Constraints

- Preserve the supplied PNG byte-for-byte: no crop, blur, resize, or re-encoding.
- Save it exactly as `docs/images/deadline-widget-example.png`.
- Keep the screenshot's course numbers, task names, and dates unchanged.
- Do not modify application code, build workflows, tests, or unrelated documentation.

---

### Task 1: Add and reference the approved screenshot

**Files:**
- Create: `docs/images/deadline-widget-example.png`
- Modify: `README.md:18-20`
- Verify: `docs/images/deadline-widget-example.png`, `README.md`

**Interfaces:**
- Consumes: source image `C:\Users\68827\AppData\Local\Temp\codex-clipboard-5da05c5e-ea13-4982-ab2c-f941fabb9e0a.png`
- Produces: README image reference `docs/images/deadline-widget-example.png`

- [ ] **Step 1: Record the source image hash and dimensions**

Run:

```powershell
Get-FileHash 'C:\Users\68827\AppData\Local\Temp\codex-clipboard-5da05c5e-ea13-4982-ab2c-f941fabb9e0a.png' -Algorithm SHA256
Add-Type -AssemblyName System.Drawing
$image = [Drawing.Image]::FromFile('C:\Users\68827\AppData\Local\Temp\codex-clipboard-5da05c5e-ea13-4982-ab2c-f941fabb9e0a.png')
try { "$($image.Width)x$($image.Height)" } finally { $image.Dispose() }
```

Expected: a valid SHA-256 value and dimensions `456x568`.

- [ ] **Step 2: Copy the PNG without transformation**

Run:

```powershell
Copy-Item -LiteralPath 'C:\Users\68827\AppData\Local\Temp\codex-clipboard-5da05c5e-ea13-4982-ab2c-f941fabb9e0a.png' -Destination '.\docs\images\deadline-widget-example.png'
```

- [ ] **Step 3: Replace the README placeholder**

Replace the existing screenshot placeholder with:

```html
<p align="center">
  <img src="docs/images/deadline-widget-example.png" alt="Deadline Widget 桌面效果示例" width="320">
</p>

<p align="center">Deadline Widget 桌面效果示例</p>
```

- [ ] **Step 4: Verify byte identity and README reference**

Run:

```powershell
$source = (Get-FileHash 'C:\Users\68827\AppData\Local\Temp\codex-clipboard-5da05c5e-ea13-4982-ab2c-f941fabb9e0a.png' -Algorithm SHA256).Hash
$target = (Get-FileHash '.\docs\images\deadline-widget-example.png' -Algorithm SHA256).Hash
if ($source -cne $target) { throw 'Copied image differs from the approved source.' }
Select-String -LiteralPath '.\README.md' -Pattern 'docs/images/deadline-widget-example.png'
git diff --check
git status --short
```

Expected: source and target hashes match, README contains exactly one image reference, `git diff --check` succeeds, and only the planned image and README are newly changed beyond the already committed design and plan documents.

- [ ] **Step 5: Commit the documentation update**

Run:

```powershell
git add -- README.md docs/images/deadline-widget-example.png
git commit -m "docs: add Deadline Widget screenshot"
```
