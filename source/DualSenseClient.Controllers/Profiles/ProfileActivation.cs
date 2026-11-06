using DualSenseClient.Controllers.Devices;
using DualSenseClient.Settings;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Controllers.Profiles;

/// <summary>
/// Event payload for a profile becoming active on a controller.
/// </summary>
public sealed class ActiveProfileChangedEventArgs : EventArgs
{
    /// <summary>
    /// Creates the event payload.
    /// </summary>
    /// <param name="device">The controller whose active profile changed.</param>
    /// <param name="profile">The resolved profile, or <c>null</c> when none is available.</param>
    public ActiveProfileChangedEventArgs(DualSenseDevice device, Profile? profile)
    {
        Device = device;
        Profile = profile;
    }

    /// <summary>
    /// The controller whose active profile changed.
    /// </summary>
    public DualSenseDevice Device { get; }

    /// <summary>
    /// The resolved profile, or <c>null</c> when none is available.
    /// </summary>
    public Profile? Profile { get; }
}

/// <summary>
/// Owns resolving, binding, and applying the Profile a Controller should use.
/// </summary>
public interface IProfileActivation : IDisposable
{
    /// <summary>
    /// Resolves the profile a controller should use: its bound profile, or the default.
    /// </summary>
    /// <param name="device">The controller.</param>
    /// <returns>The active profile, or <c>null</c> when none is available.</returns>
    Profile? GetActiveProfile(DualSenseDevice device);

    /// <summary>
    /// Applies the controller's active profile to its light outputs.
    /// </summary>
    /// <param name="device">The controller.</param>
    void Activate(DualSenseDevice device);

    /// <summary>
    /// Applies a specific profile to a controller without changing its binding.
    /// </summary>
    /// <param name="device">The controller.</param>
    /// <param name="profile">The profile to apply.</param>
    void Activate(DualSenseDevice device, Profile profile);

    /// <summary>
    /// Binds a profile to a controller and applies it.
    /// </summary>
    /// <param name="device">The controller.</param>
    /// <param name="profile">The profile to bind.</param>
    void Bind(DualSenseDevice device, Profile profile);

    /// <summary>
    /// Renames a profile and updates every controller binding in the same transaction.
    /// </summary>
    /// <param name="oldName">The current profile name.</param>
    /// <param name="newName">The new profile name.</param>
    /// <returns><c>true</c> when the rename was applied.</returns>
    bool RenameProfile(string oldName, string newName);

    /// <summary>
    /// Deletes a profile and clears every binding referencing it.
    /// </summary>
    /// <param name="name">The profile name.</param>
    /// <returns><c>true</c> when the profile was deleted.</returns>
    bool DeleteProfile(string name);

    /// <summary>
    /// Raised after a controller's active profile is applied.
    /// </summary>
    event EventHandler<ActiveProfileChangedEventArgs>? ActiveProfileChanged;
}

/// <summary>
/// Default <see cref="IProfileActivation"/> implementation.
/// </summary>
public sealed class ProfileActivation : IProfileActivation
{
    private readonly ProfileService _profiles;
    private readonly ControllerInfoService _controllers;
    private readonly IControllerTracker _tracker;
    private bool _disposed;

    /// <summary>
    /// Creates the profile activation module.
    /// </summary>
    /// <param name="profiles">The saved profiles.</param>
    /// <param name="controllers">The controller bindings.</param>
    /// <param name="tracker">The tracked controllers.</param>
    public ProfileActivation(ProfileService profiles, ControllerInfoService controllers, IControllerTracker tracker)
    {
        _profiles = profiles;
        _controllers = controllers;
        _tracker = tracker;
        _profiles.ProfilesChanged += OnProfilesChanged;
    }

    /// <inheritdoc/>
    public event EventHandler<ActiveProfileChangedEventArgs>? ActiveProfileChanged;

    /// <inheritdoc/>
    public Profile? GetActiveProfile(DualSenseDevice device)
    {
        string? profileName = _controllers.GetBoundProfileName(device.Key) ?? ProfileService.DefaultProfileName;
        return _profiles.GetProfile(profileName);
    }

    /// <inheritdoc/>
    public void Activate(DualSenseDevice device)
    {
        Profile? profile = GetActiveProfile(device);
        if (profile is not null)
        {
            Activate(device, profile);
        }
    }

    /// <inheritdoc/>
    public void Activate(DualSenseDevice device, Profile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        device.ApplyProfile(profile);
        ActiveProfileChanged?.Invoke(this, new ActiveProfileChangedEventArgs(device, profile));
    }

    /// <inheritdoc/>
    public void Bind(DualSenseDevice device, Profile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _controllers.SetControllerProfile(device.Key, profile.Name);
        Activate(device, profile);
    }

    /// <inheritdoc/>
    public bool RenameProfile(string oldName, string newName)
    {
        if (!_profiles.RenameProfileInMemory(oldName, newName))
        {
            return false;
        }

        // Update the bindings before saving: ProfilesChanged re-activates every tracked
        // controller, so it must see the renamed profile already bound.
        _controllers.UpdateProfileName(oldName, newName.Trim());
        _profiles.Save();
        return true;
    }

    /// <inheritdoc/>
    public bool DeleteProfile(string name)
    {
        // Clear the bindings first: DeleteProfile saves and raises ProfilesChanged, and a
        // controller must not briefly resolve a profile that no longer exists.
        _controllers.RemoveProfileReferences(name);
        return _profiles.DeleteProfile(name);
    }

    private void OnProfilesChanged(object? sender, EventArgs e)
    {
        foreach (DualSenseDevice device in _tracker.Controllers.OfType<DualSenseDevice>())
        {
            Activate(device);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _profiles.ProfilesChanged -= OnProfilesChanged;
    }
}