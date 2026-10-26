package app.ryadom.family;
import android.media.audiofx.NoiseSuppressor;
import android.media.audiofx.AutomaticGainControl;

// Optional device effects. The software expander still works when these are unavailable.
final class CaptureNoiseEffects {
 private NoiseSuppressor noise; private AutomaticGainControl gain;
 private boolean originalNoise,originalGain;
 CaptureNoiseEffects(int session,int mode){
  try{if(NoiseSuppressor.isAvailable()){noise=NoiseSuppressor.create(session);if(noise!=null)originalNoise=noise.getEnabled();}}catch(RuntimeException ignored){}
  try{if(AutomaticGainControl.isAvailable()){gain=AutomaticGainControl.create(session);if(gain!=null)originalGain=gain.getEnabled();}}catch(RuntimeException ignored){}
  mode(mode);
 }
 void mode(int mode){
  try{if(noise!=null&&noise.hasControl())noise.setEnabled(mode==0?originalNoise:true);}catch(RuntimeException ignored){}
  // Strong mode must not automatically amplify distant voices between near speech.
  try{if(gain!=null&&gain.hasControl())gain.setEnabled(mode==2?false:originalGain);}catch(RuntimeException ignored){}
 }
 boolean enabled(){try{return noise!=null&&noise.getEnabled();}catch(RuntimeException ignored){return false;}}
 void close(){
  if(noise!=null){try{noise.release();}catch(RuntimeException ignored){}noise=null;}
  if(gain!=null){try{gain.release();}catch(RuntimeException ignored){}gain=null;}
 }
}
