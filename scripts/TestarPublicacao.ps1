param([Parameter(Mandatory=$true)][string]$PastaPublicada)
$ErrorActionPreference='Stop'
$published=(Resolve-Path -LiteralPath $PastaPublicada).Path
$tempRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$testDirectory=Join-Path $tempRoot ('wgs-published-smoke-'+[Guid]::NewGuid().ToString('N'))
$testProcess=$null
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class WgsPublishedWindow {
 private delegate bool Callback(IntPtr h,IntPtr p);
 [DllImport("user32.dll")] private static extern bool EnumWindows(Callback callback,IntPtr value);
 [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr h,StringBuilder text,int max);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h,uint message,IntPtr w,IntPtr l);
 public static IntPtr Find(int processId) {IntPtr found=IntPtr.Zero;EnumWindows((h,p)=>{GetWindowThreadProcessId(h,out uint pid);if(pid==processId){var text=new StringBuilder(256);GetWindowText(h,text,256);if(text.ToString()=="WGS DF-e Manager 0.17.5"){found=h;return false;}}return true;},IntPtr.Zero);return found;}
}
'@
try {
 New-Item -ItemType Directory -Path $testDirectory | Out-Null
 Copy-Item -LiteralPath (Join-Path $published 'WgsDfe.Desktop.exe') -Destination $testDirectory
 $settings=Get-Content -LiteralPath (Join-Path $published 'appsettings.json') -Raw | ConvertFrom-Json
 $settings.Application.DataDirectory=Join-Path $testDirectory 'data'
 $settings.Application.OrganizationId=[Guid]::NewGuid().ToString()
 $settings.Application.OrganizationName='TESTE DE PUBLICACAO'
 $settings.Providers.EnableExternalRequests=$false
 $settings | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $testDirectory 'appsettings.json') -Encoding utf8
 $timer=[Diagnostics.Stopwatch]::StartNew()
 $testProcess=Start-Process -FilePath (Join-Path $testDirectory 'WgsDfe.Desktop.exe') -WorkingDirectory $testDirectory -WindowStyle Hidden -PassThru
 $windowHandle=[IntPtr]::Zero
 for($attempt=0;$attempt -lt 200;$attempt++) {
  Start-Sleep -Milliseconds 100
  $testProcess.Refresh()
  if($testProcess.HasExited){throw 'A publicacao encerrou antes de abrir a janela.'}
  $windowHandle=[WgsPublishedWindow]::Find($testProcess.Id)
  if($windowHandle -ne [IntPtr]::Zero){break}
 }
 if($windowHandle -eq [IntPtr]::Zero){throw 'Janela principal nao identificada.'}
 if(!(Test-Path -LiteralPath (Join-Path $testDirectory 'data/wgs-dfe.db'))){throw 'Banco temporario nao criado.'}
 $timer.Stop()
 for($attempt=0;$attempt -lt 50;$attempt++) {
  [WgsPublishedWindow]::PostMessage($windowHandle,0x0010,[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null
  if($testProcess.WaitForExit(200)){break}
 }
 if(!$testProcess.HasExited){throw 'O aplicativo nao encerrou em dez segundos.'}
 if($testProcess.ExitCode -ne 0){throw ('Codigo de saida: '+$testProcess.ExitCode)}
 [pscustomobject]@{status='PASS';window='WGS DF-e Manager';startupMilliseconds=$timer.ElapsedMilliseconds;exitCode=$testProcess.ExitCode;data='temporary isolated directory';externalRequests=$false} | ConvertTo-Json
}
finally {
 if($testProcess -and !$testProcess.HasExited){Stop-Process -Id $testProcess.Id -Force;$testProcess.WaitForExit(10000) | Out-Null}
 $resolvedTest=[IO.Path]::GetFullPath($testDirectory)
 if(!$resolvedTest.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolvedTest) -notlike 'wgs-published-smoke-*'){throw 'Pasta temporaria fora do limite esperado.'}
 if(Test-Path -LiteralPath $resolvedTest){Remove-Item -LiteralPath $resolvedTest -Recurse -Force}
}
