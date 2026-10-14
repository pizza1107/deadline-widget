# Reproducible Build and Tagged Release Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Pin the .NET and NuGet dependency graph, enable vulnerability auditing, and implement a tested tag-driven GitHub Release pipeline without publishing a release.

**Architecture:** A repository-level SDK policy and per-project lock files make restore deterministic. CI restores in locked mode and treats NuGet vulnerability advisories as failures. The existing package builder becomes version-aware and emits a complete release bundle; a tag workflow uploads immutable artifacts and checksums.

**Tech Stack:** .NET SDK 8.0.425 feature band, NuGet PackageReference lock files, PowerShell, GitHub Actions, GitHub CLI.

## Global Constraints

- Preserve all requirements and passing tests from the Unicode/CI phase.
- Keep current package versions unless a reported vulnerability requires an isolated upgrade.
- Do not create or push a tag and do not create a GitHub Release.
- Release workflow permissions are `contents: read` by default and `contents: write` only in the release job.
- Do not commit generated release artifacts.

---

### Task 1: Pin .NET SDK and NuGet dependency graphs

**Files:**
- Create: `global.json`
- Create: `Directory.Build.props`
- Create: `src/DeadlineWidget.Core/packages.lock.json`
- Create: `src/DeadlineWidget.Manager/packages.lock.json`
- Create: `tests/DeadlineWidget.Core.Tests/packages.lock.json`
- Create: `tests/DeadlineWidget.Manager.Tests/packages.lock.json`
- Create: `tests/DeadlineWidget.Skin.Tests/packages.lock.json`
- Modify: all three test `.csproj` files
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Produces: SDK selection in the 8.0.4xx band and locked NuGet restore for every project.

- [ ] **Step 1: Add SDK policy**

Create `global.json` exactly as approved:

```json
{
  "sdk": {
    "version": "8.0.425",
    "rollForward": "latestPatch",
    "allowPrerelease": false
  }
}
```

- [ ] **Step 2: Add shared restore and audit policy**

Create `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
    <NuGetAudit>true</NuGetAudit>
    <NuGetAuditMode>all</NuGetAuditMode>
  </PropertyGroup>
</Project>
```

Remove `<NuGetAudit>false</NuGetAudit>` from the three test projects without changing package versions.

- [ ] **Step 3: Generate and inspect lock files**

```powershell
dotnet restore .\DeadlineWidgetV2.sln --force-evaluate
git status --short
```

Expected: five `packages.lock.json` files are created; no `bin`, `obj`, or cache files are tracked.

- [ ] **Step 4: Prove locked restore and audit**

```powershell
dotnet restore .\DeadlineWidgetV2.sln --locked-mode `
  -warnaserror:NU1901,NU1902,NU1903,NU1904
dotnet list .\DeadlineWidgetV2.sln package --vulnerable --include-transitive
```

Expected: restore succeeds and the vulnerability command reports no vulnerable packages.

- [ ] **Step 5: Upgrade CI from floating SDK/restore to repository policy**

Change `setup-dotnet` to:

```yaml
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
          cache: true
          cache-dependency-path: '**/packages.lock.json'
```

Change restore to:

```yaml
      - name: Restore locked dependencies and audit
        shell: pwsh
        run: dotnet restore .\DeadlineWidgetV2.sln --locked-mode -warnaserror:NU1901,NU1902,NU1903,NU1904
```

- [ ] **Step 6: Run complete regression and commit**

```powershell
dotnet test .\DeadlineWidgetV2.sln -c Release --no-restore
.\tests\Bridge.Tests.ps1
.\tests\Packaging.Tests.ps1
git add global.json Directory.Build.props .github/workflows/ci.yml src tests
git commit -m "build: lock SDK and NuGet dependencies"
```

### Task 2: Add conservative dependency automation

**Files:**
- Create: `.github/dependabot.yml`

**Interfaces:**
- Produces: monthly, separately grouped checks for NuGet and GitHub Actions.

- [ ] **Step 1: Create minimal Dependabot configuration**

```yaml
version: 2
updates:
  - package-ecosystem: nuget
    directory: /
    schedule:
      interval: monthly
    open-pull-requests-limit: 5
  - package-ecosystem: github-actions
    directory: /
    schedule:
      interval: monthly
    open-pull-requests-limit: 5
```

- [ ] **Step 2: Verify scope and commit**

Confirm the file contains no assignee, reviewer, token, custom registry, or organization-specific path, then:

```powershell
git diff --check
git add .github/dependabot.yml
git commit -m "build: add monthly dependency checks"
```

### Task 3: Make release packaging versioned and self-describing

**Files:**
- Modify: `tests/Packaging.Tests.ps1`
- Modify: `scripts/Build-Package.ps1`
- Create: `scripts/Release-Readme.txt`

**Interfaces:**
- Produces: `Build-Package.ps1 -Version <semver>` and a release directory containing `Manager/`, `DeadlineWidgetV2.rmskin`, `Install-V2.ps1`, and `README.txt`.

- [ ] **Step 1: Add failing version and bundle assertions**

Run the builder with `Version = '1.2.3'`. Assert `Rainstaller.cfg` contains `Version=1.2.3`, and assert the release directory file boundary includes:

```powershell
$expectedReleaseFiles = @(
    'DeadlineWidgetV2.rmskin',
    'Install-V2.ps1',
    'README.txt'
)
```

Also assert `Manager/DeadlineWidget.Manager.exe` exists and the script result exposes `ReleaseDirectory`, `PackagePath`, and `PackageSha256` properties.

- [ ] **Step 2: Run Packaging tests and verify RED**

```powershell
.\tests\Packaging.Tests.ps1
```

Expected: FAIL because version is hard-coded and installer/readme are absent from the release directory.

- [ ] **Step 3: Implement validated semantic version input**

Add:

```powershell
[ValidatePattern('^\d+\.\d+\.\d+$')]
[string]$Version = '0.1.0'
```

Use `Version=$Version` in `Rainstaller.cfg`. Copy `Install-V2.ps1` and `Release-Readme.txt` into the release staging directory as `README.txt`. Return:

```powershell
[pscustomobject]@{
    ReleaseDirectory = $outputPath
    PackagePath = Join-Path $outputPath 'DeadlineWidgetV2.rmskin'
    PackageSha256 = $packageHash.Hash
}
```

Update the existing safe-output validator so an existing release directory is accepted only when its exact top-level boundary is `Manager/`, `DeadlineWidgetV2.rmskin`, `Install-V2.ps1`, and `README.txt`. Any additional or missing top-level item remains unsafe and must be rejected before build output is modified.

- [ ] **Step 4: Re-run Packaging tests and regression**

```powershell
.\tests\Packaging.Tests.ps1
dotnet test .\DeadlineWidgetV2.sln -c Release
```

- [ ] **Step 5: Commit**

```powershell
git add scripts/Build-Package.ps1 scripts/Release-Readme.txt tests/Packaging.Tests.ps1
git commit -m "build: produce versioned release bundle"
```

### Task 4: Add the tag-driven release workflow

**Files:**
- Create: `.github/workflows/release.yml`

**Interfaces:**
- Consumes: annotated or lightweight tags matching `v<major>.<minor>.<patch>`.
- Produces: `.rmskin`, full win-x64 ZIP, and `SHA256SUMS.txt` attached to a GitHub Release.

- [ ] **Step 1: Create the workflow trigger and validation job**

Use:

```yaml
name: Release

on:
  push:
    tags:
      - 'v[0-9]+.[0-9]+.[0-9]+'

permissions:
  contents: write

jobs:
  release:
    runs-on: windows-2022
```

Reuse the CI setup: checkout, `setup-dotnet` from `global.json`, NuGet cache, MSYS2 x64 GCC, locked restore, .NET tests, Bridge tests, and Packaging tests.

- [ ] **Step 2: Add version extraction and production build**

```yaml
      - name: Resolve version
        id: version
        shell: pwsh
        run: |
          $version = '${{ github.ref_name }}'.TrimStart('v')
          if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Invalid release tag: $version" }
          "value=$version" >> $env:GITHUB_OUTPUT
      - name: Build release bundle
        shell: pwsh
        run: |
          .\scripts\Build-Package.ps1 `
            -Version '${{ steps.version.outputs.value }}' `
            -SkipSolutionTests `
            -DotNetPath (Get-Command dotnet).Source `
            -CompilerPath C:\msys64\mingw64\bin\g++.exe
```

- [ ] **Step 3: Archive and hash artifacts**

```yaml
      - name: Archive and checksum
        shell: pwsh
        run: |
          $version = '${{ steps.version.outputs.value }}'
          Compress-Archive -Path .\artifacts\* -DestinationPath ".\DeadlineWidgetV2-v$version-win-x64.zip"
          Copy-Item .\artifacts\DeadlineWidgetV2.rmskin ".\DeadlineWidgetV2-v$version.rmskin"
          Get-FileHash ".\DeadlineWidgetV2-v$version-win-x64.zip", ".\DeadlineWidgetV2-v$version.rmskin" -Algorithm SHA256 |
            ForEach-Object { "$($_.Hash)  $([IO.Path]::GetFileName($_.Path))" } |
            Set-Content .\SHA256SUMS.txt -Encoding ascii
```

- [ ] **Step 4: Add release creation without running it**

```yaml
      - name: Publish GitHub Release
        env:
          GH_TOKEN: ${{ github.token }}
        shell: pwsh
        run: |
          gh release create '${{ github.ref_name }}' `
            ".\DeadlineWidgetV2-v${{ steps.version.outputs.value }}-win-x64.zip" `
            ".\DeadlineWidgetV2-v${{ steps.version.outputs.value }}.rmskin" `
            .\SHA256SUMS.txt `
            --verify-tag --generate-notes --title 'Deadline Widget ${{ github.ref_name }}'
```

- [ ] **Step 5: Verify locally without creating a release**

Run the complete build script locally with `-Version 0.1.0`, create the ZIP and checksum in a temporary directory, verify each checksum, then delete only that validated temporary directory. Do not create a tag.

- [ ] **Step 6: Inspect and commit**

```powershell
git diff --check
git status --short
git add .github/workflows/release.yml
git commit -m "ci: add tagged release workflow"
```

