using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;
using GagSpeak.GagspeakConfiguration;
using GagSpeak.GagspeakConfiguration.Models;
using GagSpeak.PlayerData.Pairs;
using GagSpeak.Services.Mediator;
using GagSpeak.Utils.ChatLog;
using GagspeakAPI.Data.Enum;
using GagspeakAPI.Dto.Toybox;

namespace GagSpeak.Services;

// handles the global chat and pattern discovery social features.
public class DiscoverService : DisposableMediatorSubscriberBase
{
    private readonly PairManager _pairManager;

    public ChatLog GagspeakGlobalChat { get; private set; }


    public DiscoverService(ILogger<DiscoverService> logger, GagspeakMediator mediator,
        PairManager pairManager) : base(logger, mediator)
    {
        _pairManager = pairManager;

        // set the chat log up.
        GagspeakGlobalChat = new ChatLog();

        Mediator.Subscribe<GlobalChatMessage>(pairManager, (msg) => AddChatMessage(msg.ChatMessage));

        Mediator.Subscribe<DisconnectedMessage>(this, (msg) => GagspeakGlobalChat.ClearMessages());
    }


    private void AddChatMessage(GlobalChatMessageDto msg)
    {
        // extract the userdata from the message
        var userData = msg.MessageSender;

        // grab the list of our currently online pairs.
        var matchedPair = _pairManager.DirectPairs.FirstOrDefault(p => p.UserData.UID == userData.UID);

        string SenderName = "Anon. Kinkster";
        // see if the message Sender is in our list of online pairs.
        if (matchedPair != null)
        {
            // if they are, set the name to their nickname, alias, or UID.
            SenderName = matchedPair.GetNickname() ?? matchedPair.UserData.AliasOrUID;
        }

        // construct the chat message struct to add.
        ChatMessage msgToAdd = new ChatMessage
        {
            User = SenderName,
            SupporterTier = userData.SupporterTier ?? CkSupporterTier.NoRole,
            Message = msg.Message,
        };

        GagspeakGlobalChat.AddMessage(msgToAdd);
    }





}