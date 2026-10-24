package app.ryadom.family;
import java.util.Arrays;
public final class CallAudioTest {
 public static void main(String[] args){
  short[] samples={0,1,-1,32767,-32768,0x1234};
  byte[] expected={0,0,1,0,-1,-1,-1,127,0,-128,0x34,0x12};
  if(!Arrays.equals(CallPcm.encode(samples,samples.length),expected))throw new AssertionError("PCM16 signed little-endian");
  if(CallPcm.peak(samples,samples.length)!=32768)throw new AssertionError("negative full-scale peak");
  if(CallPcm.encode(samples,2).length!=4)throw new AssertionError("partial AudioRecord read");
  boolean rejected=false;try{CallPcm.encode(samples,7);}catch(IllegalArgumentException e){rejected=true;}if(!rejected)throw new AssertionError("out of bounds");
  CallGeneration generation=new CallGeneration();int old=generation.next();generation.next();int current=generation.next();
  int ended=0;if(generation.isCurrent(old))ended++;if(ended!=0||!generation.isCurrent(current))throw new AssertionError("previous audio error interrupted new call");
  System.out.println("PASS Android signed PCM16 bytes, partial reads, amplitude, invalid length and stale call callback isolation; microphone not used.");
 }
}
