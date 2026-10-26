package app.ryadom.family;
import android.content.Context;
final class NoisePreferences {
 static final String[] NAMES={"Выключено","Мягкое","Сильное"};
 static final String HINT="Приглушает тихий фон в звонках и новых голосовых. Говорите ближе к микрофону. Если речь обрезается — выберите мягкое. Громкие и одновременные голоса могут оставаться слышны.";
 static int get(Context c){int n=c.getSharedPreferences("audio",Context.MODE_PRIVATE).getInt("noise",2);return n>=0&&n<=2?n:2;}
 static void set(Context c,int mode){c.getSharedPreferences("audio",Context.MODE_PRIVATE).edit().putInt("noise",mode>=0&&mode<=2?mode:2).apply();}
}
