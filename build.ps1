# Kor.exe'yi Windows ile gelen .NET Framework derleyicisiyle derler.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$fw = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'

# Clevo'nun isik arayuzu DLL'i programin yaninda olmali.
if (-not (Test-Path "$here\InsydeDCHU.dll")) {
    $src = Get-AppxPackage | Where-Object Name -match 'CLEVOCO' | ForEach-Object {
        Get-ChildItem $_.InstallLocation -Recurse -Filter InsydeDCHU.dll -ErrorAction SilentlyContinue
    } | Select-Object -First 1
    if (-not $src) { throw 'InsydeDCHU.dll bulunamadi (Clevo Control Center kurulu degil).' }
    Copy-Item $src.FullName $here
}

function Compile([string[]]$extra) {
    & "$fw\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
        "/out:$here\Kor.exe" "/resource:$here\Arayuz.xaml,Arayuz.xaml" `
        "/r:$fw\WPF\PresentationFramework.dll" "/r:$fw\WPF\PresentationCore.dll" "/r:$fw\WPF\WindowsBase.dll" `
        /r:System.Xaml.dll /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll `
        @extra "$here\Kor.cs"
    if ($LASTEXITCODE -ne 0) { throw "Derleme basarisiz (kod $LASTEXITCODE)" }
}

# Simge programin kendi cizim kodundan uretilir: once simgesiz derle, simgeyi yazdir, sonra simgeyle derle.
Compile @()
Start-Process "$here\Kor.exe" -ArgumentList '--make-icon', "`"$here\Kor.ico`"" -Wait
if (-not (Test-Path "$here\Kor.ico")) { throw 'Kor.ico uretilemedi' }
Compile @("/win32icon:$here\Kor.ico")
"Tamam: $here\Kor.exe"
