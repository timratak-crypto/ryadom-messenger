import http from 'node:http';
import { pathToFileURL } from 'node:url';
import { attachCalls } from './calls.mjs';

// This is a gateway only for this chat API, never an arbitrary HTTP proxy.
const UPSTREAM = 'https://ryadom-family-chat.timur727272727.chatgpt.site';
const REQUEST_LIMIT = 2_850_000;
const RESPONSE_LIMIT = 2_000_000;
const BEARER = /^Bearer [a-f0-9]{64}$/;

function reply(res, status, value) {
  res.writeHead(status, {
    'Content-Type': 'application/json; charset=utf-8',
    'Cache-Control': 'no-store',
    'X-Content-Type-Options': 'nosniff',
    'Referrer-Policy': 'no-referrer',
  });
  res.end(JSON.stringify(value));
}

function readRequest(req) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    let size = 0;
    req.on('data', chunk => {
      size += chunk.length;
      if (size > REQUEST_LIMIT) {
        chunks.length = 0;
        reject(Object.assign(new Error('Request too large'), { status: 413 }));
      } else chunks.push(chunk);
    });
    req.on('end', () => resolve(Buffer.concat(chunks)));
    req.on('error', reject);
    req.on('aborted', () => reject(new Error('Aborted')));
  });
}

async function readResponse(response, limit = RESPONSE_LIMIT) {
  if (!response.body) throw new Error('Empty response');
  const reader = response.body.getReader();
  const chunks = [];
  let size = 0;
  try {
    for (;;) {
      const { value, done } = await reader.read();
      if (done) break;
      size += value.byteLength;
      if (size > limit) throw new Error('Response too large');
      chunks.push(Buffer.from(value));
    }
    return JSON.parse(Buffer.concat(chunks).toString('utf8'));
  } finally {
    await reader.cancel().catch(() => {});
  }
}

export function createRelay({ fetchImpl = fetch, callLogKey = process.env.RYADOM_CALL_LOG_KEY } = {}) {
  let inFlight = 0;
  const server = http.createServer({ maxHeaderSize: 8192 }, async (req, res) => {
    try {
      // Reject absolute request targets and extra query fields instead of
      // allowing clients to influence the destination of authenticated calls.
      if (!req.url?.startsWith('/') || req.url.startsWith('//') || req.url.length > 2048) {
        return reply(res, 400, { error: 'Неверный запрос.' });
      }
      const url = new URL(req.url, 'http://localhost');
      if (req.method === 'GET' && (url.pathname === '/' || url.pathname === '/health')) {
        return reply(res, 200, { ok: true, service: 'ryadom-relay', version: '2.7.0', calls: 1, callHistory: /^[a-f0-9]{64}$/.test(callLogKey || ''), message: 'Сервер доступен. Вернитесь в приложение «Рядом».' });
      }
      if (!['/api/chat', '/api/v2', '/api/admin'].includes(url.pathname)) return reply(res, 404, { error: 'Страница не найдена.' });
      if (!['GET', 'POST'].includes(req.method)) return reply(res, 405, { error: 'Метод не поддерживается.' });
      const v2 = url.pathname === '/api/v2';
      const admin = url.pathname === '/api/admin';
      const queryKeys = admin ? ['q', 'after'] : v2 ? ['view', 'q', 'conversation', 'after', 'photo', 'voice', 'changesAfter', 'user'] : ['room', 'device', 'after'];
      if ([...url.searchParams.keys()].some(key => !queryKeys.includes(key))) {
        return reply(res, 400, { error: 'Неверные параметры запроса.' });
      }
      if (inFlight >= 64) return reply(res, 503, { error: 'Сервер занят. Повторите попытку.' });
      const authorization = req.headers.authorization;
      if (authorization && !BEARER.test(authorization)) return reply(res, 401, { error: 'Войдите в аккаунт.' });
      let body;
      if (req.method === 'POST') {
        if (!(req.headers['content-type'] || '').toLowerCase().startsWith('application/json')) {
          return reply(res, 415, { error: 'Ожидается JSON.' });
        }
        if (Number(req.headers['content-length'] || 0) > REQUEST_LIMIT) return reply(res, 413, { error: 'Слишком большое сообщение.' });
        body = await readRequest(req);
        let value;
        try { value = JSON.parse(body.toString('utf8')); } catch { return reply(res, 400, { error: 'Неверный формат запроса.' }); }
        if (v2 && value?.action === 'sendPhoto' && body.length > 1_420_000) return reply(res, 413, { error: 'Фотография слишком большая.' });
        if (!(v2 && ['sendPhoto', 'sendVoice', 'setAvatar'].includes(value?.action)) && body.length > 16_384) return reply(res, 413, { error: 'Слишком большой запрос.' });
        const actions = admin ? ['delete'] : v2 ? ['register', 'login', 'logout', 'open', 'send', 'sendPhoto', 'sendVoice', 'rename', 'setAvatar', 'removeAvatar', 'editMessage', 'deleteMessage'] : ['create', 'join', 'send'];
        if (!value || typeof value !== 'object' || Array.isArray(value) || !actions.includes(value.action)) {
          return reply(res, 400, { error: 'Неизвестное действие.' });
        }
      }
      // Never forward cookies, ChatGPT service tokens, client IP headers,
      // or request-supplied hostnames. The room secret goes only to UPSTREAM.
      const headers = { Accept: 'application/json', 'User-Agent': 'RyadomRelay/1.0' };
      if (authorization) headers.Authorization = authorization;
      if (body) headers['Content-Type'] = 'application/json';
      inFlight++;
      try {
        const upstream = await fetchImpl(UPSTREAM + url.pathname + url.search, {
          method: req.method,
          headers,
          body,
          redirect: 'manual',
          signal: AbortSignal.timeout(20_000),
        });
        const type = upstream.headers.get('content-type')?.split(';')[0].trim().toLowerCase();
        if (upstream.status < 200 || (upstream.status >= 300 && upstream.status < 400) || type !== 'application/json') {
          await upstream.body?.cancel();
          return reply(res, 502, { error: 'Сервер чата временно недоступен. Повторите попытку позже.' });
        }
        const value = await readResponse(upstream, v2 && url.searchParams.get('view') === 'voice' ? 2_850_000 : RESPONSE_LIMIT);
        if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('Invalid response');
        return reply(res, upstream.status, value);
      } catch {
        return reply(res, 502, { error: 'Не удалось связаться с сервером чата. Повторите попытку позже.' });
      } finally { inFlight--; }
    } catch (error) {
      if (!res.headersSent && !res.destroyed) reply(res, error.status === 413 ? 413 : 400, { error: error.status === 413 ? 'Слишком большое сообщение.' : 'Не удалось обработать запрос.' });
    }
  });
  server.requestTimeout = 30_000;
  server.headersTimeout = 10_000;
  server.keepAliveTimeout = 5000;
  async function account(authorization, body) {
    const response = await fetchImpl(UPSTREAM + '/api/v2', {
      method: body ? 'POST' : 'GET', redirect: 'manual', signal: AbortSignal.timeout(20000),
      headers: { Authorization: authorization, 'Content-Type': 'application/json', Accept: 'application/json' },
      body: body ? JSON.stringify(body) : undefined,
    });
    if (response.status !== 200 || !response.headers.get('content-type')?.startsWith('application/json')) {
      await response.body?.cancel(); throw Object.assign(new Error('Account check failed'), { status: response.status });
    }
    return readResponse(response, RESPONSE_LIMIT);
  }
  const calls = attachCalls(server, {
    authenticate: async token => (await account(token)).user,
    resolvePeer: async (token, username) => (await account(token, { action: 'open', username })).peer,
    recordCall: async event => {
      if (!/^[a-f0-9]{64}$/.test(callLogKey || '')) return;
      for (let attempt = 0; attempt < 3; attempt++) {
        try {
          const response = await fetchImpl(UPSTREAM + '/api/call-events', {
            method: 'POST', redirect: 'manual', signal: AbortSignal.timeout(15000),
            headers: { Authorization: 'Bearer ' + callLogKey, 'Content-Type': 'application/json' }, body: JSON.stringify(event),
          });
          await response.body?.cancel();
          if (response.ok || response.status === 404) return;
          if (response.status < 500) throw Object.assign(Error('Call report rejected'), { permanent: true });
        } catch (error) { if (error.permanent || attempt === 2) throw error; }
        if (attempt < 2) await new Promise(resolve => setTimeout(resolve, 500 * (attempt + 1)));
      }
      throw Error('Call report failed');
    },
  });
  server.closeCalls = calls.close;
  return server;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const server = createRelay();
  const port = Number(process.env.PORT || 10000);
  server.listen(port, '0.0.0.0', () => console.log(`Ryadom gateway listening on port ${port}`));
  process.on('SIGTERM', async () => { await server.closeCalls(); server.close(() => process.exit(0)); });
}
