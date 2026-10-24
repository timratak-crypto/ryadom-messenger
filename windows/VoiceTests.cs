using System;
using System.Linq;
using Ryadom;
class VoiceTests {
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS: "+label);}
 static void Main(){
  var pcm=new byte[32000];for(int i=0;i<pcm.Length;i++)pcm[i]=(byte)(i%127);var wav=VoiceCapture.Encode(pcm);
  Check(BitConverter.ToInt32(wav,4)==wav.Length-8&&BitConverter.ToInt32(wav,24)==16000&&BitConverter.ToInt32(wav,28)==32000&&BitConverter.ToInt16(wav,20)==1&&BitConverter.ToInt16(wav,22)==1&&BitConverter.ToInt16(wav,34)==16,"WAV PCM format headers");
  Check(wav.Skip(44).SequenceEqual(pcm)&&VoiceCapture.Seconds(wav)==1,"exact audio bytes and duration");
  Check(VoiceCapture.Seconds(VoiceCapture.Encode(new byte[1920000]))==60,"one-minute recording format");
  bool bad=false;try{VoiceCapture.Encode(new byte[1920002]);}catch(ArgumentException){bad=true;}Check(bad,"oversized recording rejected");
  bad=false;try{VoiceCapture.Encode(new byte[3]);}catch(ArgumentException){bad=true;}Check(bad,"incomplete PCM sample rejected");
  Array.Copy(BitConverter.GetBytes(int.MaxValue),0,wav,40,4);Check(VoiceCapture.Seconds(wav)==0,"truncated audio rejected");
 }
}
