package app.ryadom.family;
// Old device callbacks must never act on the next call.
final class CallGeneration {
 private volatile int value;
 synchronized int next(){return ++value;}
 boolean isCurrent(int expected){return value==expected;}
}
