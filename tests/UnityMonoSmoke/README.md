# Unity Mono smoke fixture

This minimal Unity 2022.3 project builds a Windows x64 player with the Mono
scripting backend. It exists only to exercise the complete Insider startup path
inside a real Unity player. The fixture also exposes a method in
`Assembly-CSharp` that the external smoke plugin discovers after Unity loads the
assembly, wraps with two detours, and changes from `7` to `42`. The plugin then
disposes both handles while the player keeps running, and a later direct call
observes the restored value `7`. The plugin also verifies a value-type instance
method with `ref self`, including mutation of the original struct, and a method
whose `ref` and `out` values flow through an original-call delegate. It is not
a sample game or a compatibility claim.

Run the repository-level smoke script from PowerShell:

```powershell
./eng/Test-UnityMonoSmoke.ps1
```

Unity-generated state and player builds remain under ignored directories.
