# build.ps1 - Automated dependency downloader and compiler for PDA browser

$ErrorActionPreference = "Stop"

# Set TLS 1.2 for secure downloads
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$workingDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
if ([string]::IsNullOrEmpty($workingDir)) { $workingDir = "." }
Set-Location $workingDir

Write-Host "--- Downloading WebView2 NuGet package ---"
$nugetUrl = "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/1.0.2592.51/microsoft.web.webview2.1.0.2592.51.nupkg"
$zipFile = "webview2.zip"
$extractDir = "webview2_temp"

if (Test-Path $zipFile) { Remove-Item $zipFile -Force }
if (Test-Path $extractDir) { Remove-Item $extractDir -Recurse -Force }

Write-Host "Downloading from $nugetUrl..."
Invoke-WebRequest -Uri $nugetUrl -OutFile $zipFile

Write-Host "Extracting package files..."
Expand-Archive -Path $zipFile -DestinationPath $extractDir -Force

Write-Host "Copying required library DLLs..."
Copy-Item "$extractDir\lib\net462\Microsoft.Web.WebView2.WinForms.dll" -Destination "." -Force
Copy-Item "$extractDir\lib\net462\Microsoft.Web.WebView2.Core.dll" -Destination "." -Force
Copy-Item "$extractDir\build\native\x64\WebView2Loader.dll" -Destination "." -Force

Write-Host "--- Compiling PdaBrowser.cs ---"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $csc)) {
    Write-Error "Microsoft .NET Framework 4.8 Compiler (csc.exe) not found at expected path: $csc"
}

# Run C# Compiler
& $csc /target:winexe /out:PdaBrowser.exe /platform:x64 /r:Microsoft.Web.WebView2.Core.dll /r:Microsoft.Web.WebView2.WinForms.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.dll PdaBrowser.cs

if ($LASTEXITCODE -ne 0) {
    Write-Error "Compilation failed!"
} else {
    Write-Host "Compilation successful! Generated: PdaBrowser.exe"
}

# Clean up temp files
Write-Host "Cleaning up temporary installation files..."
Remove-Item $zipFile -Force
Remove-Item $extractDir -Recurse -Force

Write-Host "--- Done! ---"
