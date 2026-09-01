# Unity Mono smoke fixture

This minimal Unity 2022.3 project builds a Windows x64 player with the Mono
scripting backend. It exists only to exercise the complete Insider startup path
inside a real Unity player. The fixture also exposes a method in
`Assembly-CSharp` that the external smoke plugin discovers after Unity loads the
assembly, detours, and changes from `7` to `42`. It is not a sample game or a
compatibility claim.

Run the repository-level smoke script from PowerShell:

```powershell
./eng/Test-UnityMonoSmoke.ps1
```

Unity-generated state and player builds remain under ignored directories.
