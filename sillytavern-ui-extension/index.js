/**
 * KKAiCG Bridge — SillyTavern UI extension.
 *
 * Connects to the kkaicg server plugin (ws://127.0.0.1:8085) as a "ui" client,
 * receives player messages from the Koikatsu game plugin, injects them into the
 * currently open character chat, triggers generation, and forwards the raw LLM
 * reply back. Tag parsing happens server-side (see parser.js).
 */
// NOTE: user extensions are served at /scripts/extensions/third-party/<name>/,
// so the app root (script.js) is FOUR levels up from this module.
import { Generate, sendMessageAsUser, eventSource, event_types, chat, this_chid, name2, setExtensionPrompt, extension_prompt_types, addOneMessage } from '../../../../script.js';

const WS_URL = 'ws://127.0.0.1:8085';
const RECONNECT_MS = 3000;

// Author Protection Mode: injected as an ST extension prompt (invisible in chat,
// included in every LLM request while active). Verified via the Prompt Itemization view.
const PROTECTION_KEY = 'kkaicg_protection';
const PROTECTION_DEPTH = 1; // messages from the end of the chat

// Live NPC state: injected as a system extension prompt before each generation,
// so the LLM always knows time, location, outfit, desires, stats and traits.
const CONTEXT_KEY = 'kkaicg_context';
const CONTEXT_DEPTH = 1;

// Koikatsu daily cycle (Cycle.Type) -> readable schedule time for the LLM.
const TIME_OF_DAY = {
    WakeUp: 'early morning (just woke up)',
    Morning: 'morning',
    GotoSchool: 'walking to school',
    HR1: 'first homeroom',
    Lesson1: 'first lesson',
    LunchTime: 'lunch break',
    Lesson2: 'second lesson',
    HR2: 'afternoon homeroom',
    StaffTime: 'after classes (staff time)',
    AfterSchool: 'after school / club time',
    GotoMyHouse: 'heading home',
    MyHouse: 'at home (evening)',
};

function describeTimeOfDay(t) {
    return (t && TIME_OF_DAY[t]) ? TIME_OF_DAY[t] : (t || 'unknown');
}

let ws = null;
let awaitingReply = false;
let busy = false;

/** Decode UTF-8-safe base64 (atob alone breaks non-ASCII). */
function decodeBase64Utf8(b64) {
    const bytes = Uint8Array.from(atob(b64), c => c.charCodeAt(0));
    return new TextDecoder().decode(bytes);
}

/**
 * Build the live game-state context string from the player_message character block.
 * The LLM uses it for roleplay grounding and OOC questions.
 */
function buildStateContext(character) {
    if (!character || !character.name) return '';

    const lines = [];
    const loc = character.location;
    if (loc) {
        let s = `Time of day: ${describeTimeOfDay(loc.timeOfDay)}. Location: ${loc.mapName ?? 'unknown'}.`;
        if (loc.action) s += ` Activity: ${loc.action}.`;
        lines.push(s);
    }
    if (character.clothes) lines.push(`Current outfit: ${character.clothes}.`);

    const stats = character.stats;
    if (stats) {
        lines.push(`Feelings: affection=${stats.favor ?? 0}, arousal/lewdness=${stats.lewdness ?? 0}, anger=${stats.anger ?? 0}, intimacy=${stats.intimacy ?? 0}, H count=${stats.hCount ?? 0}.`);
    }
    if (typeof character.relation === 'number') lines.push(`Relation level: ${character.relation}.`);
    if (character.sexExperience) lines.push(`Sexual experience: ${character.sexExperience}.`);
    if (character.isDate) lines.push('Currently on a date.');
    if (character.isLunch) lines.push('Had lunch together today.');
    if (character.isAnger) lines.push('Currently angry.');

    const desires = character.desires;
    if (desires && Object.keys(desires).length > 0) {
        const parts = Object.entries(desires).map(([k, v]) => `${k}=${v}`);
        lines.push(`Desires: ${parts.join(', ')}.`);
    }
    const traits = character.traits;
    if (Array.isArray(traits) && traits.length > 0) {
        lines.push(`Traits: ${traits.join(', ')}.`);
    }

    if (lines.length === 0) return '';
    return `[Live game state of ${character.name}. Use it for roleplay; when asked OOC questions (e.g. "where are we?"), answer from this data. Do not dump it unprompted.]
When you accept or propose a game event (date, lunch, club, intimacy, becoming a couple, breakup), you MUST append the matching [action:...] tag in the same reply, or the game will not register it.
` + lines.join('\n');
}

/** True while the protection extension prompt is set. */
let protectionActive = false;

function log(...args) {
    console.log('[kkaicg-ui]', ...args);
}

function send(payload) {
    if (ws && ws.readyState === WebSocket.OPEN) {
        ws.send(JSON.stringify(payload));
    }
}

/**
 * Inject a game system note into the chat: a small system message the LLM sees
 * in history and that renders compactly in the UI (extra.isSmallSys).
 */
async function injectSystemNote(text) {
    if (this_chid === undefined || this_chid === null) {
        log('No chat open, system note dropped:', text);
        return;
    }
    try {
        await addOneMessage({
            name: 'System',
            is_user: false,
            is_system: true,
            is_name: true,
            mes: text,
            send_date: Date.now(),
            extra: { isSmallSys: true, isKAICG: true },
        });
        log('System note injected into chat.');
    } catch (err) {
        console.error('[kkaicg-ui] System note injection failed:', err);
    }
}

function connect() {
    ws = new WebSocket(WS_URL);

    ws.addEventListener('open', () => {
        log('Connected to kkaicg server.');
        send({ type: 'hello', client: 'ui' });
    });

    ws.addEventListener('message', async (event) => {
        let msg;
        try {
            msg = JSON.parse(event.data);
        } catch {
            return;
        }
        if (msg.type === 'player_message') {
            await handlePlayerMessage(msg);
        } else if (msg.type === 'system_note') {
            log('System note:', msg.text);
            await injectSystemNote(String(msg.text ?? ''));
        }
    });

    ws.addEventListener('close', () => {
        log('Disconnected, reconnecting in', RECONNECT_MS / 1000, 's');
        awaitingReply = false;
        busy = false;
        setTimeout(connect, RECONNECT_MS);
    });

    ws.addEventListener('error', () => {
        ws.close();
    });
}

async function handlePlayerMessage(msg) {
    if (busy) {
        log('Busy, dropping player message.');
        send({ type: 'llm_error', error: 'busy' });
        return;
    }
    if (this_chid === undefined || this_chid === null) {
        log('No character chat open in SillyTavern.');
        send({ type: 'llm_error', error: 'no_character' });
        return;
    }

    busy = true;
    awaitingReply = true;
    try {
        // Author Protection Mode: keep a system-level extension prompt set for the
        // whole restricted session; clear it as soon as a non-protected message arrives.
        // Live game state -> extension prompt, refreshed before every generation.
        const context = buildStateContext(msg.character);
        if (context) {
            setExtensionPrompt(CONTEXT_KEY, context, extension_prompt_types.IN_CHAT, CONTEXT_DEPTH, false);
            log('Game state context updated:', msg.character.name);
        }

        const protection = msg.character && msg.character.protection;
        if (protection && protection.active && protection.promptB64) {
            const prompt = decodeBase64Utf8(protection.promptB64);
            setExtensionPrompt(PROTECTION_KEY, prompt, extension_prompt_types.IN_CHAT, PROTECTION_DEPTH, false);
            protectionActive = true;
            log('Author protection prompt INJECTED (system, depth', PROTECTION_DEPTH + ').');
        } else if (protectionActive) {
            setExtensionPrompt(PROTECTION_KEY, '', extension_prompt_types.IN_CHAT, PROTECTION_DEPTH, false);
            protectionActive = false;
            log('Author protection prompt cleared.');
        }

        log('Forwarding to chat as user:', msg.text);
        await sendMessageAsUser(String(msg.text ?? ''));
        await Generate('normal');
    } catch (err) {
        console.error('[kkaicg-ui] Generation failed:', err);
        awaitingReply = false;
        send({ type: 'llm_error', error: String(err) });
    } finally {
        busy = false;
    }
}

function onMessageReceived() {
    if (!awaitingReply) return;
    awaitingReply = false;

    const last = chat[chat.length - 1];
    if (!last || last.is_user) {
        send({ type: 'llm_error', error: 'empty_reply' });
        return;
    }

    log(`Reply from ${last.name || name2}:`, last.mes);
    send({ type: 'llm_raw', raw: last.mes });
}

function onGenerationStopped() {
    if (!awaitingReply) return;
    awaitingReply = false;
    send({ type: 'llm_error', error: 'generation_stopped' });
}

eventSource.on(event_types.MESSAGE_RECEIVED, onMessageReceived);
if (event_types.GENERATION_STOPPED) {
    eventSource.on(event_types.GENERATION_STOPPED, onGenerationStopped);
}

connect();
