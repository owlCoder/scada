const byId = id => document.getElementById(id);
let key = '';
let refreshTimer;
let refreshInProgress = false;
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
          method: 'POST'
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
    title.textContent = incident.displayId + ' · ' + incident.severity + ' · ' + incident.title + ' · ' + incident.status;
    li.append(title);
    const nextStatus = { Open: 'Acknowledged', Acknowledged: 'Resolved', InProgress: 'Resolved' }[incident.status];
    if (nextStatus) {
      const button = document.createElement('button');
      button.textContent = nextStatus === 'Acknowledged' ? 'Acknowledge' : 'Resolve';
      button.addEventListener('click', async () => {
        try {
          await request('/api/incidents/' + encodeURIComponent(incident.id) + '/status', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ status: nextStatus, notes: 'Resolved from dashboard', expectedVersion: incident.version })
          });
          await refresh();
        } catch (error) {
          byId('error').textContent = 'Operator key required (' + error.message + ')';
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
    const y = h - pad - (h - 2 * pad) * sample.value / 100;
    if (i === 0) context.moveTo(x, y); else context.lineTo(x, y);
  });
  context.stroke();
}
async function refresh() {
  if (refreshInProgress) return;
  refreshInProgress = true;
  try {
    const [plant, telemetry, events, incidents, scenarios, me] = await Promise.all([
      request('/api/plant'), request('/api/trends/LT-101?count=100'),
      request('/api/events?pageSize=20'), request('/api/incidents?activeOnly=true&pageSize=20'), request('/api/scenarios'), request('/api/me')
    ]);
    const tank = plant.tanks.find(t => t.id === 'T-101');
    const pump = plant.pumps.find(p => p.id === 'P-101');
    const valve = plant.valves.find(v => v.id === 'V-101');
    const outlet = plant.valves.find(v => v.id === 'V-301');
    byId('level').textContent = tank.levelPercent.toFixed(1) + ' %';
    byId('fill').style.width = tank.levelPercent + '%';
    byId('pump').textContent = pump.state + ' · ' + pump.mode;
    byId('valve').textContent = valve.position.toFixed(0) + ' % · ' + valve.mode;
    byId('inflow').textContent = valve.flowRate.toFixed(2) + ' m³/min';
    byId('outflow').textContent = outlet.flowRate.toFixed(2) + ' m³/min';
    byId('incident-count').textContent = incidents.totalCount;
    byId('connection').textContent = 'Connected · ' + me.username + ' (' + me.role + ')';
    byId('error').textContent = '';
    draw(telemetry);
    itemList('events', events.items, e => e.category + ' · ' + e.message);
    showIncidents(incidents.items);
    showScenarios(scenarios);
  } catch (error) {
    byId('connection').textContent = 'Disconnected';
    byId('error').textContent = 'Check your API key and server availability (' + error.message + ')';
  } finally {
    refreshInProgress = false;
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
  const device = isPump ? 'P-101' : 'V-101';
  const path = isPump ? '/api/plant/pump/P-101/command' : '/api/plant/valve/V-101/command';
  const payload = isPump ? { running: command === 'pump-on' }
    : { open: command === 'valve-open' };
  try {
    // Operator commands require manual mode; the device stays in manual until switched back.
    await request('/api/plant/devices/' + device + '/mode/manual', { method: 'POST' });
    await request(path, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) });
    byId('command-result').textContent = 'Command completed.';
    await refresh();
  } catch (error) {
    byId('command-result').textContent = 'Command failed (' + error.message + ').';
  }
}));
