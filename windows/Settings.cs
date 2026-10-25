using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ryadom {
 public static class Palette {
  public static readonly string[] Names={"Зелёный","Красный","Синий","Фиолетовый","Розовый","Оранжевый","Бирюзовый","Индиго","Малиновый","Оливковый","Графит","Коричневый"};
  public static readonly string[] Colors={"#176B53","#B83E42","#2766BC","#7B49B0","#B33D7A","#AD591D","#167B82","#4F51B7","#A72E55","#687528","#465363","#84604A"};
  public static int Index;
  public static Color Color {get{return (Color)ColorConverter.ConvertFromString(Colors[Index]);}}
  public static Color Mix(Color a,Color b,double part){return System.Windows.Media.Color.FromRgb((byte)(a.R*(1-part)+b.R*part),(byte)(a.G*(1-part)+b.G*part),(byte)(a.B*(1-part)+b.B*part));}
  public static Brush Accent {get{return new SolidColorBrush(Color);}}
  public static Brush Soft {get{return new SolidColorBrush(Mix(Color,ColorsWhite,.91));}}
  static Color ColorsWhite=System.Windows.Media.Colors.White;
  public static Brush Bubble {get{return new LinearGradientBrush(Color,Mix(Color,ColorsWhite,.12),45);}}
  public static void Apply(Window window){
   window.Resources["Accent"]=Accent;window.Resources["AccentSoft"]=Soft;
   window.Resources["AccentDeep"]=Mix(Color,System.Windows.Media.Colors.Black,.35);window.Resources["AccentColor"]=Color;
   window.Resources["AccentLight"]=Mix(Color,ColorsWhite,.70);window.Resources["AccentPale"]=Mix(Color,ColorsWhite,.94);
   window.Resources["Paper"]=new SolidColorBrush(Mix(Color,ColorsWhite,.965));
   window.SetResourceReference(Window.BackgroundProperty,"Paper");
  }
 }
 public partial class App {
  long changesCursor;
  bool rebuildMessages;
  readonly Dictionary<string,BitmapSource> avatarCache=new Dictionary<string,BitmapSource>();
  readonly string stylePath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Ryadom","style.txt");
  void LoadStyle(){try{int value;if(int.TryParse(File.ReadAllText(stylePath),out value)&&value>=0&&value<Palette.Colors.Length)Palette.Index=value;}catch{}Palette.Apply(w);}
  Border ProfileAvatar(string name,string userId,string avatar,double size){
   var view=Design.Avatar(name,size);if(session==null||string.IsNullOrEmpty(avatar)||string.IsNullOrEmpty(userId))return view;
   BitmapSource cached;if(avatarCache.TryGetValue(avatar,out cached)){view.Child=new Image{Source=cached,Stretch=Stretch.UniformToFill};view.Clip=new RectangleGeometry(new Rect(0,0,size,size),size*.36,size*.36);}
   else {var ignored=LoadAvatar(view,userId,avatar,size,session);}return view;
  }
  async Task LoadAvatar(Border view,string userId,string avatar,double size,Session target){
   await imageSlots.WaitAsync();try{var response=await Api.Call<PhotoResponse>(target,null,"?view=avatar&user="+Uri.EscapeDataString(userId));if(session!=target)return;
    if(response.avatar!=null&&response.avatar!=avatar)return;var image=Decode(Convert.FromBase64String(response.data),true);if(avatarCache.Count>64)avatarCache.Clear();avatarCache[avatar]=image;
    view.Child=new Image{Source=image,Stretch=Stretch.UniformToFill};view.Clip=new RectangleGeometry(new Rect(0,0,size,size),size*.36,size*.36);
   }catch{}finally{imageSlots.Release();}
  }
  void AcceptProfile(Profile user){if(session==null||user==null||user.id!=session.user.id)return;if(user.username==session.user.username&&user.avatar==session.user.avatar)return;session.user=user;Save(session);Get<TextBlock>("Me").Text="@"+user.username;}
  void SyncPeer(List<Conversation> chats){if(selected==null)return;var fresh=chats.FirstOrDefault(c=>c.id==selected.id);if(fresh==null)return;
   if(fresh.username!=selected.username||fresh.avatar!=selected.avatar){selected.username=fresh.username;selected.avatar=fresh.avatar;Get<TextBlock>("Peer").Text="@"+fresh.username;Get<Border>("PeerAvatar").Child=ProfileAvatar(fresh.username,fresh.peerId,fresh.avatar,44);}
  }
  static byte[] AvatarJpeg(byte[] source){
   var normalized=Decode(PreparePhoto(source));double ratio=Math.Min(1,512.0/Math.Max(normalized.PixelWidth,normalized.PixelHeight));
   var image=new TransformedBitmap(normalized,new ScaleTransform(ratio,ratio));
   foreach(int quality in new[]{85,65,45,25}){var encoder=new JpegBitmapEncoder{QualityLevel=quality};encoder.Frames.Add(BitmapFrame.Create(image));using(var stream=new MemoryStream()){encoder.Save(stream);if(stream.Length<=204800)return stream.ToArray();}}
   throw new Exception("Не удалось уменьшить аватарку. Выберите другое фото.");
  }
  void ShowSettings(){
   if(session==null||busy||recording||callSession.Busy)return;var dialog=BuildSettings();dialog.Owner=w;dialog.ShowDialog();
  }
  Window BuildSettings(){var target=session;
   var dialog=new Window{Title="Настройки · Рядом",Width=540,Height=640,MinHeight=450,WindowStartupLocation=WindowStartupLocation.CenterOwner};StyleDialog(dialog);Palette.Apply(dialog);
   var root=new DockPanel{Margin=new Thickness(24)};var note=Design.Text("",13,Design.Muted);note.Margin=new Thickness(0,16,0,0);DockPanel.SetDock(note,Dock.Bottom);root.Children.Add(note);var tabs=new TabControl{Background=Brushes.Transparent,BorderThickness=new Thickness(0)};root.Children.Add(tabs);dialog.Content=root;
   var account=new StackPanel{Margin=new Thickness(16)};var avatarHost=new Border{HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,8,0,18)};Action avatar=()=>avatarHost.Child=ProfileAvatar(session.user.username,session.user.id,session.user.avatar,96);avatar();account.Children.Add(avatarHost);
   var upload=new Button{Content="Выбрать аватарку",Margin=new Thickness(0,0,0,10)};var remove=new Button{Content="Убрать аватарку",Margin=new Thickness(0,0,0,22),Style=(Style)w.Resources["Quiet"]};account.Children.Add(upload);account.Children.Add(remove);
   account.Children.Add(Design.Text("Юзернейм",14,Design.Ink));var name=new TextBox{Text=session.user.username,MaxLength=24,Margin=new Thickness(0,8,0,8)};account.Children.Add(name);account.Children.Add(Design.Text("3–24 символа: a–z, цифры и _. Переписки сохранятся.",12,Design.Muted));var rename=new Button{Content="Сохранить юзернейм",Margin=new Thickness(0,18,0,0)};account.Children.Add(rename);
   tabs.Items.Add(new TabItem{Header="Account",Content=new ScrollViewer{Content=account,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});
   Func<object,Task> update=async body=>{if(busy)return;SetBusy(true);account.IsEnabled=false;note.Text="Сохраняем…";try{var result=await Api.Call<Result>(target,body);if(session!=target)return;AcceptProfile(result.user);avatar();name.Text=session.user.username;note.Text="Сохранено";await Refresh();}catch(Exception ex){note.Text=ex.Message;Error(ex);}finally{SetBusy(false);account.IsEnabled=true;}};
   rename.Click+=async(a,e)=>await update(new{action="rename",username=name.Text});
   remove.Click+=async(a,e)=>await update(new{action="removeAvatar"});
   upload.Click+=async(a,e)=>{var file=new Microsoft.Win32.OpenFileDialog{Filter="Фотографии|*.jpg;*.jpeg;*.png;*.bmp;*.heic;*.heif",Title="Аватарка"};if(file.ShowDialog(dialog)!=true)return;try{if(new FileInfo(file.FileName).Length>40*1024*1024)throw new Exception("Выберите фото до 40 МБ.");var data=AvatarJpeg(File.ReadAllBytes(file.FileName));await update(new{action="setAvatar",data=Convert.ToBase64String(data)});}catch(Exception ex){note.Text=ex.Message;}};
   var style=new StackPanel{Margin=new Thickness(16)};style.Children.Add(Design.Text("Цвет мессенджера",23,Design.Ink));style.Children.Add(new TextBlock{Text="Цвет сохраняется на этом устройстве.",Foreground=Design.Muted,Margin=new Thickness(0,10,0,20)});var colors=new WrapPanel();style.Children.Add(colors);
   for(int i=0;i<Palette.Names.Length;i++){int index=i;var choice=new Button{Content=Palette.Names[i],Width=190,Margin=new Thickness(0,0,8,10),Background=Design.Brush(Palette.Colors[i])};choice.Click+=(a,e)=>{Palette.Index=index;Palette.Apply(w);Palette.Apply(dialog);try{Directory.CreateDirectory(Path.GetDirectoryName(stylePath));File.WriteAllText(stylePath,index.ToString());}catch{note.Text="Не удалось сохранить цвет на диске.";}inboxSignature="";RenderInbox(displayedInbox);rebuildMessages=true;if(selected!=null)Render();note.Text="Цвет: "+Palette.Names[index];};colors.Children.Add(choice);}
   tabs.Items.Add(new TabItem{Header="Стиль",Content=new ScrollViewer{Content=style,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});dialog.Closing+=(a,e)=>{if(busy)e.Cancel=true;};return dialog;
  }
  void MessageActions(Message message,Button anchor){
   if(session==null||message.sender!=session.user.id||message.deletedAt>0||message.kind=="call")return;var menu=new ContextMenu();
   var edit=new MenuItem{Header="Изменить"};edit.Click+=(a,e)=>EditMessage(message);menu.Items.Add(edit);
   var remove=new MenuItem{Header="Удалить у всех"};remove.Click+=async(a,e)=>{if(MessageBox.Show(w,"Удалить сообщение у вас и у собеседника?","Удаление",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes)await MutateMessage(message,null);};menu.Items.Add(remove);menu.PlacementTarget=anchor;menu.IsOpen=true;
  }
  async Task<bool> MutateMessage(Message message,string text){
   if(busy||recording||session==null||selected==null)return false;var target=session;var chat=selected;SetBusy(true);
   try{await Api.Call<Result>(target,new{action=text==null?"deleteMessage":"editMessage",conversation=chat.id,seq=message.seq,text});await Refresh();return true;}catch(Exception ex){Error(ex);return false;}finally{SetBusy(false);}
  }
  void EditMessage(Message message){
   if(busy||recording)return;var dialog=new Window{Owner=w,Title="Изменить сообщение",Width=500,Height=320,WindowStartupLocation=WindowStartupLocation.CenterOwner};StyleDialog(dialog);var panel=new DockPanel{Margin=new Thickness(22)};
   var save=new Button{Content="Сохранить",Margin=new Thickness(0,14,0,0)};DockPanel.SetDock(save,Dock.Bottom);panel.Children.Add(save);var input=new TextBox{Text=message.text,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxLength=4000};panel.Children.Add(input);dialog.Content=panel;
   save.Click+=async(a,e)=>{save.IsEnabled=false;bool ok=await MutateMessage(message,input.Text);save.IsEnabled=true;if(ok)dialog.Close();};dialog.Closing+=(a,e)=>{if(busy)e.Cancel=true;};dialog.ShowDialog();
  }
  static string CallSummary(Message message,bool own){return message.callStatus=="answered"?"Разговор · "+VoiceCapture.Time(message.callDuration??0):own?"Исходящий вызов · Без ответа":"Вам звонили · Пропущенный вызов";}
 }
}
