param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$Project,
    [switch]$PlanOnly
)
$ErrorActionPreference = 'Stop'
$layoutRoot = (Resolve-Path -LiteralPath $OutputDirectory).ProviderPath.TrimEnd([IO.Path]::DirectorySeparatorChar)
if (-not (Test-Path -LiteralPath $layoutRoot -PathType Container)) { throw 'OutputDirectory must be an existing directory.' }
$layoutPrefix = $layoutRoot + [IO.Path]::DirectorySeparatorChar
function Assert-LayoutPath([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    if (-not $resolved.StartsWith($layoutPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw ('Path escapes output directory: ' + $Path) }
    return $resolved
}
$layoutItems = @(Get-ChildItem -LiteralPath $layoutRoot -Force | Where-Object { $_.Name -notin @('Versions', 'Others') })
$layoutLatest = @{}
$layoutPackages = @{}
foreach ($layoutItem in $layoutItems) {
    if ($layoutItem.PSIsContainer -or $layoutItem.Name -notmatch '^(?<project>.+)-(?<version>\d+\.\d+\.\d+)\.zip$') { continue }
    $layoutName = $Matches.project
    $layoutVersion = [Version]$Matches.version
    if (-not $layoutLatest.ContainsKey($layoutName) -or $layoutVersion -gt $layoutLatest[$layoutName]) {
        $layoutLatest[$layoutName] = $layoutVersion
        $layoutPackages[$layoutName] = $layoutItem.FullName
    }
}
if ($Project -and -not $layoutLatest.ContainsKey($Project)) { throw ('No release ZIP found for project: ' + $Project) }
$layoutConfigProjects = @{
    'botanic.server.authority.cfg' = 'BotanicServerAuthority'
    'botanic.toomanylags.fgnaddon.cfg' = 'TooManyLags-FGNAddon'
    'botanic.toomanylags.standalone.cfg' = 'TooManyLags-Standalone'
}
$layoutReadmes = @{}
$layoutPlan = New-Object 'System.Collections.Generic.List[object]'
foreach ($layoutItem in $layoutItems) {
    $layoutName = $null
    $layoutVersion = $null
    $layoutCurrentAlias = $false
    if ($layoutItem.Name -match '^(?<project>.+?)-(?<version>\d+\.\d+\.\d+)(?=$|[.-])' -and $layoutLatest.ContainsKey($Matches.project)) {
        $layoutName = $Matches.project
        $layoutVersion = [Version]$Matches.version
    } elseif (-not $layoutItem.PSIsContainer -and $layoutItem.Extension -eq '.dll' -and $layoutLatest.ContainsKey($layoutItem.BaseName)) {
        $layoutName = $layoutItem.BaseName
        $layoutVersion = [Reflection.AssemblyName]::GetAssemblyName($layoutItem.FullName).Version
        $layoutVersion = [Version]$layoutVersion.ToString(3)
    } elseif (-not $layoutItem.PSIsContainer -and $layoutConfigProjects.ContainsKey($layoutItem.Name)) {
        $layoutName = $layoutConfigProjects[$layoutItem.Name]
        $layoutHeader = Get-Content -LiteralPath $layoutItem.FullName -TotalCount 4 | Where-Object { $_ -match '^## Settings file was created by plugin ' } | Select-Object -First 1
        if ($layoutHeader -match ' v(?<version>\d+\.\d+\.\d+)$') { $layoutVersion = [Version]$Matches.version }
    } elseif (-not $layoutItem.PSIsContainer -and $layoutItem.Name -match '^(?<project>.+)-(?:README|Anleitung)\.md$' -and $layoutLatest.ContainsKey($Matches.project)) {
        $layoutName = $Matches.project
        if (-not $layoutReadmes.ContainsKey($layoutName)) {
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $layoutArchive = [IO.Compression.ZipFile]::OpenRead($layoutPackages[$layoutName])
            try {
                $layoutEntry = $layoutArchive.Entries | Where-Object { $_.Name -ieq 'README.md' } | Sort-Object { $_.FullName.Length } | Select-Object -First 1
                $layoutText = $null
                if ($layoutEntry) {
                    $layoutReader = New-Object IO.StreamReader($layoutEntry.Open())
                    try { $layoutText = $layoutReader.ReadToEnd().Replace("`r`n", "`n") } finally { $layoutReader.Dispose() }
                }
                $layoutReadmes[$layoutName] = $layoutText
            } finally { $layoutArchive.Dispose() }
        }
        $layoutCurrentAlias = $null -ne $layoutReadmes[$layoutName] -and [IO.File]::ReadAllText($layoutItem.FullName).Replace("`r`n", "`n") -ceq $layoutReadmes[$layoutName]
    }
    if ($Project -and $layoutName -ne $Project) { continue }
    if ($layoutName -and $layoutLatest.ContainsKey($layoutName)) {
        if ($layoutCurrentAlias -or ($layoutVersion -and $layoutVersion -eq $layoutLatest[$layoutName])) { continue }
        if ($layoutVersion) {
            $layoutTarget = Join-Path $layoutRoot ('Versions/' + $layoutName + '/' + $layoutVersion.ToString(3) + '/' + $layoutItem.Name)
        } else {
            $layoutTarget = Join-Path $layoutRoot ('Others/' + $layoutItem.Name)
        }
    } else {
        $layoutTarget = Join-Path $layoutRoot ('Others/' + $layoutItem.Name)
    }
    $layoutPlan.Add([PSCustomObject]@{ Source = (Assert-LayoutPath $layoutItem.FullName); Destination = (Assert-LayoutPath $layoutTarget); Directory = $layoutItem.PSIsContainer })
}
# Preflight every move before changing any path. Existing archive files are never overwritten.
foreach ($layoutMove in $layoutPlan) {
    if (Test-Path -LiteralPath $layoutMove.Destination) { throw ('Archive collision; no files moved: ' + $layoutMove.Destination) }
    $layoutAncestor = $layoutMove.Destination
    while ($layoutAncestor -ne $layoutRoot) {
        if (Test-Path -LiteralPath $layoutAncestor) {
            $layoutExisting = Get-Item -LiteralPath $layoutAncestor -Force
            if (($layoutExisting.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw ('Linked destination is not supported: ' + $layoutAncestor) }
            if (-not $layoutExisting.PSIsContainer) { throw ('Destination parent is a file: ' + $layoutAncestor) }
        }
        $layoutAncestor = Split-Path $layoutAncestor
    }
    $layoutSources = @(Get-Item -LiteralPath $layoutMove.Source -Force)
    if ($layoutMove.Directory) { $layoutSources += @(Get-ChildItem -LiteralPath $layoutMove.Source -Recurse -Force) }
    foreach ($layoutSource in $layoutSources) {
        if (($layoutSource.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw ('Linked source is not supported: ' + $layoutSource.FullName) }
    }
}
if ($PlanOnly) { return $layoutPlan }
foreach ($layoutMove in $layoutPlan) {
    New-Item -ItemType Directory -Force -Path (Split-Path $layoutMove.Destination) | Out-Null
    Move-Item -LiteralPath $layoutMove.Source -Destination $layoutMove.Destination
}
Write-Output ('Output layout: ' + $layoutPlan.Count + ' items moved; current releases stay at the root.')
