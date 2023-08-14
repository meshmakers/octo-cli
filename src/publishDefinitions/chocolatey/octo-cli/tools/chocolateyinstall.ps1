$ErrorActionPreference = 'Stop'
$toolsDir   = "$(Split-Path -parent $MyInvocation.MyCommand.Definition)"
$url64      = 'https://meshdownload.blob.core.windows.net/staging/octo-cli/octo-cli-__VERSION__-win-x64.zip'

Install-ChocolateyZipPackage -PackageName $packageName -UnzipLocation $toolsDir -Url64 $url64 -checksum64 $checksum64 -checksumType64 $checksumType64















