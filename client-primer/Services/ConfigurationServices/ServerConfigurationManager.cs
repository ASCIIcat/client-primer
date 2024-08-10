using GagSpeak.GagspeakConfiguration;
using GagSpeak.GagspeakConfiguration.Models;
using GagSpeak.Services.Mediator;
using GagSpeak.UpdateMonitoring;
using GagSpeak.WebAPI;

namespace GagSpeak.Services.ConfigurationServices;

/// <summary>
/// This configuration manager helps manage the various interactions with all config files related to server-end activity.
/// <para> It provides a comprehensive interface for configuring servers, managing tags and nicknames, and handling authentication keys. </para>
/// </summary>
public class ServerConfigurationManager
{
    private readonly OnFrameworkService _frameworkUtils;            // a utilities class with methods that work with the Dalamud framework
    private readonly ILogger<ServerConfigurationManager> _logger;   // the logger for the server config manager
    private readonly GagspeakMediator _gagspeakMediator;            // the mediator for our Gagspeak Mediator
    private readonly ServerConfigService _configService;            // the config service for the server
    private readonly NicknamesConfigService _nicknamesConfig;       // config for the nicknames service (This adds lots of files that seem unessisary, but we'll see down the line.)
    private readonly ServerTagConfigService _serverTagConfig;       // the config service for the server tags (also dont think we need this, but we'll see)

    public ServerConfigurationManager(ILogger<ServerConfigurationManager> logger, OnFrameworkService onFrameworkService,
        ServerConfigService configService, ServerTagConfigService serverTagConfig, NicknamesConfigService nicknamesConfig,
        GagspeakMediator GagspeakMediator)
    {
        _logger = logger;
        _frameworkUtils = onFrameworkService;
        _configService = configService;
        _serverTagConfig = serverTagConfig;
        _nicknamesConfig = nicknamesConfig;
        _gagspeakMediator = GagspeakMediator;
        // insure the nicknames and tag configs exist in the main server.
        if (_nicknamesConfig.Current.ServerNicknames == null) { _nicknamesConfig.Current.ServerNicknames = new(); }
        if (_serverTagConfig.Current.ServerTagStorage == null) { _serverTagConfig.Current.ServerTagStorage = new(); }
        // ensure main exists
        EnsureMainExists();
    }

    /// <summary> The current API URL for the server </summary>
    public string CurrentApiUrl => CurrentServer.ServiceUri;

    /// <summary> The current server we are connected to, taken from the ServerStorage class </summary>
    public ServerStorage CurrentServer => _configService.Current.ServerStorage;
    public ServerTagStorage TagStorage => _serverTagConfig.Current.ServerTagStorage;
    public ServerNicknamesStorage NicknameStorage => _nicknamesConfig.Current.ServerNicknames;

    /// <summary> Deals with managing authentication keys and associating characters with servers. 
    /// <para> This includes retrieving secret keys for authentication and adding character information to server configurations. </para>
    /// </summary>
    /// <param name="serverIdx">The IDX of the server we are connected to.</param>
    /// <returns>The Secret Key</returns>
    public string? GetSecretKey()
    {
        // fetch the players local content ID
        var tempLocalContentID = _frameworkUtils.GetPlayerLocalContentIdAsync().GetAwaiter().GetResult();
        // fetch the characterName
        var charaName = _frameworkUtils.GetPlayerNameAsync().GetAwaiter().GetResult();
        // fetch the characters homeworld ID
        var worldId = _frameworkUtils.GetHomeWorldIdAsync().GetAwaiter().GetResult();
        // see if the current server does not have any authentications or any secret keys
        if (!CurrentServer.Authentications.Any() && CurrentServer.SecretKeys.Any())
        {
            // then add an authentication  with the character name, world, and current server's secret key.
            // (change this later so it adds on the first generation)
            CurrentServer.Authentications.Add(new Authentication()
            {
                CharacterName = charaName,
                WorldId = worldId,
                SecretKeyIdx = CurrentServer.SecretKeys.Last().Key, // add new authentication with the secret key being the last added key
            });

            Save();
        }

        // create a authentication object (possibly null) that finds the character name and world ID in the current server authentications
        Authentication? auth = CurrentServer.Authentications
                .Find(f => string.Equals(f.CharacterName, charaName, StringComparison.Ordinal) && f.WorldId == worldId);

        // if it is null (since it is possible), then return null.
        if (auth == null) return null;

        // otherwise, if the current server has a secret key with the authentication's secret key index, return the key.
        if (CurrentServer.SecretKeys.TryGetValue(auth.SecretKeyIdx, out var secretKey))
        {
            return secretKey.Key;
        }

        // if we land here for some god forsaken reason, return null.
        return null;
    }

    /// <summary> Gets the server API Url </summary>
    public string GetServerApiUrl() => _configService.Current.ServerStorage.ServiceUri;

    /// <summary> Gets the server name </summary>
    public string GetServerName() => _configService.Current.ServerStorage.ServerName;

    /// <summary> Checks if the configuration is valid </summary>
    /// <returns> True if the current server storage object is not null </returns>
    public bool HasValidConfig() => CurrentServer != null;

    /// <summary> Requests to save the configuration service file to the clients computer. </summary>
    public void Save()
    {
        var caller = new StackTrace().GetFrame(1)?.GetMethod()?.ReflectedType?.Name ?? "Unknown";
        _logger.LogDebug("{caller} Calling config save", caller);
        _configService.Save();
    }

    /// <summary> Adds the current character to the server storage object </summary>
    internal void AddCurrentCharacterToServer(bool addLastSecretKey = false)
    {
        // append the authentication to the serverStorage object
        CurrentServer.Authentications.Add(new Authentication()
        {
            CharacterName = _frameworkUtils.GetPlayerNameAsync().GetAwaiter().GetResult(),
            WorldId = _frameworkUtils.GetHomeWorldIdAsync().GetAwaiter().GetResult(),
            SecretKeyIdx = addLastSecretKey ? CurrentServer.SecretKeys.Last().Key : -1,
        });
        // Save the ServerConfigurationManager
        Save();
    }

    /// <summary> Adds an empty character to the server storage object </summary>
    internal void AddEmptyCharacterToServer()
    {
        // create a new authentication for it, then saves the server configuration manager
        CurrentServer.Authentications.Add(new Authentication());
        Save();
    }

    /// <summary> Adds a new secret key to the server storage object </summary>
    internal void AddOpenPairTag(string tag)
    {
        TagStorage.OpenPairTags.Add(tag);
        // save the tag config
        _serverTagConfig.Save();
    }

    /// <summary> Adds a tag to the server </summary>
    internal void AddTag(string tag)
    {
        _logger.LogTrace("Adding tag {tag} to server storage and saving!");
        TagStorage.ServerAvailablePairTags.Add(tag);
        // save the tag config
        _serverTagConfig.Save();
        // publish a refresh UI message
        _gagspeakMediator.Publish(new RefreshUiMessage());
    }

    /// <summary> Adds a tag for the given UID </summary>
    internal void AddTagForUid(string uid, string tagName)
    {
        if (TagStorage.UidServerPairedUserTags.TryGetValue(uid, out var tags))
        {
            tags.Add(tagName);
            // publish a refresh UI message
            _gagspeakMediator.Publish(new RefreshUiMessage());
        }
        else
        {
            TagStorage.UidServerPairedUserTags[uid] = [tagName];
        }
        // save the server tag config
        _serverTagConfig.Save();
    }

    /// <summary> Checks if the server contains an open pair tag </summary>
    /// <returns> A boolean value of if the server contains an open pair tag </returns>
    internal bool ContainsOpenPairTag(string tag)
    {
        return TagStorage.OpenPairTags.Contains(tag);
    }

    /// <summary> Checks if the server contains a tag for the given UID </summary>
    /// <returns> A boolean value of if the server contains a tag for the given UID </returns>
    internal bool ContainsTag(string uid, string tag)
    {
        // If the UID is found in the paired user tags dictionary, check if the tag is contained in the list of tags
        if (TagStorage.UidServerPairedUserTags.TryGetValue(uid, out var tags))
        {
            // Return true if the tag is found in the list of tags
            return tags.Contains(tag, StringComparer.Ordinal);
        }

        return false;
    }

    /// <summary>Retrieves the nickname associated with a given UID (User Identifier).</summary>
    /// <returns>Returns the nickname as a string if found; otherwise, returns null.</returns>
    internal string? GetNicknameForUid(string uid)
    {
        // Attempt to retrieve the nickname for the given UID from the current nicknames storage
        if (NicknameStorage.UidServerComments.TryGetValue(uid, out var nickname))
        {
            // If a nickname is found but is empty, return null
            if (string.IsNullOrEmpty(nickname)) return null;
            // Return the found nickname
            return nickname;
        }
        // Return null if no nickname is found for the given UID
        return null;
    }

    /// <summary> Retrieves a set of all available pair tags for the current server. </summary>
    /// <returns>Returns a HashSet<string> containing the tags.</returns>
    internal HashSet<string> GetServerAvailablePairTags()
    {
        // Return the set of available pair tags from the current server tag storage
        return TagStorage.ServerAvailablePairTags;
    }

    /// <summary> Retrieves a dictionary mapping UIDs to lists of paired user tags for the current server</summary>
    /// <returns>Returns a <c>Dictionary(string, List(string))</c> where the key is the UID and the value is the list of tags.</returns>
    internal Dictionary<string, List<string>> GetUidServerPairedUserTags()
    {
        // Return the dictionary of UIDs to their paired user tags from the current server tag storage
        return TagStorage.UidServerPairedUserTags;
    }

    /// <summary> Retrieves a set of UIDs for a given tag</summary>
    /// <returns> Returns a HashSet(string) containing the UIDs.</returns>
    internal HashSet<string> GetUidsForTag(string tag)
    {
        return TagStorage.UidServerPairedUserTags.Where(p => p.Value.Contains(tag, StringComparer.Ordinal)).Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary> Checks if the server has any tags for the given UID </summary>
    /// <returns> A boolean value of if the server has any tags for the given UID </returns>
    internal bool HasTags(string uid)
    {
        if (TagStorage.UidServerPairedUserTags.TryGetValue(uid, out var tags))
        {
            return tags.Any();
        }
        return false;
    }

    /// <summary> Removes the character from the server </summary>
    internal void RemoveCharacterFromServer(int serverSelectionIndex, Authentication item)
    {
        CurrentServer.Authentications.Remove(item);
        Save();
    }

    /// <summary> Removes the open pair tag from the server tag configuration</summary>
    internal void RemoveOpenPairTag(string tag)
    {
        TagStorage.OpenPairTags.Remove(tag);
        _serverTagConfig.Save();
    }

    /// <summary> Removes the tag from the server tag configuration</summary>
    internal void RemoveTag(string tag)
    {
        _logger.LogTrace("REMOVING tag {tag} to server storage and saving!");
        // Remove the tag from the available pair tags
        TagStorage.ServerAvailablePairTags.Remove(tag);
        // for each UID that had that tag, remove the tag from the list of tags
        foreach (var uid in GetUidsForTag(tag))
        {
            RemoveTagForUid(uid, tag, save: false);
        }
        // save the tag config
        _serverTagConfig.Save();
        // publish a refresh UI message
        _gagspeakMediator.Publish(new RefreshUiMessage());
    }

    /// <summary>
    /// Removes a tag from the given UID.
    /// </summary>
    /// <param name="uid">The User ID</param>
    /// <param name="tagName">The tag to remove</param>
    /// <param name="save">if the config should be saved</param>
    internal void RemoveTagForUid(string uid, string tagName, bool save = true)
    {
        if (TagStorage.UidServerPairedUserTags.TryGetValue(uid, out var tags))
        {
            tags.Remove(tagName);

            if (save)
            {
                // save the tag config
                _serverTagConfig.Save();
                // publish a refresh UI message
                _gagspeakMediator.Publish(new RefreshUiMessage());
            }
        }
    }

    /// <summary>
    /// Rename the tag in a server to a new name
    /// </summary>
    /// <param name="oldName">what the tag was previously</param>
    /// <param name="newName">what the tag is now</param>
    internal void RenameTag(string oldName, string newName)
    {
        TagStorage.ServerAvailablePairTags.Remove(oldName);
        TagStorage.ServerAvailablePairTags.Add(newName);
        foreach (var existingTags in TagStorage.UidServerPairedUserTags.Select(k => k.Value))
        {
            if (existingTags.Remove(oldName))
                existingTags.Add(newName);
        }
    }

    /// <summary> Saves the nicknames config </summary>
    internal void SaveNicknames()
    {
        _nicknamesConfig.Save();
    }

    /// <summary>
    /// Set a nickname for a user identifier.
    /// </summary>
    /// <param name="uid">the user identifier</param>
    /// <param name="nickname">the nickname to add</param>
    /// <param name="save">if the nicknames should be saved</param>
    internal void SetNicknameForUid(string uid, string nickname, bool save = true)
    {
        if (string.IsNullOrEmpty(uid)) return;

        NicknameStorage.UidServerComments[uid] = nickname;
        if (save)
            _nicknamesConfig.Save();
    }

    /// <summary> Ensure the main server exists (and honestly that is literally the only server that should even madder) </summary> 
    private void EnsureMainExists()
    {
        // if the serverstroage serverUri is not the same as the MainServiceUri defined in the api controller.
        if (!string.Equals(_configService.Current.ServerStorage.ServiceUri, ApiController.MainServiceUri, StringComparison.OrdinalIgnoreCase))
        {
            // then set it to the main server
            _configService.Current.ServerStorage = new ServerStorage()
            {
                ServerName = ApiController.MainServer,
                ServiceUri = ApiController.MainServiceUri,
            };
        }
        // save the configuration
        Save();
    }

    private void TryCreateCurrentNotesStorage()
    {
        if (_nicknamesConfig.Current.ServerNicknames is null)
        {
            _nicknamesConfig.Current.ServerNicknames = new();
        }
    }

    private void TryCreateCurrentServerTagStorage()
    {
        if (_serverTagConfig.Current.ServerTagStorage is null)
        {
            _serverTagConfig.Current.ServerTagStorage = new();
        }
    }
}
