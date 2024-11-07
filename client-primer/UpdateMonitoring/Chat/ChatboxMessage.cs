using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using GagSpeak.ChatMessages;
using GagSpeak.GagspeakConfiguration;
using GagSpeak.PlayerData.Data;
using GagSpeak.PlayerData.Handlers;
using GagSpeak.PlayerData.Pairs;
using GagSpeak.Services;
using GagSpeak.Services.Mediator;
using GagSpeak.Toybox.Controllers;
using GagSpeak.Toybox.Services;
using GagSpeak.Utils;
using GagSpeak.WebAPI;
using Lumina.Excel.GeneratedSheets;
using Lumina.Excel.GeneratedSheets2;
using System.Linq;
using static GagSpeak.PlayerData.Handlers.PuppeteerHandler;
using Territory = FFXIVClientStructs.FFXIV.Client.Game.UI.TerritoryInfo;

namespace GagSpeak.UpdateMonitoring.Chat;

/// <summary>
/// This class handles incoming chat messages, combat messages, and other related Messages we care about. 
/// It will then trigger the appropriate chat classes to handle the message.
/// 
/// It is worth noting that this detection occurs after the message is sent to the server, and should not be
/// depended on for translation prior to sending.
/// </summary>
public unsafe class ChatBoxMessage : DisposableMediatorSubscriberBase
{
    private readonly GagspeakConfigService _mainConfig;
    private readonly PlayerCharacterData _playerInfo;
    private readonly PuppeteerHandler _puppeteerHandler;
    private readonly ChatSender _chatSender;
    private readonly DeathRollService _deathRolls;
    private readonly TriggerService _triggers;
    private readonly IChatGui _chat;
    private readonly IClientState _clientState;
    private readonly IDataManager _dataManager;
    private Stopwatch messageTimer; // Stopwatch Timer for time between messages sent (should no longer be needed since we are not sending chained messages)

    private unsafe Territory* info = Territory.Instance();

    /// <summary> This is the constructor for the OnChatMsgManager class. </summary>
    public ChatBoxMessage(ILogger<ChatBoxMessage> logger, GagspeakMediator mediator,
        GagspeakConfigService mainConfig, PlayerCharacterData playerInfo,
        PuppeteerHandler puppeteerHandler, ChatSender chatSender, 
        DeathRollService deathRolls, TriggerService triggers, IChatGui clientChat, 
        IClientState clientState, IDataManager dataManager) : base(logger, mediator)
    {
        _mainConfig = mainConfig;
        _playerInfo = playerInfo;
        _chatSender = chatSender;
        _puppeteerHandler = puppeteerHandler;
        _deathRolls = deathRolls;
        _triggers = triggers;
        _chat = clientChat;
        _clientState = clientState;
        _dataManager = dataManager;
        // set variables
        MessageQueue = new Queue<string>();
        messageTimer = new Stopwatch();
        // set up the event handlers
        _chat.ChatMessage += Chat_OnChatMessage;

        Mediator.Subscribe<FrameworkUpdateMessage>(this, (_) => FrameworkUpdate());

        Mediator.Subscribe<UpdateChatListeners>(this, (msg) => OnUpdateChatListeners());

        // load in the initial list of chat listeners
        OnUpdateChatListeners();
    }

    public static Queue<string> MessageQueue; // the messages to send to the server.
    public static List<string> PlayersToListenFor; // players to listen to messages from. (Format of NameWithWorld)

    /// <summary> This is the disposer for the OnChatMsgManager class. </summary>
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _chat.ChatMessage -= Chat_OnChatMessage;
    }

    public static void EnqueueMessage(string message)
    {
        MessageQueue.Enqueue(message);
    }

    private void OnUpdateChatListeners()
    {
        Logger.LogDebug("Updating Chat Listeners", LoggerType.ToyboxTriggers);
        PlayersToListenFor = _puppeteerHandler.GetPlayersToListenFor();
    }

    private void Chat_OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        // Don't process messages if we are not visible or connected.
        if (_clientState.LocalPlayer is null || MainHub.IsConnected is false)
            return;

        // Handle PVP Kills for achievement.
/*        if (type is (XivChatType)4922)
        {
            Logger.LogTrace("["+type+"] {"+message+"Message}", LoggerType.Puppeteer);
            // only process if in pvp.
            if (_clientState.IsPvP)
            {
                Logger.LogInformation("We were in PvP. Checking for PvP kill.", LoggerType.Puppeteer);
                // get the player payloads.
                Payload[] playerPayloads = message.Payloads.Where(x => x.Type == PayloadType.Player).ToArray();
                Logger.LogTrace("["+type+"] {"+message+"Message}", LoggerType.Puppeteer);
                if (playerPayloads.Length == 2)
                {
                    Logger.LogTrace("2 Player Payloads Found", LoggerType.Puppeteer);
                    PlayerPayload player1 = (PlayerPayload)playerPayloads[0];
                    PlayerPayload player2 = (PlayerPayload)playerPayloads[1];
                    Logger.LogTrace("Player 1: " + player1.PlayerName + "@" + player1.World.Name, LoggerType.Puppeteer);
                    Logger.LogTrace("Player 2: " + player2.PlayerName + "@" + player2.World.Name, LoggerType.Puppeteer);

                    if(_clientState.LocalPlayer.GetNameWithWorld() == player1.PlayerName + "@" + player1.World.Name)
                    {
                        Logger.LogInformation("We were the killer. We just killed " + player2.PlayerName + "@" + player2.World.Name, LoggerType.Puppeteer);
                        UnlocksEventManager.AchievementEvent(UnlocksEvent.PvpPlayerSlain);
                    }
                }
            }
        }*/

        // log all types of payloads included in the message.
        if (_clientState.IsPvP && type is (XivChatType)2874)
        {
            Logger.LogInformation("---------------------");
            Logger.LogInformation("ChatType[" + type + "] {" + message + "Message}");
            foreach (var payloadType in message.Payloads)
            {
                string text = payloadType.Type.ToString();
                if (payloadType.Type is PayloadType.RawText) text = text + "(" + payloadType.ToString() + ")";
                if (payloadType.Type is PayloadType.MapLink) text = text + "(" + payloadType.ToString() + ")";
                if (payloadType is PlayerPayload playerPayload)
                {
                    Logger.LogInformation("Player Payload: " + playerPayload.PlayerName + "@" + playerPayload.World.Name);
                }
                Logger.LogInformation("Payload Type: " + text);
            }
            // log current player state.
            Logger.LogInformation("IsDead? " + _clientState.LocalPlayer.IsDead);
            Logger.LogInformation("HP? " + _clientState.LocalPlayer.CurrentHp);
            Logger.LogInformation("Status Flags? " + _clientState.LocalPlayer.StatusFlags);
            Logger.LogInformation("Status Flags (tostring)" + _clientState.LocalPlayer.StatusFlags.ToString());
        }


        // Handle the special case where we are checking a DeathRoll
        if (type == (XivChatType)2122 || type == (XivChatType)8266 || type == (XivChatType)4170)
        {
            if (message.Payloads[1] is PlayerPayload)
            {
                // grab the player payload from the message
                var playerPayload = message.Payloads[1] as PlayerPayload;
                if (playerPayload != null)
                {
                    // check for social triggers
                    _deathRolls.ProcessMessage(type, playerPayload.PlayerName + "@" + playerPayload.World.Name, message);
                }
            }
            else
            {
                // Should check under our name if this isn't valid as someone elses player payload.
                Logger.LogDebug("Message was from self.", LoggerType.ToyboxTriggers);
                _deathRolls.ProcessMessage(type, _clientState.LocalPlayer.GetNameWithWorld(), message);
            }
        }

        // get the player payload of the sender. If we are sending the message, this is null.
        PlayerPayload? senderPlayerPayload = sender.Payloads.SingleOrDefault(x => x is PlayerPayload) as PlayerPayload;
        string senderName = "";
        string senderWorld = "";
        if (senderPlayerPayload == null)
        {
            senderName = _clientState.LocalPlayer.Name.TextValue;
            senderWorld = _clientState.LocalPlayer.HomeWorld.GameData!.Name;
        }
        else
        {
            senderName = senderPlayerPayload.PlayerName;
            senderWorld = senderPlayerPayload.World.Name;
        }

        // After this point we only check triggers, so if its not a valid trigger then dont worry about it.
        var channel = ChatChannel.GetChatChannelFromXivChatType(type);
        if (channel is null) return;

        // if we are the sender, return after checking if what we sent matches any of our pairs triggers.
        if (senderName + "@" + senderWorld == _clientState.LocalPlayer.GetNameWithWorld())
        {
            // check if the message we sent contains any of our pairs triggers.
            _puppeteerHandler.OnClientMessageContainsPairTrigger(message.TextValue);
            // if our message is longer than 5 words, fire our on-chat-message achievement.
            if (_playerInfo.IsPlayerGagged && (_playerInfo.GlobalPerms?.LiveChatGarblerActive ?? false) && message.TextValue.Split(' ').Length > 5)
            {
                if (_mainConfig.Current.ChannelsGagSpeak.Contains(channel.Value))
                    UnlocksEventManager.AchievementEvent(UnlocksEvent.ChatMessageSent, type);
            }
            return;
        }

        // route to scan for any active triggers. (block outgoing tells because otherwise they always come up as from the recipient).
        if (type != XivChatType.TellOutgoing) _triggers.CheckActiveChatTriggers(type, senderName + "@" + senderWorld, message.TextValue);

        // return if the message type is not in our valid chat channels for puppeteer.
        if (_playerInfo.CoreDataNull || !_mainConfig.Current.ChannelsPuppeteer.Contains(channel.Value))
        {
            Logger.LogDebug("Message was not in a valid channel for puppeteer.", LoggerType.Puppeteer);
            return;
        }
        

        // check for global puppeteer triggers
        var globalTriggers = _playerInfo.GlobalPerms?.GlobalTriggerPhrase.Split('|').ToList() ?? new List<string>();
        if (_puppeteerHandler.IsValidTriggerWord(globalTriggers, message, out string matchedTrigger))
        {
            // convert everything to PuppeteerPerms
            var permsGlobal = new PuppeteerPerms(
                _playerInfo.GlobalPerms!.GlobalAllowSitRequests,
                _playerInfo.GlobalPerms.GlobalAllowMotionRequests,
                _playerInfo.GlobalPerms.GlobalAllowAllRequests);
            // the message did contain the trigger word, to obtain the message to send.
            SeString msgToSend = _puppeteerHandler.GetMessageFromTrigger(matchedTrigger, permsGlobal, message, type);
            
            if (msgToSend.TextValue.IsNullOrEmpty()) 
                return;

            // enqueue the message and log success
            Logger.LogInformation(senderName + " used your global trigger phase to make you execute a message!", LoggerType.Puppeteer);
            EnqueueMessage("/" + msgToSend.TextValue);
        }

        // check for puppeteer pair triggers
        if (SenderIsInPuppeteerListeners(senderName, senderWorld, out Pair pair))
        {
            var pairTriggers = pair.UserPairOwnUniquePairPerms.TriggerPhrase.Split('|').ToList();
            if (_puppeteerHandler.IsValidTriggerWord(pairTriggers, message, out string matchedPairTrigger))
            {
                Logger.LogInformation(senderName + " used your pair trigger phrase to make you execute a message!");
                var permsPair = new PuppeteerPerms(pair.UserPairOwnUniquePairPerms.AllowSitRequests,
                    pair.UserPairOwnUniquePairPerms.AllowMotionRequests, pair.UserPairOwnUniquePairPerms.AllowAllRequests,
                    pair.UserPairOwnUniquePairPerms.StartChar, pair.UserPairOwnUniquePairPerms.EndChar);

                SeString msgToSend = _puppeteerHandler.GetMessageFromTrigger(matchedPairTrigger, permsPair, message, type, pair.UserData.UID);
                
                if (msgToSend.TextValue.IsNullOrEmpty())
                    return;

                Logger.LogInformation(senderName + " used your pair trigger phrase to make you execute a message!", LoggerType.Puppeteer);
                EnqueueMessage("/" + msgToSend.TextValue);
            }
        }
    }

    private bool SenderIsInPuppeteerListeners(string name, string world, out Pair matchedPair)
    {
        matchedPair = null!;
        string nameWithWorld = name + "@" + world;
        // make sure we are listening for this player.
        if (!PlayersToListenFor.Contains(nameWithWorld)) 
            return false;

        // make sure they exist in our alias list config
        var uidOfSender = _puppeteerHandler.GetUIDMatchingSender(name, world);
        if (uidOfSender.IsNullOrEmpty()) 
            return false;

        var pairOfUid = _puppeteerHandler.GetPairOfUid(uidOfSender!);
        if (pairOfUid is null) 
            return false;

        // successful match
        matchedPair = pairOfUid;
        return true;
    }



    /// <summary> <b> SENDS A REAL CHAT MESSAGE TO THE SERVER </b></summary>
    public void SendRealMessage(string message)
    {
        try
        {
            _chatSender.SendMessage(message);
        }
        catch (Exception e)
        {
            Logger.LogError($"Failed to send message {e}: {message}");
        }
    }

    /// <summary>
    /// This function will handle the framework update, 
    /// and will send messages to the server if there are any in the queue.
    /// </summary>
    private void FrameworkUpdate()
    {
        if (MessageQueue.Count <= 0 || _chatSender is null) return;

        if (!messageTimer.IsRunning)
        {
            messageTimer.Start();
        }
        else
        {
            if (messageTimer.ElapsedMilliseconds > 500)
            {
                try
                {
                    _chatSender.SendMessage(MessageQueue.Dequeue());
                }
                catch (Exception e)
                {
                    Logger.LogWarning($"{e},{e.Message}");
                }
                messageTimer.Restart();
            }
        }
    }
}

