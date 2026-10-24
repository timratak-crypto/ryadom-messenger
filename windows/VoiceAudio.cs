using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Ryadom {
 // Audio is captured only after an explicit click. The worker owns all native buffers.
 public sealed class VoiceCapture {
  [StructLayout(LayoutKind.Sequential,Pack=2)] struct Format {public ushort tag,channels;public uint rate,bytes;public ushort align,bits,extra;}
  [StructLayout(LayoutKind.Sequential)] struct Header {public IntPtr data;public uint length,recorded;public UIntPtr user;public uint flags,loops;public IntPtr next;public UIntPtr reserved;}
  [DllImport("winmm.dll")] static extern uint waveInOpen(out IntPtr handle,uint device,ref Format format,IntPtr callback,IntPtr instance,uint flags);
  [DllImport("winmm.dll")] static extern uint waveInPrepareHeader(IntPtr handle,IntPtr header,uint size);
  [DllImport("winmm.dll")] static extern uint waveInUnprepareHeader(IntPtr handle,IntPtr header,uint size);
  [DllImport("winmm.dll")] static extern uint waveInAddBuffer(IntPtr handle,IntPtr header,uint size);
  [DllImport("winmm.dll")] static extern uint waveInStart(IntPtr handle);
  [DllImport("winmm.dll")] static extern uint waveInReset(IntPtr handle);
  [DllImport("winmm.dll")] static extern uint waveInClose(IntPtr handle);
  volatile bool stopped,discard; public volatile int RecordedBytes;
  public void Stop(bool save){discard=!save;stopped=true;}
  static void Check(uint status){if(status!=0)throw new Exception("Не удалось включить микрофон. Проверьте его подключение и разрешение для классических приложений в настройках Windows → Конфиденциальность → Микрофон.");}
  public byte[] Record(){
   IntPtr handle=IntPtr.Zero;var buffers=new List<IntPtr>();var prepared=new List<IntPtr>();var pointers=new List<IntPtr>();uint size=(uint)Marshal.SizeOf(typeof(Header));
   using(var pcm=new MemoryStream()){
    try{
     if(stopped)return null;
     var format=new Format{tag=1,channels=1,rate=16000,bytes=32000,align=2,bits=16,extra=0};Check(waveInOpen(out handle,uint.MaxValue,ref format,IntPtr.Zero,IntPtr.Zero,0));
     for(int i=0;i<8;i++){var data=Marshal.AllocHGlobal(3200);pointers.Add(data);var ptr=Marshal.AllocHGlobal((int)size);buffers.Add(ptr);Marshal.StructureToPtr(new Header{data=data,length=3200},ptr,false);Check(waveInPrepareHeader(handle,ptr,size));prepared.Add(ptr);Check(waveInAddBuffer(handle,ptr,size));}
     int next=0;byte[] block=new byte[3200];Check(waveInStart(handle));
     while(!stopped&&pcm.Length<1920000){
      var ptr=buffers[next];var header=(Header)Marshal.PtrToStructure(ptr,typeof(Header));
      if((header.flags&1)==0){Thread.Sleep(10);continue;}
      int count=Math.Min((int)header.recorded,1920000-(int)pcm.Length);if(count>0){Marshal.Copy(header.data,block,0,count);pcm.Write(block,0,count);RecordedBytes=(int)pcm.Length;}
      if(pcm.Length>=1920000)break;
      Check(waveInAddBuffer(handle,ptr,size));next=(next+1)%buffers.Count;
     }
     Check(waveInReset(handle));
     // Reset returns queued buffers, including the final partly filled buffer.
     for(int i=0;i<buffers.Count&&pcm.Length<1920000;i++){
      var header=(Header)Marshal.PtrToStructure(buffers[(next+i)%buffers.Count],typeof(Header));
      int count=Math.Min((int)header.recorded,1920000-(int)pcm.Length);if((header.flags&1)!=0&&count>0){Marshal.Copy(header.data,block,0,count);pcm.Write(block,0,count);}
     }
     if(discard)return null;if(pcm.Length<8000)throw new Exception("Запись слишком короткая. Скажите сообщение и нажмите стоп.");return Encode(pcm.ToArray());
    }finally{
     if(handle!=IntPtr.Zero){waveInReset(handle);foreach(var ptr in prepared)waveInUnprepareHeader(handle,ptr,size);waveInClose(handle);}
     foreach(var ptr in buffers)Marshal.FreeHGlobal(ptr);foreach(var ptr in pointers)Marshal.FreeHGlobal(ptr);
    }
   }
  }
  public static byte[] Encode(byte[] pcm){
   if(pcm.Length%2!=0||pcm.Length>1920000)throw new ArgumentException("Invalid PCM length");
   using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream)){
    writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+pcm.Length);writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(16000);writer.Write(32000);writer.Write((short)2);writer.Write((short)16);writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(pcm.Length);writer.Write(pcm);return stream.ToArray();
   }
  }
  public static double Seconds(byte[] data){
   if(data==null||data.Length<44)return 0;int at=12;
   while(at<=data.Length-8){int count=BitConverter.ToInt32(data,at+4);if(count<0||count>data.Length-at-8)return 0;if(Encoding.ASCII.GetString(data,at,4)=="data")return count/32000.0;at+=8+count+count%2;}return 0;
  }
  public static string Time(double seconds){int n=Math.Max(0,(int)Math.Round(seconds));return (n/60)+":"+(n%60).ToString("00");}
 }
}
