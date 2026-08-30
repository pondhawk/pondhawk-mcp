# CLAUDE.md

## Build System

This project uses **Cake Frosting** (`build/` directory). Always use the Cake build system instead of running `dotnet` commands directly.

### Publishing

```bash
dotnet run --project build -- --target=Publish
```

This runs Clean → Restore → Build → Test → Publish (self-contained single-file binaries for win-x64, osx-arm64, linux-x64, linux-arm64).

### Running Tests

Always run tests using `dotnet run` (not `dotnet test`) because xunit v3 projects are self-hosted executables and the VSTest testhost has a version mismatch:

```bash
dotnet run --project tests/Pondhawk.Generation.Tests --configuration Release
dotnet run --project tests/Pondhawk.Generation.Mcp.Tests --configuration Release
```

### Coverage

```bash
dotnet run --project build -- --target=Coverage
```

Merges both suites into `coverage/report/index.html` and prints a summary. Add
`--threshold=N` to fail below N percent line coverage. Tools are restored from
`dotnet-tools.json`; nothing needs installing globally.

The report covers **assembly** names, not project names -- the MCP server builds as
`pondhawk-generation-mcp`. If an assembly is renamed, update `CoveredAssemblies` in
`build/Tasks/CoverageTask.cs` or the target will fail rather than silently omit it.
