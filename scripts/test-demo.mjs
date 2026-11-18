import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { test } from 'node:test';

const base = process.env.DEMO_URL ?? 'http://localhost:5180';
const password = process.env.DEMO_PASSWORD ?? 'CampoLocal!2026';

function request(path, { method = 'GET', body, token } = {}) {
  const headers = {};
  if (body) headers['Content-Type'] = 'application/json';
  if (token) headers.Authorization = `Bearer ${token}`;
  return fetch(new URL(path, base), {
    method, headers, body: body && JSON.stringify(body), signal: AbortSignal.timeout(8000),
  });
}

async function login(email) {
  const response = await request('/api/session', { method: 'POST', body: { email, password } });
  assert.equal(response.status, 200, await response.clone().text());
  const { accessToken } = await response.json();
  assert.ok(accessToken);
  return accessToken;
}

test('o formulário MVC emite token antifalsificação e a API autentica e aplica os perfis', async () => {
  const form = await request('/entrar');
  assert.equal(form.status, 200, 'GET /entrar precisa renderizar o formulário com a sessão disponível');
  assert.match(await form.text(), /name="__RequestVerificationToken"/);
  assert.equal((await request('/api/chamados')).status, 401);

  const token = await login('tecnico@demo.test');
  const current = await request('/api/session', { token });
  assert.equal(current.status, 200);
  assert.equal((await current.json()).role, 'Technician');
  assert.equal((await request('/api/tecnicos', { token })).status, 403);
});

test('um chamado respeita atribuição, transições, validação e histórico', async () => {
  const supervisor = await login('supervisor@demo.test');
  const technician = await login('tecnico@demo.test');
  const otherTechnician = await login('tecnica@demo.test');
  const title = `Chamado de teste ${randomUUID().slice(0, 8)}`;

  const created = await request('/api/chamados', {
    token: supervisor, method: 'POST',
    body: { title, location: 'Bancada QA', description: 'Inspecionar a bomba de teste.', priority: 'High' },
  });
  assert.equal(created.status, 201, await created.clone().text());
  const { id } = await created.json();
  const ticketPath = `/api/chamados/${id}`;
  assert.equal(new URL(created.headers.get('location'), base).pathname, ticketPath);
  assert.equal((await request(ticketPath, { token: technician })).status, 404);

  const technicianList = await request('/api/tecnicos', { token: supervisor });
  assert.equal(technicianList.status, 200);
  const rafael = (await technicianList.json()).find(person => person.name === 'Rafael Lima');
  assert.ok(rafael);
  assert.equal((await request(`${ticketPath}/responsavel`, {
    token: supervisor, method: 'PUT', body: { technicianId: rafael.id },
  })).status, 204);
  assert.equal((await request(ticketPath, { token: otherTechnician })).status, 404);
  assert.equal((await request(`${ticketPath}/inicio`, { token: otherTechnician, method: 'POST' })).status, 404);
  assert.equal((await request(`${ticketPath}/inicio`, { token: supervisor, method: 'POST' })).status, 403);

  const close = note => request(`${ticketPath}/conclusao`, {
    token: technician, method: 'POST', body: { note },
  });
  assert.equal((await close('Conclusão antes do início.')).status, 409);
  assert.equal((await request(`${ticketPath}/inicio`, { token: technician, method: 'POST' })).status, 204);
  assert.equal((await close('curto')).status, 400);

  const note = 'Vedação substituída e vazamento resolvido.';
  assert.equal((await close(note)).status, 204);
  assert.equal((await close(note)).status, 409);

  const detail = await request(ticketPath, { token: supervisor });
  assert.equal(detail.status, 200);
  const { ticket, history } = await detail.json();
  assert.equal(ticket.status, 'Completed');
  assert.deepEqual(history.map(entry => entry.action), ['Created', 'Assigned', 'Started', 'Completed']);
  assert.equal(history.at(-1).actor, 'Rafael Lima');
  assert.equal(history.at(-1).note, note);
  assert.ok(history.at(-1).occurredAt.endsWith('Z'));

  const result = await request('/api/chamados?status=Completed&page=1&pageSize=1', { token: supervisor });
  assert.equal(result.status, 200);
  const page = await result.json();
  assert.equal(page.items.length, 1);
  assert.equal(page.items[0].id, id);
});
