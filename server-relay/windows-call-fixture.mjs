import http from 'node:http';
import { attachCalls } from './calls.mjs';
import { spawn } from 'node:child_process';
const users = { a: { id: '1', username: 'alice' }, b: { id: '2', username: 'bobby' } };
const server = http.createServer();
const relay = attachCalls(server, { authenticate: async token => users[token.slice(7, 8)], resolvePeer: async (token, name) => Object.values(users).find(u => u.username === name) });
server.listen(0, '127.0.0.1', () => {
  const child = spawn(process.argv[2], [`ws://127.0.0.1:${server.address().port}/api/calls`], { stdio: 'inherit', windowsHide: true });
  const timer = setTimeout(() => child.kill(), 20000);
  child.on('exit', code => { clearTimeout(timer); relay.close(); server.close(); process.exitCode = code ?? 1; });
});
