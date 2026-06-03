param ($configuration = "Release")

# Mirrors the layout used by octo-sdk's createDocumentation.ps1 — output lands under
# <repo>/bin/<configuration>/documentation/, so handle-artifacts.yml can pick the whole
# tree up as apiDocumentationPaths and copy it straight into the artifact's docs/ root.

$modulePath = Split-Path -Parent $MyInvocation.MyCommand.Definition
$baseOutputPath = Join-Path $modulePath "../bin/$configuration/documentation"
$outputPath = Join-Path $baseOutputPath "technologyGuide/tools/octo-cli/command-reference"
$generatorProject = Join-Path $modulePath "../src/CommandReferenceGenerator/CommandReferenceGenerator.csproj"
$fixturesDir = Join-Path $modulePath "../src/ManagementTool/Commands"

if (Test-Path -Path $outputPath) {
    Write-Host "Remove existing command-reference at '$outputPath'"
    Remove-Item -Path $outputPath -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $outputPath | Out-Null

Write-Host "Generating command-reference from '$fixturesDir' to '$outputPath'"
dotnet run --project $generatorProject -c $configuration -- $fixturesDir $outputPath
if ($LASTEXITCODE -ne 0) {
    Write-Error "CommandReferenceGenerator exited with code $LASTEXITCODE"
    exit $LASTEXITCODE
}
