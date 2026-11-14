const { WebSocketServer } = require('ws');
const { buildLlmResponseMessage } = require('./parser');

const PORT = 8085;

// Fallback while testing without an open SillyTavern tab: canned tagged replies.
const MOCK_WHEN_NO_UI = true;
const MOCK_REPLIES = [
    '[emotion:giggle|pose:hands_behind_back] Ой, ты меня смущаешь! [stat:affection+2]',
    '[emotion:happy|pose:idle] Привет! Рада тебя видеть. [stat:affection+1]',
    '[emotion:angry|pose:arms_crossed] Хмф! Не подходи ко мне! [stat:affection-1] [stat:anger+2]',
    '[emotion:shy] Э-э... н-не смотри так на меня... [stat:affection+1] [stat:arousal+1]',
];
let mockIndex = 0;

let wss = null;

/** @type {Set<import('ws')>} game plugin clients */
const gameClients = new Set();
/** @type {Set<import('ws')>} SillyTavern UI extension clients */
const uiClients = new Set();

function sendTo(clients, payload) {
    const raw = typeof payload === 'string' ? payload : JSON.stringify(payload);
    for (const client of clients) {
        if (client.readyState === client.OPEN) client.send(raw);
    }
}

const info = {
    id: 'kkaicg',
    name: 'KKAiCG - Koikatsu AI Chat Gateway',
    description: 'WebSocket bridge between SillyTavern and the Koikatsu kkaicg BepInEx plugin.',
};

function handleGameMessage(ws, msg) {
    switch (msg.type) {
        case 'player_message': {
            const charName = msg.character && msg.character.name ? msg.character.name : '???';
            console.log(`[kkaicg-st] Player -> ${charName}: ${msg.text}`);

            if (uiClients.size > 0) {
                sendTo(uiClients, msg);
            } else if (MOCK_WHEN_NO_UI) {
                const mockRaw = MOCK_REPLIES[mockIndex++ % MOCK_REPLIES.length];
                const response = buildLlmResponseMessage(mockRaw);
                console.log('[kkaicg-st] No UI client, mock reply:', JSON.stringify(response));
                sendTo(gameClients, response);
            } else {
                sendTo(gameClients, {
                    type: 'llm_response',
                    text: '[SillyTavern UI extension is not connected. Open a character chat in the browser.]',
                    commands: {},
                    stats: {},
                });
            }
            break;
        }

        case 'system_note':
            console.log(`[kkaicg-st] System note: ${msg.text}`);
            sendTo(uiClients, msg);
            break;

        default:
            console.warn(`[kkaicg-st] Unknown game message type: ${msg.type}`);
    }
}

function handleUiMessage(ws, msg) {
    switch (msg.type) {
        case 'llm_raw': {
            const response = buildLlmResponseMessage(msg.raw || '');
            console.log('[kkaicg-st] LLM reply parsed:', JSON.stringify(response));
            sendTo(gameClients, response);
            break;
        }

        case 'llm_error': {
            console.warn(`[kkaicg-st] UI error: ${msg.error}`);
            const texts = {
                busy: '[Персонаж занят, подождите окончания предыдущей генерации.]',
                no_character: '[Откройте чат с персонажем в SillyTavern.]',
                generation_stopped: '[Генерация была остановлена.]',
            };
            sendTo(gameClients, {
                type: 'llm_response',
                text: texts[msg.error] || `[ST error: ${msg.error}]`,
                commands: {},
                stats: {},
            });
            break;
        }

        default:
            console.warn(`[kkaicg-st] Unknown UI message type: ${msg.type}`);
    }
}

function handleConnection(ws) {
    let role = 'game'; // default for backward compatibility

    console.log('[kkaicg-st] Client connected.');

    ws.on('message', message => {
        const raw = message.toString();

        // Legacy plain-text handshake.
        if (raw === 'Hello') {
            ws.send('World');
            return;
        }

        let msg;
        try {
            msg = JSON.parse(raw);
        } catch (err) {
            console.warn('[kkaicg-st] Non-JSON message ignored:', raw.slice(0, 200));
            return;
        }

        if (msg.type === 'hello') {
            role = msg.client === 'ui' ? 'ui' : 'game';
            (role === 'ui' ? uiClients : gameClients).add(ws);
            console.log(`[kkaicg-st] "${role}" client registered (game: ${gameClients.size}, ui: ${uiClients.size}).`);
            ws.send(JSON.stringify({ type: 'ack' }));
            return;
        }

        if (role === 'ui') handleUiMessage(ws, msg);
        else handleGameMessage(ws, msg);
    });

    ws.on('close', () => {
        gameClients.delete(ws);
        uiClients.delete(ws);
        console.log(`[kkaicg-st] "${role}" client disconnected (game: ${gameClients.size}, ui: ${uiClients.size}).`);
    });

    ws.on('error', error => {
        console.error('[kkaicg-st] WebSocket error:', error);
    });
}

async function init(router) {
    wss = new WebSocketServer({ port: PORT });

    console.log(`[kkaicg-st] WebSocket server started on port ${PORT}`);

    wss.on('connection', handleConnection);
    wss.on('error', error => {
        console.error('[kkaicg-st] WebSocket server error:', error);
    });
}

async function exit() {
    if (wss) {
        console.log('[kkaicg-st] Shutting down WebSocket server.');
        wss.close();
        wss = null;
    }
    gameClients.clear();
    uiClients.clear();
}

module.exports = { info, init, exit };
