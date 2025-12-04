using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualSenseClient.Core.DualSense;
using DualSenseClient.Core.DualSense.Devices;
using DualSenseClient.Core.DualSense.Enums;
using DualSenseClient.Core.DualSense.Events;
using DualSenseClient.Core.Settings.Models;
using DualSenseClient.Core.DualSense.Actions;
using DualSenseClient.Core.Logging;

namespace DualSenseClient.ViewModels.Controls;

public enum ActionType
{
    ButtonCombination,
    SwipeGesture
    // Add actiontypes here
}

public partial class SpecialActionsViewModel : ControllerViewModelBase
{
    [ObservableProperty] private DualSenseProfileManager _profileManager;
    [ObservableProperty] private ObservableCollection<SpecialActionSettings> _specialActions = new ObservableCollection<SpecialActionSettings>();
    [ObservableProperty] private SpecialActionSettings? _selectedSpecialAction;
    [ObservableProperty] private string _newSpecialActionName = string.Empty;
    [ObservableProperty] private SpecialActionType _selectedSpecialActionType;
    [ObservableProperty] private BatteryIndicatorType? _selectedBatteryIndicatorType;
    [ObservableProperty] private ObservableCollection<ButtonType> _availableButtons = new ObservableCollection<ButtonType>();
    [ObservableProperty] private ObservableCollection<ButtonType> _selectedButtons = new ObservableCollection<ButtonType>();
    [ObservableProperty] private SwipeDirection _selectedSwipeDirection = SwipeDirection.Left;

    [ObservableProperty] private ActionType _selectedActionType = ActionType.ButtonCombination;

    public bool IsSwipeAction => SelectedActionType == ActionType.SwipeGesture;

    partial void OnSelectedActionTypeChanged(ActionType value)
    {
        OnPropertyChanged(nameof(IsSwipeAction));
    }

    public ObservableCollection<SpecialActionType> SpecialActionTypes { get; } = new ObservableCollection<SpecialActionType>();
    public ObservableCollection<BatteryIndicatorType> BatteryIndicatorTypes { get; } = new ObservableCollection<BatteryIndicatorType>();
    public ObservableCollection<SwipeDirection> SwipeDirections { get; } = new ObservableCollection<SwipeDirection>();
    public ObservableCollection<ActionType> ActionTypes { get; } = new ObservableCollection<ActionType>();

    public SpecialActionsViewModel(DualSenseController controller, ControllerInfo? controllerInfo, DualSenseProfileManager profileManager) : base(controller, controllerInfo)
    {
        Logger.Debug<SpecialActionsViewModel>($"Creating SpecialActionsViewModel for controller: {controllerInfo?.Name ?? "Unknown"}");
        ProfileManager = profileManager;

        // Subscribe to profile changes to update special actions when a profile changes
        ProfileManager.ProfileChanged += OnProfileChanged;

        InitializeSpecialActions();
        LoadAvailableButtons();
        LoadEnumCollections();
        LoadActionTypes();
        Logger.Debug<SpecialActionsViewModel>("SpecialActionsViewModel initialized successfully");
    }

    private void LoadActionTypes()
    {
        Logger.Debug<SpecialActionsViewModel>("Loading action types");

        ActionTypes.Clear();
        Array actionTypes = Enum.GetValues(typeof(ActionType));
        foreach (ActionType type in actionTypes)
        {
            ActionTypes.Add(type);
        }

        Logger.Debug<SpecialActionsViewModel>($"Loaded {ActionTypes.Count} action types");
    }

    private void OnProfileChanged(object? sender, ProfileChangedEventArgs e)
    {
        // Update the special actions when the profile changes for this controller
        if (_controllerInfo != null && e.ControllerId == _controllerInfo.Id)
        {
            Logger.Info<SpecialActionsViewModel>($"Profile changed for controller '{_controllerInfo.Name}', updating special actions");
            InitializeSpecialActions();
        }
    }

    private void InitializeSpecialActions()
    {
        Logger.Debug<SpecialActionsViewModel>("Initializing SpecialActions from controller profile");

        if (_controllerInfo != null)
        {
            ControllerProfile profile = ProfileManager.GetControllerProfile(_controllerInfo.Id);
            SpecialActions.Clear();

            foreach (SpecialActionSettings action in profile.SpecialActions)
            {
                SpecialActions.Add(action);
            }

            Logger.Debug<SpecialActionsViewModel>($"Loaded {SpecialActions.Count} SpecialAction(s)");
        }
    }

    private void LoadAvailableButtons()
    {
        Logger.Debug<SpecialActionsViewModel>("Loading available buttons");

        // Add all button types
        AvailableButtons.Clear();

        Array values = Enum.GetValues(typeof(ButtonType));
        foreach (ButtonType buttonType in values)
        {
            AvailableButtons.Add(buttonType);
        }

        Logger.Debug<SpecialActionsViewModel>($"Loaded {AvailableButtons.Count} available buttons");
    }

    private void LoadEnumCollections()
    {
        Logger.Debug<SpecialActionsViewModel>("Loading enum collections");

        // Populate SpecialActionType collection
        SpecialActionTypes.Clear();
        Array specialActionTypes = Enum.GetValues<SpecialActionType>();
        foreach (SpecialActionType type in specialActionTypes)
        {
            SpecialActionTypes.Add(type);
        }

        // Populate BatteryIndicatorType collection
        BatteryIndicatorTypes.Clear();
        Array batteryIndicatorTypes = Enum.GetValues<BatteryIndicatorType>();
        foreach (BatteryIndicatorType type in batteryIndicatorTypes)
        {
            BatteryIndicatorTypes.Add(type);
        }

        // Populate SwipeDirection collection
        SwipeDirections.Clear();
        Array swipeDirections = Enum.GetValues(typeof(SwipeDirection));
        foreach (SwipeDirection direction in swipeDirections)
        {
            SwipeDirections.Add(Enum.Parse<SwipeDirection>(direction.ToString()));
        }

        Logger.Debug<SpecialActionsViewModel>($"Loaded {SpecialActionTypes.Count} SpecialActionTypes, {BatteryIndicatorTypes.Count} BatteryIndicatorTypes, and {SwipeDirections.Count} SwipeDirections");
    }

    [RelayCommand]
    private void AddSpecialAction()
    {
        if (string.IsNullOrWhiteSpace(NewSpecialActionName))
        {
            Logger.Warning<SpecialActionsViewModel>("AddSpecialAction called with empty name");
            return;
        }

        Logger.Info<SpecialActionsViewModel>($"Creating new SpecialAction: {NewSpecialActionName}, Type: {SelectedSpecialActionType}, IsSwipeAction: {IsSwipeAction}");

        SpecialActionSettings newAction = new SpecialActionSettings
        {
            Id = Guid.NewGuid().ToString(),
            Name = NewSpecialActionName,
            Type = SelectedSpecialActionType,
            Settings = new ActionSettings()
        };

        if (SelectedActionType == ActionType.SwipeGesture)
        {
            // Create swipe action
            newAction.SwipeAction = new SwipeActionCombination
            {
                Direction = Enum.Parse<SwipeDirection>(SelectedSwipeDirection.ToString())
            };
            // Clear button combination for swipe actions
            newAction.Combination = new ButtonCombination();
        }
        else
        {
            // Create button combination action
            newAction.Combination = new ButtonCombination(SelectedButtons.ToList());
            // Clear swipe action for button combination actions
            newAction.SwipeAction = null;
        }

        if (SelectedSpecialActionType == SpecialActionType.BatteryIndicator)
        {
            newAction.Settings.BatteryIndicatorType = SelectedBatteryIndicatorType;
        }

        SpecialActions.Add(newAction);

        // Also update the profile in the manager
        if (_controllerInfo != null)
        {
            ControllerProfile profile = ProfileManager.GetControllerProfile(_controllerInfo.Id);
            profile.SpecialActions.Add(newAction);
            ProfileManager.SaveProfile(profile);
        }

        NewSpecialActionName = string.Empty;
        SelectedButtons.Clear();

        Logger.Info<SpecialActionsViewModel>("SpecialAction created successfully");
    }

    [RelayCommand]
    private void UpdateSpecialAction()
    {
        if (SelectedSpecialAction == null)
        {
            Logger.Warning<SpecialActionsViewModel>("UpdateSpecialAction called with no action selected");
            return;
        }

        Logger.Info<SpecialActionsViewModel>($"Updating SpecialAction: {SelectedSpecialAction.Name}, Type: {SelectedSpecialActionType}, IsSwipeAction: {IsSwipeAction}");

        SelectedSpecialAction.Name = NewSpecialActionName;
        SelectedSpecialAction.Type = SelectedSpecialActionType;

        if (SelectedActionType == ActionType.SwipeGesture)
        {
            // Update as swipe action
            if (SelectedSpecialAction.SwipeAction == null)
            {
                SelectedSpecialAction.SwipeAction = new SwipeActionCombination();
            }
            SelectedSpecialAction.SwipeAction.Direction = (Core.DualSense.Actions.SwipeDirection)Enum.Parse(typeof(Core.DualSense.Actions.SwipeDirection), SelectedSwipeDirection.ToString());
            // Clear button combination for swipe actions
            SelectedSpecialAction.Combination = new ButtonCombination();
        }
        else
        {
            // Update as button combination action
            SelectedSpecialAction.Combination = new ButtonCombination(SelectedButtons.ToList());
            // Clear swipe action for button combination actions
            SelectedSpecialAction.SwipeAction = null;
        }

        if (SelectedSpecialActionType == SpecialActionType.BatteryIndicator)
        {
            SelectedSpecialAction.Settings.BatteryIndicatorType = SelectedBatteryIndicatorType;
        }

        // Update the profile in the manager
        if (_controllerInfo != null)
        {
            ControllerProfile profile = ProfileManager.GetControllerProfile(_controllerInfo.Id);
            int index = profile.SpecialActions.FindIndex(sa => sa.Id == SelectedSpecialAction.Id);
            if (index >= 0)
            {
                profile.SpecialActions[index] = SelectedSpecialAction;
            }
            else
            {
                // If not found in list, add it (for safety)
                profile.SpecialActions.Add(SelectedSpecialAction);
            }
            ProfileManager.SaveProfile(profile);
        }

        Logger.Info<SpecialActionsViewModel>("SpecialAction updated successfully");
    }

    [RelayCommand]
    private void DeleteSpecialAction()
    {
        if (SelectedSpecialAction == null)
        {
            Logger.Warning<SpecialActionsViewModel>("DeleteSpecialAction called with no action selected");
            return;
        }

        string actionName = SelectedSpecialAction.Name;
        string actionId = SelectedSpecialAction.Id;

        Logger.Info<SpecialActionsViewModel>($"Deleting SpecialAction: {actionName} (ID: {actionId})");

        SpecialActions.Remove(SelectedSpecialAction);

        // Also remove from the profile in the manager
        if (_controllerInfo != null)
        {
            ControllerProfile profile = ProfileManager.GetControllerProfile(_controllerInfo.Id);
            profile.SpecialActions.RemoveAll(sa => sa.Id == actionId);
            ProfileManager.SaveProfile(profile);
        }

        SelectedSpecialAction = null;
        NewSpecialActionName = string.Empty;
        SelectedButtons.Clear();

        Logger.Info<SpecialActionsViewModel>($"SpecialAction '{actionName}' deleted successfully");
    }

    [RelayCommand]
    private void ClearSelectedButtons()
    {
        SelectedButtons.Clear();
        Logger.Debug<SpecialActionsViewModel>("Selected buttons cleared");
    }

    [RelayCommand]
    private void SelectButton(ButtonType button)
    {
        if (!SelectedButtons.Contains(button))
        {
            SelectedButtons.Add(button);
            Logger.Trace<SpecialActionsViewModel>($"Button {button} added to selection");
        }
    }

    [RelayCommand]
    private void RemoveButton(ButtonType button)
    {
        SelectedButtons.Remove(button);
        Logger.Trace<SpecialActionsViewModel>($"Button {button} removed from selection");
    }

    partial void OnSelectedSpecialActionChanged(SpecialActionSettings? value)
    {
        if (value != null)
        {
            Logger.Debug<SpecialActionsViewModel>($"Selected SpecialAction changed to: {value.Name} (ID: {value.Id})");
            LoadSpecialActionIntoControls(value);
        }
        else
        {
            Logger.Debug<SpecialActionsViewModel>("Selected SpecialAction cleared");
        }
    }

    private void LoadSpecialActionIntoControls(SpecialActionSettings action)
    {
        Logger.Debug<SpecialActionsViewModel>($"Loading SpecialAction '{action.Name}' into controls");

        NewSpecialActionName = action.Name;
        SelectedSpecialActionType = action.Type;
        SelectedActionType = action.IsSwipeAction ? ActionType.SwipeGesture : ActionType.ButtonCombination;

        // Load button combination or swipe action
        if (action.IsSwipeAction)
        {
            SelectedSwipeDirection = action.SwipeAction != null
                ? Enum.Parse<SwipeDirection>(action.SwipeAction.Direction.ToString())
                : SwipeDirection.Left;

            SelectedButtons.Clear(); // Clear buttons for swipe actions
        }
        else
        {
            SelectedButtons.Clear();
            foreach (ButtonType button in action.Combination.Buttons)
            {
                SelectedButtons.Add(button);
            }

            SelectedSwipeDirection = SwipeDirection.Left; // Reset for button actions
        }

        SelectedBatteryIndicatorType = action.Type == SpecialActionType.BatteryIndicator ? action.Settings.BatteryIndicatorType : null;

        Logger.Debug<SpecialActionsViewModel>("SpecialAction loaded into controls successfully");
    }

    public override void Dispose()
    {
        Logger.Debug<SpecialActionsViewModel>($"Disposing SpecialActionsViewModel for controller: {_controllerInfo?.Name ?? "Unknown"}");

        // Unsubscribe from profile changes to prevent memory leaks
        if (ProfileManager != null)
        {
            ProfileManager.ProfileChanged -= OnProfileChanged;
        }

        base.Dispose();
    }
}