<#
.SYNOPSIS
    Verifica la integridad de la internacionalizacion (i18n) de Cazador YTM.
.DESCRIPTION
    1. Comprueba que lang/es.json y lang/en.json tengan exactamente el mismo conjunto de claves.
    2. Comprueba que cada clave referenciada como {loc:Loc Clave} en XAML o
       LocalizationService.Instance["Clave"] en C# exista en los JSON.
    Devuelve exit code 1 si encuentra problemas (apto para CI).
#>
[CmdletBinding()]
param(
    [string]$RepoRoot
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $RepoRoot = (Resolve-Path (Join-Path $scriptDir "..")).Path
}

$esPath = Join-Path $RepoRoot "lang\es.json"
$enPath = Join-Path $RepoRoot "lang\en.json"

$esKeys = @((Get-Content -Raw -Encoding UTF8 $esPath | ConvertFrom-Json).PSObject.Properties.Name | Sort-Object)
$enKeys = @((Get-Content -Raw -Encoding UTF8 $enPath | ConvertFrom-Json).PSObject.Properties.Name | Sort-Object)

$failed = $false

$diff = Compare-Object $esKeys $enKeys
if ($diff) {
    $failed = $true
    Write-Host "[FALLO] Discrepancia de claves entre es.json y en.json:" -ForegroundColor Red
    $diff | ForEach-Object { Write-Host ("  {0} {1}" -f $_.SideIndicator, $_.InputObject) -ForegroundColor Red }
} else {
    Write-Host "[OK] Paridad de claves es/en: $($esKeys.Count) claves." -ForegroundColor Green
}

$known = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
foreach ($k in $esKeys) { [void]$known.Add($k) }

$patterns = @(
    'loc:Loc\s+([A-Za-z0-9_]+)',
    'LocalizationService\.Instance\[\s*"([A-Za-z0-9_]+)"'
)

$srcDir = Join-Path $RepoRoot "src"
$sourceFiles = Get-ChildItem -Path $srcDir -Recurse -File -Include *.xaml, *.cs
$missing = @{}

foreach ($file in $sourceFiles) {
    $text = Get-Content -Raw -Encoding UTF8 $file.FullName
    foreach ($pattern in $patterns) {
        foreach ($m in [regex]::Matches($text, $pattern)) {
            $key = $m.Groups[1].Value
            if (-not $known.Contains($key)) {
                $rel = $file.FullName.Substring($RepoRoot.Length).TrimStart('\')
                if (-not $missing.ContainsKey($key)) { $missing[$key] = New-Object System.Collections.Generic.List[string] }
                if (-not $missing[$key].Contains($rel)) { [void]$missing[$key].Add($rel) }
            }
        }
    }
}

if ($missing.Count -gt 0) {
    $failed = $true
    Write-Host "[FALLO] Claves referenciadas en el codigo que no existen en los JSON:" -ForegroundColor Red
    foreach ($k in ($missing.Keys | Sort-Object)) {
        Write-Host "  $k  <- $($missing[$k] -join ', ')" -ForegroundColor Red
    }
} else {
    Write-Host "[OK] Todas las claves referenciadas en XAML/C# existen en los JSON." -ForegroundColor Green
}

if ($failed) { exit 1 }
exit 0
