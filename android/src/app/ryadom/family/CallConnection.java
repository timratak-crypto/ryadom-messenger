package app.ryadom.family;
import java.net.URI;
import java.util.Collections;
import javax.net.ssl.SSLParameters;
import javax.net.ssl.SSLSocketFactory;
import org.java_websocket.WebSocketImpl;
import org.java_websocket.client.WebSocketClient;

abstract class CallConnection extends WebSocketClient {
 CallConnection(String token){this(URI.create("wss://ryadom-family-chat.onrender.com/api/calls"),token);}
 CallConnection(URI endpoint,String token){
  super(endpoint,Collections.singletonMap("Authorization","Bearer "+token));
  if(!endpoint.toString().equals("wss://ryadom-family-chat.onrender.com/api/calls")&&!"127.0.0.1".equals(endpoint.getHost()))throw new IllegalArgumentException("Invalid call endpoint");
  if("wss".equals(endpoint.getScheme()))setSocketFactory(SSLSocketFactory.getDefault());
  setTcpNoDelay(true);setConnectionLostTimeout(25);
 }
 @Override protected void onSetSSLParameters(SSLParameters parameters){super.onSetSSLParameters(parameters);parameters.setEndpointIdentificationAlgorithm("HTTPS");}
 boolean sendAudio(byte[] frame){
  if(!isOpen()||frame.length==0||frame.length>6400||frame.length%2!=0)return false;
  // A pending ping/control message must not suppress the microphone stream.
  if(((WebSocketImpl)getConnection()).outQueue.size()>=4)return false;
  send(frame);return true;
 }
}
