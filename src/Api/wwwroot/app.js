const byId = id => document.getElementById(id);
let key = '';
let refreshTimer;
const headers = () => ({ 'X-Api-Key': key });
async function request(path, options = {}) {
  const response = await fetch(path, { ...options, headers: { ...headers(), ...(options.headers || {}) } });
  if (!response.ok) throw new Error('HTTP ' + response.status);
  if (response.status === 204 || response.headers.get('content-length') === '0') return null;
  const body = await response.text();
  return body ? JSON.parse(body) : null;
}
function itemList(id, items, render) {
  const list = byId(id);
  list.replaceChildren();
  for (const item of items) {
    const li = document.createElement('li');
    const title = document.createElement('div');
    title.textContent = render(item);
    const time = document.createElement('small');
    time.textContent = new Date(item.timestamp || item.createdAt).toLocaleString('en-US');
    li.append(title, time);
    list.append(li);
  }
}
function showScenarios(scenarios) {
  const list = byId('scenarios');
  list.replaceChildren();
  for (const scenario of scenarios) {
    const button = document.createElement('button');
    button.textContent = scenario.name;
    button.title = scenario.description;
    button.addEventListener('click', async () => {
      try {
        await request('/api/scenarios/' + encodeURIComponent(scenario.id), {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({})
        });
        byId('command-result').textContent = 'Scenario started.';
        await refresh();
      } catch (error) {
        byId('error').textContent = 'Scenario could not be started (' + error.message + ')';
      }
    });
    list.append(button);
  }
}
function showIncidents(incidents) {
  const list = byId('incidents');
  list.replaceChildren();
  for (const incident of incidents.slice(0, 20)) {
    const li = document.createElement('li');
    const title = document.createElement('div');
    title.textContent = incident.category + ' · ' + incident.description + ' · ' +
      ['Open', 'Acknowledged', 'Resolved'][incident.status];
    li.append(title);
    if (incident.status < 2) {
      const button = document.createElement('button');
      const nextStatus = incident.status + 1;
      button.textContent = nextStatus === 1 ? 'Acknowledge' : 'Resolve';
      button.addEventListener('click', async () => {
        try {
          await request('/api/incidents/' + encodeURIComponent(incident.id) + '/status', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ status: nextStatus })
          });
          await refresh();
        } catch (error) {
          byId('error').textContent = 'Administrator key required (' + error.message + ')';
        }
      });
      li.append(button);
    }
    list.append(li);
  }
}
function draw(samples) {
  const canvas = byId('chart'), context = canvas.getContext('2d');
  const w = canvas.width, h = canvas.height, pad = 24;
  context.clearRect(0, 0, w, h);
  context.strokeStyle = '#33465e';
  context.lineWidth = 1;
  for (const level of [0, 25, 50, 75, 100]) {
    const y = h - pad - (h - 2 * pad) * level / 100;
    context.beginPath(); context.moveTo(pad, y); context.lineTo(w - pad, y); context.stroke();
  }
  if (!samples.length) return;
  context.strokeStyle = '#54c9a5'; context.lineWidth = 3; context.beginPath();
  samples.forEach((sample, i) => {
    const x = pad + (w - 2 * pad) * i / Math.max(1, samples.length - 1);
    const y = h - pad - (h - 2 * pad) * sample.tankLevel / 100;
    if (i === 0) context.moveTo(x, y); else context.lineTo(x, y);
  });
  context.stroke();
}
async function refresh() {
  try {
    const [plant, telemetry, events, incidents, scenarios, me] = await Promise.all([
      request('/api/plant'), request('/api/telemetry?count=100'),
      request('/api/events'), request('/api/incidents'), request('/api/scenarios'), request('/api/me')
    ]);
    byId('level').textContent = plant.tankLevel.toFixed(1) + ' %';
    byId('fill').style.width = plant.tankLevel + '%';
    byId('pump').textContent = plant.pumpRunning ? 'Running' : 'Stopped';
    byId('valve').textContent = plant.outletValveOpen ? 'Open' : 'Closed';
    byId('inflow').textContent = plant.inflowRate.toFixed(1) + ' %/s';
    byId('outflow').textContent = plant.outflowRate.toFixed(1) + ' %/s';
    byId('incident-count').textContent = incidents.filter(i => i.status !== 2).length;
    byId('connection').textContent = 'Connected · ' + me.username + ' (' + me.role + ')';
    byId('error').textContent = '';
    draw(telemetry);
    itemList('events', events.slice(0, 20), e => e.category + ' · ' + e.message);
    showIncidents(incidents);
    showScenarios(scenarios);
  } catch (error) {
    byId('connection').textContent = 'Disconnected';
    byId('error').textContent = 'Check your API key and server availability (' + error.message + ')';
  }
}
byId('connect').addEventListener('click', () => {
  key = byId('key').value;
  clearInterval(refreshTimer);
  refresh();
  refreshTimer = setInterval(refresh, 3000);
});
document.querySelectorAll('[data-command]').forEach(button => button.addEventListener('click', async () => {
  const command = button.dataset.command;
  const isPump = command.startsWith('pump');
  const path = isPump ? '/api/plant/pump/P-101/command' : '/api/plant/valve/V-101/command';
  const payload = isPump ? { running: command === 'pump-on' }
    : { open: command === 'valve-open' };
  try {
    await request(path, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) });
    byId('command-result').textContent = 'Command completed.';
    await refresh();
  } catch (error) {
    byId('command-result').textContent = 'Command failed (' + error.message + ').';
  }
}));
