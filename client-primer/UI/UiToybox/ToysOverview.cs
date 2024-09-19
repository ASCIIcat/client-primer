using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using GagSpeak.Services.ConfigurationServices;
using GagSpeak.Services.Mediator;
using GagSpeak.Toybox.Controllers;
using GagSpeak.Toybox.Data;
using GagSpeak.Toybox.Services;
using GagSpeak.UI.UiRemote;
using GagSpeak.Utils;
using GagSpeak.WebAPI;
using GagspeakAPI.Enums;
using ImGuiNET;
using OtterGui.Text;

namespace GagSpeak.UI.UiToybox;

public class ToyboxOverview
{
    private readonly ILogger<ToyboxOverview> _logger;
    private readonly GagspeakMediator _mediator;
    private readonly ApiController _apiController;
    private readonly UiSharedService _uiShared;
    private readonly ClientConfigurationManager _clientConfigs;
    private readonly ServerConfigurationManager _serverConfigs;
    private readonly ToyboxVibeService _vibeService;

    public ToyboxOverview(ILogger<ToyboxOverview> logger,
        GagspeakMediator mediator, ApiController controller,
        UiSharedService uiSharedService,
        ClientConfigurationManager clientConfigs,
        ServerConfigurationManager serverConfigs,
        ToyboxVibeService vibeService)
    {
        _logger = logger;
        _mediator = mediator;
        _apiController = controller;
        _uiShared = uiSharedService;
        _clientConfigs = clientConfigs;
        _serverConfigs = serverConfigs;
        _vibeService = vibeService;

        // grab path to the intiface
        if (IntifaceHelper.AppPath == string.Empty)
        {
            IntifaceHelper.GetApplicationPath();
        }
    }

    public void DrawOverviewPanel()
    {
        // draw the top display field for Intiface connectivity, similar to our other servers.
        DrawIntifaceConnectionStatus();
        // special case for the intiface connection, where if it is empty, we reset it to the default address.
        if (string.IsNullOrEmpty(_clientConfigs.GagspeakConfig.IntifaceConnectionSocket))
        {
            _clientConfigs.GagspeakConfig.IntifaceConnectionSocket = "ws://localhost:12345";
            _clientConfigs.Save();
        }

        // display a dropdown for the type of vibrator to use
        ImGui.SetNextItemWidth(125f);
        if (ImGui.BeginCombo("Set Vibrator Type##VibratorMode", _clientConfigs.GagspeakConfig.VibratorMode.ToString()))
        {
            foreach (VibratorMode mode in Enum.GetValues(typeof(VibratorMode)))
            {
                if (ImGui.Selectable(mode.ToString(), mode == _clientConfigs.GagspeakConfig.VibratorMode))
                {
                    _clientConfigs.GagspeakConfig.VibratorMode = mode;
                    _clientConfigs.Save();
                }
            }
            ImGui.EndCombo();
        }

        // display the wide list of connected devices, along with if they are active or not, below some scanner options
        if (_uiShared.IconTextButton(FontAwesomeIcon.TabletAlt, "Personal Remote", 125f))
        {
            // open the personal remote window
            _mediator.Publish(new UiToggleMessage(typeof(RemotePersonal)));
        }
        ImUtf8.SameLineInner();
        ImGui.Text("Open Personal Remote");

        // draw out the list of devices
        ImGui.Separator();
        _uiShared.BigText("Connected Device(s)");
        if (_clientConfigs.GagspeakConfig.VibratorMode == VibratorMode.Simulated)
        {
            DrawSimulatedVibeInfo();
        }
        else
        {
            DrawDevicesTable();
        }
    }


    private void DrawSimulatedVibeInfo()
    {
        ImGui.SetNextItemWidth(175 * ImGuiHelpers.GlobalScale);
        var vibeType = _clientConfigs.GagspeakConfig.VibeSimAudio;
        if (ImGui.BeginCombo("Vibe Sim Audio##SimVibeAudioType", _clientConfigs.GagspeakConfig.VibeSimAudio.ToString()))
        {
            foreach (VibeSimType mode in Enum.GetValues(typeof(VibeSimType)))
            {
                if (ImGui.Selectable(mode.ToString(), mode == _clientConfigs.GagspeakConfig.VibeSimAudio))
                {
                    _vibeService.UpdateVibeSimAudioType(mode);
                }
            }
            ImGui.EndCombo();
        }
        UiSharedService.AttachToolTip("Select the type of simulated vibrator sound to play when the intensity is adjusted.");

        // draw out the combo for the audio device selection to play to
        ImGui.SetNextItemWidth(175 * ImGuiHelpers.GlobalScale);
        int prevDeviceId = _vibeService.VibeSimAudio.ActivePlaybackDeviceId; // to only execute code to update data once it is changed
        // display the list        
        if (ImGui.BeginCombo("Playback Device##Playback Device", _vibeService.ActiveSimPlaybackDevice))
        {
            foreach (var device in _vibeService.PlaybackDevices)
            {
                bool isSelected = (_vibeService.ActiveSimPlaybackDevice == device);
                if (ImGui.Selectable(device, isSelected))
                {
                    _vibeService.SwitchPlaybackDevice(_vibeService.PlaybackDevices.IndexOf(device));
                }
            }
            ImGui.EndCombo();
        }
        UiSharedService.AttachToolTip("Select the audio device to play the simulated vibrator sound to.");


    }

    public void DrawDevicesTable()
    {
        if (_uiShared.IconTextButton(FontAwesomeIcon.Search, "Device Scanner", null, false, !_vibeService.IntifaceConnected))
        {
            // search scanning if we are not scanning, otherwise stop scanning.
            if (_vibeService.ScanningForDevices)
            {
                _vibeService.DeviceHandler.StopDeviceScanAsync().ConfigureAwait(false);
            }
            else
            {
                _vibeService.DeviceHandler.StartDeviceScanAsync().ConfigureAwait(false);
            }
        }
        var color = _vibeService.ScanningForDevices ? ImGuiColors.DalamudYellow : ImGuiColors.DalamudRed;
        var scanText = _vibeService.ScanningForDevices ? "Scanning..." : "Idle";
        ImGui.SameLine();
        ImGui.TextUnformatted("Scanner Status: ");
        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, color))
        {
            ImGui.TextUnformatted(scanText);
        }

        foreach (var device in _vibeService.DeviceHandler.ConnectedDevices)
        {
            DrawDeviceInfo(device);
        }
    }

    private void DrawDeviceInfo(ConnectedDevice Device)
    {
        if (Device == null) { ImGui.Text("Device is null for this index."); return; }

        ImGui.Text("Device Index: " + Device.DeviceIdx);

        ImGui.Text("Device Name: " + Device.DeviceName);

        ImGui.Text("Device Display Name: " + Device.DisplayName);

        // Draw Vibrate Attributes
        ImGui.Text("Vibrate Attributes:");
        ImGui.Indent();
        foreach (var attr in Device.VibeAttributes)
        {
            ImGui.Text("Feature: " + attr.FeatureDescriptor);
            ImGui.Text("Actuator Type: " + attr.ActuatorType);
            ImGui.Text("Step Count: " + attr.StepCount);
            ImGui.Text("Index: " + attr.Index);
        }
        ImGui.Unindent();

        // Draw Rotate Attributes
        ImGui.Text("Rotate Attributes:");
        ImGui.Indent();
        foreach (var attr in Device.RotateAttributes)
        {
            ImGui.Text("Feature: " + attr.FeatureDescriptor);
            ImGui.Text("Actuator Type: " + attr.ActuatorType);
            ImGui.Text("Step Count: " + attr.StepCount);
            ImGui.Text("Index: " + attr.Index);
        }
        ImGui.Unindent();

        // Check if the device has a battery
        ImGui.Text("Has Battery: " + Device.BatteryPresent);
        ImGui.Text("Battery Level: " + Device.BatteryLevel);
    }


    private void DrawIntifaceConnectionStatus()
    {
        var windowPadding = ImGui.GetStyle().WindowPadding;
        // push the style var to supress the Y window padding.
        var buttonSize = _uiShared.GetIconButtonSize(FontAwesomeIcon.Link);
        var buttplugServerAddr = DeviceController.IntifaceClientName;
        var addrSize = ImGui.CalcTextSize(buttplugServerAddr);

        string intifaceConnectionStr = $"Intiface Central Connection";

        var addrTextSize = ImGui.CalcTextSize(intifaceConnectionStr);
        var printAddr = intifaceConnectionStr != string.Empty;

        // if the server is connected, then we should display the server info
        if (_vibeService.IntifaceConnected)
        {
            // fancy math shit for clean display, adjust when moving things around
            ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth()) / 2 - (addrSize.X) / 2 - ImGui.GetStyle().ItemSpacing.X / 2);
            ImGui.TextColored(ImGuiColors.ParsedGreen, buttplugServerAddr);

        }
        // otherwise, if we are not connected, display that we aren't connected.
        else
        {
            ImGui.AlignTextToFramePadding();
            ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMin().X
                + UiSharedService.GetWindowContentRegionWidth())
                / 2 - (ImGui.CalcTextSize("No Active Client Connection").X) / 2 - ImGui.GetStyle().ItemSpacing.X / 2);
            ImGui.TextColored(ImGuiColors.DalamudRed, "No Active Client Connection");
        }

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImGui.GetStyle().ItemSpacing.Y);
        ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth()) / 2 - addrTextSize.X / 2);
        ImGui.TextUnformatted(intifaceConnectionStr);
        ImGui.SameLine();

        // now we need to display the connection link button beside it.
        var color = UiSharedService.GetBoolColor(_vibeService.IntifaceConnected);
        var connectedIcon = !_vibeService.IntifaceConnected ? FontAwesomeIcon.Link : FontAwesomeIcon.Unlink;

        ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - buttonSize.X);
        if (printAddr)
        {
            // unsure what this is doing but we can find out lol
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ((addrSize.Y + addrTextSize.Y) / 2 + addrTextSize.Y) / 2 - ImGui.GetStyle().ItemSpacing.Y + buttonSize.Y / 2);
        }


        // we need to turn the button from the connected link to the disconnected link.
        using (ImRaii.PushColor(ImGuiCol.Text, color))
        {
            if (_uiShared.IconButton(connectedIcon))
            {
                // if we are connected to intiface, then we should disconnect.
                if (_vibeService.IntifaceConnected)
                {
                    _vibeService.DeviceHandler.DisconnectFromIntifaceAsync();
                }
                // otherwise, we should connect to intiface.
                else
                {
                    _vibeService.DeviceHandler.ConnectToIntifaceAsync();
                }
            }
        }
        UiSharedService.AttachToolTip(_vibeService.IntifaceConnected
            ? "Disconnect from Intiface Central" : "Connect to Intiface Central");

        // go back to the far left, at the same height, and draw another button.
        var intifaceOpenIcon = FontAwesomeIcon.ArrowUpRightFromSquare;
        var intifaceIconSize = _uiShared.GetIconButtonSize(intifaceOpenIcon);

        ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + windowPadding.X);
        if (printAddr)
        {
            // unsure what this is doing but we can find out lol
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ((addrSize.Y + addrTextSize.Y) / 2 + addrTextSize.Y) / 2 - ImGui.GetStyle().ItemSpacing.Y + buttonSize.Y / 2);
        }

        if (_uiShared.IconButton(intifaceOpenIcon))
        {
            IntifaceHelper.OpenIntiface(_logger, true);
        }
        UiSharedService.AttachToolTip("Opens Intiface Central on your PC for connection.\nIf application is not detected, opens a link to installer.");

        // draw out the vertical slider.
        ImGui.Separator();
    }
}
