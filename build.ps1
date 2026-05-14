$ErrorActionPreference = "Stop"

$framework = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
$compiler = Join-Path $framework "csc.exe"
$outDir = Join-Path $PSScriptRoot "bin"
$outFile = Join-Path $outDir "SleepMaxxer.exe"

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "C# compiler not found at $compiler"
}

New-Item -ItemType Directory -Path $outDir -Force | Out-Null

& $compiler `
    /nologo `
    /target:winexe `
    /platform:anycpu `
    /optimize+ `
    /out:$outFile `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    "$PSScriptRoot\Program.cs"

if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

Write-Host "Built $outFile"
