param(
    [ValidateSet('Build','Portable')]
    [string]$Task = 'Build'
)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.build-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$env:DOTNET_NOLOGO = '1'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.packages'
$project = 'src/DDLDesk.csproj'
if ($Task -eq 'Portable') {
    dotnet restore $project -r win-x64 -p:SelfContained=true --source https://api.nuget.org/v3/index.json --verbosity minimal
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist/win-x64 --verbosity minimal
    exit $LASTEXITCODE
}
dotnet restore $project --configfile .config/NuGet.Config --verbosity minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet build $project -c Release --no-restore --verbosity minimal
exit $LASTEXITCODE
