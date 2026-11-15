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
The `character` block carries the full live NPC state (see NpcState.cs):
```json
{
  "type": "player_message",
  "text": "Привет, Мизуки!",
  "character": {
    "name": "Mizuki",
    "personality": 25,
    "stCard": "Mizuki.yaml",
    "stats": { "favor": 12, "lewdness": 0, "anger": 0, "intimacy": 8, "hCount": 0 },
    "relation": 0,
    "isAnger": false,
    "isDate": false,
    "isLunch": false,
    "isFirstGirlfriend": false,
    "sexExperience": "virgin|inexperienced|experienced|lewd",
    "isVirgin": true,
    "isAnalVirgin": true,
    "traits": ["lazy", "likes_girls"],
    "location": {
      "timeOfDay": "LunchTime",
      "mapNo": 45,
      "mapName": "屋上",
      "playerMapNo": 10,
      "actionNo": 7,
      "action": "talk"
    },
    "desires": { "horny": 20, "lesbian": 10, "raw_6": 0 }
  }
}
```
Trait aliases: frequent_pee (頻尿), gluttonous, dense, easy, bitchy, sulky, bookworm,
music_lover, frank, submissive, friendly, clean_freak, lazy (怠惰), sinsyutu, loner,
sporty, serious, likes_girls.
Desire aliases: horny (game desire no 5), shame (4), lesbian (26), h_join (30);
unknown numbers appear as `raw_<no>`.
Stat names for [stat:...] tags: affection|favor, trust|intimacy, arousal|lewdness, anger.

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
  },
  "desires": {
    "horny": "+10"
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
- `[desire:name+delta]` / `[desire:name-delta]` anywhere — NPC desire changes
  (aliases: horny, shame, lesbian, h_join; raw game numbers like `[desire:5+20]` also work).
- `[action:name]` anywhere — in-game character actions: date_invite, lunch_invite,
  confess, girlfriend, breakup, club_invite, h_invite. Applied via vanilla-equivalent
  effects (isDate, dateHeroine, isLunch, isGirlfriend, talkEvent...); each application
  emits a `system_note` back so the LLM sees the real state change.
- Everything else — plain dialogue text, tags stripped.
- Multiple command blocks allowed; later duplicates override earlier ones.
- Malformed tags are left in text as-is (parser must be forgiving).
