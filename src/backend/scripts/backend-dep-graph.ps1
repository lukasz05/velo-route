#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Fan-in/fan-out metrics + a namespace-scoped dependency graph for the VeloRoute
  backend, filling the gap ArchUnitNET's rule-only API doesn't cover (see
  context/map/artifact-2-structure.md). Mirrors the JSON+dot pipeline used for
  the frontend via dependency-cruiser.

.PARAMETER FocusNamespace
  First-party namespace to center the rendered subgraph on. Defaults to
  VeloRoute.Routing (the hottest backend folder per artifact-1-territory.md).

.PARAMETER SkipBuild
  Skip "dotnet build" and reuse the existing VeloRoute.Tests build output.
#>
param(
    [string]$FocusNamespace = "VeloRoute.Routing",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$backendRoot = Split-Path -Parent $PSScriptRoot
$testsProject = Join-Path $backendRoot "VeloRoute.Tests/VeloRoute.Tests.csproj"
$binDir = Join-Path $backendRoot "VeloRoute.Tests/bin/Debug/net10.0"
$mapDir = Join-Path $backendRoot "../../context/map" | Resolve-Path -ErrorAction SilentlyContinue
if (-not $mapDir) {
    $mapDir = New-Item -ItemType Directory -Force -Path (Join-Path $backendRoot "../../context/map")
}
$mapDir = $mapDir.ToString()

if (-not $SkipBuild) {
    Write-Host "Building VeloRoute.Tests..."
    dotnet build $testsProject --nologo --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }
}

if (-not (Test-Path (Join-Path $binDir "VeloRoute.dll"))) {
    throw "VeloRoute.dll not found under $binDir - build first or drop -SkipBuild"
}

# ArchUnitNET.dll and its own deps (Mono.Cecil*) were loaded at parse time in
# earlier probing via LoadFrom, which doesn't register them for the default
# ALC's normal probing path - resolve by filename from the same output dir.
$resolving = {
    param($alc, $name)
    $path = Join-Path $binDir ($name.Name + ".dll")
    if (Test-Path $path) { return $alc.LoadFromAssemblyPath($path) }
    return $null
}
[System.Runtime.Loader.AssemblyLoadContext]::Default.add_Resolving($resolving)

$veloRouteAsm = [System.Reflection.Assembly]::LoadFrom((Join-Path $binDir "VeloRoute.dll"))
Add-Type -Path (Join-Path $binDir "ArchUnitNET.dll")

Write-Host "Loading architecture..."
$loader = (New-Object ArchUnitNET.Loader.ArchLoader).LoadAssemblies(@($veloRouteAsm))
$architecture = $loader.Build()

# First-party only (mirrors doNotFollow: node_modules on the frontend side) -
# Mono.Cecil resolves BCL/NuGet types by metadata reference even though only
# VeloRoute.dll was loaded, so Dependencies includes framework noise unless filtered.
$firstPartyTypes = $architecture.Types | Where-Object { $_.FullName.StartsWith("VeloRoute.") -and -not $_.FullName.StartsWith("VeloRoute.Migrations.") }

# Build a deduped first-party edge set ourselves rather than trust
# Dependencies/BackwardsDependencies' own counting (those count every call
# site, not every distinct type pair - we want the same "distinct dependency"
# semantics the frontend fan-in/out table used).
$edges = New-Object System.Collections.Generic.HashSet[string]
$edgeList = @()
foreach ($type in $firstPartyTypes) {
    $seenTargets = New-Object System.Collections.Generic.HashSet[string]
    foreach ($dep in $type.Dependencies) {
        $targetName = $dep.Target.FullName
        if (-not $targetName.StartsWith("VeloRoute.")) { continue }
        if ($targetName -eq $type.FullName) { continue }
        if ($seenTargets.Add($targetName)) {
            $edgeKey = "$($type.FullName)=>$targetName"
            if ($edges.Add($edgeKey)) {
                $edgeList += [pscustomobject]@{ From = $type.FullName; To = $targetName }
            }
        }
    }
}

$fanOut = @{}
$fanIn = @{}
foreach ($t in $firstPartyTypes) { $fanOut[$t.FullName] = 0; $fanIn[$t.FullName] = 0 }
foreach ($e in $edgeList) {
    $fanOut[$e.From]++
    $fanIn[$e.To]++
}

# ArchUnitNET's loader drops the top-level-statements Program type as
# compiler-generated, so its DI registrations/handler wiring never show up as
# Cecil-derived edges above - a handful of first-party types (LoopRouteGenerator,
# OpenRouteServiceClient, AppDbContext, ...) read as FanIn=0 "dead code" even
# though Program.cs wires them in. Mitigate with a text scan: does the type's
# short name appear as a whole word in Program.cs? Kept as a separate,
# explicitly-labelled edge set rather than folded into FanIn/FanOut, since this
# is a source-text heuristic, not an IL-verified dependency.
$compositionRootNode = "Program.cs (composition root)"
$programCsPath = Get-ChildItem -Path $backendRoot -Filter "Program.cs" -Recurse -File |
    Where-Object { $_.FullName -notmatch "[\\/](obj|bin)[\\/]" } |
    Select-Object -First 1 -ExpandProperty FullName
$programCsText = if ($programCsPath) { Get-Content -Path $programCsPath -Raw } else { "" }

$compositionRootMatches = New-Object System.Collections.Generic.HashSet[string]
$compositionRootEdges = @()
if ($programCsPath) {
    foreach ($t in $firstPartyTypes) {
        $shortName = $t.FullName.Substring($t.FullName.LastIndexOf(".") + 1)
        if ([regex]::IsMatch($programCsText, "\b$([regex]::Escape($shortName))\b")) {
            $compositionRootMatches.Add($t.FullName) | Out-Null
            $compositionRootEdges += [pscustomobject]@{ From = $compositionRootNode; To = $t.FullName }
        }
    }
} else {
    Write-Warning "Program.cs not found under $backendRoot - skipped composition-root text scan."
}

$metrics = $firstPartyTypes | ForEach-Object {
    [pscustomobject]@{
        Type                       = $_.FullName
        Namespace                  = $_.Namespace.FullName
        FanIn                      = $fanIn[$_.FullName]
        FanOut                     = $fanOut[$_.FullName]
        Members                    = $_.Members.Count
        ReferencedByCompositionRoot = $compositionRootMatches.Contains($_.FullName)
    }
} | Sort-Object -Property @{Expression = "FanIn"; Descending = $true}, @{Expression = "FanOut"; Descending = $true}

$metricsPath = Join-Path $mapDir "backend-metrics.json"
$metrics | ConvertTo-Json -Depth 3 | Set-Content -Path $metricsPath -Encoding utf8
Write-Host "Wrote $metricsPath"

Write-Host ""
Write-Host "--- top fan-in ---"
$metrics | Select-Object -First 15 | Format-Table Type, Namespace, FanIn, FanOut, Members

Write-Host "--- top fan-out ---"
$metrics | Sort-Object -Property FanOut -Descending | Select-Object -First 15 | Format-Table Type, Namespace, FanIn, FanOut, Members

Write-Host "--- FanIn=0 types referenced by Program.cs (composition-root wiring, not IL-derived) ---"
$metrics | Where-Object { $_.FanIn -eq 0 -and $_.ReferencedByCompositionRoot } | Format-Table Type, Namespace, FanIn, FanOut, Members

# Ego graph: every edge touching the focus namespace, plus its immediate first-party neighbors.
$focusTypes = ($firstPartyTypes | Where-Object { $_.Namespace.FullName -eq $FocusNamespace }).FullName
$focusSet = New-Object System.Collections.Generic.HashSet[string]
$focusTypes | ForEach-Object { $focusSet.Add($_) | Out-Null }

$relevantEdges = $edgeList | Where-Object { $focusSet.Contains($_.From) -or $focusSet.Contains($_.To) }
$relevantCompositionRootEdges = $compositionRootEdges | Where-Object { $focusSet.Contains($_.To) }

function ShortName($fullName) { $fullName -replace '^VeloRoute\.', '' }

# Styled to match the frontend's dependency-cruiser dot output (context/map/routeapp-subgraph.svg):
# Helvetica 9pt labels, rounded light-blue filled nodes grouped into per-namespace clusters (its
# equivalent of dependency-cruiser's per-folder clusters), semi-transparent black edges. The
# composition-root text-scan edges reuse dependency-cruiser's own "external/uncertain module" red
# (#c40b0a) so the same color already means "not a verified first-party import" in both graphs.
$namespaceOf = @{}
foreach ($t in $firstPartyTypes) { $namespaceOf[$t.FullName] = $t.Namespace.FullName }

$involvedTypes = New-Object System.Collections.Generic.HashSet[string]
foreach ($e in $relevantEdges) { $involvedTypes.Add($e.From) | Out-Null; $involvedTypes.Add($e.To) | Out-Null }
foreach ($e in $relevantCompositionRootEdges) { $involvedTypes.Add($e.To) | Out-Null }

$byNamespace = [ordered]@{}
foreach ($full in ($involvedTypes | Sort-Object)) {
    # $edgeList's target filter only checks the "VeloRoute." prefix, not the Migrations
    # exclusion applied to $firstPartyTypes, so a target can be a Migrations type with no
    # entry here - group those under a catch-all rather than indexing with a null key.
    $ns = $namespaceOf[$full]
    if (-not $ns) { $ns = "(other)" }
    if (-not $byNamespace.Contains($ns)) { $byNamespace[$ns] = @() }
    $byNamespace[$ns] += $full
}

$dotLines = @(
    "digraph backend {",
    "  rankdir=LR;",
    "  fontname=`"Helvetica,sans-Serif`";",
    "  node [shape=box, style=`"rounded,filled`", fillcolor=`"#bbfeff`", color=black, fontname=`"Helvetica,sans-Serif`", fontsize=9];",
    "  edge [color=`"#00000033`", fontname=`"Helvetica,sans-Serif`", fontsize=9];"
)
$clusterIndex = 0
foreach ($ns in $byNamespace.Keys) {
    $clusterIndex++
    $dotLines += "  subgraph cluster_$clusterIndex {"
    $dotLines += "    label=`"$(ShortName $ns)`"; fontname=`"Helvetica,sans-Serif`"; fontsize=9; fontweight=bold; style=rounded; color=black;"
    foreach ($full in $byNamespace[$ns]) {
        $dotLines += "    `"$(ShortName $full)`";"
    }
    $dotLines += "  }"
}
$dotLines += "  `"$compositionRootNode`" [shape=note, style=filled, fillcolor=`"#ffe0e0`", color=`"#c40b0a`", fontcolor=`"#c40b0a`"];"
foreach ($e in $relevantEdges) {
    $dotLines += "  `"$(ShortName $e.From)`" -> `"$(ShortName $e.To)`";"
}
foreach ($e in $relevantCompositionRootEdges) {
    # Text-scan-derived, not Cecil/IL-derived - style distinctly so provenance stays honest.
    $dotLines += "  `"$($e.From)`" -> `"$(ShortName $e.To)`" [style=dashed, color=`"#c40b0a99`", fontcolor=`"#c40b0a`"];"
}
$dotLines += "}"

$dotPath = Join-Path $mapDir "routing-subgraph.dot"
$dotLines -join "`n" | Set-Content -Path $dotPath -Encoding utf8
Write-Host "Wrote $dotPath ($($relevantEdges.Count) IL-derived edges + $($relevantCompositionRootEdges.Count) composition-root edges around $FocusNamespace)"

$svgPath = Join-Path $mapDir "routing-subgraph.svg"
$dotExe = Get-Command dot -ErrorAction SilentlyContinue
if ($dotExe) {
    & dot -Tsvg -o $svgPath $dotPath
    Write-Host "Wrote $svgPath"
} else {
    Write-Warning "graphviz 'dot' not found on PATH - skipped SVG render, .dot file is still usable."
}
