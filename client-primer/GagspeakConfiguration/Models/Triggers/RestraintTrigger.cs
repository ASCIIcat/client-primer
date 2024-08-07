using GagspeakAPI.Data.Enum;
using GagspeakAPI.Data.VibeServer;

namespace GagSpeak.GagspeakConfiguration.Models;

/// <summary>
/// A basic authentication class to validate that the information from the client when they attempt to connect is correct.
/// </summary>
[Serializable]
public record RestraintTrigger : Trigger
{
    public override TriggerKind Type => TriggerKind.RestraintSet;

    // the kind of restraint set that will invoke this trigger's execution
    public string RestraintSetName { get; set; } = string.Empty;

    // the new state of it that will trigger the execution
    public UpdatedNewState RestraintState { get; set; } = UpdatedNewState.Enabled;
}
