#Requires -Version 5
<#
.SYNOPSIS
    Export the Polytoria Creator (Studio) build for desktop.

.DESCRIPTION
    Godot does NOT pass an export preset's `custom_features` (e.g. "creator") to the C# /
    MSBuild build. On desktop the creator and the client share the same build target
    (windows / win-x64 / ExportRelease), so Polytoria.csproj has no way to tell a creator
    export from a client one: `IsCreator` defaults to false and `scripts/creator/**` is
    stripped from the compile, which then fails at runtime with:

        ERROR: Cannot instantiate C# script because the associated class could not be
        found. Script: 'res://scripts/creator/ui/LoadOverlay.cs'. ...

    Polytoria.csproj DOES honor the `PT_BUILD_TARGET=CREATOR` MSBuild property / environment
    variable (it sets IsCreator=true, which compiles scripts/creator/** and defines CREATOR).
    This script sets that env var and then runs the Godot export. Godot's internal `dotnet`
    build inherits the variable, so the creator code is compiled into the exported binary.

.PARAMETER Preset
    Name of the Godot export preset to export (must exist in export_presets.cfg). Use a
    preset whose custom_features include "creator" (so the runtime routes to the Creator
    entry). See the note at the bottom of this file about using a dedicated creator preset.

.PARAMETER Output
    Output path for the exported executable.

.PARAMETER Godot
    Path to the Godot 4.x .NET (mono) editor binary. Falls back to $env:GODOT, then `godot`
    on PATH, then C:\Godot\Godot_v4.7-stable_mono_win64*.exe.

.PARAMETER DebugBuild
    Export the debug build (--export-debug) instead of release.

.PARAMETER Clean
    Delete the cached C# build output first. Recommended when switching build targets
    (client <-> creator) so no stale, wrongly-stripped assembly is reused.

.EXAMPLE
    ./export-creator.ps1 -Output "$HOME\Downloads\PolytoriaCreator.exe" -Clean
#>
param(
    [string]$Preset = "Windows Desktop",
    [string]$Output = "$PSScriptRoot\build\PolytoriaCreator.exe",
    [string]$Godot = $env:GODOT,
    [switch]$DebugBuild,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
$projectDir = $PSScriptRoot

# --- Locate the Godot .NET (mono) editor binary ---
if (-not $Godot) {
    $Godot = (Get-Command godot -ErrorAction SilentlyContinue).Source
}
if (-not $Godot) {
    $Godot = @(
        "C:\Godot\Godot_v4.7-stable_mono_win64_console.exe",
        "C:\Godot\Godot_v4.7-stable_mono_win64.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $Godot -or -not (Test-Path $Godot)) {
    throw "Godot .NET editor not found. Pass -Godot <path> or set `$env:GODOT."
}

$outDir = Split-Path -Parent $Output
if ($outDir -and -not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
}

if ($Clean) {
    Write-Host "Cleaning cached C# build output..." -ForegroundColor DarkGray
    Remove-Item -Recurse -Force `
        "$projectDir\.godot\mono\temp\obj", "$projectDir\.godot\mono\temp\bin" `
        -ErrorAction SilentlyContinue
}

# --- THE FIX: compile scripts/creator/** into this export (IsCreator=true). ---
# Godot's internal `dotnet` build inherits this environment variable.
$env:PT_BUILD_TARGET = "CREATOR"

$exportFlag = if ($DebugBuild) { "--export-debug" } else { "--export-release" }
Write-Host "Exporting Creator  preset='$Preset'  ->  '$Output'" -ForegroundColor Cyan
Write-Host "  (PT_BUILD_TARGET=CREATOR : IsCreator=true, scripts/creator/** compiled in)" -ForegroundColor DarkGray

$code = 1
try {
    & $Godot --headless --path $projectDir $exportFlag $Preset $Output
    $code = $LASTEXITCODE
}
finally {
    Remove-Item Env:\PT_BUILD_TARGET -ErrorAction SilentlyContinue
}

if ($code -ne 0) {
    throw "Godot export failed (exit $code). See output above."
}
Write-Host "Creator export complete: $Output" -ForegroundColor Green

# -----------------------------------------------------------------------------------------
# RECOMMENDED: give the creator its OWN export preset.
#
# Your "Windows Desktop" preset currently has custom_features = "client,beta,creator".
# The "creator" feature makes the RUNTIME route to the Creator entry (AppEntry checks
# OS.HasFeature("creator")). That means if you ever export that same preset WITHOUT
# PT_BUILD_TARGET=CREATOR (e.g. a normal client export), you get a binary that routes to
# the creator but has no creator code compiled in -> the same LoadOverlay.cs crash.
#
# Cleanest setup:
#   * "Windows Desktop"  -> custom_features = "client,beta"     (the game client)
#   * "Windows Creator"  -> custom_features = "creator,beta"    (export with this script)
# -----------------------------------------------------------------------------------------
