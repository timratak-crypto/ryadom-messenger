package app.ryadom.family;

// The wire format is always signed PCM16 little-endian, independently of device APIs.
final class CallPcm {
 static byte[] encode(short[] samples,int count){
  if(count<0||count>samples.length||count>3200)throw new IllegalArgumentException("PCM frame length");
  byte[] bytes=new byte[count*2];for(int i=0;i<count;i++){bytes[i*2]=(byte)samples[i];bytes[i*2+1]=(byte)(samples[i]>>>8);}return bytes;
 }
 static int peak(short[] samples,int count){int peak=0;for(int i=0;i<count;i++)peak=Math.max(peak,Math.abs((int)samples[i]));return peak;}
}
