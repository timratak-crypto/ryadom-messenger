using System;
using System.IO;
using Ryadom;
class NoiseTests {
 static int checks;
 static void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}
 static short[] Tone(int amp,int count){var x=new short[count];for(int i=0;i<count;i++){int p=i%40;x[i]=(short)((p<20?p-10:30-p)*amp/10);}return x;}
 static double Rms(short[] x,int from,int to){double sum=0;for(int i=from;i<to;i++)sum+=(double)x[i]*x[i];return Math.Sqrt(sum/(to-from));}
 static short[] Filter(short[] x,int mode){var f=new NoiseFilter();var y=new short[x.Length];for(int i=0;i<x.Length;i++)y[i]=f.Sample(x[i],mode);return y;}
 static uint Hash(short[] x){uint h=2166136261;foreach(short s in x){h=unchecked((h^(byte)s)*16777619);h=unchecked((h^(byte)(s>>8))*16777619);}return h;}
 static void Main(string[] args){
  var quiet=Tone(100,32000);var near=Tone(5000,32000);var medium=Tone(350,32000);
  Check(Hash(Filter(near,0))==Hash(near),"off is bit-exact");
  Check(Rms(Filter(quiet,2),16000,32000)<Rms(quiet,16000,32000)*.04,"strong reduces quiet background >28 dB");
  Check(Rms(Filter(quiet,1),16000,32000)<Rms(quiet,16000,32000)*.12,"soft reduces quiet background >18 dB");
  Check(Rms(Filter(near,2),16000,32000)>Rms(near,16000,32000)*.85,"near voice preserved within 1.5 dB");
  Check(Rms(Filter(medium,1),16000,32000)>Rms(Filter(medium,2),16000,32000)*10,"soft preserves quieter voice");
  var dc=new short[32000];for(int i=0;i<dc.Length;i++)dc[i]=2000;Check(Rms(Filter(dc,2),16000,32000)<1,"DC removed");
  var mixed=new short[64000];Array.Copy(near,0,mixed,16000,16000);Array.Copy(quiet,0,mixed,32000,32000);
  var y=Filter(mixed,2);Check(Rms(y,16160,16480)>Rms(mixed,16160,16480)*.8,"speech onset within 10ms");
  Check(Rms(y,32000,32320)>Rms(quiet,0,320)*.8,"hold protects word endings");
  Check(Rms(y,48000,64000)<Rms(quiet,0,16000)*.04,"background attenuates again after speech");
  Check(Hash(y)==878819357,"cross-platform golden output");
  var pcm=new byte[mixed.Length*2];for(int i=0;i<mixed.Length;i++){pcm[2*i]=(byte)mixed[i];pcm[2*i+1]=(byte)(mixed[i]>>8);}
  var whole=(byte[])pcm.Clone();new NoiseFilter().Process(whole,whole.Length,2);var chunked=(byte[])pcm.Clone();var streaming=new NoiseFilter();
  for(int at=0;at<pcm.Length;){int n=Math.Min(((at%97)+1)*2,pcm.Length-at);var block=new byte[n];Array.Copy(pcm,at,block,0,n);streaming.Process(block,n,2);Array.Copy(block,0,chunked,at,n);at+=n;}
  Check(Convert.ToBase64String(whole)==Convert.ToBase64String(chunked),"arbitrary chunks preserve output and timing");
  var reset=new NoiseFilter();for(int i=0;i<1000;i++)reset.Sample(32767,2);Check(reset.Sample(-32768,0)==-32768,"switch off immediate");Check(reset.Sample(100,2)==new NoiseFilter().Sample(100,2),"mode switch clears old state");
  try{new NoiseFilter().Process(new byte[3],3,2);throw new Exception("odd bytes accepted");}catch(ArgumentException){checks++;}
  var wav=VoiceCapture.Encode(whole);Check(VoiceCapture.Seconds(wav)==4&&wav.Length==whole.Length+44,"voice WAV duration preserved");
  if(args.Length==2){byte[] input=File.ReadAllBytes(args[0]);new NoiseFilter().Process(input,input.Length,2);File.WriteAllBytes(args[1],input);}
  Console.WriteLine("Noise tests passed: "+checks);
 }
}
