package app.ryadom.family;
import java.util.HashMap;
import java.util.Map;

// Counts are observed without fetching messages (fetching a conversation marks it read).
final class NotificationState {
 final Map<String,Integer> counts=new HashMap<>();
 boolean observe(String id,int unread,boolean visible){
  unread=Math.max(0,unread);int previous=counts.containsKey(id)?counts.get(id):0;
  counts.put(id,unread);return !visible&&unread>previous;
 }
}
