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
dotnet run --project tests/Pondhawk.Persistence.Core.Tests --configuration Release
dotnet run --project tests/Pondhawk.Persistence.Mcp.Tests --configuration Release
```
