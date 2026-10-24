using System;
using Ryadom;
public static class CallSessionTests {
 static int assertions;
 static void Check(bool ok,string message){assertions++;if(!ok)throw new Exception(message);}
 static CallChange Event(CallSession s,string type,string id="",string peer="",string reason="",string request="",int retry=0){return s.Receive(type,id,peer,reason,request,retry);}
 public static void Main(){
  var s=new CallSession();
  Check(Event(s,"incoming","first","bobby")==CallChange.Incoming,"first incoming");
  Check(s.Answer(),"answer");Check(Event(s,"active","first")==CallChange.Active,"conversation");
  Check(Event(s,"ended","first","","finished")==CallChange.Ended,"end incoming");
  Check(!s.Busy&&s.State=="ended","result does not occupy line");
  Check(s.Begin("bobby"),"immediate callback");var request=s.RequestId;
  Check(Event(s,"ended","first","","finished")==CallChange.None&&s.State=="dialing","old end cannot close callback");
  Check(Event(s,"error","","","unavailable","previous")==CallChange.None,"old error cannot close callback");
  Check(Event(s,"ringing","second","bobby","",request)==CallChange.Ringing,"callback ringing");
  Check(Event(s,"active","second")==CallChange.Active,"callback active");
  Check(Event(s,"error","","","busy")==CallChange.None&&s.State=="active","uncorrelated error cannot end conversation");
  Check(Event(s,"incoming","second","bobby")==CallChange.None,"duplicate incoming is idempotent");
  Check(Event(s,"active","second")==CallChange.None,"duplicate active cannot reopen audio");
  Check(Event(s,"incoming","other","carol")==CallChange.Reject&&s.Id=="second","busy rejects another call");
  Check(Event(s,"ended","second","","finished")==CallChange.Ended,"callback ends");
  for(int i=0;i<6;i++){
   Check(s.Begin("bobby"),"repeated dial");request=s.RequestId;
   Check(Event(s,"ringing","repeat"+i,"bobby","",request)==CallChange.Ringing,"repeat rings");
   Check(Event(s,"active","repeat"+i)==CallChange.Active,"repeat active");
   Check(Event(s,"ended","repeat"+i,"","finished")==CallChange.Ended,"repeat ends");
  }
  Check(s.Begin("bobby"),"dial before rate error");
  Check(Event(s,"error","","","rate",s.RequestId,2400)==CallChange.Ended,"rate result");
  Check(s.State=="ended"&&s.Reason=="rate"&&!s.Busy,"rate remains a result screen");
  Check(s.RetrySeconds>=2&&s.RetrySeconds<=3&&!s.Begin("bobby"),"server cooldown blocks clicks before creating a call");
  s.RetryAt=DateTime.UtcNow.AddMilliseconds(-1);Check(s.Begin("bobby"),"retry enabled after cooldown");
  Check(Event(s,"error","","","offline")==CallChange.Ended&&s.Reason=="offline","legacy server compatibility");
  Check(Event(s,"incoming","third","carol")==CallChange.Incoming,"incoming replaces finished result");
  s.Reset();Check(!s.Busy&&s.Peer==""&&s.RetrySeconds==0,"logout clears state");
  Console.WriteLine("PASS "+assertions+" Windows call lifecycle assertions: callback, repeated calls, stale events, rate countdown (no UI or microphone).");
 }
}
