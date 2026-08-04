# Contributing Guide

Welcome, and thank you for your interest in contributing to DualSense Client. Please follow these guidelines to maintain
code quality and consistency across the project.

---

## Table of Contents

1. [Project Structure](#project-structure)
2. [Naming Conventions](#naming-conventions)
    - [Functions and Methods](#functions-and-methods)
    - [UI Elements (AXAML)](#ui-elements-axaml)
    - [Variables and Fields](#variables-and-fields)
    - [Properties](#properties)
3. [Coding Standards](#coding-standards)
    - [File Organization](#file-organization)
    - [MVVM Pattern](#mvvm-pattern)
    - [Commenting](#commenting)
    - [Error Handling and Logging](#error-handling-and-logging)
    - [Formatting](#formatting)
    - [Unit Testing](#unit-testing)
4. [Creating Custom Themes](#creating-custom-themes)
5. [Translating](#translating)
6. [Submitting Changes](#submitting-changes)

---

## Project Structure

The project is organized into six projects:

- **DualSenseClient.GUI**: Main application project containing Views, ViewModels, and UI-related logic
- **DualSenseClient.Core**: Core library containing shared models, enums, and utilities
- **DualSenseClient.Controllers**: Controller handling, DualSense protocol, and audio/haptics
- **DualSenseClient.Hid**: SDL3-based HID device access
- **DualSenseClient.Settings**: JSON-based settings persistence with backup recovery
- **DualSenseClient.Logging**: Custom logging infrastructure with file and console sinks

All shared logic should be placed in the appropriate library project to facilitate easier implementation of features across different UI platforms.

---

## Naming Conventions

### Functions and Methods

- Use **PascalCase** for method names
- Methods should clearly describe their purpose
- Example:
  ```csharp
  public void LoadLibrary()
  {
      // Implementation here
  }
  ```

### UI Elements (AXAML)

- Use **Hungarian Notation** with type prefixes:
    - `ComboBox` → `Cmb`
    - `TextBox` → `Txt`
    - `Button` → `Btn`
    - `TextBlock` → `Tbl`
    - `StackPanel` → `Sp`
    - `Grid` → `Grd`
    - `ScrollViewer` → `Sv`
    - `Expander` → `Exp`

- Property order in AXAML elements:
    1. `x:Name` / `x:Class`
    2. `x:DataType` (if using compiled bindings)
    3. `Grid.Column`, `Grid.Row`, `Grid.ColumnSpan`, `Grid.RowSpan`
    4. Data bindings (`{Binding ...}`, `{DynamicResource ...}`)
    5. Layout properties (alphabetically): `HorizontalAlignment`, `Margin`, `Padding`, `VerticalAlignment`, etc.
    6. Style properties (alphabetically): `FontSize`, `FontWeight`, `Foreground`, etc.
    7. Event handlers

- Example:
  ```xaml
  <ComboBox x:Name="CmbLanguage"
            Grid.Column="1"
            AutomationProperties.Name="{DynamicResource SettingsPage_LanguageSelector}"
            AutomationProperties.HelpText="{DynamicResource SettingsPage_LanguageSelectorTooltip}"
            DisplayMemberPath="Name"
            SelectedValuePath="Name"
            HorizontalAlignment="Center"
            VerticalAlignment="Center"
            MinWidth="150"
            SelectionChanged="CmbLanguage_SelectionChanged" />
  ```

### Variables and Fields

- **Private instance fields**: Use `_camelCase` with leading underscore
    - Example: `_settings`, `_releaseService`
- **Local variables**: Use `camelCase`
    - Example: `gameId`, `userInput`
- **Static fields**: Use `PascalCase` or `_camelCase` depending on visibility
    - Example: `Games` (public), `_isRunning` (private)

### Properties

- **Public properties**: Use **PascalCase**
    - Example: `TitleId`, `Games`
- **Partial methods for property changes** (CommunityToolkit.Mvvm):
  ```csharp
  [ObservableProperty]
  private bool _checkForUpdatesOnStartup;

  partial void OnCheckForUpdatesOnStartupChanged(bool oldValue, bool newValue)
  {
      if (oldValue == newValue) return;
      _log.Info($"Check for Updates on Startup changed from '{oldValue}' to '{newValue}'");
      _settings.Settings.UpdateChecks.CheckForUpdatesOnStartup = newValue;
      _settings.SaveSettings();
  }
  ```

---

## Coding Standards

### File Organization

- Place shared models, enums, and utilities in **DualSenseClient.Core**
- Place settings-related code in **DualSenseClient.Settings**
- Place logging-related code in **DualSenseClient.Logging**
- Keep Views lightweight, delegating logic to ViewModels and services
- Organize files by feature/namespace rather than type when possible

### MVVM Pattern

- Use **ViewModels** for UI state and data binding
- Use **CommunityToolkit.Mvvm** for MVVM implementation:
    - `[ObservableProperty]` for observable properties
    - Partial methods (`On<PropertyName>Changed`) for property change logic
- Keep code-behind (`.axaml.cs`) files minimal, containing only view-specific logic

### Commenting

- Use XML documentation comments for public and internal types and members:
  ```csharp
  /// <summary>
  /// Manages application settings by loading from and saving to a JSON file
  /// </summary>
  public class SettingsService
  {
      /// <summary>
      /// Loads settings from the JSON file.
      /// If the file doesn't exist, creates default settings.
      /// If the file is corrupted, attempts to recover from backup.
      /// </summary>
      public Settings LoadSettings()
      {
          // Implementation
      }
  }
  ```

- Use inline comments sparingly, only when the intent is not obvious from the code itself

### Error Handling and Logging

- Use `try-catch` blocks to handle exceptions appropriately
- Log all exceptions using the `DualSenseClientLogger`:
  ```csharp
  private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("ClassName");

  try
  {
      // Operation
  }
  catch (Exception ex)
  {
      _log.Error("Error description");
      _log.LogExceptionDetails(ex);
  }
  ```

- Use appropriate log levels:
    - `Trace`: Detailed debugging information
    - `Debug`: General diagnostic information
    - `Info`: General operational messages
    - `Warning`: Potential issues that don't stop execution
    - `Error`: Errors that cause operations to fail
    - `Critical`: Critical errors that may cause application termination

- Throw `Exception` (or specific exception types) for unimplemented features or invalid states

### Formatting

- Use **4 spaces** for indentation (no tabs)
- Place opening braces `{` on a **new line**
- Use expression-bodied members when appropriate for simple methods/properties
- Use file-scoped namespaces:
  ```csharp
  namespace DualSenseClient.Core.Models;

  public class Game
  {
      // Implementation
  }
  ```

- Use `using` directives sorted alphabetically, with system namespaces first
- Prefer `var` when the type is obvious, explicit types when clarity is needed

### Unit Testing

The project uses **NUnit** as the testing framework. Tests are located in `tests/DualSenseClient.Tests/` and are organized by component:

```
tests/DualSenseClient.Tests/
├── Logging/      # Tests for logging infrastructure
├── Settings/     # Tests for settings persistence
├── Controllers/  # Tests for the DualSense protocol and audio/haptics
├── Hid/          # Tests for HID enumeration
└── Core/         # Tests for shared utilities (PathResolver, etc.)
```

#### Writing Tests

- Place tests in the folder matching the component being tested (e.g., `Logging/` for `DualSenseClient.Logging` source)
- Name test files `{ClassName}Tests.cs` (e.g., `FileLogSinkTests.cs`)
- Use `[SetUp]` and `[TearDown]` for test initialization and cleanup (especially for file-based tests using temp directories)
- Follow the `MethodName_Scenario_ExpectedResult` naming convention where clarity is needed
- Use `Assert.That()` with constraint-based assertions (NUnit 4 style)

#### Running Tests

```bash
# Run all tests
dotnet test

# Run tests in a specific project
dotnet test tests/DualSenseClient.Tests/DualSenseClient.Tests.csproj

# Run with verbose output
dotnet test --verbosity normal
```

#### What to Test

- **Public APIs**: Test all public methods and properties
- **Edge cases**: Null inputs, empty strings, boundary values, invalid JSON
- **Error handling**: Verify fallback behavior (e.g., corrupt settings files fall back to defaults)
- **Disposal**: Verify resources are cleaned up (e.g., file handles closed, timers disposed)
- **Thread safety**: Where applicable, test concurrent access patterns

#### File-Based Tests

When testing components that create files (e.g., `FileLogSink`, `SettingsService`):

- Use `Path.GetTempPath()` with a unique GUID-based subdirectory for isolation
- Always clean up in `[TearDown]` using `Directory.Delete(path, recursive: true)`
- Use `try-catch` in teardown to handle cleanup failures gracefully

---

## Creating Custom Themes

DualSense Client supports custom themes. To create a new theme:

1. **Copy the template file**
   - Navigate to `source/DualSenseClient.GUI/Resources/Theme/`
   - Copy `Template.axaml` to a new file (e.g., `MyCustomTheme.axaml`)

2. **Define your theme colors**
   - Open your new `.axaml` file
   - Replace all color values with your theme's colors
   - Keep the `x:Key` names unchanged - they are required by the application

3. **Register your theme**
   - Open `source/DualSenseClient.Core/Models/Theme.cs`
   - Add your theme name to the `Theme` enum:
   ```csharp
   public enum Theme
   {
       System,
       Light,
       Dark,
       MyCustom // Add your theme here
   }
   ```
   - Open `source/DualSenseClient.GUI/Services/ThemeService.cs`
   - Add your theme to the `SwapDictionary` switch expression:
   ```csharp
   string resourcePath = theme switch
   {
       Theme.Dark => "avares://DualSenseClient.GUI/Resources/Theme/Dark.axaml",
       Theme.MyCustom => "avares://DualSenseClient.GUI/Resources/Theme/MyCustomTheme.axaml",
       _ => "avares://DualSenseClient.GUI/Resources/Theme/Light.axaml"
   };
   ```
   - If your theme uses a dark variant, add a case in `ApplyTheme`:
   ```csharp
   ThemeVariant targetVariant = theme switch
   {
       Theme.Light => ThemeVariant.Light,
       _ => ThemeVariant.Dark // Dark, MyCustom, etc.
   };
   ```

4. **Add localization strings** (optional)
   - Add display name entries to `source/DualSenseClient.GUI/Resources/Language/en.axaml`:
   ```xml
   <sys:String x:Key="SettingsPage.Ui.Theme.Option.MyCustom">My Custom Theme</sys:String>
   ```

5. **Build and test**
   - Build the project and verify your theme loads correctly
   - Test with various controls (buttons, textboxes, lists, etc.)

### Theme Color Guidelines

- **For DARK themes:** Use dark backgrounds (`#FF000000`) with light text (`#FFFFFFFF`)
- **For LIGHT themes:** Use light backgrounds (`#FFFFFFFF`) with dark text (`#FF000000`)
- **Accessibility:** Maintain sufficient contrast ratios (WCAG AA minimum recommended)
- **Accent color:** Choose a color that works on both light and dark backgrounds

---

## Translating

### Adding a New Language

1. Create a new `.axaml` file in `source/DualSenseClient.GUI/Resources/Language/` named with the language code (e.g., `de.axaml` for German)
2. Copy all `<sys:String x:Key="...">` entries from `en.axaml` and translate the values
3. Add the language code to the chart script's `LANGUAGE_NAMES` mapping in `scripts/generate_translation_progress.py`:
   ```python
   LANGUAGE_NAMES = {
       "en": "English",
       "de": "German",
       # Add language code -> display name mappings here when adding new languages
   }
   ```
4. Run the progress checker to verify:
   ```bash
   python scripts/generate_translation_progress.py --verbose
   python scripts/generate_translation_progress.py --chart
   ```

### Updating an Existing Language

1. Sync all language files with the latest `en.axaml` strings:
   ```bash
   python scripts/sync_localization.py
   ```
2. Edit the corresponding `.axaml` file and translate any new or changed `<sys:String>` values
3. Avoid using `#NOTTRANSLATED#` markers in committed translations

---

## Submitting Changes

1. **Create a Branch**:
   ```bash
   git checkout -b feature/your-feature-name
   ```
   Branch naming convention:
   - `feature/description` - New features
   - `bugfix/description` - Bug fixes
   - `refactor/description` - Code refactoring
   - `docs/description` - Documentation changes

2. **Write Meaningful Commits**:
   - Use conventional commit format:
     ```bash
     git commit -m "feat(dualsense): Add game details editor dialog"
     git commit -m "fix(ci): Fix crash when loading corrupted library file"
     git commit -m "refactor(misc): Extract logging logic to separate service"
     ```
   - Keep commits atomic and focused on a single change
   - Write clear, descriptive commit messages

3. **Submit a Pull Request**:
   - Push your branch to the remote repository
   - Open a pull request targeting the `dev` branch
   - Link to any related issues
   - Provide a clear description of:
     - What changes were made
     - Why the changes were necessary
     - Any testing performed
     - Screenshots (for UI changes)

---

Thank you for contributing to DualSense Client!
