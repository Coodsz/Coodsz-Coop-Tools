<#
Makes Realm of Thrones' campaign map readable by the Coop dedicated server, offline.

The server cannot read a full campaign map scene: its engine has no landscape renderer and never
loads prefabs, so terrain and entities crash it. Its own packaging strips War Sails' Main_map the
same way. This script reads ROT_Map\SceneObj\Main_map and writes, under -OutDir:

  ROT_Map\SceneObj\Main_map\                    the map folder for the server's engine\Modules\ROT_Map
    scene.xscene                                the scene without entities and without its terrain node
    naval_map_points.txt                        War Sails' pirate spawn points and village drop offs,
                                                read from the entities before they go
    flowmap.dds                                 Realm of Thrones' wind map, or a calm one when it has none
    (every other file of the map folder, minus ShaderCache and TileSets)
  RealmOfThrones.CoopCompat\ModuleData\rot_map.txt
                                                the module, border and terrain size the compat module
                                                gives the server

Nothing is written into -RotMap. The output map folder must be new or empty.

  powershell -ExecutionPolicy Bypass -File Stage-RotServerMap.ps1 -RotMap "<...>\Modules\ROT_Map" -OutDir "<new folder>"
#>
param(
    [Parameter(Mandatory = $true)][string]$RotMap,
    [Parameter(Mandatory = $true)][string]$OutDir
)

$ErrorActionPreference = 'Stop'
$inv = [Globalization.CultureInfo]::InvariantCulture

# Relative paths are read from where the PowerShell prompt is, not from the process's folder.
$RotMap = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($RotMap)
$OutDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutDir)
$mapSource = Join-Path $RotMap 'SceneObj\Main_map'
$sceneSource = Join-Path $mapSource 'scene.xscene'
if (-not (Test-Path -LiteralPath $sceneSource)) { throw "No Realm of Thrones map scene at $sceneSource" }
if ($OutDir.StartsWith($RotMap.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or $OutDir -ieq $RotMap) {
    throw "The output folder must not be inside $RotMap, so the source map stays untouched"
}
$mapOut = Join-Path $OutDir 'ROT_Map\SceneObj\Main_map'
$dataFolder = Join-Path $OutDir 'RealmOfThrones.CoopCompat\ModuleData'
foreach ($folder in $mapOut, $dataFolder) {
    if ((Test-Path -LiteralPath $folder) -and (Get-ChildItem -LiteralPath $folder -Force | Select-Object -First 1)) {
        throw "$folder already has files. Pick a new output folder."
    }
}
# Everything is read and checked before the first file is written, so a scene this script cannot
# use leaves no half filled output behind.

# ---- entity positions ----
# An entity's frame is its origin and its side, forward and up axes, scaled. rotation_euler turns
# the axes like the game's Mat3.ApplyEulerAngles (about up by z, then side by x, then forward by y,
# each about the axes turned so far). A non_orthogonal_rotation_mat gives the three axes as rows.
function Add-Scaled([double[]]$A, [double]$Ka, [double[]]$B, [double]$Kb) {
    , ([double[]]@(($A[0] * $Ka + $B[0] * $Kb), ($A[1] * $Ka + $B[1] * $Kb), ($A[2] * $Ka + $B[2] * $Kb)))
}

function Read-Numbers($Node, [string]$Name, [double[]]$Default) {
    if ($null -eq $Node -or -not $Node.HasAttribute($Name)) { return , $Default }
    , ([double[]]($Node.GetAttribute($Name) -split ',' | ForEach-Object { [double]::Parse($_.Trim(), $inv) }))
}

function Get-LocalFrame($Entity) {
    $t = $Entity.SelectSingleNode('transform')
    $origin = Read-Numbers $t 'position' @(0, 0, 0)
    $scale = Read-Numbers $t 'scale' @(1, 1, 1)
    $matrix = Read-Numbers $t 'non_orthogonal_rotation_mat' $null
    if ($null -ne $matrix -and $matrix.Count -eq 9) {
        $s = [double[]]@($matrix[0], $matrix[1], $matrix[2])
        $f = [double[]]@($matrix[3], $matrix[4], $matrix[5])
        $u = [double[]]@($matrix[6], $matrix[7], $matrix[8])
    }
    else {
        $e = Read-Numbers $t 'rotation_euler' @(0, 0, 0)
        $s = [double[]]@(1, 0, 0); $f = [double[]]@(0, 1, 0); $u = [double[]]@(0, 0, 1)
        $c = [Math]::Cos($e[2]); $n = [Math]::Sin($e[2])
        $s, $f = (Add-Scaled $s $c $f $n), (Add-Scaled $f $c $s (-$n))
        $c = [Math]::Cos($e[0]); $n = [Math]::Sin($e[0])
        $f, $u = (Add-Scaled $f $c $u $n), (Add-Scaled $u $c $f (-$n))
        $c = [Math]::Cos($e[1]); $n = [Math]::Sin($e[1])
        $s, $u = (Add-Scaled $s $c $u (-$n)), (Add-Scaled $u $c $s $n)
    }
    @{ o = $origin; s = (Add-Scaled $s $scale[0] $s 0); f = (Add-Scaled $f $scale[1] $f 0); u = (Add-Scaled $u $scale[2] $u 0) }
}

# The game_entity this one is a child of, or $null at the top.
function Get-ParentEntity($Entity) {
    $holder = $Entity.ParentNode
    if ($null -eq $holder -or $holder.Name -ne 'children') { return $null }
    $holder.ParentNode
}

function Get-GlobalOrigin($Entity) {
    $local = Get-LocalFrame $Entity
    $point = $local.o
    for ($parent = Get-ParentEntity $Entity; $null -ne $parent; $parent = Get-ParentEntity $parent) {
        $frame = Get-LocalFrame $parent
        $point = Add-Scaled (Add-Scaled (Add-Scaled $frame.o 1 $frame.s $point[0]) 1 $frame.f $point[1]) 1 $frame.u $point[2]
    }
    , $point
}

function Format-Number([double]$Value) { $Value.ToString('0.######', $inv) }

Write-Host "Reading $sceneSource ..."
$scene = New-Object Xml.XmlDocument
$scene.Load($sceneSource)
$sceneNode = $scene.SelectSingleNode('scene')
$entities = $sceneNode.SelectSingleNode('entities')
if ($null -eq $entities) { throw "The scene has no entities node" }

# ---- extents ----
$borders = @{}
foreach ($name in 'border_min', 'border_max') {
    $found = @($entities.SelectNodes(".//game_entity[@name='$name']"))
    if ($found.Count -eq 0) { throw "The scene has no $name entity" }
    if ($found.Count -gt 1) { Write-Warning "The scene has $($found.Count) $name entities; the first one is used, like the game does" }
    $borders[$name] = Get-GlobalOrigin $found[0]
}
$terrain = $sceneNode.SelectSingleNode('terrain')
if ($null -eq $terrain) { throw "The scene has no terrain node, so it has no terrain size (is it already stripped?)" }
$nodeX = [int]$terrain.GetAttribute('node_dimension_x')
$nodeY = [int]$terrain.GetAttribute('node_dimension_y')
$nodeSize = [double]::Parse($terrain.GetAttribute('node_size'), $inv)
$terrainSize = $nodeX * $nodeSize
if ($nodeX -ne $nodeY) { Write-Warning "The terrain is $nodeX x $nodeY nodes; the server takes one size for both sides, so $nodeX x $nodeSize is used" }
$min = $borders['border_min']; $max = $borders['border_max']

# ---- naval map points ----
$points = [Collections.Generic.List[string]]::new()
$points.Add('# Realm of Thrones Main_map naval points for the Coop dedicated server (Stage-RotServerMap.ps1), global positions')
$spawns = @($entities.SelectNodes(".//game_entity[scripts/script[@name='PirateSpawnPoint']]"))
foreach ($entity in $spawns) {
    $script = $entity.SelectSingleNode("scripts/script[@name='PirateSpawnPoint']")
    $clan = $script.SelectSingleNode("variables/variable[@name='ClanStringId']")
    if ($null -eq $clan -or -not $clan.GetAttribute('value')) { Write-Warning "Pirate spawn point '$($entity.GetAttribute('name'))' has no clan; skipped"; continue }
    $radius = $script.SelectSingleNode("variables/variable[@name='Radius']")
    # PirateSpawnPoint.Radius is 10 unless the scene sets it.
    $radiusValue = if ($null -ne $radius) { [double]::Parse($radius.GetAttribute('value'), $inv) } else { 10 }
    $o = Get-GlobalOrigin $entity
    $points.Add("pirate_spawn $($clan.GetAttribute('value')) $(Format-Number $o[0]) $(Format-Number $o[1]) $(Format-Number $radiusValue)")
}
# Scene order, since the server applies drop offs in order and a village's last one wins.
$dropOffs = @($entities.SelectNodes(".//game_entity[tags/tag[@name='main_map_village_dropoff']]"))
$villages = @()
foreach ($entity in $dropOffs) {
    $village = Get-ParentEntity $entity
    if ($null -eq $village) { Write-Warning "Village drop off '$($entity.GetAttribute('name'))' has no parent village; skipped"; continue }
    $o = Get-GlobalOrigin $entity
    $villages += $village.GetAttribute('name')
    $points.Add("village_dropoff $($village.GetAttribute('name')) $(Format-Number $o[0]) $(Format-Number $o[1])")
}

# Drop offs whose village Realm of Thrones does not have are skipped by the server with a line.
$settlementIds = @{}
$settlementsXml = Join-Path $RotMap 'ModuleData\settlements.xml'
if (Test-Path -LiteralPath $settlementsXml) {
    $settlements = New-Object Xml.XmlDocument
    $settlements.Load($settlementsXml)
    foreach ($node in $settlements.SelectNodes('//Settlement')) { $settlementIds[$node.GetAttribute('id')] = $true }
}
$unknownVillages = @($villages | Where-Object { -not $settlementIds.ContainsKey($_) })

$version = '?'
$subModule = Join-Path $RotMap 'SubModule.xml'
if (Test-Path -LiteralPath $subModule) {
    $module = New-Object Xml.XmlDocument
    $module.Load($subModule)
    $versionNode = $module.SelectSingleNode('Module/Version')
    if ($null -ne $versionNode) { $version = $versionNode.GetAttribute('value') }
}

# The compat module reads these as Singles (MapData.Parse) and refuses the same numbers, so they
# are judged here as the Singles it will read, and written so they read back as exactly those.
# As Doubles, 1599.99999 would pass against a terrain of 1600 here and then round to 1600 there.
$single = @{
    MinX = [single]$min[0]; MinY = [single]$min[1]; MaxX = [single]$max[0]; MaxY = [single]$max[1]
    Height = [single]$max[2]; Terrain = [single]$terrainSize
}
# 'G7' gives the short text (1400.3) but not every Single fits in 7 digits (1599.9999 would come
# out as 1600), so it is kept only when it reads back as the same Single; 'G9' always does.
function Format-Single([single]$Value) {
    $text = $Value.ToString('G7', $inv)
    if ([single]::Parse($text, $inv) -ne $Value) { $text = $Value.ToString('G9', $inv) }
    $text
}
foreach ($value in $single.Values) {
    if ([single]::IsNaN($value) -or [single]::IsInfinity($value)) { throw "A border or terrain number is not a finite Single" }
}
if ($single.MinX -ge $single.MaxX -or $single.MinY -ge $single.MaxY) { throw "border_min is not below border_max" }
if ($single.MinX -lt 0 -or $single.MinY -lt 0) { throw "border_min is below zero" }
if ($single.Terrain -le 0 -or $single.MaxX -ge $single.Terrain -or $single.MaxY -ge $single.Terrain) {
    throw "The terrain size $(Format-Single $single.Terrain) does not cover the border (max $(Format-Single $single.MaxX),$(Format-Single $single.MaxY))"
}

$data = @(
    "# Realm of Thrones' campaign map for the Coop dedicated server, written by Stage-RotServerMap.ps1",
    "# from ROT_Map $version SceneObj\Main_map\scene.xscene. Border = border_min and border_max entities,",
    "# terrain size = node_dimension_x x node_size ($nodeX x $(Format-Number $nodeSize)).",
    "module=ROT_Map",
    "border_min_x=$(Format-Single $single.MinX)",
    "border_min_y=$(Format-Single $single.MinY)",
    "border_max_x=$(Format-Single $single.MaxX)",
    "border_max_y=$(Format-Single $single.MaxY)",
    "border_max_height=$(Format-Single $single.Height)",
    "terrain_size=$(Format-Single $single.Terrain)"
)

# ---- writing ----
# Both output folders were empty or missing, so a failure here empties them again and the next
# run can use the same folder.
$windNote = 'copied from Realm of Thrones'
$dataFile = Join-Path $dataFolder 'rot_map.txt'
try {
    New-Item -ItemType Directory -Force $mapOut, $dataFolder | Out-Null

    # The map folder's files, minus the scene (written stripped below) and the folders the server
    # never reads: ShaderCache is per PC render data and TileSets is terrain render data.
    $skipFolders = @('ShaderCache', 'TileSets')
    foreach ($item in Get-ChildItem -LiteralPath $mapSource -Force) {
        if ($item.PSIsContainer -and $skipFolders -contains $item.Name) { continue }
        if (-not $item.PSIsContainer -and $item.Name -ieq 'scene.xscene') { continue }
        Copy-Item -LiteralPath $item.FullName -Destination (Join-Path $mapOut $item.Name) -Recurse -Force
    }

    [IO.File]::WriteAllLines((Join-Path $mapOut 'naval_map_points.txt'), $points)

    $entities.RemoveAll()
    [void]$sceneNode.RemoveChild($terrain)
    $scene.Save((Join-Path $mapOut 'scene.xscene'))

    # The server's naval installer reads flowmap.dds (G16R16F) and stops the load without it. A
    # map without one gets a calm map: every texel zero, so no wind anywhere on the server.
    $windFile = Join-Path $mapOut 'flowmap.dds'
    if (-not (Test-Path -LiteralPath $windFile)) {
        $side = 64
        $bytes = New-Object byte[] (128 + $side * $side * 4)
        $writer = New-Object IO.BinaryWriter (New-Object IO.MemoryStream (, $bytes))
        $writer.Write([uint32]0x20534444)      # "DDS "
        $writer.Write([uint32]124)             # header size
        $writer.Write([uint32]0x1007)          # caps, height, width and pixel format are set
        $writer.Write([uint32]$side)           # height
        $writer.Write([uint32]$side)           # width
        $writer.Write([uint32]($side * 4))     # bytes per row
        $writer.Seek(76, 'Begin') | Out-Null
        $writer.Write([uint32]32)              # pixel format size
        $writer.Write([uint32]0x4)             # the format is a FourCC
        $writer.Write([uint32]112)             # D3DFMT_G16R16F
        $writer.Seek(108, 'Begin') | Out-Null
        $writer.Write([uint32]0x1000)          # a plain texture
        $writer.Dispose()
        [IO.File]::WriteAllBytes($windFile, $bytes)
        $windNote = "Realm of Thrones has none; wrote a calm $side x $side map (no wind on the server)"
    }

    [IO.File]::WriteAllLines($dataFile, $data)
}
catch {
    foreach ($folder in $mapOut, $dataFolder) {
        if (Test-Path -LiteralPath $folder) { Get-ChildItem -LiteralPath $folder -Force | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue }
    }
    throw
}
Write-Host ''
Write-Host "Border $(Format-Number $min[0]),$(Format-Number $min[1]) to $(Format-Number $max[0]),$(Format-Number $max[1]), max height $(Format-Number $max[2])"
Write-Host "Terrain $nodeX x $nodeY nodes of $(Format-Number $nodeSize) = $(Format-Number $terrainSize)"
Write-Host "Naval points: $($spawns.Count) pirate spawn points, $($villages.Count) village drop offs ($($unknownVillages.Count) for villages not in settlements.xml$(if ($unknownVillages.Count) { ': ' + ($unknownVillages -join ', ') }))"
Write-Host "Wind map: $windNote"
Write-Host ''
foreach ($file in @(Get-ChildItem -LiteralPath $mapOut -Recurse -File) + @(Get-Item -LiteralPath $dataFile)) {
    Write-Host ("{0,12:N0}  {1}" -f $file.Length, $file.FullName.Substring($OutDir.Length + 1))
}
