using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Ryadom;

// Feed-shaped JSON passes through the real HTTP response decoder. No application,
// saved accounts, network connections, camera or microphone are started.
public static class ApiTests {
 static int assertions;
 static void Check(bool value,string label){assertions++;if(!value)throw new Exception(label);}
 static string Summary(Message message,bool own){return (string)typeof(App).GetMethod("CallSummary",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{message,own});}
 public static int Main(string[] args){
  try{
   var http=new FakeHttp{Body=File.ReadAllText(args[0],System.Text.Encoding.UTF8)};
   typeof(Api).GetField("Client",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,new HttpClient(http));
   var account=new Session{token="synthetic-only",user=new Profile{id="qa-a",username="fixture"}};
   var feed=Api.Call<Result>(account,null,"?view=messages&conversation=fixture&after=10&changesAfter=20").GetAwaiter().GetResult();
   Check(feed.messages.Count==5,"mixed history decoded");
   Check(Object.Equals(feed.messages[0].callDuration,null),"ordinary text has no call duration");
   Check(feed.messages[0].text=="Обычное сообщение","Unicode text intact");
   Check(feed.messages[1].photo=="qa-photo"&&feed.messages[1].width==640&&feed.messages[1].height==480,"photo metadata intact");
   Check(Object.Equals(feed.messages[1].callDuration,null)&&feed.messages[1].voice==null,"photo optional fields");
   Check(feed.messages[2].voice=="qa-voice"&&feed.messages[2].duration==1250,"voice milliseconds intact");
   Check(Object.Equals(feed.messages[2].callDuration,null),"voice is not call duration");
   Check(Object.Equals(feed.messages[3].callDuration,65)&&Summary(feed.messages[3],true)=="Разговор · 1:05","call duration seconds intact");
   Check(Summary(feed.messages[4],false)=="Вам звонили · Пропущенный вызов","incoming missed label");
   Check(Summary(feed.messages[4],true)=="Исходящий вызов · Без ответа","outgoing missed label");
   Check(feed.updates.Count==2&&feed.updates[0].editedAt>0&&feed.updates[0].text=="Правка","edited update decoded");
   Check(feed.updates[1].deletedAt>0&&feed.updates[1].photo==null,"deleted update decoded");
   Check(Object.Equals(feed.updates[0].callDuration,null)&&Object.Equals(feed.updates[1].callDuration,null),"updates accept null call metadata");
   Check(feed.changesCursor==25&&feed.peerRead==13&&!feed.hasMore,"independent cursor values intact");
   http.Body="{\"messages\":[{\"seq\":1,\"sender\":\"qa-a\",\"clientId\":\"legacy\",\"text\":\"old\",\"at\":1792920000000}],\"peerRead\":0,\"hasMore\":false}";
   var legacy=Api.Call<Result>(account).GetAwaiter().GetResult();
   Check(legacy.messages.Count==1&&Object.Equals(legacy.messages[0].callDuration,null),"legacy missing call field");
   Check(legacy.messages[0].editedAt==0&&legacy.messages[0].deletedAt==0,"legacy missing edit fields");
   Check(Summary(new Message{kind="call",callStatus="answered"},true)=="Разговор · 0:00","missing call duration displays safely");
   http.Body="{\"user\":{\"id\":\"qa-a\",\"username\":\"fixture\",\"avatar\":null},\"chats\":[{\"id\":\"chat\",\"peerId\":\"qa-b\",\"username\":\"peer\",\"avatar\":null,\"lastText\":\"hello\",\"at\":1792930000000,\"unread\":1}]}";
   var inbox=Api.Call<Result>(account).GetAwaiter().GetResult();
   Check(inbox.user.avatar==null&&inbox.chats[0].avatar==null&&inbox.chats[0].unread==1,"nullable avatars decoded");
   http.Status=HttpStatusCode.Forbidden;http.Body="{\"error\":\"Fixture denied\"}";
   try{Api.Call<Result>(account,new{action="editMessage"}).GetAwaiter().GetResult();throw new Exception("HTTP error accepted");}
   catch(ApiError error){Check(error.Code==403&&error.Message=="Fixture denied","HTTP errors retained");}
   Console.WriteLine("PASS "+assertions+" Windows API assertions: real JSON decoding of text/photo/voice/calls, null optional metadata, edits/deletions, legacy fields, avatars and errors (in-memory HTTP).");return 0;
  }catch(Exception e){Console.Error.WriteLine("FAIL: "+e);return 1;}
 }
 sealed class FakeHttp:HttpMessageHandler {
  public string Body;public HttpStatusCode Status=HttpStatusCode.OK;
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
   return Task.FromResult(new HttpResponseMessage(Status){Content=new StringContent(Body,System.Text.Encoding.UTF8,"application/json")});
  }
 }
}
