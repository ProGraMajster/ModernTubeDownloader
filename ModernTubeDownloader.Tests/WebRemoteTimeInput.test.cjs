const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assetDirectory = path.join(__dirname, '../ModernTubeDownloader/WebRemote/wwwroot');
const time = require(path.join(assetDirectory, 'time-input.js'));
const parts = (hours = '', minutes = '', seconds = '') => ({ hours, minutes, seconds });

for (const [name, input, expected] of [
  ['empty bound stays optional', parts(), ''],
  ['seconds without hours or minutes', parts('', '', '2'), '00:00:02'],
  ['minutes without hours or seconds', parts('', '5'), '00:05:00'],
  ['complete bound and leading zeros', parts('01', '02', '03'), '01:02:03'],
  ['24 hours uses the existing day notation', parts('24'), '1.00:00:00'],
  ['multi-day recording', parts('49', '59', '59'), '2.01:59:59'],
  ['maximum 30 days', parts('720'), '30.00:00:00']
]) test(name, () => assert.equal(time.toProtocolTime(input), expected));

for (const [name, input] of [
  ['minutes above 59', parts('', '60')],
  ['seconds above 59', parts('', '', '60')],
  ['negative component', parts('-1')],
  ['decimal component', parts('', '1.5')],
  ['colon is not a numeric component', parts('00:05:00')],
  ['non-numeric text', parts('abc')],
  ['more than 30 days', parts('720', '', '1')],
  ['unsafe number', parts('9007199254740992')]
]) test(`reject ${name}`, () => assert.throws(() => time.toProtocolTime(input), /rangeInvalid/));

test('custom range retains the existing wire format', () => {
  assert.deepEqual(time.range('custom', parts('', '', '2'), parts('', '', '10')), { from: '00:00:02', to: '00:00:10' });
});
test('either range bound can be omitted', () => {
  assert.deepEqual(time.range('custom', parts(), parts('', '', '10')), { from: '', to: '00:00:10' });
  assert.deepEqual(time.range('custom', parts('', '5'), parts()), { from: '00:05:00', to: '' });
});
test('custom range requires at least one bound', () => {
  assert.throws(() => time.range('custom', parts(), parts()), /rangeRequired/);
});
test('range end must be later, not equal or zero', () => {
  for (const end of ['0', '1', '2'])
    assert.throws(() => time.range('custom', parts('', '', '2'), parts('', '', end)), /rangeOrder/);
  assert.throws(() => time.range('custom', parts(), parts('', '', '0')), /rangeOrder/);
});
test('full media ignores stale or invalid hidden bounds', () => {
  assert.deepEqual(time.range('full', parts('invalid'), parts('', '99')), { from: '', to: '' });
});

function createPage() {
  const elements = new Map();
  const element = id => {
    if (!elements.has(id)) elements.set(id, {
      value: '', textContent: '', disabled: false, hidden: false, checked: false,
      options: Array.from({ length: 8 }, () => ({ textContent: '' })), listeners: {},
      addEventListener(name, handler) { this.listeners[name] = handler; }
    });
    return elements.get(id);
  };
  element('rangeMode').value = 'full';
  element('quality').value = '360';
  element('container').value = 'Auto';
  const requests = [];
  const context = vm.createContext({
    MTDTimeInput: time, document: { hidden: true, getElementById: element },
    sessionStorage: { getItem() { return null; }, removeItem() {} },
    setInterval() { return 1; }, clearInterval() {},
    fetch: async (url, options) => {
      if (url === '/api/auth-mode') return new Promise(() => {});
      requests.push({ url, body: JSON.parse(options.body) });
      return { ok: true, status: 200, headers: { get() { return 'application/json'; } }, json: async () => ({}) };
    }
  });
  vm.runInContext(fs.readFileSync(path.join(assetDirectory, 'app.js'), 'utf8'), context);
  const changeMode = mode => { element('rangeMode').value = mode; element('rangeMode').listeners.change(); };
  const submit = () => element('addForm').listeners.submit({ preventDefault() {} });
  return { element, requests, context, changeMode, submit };
}

test('page enables only visible time inputs and retains edits across mode changes', () => {
  const page = createPage();
  assert.equal(page.element('fromHours').disabled, true);
  page.changeMode('custom');
  assert.equal(page.element('rangeFields').hidden, false);
  page.element('fromSeconds').value = '2';
  page.element('toSeconds').value = '10';
  page.element('toSeconds').listeners.input();
  assert.equal(page.element('rangePreview').textContent, '00:00:02 → 00:00:10');
  page.changeMode('full');
  assert.equal(page.element('toSeconds').disabled, true);
  page.changeMode('custom');
  assert.equal(page.element('toSeconds').disabled, false);
  assert.equal(page.element('toSeconds').value, '10');
});
test('page serializes numeric components only on confirmation', async () => {
  const page = createPage();
  page.changeMode('custom');
  page.element('url').value = 'https://www.youtube.com/watch?v=test';
  page.element('fromSeconds').value = '2';
  page.element('toSeconds').value = '10';
  assert.equal(page.requests.length, 0);
  await page.submit();
  assert.equal(page.requests[0].body.from, '00:00:02');
  assert.equal(page.requests[0].body.to, '00:00:10');
  assert.equal(page.element('addButton').disabled, false);
});
test('page submits full media without stale hidden values', async () => {
  const page = createPage();
  page.changeMode('custom');
  page.element('fromHours').value = 'invalid';
  page.changeMode('full');
  await page.submit();
  assert.equal(page.requests[0].body.from, '');
  assert.equal(page.requests[0].body.to, '');
});
test('invalid range never sends an add request and leaves the form editable', async () => {
  const page = createPage();
  page.changeMode('custom');
  page.element('fromSeconds').value = '10';
  page.element('toSeconds').value = '2';
  await page.submit();
  assert.equal(page.requests.length, 0);
  assert.match(page.element('message').textContent, /Koniec musi/);
  assert.equal(page.element('addButton').disabled, false);
});
test('language refresh localizes time labels without resetting edits', () => {
  const page = createPage();
  page.changeMode('custom');
  page.element('fromSeconds').value = '2';
  vm.runInContext('language="en";applyLanguage()', page.context);
  assert.equal(page.element('fromLabel').textContent, 'From');
  assert.equal(page.element('fromHoursLabel').textContent, 'Hours');
  assert.equal(page.element('fromSeconds').value, '2');
  assert.equal(page.element('rangePreview').textContent, '00:00:02 → End of media');
});
