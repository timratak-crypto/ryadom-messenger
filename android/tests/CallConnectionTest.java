package app.ryadom.family;
import java.net.URI;
import java.nio.ByteBuffer;
import java.util.Arrays;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.regex.Pattern;
import java.util.regex.Matcher;
import org.java_websocket.handshake.ServerHandshake;
// Exercises the exact Android transport and PCM encoder, without Android hardware.
public final class CallConnectionTest {
 public static void main(String[] args)throws Exception{
  final CountDownLatch done=new CountDownLatch(1);final Throwable[] failure={null};final int[] calls={0},frames={0};
  short[] samples=new short[640];for(int i=0;i<samples.length;i++)samples[i]=(short)(i*97-30000);final byte[] expected=CallPcm.encode(samples,samples.length);
  final CallConnection ws=new CallConnection(URI.create(args[0]),new String(new char[64]).replace('\0','b')){
   public void onOpen(ServerHandshake h){}
   public void onMessage(String text){
    if(text.contains("\"type\":\"ready\"")){System.out.println("ANDROID_READY");System.out.flush();}
    if(text.contains("\"type\":\"incoming\"")){Matcher m=Pattern.compile("\"id\":\"([^\"]+)\"").matcher(text);if(!m.find())throw new AssertionError("Call id");send("{\"type\":\"accept\",\"id\":\""+m.group(1)+"\"}");}
    if(text.contains("\"reason\":\"finished\"")){if(++calls[0]==3){close();done.countDown();}}
   }
   public void onMessage(ByteBuffer data){try{byte[] frame=new byte[data.remaining()];data.get(frame);if(!Arrays.equals(frame,expected))throw new AssertionError("Windows PCM mismatch");if(!sendAudio(expected))throw new AssertionError("Android audio dropped");frames[0]++;}catch(Throwable e){failure[0]=e;done.countDown();}}
   public void onClose(int code,String reason,boolean remote){if(calls[0]!=3){failure[0]=new AssertionError("Premature close "+code);done.countDown();}}
   public void onError(Exception e){failure[0]=e;done.countDown();}
  };
  ws.connect();try{if(!done.await(30,TimeUnit.SECONDS))throw new AssertionError("Call timeout");if(failure[0]!=null)throw new AssertionError(failure[0]);if(frames[0]!=90)throw new AssertionError("Missing frames: "+frames[0]);System.out.println("PASS Android actual WebSocket client: three calls, 90 exact PCM frames in both directions, heartbeat, teardown.");}finally{ws.closeBlocking();}
 }
}
