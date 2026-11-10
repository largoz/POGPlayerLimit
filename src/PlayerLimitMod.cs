using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppGame.Networking;
using MelonLoader;
using POGMods.Config;
using EosLobbyInterface = Il2CppEpic.OnlineServices.Lobby.LobbyInterface;

[assembly: MelonInfo(typeof(POGMods.PlayerLimit.PlayerLimitMod), "POG Player Limit",
    POGMods.PlayerLimit.PlayerLimitMod.VERSION, "largo", "https://github.com/largoz/POGPlayerLimit")]

namespace POGMods.PlayerLimit
{
    // How many players a lobby you host takes, you included.
    //
    // The game keeps its limit in one serialized field, NetworkHandler.m_maxClientCount (6 in
    // resources.assets on the 2026-11 build). GameAssembly.dll reads it in two places (found with
    // the Cpp2IL addresses of that build):
    //  - NetworkHandler.Awake passes it to `new ServicesEOS(maxPlayerCount)`, the only call of that
    //    constructor. ServicesEOS keeps it in m_maxPlayerCount and CreateLobbyInternal hands it to
    //    EOS as the lobby's MaxNumLobbyMembers when a lobby is created.
    //  - NetworkHandler.TryApproveConnections lets a joining player in while
    //    NetworkManager.ConnectedClients.Count + 1 <= m_maxClientCount (the host is one of them).
    // Setting both fields sets both checks. Only the host's value is used.
    public class PlayerLimitMod : MelonMod
    {
        public const string VERSION = "1.0.0";
        private const string ModName = "POG Player Limit";

        // The game's own value on the build this was written against: the slider's default and
        // what RESTORE DEFAULTS returns to. The first Awake compares it with the real field and
        // warns if a game update changed it.
        private const int GameDefault = 6;
        private const int Min = 1;
        // Used only if the EOS SDK's own constant cannot be read at runtime.
        private const int EosMaxFallback = 64;

        private static string Description() =>
            "How many players a lobby you host takes, you included. The game's own limit is " +
            $"{GameDefault}; Epic Online Services, which runs the game's lobbies, allows at most {s_max}.\n" +
            "Only the host needs this mod. Change it before you host: a lobby keeps the size " +
            "it was created with.\n" +
            $"The game is balanced for up to {GameDefault} players.";

        private static int s_limit = GameDefault;
        private static int s_max = EosMaxFallback;
        private static bool s_gameValueChecked;
        private static string s_lastLogged;
        private static MelonLogger.Instance s_log;

        public override void OnInitializeMelon()
        {
            s_log = LoggerInstance;
            s_max = ReadEosMax();
            POGConfig.Register(ModName, new List<ConfigEntry>
            {
                new SliderEntry("Max players (you included)",
                    () => s_limit, v => SetLimit((int)Math.Round(v)),
                    Min, s_max, v => $"{v:F0}", "MaxPlayers", Min, true, true),
            }, new ModInfo(VERSION, Description(), "largo"));
            // A dragged slider changes the value many times; log once when the panel closes.
            POGConfig.PanelClosed += () => Apply(Current(), "settings");
            s_log.Msg($"POG Player Limit {VERSION} loaded: {s_limit} players (range {Min}-{s_max}).");
        }

        // The EOS SDK constant shipped with the game (Epic.OnlineServices.Lobby.LobbyInterface.
        // MAX_LOBBY_MEMBERS), read through the game's own metadata.
        private static int ReadEosMax()
        {
            try
            {
                int v = EosLobbyInterface.MAX_LOBBY_MEMBERS;
                if (v >= Min) return v;
                s_log.Warning($"EOS MAX_LOBBY_MEMBERS read as {v}; using {EosMaxFallback}.");
            }
            catch (Exception ex)
            {
                s_log.Warning($"EOS MAX_LOBBY_MEMBERS unreadable ({ex.Message}); using {EosMaxFallback}.");
            }
            return EosMaxFallback;
        }

        private static void SetLimit(int value)
        {
            s_limit = Math.Clamp(value, Min, s_max);
            Apply(Current(), null);
        }

        private static NetworkHandler Current()
        {
            try { return NetworkHandler.Singleton; }
            catch { return null; }
        }

        // Writes the limit into the game. `source` names the caller for the log; null = no log
        // line (a slider being dragged).
        internal static void Apply(NetworkHandler nh, string source)
        {
            if (nh == null) return;
            try
            {
                if (!s_gameValueChecked)
                {
                    s_gameValueChecked = true;
                    int game = nh.m_maxClientCount;
                    if (game != GameDefault)
                        s_log.Warning($"The game's own limit is {game}, not {GameDefault}: a game update changed it. " +
                                      "RESTORE DEFAULTS still returns to " + GameDefault + ".");
                }

                nh.m_maxClientCount = s_limit;
                var online = nh.m_onlineService;
                if (online != null) online.m_maxPlayerCount = (uint)s_limit;

                if (source == null) return;
                // Read back what the game now holds, so the log says what the game will use.
                string state = $"joins allowed up to {nh.m_maxClientCount} players, new lobbies sized " +
                               (online == null ? "(no online service yet)" : online.m_maxPlayerCount.ToString());
                if (state == s_lastLogged) return;
                s_lastLogged = state;
                s_log.Msg($"{state} ({source}).");
            }
            catch (Exception ex)
            {
                s_log.Error($"Could not set the player limit: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(NetworkHandler), nameof(NetworkHandler.Awake))]
    internal static class NetworkHandlerAwakePatch
    {
        // After Awake: it has just created ServicesEOS from the game's own value.
        private static void Postfix(NetworkHandler __instance) => PlayerLimitMod.Apply(__instance, "game start");
    }
}
