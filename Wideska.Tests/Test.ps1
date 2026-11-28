[CmdletBinding()]
param(
    [ValidateSet('auto', 'quick', 'ui', 'tasks', 'full', 'stress')]
    [string]$Suite = 'auto',
    [string]$BaseRef = 'origin/develop',
    [switch]$Plan,
    [switch]$Previews
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $selectedSuite = $Suite
    if ($selectedSuite -eq 'auto') {
        $base = git merge-base HEAD $BaseRef
        if ($LASTEXITCODE -ne 0) { throw "Cannot resolve baseline $BaseRef" }
        $files = @(
            git diff --name-only "$base...HEAD"
            git diff --name-only
            git diff --cached --name-only
            git ls-files --others --exclude-standard
        ) | Sort-Object -Unique
        if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect changed files' }
        $code = @($files | Where-Object { $_ -notmatch '(?i)(\.md$|(^|/)LICENSE$|^\.gitignore$)' })
        $selectedSuite = 'none'
        if ($code.Count) {
            $selectedSuite = 'quick'
            if ($code -match '(?i)(Views/|Controls/|Resources/|MainWindow|Infrastructure/|ViewModels/)') { $selectedSuite = 'ui' }
            if ($code -match '(?i)(IdleReturn|Wideska.Tests/|\.csproj$|\.slnx$|\.props$|\.targets$|\.github/)' -or
                @($code | Where-Object { $_ -notmatch '(?i)(^Wideska/.*\.(cs|xaml|png|ico|svg)$)' }).Count) { $selectedSuite = 'full' }
        }
        if ($code.Count -gt 0 -and @($code | Where-Object { $_ -notmatch '(?i)^Wideska(?:\.Tests)?/(?:Models/|ViewModels/|Views/)?TaskBoard[^/]*\.(cs|xaml)$' }).Count -eq 0) { $selectedSuite = 'tasks' }
        Write-Host ('Changed files: ' + ($files -join ', '))
    }
    Write-Host "Selected suite: $selectedSuite"
    if ($Plan) { return }
    git diff --check
    if ($LASTEXITCODE -ne 0) { throw 'Whitespace validation failed' }
    if ($selectedSuite -eq 'none') { return }
    dotnet build Wideska.Tests/Wideska.Tests.csproj -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    $testArgs = @('--suite', $selectedSuite)
    if ($Previews) { $testArgs += '--previews' }
    & ./Wideska.Tests/bin/Release/net10.0-windows/Wideska.Tests.exe @testArgs
    if ($LASTEXITCODE -ne 0) { throw "Suite $selectedSuite failed" }
}
finally { Pop-Location }
