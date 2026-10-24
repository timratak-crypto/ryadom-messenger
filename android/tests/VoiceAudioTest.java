package app.ryadom.family;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.Arrays;
public class VoiceAudioTest {
 public static void main(String[] args){
  byte[] pcm=new byte[32000];for(int i=0;i<pcm.length;i++)pcm[i]=(byte)(i%127);
  byte[] wav=VoiceAudio.wav(pcm);ByteBuffer b=ByteBuffer.wrap(wav).order(ByteOrder.LITTLE_ENDIAN);
  if(b.getInt(4)!=wav.length-8||b.getInt(16)!=16||b.getShort(20)!=1||b.getShort(22)!=1||b.getInt(24)!=16000||b.getInt(28)!=32000||b.getShort(32)!=2||b.getShort(34)!=16||b.getInt(40)!=pcm.length)throw new AssertionError("WAV headers");
  if(!Arrays.equals(Arrays.copyOfRange(wav,44,wav.length),pcm)||VoiceAudio.seconds(wav)!=1)throw new AssertionError("PCM or duration");
  if(VoiceAudio.seconds(VoiceAudio.wav(new byte[1920000]))!=60)throw new AssertionError("Full minute");
  try{VoiceAudio.wav(new byte[3]);throw new AssertionError("Odd byte length accepted");}catch(IllegalArgumentException expected){}
  try{VoiceAudio.wav(new byte[1920002]);throw new AssertionError("Oversize accepted");}catch(IllegalArgumentException expected){}
  byte[] bad=wav.clone();ByteBuffer.wrap(bad).order(ByteOrder.LITTLE_ENDIAN).putInt(40,Integer.MAX_VALUE);if(VoiceAudio.seconds(bad)!=0)throw new AssertionError("Truncated chunk");
  System.out.println("PASS: voice WAV headers, exact PCM, duration, full minute, invalid size and truncated chunk; microphone not activated.");
 }
}
