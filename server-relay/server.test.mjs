import test from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';
import { createRelay } from './server.mjs';

async function withRelay(fetchImpl, run) {
  const server = createRelay({ fetchImpl });
  server.listen(0, '127.0.0.1');
  await once(server, 'listening');
  try { await run(`http://127.0.0.1:${server.address().port}`); }
  finally { server.closeAllConnections(); await new Promise(resolve => server.close(resolve)); }
}
const secret = 'a'.repeat(64);
const json = value => ({ method: 'POST', headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${secret}` }, body: JSON.stringify(value) });

test('voice accepts a full minute and bounded authenticated downloads without enlarging text requests', async () => {
  const audio = 'a'.repeat(2560100); let calls=0;
  await withRelay(async (url,init) => {
    calls++; assert.equal(init.headers.Authorization,`Bearer ${secret}`); assert.equal(init.redirect,'manual');
    if(init.method==='POST'){assert.equal(JSON.parse(init.body).action,'sendVoice');return Response.json({ok:true});}
    return Response.json({data:audio});
  }, async base => {
    assert.equal((await fetch(base+'/api/v2',json({action:'sendVoice',data:audio}))).status,200);
    const result=await fetch(base+'/api/v2?view=voice&conversation=test&voice=test',{headers:{Authorization:`Bearer ${secret}`}});
    assert.equal(result.status,200);assert.equal((await result.json()).data,audio);
    assert.equal((await fetch(base+'/api/v2',json({action:'sendVoice',data:'a'.repeat(2850000)}))).status,413);
    assert.equal((await fetch(base+'/api/v2',json({action:'send',text:audio}))).status,413);assert.equal(calls,2);
  });
});

test('photo uploads and private downloads stay bounded and retain authorization', async () => {
  let calls = 0;
  await withRelay(async (url, init) => {
    calls++; assert.equal(init.headers.Authorization, `Bearer ${secret}`);
    assert.equal(new URL(url).pathname, '/api/v2');
    if (init.method === 'POST') assert.equal(JSON.parse(init.body).action, 'sendPhoto');
    return Response.json(init.method === 'POST' ? {ok:true} : {data:'jpeg-base64'});
  }, async base => {
    assert.equal((await fetch(base+'/api/v2', json({action:'sendPhoto',data:'a'.repeat(1000000)}))).status,200);
    assert.equal((await fetch(base+'/api/v2?view=photo&conversation=test&photo=test', {headers:{Authorization:`Bearer ${secret}`}})).status,200);
    assert.equal((await fetch(base+'/api/v2', json({action:'sendPhoto',data:'a'.repeat(1420000)}))).status,413);
    assert.equal((await fetch(base+'/api/v2', json({action:'send',text:'a'.repeat(20000)}))).status,413);
    assert.equal(calls,2);
  });
});

test('forwards password confirmation and protected administration only to their fixed routes', async () => {
  const expected = [
    ['/api/v2', {action:'logout',password:'confirmation-password'}],
    ['/api/admin', {action:'delete',id:'test-id',confirmUsername:'test'}]
  ];
  await withRelay(async (url, init) => {
    const [path, body] = expected.shift();
    assert.equal(new URL(url).pathname, path);
    assert.deepEqual(JSON.parse(init.body.toString()), body);
    assert.equal(init.headers.Authorization, `Bearer ${secret}`);
    assert.equal(init.redirect, 'manual');
    return Response.json({error:'Denied'}, {status:403});
  }, async base => {
    assert.equal((await fetch(base+'/api/v2', json({action:'logout',password:'confirmation-password'}))).status,403);
    assert.equal((await fetch(base+'/api/admin', json({action:'delete',id:'test-id',confirmUsername:'test'}))).status,403);
    assert.equal((await fetch(base+'/api/admin', json({action:'send'}))).status,400);
    assert.equal((await fetch(base+'/api/admin?url=https://example.com')).status,400);
    assert.equal(expected.length,0);
  });
});

test('routes account login, search and session tokens only to v2', async () => {
  let calls = 0;
  await withRelay(async (url, init) => {
    calls++;
    assert.equal(new URL(url).origin, 'https://ryadom-family-chat.timur727272727.chatgpt.site');
    assert.equal(new URL(url).pathname, '/api/v2');
    assert.equal(init.redirect, 'manual');
    assert.equal(init.headers.Cookie, undefined);
    if (init.body) assert.equal(JSON.parse(init.body.toString()).action, 'login');
    else assert.equal(new URL(url).searchParams.get('q'), '@test');
    return Response.json({ok:true});
  }, async base => {
    assert.equal((await fetch(base+'/api/v2', json({action:'login',username:'test',password:'test-password'}))).status, 200);
    assert.equal((await fetch(base+'/api/v2?view=search&q=%40test', {headers:{Authorization:`Bearer ${secret}`}})).status, 200);
    assert.equal((await fetch(base+'/api/v2?url=https://example.com')).status, 400);
    assert.equal((await fetch(base+'/api/v2',json({action:'create'}))).status, 400);
    assert.equal(calls,2);
  });
});

test('forwards exact Unicode message and room authentication only to the fixed API', async () => {
  const body = { action: 'send', text: 'Привет! 👋\n<hello> & "тест"', room: 'room', device: 'device', clientId: 'id' };
  await withRelay(async (url, init) => {
    assert.equal(url, 'https://ryadom-family-chat.timur727272727.chatgpt.site/api/chat');
    assert.deepEqual(JSON.parse(init.body.toString()), body);
    assert.equal(init.headers.Authorization, `Bearer ${secret}`);
    assert.equal(init.headers.Cookie, undefined);
    assert.equal(init.headers['OAI-Sites-Authorization'], undefined);
    assert.equal(init.redirect, 'manual');
    return Response.json({ ok: true, seq: 123 });
  }, async base => {
    const options = json(body);
    options.headers.Cookie = 'private-cookie=must-not-leak';
    options.headers['OAI-Sites-Authorization'] = 'must-not-leak';
    const r = await fetch(base + '/api/chat', options);
    assert.equal(r.status, 200);
    assert.deepEqual(await r.json(), { ok: true, seq: 123 });
    assert.equal(r.headers.get('cache-control'), 'no-store');
    assert.equal(r.headers.get('set-cookie'), null);
  });
});

test('keeps cursor, roster and application errors intact', async () => {
  await withRelay(async url => {
    assert.equal(new URL(url).search, '?room=room&device=device&after=77');
    return Response.json({ messages: [], members: [{ name: 'Мама', readSeq: 77 }], maxMembers: 5 });
  }, async base => {
    const r = await fetch(base + '/api/chat?room=room&device=device&after=77');
    assert.equal((await r.json()).members[0].readSeq, 77);
  });
  for (const status of [401, 403, 429, 500]) {
    await withRelay(async () => Response.json({ error: 'Проверочная ошибка.' }, { status }), async base => {
      const r = await fetch(base + '/api/chat');
      assert.equal(r.status, status);
      assert.equal((await r.json()).error, 'Проверочная ошибка.');
    });
  }
});

test('rejects malformed requests and proxy targets before contacting upstream', async () => {
  await withRelay(async () => { throw new Error('Must not contact upstream'); }, async base => {
    assert.equal((await fetch(base + '/health')).status, 200);
    assert.equal((await fetch(base + '/')).status, 200);
    assert.equal((await fetch(base + '/other')).status, 404);
    assert.equal((await fetch(base + '//example.com/api/chat')).status, 400);
    assert.equal((await fetch(base + '/api/chat?url=https://example.com')).status, 400);
    assert.equal((await fetch(base + '/api/chat', { method: 'DELETE' })).status, 405);
    assert.equal((await fetch(base + '/api/chat', { headers: { Authorization: 'Bearer wrong' } })).status, 401);
    assert.equal((await fetch(base + '/api/chat', { method: 'POST', body: '{}' })).status, 415);
    assert.equal((await fetch(base + '/api/chat', json({ action: 'admin' }))).status, 400);
    assert.equal((await fetch(base + '/api/chat', json({ action: 'send', text: 'x'.repeat(17_000) }))).status, 413);
    const malformed = json({ action: 'join' }); malformed.body = '{';
    assert.equal((await fetch(base + '/api/chat', malformed)).status, 400);
  });
});

test('does not follow redirects or disclose upstream HTML, exceptions or cookies', async () => {
  const responses = [
    () => new Response('', { status: 302, headers: { Location: 'https://example.com/collect' } }),
    () => new Response('<html>private details</html>', { status: 403, headers: { 'Content-Type': 'text/html' } }),
    () => new Response('not-json', { headers: { 'Content-Type': 'application/json' } }),
    () => Response.json(['unexpected']),
    () => { throw new Error('private signing or connection details'); },
    () => Response.json({ text: 'x'.repeat(2_100_000) }),
  ];
  for (const response of responses) {
    await withRelay(response, async base => {
      const r = await fetch(base + '/api/chat');
      assert.equal(r.status, 502);
      const message = await r.text();
      assert.ok(!message.includes('private'));
      assert.ok(JSON.parse(message).error);
    });
  }
});
test('profile and message actions pass through, call service endpoint stays private', async()=>{
 let count=0;await withRelay(async(url,init)=>{count++;assert.equal(init.headers.Authorization,'Bearer '+secret);return Response.json({ok:true});},async base=>{
  for(const action of ['rename','setAvatar','removeAvatar','editMessage','deleteMessage'])assert.equal((await fetch(base+'/api/v2',json({action,data:action==='setAvatar'?'a'.repeat(200000):''}))).status,200);
  for(const query of ['view=avatar&user=test','view=messages&conversation=test&after=12&changesAfter=10'])assert.equal((await fetch(base+'/api/v2?'+query,{headers:{Authorization:'Bearer '+secret}})).status,200);
  assert.equal((await fetch(base+'/api/call-events',json({action:'call'}))).status,404);
  assert.equal((await fetch(base+'/api/v2',json({action:'recordCall'}))).status,400);
  assert.equal(count,7);
 });
});
