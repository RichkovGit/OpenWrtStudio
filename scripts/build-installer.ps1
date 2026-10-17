[CmdletBinding()]
param(
    [string]$PublishDirectory = (Join-Path $PSScriptRoot '..\publish'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\installer_output'),
    [string]$CompilerPath
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$PublishDirectory = [IO.Path]::GetFullPath($PublishDirectory)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$applicationPath = Join-Path $PublishDirectory 'OpenWrtStudio.exe'
if (-not (Test-Path -LiteralPath $applicationPath -PathType Leaf)) {
    throw "Published application is missing: $applicationPath. Run dotnet publish first."
}

$project = [xml](Get-Content -LiteralPath (Join-Path $repositoryRoot 'OpenWrtStudio.csproj') -Raw)
$releaseVersion = [string]$project.Project.PropertyGroup.Version
if ($releaseVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw 'The project Version must have three numeric components.'
}
$binaryVersion = (Get-Item -LiteralPath $applicationPath).VersionInfo.FileVersion.Trim()
if ($binaryVersion -ne "$releaseVersion.0") {
    throw "Published binary version $binaryVersion does not match project version $releaseVersion."
}

if ([string]::IsNullOrWhiteSpace($CompilerPath)) {
    $availableCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($availableCommand) {
        $CompilerPath = $availableCommand.Source
    } else {
        $compilerCandidates = @(
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
        )
        $CompilerPath = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    }
}
if ([string]::IsNullOrWhiteSpace($CompilerPath) -or -not (Test-Path -LiteralPath $CompilerPath -PathType Leaf)) {
    throw 'Inno Setup 6 compiler was not found. Install Inno Setup separately or pass -CompilerPath with an existing ISCC.exe. This script does not install software.'
}

# Stage the same support files for local and CI builds; never alter the application binary.
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'Assets\app.ico') -Destination (Join-Path $PublishDirectory 'app.ico') -Force
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination (Join-Path $PublishDirectory 'LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'RELEASE_NOTES.md') -Destination (Join-Path $PublishDirectory 'RELEASE_NOTES.md') -Force
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$compilerArguments = @(
    '/Qp',
    "/DMyAppVersion=$releaseVersion",
    "/DPublishDir=$PublishDirectory",
    "/O$OutputDirectory",
    (Join-Path $repositoryRoot 'installer\setup.iss')
)
& $CompilerPath @compilerArguments
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed with exit code $LASTEXITCODE." }

$installerPath = Join-Path $OutputDirectory "OpenWrtStudio_Setup_v$releaseVersion.exe"
if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) { throw 'The compiler did not produce the expected installer.' }
$installerVersion = (Get-Item -LiteralPath $installerPath).VersionInfo.FileVersion.Trim()
if ($installerVersion -ne "$releaseVersion.0") { throw "Unexpected installer version: $installerVersion" }
[pscustomobject]@{
    Installer = $installerPath
    Version = $releaseVersion
    SHA256 = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
}
