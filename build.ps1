$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The Windows .NET Framework 4.x compiler was not found.'
}

$output = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$exe = Join-Path $output 'MultiRdp.exe'
$icon = Join-Path $PSScriptRoot 'assets\MultiRdp.ico'
$github = Join-Path $PSScriptRoot 'assets\GitHub.png'
$telegram = Join-Path $PSScriptRoot 'assets\Telegram.png'
$manifest = Join-Path $PSScriptRoot 'assets\app.manifest'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' |
    ForEach-Object { $_.FullName })
$options = @(
    '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/codepage:65001', '/warnaserror+',
    '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll',
    '/reference:System.Security.dll', '/reference:System.Xml.dll', '/reference:Microsoft.CSharp.dll',
    "/win32manifest:$manifest", "/win32icon:$icon", "/resource:$icon,MultiRdp.ico",
    "/resource:$github,GitHub.png", "/resource:$telegram,Telegram.png", "/out:$exe"
)

& $compiler @options @sources
if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE."
}
Write-Output $exe
