using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using GagSpeak.Services.Mediator;
using GagSpeak.UpdateMonitoring;

namespace GagSpeak.Interop.Ipc;

public sealed class IpcCallerMoodles : IIpcCaller
{
    // TERMINOLOGY:
    // StatusManager == The manager handling the current active statuses on you.
    // Status == The invidual "Moodle" in your Moodles tab under the Moodles UI.
    // Preset == The collection of Statuses to apply at once. Stored in a preset.
    
    // Remember, all these are called only when OUR client changes. Not other pairs.
    private readonly ICallGateSubscriber<int> _moodlesApiVersion;
    private readonly ICallGateSubscriber<object> _moodlesReady;
    
    private readonly ICallGateSubscriber<IPlayerCharacter, object> _onStatusManagerModified;
    private readonly ICallGateSubscriber<Guid, object> _onStatusSettingsModified;
    private readonly ICallGateSubscriber<Guid, object> _onPresetModified;

    // API Getter Functions
    private readonly ICallGateSubscriber<Guid, MoodlesStatusInfo> _getMoodleInfo;
    private readonly ICallGateSubscriber<List<MoodlesStatusInfo>> _getMoodlesInfo;
    private readonly ICallGateSubscriber<Guid, (Guid, List<Guid>)> _getPresetInfo;
    private readonly ICallGateSubscriber<List<(Guid, List<Guid>)>> _getPresetsInfo;
    private readonly ICallGateSubscriber<string, string> _moodlesGetStatus;

    // API Enactor Functions
    private readonly ICallGateSubscriber<Guid, string, object> _applyStatusByGuid;
    private readonly ICallGateSubscriber<Guid, string, object> _applyPresetByGuid;
    private readonly ICallGateSubscriber<string, string, List<MoodlesStatusInfo>, object> _applyStatusesFromPair;
    private readonly ICallGateSubscriber<List<Guid>, string, object> _removeStatusByGuids;

    private readonly ICallGateSubscriber<string, string, object> _setStatusManager;
    private readonly ICallGateSubscriber<string, object> _clearStatusesFromManager;


    private readonly ILogger<IpcCallerMoodles> _logger;
    private readonly OnFrameworkService _frameworkUtil;
    private readonly GagspeakMediator _gagspeakMediator;
    private bool _shownMoodlesUnavailable = false; // safety net to prevent notification spam.

    public IpcCallerMoodles(ILogger<IpcCallerMoodles> logger, IDalamudPluginInterface pi,
        OnFrameworkService frameworkUtil, GagspeakMediator gagspeakMediator)
    {
        _logger = logger;
        _frameworkUtil = frameworkUtil;
        _gagspeakMediator = gagspeakMediator;

        _moodlesApiVersion = pi.GetIpcSubscriber<int>("Moodles.Version");
        _moodlesReady = pi.GetIpcSubscriber<object>("Moodles.Ready");

        // TODO: Change nint to name@world later.
        // API Getter Functions
        _getMoodleInfo = pi.GetIpcSubscriber<Guid, MoodlesStatusInfo>("Moodles.GetRegisteredMoodleInfo");
        _getMoodlesInfo = pi.GetIpcSubscriber<List<MoodlesStatusInfo>>("Moodles.GetRegisteredMoodlesInfo");
        _getPresetInfo = pi.GetIpcSubscriber<Guid, (Guid, List<Guid>)>("Moodles.GetRegisteredPresetInfo");
        _getPresetsInfo = pi.GetIpcSubscriber<List<(Guid, List<Guid>)>>("Moodles.GetRegisteredPresetsInfo");
        _moodlesGetStatus = pi.GetIpcSubscriber<string, string>("Moodles.GetStatusManagerByName");

        // API Enactor Functions
        _applyStatusByGuid = pi.GetIpcSubscriber<Guid, string, object>("Moodles.AddOrUpdateMoodleByGUIDByName");
        _applyPresetByGuid = pi.GetIpcSubscriber<Guid, string, object>("Moodles.ApplyPresetByGUIDByName");
        _applyStatusesFromPair = pi.GetIpcSubscriber<string, string, List<MoodlesStatusInfo>, object>("Moodles.ApplyStatusesFromGSpeakPair");
        _removeStatusByGuids = pi.GetIpcSubscriber<List<Guid>, string, object>("Moodles.RemoveMoodlesByGUIDByName");

        _setStatusManager = pi.GetIpcSubscriber<string, string, object>("Moodles.SetStatusManagerByName");
        _clearStatusesFromManager = pi.GetIpcSubscriber<string, object>("Moodles.ClearStatusManagerByName");


        // API Action Events:
        _onStatusManagerModified = pi.GetIpcSubscriber<IPlayerCharacter, object>("Moodles.StatusManagerModified");
        _onStatusSettingsModified = pi.GetIpcSubscriber<Guid, object>("Moodles.StatusModified");
        _onPresetModified = pi.GetIpcSubscriber<Guid, object>("Moodles.PresetModified");

        _moodlesReady.Subscribe(OnMoodlesReady); // fires whenever our client's moodles are ready.
        _onStatusManagerModified.Subscribe(OnStatusManagerModified); // fires whenever our client's status manager changes.
        _onStatusSettingsModified.Subscribe(OnStatusModified); // fires whenever our client's changes the settings of a Moodle.
        _onPresetModified.Subscribe(OnPresetModified); // fires whenever our client's changes the settings of a Moodle preset.

        CheckAPI(); // check to see if we have a valid API
    }

    private void OnMoodlesReady()
        => _gagspeakMediator.Publish(new MoodlesReady());

    /// <summary> This method is called when the moodles change </summary>
    /// <param name="character">The character that had modified moodles.</param>
    private void OnStatusManagerModified(IPlayerCharacter character) => 
        _gagspeakMediator.Publish(new MoodlesStatusManagerChangedMessage(character.Address));

    /// <summary> This method is called when the moodles change </summary>
    private void OnStatusModified(Guid guid) 
        => _gagspeakMediator.Publish(new MoodlesStatusModified(guid));

    /// <summary> This method is called when the moodles change </summary>
    private void OnPresetModified(Guid guid)
        => _gagspeakMediator.Publish(new MoodlesPresetModified(guid));


    /// <summary> this boolean determines if the moodles API is available or not.</summary>
    public bool APIAvailable { get; private set; } = false;

    /// <summary> This method checks if the API is available </summary>
    public void CheckAPI()
    {
        try
        {
            APIAvailable = _moodlesApiVersion.InvokeFunc() >= 1;
        }
        catch
        {
            APIAvailable = false;
        }
    }

    /// <summary> This method disposes of the IPC caller moodles</summary>
    public void Dispose()
    {
        _moodlesReady.Unsubscribe(OnMoodlesReady);
        _onStatusManagerModified.Unsubscribe(OnStatusManagerModified);
        _onStatusSettingsModified.Unsubscribe(OnStatusModified);
        _onPresetModified.Unsubscribe(OnPresetModified);
    }

    /// <summary> This method gets the moodles info for a provided GUID from the client. </summary>
    public async Task<MoodlesStatusInfo?> GetMoodleInfoAsync(Guid guid)
    {
        if (!APIAvailable) return null; // return if the API isnt available

        try // otherwise, try and return an awaited task that gets the moodles info for a provided GUID
        {
            return await _frameworkUtil.RunOnFrameworkThread(() => _getMoodleInfo.InvokeFunc(guid)).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // log it if we failed.
            _logger.LogWarning(e, "Could not Get Moodles Info");
            return null;
        }
    }

    /// <summary> This method gets the list of all our clients Moodles Info </summary>
    public async Task<List<MoodlesStatusInfo>?> GetMoodlesInfoAsync()
    {
        if (!APIAvailable) return null; // return if the API isnt available

        try // otherwise, try and return an awaited task that gets the list of all our clients Moodles Info
        {
            return await _frameworkUtil.RunOnFrameworkThread(() => _getMoodlesInfo.InvokeFunc()).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // log it if we failed.
            _logger.LogWarning(e, "Could not Get Moodles Info");
            return null;
        }
    }

    /// <summary> This method gets the preset info for a provided GUID from the client. </summary>
    public async Task<(Guid, List<Guid>)?> GetPresetInfoAsync(Guid guid)
    {
        if (!APIAvailable) return null; // return if the API isnt available

        try // otherwise, try and return an awaited task that gets the preset info for a provided GUID
        {
            return await _frameworkUtil.RunOnFrameworkThread(() => _getPresetInfo.InvokeFunc(guid)).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // log it if we failed.
            _logger.LogWarning(e, "Could not Get Moodles Preset Info");
            return null;
        }
    }

    /// <summary> This method gets the list of all our clients Presets Info </summary>
    public async Task<List<(Guid, List<Guid>)>?> GetPresetsInfoAsync()
    {
        if (!APIAvailable) return null; // return if the API isnt available

        try // otherwise, try and return an awaited task that gets the list of all our clients Presets Info
        {
            return await _frameworkUtil.RunOnFrameworkThread(() => _getPresetsInfo.InvokeFunc()).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // log it if we failed.
            _logger.LogWarning(e, "Could not Get Moodles Presets Info");
            return null;
        }
    }



    /// <summary> This method gets the status of the moodles for a partiular address</summary>
    public async Task<string?> GetStatusAsync(string playerNameWithWorld)
    {
        if (!APIAvailable) return null; // return if the API isnt available

        try // otherwise, try and return an awaited task that gets the status of the moodles for a particular address
        {
            return await _frameworkUtil.RunOnFrameworkThread(() => _moodlesGetStatus.InvokeFunc(playerNameWithWorld)).ConfigureAwait(false);

        }
        catch (Exception e)
        {
            // log it if we failed.
            _logger.LogWarning(e, "Could not Get Moodles Status");
            return null;
        }
    }

    public async Task ApplyOwnStatusByGUID(List<Guid> guid, string playerNameWithWorld)
    {
        if (!APIAvailable) return;

        foreach (var g in guid)
        {
            await ApplyOwnStatusByGUID(g, playerNameWithWorld);
        }
    }


    public async Task ApplyOwnStatusByGUID(Guid guid, string playerNameWithWorld)
    {
        if (!APIAvailable) return;
        try
        {
            await _frameworkUtil.RunOnFrameworkThread(() => 
                _applyStatusByGuid.InvokeAction(guid, playerNameWithWorld)).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not Apply Moodles Status");
        }
    }

    public async Task ApplyOwnPresetByGUID(Guid guid, string playerNameWithWorld)
    {
        if (!APIAvailable) return;
        try
        {
            await _frameworkUtil.RunOnFrameworkThread(() => 
                _applyPresetByGuid.InvokeAction(guid, playerNameWithWorld)).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not Apply Moodles Status");
        }
    }

    /// <summary> This method applies the statuses from a pair to the client </summary>
    public async Task ApplyStatusesFromPairToSelf(string applierNameWithWorld, string recipientNameWithWorld, List<MoodlesStatusInfo> statuses)
    {
        if (!APIAvailable) return;
        try
        {
            await _frameworkUtil.RunOnFrameworkThread(() => 
                _applyStatusesFromPair.InvokeAction(applierNameWithWorld, recipientNameWithWorld, statuses)).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not Apply Moodles Status");
        }
    }

    public async Task RemoveOwnStatusByGuid(List<Guid> guidsToRemove, string playerNameWithWorld)
    {
        if (!APIAvailable) return;
        try
        {
            await _frameworkUtil.RunOnFrameworkThread(() =>
                _removeStatusByGuids.InvokeAction(guidsToRemove, playerNameWithWorld)).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not Set Moodles Status");
        }
    }

    public async Task SetStatusAsync(string playerNameWithWorld, string status)
    {
        // if the API is not available, return
        if (!APIAvailable) return;
        try
        {
            await _frameworkUtil.RunOnFrameworkThread(() => 
                _setStatusManager.InvokeAction(playerNameWithWorld, status)).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not Set Moodles Status");
        }
    }


    /// <summary> Reverts the status of the moodles for a gameobject spesified by the pointer</summary>
    /// <param name="pointer">the pointer address of the player to revert the status for</param>
    public async Task ClearStatusAsync(string playerNameWithWorld)
    {
        if (!APIAvailable) return;
        try
        {
            await _frameworkUtil.RunOnFrameworkThread(() =>
                _clearStatusesFromManager.InvokeAction(playerNameWithWorld)).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not Set Moodles Status");
        }
    }
}
