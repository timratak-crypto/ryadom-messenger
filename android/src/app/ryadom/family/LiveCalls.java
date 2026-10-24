package app.ryadom.family;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.Context;
import android.content.pm.PackageManager;
import android.media.*;
import android.media.audiofx.AcousticEchoCanceler;
import android.os.Handler;
import android.widget.*;
import org.java_websocket.client.WebSocketClient;
import org.java_websocket.handshake.ServerHandshake;
import org.json.JSONObject;
import javax.net.ssl.SSLParameters;
import javax.net.ssl.SSLSocketFactory;
import java.net.URI;
import java.nio.ByteBuffer;
import java.util.Collections;
import java.util.Arrays;
import java.util.concurrent.ArrayBlockingQueue;
import java.util.concurrent.TimeUnit;

// Foreground-only native voice calls. The microphone starts only after acceptance.
final class LiveCalls {
 interface Host { void notice(String text); void stopVoice(); boolean recording(); }
 final Activity activity; final Handler ui; final Host host;
 volatile CallConnection socket; String token="",id="",peer="",state="",pendingPeer="",pendingAccept="";
 boolean enabled,ready,muted,speaker; volatile Audio audio;
 final CallGeneration generation=new CallGeneration();
 final java.util.concurrent.ExecutorService audioOperations=java.util.concurrent.Executors.newSingleThreadExecutor();
 AlertDialog dialog; TextView label; Button accept,mute,route; Ringtone ringtone; long started;
 final Runnable retry=()->connect();
 final Runnable clock=new Runnable(){public void run(){if(dialog!=null){update();ui.postDelayed(this,1000);}}};
 LiveCalls(Activity activity,Handler ui,Host host){this.activity=activity;this.ui=ui;this.host=host;}
 boolean busy(){return !state.isEmpty();}
 void sync(String next,boolean foreground){
  next=next==null?"":next;
  if(!next.equals(token)||!foreground){disconnect();token=next;}
  enabled=foreground&&!next.isEmpty();if(enabled)connect();
 }
 void connect(){
  if(!enabled||socket!=null||activity.isDestroyed())return;
  final CallConnection ws=new CallConnection(token){
   public void onOpen(ServerHandshake h){}
   public void onMessage(String text){ui.post(()->{if(socket==this)event(text);});}
   public void onMessage(ByteBuffer data){Audio target=audio;if(socket==this&&target!=null&&data.remaining()>0&&data.remaining()<=6400&&data.remaining()%2==0){byte[] frame=new byte[data.remaining()];data.get(frame);target.receive(frame);}}
   public void onClose(int code,String reason,boolean remote){ui.post(()->{if(socket!=this)return;socket=null;ready=false;end(busy()?"Связь прервалась. Повторите звонок.":"");ui.removeCallbacks(retry);if(enabled)ui.postDelayed(retry,5000);});}
   public void onError(Exception error){}
  };
  socket=ws;ws.connect();
 }
 void send(JSONObject value){if(socket!=null&&socket.isOpen())socket.send(value.toString());}
 static JSONObject json(String... values){JSONObject obj=new JSONObject();try{for(int i=0;i<values.length;i+=2)obj.put(values[i],values[i+1]);}catch(Exception ignored){}return obj;}
 void dial(String username){
  if(busy()||host.recording())return;
  if(!ready){connect();host.notice("Подключаем звонки… Повторите через несколько секунд.");return;}
  if(activity.checkSelfPermission(android.Manifest.permission.RECORD_AUDIO)!=PackageManager.PERMISSION_GRANTED){pendingPeer=username;activity.requestPermissions(new String[]{android.Manifest.permission.RECORD_AUDIO},44);return;}
  host.stopVoice();peer=username;state="dialing";id="";show();send(json("type","call","username",username));
 }
 void answer(){
  if(!state.equals("incoming"))return;
  if(activity.checkSelfPermission(android.Manifest.permission.RECORD_AUDIO)!=PackageManager.PERMISSION_GRANTED){pendingAccept=id;activity.requestPermissions(new String[]{android.Manifest.permission.RECORD_AUDIO},44);return;}
  host.stopVoice();state="connecting";stopRing();update();send(json("type","accept","id",id));
 }
 void permission(boolean allowed){
  String username=pendingPeer,call=pendingAccept;pendingPeer="";pendingAccept="";
  if(!allowed){host.notice("Разрешите микрофон в Настройках → Рядом → Разрешения.");return;}
  if(!enabled)return;if(!username.isEmpty())dial(username);else if(!call.isEmpty()&&call.equals(id))answer();
 }
 void event(String text){try{
  JSONObject e=new JSONObject(text);String type=e.optString("type"),call=e.optString("id");
  if(type.equals("ready")){ready=true;return;}
  if(type.equals("incoming")||type.equals("ringing")){
   if(type.equals("incoming")&&(busy()||host.recording())){send(json("type","hangup","id",call));return;}
   id=call;peer=e.optString("username");state=type;host.stopVoice();show();
   if(type.equals("incoming")){try{ringtone=RingtoneManager.getRingtone(activity,RingtoneManager.getDefaultUri(RingtoneManager.TYPE_RINGTONE));ringtone.play();}catch(Exception ignored){}}return;
  }
  if(type.equals("error")){end(reason(e.optString("reason")));return;}
  if(!id.equals(call)||id.isEmpty())return;
  if(type.equals("ended")){end(reason(e.optString("reason")));return;}
  if(type.equals("active")){
   stopRing();state="connecting";muted=false;speaker=false;update();
   final int attempt=generation.next();final CallConnection ws=socket;
   audioOperations.execute(()->{
    if(!generation.isCurrent(attempt))return;
    Audio next=null;
    try{
     next=new Audio(activity,ui,frame->generation.isCurrent(attempt)&&socket==ws&&ws.sendAudio(frame),error->ui.post(()->{if(generation.isCurrent(attempt)){hangup();host.notice(error);}}),()->ui.post(()->{if(generation.isCurrent(attempt)){hangup();host.notice("Звонок прерван другим приложением, использующим звук.");}}));
     audio=next;next.start();
     ui.post(()->{if(!generation.isCurrent(attempt))return;state="active";started=System.currentTimeMillis();update();});
    }catch(Exception error){if(next!=null)next.close();if(audio==next)audio=null;ui.post(()->{if(generation.isCurrent(attempt)){hangup();host.notice("Не удалось включить звук. "+error.getMessage());}});}
   });
  }
 }catch(Exception ignored){hangup();}}
 static String reason(String value){switch(value){case "offline":return "Собеседник не в сети. Попросите открыть «Рядом».";case "busy":return "Собеседник уже разговаривает.";case "declined":return "Вызов отклонён.";case "unanswered":return "Нет ответа.";case "rate":return "Подождите минуту перед следующим вызовом.";case "answered_elsewhere":return "Звонок принят на другом устройстве.";case "disconnected":return "Связь прервалась.";case "finished":case "cancelled":return "Звонок завершён.";default:return "Не удалось позвонить. Повторите попытку.";}}
 void show(){
  if(dialog==null){
   LinearLayout panel=new LinearLayout(activity);panel.setOrientation(LinearLayout.VERTICAL);int pad=(int)(24*activity.getResources().getDisplayMetrics().density);panel.setPadding(pad,pad,pad,pad);
   label=new TextView(activity);label.setTextSize(23);panel.addView(label);
   accept=new Button(activity);accept.setText("Принять");accept.setOnClickListener(v->answer());panel.addView(accept);
   mute=new Button(activity);mute.setOnClickListener(v->{muted=!muted;Audio target=audio;if(target!=null)target.muted=muted;update();});panel.addView(mute);
   route=new Button(activity);route.setOnClickListener(v->{speaker=!speaker;Audio target=audio;if(target!=null){final boolean selected=speaker;audioOperations.execute(()->{if(audio==target&&!target.stopped)target.speaker(selected);});}update();});panel.addView(route);
   Button finish=new Button(activity);finish.setText("Завершить");finish.setOnClickListener(v->hangup());panel.addView(finish);
   TextView hint=new TextView(activity);hint.setText("Звонок работает, пока «Рядом» открыт на обоих устройствах.");panel.addView(hint);
   dialog=new AlertDialog.Builder(activity).setView(panel).setCancelable(false).create();dialog.show();ui.post(clock);
   activity.setVolumeControlStream(AudioManager.STREAM_VOICE_CALL);
   activity.getWindow().addFlags(android.view.WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
  }update();
 }
 void update(){if(dialog==null)return;label.setText("@"+peer+"\n"+(state.equals("active")?"Разговор · "+VoiceAudio.time((System.currentTimeMillis()-started)/1000.0):state.equals("incoming")?"Входящий звонок":state.equals("ringing")?"Вызываем…":"Подключаемся…"));Audio meter=audio;if(state.equals("active")&&meter!=null)label.append("\n"+meter.status());accept.setVisibility(state.equals("incoming")?0:8);mute.setVisibility(state.equals("active")?0:8);route.setVisibility(state.equals("active")?0:8);mute.setText(muted?"Включить микрофон":"Выключить микрофон");route.setText(speaker?"Громкая связь: вкл.":"Громкая связь: выкл.");}
 void stopRing(){if(ringtone!=null){ringtone.stop();ringtone=null;}}
 void hangup(){send(id.isEmpty()?json("type","cancel"):json("type","hangup","id",id));end("Звонок завершён.");}
 void end(String message){generation.next();stopRing();audioOperations.execute(()->{Audio target=audio;audio=null;if(target!=null)target.close();});state="";id="";pendingAccept="";pendingPeer="";ui.removeCallbacks(clock);if(dialog!=null){dialog.dismiss();dialog=null;}activity.setVolumeControlStream(AudioManager.USE_DEFAULT_STREAM_TYPE);activity.getWindow().clearFlags(android.view.WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);if(!message.isEmpty())host.notice(message);}
 void destroy(){disconnect();audioOperations.shutdown();}
 void disconnect(){enabled=false;ui.removeCallbacks(retry);WebSocketClient old=socket;socket=null;ready=false;if(old!=null)old.close();end("");}

 interface Frames{boolean send(byte[] frame);} interface Failure{void error(String message);}
 static final class Audio {
  final ArrayBlockingQueue<byte[]> incoming=new ArrayBlockingQueue<>(6);final AudioManager manager;final Frames send;final Failure failure;
  AudioRecord recorder;AudioTrack player;final AudioFocusRequest focus;AcousticEchoCanceler echo;Thread captureThread,playThread;
  volatile boolean stopped,muted;volatile long receivedBytes,sentBytes;volatile int peak;final int oldMode;final boolean oldSpeaker;
  Audio(Context context,Handler ui,Frames send,Failure failure,Runnable interrupted){
   this.send=send;this.failure=failure;manager=(AudioManager)context.getSystemService(Context.AUDIO_SERVICE);oldMode=manager.getMode();oldSpeaker=manager.isSpeakerphoneOn();
   AudioAttributes attrs=new AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_VOICE_COMMUNICATION).setContentType(AudioAttributes.CONTENT_TYPE_SPEECH).build();
   focus=new AudioFocusRequest.Builder(AudioManager.AUDIOFOCUS_GAIN_TRANSIENT).setAudioAttributes(attrs).setOnAudioFocusChangeListener(change->{if(change<0&&!stopped)interrupted.run();},ui).build();
  }
  void speaker(boolean enabled){
   if(android.os.Build.VERSION.SDK_INT>=31){
    AudioDeviceInfo target=null;
    java.util.List<AudioDeviceInfo> devices=manager.getAvailableCommunicationDevices();
    // Keep a connected headset when speakerphone is off; forcing the earpiece
    // would put sound back beside the built-in microphone even with headphones.
    if(!enabled){AudioDeviceInfo current=manager.getCommunicationDevice();if(current!=null&&external(current))target=current;
     if(target==null)for(AudioDeviceInfo device:devices)if(external(device)){target=device;break;}
    }
    if(target==null)for(AudioDeviceInfo device:devices)if(device.getType()==(enabled?AudioDeviceInfo.TYPE_BUILTIN_SPEAKER:AudioDeviceInfo.TYPE_BUILTIN_EARPIECE)){target=device;break;}
    if(target!=null)manager.setCommunicationDevice(target);else manager.clearCommunicationDevice();
   }else manager.setSpeakerphoneOn(enabled);
   enableEcho();
  }
  static boolean external(AudioDeviceInfo device){
   int type=device.getType();return type==AudioDeviceInfo.TYPE_WIRED_HEADSET||type==AudioDeviceInfo.TYPE_WIRED_HEADPHONES||type==AudioDeviceInfo.TYPE_USB_HEADSET||type==AudioDeviceInfo.TYPE_BLUETOOTH_SCO||(android.os.Build.VERSION.SDK_INT>=31&&type==AudioDeviceInfo.TYPE_BLE_HEADSET);
  }
  void enableEcho(){
   // isAvailable only says an effect exists, not that our recording session uses it.
   AcousticEchoCanceler effect=echo;if(effect==null)return;
   try{if(effect.hasControl())effect.setEnabled(true);}catch(RuntimeException ignored){}
  }
  void releaseRecorder(){
   if(echo!=null){try{echo.release();}catch(RuntimeException ignored){}echo=null;}
   if(recorder!=null){try{recorder.stop();}catch(RuntimeException ignored){}try{recorder.release();}catch(RuntimeException ignored){}recorder=null;}
  }
  void openRecorder(int source,int buffer)throws Exception{
   try{
    recorder=new AudioRecord.Builder().setAudioSource(source).setAudioFormat(new AudioFormat.Builder().setSampleRate(16000).setChannelMask(AudioFormat.CHANNEL_IN_MONO).setEncoding(AudioFormat.ENCODING_PCM_16BIT).build()).setBufferSizeInBytes(buffer).build();
    if(recorder.getState()!=AudioRecord.STATE_INITIALIZED)throw new Exception("Не удалось открыть микрофон.");
    try{if(AcousticEchoCanceler.isAvailable()){echo=AcousticEchoCanceler.create(recorder.getAudioSessionId());enableEcho();}}catch(RuntimeException ignored){}
    recorder.startRecording();
    if(recorder.getRecordingState()!=AudioRecord.RECORDSTATE_RECORDING)throw new Exception("Микрофон не начал запись.");
   }catch(Exception error){releaseRecorder();throw error;}
  }
  void start()throws Exception{
   if(manager.requestAudioFocus(focus)!=AudioManager.AUDIOFOCUS_REQUEST_GRANTED)throw new Exception("Звук занят другим приложением.");
   manager.setMode(AudioManager.MODE_IN_COMMUNICATION);speaker(false);
   int input=AudioRecord.getMinBufferSize(16000,AudioFormat.CHANNEL_IN_MONO,AudioFormat.ENCODING_PCM_16BIT);
   if(input<=0)throw new Exception("Микрофон не поддерживает формат записи.");
   AudioAttributes attrs=new AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_VOICE_COMMUNICATION).setContentType(AudioAttributes.CONTENT_TYPE_SPEECH).build();
   AudioFormat format=new AudioFormat.Builder().setSampleRate(16000).setEncoding(AudioFormat.ENCODING_PCM_16BIT).setChannelMask(AudioFormat.CHANNEL_OUT_MONO).build();
   player=new AudioTrack.Builder().setAudioAttributes(attrs).setAudioFormat(format).setBufferSizeInBytes(Math.max(5120,AudioTrack.getMinBufferSize(16000,AudioFormat.CHANNEL_OUT_MONO,AudioFormat.ENCODING_PCM_16BIT))).setTransferMode(AudioTrack.MODE_STREAM).build();
   if(player.getState()!=AudioTrack.STATE_INITIALIZED)throw new Exception("Не удалось открыть динамик.");
   player.play();
   // The communication source selects the Android capture path with the
   // loudspeaker reference for AEC; plain MIC can bypass that preprocessing.
   // Keep explicit short[] PCM reads and serialized teardown from 2.4.1.
   try{openRecorder(MediaRecorder.AudioSource.VOICE_COMMUNICATION,Math.max(5120,input));}
   catch(SecurityException denied){throw denied;}
   catch(Exception unsupported){openRecorder(MediaRecorder.AudioSource.MIC,Math.max(5120,input));}
   captureThread=new Thread(()->{
    try{short[] samples=new short[640];while(!stopped){
     int n=recorder.read(samples,0,samples.length,AudioRecord.READ_BLOCKING);
     if(n<0)throw new Exception("Микрофон отключился ("+n+").");
     if(stopped)break;if(n==0){Thread.sleep(5);continue;}
     peak=CallPcm.peak(samples,n);
     if(!muted){byte[] frame=CallPcm.encode(samples,n);if(send.send(frame))sentBytes+=frame.length;}
    }}catch(Exception e){if(!stopped)failure.error(e.getMessage()==null?"Запись прервалась.":e.getMessage());}
   },"ryadom-call-mic");
   playThread=new Thread(()->{
    try{while(!stopped){byte[] data=incoming.poll(100,TimeUnit.MILLISECONDS);if(data!=null){int offset=0;while(offset<data.length&&!stopped){int n=player.write(data,offset,data.length-offset,AudioTrack.WRITE_BLOCKING);if(n<=0)throw new Exception("Звук прервался ("+n+").");offset+=n;}}}}
    catch(Exception e){if(!stopped)failure.error(e.getMessage()==null?"Воспроизведение прервалось.":e.getMessage());}
   },"ryadom-call-speaker");
   captureThread.start();playThread.start();
  }
  void receive(byte[] frame){if(!stopped){receivedBytes+=frame.length;if(!incoming.offer(frame)){incoming.poll();incoming.offer(frame);}}}
  boolean silenced(){try{if(android.os.Build.VERSION.SDK_INT>=29&&recorder!=null){AudioRecordingConfiguration c=recorder.getActiveRecordingConfiguration();return c!=null&&c.isClientSilenced();}}catch(RuntimeException ignored){}return false;}
  String status(){AcousticEchoCanceler effect=echo;boolean enabled=false;try{enabled=effect!=null&&effect.getEnabled();}catch(RuntimeException ignored){}return (muted?"Микрофон выключен":silenced()?"Android отключил микрофон. Проверьте доступ.":"Микрофон: "+Math.min(100,peak*100/12000)+"%")+" · "+(receivedBytes>0?"Звук получен":"Ожидаем звук")+"\n"+(enabled?"Эхоподавление включено":"Эхоподавление недоступно: лучше использовать наушники");}
  void close(){
   if(stopped)return;stopped=true;
   if(recorder!=null)try{recorder.stop();}catch(RuntimeException ignored){}
   if(player!=null)try{player.pause();player.flush();}catch(RuntimeException ignored){}
   if(captureThread!=null)try{captureThread.join(2000);}catch(InterruptedException e){Thread.currentThread().interrupt();}
   if(playThread!=null){playThread.interrupt();try{playThread.join(2000);}catch(InterruptedException e){Thread.currentThread().interrupt();}}
   releaseRecorder();if(player!=null){player.release();player=null;}
   if(android.os.Build.VERSION.SDK_INT>=31)manager.clearCommunicationDevice();else manager.setSpeakerphoneOn(oldSpeaker);
   manager.setMode(oldMode);manager.abandonAudioFocusRequest(focus);incoming.clear();
  }
 }
}
