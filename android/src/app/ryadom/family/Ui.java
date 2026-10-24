package app.ryadom.family;

import android.animation.ValueAnimator;
import android.content.Context;
import android.content.res.ColorStateList;
import android.graphics.*;
import android.graphics.drawable.*;
import android.view.*;
import android.view.animation.DecelerateInterpolator;
import android.widget.*;

final class Ui {
 static final int ACCENT=Color.rgb(23,107,83),INK=Color.rgb(24,60,50),MUTED=Color.rgb(108,130,120),PAPER=Color.rgb(240,246,242),SOFT=Color.rgb(232,243,237);
 static int dp(Context c,float n){return (int)(n*c.getResources().getDisplayMetrics().density+.5f);}
 static boolean motion(){return ValueAnimator.areAnimatorsEnabled();}
 static GradientDrawable round(Context c,int color,int radius){GradientDrawable d=new GradientDrawable();d.setColor(color);d.setCornerRadius(dp(c,radius));return d;}
 static GradientDrawable gradient(Context c,int from,int to,int radius){GradientDrawable d=new GradientDrawable(GradientDrawable.Orientation.TL_BR,new int[]{from,to});d.setCornerRadius(dp(c,radius));return d;}
 static TextView text(Context c,String text,int size,int color){TextView t=new TextView(c);t.setText(text);t.setTextSize(size);t.setTextColor(color);t.setFontFeatureSettings("kern");return t;}
 static TextView avatar(Context c,String name,int size){int[] colors={0xff277b61,0xff5988a3,0xffa77c56,0xff8a78a7,0xff4c9690};int hash=0;for(char ch:name.toCharArray())hash=(hash*31+ch)&0x7fffffff;TextView t=text(c,name.isEmpty()?"Р":name.substring(0,Math.min(2,name.length())).toUpperCase(java.util.Locale.ROOT),(int)(size*.32),Color.WHITE);t.setTypeface(android.graphics.Typeface.create("sans-serif-medium",0));t.setGravity(Gravity.CENTER);t.setBackground(gradient(c,colors[hash%colors.length],0xff96bea5,(int)(size*.36)));t.setLayoutParams(new LinearLayout.LayoutParams(dp(c,size),dp(c,size)));return t;}
 static void enter(View v,int distance){if(!motion())return;v.setAlpha(0);v.setTranslationY(dp(v.getContext(),distance));v.animate().alpha(1).translationY(0).setDuration(260).setInterpolator(new DecelerateInterpolator()).start();}
 static Button button(Context c,String label,View.OnClickListener click,boolean quiet){Button b=new Button(c);b.setText(label);b.setAllCaps(false);b.setTextSize(14);b.setTypeface(Typeface.create("sans-serif-medium",0));b.setTextColor(quiet?ACCENT:Color.WHITE);b.setMinimumWidth(0);b.setMinimumHeight(dp(c,48));b.setMinHeight(dp(c,48));b.setPadding(dp(c,14),dp(c,10),dp(c,14),dp(c,10));b.setStateListAnimator(null);b.setBackground(new RippleDrawable(ColorStateList.valueOf(0x2248a880),round(c,quiet?SOFT:ACCENT,16),null));b.setOnClickListener(click);b.setOnTouchListener((v,e)->{if(motion()){float scale=e.getAction()==MotionEvent.ACTION_DOWN?.96f:1f;v.animate().scaleX(scale).scaleY(scale).setDuration(120).start();}return false;});return b;}
 static Button icon(Context c,String kind,String description,View.OnClickListener click,boolean quiet){Button b=button(c,"",click,quiet);b.setContentDescription(description);b.setPadding(dp(c,10),dp(c,10),dp(c,10),dp(c,10));b.setCompoundDrawablesWithIntrinsicBounds(new Icon(c,kind,quiet?ACCENT:Color.WHITE),null,null,null);return b;}
 static void dialog(android.app.Dialog dialog,View content){if(dialog.getWindow()!=null){dialog.getWindow().setBackgroundDrawable(round(content.getContext(),PAPER,28));dialog.getWindow().setDimAmount(.35f);}enter(content,14);}
 static final class Icon extends Drawable {
  final Paint p=new Paint(3);final String kind;final int size;
  Icon(Context c,String kind,int color){this.kind=kind;size=dp(c,24);p.setColor(color);p.setStyle(Paint.Style.STROKE);p.setStrokeWidth(1.8f);p.setStrokeCap(Paint.Cap.ROUND);p.setStrokeJoin(Paint.Join.ROUND);setBounds(0,0,size,size);}
  public int getIntrinsicWidth(){return size;}public int getIntrinsicHeight(){return size;}public int getOpacity(){return PixelFormat.TRANSLUCENT;}public void setAlpha(int n){p.setAlpha(n);}public void setColorFilter(ColorFilter f){p.setColorFilter(f);}
  public void draw(Canvas c){c.save();c.translate(getBounds().left,getBounds().top);c.scale(getBounds().width()/24f,getBounds().height()/24f);Path path=new Path();
   switch(kind){
    case "search":c.drawCircle(10,10,6,p);c.drawLine(15,15,21,21,p);break;
    case "plus":c.drawLine(12,4,12,20,p);c.drawLine(4,12,20,12,p);break;
    case "send":path.moveTo(4,3);path.lineTo(21,12);path.lineTo(4,21);path.lineTo(7,12);path.close();c.drawPath(path,p);c.drawLine(7,12,20,12,p);break;
    case "back":path.moveTo(15,5);path.lineTo(8,12);path.lineTo(15,19);c.drawPath(path,p);break;
    case "mic":c.drawRoundRect(9,3,15,15,3,3,p);c.drawArc(6,7,18,19,0,180,false,p);c.drawLine(12,19,12,22,p);c.drawLine(9,22,15,22,p);break;
    case "phone":path.moveTo(5,3);path.lineTo(9,3);path.lineTo(11,8);path.lineTo(8,10);path.cubicTo(10,14,12,16,16,17);path.lineTo(18,14);path.lineTo(22,16);path.lineTo(22,20);path.cubicTo(11,24,0,10,5,3);c.drawPath(path,p);break;
    default:c.drawRoundRect(3,4,21,18,4,4,p);c.drawLine(6,18,6,22,p);c.drawLine(6,22,11,18,p);break;
   }c.restore();
  }
 }
}
