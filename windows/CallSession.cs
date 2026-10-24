using System;

namespace Ryadom {
 public enum CallChange { None, Incoming, Ringing, Active, Ended, Reject }

 // Each attempt owns its events. A finished call is a result screen, not a busy line.
 public sealed class CallSession {
  public string Id="",Peer="",State="",Reason="",RequestId="";
  public DateTime RetryAt=DateTime.MinValue;
  public int RetrySeconds { get { return Math.Max(0,(int)Math.Ceiling((RetryAt-DateTime.UtcNow).TotalSeconds)); } }
  public bool Busy { get { return State.Length>0&&State!="ended"; } }
  public bool Begin(string peer){
   if(Busy||RetrySeconds>0||string.IsNullOrEmpty(peer))return false;
   Id="";Peer=peer;State="dialing";Reason="";RequestId=Guid.NewGuid().ToString("N");return true;
  }
  public bool Answer(){if(State!="incoming")return false;State="connecting";return true;}
  public void Finish(string reason){Id="";RequestId="";State="ended";Reason=reason;}
  public void Reset(){Id="";Peer="";State="";Reason="";RequestId="";RetryAt=DateTime.MinValue;}
  public CallChange Receive(string type,string id,string peer,string reason,string requestId,int retryAfterMs=0){
   if(type=="incoming"){
    if(id.Length==0)return CallChange.None;
    if(id==Id)return CallChange.None;
    if(Busy)return CallChange.Reject;
    Id=id;Peer=peer;State="incoming";Reason="";RequestId="";return CallChange.Incoming;
   }
   if(type=="ringing"){
    if(id.Length==0||id==Id)return CallChange.None;
    if(State!="dialing"||peer!=Peer||(requestId.Length>0&&requestId!=RequestId))return CallChange.Reject;
    Id=id;State="ringing";return CallChange.Ringing;
   }
   if(type=="error"){
    // Protocol-1 servers without request IDs send errors only while resolving a dial.
    if(State!="dialing"||(requestId.Length>0&&requestId!=RequestId))return CallChange.None;
    if(reason=="rate")RetryAt=DateTime.UtcNow.AddMilliseconds(retryAfterMs>0?Math.Min(retryAfterMs,60000):60000);
    Finish(reason);return CallChange.Ended;
   }
   if(!Busy||Id.Length==0||id!=Id)return CallChange.None;
   if(type=="ended"){Finish(reason);return CallChange.Ended;}
   if(type=="active"&&(State=="ringing"||State=="connecting"||State=="incoming")){State="active";return CallChange.Active;}
   return CallChange.None;
  }
 }
}
