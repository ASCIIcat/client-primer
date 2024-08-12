using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using GagSpeak.GagspeakConfiguration;
using GagSpeak.Services;
using GagSpeak.Services.ConfigurationServices;
using GagSpeak.Services.Mediator;
using GagSpeak.UI.Profile;
using GagSpeak.UpdateMonitoring;
using GagSpeak.Utils;
using GagSpeak.WebAPI;
using GagspeakAPI.Data;
using GagspeakAPI.Data.Enum;
using GagspeakAPI.Dto.Permissions;
using ImGuiNET;
using ImGuiScene;
using OtterGui;
using Penumbra.GameData.Interop;
using System.Numerics;
using System.Reflection.Metadata;
using static FFXIVClientStructs.FFXIV.Client.UI.Misc.GroupPoseModule;

namespace GagSpeak.UI.MainWindow;

/// <summary> 
/// Sub-class of the main UI window. Handling displaying personal account information.
/// </summary>
public class MainUiAccount : DisposableMediatorSubscriberBase
{
    private readonly ApiController _apiController;
    private readonly UiSharedService _uiShared;
    private readonly OnFrameworkService _frameworkUtils;
    private readonly GagspeakConfigService _config;
    private readonly ProfileService _profileManager;
    private readonly IDalamudPluginInterface _pi;

    public MainUiAccount(ILogger<MainUiAccount> logger,
        GagspeakMediator mediator, ApiController apiController,
        UiSharedService uiShared, OnFrameworkService frameworkUtils,
        GagspeakConfigService config, ProfileService profileManager,
        IDalamudPluginInterface pi) : base(logger, mediator)
    {
        _apiController = apiController;
        _uiShared = uiShared;
        _frameworkUtils = frameworkUtils;
        _config = config;
        _profileManager = profileManager;
        _pi = pi;

        Mediator.Subscribe<DisconnectedMessage>(this, (_) =>
        {
            // do something to the image i guess?
        });
    }

    // until we find a way to inject image data byte arrays into a shared ImmediateTexture, we need to aquire it via this route.
    // This is basically was the sharedImmediateTexture does already, but if we can convert it, it would be cleaner.
    private IDalamudTextureWrap? _profileImageWrap = null;
    private byte[] _profileImageData = [];

    /// <summary> Main Draw function for this tab </summary>
    public void DrawAccountSection()
    {
        // get the width of the window content region we set earlier
        var _windowContentWidth = UiSharedService.GetWindowContentRegionWidth();
        var _spacingX = ImGui.GetStyle().ItemSpacing.X;

        // make this whole thing a scrollable child window.
        // (keep the border because we will style it later and helps with alignment visual)
        using (ImRaii.Child("AccountPageChild", new Vector2(UiSharedService.GetWindowContentRegionWidth(), 0), false, ImGuiWindowFlags.NoScrollbar))
        {
            try
            {
                // fetch own profile data to store / display.
                // This function itself does no API calls unless the requested UID is different.
                var profileData = _profileManager.GetGagspeakProfile(new UserData(_apiController.UID));

                // if the profile is flagged, say so.
                if (profileData.Flagged)
                {
                    UiSharedService.ColorTextWrapped("Your profile has been flagged for inappropriate content. Please review your profile.", ImGuiColors.DalamudRed);
                }

                // ensure we only update the texture wrap when the data is changed.
                if (profileData.Base64ProfilePicture.IsNullOrEmpty())
                {
                    _profileImageWrap?.Dispose();
                    _profileImageWrap = _uiShared.GetLogoNoRadialSmall();
                }
                else if (!_profileImageData.SequenceEqual(profileData.ImageData.Value) && !string.IsNullOrEmpty(profileData.Base64ProfilePicture))
                {
                    _profileImageData = profileData.ImageData.Value;
                    _profileImageWrap?.Dispose();
                    _profileImageWrap = _uiShared.LoadImage(_profileImageData);
                }


                if (!(_profileImageWrap is { } wrap))
                {
                    Logger.LogWarning("Player Image was null, replacing with default image until new image data is made!");
                }
                else
                {
                    var region = ImGui.GetContentRegionAvail();
                    ImGui.Spacing();
                    Vector2 imgSize = new Vector2(180f, 180f);
                    // move the x position so that it centeres the image to the center of the window.
                    _uiShared.SetCursorXtoCenter(imgSize.X);
                    var currentPosition = ImGui.GetCursorPos();

                    var pos = ImGui.GetCursorScreenPos();
                    ImGui.GetWindowDrawList().AddImageRounded(wrap.ImGuiHandle, pos, pos + imgSize, Vector2.Zero, Vector2.One, 
                        ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f)), 90f);
                    ImGui.SetCursorPos(new Vector2(currentPosition.X, currentPosition.Y + imgSize.Y));

                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error: {ex}");
            }
            
            // draw the UID header below this.
            DrawUIDHeader();
            // below this, draw a separator. (temp)
            ImGui.Spacing();
            ImGui.Separator();

            DrawSafewordChild();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.AlignTextToFramePadding();
            DrawAccountSettingChild(FontAwesomeIcon.PenSquare, "My Profile", "Open and Customize your Profile!", () =>
            {
                Mediator.Publish(new UiToggleMessage(typeof(EditProfileUi)));
            });

            ImGui.AlignTextToFramePadding();
            DrawAccountSettingChild(FontAwesomeIcon.Cog, "My Settings", "Opens the Settings UI", () =>
            {
                Mediator.Publish(new UiToggleMessage(typeof(SettingsUi)));
            });

            ImGui.AlignTextToFramePadding();
            DrawAccountSettingChild(FontAwesomeIcon.QuestionCircle, "Help", "Does not serve much purpose right now", () =>
            {
                Logger.LogInformation("You're on your own until its out of beta, sorry!");
            });

            // now do one for ko-fi
            ImGui.AlignTextToFramePadding();
            DrawAccountSettingChild(FontAwesomeIcon.Coffee, "Support via Ko-fi", "This plugin took a massive toll on my life as a solo dev." +
                Environment.NewLine + "As happy as I am to make this free for all of you to enjoy, " +
                Environment.NewLine + "any support or tips are much appreciated ♥", () =>
                {
                    try { Process.Start(new ProcessStartInfo { FileName = "https://www.ko-fi.com/cordeliamist", UseShellExecute = true }); }
                    catch (Exception e) { Logger.LogError($"Failed to open the Ko-Fi link. {e.Message}"); }
                });

            ImGui.AlignTextToFramePadding();
            DrawAccountSettingChild(FontAwesomeIcon.Pray, "Support via Patreon", "This plugin took a massive toll on my life as a solo dev."+
                Environment.NewLine + "As happy as I am to make this free for all of you to enjoy, "+
                Environment.NewLine + "any support / tips are much appreciated ♥", () =>
            {
                try { Process.Start(new ProcessStartInfo { FileName = "https://www.patreon.com/CordeliaMist", UseShellExecute = true }); }
                catch (Exception e) { Logger.LogError($"Failed to open the Patreon link. {e.Message}"); }
            });

            ImGui.AlignTextToFramePadding();
            DrawAccountSettingChild(FontAwesomeIcon.ThumbsUp, "Send Positive Feedback!", "Opens a short 1 question positive feedback form ♥", () =>
            {
                try { Process.Start(new ProcessStartInfo { FileName = "https://forms.gle/4AL43XUeWna2DtYK7", UseShellExecute = true }); }
                catch (Exception e) { Logger.LogError($"Failed to open the google form. {e.Message}"); }
            });

            ImGui.AlignTextToFramePadding();
            DrawAccountSettingChild(FontAwesomeIcon.Wrench, "Open Configs", "Opens the Plugin Config Folder", () =>
            {
                try
                {
                    var ConfigDirectory = _pi.ConfigDirectory.FullName;
                    Process.Start(new ProcessStartInfo { FileName = ConfigDirectory, UseShellExecute = true });
                }
                catch (Exception e)
                {
                    Logger.LogError($"[ConfigFileOpen] Failed to open the config directory. {e.Message}");
                }
            });
        }
    }

    /// <summary>
    /// Draws the UID header for the currently connected client (you)
    /// </summary>
    private void DrawUIDHeader()
    {
        // fetch the Uid Text of yourself
        var uidText = _uiShared.GetUidText();

        // push the big boi font for the UID
        using (_uiShared.UidFont.Push())
        {
            var uidTextSize = ImGui.CalcTextSize(uidText);
            ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X) / 2 - uidTextSize.X / 2);
            // display it, it should be green if connected and red when not.
            ImGui.TextColored(_uiShared.GetUidColor(), uidText);
        }

        // if we are connected
        if (_apiController.ServerState is ServerState.Connected)
        {
            UiSharedService.CopyableDisplayText(_apiController.DisplayName);

            // if the UID does not equal the display name
            if (!string.Equals(_apiController.DisplayName, _apiController.UID, StringComparison.Ordinal))
            {
                // grab the original text size for the UID in the api controller
                var origTextSize = ImGui.CalcTextSize(_apiController.UID);
                // adjust the cursor and redraw the UID (really not sure why this is here but we can trial and error later.
                ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X) / 2 - origTextSize.X / 2);
                ImGui.TextColored(_uiShared.GetUidColor(), _apiController.UID);
                // give it the same functionality.
                UiSharedService.CopyableDisplayText(_apiController.UID);
            }
        }
    }

    private bool EditingSafeword = false;
    private void DrawSafewordChild()
    {
        var height = 35f; // static height
        Vector2 textSize;
        using (_uiShared.UidFont.Push())
        {
            textSize = ImGui.CalcTextSize($"No Safeword Set");
        }
        var iconSize = _uiShared.GetIconData(FontAwesomeIcon.ExclamationTriangle);
        var editButtonSize = _uiShared.GetIconData(FontAwesomeIcon.Edit);
        var bigTextPaddingDistance = ((height - textSize.Y)/2);
        var iconFontCenterY = (height - iconSize.Y) / 2;
        var editButtonCenterY = (height - editButtonSize.Y)/ 2;

        using (ImRaii.Child($"##DrawSafewordChild", new Vector2(UiSharedService.GetWindowContentRegionWidth(), height), false))
        {
            // We love ImGui....
            var childStartYpos = ImGui.GetCursorPosY();
            ImGui.SetCursorPosY(childStartYpos + iconFontCenterY);
            _uiShared.IconText(FontAwesomeIcon.ExclamationTriangle, ImGuiColors.DalamudYellow);

            ImGui.SameLine(iconSize.X + ImGui.GetStyle().ItemSpacing.X);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX());
            var safewordText = _config.Current.Safeword == "" ? "No Safeword Set" : _config.Current.Safeword;
            if (EditingSafeword)
            {
                ImGui.SetCursorPosY(childStartYpos + ((height-23)/2) +0.5f); // 23 is the input text box height
                ImGui.SetNextItemWidth(225 * ImGuiHelpers.GlobalScale);
                var safeword = _config.Current.Safeword;
                if (ImGui.InputTextWithHint("##Your Safeword", "Enter Safeword", ref safeword, 30, ImGuiInputTextFlags.EnterReturnsTrue))
                {
                    _config.Current.Safeword = safeword;
                    _config.Save();
                    EditingSafeword = false;
                }
                // now, head to the same line of the full width minus the width of the button
                ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - editButtonSize.X - ImGui.GetStyle().ItemSpacing.X);
                ImGui.SetCursorPosY(childStartYpos + ((height - editButtonSize.Y) / 2) -2f);
            }
            else
            {
                ImGui.SetCursorPosY(bigTextPaddingDistance - 2f); // -2f accounts for font visual offset
                using (_uiShared.UidFont.Push())
                {
                    UiSharedService.ColorText(safewordText, ImGuiColors.DalamudYellow);
                }
                // now, head to the same line of the full width minus the width of the button
                ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - editButtonSize.X - ImGui.GetStyle().ItemSpacing.X);
                ImGui.SetCursorPosY(childStartYpos + ((height - editButtonSize.Y) / 2) + 1f);
            }
            // draw out the icon button
            _uiShared.IconText(FontAwesomeIcon.Edit);
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                EditingSafeword = !EditingSafeword;
            }
        }
        UiSharedService.AttachToolTip("Set a safeword to quickly revert any changes made by the plugin.");
    }

    private void DrawAccountSettingChild(FontAwesomeIcon leftIcon, string displayText, string hoverTT, Action buttonAction)
    {
        var height = 20f; // static height
        var startYpos = ImGui.GetCursorPosY();

        var textSize = ImGui.CalcTextSize(displayText);
        var iconSize = _uiShared.GetIconData(leftIcon);
        var arrowRightSize = _uiShared.GetIconData(FontAwesomeIcon.ChevronRight);
        var textCenterY = ((height - textSize.Y) / 2);
        var iconFontCenterY = (height - iconSize.Y) / 2;
        var arrowRightCenterY = (height - arrowRightSize.Y) / 2;
        // text height == 17, padding on top and bottom == 2f, so 21f
        using (ImRaii.Child($"##DrawSetting{displayText + hoverTT}", new Vector2(UiSharedService.GetWindowContentRegionWidth(), height)))
        {
            // We love ImGui....
            var childStartYpos = ImGui.GetCursorPosY();
            ImGui.SetCursorPosY(childStartYpos + iconFontCenterY);
            _uiShared.IconText(leftIcon);

            ImGui.SameLine(iconSize.X + ImGui.GetStyle().ItemSpacing.X);
            ImGui.SetCursorPosY(childStartYpos + textCenterY);
            ImGui.TextUnformatted(displayText);

            // Position the button on the same line, aligned to the right
            ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - arrowRightSize.X - ImGui.GetStyle().ItemSpacing.X);
            ImGui.SetCursorPosY(childStartYpos + arrowRightCenterY);
            // Draw the icon button and perform the action when pressed
            _uiShared.IconText(FontAwesomeIcon.ChevronRight);
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            buttonAction.Invoke();
        }
        UiSharedService.AttachToolTip(hoverTT);
    }
}
