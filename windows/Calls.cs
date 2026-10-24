using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Ryadom {
 public sealed class CallSocket : IDisposable {
  readonly ClientWebSocket socket=new ClientWebSocket(); readonly CancellationTokenSource cancel=new CancellationTokenSource();
  readonly SemaphoreSlim writer=new SemaphoreSlim(1); int queued; bool disposed;
  public bool Ready; public Action<Dictionary<string,object>> Control; public Action<byte[]> Audio; public Action Closed;
  public async Task Run(string token,Uri endpoint=null) {
   try {
    socket.Options.SetRequestHeader("Authorization","Bearer "+token); socket.Options.KeepAliveInterval=TimeSpan.FromSeconds(15);
    var address=new Uri("wss://ryadom-family-chat.onrender.com/api/calls");
    if(endpoint!=null){if(!endpoint.IsLoopback)throw new ArgumentException("Test endpoint must be loopback");address=endpoint;}
    await socket.ConnectAsync(address,cancel.Token).ConfigureAwait(false);
    var buffer=new byte[6400];
    while(!cancel.IsCancellationRequested){
     using(var frame=new MemoryStream()){
      WebSocketReceiveResult r;
      do{r=await socket.ReceiveAsync(new ArraySegment<byte>(buffer),cancel.Token).ConfigureAwait(false);if(r.MessageType==WebSocketMessageType.Close)return;frame.Write(buffer,0,r.Count);if(frame.Length>6400)throw new Exception("Frame too large");}while(!r.EndOfMessage);
      var data=frame.ToArray();
      if(r.MessageType==WebSocketMessageType.Binary){if(data.Length>0&&data.Length%2==0&&Audio!=null)Audio(data);}
      else {var value=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(data));if(Control!=null)Control(value);}
     }
    }
   }catch(Exception){ }finally{Ready=false;if(Closed!=null)Closed();}
  }
  public void Send(object value){var data=Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(value));var ignored=Write(data,false);}
  public void PCM(byte[] data){if(Ready){var ignored=Write(data,true);}}
  async Task Write(byte[] data,bool binary){
   if(disposed)return;if(binary&&Interlocked.Increment(ref queued)>3){Interlocked.Decrement(ref queued);return;}
   try{await writer.WaitAsync(cancel.Token).ConfigureAwait(false);try{if(socket.State==WebSocketState.Open)await socket.SendAsync(new ArraySegment<byte>(data),binary?WebSocketMessageType.Binary:WebSocketMessageType.Text,true,cancel.Token).ConfigureAwait(false);}finally{writer.Release();}}
   catch(Exception){if(!disposed){socket.Abort();cancel.Cancel();}}
   finally{if(binary)Interlocked.Decrement(ref queued);}
  }
  public void Dispose(){if(disposed)return;disposed=true;Ready=false;cancel.Cancel();socket.Abort();socket.Dispose();}
 }

 // Buffers are bounded and reused on one worker. No temporary recordings are made.
 public sealed class CallAudio : IDisposable {
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Caps {public ushort manufacturer,product;public uint version;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string name;public uint formats;public ushort channels,reserved;public uint support;}
  [DllImport("winmm.dll")] static extern uint waveOutGetNumDevs();
  [DllImport("winmm.dll",CharSet=CharSet.Unicode)] static extern uint waveOutGetDevCaps(UIntPtr device,out Caps caps,uint size);
  public static string[] Outputs(){var names=new List<string>();for(uint i=0;i<waveOutGetNumDevs();i++){Caps caps;names.Add(waveOutGetDevCaps((UIntPtr)i,out caps,(uint)Marshal.SizeOf(typeof(Caps)))==0?caps.name:"Динамик "+(i+1));}return names.ToArray();}
  [StructLayout(LayoutKind.Sequential,Pack=2)] struct Format {public ushort tag,channels;public uint rate,bytes;public ushort align,bits,extra;}
  [StructLayout(LayoutKind.Sequential)] struct Header {public IntPtr data;public uint length,recorded;public UIntPtr user;public uint flags,loops;public IntPtr next;public UIntPtr reserved;}
  [DllImport("winmm.dll")] static extern uint waveInOpen(out IntPtr h,uint d,ref Format f,IntPtr c,IntPtr i,uint flags);
  [DllImport("winmm.dll")] static extern uint waveOutOpen(out IntPtr h,uint d,ref Format f,IntPtr c,IntPtr i,uint flags);
  [DllImport("winmm.dll")] static extern uint waveInPrepareHeader(IntPtr h,IntPtr p,uint n);
  [DllImport("winmm.dll")] static extern uint waveOutPrepareHeader(IntPtr h,IntPtr p,uint n);
  [DllImport("winmm.dll")] static extern uint waveInUnprepareHeader(IntPtr h,IntPtr p,uint n);
  [DllImport("winmm.dll")] static extern uint waveOutUnprepareHeader(IntPtr h,IntPtr p,uint n);
  [DllImport("winmm.dll")] static extern uint waveInAddBuffer(IntPtr h,IntPtr p,uint n);
  [DllImport("winmm.dll")] static extern uint waveOutWrite(IntPtr h,IntPtr p,uint n);
  [DllImport("winmm.dll")] static extern uint waveInStart(IntPtr h);
  [DllImport("winmm.dll")] static extern uint waveInReset(IntPtr h);
  [DllImport("winmm.dll")] static extern uint waveOutReset(IntPtr h);
  [DllImport("winmm.dll")] static extern uint waveInClose(IntPtr h);
  [DllImport("winmm.dll")] static extern uint waveOutClose(IntPtr h);
  readonly ConcurrentQueue<byte[]> incoming=new ConcurrentQueue<byte[]>(); volatile bool stopped; public volatile bool Muted;
  readonly uint outputDevice;long received,played;public long ReceivedBytes{get{return Interlocked.Read(ref received);}}public long PlayedBytes{get{return Interlocked.Read(ref played);}}public volatile int InputPeak,PeerPeak;
  public CallAudio(uint outputDevice=uint.MaxValue){this.outputDevice=outputDevice;}
  public static int Peak(byte[] data){int peak=0;for(int i=0;i+1<data.Length;i+=2)peak=Math.Max(peak,Math.Abs((int)(short)(data[i]|data[i+1]<<8)));return peak;}
  public void Enqueue(byte[] data){if(!stopped&&data.Length>0&&data.Length<=6400&&data.Length%2==0){Interlocked.Add(ref received,data.Length);PeerPeak=Peak(data);byte[] old;if(incoming.Count>=6)incoming.TryDequeue(out old);incoming.Enqueue(data);}}
  static void Check(uint code){if(code!=0)throw new Exception("Не удалось открыть микрофон или динамик. Проверьте устройства и разрешения Windows.");}
  public void Run(Action<byte[]> send,Action<string> failed){
   IntPtr input=IntPtr.Zero,output=IntPtr.Zero;var ins=new List<IntPtr>();var outs=new List<IntPtr>();var allocations=new List<IntPtr>();
   uint size=(uint)Marshal.SizeOf(typeof(Header));
   try{
    var format=new Format{tag=1,channels=1,rate=16000,bytes=32000,align=2,bits=16};
    if(stopped)return;Check(waveInOpen(out input,uint.MaxValue,ref format,IntPtr.Zero,IntPtr.Zero,0));Check(waveOutOpen(out output,outputDevice,ref format,IntPtr.Zero,IntPtr.Zero,0));
    for(int i=0;i<6;i++){
     foreach(bool capture in new[]{true,false}){
      var data=Marshal.AllocHGlobal(capture?1280:6400);allocations.Add(data);var ptr=Marshal.AllocHGlobal((int)size);allocations.Add(ptr);
      Marshal.StructureToPtr(new Header{data=data,length=(uint)(capture?1280:6400)},ptr,false);
      if(capture){Check(waveInPrepareHeader(input,ptr,size));ins.Add(ptr);Check(waveInAddBuffer(input,ptr,size));}
      else{Check(waveOutPrepareHeader(output,ptr,size));outs.Add(ptr);}
     }
    }
    Check(waveInStart(input));int next=0;
    while(!stopped){
     var ptr=ins[next];var h=(Header)Marshal.PtrToStructure(ptr,typeof(Header));
     if((h.flags&1)!=0){if(h.recorded>0){var data=new byte[h.recorded];Marshal.Copy(h.data,data,0,data.Length);InputPeak=Peak(data);if(!Muted)send(data);}Check(waveInAddBuffer(input,ptr,size));next=(next+1)%ins.Count;}
     foreach(var p in outs){var o=(Header)Marshal.PtrToStructure(p,typeof(Header));byte[] data;if((o.flags&16)==0&&incoming.TryDequeue(out data)){Marshal.Copy(data,0,o.data,data.Length);o.length=(uint)data.Length;Marshal.StructureToPtr(o,p,false);Check(waveOutWrite(output,p,size));Interlocked.Add(ref played,data.Length);}}
     Thread.Sleep(5);
    }
   }catch(Exception e){if(!stopped)failed(e.Message);}
   finally{
    stopped=true;
    if(input!=IntPtr.Zero){waveInReset(input);foreach(var p in ins)waveInUnprepareHeader(input,p,size);waveInClose(input);}
    if(output!=IntPtr.Zero){waveOutReset(output);foreach(var p in outs)waveOutUnprepareHeader(output,p,size);waveOutClose(output);}
    foreach(var p in allocations)Marshal.FreeHGlobal(p);byte[] unused;while(incoming.TryDequeue(out unused)){}
   }
  }
  public void Dispose(){stopped=true;}
 }

 public partial class App {
  CallSocket calls; string callToken; volatile CallAudio liveAudio;
  readonly CallSession callSession=new CallSession();
  string callId {get{return callSession.Id;}} string callState {get{return callSession.State;}} string callPeer {get{return callSession.Peer;}}
  Task audioWorker;uint callOutputDevice=uint.MaxValue;
  Window callWindow; Border callAvatar; TextBlock callLabel; Button callAccept,callMute,callRetry,callEnd; DispatcherTimer callClock; DateTime callStart;
  void EnsureCalls(){
   string token=session==null?null:session.token;if(token==callToken&&calls!=null)return;
   StopCalls();if(token==null)return;callToken=token;var socket=new CallSocket();calls=socket;
   socket.Control=value=>Dispatcher.BeginInvoke(new Action(()=>{if(calls==socket)CallEvent(value);}));
   socket.Audio=data=>{var audio=liveAudio;if(calls==socket&&audio!=null)audio.Enqueue(data);};
   socket.Closed=()=>Dispatcher.BeginInvoke(new Action(()=>{if(calls!=socket)return;EndCall("Связь прервалась. Повторите звонок.",callSession.Busy);calls=null;callToken=null;socket.Dispose();}));
   var ignored=socket.Run(token);
  }
  void StopCalls(){var old=calls;calls=null;callToken=null;if(old!=null)old.Dispose();EndCall("");}
  void StartCall(){if(selected!=null)DialCall(selected.username);}
  void DialCall(string peer){
   if(session==null||recording||callSession.Busy)return;
   if(callSession.RetrySeconds>0){ShowCall();return;}
   EnsureCalls();
   if(calls==null||!calls.Ready){Status("Подключаем звонки… Повторите через несколько секунд.");return;}
   if(!callSession.Begin(peer))return;
   StopAudio();ShowCall();calls.Send(new{type="call",username=callPeer,requestId=callSession.RequestId});
  }
  static string CallReason(string reason){switch(reason){case "offline":return "Собеседник не в сети. Попросите открыть «Рядом».";case "busy":return "Собеседник уже разговаривает.";case "declined":return "Вызов отклонён.";case "unanswered":return "Нет ответа.";case "rate":return "Слишком много попыток. Следующий вызов станет доступен после отсчёта.";case "disconnected":return "Связь прервалась.";case "answered_elsewhere":return "Звонок принят на другом устройстве.";case "finished":case "cancelled":return "Звонок завершён.";default:return "Звонок недоступен. Повторите попытку.";}}
  static string CallField(Dictionary<string,object> value,string key){object item;return value.TryGetValue(key,out item)&&item is string?(string)item:"";}
  void CallEvent(Dictionary<string,object> value){
   string type=CallField(value,"type"),id=CallField(value,"id");
   if(type=="ready"){calls.Ready=true;return;}
   if(type=="incoming"&&recording){calls.Send(new{type="hangup",id});return;}
   int retryAfter=0;object delay;if(value.TryGetValue("retryAfterMs",out delay))int.TryParse(Convert.ToString(delay),out retryAfter);
   var change=callSession.Receive(type,id,CallField(value,"username"),CallField(value,"reason"),CallField(value,"requestId"),retryAfter);
   if(change==CallChange.Reject){calls.Send(new{type="hangup",id});return;}
   if(change==CallChange.Incoming||change==CallChange.Ringing){StopAudio();ShowCall();if(change==CallChange.Incoming)NotifyCall();return;}
   if(change==CallChange.Ended){EndCall(CallReason(callSession.Reason),true);return;}
   if(change==CallChange.Active){callStart=DateTime.UtcNow;ShowCall();StartCallAudio();}
  }
  void StartCallAudio(){
   var old=liveAudio;bool muted=old!=null&&old.Muted;if(old!=null)old.Dispose();
   var audio=new CallAudio(callOutputDevice){Muted=muted};liveAudio=audio;var socket=calls;var previous=audioWorker;
   audioWorker=Task.Run(async()=>{if(previous!=null)await previous; if(liveAudio!=audio)return;
    audio.Run(data=>{if(liveAudio==audio&&calls==socket)socket.PCM(data);},message=>Dispatcher.BeginInvoke(new Action(()=>{if(liveAudio==audio){SendHangup();EndCall(message,true);}})));
   });
  }
  void ShowCall(){
   if(callWindow==null){
    callWindow=new Window{Owner=w,Title="Звонок · Рядом",Width=430,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=new SolidColorBrush(Color.FromRgb(245,245,239))};
    StyleDialog(callWindow);var panel=new StackPanel{Margin=new Thickness(32)};
    var eyebrow=Design.Text("ГОЛОСОВОЙ ЗВОНОК",10,Design.Muted);eyebrow.HorizontalAlignment=HorizontalAlignment.Center;eyebrow.Margin=new Thickness(0,0,0,26);panel.Children.Add(eyebrow);
    callAvatar=Design.Avatar(callPeer,92);callAvatar.HorizontalAlignment=HorizontalAlignment.Center;callAvatar.Margin=new Thickness(0,0,0,22);panel.Children.Add(callAvatar);
    callLabel=new TextBlock{TextAlignment=TextAlignment.Center,LineHeight=32,FontWeight=FontWeights.SemiBold,FontSize=22,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,20)};panel.Children.Add(callLabel);
    panel.Children.Add(new TextBlock{Text="Динамик",Margin=new Thickness(0,0,0,4)});
    var outputs=new ComboBox{Margin=new Thickness(0,0,0,14),Padding=new Thickness(8)};outputs.Items.Add("По умолчанию в Windows");foreach(var name in CallAudio.Outputs())outputs.Items.Add(name);outputs.SelectedIndex=callOutputDevice==uint.MaxValue?0:(int)callOutputDevice+1;if(outputs.SelectedIndex<0)outputs.SelectedIndex=0;
    outputs.SelectionChanged+=(a,b)=>{callOutputDevice=outputs.SelectedIndex<=0?uint.MaxValue:(uint)(outputs.SelectedIndex-1);if(callState=="active")StartCallAudio();};panel.Children.Add(outputs);
    callAccept=new Button{Content="Принять",Padding=new Thickness(12),Margin=new Thickness(0,0,0,12)};callAccept.Click+=(a,b)=>{if(calls!=null&&callSession.Answer()){calls.Send(new{type="accept",id=callId});ShowCall();}};panel.Children.Add(callAccept);
    callMute=new Button{Content="Выключить микрофон",Padding=new Thickness(12),Margin=new Thickness(0,0,0,12)};callMute.Click+=(a,b)=>{if(liveAudio!=null){liveAudio.Muted=!liveAudio.Muted;callMute.Content=liveAudio.Muted?"Включить микрофон":"Выключить микрофон";}};panel.Children.Add(callMute);
    callRetry=new Button{Content="Позвонить снова",Padding=new Thickness(12),Margin=new Thickness(0,0,0,12)};callRetry.Click+=(a,b)=>DialCall(callPeer);panel.Children.Add(callRetry);
    callEnd=new Button{Content="Завершить",Padding=new Thickness(12),Background=Brushes.Firebrick,Foreground=Brushes.White};callEnd.Click+=(a,b)=>Hangup();panel.Children.Add(callEnd);
    panel.Children.Add(new TextBlock{Text="Для чистого звука используйте наушники.\nНа телефоне приложение должно быть открыто.",FontSize=12,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,18,0,0)});
    callWindow.Content=panel;Design.Enter(panel,16);var shownWindow=callWindow;
    shownWindow.Closing+=(a,b)=>{if(callWindow==shownWindow){SendHangup();EndCall("",false,false);}};shownWindow.Show();
    callClock=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};callClock.Tick+=(a,b)=>UpdateCallLabel();callClock.Start();
   }
   if(callAvatar!=null){((TextBlock)callAvatar.Child).Text=string.IsNullOrEmpty(callPeer)?"Р":callPeer.Substring(0,Math.Min(2,callPeer.Length)).ToUpperInvariant();Design.Pulse(callAvatar,callState=="incoming"||callState=="ringing");}
   callAccept.Visibility=callState=="incoming"?Visibility.Visible:Visibility.Collapsed;callMute.Visibility=callState=="active"?Visibility.Visible:Visibility.Collapsed;UpdateCallLabel();
  }
  void UpdateCallLabel(){
   RingCall();if(callLabel==null)return;
   bool ended=callState=="ended";
   callRetry.Visibility=ended?Visibility.Visible:Visibility.Collapsed;
   callRetry.IsEnabled=callSession.RetrySeconds==0;
   callRetry.Content=callSession.RetrySeconds>0?"Повторить через "+callSession.RetrySeconds+" с":"Позвонить снова";
   callEnd.Content=ended?"Закрыть":"Завершить";
   callLabel.Text="@"+callPeer+"\n"+(ended?callSession.Reason:callState=="active"?"Разговор · "+(DateTime.UtcNow-callStart).ToString(@"mm\:ss"):callState=="incoming"?"Входящий звонок":callState=="ringing"?"Вызываем…":"Подключаемся…");
   var audio=liveAudio;if(callState=="active"&&audio!=null)callLabel.Text+="\nМикрофон: "+(audio.Muted?"выкл.":Math.Min(100,audio.InputPeak*100/12000)+"%")+"\n"+(audio.ReceivedBytes==0?"Ожидаем звук собеседника":"Собеседник: "+Math.Min(100,audio.PeerPeak*100/12000)+"%");
  }
  void SendHangup(){if(calls!=null&&callSession.Busy){if(callId.Length>0)calls.Send(new{type="hangup",id=callId});else calls.Send(new{type="cancel"});}}
  void Hangup(){SendHangup();EndCall("Звонок завершён.");}
  void EndCall(string message,bool showResult=false,bool closeWindow=true){
   if(liveAudio!=null)liveAudio.Dispose();liveAudio=null;
   if(showResult&&callWindow!=null){callSession.Finish(message);ShowCall();Status(message,true);return;}
   // The rate deadline applies to the account even if its result window is dismissed.
   callSession.Finish(message);
   if(callClock!=null)callClock.Stop();callClock=null;
   var window=callWindow;callWindow=null;Design.Pulse(callAvatar,false);callAvatar=null;callLabel=null;callAccept=null;callMute=null;callRetry=null;callEnd=null;
   if(closeWindow&&window!=null)window.Close();if(message.Length>0)Status(message);
  }
 }
}
