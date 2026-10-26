using System;
using System.IO;

namespace Ryadom {
 // Level-based expander, not speaker recognition. State belongs to one capture stream.
 // PCM16 mono, 16 kHz. No look-ahead buffering, gain boost or wire-format changes.
 public sealed class NoiseFilter {
  int mode=-1,hold; double previous,high,power,gain;
  public short Sample(short input,int selected){
   selected=selected>=0&&selected<=2?selected:2;
   if(mode!=selected){mode=selected;previous=high=power=0;hold=0;gain=selected==1?.1:.025;}
   if(mode==0)return input;
   double x=input;high=.969*(high+x-previous);previous=x;
   double square=high*high;power+=(square>power?.03:.00078)*(square-power);
   double threshold=mode==1?130:520, floor=mode==1?.1:.025;
   if(power>=threshold*threshold)hold=2560;
   else if(hold>0)hold--;
   // Hysteresis prevents chatter around the opening threshold. Release stays smooth.
   double target=hold>0||power>=threshold*threshold*.36&&gain>.5?1:floor;
   gain+=(target>gain?.02:.00052)*(target-gain);
   return (short)Math.Max(-32768,Math.Min(32767,high*gain));
  }
  public void Process(byte[] data,int count,int mode){
   if(data==null||count<0||count>data.Length||count%2!=0)throw new ArgumentException("Invalid PCM16");
   for(int i=0;i<count;i+=2){short value=Sample((short)(data[i]|data[i+1]<<8),mode);data[i]=(byte)value;data[i+1]=(byte)(value>>8);}
  }
 }
 public static class NoisePreferences {
  public static readonly string[] Names={"Выключено","Мягкое","Сильное"};
  public const string Hint="Приглушает тихий фон в звонках и новых голосовых. Говорите ближе к микрофону. Если речь обрезается — выберите мягкое. Громкие и одновременные голоса могут оставаться слышны.";
  static readonly string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Ryadom","noise.txt");
  public static volatile int Mode=2;
  static NoisePreferences(){try{int n;if(int.TryParse(File.ReadAllText(path),out n)&&n>=0&&n<=2)Mode=n;}catch{}}
  public static void Save(int mode){Mode=mode>=0&&mode<=2?mode:2;Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,Mode.ToString());}
 }
}
