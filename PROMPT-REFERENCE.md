# kkaicg: команды для LLM (справочник для промпта карточки SillyTavern)

Вставьте этот блок в описание персонажа (или в Post-History Instructions):

```
Чтобы управлять своими эмоциями и позами, ты можешь в начале ответа поставить тег:
[emotion:АЛИАС|pose:АЛИАС]
Разрешённые алиасы emotion:
happy, smile, joy, giggle, angry, furious, sad, cry, shy, blush, surprise,
slight_surprise, wink, thinking, worried, troubled, annoyed, displeased, disgust,
painful, wry_smile, excited, aroused, doki, flirty_smile, smug, kiss, serious,
question, neutral, down, lonely_smile, bittersweet, embarrassed_smile, pout,
bored, gaze_away, gaze_at, closed_eyes
Разрешённые алиасы pose:
idle, angry_stand, embarrassed_1, embarrassed_2, embarrassed_3, hide_face,
chest_shy, chest_hide_1, chest_hide_2, chest_hide_3, hand_shy, hand_refuse,
cheek_refuse, joy_0..joy_9, sorrow_0..sorrow_9, anger_0..anger_9, shame_0..shame_4,
surprise_0, surprise_1, base_0..base_29, other_0, other_1, other_2
Статы: [stat:affection+N] / [stat:anger-N] и т.п. (affection, trust, arousal, anger).
Всегда отвечай текстом речи, теги — дополнение, а не замена текста.
```

## Полный маппинг алиасов (BepInEx/config/kkaicg-mapping.json)

| Алиас | Игровое имя (эмоция) |
|---|---|
| happy | 笑顔 |
| happy_closed | 笑顔（目閉じ） |
| smile | 微笑 |
| smile_closed | 微笑み（目閉じ） |
| joy | 嬉しい |
| giggle | ちょっと照れた笑顔 |
| flirty_smile | デレ顔 |
| excited | 興奮気味の笑顔 |
| slight_smile | 少し元気に微笑み |
| lonely_smile | 寂しい笑み |
| bittersweet | 切ない |
| bittersweet_smile | 切ない笑み |
| doki | ドキドキ |
| doki_wet | ドキドキ（潤んだ目） |
| doki_smile | ドキドキ笑顔 |
| stare_doki | 見つめながらドキドキ |
| stare_tears | 見つめ合い（ちょっと涙目） |
| tearful_smile | 涙目笑顔 |
| angry | 怒り１ |
| furious | 怒り2 |
| pout | ちょっと膨れる感じ |
| annoyed | 呆れ |
| displeased | 不満 |
| disgust | 嫌悪 |
| boring | つまらない |
| sad | 悲しい |
| cry | 泣き顔 |
| down | 落胆 |
| anxious | 不安 |
| troubled | 困り顔 |
| impatient | 焦り |
| shy | 照れ顔 |
| blush | 恥ずかしくて真っ赤 |
| embarrassed_smile | 恥ずかしくて… (см. mapping) |
| wink | ウィンク(左閉じ) |
| surprise | 驚き |
| slight_surprise | 少し驚き |
| serious | 真剣 |
| serious_blush | 真剣な顔（照れ） |
| question | 疑問 |
| thinking | 思案 |
| gaze_away | 思わず目を逸らす |
| gaze_at | 視線(こっち) |
| gaze_front | 視線(正面) |
| gaze_away2 | 視線(そらす) |
| smug | ドヤ顔 |
| wry_smile | 苦笑 |
| painful | 苦しげな表情 |
| aroused | 感じてる |
| kiss | キス（目閉じ） |
| neutral | 通常 |
| neutral_closed | 通常(目閉じ) |
| standard | 標準 |
| puffed | 膨らみつつ上目遣い |
| nervous | 少し緊張 |
| look_up | 上目遣い系 — 膨らみつつ上目遣い |
| bored | つまらない (дубль boring) |

Позы: `idle` (通常立ち), `angry_stand` (怒り立ち), `embarrassed_1..3` (恥ずかしがる1..3),
`hide_face` (顔を見られ隠す), `chest_shy` (胸を見られ恥ずかしがる), `chest_hide_1..3` (胸を触られ隠す1..3),
`hand_shy` (手を触れられ恥ずかしがる), `hand_refuse` (手を触れられ嫌がられる), `cheek_refuse` (頬を触られ嫌がられる),
`joy_0..9` (喜00..09), `sorrow_0..9` (哀00..09), `anger_0..9` (怒00..09), `shame_0..4` (恥00..04),
`surprise_0, surprise_1` (驚00, 驚01), `base_0..29` (ベース00..29), `other_0..2` (他00..02).

## Примечания
- В тегах LLM можно использовать только ASCII-алиасы (кириллица/японский в тегах парсер не примет —
  это защита от случайного распарсивания обычного текста). Маппинг переводит алиас в японское игровое имя.
- Алиасы можно менять/дополнять в `BepInEx/config/kkaicg-mapping.json` без пересборки — файл читается при старте игры.
- `emotion`/`pose` — зарезервированные ключи команд; расширение может слать и другие (плагин пока их игнорирует).
