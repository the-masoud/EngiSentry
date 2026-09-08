# EngiSentry

EngiSentry is a read-only AutoCAD 2021 x64 engineering drawing QA plugin targeting .NET Framework 4.8.

Current commands: `ENGISENTRY`, `ES_HELLO`, `ES_SCAN`, `ES_SEMANTIC`, `ES_INDEX`, `ES_QA`, `ES_PLOTQA`, `ES_PROFILEQA`, `ES_CROSSQA`, `ES_REVBASELINE`, `ES_REVISION`, and `ES_DISTANCESETTINGS`.

It provides semantic extraction, Plot Plan QA, native XLSX cross-document QA, revision comparison, project distance criteria, and standards authority/review infrastructure. Analysis is read-only: EngiSentry does not save or auto-fix DWGs.

## Build

From a Visual Studio Developer PowerShell or an ordinary PowerShell prompt:

```powershell
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\amd64\MSBuild.exe' .\EngiSentry.sln /t:Rebuild /p:Configuration=Release /p:Platform=x64
```

The output is `src\EngiSentry.AutoCAD2021\bin\x64\Release\EngiSentry.AutoCAD2021.dll`.

AutoCAD 2021 managed assemblies are referenced from the installed AutoCAD directory and are not copied into this repository.
