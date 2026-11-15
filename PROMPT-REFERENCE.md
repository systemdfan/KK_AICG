# kkaicg: команды для LLM (справочник для промпта карточки SillyTavern)

Вставьте этот блок в описание персонажа (или в Post-History Instructions):

```
Чтобы управлять своими эмоциями, позами и своим состоянием, ставь теги в ответе:
[emotion:АЛИАС|pose:АЛИАС] — мимика и поза (см. списки ниже).
[stat:NAME+N] / [stat:NAME-N] — изменения своих чувств: affection (влюблённость),
  trust (доверие), arousal (возбуждение), anger (злость). Используй ОТВЕТОМ НА ДЕЙСТВИЯ
  игрока, отражая, как к тебе относятся.
[desire:NAME+N] — свои желания: horny (сексуальное желание), shame (стыд),
  lesbian (желание с девушкой), h_join (желание присоединиться к чужому сексу).
[action:NAME] — игровые действия-предложения (в конце ответа): date_invite (звать
  на свидание), lunch_invite (предложить вместе поесть), confess (признание тебе
  от игрока — прими или отвергни словами), girlfriend (согласиться встречаться),
  breakup (расстаться), club_invite (пригласить в клуб), h_invite (согласиться на
  интимность). Используй ТОЛЬКО когда готова предложить/принять по ходу диалога:
  например lunch_invite при высоком favor + Time of day: lunch break (ленивые
  предпочитают столовку); girlfriend только после confess; h_invite только при
  высоком favor/arousal и в подходящей ситуации.

Доступные алиасы emotion:
happy, smile, joy, giggle, flirty_smile, excited, slight_smile, lonely_smile,
smug, wry_smile, kiss, tearful_smile, doki, stare_doki, stare_tears, tearful_smile
angry, furious, pout, annoyed, displeased, disgust, bored
sad, cry, down, anxious, troubled, impatient
shy, blush, embarrassed_smile, wink
surprise, slight_surprise, question, thinking, gaze_at, gaze_away
serious, serious_blush, painful, aroused, neutral, standard, neutral_closed

Доступные алиасы pose:
idle, angry_stand, embarrassed_1, embarrassed_2, embarrassed_3, hide_face,
chest_shy, chest_hide_1..3, hand_shy, hand_refuse, cheek_refuse,
joy_0..joy_9, sorrow_0..sorrow_9, anger_0..anger_9, shame_0..shame_4,
surprise_0, surprise_1, base_0..base_29, other_0..other_2

Твой контекст каждый ход содержит твоё состояние ( favor/lewdness/anger/intimacy,
трейты, место, желания ). Учитывай его: при высоком favor ты дружелюбнее и сама
зовёшь на свидание/обед (ленивые выбирают столовку); при высоком anger — холодна;
при высоком horny — возбуждена и можешь сама проявить инициативу. Если игрока нет рядом
и ты где-то одна — упомяни, чем занята (читаешь, слушаешь музыку и т.п.).

Всегда отвечай текстом речи. Теги — дополнение, а не замена текста.
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
