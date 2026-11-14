using System;
using System.Collections.Generic;
using UnityEngine;

namespace KKAiChatGateway
{
    /// <summary>
    /// IMGUI-based chat window. IMGUI is used deliberately: it does not touch the game's
    /// uGUI canvases, so it cannot break other plugins' UI. All calls happen on the main thread.
    /// </summary>
    public class ChatUI
    {
        private const int WindowId = 0x4B4B41; // "KKA"
        private const string InputControlName = "kkaicg_input";
        private const int MaxMessages = 200;

        private struct ChatMessage
        {
            public string Sender;
            public string Text;
            public bool IsPlayer;
        }

        private readonly List<ChatMessage> _messages = new List<ChatMessage>();
        private readonly Action<string> _onSend;

        private Rect _windowRect = new Rect(Screen.width / 2f - 260f, Screen.height / 2f - 200f, 520f, 400f);
        private Vector2 _scrollPos;
        private string _input = "";
        private bool _visible;
        private bool _focusInput;
        private bool _scrollToBottom;
        private string _titleSuffix = "";

        private bool _cursorWasLocked;
        private bool _cursorWasVisible;

        private GUIStyle _msgStyle;
        private GUIStyle _playerStyle;

        public bool Visible
        {
            get { return _visible; }
        }

        /// <summary>True while the chat input field has keyboard focus (game input should be ignored).</summary>
        public bool InputFocused { get; private set; }

        public ChatUI(Action<string> onSend)
        {
            _onSend = onSend;
        }

        public void SetConnectionStatus(string status)
        {
            _titleSuffix = status;
        }

        public void Toggle()
        {
            if (_visible) Hide(); else Show();
        }

        public void Show()
        {
            if (_visible) return;
            _visible = true;
            _cursorWasLocked = Cursor.lockState == CursorLockMode.Locked;
            _cursorWasVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _focusInput = true;
        }

        public void Hide()
        {
            if (!_visible) return;
            _visible = false;
            InputFocused = false;
            if (_cursorWasLocked) Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = _cursorWasVisible;
        }

        public void AddPlayerMessage(string text)
        {
            AddMessage("Вы", text, true);
        }

        public void AddNpcMessage(string npcName, string text)
        {
            AddMessage(npcName, text, false);
        }

        public void AddSystemMessage(string text)
        {
            AddMessage("*", text, false);
        }

        private void AddMessage(string sender, string text, bool isPlayer)
        {
            _messages.Add(new ChatMessage { Sender = sender, Text = text, IsPlayer = isPlayer });
            if (_messages.Count > MaxMessages)
                _messages.RemoveRange(0, _messages.Count - MaxMessages);
            _scrollToBottom = true;
        }

        public void OnGUI()
        {
            if (!_visible) return;

            EnsureStyles();

            _windowRect = GUILayout.Window(WindowId, _windowRect, DrawWindow, "kk/aicg/ " + _titleSuffix);
            InputFocused = GUI.GetNameOfFocusedControl() == InputControlName;
        }

        private void EnsureStyles()
        {
            if (_msgStyle != null) return;

            _msgStyle = new GUIStyle(GUI.skin.label)
            {
                wordWrap = true,
                richText = true,
                padding = new RectOffset(4, 4, 2, 2),
            };
            _playerStyle = new GUIStyle(_msgStyle);
        }

        private void DrawWindow(int id)
        {
            // Message history (scrollable).
            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.ExpandHeight(true));
            foreach (var msg in _messages)
            {
                var colorTag = msg.IsPlayer ? "#7ec8ff" : (msg.Sender == "*" ? "#aaaaaa" : "#ffd27e");
                GUILayout.Label("<color=" + colorTag + "><b>" + Escape(msg.Sender) + ":</b></color> " + Escape(msg.Text),
                    msg.IsPlayer ? _playerStyle : _msgStyle);
            }
            if (_scrollToBottom)
            {
                _scrollPos.y = float.MaxValue;
                _scrollToBottom = false;
            }
            GUILayout.EndScrollView();

            // Send on Enter while the input field is focused.
            if (Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                && GUI.GetNameOfFocusedControl() == InputControlName)
            {
                Submit();
                Event.current.Use();
            }

            // Close on Escape.
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                Hide();
                Event.current.Use();
            }

            // Input row.
            GUILayout.BeginHorizontal();
            GUI.SetNextControlName(InputControlName);
            _input = GUILayout.TextField(_input, 500, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Send", GUILayout.Width(60f)))
                Submit();
            GUILayout.EndHorizontal();

            if (_focusInput)
            {
                GUI.FocusControl(InputControlName);
                _focusInput = false;
            }

            GUI.DragWindow();
        }

        private void Submit()
        {
            var text = _input.Trim();
            if (text.Length == 0) return;

            _input = "";
            _focusInput = true;
            AddPlayerMessage(text);
            if (_onSend != null) _onSend(text);
        }

        private static string Escape(string s)
        {
            return s.Replace("<", "<\u200b").Replace(">", ">\u200b"); // cheap rich-text escaping
        }
    }
}
