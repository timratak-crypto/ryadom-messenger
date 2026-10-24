using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Ryadom;
public static class CallTests {
 static T Next<T>(BlockingCollection<T> queue){T result;if(!queue.TryTake(out result,5000))throw new Exception("Call event timeout");return result;}
 static void Require(bool yes){if(!yes)throw new Exception("Call transport assertion failed");}
 public static void Main(string[] args){
  if(args.Length>1){AndroidPeer(args[0]);return;}
  using(var a=new CallSocket())using(var b=new CallSocket()){
   var ae=new BlockingCollection<Dictionary<string,object>>();var be=new BlockingCollection<Dictionary<string,object>>();
   var ap=new BlockingCollection<byte[]>();var bp=new BlockingCollection<byte[]>();
   a.Control=x=>ae.Add(x);b.Control=x=>be.Add(x);a.Audio=x=>ap.Add(x);b.Audio=x=>bp.Add(x);
   var endpoint=new Uri(args[0]);var ar=a.Run(new string('a',64),endpoint);var br=b.Run(new string('b',64),endpoint);
   Require((string)Next(ae)["type"]=="ready");Require((string)Next(be)["type"]=="ready");a.Ready=true;b.Ready=true;
   a.Send(new{type="call",username="bobby"});var outgoing=Next(ae);var incoming=Next(be);Require((string)outgoing["type"]=="ringing"&&(string)incoming["username"]=="alice");
   var id=(string)incoming["id"];b.Send(new{type="accept",id});Require((string)Next(ae)["type"]=="active");Require((string)Next(be)["type"]=="active");
   var pcm=Enumerable.Range(0,1280).Select(i=>(byte)(i%251)).ToArray();a.PCM(pcm);Require(Next(bp).SequenceEqual(pcm));b.PCM(pcm);Require(Next(ap).SequenceEqual(pcm));
   a.Send(new{type="hangup",id});Require((string)Next(ae)["reason"]=="finished");Require((string)Next(be)["reason"]=="finished");
   Console.WriteLine("PASS Windows native WebSocket authentication, incoming call, acceptance, exact duplex PCM and hangup (no microphone activated)");
  }
 }
 static void AndroidPeer(string address){
  using(var a=new CallSocket()){
   var events=new BlockingCollection<Dictionary<string,object>>();var frames=new BlockingCollection<byte[]>();a.Control=x=>events.Add(x);a.Audio=x=>frames.Add(x);
   var running=a.Run(new string('a',64),new Uri(address));Require((string)Next(events)["type"]=="ready");a.Ready=true;
   var pcm=new byte[1280];for(int i=0;i<640;i++){short sample=unchecked((short)(i*97-30000));pcm[2*i]=(byte)sample;pcm[2*i+1]=(byte)(sample>>8);}
   Require(CallAudio.Peak(new byte[]{0,128})==32768);
   for(int call=0;call<3;call++){
    a.Send(new{type="call",username="bobby"});var ring=Next(events);Require((string)ring["type"]=="ringing");Require((string)Next(events)["type"]=="active");
    for(int i=0;i<30;i++){a.PCM(pcm);var response=Next(frames);Require(response.SequenceEqual(pcm));Require(CallAudio.Peak(response)>0);System.Threading.Thread.Sleep(40);}
    a.Send(new{type="hangup",id=(string)ring["id"]});Require((string)Next(events)["reason"]=="finished");
   }
   Console.WriteLine("PASS Windows receiving actual Android PCM: three calls, 90 non-silent frames, reconnect-free redial and end.");
  }
 }
}
