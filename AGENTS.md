# AGENTS.md

Guidance for AI coding agents working on this fork of Azure Key Vault Explorer.

## What this project is

A cross-platform desktop GUI (Avalonia/.NET 10) for browsing Azure Key Vault secrets, keys, and certificates. The upstream project is [cricketthomas/AzureKeyVaultExplorer](https://github.com/cricketthomas/AzureKeyVaultExplorer). This fork adds DevOps-focused features (copy as ENV/Docker/K8s, bulk CSV export/import) intended to be contributed back upstream.

## Repository layout

```
src/
  avalonia/
    KeyVaultExplorer/        # Main application project
      ViewModels/            # MVVM view models ([RelayCommand], [ObservableProperty])
      Views/Pages/           # Avalonia AXAML views
      Services/              # VaultService, AuthService, ClipboardService, StorageProviderService, …
      Models/                # KeyVaultContentsAmalgamation, KeyVaultItemType, …
      Exceptions/            # KeyVaultItemNotFoundException, KeyVaultInsufficientPrivilegesException
    Desktop/                 # Entry-point / desktop host
docs/                        # BUILDING.md, FIRST-TIME-SETUP.md, TROUBLESHOOTING.md
```

## Architecture

- **Pattern**: MVVM via [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/). ViewModels use `[ObservableProperty]` and `[RelayCommand]` source generators — do not write boilerplate property/command wrappers by hand.
- **DI**: Services are resolved through `Defaults.Locator.GetRequiredService<T>()` in ViewModel constructors. Register new services in `Services/ServiceCollectionExtension.cs`.
- **Notifications**: Call `ShowInAppNotification(title, message, NotificationType.Success|Warning|Error)` from any ViewModel — do not write custom toast logic.
- **Busy state**: Set `IsBusy = true` before long async work, always reset in a `finally` block.
- **Clipboard**: After writing to clipboard, always fire `_ = Task.Run(async () => await ClearClipboardAsync().ConfigureAwait(false))` to honour the user's auto-clear timeout.
- **File dialogs**: Use `StorageProviderService` (wraps Avalonia's `IStorageProvider`). Do not use `System.IO.File` for path-based file access — it bypasses sandboxing on some platforms.

## Building

Requires .NET 10 SDK. No build script is needed for a basic compile:

```bash
dotnet build src/avalonia/KeyVaultExplorer/KeyVaultExplorer.csproj
```

The full publish pipeline uses `build.ps1` (see `docs/BUILDING.md`).

## Code conventions

- Commands that operate on a single vault item receive `KeyVaultContentsAmalgamation` as their parameter and guard with `if (keyVaultItem is null) return;`.
- Gate non-secret operations with a type check and a `NotificationType.Warning` notification rather than silently doing nothing.
- Exception handling in commands: catch `KeyVaultItemNotFoundException`, `KeyVaultInsufficientPrivilegesException`, then `Exception` — in that order. Never use a bare `catch {}`.
- Do not add comments that describe *what* code does; only add them when the *why* is non-obvious.

## Security requirements

This application handles live secret values. All new code must follow these rules:

- **YAML output**: Always use `YamlDoubleQuote()` (in `VaultPageViewModel.cs`) when embedding secret values in YAML strings. Raw interpolation into YAML scalars enables injection.
- **CSV output**: Always use `CsvEscape()` for every field. The helper prefixes formula-injection characters (`=`, `+`, `-`, `@`, `|`) with a tab per OWASP guidance. Strip the tab again when reading values back on import.
- **No shell execution of secret values**: Never pass a secret value as a shell argument or build a command string containing one.
- **No logging of secret values**: Do not write `sv.Value` to any log, trace, or debug output.
- **Notification messages**: Do not include raw secret values in notification titles or bodies.

## Upstream contribution intent

Features added here are intended as PRs to `cricketthomas/AzureKeyVaultExplorer`. Keep changes scoped, follow the upstream code style, and avoid fork-specific branding or config in feature code.
