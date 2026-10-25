using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Windows.Media.Capture;
using Windows.Storage.Streams;
using Windows.Media.Capture.Frames;
using Windows.Graphics.Imaging;
using BitmapDecoder=System.Windows.Media.Imaging.BitmapDecoder;
using BitmapFrame=System.Windows.Media.Imaging.BitmapFrame;
using Windows.Media.MediaProperties;
[assembly:System.Reflection.AssemblyVersion("2.7.0.0")]
namespace Ryadom {
 public class Profile { public string id; public string username; public string avatar; }
 public class Session { public string token; public long expiresAt; public Profile user; }
 public class Conversation { public string avatar; public string id; public string peerId; public string username; public string lastText; public long at; public int unread; }
 public class Message { public long editedAt,deletedAt; public string kind,callStatus; public int callDuration; public long seq; public string sender; public string clientId; public string text; public long at; public string photo; public int? width; public int? height; public string voice; public int? duration; }
 public class PhotoResponse { public string data; public string avatar; }
 public class Result { public long changesCursor; public List<Message> updates; public string error; public string conversation; public Profile peer; public Profile user; public List<Profile> users; public List<Conversation> chats; public List<Message> messages; public long peerRead; public bool hasMore; }
 public class ApiError:Exception { public int Code; public ApiError(int code,string message):base(message){Code=code;} }
 public static class Api {
  public const string Server="https://ryadom-family-chat.onrender.com";
  static readonly HttpClient Client=new HttpClient(new HttpClientHandler {AllowAutoRedirect=false}) {Timeout=TimeSpan.FromSeconds(90)};
  static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=3000000};
  public static async Task<T> Call<T>(Session s,object body=null,string query="") {
   using(var req=new HttpRequestMessage(body==null?HttpMethod.Get:HttpMethod.Post,Server+"/api/v2"+query)){
    if(s!=null&&!string.IsNullOrEmpty(s.token))req.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",s.token);
    if(body!=null)req.Content=new StringContent(Json.Serialize(body),Encoding.UTF8,"application/json");
    using(var response=await Client.SendAsync(req)){
     var raw=await response.Content.ReadAsStringAsync();
     if(response.Content.Headers.ContentType==null||response.Content.Headers.ContentType.MediaType!="application/json")throw new Exception("Сервер просыпается. Подождите минуту и повторите.");
     if(!response.IsSuccessStatusCode){var error=Json.Deserialize<Result>(raw);throw new ApiError((int)response.StatusCode,error.error??"Ошибка сервера.");}
     return Json.Deserialize<T>(raw);
    }
   }
  }
 }
 public static class Program {
  [STAThread] public static void Main(){ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;new App().Run();}
 }
 public partial class App:Application {
  Window w; Session session; Conversation selected; bool busy,polling,register;
  long cursor,peerRead; List<Message> messages=new List<Message>();
  byte[] photo,retryPhoto,voice,retryVoice;
  Dictionary<string,byte[]> voiceDrafts=new Dictionary<string,byte[]>();
  VoiceCapture captureVoice; bool recording;
  System.Media.SoundPlayer voicePlayer; MemoryStream voiceStream;
  Button playingButton; object playingLabel; int playbackGeneration;
  DispatcherTimer voiceClock,playbackClock;
  Dictionary<string,byte[]> photoDrafts=new Dictionary<string,byte[]>();
  Dictionary<long,TextBlock> receipts=new Dictionary<long,TextBlock>();
  System.Threading.SemaphoreSlim imageSlots=new System.Threading.SemaphoreSlim(2);
  Dictionary<string,string> drafts=new Dictionary<string,string>(); string retryText="",retryId="",retryChat="";
  static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=3000000};
  static readonly string FilePath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Ryadom","account-v2.bin");
  T Get<T>(string n) where T:FrameworkElement{return (T)w.FindName(n);}
  protected override void OnStartup(StartupEventArgs e) {
   base.OnStartup(e);
   using(var stream=typeof(App).Assembly.GetManifestResourceStream("Ryadom.MainWindow.xaml"))w=(Window)XamlReader.Load(stream);
   MainWindow=w;LoadStyle();Get<Button>("Settings").Click+=(a,b)=>ShowSettings();
   try{if(File.Exists(FilePath)){var s=Json.Deserialize<Session>(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(FilePath),null,DataProtectionScope.CurrentUser)));if(s.user!=null&&s.expiresAt>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())session=s;}}catch{}
   Get<Button>("Auth").Click+=async(a,b)=>await Authenticate();
   Get<Button>("AuthMode").Click+=(a,b)=>{if(busy)return;register=!register;UpdateAuth();};
   Get<PasswordBox>("Password").KeyDown+=async(a,b)=>{if(b.Key==Key.Enter)await Authenticate();};
   Get<Button>("Search").Click+=async(a,b)=>await Search();
   Get<TextBox>("Query").KeyDown+=async(a,b)=>{if(b.Key==Key.Enter)await Search();};
   Get<Button>("Logout").Click+=async(a,b)=>await Logout();
   Get<Button>("Microphone").Click+=async(a,b)=>{if(recording)captureVoice.Stop(true);else await RecordVoice();};
   Get<Button>("CancelVoice").Click+=(a,b)=>{if(recording)captureVoice.Stop(false);};
   Get<Button>("RemoveVoice").Click+=(a,b)=>{if(busy)return;StopAudio();voice=null;ShowVoice();};
   Get<Button>("PlayDraftVoice").Click+=(a,b)=>{if(!busy&&voice!=null)PlayVoice(voice,Get<Button>("PlayDraftVoice"));};
   w.Deactivated+=(a,b)=>{if(recording)captureVoice.Stop(true);StopAudio();};
   SetupNotifications();
   Get<Button>("Call").Click+=(a,b)=>StartCall();
   Get<Button>("Attach").Click+=(a,b)=>Attach();
   Get<Button>("RemovePhoto").Click+=(a,b)=>{if(busy)return;photo=null;ShowPhoto();};
   Get<Button>("Send").Click+=async(a,b)=>await Send();
   Get<TextBox>("Draft").PreviewKeyDown+=async(a,b)=>{if(b.Key==Key.Enter&&(Keyboard.Modifiers&ModifierKeys.Shift)==0){b.Handled=true;await Send();}};
   var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(3)};
   timer.Tick+=async(a,b)=>{EnsureCalls();await Refresh();};w.Activated+=async(a,b)=>await Refresh();w.Closed+=(a,b)=>timer.Stop();
   UpdateAuth();Show();w.Show();Design.Enter(session==null?(UIElement)Get<Grid>("Setup"):Get<Grid>("Main"));timer.Start();
  }
  void Status(string text,bool error=false){Get<TextBlock>("Status").Text=text;Get<TextBlock>("Status").Foreground=error?Brushes.Firebrick:Brushes.Gray;}
  void UpdateAuth(){Get<Button>("Auth").Content=register?"Зарегистрироваться":"Войти";Get<Button>("AuthMode").Content=register?"Уже есть аккаунт? Войти":"Нет аккаунта? Регистрация";Get<StackPanel>("ConfirmRow").Visibility=register?Visibility.Visible:Visibility.Collapsed;Get<TextBlock>("AuthTitle").Text=register?"Создать аккаунт":"С возвращением";Get<PasswordBox>("Password").Clear();Get<PasswordBox>("Confirm").Clear();}
  void Show(){
   EnsureCalls();bool signed=session!=null;
   Get<Grid>("Setup").Visibility=signed?Visibility.Collapsed:Visibility.Visible;
   Get<Grid>("Main").Visibility=signed?Visibility.Visible:Visibility.Collapsed;
   Get<Button>("Settings").Visibility=signed?Visibility.Visible:Visibility.Collapsed;
   Get<Button>("Logout").Visibility=signed?Visibility.Visible:Visibility.Collapsed;
   Get<TextBlock>("Me").Text=signed?"@"+session.user.username:"Личные сообщения";
   Get<Grid>("Chat").Visibility=signed&&selected!=null?Visibility.Visible:Visibility.Collapsed;
   Get<Grid>("Empty").Visibility=selected==null?Visibility.Visible:Visibility.Collapsed;
   UpdateComposerControls();
  }
  void Save(Session s){
   Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
   if(s==null){if(File.Exists(FilePath))File.Delete(FilePath);return;}
   byte[] data=ProtectedData.Protect(Encoding.UTF8.GetBytes(Json.Serialize(s)),null,DataProtectionScope.CurrentUser);
   File.WriteAllBytes(FilePath+".tmp",data);if(File.Exists(FilePath))File.Replace(FilePath+".tmp",FilePath,null);else File.Move(FilePath+".tmp",FilePath);
  }
  void Clear(){displayedInbox.Clear();inboxSignature="";notificationCursor.Clear();notificationAction=null;StopCalls();callSession.Reset();if(captureVoice!=null)captureVoice.Stop(false);StopAudio();voice=null;retryVoice=null;voiceDrafts.Clear();session=null;selected=null;photo=null;retryPhoto=null;photoDrafts.Clear();receipts.Clear();messages.Clear();drafts.Clear();cursor=0;changesCursor=0;Get<TextBox>("Draft").Clear();Get<StackPanel>("Chats").Children.Clear();Get<StackPanel>("Results").Children.Clear();Get<TextBox>("Query").Clear();Save(null);Show();}
  void Error(Exception ex,bool auth=false){
   var api=ex as ApiError;if(!auth&&api!=null&&api.Code==401)Clear();
   Status(ex is HttpRequestException||ex is TaskCanceledException?"Нет связи с сервером. При первом запуске подождите минуту и повторите.":ex.Message,true);
  }
  async Task Authenticate(){
   if(busy)return;
   string name=Get<TextBox>("Username").Text.Trim(),password=Get<PasswordBox>("Password").Password;
   if(register&&password!=Get<PasswordBox>("Confirm").Password){Status("Пароли не совпадают.",true);return;}
   SetBusy(true);Get<Button>("Auth").IsEnabled=false;Status("Подключаемся… Первый запуск сервера может занять минуту.");
   try{var next=await Api.Call<Session>(null,new{action=register?"register":"login",username=name,password});Save(next);session=next;selected=null;Show();Status("Вы вошли как @"+next.user.username);await Refresh();}
   catch(Exception ex){Error(ex,true);}finally{SetBusy(false);Get<Button>("Auth").IsEnabled=true;Get<PasswordBox>("Password").Clear();Get<PasswordBox>("Confirm").Clear();}
  }
  Task Logout(){
   if(busy||recording||session==null)return Task.FromResult(0);var target=session;
   var dialog=new Window{Owner=w,Title="Подтвердите выход",Width=410,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=new SolidColorBrush(Color.FromRgb(245,245,239))};
   var panel=new StackPanel{Margin=new Thickness(24)};
   panel.Children.Add(new TextBlock{Text="Пароль от @"+target.user.username,FontSize=20,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14)});
   var password=new PasswordBox{MaxLength=128,Padding=new Thickness(10),FontSize=16};panel.Children.Add(password);
   var feedback=new TextBlock{Foreground=Brushes.Firebrick,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,12)};panel.Children.Add(feedback);
   var submit=new Button{Content="Выйти из аккаунта",Padding=new Thickness(12),IsDefault=true};panel.Children.Add(submit);
   var cancel=new Button{Content="Отмена",Padding=new Thickness(12),Margin=new Thickness(0,8,0,0),IsCancel=true};panel.Children.Add(cancel);
   StyleDialog(dialog);dialog.Content=panel;dialog.Closing+=(a,e)=>{if(busy)e.Cancel=true;};dialog.Loaded+=(a,e)=>password.Focus();
   submit.Click+=async(a,e)=>{
    if(busy)return;string confirmation=password.Password;if(confirmation.Length==0){feedback.Text="Введите пароль.";return;}
    SetBusy(true);submit.IsEnabled=false;cancel.IsEnabled=false;feedback.Text="Проверяем пароль…";
    try{await Api.Call<Result>(target,new{action="logout",password=confirmation});Clear();Status("Вы вышли из аккаунта.");SetBusy(false);dialog.DialogResult=true;}
    catch(Exception ex){Error(ex);feedback.Text=ex is HttpRequestException||ex is TaskCanceledException?"Нет связи с сервером. Вы остались в аккаунте.":ex.Message;if(session==null){SetBusy(false);dialog.DialogResult=true;}}
    finally{SetBusy(false);submit.IsEnabled=true;cancel.IsEnabled=true;password.Clear();}
   };
   dialog.ShowDialog();return Task.FromResult(0);
  }
  Button Row(string text,Action click){var b=new Button{Content=new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap},HorizontalContentAlignment=HorizontalAlignment.Left,Background=Brushes.White,Foreground=new SolidColorBrush(Color.FromRgb(33,58,50)),Margin=new Thickness(0,0,0,8),Padding=new Thickness(12)};b.Click+=(a,e)=>click();return b;}
  async Task Search(){
   if(busy||session==null)return;SetBusy(true);var target=session;Get<Button>("Search").IsEnabled=false;
   try{var r=await Api.Call<Result>(target,null,"?view=search&q="+Uri.EscapeDataString(Get<TextBox>("Query").Text.Trim()));if(session!=target)return;var p=Get<StackPanel>("Results");p.Children.Clear();foreach(var user in r.users){var u=user;p.Children.Add(ContactRow(u.username,"Открыть профиль",0,0,false,()=>{if(busy)return;if(MessageBox.Show("@"+u.username+"\n\nОткрыть личную переписку?","Профиль",MessageBoxButton.YesNo)==MessageBoxResult.Yes)Open(u.username);},u.id,u.avatar));}Status(r.users.Count==0?"Пользователь не найден. Проверьте юзернейм.":"Выберите профиль, чтобы написать.");}
   catch(Exception ex){Error(ex);}finally{SetBusy(false);Get<Button>("Search").IsEnabled=true;}
  }
  async void Open(string username){
   if(busy||session==null)return;SetBusy(true);var target=session;
   try{var r=await Api.Call<Result>(target,new{action="open",username});if(session!=target)return;Select(new Conversation{id=r.conversation,peerId=r.peer.id,username=r.peer.username,avatar=r.peer.avatar});}
   catch(Exception ex){Error(ex);}finally{SetBusy(false);}await Refresh();
  }
  void Select(Conversation c){
   if(recording)return;StopAudio();if(selected!=null)voiceDrafts[selected.id]=voice;voiceDrafts.TryGetValue(c.id,out voice);retryVoice=null;
   if(selected!=null)drafts[selected.id]=Get<TextBox>("Draft").Text;
   if(selected!=null)photoDrafts[selected.id]=photo;photoDrafts.TryGetValue(c.id,out photo);retryPhoto=null;receipts.Clear();Get<StackPanel>("Messages").Children.Clear();
   selected=c;messages.Clear();cursor=0;changesCursor=0;rebuildMessages=false;peerRead=0;retryText="";retryId="";retryChat="";
   string draft;Get<TextBox>("Draft").Text=drafts.TryGetValue(c.id,out draft)?draft:"";
   inboxSignature="";RenderInbox(displayedInbox);Get<Border>("PeerAvatar").Child=ProfileAvatar(c.username,c.peerId,c.avatar,44);Get<TextBlock>("Peer").Text="@"+c.username;Get<TextBlock>("Presence").Text="Личная переписка";Show();ShowPhoto();ShowVoice();Render();Fade(Get<Grid>("Chat"));Get<TextBox>("Draft").Focus();
  }
  async Task Refresh(){
   if(polling||session==null)return;polling=true;var target=session;var chat=selected;
   try{
    var inbox=await Api.Call<Result>(target);if(session!=target)return;AcceptProfile(inbox.user);SyncPeer(inbox.chats);NotifyMessages(inbox.chats);
    RenderInbox(inbox.chats);
    if(chat!=null&&w.IsVisible&&w.IsActive&&w.WindowState!=WindowState.Minimized){
     var f=await Api.Call<Result>(target,null,"?view=messages&conversation="+chat.id+"&after="+cursor+"&changesAfter="+changesCursor);
     if(session!=target||selected!=chat)return;
     if(ApplyFeed(f))Render();
     Status("Соединение установлено · Enter — отправить · Shift+Enter — новая строка");
    }
   }catch(Exception ex){if(session==target)Error(ex);}finally{polling=false;}
  }
  bool ApplyFeed(Result f){
     bool changed=peerRead!=f.peerRead;peerRead=f.peerRead;
     foreach(var m in f.messages)if(messages.All(x=>x.seq!=m.seq)){messages.Add(m);cursor=Math.Max(cursor,m.seq);changed=true;}
     if(f.updates!=null)foreach(var update in f.updates){int index=messages.FindIndex(x=>x.seq==update.seq);if(index>=0&&(messages[index].editedAt!=update.editedAt||messages[index].deletedAt!=update.deletedAt)){messages[index]=update;rebuildMessages=true;changed=true;}}
     changesCursor=Math.Max(changesCursor,f.changesCursor);if(rebuildMessages)StopAudio();
     return changed;
  }
  void Render(){
   var p=Get<StackPanel>("Messages");var scroll=Get<ScrollViewer>("Scroll");bool bottom=scroll.ScrollableHeight-scroll.VerticalOffset<90;bool animate=receipts.Count>0;double previousOffset=scroll.VerticalOffset;if(rebuildMessages){receipts.Clear();rebuildMessages=false;animate=false;}
   if(receipts.Count==0){p.Children.Clear();messageDay="";}
   if(messages.Count==0){var hello=new StackPanel{Margin=new Thickness(24,70,24,0),HorizontalAlignment=HorizontalAlignment.Center};hello.Children.Add(new TextBlock{Text="Скажите «привет»",FontSize=24,FontWeight=FontWeights.SemiBold,Foreground=Design.Ink,TextAlignment=TextAlignment.Center});hello.Children.Add(new TextBlock{Text="И начнётся ваш разговор",FontSize=14,Foreground=Design.Muted,Margin=new Thickness(0,10,0,0),TextAlignment=TextAlignment.Center});p.Children.Add(hello);}
   foreach(var m in messages){
    bool own=m.sender==session.user.id;var ink=own?Brushes.White:Design.Ink;var inner=new StackPanel();var date=DateTimeOffset.FromUnixTimeMilliseconds(m.at).LocalDateTime;
    string stamp=date.ToString("HH:mm")+(m.editedAt>0&&m.deletedAt==0?" · изменено":"")+(own?(peerRead>=m.seq?"  ·  Прочитано":"  ·  Отправлено"):"");
    if(receipts.ContainsKey(m.seq)){receipts[m.seq].Text=stamp;continue;}
    string day=date.ToString("yyyy-MM-dd");if(messageDay!=day){messageDay=day;string label=date.Date==DateTime.Today?"Сегодня":date.Date==DateTime.Today.AddDays(-1)?"Вчера":date.ToString("d MMMM");p.Children.Add(new Border{HorizontalAlignment=HorizontalAlignment.Center,Background=Palette.Soft,CornerRadius=new CornerRadius(10),Padding=new Thickness(12,5,12,5),Margin=new Thickness(0,4,0,20),Child=Design.Text(label,11,Design.Muted)});}
    if(m.deletedAt==0&&!string.IsNullOrEmpty(m.photo))AddPhoto(inner,m.photo,selected.id);
    if(m.deletedAt==0&&!string.IsNullOrEmpty(m.voice))AddVoice(inner,m.voice,selected.id,m.duration??0);
    if(m.kind=="call"){
     inner.Children.Add(Design.Text(CallSummary(m,own),15,Design.Ink));var callback=new Button{Content=m.callStatus=="missed"&&!own?"Перезвонить":"Позвонить",Margin=new Thickness(0,10,0,0),Style=(Style)w.Resources["Quiet"]};callback.Click+=(a,e)=>{if(!busy)StartCall();};inner.Children.Add(callback);ink=Design.Ink;
    }else if(!string.IsNullOrEmpty(m.text))inner.Children.Add(new TextBox{Text=m.deletedAt>0?"Сообщение удалено":m.text,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,FontSize=15,Foreground=ink,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0),AcceptsReturn=true,IsReadOnlyCaretVisible=true});
    var receipt=new TextBlock{Text=stamp,FontSize=10,Opacity=.7,Foreground=ink,Margin=new Thickness(0,9,0,0),HorizontalAlignment=HorizontalAlignment.Right};inner.Children.Add(receipt);receipts[m.seq]=receipt;
    if(own&&m.deletedAt==0&&m.kind!="call"){var item=m;var more=new Button{Content="•••",Padding=new Thickness(8,0,8,0),FontSize=12,HorizontalAlignment=HorizontalAlignment.Right,Background=Brushes.Transparent,Foreground=ink,ToolTip="Изменить или удалить"};more.Click+=(a,e)=>MessageActions(item,more);inner.Children.Add(more);}
    var bubble=new Border{Child=inner,Padding=new Thickness(17,13,17,12),Margin=new Thickness(0,0,0,10),CornerRadius=own?new CornerRadius(18,18,5,18):new CornerRadius(18,18,18,5),MaxWidth=Math.Max(220,((w.ActualWidth>0?w.ActualWidth:w.Width)-420)*.76),HorizontalAlignment=m.kind=="call"?HorizontalAlignment.Center:own?HorizontalAlignment.Right:HorizontalAlignment.Left,Background=m.kind=="call"?Palette.Soft:own?Palette.Bubble:Brushes.White,BorderBrush=own?Brushes.Transparent:Design.Brush("#E6EDE8"),BorderThickness=new Thickness(1)};
    p.Children.Add(bubble);if(animate&&bottom)Design.Enter(bubble,8);
   }if(bottom)scroll.ScrollToEnd();else scroll.ScrollToVerticalOffset(previousOffset);
  }
  void Fade(UIElement view){Design.Enter(view,16);}
  void ShowPhoto(){ShowVoice();Get<Border>("PhotoPreview").Visibility=photo==null?Visibility.Collapsed:Visibility.Visible;Get<Image>("PendingPhoto").Source=photo==null?null:Decode(photo);}
  static BitmapImage Decode(byte[] data,bool thumbnail=false){int decodeWidth=0;if(thumbnail){using(var header=new MemoryStream(data)){var frame=BitmapDecoder.Create(header,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None).Frames[0];decodeWidth=Math.Max(1,(int)(frame.PixelWidth*Math.Min(1,600.0/Math.Max(frame.PixelWidth,frame.PixelHeight))));}}var image=new BitmapImage();using(var stream=new MemoryStream(data)){image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;if(decodeWidth>0)image.DecodePixelWidth=decodeWidth;image.StreamSource=stream;image.EndInit();}image.Freeze();return image;}
  public static byte[] PreparePhoto(byte[] data){
   if(data.Length>40*1024*1024)throw new Exception("Выберите фотографию размером до 40 МБ.");
   int width,height,orientation=1;
   using(var stream=new MemoryStream(data)){var frame=BitmapDecoder.Create(stream,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None).Frames[0];width=frame.PixelWidth;height=frame.PixelHeight;try{var metadata=frame.Metadata as BitmapMetadata;if(metadata!=null&&metadata.ContainsQuery("/app1/ifd/{ushort=274}"))orientation=Convert.ToInt32(metadata.GetQuery("/app1/ifd/{ushort=274}"));}catch{}}
   if(width<=0||height<=0||(long)width*height>100000000)throw new Exception("Фотография слишком большая.");
   var image=new BitmapImage();using(var stream=new MemoryStream(data)){image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=Math.Max(1,(int)(width*Math.Min(1,1600.0/Math.Max(width,height))));image.StreamSource=stream;image.EndInit();}image.Freeze();
   BitmapSource source=image;var transform=new TransformGroup();
   if(orientation==2||orientation==4||orientation==5||orientation==7)transform.Children.Add(new ScaleTransform(-1,1));
   double angle=orientation==3||orientation==4?180:orientation==5||orientation==6?90:orientation==7||orientation==8?270:0;
   if(angle!=0)transform.Children.Add(new RotateTransform(angle));if(transform.Children.Count>0){var adjusted=new TransformedBitmap(image,transform);adjusted.Freeze();source=adjusted;}
   var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){dc.DrawRectangle(Brushes.White,null,new Rect(0,0,source.PixelWidth,source.PixelHeight));dc.DrawImage(source,new Rect(0,0,source.PixelWidth,source.PixelHeight));}
   var normalized=new RenderTargetBitmap(source.PixelWidth,source.PixelHeight,96,96,PixelFormats.Pbgra32);normalized.Render(visual);normalized.Freeze();
   foreach(int quality in new[]{82,65,45,25}){var encoder=new JpegBitmapEncoder{QualityLevel=quality};encoder.Frames.Add(BitmapFrame.Create(normalized));using(var output=new MemoryStream()){encoder.Save(output);if(output.Length<=1024*1024)return output.ToArray();}}
   throw new Exception("Не удалось уменьшить фотографию. Выберите другой снимок.");
  }
  void Attach(){
   if(callSession.Busy){Status("Сначала завершите звонок.");return;}
   if(busy||recording||voice!=null||session==null||selected==null)return;var menu=new ContextMenu();
   var gallery=new MenuItem{Header="Фото с компьютера"};gallery.Click+=async(a,b)=>await PickPhoto();menu.Items.Add(gallery);
   var camera=new MenuItem{Header="Снять на камеру"};camera.Click+=async(a,b)=>await TakePhoto();menu.Items.Add(camera);menu.PlacementTarget=Get<Button>("Attach");menu.IsOpen=true;
  }
  async Task PickPhoto(){
   if(busy||recording||voice!=null||selected==null)return;var target=selected;var file=new Microsoft.Win32.OpenFileDialog{Filter="Фотографии|*.jpg;*.jpeg;*.png;*.bmp;*.heic;*.heif|Все файлы|*.*",Title="Выберите фотографию"};if(file.ShowDialog(w)!=true)return;
   SetBusy(true);Status("Готовим фотографию…");try{if(new FileInfo(file.FileName).Length>40*1024*1024)throw new Exception("Выберите фото размером до 40 МБ.");var bytes=await Task.Run(()=>File.ReadAllBytes(file.FileName));var prepared=PreparePhoto(bytes);if(selected==target){photo=prepared;ShowPhoto();Status("Фото готово. Добавьте подпись или нажмите ↑.");}}catch(Exception ex){Status("Не удалось открыть фото: "+ex.Message,true);}finally{SetBusy(false);}
  }
  async Task TakePhoto(){
   if(busy||recording||voice!=null||selected==null)return;var target=selected;SetBusy(true);
   var dialog=new Window{Owner=w,Title="Камера",Width=720,Height=600,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brushes.Black};
   var layout=new DockPanel{Margin=new Thickness(16)};var controls=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center};DockPanel.SetDock(controls,Dock.Bottom);layout.Children.Add(controls);
   var take=new Button{Content="Сделать снимок",Padding=new Thickness(20,12,20,12),Margin=new Thickness(8),IsEnabled=false};controls.Children.Add(take);
   var cancel=new Button{Content="Отмена",Padding=new Thickness(20,12,20,12),Margin=new Thickness(8)};controls.Children.Add(cancel);cancel.Click+=(a,b)=>dialog.Close();
   var hint=new TextBlock{Text="Подключаем камеру…",Foreground=Brushes.White,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(8)};DockPanel.SetDock(hint,Dock.Top);layout.Children.Add(hint);
   var preview=new Image{Stretch=Stretch.Uniform};layout.Children.Add(preview);dialog.Content=layout;
   MediaCapture capture=null;MediaFrameReader reader=null;bool closed=false;int processing=0;Task setup=Task.FromResult(0);
   dialog.Closed+=(a,b)=>closed=true;
   take.Click+=(a,b)=>{if(preview.Source==null)return;try{var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create((BitmapSource)preview.Source));using(var buffer=new MemoryStream()){png.Save(buffer);if(selected==target){photo=PreparePhoto(buffer.ToArray());ShowPhoto();Status("Снимок готов. Нажмите ↑, чтобы отправить.");}}dialog.Close();}catch(Exception ex){hint.Text=ex.Message;}};
   Func<Task> initialize=async()=>{
    try{
     capture=new MediaCapture();await capture.InitializeAsync(new MediaCaptureInitializationSettings{StreamingCaptureMode=StreamingCaptureMode.Video,MemoryPreference=MediaCaptureMemoryPreference.Cpu,SharingMode=MediaCaptureSharingMode.SharedReadOnly});if(closed)return;
     var source=capture.FrameSources.Values.FirstOrDefault(x=>x.Info.SourceKind==MediaFrameSourceKind.Color&&(x.Info.MediaStreamType==MediaStreamType.VideoPreview||x.Info.MediaStreamType==MediaStreamType.VideoRecord));
     if(source==null)throw new Exception("Совместимая камера не найдена.");
     var format=source.CurrentFormat.VideoFormat;uint width=format.Width,height=format.Height;double scale=Math.Min(1,1280.0/Math.Max(width,height));
     reader=await capture.CreateFrameReaderAsync(source,MediaEncodingSubtypes.Bgra8,new BitmapSize{Width=Math.Max(1,(uint)(width*scale)),Height=Math.Max(1,(uint)(height*scale))});if(closed)return;
     reader.AcquisitionMode=MediaFrameReaderAcquisitionMode.Realtime;
     reader.FrameArrived+=(sender,args)=>{
      if(closed||System.Threading.Interlocked.Exchange(ref processing,1)!=0)return;
      try{using(var frame=sender.TryAcquireLatestFrame()){
       using(var bitmap=frame==null||frame.VideoMediaFrame==null?null:frame.VideoMediaFrame.SoftwareBitmap){if(bitmap==null){System.Threading.Interlocked.Exchange(ref processing,0);return;}
       using(var converted=SoftwareBitmap.Convert(bitmap,BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied)){
        int stride=converted.PixelWidth*4;var buffer=new Windows.Storage.Streams.Buffer((uint)(stride*converted.PixelHeight));converted.CopyToBuffer(buffer);byte[] pixels=new byte[buffer.Length];using(var data=DataReader.FromBuffer(buffer))data.ReadBytes(pixels);
        var image=BitmapSource.Create(converted.PixelWidth,converted.PixelHeight,96,96,PixelFormats.Pbgra32,null,pixels,stride);image.Freeze();
        dialog.Dispatcher.BeginInvoke(new Action(()=>{try{if(!closed){preview.Source=image;take.IsEnabled=true;hint.Text="Выберите кадр и нажмите «Сделать снимок».";}}finally{System.Threading.Interlocked.Exchange(ref processing,0);}}));
       }}
      }}catch{System.Threading.Interlocked.Exchange(ref processing,0);}
     };
     var started=await reader.StartAsync();if(started!=MediaFrameReaderStartStatus.Success)throw new Exception("Не удалось включить камеру.");
    }catch{if(!closed)hint.Text="Не удалось включить камеру. Проверьте её подключение и разрешите доступ классическим приложениям в настройках Windows → Конфиденциальность → Камера.";}
   };
   dialog.Loaded+=(a,b)=>setup=initialize();
   try{dialog.ShowDialog();await setup;if(reader!=null)await reader.StopAsync();}catch{}finally{if(reader!=null)reader.Dispose();if(capture!=null)capture.Dispose();SetBusy(false);}
  }
  void AddPhoto(StackPanel panel,string id,string conversation){
   var image=new Image{MaxWidth=300,MaxHeight=300,Stretch=Stretch.Uniform,Cursor=Cursors.Hand};panel.Children.Add(image);
   var retry=new Button{Content="Загрузить фото",Padding=new Thickness(8),Margin=new Thickness(0,0,0,6)};panel.Children.Add(retry);var target=session;
   Func<Task> load=async()=>{retry.IsEnabled=false;retry.Content="Загрузка фото…";await imageSlots.WaitAsync();try{if(session!=target||selected==null||selected.id!=conversation)return;var response=await Api.Call<PhotoResponse>(target,null,"?view=photo&conversation="+conversation+"&photo="+id);if(session!=target)return;image.Source=Decode(Convert.FromBase64String(response.data),true);retry.Visibility=Visibility.Collapsed;}catch{retry.Content="Загрузить фото ещё раз";retry.IsEnabled=true;}finally{imageSlots.Release();}};
   retry.Click+=async(a,b)=>await load();image.MouseLeftButtonUp+=(a,b)=>{if(image.Source==null)return;new Window{Owner=w,Title="Фотография",Width=800,Height=650,WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=new Image{Source=image.Source,Stretch=Stretch.Uniform,Margin=new Thickness(12)}}.ShowDialog();};retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
  }

  void SetBusy(bool value){busy=value;UpdateComposerControls();}
  void UpdateComposerControls(){
   // Recompute every composer action together when an operation starts or ends.
   bool ready=!busy&&session!=null&&selected!=null;
   Get<Button>("Microphone").IsEnabled=ready&&voice==null&&photo==null;
   Get<Button>("Attach").IsEnabled=ready&&!recording&&voice==null;
   Get<Button>("Send").IsEnabled=ready&&!recording;
  }
  void ShowVoice(){
   Get<Border>("VoicePreview").Visibility=voice!=null&&!recording?Visibility.Visible:Visibility.Collapsed;
   Get<Border>("VoiceRecording").Visibility=recording?Visibility.Visible:Visibility.Collapsed;
   if(voice!=null)Get<Button>("PlayDraftVoice").Content="▶ "+VoiceCapture.Time(VoiceCapture.Seconds(voice))+" · Прослушать";
   Get<Button>("Microphone").Content=recording?"\uE71A":"\uE720";Get<Button>("Microphone").ToolTip=recording?"Остановить запись":"Записать голосовое сообщение";
   UpdateComposerControls();
  }
  async Task RecordVoice(){
   if(callSession.Busy){Status("Сначала завершите звонок.");return;}
   if(busy||recording||voice!=null||photo!=null||session==null||selected==null)return;
   StopAudio();var target=session;var chat=selected;var capture=new VoiceCapture();captureVoice=capture;recording=true;
   Get<TextBlock>("RecordingTime").Text="● Запись 0:00 / 1:00";ShowVoice();
   voiceClock=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};voiceClock.Tick+=(a,b)=>Get<TextBlock>("RecordingTime").Text="● Запись "+VoiceCapture.Time(capture.RecordedBytes/32000.0)+" / 1:00";voiceClock.Start();
   try{var data=await Task.Run(()=>capture.Record());if(session==target&&selected==chat&&data!=null){voice=data;Status("Запись готова. Прослушайте её или нажмите ↑.");}}
   catch(Exception ex){if(session==target)Status(ex.Message,true);}
   finally{voiceClock.Stop();captureVoice=null;recording=false;ShowVoice();}
  }
  void StopAudio(){
   playbackGeneration++;if(playbackClock!=null){playbackClock.Stop();playbackClock=null;}
   if(voicePlayer!=null){voicePlayer.Stop();voicePlayer.Dispose();voicePlayer=null;}if(voiceStream!=null){voiceStream.Dispose();voiceStream=null;}
   if(playingButton!=null){playingButton.Content=playingLabel;playingButton.IsEnabled=true;playingButton=null;}
  }
  void PlayVoice(byte[] data,Button button){
   if(callSession.Busy)return;
   if(recording)return;if(playingButton==button){StopAudio();return;}StopAudio();playingButton=button;playingLabel=button.Content;
   try{
    voiceStream=new MemoryStream(data);voicePlayer=new System.Media.SoundPlayer(voiceStream);voicePlayer.Load();voicePlayer.Play();button.Content="■ Остановить";
    playbackClock=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(Math.Max(250,VoiceCapture.Seconds(data)*1000))};playbackClock.Tick+=(a,b)=>StopAudio();playbackClock.Start();
   }catch{StopAudio();Status("Не удалось воспроизвести запись. Проверьте аудиоустройство.",true);}
  }
  void AddVoice(StackPanel panel,string id,string conversation,int duration){
   var play=new Button{Content="▶ Голосовое · "+VoiceCapture.Time(duration/1000.0),Padding=new Thickness(10),Margin=new Thickness(0,0,0,6)};panel.Children.Add(play);
   play.Click+=async(a,b)=>{
    if(recording)return;if(playingButton==play){StopAudio();return;}StopAudio();var target=session;int generation=playbackGeneration;var label=play.Content;
    playingButton=play;playingLabel=label;play.Content="Загрузка… Нажмите для отмены";
    try{
     var response=await Api.Call<PhotoResponse>(target,null,"?view=voice&conversation="+conversation+"&voice="+id);
     if(generation!=playbackGeneration||session!=target||selected==null||selected.id!=conversation)return;
     var data=Convert.FromBase64String(response.data);if(data.Length>2*1024*1024)throw new Exception("Запись слишком большая.");
     playingButton=null;play.Content=label;PlayVoice(data,play);
    }catch{if(generation==playbackGeneration){StopAudio();Status("Не удалось загрузить запись. Нажмите ▶ ещё раз.",true);}}
   };
  }

  async Task Send(){
   if(busy||recording||session==null||selected==null)return;StopAudio();var value=Get<TextBox>("Draft").Text.Trim();if(value.Length==0&&photo==null&&voice==null)return;var image=photo;var clip=voice;
   SetBusy(true);var target=session;var chat=selected;
   if(value!=retryText||chat.id!=retryChat||!object.ReferenceEquals(image,retryPhoto)||!object.ReferenceEquals(clip,retryVoice)){retryVoice=clip;retryPhoto=image;retryText=value;retryChat=chat.id;retryId=Guid.NewGuid().ToString();}
   try{await Api.Call<Result>(target,new{action=clip!=null?"sendVoice":(image==null?"send":"sendPhoto"),conversation=chat.id,clientId=retryId,text=value,data=clip!=null?Convert.ToBase64String(clip):(image==null?null:Convert.ToBase64String(image))});if(session==target&&selected==chat){if(Get<TextBox>("Draft").Text.Trim()==value)Get<TextBox>("Draft").Clear();drafts.Remove(chat.id);if(object.ReferenceEquals(photo,image))photo=null;photoDrafts.Remove(chat.id);retryPhoto=null;if(object.ReferenceEquals(voice,clip))voice=null;voiceDrafts.Remove(chat.id);retryVoice=null;ShowPhoto();ShowVoice();retryText="";retryId="";await Refresh();Get<ScrollViewer>("Scroll").ScrollToEnd();}}
   catch(Exception ex){Error(ex);}finally{SetBusy(false);}
  }
 }
}
