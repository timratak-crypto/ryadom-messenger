using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
public static class PhotoTests {
 [STAThread] public static void Main(){
  var bitmap=new RenderTargetBitmap(2400,1200,96,96,PixelFormats.Pbgra32);var drawing=new DrawingVisual();using(var dc=drawing.RenderOpen()){dc.DrawRectangle(Brushes.Green,null,new Rect(0,0,2400,1200));}bitmap.Render(drawing);
  var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));byte[] input;using(var stream=new MemoryStream()){png.Save(stream);input=stream.ToArray();}
  var jpeg=Ryadom.App.PreparePhoto(input);using(var stream=new MemoryStream(jpeg)){var result=BitmapDecoder.Create(stream,BitmapCreateOptions.None,BitmapCacheOption.OnLoad).Frames[0];if(result.PixelWidth!=1600||result.PixelHeight!=800||jpeg.Length>1048576)throw new Exception("Image not resized");}
  bool rejected=false;try{Ryadom.App.PreparePhoto(new byte[]{1,2,3});}catch{rejected=true;}if(!rejected)throw new Exception("Invalid file accepted");
  File.WriteAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-photo.jpg"),jpeg);
  Console.WriteLine("PASS Windows image resize, valid JPEG and malformed input checks");
  using(var capture=new Windows.Media.Capture.MediaCapture()){Console.WriteLine("PASS Windows MediaCapture activation (camera not started)");}
  using(var xaml=typeof(Ryadom.App).Assembly.GetManifestResourceStream("Ryadom.MainWindow.xaml")){
   var window=(Window)System.Windows.Markup.XamlReader.Load(xaml);
   foreach(var name in new[]{"Call","Attach","Draft","Send","PhotoPreview","PendingPhoto","RemovePhoto","Microphone","VoicePreview","VoiceRecording","CancelVoice","RemoveVoice","PlayDraftVoice","RecordingTime"})if(window.FindName(name)==null)throw new Exception("Missing control: "+name);
   window.Close();Console.WriteLine("PASS Windows XAML and attachment controls");
  }
 }
}
