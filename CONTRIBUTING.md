# Contributing

Insider is pre-alpha. Small, focused changes that preserve the current Mono-first
scope are welcome.

## Development setup

1. Install a supported .NET 10 SDK.
2. Run `dotnet build Insider.slnx --configuration Release`.
3. Run `dotnet run --project tests/Insider.Tests --configuration Release --no-build`.

## Pull requests

- Keep unrelated changes separate.
- Add or update executable tests for behavior changes.
- Update `docs/hooking.md` whenever the hooking contract, supported signatures,
  lifecycle, backend limits, or runtime evidence changes.
- Document compatibility claims with a reproducible Unity player fixture.
- Do not add redistributed native binaries without updating
  `THIRD_PARTY_NOTICES.md` and documenting their provenance.
- Do not turn planned IL2CPP behavior into a public compatibility claim before
  it is tested end to end.

By submitting a contribution, you agree that it is licensed under Apache-2.0.
