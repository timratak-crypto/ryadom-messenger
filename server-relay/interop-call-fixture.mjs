import http from 'node:http';
import { attachCalls } from './calls.mjs';
import { spawn } from 'node:child_process';
const users={a:{id:'1',username:'alice'},b:{id:'2',username:'bobby'}};
const server=http.createServer();
const relay=attachCalls(server,{authenticate:async token=>users[token.slice(7,8)],resolvePeer:async(token,name)=>Object.values(users).find(u=>u.username===name),heartbeatMs:100,reauthMs:200});
server.listen(0,'127.0.0.1',()=>{
 const url=`ws://127.0.0.1:${server.address().port}/api/calls`,children=[];let complete=0,failed=false,launched=false;
 const timer=setTimeout(()=>{failed=true;for(const child of children)child.kill();},35000);
 function track(child){children.push(child);child.on('error',error=>{console.error(error.message);failed=true;child.kill();});child.on('exit',code=>{if(code!==0)failed=true;if(++complete===2){clearTimeout(timer);relay.close();server.close();process.exitCode=failed?1:0;}});return child;}
 const java=track(spawn(process.argv[3],['-cp',process.argv[4],'app.ryadom.family.CallConnectionTest',url],{stdio:['ignore','pipe','inherit'],windowsHide:true}));
 java.stdout.on('data',chunk=>{process.stdout.write(chunk);if(!launched&&chunk.toString().includes('ANDROID_READY')){launched=true;track(spawn(process.argv[2],[url,'--android-peer'],{stdio:'inherit',windowsHide:true}));}});
});
