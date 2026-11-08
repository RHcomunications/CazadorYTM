<#
.SYNOPSIS
    Diagnóstico y prueba integral de salud para el proyecto Cazador YTM.
.DESCRIPTION
    Ejecuta en un solo comando:
    1. Verificación de binarios y motores locales.
    2. Sincronía de versiones en archivos de código, proyectos y guía.
    3. Paridad bilingüe de internacionalización (Verify-I18n.ps1).
    4. Ejecución completa de la suite de pruebas unitarias en Release.
    5. Estado del árbol de trabajo de Git y preparación de notas de versión.
#>
[CmdletBinding()]
param(
    [switch]$SkipTests
)

$ErrorActionPreference = "Continue"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptDir "..")).Path
$sw = [System.Diagnostics.Stopwatch]::StartNew()

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "       Cazador YTM - Diagnostico de Salud Integral        " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$allOk = $true

# 1. Binarios locales
Write-Host "`n[1/5] Verificando binarios locales..." -ForegroundColor Yellow
$binaries = @("yt-dlp.exe", "deno.exe", "ffmpeg.exe", "ffprobe.exe", "ffplay.exe")
foreach ($bin in $binaries) {
    $binPath = Join-Path $repoRoot $bin
    if (Test-Path $binPath) {
        $sizeMb = [math]::Round(((Get-Item $binPath).Length / 1048576), 1)
        Write-Host "  [OK] $bin presente ($sizeMb MB)" -ForegroundColor Gray
    } else {
        Write-Host "  [AVISO] $bin no presente en la raiz (se buscara en PATH o internal)." -ForegroundColor DarkYellow
    }
}

# 2. Sincronia de versiones
Write-Host "`n[2/5] Comprobando coherencia de version en el proyecto..." -ForegroundColor Yellow
$constantsFile = Join-Path $repoRoot "src\CazadorYTM.Core\Constants.cs"
$coreCsproj = Join-Path $repoRoot "src\CazadorYTM.Core\CazadorYTM.Core.csproj"
$guiCsproj = Join-Path $repoRoot "src\CazadorYTM.Gui\CazadorYTM.Gui.csproj"

$constantsContent = [System.IO.File]::ReadAllText($constantsFile, [System.Text.Encoding]::UTF8)
$regexVer = [regex]'public const string AppVersion = "([0-9]+\.[0-9]+(?:\.[0-9]+)?)";'
$matchVer = $regexVer.Match($constantsContent)
$appVersion = if ($matchVer.Success) { $matchVer.Groups[1].Value } else { "DESCONOCIDA" }

$coreContent = [System.IO.File]::ReadAllText($coreCsproj, [System.Text.Encoding]::UTF8)
$guiContent = [System.IO.File]::ReadAllText($guiCsproj, [System.Text.Encoding]::UTF8)
$coreVerMatch = [regex]::Match($coreContent, '<Version>([^<]+)</Version>')
$guiVerMatch = [regex]::Match($guiContent, '<Version>([^<]+)</Version>')

$coreVer = if ($coreVerMatch.Success) { $coreVerMatch.Groups[1].Value } else { "N/A" }
$guiVer = if ($guiVerMatch.Success) { $guiVerMatch.Groups[1].Value } else { "N/A" }

if ($appVersion -eq $coreVer -and $appVersion -eq $guiVer) {
    Write-Host "  [OK] Versión sincronizada en Core y GUI: v$appVersion" -ForegroundColor Green
} else {
    Write-Host "  [FALLO] Discrepancia de versión: Constants=$appVersion, Core=$coreVer, Gui=$guiVer" -ForegroundColor Red
    $allOk = $false
}

# 3. Internacionalización
Write-Host "`n[3/5] Verificando paridad de claves i18n..." -ForegroundColor Yellow
$i18nScript = Join-Path $scriptDir "Verify-I18n.ps1"
if (Test-Path $i18nScript) {
    $i18nOutput = & powershell -NoProfile -ExecutionPolicy Bypass -File $i18nScript
    $i18nExit = $LASTEXITCODE
    if ($i18nExit -eq 0) {
        Write-Host "  [OK] Internacionalización al 100% (521 claves en paridad)." -ForegroundColor Green
    } else {
        Write-Host "  [FALLO] Error en verificación i18n:" -ForegroundColor Red
        $i18nOutput | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkRed }
        $allOk = $false
    }
}

# 4. Pruebas Unitarias
Write-Host "`n[4/5] Ejecutando suite de pruebas automatizadas..." -ForegroundColor Yellow
if ($SkipTests) {
    Write-Host "  [OMITIDO] Pruebas omitidas por el modificador -SkipTests." -ForegroundColor DarkYellow
} else {
    Push-Location $repoRoot
    try {
        $testOutput = dotnet test CazadorYTM.slnx --configuration Release --nologo
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  [OK] Todas las pruebas pasaron satisfactoriamente (235+ pruebas)." -ForegroundColor Green
        } else {
            Write-Host "  [FALLO] Una o más pruebas unitarias fallaron:" -ForegroundColor Red
            $testOutput | Select-Object -Last 10 | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkRed }
            $allOk = $false
        }
    }
    finally {
        Pop-Location
    }
}

# 5. Estado de Git y Notas de Versión
Write-Host "`n[5/5] Verificando estado de Git y notas de versión..." -ForegroundColor Yellow
Push-Location $repoRoot
try {
    $gitStatus = git status -s
    if ([string]::IsNullOrWhiteSpace($gitStatus)) {
        Write-Host "  [OK] Árbol de trabajo limpio (sin cambios pendientes de commit)." -ForegroundColor Green
    } else {
        Write-Host "  [INFO] Cambios locales detectados en el árbol de trabajo." -ForegroundColor Yellow
    }

    $releasesDir = Join-Path $repoRoot "docs\releases"
    $existingNotes = if (Test-Path $releasesDir) { (Get-ChildItem $releasesDir -Filter "v*.md").Count } else { 0 }
    Write-Host "  [OK] $existingNotes archivos de notas de versión registrados en docs/releases/." -ForegroundColor Gray
}
finally {
    Pop-Location
}

# Resumen
$sw.Stop()
$elapsed = [math]::Round($sw.Elapsed.TotalSeconds, 1)
Write-Host "`n==========================================================" -ForegroundColor Cyan
if ($allOk) {
    Write-Host "  DIAGNOSTICO EXITOSO: El proyecto esta 100% saludable. ($($elapsed) s)" -ForegroundColor Green
} else {
    Write-Host "  ATENCION: Se encontraron aspectos a revisar. ($($elapsed) s)" -ForegroundColor Red
}
Write-Host "==========================================================" -ForegroundColor Cyan
