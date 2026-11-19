# Compile FipMini.exe avec le compilateur C# livré avec Windows (aucun SDK requis).
param(
    [string]$Out,          # sortie alternative, utile si FipMini.exe est en cours d'exécution
    [switch]$NoDownload    # ne télécharge pas le logo FIP : icône neutre (icon.svg)
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Out) { $Out = "$here\FipMini.exe" }
$fw   = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$md   = "$env:WINDIR\System32\WinMetadata"
$ico  = Join-Path $here 'fip.ico'
$svg  = Join-Path $here 'fip.svg'

# Le logo FIP appartient à Radio France : il n'est pas distribué avec ce dépôt.
# Il est téléchargé ici, pour un usage personnel ; à défaut, l'icône neutre icon.svg est utilisée.
$logoUrl = 'https://www.radiofrance.fr/_app/immutable/assets/fip.DGUjwW_p.svg'
if ($NoDownload) { $svg = Join-Path $here 'icon.svg' }
elseif (-not (Test-Path $svg)) {
    try {
        $tmp = "$svg.tmp"
        Invoke-WebRequest $logoUrl -OutFile $tmp -UseBasicParsing -UserAgent 'Mozilla/5.0'
        if ((Get-Content $tmp -Raw) -notmatch '^\s*<svg') { throw 'réponse inattendue' }
        Move-Item $tmp $svg -Force
    } catch {
        Remove-Item "$svg.tmp" -ErrorAction SilentlyContinue
        Write-Warning "Logo FIP indisponible ($($_.Exception.Message)) : icône neutre utilisée."
        $svg = Join-Path $here 'icon.svg'
    }
}

# Icône : le SVG retenu, rendu par WPF en PNG 256/48/32/16 px, encapsulés dans un .ico
Add-Type -AssemblyName PresentationCore, WindowsBase
$xml = [xml](Get-Content $svg -Raw -Encoding UTF8)
$box = [double]$xml.svg.width
$pngs = foreach ($size in 256, 48, 32, 16) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform ($size / $box), ($size / $box)))
    foreach ($p in $xml.svg.path) {
        $brush = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.ColorConverter]::ConvertFromString($p.fill))
        $dc.DrawGeometry($brush, $null, [System.Windows.Media.Geometry]::Parse($p.d))
    }
    $dc.Close()
    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($visual)
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $ms = New-Object System.IO.MemoryStream
    $enc.Save($ms)
    , @($size, $ms.ToArray())
}
$bw = New-Object System.IO.BinaryWriter ([System.IO.File]::Create($ico))
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$pngs.Count)   # en-tête ICO
$offset = 6 + 16 * $pngs.Count
foreach ($e in $pngs) {
    $dim = [byte]($e[0] % 256)                                                # 0 = 256 px
    $bw.Write($dim); $bw.Write($dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$e[1].Length); $bw.Write([uint32]$offset)
    $offset += $e[1].Length
}
foreach ($e in $pngs) { $bw.Write([byte[]]$e[1]) }
$bw.Close()

& "$fw\csc.exe" /nologo /target:winexe /optimize+ /platform:x64 /codepage:65001 `
    "/out:$out" "/win32icon:$ico" "/resource:$svg,fip.svg" `
    "/r:$fw\WPF\PresentationCore.dll" "/r:$fw\WPF\PresentationFramework.dll" "/r:$fw\WPF\WindowsBase.dll" `
    "/r:$fw\System.Xaml.dll" "/r:$fw\System.Web.Extensions.dll" `
    "/r:$fw\System.Windows.Forms.dll" "/r:$fw\System.Drawing.dll" `
    "/r:$fw\System.Runtime.dll" "/r:$fw\System.Runtime.WindowsRuntime.dll" "/r:$fw\System.Runtime.InteropServices.WindowsRuntime.dll" `
    "/r:$md\Windows.Media.winmd" "/r:$md\Windows.Foundation.winmd" "/r:$md\Windows.Storage.winmd" `
    "$here\FipMini.cs"
if ($LASTEXITCODE -ne 0) { throw "Compilation échouée" }
Get-Item $out | Select-Object Name, Length
