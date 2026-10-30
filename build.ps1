$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$appProject = Join-Path $projectRoot 'src\Mocse.App\Mocse.App.csproj'
$testProject = Join-Path $projectRoot 'tests\Mocse.Tests\Mocse.Tests.csproj'
$testOutput = Join-Path $projectRoot 'artifacts\tests'
$releaseOutput = Join-Path $projectRoot 'release'
dotnet run --project $testProject -c Release -- $testOutput
if ($LASTEXITCODE -ne 0) { throw '存档测试失败。' }
dotnet publish $appProject -c Release -o $releaseOutput --nologo
if ($LASTEXITCODE -ne 0) { throw '发布失败。' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $releaseOutput '使用说明.md') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination (Join-Path $releaseOutput 'LICENSE') -Force
Write-Host "完成：$releaseOutput\MOCSE.exe"
