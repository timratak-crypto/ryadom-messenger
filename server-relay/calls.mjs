import { WebSocketServer, WebSocket } from 'ws';
import { randomUUID } from 'node:crypto';

// Authenticated audio relay. No audio recording; only call outcome metadata is persisted.
// Protocol 1: raw little-endian PCM, 16000 Hz, one channel, signed 16-bit.
export function attachCalls(server, { authenticate, resolvePeer, recordCall = async () => {}, ringMs = 35000, heartbeatMs = 20000, reauthMs = 60000, authGraceMs = 180000 } = {}) {
  const wss = new WebSocketServer({ noServer: true, maxPayload: 6400, perMessageDeflate: false });
  const users = new Map(), occupied = new Map(), calls = new Map(), attempts = new Map();
  let pending = 0, closed = false;
  const historyTasks = new Set();
  function history(call) {
    const event = { id: call.id, from: call.from, to: call.to, createdAt: call.createdAt, answeredAt: call.answeredAt ?? null, endedAt: Date.now() };
    const task = Promise.resolve().then(() => recordCall(event)).catch(() => console.error('Call history temporarily unavailable'));
    historyTasks.add(task);void task.finally(() => historyTasks.delete(task));
  }
  const send = (ws, value) => { if (ws.readyState === WebSocket.OPEN) ws.send(JSON.stringify(value)); };
  function end(call, reason) {
    if (!call || !calls.delete(call.id)) return;
    clearTimeout(call.timer);history(call);
    occupied.delete(call.from); occupied.delete(call.to);
    for (const ws of call.sockets) send(ws, { type: 'ended', id: call.id, reason });
  }
  function validUser(user) { return user && typeof user.id === 'string' && /^[a-z0-9_]{3,24}$/.test(user.username); }
  server.on('upgrade', async (req, socket, head) => {
    const token = req.headers.authorization;
    const origin = req.headers.origin;
    if (closed || req.url !== '/api/calls' || !/^Bearer [a-f0-9]{64}$/.test(token || '') ||
        (origin && origin !== 'https://ryadom-family-chat.onrender.com') || pending >= 8 || wss.clients.size >= 128) {
      socket.end('HTTP/1.1 403 Forbidden\r\nConnection: close\r\n\r\n'); return;
    }
    pending++;
    try {
      const user = await authenticate(token);
      if (closed || socket.destroyed) return socket.destroy();
      if (!validUser(user) || (users.get(user.id)?.size || 0) >= 3) throw new Error('Unauthorized');
      wss.handleUpgrade(req, socket, head, ws => {
        ws.user = user; ws.authorization = token; ws.alive = true; ws.checking = false;
        ws.lastAuth = Date.now(); ws.audioBytes = 0; ws.windowAt = Date.now(); ws.controls = 0;
        if (!users.has(user.id)) users.set(user.id, new Set());
        users.get(user.id).add(ws);
        ws.on('pong', () => { ws.alive = true; });
        ws.on('error', () => {});
        ws.on('close', () => {
          const group = users.get(user.id); group?.delete(ws); if (!group?.size) users.delete(user.id);
          const call = calls.get(occupied.get(user.id));
          if (call && (call.caller === ws || call.callee === ws || (!call.callee && !(users.get(call.to)?.size)))) end(call, 'disconnected');
        });
        ws.on('message', (data, binary) => { void message(ws, data, binary).catch(() => { send(ws, { type: 'error', reason: 'unavailable' }); }); });
        send(ws, { type: 'ready', protocol: 1 });
        // A notification opens a new foreground connection while the watcher keeps ringing.
        const incoming = calls.get(occupied.get(user.id));
        if (incoming && !incoming.callee && incoming.to === user.id) {
          incoming.sockets.push(ws);
          send(ws, { type: 'incoming', id: incoming.id, username: incoming.caller.user.username });
        }
      });
    } catch { if (!socket.destroyed) socket.end('HTTP/1.1 401 Unauthorized\r\nConnection: close\r\n\r\n'); }
    finally { pending--; }
  });
  async function message(ws, data, binary) {
    if (Date.now() - ws.windowAt >= 1000) { ws.windowAt = Date.now(); ws.audioBytes = 0; ws.controls = 0; }
    if (binary) {
      ws.audioBytes += data.length;
      if (data.length < 2 || data.length % 2 || ws.audioBytes > 96000) return ws.close(1008, 'Audio limit');
      const call = calls.get(occupied.get(ws.user.id));
      if (!call?.callee || (ws !== call.caller && ws !== call.callee)) return;
      const peer = call.caller === ws ? call.callee : call.caller;
      if (peer.readyState === WebSocket.OPEN && peer.bufferedAmount < 16000) peer.send(data, { binary: true });
      return;
    }
    if (data.length > 1024 || ++ws.controls > 15) return ws.close(1008, 'Control limit');
    let value; try { value = JSON.parse(data.toString()); } catch { return ws.close(1008, 'Invalid JSON'); }
    if (value?.type === 'call') {
      // Optional correlation keeps late dial responses out of a subsequent call.
      const requestId = typeof value.requestId === 'string' && /^[a-zA-Z0-9_-]{1,64}$/.test(value.requestId) ? value.requestId : undefined;
      const fail = (reason, extra = {}) => send(ws, { type: 'error', reason, requestId, ...extra });
      if (occupied.has(ws.user.id) || ws.dialing) return fail('busy');
      if (!/^[a-z0-9_]{3,24}$/.test(value.username || '') || value.username === ws.user.username) return fail('invalid');
      const recent = (attempts.get(ws.user.id) || []).filter(t => Date.now() - t < 60000);
      if (recent.length >= 12) return fail('rate', { retryAfterMs: Math.max(1, 60000 - (Date.now() - recent[0])) });
      recent.push(Date.now()); attempts.set(ws.user.id, recent); ws.dialing = true;
      // Cancellation or disconnect during the authenticated peer lookup must win.
      const generation = ws.dialGeneration = randomUUID();
      try {
        const peer = await resolvePeer(ws.authorization, value.username);
        if (ws.readyState !== WebSocket.OPEN || ws.dialGeneration !== generation) return;
        if (!validUser(peer) || peer.username !== value.username || peer.id === ws.user.id) return fail('invalid');
        if (occupied.has(ws.user.id)) return fail('busy');
        if (occupied.has(peer.id)) { history({ id: randomUUID(), from: ws.user.id, to: peer.id, createdAt: Date.now() }); return fail('busy'); }
        const targets = [...(users.get(peer.id) || [])].filter(s => s.readyState === WebSocket.OPEN);
        if (!targets.length) { history({ id: randomUUID(), from: ws.user.id, to: peer.id, createdAt: Date.now() }); return fail('offline'); }
        const call = { id: randomUUID(), createdAt: Date.now(), answeredAt: null, from: ws.user.id, to: peer.id, caller: ws, callee: null, sockets: [ws, ...targets] };
        calls.set(call.id, call); occupied.set(call.from, call.id); occupied.set(call.to, call.id);
        call.timer = setTimeout(() => end(call, 'unanswered'), ringMs); call.timer.unref();
        send(ws, { type: 'ringing', id: call.id, username: peer.username, requestId });
        for (const target of targets) send(target, { type: 'incoming', id: call.id, username: ws.user.username });
      } catch (error) {
        // A failed lookup from a cancelled call cannot end a later call.
        if (ws.dialGeneration === generation) fail('unavailable');
      } finally { if (ws.dialGeneration === generation) ws.dialing = false; }
      return;
    }
    if (value?.type === 'cancel') {
      ws.dialGeneration = null; ws.dialing = false;
      const current = calls.get(occupied.get(ws.user.id)); if (current?.caller === ws) end(current, 'cancelled');
      return;
    }
    const call = calls.get(value?.id);
    if (!call || !call.sockets.includes(ws)) return;
    if (value.type === 'accept' && ws.user.id === call.to && !call.callee) {
      call.callee = ws; call.answeredAt = Date.now(); clearTimeout(call.timer);
      for (const target of call.sockets) {
        if (target !== call.caller && target !== ws) send(target, { type: 'ended', id: call.id, reason: 'answered_elsewhere' });
      }
      call.sockets = [call.caller, ws];
      send(call.caller, { type: 'active', id: call.id }); send(ws, { type: 'active', id: call.id });
    } else if (value.type === 'hangup') end(call, call.callee ? 'finished' : (ws === call.caller ? 'cancelled' : 'declined'));
  }
  const heartbeat = setInterval(() => {
    for (const [id, times] of attempts) if (Date.now() - times.at(-1) > 60000) attempts.delete(id);
    for (const ws of wss.clients) {
      if (!ws.alive) { ws.terminate(); continue; }
      ws.alive = false; ws.ping();
      if (!ws.checking && Date.now() - ws.lastAuth >= reauthMs) {
        ws.checking = true;
        void authenticate(ws.authorization).then(user => {
          if (!validUser(user) || user.id !== ws.user.id) ws.close(1008, 'Session expired');
          else { ws.user = user; ws.lastAuth = Date.now(); }
        }).catch(error => {
          // A transient upstream outage is not a revoked session. Never extend the
          // bounded lease without a successful authenticated check.
          if ([401, 403].includes(error.status) || Date.now() - ws.lastAuth >= authGraceMs) ws.close(1008, 'Session expired');
        }).finally(() => { ws.checking = false; });
      }
    }
  }, heartbeatMs); heartbeat.unref();
  const close = () => { closed = true; clearInterval(heartbeat); for (const call of calls.values()) end(call, 'disconnected'); for (const ws of wss.clients) ws.terminate(); wss.close(); return Promise.allSettled([...historyTasks]); };
  server.on('close', close);
  return { close };
}
