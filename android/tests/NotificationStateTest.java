package app.ryadom.family;
public final class NotificationStateTest {
 static void check(boolean ok){if(!ok)throw new AssertionError("Notification state regression");}
 public static void main(String[] args){
  NotificationState s=new NotificationState();check(!s.observe("a",0,false));check(s.observe("a",1,false));check(!s.observe("a",1,false));
  check(s.observe("a",2,false));check(!s.observe("a",0,false));check(s.observe("a",1,false));
  check(!s.observe("a",2,true));check(!s.observe("a",2,false));check(s.observe("b",1,false));
  check(!s.observe("c",-1,false));NotificationState restored=new NotificationState();restored.counts.putAll(s.counts);check(!restored.observe("b",1,false));check(restored.observe("b",2,false));
  check(new NotificationState().observe("b",1,false));
  System.out.println("PASS notifications: unread increase, duplicate polls, reads, visible conversation, other sender, restored counts and separate account.");
 }
}
