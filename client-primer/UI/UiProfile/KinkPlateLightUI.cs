using Dalamud.Interface.Colors;
using GagSpeak.GagspeakConfiguration;
using GagSpeak.PlayerData.Pairs;
using GagSpeak.Services;
using GagSpeak.Services.ConfigurationServices;
using GagSpeak.Services.Mediator;
using GagspeakAPI.Data;
using ImGuiNET;
using System.Numerics;

namespace GagSpeak.UI.Profile;

public class KinkPlateLightUI : WindowMediatorSubscriberBase
{
    private readonly KinkPlateLight _lightUI;
    private readonly KinkPlateService _KinkPlateManager;
    private readonly PairManager _pairManager;
    private readonly UiSharedService _uiShared;
    private bool _showFullUID;

    private bool ThemePushed = false;

    public KinkPlateLightUI(ILogger<KinkPlateLightUI> logger, GagspeakMediator mediator,
        KinkPlateLight plateLightUi, KinkPlateService KinkPlateManager,
        PairManager pairManager, UiSharedService uiShared, UserData pairUserData)
        : base(logger, mediator, "###GagSpeakKinkPlateLight" + pairUserData.UID)
    {
        _lightUI = plateLightUi;
        _KinkPlateManager = KinkPlateManager;
        _pairManager = pairManager;
        _uiShared = uiShared;

        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoTitleBar;
        Size = new(256, 512);
        IsOpen = true;

        _showFullUID = _pairManager.DirectPairs.Any(x => x.UserData.UID == pairUserData.UID);
        UserDataToDisplay = pairUserData;
    }

    public UserData UserDataToDisplay { get; init; }
    private bool HoveringCloseButton = false;

    protected override void PreDrawInternal()
    {
        if (!ThemePushed)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 35f);

            ThemePushed = true;
        }
    }
    protected override void PostDrawInternal()
    {
        if (ThemePushed)
        {
            ImGui.PopStyleVar(2);
            ThemePushed = false;
        }
    }

    protected override void DrawInternal()
    {
        // do not display if pair is null.
        if (UserDataToDisplay is null)
            return;

        // obtain the profile for this userPair.
        var KinkPlate = _KinkPlateManager.GetKinkPlate(UserDataToDisplay);
        if (KinkPlate.KinkPlateInfo.Flagged)
        {
            ImGui.TextUnformatted("This profile is flagged by moderation.");
            return;
        }

        string DisplayName = _showFullUID
            ? UserDataToDisplay.AliasOrUID
            : "Anon.Kinkster-" + UserDataToDisplay.UID.Substring(UserDataToDisplay.UID.Length - 3);

        var drawList = ImGui.GetWindowDrawList();
        // clip based on the region of our draw space.
        _lightUI.RectMin = drawList.GetClipRectMin();
        _lightUI.RectMax = drawList.GetClipRectMax();

        // draw the plate.
        _lightUI.DrawKinkPlateLight(drawList, KinkPlate, DisplayName, UserDataToDisplay, _showFullUID);

        // Draw the close button.
        CloseButton(drawList, DisplayName);
        KinkPlateUI.AddRelativeTooltip(_lightUI.CloseButtonPos, _lightUI.CloseButtonSize, "Close " + DisplayName + "'s KinkPlate™");
    }

    private void CloseButton(ImDrawListPtr drawList, string displayName)
    {
        var btnPos = _lightUI.CloseButtonPos;
        var btnSize = _lightUI.CloseButtonSize;

        var closeButtonColor = HoveringCloseButton ? ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f)) : ImGui.GetColorU32(ImGuiColors.ParsedPink);

        drawList.AddLine(btnPos, btnPos + btnSize, closeButtonColor, 3);
        drawList.AddLine(new Vector2(btnPos.X + btnSize.X, btnPos.Y), new Vector2(btnPos.X, btnPos.Y + btnSize.Y), closeButtonColor, 3);

        ImGui.SetCursorScreenPos(btnPos);
        if (ImGui.InvisibleButton($"CloseButton##KinkPlateClose" + displayName, btnSize))
            this.IsOpen = false;

        HoveringCloseButton = ImGui.IsItemHovered();
    }

    public override void OnClose()
    {
        Mediator.Publish(new RemoveWindowMessage(this));
    }
}
