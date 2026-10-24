package app.ryadom.family;
import android.app.Activity;
import android.app.AlertDialog;
import android.content.ClipData;
import android.content.Intent;
import android.net.Uri;
import android.provider.MediaStore;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Matrix;
import android.media.ExifInterface;
import android.widget.ImageView;
import android.util.LruCache;
import android.animation.ValueAnimator;
import java.io.File;
import java.io.FileOutputStream;
import java.util.Arrays;
import android.content.Context;
import android.content.SharedPreferences;
import android.content.res.ColorStateList;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.text.InputFilter;
import android.text.InputType;
import android.util.Base64;
import android.view.Gravity;
import android.view.View;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import android.widget.Toast;
import org.json.JSONArray;
import org.json.JSONObject;
import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.net.URLEncoder;
import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.HashMap;
import java.util.Locale;
import java.util.UUID;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

public class MainActivity extends Activity {
 private static final int PINE=Ui.ACCENT,PAPER=Ui.PAPER,INK=Ui.INK,MUTED=Ui.MUTED;
 private static final String SERVER="https://ryadom-family-chat.onrender.com",KEY_ALIAS="ryadom.account.v2";
 private final Handler ui=new Handler(Looper.getMainLooper());
 private final ExecutorService images=Executors.newFixedThreadPool(2);
 private final ExecutorService network=Executors.newSingleThreadExecutor();
 private final ArrayList<JSONObject> messages=new ArrayList<>();
 private final HashMap<String,String> drafts=new HashMap<>();
 private final HashMap<String,byte[]> photoDrafts=new HashMap<>();
 private final HashMap<Long,TextView> receipts=new HashMap<>();
 private final LruCache<String,Bitmap> photoCache=new LruCache<String,Bitmap>(16*1024*1024){protected int sizeOf(String key,Bitmap value){return value.getByteCount();}};
 private byte[] photo,retryPhoto,voice,retryVoice;
 private final HashMap<String,byte[]> voiceDrafts=new HashMap<>();
 private VoiceAudio audio;
 private LiveCalls calls;
 private boolean recording;
 private Button micButton,playingButton;
 private TextView recordingLabel;
 private int playbackGeneration;
 private String permissionChat="";
 private JSONObject permissionSession;
 private boolean startAfterPermission;
 private final Runnable recordingClock=new Runnable(){public void run(){if(recording){showAttachment();ui.postDelayed(this,100);}}};
 private LinearLayout attachment;
 private String pickerChat="";
 private Uri deferredPhoto;
 private static final int GALLERY=41,CAMERA=42;
 private JSONArray chats=new JSONArray(),users=new JSONArray();
 private JSONObject session,selected;
 private LinearLayout root,chatList,searchResults,messageList;
 private ScrollView messageScroll;
 private TextView status,title;
 private EditText username,password,confirm,query,draft;
 private Button authButton,searchButton,sendButton;
 private boolean active,busy,polling,register,sessionLoaded;
 private long cursor,peerRead;
 private String inboxAppearance="",messageDay="";
 private String searchText="",retryText="",retryId="",retryChat="";
 private final Runnable ticker=new Runnable(){public void run(){if(active){refresh();ui.postDelayed(this,3000);}}};
 static class ApiError extends Exception {final int code;ApiError(int c,String m){super(m);code=c;}}
 private static JSONObject json(Object... pairs){JSONObject r=new JSONObject();try{for(int i=0;i<pairs.length;i+=2)r.put((String)pairs[i],pairs[i+1]);}catch(Exception e){throw new IllegalArgumentException(e);}return r;}
 @Override public void onCreate(Bundle state){
  super.onCreate(state);audio=new VoiceAudio(this,ui);calls=new LiveCalls(this,ui,new LiveCalls.Host(){public void notice(String text){if(status!=null)status.setText(text);}public void stopVoice(){stopAudio();}public boolean recording(){return recording;}});if(state!=null){pickerChat=state.getString("pickerChat","");String chat=state.getString("selected");try{if(chat!=null)selected=new JSONObject(chat);}catch(Exception ignored){}}
  busy=true;screen();network.execute(()->{
   try{JSONObject saved=loadSession(this);ui.post(()->{if(isDestroyed())return;sessionLoaded=true;session=saved;if(session==null)selected=null;busy=false;screen();consumePhoto();refresh();});}
   catch(Exception e){ui.post(()->{if(isDestroyed())return;busy=false;screen();error(e,true);});}
  });
 }
 @Override protected void onResume(){super.onResume();active=true;ui.removeCallbacks(ticker);ui.post(ticker);resumeVoicePermission();syncCalls();}
 @Override protected void onPause(){NotificationService.visible(false,null);active=false;ui.removeCallbacks(ticker);if(recording)audio.stopRecording(true);stopAudio();super.onPause();}
 @Override protected void onStop(){if(calls!=null)calls.sync(session==null?null:session.optString("token"),false);super.onStop();}
 @Override protected void onDestroy(){calls.destroy();audio.close();ui.removeCallbacksAndMessages(null);network.shutdownNow();images.shutdownNow();super.onDestroy();}
 @Override public void onBackPressed(){if(recording){audio.stopRecording(true);return;}if(selected!=null&&!busy){select(null);refresh();}else super.onBackPressed();}
 @Override protected void onSaveInstanceState(Bundle state){super.onSaveInstanceState(state);state.putString("pickerChat",pickerChat);if(selected!=null)state.putString("selected",selected.toString());}
 private int dp(float x){return (int)(getResources().getDisplayMetrics().density*x+.5f);}
 private LinearLayout column(){LinearLayout l=new LinearLayout(this);l.setOrientation(LinearLayout.VERTICAL);return l;}
 private LinearLayout.LayoutParams lp(int w,int h){return new LinearLayout.LayoutParams(w,h);}
 private TextView text(String v,int size,int color){TextView t=new TextView(this);t.setText(v);t.setTextSize(size);t.setTextColor(color);return t;}
 private GradientDrawable bg(int color,int radius){GradientDrawable g=new GradientDrawable();g.setColor(color);g.setCornerRadius(dp(radius));return g;}
 private void gap(LinearLayout p,int h){p.addView(new View(this),lp(1,dp(h)));}
 private Button button(String label,View.OnClickListener action){return Ui.button(this,label,action,false);}
 private Button quiet(String label,View.OnClickListener action){return Ui.button(this,label,action,true);}
 private TextView heading(String label,int size){TextView t=text(label,size,INK);t.setTypeface(Typeface.create("sans-serif-medium",0));return t;}
 private View contact(JSONObject person,boolean search){
  String name=person.optString("username");LinearLayout row=new LinearLayout(this);row.setGravity(Gravity.CENTER_VERTICAL);row.setPadding(dp(14),dp(15),dp(14),dp(15));row.setBackground(new android.graphics.drawable.RippleDrawable(ColorStateList.valueOf(0x2248a880),bg(Color.WHITE,20),null));row.setClickable(true);row.setFocusable(true);row.addView(Ui.avatar(this,name,48));
  LinearLayout copy=column();copy.setPadding(dp(14),0,dp(8),0);TextView who=heading("@"+name,16);who.setSingleLine();who.setEllipsize(android.text.TextUtils.TruncateAt.END);copy.addView(who);String preview=search?"Открыть профиль":person.optString("lastText");TextView sub=text(preview.isEmpty()?"Напишите первое сообщение":preview,13,MUTED);sub.setSingleLine();sub.setEllipsize(android.text.TextUtils.TruncateAt.END);copy.addView(sub);row.addView(copy,new LinearLayout.LayoutParams(0,-2,1));
  int unread=person.optInt("unread");if(unread>0){TextView badge=text(unread>99?"99+":String.valueOf(unread),11,Color.WHITE);badge.setGravity(Gravity.CENTER);badge.setPadding(dp(7),dp(4),dp(7),dp(4));badge.setBackground(bg(PINE,10));row.addView(badge);}
  row.setOnClickListener(v->{if(busy)return;if(search)new AlertDialog.Builder(this).setTitle("@"+name).setMessage("Начните ваш разговор").setPositiveButton("Написать",(d,w)->open(person)).setNegativeButton("Закрыть",null).show();else{select(person);refresh();}});return row;
 }
 private EditText input(String hint,int max,boolean secret){EditText e=new EditText(this);e.setTextSize(16);e.setTextColor(INK);e.setHintTextColor(MUTED);e.setHint(hint);e.setPadding(dp(12),dp(10),dp(12),dp(10));GradientDrawable surface=bg(Color.WHITE,16);surface.setStroke(dp(1),0xffdce8e2);e.setBackground(surface);e.setMinimumHeight(dp(48));e.setFilters(new InputFilter[]{new InputFilter.LengthFilter(max)});e.setInputType(InputType.TYPE_CLASS_TEXT|(secret?InputType.TYPE_TEXT_VARIATION_PASSWORD:InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS));e.setSingleLine(true);return e;}
 private void syncCalls(){if(calls!=null)calls.sync(session==null?null:session.optString("token"),active);NotificationService.visible(active,selected==null?null:selected.optString("id"));syncNotifications();}
 private void syncNotifications(){
  if(!active||!sessionLoaded||session==null)return;
  if(android.os.Build.VERSION.SDK_INT>=33&&checkSelfPermission("android.permission.POST_NOTIFICATIONS")!=android.content.pm.PackageManager.PERMISSION_GRANTED){if(!getSharedPreferences("notifications",MODE_PRIVATE).getBoolean("asked",false)){getSharedPreferences("notifications",MODE_PRIVATE).edit().putBoolean("asked",true).apply();requestPermissions(new String[]{"android.permission.POST_NOTIFICATIONS"},45);}return;}
  NotificationService.start(this,session);
 }
 private void notificationSettings(){
  boolean on=NotificationService.enabled(this)&&NotificationService.allowed(this);
  new AlertDialog.Builder(this).setTitle("Уведомления").setMessage("Сообщения и звонки приходят, пока работает фоновая служба «Рядом». Android показывает её значок постоянно. После принудительной остановки или перезагрузки откройте приложение снова. Энергосбережение может задерживать уведомления.").setPositiveButton(on?"Отключить":"Включить",(d,w)->{getSharedPreferences("notifications",MODE_PRIVATE).edit().putBoolean("enabled",!on).apply();if(on){NotificationService.stop(this);}else if(!NotificationService.allowed(this)){if(android.os.Build.VERSION.SDK_INT>=33&&checkSelfPermission("android.permission.POST_NOTIFICATIONS")!=android.content.pm.PackageManager.PERMISSION_GRANTED)requestPermissions(new String[]{"android.permission.POST_NOTIFICATIONS"},45);else startActivity(new Intent("android.settings.APP_NOTIFICATION_SETTINGS").putExtra("android.provider.extra.APP_PACKAGE",getPackageName()));}else syncNotifications();}).setNegativeButton("Закрыть",null).show();
 }
 @Override protected void onNewIntent(Intent intent){super.onNewIntent(intent);setIntent(intent);consumeNotification();}
 private void consumeNotification(){
  if(session==null||busy||recording)return;String account=getIntent().getStringExtra("notificationAccount"),id=getIntent().getStringExtra("notificationChat");if(id==null||id.isEmpty())return;
  if(!session.optJSONObject("user").optString("id").equals(account)){getIntent().removeExtra("notificationChat");return;}
  for(int i=0;i<chats.length();i++){JSONObject c=chats.optJSONObject(i);if(c.optString("id").equals(id)){getIntent().removeExtra("notificationChat");select(c);ui.post(()->refresh());return;}}
 }
 private void screen(){
  syncCalls();
  inboxAppearance="";messageDay="";chatList=null;searchResults=null;messageList=null;receipts.clear();authButton=null;searchButton=null;sendButton=null;micButton=null;attachment=null;recordingLabel=null;
  root=column();root.setBackgroundColor(PAPER);root.setPadding(dp(18),dp(12),dp(18),dp(8));
  root.setOnApplyWindowInsetsListener((v,i)->{v.setPadding(dp(18)+i.getSystemWindowInsetLeft(),dp(12)+i.getSystemWindowInsetTop(),dp(18)+i.getSystemWindowInsetRight(),dp(8)+i.getSystemWindowInsetBottom());return i;});
  setContentView(root);root.requestApplyInsets();title=heading("Рядом",30);if(selected==null){root.addView(title);TextView tagline=text("Ближе, чем кажется",12,MUTED);root.addView(tagline);gap(root,20);}
  status=text("",12,MUTED);
  if(session==null)authScreen();else if(selected==null)inboxScreen();else conversationScreen();
  Ui.enter(root,12);
  status.setPadding(dp(4),dp(6),dp(4),0);root.addView(status);updateButtons();
 }
 private void authScreen(){
  ScrollView scroll=new ScrollView(this);scroll.setFillViewport(true);scroll.setVerticalScrollBarEnabled(false);LinearLayout form=column();scroll.addView(form);root.addView(scroll,new LinearLayout.LayoutParams(-1,0,1));
  LinearLayout hero=column();hero.setPadding(dp(24),dp(26),dp(24),dp(26));hero.setBackground(Ui.gradient(this,0xff103f34,0xff23745a,26));TextView big=heading("Хорошо, когда\nблизкие рядом.",30);big.setTextColor(Color.WHITE);hero.addView(big);gap(hero,12);hero.addView(text("Пара слов. Знакомый голос.\nМоменты, которыми хочется делиться.",14,0xffcbe4d8));form.addView(hero);gap(form,26);
  form.addView(heading(register?"Создать аккаунт":"С возвращением",25));gap(form,18);form.addView(heading("Юзернейм",13));gap(form,6);username=input("username",25,false);form.addView(username);gap(form,5);form.addView(text("3–24 символа: a–z, 0–9, _",11,MUTED));gap(form,18);
  password=input("Пароль — от 8 символов",128,true);form.addView(password);gap(form,12);confirm=input("Повторите пароль",128,true);if(register){form.addView(confirm);gap(form,12);}
  authButton=button(register?"Зарегистрироваться":"Войти",v->authenticate());form.addView(authButton,lp(-1,dp(50)));gap(form,10);form.addView(quiet(register?"Уже есть аккаунт? Войти":"Нет аккаунта? Регистрация",v->{if(busy)return;register=!register;screen();}),lp(-1,dp(48)));gap(form,18);form.addView(text("Сохраните пароль в надёжном месте. Восстановление через почту пока недоступно.",11,MUTED));gap(form,12);
 }
 private void inboxScreen(){
  LinearLayout me=new LinearLayout(this);me.setGravity(Gravity.CENTER_VERTICAL);me.addView(Ui.avatar(this,session.optJSONObject("user").optString("username"),42));TextView name=heading("@"+session.optJSONObject("user").optString("username"),15);name.setPadding(dp(12),0,0,0);me.addView(name,new LinearLayout.LayoutParams(0,-2,1));Button settings=quiet("•••",v->{android.widget.PopupMenu menu=new android.widget.PopupMenu(this,v);menu.getMenu().add("Уведомления");menu.getMenu().add("Выйти");menu.setOnMenuItemClickListener(item->{if(item.getTitle().equals("Выйти"))logout();else notificationSettings();return true;});menu.show();});settings.setContentDescription("Настройки аккаунта");me.addView(settings,lp(dp(48),dp(48)));root.addView(me);gap(root,20);
  LinearLayout search=new LinearLayout(this);search.setGravity(Gravity.CENTER_VERTICAL);query=input("Найти по @username",25,false);query.setText(searchText);search.addView(query,new LinearLayout.LayoutParams(0,dp(50),1));searchButton=Ui.icon(this,"search","Найти человека",v->search(),false);LinearLayout.LayoutParams searchSize=lp(dp(48),dp(48));searchSize.leftMargin=dp(8);search.addView(searchButton,searchSize);root.addView(search);gap(root,18);
  ScrollView scroll=new ScrollView(this);scroll.setVerticalScrollBarEnabled(false);LinearLayout content=column();scroll.addView(content);root.addView(scroll,new LinearLayout.LayoutParams(-1,0,1));searchResults=column();content.addView(searchResults);content.addView(heading("Ваши разговоры",23));gap(content,12);chatList=column();content.addView(chatList);renderInbox();renderSearch();
 }
 private void renderSearch(){if(searchResults==null)return;searchResults.removeAllViews();for(int i=0;i<users.length();i++){searchResults.addView(contact(users.optJSONObject(i),true),lp(-1,-2));gap(searchResults,8);}if(users.length()>0)gap(searchResults,12);}
 private void renderInbox(){
  if(chatList==null)return;String signature=chats.toString();if(signature.equals(inboxAppearance))return;inboxAppearance=signature;chatList.removeAllViews();
  if(chats.length()==0){gap(chatList,30);chatList.addView(heading("Пока тихо",22));gap(chatList,10);chatList.addView(text("Найдите близкого человека\nи начните первый разговор.",15,MUTED));}
  for(int i=0;i<chats.length();i++){chatList.addView(contact(chats.optJSONObject(i),false),lp(-1,-2));gap(chatList,8);}
 }
 private void conversationScreen(){
  LinearLayout header=new LinearLayout(this);header.setGravity(Gravity.CENTER_VERTICAL);Button back=Ui.icon(this,"back","Назад к сообщениям",v->{if(busy||recording)return;select(null);refresh();},true);header.addView(back,lp(dp(44),dp(48)));TextView avatar=Ui.avatar(this,selected.optString("username"),40);LinearLayout.LayoutParams avatarSize=lp(dp(40),dp(40));avatarSize.setMargins(dp(10),0,dp(12),0);header.addView(avatar,avatarSize);LinearLayout names=column();TextView peer=heading("@"+selected.optString("username"),17);peer.setSingleLine();peer.setEllipsize(android.text.TextUtils.TruncateAt.END);names.addView(peer);names.addView(text("Личная переписка",11,MUTED));header.addView(names,new LinearLayout.LayoutParams(0,-2,1));header.addView(Ui.icon(this,"phone","Позвонить",v->{if(!busy&&!recording&&selected!=null)calls.dial(selected.optString("username"));},true),lp(dp(48),dp(48)));root.addView(header);gap(root,12);
  messageScroll=new ScrollView(this);messageScroll.setVerticalScrollBarEnabled(false);messageList=column();messageList.setPadding(dp(2),dp(8),dp(2),dp(8));messageScroll.addView(messageList);root.addView(messageScroll,new LinearLayout.LayoutParams(-1,0,1));attachment=column();root.addView(attachment);showAttachment();
  LinearLayout composer=new LinearLayout(this);composer.setGravity(Gravity.BOTTOM);composer.setPadding(dp(4),dp(5),dp(4),dp(5));composer.setBackground(bg(Color.WHITE,22));Button add=Ui.icon(this,"plus","Прикрепить фотографию",v->attach(),true);composer.addView(add,lp(dp(44),dp(48)));draft=input("Сообщение…",4000,false);draft.setBackgroundColor(Color.TRANSPARENT);draft.setPadding(dp(10),dp(12),dp(6),dp(12));draft.setSingleLine(false);draft.setInputType(InputType.TYPE_CLASS_TEXT|InputType.TYPE_TEXT_FLAG_MULTI_LINE|InputType.TYPE_TEXT_FLAG_CAP_SENTENCES);draft.setMaxLines(5);String saved=drafts.get(selected.optString("id"));draft.setText(saved==null?"":saved);composer.addView(draft,new LinearLayout.LayoutParams(0,-2,1));
  micButton=Ui.icon(this,"mic","Записать голосовое сообщение",v->recordVoice(),true);composer.addView(micButton,lp(dp(44),dp(48)));sendButton=Ui.icon(this,"send","Отправить сообщение",v->send(),false);LinearLayout.LayoutParams sendSize=lp(dp(48),dp(48));sendSize.leftMargin=dp(4);composer.addView(sendButton,sendSize);root.addView(composer);renderMessages();
 }
 private void updateButtons(){if(authButton!=null)authButton.setEnabled(!busy);if(searchButton!=null)searchButton.setEnabled(!busy);if(sendButton!=null)sendButton.setEnabled(!busy&&!recording);if(micButton!=null){micButton.setEnabled(!busy&&photo==null&&voice==null);micButton.setText(recording?"■":"");micButton.setCompoundDrawablesWithIntrinsicBounds(recording?null:new Ui.Icon(this,"mic",PINE),null,null,null);micButton.setContentDescription(recording?"Остановить запись":"Записать голосовое сообщение");}}
 private void clear(){NotificationService.stop(this);calls.disconnect();audio.stopRecording(false);stopAudio();voice=null;retryVoice=null;voiceDrafts.clear();session=null;selected=null;photo=null;retryPhoto=null;photoDrafts.clear();photoCache.evictAll();messages.clear();chats=new JSONArray();users=new JSONArray();drafts.clear();searchText="";cursor=0;screen();}
 private void error(Exception e,boolean auth){
  if(!auth&&e instanceof ApiError&&((ApiError)e).code==401){getSharedPreferences("account-v2",Context.MODE_PRIVATE).edit().clear().commit();clear();}
  status.setText(e instanceof java.io.IOException?"Нет связи. Первый запуск сервера может занять минуту. Повторите попытку.":e.getMessage());status.setTextColor(Color.rgb(178,72,57));
 }
 private void authenticate(){
  if(busy)return;String name=username.getText().toString().trim(),pass=password.getText().toString();
  if(register&&!pass.equals(confirm.getText().toString())){status.setText("Пароли не совпадают.");return;}
  final boolean create=register;busy=true;updateButtons();status.setText("Подключаемся… Первый запуск может занять минуту.");
  network.execute(()->{try{JSONObject next=request(null,json("action",create?"register":"login","username",name,"password",pass),"");saveSession(next);ui.post(()->{if(isDestroyed())return;password.setText("");confirm.setText("");sessionLoaded=true;session=next;busy=false;screen();refresh();});}
   catch(Exception e){ui.post(()->{if(isDestroyed())return;busy=false;password.setText("");confirm.setText("");updateButtons();error(e,true);});}});
 }
 private void logout(){
  if(busy||session==null)return;final JSONObject target=session;
  LinearLayout form=column();form.setPadding(dp(24),dp(12),dp(24),dp(12));form.addView(text("Пароль от @"+target.optJSONObject("user").optString("username"),16,INK));gap(form,12);
  EditText confirmation=input("Пароль",128,true);form.addView(confirmation);
  TextView feedback=text("",14,Color.rgb(178,72,57));form.addView(feedback);
  AlertDialog dialog=new AlertDialog.Builder(this).setTitle("Подтвердите выход").setView(form).setPositiveButton("Выйти",null).setNegativeButton("Отмена",null).create();
  dialog.setOnDismissListener(d->confirmation.setText(""));dialog.show();
  dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(v->{
   if(busy)return;final String value=confirmation.getText().toString();if(value.isEmpty()){confirmation.setError("Введите пароль");return;}
   busy=true;updateButtons();dialog.setCancelable(false);dialog.getButton(AlertDialog.BUTTON_POSITIVE).setEnabled(false);dialog.getButton(AlertDialog.BUTTON_NEGATIVE).setEnabled(false);feedback.setText("Проверяем пароль…");
   network.execute(()->{try{
    request(target,json("action","logout","password",value),"");getSharedPreferences("account-v2",Context.MODE_PRIVATE).edit().clear().commit();
    ui.post(()->{if(isDestroyed())return;busy=false;dialog.dismiss();clear();});
   }catch(Exception e){ui.post(()->{if(isDestroyed())return;busy=false;updateButtons();confirmation.setText("");dialog.setCancelable(true);dialog.getButton(AlertDialog.BUTTON_POSITIVE).setEnabled(true);dialog.getButton(AlertDialog.BUTTON_NEGATIVE).setEnabled(true);feedback.setText(e instanceof java.io.IOException?"Нет связи с сервером. Вы остались в аккаунте.":e.getMessage());error(e,false);if(session==null)dialog.dismiss();});}});
  });
 }
 private void search(){
  if(busy||session==null)return;searchText=query.getText().toString();final String q=searchText;final JSONObject target=session;busy=true;updateButtons();
  network.execute(()->{try{JSONObject r=request(target,null,"?view=search&q="+URLEncoder.encode(q,"UTF-8"));ui.post(()->{if(isDestroyed()||session!=target)return;busy=false;users=r.optJSONArray("users");renderSearch();updateButtons();status.setText(users.length()==0?"Пользователь не найден. Проверьте юзернейм.":"Выберите профиль, чтобы написать.");});}catch(Exception e){ui.post(()->{if(isDestroyed()||session!=target)return;busy=false;updateButtons();error(e,false);});}});
 }
 private void open(JSONObject profile){
  if(busy||session==null)return;final JSONObject target=session;busy=true;updateButtons();network.execute(()->{try{JSONObject r=request(target,json("action","open","username",profile.optString("username")),"");JSONObject c=json("id",r.getString("conversation"),"peerId",profile.getString("id"),"username",profile.getString("username"));
   ui.post(()->{if(isDestroyed()||session!=target)return;busy=false;select(c);refresh();});}catch(Exception e){ui.post(()->{if(isDestroyed()||session!=target)return;busy=false;updateButtons();error(e,false);});}});
 }
 private void select(JSONObject c){if(recording)return;stopAudio();if(selected!=null)voiceDrafts.put(selected.optString("id"),voice);voice=c==null?null:voiceDrafts.get(c.optString("id"));retryVoice=null;if(selected!=null&&draft!=null)drafts.put(selected.optString("id"),draft.getText().toString());if(selected==null&&query!=null)searchText=query.getText().toString();if(selected!=null)photoDrafts.put(selected.optString("id"),photo);selected=c;photo=c==null?null:photoDrafts.get(c.optString("id"));retryPhoto=null;messages.clear();cursor=0;peerRead=0;retryText="";retryId="";retryChat="";screen();}
 private void refresh(){
  if(!active||session==null||polling)return;polling=true;final JSONObject target=session,chat=selected;final long after=cursor;
  network.execute(()->{try{
   JSONObject inbox=request(target,null,"");JSONObject feed=chat==null?null:request(target,null,"?view=messages&conversation="+chat.optString("id")+"&after="+after);
   ui.post(()->{polling=false;if(isDestroyed()||session!=target)return;chats=inbox.optJSONArray("chats");renderInbox();consumeNotification();
    if(chat!=null&&selected==chat&&feed!=null){JSONArray incoming=feed.optJSONArray("messages");boolean changed=peerRead!=feed.optLong("peerRead");peerRead=feed.optLong("peerRead");for(int i=0;i<incoming.length();i++){JSONObject m=incoming.optJSONObject(i);if(m.optLong("seq")>cursor){messages.add(m);cursor=m.optLong("seq");changed=true;}}if(changed)renderMessages();}
    status.setText("Соединение установлено");status.setTextColor(MUTED);
   });
  }catch(Exception e){ui.post(()->{polling=false;if(!isDestroyed()&&session==target)error(e,false);});}});
 }
 private void renderMessages(){
  if(messageList==null||session==null)return;int old=messageScroll.getScrollY();boolean bottom=messageList.getHeight()-old-messageScroll.getHeight()<dp(90);boolean animate=!receipts.isEmpty();if(receipts.isEmpty()){messageList.removeAllViews();messageDay="";}
  if(messages.isEmpty()){TextView empty=text("Скажите «привет»\nИ начнётся ваш разговор",20,MUTED);empty.setGravity(Gravity.CENTER);empty.setPadding(0,dp(28),0,0);messageList.addView(empty);}
  for(JSONObject m:messages){
   boolean own=m.optString("sender").equals(session.optJSONObject("user").optString("id"));int ink=own?Color.WHITE:INK;
   String time=new SimpleDateFormat("HH:mm",Locale.getDefault()).format(new Date(m.optLong("at")));
   String stamp=time+(own?(peerRead>=m.optLong("seq")?" · Прочитано":" · Отправлено"):"");
   if(receipts.containsKey(m.optLong("seq"))){receipts.get(m.optLong("seq")).setText(stamp);continue;}
   String day=new SimpleDateFormat("d MMMM",Locale.getDefault()).format(new Date(m.optLong("at")));if(!day.equals(messageDay)){messageDay=day;TextView divider=text(day,11,MUTED);divider.setGravity(Gravity.CENTER);divider.setPadding(0,dp(10),0,dp(16));messageList.addView(divider);}
   LinearLayout row=new LinearLayout(this);row.setGravity(own?Gravity.RIGHT:Gravity.LEFT);LinearLayout bubble=column();bubble.setPadding(dp(14),dp(10),dp(14),dp(6));bubble.setBackground(own?Ui.gradient(this,PINE,0xff258263,20):bg(Color.WHITE,20));
   if(!m.isNull("photo")&&!m.optString("photo").isEmpty())addPhoto(bubble,m.optString("photo"),selected.optString("id"));
   if(!m.isNull("voice")&&!m.optString("voice").isEmpty())addVoice(bubble,m.optString("voice"),selected.optString("id"),m.optInt("duration"));
   String body=m.optString("text");TextView content=text(body,16,ink);content.setTextIsSelectable(true);content.setMaxWidth(Math.max(dp(160),getResources().getDisplayMetrics().widthPixels-dp(110)));if(!body.isEmpty())bubble.addView(content);
   TextView receipt=text(stamp,10,ink);receipt.setAlpha(.7f);receipt.setGravity(Gravity.RIGHT);receipt.setPadding(0,dp(7),0,dp(3));bubble.addView(receipt);receipts.put(m.optLong("seq"),receipt);
   row.addView(bubble);LinearLayout.LayoutParams p=lp(-1,-2);p.setMargins(0,dp(6),0,dp(6));messageList.addView(row,p);if(animate&&bottom)Ui.enter(row,8);
  }messageScroll.post(()->{if(bottom)messageScroll.fullScroll(View.FOCUS_DOWN);else messageScroll.scrollTo(0,old);});
 }
 private void send(){
  if(busy||recording||session==null||selected==null)return;stopAudio();final String value=draft.getText().toString().trim();if(value.isEmpty()&&photo==null&&voice==null)return;final byte[] image=photo,clip=voice;final JSONObject target=session,chat=selected;
  if(!value.equals(retryText)||!chat.optString("id").equals(retryChat)||!Arrays.equals(image,retryPhoto)||!Arrays.equals(clip,retryVoice)){retryVoice=clip;retryText=value;retryPhoto=image;retryChat=chat.optString("id");retryId=UUID.randomUUID().toString();}
  final JSONObject body=json("action",clip!=null?"sendVoice":(image==null?"send":"sendPhoto"),"conversation",chat.optString("id"),"clientId",retryId,"text",value);if(image!=null||clip!=null)try{body.put("data",Base64.encodeToString(clip!=null?clip:image,Base64.NO_WRAP));}catch(Exception ignored){}busy=true;updateButtons();
  network.execute(()->{try{request(target,body,"");ui.post(()->{if(isDestroyed()||session!=target)return;busy=false;updateButtons();if(selected==chat){if(draft.getText().toString().trim().equals(value))draft.setText("");drafts.remove(chat.optString("id"));if(Arrays.equals(photo,image))photo=null;photoDrafts.remove(chat.optString("id"));if(Arrays.equals(voice,clip))voice=null;voiceDrafts.remove(chat.optString("id"));retryVoice=null;showAttachment();updateButtons();retryPhoto=null;retryText="";retryId="";refresh();}});}catch(Exception e){ui.post(()->{if(isDestroyed()||session!=target)return;busy=false;updateButtons();error(e,false);});}});
 }

 private void recordVoice(){
  if(calls.busy()){status.setText("Сначала завершите звонок.");return;}
  if(recording){audio.stopRecording(true);return;}if(busy||voice!=null||photo!=null||session==null||selected==null)return;
  if(checkSelfPermission(android.Manifest.permission.RECORD_AUDIO)!=android.content.pm.PackageManager.PERMISSION_GRANTED){permissionChat=selected.optString("id");permissionSession=session;requestPermissions(new String[]{android.Manifest.permission.RECORD_AUDIO},43);return;}
  final JSONObject target=session,chat=selected;stopAudio();
  try{
   recording=true;audio.record((data,error)->{if(isDestroyed())return;recording=false;ui.removeCallbacks(recordingClock);if(session==target&&selected==chat){if(data!=null)voice=data;showAttachment();if(error!=null){status.setText(error.getMessage());status.setTextColor(Color.rgb(178,72,57));}}updateButtons();});
   updateButtons();ui.post(recordingClock);
  }catch(Exception e){recording=false;updateButtons();status.setText(e.getMessage());}
 }
 @Override public void onRequestPermissionsResult(int requestCode,String[] permissions,int[] grants){
  super.onRequestPermissionsResult(requestCode,permissions,grants);if(requestCode==45){if(grants.length>0&&grants[0]==android.content.pm.PackageManager.PERMISSION_GRANTED)syncNotifications();else if(status!=null)status.setText("Уведомления отключены. Их можно включить в настройках приложения.");return;}if(requestCode==44){calls.permission(grants.length>0&&grants[0]==android.content.pm.PackageManager.PERMISSION_GRANTED);return;}if(requestCode!=43)return;
  if(grants.length>0&&grants[0]==android.content.pm.PackageManager.PERMISSION_GRANTED){startAfterPermission=true;resumeVoicePermission();syncCalls();}
  else status.setText("Разрешите микрофон в Настройках → Приложения → Рядом → Разрешения.");
 }
 private void resumeVoicePermission(){if(!active||!startAfterPermission)return;startAfterPermission=false;if(session==permissionSession&&selected!=null&&selected.optString("id").equals(permissionChat))recordVoice();}
 private void stopAudio(){playbackGeneration++;if(audio!=null)audio.stopPlayback();if(playingButton!=null){playingButton.setText((CharSequence)playingButton.getTag());playingButton.setEnabled(true);playingButton=null;}}
 private void playVoice(byte[] data,Button button){
  if(calls.busy())return;
  if(recording)return;if(playingButton==button){stopAudio();return;}stopAudio();
  final CharSequence original=button.getText();button.setTag(original);playingButton=button;button.setText("■ Остановить");
  try{audio.play(data,()->{button.setText(original);if(playingButton==button)playingButton=null;});}
  catch(Exception e){stopAudio();status.setText("Не удалось воспроизвести запись. "+e.getMessage());}
 }
 private void addVoice(LinearLayout panel,String id,String conversation,int duration){
  Button play=button("▶ Голосовое · "+VoiceAudio.time(duration/1000.0),v->{});panel.addView(play);
  play.setOnClickListener(v->{
   if(recording)return;if(playingButton==play){stopAudio();return;}stopAudio();final JSONObject target=session;final int generation=playbackGeneration;
   final CharSequence label=play.getText();play.setTag(label);playingButton=play;play.setText("Загрузка… Нажмите для отмены");
   images.execute(()->{try{
    JSONObject response=request(target,null,"?view=voice&conversation="+conversation+"&voice="+id);byte[] data=Base64.decode(response.getString("data"),Base64.DEFAULT);if(data.length>2*1024*1024)throw new Exception("Запись слишком большая.");
    ui.post(()->{if(isDestroyed()||!active||generation!=playbackGeneration||session!=target||selected==null||!selected.optString("id").equals(conversation))return;playingButton=null;play.setText(label);playVoice(data,play);});
   }catch(Exception e){ui.post(()->{if(isDestroyed()||generation!=playbackGeneration)return;stopAudio();status.setText("Не удалось загрузить запись. Нажмите ▶ ещё раз.");});}});
  });
 }

 private void showAttachment(){
  if(attachment==null)return;if(recording&&recordingLabel!=null){recordingLabel.setText("● Запись "+VoiceAudio.time(audio.recordedBytes/32000.0)+" / 1:00");return;}attachment.removeAllViews();recordingLabel=null;updateButtons();
  if(recording){LinearLayout row=new LinearLayout(this);row.setGravity(Gravity.CENTER_VERTICAL);recordingLabel=text("● Запись "+VoiceAudio.time(audio.recordedBytes/32000.0)+" / 1:00",14,Color.rgb(178,72,57));row.addView(recordingLabel,new LinearLayout.LayoutParams(0,-2,1));row.addView(button("Отмена",v->audio.stopRecording(false)));attachment.addView(row);return;}
  if(voice!=null){final byte[] clip=voice;LinearLayout row=new LinearLayout(this);Button preview=button("▶ "+VoiceAudio.time(VoiceAudio.seconds(clip))+" · Прослушать",v->{});preview.setOnClickListener(v->{if(busy)return;playVoice(clip,preview);});row.addView(preview,new LinearLayout.LayoutParams(0,-2,1));row.addView(button("×",v->{if(busy)return;stopAudio();voice=null;showAttachment();}),lp(dp(48),dp(48)));attachment.addView(row);return;}
  if(photo==null)return;
  LinearLayout row=new LinearLayout(this);row.setGravity(Gravity.CENTER_VERTICAL);ImageView preview=new ImageView(this);preview.setImageBitmap(BitmapFactory.decodeByteArray(photo,0,photo.length));preview.setScaleType(ImageView.ScaleType.FIT_CENTER);row.addView(preview,lp(dp(80),dp(70)));
  row.addView(text("Фото готово к отправке",12,MUTED),new LinearLayout.LayoutParams(0,-2,1));row.addView(button("×",v->{if(busy)return;photo=null;showAttachment();}),lp(dp(48),dp(48)));attachment.addView(row);
 }
 private void attach(){
  if(calls.busy())return;
  if(recording||voice!=null){status.setText("Сначала отправьте или удалите голосовую запись.");return;}
  if(busy||session==null||selected==null)return;
  new AlertDialog.Builder(this).setTitle("Прикрепить фотографию").setItems(new String[]{"Фото из галереи","Снять на камеру"},(dialog,which)->{
   pickerChat=selected.optString("id");
   try{
    if(which==0){Intent intent=new Intent(Intent.ACTION_OPEN_DOCUMENT);intent.setType("image/*");intent.addCategory(Intent.CATEGORY_OPENABLE);startActivityForResult(intent,GALLERY);}
    else{File file=new File(getCacheDir(),"capture.jpg");try(FileOutputStream out=new FileOutputStream(file)){out.write(new byte[0]);}
     Uri uri=Uri.parse("content://"+getPackageName()+".camera/capture.jpg");Intent intent=new Intent(MediaStore.ACTION_IMAGE_CAPTURE);intent.putExtra(MediaStore.EXTRA_OUTPUT,uri);intent.setClipData(ClipData.newRawUri("Снимок",uri));intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION|Intent.FLAG_GRANT_WRITE_URI_PERMISSION);startActivityForResult(intent,CAMERA);}
   }catch(Exception e){status.setText(which==1?"Камера недоступна. Проверьте, установлено ли приложение камеры.":"Не удалось открыть галерею.");}
  }).show();
 }
 @Override protected void onActivityResult(int requestCode,int resultCode,Intent data){
  super.onActivityResult(requestCode,resultCode,data);if(requestCode!=GALLERY&&requestCode!=CAMERA)return;
  if(resultCode!=RESULT_OK)return;
  deferredPhoto=requestCode==CAMERA?Uri.fromFile(new File(getCacheDir(),"capture.jpg")):(data==null?null:data.getData());consumePhoto();
 }
 private void consumePhoto(){
  if(deferredPhoto==null||session==null||busy)return;final Uri uri=deferredPhoto;deferredPhoto=null;
  if(selected==null||!pickerChat.equals(selected.optString("id")))return;final JSONObject chat=selected,target=session;
  busy=true;updateButtons();status.setText("Готовим фотографию…");
  images.execute(()->{try{byte[] bytes=preparePhoto(uri);ui.post(()->{if(isDestroyed()||session!=target)return;busy=false;updateButtons();if(selected==chat){photo=bytes;showAttachment();status.setText("Добавьте подпись или нажмите ↑, чтобы отправить.");}});}
   catch(Exception e){ui.post(()->{if(isDestroyed())return;busy=false;updateButtons();status.setText("Не удалось открыть фото. Выберите другой снимок или загрузите его из облака.");});}
   finally{if("file".equals(uri.getScheme()))new File(getCacheDir(),"capture.jpg").delete();}});
 }
 private byte[] preparePhoto(Uri uri)throws Exception{
  BitmapFactory.Options bounds=new BitmapFactory.Options();bounds.inJustDecodeBounds=true;try(InputStream in=getContentResolver().openInputStream(uri)){BitmapFactory.decodeStream(in,null,bounds);}
  if(bounds.outWidth<=0||bounds.outHeight<=0)throw new Exception("Неизвестный формат фото");
  BitmapFactory.Options options=new BitmapFactory.Options();options.inSampleSize=1;while(Math.max(bounds.outWidth,bounds.outHeight)/options.inSampleSize>2000)options.inSampleSize*=2;
  Bitmap bitmap;try(InputStream in=getContentResolver().openInputStream(uri)){bitmap=BitmapFactory.decodeStream(in,null,options);}if(bitmap==null)throw new Exception("Не удалось прочитать фото");
  int orientation=1;try(InputStream in=getContentResolver().openInputStream(uri)){orientation=new ExifInterface(in).getAttributeInt(ExifInterface.TAG_ORIENTATION,1);}catch(Exception ignored){}
  Matrix matrix=new Matrix();switch(orientation){case 2:matrix.setScale(-1,1);break;case 3:matrix.setRotate(180);break;case 4:matrix.setScale(1,-1);break;case 5:matrix.setRotate(90);matrix.postScale(-1,1);break;case 6:matrix.setRotate(90);break;case 7:matrix.setRotate(-90);matrix.postScale(-1,1);break;case 8:matrix.setRotate(-90);break;}
  Bitmap oriented=Bitmap.createBitmap(bitmap,0,0,bitmap.getWidth(),bitmap.getHeight(),matrix,true);if(oriented!=bitmap)bitmap.recycle();bitmap=oriented;
  float scale=Math.min(1f,1600f/Math.max(bitmap.getWidth(),bitmap.getHeight()));Bitmap scaled=Bitmap.createScaledBitmap(bitmap,Math.max(1,Math.round(bitmap.getWidth()*scale)),Math.max(1,Math.round(bitmap.getHeight()*scale)),true);if(scaled!=bitmap)bitmap.recycle();
  try{for(int quality:new int[]{82,65,45,25}){ByteArrayOutputStream out=new ByteArrayOutputStream();scaled.compress(Bitmap.CompressFormat.JPEG,quality,out);if(out.size()<=1024*1024)return out.toByteArray();}throw new Exception("Слишком большое фото");}finally{scaled.recycle();}
 }
 private void addPhoto(LinearLayout bubble,String id,String conversation){
  ImageView view=new ImageView(this);view.setContentDescription("Открыть фотографию");view.setScaleType(ImageView.ScaleType.FIT_CENTER);view.setAdjustViewBounds(true);int width=Math.min(dp(260),getResources().getDisplayMetrics().widthPixels-dp(110));bubble.addView(view,lp(width,dp(180)));
  Button retry=button("Загрузить фото",v->{});bubble.addView(retry);final JSONObject target=session;
  Runnable load=()->{Bitmap cached=photoCache.get(id);if(cached!=null){view.setImageBitmap(cached);retry.setVisibility(View.GONE);return;}
   retry.setEnabled(false);retry.setText("Загрузка фото…");images.execute(()->{try{JSONObject response=request(target,null,"?view=photo&conversation="+conversation+"&photo="+id);byte[] bytes=Base64.decode(response.getString("data"),Base64.DEFAULT);BitmapFactory.Options options=new BitmapFactory.Options();options.inSampleSize=4;Bitmap bitmap=BitmapFactory.decodeByteArray(bytes,0,bytes.length,options);if(bitmap==null)throw new Exception("Фото не прочитано");
    ui.post(()->{if(isDestroyed()||session!=target)return;photoCache.put(id,bitmap);view.setImageBitmap(bitmap);retry.setVisibility(View.GONE);});
   }catch(Exception e){ui.post(()->{if(isDestroyed())return;retry.setEnabled(true);retry.setText("Загрузить фото ещё раз");});}});
  };retry.setOnClickListener(v->load.run());view.setOnClickListener(v->{Bitmap bitmap=photoCache.get(id);if(bitmap==null){load.run();return;}ImageView expanded=new ImageView(this);expanded.setImageBitmap(bitmap);expanded.setAdjustViewBounds(true);expanded.setScaleType(ImageView.ScaleType.FIT_CENTER);new AlertDialog.Builder(this).setTitle("Фотография").setView(expanded).setPositiveButton("Готово",null).show();});load.run();
 }
 static JSONObject request(JSONObject target,JSONObject body,String query)throws Exception{
  HttpURLConnection c=(HttpURLConnection)new URL(SERVER+"/api/v2"+query).openConnection();
  try{c.setInstanceFollowRedirects(false);c.setConnectTimeout(90000);c.setReadTimeout(90000);c.setUseCaches(false);c.setRequestMethod(body==null?"GET":"POST");
   if(target!=null)c.setRequestProperty("Authorization","Bearer "+target.getString("token"));
   if(body!=null){c.setDoOutput(true);c.setRequestProperty("Content-Type","application/json; charset=utf-8");byte[] data=body.toString().getBytes(StandardCharsets.UTF_8);c.setFixedLengthStreamingMode(data.length);try(java.io.OutputStream out=c.getOutputStream()){out.write(data);}}
   int code=c.getResponseCode();String type=c.getContentType();if(type==null||!type.contains("application/json"))throw new Exception("Сервер просыпается. Подождите минуту и повторите.");
   InputStream stream=code>=200&&code<300?c.getInputStream():c.getErrorStream();if(stream==null)throw new Exception("Нет ответа от сервера.");ByteArrayOutputStream output=new ByteArrayOutputStream();
   try(InputStream in=stream){byte[] buf=new byte[8192];int n;while((n=in.read(buf))!=-1){output.write(buf,0,n);if(output.size()>2850000)throw new Exception("Слишком большой ответ.");}}
   JSONObject result=new JSONObject(new String(output.toByteArray(),StandardCharsets.UTF_8));if(code<200||code>=300)throw new ApiError(code,result.optString("error","Ошибка сервера."));return result;
  }finally{c.disconnect();}
 }
 private static SecretKey key()throws Exception{
  KeyStore store=KeyStore.getInstance("AndroidKeyStore");store.load(null);if(!store.containsAlias(KEY_ALIAS)){KeyGenerator g=KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES,"AndroidKeyStore");g.init(new KeyGenParameterSpec.Builder(KEY_ALIAS,KeyProperties.PURPOSE_ENCRYPT|KeyProperties.PURPOSE_DECRYPT).setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build());g.generateKey();}return (SecretKey)store.getKey(KEY_ALIAS,null);
 }
 private void saveSession(JSONObject value)throws Exception{Cipher c=Cipher.getInstance("AES/GCM/NoPadding");c.init(Cipher.ENCRYPT_MODE,key());byte[] data=c.doFinal(value.toString().getBytes(StandardCharsets.UTF_8));if(!getSharedPreferences("account-v2",Context.MODE_PRIVATE).edit().putString("iv",Base64.encodeToString(c.getIV(),Base64.NO_WRAP)).putString("data",Base64.encodeToString(data,Base64.NO_WRAP)).commit())throw new Exception("Не удалось сохранить вход.");}
 static JSONObject loadSession(Context context)throws Exception{SharedPreferences p=context.getSharedPreferences("account-v2",Context.MODE_PRIVATE);String value=p.getString("data",null);if(value==null)return null;Cipher c=Cipher.getInstance("AES/GCM/NoPadding");c.init(Cipher.DECRYPT_MODE,key(),new GCMParameterSpec(128,Base64.decode(p.getString("iv",""),Base64.NO_WRAP)));JSONObject result=new JSONObject(new String(c.doFinal(Base64.decode(value,Base64.NO_WRAP)),StandardCharsets.UTF_8));return result.optLong("expiresAt")>System.currentTimeMillis()?result:null;}
}
