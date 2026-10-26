package app.ryadom.family;

import android.content.Context;
import android.media.AudioAttributes;
import android.media.AudioFocusRequest;
import android.media.AudioFormat;
import android.media.AudioManager;
import android.media.AudioRecord;
import android.media.MediaPlayer;
import android.media.MediaRecorder;
import android.os.Handler;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;

// PCM 16 kHz mono: supported by iPhone, Android and Windows without codec downloads.
final class VoiceAudio {
 interface Recorded { void done(byte[] data,Exception error); }
 private final Context context; private final Handler ui;
 private final AudioManager manager;
 private volatile boolean running,save;
 private Thread worker;
 private MediaPlayer player; private File file; private AudioFocusRequest focus;
 private Runnable playbackEnded;
 volatile int recordedBytes;
 VoiceAudio(Context context,Handler ui){this.context=context;this.ui=ui;manager=(AudioManager)context.getSystemService(Context.AUDIO_SERVICE);}
 synchronized boolean isRecording(){return worker!=null;}
 synchronized void record(Recorded callback) throws Exception {
  if(worker!=null)throw new Exception("Запись уже идёт.");
  final int noiseMode=NoisePreferences.get(context);stopPlayback();running=true;save=true;recordedBytes=0;
  worker=new Thread(()->{
   CaptureNoiseEffects effects=null;AudioRecord recorder=null;byte[] result=null;Exception failure=null;
   try{
    int minimum=AudioRecord.getMinBufferSize(16000,AudioFormat.CHANNEL_IN_MONO,AudioFormat.ENCODING_PCM_16BIT);
    if(minimum<=0)throw new Exception("Микрофон не поддерживает запись.");
    recorder=new AudioRecord.Builder().setAudioSource(MediaRecorder.AudioSource.MIC).setAudioFormat(new AudioFormat.Builder().setSampleRate(16000).setChannelMask(AudioFormat.CHANNEL_IN_MONO).setEncoding(AudioFormat.ENCODING_PCM_16BIT).build()).setBufferSizeInBytes(Math.max(minimum,6400)).build();
    if(recorder.getState()!=AudioRecord.STATE_INITIALIZED)throw new Exception("Не удалось включить микрофон.");
    effects=new CaptureNoiseEffects(recorder.getAudioSessionId(),noiseMode);if(running)recorder.startRecording();
    ByteArrayOutputStream pcm=new ByteArrayOutputStream();byte[] block=new byte[3200];
    while(running&&pcm.size()<1920000){
     int count=recorder.read(block,0,Math.min(block.length,1920000-pcm.size()),AudioRecord.READ_BLOCKING);
     if(count<0)throw new Exception("Запись прервалась. Попробуйте ещё раз.");
     if(count==0)break;
     pcm.write(block,0,count);recordedBytes=pcm.size();
    }
    if(save){if(pcm.size()<8000)throw new Exception("Запись слишком короткая. Скажите сообщение и нажмите стоп.");byte[] captured=pcm.toByteArray();new NoiseFilter().process(captured,captured.length,noiseMode);result=wav(captured);}
   }catch(Exception e){if(save)failure=e;}
   finally{running=false;if(effects!=null)effects.close();if(recorder!=null){try{recorder.stop();}catch(Exception ignored){}recorder.release();}synchronized(this){worker=null;}}
   final byte[] data=result;final Exception error=failure;ui.post(()->callback.done(data,error));
  },"Ryadom microphone");worker.start();
 }
 void stopRecording(boolean keep){save=keep;running=false;}
 static byte[] wav(byte[] pcm){
  if(pcm.length%2!=0||pcm.length>1920000)throw new IllegalArgumentException("Invalid PCM length");
  ByteBuffer b=ByteBuffer.allocate(44+pcm.length).order(ByteOrder.LITTLE_ENDIAN);
  b.put(new byte[]{'R','I','F','F'}).putInt(36+pcm.length).put(new byte[]{'W','A','V','E','f','m','t',' '}).putInt(16).putShort((short)1).putShort((short)1).putInt(16000).putInt(32000).putShort((short)2).putShort((short)16).put(new byte[]{'d','a','t','a'}).putInt(pcm.length).put(pcm);return b.array();
 }
 static double seconds(byte[] data){
  if(data==null||data.length<44)return 0;
  ByteBuffer b=ByteBuffer.wrap(data).order(ByteOrder.LITTLE_ENDIAN);int at=12;
  while(at<=data.length-8){int n=b.getInt(at+4);if(n<0||n>data.length-at-8)return 0;if(b.getInt(at)==0x61746164)return n/32000.0;at+=8+n+(n%2);}return 0;
 }
 static String time(double seconds){int n=Math.max(0,(int)Math.round(seconds));return String.format(java.util.Locale.ROOT,"%d:%02d",n/60,n%60);}
 void play(byte[] data,Runnable ended) throws Exception {
  stopPlayback();playbackEnded=ended;
  focus=new AudioFocusRequest.Builder(AudioManager.AUDIOFOCUS_GAIN_TRANSIENT).setAudioAttributes(new AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_MEDIA).setContentType(AudioAttributes.CONTENT_TYPE_SPEECH).build()).setOnAudioFocusChangeListener(change->{if(change<0)stopPlayback();},ui).build();
  if(manager.requestAudioFocus(focus)!=AudioManager.AUDIOFOCUS_REQUEST_GRANTED){stopPlayback();throw new Exception("Звук занят другим приложением. Попробуйте позже.");}
  try{
   file=File.createTempFile("ryadom-voice-",".wav",context.getCacheDir());try(FileOutputStream out=new FileOutputStream(file)){out.write(data);}
   player=new MediaPlayer();player.setAudioAttributes(new AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_MEDIA).setContentType(AudioAttributes.CONTENT_TYPE_SPEECH).build());
   try(FileInputStream in=new FileInputStream(file)){player.setDataSource(in.getFD());}
   player.setOnCompletionListener(p->stopPlayback());player.setOnErrorListener((p,a,b)->{stopPlayback();return true;});player.prepare();player.start();
  }catch(Exception e){stopPlayback();throw e;}
 }
 void stopPlayback(){
  if(player!=null){try{player.stop();}catch(Exception ignored){}player.release();player=null;}
  if(file!=null){file.delete();file=null;}if(focus!=null){manager.abandonAudioFocusRequest(focus);focus=null;}
  Runnable ended=playbackEnded;playbackEnded=null;if(ended!=null)ended.run();
 }
 void close(){stopRecording(false);stopPlayback();}
}
