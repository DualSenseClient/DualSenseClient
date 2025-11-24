# 🤝 Contributing to DualSense Client

Thank you for your interest in contributing to DualSense Client! This document outlines the guidelines and processes for contributing to the project to ensure a smooth and collaborative development experience.

## 📋 Table of Contents

- [📋 Prerequisites](#-prerequisites)
- [🔄 Getting Started](#-getting-started)
- [🔍 Development Workflow](#-development-workflow)
- [🚀 Code Style Guidelines](#-code-style-guidelines)
- [🧪 Testing Guidelines](#-testing-guidelines)
- [📝 Documentation](#-documentation)
- [🐛 Issue Reporting](#-issue-reporting)
- [🎯 Pull Request Process](#-pull-request-process)
- [🔧 Technical Guidelines](#-technical-guidelines)
- [🤝 Community Guidelines](#-community-guidelines)

## 📋 Prerequisites

Before contributing to DualSense Client, ensure you have the following:

### Development Environment

- **JetBrains Rider** (recommended) or **Visual Studio** or **Visual Studio Code**
- **.NET 9.0 SDK** or later
- **Git** for version control
- **GitHub account** for forking and pull requests

### System Requirements

- **Windows 10 version 1909 or later** (Windows 11 recommended)
- **PlayStation 5 DualSense controller** for testing

### Required Dependencies

- **.NET 9.0 Desktop Runtime** for application execution
- **ViGEmBus driver** for virtual controller emulation (for development testing)
- **HidHide driver** for device hiding functionality (for development testing)

## 🔄 Getting Started

### Fork and Clone

1. **Fork the repository** on GitHub by clicking the "Fork" button
2. **Clone your forked repository**:
   ```bash
   git clone https://github.com/YOUR_USERNAME/DualSenseClient.git
   cd DualSenseClient
   ```
3. **Add the upstream remote**:
   ```bash
   git remote add upstream https://github.com/shazzaam7/DualSenseClient.git
   ```

### Setup Development Environment

1. **Open the solution** in your IDE:
   ```
   DualSenseClient.sln
   ```
2. **Restore NuGet packages**:
   ```bash
   dotnet restore
   ```
3. **Build the solution**:
   ```bash
   dotnet build
   ```
4. **Run the application** in Debug mode to verify the setup

### Understanding the Codebase

1. Start by exploring the [Project Structure](PROJECT_STRUCTURE.md) documentation
2. Familiarize yourself with the MVVM architecture and dependency injection patterns
3. Review existing issues and pull requests to understand current development patterns

## 🔍 Development Workflow

### Finding Issues to Work On

- Look for issues with the `good first issue` label if you're new to the project
- Check issues with `help wanted` label for more experienced contributors
- Comment on issues you'd like to work on to avoid duplicate efforts

### Feature Development Process

1. **Create a feature branch** from `main`:
   ```bash
   git checkout main
   git pull upstream main
   git checkout -b feature/your-feature-name
   ```
2. **Implement your changes** following the coding guidelines
3. **Write or update tests** as needed
4. **Update documentation** if you're adding new features
5. **Test your changes** thoroughly
6. **Commit your changes** with clear commit messages
7. **Push to your fork** and create a pull request

### Bug Fix Process

1. **Create a bug fix branch** from `main`:
   ```bash
   git checkout main
   git pull upstream main
   git checkout -b fix/issue-description
   ```
2. **Reproduce the bug** to understand the issue
3. **Create or update tests** to verify the bug
4. **Implement the fix**
5. **Test the fix** thoroughly
6. **Commit and push** your changes

## 🚀 Code Style Guidelines

### C# Coding Standards

- Follow the [Microsoft C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- Use consistent naming conventions throughout the codebase
- Maintain code readability and proper formatting

### Naming Conventions

- **Classes**: PascalCase (`DualSenseController`, `ProfileManager`)
- **Methods**: PascalCase (`ConnectController`, `ApplyProfile`)
- **Properties**: PascalCase (`BatteryPercentage`, `IsConnected`)
- **Private Fields**: camelCase with underscore prefix (`_controller`, `_logger`)
- **Constants**: PascalCase (`DefaultTimeout`, `MaxRetries`)
- **Interfaces**: Prefix with `I` (`IControllerService`, `IProfileManager`)

### Code Structure

- Keep classes focused with single responsibility
- Use proper access modifiers (public, private, internal, protected)
- Group related functionality in appropriate namespaces
- Follow the existing folder structure and organization patterns

### XAML/Avalonia Guidelines

- Use consistent AXAML formatting and organization
- Follow Avalonia's data binding best practices
- Use appropriate styles and resources from the theme system
- Implement proper accessibility features

### Code Comments

- Use XML documentation comments for public APIs
- Add comments only when necessary to explain complex logic
- Keep comments up-to-date when modifying code
- Avoid redundant or obvious comments

### Async Programming

- Use `async/await` for asynchronous operations
- Follow proper async patterns and avoid `.Result` or `.Wait()`
- Consider cancellation tokens for long-running operations
- Handle exceptions appropriately in async methods

## 🧪 Testing Guidelines

### Unit Testing 

- Write unit tests for all business logic components
- Use appropriate testing frameworks (xUnit, NUnit, or MSTest)
- Follow AAA (Arrange-Act-Assert) pattern for test structure
- Test both positive and negative scenarios

### Integration Testing

- Test interactions between different components
- Verify proper controller communication
- Test profile loading and saving functionality

### Manual Testing

- Test all changes on actual hardware when possible
- Verify proper behavior with different DualSense controller versions
- Test with both USB and Bluetooth connections

## 📝 Documentation

### Code Documentation

- Document all public classes, methods, and properties using XML comments
- Include usage examples for complex APIs
- Document any important implementation details or limitations

### External Documentation

- Update README.md when adding major features
- Update this CONTRIBUTING.md document as needed
- Add or update feature documentation in the `/docs` folder

### Inline Comments

- Add comments to explain complex algorithms or business logic
- Document any workarounds or unusual implementation choices
- Keep comments concise and relevant

## 🐛 Issue Reporting

### Before Submitting an Issue

- Search existing issues to avoid duplicates
- Verify the issue exists on the latest `main` branch
- Test the issue on different hardware if possible

### Creating an Issue

When submitting an issue, please include:

**For Bug Reports:**

- Clear and descriptive title
- Detailed steps to reproduce the issue
- Expected behavior vs. actual behavior
- Operating system version and .NET runtime version
- DualSense controller firmware version
- Any relevant error messages or logs
- Screenshots if applicable

**For Feature Requests:**

- Clear and descriptive title
- Detailed description of the requested feature
- Use cases and scenarios where the feature would be useful
- Any relevant examples from similar projects

### Issue Labels

Issues are categorized using labels:

- `bug`: Something isn't working correctly
- `enhancement`: New feature or request
- `documentation`: Improvements or additions to documentation
- `good first issue`: Good for newcomers to the project
- `help wanted`: Extra attention is needed

## 🎯 Pull Request Process

### Creating a Pull Request

1. **Ensure your branch is up-to-date** with the main branch:
   ```bash
   git checkout main
   git pull upstream main
   git checkout your-branch
   git rebase main
   ```
2. **Push your changes** to your fork:
   ```bash
   git push origin your-branch
   ```
3. **Open a pull request** on GitHub with a descriptive title and detailed description
4. **Link related issues** using keywords like "Fixes #issue-number"

### Pull Request Requirements

- Follow all coding and style guidelines
- Include appropriate tests for new functionality
- Update documentation as needed
- Ensure all automated checks pass
- Provide a clear description of the changes
- Reference any related issues

### Code Review Process

- Pull requests require at least one approval before merging
- Reviewers may request changes or suggest improvements
- Address feedback promptly and professionally
- Re-request review after making changes

### Before Merging

- Ensure all tests pass
- Verify the fix or feature works as expected
- Update documentation if necessary
- Clean up any temporary changes made during development

## 🔧 Technical Guidelines

### Architecture Principles

- Follow the MVVM pattern for clean separation of concerns
- Use dependency injection for proper testability
- Implement proper error handling and logging
- Respect the existing architectural patterns

### Performance Considerations

- Optimize HID communication for responsiveness
- Implement proper resource disposal patterns
- Use async/await appropriately for I/O operations
- Ensure UI remains responsive during controller operations

### Security Considerations

- Validate all user input and configuration data
- Implement proper access controls for system-level operations
- Follow secure coding practices
- Protect against buffer overflows in HID communication

### Cross-Platform Considerations

- While currently Windows-focused, maintain code structure for potential cross-platform support
- Use platform-agnostic patterns where possible
- Properly handle platform-specific implementations

## 🤝 Community Guidelines

### Code of Conduct

- Be respectful and considerate in all interactions
- Provide constructive feedback during code reviews
- Be patient with newcomers to the project
- Maintain a welcoming and inclusive environment

### Communication

- Use clear and profess0ional language in all communications
- Be specific when discussing technical issues
- Provide context when requesting changes or features
- Respect different perspectives and approaches

### Recognition

- Contributors will be acknowledged in project documentation
- Significant contributions may be highlighted in release notes
- Maintainers will provide feedback and guidance for improvement

---

Thank you for contributing to DualSense Client! Your efforts help improve the application for all users. If you have any questions about contributing, feel free to reach out through GitHub issues or discussions.
