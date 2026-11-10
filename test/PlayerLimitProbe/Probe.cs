// Test driver for POG Player Limit. Never shipped; run_probe.sh copies it into Mods/ for one run.
//
// Drives the mod the way a player does, through POG Config's own slider, then hosts a PRIVATE
// lobby (not listed in the browser) and reads the lobby size back from EOS. Each step logs
// "[PROBE] <n> ..."; run_probe.sh screenshots the window after each.
//
//   0  main menu: what the game holds after start (run_probe.sh saved MaxPlayers = 10 first, so
//      this checks the value applied at game start); opens POG Config on the mod's page
//   1  sets the slider to its maximum through SliderEntry.ApplyValue (what dragging it calls)
//   2  closes the panel and signs in to EOS (ServicesEOS.SignInAsync; the game itself signs in
//      only once you open the multiplayer menu); waits for the sign-in
//   3  creates the lobby the way the game does when you host privately after picking a save:
//      ServicesEOS.HostRelay(name, InviteAndRoomCode). Not through the save menu, so no save
//      file is touched. The lobby is invite/room-code only, not listed in the browser.
//   4  once it exists: the lobby size as the game's lobby manager holds it, and as EOS reports it
//      for a fresh read of the same lobby id (Lobby.InitFromLobbyHandle -> LobbyDetails)
//   5  leaves the lobby, RESTORE DEFAULTS on the slider, logs the saved preference; "done"
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Il2CppGame.Menu;
using Il2CppGame.Networking;
using Il2CppGame.Platform;
using Il2CppInterop.Runtime;
using MelonLoader;
using Unity.Netcode;
using UnityEngine;
using EosLobby = Il2CppPlayEveryWare.EpicOnlineServices.Samples.Lobby;

[assembly: MelonInfo(typeof(PlayerLimitProbe.Probe), "PlayerLimitProbe", "0.0.1", "test")]

namespace PlayerLimitProbe
{
    public class Probe : MelonMod
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        private const string Page = "POG Player Limit";
        private const float StepGap = 5f;
        private const float SignInTimeout = 60f;
        private const float HostTimeout = 60f;
        private const string LobbyName = "PlayerLimitProbe";

        private int _step = -1;
        private bool _started, _finished;
        private float _next, _deadline;
        private string _lastLogin;
        private volatile string _hostResult;

        public override void OnUpdate()
        {
            if (_finished) return;
            if (!_started)
            {
                if (GameObject.Find("POGModsButton") == null) return;
                _started = true; _next = Time.unscaledTime + 4f; _step = 0;
                return;
            }
            if (Time.unscaledTime < _next) return;
            try { Step(); }
            catch (Exception ex)
            {
                Log($"{_step} FAILED {ex.GetType().Name}: {(ex.InnerException ?? ex).Message}");
                Finish();
            }
        }

        private void Step()
        {
            switch (_step)
            {
                case 0:
                    Log($"0 menu: {GameState()}; saved MaxPlayers {Pref()}");
                    var beh = PogConfig().GetType("POGMods.Config.ConfigBehaviour");
                    object inst = beh.GetField("_instance", Any).GetValue(null);
                    beh.GetMethod("OpenPanel", Any).Invoke(inst, null);
                    beh.GetMethod("SelectMod", Any).Invoke(inst, new object[] { PageIndex() });
                    Advance(StepGap); break;

                case 1:
                    object slider = Slider();
                    float max = (float)slider.GetType().GetField("_max", Any).GetValue(slider);
                    slider.GetType().GetMethod("ApplyValue", Any).Invoke(slider, new object[] { max, true });
                    Log($"1 slider set to its maximum {max}: {GameState()}");
                    Advance(StepGap); break;

                case 2:
                    if (_deadline == 0f)
                    {
                        PogConfig().GetType("POGMods.Config.ConfigBehaviour").GetMethod("RequestClose", Any).Invoke(null, null);
                        var svc = NetworkHandler.Singleton?.m_onlineService;
                        if (svc == null) { Log("2 FAILED no online service"); Finish(); return; }
                        // NetworkMenu.CurrentLoginState is the game's own sign-in state (None,
                        // Offline, LoggingIn, Online). ServicesEOS.m_isAuthorized is not it: it
                        // stayed false for 60 s while the menu said Online (run 3, 2026-11-10).
                        bool already = LoginState() == "Online";
                        if (!already) svc.SignInAsync();
                        Log($"2 {(already ? "already signed in" : "SignInAsync called")}; login {LoginState()}");
                        _deadline = Time.unscaledTime + SignInTimeout;
                    }
                    if (LoginState() != "Online")
                    {
                        string login = LoginState();
                        if (login != _lastLogin) { Log($"2 login {login}"); _lastLogin = login; }
                        if (Time.unscaledTime > _deadline) { Log($"2 FAILED not signed in to EOS after {SignInTimeout} s"); Finish(); }
                        else _next = Time.unscaledTime + 1f;
                        return;
                    }
                    Log($"2 signed in to EOS; login {LoginState()}");
                    _deadline = 0f;
                    Advance(2f); break;

                case 3:
                    NetworkHandler.Singleton.m_onlineService.HostRelay(LobbyName, JoinRestriction.InviteAndRoomCode,
                        (Il2CppSystem.Action<bool, string>)new Action<bool, string>((ok, msg) => _hostResult = $"{ok} '{msg}'"));
                    Log($"3 HostRelay called: {GameState()}");
                    _deadline = Time.unscaledTime + HostTimeout;
                    Advance(1f); break;

                case 4:
                    var lobby = NetworkHandler.Singleton?.m_onlineService?.m_lobbyManager?.GetCurrentLobby();
                    if (_hostResult == null || lobby == null || !lobby.IsValid() || string.IsNullOrEmpty(lobby.Id))
                    {
                        if (_hostResult != null && _hostResult.StartsWith("False")) { Log($"4 FAILED HostRelay answered {_hostResult}"); Finish(); return; }
                        if (Time.unscaledTime > _deadline) { Log($"4 FAILED no lobby after {HostTimeout} s (HostRelay answered {_hostResult ?? "nothing"}, lobby valid {lobby?.IsValid()})"); Finish(); }
                        else _next = Time.unscaledTime + 1f;
                        return;
                    }
                    var fresh = new EosLobby();
                    fresh.InitFromLobbyHandle(lobby.Id);
                    Log($"4 lobby created (HostRelay answered {_hostResult}): {GameState()}; " +
                        $"lobby manager says max {lobby.MaxNumLobbyMembers}; " +
                        $"EOS says max {fresh.MaxNumLobbyMembers}, members {fresh.Members?.Count}, valid {fresh.IsValid()}");
                    Advance(StepGap); break;

                case 5:
                    NetworkHandler.Singleton.m_onlineService.Disconnect();
                    var after = NetworkHandler.Singleton.m_onlineService.m_lobbyManager?.GetCurrentLobby();
                    Log($"5 left the lobby (current lobby valid {after?.IsValid()})");
                    Finish(); break;
            }
        }

        private static string LoginState()
        {
            var menu = NetworkMenu.Instance;
            return menu == null ? "(no menu)" : menu.CurrentLoginState.ToString();
        }

        private void Advance(float wait) { _step++; _next = Time.unscaledTime + wait; }

        private void Finish()
        {
            // Never leave a test value behind: whatever happened, the slider goes back to default.
            try
            {
                object s = Slider();
                s.GetType().GetMethod("RestoreDefault", Any).Invoke(s, null);
                Log($"restored default: {GameState()}; saved MaxPlayers {Pref()}");
            }
            catch (Exception ex) { Log($"restore FAILED {(ex.InnerException ?? ex).Message}"); }
            Log("done");
            _finished = true;
        }

        private static string GameState()
        {
            var nh = NetworkHandler.Singleton;
            if (nh == null) return "no NetworkHandler";
            var online = nh.m_onlineService;
            return $"m_maxClientCount {nh.m_maxClientCount}, ServicesEOS.m_maxPlayerCount " +
                   (online == null ? "(none)" : online.m_maxPlayerCount.ToString());
        }

        private static string Pref()
            => MelonPreferences.GetEntry<float>("POG_Player_Limit", "MaxPlayers")?.Value.ToString() ?? "(no entry)";

        private static Assembly PogConfig()
            => AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "POGConfig");

        private static IEnumerable Records()
            => (IEnumerable)PogConfig().GetType("POGMods.Config.POGConfig").GetMethod("GetAllRecords", Any).Invoke(null, null);

        private static int PageIndex()
            => Records().Cast<object>().ToList().FindIndex(r => (string)r.GetType().GetField("Name", Any).GetValue(r) == Page);

        private static object Slider()
        {
            object rec = Records().Cast<object>().First(r => (string)r.GetType().GetField("Name", Any).GetValue(r) == Page);
            var entries = (IEnumerable)rec.GetType().GetField("Entries", Any).GetValue(rec);
            return entries.Cast<object>().First(e => e.GetType().Name == "SliderEntry");
        }

        private static T FindObject<T>() where T : UnityEngine.Object
        {
            foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<T>()))
            {
                var t = o.TryCast<T>();
                if (t != null) return t;
            }
            return null;
        }

        private void Log(string msg) => LoggerInstance.Msg("[PROBE] " + msg);
    }
}
