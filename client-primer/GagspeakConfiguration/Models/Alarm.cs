using GagSpeak.Toybox.Models;

namespace GagSpeak.GagspeakConfiguration.Models;

[Serializable]
public record Alarm
{
    public bool Enabled { get; set; } = false;
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset SetTimeUTC { get; set; } = DateTimeOffset.MinValue;
    public string PatternToPlay { get; set; } = string.Empty;
    public string PatternDuration { get; set; } = "00:00";
    public List<AlarmRepeat> RepeatFrequency { get; set; } = [];
}