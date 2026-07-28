# FluentAvalonia MVVM App with Dependency Injection

A cross-platform desktop application template built with FluentAvalonia UI, MVVM pattern, dependency injection, custom logging infrastructure, and settings persistence.

## Features

- **FluentAvalonia 3** — Modern Fluent design system on Avalonia 12
- **MVVM** — CommunityToolkit.Mvvm with compiled bindings
- **Dependency Injection** — Microsoft.Extensions.DependencyInjection
- **Custom Logging** — File and console log sinks with configurable log levels
- **Settings Persistence** — JSON-based settings with backup recovery and lenient deserialization
- **Theme System** — Light/Dark/System themes with custom resource dictionary overrides
- **Localization** — Multi-language support with untranslated-string fallback

## Getting Started

```bash
dotnet restore
dotnet build
dotnet run
```

## Installing as a Template

```bash
dotnet new install <path-to-this-folder>
dotnet new fluentavalonia-mvvm-di -n MyApp
```

## Project Structure

```
source/
├── DualSenseClient.Core/        # Shared models, enums, utilities
├── DualSenseClient.GUI/         # Main Avalonia application
│   ├── Controls/                 # Custom controls (splash screen, etc.)
│   ├── Resources/                # Theme dictionaries, language files
│   ├── Services/                 # Theme, localization, message box, notification services
│   ├── ViewModels/               # MVVM ViewModels
│   └── Views/                    # AXAML views
├── DualSenseClient.Logging/     # Custom logging infrastructure
└── DualSenseClient.Settings/    # JSON-based settings persistence
```

## Adding a Custom Theme

1. Copy `source/DualSenseClient.GUI/Resources/Theme/Template.axaml` to a new file
2. Replace color/brush values with your theme colors
3. Add your theme to the `Theme` enum in `source/DualSenseClient.Core/Models/Theme.cs`
4. Add a case to the `SwapDictionary` switch in `source/DualSenseClient.GUI/Services/ThemeService.cs`

See `docs/CONTRIBUTING.md` for detailed instructions.

## Acknowledgments

- [SharpEmu](https://github.com/par274/sharpemu/) — The custom logging infrastructure was inspired by SharpEmu's logger.
- [Lenovo Legion Toolkit](https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit) — The settings persistence library was inspired by Lenovo Legion Toolkit's settings system.

## License

See [LICENSE](LICENSE) for details.
