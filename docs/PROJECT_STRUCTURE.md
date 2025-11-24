# 🎮 DualSense Client Project Structure

This document provides a comprehensive overview of the DualSense Client project's architecture, organization, and practical workflows for development.

## 📋 Table of Contents

- [📖 Overview](#-overview)
- [🏗️ Solution Structure](#️-solution-structure)
- [🎨 View Layer](#-view-layer)
  - [Avalonia UI Architecture](#avalonia-ui-architecture)
  - [Data Binding](#data-binding)
  - [Navigation System](#navigation-system)
- [🔄 ViewModel Layer](#-viewmodel-layer)
  - [PropertyChanged Events](#propertychanged-events)
  - [Messaging System](#messaging-system)
- [🔧 Model Layer](#-model-layer)
  - [Services Architecture](#services-architecture)
  - [DualSense Controller Management](#dualsense-controller-management)
  - [Data Models and Configuration](#data-models-and-configuration)
- [🛠️ Technology Stack](#️-technology-stack)
- [📋 Development Guidelines](#-development-guidelines)

## 📖 Overview

DualSense Client is a comprehensive management tool for the PlayStation 5 DualSense Controller built using modern .NET 9.0 and Avalonia UI framework. The application follows the Model-View-ViewModel ([MVVM](https://docs.avaloniaui.net/docs/concepts/the-mvvm-pattern/)) design pattern with dependency injection for maintainable and testable code.

The architecture is built around clean separation of concerns:
- **View Layer**: Avalonia XAML-based UI components and custom controls
- **ViewModel Layer**: Presentation logic and data binding with MVVM
- **Model Layer**: Business logic, services, and controller communication

## 🏗️ Solution Structure

The solution contains two main projects organized for clear separation of concerns:

```
DualSenseClient.sln
├── DualSenseClient/              # Main Avalonia application (UI layer)
└── DualSenseClient.Core/         # Core business logic library
```

### Main Project: DualSenseClient
The primary Avalonia application containing all user interface components, platform-specific code, and XAML resources.

### Core Library: DualSenseClient.Core
Contains the business logic, controller services, DualSense communication protocols, and application settings that can be shared across different platforms.

## 🎨 View Layer

The View layer is contained in the DualSenseClient project. This project consists primarily of AXAML files and custom controls that define the user interface. The application follows Avalonia's recommended practices for XAML-based UI development.

### Main UI Structure

The application uses a tabbed interface with `MainWindow.axaml` as the root container. The main window utilizes a tab control for the navigation system and houses content frames for different application functions:

```
MainWindow.axaml (Root Container)
├── TabControl (Navigation System)
│   ├── HomePage.axaml (Controller Information)
│   ├── DevicesPage.axaml (Device Management)
│   ├── ProfilePage.axaml (Configuration Profiles)
│   ├── MonitorPage.axaml (Real-time Monitoring)
│   ├── DebugPage.axaml (Advanced Debugging, only for testing purposes)
│   └── SettingsPage.axaml (Application Settings)
└── TrayIcon (System Tray Integration)
```

### Key Page Components

The application organizes its content into several main page categories:

- **`HomePage.axaml`**: Controller information and status overview
- **`DevicesPage.axaml`**: Controller selection and device management
- **`ProfilePage.axaml`**: Profile creation and controller customization
- **`MonitorPage.axaml`**: Real-time input monitoring and diagnostics
- **`DebugPage.axaml`**: Advanced debugging and testing tools
- **`SettingsPage.axaml`**: Application configuration and preferences

### Custom Controls Architecture

DualSense Client implements numerous custom controls for specialized functionality:

#### Core Controller Controls
- **`ControllerInformation.axaml`**: Controller status and information display
- **`ControllerSelector.axaml`**: Device selection and connection management
- **`ControllerMonitor.axaml`**: Real-time input visualization
- **`ProfileSelector.axaml`**: Profile selection and management interface

#### Configuration Controls  
- **`ControllerLights.axaml`**: Lightbar and LED configuration controls
- **`SpecialActions.axaml`**: Custom button combination settings
- **`VirtualControllerSettings.axaml`**: Virtual controller emulation controls

#### Advanced Controls
- **`DebugMonitor.axaml`**: Advanced debugging interface

### Avalonia UI Architecture

DualSense Client uses [Avalonia UI](https://avaloniaui.net/) for cross-platform XAML development. The UI framework provides native performance and system integration across different platforms, although the current implementation is Windows-focused.

#### Fluent Design Implementation
The application implements [Fluent Avalonia](https://github.com/amwx/FluentAvalonia) to provide a modern Microsoft Fluent Design experience that integrates well with Windows 11 and modern Windows systems.

#### AXAML Data Binding

DualSense Client uses [data binding](https://docs.avaloniaui.net/docs/guides/data-binding/how-to-bind-to-a-command-without-reactiveui) extensively to create dynamic, responsive UI components. The application primarily uses property binding to connect UI elements to ViewModel properties.

Example of AXAML binding usage:
```xml
<TextBlock Text="{Binding SelectedController.BatteryPercentage, StringFormat='Battery: {0}%'}" />
<ToggleSwitch IsChecked="{Binding EnableEmulation}" />
<Slider Value="{Binding SelectedController.LightbarColor.R, Minimum=0, Maximum=255}" />
```

### Data Binding

The binding system enables automatic UI updates when controller state changes, providing smooth user experiences without manual UI manipulation code. The application uses both OneWay and TwoWay bindings depending on the scenario.

### Navigation System

Navigation in DualSense Client is handled through Avalonia's built-in navigation patterns with ViewModels that correspond to each page. The navigation system is configured in `App.axaml.cs` and enables loose coupling between the ViewModel and View layers.

## 🔄 ViewModel Layer

The ViewModel layer is contained in the DualSenseClient project and serves as the intermediary between the UI components and business logic. ViewModels provide data sources for UI binding and encapsulate presentation logic while remaining independent of specific UI implementations.

### Key ViewModel Architecture

#### Primary Application ViewModels
- **`MainViewModel.cs`**: Root application state and coordination
- **`MainWindowViewModel.cs`**: Main window state and lifecycle management
- **`TrayIconViewModel.cs`**: System tray integration and context menus
- **`SettingsPageViewModel.cs`**: Application configuration management

#### Controller Management ViewModels
- **`HomePageViewModel.cs`**: Controller information and status display
- **`DevicesPageViewModel.cs`**: Device selection and management
- **`MonitorPageViewModel.cs`**: Real-time monitoring state
- **`DebugPageViewModel.cs`**: Debugging interface state

#### Profile and Configuration ViewModels
- **`ProfilePageViewModel.cs`**: Profile management and controller customization
- **`ControllerProfileViewModel.cs`**: Individual profile settings and state
- **`VirtualControllerSettingsViewModel.cs`**: Virtual controller configuration
- **`SpecialActionsViewModel.cs`**: Custom action configuration

### PropertyChanged Events

ViewModels implement property change notification through the [CommunityToolkit.Mvvm](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/) framework. Most ViewModels inherit from `ObservableObject` or use source generators to implement [INotifyPropertyChanged](https://learn.microsoft.com/en-us/dotnet/api/system.componentmodel.inotifypropertychanged) automatically:

```csharp
[ObservableProperty]
private bool _enableEmulation;

[ObservableProperty]  
private int _batteryPercentage;
```

This enables automatic UI updates when ViewModel properties change, maintaining synchronization between the data layer and user interface.

### Messaging System

DualSense Client uses the [CommunityToolkit.Mvvm messaging system](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/messenger) for decoupled communication between components. This allows ViewModels, Services, and other components to communicate without direct references.

#### Core Message Types

**Controller State Messages**
- **`ControllerConnectedMessage.cs`**: Controller connection notifications
- **`ControllerDisconnectedMessage.cs`**: Controller disconnection notifications
- **`ControllerStateChangedMessage.cs`**: Controller state updates

**Profile Management Messages**
- **`ProfileAppliedMessage.cs`**: Profile application notifications
- **`ProfileChangedMessage.cs`**: Profile modification notifications
- **`ProfileDeletedMessage.cs`**: Profile deletion notifications

**System Integration Messages**
- **`ApplicationStateChangedMessage.cs`**: Application lifecycle events
- **`ErrorMessage.cs`**: Error reporting and handling
- **`NotificationMessage.cs`**: User notification display

## 🔧 Model Layer

The Model layer contains the core business logic and is primarily located in the DualSenseClient.Core project. This layer consists of services, DualSense communication protocols, and data models that provide the foundation for the application functionality.

### Services Architecture

DualSense Client implements a comprehensive service-oriented architecture using [Microsoft.Extensions.DependencyInjection](https://docs.microsoft.com/en-us/dotnet/core/extensions/dependency-injection) for service registration and resolution. Services are registered in `Program.cs` and injected throughout the application.

#### Core Business Services

**Controller Management Services**
- **`DualSenseProfileManager.cs`**: Profile management and controller configuration
- **`ControllerEmulationService.cs`**: Virtual controller emulation using ViGEm
- **`HidHideService.cs`**: HID device hiding for application compatibility

**Communication Services**
- **`HidDeviceService.cs`**: Low-level HID communication with controllers
- **`BluetoothService.cs`**: Bluetooth connection management (if implemented)

**System Integration Services**
- **`ISettingsManager.cs`**: Application configuration persistence
- **`IViGEmBusService.cs`**: ViGEmBus driver status monitoring
- **`IHidHideService.cs`**: HidHide driver service management

#### UI-Specific Services (DualSenseClient Project)
- **`SelectedControllerService.cs`**: Active controller selection and management
- **`TrayIconService.cs`**: System tray icon and context menu management

### DualSense Controller Management

The controller management system handles communication with Sony DualSense controllers through HID protocols:

#### Core Controller Components
- **`DualSenseController.cs`**: Main controller interface and state management
- **`InputState.cs`**: Controller input data structure
- **`MotionState.cs`**: Motion sensor data
- **`TouchpadState.cs`**: Touchpad input data

#### Communication Protocols
- **`HidCommunication.cs`**: Low-level HID communication layer
- **`DualSenseReport.cs`**: Controller input report parsing
- **`FeatureReport.cs`**: Controller feature request handling

The controller management system provides a clean interface for the ViewModel layer while abstracting the complexities of the underlying HID communication protocols.

### Data Models and Configuration

#### Controller State Models
- **`ControllerInfo.cs`**: Controller identification and connection information
- **`ConnectionStatus.cs`**: Connection status and pairing information
- **`BatteryStatus.cs`**: Battery level and charging state

#### Configuration Models  
- **`ControllerProfile.cs`**: Controller configuration profile structure
- **`LightbarColor.cs`**: RGB color definition for lightbar
- **`VirtualControllerSettings.cs`**: Virtual controller configuration
- **`SpecialActionsSettings.cs`**: Custom action configuration

#### Application Settings Models
- **`ApplicationSettings.cs`**: General application configuration
- **`ProfileSettings.cs`**: Profile management settings
- **`EmulationSettings.cs`**: Virtual controller settings

## 🛠️ Technology Stack

- **Avalonia UI**: Cross-platform XAML framework providing native performance and system integration
- **C#**: Primary programming language with modern features
- **AXAML**: Declarative markup for user interface definition
- **HidSharp**: Cross-platform HID device access for controller communication
- **CommunityToolkit.Mvvm**: Modern MVVM framework with source generators and messaging
- **FluentAvaloniaUI**: Fluent Design System implementation for Avalonia

### Development and Build Tools  
- **JetBrains Rider**/**Visual Studio**: Primary integrated development environment
- **.NET 9.0**: Target runtime framework
- **MSBuild**: Build system and project management
- **NuGet**: Package dependency management

### Key Dependencies

#### UI Framework
- [Avalonia](https://github.com/AvaloniaUI/Avalonia) - Cross-platform XAML framework
- [FluentAvaloniaUI](https://github.com/amwx/FluentAvalonia) - Fluent Design implementation
- [FluentIcons.Avalonia](https://github.com/davidxuang/FluentIcons) - Fluent icon library

#### Controller Emulation and HID
- [HidSharp](https://github.com/SeekHisKingdom/HIDSharp) - HID device communication
- [Nefarius.ViGEmBus](https://github.com/nefarius/ViGEmBus) - Virtual gamepad emulation
- [Nefarius.HidHide](https://github.com/nefarius/HidHide) - HID device filtering

#### Dependency Injection and MVVM
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet/) - MVVM helpers and source generators
- [Microsoft.Extensions.DependencyInjection](https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection/) - Service container and dependency injection

#### Logging and Diagnostics
- [NLog](https://github.com/NLog/NLog) - Flexible and high-performance logging library

## 📋 Development Guidelines

### Code Organization
Follow [C# coding conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions) and [Avalonia](https://docs.avaloniaui.net/) best practices throughout the codebase.

### Naming Conventions
Adhere to [.NET naming guidelines](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/naming-guidelines):

- **PascalCase**: Classes, methods, properties, public fields, enums
- **camelCase**: Local variables, parameters, private fields
- **Interfaces**: Prefixed with `I` (e.g., `IDualSenseController`)
- **Private fields**: Prefixed with `_` (e.g., `_controllerService`)
- **XAML resources**: Descriptive, consistent naming patterns

### File Organization
- One class per file
- Nested namespaces match folder structure
- Partial classes for AXAML code-behind

### Performance Considerations

#### Asynchronous Programming
- Use `async/await` for HID operations following [async best practices](https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/async-scenarios)
- Proper `ConfigureAwait(false)` usage in library code
- Task-based operations for device communication

#### Memory Management
- Follow [.NET memory management guidelines](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/)
- Use weak references for event handlers where appropriate

#### UI Performance
- Follow [Avalonia performance best practices](https://docs.avaloniaui.net/docs/performance)
- Implement proper data virtualization for large collections
- Process heavy operations on background threads to prevent UI freezing

### Controller Communication Best Practices

#### HID Communication
- Use proper error handling for USB/Bluetooth disconnections
- Implement reconnection logic for stable controller management
- Follow Sony's DualSense communication protocols

#### Virtual Controller Emulation
- Properly handle ViGEmBus driver installation and status checks
- Manage virtual controller lifecycle with proper disposal
- Handle trigger threshold and rumble configuration appropriately

### Build Configuration and Deployment

#### Supported Platforms
- **Windows x64**: Primary target platform

#### Build Modes
- **Debug**: Development builds with detailed logging
- **Release**: Optimized production builds
- **NuGet**: Package creation for distribution

### External Resources and Documentation

#### Documentation
- [Avalonia UI Documentation](https://docs.avaloniaui.net/)
- [C# Programming Reference](https://learn.microsoft.com/en-us/dotnet/csharp/)
- [MVVM Toolkit Documentation](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/)
- [HID Device Programming](https://docs.microsoft.com/en-us/windows-hardware/drivers/hid/)

#### Best Practices and Guidelines
- [Dependency Injection in .NET](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection)
- [Async Programming Patterns](https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/async-scenarios)
- [Sony DualSense Controller Documentation](https://controllers.fandom.com/wiki/Sony_DualSense)

This structure enables maintainable, scalable development while supporting the advanced features of a modern DualSense controller management application.