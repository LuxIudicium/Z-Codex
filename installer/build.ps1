<#
    Fabrique l'installateur de Z-Codex, de la publication .NET au .exe final.

        powershell -ExecutionPolicy Bypass -File installer\build.ps1

    Étapes :
      1. dotnet publish (profil win-x64-selfcontained) -> src\ZCodex.App\bin\publish\win-x64
      2. make-wizard-images.ps1                        -> installer\assets\*.bmp
      3. ISCC Z-Codex.iss                              -> installer\output\Z-Codex-<version>-setup.exe

    -SkipPublish réutilise la publication existante (utile quand on n'itère que sur
    l'habillage de l'assistant : la publication pèse la plus grosse part du temps).
    -SkipImages fait de même pour les bitmaps.

    Prérequis : SDK .NET 8 et Inno Setup 6 (winget install JRSoftware.InnoSetup).
#>
[CmdletBinding()]
param(
    [switch]$SkipPublish,
    [switch]$SkipImages
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# Inno s'installe soit pour la machine, soit pour l'utilisateur (c'est ce que fait
# winget par défaut) : on regarde les deux plutôt que de supposer.
$isccCandidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
    'C:\Program Files\Inno Setup 6\ISCC.exe'
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw "ISCC.exe introuvable. Installez Inno Setup 6 : winget install JRSoftware.InnoSetup"
}

if (-not $SkipPublish) {
    Write-Host '=== 1/3  Publication .NET (autonome win-x64) ===' -ForegroundColor Cyan
    # L'application verrouille ses propres DLL quand elle tourne : on prévient ici plutôt
    # que de laisser MSBuild échouer sur un MSB3026 illisible.
    if (Get-Process -Name 'Z-Codex' -ErrorAction SilentlyContinue) {
        throw 'Z-Codex est en cours d''exécution : fermez l''application avant de publier.'
    }
    dotnet publish (Join-Path $root 'src\ZCodex.App') -c Release `
        -p:PublishProfile=win-x64-selfcontained --nologo
    if ($LASTEXITCODE -ne 0) { throw "Échec de dotnet publish (code $LASTEXITCODE)." }
}
else { Write-Host '=== 1/3  Publication ignoree (-SkipPublish) ===' -ForegroundColor DarkGray }

if (-not $SkipImages) {
    Write-Host '=== 2/3  Bitmaps de l''assistant ===' -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'make-wizard-images.ps1')
}
else { Write-Host '=== 2/3  Bitmaps ignores (-SkipImages) ===' -ForegroundColor DarkGray }

Write-Host '=== 3/3  Compilation de l''installateur ===' -ForegroundColor Cyan
$payload = Join-Path $root 'src\ZCodex.App\bin\publish\win-x64\Z-Codex.exe'
if (-not (Test-Path $payload)) {
    throw "Publication absente ($payload). Relancez sans -SkipPublish."
}

# Copie de la licence signée d'un BOM UTF-8. Inno lit un fichier de licence sans marque
# comme de l'ANSI : les « ² » de « paw*ned² » deviendraient « Â² » sur la page de licence.
# On génère plutôt que d'imposer un BOM au LICENSE de la racine, qui n'a pas à porter les
# contraintes d'un outil d'empaquetage.
$objDir = Join-Path $PSScriptRoot 'obj'
if (-not (Test-Path $objDir)) { New-Item -ItemType Directory -Path $objDir | Out-Null }
$licenseBytes = [System.IO.File]::ReadAllBytes((Join-Path $root 'LICENSE'))
if ($licenseBytes.Length -ge 3 -and $licenseBytes[0] -eq 0xEF -and
    $licenseBytes[1] -eq 0xBB -and $licenseBytes[2] -eq 0xBF) {
    [System.IO.File]::WriteAllBytes((Join-Path $objDir 'LICENSE.txt'), $licenseBytes)
}
else {
    [System.IO.File]::WriteAllBytes((Join-Path $objDir 'LICENSE.txt'),
        ([byte[]](0xEF, 0xBB, 0xBF) + $licenseBytes))
}

# Table rase : le nom du setup porte le numéro de version, donc une fabrication ne remplace
# PAS la précédente — elle s'ajoute à côté, à l'octet près de la même taille. Deux exécutables
# indiscernables au nom près, c'est un lien de téléchargement erroné qui attend son heure.
# output\ n'est qu'un répertoire de sortie (ignoré par git) : l'archive des versions livrées,
# ce sont les Releases GitHub, pas ce dossier.
$outDir = Join-Path $PSScriptRoot 'output'
if (Test-Path $outDir) {
    Get-ChildItem $outDir -Filter '*-setup.exe' | Remove-Item -Force
}

& $iscc (Join-Path $PSScriptRoot 'Z-Codex.iss')
if ($LASTEXITCODE -ne 0) { throw "Échec d'ISCC (code $LASTEXITCODE)." }

$setup = Get-ChildItem (Join-Path $PSScriptRoot 'output') -Filter '*-setup.exe' |
         Sort-Object LastWriteTime -Descending | Select-Object -First 1
$payloadSize = (Get-ChildItem (Split-Path $payload) -Recurse -File |
                Measure-Object -Property Length -Sum).Sum

Write-Host ''
Write-Host ('Installateur : {0}' -f $setup.FullName) -ForegroundColor Green
Write-Host ('Taille       : {0:N1} Mo  (charge non compressee : {1:N1} Mo, ratio {2:P0})' -f `
    ($setup.Length / 1MB), ($payloadSize / 1MB), ($setup.Length / $payloadSize))
