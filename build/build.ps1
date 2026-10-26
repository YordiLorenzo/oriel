# build.ps1: build dist\oriel.exe with the C# compiler that ships with the .NET Framework (every Windows 10/11 has
# it), so no SDK is needed. The UI Automation interop types are generated here from UIAutomationCore.dll's own type
# library (what tlbimp does) and embedded into the exe (/link), so oriel.exe is the only file to ship.
#   powershell -ExecutionPolicy Bypass -File build\build.ps1 [-Version 0.2.0]
# -Version is the only place the version lives (the release workflow passes the tag): it becomes what `oriel version`
# prints and the exe's file version. If build\oriel.ico exists, it becomes the exe's icon.
param([string]$Version = '0.1.0')
$ErrorActionPreference = 'Stop'
# A Windows file version is four numbers, so a prerelease suffix (0.2.0-rc.1) shows only in the product version.
if ($Version -notmatch '^(\d+)\.(\d+)\.(\d+)(-[0-9A-Za-z.-]+)?$') { throw "-Version takes 1.2.3 or 1.2.3-rc.1, not '$Version'" }
$fileVersion = "$($Matches[1]).$($Matches[2]).$($Matches[3]).0"
$root = Split-Path -Parent $PSScriptRoot
$obj = Join-Path $root 'build\obj'
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $obj, $dist | Out-Null
$fx = Join-Path $env:windir 'Microsoft.NET\Framework64\v4.0.30319'
$interop = Join-Path $obj 'Interop.UIAutomationClient.dll'
if (-not (Test-Path $interop)) {
    Add-Type -TypeDefinition @'
using System; using System.Reflection; using System.Runtime.InteropServices;
public static class OrielTypeLib {
    class Sink : ITypeLibImporterNotifySink {
        public void ReportEvent(ImporterEventKind kind, int code, string message) { }
        public Assembly ResolveRef(object typeLib) { return null; }
    }
    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void LoadTypeLibEx(string file, int regKind, [MarshalAs(UnmanagedType.Interface)] out object typeLib);
    public static void Convert(string dll, string output) {
        object lib;
        LoadTypeLibEx(dll, 2, out lib);  // REGKIND_NONE
        var asm = new TypeLibConverter().ConvertTypeLibToAssembly(lib, output, TypeLibImporterFlags.None, new Sink(), null, null,
            "Interop.UIAutomationClient", null);
        asm.Save(System.IO.Path.GetFileName(output));
    }
}
'@
    Push-Location $obj
    try { [OrielTypeLib]::Convert("$env:windir\System32\UIAutomationCore.dll", $interop) } finally { Pop-Location }
}
# A daemon started from dist\oriel.exe holds the file open: stop that one (an installed copy elsewhere is left alone).
$target = Join-Path $dist 'oriel.exe'
Get-Process oriel -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $target } | Stop-Process -Force
Start-Sleep -Milliseconds 300
# csc turns these attributes into the exe's version resource (FileVersion, ProductVersion, FileDescription).
$info = Join-Path $obj 'BuildInfo.cs'
Set-Content -Encoding UTF8 $info @"
// Written by build\build.ps1 -Version at every build.
[assembly: System.Reflection.AssemblyTitle("oriel")]
[assembly: System.Reflection.AssemblyProduct("oriel")]
[assembly: System.Reflection.AssemblyVersion("$fileVersion")]
[assembly: System.Reflection.AssemblyFileVersion("$fileVersion")]
[assembly: System.Reflection.AssemblyInformationalVersion(Oriel.BuildInfo.Version)]
namespace Oriel { static class BuildInfo { public const string Version = "$Version"; } }
"@
$sources = @(Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object FullName) + $info
$refs = 'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'Microsoft.CSharp.dll', 'System.Web.Extensions.dll', 'System.Windows.Forms.dll' | ForEach-Object { "/r:$(Join-Path $fx $_)" }
$icon = Join-Path $root 'build\oriel.ico'
$iconArg = @(if (Test-Path $icon) { "/win32icon:$icon" })
& (Join-Path $fx 'csc.exe') /nologo /target:exe /optimize+ /warn:4 "/out:$(Join-Path $dist 'oriel.exe')" "/link:$interop" @iconArg @refs @sources
if ($LASTEXITCODE -ne 0) { throw "csc failed ($LASTEXITCODE)" }
$exe = Get-Item (Join-Path $dist 'oriel.exe')
"built $($exe.FullName) $Version ($([math]::Round($exe.Length / 1KB)) KB)"
