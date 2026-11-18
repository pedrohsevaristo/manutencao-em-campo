import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { once } from 'node:events';
import { mkdtemp, rm } from 'node:fs/promises';
import { createServer, request as httpRequest } from 'node:http';
import { request as httpsRequest } from 'node:https';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { resolveApiOrigin } from '../mobile/src/apiOrigin.ts';

const root = fileURLToPath(new URL('../', import.meta.url));
const webProject = join(root, 'src', 'ManutencaoEmCampo.Web');
const webDll = join(webProject, 'bin', 'Release', 'net10.0', 'ManutencaoEmCampo.Web.dll');
const legacyExe = join(root, 'src', 'ManutencaoEmCampo.Legacy', 'bin', 'Release', 'net48',
  'ManutencaoEmCampo.Legacy.exe');
const password = 'SecurityFixture!2026';
const supervisor = 'supervisor@demo.test';
const technician = 'tecnico@demo.test';

function createTlsFixture(folder) {
  const pfx = join(folder, 'fixture.pfx');
  const passphrase = randomUUID();
  const script = [
    "$ErrorActionPreference = 'Stop'",
    '$rsa = [System.Security.Cryptography.RSA]::Create(2048)',
    'try {',
    "  $request = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=localhost', $rsa,",
    '    [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)',
    '  $names = [System.Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()',
    '  $names.AddIpAddress([System.Net.IPAddress]::Loopback)',
    '  $request.CertificateExtensions.Add($names.Build())',
    '  $cert = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-5), [DateTimeOffset]::UtcNow.AddHours(1))',
    '  try {',
    '    [IO.File]::WriteAllBytes($env:MAINTENANCE_TEST_PFX, $cert.Export(',
    '      [System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $env:MAINTENANCE_TEST_CERT_PASSWORD))',
    '  } finally { $cert.Dispose() }',
    '} finally { $rsa.Dispose() }',
  ].join('\n');
  const result = spawnSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command', script], {
    env: {
      ...process.env,
      MAINTENANCE_TEST_PFX: pfx,
      MAINTENANCE_TEST_CERT_PASSWORD: passphrase,
    },
    encoding: 'utf8', timeout: 20000, windowsHide: true,
  });
  if (result.error) throw result.error;
  assert.equal(result.status, 0, result.stderr);
  return {
    Kestrel__Certificates__Default__Path: pfx,
    Kestrel__Certificates__Default__Password: passphrase,
  };
}

function requestHttps(base, path, { method = 'GET', headers = {}, body } = {}) {
  const url = new URL(path, base);
  assert.equal(url.origin, base);
  assert.equal(url.protocol, 'https:');
  assert.equal(url.hostname, '127.0.0.1');

  return new Promise((resolve, reject) => {
    const encoded = body?.toString();
    const outgoing = encoded === undefined ? headers : {
      ...headers, 'Content-Type': 'application/x-www-form-urlencoded',
      'Content-Length': Buffer.byteLength(encoded),
    };
    const req = httpsRequest(url, {
      // Accept the disposable certificate only on this loopback test connection.
      rejectUnauthorized: false, method, headers: outgoing, signal: AbortSignal.timeout(6000),
    }, incoming => {
      const chunks = [];
      incoming.on('data', chunk => chunks.push(chunk));
      incoming.once('error', reject);
      incoming.once('end', () => {
        const resultHeaders = new Headers();
        for (const [name, values] of Object.entries(incoming.headersDistinct))
          for (const value of values) resultHeaders.append(name, value);
        resolve({
          status: incoming.statusCode,
          headers: resultHeaders,
          text: async () => Buffer.concat(chunks).toString('utf8'),
        });
      });
    });
    req.once('error', reject);
    req.end(encoded);
  });
}

async function startWeb(folder, { environment = 'Development', allowHttp = true, demo = true,
  forwardedHeaders = false, https = false, overrides = {} } = {}) {
  const tls = https ? createTlsFixture(folder) : null;
  const scheme = https ? 'https' : 'http';
  const dbPath = join(folder, 'test.db');
  const child = spawn(process.env.DOTNET_EXE ?? 'dotnet', [webDll, '--urls', scheme + '://127.0.0.1:0'], {
    cwd: webProject,
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: environment,
      DOTNET_ENVIRONMENT: environment,
      ASPNETCORE_FORWARDEDHEADERS_ENABLED: String(forwardedHeaders),
      AllowedHosts: 'localhost;127.0.0.1;[::1]',
      Database__Provider: 'Sqlite',
      ConnectionStrings__Sqlite: 'Data Source=' + dbPath,
      ConnectionStrings__SqlServer: 'Server=127.0.0.1,1;Database=SecurityFixture;Integrated Security=True',
      Demo__Enabled: String(demo),
      Demo__AllowHttp: String(allowHttp),
      Demo__Password: password,
      Mobile__WebOrigin: environment === 'Development' && demo && allowHttp ? 'http://localhost:8081' : '',
      Logging__LogLevel__Microsoft__Hosting__Lifetime: 'Information',
      ...tls,
      ...overrides,
    },
    stdio: ['ignore', 'pipe', 'pipe'],
    windowsHide: true,
  });

  let output = '';
  const closed = new Promise(resolve => child.once('close', resolve));
  const stop = async () => {
    if (child.exitCode === null && child.signalCode === null) child.kill();
    await closed;
  };
  try {
    const base = await new Promise((resolve, reject) => {
      const timeout = setTimeout(() => reject(Error('Servidor não iniciou: ' + output)), 15000);
      child.once('error', error => { clearTimeout(timeout); reject(error); });
      child.once('close', () => { clearTimeout(timeout); reject(Error('Servidor encerrou: ' + output)); });
      child.stderr.on('data', chunk => { output = (output + chunk).slice(-16000); });
      child.stdout.on('data', chunk => {
        output = (output + chunk).slice(-16000);
        const url = /Now listening on:\s+(https?:\/\/127\.0\.0\.1:\d+)/.exec(output)?.[1];
        if (url) { clearTimeout(timeout); resolve(url); }
      });
    });
    const send = https ? requestHttps : request;
    return { base, dbPath, stop, send, output: () => output };
  } catch (error) {
    await stop();
    throw error;
  }
}

async function withFixture(options, run) {
  const folder = await mkdtemp(join(tmpdir(), 'maintenance-security-'));
  try {
    const fixture = await startWeb(folder, options);
    try { return await run(fixture, folder); }
    finally { await fixture.stop(); }
  } finally {
    await rm(folder, { recursive: true, force: true, maxRetries: 3 });
  }
}

const request = (base, path, init) => fetch(new URL(path, base), {
  redirect: 'manual', signal: AbortSignal.timeout(6000), ...init,
});
const bearer = token => ({ Authorization: 'Bearer ' + token });

async function login(base, email, expected = 200) {
  const response = await request(base, '/api/session', {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password }),
  });
  assert.equal(response.status, expected);
  if (expected !== 200) return;
  const session = await response.json();
  assert.equal(session.expiresIn, 1800);
  assert.equal(typeof session.accessToken, 'string');
  return session.accessToken;
}

const currentSession = (base, token) => request(base, '/api/session', { headers: bearer(token) });

function cookieJar() {
  const values = new Map();
  return {
    update(response) {
      for (const cookie of response.headers.getSetCookie()) {
        const pair = cookie.split(';')[0];
        values.set(pair.split('=')[0], pair);
      }
    },
    header() { return { Cookie: [...values.values()].join('; ') }; },
  };
}

async function getForm(base, path, cookies, send = request) {
  const response = await send(base, path, { headers: cookies.header() });
  assert.equal(response.status, 200);
  cookies.update(response);
  const html = await response.text();
  const tag = /<input[^>]*name="__RequestVerificationToken"[^>]*>/.exec(html)?.[0];
  const token = /\bvalue="([^"]+)"/.exec(tag ?? '')?.[1];
  assert.ok(token, 'Token antifalsificação ausente');
  return token;
}

const cookieIsSecure = cookie => /;\s*secure(?:;|$)/i.test(cookie);

async function loginMvc(base, send = request) {
  const cookies = cookieJar();
  const token = await getForm(base, '/entrar', cookies, send);
  const response = await send(base, '/entrar', {
    method: 'POST', headers: cookies.header(),
    body: new URLSearchParams({ Email: supervisor, Password: password, __RequestVerificationToken: token }),
  });
  assert.equal(response.status, 302);
  const session = response.headers.getSetCookie().find(cookie => cookie.startsWith('ManutencaoEmCampo.Session='));
  assert.match(session ?? '', /;\s*httponly(?:;|$)/i);
  assert.match(session, /;\s*samesite=lax(?:;|$)/i);
  assert.equal(cookieIsSecure(session), base.startsWith('https:'));
  cookies.update(response);
  return cookies;
}

function query(path, operation) {
  const database = new DatabaseSync(path);
  try {
    database.exec('PRAGMA busy_timeout=5000');
    return operation(database);
  } finally { database.close(); }
}

test('SEC-01: sessões bearer e MVC podem ser revogadas separadamente e têm permissões atualizadas', async t => {
  await withFixture({}, async ({ base, dbPath }) => {
    let otherBearer;
    let otherCookie;
    await t.test('logout bearer invalida só o token usado', async () => {
      const first = await login(base, technician);
      otherBearer = await login(base, technician);
      assert.equal((await currentSession(base, first)).status, 200);
      assert.equal((await request(base, '/api/session', { method: 'DELETE', headers: bearer(first) })).status, 204);
      assert.equal((await currentSession(base, first)).status, 401);
      assert.equal((await currentSession(base, otherBearer)).status, 200);
    });

    await t.test('logout MVC rejeita o cookie anterior e preserva a outra sessão', async () => {
      const first = await loginMvc(base);
      otherCookie = await loginMvc(base);
      const token = await getForm(base, '/', first);
      const snapshot = first.header();
      const logout = await request(base, '/sair', {
        method: 'POST', headers: snapshot, body: new URLSearchParams({ __RequestVerificationToken: token }),
      });
      assert.equal(logout.status, 302);
      const invalid = await request(base, '/', { headers: snapshot });
      assert.equal(invalid.status, 302);
      assert.equal(new URL(invalid.headers.get('location'), base).pathname, '/entrar');
      assert.equal((await request(base, '/', { headers: otherCookie.header() })).status, 200);
    });

    await t.test('desativação impede login e revoga bearer e cookie em uso', async () => {
      query(dbPath, db => db.prepare('UPDATE AspNetUsers SET IsActive = 0 WHERE Email IN (?, ?)').run(technician, supervisor));
      try {
        assert.equal((await currentSession(base, otherBearer)).status, 401);
        assert.equal((await request(base, '/', { headers: otherCookie.header() })).status, 302);
        await login(base, technician, 401);
      } finally {
        query(dbPath, db => db.prepare('UPDATE AspNetUsers SET IsActive = 1 WHERE Email IN (?, ?)').run(technician, supervisor));
      }
      assert.equal((await currentSession(base, otherBearer)).status, 401);
      assert.equal((await request(base, '/', { headers: otherCookie.header() })).status, 302);
    });

    await t.test('mudanças de papel e de carimbo invalidam as credenciais anteriores', async () => {
      const first = await login(base, supervisor);
      const cookie = (await loginMvc(base)).header();
      const membership = query(dbPath, db => {
        const grant = db.prepare('SELECT ur.UserId, ur.RoleId FROM AspNetUserRoles ur JOIN AspNetUsers u ON u.Id = ur.UserId WHERE u.Email = ?').get(supervisor);
        assert.ok(grant);
        db.prepare('DELETE FROM AspNetUserRoles WHERE UserId = ? AND RoleId = ?').run(grant.UserId, grant.RoleId);
        return grant;
      });
      try {
        assert.equal((await request(base, '/api/tecnicos', { headers: bearer(first) })).status, 401);
        assert.equal((await request(base, '/', { headers: cookie })).status, 302);
      } finally {
        query(dbPath, db => db.prepare('INSERT INTO AspNetUserRoles (UserId, RoleId) VALUES (?, ?)')
          .run(membership.UserId, membership.RoleId));
      }
      assert.equal((await currentSession(base, first)).status, 401);

      const updated = await login(base, supervisor);
      assert.equal((await request(base, '/api/tecnicos', { headers: bearer(updated) })).status, 200);
      query(dbPath, db => db.prepare('UPDATE AspNetUsers SET SecurityStamp = ? WHERE Email = ?')
        .run(randomUUID(), supervisor));
      assert.equal((await currentSession(base, updated)).status, 401);
    });

    await t.test('expiração no banco bloqueia um bearer dentro do prazo criptográfico', async () => {
      const token = await login(base, 'tecnica@demo.test');
      assert.equal((await currentSession(base, token)).status, 200);
      query(dbPath, db => db.prepare('UPDATE Sessions SET ExpiresAt = ?').run('2000-01-01 00:00:00'));
      assert.equal((await currentSession(base, token)).status, 401);
    });
  });
});

test('SEC-01: um banco SQLite de demo anterior é atualizado preservando os dados', async () => {
  await withFixture({}, async ({ stop, dbPath }, folder) => {
    await stop();
    // Recreate the pre-migration shape in the temporary test database only.
    const stampBefore = query(dbPath, db => {
      db.exec('DROP TABLE Sessions; ALTER TABLE AspNetUsers DROP COLUMN IsActive');
      db.prepare('UPDATE AspNetUsers SET DisplayName = ? WHERE Email = ?').run('Técnico já cadastrado', technician);
      // The demo seeder restores this role through the production Identity store.
      const user = db.prepare('SELECT Id, SecurityStamp FROM AspNetUsers WHERE Email = ?').get(technician);
      db.prepare('DELETE FROM AspNetUserRoles WHERE UserId = ?').run(user.Id);
      return user.SecurityStamp;
    });
    const restarted = await startWeb(folder);
    try {
      const stampAfter = query(dbPath, db => db.prepare('SELECT SecurityStamp FROM AspNetUsers WHERE Email = ?')
        .get(technician).SecurityStamp);
      assert.notEqual(stampAfter, stampBefore);
      const token = await login(restarted.base, technician);
      const response = await currentSession(restarted.base, token);
      assert.equal(response.status, 200);
      assert.equal((await response.json()).name, 'Técnico já cadastrado');
      assert.ok((await (await request(restarted.base, '/api/chamados', { headers: bearer(token) })).json()).total > 0);
    } finally { await restarted.stop(); }
  });
});

test('SEC-02: Production recusa HTTP antes do login, mesmo com flags de demo e cabeçalho encaminhado', async () => {
  await withFixture({ environment: 'Production', forwardedHeaders: true }, async ({ base }) => {
    const res = await request(base, '/api/session', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-Forwarded-Proto': 'https' },
      body: JSON.stringify({ email: technician, password }),
    });
    assert.equal(res.status, 400);
    assert.equal((await res.json()).title, 'Use uma conexão HTTPS.');
    assert.equal(res.headers.getSetCookie().length, 0);
    assert.equal((await request(base, '/openapi/v1.json')).status, 400);
  });
});

test('SEC-02: Development sem opção explícita também recusa HTTP', async () => {
  await withFixture({ allowHttp: false }, async ({ base }) => {
    assert.equal((await request(base, '/entrar')).status, 400);
  });
});

test('SEC-02: demo explícita permite somente host de loopback e a origem de Expo configurada', async () => {
  await withFixture({}, async ({ base }) => {
    assert.equal((await request(base, '/entrar')).status, 200);
    const otherHostStatus = await new Promise((resolve, reject) => {
      // The Fetch implementation may replace the Host header with the connection origin.
      const req = httpRequest(new URL('/entrar', base), { headers: { Host: 'demo.invalid' } }, res => {
        res.resume();
        resolve(res.statusCode);
      });
      req.on('error', reject).setTimeout(6000, () => req.destroy(new Error('Tempo limite')));
      req.end();
    });
    assert.equal(otherHostStatus, 400);
    const res = await request(base, '/api/session', { method: 'OPTIONS', headers: {
      Origin: 'http://localhost:8081', 'Access-Control-Request-Method': 'DELETE',
      'Access-Control-Request-Headers': 'authorization',
    } });
    assert.equal(res.headers.get('access-control-allow-origin'), 'http://localhost:8081');
    assert.match(res.headers.get('access-control-allow-methods') ?? '', /DELETE/i);
  });
});

test('SEC-02: runtime do Expo aceita HTTPS e restringe HTTP à demo com loopback', () => {
  assert.equal(resolveApiOrigin('https://Example.test:8443/', false), 'https://example.test:8443');
  assert.equal(resolveApiOrigin('https://[2001:db8::1]:8443/', false), 'https://[2001:db8::1]:8443');
  assert.equal(resolveApiOrigin(undefined, true), 'http://localhost:5180');
  assert.equal(resolveApiOrigin('http://[::1]:5180/', true), 'http://[::1]:5180');
  for (const bad of [undefined, 'http://localhost:5180', 'http://127.0.0.1:5180', 'http://demo.test']) {
    assert.throws(() => resolveApiOrigin(bad, false), /HTTPS/);
  }
  for (const bad of ['http://demo.test', 'http://localhost.demo.test', 'http://localhost@demo.test',
    'https://user:password@demo.test', 'https://demo.test:65536', 'https://demo.test/invalid']) {
    assert.throws(() => resolveApiOrigin(bad, true), /HTTPS/);
  }
});

async function withHttpServer(listener, run) {
  const server = createServer(listener);
  server.listen(0, '127.0.0.1');
  await once(server, 'listening');
  try { return await run('http://127.0.0.1:' + server.address().port); }
  finally { await new Promise(resolve => server.close(resolve)); }
}

async function runLegacy(...args) {
  const child = spawn(legacyExe, args, {
    env: { ...process.env, LEGACY_EMAIL: technician, DEMO_PASSWORD: password },
    stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true,
  });
  let output = '';
  child.stdout.on('data', chunk => { output += chunk.toString(); });
  child.stderr.on('data', chunk => { output += chunk.toString(); });
  const [code] = await once(child, 'close');
  return { code, output };
}

test('SEC-02/03: legado exige modo demo para HTTP e não segue redirecionamento de login', {
  skip: process.platform !== 'win32',
}, async () => {
  let destinationCalls = 0;
  await withHttpServer((_, res) => { destinationCalls++; res.writeHead(204).end(); }, async destination => {
    let logins = 0;
    await withHttpServer((req, res) => {
      if (req.method === 'POST' && req.url === '/api/session') logins++;
      req.resume();
      res.writeHead(307, { Location: destination + '/test-redirect' }).end();
    }, async source => {
      const rejected = await runLegacy(source);
      assert.equal(rejected.code, 1);
      assert.equal(logins, 0);
      const demo = await runLegacy('--demo', source);
      assert.equal(demo.code, 1);
      assert.equal(logins, 1);
      assert.equal(destinationCalls, 0);
    });
  });
});

test('SEC-01/03: cliente legado consulta a API real e encerra a sessão ao sair', {
  skip: process.platform !== 'win32',
}, async () => {
  await withFixture({}, async ({ base, dbPath }) => {
    const execution = await runLegacy('--demo', base);
    assert.equal(execution.code, 0, execution.output);
    assert.match(execution.output, /Exibindo 3 de 3 chamados/);
    assert.equal(query(dbPath, db => db.prepare('SELECT count(*) AS total FROM Sessions').get().total), 0);
  });
});

test('SEC-04/07: no-store em respostas sensíveis, host exato e Location relativa', async () => {
  await withFixture({}, async ({ base }) => {
    const noStore = response => {
      assert.match(response.headers.get('cache-control') ?? '', /\bno-store\b/);
      return response;
    };
    assert.equal(noStore(await request(base, '/entrar')).status, 200);
    assert.equal(noStore(await request(base, '/api/chamados')).status, 401);
    const signin = noStore(await request(base, '/api/session', {
      method: 'POST', headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ email: supervisor, password }),
    }));
    assert.equal(signin.status, 200);
    const { accessToken } = await signin.json();
    assert.equal(noStore(await currentSession(base, accessToken)).status, 200);
    const cookies = await loginMvc(base);
    assert.equal(noStore(await request(base, '/', { headers: cookies.header() })).status, 200);
    const asset = await request(base, '/css/site.css');
    assert.equal(asset.status, 200);
    assert.doesNotMatch(asset.headers.get('cache-control') ?? '', /\bno-store\b/);

    const created = noStore(await request(base, '/api/chamados', {
      method: 'POST', headers: { ...bearer(accessToken), 'content-type': 'application/json' },
      body: JSON.stringify({ title: 'Chamado de teste', location: 'Local', description: 'Descrição fictícia.' }),
    }));
    assert.equal(created.status, 201);
    assert.match(created.headers.get('location'), /^\/api\/chamados\/\d+$/);
    assert.equal(noStore(await request(base, created.headers.get('location'), { headers: bearer(accessToken) })).status, 200);
  });

  await withFixture({ overrides: { AllowedHosts: 'outro.test' } }, async ({ base }) => {
    // The connection is loopback and its Host would pass the demo transport policy.
    assert.equal((await request(base, '/entrar')).status, 400);
  });
  await assert.rejects(withFixture({ overrides: { AllowedHosts: '*' } }, () => {}), /AllowedHosts deve/);
});

test('regressão preexistente de chamados e sessões', async () => {
  await withFixture({}, async ({ base }) => {
    const env = { ...process.env, DEMO_URL: base, DEMO_PASSWORD: password };
    delete env.NODE_TEST_CONTEXT;
    const child = spawn(process.execPath, ['--test', join(root, 'scripts', 'test-demo.mjs')], {
      env,
      stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true,
    });
    let output = '';
    for (const stream of [child.stdout, child.stderr]) stream.on('data', chunk => { output += chunk; });
    const [code] = await once(child, 'close');
    assert.equal(code, 0, output);
    assert.match(output, /# pass 2/);
  });
});

test('SEC-05: cota por origem e conta sem bloquear a conta no Identity', async () => {
  await withFixture({ forwardedHeaders: true, overrides: { Traffic__LoginPerAccountAndSource: '6' } },
    async ({ base, dbPath }) => {
      for (let i = 0; i < 6; i++) {
        const email = i % 2 ? supervisor.toUpperCase() : supervisor;
        await loginWithPassword(base, email, 'Senha incorreta fictícia', 401);
      }

      const cookies = cookieJar();
      const antiforgery = await getForm(base, '/entrar', cookies);
      const limited = await request(base, '/entrar', {
        method: 'POST', headers: { ...cookies.header(), 'X-Forwarded-For': '127.0.0.2' },
        body: new URLSearchParams({
          Email: supervisor, Password: password, __RequestVerificationToken: antiforgery,
        }),
      });
      assert.equal(limited.status, 429);
      assert.ok(Number(limited.headers.get('retry-after')) > 0);
      const state = query(dbPath, db => db.prepare('SELECT AccessFailedCount, LockoutEnd FROM AspNetUsers WHERE Email = ?').get(supervisor));
      assert.equal(state.AccessFailedCount, 0);
      assert.equal(state.LockoutEnd, null);
      await login(base, technician);

      const body = JSON.stringify({ email: supervisor, password });
      const status = await rawRequest(base, '/api/session', {
        method: 'POST', localAddress: '127.0.0.2',
        headers: { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(body) },
      }, body);
      assert.equal(status, 200);
    });
});

async function loginWithPassword(base, email, suppliedPassword, expected) {
  const response = await request(base, '/api/session', {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password: suppliedPassword }),
  });
  assert.equal(response.status, expected);
  return response;
}

function rawRequest(base, path, options, body) {
  return new Promise((resolve, reject) => {
    const req = httpRequest(new URL(path, base), options, res => {
      res.resume();
      resolve(res.statusCode);
    });
    req.on('error', reject).setTimeout(6000, () => req.destroy(new Error('Tempo limite')));
    if (body) req.write(body);
    req.end();
  });
}

test('SEC-09: limites de corpo, listagem e operações preservam outras contas e logout', async () => {
  await withFixture({ overrides: { Traffic__ListPerAccount: '2', Traffic__OperationsPerAccount: '3' } },
    async ({ base }) => {
      const first = await login(base, supervisor);
      const second = await login(base, technician);
      const sent = {
        title: 'Chamado fictício', location: 'Bancada QA', description: 'á'.repeat(1000),
      };
      const body = JSON.stringify({ ...sent, description: 'x'.repeat(33 * 1024) });
      const headers = { ...bearer(first), 'Content-Type': 'application/json' };
      const tooLarge = await request(base, '/api/chamados', {
        method: 'POST', headers: { ...headers, Origin: 'http://localhost:8081' }, body,
      });
      assert.equal(tooLarge.status, 413);
      assert.equal(tooLarge.headers.get('access-control-allow-origin'), 'http://localhost:8081');
      assert.equal(await rawRequest(base, '/api/chamados', { method: 'POST', headers }, body), 413);
      assert.equal((await request(base, '/api/chamados', {
        method: 'POST', headers, body: JSON.stringify(sent),
      })).status, 201);

      const cookies = await loginMvc(base);
      assert.equal((await request(base, '/', { headers: cookies.header() })).status, 200);
      const lastPage = await request(base, '/api/chamados?page=10000', { headers: bearer(first) });
      assert.equal(lastPage.status, 200);
      assert.deepEqual((await lastPage.json()).items, []);
      const limited = await request(base, '/api/chamados', { headers: bearer(first) });
      assert.equal(limited.status, 429);
      assert.ok(Number(limited.headers.get('retry-after')) > 0);
      assert.equal((await request(base, '/api/chamados', { headers: bearer(second) })).status, 200);
      assert.equal((await request(base, '/api/tecnicos', { headers: bearer(first) })).status, 200);
      assert.equal((await request(base, '/api/tecnicos', { headers: bearer(first) })).status, 429);
      assert.equal((await request(base, '/api/session', { method: 'DELETE', headers: bearer(first) })).status, 204);
    });
});

test('SEC-08: logs estruturados cobrem os eventos sem valores sensíveis', async () => {
  await withFixture({ overrides: {
    Logging__Console__FormatterName: 'json', Traffic__LoginPerAccountAndSource: '2',
  } }, async ({ base, dbPath, output }) => {
    const secret = 'ErroFicticio#Auditoria';
    assert.equal((await request(base, '/api/chamados')).status, 401);
    const token = await login(base, technician);
    assert.equal((await request(base, '/api/tecnicos', { headers: bearer(token) })).status, 403);
    assert.equal((await request(base, '/api/session', { method: 'DELETE', headers: bearer(token) })).status, 204);
    const invalidated = await login(base, technician);
    query(dbPath, db => db.prepare('UPDATE AspNetUsers SET SecurityStamp = ? WHERE Email = ?').run(randomUUID(), technician));
    assert.equal((await currentSession(base, invalidated)).status, 401);
    await loginWithPassword(base, supervisor, secret, 401);
    await loginWithPassword(base, supervisor, secret, 401);
    await loginWithPassword(base, supervisor, secret, 429);

    let entries = [];
    for (let tryNumber = 0; tryNumber < 20; tryNumber++) {
      entries = output().split(/\r?\n/).filter(line => line.includes('"Category":"ManutencaoEmCampo.Web.Data.SecurityEvents"'))
        .map(line => JSON.parse(line));
      if (entries.some(entry => entry.EventId === 4604)) break;
      await new Promise(resolve => setTimeout(resolve, 50));
    }
    for (const expected of [4600, 4601, 4602, 4603, 4604, 4605, 4606]) {
      assert.ok(entries.some(entry => entry.EventId === expected), 'Evento esperado ' + expected);
    }
    for (const entry of entries) {
      assert.equal(typeof entry.State.SecurityEvent, 'string');
      assert.match(entry.State.Surface, /^(api|mvc|session)$/);
      assert.equal(typeof entry.State.TraceId, 'string');
      const serialized = JSON.stringify(entry);
      for (const sensitive of [secret, password, token, invalidated, technician, supervisor, '127.0.0.1'])
        assert.ok(!serialized.includes(sensitive), 'Valor sensível no evento ' + entry.EventId);
    }
  });
});

test('SEC-06: conexão de execução exige identidade própria e certificado verificado', async () => {
  const address = 'Server=127.0.0.1,1;Database=SecurityFixture';
  for (const [connection, message] of [
    [address + ';User ID=SA;Encrypt=Mandatory', /A conta sa .*pode executar/],
    [address + ';Integrated Security=True;Encrypt=Optional', /exige criptografia.*certificado/],
    [address + ';Integrated Security=True;TrustServerCertificate=True', /exige criptografia.*certificado/],
    ['', /Configure ConnectionStrings__SqlServer para SQL Server/],
  ]) {
    await assert.rejects(withFixture({ environment: 'Production', overrides: {
      ConnectionStrings__SqlServer: connection,
      ConnectionStrings__SqlServerMigrations: address + ';Integrated Security=True;Encrypt=Mandatory',
    } }, () => {}), message);
  }
});

test('FR-01: login e páginas autenticadas do painel impedem enquadramento', async () => {
  await withFixture({ https: true, allowHttp: false }, async ({ base, send }) => {
    const assertFramePolicy = response => {
      assert.equal(response.status, 200);
      assert.match(response.headers.get('content-type') ?? '', /^text\/html\b/i);
      assert.equal(response.headers.get('x-frame-options'), 'DENY');
      assert.match(response.headers.get('content-security-policy') ?? '', /(?:^|;\s*)frame-ancestors 'none'(?:;|$)/);
    };
    const loginPage = await send(base, '/entrar');
    assertFramePolicy(loginPage);
    assert.doesNotMatch(await loginPage.text(), /\sstyle=/i);
    const anonymous = cookieJar();
    const token = await getForm(base, '/entrar', anonymous, send);
    const invalid = await send(base, '/entrar', {
      method: 'POST', headers: anonymous.header(), body: new URLSearchParams({
        Email: supervisor, Password: 'incorrect-fixture-password', __RequestVerificationToken: token,
      }),
    });
    assertFramePolicy(invalid);
    assert.match(await invalid.text(), /E-mail ou senha inv/);
    const cookies = await loginMvc(base, send);
    for (const path of ['/', '/chamados/1', '/chamados/novo', '/acesso-negado'])
      assertFramePolicy(await send(base, path, { headers: cookies.header() }));
  });
});

test('FR-02: CSP mínima no painel e nosniff preservam CSS, API e preflight Expo', async () => {
  await withFixture({}, async ({ base }) => {
    const page = await request(base, '/entrar');
    assert.equal(page.status, 200);
    assert.equal(page.headers.get('x-content-type-options'), 'nosniff');
    const directives = (page.headers.get('content-security-policy') ?? '').split(';').map(value => value.trim()).sort();
    assert.deepEqual(directives, [
      "base-uri 'none'", "default-src 'none'", "form-action 'self'", "frame-ancestors 'none'", "style-src 'self'",
    ]);
    const cssPath = /href="(\/css\/site\.css[^"]*)"/.exec(await page.text())?.[1];
    assert.ok(cssPath);
    const css = await request(base, cssPath);
    assert.equal(css.status, 200);
    assert.match(css.headers.get('content-type') ?? '', /^text\/css\b/i);
    assert.doesNotMatch(css.headers.get('cache-control') ?? '', /\bno-store\b/);
    const accessToken = await login(base, technician);
    const api = await currentSession(base, accessToken);
    assert.equal(api.status, 200);
    assert.match(api.headers.get('content-type') ?? '', /^application\/json\b/i);
    assert.match(api.headers.get('cache-control') ?? '', /\bno-store\b/);

    const origin = 'http://localhost:8081';
    const preflight = await request(base, '/api/chamados', {
      method: 'OPTIONS', headers: { Origin: origin, 'Access-Control-Request-Method': 'GET' },
    });
    assert.equal(preflight.status, 204);
    assert.equal(preflight.headers.get('access-control-allow-origin'), origin);
    for (const response of [css, api, preflight]) {
      assert.equal(response.headers.get('x-content-type-options'), 'nosniff');
      assert.equal(response.headers.get('x-frame-options'), null);
      assert.equal(response.headers.get('content-security-policy'), null);
    }
  });
});

test('FR-03: TempData é Secure em HTTPS e permanece funcional na demo HTTP explícita', async () => {
  for (const options of [
    { https: true, allowHttp: false },
    { https: true, allowHttp: true },
    { https: false, allowHttp: true },
  ]) {
    await withFixture(options, async ({ base, send }) => {
      const cookies = await loginMvc(base, send);
      const fields = {
        Title: 'Chamado fictício de segurança',
        Location: 'Bancada de testes',
        Description: 'Validação de formulário e aviso após redirecionamento.',
        Priority: 'Normal',
      };
      const path = '/chamados/novo';
      const submit = data => send(base, path, {
        method: 'POST', headers: cookies.header(), body: new URLSearchParams(data),
      });
      assert.equal((await submit(fields)).status, 400, 'O formulário ainda exige antifalsificação');
      const token = await getForm(base, path, cookies, send);
      const created = await submit({ ...fields, __RequestVerificationToken: token });
      assert.equal(created.status, 302);
      const noticeCookie = created.headers.getSetCookie().find(cookie => cookie.startsWith('.AspNetCore.Mvc.CookieTempDataProvider='));
      assert.match(noticeCookie ?? '', /^\.AspNetCore\.Mvc\.CookieTempDataProvider=[^;]+;/);
      assert.equal(cookieIsSecure(noticeCookie), options.https);
      assert.match(noticeCookie, /;\s*httponly(?:;|$)/i);
      assert.match(noticeCookie, /;\s*samesite=lax(?:;|$)/i);

      const destination = created.headers.get('location');
      assert.match(destination ?? '', /^\/chamados\/\d+$/);
      cookies.update(created);
      const details = await send(base, destination, { headers: cookies.header() });
      assert.equal(details.status, 200);
      assert.match(await details.text(), /Chamado registrado\./);
    });
  }
});
