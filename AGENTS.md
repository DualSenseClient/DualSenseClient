# AGENTS.md

Guidance for AI coding agents working in this repository.

## Project Overview

DualSense Client is an unofficial open-source management tool for the PlayStation 5 DualSense controller on Windows and Linux. It controls lighting, player/mic LEDs, profiles, rumble, adaptive triggers, and audio playback to the controller's speaker, headset jack, and haptics. It also provides virtual controller emulation (DualSense, DualShock 4, Xbox 360) and controller hiding through HidHide; both are Windows-only for now.

- **Stack:** C# / .NET 10, Avalonia 12, FluentAvalonia, CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection
- **Testing:** NUnit 5 with NUnit3TestAdapter
- **License:** GPL-3.0
- **Docs:** Zensical site built from `docs/`, deployed to GitHub Pages

`docs/CONTRIBUTING.md` is the source of truth for coding standards and contribution workflow. `docs/development.md` covers building and hardware tests. Read both before making non-trivial changes.

### Do Not Assume

Do not apply patterns from other C# desktop projects:

- This is **Avalonia 12**, not WPF, UWP, WinUI, or WinForms.
- This is **.NET 10**. Do not downgrade target frameworks or use .NET Framework APIs.
- Controller I/O uses **HIDAPI**; emulation uses the built-in **USB/IP** engine, not ViGEmBus or host input APIs such as XInput or DirectInput.
- Tests use **NUnit 5** and `Assert.That(...)`, not xUnit, MSTest, or FluentAssertions.
- Settings are **lenient JSON with backup recovery**. Do not replace the loader with strict deserialization that rejects partially valid files.

## Non-Negotiables

These rules override convenience and speed. If a request conflicts with them, say so before acting.

- **Work from source.** Never edit generated, fetched, or build output. Find the source and regenerate instead.
- **Read before editing.** Read the files you will change and the files that call them. Do not rely on search snippets for broad changes.
- **Keep changes surgical.** Touch only what the task needs. No drive-by refactors, formatting sweeps, or unrelated dependency updates.
- **Verify before saying done.** A plausible diff is not proof. Run the relevant build, tests, and lint, and report the exact results.
- **Never fabricate.** Do not invent file paths, symbols, command output, test results, or API behavior. If you did not run it, say so.
- **Stop when ambiguity matters.** If two interpretations would produce materially different changes, ask before editing.
- **Do not open or update a PR unless the user explicitly asks.**

## Prerequisites and Setup

- .NET 10 SDK (`net10.0`; nullable reference types are enabled)
- Python 3 for the scripts in `scripts/`
- On Windows, fetch the embedded HIDAPI native library once after cloning:

```powershell
python scripts/fetch_native_libraries.py win-x64
```

`DualSenseClient.Hid` embeds `native/version.txt` unconditionally and `native/win-x64/hidapi.dll` on Windows, so the build fails without this step. The fetched `source/DualSenseClient.Hid/native/` directory is gitignored; do not delete it as "cleanup". On Linux the app loads the system `libhidapi-hidraw` library instead.

## Commands

Run all commands from the repository root. Use these scripts instead of hand-rolled equivalents.

| Task                         | Command                                                                                                               |
| ---------------------------- | --------------------------------------------------------------------------------------------------------------------- |
| Restore / build              | `dotnet restore` then `dotnet build DualSenseClient.sln`                                                              |
| Run the app                  | `dotnet run --project source/DualSenseClient.GUI/DualSenseClient.GUI.csproj`                                          |
| Run all tests                | `dotnet test tests/DualSenseClient.Tests/DualSenseClient.Tests.csproj`                                                |
| Run a test class             | `dotnet test tests/DualSenseClient.Tests/DualSenseClient.Tests.csproj --filter "FullyQualifiedName~FileLogSinkTests"` |
| Format changed files         | `python scripts/lint.py --changed`                                                                                    |
| Verify formatting (CI)       | `python scripts/lint.py --check`                                                                                      |
| Check localization keys (CI) | `python scripts/check_localization.py`                                                                                |
| Sync language files          | `python scripts/sync_localization.py`                                                                                 |
| Preview docs                 | `pip install -r requirements.txt` then `zensical serve`                                                               |
| Build docs strictly          | `zensical build --strict`                                                                                             |

CI runs `python scripts/lint.py --install --pin-version --check`; the pinned ReSharper Command Line Tools version is in `scripts/lint.py` (`TOOL_VERSION`). `--check` applies formatting to the working tree before reporting whether anything changed, so run `--changed` first.

The default test run uses virtual devices only. Hardware and USB/IP suites are opt-in:

```powershell
# Physical DualSense / DualSense Edge
$env:DUALSENSE_HARDWARE = "1"
dotnet test tests/DualSenseClient.Tests/DualSenseClient.Tests.csproj --filter "FullyQualifiedName~DualSenseHardwareSmokeTests"

# Bluetooth audio + trigger readback (requires a Bluetooth DualSense)
$env:DUALSENSE_HARDWARE = "1"
dotnet test tests/DualSenseClient.Tests/DualSenseClient.Tests.csproj --filter "FullyQualifiedName~DualSenseBluetoothAudioHardwareTests"

# USB/IP emulation (requires usbip-win2; close the app first)
$env:EMULATION_INTEGRATION = "1"
dotnet test tests/DualSenseClient.Tests/DualSenseClient.Tests.csproj --filter "FullyQualifiedName~DualSenseEndToEndTests"
```

On Linux/macOS, set the same variables inline, e.g. `DUALSENSE_HARDWARE=1 dotnet test ...`. Close the application before hardware runs so the test process owns the HID handles.

## Repository Layout

| Path                                 | Responsibility                                                                                                                                     |
| ------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| `source/DualSenseClient.GUI`         | Avalonia desktop app: entry point, Views/ViewModels, DI composition, themes, localization resources                                                |
| `source/DualSenseClient.Controllers` | DualSense protocol, input/output reports, triggers, lightbar, audio forwarding, profiles, special actions, touchpad mouse, emulation orchestration |
| `source/DualSenseClient.Emulation`   | USB/IP virtual controller engine: device descriptors, HID/audio lanes, DualSense/DualShock 4/Xbox 360 personas, VHCI attach                        |
| `source/DualSenseClient.Hid`         | HIDAPI P/Invoke wrapper, device enumeration and handles; embeds the Windows native library                                                         |
| `source/DualSenseClient.Bluetooth`   | Bluetooth integration: native Windows radio driver vs. BlueZ over Tmds.DBus on Linux                                                               |
| `source/DualSenseClient.Settings`    | JSON settings, profiles, auto-profiles, controller info, special actions; lenient deserialization and backup recovery                              |
| `source/DualSenseClient.Core`        | Shared models and utilities: paths, updates, changelog, foreground app detection, timer resolution                                                 |
| `source/DualSenseClient.Logging`     | Custom logging infrastructure and sink implementations                                                                                             |
| `source/DualSenseClient.HidHide`     | HidHide driver integration (Windows only)                                                                                                          |
| `tests/DualSenseClient.Tests`        | NUnit tests, one folder per tested source area, plus emulation conformance/integration suites                                                      |
| `scripts/`                           | Lint, localization, native-library, changelog, and translation-progress tooling                                                                    |
| `docs/`                              | Zensical documentation sources; `site/` is generated and gitignored                                                                                |

Dependency direction: most libraries depend on `Core`/`Logging`; `Controllers` composes `Core`, `Emulation`, `Hid`, `Settings`, and `Bluetooth`; `GUI` composes the application through DI. Put shared logic in the library matching its concern. When a new concern appears, ask the user before adding a new `DualSenseClient.<Area>` project.

Where to start by task:

| Task                                  | Read first                                                                                            |
| ------------------------------------- | ----------------------------------------------------------------------------------------------------- |
| GUI, MVVM, themes                     | `source/DualSenseClient.GUI/`, `docs/CONTRIBUTING.md`                                                 |
| DualSense protocol, reports, triggers | `source/DualSenseClient.Controllers/DualSense/`, `tests/DualSenseClient.Tests/Controllers/DualSense/` |
| Emulation / USB/IP                    | `source/DualSenseClient.Emulation/`, `tests/DualSenseClient.Tests/Emulation/`                         |
| Settings, profiles, auto-profiles     | `source/DualSenseClient.Settings/README.md`, `source/DualSenseClient.Settings/Sections/`              |
| Audio forwarding, haptics             | `source/DualSenseClient.Controllers/DualSense/Audio/`, `docs/guides/audio.md`                         |
| Touchpad mouse                        | `source/DualSenseClient.Controllers/TouchpadMouse/`, `docs/guides/touchpad-mouse.md`                  |
| HID / Bluetooth                       | `source/DualSenseClient.Hid/`, `source/DualSenseClient.Bluetooth/`                                    |

## Architecture and Patterns

- **MVVM:** CommunityToolkit.Mvvm source generators. Use `[ObservableProperty]` on `_camelCase` fields and `partial void On<PropertyName>Changed(...)` for change logic. Keep `.axaml.cs` files minimal and view-specific.
- **Dependency injection:** Register services as singletons in `source/DualSenseClient.GUI/Services/ServiceConfigurator.cs`. Resolve dependencies through constructors; avoid service locators in new code.
- **Logging:** `private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("ClassName");` (the existing code often omits `private`). Use `_log.LogExceptionDetails(ex)` inside catch blocks. Levels: Trace, Debug, Info, Warning, Error, Critical.
- **Settings:** Add a section under `source/DualSenseClient.Settings/Sections/`, expose it from `Settings.cs` with a `[JsonPropertyName]` attribute and a default instance, and rely on the lenient deserializer to preserve valid values when JSON is missing or malformed. See `source/DualSenseClient.Settings/README.md`.
- **Platform-specific code:** Gate features with `OperatingSystem.IsWindows()` / `OperatingSystem.IsLinux()` rather than compile-time symbols. Emulation, HidHide, touchpad mouse injection, and per-application audio loopback are Windows-only; Linux connects through BlueZ and system HIDAPI.
- **Timing-sensitive code:** Audio forwarding, USB/IP pacing, and lightbar effects use clocks and schedulers. Preserve existing threading and timing patterns, and inject interfaces/`TimeProvider`-style abstractions so tests remain deterministic.

## Code Style

The root `.editorconfig` is authoritative; the main rules are:

- 4 spaces, no tabs; opening braces on a new line (Allman); CRLF line endings for repository files
- File-scoped namespaces
- Explicit types only: never `var`, never target-typed `new()`
- Properties and accessors always use block bodies; expression-bodied members only for simple single-line methods
- `using` directives sorted alphabetically with `System.*` first
- `_camelCase` for private fields, `camelCase` for locals, `PascalCase` for types/methods/properties/constants, `IPascalCase` for interfaces
- XML documentation comments for public and internal types and members
- AXAML: descriptive PascalCase names, with a short type suffix when useful (`...Combo`, `...Block`, `...Border`, `...View`, `...Frame`, `...NavItem`); order attributes as `x:Name`/`x:Class`, `x:DataType`, grid position, bindings, layout, style, then event handlers
- Comments explain non-obvious constraints or reasons, not what the code does
- Match the style of the surrounding file; do not reformat unrelated code

## Testing Conventions

- NUnit 5 and constraint-based assertions (`Assert.That(...)`).
- Put tests in the folder matching the source area (`tests/DualSenseClient.Tests/<Area>/`); name files `{ClassName}Tests.cs`.
- Use `MethodName_Scenario_ExpectedResult` names when clarity benefits. Prefer small fakes/hand-written test doubles already present under `Emulation/TestSupport/`, `Controllers/TouchpadMouse/RecordingMouseInjector.cs`, etc.
- Cover public behavior, edge cases (null/empty/boundary/invalid JSON), error and fallback paths, disposal, and thread safety where relevant.
- File-based tests: create a unique directory under `Path.GetTempPath()` and delete it recursively in `[TearDown]`, tolerating cleanup failures.
- Add or update tests for every behavior change. Do not weaken, delete, or skip tests to make a run pass. Skipped hardware/USBIP tests are expected unless the corresponding environment variable is set.
- For bug fixes, add a regression test that fails before the fix and passes after when practical; keep test changes proportional to the fix.
- Test behavior and contracts, not private state, implementation details, or third-party code. Do not add trivial tests just for coverage.
- While iterating, run the focused test class. Run the full suite before finishing.

## Localization, Themes, and Docs

- All user-visible strings must be localized. Define keys in `source/DualSenseClient.GUI/Resources/Language/en.axaml`; use `{DynamicResource Key}` in AXAML or `LocalizationService.GetText("Key")` in C#. Run `python scripts/check_localization.py` before finishing; missing keys fail CI.
- To add a language, copy `en.axaml`, translate values, register the language code in `scripts/generate_translation_progress.py`, and run the sync/check scripts. Do not commit `#NOTTRANSLATED#` markers.
- Themes: copy `Resources/Theme/Template.axaml`, keep existing `x:Key` names, register the theme in `Core/Models/Theme.cs` and `GUI/Services/ThemeService.cs`.
- Documentation lives in `docs/` as Markdown; navigation is in `zensical.toml`. `docs/CONTRIBUTING.md` is excluded from the published site.

## Git and Pull Requests

- Use `feature/...`, `bugfix/...`, `refactor/...`, or `docs/...` branches. Target `dev` when it exists; use `main` as the fallback branch and open PRs against `main` until `dev` exists.
- Use conventional commits, e.g. `feat(touchpad-mouse): Add two-finger scrolling`, `fix(hid): Run the watcher on one thread`, `docs(linux): Update udev rules`.
- Keep commits atomic and focused.
- Never add an AI agent, tool, or model as a commit co-author (`Co-Authored-By`) or sign-off.
- Never commit build output, fetched native binaries, generated docs (`site/`), or IDE user settings. `bin/`, `obj/`, `source/DualSenseClient.Hid/native/`, `site/`, and `publish/` are ignored.
- Before opening a PR, search open and closed issues and PRs for duplicates, and link the related issue when one exists.
- Keep one concern per PR. Do not bundle unrelated formatting, dependency, or refactor changes.
- Describe what changed, why, and how it was tested. List the exact commands and results, and state any check you could not run.
- For UI changes, include a screenshot or recording when practical.

## Contribution and Communication Rules

These rules keep review and triage manageable. They apply to humans and to AI agents acting on their behalf.

### AI-assisted contributions

- Disclose meaningful AI/LLM assistance in the pull request description. Routine autocomplete does not need disclosure.
- You are responsible for every line you submit. Be able to explain what it does, why it is needed, and how it was tested.
- Keep AI-assisted changes small and reviewable. Do not submit bulk-generated code, speculative refactors, or unreviewed output.
- Do not use AI to fabricate test results, logs, issue reports, or reviewer responses.
- Maintainers may close contributions that cannot be explained or that ignore these rules.

### GitHub interactions

- Agents must not open, update, or close pull requests, issues, or discussions, and must not post comments or reviews, on the user's behalf.
- Agents may draft pull request descriptions, issue text, commit messages, and review replies locally. A human must review and post them.
- Do not automate reactions, approvals, or repetitive comments.
- Keep maintainer interactions human-to-human. Do not impersonate a maintainer or another contributor.

### Review expectations

- Explain what changed, why, and how you tested it.
- Ask before large refactors, new dependencies, or changes outside the scope of the issue.
- Respond to review comments yourself; do not paste unreviewed AI output.
- If you cannot explain a change, do not submit it.

### Communication style

- Be direct and concise. Start with the answer or the action.
- No flattery, filler, or fake certainty. Do not narrate every tool call.
- Report what changed, how it was verified, and what was not done.
- Distinguish observed results from inference. Never claim a command ran if it did not.
- If the request's premise is wrong, say so before acting on it.

## Boundaries

Ask before:

- Adding a NuGet dependency, changing the target framework, or replacing a core library.
- Changing the settings schema, profile format, or on-disk file layout.
- Changing controller report byte layouts, public protocol behavior, or emulation descriptors.
- Large refactors, formatting sweeps, or changes outside the task's scope.
- Destructive git operations (`git reset --hard`, `git clean -fd`, force-push) or history rewrites.

Never:

- Commit secrets, credentials, machine-specific paths, build output, fetched binaries, or generated docs.
- Weaken, delete, or skip tests to make a run pass.
- Bypass lint, build, or test failures with `--no-verify` or equivalent.
- Edit generated or fetched files instead of their source.
- Revert or overwrite user changes you did not make.

## Before Finishing

Run the checks that match the change, then the full gate:

| Change                           | Minimum verification                                                                                                    |
| -------------------------------- | ----------------------------------------------------------------------------------------------------------------------- |
| Any code change                  | `dotnet build DualSenseClient.sln`, focused tests, then the full test suite, `python scripts/lint.py --changed`         |
| Controllers / protocol / reports | Focused tests under `tests/DualSenseClient.Tests/Controllers/DualSense/`; report byte changes need report tests         |
| Emulation / USB/IP               | Tests under `tests/DualSenseClient.Tests/Emulation/`; optionally `EMULATION_INTEGRATION=1` when usbip-win2 is available |
| GUI / AXAML                      | Build and, when the environment supports it, run the app; verify the affected theme, localization, and visual behavior  |
| Settings / profiles              | Settings tests; verify existing config files still load and backups remain valid                                        |
| Localization                     | `python scripts/sync_localization.py` and `python scripts/check_localization.py`                                        |
| Docs only                        | `zensical build --strict`                                                                                               |

Before declaring done:

- [ ] Build succeeds with 0 warnings and 0 errors.
- [ ] Relevant tests pass; skipped hardware/USBIP tests are called out.
- [ ] Formatting is clean.
- [ ] Localization check passes if UI strings changed.
- [ ] Platform-specific changes were tested on each affected OS, or the untested platform is called out.
- [ ] `git status --short` shows only intended files.
- [ ] The report lists the exact commands run, their results, and any check you could not run.

Do not claim success without command output.

## Gotchas

- A clean build requires `source/DualSenseClient.Hid/native/version.txt`; run the fetch script after cloning or if the directory was removed.
- The test suite intentionally logs `CRITICAL`/`ERROR` entries from negative-path tests. Judge the run by the final `Passed!`/`Failed!` summary, not by scary-looking log lines.
- The app's emulation server binds loopback port 3242. Stop a running app instance if emulation or USB/IP tests report "address already in use".
- `DualSenseClient.sln.DotSettings.user` is local IDE state; do not edit or commit it.
- The Windows build is framework-dependent; Linux releases are self-contained. CI also builds an AppImage.

### Generated and fetched files

Do not edit these by hand:

| Path                                 | What it is                                | Regenerate with                                    |
| ------------------------------------ | ----------------------------------------- | -------------------------------------------------- |
| `source/DualSenseClient.Hid/native/` | Fetched HIDAPI binaries and version stamp | `python scripts/fetch_native_libraries.py win-x64` |
| `site/`                              | Generated Zensical documentation          | `zensical build`                                   |
| `bin/`, `obj/`, `publish/`           | Build and publish output                  | `dotnet build` / `dotnet publish`                  |

## Keeping This File Current

- When the user corrects an approach or a convention changes, add one concrete rule here before ending the session.
- Keep this file concise. Move subsystem-specific detail into `docs/` or a nested `AGENTS.md` in that area.
- `docs/CONTRIBUTING.md` remains the source of truth for style and contribution workflow; this file must not contradict it.
