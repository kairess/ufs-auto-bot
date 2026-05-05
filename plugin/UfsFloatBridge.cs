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

        private void Update()
        {
            if (!_firstTickLogged)
            {
                _firstTickLogged = true;
                Logger.LogInfo("first Update tick fired");
            }

            try { DrainCommands(); }
            catch (Exception ex) { Logger.LogError("DrainCommands: " + ex.Message); }

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
