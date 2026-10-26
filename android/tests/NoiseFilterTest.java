package app.ryadom.family;
import java.util.Arrays;
public class NoiseFilterTest {
 static int checks;
 static void check(boolean ok,String name){checks++;if(!ok)throw new AssertionError(name);}
 static short[] tone(int amp,int count){short[] x=new short[count];for(int i=0;i<count;i++){int p=i%40;x[i]=(short)((p<20?p-10:30-p)*amp/10);}return x;}
 static double rms(short[] x,int from,int to){double sum=0;for(int i=from;i<to;i++)sum+=(double)x[i]*x[i];return Math.sqrt(sum/(to-from));}
 static short[] filter(short[] x,int mode){short[] y=x.clone();new NoiseFilter().process(y,y.length,mode);return y;}
 static long hash(short[] x){int h=(int)2166136261L;for(short s:x){h=(h^(s&255))*16777619;h=(h^((s>>8)&255))*16777619;}return h&0xffffffffL;}
 static byte[] encode(short[] x){java.io.ByteArrayOutputStream out=new java.io.ByteArrayOutputStream();for(int at=0;at<x.length;at+=640){short[] part=Arrays.copyOfRange(x,at,Math.min(at+640,x.length));byte[] frame=CallPcm.encode(part,part.length);out.write(frame,0,frame.length);}return out.toByteArray();}
 public static void main(String[] args){
  short[] quiet=tone(100,32000),near=tone(5000,32000),medium=tone(350,32000);
  check(Arrays.equals(filter(near,0),near),"off bit-exact");
  check(rms(filter(quiet,2),16000,32000)<rms(quiet,16000,32000)*.04,"strong >28 dB quiet reduction");
  check(rms(filter(quiet,1),16000,32000)<rms(quiet,16000,32000)*.12,"soft >18 dB quiet reduction");
  check(rms(filter(near,2),16000,32000)>rms(near,16000,32000)*.85,"near voice preserved");
  check(rms(filter(medium,1),16000,32000)>rms(filter(medium,2),16000,32000)*10,"soft preserves quieter voice");
  short[] dc=new short[32000];Arrays.fill(dc,(short)2000);check(rms(filter(dc,2),16000,32000)<1,"DC removed");
  short[] mixed=new short[64000];System.arraycopy(near,0,mixed,16000,16000);System.arraycopy(quiet,0,mixed,32000,32000);
  short[] y=filter(mixed,2);check(rms(y,16160,16480)>rms(mixed,16160,16480)*.8,"speech onset");
  check(rms(y,32000,32320)>rms(quiet,0,320)*.8,"hold protects endings");
  check(rms(y,48000,64000)<rms(quiet,0,16000)*.04,"background closes after speech");
  check(hash(y)==878819357L,"cross-platform golden output");
  byte[] pcm=encode(mixed),whole=pcm.clone(),chunked=pcm.clone();new NoiseFilter().process(whole,whole.length,2);NoiseFilter streaming=new NoiseFilter();
  for(int at=0;at<pcm.length;){int n=Math.min(((at%97)+1)*2,pcm.length-at);byte[] block=Arrays.copyOfRange(pcm,at,at+n);streaming.process(block,n,2);System.arraycopy(block,0,chunked,at,n);at+=n;}
  check(Arrays.equals(whole,chunked),"arbitrary chunks identical");check(Arrays.equals(whole,encode(y)),"byte and short paths identical");
  NoiseFilter reset=new NoiseFilter();for(int i=0;i<1000;i++)reset.sample((short)32767,2);check(reset.sample((short)-32768,0)==-32768,"switch off immediate");check(reset.sample((short)100,2)==new NoiseFilter().sample((short)100,2),"mode clears state");
  try{new NoiseFilter().process(new byte[3],3,2);throw new AssertionError("odd input accepted");}catch(IllegalArgumentException expected){checks++;}
  System.out.println("Noise tests passed: "+checks);
 }
}
