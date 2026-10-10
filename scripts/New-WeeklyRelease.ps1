<#
.SYNOPSIS
    Automatiza el ciclo de lanzamiento dominical para Cazador YTM.
.DESCRIPTION
    1. Verifica binarios locales (yt-dlp, deno, ffmpeg suite).
    2. Sincroniza e incrementa la version en Constants.cs y archivos .csproj.
    3. Asegura la plantilla de notas de version en docs/releases/v<Version>.md.
    4. Ejecuta la suite de pruebas unitarias en modo Release.
    5. Realiza el commit y crea el tag Git.
    6. Opcionalmente empuja a GitHub para disparar el CI/CD que empaqueta los motores llave en mano.
.PARAMETER TargetVersion
    Version explicita (ej. 1.2.1). Si se omite, incrementa automaticamente la version de parche.
.PARAMETER Push
    Sube automaticamente los cambios y tags a GitHub.
.PARAMETER DryRun
    Ejecuta validaciones y pruebas sin realizar cambios en Git ni archivos.
#>
[CmdletBinding()]
param(
    [string]$TargetVersion,
    [switch]$Push,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptDir "..")).Path

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "   Cazador YTM - Automatizacion de Release Semanal" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

# 1. Detectar version actual
$constantsFile = Join-Path $repoRoot "src\CazadorYTM.Core\Constants.cs"
$coreCsproj = Join-Path $repoRoot "src\CazadorYTM.Core\CazadorYTM.Core.csproj"
$guiCsproj = Join-Path $repoRoot "src\CazadorYTM.Gui\CazadorYTM.Gui.csproj"

$constantsContent = [System.IO.File]::ReadAllText($constantsFile, [System.Text.Encoding]::UTF8)
$regexVer = [regex]'public const string AppVersion = "([0-9]+\.[0-9]+(?:\.[0-9]+)?)";'
$matchVer = $regexVer.Match($constantsContent)
if ($matchVer.Success) {
    $currentVersion = $matchVer.Groups[1].Value
} else {
    throw "No se pudo detectar AppVersion en $constantsFile"
}

Write-Host "[1/6] Version actual detectada: v$currentVersion" -ForegroundColor Yellow

# 2. Calcular version de destino
if ([string]::IsNullOrWhiteSpace($TargetVersion)) {
    $parts = $currentVersion.Split('.')
    if ($parts.Length -ge 3) {
        $patchNum = [int]$parts[2] + 1
        $TargetVersion = "$($parts[0]).$($parts[1]).$patchNum"
    } else {
        $TargetVersion = "$currentVersion.1"
    }
}

Write-Host "[2/6] Preparando release para: v$TargetVersion" -ForegroundColor Green

# 3. Comprobar binarios locales
Write-Host "[3/6] Verificando componentes y motores locales..." -ForegroundColor Yellow
$binaries = @("yt-dlp.exe", "deno.exe", "ffmpeg.exe", "ffprobe.exe", "ffplay.exe")
foreach ($bin in $binaries) {
    $binPath = Join-Path $repoRoot $bin
    if (Test-Path $binPath) {
        $sizeMb = [math]::Round(((Get-Item $binPath).Length / 1048576), 1)
        Write-Host "  [OK] $bin presente ($sizeMb MB)" -ForegroundColor Gray
    } else {
        Write-Warning "  [FALTA] $bin no se encuentra en el directorio raiz."
    }
}

if ($DryRun) {
    Write-Host "`n[DryRun] Validando ejecucion de pruebas unitarias en Release..." -ForegroundColor Magenta
    Push-Location $repoRoot
    try {
        dotnet test CazadorYTM.slnx --configuration Release --nologo
        if ($LASTEXITCODE -ne 0) {
            throw "Las pruebas unitarias fallaron en modo DryRun."
        }
        Write-Host "  [OK] Pruebas unitarias pasaron satisfactoriamente." -ForegroundColor Green
    }
    finally {
        Pop-Location
    }
    Write-Host "[DryRun] Completado. No se modificaron archivos ni Git." -ForegroundColor Magenta
    exit 0
}

# 4. Actualizar version en codigo fuente y proyectos
Write-Host "[4/6] Sincronizando numeros de version a $TargetVersion..." -ForegroundColor Yellow

# Actualizar Constants.cs
$newConstants = $constantsContent -replace 'public const string AppVersion = "[0-9]+\.[0-9]+(?:\.[0-9]+)?";', "public const string AppVersion = `"$TargetVersion`";"
[System.IO.File]::WriteAllText($constantsFile, $newConstants, [System.Text.Encoding]::UTF8)

# Actualizar CazadorYTM.Core.csproj
$coreContent = [System.IO.File]::ReadAllText($coreCsproj, [System.Text.Encoding]::UTF8)
$newCoreContent = [System.Text.RegularExpressions.Regex]::Replace($coreContent, '<Version>[^<]+</Version>', "<Version>$TargetVersion</Version>")
[System.IO.File]::WriteAllText($coreCsproj, $newCoreContent, [System.Text.Encoding]::UTF8)

# Actualizar CazadorYTM.Gui.csproj
$guiContent = [System.IO.File]::ReadAllText($guiCsproj, [System.Text.Encoding]::UTF8)
$newGuiContent = [System.Text.RegularExpressions.Regex]::Replace($guiContent, '<Version>[^<]+</Version>', "<Version>$TargetVersion</Version>")
[System.IO.File]::WriteAllText($guiCsproj, $newGuiContent, [System.Text.Encoding]::UTF8)

# Asegurar notas de version en docs/releases/
$releasesDir = Join-Path $repoRoot "docs\releases"
if (-not (Test-Path $releasesDir)) {
    New-Item -ItemType Directory -Path $releasesDir -Force | Out-Null
}
$releaseNotesFile = Join-Path $releasesDir "v$TargetVersion.md"
if (-not (Test-Path $releaseNotesFile)) {
    $nl = [Environment]::NewLine
    $notes = "## Cazador YTM v$TargetVersion" + $nl + $nl
    $notes += "Lanzamiento semanal con actualizacion de motores y mejoras de estabilidad." + $nl + $nl
    $notes += "### Novedades y Componentes:" + $nl
    $notes += "- Motores actualizados: yt-dlp, Deno y suite completa de FFmpeg al dia." + $nl
    $notes += "- Mejoras continuas: Optimizaciones de rendimiento y estabilidad." + $nl
    [System.IO.File]::WriteAllText($releaseNotesFile, $notes, [System.Text.Encoding]::UTF8)
    Write-Host "  [OK] Creada plantilla de notas: docs/releases/v$TargetVersion.md" -ForegroundColor Gray
}

# 5. Ejecutar suite de pruebas unitarias
Write-Host "[5/6] Ejecutando pruebas unitarias en modo Release..." -ForegroundColor Yellow
Push-Location $repoRoot
try {
    dotnet test CazadorYTM.slnx --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Las pruebas unitarias fallaron. Abortando release para proteger la integridad del proyecto."
    }
    Write-Host "  [OK] Todas las pruebas pasaron satisfactoriamente." -ForegroundColor Green
}
finally {
    Pop-Location
}

# 6. Commit y creacion de tag en Git
Write-Host "[6/6] Creando commit y etiqueta Git v$TargetVersion..." -ForegroundColor Yellow
Push-Location $repoRoot
try {
    git add src/CazadorYTM.Core/Constants.cs src/CazadorYTM.Core/CazadorYTM.Core.csproj src/CazadorYTM.Gui/CazadorYTM.Gui.csproj docs/releases/
    git commit -m "chore: bump version to v$TargetVersion"
    git tag "v$TargetVersion"
    Write-Host "  [OK] Commit y Tag v$TargetVersion creados localmente." -ForegroundColor Green

    $doPush = $Push
    if (-not $doPush -and [Environment]::UserInteractive) {
        $response = Read-Host "`n¿Deseas subir el release a GitHub (origin main y tags) ahora mismo? (S/n)"
        if ([string]::IsNullOrWhiteSpace($response) -or $response -match '^[sSyY]') {
            $doPush = $true
        }
    }

    if ($doPush) {
        Write-Host "Subiendo a GitHub (origin main y tags)..." -ForegroundColor Cyan
        git push origin main --tags
        Write-Host "[EXITO] Push completado. GitHub Actions ha iniciado la compilacion del release." -ForegroundColor Green
    } else {
        Write-Host "`nPara publicar este release en GitHub posteriormente, ejecuta:" -ForegroundColor Cyan
        Write-Host "  git push origin main --tags" -ForegroundColor White
    }
}
finally {
    Pop-Location
}

Write-Host "`nProceso de release finalizado exitosamente." -ForegroundColor Green
