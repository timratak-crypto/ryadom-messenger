using System;
using Ryadom;
class NotificationTests {
 static void Check(bool ok){if(!ok)throw new Exception("Notification cursor regression");}
 static void Main(){var s=new NotificationCursor();Check(!s.Observe("a",0,false));Check(s.Observe("a",1,false));Check(!s.Observe("a",1,false));Check(s.Observe("a",2,false));Check(!s.Observe("a",0,false));Check(s.Observe("a",1,false));Check(!s.Observe("a",2,true));Check(!s.Observe("a",2,false));Check(s.Observe("b",1,false));s.Clear();Check(s.Observe("b",1,false));Console.WriteLine("PASS Windows notifications: unread changes, duplicate suppression, visible chat, reads and account reset. No desktop notification emitted.");}
}
