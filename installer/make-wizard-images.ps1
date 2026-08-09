<#
    Fabrique les bitmaps de l'assistant d'installation à partir des illustrations de
    l'application. À relancer uniquement si Z-Codex-Book.png ou Z-Codex.ico changent :
    le reste du temps les .bmp produits dans assets\ suffisent à compiler l'installateur.

    Inno Setup n'accepte que des BMP, et choisit tout seul la variante la mieux
    dimensionnée pour la mise à l'échelle de l'écran — d'où les cinq tailles de chaque
    image (100 %, 125 %, 150 %, 175 %, 200 %). Sans elles, l'assistant étire la seule
    image fournie et le rendu bave sur un écran haute densité.

        powershell -ExecutionPolicy Bypass -File installer\make-wizard-images.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root    = Split-Path -Parent $PSScriptRoot
$srcBook = Join-Path $root 'src\ZCodex.App\Assets\Z-Codex-Book.png'
$srcIcon = Join-Path $root 'src\ZCodex.App\Assets\Z-Codex.ico'
$outDir  = Join-Path $PSScriptRoot 'assets'

foreach ($f in @($srcBook, $srcIcon)) {
    if (-not (Test-Path $f)) { throw "Illustration source introuvable : $f" }
}
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

# Le fond des deux illustrations est noir : les bandes de remplissage le sont aussi, ce qui
# rend le recadrage invisible plutôt que de cerner le livre d'un liseré clair.
$background = [System.Drawing.Color]::FromArgb(0, 0, 0)

# Dessine $source centrée dans un canevas $w x $h, à l'échelle qui la fait tenir en entier
# (donc sans déformation), et écrit le résultat en BMP 24 bits — Inno ignore la couche alpha.
function Write-Bmp {
    param([System.Drawing.Image]$Source, [int]$W, [int]$H, [string]$Path)

    $canvas = New-Object System.Drawing.Bitmap($W, $H, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($canvas)
    try {
        $g.Clear($background)
        $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality

        $scale = [Math]::Min([double]$W / $Source.Width, [double]$H / $Source.Height)
        $dw = [int][Math]::Round($Source.Width  * $scale)
        $dh = [int][Math]::Round($Source.Height * $scale)
        $g.DrawImage($Source, [int](($W - $dw) / 2), [int](($H - $dh) / 2), $dw, $dh)
    }
    finally { $g.Dispose() }

    $canvas.Save($Path, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $canvas.Dispose()
    '  {0,-26} {1} x {2}' -f (Split-Path -Leaf $Path), $W, $H
}

# Extrait une frame d'un .ico en lisant le répertoire d'icônes, puis en décodant la charge
# utile telle quelle (PNG ou DIB). Voir le commentaire de la vignette plus bas pour la raison.
function Get-IconFrame {
    param([string]$Path, [int]$Size)

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $count = [BitConverter]::ToUInt16($bytes, 4)
    for ($k = 0; $k -lt $count; $k++) {
        $entry = 6 + $k * 16
        $w = $bytes[$entry]
        if ($w -eq 0) { $w = 256 }        # 0 encode 256 dans le format ICO
        if ($w -ne $Size) { continue }

        $len = [BitConverter]::ToUInt32($bytes, $entry + 8)
        $off = [BitConverter]::ToUInt32($bytes, $entry + 12)
        $payload = New-Object byte[] $len
        [Array]::Copy($bytes, $off, $payload, 0, $len)

        $isPng = $payload[0] -eq 0x89 -and $payload[1] -eq 0x50 -and
                 $payload[2] -eq 0x4E -and $payload[3] -eq 0x47
        if (-not $isPng) {
            # Frame DIB : la reconstruire en .ico d'une seule entrée est plus sûr que de
            # rebâtir un en-tête BMP à la main (masque de transparence, hauteur doublée…).
            $ms = New-Object System.IO.MemoryStream
            $bw = New-Object System.IO.BinaryWriter($ms)
            $bw.Write([byte[]]@(0,0,1,0,1,0)); $bw.Write($bytes[$entry..($entry+7)])
            $bw.Write([uint32]$len); $bw.Write([uint32]22); $bw.Write($payload); $bw.Flush()
            $ms.Position = 0
            $ic = New-Object System.Drawing.Icon($ms)
            $out = $ic.ToBitmap(); $ic.Dispose(); $ms.Dispose()
            return $out
        }

        $ms = New-Object System.IO.MemoryStream(,$payload)
        return [System.Drawing.Image]::FromStream($ms)
    }
    throw "Frame ${Size}px absente de $Path"
}

# --- Bandeau vertical des pages Bienvenue et Terminé -------------------------------------
# 164 x 314 est la taille de référence d'Inno ; les autres en sont les multiples d'échelle.
$book = [System.Drawing.Image]::FromFile($srcBook)
try {
    'Bandeau (Z-Codex-Book.png) :'
    Write-Bmp $book 164 314 (Join-Path $outDir 'WizardImage.bmp')
    Write-Bmp $book 205 393 (Join-Path $outDir 'WizardImage-125.bmp')
    Write-Bmp $book 246 471 (Join-Path $outDir 'WizardImage-150.bmp')
    Write-Bmp $book 287 550 (Join-Path $outDir 'WizardImage-175.bmp')
    Write-Bmp $book 328 628 (Join-Path $outDir 'WizardImage-200.bmp')
}
finally { $book.Dispose() }

# --- Vignette d'en-tête des pages intermédiaires ------------------------------------------
# On prend la frame 256 du .ico : c'est l'une des trois qui gardent le cadre complet du
# livre. Les frames 48 et en dessous sont recadrées serré sur le blason et donneraient une
# vignette sans rapport visuel avec le bandeau.
#
# On lit la frame directement dans le fichier au lieu de passer par Icon.ToBitmap() : les
# frames 256 et 128 de ce .ico sont compressées en PNG, et ToBitmap() les décode de travers
# (image de bruit coloré). Les frames 64 et en dessous, elles, sont des DIB classiques.
$iconBmp = Get-IconFrame -Path $srcIcon -Size 256
try {
    'Vignette (Z-Codex.ico, frame 256) :'
    Write-Bmp $iconBmp  55  55 (Join-Path $outDir 'WizardSmallImage.bmp')
    Write-Bmp $iconBmp  69  69 (Join-Path $outDir 'WizardSmallImage-125.bmp')
    Write-Bmp $iconBmp  83  83 (Join-Path $outDir 'WizardSmallImage-150.bmp')
    Write-Bmp $iconBmp  96  96 (Join-Path $outDir 'WizardSmallImage-175.bmp')
    Write-Bmp $iconBmp 110 110 (Join-Path $outDir 'WizardSmallImage-200.bmp')
}
finally { $iconBmp.Dispose() }

"Terminé : $outDir"
