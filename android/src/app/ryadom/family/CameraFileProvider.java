package app.ryadom.family;
import android.content.ContentProvider;
import android.content.ContentValues;
import android.database.Cursor;
import android.database.MatrixCursor;
import android.net.Uri;
import android.os.ParcelFileDescriptor;
import android.provider.OpenableColumns;
import java.io.File;
import java.io.FileNotFoundException;
// Exposes only this one temporary capture file through per-URI grants to the camera.
public final class CameraFileProvider extends ContentProvider {
 public boolean onCreate(){return true;}
 private File file(Uri uri){if(!"/capture.jpg".equals(uri.getPath()))throw new SecurityException("Unknown file");return new File(getContext().getCacheDir(),"capture.jpg");}
 public ParcelFileDescriptor openFile(Uri uri,String mode)throws FileNotFoundException{return ParcelFileDescriptor.open(file(uri),ParcelFileDescriptor.parseMode(mode));}
 public String getType(Uri uri){file(uri);return "image/jpeg";}
 public Cursor query(Uri uri,String[] projection,String selection,String[] args,String sort){File f=file(uri);MatrixCursor c=new MatrixCursor(new String[]{OpenableColumns.DISPLAY_NAME,OpenableColumns.SIZE});c.addRow(new Object[]{"capture.jpg",f.length()});return c;}
 public Uri insert(Uri u,ContentValues v){throw new UnsupportedOperationException();}
 public int update(Uri u,ContentValues v,String s,String[] a){throw new UnsupportedOperationException();}
 public int delete(Uri u,String s,String[] a){throw new UnsupportedOperationException();}
}
