#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
SDK="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-}}"
test -n "$SDK"
TOOLS="$(find "$SDK/build-tools" -mindepth 1 -maxdepth 1 -type d | sort -V | tail -1)"
PLATFORM="$SDK/platforms/android-35/android.jar"
test -f "$PLATFORM"
mkdir -p build/classes build/dex build/generated build/libs
fetch_jar() {
  local path="$1" hash="$2" dest="build/libs/${1##*/}"
  curl --fail --silent --show-error --location "https://repo.maven.apache.org/maven2/$path" -o "$dest"
  printf '%s  %s\n' "$hash" "$dest" | sha256sum --check
}
fetch_jar org/java-websocket/Java-WebSocket/1.6.0/Java-WebSocket-1.6.0.jar eae29213e4f16515639c28957200f011b3967fffcada1962cf0255d24919c22f
fetch_jar org/slf4j/slf4j-api/2.0.13/slf4j-api-2.0.13.jar e7c2a48e8515ba1f49fa637d57b4e2f590b3f5bd97407ac699c3aa5efb1204a9
fetch_jar org/slf4j/slf4j-nop/2.0.13/slf4j-nop-2.0.13.jar 8962a107b4a8bdf80b6c17e470cd3614ad3329643833ff5cf0c60c7dce9deaac
# Android does not use Java module descriptors or multi-release desktop classes.
for lib in build/libs/*.jar; do zip -dq "$lib" 'META-INF/versions/*' 'module-info.class' || [ "$?" = 12 ]; done
"$TOOLS/aapt2" compile --dir res -o build/resources.zip
"$TOOLS/aapt2" link -o build/unsigned.apk -I "$PLATFORM" --manifest AndroidManifest.xml --java build/generated -A assets build/resources.zip
find src build/generated -name '*.java' > build/sources.txt
javac -encoding UTF-8 -source 8 -target 8 -classpath "$PLATFORM:build/libs/*" -d build/classes @build/sources.txt
mkdir -p build/test-classes
javac -encoding UTF-8 -source 8 -target 8 -classpath "$PLATFORM:build/classes" -d build/test-classes tests/VoiceAudioTest.java tests/CallAudioTest.java tests/NotificationStateTest.java tests/NoiseFilterTest.java
java -classpath "$PLATFORM:build/classes:build/test-classes" app.ryadom.family.VoiceAudioTest
java -classpath "$PLATFORM:build/classes:build/test-classes" app.ryadom.family.CallAudioTest
java -classpath "$PLATFORM:build/classes:build/test-classes" app.ryadom.family.NotificationStateTest
java -classpath "$PLATFORM:build/classes:build/test-classes" app.ryadom.family.NoiseFilterTest
jar cf build/classes.jar -C build/classes .
"$TOOLS/d8" --lib "$PLATFORM" --min-api 26 --output build/dex build/classes.jar build/libs/*.jar
(cd build/dex && zip -q ../unsigned.apk classes*.dex)
"$TOOLS/zipalign" -f -P 16 4 build/unsigned.apk build/aligned.apk
SIGNING="$HOME/.android/ryadom-debug.keystore"
mkdir -p "$HOME/.android"
if [ ! -f "$SIGNING" ]; then
  keytool -genkeypair -keystore "$SIGNING" -storepass android -keypass android -alias androiddebugkey -keyalg RSA -keysize 2048 -validity 10000 -dname 'CN=Ryadom Development,O=Ryadom,C=US'
fi
"$TOOLS/apksigner" sign --ks "$SIGNING" --ks-key-alias androiddebugkey --ks-pass pass:android --key-pass pass:android --out build/Ryadom-Android.apk build/aligned.apk
"$TOOLS/apksigner" verify --verbose --print-certs build/Ryadom-Android.apk > build/APK-verification.txt
"$TOOLS/zipalign" -c -P 16 4 build/Ryadom-Android.apk
"$TOOLS/aapt2" dump badging build/Ryadom-Android.apk >> build/APK-verification.txt
cat build/APK-verification.txt
