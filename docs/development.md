# Development

This page covers building DualSense Client from source. For coding standards, project structure, themes, and translations, see the full [Contributing Guide](https://github.com/DualSenseClient/DualSenseClient/blob/main/docs/CONTRIBUTING.md).

## Building from Source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
dotnet restore
dotnet build
dotnet test
```

Releases are produced automatically by CI for Windows (zip) and Linux (zip and AppImage).

## Project Structure

The solution follows a multi-project layout: each distinct concern lives in its own library project under `source/`, named `DualSenseClient.<Area>` (for example `DualSenseClient.Core`, `DualSenseClient.Hid`, `DualSenseClient.Settings`). **DualSenseClient.GUI** is the main application project containing Views, ViewModels, and UI-related logic.

Tests live in `tests/DualSenseClient.Tests/`, organized into one folder per source project.

## Contributing

Pull requests are welcome! Before submitting:

1. Read the [Contributing Guide](https://github.com/DualSenseClient/DualSenseClient/blob/main/docs/CONTRIBUTING.md)
2. Create a branch (`feature/...`, `bugfix/...`, `refactor/...`, or `docs/...`)
3. Run the linter:

    ```bash
    python scripts/lint.py --check
    ```

4. Open a pull request targeting the `dev` branch with a clear description of what changed, why, and how you tested it

For bug reports and feature requests, [open an issue](https://github.com/DualSenseClient/DualSenseClient/issues) instead.

## Translations & Themes

- **Translating**: add a language file under `source/DualSenseClient.GUI/Resources/Language/` — instructions in the [Contributing Guide](https://github.com/DualSenseClient/DualSenseClient/blob/main/docs/CONTRIBUTING.md#translating)
- **Custom themes**: copy the theme template and register it — instructions in the [Contributing Guide](https://github.com/DualSenseClient/DualSenseClient/blob/main/docs/CONTRIBUTING.md#creating-custom-themes)

These documentation pages are built with [Material for MkDocs](https://squidfunk.github.io/mkdocs-material/) from the Markdown files in `docs/` and deployed to GitHub Pages. To preview them locally:

```bash
pip install -r requirements.txt
mkdocs serve
```

