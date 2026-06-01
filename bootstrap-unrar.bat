@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "URL=https://www.rarlab.com/rar/unrarw64.exe"
set "BASE_DIR=%~dp0"
set "STORAGE_DIR=%KLEVADEPLOY_STORAGE_DIR%"
if "%STORAGE_DIR%"=="" set "STORAGE_DIR=%BASE_DIR%Data"
set "TOOLS_DIR=%STORAGE_DIR%\Tools"
set "SFX=%TOOLS_DIR%\unrarw64.exe"
set "UNRAR=%TOOLS_DIR%\UnRAR.exe"
set "ALIAS=%TOOLS_DIR%\unrar.exe"
set "SEVENZR=%TOOLS_DIR%\7zr.exe"

if not exist "%TOOLS_DIR%" mkdir "%TOOLS_DIR%" >nul 2>&1

echo [info] Target tools dir: %TOOLS_DIR%
echo [info] Downloading: %URL%

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference='Stop';" ^
  "$url=$env:URL; $dst=$env:SFX; $tmp=$dst+'.'+[guid]::NewGuid().ToString('N')+'.tmp';" ^
  "Write-Host ('[info] Download -> ' + $dst);" ^
  "$client = [System.Net.Http.HttpClient]::new();" ^
  "$client.Timeout = [TimeSpan]::FromMinutes(10);" ^
  "$req = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get, $url);" ^
  "$req.Headers.UserAgent.ParseAdd('KlevaDeploy/1.0');" ^
  "$resp = $client.SendAsync($req, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult();" ^
  "$resp.EnsureSuccessStatusCode() | Out-Null;" ^
  "$len = $resp.Content.Headers.ContentLength;" ^
  "$in = $resp.Content.ReadAsStreamAsync().GetAwaiter().GetResult();" ^
  "$out = [System.IO.File]::Open($tmp,[System.IO.FileMode]::Create,[System.IO.FileAccess]::Write,[System.IO.FileShare]::None);" ^
  "try {" ^
  "  $buf = New-Object byte[] (1024*128);" ^
  "  $readTotal = 0L;" ^
  "  while(($read = $in.Read($buf,0,$buf.Length)) -gt 0) {" ^
  "    $out.Write($buf,0,$read);" ^
  "    $readTotal += $read;" ^
  "    if($len) { $pct=[math]::Floor(($readTotal*100.0)/$len); Write-Progress -Activity 'Downloading UnRAR' -Status ($readTotal.ToString()+' / '+$len.ToString()+' bytes') -PercentComplete $pct }" ^
  "  }" ^
  "} finally { $out.Dispose(); $in.Dispose(); $resp.Dispose(); $client.Dispose() }" ^
  "if(Test-Path $dst) { Remove-Item -Force $dst }" ^
  "Move-Item -Force $tmp $dst;" ^
  "Write-Host ('[info] Download complete (' + (Get-Item $dst).Length + ' bytes)');"

if errorlevel 1 (
  echo [error] Download failed
  exit /b 1
)

if exist "%UNRAR%" (
  echo [info] UnRAR CLI already present: %UNRAR%
  goto :alias
)

if not exist "%SEVENZR%" (
  echo [info] 7zr.exe missing, downloading it...
  powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
    "$ErrorActionPreference='Stop';" ^
    "$dst=$env:SEVENZR; $url='https://www.7-zip.org/a/7zr.exe';" ^
    "Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $dst;" ^
    "Write-Host ('[info] 7zr.exe ready: ' + $dst);"
  if errorlevel 1 (
    echo [error] Failed to download 7zr.exe
    exit /b 1
  )
)

echo [info] Extracting UnRAR.exe from %SFX%
"%SEVENZR%" e -y -o"%TOOLS_DIR%" "%SFX%" "UnRAR.exe" "unrar.exe" >nul 2>&1

if not exist "%UNRAR%" (
  echo [error] Extraction did not produce UnRAR.exe in %TOOLS_DIR%
  exit /b 1
)

:alias
copy /y "%UNRAR%" "%ALIAS%" >nul 2>&1
echo [info] UnRAR CLI ready:
echo        %UNRAR%
echo        %ALIAS%

del /f /q "%SFX%" >nul 2>&1
exit /b 0
