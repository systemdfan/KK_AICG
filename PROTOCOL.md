# kkaicg WebSocket Protocol

Transport: WebSocket, `ws://127.0.0.1:8085`. The ST server plugin is the hub.
Two client roles: `game` (Koikatsu BepInEx plugin) and `ui` (SillyTavern browser
extension, bridges to the real LLM chat). All messages are JSON objects with a
mandatory `type` field (UTF-8 text frames). Plain-text `Hello` / `World` is kept
as a legacy handshake fallback.

Routing:
- `player_message` / `system_note`  : game -> hub -> ui
- `llm_raw` / `llm_error`           : ui -> hub (parse tags) -> game (`llm_response`)

## Any client → Hub

### hello
Sent right after connect. `client` defaults to `"game"`.
```json
{ "type": "hello", "client": "game", "version": "0.1.0" }
```

## Game → Hub

### player_message
Player typed a message in the in-game chat UI.
```json
{
  "type": "player_message",
  "text": "Привет, Мизуки!",
  "character": {
    "name": "Mizuki",
    "stCard": "Mizuki.yaml",
    "stats": {
      "affection": 12,
      "trust": 8,
      "arousal": 0,
      "anger": 0
    },
    "sexExperience": "virgin",
    "protection": null
  }
}
```

### system_note
Game event the LLM should know about (phone call, scene change, etc.).
```json
{ "type": "system_note", "text": "[System Note: Я прибежала на твой звонок.]" }
```

## UI → Hub

### llm_raw
Raw LLM reply text (tags not parsed yet — the hub parses them).
```json
{ "type": "llm_raw", "raw": "[emotion:giggle] Ой! [stat:affection+2]" }
```

### llm_error
Generation problem. `error`: `busy` | `no_character` | `generation_stopped` | any string.
```json
{ "type": "llm_error", "error": "no_character" }
```

## Hub → Game

### player_message
Player typed a message in the in-game chat UI.
```json
{
  "type": "player_message",
  "text": "Привет, Мизуки!",
  "character": {
    "name": "Mizuki",
    "stCard": "Mizuki.yaml",
    "stats": {
      "affection": 12,
      "trust": 8,
      "arousal": 0,
      "anger": 0
    },
    "sexExperience": "virgin"
  }
}
```

### system_note
Game event the LLM should know about (phone call, scene change, etc.).
```json
{ "type": "system_note", "text": "[System Note: Я прибежала на твой звонок.]" }
```

## Hub → Any client

### ack
Handshake response.
```json
{ "type": "ack" }
```

## Hub → Game

### llm_response
Parsed LLM reply. `commands` fields are optional; unknown ones are ignored by the game.
```json
{
  "type": "llm_response",
  "text": "Ой, ты меня смущаешь!",
  "commands": {
    "emotion": "giggle",
    "pose": "hands_behind_back"
  },
  "stats": {
    "affection": "+2"
  }
}
```

## LLM tag syntax (parsed by the ST extension)

Raw LLM output example:
```
[emotion:giggle|pose:hands_behind_back] Ой, ты меня смущаешь! [stat:affection+2]
```

- Leading `[key:value|key:value]` block(s) — commands (emotion, pose, anything else).
- `[stat:name+delta]` / `[stat:name-delta]` anywhere — stat changes (integers).
- Everything else — plain dialogue text, tags stripped.
- Multiple command blocks allowed; later duplicates override earlier ones.
- Malformed tags are left in text as-is (parser must be forgiving).
