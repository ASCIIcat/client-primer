using GagspeakAPI.Dto.User;
using GagSpeak.PlayerData.Pairs;
using GagSpeak.Services.Mediator;
using GagSpeak.Services.ConfigurationServices;
using GagspeakAPI.Dto.UserPair;
using GagSpeak.PlayerData.PrivateRooms;
using GagspeakAPI.Data;
using GagspeakAPI.Data.VibeServer;

namespace GagSpeak.PlayerData.Factories;

/// <summary>
/// Handles creating instances of participants in the private room.
/// </summary>
public class ParticipantFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly GagspeakMediator _gagspeakMediator;

    public ParticipantFactory(ILoggerFactory loggerFactory,
        GagspeakMediator gagspeakMediator)
    {

        _loggerFactory = loggerFactory;
        _gagspeakMediator = gagspeakMediator;
    }

    /// <summary> Creates a new Participant object from the UserDto</summary>
    /// <returns> A new Participant object </returns>
    public Participant Create(PrivateRoomUser user)
    {
        return new Participant(_loggerFactory.CreateLogger<Participant>(),
            user, _gagspeakMediator);
    }
}
