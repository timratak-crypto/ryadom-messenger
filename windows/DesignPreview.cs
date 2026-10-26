using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ryadom;
// Offscreen visual QA with fabricated data. Does not Run App or touch saved accounts/network/audio.
public static class DesignPreview {
 static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Field(object app,string name,object value){typeof(App).GetField(name,Flags).SetValue(app,value);}
 static object Call(object app,string name,params object[] args){return typeof(App).GetMethod(name,Flags).Invoke(app,args);}
 static T Find<T>(Window w,string n)where T:FrameworkElement{return (T)w.FindName(n);}
 static void Save(Window w,string file,int width,int height){
  w.Width=width;w.Height=height;w.Measure(new Size(width,height));w.Arrange(new Rect(0,0,width,height));w.UpdateLayout();
  var view=(FrameworkElement)w.Content;view.Measure(new Size(width,height));view.Arrange(new Rect(0,0,width,height));view.UpdateLayout();
  var bmp=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);var backdrop=new DrawingVisual();using(var dc=backdrop.RenderOpen())dc.DrawRectangle(w.Background,null,new Rect(0,0,width,height));bmp.Render(backdrop);bmp.Render(view);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using(var f=File.Create(file))png.Save(f);
 }
 [STAThread] public static void Main(string[] args){
  Directory.CreateDirectory(args[0]);var app=new PreviewApp();Window w;using(var stream=typeof(App).Assembly.GetManifestResourceStream("Ryadom.MainWindow.xaml"))w=(Window)XamlReader.Load(stream);Field(app,"w",w);
  Save(w,Path.Combine(args[0],"login.png"),1180,780);Save(w,Path.Combine(args[0],"login-narrow.png"),820,660);
  var now=DateTimeOffset.Now.ToUnixTimeMilliseconds();var chat=new Conversation{id="preview",username="mama",peerId="other",lastText="Хорошо, буду ждать ♡",at=now};
  Field(app,"session",new Session{user=new Profile{id="me",username="alex"},token="preview-only"});Field(app,"selected",chat);
  Find<Grid>(w,"Setup").Visibility=Visibility.Collapsed;Find<Grid>(w,"Main").Visibility=Visibility.Visible;Find<Grid>(w,"Empty").Visibility=Visibility.Collapsed;Find<Grid>(w,"Chat").Visibility=Visibility.Visible;Find<Button>(w,"Logout").Visibility=Visibility.Visible;Find<TextBlock>(w,"Me").Text="@alex";Find<TextBlock>(w,"Peer").Text="@mama";Find<Border>(w,"PeerAvatar").Child=Design.Avatar("mama",44);
  Call(app,"RenderInbox",new List<Conversation>{chat,new Conversation{id="2",username="pasha",lastText="Давай созвонимся вечером?",unread=2,at=now-120000},new Conversation{id="3",username="alina",lastText="Спасибо за фотографии!",at=now-500000},new Conversation{id="4",username="grandma",lastText="Голосовое сообщение",at=now-1600000}});
  Field(app,"messages",new List<Message>{new Message{seq=1,sender="other",text="Привет! Как проходит твой день?",at=now-300000},new Message{seq=2,sender="me",text="Привет, мам! Всё хорошо, уже закончил дела ☀",at=now-240000},new Message{seq=3,sender="other",text="Здорово! Заедешь сегодня на чай?\nЯ испекла твой любимый пирог.",at=now-180000},new Message{seq=4,sender="me",text="Конечно! Буду около семи.\nА пока можем немного поговорить?",at=now-60000},new Message{seq=5,sender="other",text="Хорошо, буду ждать ♡",at=now}});Field(app,"peerRead",(long)4);
  w.Measure(new Size(1180,780));w.Arrange(new Rect(0,0,1180,780));Call(app,"Render");Save(w,Path.Combine(args[0],"conversation.png"),1180,780);Save(w,Path.Combine(args[0],"conversation-narrow.png"),820,660);
  foreach(var name in new[]{"Call","Attach","Draft","Send","PhotoPreview","PendingPhoto","RemovePhoto","Microphone","VoicePreview","VoiceRecording","CancelVoice","RemoveVoice","PlayDraftVoice","RecordingTime","PeerAvatar"})if(w.FindName(name)==null)throw new Exception("Missing control: "+name);
  Find<Button>(w,"Settings").Visibility=Visibility.Visible;
  Palette.Index=1;Palette.Apply(w);Field(app,"rebuildMessages",true);
  Field(app,"messages",new List<Message>{new Message{seq=1,sender="me",text="Изменённое сообщение",editedAt=now,at=now-300000},new Message{seq=2,sender="other",text="Сообщение удалено",deletedAt=now,at=now-240000},new Message{seq=3,sender="me",kind="call",callStatus="answered",callDuration=125,at=now-180000},new Message{seq=4,sender="other",kind="call",callStatus="missed",at=now}});
  Call(app,"Render");Save(w,Path.Combine(args[0],"messages-red.png"),1180,780);
  var settings=(Window)Call(app,"BuildSettings");Save(settings,Path.Combine(args[0],"settings-account.png"),540,640);
  var dock=(DockPanel)settings.Content;var tabs=(TabControl)dock.Children[1];tabs.SelectedIndex=1;Save(settings,Path.Combine(args[0],"settings-style.png"),540,640);tabs.SelectedIndex=2;Save(settings,Path.Combine(args[0],"settings-sound.png"),540,640);settings.Close();
  var edited=new Message{seq=1,sender="me",text="Правка",editedAt=now+1,at=now};
  Field(app,"cursor",(long)4);Field(app,"changesCursor",(long)0);
  if(!(bool)Call(app,"ApplyFeed",new Result{messages=new List<Message>(),updates=new List<Message>{edited,new Message{seq=900,editedAt=now}},changesCursor=100}))throw new Exception("Edit did not refresh UI");
  if((long)typeof(App).GetField("cursor",Flags).GetValue(app)!=4)throw new Exception("Update incorrectly advanced normal cursor");
  var list=(List<Message>)typeof(App).GetField("messages",Flags).GetValue(app);if(list.Count!=4||list[0].text!="Правка")throw new Exception("Wrong change merge");
  Call(app,"ApplyFeed",new Result{messages=new List<Message>{new Message{seq=5,sender="other",text="Новое",at=now}},updates=new List<Message>{new Message{seq=1,sender="me",deletedAt=now+2,at=now}},changesCursor=101});
  if(list.Count!=5||list[0].deletedAt==0||(long)typeof(App).GetField("cursor",Flags).GetValue(app)!=5)throw new Exception("Deletion or new message missing");
  w.Close();Console.WriteLine("PASS offscreen login/conversation/settings, red theme, call cards, edit/delete merge and independent cursors. Synthetic data only.");
 }
 sealed class PreviewApp:App {protected override void OnStartup(StartupEventArgs e){}}
}
