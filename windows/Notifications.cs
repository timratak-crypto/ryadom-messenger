using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace Ryadom {
 public sealed class NotificationCursor {
  readonly Dictionary<string,int> counts=new Dictionary<string,int>();
  public bool Observe(string id,int unread,bool visible){int old;counts.TryGetValue(id,out old);unread=Math.Max(0,unread);counts[id]=unread;return !visible&&unread>old;}
  public void Clear(){counts.Clear();}
 }
 public partial class App {
  Forms.NotifyIcon tray;bool quitting,notificationsEnabled=true;Action notificationAction;
  readonly NotificationCursor notificationCursor=new NotificationCursor();
  readonly string notificationSetting=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Ryadom","notifications-off");
  DateTime lastCallRing=DateTime.MinValue;
  void SetupNotifications(){
   notificationsEnabled=!File.Exists(notificationSetting);
   tray=new Forms.NotifyIcon{Text="Рядом · сообщения и звонки",Icon=System.Drawing.Icon.ExtractAssociatedIcon(typeof(App).Assembly.Location),Visible=true};
   var menu=new Forms.ContextMenuStrip();menu.Items.Add("Открыть Рядом",null,(a,b)=>RestoreMessenger());
   var enabled=new Forms.ToolStripMenuItem("Уведомления со звуком"){Checked=notificationsEnabled,CheckOnClick=true};enabled.CheckedChanged+=(a,b)=>{notificationsEnabled=enabled.Checked;try{Directory.CreateDirectory(Path.GetDirectoryName(notificationSetting));if(notificationsEnabled){if(File.Exists(notificationSetting))File.Delete(notificationSetting);}else File.WriteAllText(notificationSetting,"off");}catch{}};menu.Items.Add(enabled);
   menu.Items.Add("Закрыть приложение",null,(a,b)=>{quitting=true;w.Close();});tray.ContextMenuStrip=menu;
   tray.DoubleClick+=(a,b)=>RestoreMessenger();tray.BalloonTipClicked+=(a,b)=>{var action=notificationAction;notificationAction=null;RestoreMessenger();if(action!=null)action();};
   w.Closing+=(a,b)=>{if(!quitting&&session!=null){b.Cancel=true;if(recording&&captureVoice!=null)captureVoice.Stop(true);StopAudio();w.Hide();return;}StopCalls();if(captureVoice!=null)captureVoice.Stop(false);StopAudio();};
   w.Closed+=(a,b)=>{tray.Visible=false;tray.Dispose();};
  }
  void RestoreMessenger(){w.Show();if(w.WindowState==WindowState.Minimized)w.WindowState=WindowState.Normal;w.Activate();if(callWindow!=null){callWindow.Show();callWindow.Activate();}}
  void Notify(string title,string text,Action action){if(!notificationsEnabled||tray==null)return;notificationAction=action;tray.ShowBalloonTip(7000,title,text,Forms.ToolTipIcon.Info);System.Media.SystemSounds.Asterisk.Play();}
  void NotifyMessages(List<Conversation> chats){
   var target=session;if(target==null)return;int shown=0;
   foreach(var chat in chats){bool displayed=w.IsVisible&&w.IsActive&&w.WindowState!=WindowState.Minimized&&selected!=null&&selected.id==chat.id;
    if(notificationCursor.Observe(chat.id,chat.unread,displayed)&&shown++<5){var c=chat;Notify("@"+chat.username,chat.unread==1?"Новое сообщение":"Новых сообщений: "+chat.unread,()=>{if(session==target&&!busy&&!recording){Select(c);var ignored=Refresh();}});}
   }
  }
  void NotifyCall(){lastCallRing=DateTime.UtcNow;Notify("Входящий звонок","@"+callPeer+" звонит вам",()=>{if(callWindow!=null)callWindow.Activate();});}
  void RingCall(){if(notificationsEnabled&&callState=="incoming"&&(DateTime.UtcNow-lastCallRing).TotalSeconds>=4){lastCallRing=DateTime.UtcNow;System.Media.SystemSounds.Asterisk.Play();}}
 }
}
