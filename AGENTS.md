# AGENTS.md

## Cursor Cloud specific instructions

- The .NET 8 SDK is installed under `~/.dotnet` and is on `PATH` via `~/.bashrc`.
- The test project multi-targets `net8.0` and `net472`. `net472` requires Mono,
  which is **not** installed on this Linux VM, so a plain `dotnet test` aborts on
  the `net472` run. Use `dotnet test -f net8.0` locally; the full multi-target
  matrix (including `net472`) is a CI/Windows concern.
- Tests consume `whisperr-spec` fixtures. The sibling `../whisperr-spec` checkout
  is auto-detected in this workspace; override with `WHISPERR_SPEC_PATH` only when
  running from a different layout (see `README.md`).

## Design

- Avoid unnecessary secondary labels and descriptions. Let the layout do the explaining
