const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const script = fs.readFileSync(path.join(__dirname, '../../src/Api/wwwroot/app.js'), 'utf8');

function createDashboard(fetch) {
  const elements = new Map();
  const document = {
    getElementById(id) {
      if (!elements.has(id)) {
        elements.set(id, {
          value: '', textContent: '', style: {},
          addEventListener() {}, replaceChildren() {}, append() {}
        });
      }
      return elements.get(id);
    },
    querySelectorAll() { return []; }
  };
  const context = vm.createContext({ document, fetch, clearInterval() {}, setInterval() {} });
  vm.runInContext(script, context);
  return context;
}

test('accepts an empty 202 response from a scenario command', async () => {
  const context = createDashboard(async () => ({
    ok: true, status: 202,
    headers: { get: () => null },
    text: async () => ''
  }));
  const result = await vm.runInContext('request("/api/scenarios/sensor-spoofing")', context);
  assert.equal(result, null);
});

test('parses successful JSON responses', async () => {
  const context = createDashboard(async () => ({
    ok: true, status: 200,
    headers: { get: () => '17' },
    text: async () => '{"tankLevel":42}'
  }));
  const result = await vm.runInContext('request("/api/plant")', context);
  assert.equal(result.tankLevel, 42);
});

test('rejects unauthorized responses', async () => {
  const context = createDashboard(async () => ({ ok: false, status: 401 }));
  await assert.rejects(
    vm.runInContext('request("/api/plant")', context),
    /HTTP 401/
  );
});
