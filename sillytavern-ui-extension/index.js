/**
 * KKAiCG Bridge — SillyTavern UI extension.
 *
 * Connects to the kkaicg server plugin (ws://127.0.0.1:8085) as a "ui" client,
 * receives player messages from the Koikatsu game plugin, injects them into the
 * currently open character chat, triggers generation, and forwards the raw LLM
 * reply back. Tag parsing happens server-side (see parser.js).
 */
import { Generate, sendMessageAsUser, eventSource, event_types, chat, this_chid, name2 } from '../../../../script.js';

const WS_URL = 'ws://127.0.0.1:8085';
const RECONNECT_MS = 3000;

let ws = null;
let awaitingReply = false;
let busy = false;

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
