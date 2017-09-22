$ErrorActionPreference = "Stop"

$ARTIFACTS = Get-ChildItem $([io.path]::combine("artifacts", '*')) -Include "*.zip", "*.tar", "*.tar.bz2"

. '.\scripts\version.ps1'
. '.\scripts\checkLastExitCode.ps1'
. '.\scripts\upload.ps1'
. '.\scripts\getScriptDirectory.ps1'

$projectDir = Get-ScriptDirectory
$uploader = [io.path]::combine($projectDir, '..', 'Uploader', 'S3Uploader.exe')
$versionInfo = GetVersionInfo
$files = Get-ChildItem $ARTIFACTS

Upload "$uploader" $versionInfo $files
