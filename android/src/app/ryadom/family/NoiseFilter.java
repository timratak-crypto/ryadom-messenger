package app.ryadom.family;

// Level-based expander, not speaker recognition. One instance per PCM16/16 kHz stream.
final class NoiseFilter {
 private int mode=-1,hold; private double previous,high,power,gain;
 short sample(short input,int selected){
  selected=selected>=0&&selected<=2?selected:2;
  if(mode!=selected){mode=selected;previous=high=power=0;hold=0;gain=selected==1?.1:.025;}
  if(mode==0)return input;
  double x=input;high=.969*(high+x-previous);previous=x;
  double square=high*high;power+=(square>power?.03:.00078)*(square-power);
  double threshold=mode==1?130:520, floor=mode==1?.1:.025;
  if(power>=threshold*threshold)hold=2560;else if(hold>0)hold--;
  double target=hold>0||power>=threshold*threshold*.36&&gain>.5?1:floor;
  gain+=(target>gain?.02:.00052)*(target-gain);
  return (short)Math.max(-32768,Math.min(32767,high*gain));
 }
 void process(short[] data,int count,int mode){
  if(data==null||count<0||count>data.length)throw new IllegalArgumentException("Invalid PCM16");
  for(int i=0;i<count;i++)data[i]=sample(data[i],mode);
 }
 void process(byte[] data,int count,int mode){
  if(data==null||count<0||count>data.length||count%2!=0)throw new IllegalArgumentException("Invalid PCM16");
  for(int i=0;i<count;i+=2){short value=sample((short)((data[i]&255)|(data[i+1]&255)<<8),mode);data[i]=(byte)value;data[i+1]=(byte)(value>>8);}
 }
}
