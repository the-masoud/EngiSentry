# EngiSentry M0.1

Minimal AutoCAD 2021 x64 plugin bootstrap. The plugin targets .NET Framework 4.8 and provides only the `ES_HELLO` and `ENGISENTRY` read-only commands.

## Build

From a Visual Studio Developer PowerShell or an ordinary PowerShell prompt:

```powershell
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\amd64\MSBuild.exe' .\EngiSentry.sln /t:Rebuild /p:Configuration=Release /p:Platform=x64
```

The output is `src\EngiSentry.AutoCAD2021\bin\x64\Release\EngiSentry.AutoCAD2021.dll`.

AutoCAD 2021 managed assemblies are referenced from the installed AutoCAD directory and are not copied into this repository.
