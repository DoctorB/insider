# Third-party notices

Insider Windows packages redistribute the following .NET Standard 2.0 runtime
components for the managed hooking backend. They are not relicensed under the
Insider Apache-2.0 license.

## Components

| Package | Version | Redistributed assemblies | License | Source |
| --- | --- | --- | --- | --- |
| MonoMod.RuntimeDetour | 25.3.6 | `MonoMod.RuntimeDetour.dll` | MIT | [MonoMod](https://github.com/MonoMod/MonoMod) |
| MonoMod.Utils | 25.0.14 | `MonoMod.Utils.dll` | MIT | [MonoMod](https://github.com/MonoMod/MonoMod) |
| MonoMod.Core | 1.3.6 | `MonoMod.Core.dll`, `MonoMod.Iced.dll` | MIT | [MonoMod](https://github.com/MonoMod/MonoMod) |
| MonoMod.Backports | 1.1.2 | `MonoMod.Backports.dll` | MIT | [MonoMod](https://github.com/MonoMod/MonoMod) |
| MonoMod.ILHelpers | 1.1.0 | `MonoMod.ILHelpers.dll` | MIT | [MonoMod](https://github.com/MonoMod/MonoMod) |
| Mono.Cecil | 0.11.6 | `Mono.Cecil.dll`, `Mono.Cecil.Mdb.dll`, `Mono.Cecil.Pdb.dll`, `Mono.Cecil.Rocks.dll` | MIT | [Mono.Cecil](https://github.com/jbevain/cecil) |
| System.Reflection.Emit.ILGeneration | 4.7.0 | `System.Reflection.Emit.ILGeneration.dll` | MIT | [.NET CoreFX](https://github.com/dotnet/corefx) |
| System.Reflection.Emit.Lightweight | 4.7.0 | `System.Reflection.Emit.Lightweight.dll` | MIT | [.NET CoreFX](https://github.com/dotnet/corefx) |

Package metadata and dependency versions are resolved from NuGet. The direct
RuntimeDetour package is available at
[nuget.org/packages/MonoMod.RuntimeDetour](https://www.nuget.org/packages/MonoMod.RuntimeDetour/25.3.6).

## Redistributed assembly hashes

These SHA-256 values identify the exact package assemblies selected for the
current .NET Standard 2.0 bundle:

| Assembly | SHA-256 |
| --- | --- |
| `Mono.Cecil.dll` | `831DCA77470D85CB6FFBEA3072DAA7A3DF5B7C9FCFD9C3F43674A9BE99D4BFCF` |
| `Mono.Cecil.Mdb.dll` | `28CB367972BDC1CD43E4006306AF2FD96D37F4ED4B239EE90E1DC7237A93AF7F` |
| `Mono.Cecil.Pdb.dll` | `A332332633FBCB20E8D50E49B4DB7BD1557721417122CF0C5F4C42F2332391D0` |
| `Mono.Cecil.Rocks.dll` | `BF992F3DCE364EBCC3200FA7832EF07E20B4E2DBC3A8A6213CE44E3D239DB984` |
| `MonoMod.Backports.dll` | `1018A3604A8143913BF4A60AC9FE78050AFE4F91D2581CEA1A37AAEF9F3549F2` |
| `MonoMod.Core.dll` | `6A05EC34323C12D2F5CEBA3E7343BCEE1479CBB66D41CAA4D6EA5A082C6ACF19` |
| `MonoMod.Iced.dll` | `44A209E110CDF59ED92975050BE34A03C7ADA3CE281326B57F61660CBBC7FB70` |
| `MonoMod.ILHelpers.dll` | `D478BCF2E03337E14526C6DCFA8EDF0F5C653FE4E08ED9512F27CB9652CBA2E3` |
| `MonoMod.RuntimeDetour.dll` | `708E9BC593FE76A30F70468BF77981A11C8C45C1FC266E208904856557ADDF31` |
| `MonoMod.Utils.dll` | `E181D3ABA8CA8EB2C5CF1A3F6A3BCFA9DFFD5B302DBDFA43A4BBE866CF8ED498` |
| `System.Reflection.Emit.ILGeneration.dll` | `CAC0339E1222085FF8BF1E5225F4AA9559A1CE15B6CF5C2E1F65A3B4EF496A86` |
| `System.Reflection.Emit.Lightweight.dll` | `60C01BA12B3C03EAC692D14B6F1CE69900BF7425C4DAC487B99AB7DCAA9D7287` |

## MIT License

Copyright (c) MonoMod contributors

Copyright (c) Jb Evain

Copyright (c) Microsoft Corporation and .NET contributors

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
