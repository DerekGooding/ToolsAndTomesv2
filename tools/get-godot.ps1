<#
.SYNOPSIS
    Downloads the project's pinned Godot .NET build into tools/godot (portable, self-contained, git-ignored).

.DESCRIPTION
    Fetches the official Windows x64 .NET ("mono") build from Godot's GitHub releases, checks it against the
    release's published SHA-512 sums, extracts it to tools/godot, and creates the `_sc_` marker so the editor keeps
    its settings and caches in tools/godot/editor_data instead of %APPDATA%. Deleting the repo removes everything.

    Run from anywhere:  powershell -ExecutionPolicy Bypass -File tools/get-godot.ps1 [-Force]
#>
param(
    [string]$Version = "4.7.2-stable",
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue" # Invoke-WebRequest is far faster without the progress bar

$dest = Join-Path $PSScriptRoot "godot"
$name = "Godot_v$($Version)_mono_win64"
$exe = Join-Path $dest "$($name)_console.exe"

if ((Test-Path $exe) -and -not $Force) {
    Write-Host "Godot $Version is already in $dest (use -Force to reinstall)."
    exit 0
}

$base = "https://github.com/godotengine/godot/releases/download/$Version"
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("godot-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $tmp | Out-Null
try {
    $zip = Join-Path $tmp "$name.zip"
    Write-Host "Downloading $name.zip ..."
    Invoke-WebRequest "$base/$name.zip" -OutFile $zip
    Invoke-WebRequest "$base/SHA512-SUMS.txt" -OutFile (Join-Path $tmp "SHA512-SUMS.txt")

    $expected = (Get-Content (Join-Path $tmp "SHA512-SUMS.txt") | Where-Object { $_ -match "\s$([regex]::Escape("$name.zip"))$" }) -split '\s+' | Select-Object -First 1
    if (-not $expected) { throw "$name.zip is not listed in the release's SHA512-SUMS.txt." }
    $actual = (Get-FileHash $zip -Algorithm SHA512).Hash
    if ($actual -ne $expected.ToUpperInvariant()) { throw "Checksum mismatch for $name.zip: expected $expected, got $actual." }
    Write-Host "Checksum OK."

    Expand-Archive $zip -DestinationPath $tmp -Force
    $extracted = Join-Path $tmp $name
    New-Item -ItemType Directory -Force $dest | Out-Null
    # Replace the engine files; keep editor_data (settings, caches) if it exists.
    Get-ChildItem $dest -Exclude "editor_data" | Remove-Item -Recurse -Force
    Get-ChildItem $extracted | Move-Item -Destination $dest
    New-Item -ItemType File -Force (Join-Path $dest "_sc_") | Out-Null
    Write-Host "Installed Godot $Version to $dest (self-contained)."
}
finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
