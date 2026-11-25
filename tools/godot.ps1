<#
.SYNOPSIS
    Runs the repo's own Godot (tools/godot) with any arguments, e.g.
      tools/godot.ps1 --path client/ToolsAndTomes.Client                          # play
      tools/godot.ps1 -e --path client/ToolsAndTomes.Client                       # open the editor
      tools/godot.ps1 --headless --path client/ToolsAndTomes.Client -- --autoplay # smoke test
    Run tools/get-godot.ps1 first on a fresh clone.
#>
$exe = Get-ChildItem (Join-Path $PSScriptRoot "godot") -Filter "Godot_v*_mono_win64_console.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $exe) {
    Write-Error "Godot isn't in tools/godot yet. Run: powershell -ExecutionPolicy Bypass -File tools/get-godot.ps1"
    exit 1
}
& $exe.FullName @args
exit $LASTEXITCODE
