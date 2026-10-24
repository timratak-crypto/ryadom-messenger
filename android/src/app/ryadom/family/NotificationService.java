package app.ryadom.family;

import android.app.*;
import android.content.*;
import android.content.pm.PackageManager;
import android.os.*;
import org.json.*;
import org.java_websocket.handshake.ServerHandshake;
import java.nio.ByteBuffer;
import java.util.concurrent.Executors;
import java.util.concurrent.ExecutorService;

// User-visible foreground connection; no microphone, camera or message-read requests.
public final class NotificationService extends Service {
 static final String MESSAGES="messages-v1",CALLS="incoming-calls-v1",CONNECTION="connection-v1";
 static final int CONNECTION_ID=1,CALL_ID=2;
 static NotificationService current;
 static boolean visible;static String visibleChat="";
 final Handler ui=new Handler(Looper.getMainLooper());
 final ExecutorService network=Executors.newSingleThreadExecutor();
 NotificationManager manager;JSONObject session;CallConnection socket;
 NotificationState tracker=new NotificationState();String account="",pendingCall="",pendingPeer="";
 boolean destroyed,loading;int generation;
 final Runnable poll=()->poll();
 final Runnable reconnect=()->connect();
 static boolean allowed(Context c){return c.getSystemService(NotificationManager.class).areNotificationsEnabled()&&(Build.VERSION.SDK_INT<33||c.checkSelfPermission("android.permission.POST_NOTIFICATIONS")==PackageManager.PERMISSION_GRANTED);}
 static boolean enabled(Context c){return c.getSharedPreferences("notifications",MODE_PRIVATE).getBoolean("enabled",true);}
 static void visible(boolean value,String chat){visible=value;visibleChat=value&&chat!=null?chat:"";NotificationService s=current;if(s!=null){if(!visibleChat.isEmpty())s.manager.cancel("message:"+visibleChat,10);if(!value&&!s.pendingCall.isEmpty())s.ring();}}
 static void start(Context c,JSONObject session){
  if(session==null||!enabled(c)||!allowed(c))return;
  if(current!=null&&current.session!=null&&current.session.optString("token").equals(session.optString("token")))return;
  try{c.startForegroundService(new Intent(c,NotificationService.class));}catch(RuntimeException ignored){}
 }
 static void stop(Context c){c.stopService(new Intent(c,NotificationService.class));c.getSystemService(NotificationManager.class).cancelAll();}
 @Override public void onCreate(){super.onCreate();current=this;manager=getSystemService(NotificationManager.class);
  NotificationChannel messages=new NotificationChannel(MESSAGES,"Новые сообщения",NotificationManager.IMPORTANCE_HIGH);messages.setDescription("Сообщения в других переписках");messages.enableVibration(true);messages.setLockscreenVisibility(Notification.VISIBILITY_PRIVATE);manager.createNotificationChannel(messages);
  NotificationChannel calls=new NotificationChannel(CALLS,"Входящие звонки",NotificationManager.IMPORTANCE_HIGH);calls.enableVibration(true);calls.setLockscreenVisibility(Notification.VISIBILITY_PRIVATE);manager.createNotificationChannel(calls);
  NotificationChannel connection=new NotificationChannel(CONNECTION,"Связь в фоне",NotificationManager.IMPORTANCE_LOW);connection.setShowBadge(false);manager.createNotificationChannel(connection);
  Intent stop=new Intent(this,NotificationService.class).setAction("stop");
  Notification n=new Notification.Builder(this,CONNECTION).setSmallIcon(R.drawable.notification).setContentTitle("Рядом · уведомления включены").setContentText("Ожидаем сообщения и звонки. Нажмите, чтобы открыть.").setContentIntent(open("",1)).setOngoing(true).setShowWhen(false).addAction(new Notification.Action.Builder(null,"Отключить",PendingIntent.getService(this,1,stop,PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE)).build()).build();
  startForeground(CONNECTION_ID,n);
 }
 @Override public int onStartCommand(Intent intent,int flags,int id){
  if(intent!=null&&"stop".equals(intent.getAction())){getSharedPreferences("notifications",MODE_PRIVATE).edit().putBoolean("enabled",false).apply();stopSelf();return START_NOT_STICKY;}
  if(intent!=null&&"decline".equals(intent.getAction())){if(account.equals(intent.getStringExtra("account"))&&pendingCall.equals(intent.getStringExtra("call"))&&socket!=null&&socket.isOpen()){socket.send(LiveCalls.json("type","hangup","id",pendingCall).toString());clearCall();}return START_NOT_STICKY;}
  if(!enabled(this)||!allowed(this)){stopSelf();return START_NOT_STICKY;}
  if(!loading){loading=true;network.execute(()->{try{JSONObject saved=MainActivity.loadSession(this);ui.post(()->{loading=false;if(destroyed)return;if(saved==null){stopSelf();return;}if(session!=null&&session.optString("token").equals(saved.optString("token")))return;
   generation++;if(socket!=null)socket.close();socket=null;clearCall();manager.cancelAll(); // Repost persistent service notification below via notify.
   session=saved;account=saved.optJSONObject("user").optString("id");tracker=new NotificationState();
   try{JSONObject stored=new JSONObject(getSharedPreferences("notifications",MODE_PRIVATE).getString("counts:"+account,"{}"));java.util.Iterator<String> keys=stored.keys();while(keys.hasNext()){String key=keys.next();tracker.counts.put(key,stored.optInt(key));}}catch(Exception ignored){}
   // cancelAll must not remove the foreground-service notification.
   manager.notify(CONNECTION_ID,new Notification.Builder(this,CONNECTION).setSmallIcon(R.drawable.notification).setContentTitle("Рядом · уведомления включены").setContentText("Сообщения и звонки принимаются в фоне").setContentIntent(open("",1)).setOngoing(true).setShowWhen(false).addAction(new Notification.Action.Builder(null,"Отключить",PendingIntent.getService(this,1,new Intent(this,NotificationService.class).setAction("stop"),PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE)).build()).build());
   ui.removeCallbacks(poll);ui.removeCallbacks(reconnect);connect();poll();
  });}catch(Exception e){ui.post(()->{loading=false;if(!destroyed)stopSelf();});}});}
  return START_NOT_STICKY;
 }
 PendingIntent open(String chat,int code){Intent i=new Intent(this,MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK|Intent.FLAG_ACTIVITY_SINGLE_TOP|Intent.FLAG_ACTIVITY_CLEAR_TOP).putExtra("notificationAccount",account).putExtra("notificationChat",chat);return PendingIntent.getActivity(this,code,i,PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);}
 void poll(){if(destroyed||session==null)return;final JSONObject target=session;final int attempt=generation;network.execute(()->{try{JSONObject result=MainActivity.request(target,null,"");ui.post(()->{if(destroyed||attempt!=generation)return;JSONArray chats=result.optJSONArray("chats");if(chats==null)return;int sent=0;
   for(int i=0;i<chats.length();i++){JSONObject c=chats.optJSONObject(i);String chat=c.optString("id");int unread=c.optInt("unread");boolean displayed=visible&&visibleChat.equals(chat);
    boolean notify=tracker.observe(chat,unread,displayed);if(unread==0||displayed)manager.cancel("message:"+chat,10);
    if(notify&&sent++<5&&allowed(this)){Notification n=new Notification.Builder(this,MESSAGES).setSmallIcon(R.drawable.notification).setContentTitle("@"+c.optString("username")).setContentText(unread==1?"Новое сообщение":"Новых сообщений: "+unread).setCategory(Notification.CATEGORY_MESSAGE).setVisibility(Notification.VISIBILITY_PRIVATE).setAutoCancel(true).setContentIntent(open(chat,chat.hashCode())).build();manager.notify("message:"+chat,10,n);}
   }
   getSharedPreferences("notifications",MODE_PRIVATE).edit().putString("counts:"+account,new JSONObject(tracker.counts).toString()).apply();
  });}catch(Exception e){if(e instanceof MainActivity.ApiError&&((MainActivity.ApiError)e).code==401)ui.post(()->{if(!destroyed&&attempt==generation)stopSelf();});}
  finally{ui.post(()->{if(!destroyed&&attempt==generation)ui.postDelayed(poll,10000);});}
 });}
 void connect(){if(destroyed||session==null||socket!=null)return;final int attempt=generation;
  CallConnection ws=new CallConnection(session.optString("token")){
   public void onOpen(ServerHandshake h){}
   public void onMessage(ByteBuffer ignored){} // A watcher never accepts or receives conversation audio.
   public void onMessage(String raw){ui.post(()->{if(destroyed||socket!=this||attempt!=generation)return;try{JSONObject e=new JSONObject(raw);String type=e.optString("type");if(type.equals("incoming")){pendingCall=e.optString("id");pendingPeer=e.optString("username");if(!visible)ring();}else if(type.equals("ended")&&pendingCall.equals(e.optString("id"))){boolean missed=e.optString("reason").equals("unanswered")||e.optString("reason").equals("cancelled");String peer=pendingPeer;clearCall();if(missed&&!visible)manager.notify(3,new Notification.Builder(NotificationService.this,MESSAGES).setSmallIcon(R.drawable.notification).setContentTitle("Пропущенный звонок").setContentText("@"+peer).setVisibility(Notification.VISIBILITY_PRIVATE).setAutoCancel(true).setContentIntent(open("",3)).build());}}catch(Exception ignored){}});}
   public void onClose(int code,String reason,boolean remote){ui.post(()->{if(destroyed||socket!=this)return;socket=null;clearCall();ui.removeCallbacks(reconnect);ui.postDelayed(reconnect,5000);});}
   public void onError(Exception error){}
  };socket=ws;ws.connect();
 }
 void ring(){if(pendingCall.isEmpty()||!allowed(this))return;Intent decline=new Intent(this,NotificationService.class).setAction("decline").putExtra("account",account).putExtra("call",pendingCall);
  Notification n=new Notification.Builder(this,CALLS).setSmallIcon(R.drawable.notification).setContentTitle("Входящий звонок · @"+pendingPeer).setContentText("Нажмите, чтобы открыть и ответить").setCategory(Notification.CATEGORY_CALL).setVisibility(Notification.VISIBILITY_PRIVATE).setOngoing(true).setTimeoutAfter(35000).setOnlyAlertOnce(true).setContentIntent(open("",2)).addAction(new Notification.Action.Builder(null,"Открыть",open("",2)).build()).addAction(new Notification.Action.Builder(null,"Отклонить",PendingIntent.getService(this,2,decline,PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE)).build()).build();manager.notify(CALL_ID,n);
 }
 void clearCall(){pendingCall="";pendingPeer="";manager.cancel(CALL_ID);}
 @Override public void onDestroy(){destroyed=true;generation++;ui.removeCallbacksAndMessages(null);if(socket!=null)socket.close();socket=null;network.shutdownNow();manager.cancelAll();if(current==this)current=null;super.onDestroy();}
 @Override public IBinder onBind(Intent intent){return null;}
}
