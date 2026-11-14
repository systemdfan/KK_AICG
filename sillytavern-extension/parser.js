/**
 * Parser for LLM replies with kkaicg command tags.
 *
 * Syntax:
 *   [emotion:giggle|pose:hands_behind_back] Dialogue text [stat:affection+2]
 *
 * - [key:value|key:value] blocks -> commands
 * - [stat:name+N] / [stat:name-N]        -> stat deltas
 * - everything else                     -> dialogue text (tags stripped)
 *
 * The parser is forgiving: malformed tags stay in the text untouched.
 */

// Matches a complete [key:value|key:value ...] block.
// Keys/values: letters, digits, underscore, dash, dot. No brackets/spaces inside.
const COMMAND_BLOCK_RE = /\[((?:[A-Za-z0-9_.-]+:[A-Za-z0-9_.-]+)(?:\|[A-Za-z0-9_.-]+:[A-Za-z0-9_.-]+)*)\]/g;

// Matches [stat:name+2] / [stat:name-1]
const STAT_RE = /\[stat:([A-Za-z0-9_.-]+)([+-]\d+)\]/g;

// Matches [desire:name+20] / [desire:name-10] (NPC desire changes)
const DESIRE_RE = /\[desire:([A-Za-z0-9_.-]+)([+-]\d+)\]/g;

/**
 * Parse a raw LLM reply into structured data.
 * @param {string} raw Raw LLM output.
 * @returns {{ text: string, commands: Object<string,string>, stats: Object<string,number>, desires: Object<string,number> }}
 */
function parseLlmResponse(raw) {
    if (typeof raw !== 'string') {
        return { text: '', commands: {}, stats: {}, desires: {} };
    }

    const commands = {};
    const stats = {};
    const desires = {};
    let text = raw;

    // 1. Extract stat tags first (they may appear anywhere, including inside text).
    text = text.replace(STAT_RE, (match, name, delta) => {
        const value = parseInt(delta, 10);
        if (Number.isNaN(value)) return match; // shouldn't happen given the regex, but be safe
        stats[name] = (stats[name] || 0) + value;
        return ' ';
    });

    // 1b. Extract desire tags ([desire:horny+20]) the same way.
    text = text.replace(DESIRE_RE, (match, name, delta) => {
        const value = parseInt(delta, 10);
        if (Number.isNaN(value)) return match;
        desires[name] = (desires[name] || 0) + value;
        return ' ';
    });

    // 2. Extract command blocks. "stat:..." tags were already handled above,
    //    so COMMAND_BLOCK_RE won't see them (they contain '+'/'-' which the value pattern rejects).
    text = text.replace(COMMAND_BLOCK_RE, (match, body) => {
        for (const pair of body.split('|')) {
            const sep = pair.indexOf(':');
            const key = pair.slice(0, sep).trim();
            const value = pair.slice(sep + 1).trim();
            if (key && value) commands[key] = value;
        }
        return ' ';
    });

    // 3. Clean up whitespace left by stripped tags.
    text = text.replace(/\s{2,}/g, ' ').trim();

    return { text, commands, stats, desires };
}

/**
 * Build the JSON message sent to the game plugin.
 */
function buildLlmResponseMessage(raw) {
    const parsed = parseLlmResponse(raw);
    return {
        type: 'llm_response',
        text: parsed.text,
        commands: parsed.commands,
        stats: parsed.stats,
        desires: parsed.desires,
    };
}

module.exports = { parseLlmResponse, buildLlmResponseMessage };
