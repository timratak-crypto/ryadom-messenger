using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Ryadom {
 public static class Design {
  public static SolidColorBrush Brush(string hex){var b=new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));b.Freeze();return b;}
  public static readonly Brush Ink=Brush("#183C32"),Muted=Brush("#7A9185"),Accent=Brush("#176B53"),Soft=Brush("#E9F3EE");
  public static LinearGradientBrush Gradient(string first,string last){var b=new LinearGradientBrush((Color)ColorConverter.ConvertFromString(first),(Color)ColorConverter.ConvertFromString(last),45);b.Freeze();return b;}
  public static Border Avatar(string name,double size){
   string[] colors={"#277B61","#5988A3","#A77C56","#8A78A7","#4C9690"};int hash=0;foreach(char c in name)hash=(hash*31+c)&0x7fffffff;
   return new Border{Width=size,Height=size,CornerRadius=new CornerRadius(size*.36),Background=Gradient(colors[hash%colors.Length],"#96BEA5"),Child=new TextBlock{Text=string.IsNullOrEmpty(name)?"Р":name.Substring(0,Math.Min(2,name.Length)).ToUpperInvariant(),FontSize=size*.32,FontWeight=FontWeights.SemiBold,Foreground=Brushes.White,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
  }
  public static TextBlock Text(string value,double size,Brush color){return new TextBlock{Text=value,FontSize=size,Foreground=color,TextWrapping=TextWrapping.Wrap};}
  public static void Enter(UIElement view,double offset=12,int delay=0){
   if(!SystemParameters.ClientAreaAnimation)return;
   var move=new TranslateTransform();view.RenderTransform=move;
   var duration=TimeSpan.FromMilliseconds(260);var ease=new CubicEase{EasingMode=EasingMode.EaseOut};
   move.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(offset,0,duration){EasingFunction=ease,BeginTime=TimeSpan.FromMilliseconds(delay)});
   view.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,duration){BeginTime=TimeSpan.FromMilliseconds(delay)});
  }
  public static void Pulse(UIElement view,bool enabled){
   if(view==null)return;var scale=view.RenderTransform as ScaleTransform;
   if(!enabled||!SystemParameters.ClientAreaAnimation){if(scale!=null){scale.BeginAnimation(ScaleTransform.ScaleXProperty,null);scale.BeginAnimation(ScaleTransform.ScaleYProperty,null);}view.RenderTransform=Transform.Identity;return;}
   if(scale!=null)return;scale=new ScaleTransform(1,1);view.RenderTransformOrigin=new Point(.5,.5);view.RenderTransform=scale;
   var pulse=new DoubleAnimation(1,1.055,TimeSpan.FromMilliseconds(1150)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}};
   scale.BeginAnimation(ScaleTransform.ScaleXProperty,pulse);scale.BeginAnimation(ScaleTransform.ScaleYProperty,pulse);
  }
 }
 public partial class App {
  string inboxSignature="",messageDay="";
  System.Collections.Generic.List<Conversation> displayedInbox=new System.Collections.Generic.List<Conversation>();
  Button ContactRow(string username,string preview,int unread,long at,bool active,Action click){
   var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   var avatar=Design.Avatar(username,44);avatar.Margin=new Thickness(0,0,12,0);grid.Children.Add(avatar);
   var labels=new StackPanel{VerticalAlignment=VerticalAlignment.Center};var title=Design.Text("@"+username,14,Design.Ink);title.FontWeight=FontWeights.SemiBold;title.TextWrapping=TextWrapping.NoWrap;title.TextTrimming=TextTrimming.CharacterEllipsis;labels.Children.Add(title);
   var subtitle=Design.Text(preview,12,Design.Muted);subtitle.TextWrapping=TextWrapping.NoWrap;subtitle.TextTrimming=TextTrimming.CharacterEllipsis;subtitle.Margin=new Thickness(0,5,0,0);labels.Children.Add(subtitle);Grid.SetColumn(labels,1);grid.Children.Add(labels);
   var detail=new StackPanel{Margin=new Thickness(8,0,0,0),VerticalAlignment=VerticalAlignment.Center};
   if(at>0){var time=Design.Text(DateTimeOffset.FromUnixTimeMilliseconds(at).ToLocalTime().ToString("HH:mm"),10,Design.Muted);time.HorizontalAlignment=HorizontalAlignment.Right;detail.Children.Add(time);}
   if(unread>0){var number=Design.Text(unread>99?"99+":unread.ToString(),10,Brushes.White);number.FontWeight=FontWeights.Bold;detail.Children.Add(new Border{Child=number,Background=Design.Accent,CornerRadius=new CornerRadius(9),Padding=new Thickness(6,3,6,3),Margin=new Thickness(0,6,0,0),HorizontalAlignment=HorizontalAlignment.Right});}
   Grid.SetColumn(detail,2);grid.Children.Add(detail);
   var row=new Button{Content=grid,HorizontalContentAlignment=HorizontalAlignment.Stretch,Background=active?Design.Soft:Brushes.Transparent,Padding=new Thickness(10,12,10,12),Margin=new Thickness(0,0,0,4)};row.Click+=(a,e)=>click();return row;
  }
  void RenderInbox(System.Collections.Generic.List<Conversation> chats){
   displayedInbox=chats;
   var signature=(selected==null?"":selected.id)+Json.Serialize(chats);if(inboxSignature==signature)return;inboxSignature=signature;
   var panel=Get<StackPanel>("Chats");panel.Children.Clear();
   if(chats.Count==0){var empty=new StackPanel{Margin=new Thickness(12,26,12,0)};empty.Children.Add(Design.Text("Пока тихо",20,Design.Ink));empty.Children.Add(new TextBlock{Text="Найдите близкого человека\nи начните первый разговор.",FontSize=13,LineHeight=21,Foreground=Design.Muted,Margin=new Thickness(0,10,0,0)});panel.Children.Add(empty);}
   foreach(var item in chats){var c=item;panel.Children.Add(ContactRow(c.username,string.IsNullOrEmpty(c.lastText)?"Напишите первое сообщение":c.lastText,c.unread,c.at,selected!=null&&selected.id==c.id,()=>{if(!busy)Select(c);}));}
  }
  void StyleDialog(Window dialog){foreach(object key in w.Resources.Keys)dialog.Resources[key]=w.Resources[key];dialog.Background=Design.Brush("#F6FAF7");dialog.Foreground=Design.Ink;dialog.FontFamily=new FontFamily("Segoe UI");}
 }
}
