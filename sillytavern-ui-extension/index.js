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
import { Generate, sendMessageAsUser, eventSource, event_types, chat, this_chid, name2, setExtensionPrompt, extension_prompt_types } from '../../../../script.js';

const WS_URL = 'ws://127.0.0.1:8085';
const RECONNECT_MS = 3000;

// Author Protection Mode: injected as an ST extension prompt (invisible in chat,
// included in every LLM request while active). Verified via the Prompt Itemization view.
const PROTECTION_KEY = 'kkaicg_protection';
const PROTECTION_DEPTH = 1; // messages from the end of the chat

let ws = null;
let awaitingReply = false;
let busy = false;

/** Decode UTF-8-safe base64 (atob alone breaks non-ASCII). */
function decodeBase64Utf8(b64) {
    const bytes = Uint8Array.from(atob(b64), c => c.charCodeAt(0));
    return new TextDecoder().decode(bytes);
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
            log('System note (not injected yet):', msg.text);
            // TODO: inject into chat context (e.g. as a hidden system/Narrator message).
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
