# What a dash keeps on the screen's RAM drive, file by file: the 160 px grid tiles of the static layer, the area tiles
# (the static picture under a box that comes and goes), the value bands and the pictures of shapes that come and go,
# each with its area and JPEG bytes. `fxdash check` gives only the total and `fxdash pictures` only the pictures; this
# shows where the rest goes (the Toyota GR010: caption pictures took the left column's tiles from ~2 to 6-9 KB each).
#
#   C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\dashes\ram_files.ps1 DASH.json
#
# 32-bit PowerShell: fxdash is x86. It loads fxdash.exe (build it first) and SimHub's DLLs, and calls the renderer
# through reflection. The drive counts 512 B more per file than the bytes shown (docs/screen-ram.md).
param([Parameter(Mandatory = $true)][string]$Dash)
$bin = Join-Path $PSScriptRoot '..\fxdash\bin\Release\net48'
$simhub = if ($env:SIMHUB_INSTALL_PATH) { $env:SIMHUB_INSTALL_PATH } else { 'C:\Program Files (x86)\SimHub' }
[AppDomain]::CurrentDomain.add_AssemblyResolve({
    param($s, $e)
    $n = ($e.Name -split ',')[0]
    foreach ($d in @($bin, $simhub)) { $p = Join-Path $d "$n.dll"; if (Test-Path $p) { return [Reflection.Assembly]::LoadFrom($p) } }
    return $null
})
$a = [Reflection.Assembly]::LoadFrom((Join-Path $bin 'fxdash.exe'))
$all = $a.GetTypes()
$bf = [Reflection.BindingFlags]'Static,Instance,Public,NonPublic'
$tools = $all | Where-Object Name -eq 'DashTools'
$rt = $all | Where-Object Name -eq 'DashRenderer'
$pt = $all | Where-Object Name -eq 'PreviewScreen'
$parse = $tools.GetMethods($bf) | Where-Object { $_.Name -eq 'Parse' -and $_.GetParameters().Count -eq 1 } | Select-Object -First 1
$d = $parse.Invoke($null, @([IO.File]::ReadAllText((Resolve-Path $Dash))))
$r = [Activator]::CreateInstance($rt, $bf, $null, @([Activator]::CreateInstance($pt, $true), $d, 0, 0), $null)
$t = $rt.GetMethod('EnableTiles').Invoke($r, @())
$rt.GetMethod('PictureList', $bf).Invoke($r, @()) | Out-Null   # (the pictures of shapes that come and go)
function Show($title, $tiles) {
    $sum = 0
    "${title}:"
    foreach ($x in $tiles) { $n = if ($x.Jpeg) { $x.Jpeg.Length } else { 0 }; $sum += $n; '  {0},{1} {2}x{3}  {4} B' -f $x.R.X, $x.R.Y, $x.R.Width, $x.R.Height, $n }
    '  = {0:0.0} KB' -f ($sum / 1024)
}
Show 'grid tiles (0 B: one colour, no file)' $t.GridTiles
Show 'area tiles' $t.AreaTiles
Show 'value bands' $t.Bands.Values
Show 'pictures' $t.Pictures.Values
Show 'picture variants' $t.Variants
'total {0:0.0} KB in {1} files ({2:0.0} KB as the drive counts it)' -f ($t.Bytes / 1024), $t.FileCount, (($t.Bytes + 512 * $t.FileCount) / 1024)
