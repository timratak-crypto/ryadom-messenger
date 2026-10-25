import { test } from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import { once } from 'node:events';
import { WebSocket } from 'ws';
import { attachCalls } from './calls.mjs';

async function fixture(t, options = {}) {
  const people = { a: { id: '1', username: 'alice' }, b: { id: '2', username: 'bobby' }, c: { id: '3', username: 'carol' } };
  const revoked = new Set();
  const server = http.createServer();
  const calls = attachCalls(server, { authenticate: async token => {
    const key = token.slice(7, 8); if (revoked.has(key) || !people[key]) throw Object.assign(Error('auth'), { status: 401 }); return people[key];
  }, resolvePeer: async (token, username) => Object.values(people).find(u => u.username === username), ...options });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const url = `ws://127.0.0.1:${server.address().port}/api/calls`;
  t.after(async () => { calls.close(); await new Promise(resolve => server.close(resolve)); });
  async function connect(key) {
    const ws = new WebSocket(url, { headers: { Authorization: 'Bearer ' + key.repeat(64) } });
    const events = [], waiters = [];
    ws.on('message', (data, binary) => { const v = binary ? Buffer.from(data) : JSON.parse(data.toString()); if (waiters.length) waiters.shift()(v); else events.push(v); });
    ws.next = () => events.length ? Promise.resolve(events.shift()) : new Promise((resolve, reject) => {
      const timer = setTimeout(() => reject(Error('event timeout')), 3000); waiters.push(v => { clearTimeout(timer); resolve(v); });
    });
    ws.control = v => ws.send(JSON.stringify(v));
    await once(ws, 'open'); assert.equal((await ws.next()).type, 'ready'); return ws;
  }
  return { connect, url, revoked };
}
test('authentication and browser origins are checked before upgrade', async t => {
  const f = await fixture(t);
  for (const headers of [{}, { Authorization: 'Bearer ' + 'f'.repeat(64) }, { Authorization: 'Bearer ' + 'a'.repeat(64), Origin: 'https://evil.example' }]) {
    await new Promise(resolve => { const ws = new WebSocket(f.url, { headers }); ws.on('error', resolve); ws.on('open', () => { assert.fail('Unauthorized upgrade'); }); });
  }
});
test('call summaries use authenticated participants and timestamps and survive repeated hangup', async t => {
 const events=[],f=await fixture(t,{recordCall:async e=>events.push(e)}),a=await f.connect('a'),b=await f.connect('b'),c=await f.connect('c');
 a.control({type:'call',username:'bobby',from:'3',duration:999});const ringing=await a.next();await b.next();
 c.control({type:'hangup',id:ringing.id});b.control({type:'accept',id:ringing.id,answeredAt:0});await a.next();await b.next();
 b.control({type:'hangup',id:ringing.id,endedAt:0,duration:999});await a.next();await b.next();
 a.control({type:'hangup',id:ringing.id});await new Promise(resolve=>setTimeout(resolve,30));
 assert.equal(events.length,1);assert.equal(events[0].from,'1');assert.equal(events[0].to,'2');assert.equal(events[0].id,ringing.id);
 assert.ok(events[0].createdAt<=events[0].answeredAt&&events[0].answeredAt<=events[0].endedAt);assert.ok(events[0].answeredAt>Date.now()-10000);
 assert.equal(events[0].duration,undefined);
});
test('missed online and offline calls leave one callback record each',async t=>{
 const events=[],f=await fixture(t,{recordCall:async e=>events.push(e)}),a=await f.connect('a'),b=await f.connect('b');
 a.control({type:'call',username:'bobby'});const ringing=await a.next();await b.next();a.control({type:'cancel'});await a.next();await b.next();
 a.control({type:'call',username:'carol'});assert.equal((await a.next()).reason,'offline');await new Promise(resolve=>setTimeout(resolve,30));
 assert.equal(events.length,2);assert.equal(events[0].id,ringing.id);assert.ok(events.every(e=>e.answeredAt===null&&e.endedAt>=e.createdAt));
});
test('accepted call forwards exact PCM in both directions, isolated from third user', async t => {
  const f = await fixture(t), a = await f.connect('a'), b = await f.connect('b'), c = await f.connect('c');
  a.control({ type: 'call', username: 'bobby' }); const ringing = await a.next(), incoming = await b.next();
  assert.equal(ringing.type, 'ringing'); assert.equal(incoming.username, 'alice'); assert.equal(incoming.id, ringing.id);
  // Forged third-party acceptance/hangup/audio cannot reach this call.
  c.control({ type: 'accept', id: ringing.id }); c.control({ type: 'hangup', id: ringing.id }); c.send(Buffer.alloc(640, 9));
  a.send(Buffer.alloc(640, 8)); // no audio is relayed before acceptance
  b.control({ type: 'accept', id: incoming.id }); assert.equal((await a.next()).type, 'active'); assert.equal((await b.next()).type, 'active');
  const pcm = Buffer.alloc(640, 11); a.send(pcm); assert.deepEqual(await b.next(), pcm);
  const response = Buffer.alloc(3200, 22); b.send(response); assert.deepEqual(await a.next(), response);
  a.control({ type: 'hangup', id: ringing.id }); assert.equal((await a.next()).reason, 'finished'); assert.equal((await b.next()).reason, 'finished');
});
test('offline, busy, decline and disconnect are explicit', async t => {
  const f = await fixture(t), a = await f.connect('a');
  a.control({ type: 'call', username: 'bobby' }); assert.equal((await a.next()).reason, 'offline');
  const b = await f.connect('b'), c = await f.connect('c');
  a.control({ type: 'call', username: 'bobby' }); const ring = await a.next(); await b.next();
  c.control({ type: 'call', username: 'bobby' }); assert.equal((await c.next()).reason, 'busy');
  b.control({ type: 'hangup', id: ring.id }); assert.equal((await a.next()).reason, 'declined'); await b.next();
  a.control({ type: 'call', username: 'bobby' }); await a.next(); await b.next(); b.terminate(); assert.equal((await a.next()).reason, 'disconnected');
});
test('only first answering device receives audio', async t => {
  const f = await fixture(t), a = await f.connect('a'), b = await f.connect('b'), b2 = await f.connect('b');
  a.control({ type: 'call', username: 'bobby' }); const ring = await a.next(); await b.next(); await b2.next();
  b.control({ type: 'accept', id: ring.id }); await a.next(); await b.next(); assert.equal((await b2.next()).reason, 'answered_elsewhere');
  b2.control({ type: 'hangup', id: ring.id }); b2.send(Buffer.alloc(640, 1));
  b.send(Buffer.alloc(640, 2)); assert.deepEqual(await a.next(), Buffer.alloc(640, 2));
});

test('opening an incoming notification attaches the new foreground connection', async t => {
  const f=await fixture(t),a=await f.connect('a'),watcher=await f.connect('b');
  a.control({type:'call',username:'bobby'});const ringing=await a.next();await watcher.next();
  const foreground=await f.connect('b'),incoming=await foreground.next();
  assert.equal(incoming.type,'incoming');assert.equal(incoming.id,ringing.id);assert.equal(incoming.username,'alice');
  foreground.control({type:'accept',id:incoming.id});assert.equal((await a.next()).type,'active');assert.equal((await foreground.next()).type,'active');
  assert.equal((await watcher.next()).reason,'answered_elsewhere');watcher.terminate();
  foreground.send(Buffer.alloc(1280,19));assert.deepEqual(await a.next(),Buffer.alloc(1280,19));
  a.control({type:'hangup',id:ringing.id});assert.equal((await foreground.next()).reason,'finished');await a.next();
});

test('new connection after cancellation does not replay an expired notification call', async t => {
  const f=await fixture(t),a=await f.connect('a'),watcher=await f.connect('b');
  a.control({type:'call',username:'bobby'});const ringing=await a.next();await watcher.next();
  a.control({type:'hangup',id:ringing.id});await a.next();await watcher.next();
  const foreground=await f.connect('b');foreground.control({type:'call',username:'carol'});
  assert.equal((await foreground.next()).reason,'offline');
});
test('unanswered calls expire and session revocation closes connection', async t => {
  const f = await fixture(t, { ringMs: 80, heartbeatMs: 50, reauthMs: 100 }), a = await f.connect('a'), b = await f.connect('b');
  a.control({ type: 'call', username: 'bobby' }); await a.next(); await b.next(); assert.equal((await a.next()).reason, 'unanswered'); await b.next();
  f.revoked.add('a'); const [code] = await once(a, 'close'); assert.equal(code, 1008);
});
test('oversized binary frames and malformed controls close connection', async t => {
  const f = await fixture(t), a = await f.connect('a'), b = await f.connect('b');
  a.send(Buffer.alloc(6402)); assert.equal((await once(a, 'close'))[0], 1009);
  b.send('{'); assert.equal((await once(b, 'close'))[0], 1008);
});
test('three consecutive accepted calls on the same connections retain duplex audio', async t => {
  const f = await fixture(t), a = await f.connect('a'), b = await f.connect('b');
  for (let i=0;i<3;i++) {
    a.control({type:'call',username:'bobby'});const outgoing=await a.next();await b.next();
    b.control({type:'accept',id:outgoing.id});assert.equal((await a.next()).type,'active');await b.next();
    b.send(Buffer.alloc(1280,i+1));assert.deepEqual(await a.next(),Buffer.alloc(1280,i+1));
    a.send(Buffer.alloc(1280,i+11));assert.deepEqual(await b.next(),Buffer.alloc(1280,i+11));
    b.control({type:'hangup',id:outgoing.id});await a.next();await b.next();
  }
});
test('a cancelled peer lookup cannot send an error into a later accepted call', async t => {
  let rejectOld, started;const firstStarted=new Promise(resolve=>{started=resolve;});let lookups=0;
  const f=await fixture(t,{resolvePeer:async()=>{if(++lookups===1){started();return new Promise((resolve,reject)=>{rejectOld=reject;});}return {id:'2',username:'bobby'};}});
  const a=await f.connect('a'),b=await f.connect('b');a.control({type:'call',username:'bobby'});await firstStarted;
  a.control({type:'cancel'});a.control({type:'call',username:'bobby'});const outgoing=await a.next();await b.next();
  b.control({type:'accept',id:outgoing.id});await a.next();await b.next();rejectOld(Error('old lookup timeout'));
  await new Promise(resolve=>setTimeout(resolve,20));b.send(Buffer.alloc(640,77));assert.deepEqual(await a.next(),Buffer.alloc(640,77));
});

test('incoming conversation can be followed immediately by a callback with notification watcher online', async t => {
  const f=await fixture(t),pc=await f.connect('a'),phone=await f.connect('b'),watcher=await f.connect('b');
  phone.control({type:'call',username:'alice'});const first=await phone.next();await pc.next();
  pc.control({type:'accept',id:first.id});await phone.next();await pc.next();
  phone.control({type:'hangup',id:first.id});await phone.next();await pc.next();
  for(let i=0;i<5;i++){
    pc.control({type:'call',username:'bobby',requestId:'callback'+i});const ring=await pc.next();
    assert.equal(ring.type,'ringing');assert.equal(ring.requestId,'callback'+i);assert.equal((await phone.next()).id,ring.id);await watcher.next();
    phone.control({type:'accept',id:ring.id});assert.equal((await pc.next()).type,'active');await phone.next();
    assert.equal((await watcher.next()).reason,'answered_elsewhere');
    pc.send(Buffer.alloc(640,22+i));assert.deepEqual(await phone.next(),Buffer.alloc(640,22+i));
    phone.send(Buffer.alloc(640,42+i));assert.deepEqual(await pc.next(),Buffer.alloc(640,42+i));
    pc.control({type:'hangup',id:ring.id});await pc.next();await phone.next();
    await new Promise(resolve=>setTimeout(resolve,160));
  }
});

test('twelve attempts per minute allow normal retries and return a correlated cooldown for the thirteenth', async t => {
  const f=await fixture(t),a=await f.connect('a');
  for(let i=0;i<12;i++){
    a.control({type:'call',username:'bobby',requestId:'attempt'+i});const reply=await a.next();
    assert.equal(reply.reason,'offline');assert.equal(reply.requestId,'attempt'+i);
  }
  a.control({type:'call',username:'bobby',requestId:'rate-test'});const rate=await a.next();
  assert.equal(rate.type,'error');assert.equal(rate.reason,'rate');assert.equal(rate.requestId,'rate-test');
  assert.ok(rate.retryAfterMs>0&&rate.retryAfterMs<=60000);
});
test('transient authentication outage preserves calls but expired grace does not', async t => {
  let outage=false;
  const f=await fixture(t,{heartbeatMs:30,reauthMs:40,authGraceMs:400,authenticate:async token=>{if(outage)throw Object.assign(Error('Temporary upstream error'),{status:503});return token[7]==='a'?{id:'1',username:'alice'}:{id:'2',username:'bobby'};}});
  const a=await f.connect('a'),b=await f.connect('b');a.control({type:'call',username:'bobby'});const outgoing=await a.next();await b.next();
  b.control({type:'accept',id:outgoing.id});await a.next();await b.next();outage=true;
  await new Promise(resolve=>setTimeout(resolve,130));b.send(Buffer.alloc(640,88));assert.deepEqual(await a.next(),Buffer.alloc(640,88));
  outage=false;await new Promise(resolve=>setTimeout(resolve,80));outage=true;
  assert.equal((await once(a,'close'))[0],1008);
});
