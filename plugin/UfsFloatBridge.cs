using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using BepInEx;
using UnityEngine;

namespace UfsFloatBridge
{
    [BepInPlugin(GUID, NAME, VERSION)]
    public class FloatBridge : BaseUnityPlugin
    {
        public const string GUID = "kairess.ufs.floatbridge";
        public const string NAME = "UFS Float Bridge";
        public const string VERSION = "0.1.0";

        private const int PORT = 18500;
        private const int CMD_PORT = 18501;
        private const float SEND_HZ = 60f;

        private UdpClient _udp;
        private UdpClient _cmdUdp;
        private IPEndPoint _ep;
        private readonly StringBuilder _sb = new StringBuilder(1024);
        private float _lastSend;
        private bool _firstTickLogged;
        private int _emitCount;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private readonly System.Collections.Generic.Queue<string> _pendingCmds =
            new System.Collections.Generic.Queue<string>();
        private readonly object _cmdLock = new object();

        // Latest bot-side status (drawn in the on-screen overlay).
        private string _botState = "IDLE";
        private string _botAction = "idle";
        private bool _botAuto = false;
        private float _botFps = 0f;
        private float _hudStatusTime;
        private bool _hudVisible = true;

        // Zone reco overlay state. Populated by ZoneReco at the end of its
        // compute. Auto-refresh happens on a slow timer; manual `zone:reco`
        // command path forces a verbose dump.
        private struct ZoneSpeciesRow {
            public string ko;
            public int spawnerCount;
            public Vector2 weightRange;
            public string depthStr;
            public string baitsStr;
        }
        private System.Collections.Generic.List<ZoneSpeciesRow> _zoneRows = new System.Collections.Generic.List<ZoneSpeciesRow>();
        private float _lastZoneRecoT = -999f;
        private const float ZONE_RECO_INTERVAL = 10f;
        private bool _zoneRecoQuiet;

        // Saved player view (UFPS camera yaw/pitch). Recall after the catch
        // dialog closes since the WATCH_FISH animation rotates the camera
        // to look at the held fish and never restores it.
        private float _savedPitch, _savedYaw;
        private bool _savedPresent;
        private float _restoreUntil;
        private const float RESTORE_DURATION = 2.0f;
        private bool _bqPrev;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        // Windows-only: a UDP socket that has received an ICMP "port unreachable"
        // (because the bot wasn't listening yet) goes into a state where every
        // subsequent Send throws WSAECONNRESET. Disable that behaviour so the
        // plugin keeps emitting whether the bot is up or not.
        private const int SIO_UDP_CONNRESET = -1744830452;

        private void DisableConnReset(UdpClient c)
        {
            try
            {
                c.Client.IOControl(SIO_UDP_CONNRESET, new byte[] { 0 }, null);
            }
            catch { }
        }

        private UdpClient MakeSendSocket()
        {
            var s = new UdpClient();
            DisableConnReset(s);
            return s;
        }

        private void Awake()
        {
            _udp = MakeSendSocket();
            _ep = new IPEndPoint(IPAddress.Loopback, PORT);

            // Command channel: bot -> plugin. Async receive into a queue so
            // commands are applied on the Unity main thread inside Update().
            try
            {
                _cmdUdp = new UdpClient(new IPEndPoint(IPAddress.Loopback, CMD_PORT));
                DisableConnReset(_cmdUdp);
                BeginCmdReceive();
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Could not bind cmd port " + CMD_PORT + ": " + ex.Message);
            }

            Logger.LogInfo("UFS Float Bridge active. state -> 127.0.0.1:" + PORT
                + " ; cmds <- 127.0.0.1:" + CMD_PORT);
        }

        private void OnDestroy()
        {
            try { _udp?.Close(); } catch { }
            try { _cmdUdp?.Close(); } catch { }
        }

        // ---- saved-view (camera pose recall) ------------------------------

        private Camera GetUfpsCamera()
        {
            var gc = GameController.Instance;
            if (gc == null || gc.fishingPlayer == null) return null;
            var fp = gc.fishingPlayer;
            if (fp.ufpsWeaponCamera != null) return fp.ufpsWeaponCamera;
            if (fp.ufpsCameraCamera != null) return fp.ufpsCameraCamera;
            return Camera.main;
        }

        // UFPS' vp_FPCamera holds the authoritative pitch/yaw in private
        // fields; setting Transform directly is overwritten next frame. We
        // reflect into m_Pitch / m_Yaw (or fall back to SetRotation).
        private static System.Reflection.FieldInfo _fiPitch, _fiYaw;
        private static System.Reflection.MethodInfo _miSetRot;
        private static System.Type _tVpCam;

        private Component FindVpFpCamera(Camera cam)
        {
            if (cam == null) return null;
            var comps = cam.GetComponents<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] != null && comps[i].GetType().Name == "vp_FPCamera")
                    return comps[i];
            }
            // Some setups attach vp_FPCamera to the camera's parent.
            var p = cam.transform.parent;
            while (p != null)
            {
                var c2 = p.GetComponents<Component>();
                for (int i = 0; i < c2.Length; i++)
                {
                    if (c2[i] != null && c2[i].GetType().Name == "vp_FPCamera")
                        return c2[i];
                }
                p = p.parent;
            }
            return null;
        }

        private bool TryGetUfpsRotation(out float pitch, out float yaw)
        {
            pitch = 0f; yaw = 0f;
            var cam = GetUfpsCamera();
            var vp = FindVpFpCamera(cam);
            if (vp == null) return false;
            CacheVpReflection(vp.GetType());
            if (!ReferenceEquals(_fiPitch, null) && !ReferenceEquals(_fiYaw, null))
            {
                pitch = (float)_fiPitch.GetValue(vp);
                yaw = (float)_fiYaw.GetValue(vp);
                return true;
            }
            return false;
        }

        private bool TrySetUfpsRotation(float pitch, float yaw)
        {
            var cam = GetUfpsCamera();
            var vp = FindVpFpCamera(cam);
            if (vp == null) return false;
            CacheVpReflection(vp.GetType());
            if (!ReferenceEquals(_miSetRot, null))
            {
                _miSetRot.Invoke(vp, new object[] { new Vector2(pitch, yaw) });
                return true;
            }
            if (!ReferenceEquals(_fiPitch, null) && !ReferenceEquals(_fiYaw, null))
            {
                _fiPitch.SetValue(vp, pitch);
                _fiYaw.SetValue(vp, yaw);
                return true;
            }
            return false;
        }

        private void CacheVpReflection(System.Type t)
        {
            if (ReferenceEquals(_tVpCam, t)) return;
            _tVpCam = t;
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic;
            _fiPitch = t.GetField("m_Pitch", flags);
            _fiYaw = t.GetField("m_Yaw", flags);
            _miSetRot = t.GetMethod("SetRotation", new System.Type[] { typeof(Vector2) });
        }

        private void TrySaveView()
        {
            Logger.LogInfo("[view] TrySaveView: enter");
            try
            {
                var cam = GetUfpsCamera();
                Logger.LogInfo("[view] cam=" + (cam == null ? "null" : cam.name));

                // List ALL components on the camera + its parent chain so we
                // can see what's available if vp_FPCamera isn't found by name.
                if (cam != null)
                {
                    var sb2 = new StringBuilder();
                    sb2.Append("[view] components on cam '").Append(cam.name).Append("': ");
                    foreach (var c in cam.GetComponents<Component>())
                        if (c != null) sb2.Append(c.GetType().Name).Append(", ");
                    Logger.LogInfo(sb2.ToString());

                    var p = cam.transform.parent;
                    int depth = 0;
                    while (p != null && depth < 4)
                    {
                        var sb3 = new StringBuilder();
                        sb3.Append("[view] components on parent '").Append(p.name).Append("': ");
                        foreach (var c in p.GetComponents<Component>())
                            if (c != null) sb3.Append(c.GetType().Name).Append(", ");
                        Logger.LogInfo(sb3.ToString());
                        p = p.parent; depth++;
                    }
                }

                var vp = FindVpFpCamera(cam);
                Logger.LogInfo("[view] vp_FPCamera=" + (vp == null ? "null" :
                    vp.GetType().Name + " on " + vp.gameObject.name));

                if (vp != null)
                {
                    CacheVpReflection(vp.GetType());
                    Logger.LogInfo("[view] reflection: m_Pitch="
                        + (ReferenceEquals(_fiPitch, null) ? "missing" : "ok")
                        + " m_Yaw=" + (ReferenceEquals(_fiYaw, null) ? "missing" : "ok")
                        + " SetRotation=" + (ReferenceEquals(_miSetRot, null) ? "missing" : "ok"));
                }

                float pp, yy;
                if (TryGetUfpsRotation(out pp, out yy))
                {
                    _savedPitch = pp; _savedYaw = yy; _savedPresent = true;
                    Logger.LogInfo("[view] saved  pitch=" + pp.ToString("F1", Inv)
                                   + " yaw=" + yy.ToString("F1", Inv));
                }
                else
                {
                    Logger.LogWarning("[view] could not read UFPS rotation");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("[view] TrySaveView threw: "
                    + ex.GetType().Name + " " + ex.Message
                    + "\n" + ex.StackTrace);
            }
        }

        private void TryApplyView()
        {
            TrySetUfpsRotation(_savedPitch, _savedYaw);
        }

        private void ArmRestoreView()
        {
            if (!_savedPresent) return;
            // Animation/dialog typically takes ~3-5s. Start clamping shortly
            // after the click so the watch-fish camera doesn't fight us, then
            // hold the saved pose for the rest of the window.
            _restoreUntil = Time.realtimeSinceStartup + 6f;
        }

        // ---- on-screen overlay (visible to Steam Remote Play) -------------

        private static Texture2D _whitePx;
        private GUIStyle _styleTitle, _styleLabel, _styleBanner;

        private static Texture2D WhitePixel()
        {
            if (_whitePx == null)
            {
                _whitePx = new Texture2D(1, 1);
                _whitePx.SetPixel(0, 0, Color.white);
                _whitePx.Apply();
            }
            return _whitePx;
        }

        private void EnsureStyles()
        {
            if (_styleTitle != null) return;
            _styleTitle = new GUIStyle();
            _styleTitle.fontSize = 18;
            _styleTitle.fontStyle = FontStyle.Bold;
            _styleTitle.normal.textColor = Color.white;
            _styleLabel = new GUIStyle();
            _styleLabel.fontSize = 13;
            _styleLabel.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
            _styleBanner = new GUIStyle();
            _styleBanner.fontSize = 13;
            _styleBanner.fontStyle = FontStyle.Bold;
            _styleBanner.normal.textColor = Color.white;
        }

        private static Color StateColor(string s)
        {
            switch (s)
            {
                case "IDLE":         return new Color(0.6f, 0.6f, 0.6f);
                case "AUTOCAST":     return new Color(1f, 0.78f, 0f);
                case "CASTING":      return new Color(0f, 0.78f, 1f);
                case "WAITING":      return new Color(0f, 0.86f, 0f);
                case "BITE":         return new Color(1f, 0.53f, 0f);
                case "HOOK":         return new Color(1f, 0.19f, 0.19f);
                case "FIGHT":        return new Color(1f, 0f, 0.25f);
                case "LANDED":       return new Color(0.63f, 0.44f, 1f);
                case "CATCH_DIALOG": return new Color(0.78f, 0f, 0.78f);
                default:             return Color.gray;
            }
        }

        private void FillRect(float x, float y, float w, float h, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(x, y, w, h), WhitePixel());
            GUI.color = prev;
        }

        private void OnGUI()
        {
            if (!_hudVisible) return;
            EnsureStyles();

            // Pull live game state — same null guards as Emit().
            var gc = GameController.Instance;
            FishingHands hands = (gc != null && gc.fishingPlayer != null)
                ? gc.fishingPlayer.currentHands : null;
            FishingFloat f = hands != null ? hands.fishingFloat : null;
            FishingLine line = hands != null ? hands.fishingLine : null;
            var fp = gc != null ? gc.fishingPlayer : null;

            bool watchFish = fp != null && fp.currentState.ToString() == "WATCH_FISH";
            bool hasFish = fp != null && fp.fish != null;
            bool hasJunk = fp != null && fp.junk != null;
            float tension = line != null ? line.currentTension : 0f;
            bool isOnWater = f != null && f.isOnWater;
            bool isBite = f != null && f.isTryAnimation;

            bool botStale = (Time.realtimeSinceStartup - _hudStatusTime) > 2f;
            string botState = botStale ? "..." : _botState;
            bool autoOn = _botAuto && !botStale;

            const int W = 260;
            const int H = 180;
            const int X = 16;
            int Y = Mathf.Max(16, (Screen.height - H) / 2);

            // Background
            FillRect(X, Y, W, H, new Color(0.04f, 0.04f, 0.04f, 0.78f));

            // Auto banner
            FillRect(X, Y, W, 22,
                autoOn ? new Color(0.06f, 0.13f, 0.06f, 0.95f)
                       : new Color(0.10f, 0.10f, 0.10f, 0.95f));
            _styleBanner.normal.textColor = autoOn
                ? new Color(0f, 0.86f, 0f)
                : new Color(0.78f, 0.78f, 0.78f);
            GUI.Label(new Rect(X + 8, Y + 3, W - 16, 18),
                autoOn ? "[AUTO ON]   Delete to pause"
                       : "[AUTO OFF]  Delete to start",
                _styleBanner);

            // State (big)
            _styleTitle.normal.textColor = StateColor(botState);
            GUI.Label(new Rect(X + 8, Y + 26, W - 16, 24), botState, _styleTitle);

            // Badges
            string badges = "";
            if (hasFish) badges += " FISH ";
            if (hasJunk) badges += " JUNK ";
            if (watchFish) badges += " WATCH ";
            if (isBite && !hasFish) badges += " BITE ";
            _styleLabel.normal.textColor = hasFish ? new Color(1f, 0.3f, 0.3f)
                                                   : new Color(0.85f, 0.85f, 0.85f);
            GUI.Label(new Rect(X + 8, Y + 54, W - 16, 16), badges, _styleLabel);

            // Tension bar
            _styleLabel.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
            GUI.Label(new Rect(X + 8, Y + 72, W - 16, 16),
                "tension " + tension.ToString("F2", Inv), _styleLabel);

            const int barX = X + 8;
            int barY = Y + 92;
            const int barW = W - 16;
            const int barH = 8;
            FillRect(barX, barY, barW, barH, new Color(0.1f, 0.1f, 0.1f));
            Color tc = tension >= 1f ? new Color(1f, 0.19f, 0.19f)
                     : tension >= 0.8f ? new Color(1f, 0.82f, 0f)
                     : new Color(0.23f, 0.6f, 0.23f);
            FillRect(barX, barY, barW * Mathf.Clamp01(tension), barH, tc);
            // 0.8 marker
            FillRect(barX + barW * 0.8f, barY, 1, barH, new Color(0.5f, 0.5f, 0.5f));

            // Action
            string actionLabel; Color actionColor;
            if (_botAction == "reel") { actionLabel = "* REEL  (LMB DOWN)"; actionColor = new Color(1f, 0.19f, 0.19f); }
            else if (_botAction == "ease") { actionLabel = "^ EASE  (LMB UP)"; actionColor = new Color(1f, 0.82f, 0f); }
            else if (_botAction == "paused") { actionLabel = "|| paused"; actionColor = new Color(0.5f, 0.5f, 0.5f); }
            else { actionLabel = "-"; actionColor = new Color(0.5f, 0.5f, 0.5f); }
            _styleLabel.normal.textColor = actionColor;
            GUI.Label(new Rect(X + 8, Y + 106, W - 16, 16), actionLabel, _styleLabel);

            // Saved view (Backquote ` to capture; restored after sell)
            bool restoreActive = _savedPresent
                && Time.realtimeSinceStartup < _restoreUntil;
            if (_savedPresent)
            {
                _styleLabel.normal.textColor = restoreActive
                    ? new Color(0.4f, 1f, 0.4f)         // bright green while restoring
                    : new Color(0.55f, 0.85f, 1f);      // cyan when armed
                string viewLabel = (restoreActive ? "* RESTORING " : "view ")
                    + "yaw=" + _savedYaw.ToString("F1", Inv)
                    + " pitch=" + _savedPitch.ToString("F1", Inv);
                GUI.Label(new Rect(X + 8, Y + 126, W - 16, 16), viewLabel, _styleLabel);
            }
            else
            {
                _styleLabel.normal.textColor = new Color(0.45f, 0.45f, 0.45f);
                GUI.Label(new Rect(X + 8, Y + 126, W - 16, 16),
                    "view: press ` to save", _styleLabel);
            }

            // Footer
            _styleLabel.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
            GUI.Label(new Rect(X + 8, Y + 144, W - 16, 16),
                "water=" + (isOnWater ? "1" : "0") + "  bot " + _botFps.ToString("F1", Inv) + " fps",
                _styleLabel);

            if (botStale)
            {
                _styleLabel.normal.textColor = new Color(1f, 0.4f, 0.4f);
                GUI.Label(new Rect(X + 8, Y + 162, W - 16, 16),
                    "bot disconnected", _styleLabel);
            }

            // ---- Zone reco panel (right under the status box) -------------
            if (_zoneRows == null || _zoneRows.Count == 0) return;

            const int ZW = 360;
            const int rowH = 32; // header line + bait line per species
            int titleH = 22;
            int ZH = titleH + 6 + rowH * _zoneRows.Count + 6;
            int ZX = X;
            int ZY = Y + H + 8;
            // If we'd run off the bottom, lift the whole panel up.
            if (ZY + ZH > Screen.height - 8)
                ZY = Math.Max(8, Screen.height - 8 - ZH);

            FillRect(ZX, ZY, ZW, ZH, new Color(0.04f, 0.04f, 0.04f, 0.78f));
            FillRect(ZX, ZY, ZW, titleH, new Color(0.10f, 0.10f, 0.10f, 0.95f));
            _styleBanner.normal.textColor = new Color(0.55f, 0.85f, 1f);
            float ageSec = Time.realtimeSinceStartup - _lastZoneRecoT;
            GUI.Label(new Rect(ZX + 8, ZY + 3, ZW - 16, 18),
                "ZONE RECO  (" + _zoneRows.Count + " species)  +"
                + ageSec.ToString("F0", Inv) + "s",
                _styleBanner);

            for (int i = 0; i < _zoneRows.Count; i++)
            {
                var r = _zoneRows[i];
                int ry = ZY + titleH + 4 + i * rowH;
                _styleLabel.normal.textColor = new Color(1f, 0.95f, 0.6f);
                string head = r.ko
                    + "  x" + r.spawnerCount
                    + "  " + r.weightRange.x.ToString("F1", Inv)
                    + "~" + r.weightRange.y.ToString("F1", Inv) + "kg"
                    + "  " + r.depthStr;
                GUI.Label(new Rect(ZX + 8, ry, ZW - 16, 16), head, _styleLabel);
                _styleLabel.normal.textColor = new Color(0.78f, 0.92f, 0.78f);
                GUI.Label(new Rect(ZX + 14, ry + 14, ZW - 22, 16),
                    r.baitsStr, _styleLabel);
            }
        }

        private void BeginCmdReceive()
        {
            if (_cmdUdp == null) return;
            try
            {
                _cmdUdp.BeginReceive(OnCmdReceived, null);
            }
            catch { }
        }

        // Mono 2017 (Unity 2017's runtime) lacks the Monitor.Enter(object, ref bool)
        // overload, which is what the modern C# `lock` statement compiles to when
        // targeting net46. Use the legacy single-arg Monitor.Enter via manual
        // try/finally so the IL stays compatible.
        private static void EnterLock(object o) { Monitor.Enter(o); }
        private static void ExitLock(object o) { Monitor.Exit(o); }

        private void OnCmdReceived(IAsyncResult ar)
        {
            try
            {
                IPEndPoint ep = null;
                var data = _cmdUdp.EndReceive(ar, ref ep);
                var msg = Encoding.UTF8.GetString(data).Trim();
                if (!string.IsNullOrEmpty(msg))
                {
                    EnterLock(_cmdLock);
                    try { _pendingCmds.Enqueue(msg); }
                    finally { ExitLock(_cmdLock); }
                }
            }
            catch { }
            finally { BeginCmdReceive(); }
        }

        private void DrainCommands()
        {
            while (true)
            {
                string cmd = null;
                EnterLock(_cmdLock);
                try
                {
                    if (_pendingCmds.Count == 0) return;
                    cmd = _pendingCmds.Dequeue();
                }
                finally { ExitLock(_cmdLock); }
                try { Execute(cmd); }
                catch (Exception ex) { Logger.LogError("cmd " + cmd + " failed: " + ex.Message); }
            }
        }

        private void Execute(string cmd)
        {
            // Bot pushes a small status JSON for the overlay. Format:
            //   "S|<state>|<action>|<auto:0/1>|<fps>"
            // Pipe-delimited so we don't need a JSON parser in the plugin.
            if (cmd.Length > 2 && cmd[0] == 'S' && cmd[1] == '|')
            {
                var parts = cmd.Split('|');
                if (parts.Length >= 5)
                {
                    _botState = parts[1];
                    _botAction = parts[2];
                    _botAuto = parts[3] == "1";
                    float f;
                    if (float.TryParse(parts[4], System.Globalization.NumberStyles.Float, Inv, out f))
                        _botFps = f;
                    _hudStatusTime = Time.realtimeSinceStartup;
                }
                return;
            }
            if (cmd == "hud:hide") { _hudVisible = false; return; }
            if (cmd == "hud:show") { _hudVisible = true; return; }
            if (cmd == "zone:probe") { ProbeZone(); return; }
            if (cmd == "zone:dump")  { DumpZone();  return; }
            if (cmd == "fish:probe") { ProbeFishSpawner(); return; }
            if (cmd == "fish:fields") { ProbeFishType(); return; }
            if (cmd == "fish:terrain") { ProbeFishTerrainMods(); return; }
            if (cmd == "bait:probe") { ProbeBaitType(); return; }
            if (cmd == "bait:likes") { ProbeFishLikesParams(); return; }
            if (cmd == "bait:interests") { ProbeFishInterest(); return; }
            if (cmd == "species:enum") { ProbeSpeciesEnum(); return; }
            if (cmd == "zone:reco") { ZoneReco(); return; }
            if (cmd == "loc:test") { ProbeLocalization(); return; }
            if (cmd == "loc:bait") { ProbeBaitLocalization(); return; }
            if (cmd == "loc:terms") { ProbeTermList(); return; }
            if (cmd == "bait:all") { ProbeBaitAllFields(); return; }
            if (cmd == "equip:probe") { ProbeEquipmentObject(); return; }
            if (cmd == "equip:mgr") { ProbeEquipmentManager(); return; }
            if (cmd == "equip:catalog") { ProbeCatalogEntries(); return; }
            if (cmd == "equip:natural") { ProbeNaturalBaits(); return; }
            if (cmd == "nat:check") { ProbeNaturalBaitComponents(); return; }
            if (cmd == "fish:likes") { ProbeFishLikesMembers(); return; }
            if (cmd.StartsWith("il:")) { ProbeMethodIL(cmd.Substring(3)); return; }
            if (cmd == "boilie:probe") { ProbeBoilie(); return; }
            if (cmd == "baitpart:probe") { ProbeBaitPart(); return; }
            if (cmd == "equip:full") { ProbeEquipmentObjectFull(); return; }
            if (cmd == "hook:cm") { ProbeHookSizesCm(); return; }
            if (cmd == "equip:params") { ProbeEquipmentParameters(); return; }

            var hud = HUDManager.Instance;
            if (hud == null || hud.hudWatchFish == null)
            {
                Logger.LogWarning("cmd '" + cmd + "': watch-fish HUD not available");
                return;
            }
            switch (cmd)
            {
                case "sell":
                    if (hud.hudWatchFish.watchSellBtn != null)
                    {
                        hud.hudWatchFish.watchSellBtn.onClick.Invoke();
                        Logger.LogInfo("invoked sell");
                        ArmRestoreView();
                    }
                    break;
                case "release":
                    if (hud.hudWatchFish.watchReleaseBtn != null)
                    {
                        hud.hudWatchFish.watchReleaseBtn.onClick.Invoke();
                        Logger.LogInfo("invoked release");
                        ArmRestoreView();
                    }
                    break;
                case "take":
                    if (hud.hudWatchFish.watchTakeBtn != null)
                    {
                        hud.hudWatchFish.watchTakeBtn.onClick.Invoke();
                        Logger.LogInfo("invoked take");
                        ArmRestoreView();
                    }
                    break;
                default:
                    Logger.LogWarning("unknown cmd: " + cmd);
                    break;
            }
        }

        // ---------------------------------------------------------------
        // Zone / fish-spawner discovery + dump.
        //
        // We don't know the exact type names ahead of time, so the probe
        // scans every loaded type for tell-tale field/property names and
        // logs candidates. Run it once via `zone:probe`, read BepInEx.log,
        // then refine `DumpZone` to actually walk the live data.
        // ---------------------------------------------------------------
        private static readonly string[] ZoneNeedles =
            { "currentActiveZone", "fishSpawners", "iceFishSpawners", "zoneName" };
        private static readonly string[] FishLikeNeedles =
            { "LikesBait", "LikesBoilie", "LikesPlant", "LikesTerrain",
              "likesBait", "likesBoilie", "likesPlant", "likesTerrain" };

        private void ProbeZone()
        {
            Logger.LogInfo("[zone:probe] === scanning loaded assemblies ===");
            int matched = 0;
            try
            {
                var asms = AppDomain.CurrentDomain.GetAssemblies();
                foreach (var asm in asms)
                {
                    string asmName;
                    try { asmName = asm.GetName().Name; }
                    catch { continue; }
                    if (asmName != "Assembly-CSharp" && asmName != "Assembly-CSharp-firstpass")
                        continue;
                    Type[] types;
                    try { types = asm.GetTypes(); }
                    catch (System.Reflection.ReflectionTypeLoadException rex)
                    { types = rex.Types; }
                    foreach (var t in types)
                    {
                        if (ReferenceEquals(t, null)) continue;
                        var fis = t.GetFields(
                            System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.NonPublic |
                            System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.Static);
                        var hits = new System.Collections.Generic.List<string>();
                        foreach (var fi in fis)
                        {
                            string fn = fi.Name;
                            for (int i = 0; i < ZoneNeedles.Length; i++)
                                if (fn.IndexOf(ZoneNeedles[i], StringComparison.OrdinalIgnoreCase) >= 0)
                                { hits.Add(fn + ":" + fi.FieldType.Name); break; }
                            for (int i = 0; i < FishLikeNeedles.Length; i++)
                                if (fn.IndexOf(FishLikeNeedles[i], StringComparison.OrdinalIgnoreCase) >= 0)
                                { hits.Add(fn + ":" + fi.FieldType.Name); break; }
                        }
                        if (hits.Count > 0)
                        {
                            matched++;
                            Logger.LogInfo("[zone:probe] " + t.FullName
                                + " {" + string.Join(", ", hits.ToArray()) + "}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("[zone:probe] " + ex.GetType().Name + ": " + ex.Message);
            }
            Logger.LogInfo("[zone:probe] === " + matched + " candidate types ===");
            ProbeLiveZoneObjects();
        }

        private void ProbeLiveZoneObjects()
        {
            // Find live MonoBehaviours whose type name suggests it's a zone
            // or fish spawner manager. Use Resources.FindObjectsOfTypeAll
            // so we also pick up disabled/inactive scene objects.
            try
            {
                var all = Resources.FindObjectsOfTypeAll(typeof(MonoBehaviour));
                int n = 0;
                foreach (var o in all)
                {
                    if (ReferenceEquals(o, null)) continue;
                    string tn = o.GetType().Name;
                    if (tn.IndexOf("Zone", StringComparison.OrdinalIgnoreCase) < 0
                        && tn.IndexOf("Spawner", StringComparison.OrdinalIgnoreCase) < 0
                        && tn.IndexOf("FishingMan", StringComparison.OrdinalIgnoreCase) < 0
                        && tn.IndexOf("LakeMan", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    var go = (o as MonoBehaviour);
                    string goName = go != null && go.gameObject != null
                        ? go.gameObject.name : "?";
                    Logger.LogInfo("[zone:probe.live] " + o.GetType().FullName
                        + "  on GameObject '" + goName + "'");
                    n++;
                    if (n > 60) { Logger.LogInfo("[zone:probe.live] ...truncated"); break; }
                }
                Logger.LogInfo("[zone:probe.live] === " + n + " live components ===");
            }
            catch (Exception ex)
            {
                Logger.LogError("[zone:probe.live] " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void DumpZone()
        {
            try
            {
                // 1. Current zone name via EnviroWeather.currentActiveZone.
                string zoneName = "?";
                var envType = FindType("EnviroWeather");
                if (!ReferenceEquals(envType, null))
                {
                    var envInsts = UnityEngine.Object.FindObjectsOfType(envType);
                    var envInst = (envInsts != null && envInsts.Length > 0) ? envInsts[0] : null;
                    if (!ReferenceEquals(envInst, null))
                    {
                        var fiZone = envType.GetField("currentActiveZone",
                            System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.NonPublic |
                            System.Reflection.BindingFlags.Instance);
                        if (!ReferenceEquals(fiZone, null))
                        {
                            var zone = fiZone.GetValue(envInst);
                            if (!ReferenceEquals(zone, null))
                            {
                                var fiName = zone.GetType().GetField("zoneName",
                                    System.Reflection.BindingFlags.Public |
                                    System.Reflection.BindingFlags.NonPublic |
                                    System.Reflection.BindingFlags.Instance);
                                if (!ReferenceEquals(fiName, null))
                                {
                                    var v = fiName.GetValue(zone) as string;
                                    if (!string.IsNullOrEmpty(v)) zoneName = v;
                                }
                            }
                        }
                    }
                }
                Logger.LogInfo("[zone:dump] zone='" + zoneName + "'");

                // 2. Active fish spawners — group by species parsed from
                //    the GameObject name 'FishSpawner_<Species> (n)'.
                var fsType = FindType("FishSpawner");
                if (ReferenceEquals(fsType, null))
                {
                    Logger.LogWarning("[zone:dump] FishSpawner type not found");
                    return;
                }
                var counts = new System.Collections.Generic.Dictionary<string, int>();
                var actives = new System.Collections.Generic.Dictionary<string, int>();
                var spawners = UnityEngine.Object.FindObjectsOfType(fsType);
                foreach (var sp in spawners)
                {
                    var mb = sp as MonoBehaviour;
                    if (ReferenceEquals(mb, null) || mb.gameObject == null) continue;
                    string n = mb.gameObject.name;
                    string species = ParseSpecies(n);
                    if (!counts.ContainsKey(species)) counts[species] = 0;
                    counts[species]++;
                    if (mb.gameObject.activeInHierarchy && mb.enabled)
                    {
                        if (!actives.ContainsKey(species)) actives[species] = 0;
                        actives[species]++;
                    }
                }
                Logger.LogInfo("[zone:dump] " + spawners.Length + " spawners total");
                foreach (var kv in counts)
                {
                    int act = 0; actives.TryGetValue(kv.Key, out act);
                    Logger.LogInfo("[zone:dump]   " + kv.Key
                        + "  total=" + kv.Value + "  active=" + act);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("[zone:dump] " + ex.GetType().Name + ": " + ex.Message
                    + "\n" + ex.StackTrace);
            }
        }

        private static string ParseSpecies(string goName)
        {
            // 'FishSpawner_BullTrout (1)' -> 'BullTrout'
            const string pfx = "FishSpawner_";
            int p = goName.IndexOf(pfx, StringComparison.Ordinal);
            if (p < 0) return goName;
            string s = goName.Substring(p + pfx.Length);
            int sp = s.IndexOf(' ');
            if (sp > 0) s = s.Substring(0, sp);
            return s;
        }

        private static Type FindType(string simpleName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (System.Reflection.ReflectionTypeLoadException rex) { types = rex.Types; }
                catch { continue; }
                foreach (var t in types)
                {
                    if (ReferenceEquals(t, null)) continue;
                    if (t.Name == simpleName) return t;
                }
            }
            return null;
        }

        private void ProbeBaitLocalization()
        {
            // Try every plausible I2 term pattern for a known catalog entry
            // (HOOK_STREM_MIKADO_01 a.k.a. KKW-5).
            EnsureLoc();
            string id = "HOOK_STREM_MIKADO_01";
            string nm = "Cajun Chatterbait"; // also test by raw name
            string[] patterns = {
                id, "EQUIPMENT/" + id, "EQUIPMENT/" + id + "_NAME",
                "EQUIPMENT/NAME/" + id, "BAIT/" + id, "BAIT/" + id + "_NAME",
                "ITEM/" + id, "EquipmentName/" + id,
                nm, "EQUIPMENT/" + nm,
            };
            foreach (var p in patterns)
            {
                string r = Tr(p);
                Logger.LogInfo("[loc:bait]   '" + p + "' -> '" + r + "'"
                    + (r != p ? "  ✓" : ""));
            }
        }

        private void ProbeTermList()
        {
            // Dump categories + sample of EQUIPMENT/* terms so we know if any
            // localization at all exists for shop items.
            EnsureLoc();
            try
            {
                var lmT = FindType("LocalizationManager");
                if (ReferenceEquals(lmT, null)) return;
                var mCats = lmT.GetMethod("GetCategories",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Static);
                var mTerms = lmT.GetMethod("GetTermsList",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Static);
                if (!ReferenceEquals(mCats, null))
                {
                    var cats = mCats.Invoke(null, null) as System.Collections.IEnumerable;
                    if (cats != null)
                    {
                        Logger.LogInfo("[loc:terms] categories:");
                        int i = 0;
                        foreach (var c in cats)
                        {
                            Logger.LogInfo("[loc:terms]   " + c);
                            if (++i > 30) break;
                        }
                    }
                }
                if (!ReferenceEquals(mTerms, null))
                {
                    var terms = mTerms.Invoke(null, null) as System.Collections.IEnumerable;
                    if (terms != null)
                    {
                        Logger.LogInfo("[loc:terms] EQUIPMENT/HOOK_* terms (first 25):");
                        int n = 0, total = 0;
                        foreach (var t in terms)
                        {
                            string s = t == null ? "" : t.ToString();
                            total++;
                            if (s.IndexOf("EQUIPMENT", StringComparison.Ordinal) >= 0
                                || s.IndexOf("BAIT", StringComparison.Ordinal) >= 0
                                || s.IndexOf("HOOK", StringComparison.Ordinal) >= 0)
                            {
                                if (n < 25) Logger.LogInfo("[loc:terms]   " + s);
                                n++;
                            }
                        }
                        Logger.LogInfo("[loc:terms] " + n + " matched / " + total + " total terms");
                    }
                }
            }
            catch (Exception ex)
            { Logger.LogError("[loc:terms] " + ex.GetType().Name + ": " + ex.Message); }
        }

        private void ProbeLocalization()
        {
            try
            {
                var lmT = FindType("LocalizationManager");
                if (ReferenceEquals(lmT, null))
                {
                    Logger.LogWarning("[loc:test] LocalizationManager type not found");
                    return;
                }
                Logger.LogInfo("[loc:test] LocalizationManager FullName=" + lmT.FullName);

                // Show current language and a list of methods worth calling.
                foreach (var p in lmT.GetProperties(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Static))
                {
                    if (p.Name.IndexOf("Language", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    object v = null;
                    try { v = p.GetValue(null, null); } catch { }
                    Logger.LogInfo("[loc:test]   " + p.Name + " = " + (v == null ? "null" : v.ToString()));
                }

                // List every static method on LocalizationManager so we can
                // see what's actually exported in this build of I2.Loc.
                Logger.LogInfo("[loc:test] -- static methods --");
                foreach (var m in lmT.GetMethods(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Static))
                {
                    if (m.Name.IndexOf("Trans", StringComparison.OrdinalIgnoreCase) < 0
                        && m.Name.IndexOf("Term", StringComparison.OrdinalIgnoreCase) < 0
                        && m.Name.IndexOf("Get", StringComparison.OrdinalIgnoreCase) != 0)
                        continue;
                    var ps2 = m.GetParameters();
                    var sb2 = new StringBuilder();
                    sb2.Append(m.ReturnType.Name).Append(" ").Append(m.Name).Append("(");
                    for (int i = 0; i < ps2.Length; i++)
                    {
                        if (i > 0) sb2.Append(", ");
                        sb2.Append(ps2[i].ParameterType.Name).Append(" ").Append(ps2[i].Name);
                    }
                    sb2.Append(")");
                    Logger.LogInfo("[loc:test]   " + sb2);
                }

                // Pick the most permissive translation method whose first
                // parameter is string (could be GetTermTranslation, etc).
                System.Reflection.MethodInfo mGet = null;
                foreach (var m in lmT.GetMethods(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Static))
                {
                    if (m.Name != "GetTranslation" && m.Name != "GetTermTranslation"
                        && m.Name != "TryGetTermTranslation" && m.Name != "TryGetTranslation")
                        continue;
                    if (!ReferenceEquals(m.ReturnType, typeof(string))) continue;
                    var ps = m.GetParameters();
                    if (ps.Length < 1) continue;
                    if (!ReferenceEquals(ps[0].ParameterType, typeof(string))) continue;
                    // verify all extra params have defaults
                    bool ok = true;
                    for (int i = 1; i < ps.Length; i++)
                        if (!ps[i].IsOptional) { ok = false; break; }
                    if (!ok) continue;
                    if (ReferenceEquals(mGet, null) || ps.Length < mGet.GetParameters().Length)
                        mGet = m;
                }
                if (ReferenceEquals(mGet, null))
                {
                    Logger.LogWarning("[loc:test] GetTranslation(string) not found");
                    return;
                }
                var sigSb = new StringBuilder();
                sigSb.Append(mGet.Name).Append("(");
                var pps = mGet.GetParameters();
                for (int i = 0; i < pps.Length; i++)
                {
                    if (i > 0) sigSb.Append(",");
                    sigSb.Append(pps[i].ParameterType.Name);
                }
                sigSb.Append(")");
                Logger.LogInfo("[loc:test] using " + sigSb);

                string[] terms = new string[] {
                    "FISH/BULL_TROUT_NAME",
                    "FISH/RAINBOW_TROUT_NAME",
                    "FISH/BROWN_TROUT_NAME",
                    "FISH/CUTTHROAT_TROUT_NAME",
                    "FISH/BROOK_TROUT_NAME",
                };
                var psCall = mGet.GetParameters();
                foreach (var t in terms)
                {
                    object[] args = new object[psCall.Length];
                    args[0] = t;
                    for (int i = 1; i < psCall.Length; i++)
                        args[i] = psCall[i].DefaultValue == DBNull.Value ? null : psCall[i].DefaultValue;
                    object r;
                    try { r = mGet.Invoke(null, args); }
                    catch (Exception ex) { Logger.LogError("[loc:test] " + t + " -> " + ex.GetType().Name + ": " + ex.Message); continue; }
                    Logger.LogInfo("[loc:test]   " + t + " -> '" + (r == null ? "<null>" : r.ToString()) + "'");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("[loc:test] " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void ProbeEquipmentParameters()
        {
            try
            {
                var eqT = FindType("EquipmentObject");
                if (ReferenceEquals(eqT, null)) return;
                Type pT = null;
                foreach (var nt in eqT.GetNestedTypes(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic))
                { if (nt.Name == "Parameter") { pT = nt; break; } }
                if (ReferenceEquals(pT, null)) { Logger.LogWarning("[equip:params] Parameter not found"); return; }
                Logger.LogInfo("[equip:params] Parameter FullName=" + pT.FullName);
                var pfis = pT.GetFields(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                foreach (var fi in pfis)
                    Logger.LogInfo("[equip:params]   field " + fi.Name + " : " + fi.FieldType.FullName);

                // Pull parameters of first 3 spinners + first 3 normalHooks.
                var mgrT = FindType("EquipmentManager");
                var live = UnityEngine.Object.FindObjectsOfType(mgrT);
                if (live == null || live.Length == 0) return;
                var fiParams = eqT.GetField("parameters",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                var fiName = eqT.GetField("equipmentName",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                if (ReferenceEquals(fiParams, null)) return;

                string[] cats = { "equipmentSpinners", "equipmentNormalHooks" };
                foreach (var c in cats)
                {
                    var lf = mgrT.GetField(c,
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);
                    if (ReferenceEquals(lf, null)) continue;
                    var list = lf.GetValue(live[0]) as System.Collections.IEnumerable;
                    if (list == null) continue;
                    Logger.LogInfo("[equip:params] === " + c + " ===");
                    int i = 0;
                    foreach (var entry in list)
                    {
                        if (i >= 3) break;
                        string nm = ReferenceEquals(fiName, null) ? "?" : (fiName.GetValue(entry) as string ?? "?");
                        var ps = fiParams.GetValue(entry) as System.Collections.IEnumerable;
                        Logger.LogInfo("[equip:params]   [" + i + "] " + nm);
                        if (ps != null)
                        {
                            int j = 0;
                            foreach (var p in ps)
                            {
                                var sb = new StringBuilder();
                                foreach (var fi in pfis)
                                {
                                    object v;
                                    try { v = fi.GetValue(p); } catch { continue; }
                                    sb.Append(fi.Name).Append("=");
                                    if (v == null) sb.Append("null");
                                    else if (v is string) sb.Append("'").Append(v).Append("'");
                                    else sb.Append(v);
                                    sb.Append(", ");
                                }
                                Logger.LogInfo("[equip:params]     param[" + j + "] " + sb);
                                j++;
                            }
                        }
                        i++;
                    }
                }
            }
            catch (Exception ex)
            { Logger.LogError("[equip:params] " + ex.GetType().Name + ": " + ex.Message); }
        }

        private void ProbeHookSizesCm()
        {
            try
            {
                var mgrT = FindType("EquipmentManager");
                if (ReferenceEquals(mgrT, null)) return;
                var live = UnityEngine.Object.FindObjectsOfType(mgrT);
                if (live == null || live.Length == 0) return;
                var fi = mgrT.GetField("hookSizesCm",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                if (ReferenceEquals(fi, null)) { Logger.LogWarning("[hook:cm] field not found"); return; }
                var list = fi.GetValue(live[0]) as System.Collections.IEnumerable;
                if (list == null) return;
                int idx = 0;
                Logger.LogInfo("[hook:cm] hookSizesCm:");
                foreach (var v in list)
                {
                    Logger.LogInfo("[hook:cm]   [" + idx + "] = " + Convert.ToSingle(v).ToString("F2", Inv) + " cm");
                    idx++;
                }
                var fiCur = mgrT.GetField("currentHookSize",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                if (!ReferenceEquals(fiCur, null))
                {
                    var cv = fiCur.GetValue(live[0]);
                    Logger.LogInfo("[hook:cm] currentHookSize = " + cv);
                }
            }
            catch (Exception ex)
            { Logger.LogError("[hook:cm] " + ex.GetType().Name + ": " + ex.Message); }
        }

        private void ProbeEquipmentObjectFull()
        {
            try
            {
                var eqT = FindType("EquipmentObject");
                if (ReferenceEquals(eqT, null)) return;
                var fis = eqT.GetFields(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                Logger.LogInfo("[equip:full] EquipmentObject all " + fis.Length + " fields:");
                foreach (var fi in fis)
                    Logger.LogInfo("[equip:full]   " + fi.Name + " : " + fi.FieldType.FullName);

                // Pull one catalog spinner and show every value (including refs).
                var mgrT = FindType("EquipmentManager");
                if (ReferenceEquals(mgrT, null)) return;
                var live = UnityEngine.Object.FindObjectsOfType(mgrT);
                if (live == null || live.Length == 0) return;
                var fiSp = mgrT.GetField("equipmentSpinners",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                if (ReferenceEquals(fiSp, null)) return;
                var spList = fiSp.GetValue(live[0]) as System.Collections.IEnumerable;
                if (spList == null) return;
                object first = null;
                foreach (var e in spList) { first = e; break; }
                if (first == null) return;
                Logger.LogInfo("[equip:full] sample catalog spinner [0]:");
                foreach (var fi in fis)
                {
                    object v;
                    try { v = fi.GetValue(first); } catch { continue; }
                    string sval;
                    if (v == null) sval = "null";
                    else if (v is string) sval = "'" + v + "'";
                    else if (v is UnityEngine.Object) sval = "<UO " + ((UnityEngine.Object)v).name + ">";
                    else if (v.GetType().IsPrimitive || v.GetType().IsEnum) sval = v.ToString();
                    else if (v is System.Collections.ICollection)
                        sval = "<" + v.GetType().Name + " count=" + ((System.Collections.ICollection)v).Count + ">";
                    else sval = "<" + v.GetType().Name + ">";
                    Logger.LogInfo("[equip:full]   " + fi.Name + " = " + sval);
                }
            }
            catch (Exception ex)
            { Logger.LogError("[equip:full] " + ex.GetType().Name + ": " + ex.Message); }
        }

        private void ProbeBaitPart()
        {
            try
            {
                var bpT = FindType("BaitPart");
                if (ReferenceEquals(bpT, null)) { Logger.LogWarning("[baitpart] type not found"); return; }
                Logger.LogInfo("[baitpart] FullName=" + bpT.FullName);
                foreach (var fi in bpT.GetFields(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance))
                    Logger.LogInfo("[baitpart]   field " + fi.Name + " : " + fi.FieldType.FullName);

                // Iterate every catalog natural-bait + boilie prefab and check
                // for a BaitPart / Boilie component.
                var mgrT = FindType("EquipmentManager");
                var eqT = FindType("EquipmentObject");
                if (ReferenceEquals(mgrT, null) || ReferenceEquals(eqT, null)) return;
                var live = Resources.FindObjectsOfTypeAll(mgrT);
                if (live == null || live.Length == 0) { Logger.LogWarning("[baitpart] no EquipmentManager"); return; }
                Logger.LogInfo("[baitpart] manager found");
                var fiPref = eqT.GetField("prefab",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                var fiTrName = eqT.GetField("equipmentTranslateName",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                var fiName = eqT.GetField("equipmentName",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                var boilieT = FindType("Boilie");

                string[] cats = { "equipmentBaits", "equipmentBoilies" };
                foreach (var c in cats)
                {
                    var lf = mgrT.GetField(c,
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);
                    if (ReferenceEquals(lf, null)) continue;
                    var list = lf.GetValue(live[0]) as System.Collections.IEnumerable;
                    if (list == null) continue;
                    Logger.LogInfo("[baitpart] === " + c + " ===");
                    foreach (var entry in list)
                    {
                        var pref = fiPref.GetValue(entry) as UnityEngine.GameObject;
                        if (ReferenceEquals(pref, null)) continue;
                        string nm = fiName.GetValue(entry) as string ?? "";
                        string trn = fiTrName.GetValue(entry) as string ?? "";
                        string ko = string.IsNullOrEmpty(trn) ? nm : Tr(trn);

                        var bp = pref.GetComponent(bpT);
                        if (ReferenceEquals(bp, null)) bp = pref.GetComponentInChildren(bpT);
                        UnityEngine.Component bo = null;
                        if (!ReferenceEquals(boilieT, null))
                        {
                            bo = pref.GetComponent(boilieT);
                            if (ReferenceEquals(bo, null)) bo = pref.GetComponentInChildren(boilieT);
                        }
                        Logger.LogInfo("[baitpart]   " + ko + " (" + pref.name + ")"
                            + "  BaitPart=" + (ReferenceEquals(bp, null) ? "no" : "YES")
                            + "  Boilie=" + (ReferenceEquals(bo, null) ? "no" : "YES"));
                    }
                }
            }
            catch (Exception ex)
            { Logger.LogError("[baitpart] " + ex.GetType().Name + ": " + ex.Message); }
        }

        private void ProbeBoilie()
        {
            try
            {
                var bT = FindType("Boilie");
                if (ReferenceEquals(bT, null)) { Logger.LogWarning("[boilie] type not found"); return; }
                Logger.LogInfo("[boilie] FullName=" + bT.FullName);
                foreach (var fi in bT.GetFields(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Static))
                {
                    string n = fi.Name.ToLowerInvariant();
                    if (n.IndexOf("interest", StringComparison.Ordinal) < 0
                        && n.IndexOf("like", StringComparison.Ordinal) < 0
                        && n.IndexOf("fish", StringComparison.Ordinal) < 0
                        && n.IndexOf("species", StringComparison.Ordinal) < 0
                        && n.IndexOf("taste", StringComparison.Ordinal) < 0
                        && n.IndexOf("flavor", StringComparison.Ordinal) < 0
                        && n.IndexOf("param", StringComparison.Ordinal) < 0
                        && n.IndexOf("type", StringComparison.Ordinal) < 0)
                        continue;
                    Logger.LogInfo("[boilie]   field " + fi.Name + " : " + fi.FieldType.FullName);
                }

                // Find a live Boilie instance via Resources lookup, then dump
                // values so we can see per-species interest.
                var liveBoilies = Resources.FindObjectsOfTypeAll(bT);
                Logger.LogInfo("[boilie] live count = " + liveBoilies.Length);
                if (liveBoilies.Length == 0) return;
                var sample = liveBoilies[0];
                var uo = sample as UnityEngine.Object;
                Logger.LogInfo("[boilie] sample = " + (uo != null ? uo.name : "?"));

                // Try invoking GetFishInterest for the active zone species.
                var fishT = FindType("Fish");
                Type spT = null;
                foreach (var nt in fishT.GetNestedTypes(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic))
                { if (nt.Name == "Species" && nt.IsEnum) { spT = nt; break; } }
                if (ReferenceEquals(spT, null)) return;
                var mGet = bT.GetMethod("GetFishInterest",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance,
                    null, new Type[] { spT }, null);
                if (ReferenceEquals(mGet, null)) { Logger.LogWarning("[boilie] GetFishInterest not found"); return; }

                Logger.LogInfo("[boilie] sample interest values:");
                int[] testSp = new int[] { 0, 1, 4, 16, 17, 25, 30, 32, 33 };
                string[] testNm = { "RAINBOW_TROUT","BROWN_TROUT","BULL_TROUT","PIKE","PERCH","LARGEMOUTH_BASS","CHUB","MIRROR_CARP","CRUCIAN_CARP" };
                for (int i = 0; i < testSp.Length; i++)
                {
                    object enumVal = Enum.ToObject(spT, testSp[i]);
                    object r;
                    try { r = mGet.Invoke(sample, new object[] { enumVal }); }
                    catch (Exception ex) { Logger.LogInfo("[boilie]   " + testNm[i] + " -> ERR " + ex.GetType().Name + ": " + (ex.InnerException == null ? ex.Message : ex.InnerException.Message)); continue; }
                    Logger.LogInfo("[boilie]   " + testNm[i] + " -> " + r);
                }
            }
            catch (Exception ex)
            { Logger.LogError("[boilie] " + ex.GetType().Name + ": " + ex.Message); }
        }

        // Tiny IL walker. We don't decode every opcode — just the metadata-
        // token forms (call, callvirt, newobj, ldfld, ldsfld, ldstr, ldtoken)
        // so we can see which fields/methods/types `LikesBait` consults.
        private void ProbeMethodIL(string spec)
        {
            // spec format: "Fish.LikesBait" or "Fish.LikesBait:GameObject".
            int colon = spec.IndexOf(':');
            string body = colon >= 0 ? spec.Substring(0, colon) : spec;
            int dot = body.LastIndexOf('.');
            if (dot < 0) { Logger.LogWarning("[il] need Type.Method"); return; }
            string typeName = body.Substring(0, dot);
            string methodName = body.Substring(dot + 1);
            try
            {
                var t = FindType(typeName);
                if (ReferenceEquals(t, null)) { Logger.LogWarning("[il] type not found: " + typeName); return; }
                System.Reflection.MethodInfo m = null;
                foreach (var cand in t.GetMethods(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Static))
                {
                    if (cand.Name != methodName) continue;
                    m = cand; break;
                }
                if (ReferenceEquals(m, null)) { Logger.LogWarning("[il] method not found: " + methodName); return; }

                var mb = m.GetMethodBody();
                if (mb == null) { Logger.LogWarning("[il] no body"); return; }
                Logger.LogInfo("[il] " + t.Name + "." + m.Name + "  locals=" + mb.LocalVariables.Count);
                foreach (var lv in mb.LocalVariables)
                    Logger.LogInfo("[il]   local " + lv.LocalIndex + " : " + lv.LocalType.FullName);

                var mod = m.Module;
                byte[] il = mb.GetILAsByteArray();
                int i = 0;
                while (i < il.Length)
                {
                    byte op = il[i];
                    int sz; string name = OpName(op, out sz);
                    if (sz == 5)
                    {
                        int tok = BitConverter.ToInt32(il, i + 1);
                        string sym = "?";
                        try
                        {
                            switch (op)
                            {
                                case 0x28: case 0x6F: case 0x73: // call/callvirt/newobj
                                    var mr = mod.ResolveMethod(tok);
                                    sym = (ReferenceEquals(mr.DeclaringType, null) ? "?" : mr.DeclaringType.Name) + "." + mr.Name;
                                    break;
                                case 0x7B: case 0x7E: case 0x7D: case 0x80: // ldfld/ldsfld/stfld/stsfld
                                    var fr = mod.ResolveField(tok);
                                    string dn = ReferenceEquals(fr.DeclaringType, null) ? "?" : fr.DeclaringType.Name;
                                    sym = dn + "." + fr.Name + " : " + fr.FieldType.Name;
                                    break;
                                case 0x72: // ldstr
                                    sym = "\"" + mod.ResolveString(tok) + "\"";
                                    break;
                                case 0xD0: // ldtoken
                                {
                                    var mb2 = mod.ResolveMember(tok);
                                    if (ReferenceEquals(mb2, null)) sym = "?";
                                    else if (ReferenceEquals(mb2.DeclaringType, null)) sym = mb2.Name;
                                    else sym = mb2.DeclaringType.Name + "." + mb2.Name;
                                    break;
                                }
                                case 0x74: case 0x75: // castclass/isinst
                                    sym = mod.ResolveType(tok).Name;
                                    break;
                            }
                        }
                        catch (Exception ex) { sym = "<" + ex.GetType().Name + ">"; }
                        Logger.LogInfo(string.Format("[il]   {0:X4}  {1,-12} {2}", i, name, sym));
                    }
                    else
                    {
                        // skip silently for trivial ops to keep noise down
                    }
                    i += sz;
                }
            }
            catch (Exception ex)
            { Logger.LogError("[il] " + ex.GetType().Name + ": " + ex.Message); }
        }

        private static string OpName(byte op, out int size)
        {
            // We only need to know operand size for each opcode. Default 1
            // (no operand). Below covers the 1-byte opcodes used here.
            size = 1;
            switch (op)
            {
                case 0x28: size = 5; return "call";
                case 0x6F: size = 5; return "callvirt";
                case 0x73: size = 5; return "newobj";
                case 0x7B: size = 5; return "ldfld";
                case 0x7E: size = 5; return "ldsfld";
                case 0x7D: size = 5; return "stfld";
                case 0x80: size = 5; return "stsfld";
                case 0x72: size = 5; return "ldstr";
                case 0xD0: size = 5; return "ldtoken";
                case 0x74: size = 5; return "castclass";
                case 0x75: size = 5; return "isinst";
                case 0x20: size = 5; return "ldc.i4"; // int32 imm
                case 0x22: size = 5; return "ldc.r4"; // float32 imm
                case 0x38: case 0x39: case 0x3A: case 0x3B: case 0x3C:
                case 0x3D: case 0x3E: case 0x3F: case 0x40: case 0x41:
                case 0x42: case 0x43: case 0x44: case 0x45:
                    size = 5; return "br/cond";
                case 0x2B: case 0x2C: case 0x2D: case 0x2E: case 0x2F:
                case 0x30: case 0x31: case 0x32: case 0x33: case 0x34:
                case 0x35: case 0x36: case 0x37:
                    size = 2; return "br/cond.s";
                case 0x1F: size = 2; return "ldc.i4.s";
                case 0x13: case 0x11: case 0x12: case 0x10: case 0x0E:
                case 0xFE: size = 2; return "ldarg/ldloc/...";
            }
            return "op_" + op.ToString("X2");
        }

        private void ProbeFishLikesMembers()
        {
            try
            {
                var fishT = FindType("Fish");
                if (ReferenceEquals(fishT, null)) return;
                Logger.LogInfo("[fish:likes] === fields with 'like'/'bait'/'boilie'/'plant'/'terrain' ===");
                foreach (var fi in fishT.GetFields(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Static))
                {
                    string n = fi.Name.ToLowerInvariant();
                    if (n.IndexOf("like", StringComparison.Ordinal) < 0
                        && n.IndexOf("boilie", StringComparison.Ordinal) < 0
                        && n.IndexOf("plant", StringComparison.Ordinal) < 0
                        && n.IndexOf("terrain", StringComparison.Ordinal) < 0
                        && n.IndexOf("bait", StringComparison.Ordinal) < 0)
                        continue;
                    Logger.LogInfo("[fish:likes]   field " + fi.Name + " : " + fi.FieldType.FullName);
                }
                Logger.LogInfo("[fish:likes] === methods with 'like'/'bait'/'boilie' ===");
                foreach (var m in fishT.GetMethods(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Static))
                {
                    string n = m.Name.ToLowerInvariant();
                    if (n.IndexOf("like", StringComparison.Ordinal) < 0
                        && n.IndexOf("boilie", StringComparison.Ordinal) < 0
                        && n.IndexOf("plant", StringComparison.Ordinal) < 0
                        && n.IndexOf("baitid", StringComparison.Ordinal) < 0)
                        continue;
                    var ps = m.GetParameters();
                    var sb = new StringBuilder();
                    sb.Append(m.ReturnType.Name).Append(" ").Append(m.Name).Append("(");
                    for (int i = 0; i < ps.Length; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(ps[i].ParameterType.Name).Append(" ").Append(ps[i].Name);
                    }
                    sb.Append(")");
                    Logger.LogInfo("[fish:likes]   " + sb);
                }
                // Try probing FishBaitEvaluator since that name appeared earlier.
                var evT = FindType("FishBaitEvaluator");
                if (!ReferenceEquals(evT, null))
                {
                    Logger.LogInfo("[fish:likes] FishBaitEvaluator FullName=" + evT.FullName);
                    foreach (var fi in evT.GetFields(
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Static))
                        Logger.LogInfo("[fish:likes]   ev.field " + fi.Name + " : " + fi.FieldType.FullName);
                    foreach (var m in evT.GetMethods(
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Static))
                    {
                        if (ReferenceEquals(m.DeclaringType, typeof(object))) continue;
                        var ps = m.GetParameters();
                        var sb = new StringBuilder();
                        sb.Append(m.ReturnType.Name).Append(" ").Append(m.Name).Append("(");
                        for (int i = 0; i < ps.Length; i++)
                        {
                            if (i > 0) sb.Append(", ");
                            sb.Append(ps[i].ParameterType.Name);
                        }
                        sb.Append(")");
                        Logger.LogInfo("[fish:likes]   ev." + sb);
                    }
                }
            }
            catch (Exception ex)
            { Logger.LogError("[fish:likes] " + ex.GetType().Name + ": " + ex.Message); }
        }

        private void ProbeNaturalBaitComponents()
        {
            try
            {
                var mgrT = FindType("EquipmentManager");
                var eqT = FindType("EquipmentObject");
                var baitT = FindType("Bait");
                if (ReferenceEquals(mgrT, null) || ReferenceEquals(eqT, null) || ReferenceEquals(baitT, null)) return;
                var live = UnityEngine.Object.FindObjectsOfType(mgrT);
                if (live == null || live.Length == 0) return;

                var fiPrefab = eqT.GetField("prefab",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                var fiName = eqT.GetField("equipmentName",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                var fiTrName = eqT.GetField("equipmentTranslateName",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                var flT = FindType("FishLikesParams");
                System.Reflection.FieldInfo fiFLP = null, fiInts = null;
                if (!ReferenceEquals(flT, null))
                {
                    fiFLP = baitT.GetField("fishLikesParams",
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);
                    fiInts = flT.GetField("fishInterests",
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);
                }

                string[] cats = { "equipmentBaits", "equipmentBoilies", "equipmentFeederBaits" };
                foreach (var c in cats)
                {
                    var lf = mgrT.GetField(c,
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);
                    if (ReferenceEquals(lf, null)) continue;
                    var list = lf.GetValue(live[0]) as System.Collections.IEnumerable;
                    if (list == null) continue;
                    Logger.LogInfo("[nat:check] === " + c + " ===");
                    foreach (var entry in list)
                    {
                        var pref = fiPrefab.GetValue(entry) as UnityEngine.GameObject;
                        string nm = (fiName.GetValue(entry) as string) ?? "";
                        string trn = (fiTrName.GetValue(entry) as string) ?? "";
                        string ko = string.IsNullOrEmpty(trn) ? nm : Tr(trn);
                        if (ReferenceEquals(pref, null))
                        { Logger.LogInfo("[nat:check]   '" + ko + "' prefab=null"); continue; }
                        var bait = pref.GetComponent(baitT);
                        if (ReferenceEquals(bait, null))
                        {
                            // Also check children (some lures put Bait on a child).
                            bait = pref.GetComponentInChildren(baitT);
                        }
                        if (ReferenceEquals(bait, null))
                        { Logger.LogInfo("[nat:check]   '" + ko + "' (" + pref.name + ") -> NO Bait component"); continue; }

                        int interestCount = 0, nonZeroInterest = 0;
                        if (!ReferenceEquals(fiFLP, null) && !ReferenceEquals(fiInts, null))
                        {
                            var flp = fiFLP.GetValue(bait);
                            if (flp != null)
                            {
                                var il = fiInts.GetValue(flp) as System.Collections.IEnumerable;
                                if (il != null)
                                {
                                    foreach (var ii in il)
                                    {
                                        interestCount++;
                                        var v = flT.GetNestedType("FishInterest", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                                    }
                                }
                            }
                        }
                        Logger.LogInfo("[nat:check]   '" + ko + "' (" + pref.name + ") -> Bait OK, interests=" + interestCount);
                    }
                }
            }
            catch (Exception ex)
            { Logger.LogError("[nat:check] " + ex.GetType().Name + ": " + ex.Message); }
        }

        private void ProbeNaturalBaits()
        {
            try
            {
                var mgrT = FindType("EquipmentManager");
                var eqT = FindType("EquipmentObject");
                if (ReferenceEquals(mgrT, null) || ReferenceEquals(eqT, null)) return;
                var live = UnityEngine.Object.FindObjectsOfType(mgrT);
                if (live == null || live.Length == 0) return;
                var fis = eqT.GetFields(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                string[] cats = { "equipmentBaits", "equipmentBoilies", "equipmentFeederBaits" };
                foreach (var c in cats)
                {
                    var lf = mgrT.GetField(c,
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);
                    if (ReferenceEquals(lf, null)) { Logger.LogInfo("[equip:natural] " + c + ": field missing"); continue; }
                    var list = lf.GetValue(live[0]) as System.Collections.IList;
                    if (list == null) continue;
                    Logger.LogInfo("[equip:natural] === " + c + " (" + list.Count + ") ===");
                    int i = 0;
                    foreach (var entry in list)
                    {
                        if (i >= 6) { Logger.LogInfo("[equip:natural]   ..."); break; }
                        var sb = new StringBuilder();
                        sb.Append("[" + i + "] ");
                        foreach (var fi in fis)
                        {
                            object v;
                            try { v = fi.GetValue(entry); } catch { continue; }
                            string display = null;
                            if (v == null) continue;
                            if (v is string)
                            {
                                if (string.IsNullOrEmpty((string)v)) continue;
                                display = "'" + v + "'";
                            }
                            else if (v.GetType().IsPrimitive || v.GetType().IsEnum)
                                display = v.ToString();
                            else if (v is UnityEngine.Object)
                                display = "<UO " + ((UnityEngine.Object)v).name + ">";
                            else continue;
                            sb.Append(fi.Name).Append("=").Append(display).Append(", ");
                        }
                        Logger.LogInfo("[equip:natural]   " + sb);
                        i++;
                    }
                }
            }
            catch (Exception ex)
            { Logger.LogError("[equip:natural] " + ex.GetType().Name + ": " + ex.Message); }
        }

        private void ProbeCatalogEntries()
        {
            try
            {
                var mgrT = FindType("EquipmentManager");
                var eqT = FindType("EquipmentObject");
                if (ReferenceEquals(mgrT, null) || ReferenceEquals(eqT, null)) return;

                object inst = null;
                var live = UnityEngine.Object.FindObjectsOfType(mgrT);
                if (live != null && live.Length > 0) inst = live[0];
                if (inst == null) { Logger.LogWarning("[equip:catalog] no manager"); return; }

                var fis = eqT.GetFields(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                string[] catalogs = {
                    "equipmentSpinners", "equipmentSpoons", "equipmentWobblers",
                    "equipmentSoftBaits", "equipmentFlies", "equipmentNormalHooks",
                };
                foreach (var catName in catalogs)
                {
                    var fi = mgrT.GetField(catName,
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);
                    if (ReferenceEquals(fi, null)) continue;
                    var list = fi.GetValue(inst) as System.Collections.IEnumerable;
                    if (list == null) continue;
                    Logger.LogInfo("[equip:catalog] === " + catName + " ===");
                    int i = 0;
                    foreach (var e in list)
                    {
                        if (i >= 3) { Logger.LogInfo("[equip:catalog]   ..."); break; }
                        var sb = new StringBuilder();
                        sb.Append("[" + i + "] ");
                        var uo = e as UnityEngine.Object;
                        sb.Append("name=").Append(uo != null ? uo.name : "?").Append("  ");
                        // every non-empty string field
                        foreach (var ef in fis)
                        {
                            if (!ReferenceEquals(ef.FieldType, typeof(string))) continue;
                            object v;
                            try { v = ef.GetValue(e); } catch { continue; }
                            string s = v as string;
                            if (string.IsNullOrEmpty(s)) continue;
                            sb.Append(ef.Name).Append("='").Append(s).Append("' ");
                        }
                        Logger.LogInfo("[equip:catalog]   " + sb);
                        i++;
                    }
                }
            }
            catch (Exception ex)
            { Logger.LogError("[equip:catalog] " + ex.GetType().Name + ": " + ex.Message); }
        }

        private void ProbeEquipmentManager()
        {
            try
            {
                var mgrT = FindType("EquipmentManager");
                if (ReferenceEquals(mgrT, null))
                {
                    Logger.LogWarning("[equip:mgr] EquipmentManager type not found");
                    return;
                }
                Logger.LogInfo("[equip:mgr] EquipmentManager FullName=" + mgrT.FullName);

                // Try to access Instance/instance singleton.
                object inst = null;
                foreach (var p in mgrT.GetProperties(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Static))
                {
                    if (p.Name == "Instance" || p.Name == "instance")
                    { try { inst = p.GetValue(null, null); } catch { } break; }
                }
                if (ReferenceEquals(inst, null))
                {
                    foreach (var f in mgrT.GetFields(
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Static))
                    {
                        if (f.Name == "Instance" || f.Name == "instance" || f.Name == "_instance")
                        { try { inst = f.GetValue(null); } catch { } break; }
                    }
                }
                if (inst == null)
                {
                    var live = UnityEngine.Object.FindObjectsOfType(mgrT);
                    if (live != null && live.Length > 0) inst = live[0];
                }
                if (inst == null)
                {
                    Logger.LogInfo("[equip:mgr] no live instance");
                    return;
                }
                Logger.LogInfo("[equip:mgr] sample instance found");

                var fis = mgrT.GetFields(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                Logger.LogInfo("[equip:mgr] " + fis.Length + " instance fields");
                foreach (var fi in fis)
                {
                    string n = fi.Name.ToLowerInvariant();
                    if (n.IndexOf("list", StringComparison.Ordinal) < 0
                        && n.IndexOf("data", StringComparison.Ordinal) < 0
                        && n.IndexOf("equip", StringComparison.Ordinal) < 0
                        && n.IndexOf("catalog", StringComparison.Ordinal) < 0
                        && n.IndexOf("item", StringComparison.Ordinal) < 0
                        && n.IndexOf("dict", StringComparison.Ordinal) < 0
                        && n.IndexOf("array", StringComparison.Ordinal) < 0
                        && n.IndexOf("bait", StringComparison.Ordinal) < 0
                        && n.IndexOf("hook", StringComparison.Ordinal) < 0)
                        continue;
                    string typeName = fi.FieldType.FullName;
                    object v;
                    try { v = fi.GetValue(inst); } catch { v = null; }
                    int count = -1;
                    if (v is System.Collections.ICollection)
                        count = ((System.Collections.ICollection)v).Count;
                    Logger.LogInfo("[equip:mgr]   " + fi.Name + " : " + typeName
                        + (count >= 0 ? "  count=" + count : ""));
                }
            }
            catch (Exception ex)
            { Logger.LogError("[equip:mgr] " + ex.GetType().Name + ": " + ex.Message); }
        }

        private void ProbeEquipmentObject()
        {
            try
            {
                var eqT = FindType("EquipmentObject");
                if (ReferenceEquals(eqT, null))
                {
                    Logger.LogWarning("[equip:probe] EquipmentObject type not found");
                    return;
                }
                Logger.LogInfo("[equip:probe] EquipmentObject FullName=" + eqT.FullName);
                var fis = eqT.GetFields(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                Logger.LogInfo("[equip:probe] " + fis.Length + " fields");
                foreach (var fi in fis)
                {
                    string nm = fi.Name.ToLowerInvariant();
                    if (nm.IndexOf("name", StringComparison.Ordinal) < 0
                        && nm.IndexOf("title", StringComparison.Ordinal) < 0
                        && nm.IndexOf("label", StringComparison.Ordinal) < 0
                        && nm.IndexOf("loc", StringComparison.Ordinal) < 0
                        && nm.IndexOf("term", StringComparison.Ordinal) < 0
                        && nm.IndexOf("text", StringComparison.Ordinal) < 0
                        && nm.IndexOf("id", StringComparison.Ordinal) < 0
                        && nm.IndexOf("key", StringComparison.Ordinal) < 0
                        && nm.IndexOf("price", StringComparison.Ordinal) < 0
                        && nm.IndexOf("size", StringComparison.Ordinal) < 0
                        && nm.IndexOf("type", StringComparison.Ordinal) < 0
                        && !ReferenceEquals(fi.FieldType, typeof(string)))
                        continue;
                    Logger.LogInfo("[equip:probe]   " + fi.Name + " : " + fi.FieldType.FullName);
                }

                // Sample one Bait's equipmentObject and dump its string fields.
                var baitT = FindType("Bait");
                if (ReferenceEquals(baitT, null)) return;
                var fiEq = baitT.GetField("equipmentObject",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                if (ReferenceEquals(fiEq, null)) return;
                var fiBT = baitT.GetField("baitType",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                var seen = new System.Collections.Generic.HashSet<string>();
                var allBaits = Resources.FindObjectsOfTypeAll(baitT);
                foreach (var b in allBaits)
                {
                    var btv = fiBT.GetValue(b);
                    string bts = btv == null ? "?" : btv.ToString();
                    if (!seen.Add(bts)) continue;
                    var eq = fiEq.GetValue(b);
                    var uo = b as UnityEngine.Object;
                    string nm = uo != null ? uo.name : "?";
                    if (eq == null)
                    {
                        Logger.LogInfo("[equip:probe] " + bts + " (" + nm + "): equipmentObject=null");
                        continue;
                    }
                    Logger.LogInfo("[equip:probe] " + bts + " (" + nm + ")");
                    foreach (var fi in fis)
                    {
                        if (!ReferenceEquals(fi.FieldType, typeof(string))) continue;
                        object v;
                        try { v = fi.GetValue(eq); } catch { continue; }
                        if (v == null) continue;
                        string s = v.ToString();
                        if (string.IsNullOrEmpty(s)) continue;
                        Logger.LogInfo("[equip:probe]   " + fi.Name + " = '" + s + "'");
                    }
                    // Also dump int/enum fields whose name suggests label/size.
                    foreach (var fi in fis)
                    {
                        if (ReferenceEquals(fi.FieldType, typeof(string))) continue;
                        string n = fi.Name.ToLowerInvariant();
                        if (n.IndexOf("name", StringComparison.Ordinal) < 0
                            && n.IndexOf("size", StringComparison.Ordinal) < 0
                            && n.IndexOf("number", StringComparison.Ordinal) < 0
                            && n.IndexOf("id", StringComparison.Ordinal) < 0
                            && n.IndexOf("type", StringComparison.Ordinal) < 0)
                            continue;
                        object v;
                        try { v = fi.GetValue(eq); } catch { continue; }
                        if (v == null) continue;
                        Logger.LogInfo("[equip:probe]   " + fi.Name + " = " + v);
                    }
                }
            }
            catch (Exception ex)
            { Logger.LogError("[equip:probe] " + ex.GetType().Name + ": " + ex.Message); }
        }

        private void ProbeBaitAllFields()
        {
            try
            {
                var baitT = FindType("Bait");
                if (ReferenceEquals(baitT, null)) return;
                var fis = baitT.GetFields(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                Logger.LogInfo("[bait:all] Bait has " + fis.Length + " instance fields:");
                foreach (var fi in fis)
                    Logger.LogInfo("[bait:all]   " + fi.Name + " : " + fi.FieldType.FullName);

                // Now show every string-typed field's value on a sample.
                var allBaits = Resources.FindObjectsOfTypeAll(baitT);
                if (allBaits.Length == 0) return;
                var sample = allBaits[0];
                string nm = (sample is UnityEngine.Object) ? ((UnityEngine.Object)sample).name : "?";
                Logger.LogInfo("[bait:all] sample = " + nm);
                foreach (var fi in fis)
                {
                    if (!ReferenceEquals(fi.FieldType, typeof(string))) continue;
                    object v;
                    try { v = fi.GetValue(sample); } catch { continue; }
                    Logger.LogInfo("[bait:all]   string " + fi.Name + " = '" + (v ?? "null") + "'");
                }
            }
            catch (Exception ex)
            { Logger.LogError("[bait:all] " + ex.GetType().Name + ": " + ex.Message); }
        }

        // ----- I2 Localization helper (cached). ---------------------------
        private System.Reflection.MethodInfo _miGetTerm;
        private bool _locInitTried;

        private void EnsureLoc()
        {
            if (_locInitTried) return;
            _locInitTried = true;
            try
            {
                var lmT = FindType("LocalizationManager");
                if (ReferenceEquals(lmT, null)) return;
                foreach (var m in lmT.GetMethods(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Static))
                {
                    if (m.Name != "GetTermTranslation") continue;
                    var ps = m.GetParameters();
                    if (ps.Length == 1 && ReferenceEquals(ps[0].ParameterType, typeof(string)))
                    { _miGetTerm = m; break; }
                }
            }
            catch { }
        }

        private string Tr(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;
            EnsureLoc();
            if (ReferenceEquals(_miGetTerm, null)) return key;
            try
            {
                object r = _miGetTerm.Invoke(null, new object[] { key });
                if (r == null) return key;
                string s = r.ToString();
                return string.IsNullOrEmpty(s) ? key : s;
            }
            catch { return key; }
        }

        private static readonly System.Collections.Generic.Dictionary<string, string> BaitTypeKor =
            new System.Collections.Generic.Dictionary<string, string> {
                { "HOOK",      "훅" },
                { "SPINNER",   "스피너" },
                { "SPOON",     "스푼" },
                { "WOBBLER",   "워블러" },
                { "SOFT_BAIT", "소프트베이트" },
                { "FLY",       "플라이" },
                // Catalog-derived categories (preferred over BaitType enum).
                { "equipmentBaits",       "생미끼" },
                { "equipmentBoilies",     "보일리" },
                { "equipmentFeederBaits", "피더 미끼" },
                { "equipmentSpinners",    "스피너" },
                { "equipmentSpoons",      "스푼" },
                { "equipmentWobblers",    "워블러" },
                { "equipmentSoftBaits",   "소프트베이트" },
                { "equipmentFlies",       "플라이" },
                { "equipmentNormalHooks", "훅(생미끼용)" },
            };

        // Recommendation engine. Combines:
        //  1) live FishSpawner list -> active species + counts
        //  2) Fish prefab fields per species (baitSize, depth)
        //  3) every Bait's fishLikesParams.fishInterests scored against zone
        private void ZoneReco()
        {
            try
            {
                var fsType = FindType("FishSpawner");
                var fishType = FindType("Fish");
                var baitT = FindType("Bait");
                var flT = FindType("FishLikesParams");
                if (ReferenceEquals(fsType, null) || ReferenceEquals(fishType, null)
                    || ReferenceEquals(baitT, null) || ReferenceEquals(flT, null))
                {
                    RZ_W("[zone:reco] required type not found");
                    return;
                }

                var fiPrefab = fsType.GetField("fishPrefab",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                var fiSpawnerCount = fsType.GetField("count",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                // fishList tracks the spawner's currently-alive fish; this
                // shrinks when one is caught/removed, unlike the static
                // `count` configuration field.
                var fiSpawnerFishList = fsType.GetField("fishList",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                var fiSpecies = fishType.GetField("species",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                var fiBaitSize = fishType.GetField("baitSize",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                var fiNormalDepthStyle = fishType.GetField("normalDepthStyle",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                var fiNormalDepthLevel = fishType.GetField("normalDepthLevel",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                var fiBaitType = baitT.GetField("baitType",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                var fiHookSize = baitT.GetField("hookSize",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                var fiFLP = baitT.GetField("fishLikesParams",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                var fiInterests = flT.GetField("fishInterests",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                Type fiInterestT = null;
                foreach (var nt in flT.GetNestedTypes(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic))
                { if (nt.Name == "FishInterest") { fiInterestT = nt; break; } }
                if (ReferenceEquals(fiInterestT, null)) return;
                var fiInterestSpecies = fiInterestT.GetField("species",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                var fiInterestValue = fiInterestT.GetField("interest",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                // 1) Live fish in this spot. Counting active Fish components
                //    in the scene means the count goes down as fish are caught
                //    and back up as they respawn. Also captures species that
                //    don't show up in FishSpawner enumeration (e.g. trophy
                //    spawners or rare variants).
                var fiFishWMM_pre = fishType.GetField("WeightMinMax",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                var fiFishWeightCur = fishType.GetField("Weight",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                var weights = new System.Collections.Generic.Dictionary<int, int>();
                var prefabBySpecies = new System.Collections.Generic.Dictionary<int, object>();
                var effWeightRanges = new System.Collections.Generic.Dictionary<int, Vector2>();
                int totalWeight = 0;

                var liveFish = UnityEngine.Object.FindObjectsOfType(fishType);
                foreach (var f in liveFish)
                {
                    var mb = f as MonoBehaviour;
                    if (mb == null || mb.gameObject == null) continue;
                    if (!mb.gameObject.activeInHierarchy || !mb.enabled) continue;
                    var spv = fiSpecies.GetValue(mb);
                    if (spv == null) continue;
                    int sp = Convert.ToInt32(spv);

                    if (!weights.ContainsKey(sp)) weights[sp] = 0;
                    weights[sp]++;
                    totalWeight++;
                    if (!prefabBySpecies.ContainsKey(sp)) prefabBySpecies[sp] = mb;

                    // Track effective weight range from this fish's actual
                    // current Weight (already scaled by spawner multiplier).
                    float curW = 0f;
                    if (!ReferenceEquals(fiFishWeightCur, null))
                    { var wv2 = fiFishWeightCur.GetValue(mb); if (wv2 != null) curW = Convert.ToSingle(wv2); }
                    if (curW <= 0f && !ReferenceEquals(fiFishWMM_pre, null))
                    { // fall back to species default
                        var wmmDef = (Vector2)fiFishWMM_pre.GetValue(mb);
                        curW = (wmmDef.x + wmmDef.y) * 0.5f;
                    }
                    Vector2 cur;
                    if (!effWeightRanges.TryGetValue(sp, out cur))
                        effWeightRanges[sp] = new Vector2(curW, curW);
                    else
                        effWeightRanges[sp] = new Vector2(
                            Math.Min(cur.x, curW), Math.Max(cur.y, curW));
                }

                // Augment with FishSpawner data so species with 0 alive (mid-
                // respawn) still appear, and so we still show the configured
                // distribution even before the player has approached enough
                // for fish to spawn.
                var fiSpawnerMult = fsType.GetField("fishSizeMultiplier",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                // Pick up additional spawner types (e.g. TrophyFishSpawner)
                // so rare/golden variants don't get missed.
                var spawnerTypes = new System.Collections.Generic.List<Type> { fsType };
                var trophyT = FindType("TrophyFishSpawner");
                if (!ReferenceEquals(trophyT, null)) spawnerTypes.Add(trophyT);
                var spawners = new System.Collections.Generic.List<UnityEngine.Object>();
                foreach (var st in spawnerTypes)
                    spawners.AddRange(UnityEngine.Object.FindObjectsOfType(st));
                foreach (var s in spawners)
                {
                    var mb = s as MonoBehaviour;
                    if (mb == null || mb.gameObject == null) continue;
                    if (!mb.gameObject.activeInHierarchy || !mb.enabled) continue;
                    // Different spawner types may use different field names.
                    var st = mb.GetType();
                    var fiPref = st.GetField("fishPrefab",
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);
                    if (ReferenceEquals(fiPref, null))
                        fiPref = st.GetField("fish",
                            System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.NonPublic |
                            System.Reflection.BindingFlags.Instance);
                    if (ReferenceEquals(fiPref, null)) continue;
                    var fish = fiPref.GetValue(mb);
                    if (fish == null) continue;
                    var spv = fiSpecies.GetValue(fish);
                    if (spv == null) continue;
                    int sp = Convert.ToInt32(spv);

                    if (!weights.ContainsKey(sp)) weights[sp] = 0;
                    if (!prefabBySpecies.ContainsKey(sp)) prefabBySpecies[sp] = fish;

                    if (!effWeightRanges.ContainsKey(sp))
                    {
                        float mult = 1f;
                        var fiMult = st.GetField("fishSizeMultiplier",
                            System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.NonPublic |
                            System.Reflection.BindingFlags.Instance);
                        if (!ReferenceEquals(fiMult, null))
                        { var mv = fiMult.GetValue(mb); if (mv != null) mult = Convert.ToSingle(mv); }
                        if (mult <= 0f) mult = 1f;
                        Vector2 base_ = ReferenceEquals(fiFishWMM_pre, null) ? new Vector2(0, 0)
                            : (Vector2)fiFishWMM_pre.GetValue(fish);
                        effWeightRanges[sp] = new Vector2(base_.x * mult, base_.y * mult);
                    }
                }
                if (weights.Count == 0)
                {
                    RZ_I("[zone:reco] no fish or spawners — enter a fishing spot first");
                    _zoneRows = new System.Collections.Generic.List<ZoneSpeciesRow>();
                    _lastZoneRecoT = Time.realtimeSinceStartup;
                    return;
                }
                // Avoid divide-by-zero in the weighted lure score; if every
                // species is mid-respawn use 1 so the math still runs.
                int scoreDenom = totalWeight > 0 ? totalWeight : 1;
                var fiFishName = fishType.GetField("fishName",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                var fiFishWMM = fishType.GetField("WeightMinMax",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                // Sort species keys by their localized Korean name so the
                // 활성어종 list and the per-species reco block both render
                // in stable 가나다 order.
                var speciesKeys = new System.Collections.Generic.List<int>(weights.Keys);
                var koByKey = new System.Collections.Generic.Dictionary<int, string>();
                foreach (var key in speciesKeys)
                {
                    object pf;
                    string ko_;
                    if (prefabBySpecies.TryGetValue(key, out pf) && !ReferenceEquals(fiFishName, null))
                        ko_ = Tr((fiFishName.GetValue(pf) ?? "").ToString());
                    else
                        ko_ = Enum.GetName(fiSpecies.FieldType, key) ?? "";
                    koByKey[key] = ko_;
                }
                // Sort by Korean name without a capturing lambda (Mono BCL).
                {
                    int n = speciesKeys.Count;
                    var koArr = new string[n];
                    var keyArr = new int[n];
                    for (int i = 0; i < n; i++) { keyArr[i] = speciesKeys[i]; koArr[i] = koByKey[speciesKeys[i]] ?? ""; }
                    Array.Sort(koArr, keyArr, StringComparer.CurrentCulture);
                    speciesKeys.Clear();
                    for (int i = 0; i < n; i++) speciesKeys.Add(keyArr[i]);
                }
                {
                    var sb = new StringBuilder();
                    sb.Append("[zone:reco] detected species: ");
                    foreach (var k in speciesKeys)
                    {
                        sb.Append(koByKey[k]).Append("(")
                          .Append(Enum.GetName(fiSpecies.FieldType, k))
                          .Append(",x").Append(weights[k]).Append(") ");
                    }
                    RZ_I(sb.ToString());
                }

                var weightRanges = new System.Collections.Generic.Dictionary<int, Vector2>();
                RZ_I("[zone:reco] === 활성 어종 (스포너수) ===");
                foreach (var k in speciesKeys)
                {
                    var kv = new System.Collections.Generic.KeyValuePair<int, int>(k, weights[k]);
                    string spName = Enum.GetName(fiSpecies.FieldType, kv.Key);
                    var fish = prefabBySpecies[kv.Key];
                    var dst = fiNormalDepthStyle.GetValue(fish).ToString();
                    float dlv = Convert.ToSingle(fiNormalDepthLevel.GetValue(fish));
                    string ko = ReferenceEquals(fiFishName, null) ? spName
                        : Tr((fiFishName.GetValue(fish) ?? "").ToString());
                    Vector2 wmm;
                    if (!effWeightRanges.TryGetValue(kv.Key, out wmm))
                        wmm = ReferenceEquals(fiFishWMM, null) ? new Vector2(0, 999)
                            : (Vector2)fiFishWMM.GetValue(fish);
                    weightRanges[kv.Key] = wmm;
                    RZ_I("[zone:reco]   " + ko
                        + "  스포너=" + kv.Value
                        + "  무게=" + wmm.x.ToString("F1", Inv) + "~" + wmm.y.ToString("F1", Inv) + "kg"
                        + "  서식=" + dst + " " + dlv.ToString("F1", Inv) + "m");
                }

                // 2) Score every (unique-named) Bait against the zone.
                //    Pull display names from the attached EquipmentObject when
                //    populated, else fall back to the EquipmentManager catalog
                //    looked up by id.
                var fiEqObj = baitT.GetField("equipmentObject",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                var eqT = FindType("EquipmentObject");
                System.Reflection.FieldInfo fiEqName = null, fiEqProducer = null, fiEqId = null;
                if (!ReferenceEquals(eqT, null))
                {
                    fiEqName = eqT.GetField("equipmentName",
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);
                    fiEqProducer = eqT.GetField("producerName",
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);
                    fiEqId = eqT.GetField("id",
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);
                }

                // Build catalog: prefab GameObject name -> CatalogInfo using
                // every list on EquipmentManager whose name starts with
                // "equipment". The catalog entry's `prefab` field links
                // straight to the same Bait prefab the scene clones.
                System.Reflection.FieldInfo fiEqPrefab = null, fiEqLevel = null, fiEqPrice = null, fiEqParams = null, fiEqTrName = null;
                System.Reflection.FieldInfo fiParamStr = null;
                if (!ReferenceEquals(eqT, null))
                {
                    fiEqTrName = eqT.GetField("equipmentTranslateName",
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);
                    fiEqPrefab = eqT.GetField("prefab",
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);
                    fiEqLevel = eqT.GetField("level",
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);
                    fiEqPrice = eqT.GetField("price",
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);
                    fiEqParams = eqT.GetField("parameters",
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);
                    Type pT = null;
                    foreach (var nt in eqT.GetNestedTypes(
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic))
                    { if (nt.Name == "Parameter") { pT = nt; break; } }
                    if (!ReferenceEquals(pT, null))
                        fiParamStr = pT.GetField("parameterString",
                            System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.NonPublic |
                            System.Reflection.BindingFlags.Instance);
                }
                var catalog = new System.Collections.Generic.Dictionary<string, CatalogInfo>();
                var mgrT = FindType("EquipmentManager");
                if (!ReferenceEquals(mgrT, null) && !ReferenceEquals(eqT, null))
                {
                    var mgrLive = UnityEngine.Object.FindObjectsOfType(mgrT);
                    if (mgrLive != null && mgrLive.Length > 0)
                    {
                        var mgr = mgrLive[0];
                        // Categories we want to surface as recommendations.
                        // Order matters: first match wins for prefab→category.
                        string[] wantCats = {
                            "equipmentBaits", "equipmentBoilies", "equipmentFeederBaits",
                            "equipmentSpinners", "equipmentSpoons", "equipmentWobblers",
                            "equipmentSoftBaits", "equipmentFlies", "equipmentNormalHooks",
                        };
                        foreach (var catName in wantCats)
                        {
                            var mfi = mgrT.GetField(catName,
                                System.Reflection.BindingFlags.Public |
                                System.Reflection.BindingFlags.NonPublic |
                                System.Reflection.BindingFlags.Instance);
                            if (ReferenceEquals(mfi, null)) continue;
                            var listVal = mfi.GetValue(mgr) as System.Collections.IEnumerable;
                            if (listVal == null) continue;
                            foreach (var entry in listVal)
                            {
                                if (entry == null) continue;
                                if (!eqT.IsAssignableFrom(entry.GetType())) continue;
                                var pref = ReferenceEquals(fiEqPrefab, null) ? null : fiEqPrefab.GetValue(entry) as UnityEngine.GameObject;
                                if (ReferenceEquals(pref, null)) continue;
                                string key = pref.name;
                                if (catalog.ContainsKey(key)) continue;
                                string sizeStr = "";
                                string weightStr = "";
                                float wMin = 0, wMax = 0;
                                if (!ReferenceEquals(fiEqParams, null) && !ReferenceEquals(fiParamStr, null))
                                {
                                    var plist = fiEqParams.GetValue(entry) as System.Collections.IList;
                                    if (plist != null)
                                    {
                                        if (plist.Count > 0)
                                            sizeStr = (fiParamStr.GetValue(plist[0]) as string) ?? "";
                                        // Find the param string containing "kg" (weight range).
                                        for (int pi = plist.Count - 1; pi >= 0; pi--)
                                        {
                                            var s = fiParamStr.GetValue(plist[pi]) as string;
                                            if (string.IsNullOrEmpty(s)) continue;
                                            if (s.IndexOf("kg", StringComparison.Ordinal) < 0) continue;
                                            weightStr = s;
                                            ParseKgRange(s, out wMin, out wMax);
                                            break;
                                        }
                                    }
                                }
                                var info = new CatalogInfo {
                                    equipName  = ReferenceEquals(fiEqName, null) ? "" : (fiEqName.GetValue(entry) as string ?? ""),
                                    translateName = ReferenceEquals(fiEqTrName, null) ? "" : (fiEqTrName.GetValue(entry) as string ?? ""),
                                    producer   = ReferenceEquals(fiEqProducer, null) ? "" : (fiEqProducer.GetValue(entry) as string ?? ""),
                                    id         = ReferenceEquals(fiEqId, null) ? "" : (fiEqId.GetValue(entry) as string ?? ""),
                                    level      = ReferenceEquals(fiEqLevel, null) ? 0 : Convert.ToInt32(fiEqLevel.GetValue(entry)),
                                    price      = ReferenceEquals(fiEqPrice, null) ? 0 : Convert.ToInt32(fiEqPrice.GetValue(entry)),
                                    category   = catName,
                                    sizeStr    = sizeStr,
                                    weightStr  = weightStr,
                                    weightMin  = wMin,
                                    weightMax  = wMax,
                                    prefab     = pref,
                                };
                                catalog[key] = info;
                            }
                        }
                    }
                }
                RZ_I("[zone:reco] catalog entries: " + catalog.Count);

                var seenNames = new System.Collections.Generic.HashSet<string>();
                var scores = new System.Collections.Generic.List<BaitScore>();
                var allBaits = Resources.FindObjectsOfTypeAll(baitT);
                foreach (var b in allBaits)
                {
                    var uo = b as UnityEngine.Object;
                    string prefabNm = uo != null ? uo.name : "?";
                    int p = prefabNm.IndexOf("(Clone)", StringComparison.Ordinal);
                    if (p >= 0) prefabNm = prefabNm.Substring(0, p);
                    if (!seenNames.Add(prefabNm)) continue;

                    var btv = fiBaitType.GetValue(b);
                    string bts = btv == null ? "?" : btv.ToString();
                    int hookSz = 0;
                    var hsv = fiHookSize.GetValue(b);
                    if (hsv != null) hookSz = Convert.ToInt32(hsv);
                    var flp = fiFLP.GetValue(b);
                    if (flp == null) continue;
                    var list = fiInterests.GetValue(flp) as System.Collections.IEnumerable;
                    if (list == null) continue;

                    // Pull catalog weight range up-front so we can filter
                    // before paying the per-species interest sum.
                    Vector2 baitWR = new Vector2(0, 999);
                    CatalogInfo preInfo;
                    bool haveBaitRange = false;
                    if (catalog.TryGetValue(prefabNm, out preInfo)
                        && preInfo.weightMax > 0
                        && preInfo.weightMax > preInfo.weightMin)
                    { baitWR = new Vector2(preInfo.weightMin, preInfo.weightMax); haveBaitRange = true; }

                    float weighted = 0f;
                    var perSp = new System.Collections.Generic.Dictionary<int, float>();
                    foreach (var e in list)
                    {
                        int sp = Convert.ToInt32(fiInterestSpecies.GetValue(e));
                        int w; if (!weights.TryGetValue(sp, out w)) continue;
                        float iv = Convert.ToSingle(fiInterestValue.GetValue(e));
                        Vector2 fishWR;
                        if (haveBaitRange && weightRanges.TryGetValue(sp, out fishWR))
                        {
                            float lo = Math.Max(baitWR.x, fishWR.x);
                            float hi = Math.Min(baitWR.y, fishWR.y);
                            if (hi <= lo) continue;
                            float overlap = hi - lo;
                            float fishSpan = Math.Max(0.001f, fishWR.y - fishWR.x);
                            float fit = Math.Min(1f, overlap / fishSpan);
                            iv *= fit;
                        }
                        if (iv > 0f) perSp[sp] = iv;
                        weighted += w * iv;
                    }
                    weighted /= scoreDenom;
                    if (weighted <= 0.01f) continue;

                    string display = prefabNm, producer = "", eqId = "", sizeStr = "", weightStr = "";
                    int level = 0, price = 0;
                    string category = bts; // fall back to Bait.baitType enum
                    CatalogInfo info;
                    if (catalog.TryGetValue(prefabNm, out info))
                    {
                        // Prefer the localized translateName ('EQUIPMENT/NATURAL_BAITS/PEA' -> '완두콩').
                        if (!string.IsNullOrEmpty(info.translateName))
                        {
                            string t = Tr(info.translateName);
                            if (!string.IsNullOrEmpty(t) && t != info.translateName) display = t;
                        }
                        if (display == prefabNm
                            && !string.IsNullOrEmpty(info.equipName)
                            && info.equipName.IndexOf("TEST", StringComparison.Ordinal) < 0
                            && !info.equipName.StartsWith("EQUIPMENT/", StringComparison.Ordinal))
                            display = info.equipName;
                        producer = info.producer;
                        eqId = info.id;
                        level = info.level;
                        price = info.price;
                        sizeStr = info.sizeStr;
                        weightStr = info.weightStr;
                        if (!string.IsNullOrEmpty(info.category)) category = info.category;
                    }

                    scores.Add(new BaitScore {
                        name = display, prefab = prefabNm, type = category, producer = producer,
                        eqId = eqId, hookSize = hookSz, score = weighted,
                        level = level, price = price, sizeStr = sizeStr, weightStr = weightStr,
                        perSpecies = perSp });
                }
                // Natural baits and boilies have their own fishLikesParams
                // on a BaitPart / Boilie component (same shape as Bait —
                // a list<FishInterest>). We score them identically to lures.
                var bpT = FindType("BaitPart");
                var boilieT = FindType("Boilie");
                System.Reflection.FieldInfo fiBPFLP = null, fiBoilieFLP = null;
                if (!ReferenceEquals(bpT, null))
                    fiBPFLP = bpT.GetField("fishLikesParams",
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);
                if (!ReferenceEquals(boilieT, null))
                    fiBoilieFLP = boilieT.GetField("fishLikesParams",
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);

                int dbgScored = 0;
                foreach (var kvCat in catalog)
                {
                    var info = kvCat.Value;
                    bool isBait   = info.category == "equipmentBaits";
                    bool isBoilie = info.category == "equipmentBoilies"
                                 || info.category == "equipmentFeederBaits";
                    if (!isBait && !isBoilie) continue;
                    if (ReferenceEquals(info.prefab, null)) continue;

                    UnityEngine.Component comp = null;
                    System.Reflection.FieldInfo flpFi = null;
                    if (isBait && !ReferenceEquals(bpT, null))
                    {
                        comp = info.prefab.GetComponent(bpT);
                        if (ReferenceEquals(comp, null)) comp = info.prefab.GetComponentInChildren(bpT);
                        flpFi = fiBPFLP;
                    }
                    if ((isBoilie || (isBait && ReferenceEquals(comp, null))) && !ReferenceEquals(boilieT, null))
                    {
                        comp = info.prefab.GetComponent(boilieT);
                        if (ReferenceEquals(comp, null)) comp = info.prefab.GetComponentInChildren(boilieT);
                        flpFi = fiBoilieFLP;
                    }
                    if (ReferenceEquals(comp, null) || ReferenceEquals(flpFi, null)) continue;

                    var flp = flpFi.GetValue(comp);
                    if (flp == null) continue;
                    var ints = fiInterests.GetValue(flp) as System.Collections.IEnumerable;
                    if (ints == null) continue;

                    float weighted = 0f;
                    var perSp = new System.Collections.Generic.Dictionary<int, float>();
                    foreach (var e in ints)
                    {
                        int sp = Convert.ToInt32(fiInterestSpecies.GetValue(e));
                        int w; if (!weights.TryGetValue(sp, out w)) continue;
                        float iv = Convert.ToSingle(fiInterestValue.GetValue(e));
                        if (iv > 0f) perSp[sp] = iv;
                        weighted += w * iv;
                    }
                    weighted /= scoreDenom;
                    if (weighted <= 0.01f) continue;
                    dbgScored++;

                    string display = info.equipName;
                    if (!string.IsNullOrEmpty(info.translateName))
                    {
                        string t = Tr(info.translateName);
                        if (!string.IsNullOrEmpty(t) && t != info.translateName) display = t;
                    }
                    if (string.IsNullOrEmpty(display) || display.StartsWith("EQUIPMENT/", StringComparison.Ordinal))
                        display = info.prefab.name;

                    scores.Add(new BaitScore {
                        name = display, prefab = info.prefab.name, type = info.category,
                        producer = info.producer, eqId = info.id, hookSize = 0,
                        score = weighted, level = info.level, price = info.price,
                        sizeStr = "", weightStr = info.weightStr, perSpecies = perSp });
                }
                RZ_I("[zone:reco] natural+boilie scored: " + dbgScored);

                scores.Sort(CompareScoreThenPrice);

                RZ_I("[zone:reco] === 어종별 추천 (생미끼 3 / 보일리 1 / 루어 1) ===");
                var newRows = new System.Collections.Generic.List<ZoneSpeciesRow>();
                foreach (var k2 in speciesKeys)
                {
                    var kv = new System.Collections.Generic.KeyValuePair<int, int>(k2, weights[k2]);
                    var fish = prefabBySpecies[kv.Key];
                    string ko = ReferenceEquals(fiFishName, null)
                        ? Enum.GetName(fiSpecies.FieldType, kv.Key)
                        : Tr((fiFishName.GetValue(fish) ?? "").ToString());

                    var natList = new System.Collections.Generic.List<BaitScore>();
                    var boilieList = new System.Collections.Generic.List<BaitScore>();
                    var lureList = new System.Collections.Generic.List<BaitScore>();
                    foreach (var s in scores)
                    {
                        if (s.perSpecies == null) continue;
                        float iv;
                        if (!s.perSpecies.TryGetValue(kv.Key, out iv) || iv <= 0f) continue;
                        var entry = s; entry.score = iv;
                        if (s.type == "equipmentBaits") natList.Add(entry);
                        else if (s.type == "equipmentBoilies" || s.type == "equipmentFeederBaits") boilieList.Add(entry);
                        else lureList.Add(entry);
                    }
                    natList.Sort(CompareScoreThenPrice);
                    boilieList.Sort(CompareScoreThenPrice);
                    lureList.Sort(CompareScoreThenPrice);

                    RZ_I("[zone:reco]  ▶ " + ko);
                    int nshow = Math.Min(3, natList.Count);
                    if (natList.Count == 0 && boilieList.Count == 0 && lureList.Count == 0)
                        RZ_I("[zone:reco]      (해당 어종에 대한 점수 데이터 없음)");
                    for (int i = 0; i < nshow; i++)
                    {
                        string typeKor; if (!BaitTypeKor.TryGetValue(natList[i].type, out typeKor)) typeKor = natList[i].type;
                        RZ_I("[zone:reco]      " + typeKor + " : " + FormatBait(natList[i]));
                    }
                    if (boilieList.Count > 0)
                    {
                        string typeKor; if (!BaitTypeKor.TryGetValue(boilieList[0].type, out typeKor)) typeKor = boilieList[0].type;
                        RZ_I("[zone:reco]      " + typeKor + " : " + FormatBait(boilieList[0]));
                    }
                    if (lureList.Count > 0)
                    {
                        string typeKor; if (!BaitTypeKor.TryGetValue(lureList[0].type, out typeKor)) typeKor = lureList[0].type;
                        RZ_I("[zone:reco]      " + typeKor + " : " + FormatBait(lureList[0]));
                    }

                    // Compact row for the in-game overlay: 생미끼 / 보일리 / 루어.
                    var sbBaits = new StringBuilder();
                    for (int i = 0; i < nshow; i++)
                    {
                        if (i > 0) sbBaits.Append(" / ");
                        sbBaits.Append(natList[i].name);
                    }
                    if (boilieList.Count > 0)
                    {
                        if (sbBaits.Length > 0) sbBaits.Append("  |  ");
                        if (!string.IsNullOrEmpty(boilieList[0].producer))
                            sbBaits.Append(boilieList[0].producer).Append(" ");
                        sbBaits.Append(boilieList[0].name);
                    }
                    if (lureList.Count > 0)
                    {
                        if (sbBaits.Length > 0) sbBaits.Append("  |  ");
                        if (!string.IsNullOrEmpty(lureList[0].producer))
                            sbBaits.Append(lureList[0].producer).Append(" ");
                        sbBaits.Append(lureList[0].name);
                    }

                    Vector2 wmm2;
                    if (!effWeightRanges.TryGetValue(kv.Key, out wmm2))
                        wmm2 = ReferenceEquals(fiFishWMM, null) ? new Vector2(0, 999)
                            : (Vector2)fiFishWMM.GetValue(fish);
                    string dst2 = fiNormalDepthStyle.GetValue(fish).ToString();
                    float dlv2 = Convert.ToSingle(fiNormalDepthLevel.GetValue(fish));
                    string depthShort;
                    if (dst2 == "GROUND_LEVEL") depthShort = "바닥+" + dlv2.ToString("F1", Inv) + "m";
                    else if (dst2 == "WATER_LEVEL") depthShort = "수면" + dlv2.ToString("F1", Inv) + "m";
                    else depthShort = dst2 + " " + dlv2.ToString("F1", Inv);

                    newRows.Add(new ZoneSpeciesRow {
                        ko = ko, spawnerCount = kv.Value,
                        weightRange = wmm2, depthStr = depthShort,
                        baitsStr = sbBaits.ToString(),
                    });
                }
                _zoneRows = newRows;
                _lastZoneRecoT = Time.realtimeSinceStartup;

                RZ_I("[zone:reco] === 카테고리별 최고 미끼 ===");
                var bestByType = new System.Collections.Generic.Dictionary<string, BaitScore>();
                foreach (var bs in scores)
                    if (!bestByType.ContainsKey(bs.type)) bestByType[bs.type] = bs;
                foreach (var kv in bestByType)
                {
                    string typeKor;
                    if (!BaitTypeKor.TryGetValue(kv.Key, out typeKor)) typeKor = kv.Key;
                    RZ_I("[zone:reco]   " + typeKor + " : " + FormatBait(kv.Value));
                }

                RZ_I("[zone:reco] === 종합 추천 Top 5 ===");
                for (int i = 0; i < scores.Count && i < 5; i++)
                {
                    var bs = scores[i];
                    string typeKor;
                    if (!BaitTypeKor.TryGetValue(bs.type, out typeKor)) typeKor = bs.type;
                    RZ_I("[zone:reco]   " + (i + 1) + ". " + typeKor + " - " + FormatBait(bs));
                }

                // 4) Aggregate depth — group by style and average within each
                //    so WATER_LEVEL (-3) and GROUND_LEVEL (+1) don't cancel out.
                var styleVotes = new System.Collections.Generic.Dictionary<string, int>();
                var styleDepthSum = new System.Collections.Generic.Dictionary<string, float>();
                foreach (var kv in weights)
                {
                    var fish = prefabBySpecies[kv.Key];
                    var st = fiNormalDepthStyle.GetValue(fish).ToString();
                    float dl = Convert.ToSingle(fiNormalDepthLevel.GetValue(fish));
                    if (!styleVotes.ContainsKey(st)) { styleVotes[st] = 0; styleDepthSum[st] = 0f; }
                    styleVotes[st] += kv.Value;
                    styleDepthSum[st] += kv.Value * dl;
                }

                // 게임이 표시하는 사이즈 문자열(예 '#8, #4, #1')은 lure 류에만
                //있음. 자연미끼는 hookSize 별도 필요 없으므로 lure top 5 만 대상.
                var lureScores = new System.Collections.Generic.List<BaitScore>();
                foreach (var s in scores)
                {
                    if (s.type == "equipmentBaits" || s.type == "equipmentBoilies"
                        || s.type == "equipmentFeederBaits") continue;
                    lureScores.Add(s);
                }
                int topN = Math.Min(lureScores.Count, 5);
                var sizeVotes = new System.Collections.Generic.Dictionary<string, int>();
                for (int i = 0; i < topN; i++)
                {
                    string s = lureScores[i].sizeStr;
                    if (string.IsNullOrEmpty(s)) continue;
                    if (!sizeVotes.ContainsKey(s)) sizeVotes[s] = 0;
                    sizeVotes[s]++;
                }
                string commonSize = ""; int topVoteSize = 0;
                foreach (var kv in sizeVotes)
                    if (kv.Value > topVoteSize) { topVoteSize = kv.Value; commonSize = kv.Key; }

                RZ_I("[zone:reco] === 추천 세팅 ===");
                if (!string.IsNullOrEmpty(commonSize))
                    RZ_I("[zone:reco]   추천 훅 사이즈  ~ " + commonSize
                        + "  (루어 Top " + topN + " 중 " + topVoteSize + "개)");
                else
                    RZ_I("[zone:reco]   추천 훅 사이즈  ~ (자연미끼 위주)");
                foreach (var kv in styleVotes)
                {
                    float avgD = styleDepthSum[kv.Key] / kv.Value;
                    string ref2 = kv.Key == "WATER_LEVEL" ? "수면 아래"
                        : kv.Key == "GROUND_LEVEL" ? "바닥 위" : kv.Key;
                    RZ_I("[zone:reco]   찌 깊이 (" + ref2 + " 기준) "
                        + Math.Abs(avgD).ToString("F1", Inv) + " m"
                        + "  (해당 어종 weight=" + kv.Value + ")");
                }
            }
            catch (Exception ex)
            {
                RZ_E("[zone:reco] " + ex.GetType().Name + ": " + ex.Message
                    + "\n" + ex.StackTrace);
            }
        }

        private struct BaitScore
        {
            public string name;
            public string prefab;
            public string type;
            public string producer;
            public string eqId;
            public int hookSize;
            public float score;
            public int level;
            public int price;
            public string sizeStr;
            public string weightStr;
            // species enum int -> interest (after weight-range filter).
            public System.Collections.Generic.Dictionary<int, float> perSpecies;
        }

        // Quiet-aware logger wrappers used inside ZoneReco. When the auto-
        // refresh tick triggers ZoneReco we don't want the BepInEx log to
        // spam every 30 seconds.
        private void RZ_I(string s) { if (!_zoneRecoQuiet) Logger.LogInfo(s); }
        private void RZ_W(string s) { if (!_zoneRecoQuiet) Logger.LogWarning(s); }
        private void RZ_E(string s) { Logger.LogError(s); }

        private static int CompareScoreThenPrice(BaitScore a, BaitScore b)
        {
            int c = b.score.CompareTo(a.score);          // higher score first
            if (c != 0) return c;
            // score tie -> cheaper first; price 0 (unknown) sinks to the end
            int ap = a.price > 0 ? a.price : int.MaxValue;
            int bp = b.price > 0 ? b.price : int.MaxValue;
            return ap.CompareTo(bp);
        }

        private string FormatBait(BaitScore bs)
        {
            bool isNatural = bs.type == "equipmentBaits"
                          || bs.type == "equipmentBoilies"
                          || bs.type == "equipmentFeederBaits";
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(bs.producer)) sb.Append(bs.producer).Append(" ");
            sb.Append(bs.name);
            if (!isNatural)
            {
                if (!string.IsNullOrEmpty(bs.sizeStr))
                    sb.Append("  훅 ").Append(bs.sizeStr);
                else if (bs.hookSize > 0)
                    sb.Append("  훅=").Append(bs.hookSize);
                if (!string.IsNullOrEmpty(bs.weightStr))
                    sb.Append("  적정 ").Append(bs.weightStr);
            }
            if (bs.level > 0) sb.Append("  Lv.").Append(bs.level);
            if (bs.price > 0) sb.Append("  ").Append(bs.price.ToString("N0", Inv)).Append("원");
            sb.Append("  score=").Append(bs.score.ToString("F3", Inv));
            return sb.ToString();
        }

        private struct CatalogInfo
        {
            public string equipName;
            public string translateName;
            public string producer;
            public string id;
            public int level;
            public int price;
            public string category;
            public string sizeStr;
            public string weightStr;
            public float weightMin;
            public float weightMax;
            public UnityEngine.GameObject prefab;
        }

        private static void ParseKgRange(string s, out float lo, out float hi)
        {
            // Format: '0.00 kg - 52.50 kg'.
            lo = 0; hi = 0;
            int dash = s.IndexOf('-');
            if (dash < 0) return;
            string a = s.Substring(0, dash);
            string b = s.Substring(dash + 1);
            float.TryParse(StripNonNumeric(a), System.Globalization.NumberStyles.Float, Inv, out lo);
            float.TryParse(StripNonNumeric(b), System.Globalization.NumberStyles.Float, Inv, out hi);
        }

        private static string StripNonNumeric(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsDigit(c) || c == '.' || c == '-') sb.Append(c);
            }
            return sb.ToString();
        }

        private void ProbeSpeciesEnum()
        {
            try
            {
                var fishT = FindType("Fish");
                if (ReferenceEquals(fishT, null)) return;
                Type spT = null;
                foreach (var nt in fishT.GetNestedTypes(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic))
                {
                    if (nt.Name == "Species" && nt.IsEnum) { spT = nt; break; }
                }
                if (ReferenceEquals(spT, null))
                {
                    Logger.LogWarning("[species:enum] Fish+Species enum not found");
                    return;
                }
                Logger.LogInfo("[species:enum] FullName=" + spT.FullName);
                var names = Enum.GetNames(spT);
                var values = Enum.GetValues(spT);
                for (int i = 0; i < names.Length; i++)
                    Logger.LogInfo("[species:enum]   " + i + " : " + names[i]
                        + " (=" + Convert.ToInt32(values.GetValue(i)) + ")");
            }
            catch (Exception ex)
            { Logger.LogError("[species:enum] " + ex.GetType().Name + ": " + ex.Message); }
        }

        private void ProbeFishInterest()
        {
            try
            {
                var flT = FindType("FishLikesParams");
                if (ReferenceEquals(flT, null)) return;
                Type fiT = null;
                foreach (var nt in flT.GetNestedTypes(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic))
                {
                    if (nt.Name == "FishInterest") { fiT = nt; break; }
                }
                if (ReferenceEquals(fiT, null))
                {
                    Logger.LogWarning("[bait:interests] FishInterest type not found");
                    return;
                }
                Logger.LogInfo("[bait:interests] FullName=" + fiT.FullName);
                var fis = fiT.GetFields(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                foreach (var fi in fis)
                    Logger.LogInfo("[bait:interests]   field " + fi.Name + " : " + fi.FieldType.FullName);

                // Dump one full bait's fishInterests entries.
                var baitT = FindType("Bait");
                if (ReferenceEquals(baitT, null)) return;
                var fiBT = baitT.GetField("baitType",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                var fiFLP = baitT.GetField("fishLikesParams",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                var fiList = flT.GetField("fishInterests",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                if (ReferenceEquals(fiBT, null) || ReferenceEquals(fiFLP, null) || ReferenceEquals(fiList, null)) return;

                // Pick a SPINNER (Mikado) — has clear `<2 >5 >5...` text.
                var allBaits = Resources.FindObjectsOfTypeAll(baitT);
                object pick = null;
                foreach (var b in allBaits)
                {
                    var btv = fiBT.GetValue(b);
                    if (btv != null && btv.ToString() == "SPINNER") { pick = b; break; }
                }
                if (pick == null && allBaits.Length > 0) pick = allBaits[0];
                if (pick == null) { Logger.LogInfo("[bait:interests] no bait sample"); return; }
                string nm = (pick is UnityEngine.Object) ? ((UnityEngine.Object)pick).name : "?";
                Logger.LogInfo("[bait:interests] sample = " + nm);

                var flp = fiFLP.GetValue(pick);
                if (flp == null) return;
                var list = fiList.GetValue(flp) as System.Collections.IEnumerable;
                if (list == null) return;
                int idx = 0;
                foreach (var e in list)
                {
                    var sb = new StringBuilder();
                    sb.Append("[").Append(idx).Append("] ");
                    bool first = true;
                    foreach (var fi in fis)
                    {
                        object v;
                        try { v = fi.GetValue(e); } catch { continue; }
                        if (!first) sb.Append(", ");
                        first = false;
                        sb.Append(fi.Name).Append("=");
                        if (v == null) sb.Append("null");
                        else
                        {
                            var t = v.GetType();
                            bool isString = ReferenceEquals(t, typeof(string));
                            if (isString || t.IsPrimitive || t.IsEnum) sb.Append(v);
                            else sb.Append("<").Append(t.Name).Append(">");
                        }
                    }
                    Logger.LogInfo("[bait:interests]   " + sb);
                    idx++;
                    if (idx > 20) { Logger.LogInfo("[bait:interests]   ..."); break; }
                }
            }
            catch (Exception ex)
            { Logger.LogError("[bait:interests] " + ex.GetType().Name + ": " + ex.Message); }
        }

        private void ProbeFishLikesParams()
        {
            try
            {
                var flT = FindType("FishLikesParams");
                if (ReferenceEquals(flT, null))
                {
                    Logger.LogWarning("[bait:likes] FishLikesParams type not found");
                    return;
                }
                Logger.LogInfo("[bait:likes] FishLikesParams FullName=" + flT.FullName);
                var fis = flT.GetFields(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                foreach (var fi in fis)
                    Logger.LogInfo("[bait:likes]   field " + fi.Name + " : " + fi.FieldType.FullName);

                // Sample one bait of each baitType and dump its fishLikesParams values.
                var baitT = FindType("Bait");
                if (ReferenceEquals(baitT, null)) return;
                var fiBT = baitT.GetField("baitType",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                var fiFLP = baitT.GetField("fishLikesParams",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                if (ReferenceEquals(fiBT, null) || ReferenceEquals(fiFLP, null)) return;

                var seen = new System.Collections.Generic.HashSet<string>();
                var allBaits = Resources.FindObjectsOfTypeAll(baitT);
                foreach (var b in allBaits)
                {
                    var btv = fiBT.GetValue(b);
                    string bts = btv == null ? "?" : btv.ToString();
                    if (!seen.Add(bts)) continue;
                    var flp = fiFLP.GetValue(b);
                    string nm = (b is UnityEngine.Object) ? ((UnityEngine.Object)b).name : "?";
                    if (flp == null)
                    {
                        Logger.LogInfo("[bait:likes] " + bts + " (" + nm + "): fishLikesParams=null");
                        continue;
                    }
                    var sb = new StringBuilder();
                    sb.Append(bts).Append(" (").Append(nm).Append("): ");
                    bool first = true;
                    foreach (var fi in fis)
                    {
                        object v;
                        try { v = fi.GetValue(flp); } catch { continue; }
                        if (!first) sb.Append(", ");
                        first = false;
                        sb.Append(fi.Name).Append("=");
                        if (v == null) { sb.Append("null"); continue; }
                        var t = v.GetType();
                        bool isString = ReferenceEquals(t, typeof(string));
                        if (isString || t.IsPrimitive || t.IsEnum) sb.Append(v);
                        else if (typeof(System.Collections.IEnumerable).IsAssignableFrom(t) && !isString)
                        {
                            sb.Append("[");
                            int i = 0;
                            foreach (var e in (System.Collections.IEnumerable)v)
                            {
                                if (i > 0) sb.Append(",");
                                if (i >= 12) { sb.Append("..."); break; }
                                sb.Append(e == null ? "null" : e.ToString());
                                i++;
                            }
                            sb.Append("]");
                        }
                        else sb.Append("<").Append(t.Name).Append(">");
                    }
                    Logger.LogInfo("[bait:likes]   " + sb);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("[bait:likes] " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void ProbeBaitType()
        {
            try
            {
                var baitT = FindType("Bait");
                if (ReferenceEquals(baitT, null))
                {
                    Logger.LogWarning("[bait:probe] Bait type not found");
                    return;
                }
                Logger.LogInfo("[bait:probe] Bait FullName=" + baitT.FullName);
                var fis = baitT.GetFields(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                Logger.LogInfo("[bait:probe] " + fis.Length + " fields");
                var keywords = new string[] {
                    "name","type","size","color","species","fish","level",
                    "category","kind","group","tag","prefer","like"
                };
                foreach (var fi in fis)
                {
                    string n = fi.Name.ToLowerInvariant();
                    bool match = false;
                    for (int i = 0; i < keywords.Length; i++)
                        if (n.IndexOf(keywords[i], StringComparison.Ordinal) >= 0) { match = true; break; }
                    if (!match) continue;
                    Logger.LogInfo("[bait:probe]   field " + fi.Name + " : " + fi.FieldType.FullName);
                }

                // Look for the BaitType enum and dump its values.
                var btT = FindType("BaitType");
                if (!ReferenceEquals(btT, null) && btT.IsEnum)
                {
                    Logger.LogInfo("[bait:probe] BaitType enum FullName=" + btT.FullName);
                    foreach (var n in Enum.GetNames(btT))
                        Logger.LogInfo("[bait:probe]   " + n);
                }
                else
                {
                    Logger.LogInfo("[bait:probe] BaitType enum not found");
                }

                // Dump one sample bait if any are loaded.
                var allBaits = Resources.FindObjectsOfTypeAll(baitT);
                Logger.LogInfo("[bait:probe] live Bait objects: " + allBaits.Length);
                if (allBaits.Length > 0)
                {
                    var b = allBaits[0];
                    string nm = (b is UnityEngine.Object) ? ((UnityEngine.Object)b).name : b.ToString();
                    Logger.LogInfo("[bait:probe] sample = '" + nm + "'");
                    foreach (var fi in fis)
                    {
                        string n = fi.Name.ToLowerInvariant();
                        bool match = false;
                        for (int i = 0; i < keywords.Length; i++)
                            if (n.IndexOf(keywords[i], StringComparison.Ordinal) >= 0) { match = true; break; }
                        if (!match) continue;
                        object v;
                        try { v = fi.GetValue(b); } catch { continue; }
                        if (v == null) { Logger.LogInfo("[bait:probe]   " + fi.Name + " = null"); continue; }
                        var t = v.GetType();
                        bool isString = ReferenceEquals(t, typeof(string));
                        if (isString || t.IsPrimitive || t.IsEnum)
                            Logger.LogInfo("[bait:probe]   " + fi.Name + " = " + v);
                        else
                            Logger.LogInfo("[bait:probe]   " + fi.Name + " : " + t.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("[bait:probe] " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void ProbeFishTerrainMods()
        {
            // TerrainModifiers is empty on most prefabs — try every species
            // and dump the first non-empty list's element schema + values.
            try
            {
                var fsType = FindType("FishSpawner");
                var fishType = FindType("Fish");
                if (ReferenceEquals(fsType, null) || ReferenceEquals(fishType, null)) return;

                var fiPrefab = fsType.GetField("fishPrefab",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                var fiTM = fishType.GetField("terrainModifiers",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                var fiSpecies = fishType.GetField("species",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                if (ReferenceEquals(fiPrefab, null) || ReferenceEquals(fiTM, null)) return;

                var seen = new System.Collections.Generic.HashSet<string>();
                bool schemaLogged = false;
                var spawners = UnityEngine.Object.FindObjectsOfType(fsType);

                foreach (var s in spawners)
                {
                    var mb = s as MonoBehaviour;
                    if (mb == null || mb.gameObject == null) continue;
                    var fish = fiPrefab.GetValue(mb);
                    if (fish == null) continue;
                    object spv = ReferenceEquals(fiSpecies, null) ? null : fiSpecies.GetValue(fish);
                    var spName = spv == null ? "?" : spv.ToString();
                    if (!seen.Add(spName)) continue;

                    var list = fiTM.GetValue(fish) as System.Collections.IEnumerable;
                    int count = 0;
                    if (list != null) foreach (var _ in list) count++;
                    Logger.LogInfo("[fish:terrain] " + spName + "  terrainModifiers count=" + count);
                    if (count == 0 || list == null) continue;

                    // Element schema (one-shot).
                    object first = null;
                    foreach (var e in list) { first = e; break; }
                    if (first == null) continue;
                    var elT = first.GetType();
                    if (!schemaLogged)
                    {
                        Logger.LogInfo("[fish:terrain] element type = " + elT.FullName);
                        var fis = elT.GetFields(
                            System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.NonPublic |
                            System.Reflection.BindingFlags.Instance);
                        foreach (var fi in fis)
                            Logger.LogInfo("[fish:terrain]   field " + fi.Name + " : " + fi.FieldType.FullName);
                        schemaLogged = true;
                    }

                    int idx = 0;
                    foreach (var e in list)
                    {
                        if (idx > 8) { Logger.LogInfo("[fish:terrain]   ..."); break; }
                        var fis = elT.GetFields(
                            System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.NonPublic |
                            System.Reflection.BindingFlags.Instance);
                        var sb = new StringBuilder();
                        sb.Append("[").Append(idx).Append("] ");
                        bool first2 = true;
                        foreach (var fi in fis)
                        {
                            object v;
                            try { v = fi.GetValue(e); } catch { continue; }
                            if (!first2) sb.Append(", ");
                            first2 = false;
                            sb.Append(fi.Name).Append("=");
                            if (v == null) sb.Append("null");
                            else
                            {
                                var t = v.GetType();
                                bool isString = ReferenceEquals(t, typeof(string));
                                if (isString || t.IsPrimitive || t.IsEnum) sb.Append(v);
                                else sb.Append("<").Append(t.Name).Append(">");
                            }
                        }
                        Logger.LogInfo("[fish:terrain]   " + spName + " " + sb);
                        idx++;
                    }
                }
                Logger.LogInfo("[fish:terrain] done. " + seen.Count + " species inspected.");
            }
            catch (Exception ex)
            {
                Logger.LogError("[fish:terrain] " + ex.GetType().Name + ": " + ex.Message
                    + "\n" + ex.StackTrace);
            }
        }

        private void ProbeFishType()
        {
            try
            {
                var fishType = FindType("Fish");
                if (ReferenceEquals(fishType, null))
                {
                    Logger.LogWarning("[fish:fields] Fish type not found");
                    return;
                }
                Logger.LogInfo("[fish:fields] Fish FullName=" + fishType.FullName);

                // Filter to what we likely care about: name/species/likes/bait/hook/depth/prefer.
                var keywords = new string[] {
                    "name", "species", "type",
                    "like", "prefer", "bait", "boilie", "plant", "terrain",
                    "hook", "depth", "weight", "size", "length",
                    "lure", "fly", "spinner", "feeder", "pop", "wobbler"
                };
                var fis = fishType.GetFields(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                Logger.LogInfo("[fish:fields] " + fis.Length + " fields total. Filtered:");
                foreach (var fi in fis)
                {
                    string n = fi.Name.ToLowerInvariant();
                    bool match = false;
                    for (int i = 0; i < keywords.Length; i++)
                        if (n.IndexOf(keywords[i], StringComparison.Ordinal) >= 0) { match = true; break; }
                    if (!match) continue;
                    Logger.LogInfo("[fish:fields]   " + fi.Name + " : " + fi.FieldType.FullName);
                }

                // Pick a live BullTrout fish prefab via FishSpawner.fishPrefab.
                var fsType = FindType("FishSpawner");
                if (ReferenceEquals(fsType, null)) return;
                var spawners = UnityEngine.Object.FindObjectsOfType(fsType);
                MonoBehaviour pick = null;
                foreach (var s in spawners)
                {
                    var mb = s as MonoBehaviour;
                    if (mb != null && mb.gameObject != null
                        && mb.gameObject.name.IndexOf("BullTrout", StringComparison.Ordinal) >= 0)
                    { pick = mb; break; }
                }
                if (pick == null) { Logger.LogInfo("[fish:fields] no BullTrout spawner"); return; }

                var fiPrefab = fsType.GetField("fishPrefab",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                if (ReferenceEquals(fiPrefab, null)) return;
                var fish = fiPrefab.GetValue(pick);
                if (ReferenceEquals(fish, null))
                {
                    Logger.LogInfo("[fish:fields] BullTrout fishPrefab is null");
                    return;
                }

                Logger.LogInfo("[fish:fields] sample = BullTrout prefab '"
                    + ((fish is UnityEngine.Object) ? ((UnityEngine.Object)fish).name : fish.ToString()) + "'");
                foreach (var fi in fis)
                {
                    string n = fi.Name.ToLowerInvariant();
                    bool match = false;
                    for (int i = 0; i < keywords.Length; i++)
                        if (n.IndexOf(keywords[i], StringComparison.Ordinal) >= 0) { match = true; break; }
                    if (!match) continue;

                    object v;
                    try { v = fi.GetValue(fish); }
                    catch { continue; }
                    if (v == null) { Logger.LogInfo("[fish:fields]   " + fi.Name + " = null"); continue; }
                    var t = v.GetType();
                    bool isString = ReferenceEquals(t, typeof(string));
                    if (isString || t.IsPrimitive || t.IsEnum)
                        Logger.LogInfo("[fish:fields]   " + fi.Name + " = " + v);
                    else if (typeof(System.Collections.IEnumerable).IsAssignableFrom(t) && !isString)
                    {
                        var sb = new StringBuilder();
                        sb.Append(fi.Name).Append(" : ").Append(t.Name).Append(" [");
                        int i = 0;
                        foreach (var e in (System.Collections.IEnumerable)v)
                        {
                            if (i > 0) sb.Append(", ");
                            if (i >= 12) { sb.Append("..."); break; }
                            sb.Append(e == null ? "null" : e.ToString());
                            i++;
                        }
                        sb.Append("]");
                        Logger.LogInfo("[fish:fields]   " + sb);
                    }
                    else
                        Logger.LogInfo("[fish:fields]   " + fi.Name + " : " + t.Name + " = " + v);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("[fish:fields] " + ex.GetType().Name + ": " + ex.Message
                    + "\n" + ex.StackTrace);
            }
        }

        private void ProbeFishSpawner()
        {
            try
            {
                var fsType = FindType("FishSpawner");
                if (ReferenceEquals(fsType, null))
                {
                    Logger.LogWarning("[fish:probe] FishSpawner type not found");
                    return;
                }
                Logger.LogInfo("[fish:probe] FishSpawner FullName=" + fsType.FullName);
                var fis = fsType.GetFields(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                foreach (var fi in fis)
                    Logger.LogInfo("[fish:probe]   field " + fi.Name + " : " + fi.FieldType.FullName);

                // Dump one live instance's primitive/string fields so we can
                // see actual species/bait/likes values.
                var spawners = UnityEngine.Object.FindObjectsOfType(fsType);
                if (spawners.Length == 0)
                {
                    Logger.LogInfo("[fish:probe] no live spawners");
                    return;
                }
                MonoBehaviour pick = null;
                foreach (var s in spawners)
                {
                    var mb = s as MonoBehaviour;
                    if (mb != null && mb.gameObject != null
                        && mb.gameObject.name.IndexOf("BullTrout", StringComparison.Ordinal) >= 0)
                    { pick = mb; break; }
                }
                if (pick == null) pick = spawners[0] as MonoBehaviour;
                Logger.LogInfo("[fish:probe] sample = " + pick.gameObject.name);
                foreach (var fi in fis)
                {
                    object v;
                    try { v = fi.GetValue(pick); }
                    catch { continue; }
                    if (v == null) { Logger.LogInfo("[fish:probe]   " + fi.Name + " = null"); continue; }
                    var t = v.GetType();
                    bool isString = ReferenceEquals(t, typeof(string));
                    if (isString || t.IsPrimitive || t.IsEnum)
                        Logger.LogInfo("[fish:probe]   " + fi.Name + " = " + v);
                    else if (typeof(System.Collections.IEnumerable).IsAssignableFrom(t) && !isString)
                    {
                        int n = 0;
                        foreach (var _ in (System.Collections.IEnumerable)v) n++;
                        Logger.LogInfo("[fish:probe]   " + fi.Name + " : " + t.Name + " count=" + n);
                    }
                    else
                        Logger.LogInfo("[fish:probe]   " + fi.Name + " : " + t.Name);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("[fish:probe] " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void Update()
        {
            if (!_firstTickLogged)
            {
                _firstTickLogged = true;
                Logger.LogInfo("first Update tick fired");
            }

            try { DrainCommands(); }
            catch (Exception ex) { Logger.LogError("DrainCommands: " + ex.Message); }

            // Auto-refresh the zone reco overlay every ZONE_RECO_INTERVAL
            // seconds. Runs silently (BepInEx log not spammed); the manual
            // `zone:reco` command still produces a verbose dump.
            if (Time.realtimeSinceStartup - _lastZoneRecoT > ZONE_RECO_INTERVAL)
            {
                _zoneRecoQuiet = true;
                try { ZoneReco(); }
                catch (Exception ex) { Logger.LogError("ZoneReco auto: " + ex.Message); }
                _zoneRecoQuiet = false;
            }

            // BackQuote (` / ~ key, top-left) saves the current player view.
            // The game disables Unity Input while in fishing mode, so we
            // poll the OS key state directly via Win32 GetAsyncKeyState.
            // VK_OEM_3 (0xC0) is the ` / ~ key on US-style keyboards.
            bool bqNow = (GetAsyncKeyState(0xC0) & 0x8000) != 0;
            if (bqNow && !_bqPrev && Application.isFocused)
            {
                Logger.LogInfo("[view] backquote pressed -> saving");
                try
                {
                    TrySaveView();
                    Logger.LogInfo("[view] TrySaveView returned normally");
                }
                catch (Exception ex)
                {
                    Logger.LogError("[view] outer caught: "
                        + ex.GetType().FullName + " " + ex.Message
                        + "\n" + ex.StackTrace);
                }
            }
            _bqPrev = bqNow;
            // While a restore window is active, keep clamping the UFPS camera
            // pose back to the saved orientation. The fishing watch-camera
            // animation otherwise yanks it sideways and never restores it.
            if (_savedPresent && Time.realtimeSinceStartup < _restoreUntil)
            {
                TryApplyView();
            }

            // Use realtimeSinceStartup — `unscaledTime` can stall in menus
            // depending on Unity version, which would silently pin the
            // rate-limit gate and stop us from ever calling Send.
            float now = Time.realtimeSinceStartup;
            if (now - _lastSend < 1f / SEND_HZ) return;
            _lastSend = now;

            try
            {
                Emit();
                _emitCount++;
                if (_emitCount == 1 || _emitCount == 60 || _emitCount == 600)
                    Logger.LogInfo("emitted packet #" + _emitCount);
            }
            catch (Exception ex)
            {
                Logger.LogError("Emit: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        private void Emit()
        {
            _sb.Length = 0;
            _sb.Append('{');

            var gc = GameController.Instance;
            if (gc == null || gc.fishingPlayer == null)
            {
                _sb.Append("\"ok\":false}");
                Send(); return;
            }

            var hands = gc.fishingPlayer.currentHands;
            if (hands == null)
            {
                _sb.Append("\"ok\":false,\"reason\":\"no_hands\"}");
                Send(); return;
            }

            var line = hands.fishingLine;
            var f = hands.fishingFloat;
            var fp = gc.fishingPlayer;

            // Player/game state — drives the catch-dialog and hookup signals.
            string playerState = fp != null ? fp.currentState.ToString() : "";
            bool watchFish = playerState == "WATCH_FISH";
            bool hasFish = fp != null && fp.fish != null;
            bool hasJunk = fp != null && fp.junk != null;

            if (f == null)
            {
                _sb.Append("\"ok\":true,\"hasFloat\":false");
                AppendLine(line);
                AppendPlayer(playerState, watchFish, hasFish, hasJunk);
                _sb.Append('}');
                Send(); return;
            }

            var wp = f.transform.position;

            // Pick the camera the gameplay HUD is rendered through.
            Camera cam = null;
            if (fp.ufpsWeaponCamera != null) cam = fp.ufpsWeaponCamera;
            else if (fp.ufpsCameraCamera != null) cam = fp.ufpsCameraCamera;
            else cam = Camera.main;

            float sx = -1f, sy = -1f;
            bool sv = false;
            int cw = 0, ch = 0;
            if (cam != null)
            {
                var v = cam.WorldToScreenPoint(wp);
                sv = v.z > 0f;
                sx = v.x;
                cw = cam.pixelWidth;
                ch = cam.pixelHeight;
                // Unity screen Y is bottom-up; flip so top-left origin matches OpenCV / mss.
                sy = ch - v.y;
            }

            var rb = f.rigidbody;
            float vy = rb != null ? rb.velocity.y : 0f;
            float vmag = rb != null ? rb.velocity.magnitude : 0f;

            _sb.Append("\"ok\":true,\"hasFloat\":true,");
            App("t", Time.time, "F4"); _sb.Append(',');
            App("baitWasThrown", hands.baitWasThrown); _sb.Append(',');
            App("isOnWater", f.isOnWater); _sb.Append(',');
            App("canHitWater", f.canHitWater); _sb.Append(',');
            App("isLoose", f.isLoose); _sb.Append(',');
            App("isTryAnimation", f.isTryAnimation); _sb.Append(',');
            App("fishTriesTimer", f.fishTriesTimer, "F3"); _sb.Append(',');
            App("fishTriesPullTimer", f.fishTriesPullTimer, "F3"); _sb.Append(',');
            App("burdenFactor", f.burdenFactor, "F3"); _sb.Append(',');
            App("floatingEasedOffset", f.floatingEasedOffset, "F4"); _sb.Append(',');
            App("velY", vy, "F3"); _sb.Append(',');
            App("velMag", vmag, "F3"); _sb.Append(',');
            _sb.Append("\"world\":[")
                .Append(wp.x.ToString("F3", Inv)).Append(',')
                .Append(wp.y.ToString("F3", Inv)).Append(',')
                .Append(wp.z.ToString("F3", Inv)).Append("],");
            _sb.Append("\"screen\":[")
                .Append(sx.ToString("F1", Inv)).Append(',')
                .Append(sy.ToString("F1", Inv)).Append("],");
            _sb.Append("\"camSize\":[").Append(cw).Append(',').Append(ch).Append("],");
            App("screenVisible", sv);
            AppendLine(line);
            AppendPlayer(playerState, watchFish, hasFish, hasJunk);
            _sb.Append('}');

            Send();
        }

        private void AppendPlayer(string playerState, bool watchFish, bool hasFish, bool hasJunk)
        {
            _sb.Append(",\"playerState\":\"").Append(playerState).Append("\",");
            App("watchFish", watchFish); _sb.Append(',');
            App("hasFish", hasFish); _sb.Append(',');
            App("hasJunk", hasJunk);
        }

        private void AppendLine(FishingLine line)
        {
            if (line == null)
            {
                _sb.Append(",\"hasLine\":false");
                return;
            }
            _sb.Append(",\"hasLine\":true,");
            App("currentTension", line.currentTension, "F4"); _sb.Append(',');
            App("pumpTension", line.pumpTension, "F4"); _sb.Append(',');
            App("tryPullTension", line.tryPullTension, "F4"); _sb.Append(',');
            App("currentTryReelTension", line.currentTryReelTension, "F4"); _sb.Append(',');
            App("stretchToDistance", line.stretchToDistance, "F4"); _sb.Append(',');
            App("stretchFactor", line.stretchFactor, "F4"); _sb.Append(',');
            App("breakTensionTimer", line.breakTensionTimer, "F3"); _sb.Append(',');
            App("looseTensionFactor", line.looseTensionFactor, "F4"); _sb.Append(',');
            App("looseLength", line.looseLength, "F3"); _sb.Append(',');
            App("lineIsLoose", line.isLoose); _sb.Append(',');
            App("lineIsLooseColliders", line.isLooseColliders); _sb.Append(',');
            App("isReeling", line.isReeling, "F2"); _sb.Append(',');
            App("durability", line.durability, "F3"); _sb.Append(',');
            _sb.Append("\"lineState\":").Append((int)line.lineState).Append(',');
            _sb.Append("\"lineType\":").Append((int)line.lineType);
        }

        private void App(string key, bool v)
        {
            _sb.Append('"').Append(key).Append("\":").Append(v ? "true" : "false");
        }

        private void App(string key, float v, string fmt)
        {
            _sb.Append('"').Append(key).Append("\":").Append(v.ToString(fmt, Inv));
        }

        private void Send()
        {
            var bytes = Encoding.UTF8.GetBytes(_sb.ToString());
            try
            {
                _udp.Send(bytes, bytes.Length, _ep);
            }
            catch (SocketException ex)
            {
                // Socket entered an error state — recreate it so the next
                // tick has a clean send path.
                Logger.LogWarning("send failed (" + ex.SocketErrorCode + "), recreating socket");
                try { _udp?.Close(); } catch { }
                _udp = MakeSendSocket();
            }
        }
    }
}
