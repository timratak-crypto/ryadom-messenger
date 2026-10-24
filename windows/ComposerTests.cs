using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ryadom;

// Exercise the production Send method and XAML with an in-memory HTTP handler.
// Never run App.OnStartup, read saved accounts, connect to a server or open capture devices.
public static class ComposerTests {
 static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
 static App app; static Window window; static FakeHttp http; static int assertions;
 static object Field(string name){return typeof(App).GetField(name,Hidden).GetValue(app);}
 static void Field(string name,object value){typeof(App).GetField(name,Hidden).SetValue(app,value);}
 static object Call(string name,params object[] values){return typeof(App).GetMethod(name,Hidden).Invoke(app,values);}
 static T Find<T>(string name)where T:FrameworkElement{return (T)window.FindName(name);}
 static void Check(bool valid,string message){assertions++;if(!valid)throw new Exception(message);}
 static void Enabled(bool attach,bool microphone,bool send,string context){
  Check(Find<Button>("Attach").IsEnabled==attach,context+": attachment button");
  Check(Find<Button>("Microphone").IsEnabled==microphone,context+": microphone button");
  Check(Find<Button>("Send").IsEnabled==send,context+": send button");
 }
 static void Wait(Task task){
  if(!task.IsCompleted){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(5)};
   timer.Tick+=(s,e)=>frame.Continue=false;timer.Start();
   task.ContinueWith(t=>window.Dispatcher.BeginInvoke(new Action(()=>frame.Continue=false)));
   Dispatcher.PushFrame(frame);timer.Stop();Check(task.IsCompleted,"Send timed out");
  }task.GetAwaiter().GetResult();
 }
 static Task Start(string text){
  Find<TextBox>("Draft").Text=text;var task=(Task)Call("Send");
  if(!http.Pending&&!task.IsCompleted){var frame=new DispatcherFrame();var until=DateTime.UtcNow.AddSeconds(5);var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(10)};
   timer.Tick+=(s,e)=>{if(http.Pending||task.IsCompleted||DateTime.UtcNow>=until)frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);timer.Stop();
  }
  Check(http.Pending,"Request did not reach fake HTTP handler: "+Find<TextBlock>("Status").Text);return task;
 }
 static void Complete(Task task,bool success){http.Complete(success);Wait(task);Check(!(bool)Field("busy"),"busy cleared after request");}
 static byte[] Photo(){
  var image=BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,new byte[]{0,128,0,255},4);
  var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
  using(var stream=new MemoryStream()){encoder.Save(stream);return stream.ToArray();}
 }
 [STAThread] public static int Main(){
  try {
   app=new TestApp();using(var stream=typeof(App).Assembly.GetManifestResourceStream("Ryadom.MainWindow.xaml"))window=(Window)XamlReader.Load(stream);
   Field("w",window);Field("session",new Session{token="synthetic-only",user=new Profile{id="me",username="test"}});
   Field("selected",new Conversation{id="test-chat",peerId="other",username="peer"});
   SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
   http=new FakeHttp();typeof(Api).GetField("Client",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,new HttpClient(http));
   Call("ShowVoice");Enabled(true,true,true,"initial composer");
   var first=Start("first message");Check(!Find<Button>("Send").IsEnabled,"sending disables send");Complete(first,true);
   Enabled(true,true,true,"after successful text send");Check(Find<TextBox>("Draft").Text=="","sent text cleared");
   for(int i=0;i<3;i++){
    var task=Start("next "+i);Enabled(false,false,false,"request in flight");int sent=http.Posts;
    Wait((Task)Call("Send"));Check(http.Posts==sent,"duplicate send suppressed");Complete(task,true);Enabled(true,true,true,"repeated send");
   }
   var photo=Photo();Field("photo",photo);Call("ShowPhoto");Enabled(true,false,true,"photo draft");
   var picture=Start("caption");Check(http.Body.Contains("sendPhoto"),"photo uses upload action");Complete(picture,true);
   Check(Field("photo")==null,"sent photo removed");Enabled(true,true,true,"after photo send");
   var voice=VoiceCapture.Encode(new byte[16000]);Field("voice",voice);Call("ShowVoice");Enabled(false,false,true,"voice draft");
   var audio=Start("");Check(http.Body.Contains("sendVoice"),"voice uses upload action");Complete(audio,true);
   Check(Field("voice")==null,"sent voice removed");Enabled(true,true,true,"after voice send");
   var failed=Start("keep text");Complete(failed,false);Enabled(true,true,true,"rejected text send");Check(Find<TextBox>("Draft").Text=="keep text","rejected text kept");
   Field("photo",photo);Call("ShowPhoto");var failedPhoto=Start("keep caption");Complete(failedPhoto,false);
   Check(Object.ReferenceEquals(Field("photo"),photo),"rejected photo kept");Enabled(true,false,true,"rejected photo can retry");
   Field("photo",null);Call("ShowPhoto");Enabled(true,true,true,"removing rejected photo restores microphone");
   Field("voice",voice);Call("ShowVoice");var failedVoice=Start("");Complete(failedVoice,false);
   Check(Object.ReferenceEquals(Field("voice"),voice),"rejected voice kept");Enabled(false,false,true,"rejected voice can retry");
   Field("voice",null);Call("ShowVoice");Enabled(true,true,true,"removing rejected voice restores attachments");
   var offline=Start("offline draft");http.Fail();Wait(offline);Enabled(true,true,true,"network failure restores composer");Check(Find<TextBox>("Draft").Text=="offline draft","offline draft kept");
   var editing=Start("outgoing text");Find<TextBox>("Draft").Text="new draft while sending";Complete(editing,true);
   Check(Find<TextBox>("Draft").Text=="new draft while sending","new draft preserved");Enabled(true,true,true,"editing during send");
   Call("SetBusy",true);Field("photo",photo);Call("ShowPhoto");Enabled(false,false,false,"photo preparation busy");Call("SetBusy",false);Enabled(true,false,true,"prepared photo can send");
   Field("photo",null);Call("ShowPhoto");Field("recording",true);Call("ShowVoice");Call("SetBusy",false);Enabled(false,true,false,"recording keeps stop available");Field("recording",false);Call("ShowVoice");Enabled(true,true,true,"recording cancelled");
   Console.WriteLine("PASS "+assertions+" Windows composer assertions: repeated text/photo/voice sends, rejected uploads, network failure, draft preservation and busy transitions (in-memory HTTP; no account, microphone or camera).");
   window.Close();return 0;
  }catch(Exception e){Console.Error.WriteLine("FAIL: "+e);return 1;}
 }
 // Application queues startup in its constructor, even without Run(). Suppress it
 // before pumping the dispatcher so tests cannot load a real saved account.
 sealed class TestApp:App {protected override void OnStartup(StartupEventArgs e){}}
 sealed class FakeHttp:HttpMessageHandler {
  public int Posts;public string Body;volatile TaskCompletionSource<HttpResponseMessage> pending;
  public bool Pending {get{return pending!=null;}}
  static HttpResponseMessage Json(HttpStatusCode status,string content){return new HttpResponseMessage(status){Content=new StringContent(content,System.Text.Encoding.UTF8,"application/json")};}
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
   if(request.Method==HttpMethod.Get)return Json(HttpStatusCode.OK,"{\"chats\":[]}");
   if(pending!=null)throw new Exception("Unexpected overlapping request");Posts++;Body=await request.Content.ReadAsStringAsync();pending=new TaskCompletionSource<HttpResponseMessage>();return await pending.Task;
  }
  public void Complete(bool success){var active=pending;pending=null;active.SetResult(Json(success?HttpStatusCode.OK:HttpStatusCode.BadRequest,success?"{}":"{\"error\":\"Synthetic rejection\"}"));}
  public void Fail(){var active=pending;pending=null;active.SetException(new HttpRequestException("Synthetic offline failure"));}
 }
}
