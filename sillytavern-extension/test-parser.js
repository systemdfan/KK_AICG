const assert = require('assert');
const { parseLlmResponse, buildLlmResponseMessage } = require('./parser');

let passed = 0;
function test(name, fn) {
    try {
        fn();
        passed++;
        console.log(`ok - ${name}`);
    } catch (err) {
        console.error(`FAIL - ${name}`);
        console.error(err.message);
        process.exitCode = 1;
    }
}

test('full example from spec', () => {
    const r = parseLlmResponse('[emotion:giggle|pose:hands_behind_back] Ой, ты меня смущаешь! [stat:affection+2]');
    assert.strictEqual(r.text, 'Ой, ты меня смущаешь!');
    assert.deepStrictEqual(r.commands, { emotion: 'giggle', pose: 'hands_behind_back' });
    assert.deepStrictEqual(r.stats, { affection: 2 });
});

test('plain text without tags', () => {
    const r = parseLlmResponse('Просто ответ без тегов.');
    assert.strictEqual(r.text, 'Просто ответ без тегов.');
    assert.deepStrictEqual(r.commands, {});
    assert.deepStrictEqual(r.stats, {});
});

test('negative and multiple stats, duplicates accumulate', () => {
    const r = parseLlmResponse('[stat:affection-3] Уходи! [stat:anger+2] [stat:anger+1]');
    assert.strictEqual(r.text, 'Уходи!');
    assert.deepStrictEqual(r.stats, { affection: -3, anger: 3 });
});

test('emotion only', () => {
    const r = parseLlmResponse('[emotion:happy] Привет!');
    assert.deepStrictEqual(r.commands, { emotion: 'happy' });
    assert.strictEqual(r.text, 'Привет!');
});

test('malformed tags stay in text', () => {
    const r = parseLlmResponse('[emotion] текст [foo bar] и [emotion:] конец');
    assert.strictEqual(r.text, '[emotion] текст [foo bar] и [emotion:] конец');
    assert.deepStrictEqual(r.commands, {});
});

test('later duplicate command overrides earlier', () => {
    const r = parseLlmResponse('[emotion:happy] а [emotion:sad] б');
    assert.strictEqual(r.commands.emotion, 'sad');
    assert.strictEqual(r.text, 'а б');
});

test('multiline text survives', () => {
    const r = parseLlmResponse('[emotion:shy] Первая строка.\nВторая строка.');
    assert.strictEqual(r.text, 'Первая строка.\nВторая строка.');
});

test('buildLlmResponseMessage wraps into protocol envelope', () => {
    const m = buildLlmResponseMessage('[emotion:happy] Привет! [stat:affection+1]');
    assert.strictEqual(m.type, 'llm_response');
    assert.strictEqual(m.text, 'Привет!');
    assert.deepStrictEqual(m.commands, { emotion: 'happy' });
    assert.deepStrictEqual(m.stats, { affection: 1 });
});

test('desire tags parsed separately from stats', () => {
    const r = parseLlmResponse('[emotion:aroused] Я так хочу тебя... [desire:horny+20] [desire:lesbian+10]');
    assert.deepStrictEqual(r.desires, { horny: 20, lesbian: 10 });
    assert.deepStrictEqual(r.stats, {});
    assert.strictEqual(r.text, 'Я так хочу тебя...');
});

test('desire deltas accumulate', () => {
    const r = parseLlmResponse('а [desire:horny+10] б [desire:horny+15] [desire:horny-5]');
    assert.strictEqual(r.desires.horny, 20);
});

test('action tags collected', () => {
    const r = parseLlmResponse('[action:lunch_invite] Пошли поедим! [stat:affection+1]');
    assert.deepStrictEqual(r.actions, ['lunch_invite']);
    assert.strictEqual(r.text, 'Пошли поедим!');
});

test('multiple actions and desire+action mix', () => {
    const r = parseLlmResponse('[action:date_invite] [desire:horny+10] Свидание! [action:h_invite]');
    assert.deepStrictEqual(r.actions, ['date_invite', 'h_invite']);
    assert.deepStrictEqual(r.desires, { horny: 10 });
});

test('non-string input is safe', () => {
    const r = parseLlmResponse(null);
    assert.strictEqual(r.text, '');
    assert.deepStrictEqual(r.commands, {});
    assert.deepStrictEqual(r.stats, {});
    assert.deepStrictEqual(r.desires, {});
    assert.deepStrictEqual(r.actions, []);
});

console.log(`\n${passed} tests passed.`);
