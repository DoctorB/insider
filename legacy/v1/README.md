# Insider v1 archive

This directory preserves the source and exploratory projects recovered from the
original 2016 Insider codebase.

The archive targets .NET Framework 3.5 and Visual Studio 2013. Its hook engine
rewrites JIT code memory directly and is not compatible with current CoreCLR
memory protection, tiered compilation, or the modern support policy.

> [!CAUTION]
> Do not use this implementation in a game or production process. It is excluded
> from `Insider.slnx` and retained only for provenance and design history.

The archived source includes:

- The original `Hook` and `InsiderManager` implementation.
- Original custom exceptions and project metadata.
- The Unity-like console test harness.
- The function-pointer exploration project.
- The original long-form documentation under `docs/`.

The previously distributed obfuscated binary, ConfuserEx project, and generated
Jekyll site are intentionally not included.
