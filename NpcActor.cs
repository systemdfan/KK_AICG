using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using System.Linq;
using System.Reflection;
using LitJson;
using Manager;
using UnityEngine;

namespace KKAiChatGateway
{
    /// <summary>
    /// Applies LLM emotion/pose commands to the NPC the player is currently talking to.
    /// Works on top of the vanilla TalkScene API (Communication.ChangeExpression /
    /// TalkScene.AnimePlay via reflection) instead of Harmony-patching, so it stays
    /// compatible with other plugins.
    /// </summary>
    public class NpcActor
    {
        private readonly ManualLogSource _log;
        private readonly string _mappingPath;
        private readonly string _dumpPath;

        /// <summary>alias -> in-game expression/pose name</summary>
        private readonly Dictionary<string, string> _emotionMap = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _poseMap = new Dictionary<string, string>();

        public NpcActor(ManualLogSource log, string configDirectory)
        {
            _log = log;
            _mappingPath = Path.Combine(configDirectory, "kkaicg-mapping.json");
            _dumpPath = Path.Combine(configDirectory, "kkaicg-lists.txt");
        }

        #region Mapping

        /// <summary>
        /// Loads (or creates) the user-editable alias mapping file.
        /// Unmapped names are passed through to the game API as-is, so raw
        /// in-game names (e.g. "笑顔", "通常立ち") can be used directly in prompts.
        /// </summary>
        public void LoadMapping()
        {
            try
            {
                if (!File.Exists(_mappingPath))
                {
                    var defaults = new
                    {
                        _comment = "kkaicg alias mapping: LLM tag value -> in-game name. " +
                                   "Unmapped values pass through as-is. Press the DumpListsKey in-game " +
                                   "and see kkaicg-lists.txt for all valid in-game names.",
                        emotion = new Dictionary<string, string>
                        {
                            { "happy", "笑顔" },
                            { "angry", "怒り１" },
                        },
                        pose = new Dictionary<string, string>
                        {
                            { "idle", "通常立ち" },
                            { "angry_stand", "怒り立ち" },
                        },
                    };
                    File.WriteAllText(_mappingPath, JsonMapper.ToJson(defaults));
                    _log.LogInfo("[kkaicg] Created default mapping file: " + _mappingPath);
                }

                var data = JsonMapper.ToObject(File.ReadAllText(_mappingPath));
                _emotionMap.Clear();
                _poseMap.Clear();
                FillMap(data, "emotion", _emotionMap);
                FillMap(data, "pose", _poseMap);
                _log.LogInfo($"[kkaicg] Mapping loaded: {_emotionMap.Count} emotions, {_poseMap.Count} poses.");
            }
            catch (Exception ex)
            {
                _log.LogWarning("[kkaicg] Failed to load mapping, using pass-through only: " + ex.Message);
            }
        }

        private static void FillMap(JsonData data, string key, Dictionary<string, string> map)
        {
            if (data == null || !data.Keys.Contains(key) || !data[key].IsObject) return;
            foreach (string k in data[key].Keys)
            {
                var v = data[key][k];
                if (v.IsString) map[k] = (string)v;
            }
        }

        private string Resolve(string value, Dictionary<string, string> map)
        {
            return map.TryGetValue(value, out var mapped) ? mapped : value;
        }

        #endregion

        #region Current NPC

        /// <summary>The heroine of the active talk scene, or null.</summary>
        public SaveData.Heroine GetCurrentHeroine()
        {
            try
            {
                var talkScene = UnityEngine.Object.FindObjectOfType<TalkScene>();
                if (talkScene == null) return null;
                var heroine = talkScene.targetHeroine;
                if (heroine == null || heroine.chaCtrl == null) return null;
                return heroine;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The active TalkScene, or null.</summary>
        public TalkScene GetCurrentTalkScene()
        {
            try
            {
                return UnityEngine.Object.FindObjectOfType<TalkScene>();
            }
            catch (Exception)
            {
                return null;
            }
        }

        #endregion

        #region Applying

        /// <summary>
        /// Apply a facial expression to the talk-scene NPC.
        /// No-op when not in a talk scene; names are validated against the personality's expression list.
        /// </summary>
        public void ApplyEmotion(string emotion)
        {
            var heroine = GetCurrentHeroine();
            if (heroine == null)
            {
                _log.LogWarning("[kkaicg] ApplyEmotion skipped: no talk scene.");
                return;
            }

            var name = Resolve(emotion, _emotionMap);
            var personality = heroine.FixCharaIDOrPersonality;

            if (Singleton<Game>.Instance.GetExpression(personality, name) == null)
            {
                _log.LogWarning($"[kkaicg] Expression \"{name}\" (from \"{emotion}\") not found for personality {personality}. " +
                                "Use the DumpListsKey to see valid names.");
                return;
            }

            Singleton<Communication>.Instance.ChangeExpression(personality, name, heroine.chaCtrl);
            _log.LogInfo($"[kkaicg] Expression applied: {emotion} -> {name} ({heroine.Name})");
        }

        /// <summary>
        /// Play a pose animation on the talk-scene NPC via TalkScene.AnimePlay (private, reflection).
        /// </summary>
        public void ApplyPose(string pose)
        {
            var talkScene = GetCurrentTalkScene();
            var heroine = GetCurrentHeroine();
            if (talkScene == null || heroine == null)
            {
                _log.LogWarning("[kkaicg] ApplyPose skipped: no talk scene.");
                return;
            }

            var name = Resolve(pose, _poseMap);

            // Some pose names in the game data have trailing (full-width) spaces,
            // inconsistently across personalities (e.g. 手を触れられ恥ずかしがる　).
            // Try exact, trimmed and padded variants before giving up.
            var personality = heroine.FixCharaIDOrPersonality;
            var comm = Singleton<Communication>.Instance;
            var trimmed = name.TrimEnd(' ', '\u3000');
            string found = null;
            if (comm.GetPoseInfo(personality, name) != null) found = name;
            else if (comm.GetPoseInfo(personality, trimmed) != null) found = trimmed;
            else if (comm.GetPoseInfo(personality, trimmed + "\u3000") != null) found = trimmed + "\u3000";

            if (found == null)
            {
                _log.LogWarning($"[kkaicg] Pose \"{name}\" (from \"{pose}\") not found for personality {personality}. " +
                                "Use the DumpListsKey to see valid names.");
                return;
            }
            name = found;

            try
            {
                var method = talkScene.GetType().GetMethod("AnimePlay",
                    BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new[] { typeof(string), typeof(bool) }, null);
                if (method == null)
                {
                    _log.LogError("[kkaicg] TalkScene.AnimePlay not found via reflection.");
                    return;
                }
                method.Invoke(talkScene, new object[] { name, false });
                _log.LogInfo($"[kkaicg] Pose applied: {pose} -> {name} ({heroine.Name})");
            }
            catch (Exception ex)
            {
                _log.LogError("[kkaicg] ApplyPose failed: " + ex);
            }
        }

        #endregion

        #region Debug dump

        /// <summary>
        /// Dumps all valid expression and pose names for every personality
        /// to BepInEx/config/kkaicg-lists.txt and to the log.
        /// </summary>
        public void DumpLists()
        {
            _log.LogInfo("[kkaicg] DumpLists triggered.");
            try
            {
                var heroine = GetCurrentHeroine();
                int currentPersonality = heroine != null ? heroine.FixCharaIDOrPersonality : -1;

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("kkaicg valid emotion/pose names per personality.");
                if (currentPersonality >= 0)
                    sb.AppendLine($"Current talk-scene personality: {currentPersonality}");
                sb.AppendLine();

                // Expressions (public Game.expCharaDic).
                var expDic = Singleton<Game>.Instance.expCharaDic;
                sb.AppendLine("=== Expressions per personality ===");
                foreach (var personality in expDic.Keys.OrderBy(p => p))
                {
                    sb.AppendLine($"-- personality {personality} " + (personality == currentPersonality ? "(current)" : "") + " --");
                    foreach (var key in expDic[personality].Keys.OrderBy(k => k))
                        sb.AppendLine("  [emotion:" + key + "]");
                }

                // Poses (private Communication.dicPoseChara via reflection).
                var comm = Singleton<Communication>.Instance;
                var poseField = comm.GetType().GetField("dicPoseChara", BindingFlags.NonPublic | BindingFlags.Instance);
                var poseDic = poseField != null ? poseField.GetValue(comm) as Dictionary<int, Dictionary<string, string[]>> : null;
                sb.AppendLine();
                sb.AppendLine("=== Poses per personality ===");
                if (poseDic != null)
                {
                    foreach (var personality in poseDic.Keys.OrderBy(p => p))
                    {
                        sb.AppendLine($"-- personality {personality} " + (personality == currentPersonality ? "(current)" : "") + " --");
                        foreach (var key in poseDic[personality].Keys.OrderBy(k => k))
                            sb.AppendLine("  [pose:" + key + "]");
                    }
                }
                else
                {
                    sb.AppendLine("  (dicPoseChara not found via reflection)");
                }

                File.WriteAllText(_dumpPath, sb.ToString());
                _log.LogInfo("[kkaicg] Lists written to " + _dumpPath);
            }
            catch (Exception ex)
            {
                _log.LogError("[kkaicg] DumpLists failed: " + ex);
            }
        }

        #endregion
    }
}
