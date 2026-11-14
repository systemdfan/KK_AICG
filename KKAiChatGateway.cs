using System;
using System.Collections.Generic;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using KKAiChatGateway.Policy;
using LitJson;
using UnityEngine;
using WebSocketSharp;

namespace KKAiChatGateway
{
    [BepInPlugin(GUID, PluginName, Version)]
    public class KKAiChatGatewayPlugin : BaseUnityPlugin
    {
        public const string GUID = "com.systemdfag.kkaicg";
        public const string PluginName = "KKAiChatGateway";
        public const string Version = "0.1.0";

        private const string ServerUri = "ws://127.0.0.1:8085";
        private const int ReconnectDelayMs = 5000;

        private WebSocket _ws;
        private Thread _connectionThread;
        private volatile bool _shutdown;
        private readonly object _wsLock = new object();

        private ChatUI _chatUI;
        private NpcActor _npcActor;
        private StateValidator _validator;
        private KeyResolver _keyResolver;
        private ConfigEntry<KeyboardShortcut> _toggleKey;
        private ConfigEntry<KeyboardShortcut> _dumpListsKey;
        private ConfigEntry<string> _fallbackCharacterName;
        private ConfigEntry<bool> _autoOpenInTalkScene;

        private float _sceneCheckTimer;
        private bool _talkSceneActive;

        /// <summary>
        /// Actions coming from the WS thread, executed on the Unity main thread in Update().
        /// Unity/KKAPI calls are only safe on the main thread.
        /// </summary>
        private readonly Queue<Action> _mainThreadQueue = new Queue<Action>();
        private readonly object _queueLock = new object();

        private void Awake()
        {
            Logger.LogInfo(PluginName + " v" + Version + " loaded. Connecting to " + ServerUri + " ...");

            _toggleKey = Config.Bind("UI", "ToggleChatKey", new KeyboardShortcut(KeyCode.F9),
                "Hotkey to show/hide the kkaicg chat window.");
            _fallbackCharacterName = Config.Bind("Chat", "FallbackCharacterName", "Unknown",
                "Used as the NPC name when no talk scene is active (temporary, until character linking is implemented).");
            _autoOpenInTalkScene = Config.Bind("UI", "AutoOpenInTalkScene", true,
                "Automatically show the chat window when a conversation with an NPC starts (and hide when it ends).");
            _dumpListsKey = Config.Bind("UI", "DumpListsKey", new KeyboardShortcut(KeyCode.F10),
                "While in a talk scene: write all valid emotion/pose names to BepInEx/config/kkaicg-lists.txt.");

            _npcActor = new NpcActor(Logger, Paths.ConfigPath);
            _npcActor.LoadMapping();

            _validator = new StateValidator();
            _keyResolver = new KeyResolver(Paths.ConfigPath, Logger);

            _chatUI = new ChatUI(OnPlayerSubmit);
            _chatUI.SetConnectionStatus("(offline)");
            _chatUI.AddSystemMessage("Нажмите " + _toggleKey.Value + " чтобы скрыть/показать окно.");

            _connectionThread = new Thread(ConnectionLoop);
            _connectionThread.IsBackground = true;
            _connectionThread.Start();
        }

        private void OnGUI()
        {
            if (_chatUI != null) _chatUI.OnGUI();
        }

        private void Update()
        {
            if (_toggleKey.Value.IsDown())
                _chatUI.Toggle();

            if (_dumpListsKey.Value.IsDown())
                _npcActor.DumpLists();

            // Auto show/hide the chat window when a talk scene starts/ends.
            _sceneCheckTimer -= Time.deltaTime;
            if (_sceneCheckTimer <= 0f)
            {
                _sceneCheckTimer = 0.5f;
                CheckTalkScene();
            }

            // Drain queued main-thread actions (bounded per frame to avoid hitches).
            for (var i = 0; i < 10; i++)
            {
                Action action = null;
                lock (_queueLock)
                {
                    if (_mainThreadQueue.Count > 0)
                        action = _mainThreadQueue.Dequeue();
                }
                if (action == null) break;

                try { action(); }
                catch (Exception ex) { Logger.LogError("[kkaicg] Main-thread action failed: " + ex); }
            }
        }

        private void CheckTalkScene()
        {
            if (!_autoOpenInTalkScene.Value) return;

            var active = false;
            try
            {
                var talkScene = FindObjectOfType<TalkScene>();
                active = talkScene != null && talkScene.targetHeroine != null;
            }
            catch (Exception) { /* scene not loaded */ }

            if (active == _talkSceneActive) return;
            _talkSceneActive = active;

            if (active)
            {
                _chatUI.Show();
                _chatUI.AddSystemMessage("Разговор с " + GetCurrentCharacterName() + " начат.");
                Logger.LogInfo("[kkaicg] Talk scene started with " + GetCurrentCharacterName());
            }
            else
            {
                _chatUI.Hide();
                Logger.LogInfo("[kkaicg] Talk scene ended.");
            }
        }

        private void RunOnMainThread(Action action)
        {
            lock (_queueLock)
                _mainThreadQueue.Enqueue(action);
        }

        #region WebSocket connection (background thread)

        /// <summary>
        /// Maintains a persistent WebSocket connection, reconnecting on failure.
        /// Runs on a background thread because the game's Mono runtime (Unity 5.6, .NET 3.5 profile)
        /// has no System.Threading.Tasks.
        /// </summary>
        private void ConnectionLoop()
        {
            while (!_shutdown)
            {
                var closed = new ManualResetEvent(false);

                try
                {
                    WebSocket ws;
                    lock (_wsLock)
                    {
                        _ws = new WebSocket(ServerUri);
                        ws = _ws;
                    }

                    ws.OnOpen += (sender, e) =>
                    {
                        Logger.LogInfo("[kkaicg] Connected to SillyTavern server.");
                        RunOnMainThread(() => _chatUI.SetConnectionStatus("(online)"));
                        SendJson(new { type = "hello", version = Version });
                    };

                    ws.OnMessage += (sender, e) =>
                    {
                        if (e.IsText)
                            HandleMessage(e.Data);
                    };

                    ws.OnError += (sender, e) =>
                    {
                        Logger.LogWarning("[kkaicg] WebSocket error: " + e.Message);
                    };

                    ws.OnClose += (sender, e) =>
                    {
                        Logger.LogInfo("[kkaicg] Connection closed (code " + e.Code + ").");
                        RunOnMainThread(() => _chatUI.SetConnectionStatus("(offline)"));
                        closed.Set();
                    };

                    ws.Connect();

                    // Wait until the session ends (close, error or shutdown).
                    closed.WaitOne();
                }
                catch (Exception ex)
                {
                    if (!_shutdown)
                        Logger.LogWarning("[kkaicg] Connection error: " + ex.Message);
                }
                finally
                {
                    CleanupSocket();
                    closed.Close();
                }

                if (!_shutdown)
                {
                    Logger.LogInfo("[kkaicg] Reconnecting in " + (ReconnectDelayMs / 1000) + " seconds...");
                    for (var i = 0; i < ReconnectDelayMs / 100 && !_shutdown; i++)
                        Thread.Sleep(100);
                }
            }
        }

        private void CleanupSocket()
        {
            lock (_wsLock)
            {
                if (_ws != null)
                {
                    try
                    {
                        if (_ws.IsAlive)
                            _ws.Close();
                    }
                    catch (Exception) { /* ignore during shutdown */ }
                    _ws = null;
                }
            }
        }

        #endregion

        #region Sending

        /// <summary>
        /// Thread-safe plain-text send. Safe to call from any thread.
        /// </summary>
        public void Send(string message)
        {
            WebSocket ws;
            lock (_wsLock)
                ws = _ws;

            if (ws == null || !ws.IsAlive) return;

            try
            {
                ws.Send(message);
                Logger.LogInfo("[kkaicg] Sent: " + message);
            }
            catch (Exception ex)
            {
                Logger.LogWarning("[kkaicg] Send failed: " + ex.Message);
            }
        }

        /// <summary>Serialize an object to JSON and send it.</summary>
        public void SendJson(object payload)
        {
            Send(JsonMapper.ToJson(payload));
        }

        /// <summary>Called by the chat UI when the player submits a message (main thread).</summary>
        private void OnPlayerSubmit(string text)
        {
            // Local commands: never sent to SillyTavern.
            var lower = text.ToLowerInvariant();
            if (lower == "/dump")
            {
                _chatUI.AddSystemMessage("Дампим списки эмоций/поз... (см. BepInEx/config/kkaicg-lists.txt)");
                _npcActor.DumpLists();
                return;
            }
            if (lower == "/help")
 {
                _chatUI.AddSystemMessage("Команды: /dump — дампить доступные эмоции/позы в файл; " +
                    _toggleKey.Value + " — скрыть/показать окно; Esc — закрыть окно.");
                return;
            }
            if (lower == "/close")
            {
                _chatUI.Hide();
                return;
            }

            SendPlayerMessage(text);
        }

        /// <summary>Send a player chat message to SillyTavern (protocol: player_message).</summary>
        public void SendPlayerMessage(string text)
        {
            GetCurrentCharacterInfo(out var name, out var personality);
            var authorized = _keyResolver.IsAuthorized();
            var policy = _validator.Evaluate(name, personality, authorized);

            if (policy.RequiresAuthorization)
            {
                _chatUI.AddSystemMessage("Защищённый персонаж: активирован режим ограниченного взаимодействия.");
                ApplyPolicyBias(policy);
            }

            // TODO: fill full character info (KKAPI + ExtendedData card link, real stats).
            SendJson(new
            {
                type = "player_message",
                text = text,
                character = new
                {
                    name = name,
                    stCard = "",
                    stats = new { affection = 0, trust = 0, arousal = 0, anger = 0 },
                    sexExperience = "unknown",
                    protection = policy.RequiresAuthorization ? new
                    {
                        active = true,
                        promptB64 = policy.PromptB64
                    } : null
                },
            });
        }

        private void ApplyPolicyBias(ValidationResult policy)
        {
            ApplyStatDelta("affection", policy.AffectionBias);
            ApplyStatDelta("trust", policy.TrustBias);
            ApplyStatDelta("arousal", policy.ArousalBias);
            ApplyStatDelta("anger", policy.AngerBias);
        }

        /// <summary>
        /// Name of the NPC the player is currently talking to (main thread only).
        /// Falls back to the configured placeholder outside of talk scenes.
        /// </summary>
        private string GetCurrentCharacterName()
        {
            GetCurrentCharacterInfo(out var name, out _);
            return name;
        }

        /// <summary>
        /// Full character info for the active talk scene (main thread only).
        /// </summary>
        private void GetCurrentCharacterInfo(out string name, out int personality)
        {
            name = _fallbackCharacterName.Value;
            personality = -1;
            try
            {
                var heroine = _npcActor.GetCurrentHeroine();
                if (heroine != null)
                {
                    name = heroine.Name;
                    personality = heroine.FixCharaIDOrPersonality;
                    return;
                }
            }
            catch (Exception) { /* not in a talk scene */ }
        }

        #endregion

        #region Incoming message dispatch

        private void HandleMessage(string raw)
        {
            // Legacy plain-text handshake.
            if (raw == "World")
            {
                Logger.LogInfo("[kkaicg] Legacy handshake reply received.");
                return;
            }

            JsonData msg;
            try
            {
                msg = JsonMapper.ToObject(raw);
            }
            catch (Exception)
            {
                Logger.LogWarning("[kkaicg] Non-JSON message ignored: " + raw.Substring(0, Math.Min(200, raw.Length)));
                return;
            }

            var type = msg.IsObject && msg.Keys.Contains("type") ? (string)msg["type"] : null;
            switch (type)
            {
                case "ack":
                    Logger.LogInfo("[kkaicg] Server acknowledged handshake.");
                    break;

                case "llm_response":
                    var response = LlmResponse.FromJson(msg);
                    Logger.LogInfo("[kkaicg] LLM response: " + response);
                    RunOnMainThread(() => ApplyLlmResponse(response));
                    break;

                default:
                    Logger.LogWarning("[kkaicg] Unknown message type: " + (type ?? "<none>"));
                    break;
            }
        }

        /// <summary>
        /// Applies a parsed LLM response on the Unity main thread.
        /// </summary>
        private void ApplyLlmResponse(LlmResponse response)
        {
            if (!string.IsNullOrEmpty(response.Text))
            {
                _chatUI.AddNpcMessage(GetCurrentCharacterName(), response.Text);
            }
            else if (response.Emotion != null || response.Pose != null || response.StatDeltas.Count > 0)
            {
                // LLM answered with tags only — show a placeholder so the turn is not invisible.
                _chatUI.AddSystemMessage("(персонаж молчит, но реагирует)");
                Logger.LogWarning("[kkaicg] LLM reply had no text, only commands. " +
                    "Consider telling the character in its ST card to always write speech text.");
            }

            if (response.Emotion != null) _npcActor.ApplyEmotion(response.Emotion);
            if (response.Pose != null) _npcActor.ApplyPose(response.Pose);
            foreach (var kv in response.StatDeltas)
                ApplyStatDelta(kv.Key, kv.Value);
        }

        private void ApplyEmotion(string emotion)
        {
            _npcActor.ApplyEmotion(emotion);
        }

        private void ApplyPose(string pose)
        {
            _npcActor.ApplyPose(pose);
        }

        private void ApplyStatDelta(string stat, int delta)
        {
            Logger.LogInfo("[kkaicg] Applying stat change: " + stat + " " + (delta >= 0 ? "+" : "") + delta);
            // TODO: modify NPC parameters (affection/trust/arousal/anger...).
        }

        #endregion

        private void OnDestroy()
        {
            _shutdown = true;
            CleanupSocket();
        }
    }

    /// <summary>
    /// Parsed "llm_response" protocol message (see PROTOCOL.md).
    /// </summary>
    public class LlmResponse
    {
        public string Text = "";
        public string Emotion;
        public string Pose;
        public readonly Dictionary<string, int> StatDeltas = new Dictionary<string, int>();

        public static LlmResponse FromJson(JsonData msg)
        {
            var r = new LlmResponse();

            if (msg.Keys.Contains("text") && msg["text"].IsString)
                r.Text = (string)msg["text"];

            if (msg.Keys.Contains("commands") && msg["commands"].IsObject)
            {
                var commands = msg["commands"];
                if (commands.Keys.Contains("emotion") && commands["emotion"].IsString)
                    r.Emotion = (string)commands["emotion"];
                if (commands.Keys.Contains("pose") && commands["pose"].IsString)
                    r.Pose = (string)commands["pose"];
            }

            if (msg.Keys.Contains("stats") && msg["stats"].IsObject)
            {
                foreach (string key in msg["stats"].Keys)
                {
                    var v = msg["stats"][key];
                    if (v.IsInt) r.StatDeltas[key] = (int)v;
                    else if (v.IsLong) r.StatDeltas[key] = (int)(long)v;
                    else if (v.IsDouble) r.StatDeltas[key] = (int)(double)v;
                }
            }

            return r;
        }

        public override string ToString()
        {
            var parts = new List<string>();
            if (Emotion != null) parts.Add("emotion=" + Emotion);
            if (Pose != null) parts.Add("pose=" + Pose);
            foreach (var kv in StatDeltas) parts.Add(kv.Key + (kv.Value >= 0 ? "+" : "") + kv.Value);
            var suffix = parts.Count > 0 ? " [" + string.Join(", ", parts.ToArray()) + "]" : "";
            return "\"" + Text + "\"" + suffix;
        }
    }
}
