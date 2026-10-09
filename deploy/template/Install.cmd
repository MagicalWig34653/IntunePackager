@echo off
rem Entry point of the package. Intune runs "Install.cmd" (install) or "Install.cmd -DeploymentType Uninstall".
rem It starts the wrapper in the native 64-bit Windows PowerShell, also when Intune started this file as a 32-bit process.
setlocal
set "WRAPPER=%~dp0Deploy-Wrapper.ps1"
set "POWERSHELL=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if exist "%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe" set "POWERSHELL=%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe"
"%POWERSHELL%" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%WRAPPER%" %*
exit /b %ERRORLEVEL%
