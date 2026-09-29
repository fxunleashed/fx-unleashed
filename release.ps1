<#
.SYNOPSIS
  Builds a release of FX Unleashed: version, build, offline tests, zip, manifest.json, notes, safety scan, and (with
  -Publish) the GitHub release. CI can't do this because SimHub's DLLs aren't redistributable (NEXT.md B).

.EXAMPLE
  .\release.ps1 0.3.0 -Beta 1            # builds release\v0.3.0-beta.1\ (nothing is published)
  .\release.ps1 0.3.0 -Publish           # ... and creates the GitHub release v0.3.0 with gh

.NOTES
  Output: release\v<version>\FXUnleashed-v<version>.zip, manifest.json, notes.md. The updater (Updater.cs) needs the
  zip and manifest.json as release assets; the manifest's hashes are checked before anything is installed.
  -Publish is outward-facing: it creates a public release on -Repo. Without it the script only prints the command.
#>
param(
    [Parameter(Mandatory = $true, Position = 0)][string]$Version,
    [int]$Beta = 0,
    [string]$Repo = "fxunleashed/fx-unleashed",
    [string]$NotesFile,
    [string]$MinSimHub = "9.0",
    [int]$FirmwareMin = 6,
    [int]$FirmwareRecommended = 6,
    [switch]$SkipTests,
    [switch]$Publish
)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must be X.Y.Z (add -Beta N for a pre-release)" }
$full = if ($Beta -gt 0) { "$Version-beta.$Beta" } else { $Version }
$channel = if ($Beta -gt 0) { "beta" } else { "stable" }
$tag = "v$full"
Write-Host "== FX Unleashed $tag ($channel)"

# 1. Version: one source of truth, <Version> in the csproj
$csproj = Join-Path $PSScriptRoot "FXProRpmSync.csproj"
$text = [IO.File]::ReadAllText($csproj)
$new = [Text.RegularExpressions.Regex]::Replace($text, '<Version>[^<]*</Version>', "<Version>$full</Version>")
if ($new -ne $text) { [IO.File]::WriteAllText($csproj, $new); Write-Host "   <Version> set to $full" }

# 2. Build (no copy into SimHub) and the offline tests
dotnet build $csproj -c Release -p:DeployToSimHub=false -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "build failed" }
$dll = Join-Path $PSScriptRoot "bin\Release\net48\User.FXProRpmSync.dll"
$built = (Get-Item $dll).VersionInfo.ProductVersion -replace '\+.*$', ''
if ($built -ne $full) { throw "the built DLL says $built, not $full" }
if (-not $SkipTests) {
    dotnet build (Join-Path $PSScriptRoot "tools\UsbTest\UsbTest.csproj") -c Release -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "UsbTest build failed" }
    $out = Join-Path $env:TEMP "fxu-release-tests"
    # the exit code decides; log4net's harmless stderr line must not stop the script (PowerShell 5.1 wraps it as an error)
    $ErrorActionPreference = "Continue"
    $testLog = & (Join-Path $PSScriptRoot "tools\UsbTest\bin\Release\net48\UsbTest.exe") $out 2>&1 | ForEach-Object { "$_" }
    $code = $LASTEXITCODE
    $ErrorActionPreference = "Stop"
    if ($code -ne 0) { $testLog | Write-Host; throw "offline tests failed" }
    Write-Host "   offline tests: OK"
}

# 3. Zip
$dir = Join-Path $PSScriptRoot "release\$tag"
if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
New-Item -ItemType Directory $dir | Out-Null
$stage = Join-Path $dir "zip"
New-Item -ItemType Directory $stage | Out-Null
$files = @("bin\Release\net48\User.FXProRpmSync.dll", "README.md", "LICENSE", "NOTICE", "assets\Simagic_FX-Pro.atsrdevice")
foreach ($f in $files) { Copy-Item (Join-Path $PSScriptRoot $f) $stage }

# 4. Safety scan (NEXT.md O4): no Simagic firmware, no decryptor, no key material in what we ship
foreach ($f in Get-ChildItem $stage -File) {
    if ($f.Extension -in ".sfu", ".bin", ".tft", ".hex") { throw "refusing to ship $($f.Name)" }
    if ($f.Name -match 'SfuDecrypt') { throw "refusing to ship $($f.Name)" }
    $bytes = [IO.File]::ReadAllBytes($f.FullName)
    $ascii = [Text.Encoding]::ASCII.GetString($bytes)
    if ($ascii.Contains("SIMPROSFU100")) { throw "$($f.Name) contains a SimPro firmware header" }
}
$zipName = "FXUnleashed-$tag.zip"
$zip = Join-Path $dir $zipName
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
Remove-Item -Recurse -Force $stage

function Sha256($path) { (Get-FileHash -Algorithm SHA256 $path).Hash.ToLowerInvariant() }

# 5. manifest.json (what the updater checks)
$manifest = [ordered]@{
    version   = $full
    channel   = $channel
    dll       = [ordered]@{ name = "User.FXProRpmSync.dll"; sha256 = (Sha256 $dll); size = (Get-Item $dll).Length }
    zip       = [ordered]@{ name = $zipName; sha256 = (Sha256 $zip) }
    minSimHub = $MinSimHub
    firmware  = [ordered]@{ min = $FirmwareMin; recommended = $FirmwareRecommended }
    notes     = "https://github.com/$Repo/releases/tag/$tag"
    published = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
}
$manifestPath = Join-Path $dir "manifest.json"
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 4))

# 6. Notes: a given file, else the git log since the last tag; the disclaimer always goes first
$notes = Join-Path $dir "notes.md"
$ErrorActionPreference = "Continue" # git writes "no tags yet" to stderr
$body = if ($NotesFile) { Get-Content -Raw $NotesFile } else {
    $last = git describe --tags --abbrev=0 2>$null
    $range = if ($LASTEXITCODE -eq 0 -and $last) { "$last..HEAD" } else { "HEAD~30..HEAD" }
    "## Changes`n`n" + ((git log --no-merges --format="- %s" $range) -join "`n")
}
$disclaimer = (Get-Content -Raw (Join-Path $PSScriptRoot "docs\legal\disclaimer.md")).Trim()
[IO.File]::WriteAllText($notes, "> $($disclaimer -replace "`r?`n", ' ')`n`n$body`n")

Write-Host "   $zip"
Write-Host "   $manifestPath"
Write-Host "   $notes"
$cmd = "gh release create $tag `"$zip`" `"$manifestPath`" --repo $Repo --title `"FX Unleashed $tag`" --notes-file `"$notes`"" + $(if ($Beta -gt 0) { " --prerelease" } else { "" })
if ($Publish) {
    Write-Host "== publishing $tag to $Repo"
    Invoke-Expression $cmd
    if ($LASTEXITCODE -ne 0) { throw "gh release create failed" }
} else {
    Write-Host "== not published. To publish:"
    Write-Host "   $cmd"
}
