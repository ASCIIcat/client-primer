using GagSpeak.Services.Mediator;
using ImGuiNET;

namespace GagSpeak.UI.UiToybox;

public class ToyboxTriggerCreator
{
    private readonly ILogger<ToyboxTriggerCreator> _logger;
    private readonly GagspeakMediator _mediator;
    private readonly UiSharedService _uiSharedService;

    public ToyboxTriggerCreator(ILogger<ToyboxTriggerCreator> logger, GagspeakMediator mediator,
        UiSharedService uiSharedService)
    {
        _logger = logger;
        _mediator = mediator;
        _uiSharedService = uiSharedService;
    }

    public void DrawToyboxTriggerCreatorPanel()
    {
        ImGui.Text("Create Trigger Panel");
    }
}
