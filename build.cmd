@echo off
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "REF=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%CSC%" (
  set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
  set "REF=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
)
if not exist "%CSC%" (
  echo .NET Framework 4.x compiler not found.
  exit /b 1
)

if not exist dist mkdir dist

"%CSC%" -nologo -target:winexe -codepage:65001 -optimize+ ^
  -out:"%CD%\dist\MineCraftDownloadTool.exe" ^
  -win32icon:"%CD%\assets\app.ico" ^
  -win32manifest:"%CD%\src\app.manifest" ^
  -r:"%REF%\System.dll" ^
  -r:"%REF%\System.Core.dll" ^
  -r:"%REF%\System.IO.Compression.dll" ^
  -r:"%REF%\System.Net.Http.dll" ^
  -r:"%REF%\System.Web.Extensions.dll" ^
  -r:"%REF%\System.Xml.dll" ^
  -r:"%REF%\System.Xml.Linq.dll" ^
  -r:"%REF%\System.Windows.Forms.dll" ^
  -r:"%REF%\System.Drawing.dll" ^
  -r:"%REF%\System.Xaml.dll" ^
  -r:"%REF%\WPF\WindowsBase.dll" ^
  -r:"%REF%\WPF\PresentationCore.dll" ^
  "%CD%\src\Models.cs" ^
  "%CD%\src\JsonUtil.cs" ^
  "%CD%\src\HttpUtil.cs" ^
  "%CD%\src\PathUtil.cs" ^
  "%CD%\src\DownloadEngine.cs" ^
  "%CD%\src\VersionService.cs" ^
  "%CD%\src\LoaderCatalog.cs" ^
  "%CD%\src\JavaLocator.cs" ^
  "%CD%\src\MinecraftInstaller.cs" ^
  "%CD%\src\ContentModels.cs" ^
  "%CD%\src\AppSettings.cs" ^
  "%CD%\src\Theme.cs" ^
  "%CD%\src\ThemeColorSwatch.cs" ^
  "%CD%\src\ThemedButtons.cs" ^
  "%CD%\src\ThemedProgressBar.cs" ^
  "%CD%\src\ContentService.cs" ^
  "%CD%\src\ModFileIndex.cs" ^
  "%CD%\src\ImageCache.cs" ^
  "%CD%\src\TargetIcons.cs" ^
  "%CD%\src\TargetPickerForm.cs" ^
  "%CD%\src\ModDependencyForm.cs" ^
  "%CD%\src\DownloadTaskManager.cs" ^
  "%CD%\src\TaskPage.cs" ^
  "%CD%\src\SettingsForm.cs" ^
  "%CD%\src\ModpackInstaller.cs" ^
  "%CD%\src\ContentPage.cs" ^
  "%CD%\src\MainForm.cs" ^
  "%CD%\src\Program.cs" ^
  "%CD%\src\AssemblyInfo.cs"

if errorlevel 1 (
  echo Build failed.
  exit /b 1
)
echo Built: %CD%\dist\MineCraftDownloadTool.exe
endlocal
