using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using GagSpeak.PlayerData.Handlers;
using GagSpeak.Services.ConfigurationServices;
using GagSpeak.Services.Mediator;
using GagSpeak.Toybox.Debouncer;
using GagSpeak.Toybox.Services;
using ImGuiNET;
using ImPlotNET;
using OtterGui;
using System.Configuration;
using System.Numerics;
using System.Timers;

namespace GagSpeak.UI.UiRemote;

/// <summary>
/// I Blame ImPlot for its messiness as a result for this abyssmal display of code here.
/// </summary>
public class RemotePersonal : RemoteBase
{
    // the class includes are shared however (i think), so dont worry about that.
    private readonly UiSharedService _uiShared;
    private readonly DeviceHandler _intifaceHandler; // these SHOULD all be shared. but if not put into Service.
    private readonly ToyboxRemoteService _remoteService;
    private readonly string _windowName;

    public RemotePersonal(ILogger<RemotePersonal> logger,
        GagspeakMediator mediator, UiSharedService uiShared,
        ToyboxRemoteService remoteService, DeviceHandler deviceHandler,
        string windowName = "Personal") : base(logger, mediator, uiShared, remoteService, deviceHandler, windowName)
    {
        // grab the shared services
        _uiShared = uiShared;
        _intifaceHandler = deviceHandler;
        _remoteService = remoteService;

    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        // anything else we should add here we can add here.
    }

    /// <summary>
    /// Will display personal devices, their motors and additional options. </para>
    /// </summary>
    public override void DrawCenterBar(ref float xPos, ref float yPos, ref float width)
    {
        // grab the content region of the current section
        var CurrentRegion = ImGui.GetContentRegionAvail();
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImGuiHelpers.GlobalScale * 5);
        using (var color = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.2f, 0.2f, 0.2f, 0.930f)))
        {
            // create a child for the center bar
            using (var canterBar = ImRaii.Child($"###CenterBarDrawPersonal", new Vector2(CurrentRegion.X, 40f), false))
            {
                UiSharedService.ColorText("CenterBar dummy placement", ImGuiColors.ParsedGreen);
            }
        }
    }


    /// <summary>
    /// This method is also an overrided function, as depending on the use.
    /// We may also implement unique buttons here on the side that execute different functionalities.
    /// </summary>
    /// <param name="region"> The region of the side button section of the UI </param>
    public override void DrawSideButtonsTable(Vector2 region)
    {
        // push our styles
        using var styleColor = ImRaii.PushColor(ImGuiCol.Button, new Vector4(.2f, .2f, .2f, .2f))
            .Push(ImGuiCol.ButtonHovered, new Vector4(.3f, .3f, .3f, .4f))
            .Push(ImGuiCol.ButtonActive, _remoteService.LushPinkButton);
        using var styleVar = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 40);

        // grab the content region of the current section
        var CurrentRegion = ImGui.GetContentRegionAvail();
        var yPos2 = ImGui.GetCursorPosY();

        // setup a child for the table cell space
        using (var leftChild = ImRaii.Child($"###ButtonsList", CurrentRegion with { Y = region.Y }, false, ImGuiWindowFlags.NoDecoration))
        {
            var InitPos = ImGui.GetCursorPosY();
            if (RemoteOnline)
            {
                ImGuiUtil.Center($"{DurationStopwatch.Elapsed.ToString(@"mm\:ss")}");
            }

            // move our yposition down to the top of the frame height times a .3f scale of the current region
            ImGui.SetCursorPosY(InitPos + CurrentRegion.Y * .1f);
            ImGui.Separator();
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + CurrentRegion.Y * .025f);

            // attempt to obtain an image wrap for it
            var spinnyArrow = _uiShared.GetImageFromDirectoryFile("arrows-spin.png");
            if (!(spinnyArrow is { } wrap))
            {
                _logger.LogWarning("Failed to render image!");
            }
            else
            {
                Vector4 buttonColor = IsLooping ? _remoteService.LushPinkButton : _remoteService.SideButton;
                // aligns the image in the center like we want.
                if (_uiShared.DrawScaledCenterButtonImage("LoopButton", new Vector2(50, 50),
                    buttonColor, new Vector2(40, 40), wrap))
                {
                    ProcessLoopToggle();
                }
            }

            // move it down from current position by another .2f scale
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + CurrentRegion.Y * .05f);

            var circlesDot = _uiShared.GetImageFromDirectoryFile("circle-dot.png");
            if (!(circlesDot is { } wrap2))
            {
                _logger.LogWarning("Failed to render image!");
            }
            else
            {
                Vector4 buttonColor2 = IsFloating ? _remoteService.LushPinkButton : _remoteService.SideButton;
                // aligns the image in the center like we want.
                if (_uiShared.DrawScaledCenterButtonImage("FloatButton", new Vector2(50, 50),
                    buttonColor2, new Vector2(40, 40), wrap2))
                {
                    ProcessFloatToggle();
                }
            }

            ImGui.SetCursorPosY(CurrentRegion.Y * .775f);

            var power = _uiShared.GetImageFromDirectoryFile("power.png");
            if (!(power is { } wrap3))
            {
                _logger.LogWarning("Failed to render image!");
            }
            else
            {
                Vector4 buttonColor3 = RemoteOnline ? _remoteService.LushPinkButton : _remoteService.SideButton;
                // aligns the image in the center like we want.
                if (_uiShared.DrawScaledCenterButtonImage("PowerToggleButton", new Vector2(50, 50),
                    buttonColor3, new Vector2(40, 40), wrap3))
                {
                    if (!RemoteOnline)
                    {
                        _logger.LogTrace("Starting Recording!");
                        StartVibrating();
                    }
                    else
                    {
                        _logger.LogTrace("Stopping Recording!");
                        StopVibrating();
                    }
                }
            }
        }
        // pop what we appended
        styleColor.Pop(3);
        styleVar.Pop();
    }

    /// <summary>
    /// Override method for the recording data.
    /// It is here that we decide how our class handles the recordData function for our personal remote.
    /// </summary>
    public override void RecordData(object? sender, ElapsedEventArgs e)
    {
        // _logger.LogTrace($"AnyDeviceConnected: {_intifaceHandler.AnyDeviceConnected}, ConnectedToIntiface: {_intifaceHandler.ConnectedToIntiface}");
        // if any devices are currently connected, and our intiface client is connected,
        if (_intifaceHandler.AnyDeviceConnected && _intifaceHandler.ConnectedToIntiface)
        {
            //_logger.LogTrace("Sending Vibration Data to Devices!");
            // send the vibration data to all connected devices
            if (IsLooping && !IsDragging && StoredLoopDataBlock.Count > 0)
            {
                //_logger.LogTrace($"{(byte)Math.Round(StoredLoopDataBlock[BufferLoopIndex])}");
                _intifaceHandler.SendVibeToAllDevices((byte)Math.Round(StoredLoopDataBlock[BufferLoopIndex]));
            }
            else
            {
                //_logger.LogTrace($"{(byte)Math.Round(CirclePosition[1])}");
                _intifaceHandler.SendVibeToAllDevices((byte)Math.Round(CirclePosition[1]));
            }
        }
    }
}