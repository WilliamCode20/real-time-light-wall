// ===== Light Wall Beat-Synced Show (Master v9.14.fix11) =====
#define BPM 130
#define BEAT_MS (60000 / BPM)
#define BEAT_US (60000000UL / BPM)
#define MEASURE_BEATS 4

unsigned long songStart;
unsigned long songStartUs;
bool didSyncChorus3 = false;

#define PIX_ON HIGH
#define PIX_OFF LOW

const int ROWS = 5;
const int COLS = 7;

int lights[ROWS][COLS] = {
{2,3,4,5,6,7,8},
{9,10,11,12,13,22,23},
{24,25,26,27,28,29,30},
{31,32,33,34,35,36,37},
{38,39,40,41,42,43,44}
};

int allLights[35] = {
2,3,4,5,6,7,8,
9,10,11,12,13,22,23,
24,25,26,27,28,29,30,
31,32,33,34,35,36,37,
38,39,40,41,42,43,44
};

inline long barMs(){ return BEAT_MS * 4; }

// ===== Helpers =====
void allOn(){ for(int i=0;i<35;i++) digitalWrite(allLights[i],HIGH); }
void allOff(){ for(int i=0;i<35;i++) digitalWrite(allLights[i],LOW); }
void rowOn(int r){ for(int c=0;c<COLS;c++) digitalWrite(lights[r][c],HIGH); }
void colOn(int c){ for(int r=0;r<ROWS;r++) digitalWrite(lights[r][c],HIGH); }
void waitExact(unsigned long duration){ unsigned long start=millis(); while(millis()-start<duration); }
void waitUntilBeat(long beatIndex){
unsigned long targetUs = songStartUs + (unsigned long)((unsigned long long)beatIndex * (unsigned long long)BEAT_US);
while ((long)(micros() - targetUs) < 0) { }
}

// ===== Spiral (utility) =====
void spiralEffect(int delayTime){
int top=0,bottom=ROWS-1,left=0,right=COLS-1;
while(top<=bottom && left<=right){
for(int c=left;c<=right;c++){ digitalWrite(lights[top][c],HIGH); delay(delayTime); digitalWrite(lights[top][c],LOW); }
top++;
for(int r=top;r<=bottom;r++){ digitalWrite(lights[r][right],HIGH); delay(delayTime); digitalWrite(lights[r][right],LOW); }
right--;
if(top<=bottom){ for(int c=right;c>=left;c--){ digitalWrite(lights[bottom][c],HIGH); delay(delayTime); digitalWrite(lights[bottom][c],LOW);} bottom--; }
if(left<=right){ for(int r=bottom;r>=top;r--){ digitalWrite(lights[r][left],HIGH); delay(delayTime); digitalWrite(lights[r][left],LOW);} left++; }
}
}

// ===================== INTRO =====================
void introEffectBeat(){
allOff();
int chunkSize=random(4,9);
for(int j=0;j<chunkSize;j++){ int idx=random(0,35); digitalWrite(allLights[idx],HIGH); }
waitExact(BEAT_MS);
allOff();
}

// ===================== INTRO CHORUS =====================
void introChorus_checkerboard(){
for(int r=0;r<ROWS;r++) for(int c=0;c<COLS;c++) digitalWrite(lights[r][c], (r+c)%2);
waitExact(BEAT_MS);
allOff();
}
void introChorus_sparkle(){
unsigned long start=millis();
while(millis()-start<BEAT_MS){
int r=random(ROWS),c=random(COLS);
digitalWrite(lights[r][c],HIGH);
waitExact(40);
digitalWrite(lights[r][c],LOW);
waitExact(40);
}
allOff();
}
void introChorus_softStrobe(){ allOn(); waitExact(BEAT_MS/2); allOff(); waitExact(BEAT_MS/2); }
void introChorus_meteor(){
int r=random(ROWS),c=0;
int stepDelay=BEAT_MS/COLS;
for(int s=0;s<COLS;s++){ allOff(); digitalWrite(lights[r][c],HIGH); if(c-1>=0) digitalWrite(lights[r][c-1],HIGH); waitExact(stepDelay); c++; }
allOff();
}

// ===================== INTERLUDE 1 =====================
void col4On(){ for(int r=0;r<ROWS;r++) digitalWrite(lights[r][3],HIGH); }
void col35On(){ for(int r=0;r<ROWS;r++){ digitalWrite(lights[r][2],HIGH); digitalWrite(lights[r][4],HIGH);} }
void col26On(){ for(int r=0;r<ROWS;r++){ digitalWrite(lights[r][1],HIGH); digitalWrite(lights[r][5],HIGH);} }
void col17On(){ for(int r=0;r<ROWS;r++){ digitalWrite(lights[r][0],HIGH); digitalWrite(lights[r][6],HIGH);} }
void rowCOff(){ for(int c=0;c<COLS;c++) digitalWrite(lights[2][c],LOW); }
void rowBDOff(){ for(int c=0;c<COLS;c++){ digitalWrite(lights[1][c],LOW); digitalWrite(lights[3][c],LOW);} }
void rowAEOff(){ for(int c=0;c<COLS;c++){ digitalWrite(lights[0][c],LOW); digitalWrite(lights[4][c],LOW);} }
void randomChunks(){ int stepTime=(BEAT_MS*1)/6; for(int i=0;i<6;i++){ allOff(); int count=max(0,5-i); for(int j=0;j<count;j++){ int idx=random(0,35); digitalWrite(allLights[idx],HIGH);} waitExact(stepTime);} allOff(); }
void wipeDown(){ int stepTime=BEAT_MS/ROWS; for(int r=0;r<ROWS;r++){ for(int c=0;c<COLS;c++) digitalWrite(lights[r][c],LOW); waitExact(stepTime);} allOff(); waitExact(BEAT_MS); }
void interlude1(){
unsigned long dottedQuarter=BEAT_MS*3/4;
unsigned long quarter=BEAT_MS/2;
col4On(); waitExact(dottedQuarter); col35On(); waitExact(dottedQuarter); col26On(); waitExact(quarter); col17On(); waitExact(quarter);
rowCOff(); waitExact(quarter); rowBDOff(); waitExact(quarter); rowAEOff(); waitExact(quarter);
col4On(); waitExact(dottedQuarter); col35On(); waitExact(dottedQuarter); col26On(); waitExact(quarter); col17On(); waitExact(quarter);
randomChunks();
col4On(); waitExact(dottedQuarter); col35On(); waitExact(dottedQuarter); col26On(); waitExact(quarter); col17On(); waitExact(quarter);
rowCOff(); waitExact(quarter); rowBDOff(); waitExact(quarter); rowAEOff(); waitExact(quarter);
col4On(); waitExact(dottedQuarter); col35On(); waitExact(dottedQuarter); col26On(); waitExact(quarter); col17On(); waitExact(quarter);
wipeDown();
}

// ===================== VERSE 1 (all effects) =====================

// 1) Waveform spikes
void verse1_waveformBeat(){
for(int b=0;b<4;b++){
int heights[COLS];
for(int c=0;c<COLS;c++) heights[c]=random(1,ROWS+1);
int frames=ROWS*2;
int frameTime=BEAT_MS/frames;
for(int step=1;step<=ROWS;step++){
allOff();
for(int c=0;c<COLS;c++){
digitalWrite(lights[ROWS-1][c],HIGH);
int h=(step<heights[c])?step:heights[c];
for(int r=0;r<h;r++) digitalWrite(lights[ROWS-1-r][c],HIGH);
}
waitExact(frameTime);
}
for(int step=ROWS;step>=1;step--){
allOff();
for(int c=0;c<COLS;c++){
digitalWrite(lights[ROWS-1][c],HIGH);
int h=(step<heights[c])?step:heights[c];
for(int r=0;r<h;r++) digitalWrite(lights[ROWS-1-r][c],HIGH);
}
waitExact(frameTime);
}
allOff();
}
}
void verse1_waveformBeat2(){ verse1_waveformBeat(); }

// 2) Worm jump
void verse1_wormJump(){
int frames[][COLS]={{4,4,4,4,4,4,4},{4,4,3,3,3,4,4},{4,3,2,2,2,3,4},{3,2,1,1,1,2,3},{2,1,0,0,0,1,2},{3,2,1,1,1,2,3},{4,3,2,2,2,3,4},{4,4,4,4,4,4,4}};
int n=sizeof(frames)/sizeof(frames[0]);
int totalFrames=n*2;
int frameTime=(4*BEAT_MS)/totalFrames;
for(int cyc=0;cyc<2;cyc++){
for(int f=0;f<n;f++){
allOff();
for(int c=0;c<COLS;c++) digitalWrite(lights[frames[f][c]][c],HIGH);
waitExact(frameTime);
}
}
allOff();
}

// 3) Aura pulse
void verse1_auraPulse(){
int flashes=8;
int flashTime=(4*BEAT_MS)/flashes;
for(int i=0;i<flashes;i++){
int num=random(3,8);
for(int k=0;k<num;k++){ int r=random(ROWS),c=random(COLS); digitalWrite(lights[r][c],HIGH); }
waitExact(flashTime/2);
allOff();
waitExact(flashTime/2);
}
}

// 4) Wipe up then down
void verse1_wipeUpDown(){
int steps=ROWS*2;
int stepTime=(4*BEAT_MS)/steps;
for(int r=ROWS-1;r>=0;r--){ for(int c=0;c<COLS;c++) digitalWrite(lights[r][c],HIGH); waitExact(stepTime);}
for(int r=0;r<ROWS;r++){ for(int c=0;c<COLS;c++) digitalWrite(lights[r][c],LOW); waitExact(stepTime);}
}

// 5) Twinkle + sweep off
void verse1_twinkleSweep(){
int steps=(COLS+1)/2;
int twinkleTime=(2*BEAT_MS)/steps;
for(int s=0;s<steps;s++){
int left=(COLS/2)-s,right=(COLS/2)+s;
int mini=8,sub=max(1,twinkleTime/mini);
for(int i=0;i<mini;i++){
for(int r=0;r<ROWS;r++){
if(left>=0&&left<COLS) digitalWrite(lights[r][left],random(0,2));
if(right>=0&&right<COLS) digitalWrite(lights[r][right],random(0,2));
}
waitExact(sub);
}
if(left>=0&&left<COLS) for(int r=0;r<ROWS;r++) digitalWrite(lights[r][left],LOW);
if(right>=0&&right<COLS) for(int r=0;r<ROWS;r++) digitalWrite(lights[r][right],LOW);
}
int rowDelay=(2*BEAT_MS)/ROWS;
for(int r=0;r<ROWS;r++){ for(int c=0;c<COLS;c++) digitalWrite(lights[r][c],LOW); waitExact(rowDelay);}
}

// 6) Brother Muis
void verse1_brotherMuis(){
allOn(); waitExact(2*BEAT_MS);
int rowDelay=(2*BEAT_MS)/ROWS;
for(int r=ROWS-1;r>=0;r--){ for(int c=0;c<COLS;c++) digitalWrite(lights[r][c],LOW); waitExact(rowDelay);}
allOff();
}

// 7) Jesus cross
void verse1_jesusCross(){
allOff();
int vDelay=BEAT_MS/5;
digitalWrite(lights[4][3],HIGH); waitExact(vDelay);
digitalWrite(lights[3][3],HIGH); waitExact(vDelay);
digitalWrite(lights[2][3],HIGH); waitExact(vDelay);
digitalWrite(lights[1][3],HIGH); waitExact(vDelay);
digitalWrite(lights[0][3],HIGH); waitExact(vDelay);
waitExact(BEAT_MS);
int hDelay=BEAT_MS/3;
digitalWrite(lights[1][3],HIGH); waitExact(hDelay);
digitalWrite(lights[1][2],HIGH); digitalWrite(lights[1][4],HIGH); waitExact(hDelay);
digitalWrite(lights[1][1],HIGH); digitalWrite(lights[1][5],HIGH); waitExact(hDelay);
waitExact(BEAT_MS);
allOff();
}

// 8) Exploding clusters
void explodingClusters(){
int stepTime=(4*BEAT_MS)/4;
int centers[4][2]={{1,1},{3,5},{0,6},{4,0}};
for(int s=0;s<4;s++){
allOff();
int cr=centers[s][0],cc=centers[s][1];
for(int r=0;r<ROWS;r++) for(int c=0;c<COLS;c++)
if(abs(r-cr)+abs(c-cc)<=s) digitalWrite(lights[r][c],HIGH);
waitExact(stepTime);
}
allOff();
}

// 9) Epileptic seizure
void epilepticSeizure(){
int flashes=32;
int flashTime=(4*BEAT_MS)/flashes;
for(int i=0;i<flashes;i++){
for(int r=0;r<ROWS;r++) for(int c=0;c<COLS;c++) digitalWrite(lights[r][c],(r+c+i+random(0,2))%2);
waitExact(flashTime/2);
allOff();
waitExact(flashTime/2);
}
}

// 10) Bubblegum grow
void bubblegumGrow(){
int cr=2,cc=3,steps=4;
int stepTime=(4*BEAT_MS)/steps;
for(int rad=1;rad<=steps;rad++){
allOff();
for(int r=0;r<ROWS;r++) for(int c=0;c<COLS;c++) if(abs(r-cr)+abs(c-cc)<=rad) digitalWrite(lights[r][c],HIGH);
waitExact(stepTime);
}
allOff();
}

// 11) Bubblegum pop
void bubblegumPop(){
int bursts=12;
int burstTime=(4*BEAT_MS)/bursts;
for(int b=0;b<bursts;b++){
allOff();
int sparks=random(6,10);
for(int i=0;i<sparks;i++){ int r=random(ROWS),c=random(COLS); digitalWrite(lights[r][c],HIGH);}
waitExact(burstTime/2);
allOff();
waitExact(burstTime/2);
}
}

// 12) Spiral fill
void spiralFill(){
int total=ROWS*COLS;
int stepTime=(4*BEAT_MS)/total;
int top=0,bottom=ROWS-1,left=0,right=COLS-1;
while(top<=bottom&&left<=right){
for(int c=left;c<=right;c++){ digitalWrite(lights[top][c],HIGH); waitExact(stepTime);} top++;
for(int r=top;r<=bottom;r++){ digitalWrite(lights[r][right],HIGH); waitExact(stepTime);} right--;
for(int c=right;c>=left;c--){ digitalWrite(lights[bottom][c],HIGH); waitExact(stepTime);} bottom--;
for(int r=bottom;r>=top;r--){ digitalWrite(lights[r][left],HIGH); waitExact(stepTime);} left++;
}
}

// 13) Edgy glitch
void edgyGlitch(){
int flashes=24;
int flashTime=(4*BEAT_MS)/flashes;
for(int i=0;i<flashes;i++){
allOff();
for(int r=0;r<ROWS;r++) for(int c=0;c<COLS;c++) if((r+c+random(0,2))%2==0) digitalWrite(lights[r][c],HIGH);
waitExact(flashTime);
}
allOff();
}

// 14) Rings on/off
void setRingManhattan(int d,int state){ int cr=2,cc=3; for(int r=0;r<ROWS;r++) for(int c=0;c<COLS;c++) if(abs(r-cr)+abs(c-cc)==d) digitalWrite(lights[r][c],state);}
void ringsOnOff(){
int steps=6,stepTime=(4*BEAT_MS)/steps;
allOff(); digitalWrite(lights[2][3],HIGH); waitExact(stepTime);
setRingManhattan(1,HIGH); setRingManhattan(2,HIGH); waitExact(stepTime);
for(int c=0;c<COLS;c++){ digitalWrite(lights[0][c],HIGH); digitalWrite(lights[4][c],HIGH);} for(int r=0;r<ROWS;r++){ digitalWrite(lights[r][0],HIGH); digitalWrite(lights[r][6],HIGH);} waitExact(stepTime*2);
digitalWrite(lights[2][3],LOW); waitExact(stepTime);
setRingManhattan(1,LOW); setRingManhattan(2,LOW); waitExact(stepTime);
for(int c=0;c<COLS;c++){ digitalWrite(lights[0][c],LOW); digitalWrite(lights[4][c],LOW);} for(int r=0;r<ROWS;r++){ digitalWrite(lights[r][0],LOW); digitalWrite(lights[r][6],LOW);}
}

// 15) Checkerboard strobe
void checkerboardStrobe(){
int flashes=8;
int flashTime=(4*BEAT_MS)/flashes;
for(int i=0;i<flashes;i++){
for(int r=0;r<ROWS;r++) for(int c=0;c<COLS;c++) digitalWrite(lights[r][c],(r+c+i)%2);
waitExact(flashTime/2);
allOff();
waitExact(flashTime/2);
}
}

// ===================== CHORUS EFFECTS =====================

void chorus_diagonalMeteor(){
int dir=random(0,4);
int dr=(dir==0||dir==1)?1:-1;
int dc=(dir==0||dir==2)?1:-1;

int r=random(0,ROWS), c=random(0,COLS);
while((r-dr)>=0&&(r-dr)<ROWS&&(c-dc)>=0&&(c-dc)<COLS){ r-=dr; c-=dc; }

int steps=0, rr=r, cc=c;
while(rr>=0&&rr<ROWS&&cc>=0&&cc<COLS){ steps++; rr+=dr; cc+=dc; }
if(steps<1) steps=1;

int stepDelay=max(1,BEAT_MS/steps);
const int trail=3;

for(int s=0;s<steps;s++){
allOff();
for(int t=0;t<trail;t++){
int tr=r-t*dr, tc=c-t*dc;
if(tr>=0&&tr<ROWS&&tc>=0&&tc<COLS) digitalWrite(lights[tr][tc],HIGH);
}
waitExact(stepDelay);
r+=dr; c+=dc;
}
allOff();
}

void chorus_diagonalMeteorSmall(){
int dir=random(0,4);
int dr=(dir==0||dir==1)?1:-1;
int dc=(dir==0||dir==2)?1:-1;

int r=random(0,ROWS), c=random(0,COLS);
while((r-dr)>=0&&(r-dr)<ROWS&&(c-dc)>=0&&(c-dc)<COLS){ r-=dr; c-=dc; }

int pathLen=0, rr=r, cc=c;
while(rr>=0&&rr<ROWS&&cc>=0&&cc<COLS){ pathLen++; rr+=dr; cc+=dc; }

int steps=min(4,max(2,pathLen));
int stepDelay=max(1,BEAT_MS/steps);
const int trail=2;

for(int s=0;s<steps;s++){
allOff();
for(int t=0;t<trail;t++){
int tr=r-t*dr, tc=c-t*dc;
if(tr>=0&&tr<ROWS&&tc>=0&&tc<COLS) digitalWrite(lights[tr][tc],HIGH);
}
int onTime=(stepDelay>20)?(stepDelay-20):stepDelay;
waitExact(onTime);
if(stepDelay>onTime){ allOff(); waitExact(stepDelay-onTime); }
r+=dr; c+=dc;
}
allOff();
}

void chorus_sparkleStorm(){
unsigned long t0=millis();
while(millis()-t0<BEAT_MS){
int r=random(ROWS),c=random(COLS);
digitalWrite(lights[r][c],HIGH);
waitExact(20);
digitalWrite(lights[r][c],LOW);
waitExact(20);
}
allOff();
}

void chorus_sparkleGroove(){
const int steps=8; int stepDur=BEAT_MS/steps;
bool hit[steps]={1,0,1,1,1,0,1,0};
for(int i=0;i<steps;i++){
if(hit[i]){
allOff();
for(int k=0;k<4;k++){
int r=random(ROWS),c=random(COLS);
digitalWrite(lights[r][c],HIGH);
}
int onTime=(stepDur*3)/4;
waitExact(onTime);
allOff();
waitExact(stepDur-onTime);
} else {
allOff();
waitExact(stepDur);
}
}
allOff();
}

void chorus_strobeRow(){
unsigned long t0=millis();
while(millis()-t0<BEAT_MS/2){
allOn(); waitExact(30); allOff(); waitExact(30);
}
int row=random(0,ROWS);
int stepDelay=(BEAT_MS/2)/COLS;
for(int cc=0;cc<COLS;cc++){
digitalWrite(lights[row][cc],HIGH);
waitExact(stepDelay);
digitalWrite(lights[row][cc],LOW);
}
allOff();
}

void chorus_lightningChaos(){
unsigned long t0=millis();
while(millis()-t0<BEAT_MS){
int r=random(ROWS),c=random(COLS);
digitalWrite(lights[r][c],HIGH); waitExact(15);
digitalWrite(lights[r][c],LOW); waitExact(15);
}
allOff();
}

void chorus_meteor(){
int dir=random(0,2);
int r=random(0,ROWS);
int c=(dir==0)?0:COLS-1;
int stepDelay=BEAT_MS/COLS;
for(int s=0;s<COLS;s++){
allOff();
for(int t=0;t<3;t++){
int rr=r, cc=(dir==0)?(c-t):(c+t);
if(rr>=0&&rr<ROWS&&cc>=0&&cc<COLS) digitalWrite(lights[rr][cc],HIGH);
}
waitExact(stepDelay);
c+=(dir==0)?1:-1;
}
allOff();
}

void chorus_meteorGroove(){
int dir=random(0,2);
int r=random(0,ROWS);
int c=(dir==0)?0:COLS-1;
int travel=(BEAT_MS*3)/4;
int stepDelay=max(1,travel/COLS);

unsigned long t0=millis();
for(int s=0;s<COLS && (millis()-t0)<travel; s++){
allOff();
for(int t=0;t<2;t++){
int rr=r, cc=(dir==0)?(c-t):(c+t);
if(rr>=0&&rr<ROWS&&cc>=0&&cc<COLS) digitalWrite(lights[rr][cc],HIGH);
}
waitExact(stepDelay);
c+=(dir==0)?1:-1;
}
allOff();

unsigned long elapsed=millis()-t0;
long resid=BEAT_MS - elapsed;
while(resid>0){
allOn(); waitExact(30);
allOff(); waitExact(30);
resid-=60;
}
allOff();
}

// New: Rapid Row Sweep (2 beats total, up then down)
void chorus_rapidRowSweep(){
int stepTime=(2*BEAT_MS)/(ROWS*2); // up + down in 2 beats
for(int r=ROWS-1;r>=0;r--){ rowOn(r); waitExact(stepTime); allOff(); }
for(int r=0;r<ROWS;r++){ rowOn(r); waitExact(stepTime); allOff(); }
}

// ===================== INTERLUDE 2 (192–208) =====================
void interlude2(){
for(int b=0;b<16;b++){
allOn(); waitExact(BEAT_MS/2);
allOff(); waitExact(BEAT_MS/2);
}
}

// ===================== VERSE 2 (Ninja Rap) — Integrated =====================
// (All functions use the master `lights` grid and helpers.)

// --- Verse 2 timing constants ---
const int v2_bpm65 = 65;
const int v2_beatMs65 = 60000 / v2_bpm65;
const int v2_bpm130 = 130;
const int v2_beatMs130 = 60000 / v2_bpm130;
const int v2_flashDelay130 = v2_beatMs130 / 4;

// --- Verse 2 helpers (scoped to verse 2) ---
inline void v2_setPix(int r,int c,bool on){ if(r>=0&&r<ROWS&&c>=0&&c<COLS) digitalWrite(lights[r][c], on?HIGH:LOW); }
void v2_clearGrid(){ for(int r=0;r<ROWS;r++) for(int c=0;c<COLS;c++) digitalWrite(lights[r][c],LOW); }
void v2_setAll(bool state){ for(int i=0;i<35;i++) digitalWrite(allLights[i], state?HIGH:LOW); }
void v2_randomFlashSet(){
v2_setAll(false);
int count = random(3, 8);
for (int j=0; j<count; j++){
int idx = random(0, 35);
digitalWrite(allLights[idx], HIGH);
}
}
void v2_flashLight(int r,int c,int d){ v2_setPix(r,c,true); delay(d); v2_setPix(r,c,false); }

// 1) Clockwise Border Sweep
void v2_clockwiseBorderSweep(){
const int coords[][2] = {
{0,3},{0,4},{0,5},{0,6},
{1,6},{2,6},{3,6},{4,6},
{4,5},{4,4},{4,3},{4,2},{4,1},{4,0},
{3,0},{2,0},{1,0},
{0,0},{0,1},{0,2}
};
const int steps = sizeof(coords)/sizeof(coords[0]);
int stepDelay = v2_beatMs65 / steps;

v2_clearGrid();
for(int i=0;i<steps;i++){
v2_setPix(coords[i][0], coords[i][1], true);
delay(stepDelay);
}
v2_clearGrid();
}

// 2) Three-Phase Center-to-Outer Beat
void v2_threePhaseBeat(){
const int phase1[3][2]={{2,2},{2,3},{2,4}};
for(int i=0;i<3;i++) v2_setPix(phase1[i][0], phase1[i][1], true);
delay(v2_beatMs65/3);
v2_clearGrid();

const int phase2[12][2]={
{1,1},{1,2},{1,3},{1,4},{1,5},
{2,1},{2,5},
{3,1},{3,2},{3,3},{3,4},{3,5}
};
for(int i=0;i<12;i++) v2_setPix(phase2[i][0], phase2[i][1], true);
delay(v2_beatMs65/3);
v2_clearGrid();

const int phase3[20][2]={
{0,0},{0,1},{0,2},{0,3},{0,4},{0,5},{0,6},
{1,0},{2,0},{3,0},{4,0},
{1,6},{2,6},{3,6},{4,6},
{4,1},{4,2},{4,3},{4,4},{4,5}
};
for(int i=0;i<20;i++) v2_setPix(phase3[i][0], phase3[i][1], true);
delay(v2_beatMs65/3);
v2_clearGrid();
}

// 3) Checkerboard Flash
void v2_checkerboardFlash(){
const int flashes = 8;
for (int f=0; f<flashes; f++){
for (int i=0; i<35; i++){
int r = i / COLS;
int c = i % COLS;
bool on = ((r + c + f) % 2 == 0);
digitalWrite(allLights[i], on ? HIGH : LOW);
}
delay(v2_flashDelay130);
}
v2_setAll(false);
}

// 4) BPM Ramp — 13 bars ramp (70→130) + 2 bars steady at 130 (total 60 beats)
void v2_bpmRampSequence() {
const int startBpm = 70;
const int endBpm = 130;
const int totalBeats = 13 * 4; // 52 beats

for (int beat = 0; beat < totalBeats; beat++) {
float t = (float)beat / (totalBeats - 1);
float bpm = startBpm + t * (endBpm - startBpm);
int beatMs = (int)(60000.0 / bpm);

v2_setAll(true); delay(beatMs / 4);
v2_setAll(false);

v2_randomFlashSet(); delay(beatMs / 4);
v2_randomFlashSet(); delay(beatMs / 2);
}

for (int beat = 0; beat < 8; beat++) {
const int beatMs = 60000 / 130;
v2_setAll(true); delay(beatMs / 4);
v2_setAll(false);
v2_randomFlashSet(); delay(beatMs / 4);
v2_randomFlashSet(); delay(beatMs / 2);
}
v2_setAll(false);
}

// 5) Wave Up effect (2 bars normal + 2 bars double speed)
void v2_waveSweep(int totalDuration){
int rowDelay = totalDuration / ROWS;
for (int row = ROWS - 1; row >= 0; row--){
for (int c=0;c<COLS;c++) digitalWrite(lights[row][c], HIGH);
delay(rowDelay);
for (int c=0;c<COLS;c++) digitalWrite(lights[row][c], LOW);
}
}
void v2_waveUpSequence(){
for (int beat=0; beat<8; beat++) v2_waveSweep(v2_beatMs130);
for (int beat=0; beat<8; beat++){ v2_waveSweep(v2_beatMs130/2); v2_waveSweep(v2_beatMs130/2); }
}

// Finale helpers
void v2_setChunk(int idx){
v2_clearGrid();
switch(idx){
case 0: v2_setPix(0,0,true); v2_setPix(0,1,true); v2_setPix(0,2,true); v2_setPix(1,0,true); v2_setPix(1,1,true); v2_setPix(1,2,true); break;
case 1: v2_setPix(0,4,true); v2_setPix(0,5,true); v2_setPix(0,6,true); v2_setPix(1,4,true); v2_setPix(1,5,true); v2_setPix(1,6,true); break;
case 2: v2_setPix(3,0,true); v2_setPix(3,1,true); v2_setPix(3,2,true); v2_setPix(4,0,true); v2_setPix(4,1,true); v2_setPix(4,2,true); break;
case 3: v2_setPix(3,4,true); v2_setPix(3,5,true); v2_setPix(3,6,true); v2_setPix(4,4,true); v2_setPix(4,5,true); v2_setPix(4,6,true); break;
}
}
void v2_explodingClustersBeat(int beatMs){
unsigned long t0=millis();
int stepDelay=70;
int centerR=random(0,ROWS), centerC=random(0,COLS);
for(int radius=0; radius<4; radius++){
for(int r=0;r<ROWS;r++) for(int c=0;c<COLS;c++)
if(abs(r-centerR)+abs(c-centerC)==radius) v2_setPix(r,c,true);
delay(stepDelay);
v2_clearGrid();
}
while(millis()-t0<beatMs){}
}
void v2_megaStrobeRowWipe(){
v2_setAll(true); delay(80); v2_setAll(false); delay(80);
int row=random(0,ROWS);
for(int c=0;c<COLS;c++){ v2_flashLight(row,c,25); }
}
void v2_finaleSequence(){
int beatMs = 60000 / 130;
int totalBeats=16, beat=0;
while(beat<totalBeats){
// Exploding Clusters: 4 beats
for(int b=0;b<4 && beat<totalBeats; b++,beat++) v2_explodingClustersBeat(beatMs);
// Chunk Flashes: 2 beats
for(int b=0;b<2 && beat<totalBeats; b++,beat++){
unsigned long t0=millis();
for(int f=0;f<2;f++){
int chunkIndex=(beat+f)%4; v2_setChunk(chunkIndex);
delay(beatMs/4); v2_clearGrid(); delay(beatMs/4);
}
while(millis()-t0<beatMs){}
}
// Mega Strobe Row Wipe: 2 beats
for(int b=0;b<2 && beat<totalBeats; b++,beat++){ unsigned long t0=millis(); v2_megaStrobeRowWipe(); while(millis()-t0<beatMs){} }
}
v2_clearGrid();
}

// Master Verse 2 runner
void verse2_sequence(){
v2_clockwiseBorderSweep();
v2_threePhaseBeat();
v2_checkerboardFlash();
v2_bpmRampSequence();
v2_waveUpSequence();
v2_finaleSequence();
v2_clearGrid();
}

// ===================== SEIZURE INTERLUDE HELPERS =====================

void randomJumpingLights_1Measure(int numLights){
const long sixteenth = BEAT_MS/4;
for(int s=0;s<16;s++){
bool trigger=(s%3==0);
if(trigger){
allOff();
for(int i=0;i<numLights;i++){
int rr=random(ROWS),cc=random(COLS);
digitalWrite(lights[rr][cc],HIGH);
}
}
delay(sixteenth);
}
allOff();
}
void halfBarChaos(){
long halfBarMs=BEAT_MS*2;
unsigned long t0=millis();
while(millis()-t0<halfBarMs/3){ allOn();delay(40);allOff();delay(40); }
t0=millis();
while(millis()-t0<halfBarMs/3){
int rr=random(ROWS),cc=random(COLS);
digitalWrite(lights[rr][cc],HIGH);
delay(30);
digitalWrite(lights[rr][cc],LOW);
}
for(int r=0;r<ROWS;r++){ rowOn(r); delay(50); allOff(); }
}
void twoBeatBurst(){
long twoBeats=BEAT_MS*2;
unsigned long t0=millis();
while(millis()-t0<twoBeats){
allOff();
for(int k=0;k<random(6,12);k++){
int rr=random(ROWS),cc=random(COLS);
digitalWrite(lights[rr][cc],HIGH);
}
delay(80);
}
allOff();
}
void lightningChaosBar(){
unsigned long t0=millis();
while(millis()-t0<barMs()){
int r=random(ROWS),c=random(COLS);
digitalWrite(lights[r][c],HIGH);
delay(random(5,30));
digitalWrite(lights[r][c],LOW);
}
}
void spiralEffectBar(){ unsigned long t0=millis(); while(millis()-t0<barMs()){ spiralEffect(15); } }
void megaStrobeRowWipeBar(){
unsigned long t0=millis();
while(millis()-t0<barMs()){
allOn();delay(40);allOff();delay(40);
int row=random(ROWS);
for(int c=0;c<COLS;c++){ digitalWrite(lights[row][c],HIGH); delay(20); digitalWrite(lights[row][c],LOW); }
}
}
void waveSweepsBar(){
unsigned long t0=millis();
while(millis()-t0<barMs()){
for(int col=0;col<COLS;col++){
for(int row=0;row<ROWS;row++) digitalWrite(lights[row][col],HIGH);
delay(30);
for(int row=0;row<ROWS;row++) digitalWrite(lights[row][col],LOW);
}
}
}
void sparkleStormBar(){
unsigned long t0=millis();
while(millis()-t0<barMs()){
int r=random(ROWS),c=random(COLS);
digitalWrite(lights[r][c],HIGH);
delay(random(5,20));
digitalWrite(lights[r][c],LOW);
}
}
void zigZagChaserBar(){
unsigned long t0=millis();
while(millis()-t0<barMs()){
for(int r=0;r<ROWS;r++){
if(r%2==0){ for(int c=0;c<COLS;c++){ digitalWrite(lights[r][c],HIGH); delay(30); digitalWrite(lights[r][c],LOW);} }
else { for(int c=COLS-1;c>=0;c--){ digitalWrite(lights[r][c],HIGH); delay(30); digitalWrite(lights[r][c],LOW);} }
}
}
}
void playBar7(){
long beat=BEAT_MS;
unsigned long t0=millis();
while(millis()-t0<beat){ allOn();delay(40);allOff();delay(40); }
t0=millis();
while(millis()-t0<beat){ int rr=random(ROWS),cc=random(COLS); digitalWrite(lights[rr][cc],HIGH); delay(20); digitalWrite(lights[rr][cc],LOW); }
for(int r=0;r<ROWS;r++){ rowOn(r); delay(50); allOff(); }
t0=millis();
while(millis()-t0<beat){
for(int r=0;r<ROWS;r++) for(int c=0;c<COLS;c++) if((r+c)%2==0) digitalWrite(lights[r][c],HIGH);
delay(60); allOff(); delay(60);
}
}
void playBar8(){
long beat=BEAT_MS;
allOn();delay(beat/2);allOff();delay(beat/2);
unsigned long t0=millis();
while(millis()-t0<beat){ allOn();delay(30);allOff();delay(30); }
int rr=random(ROWS),cc=random(COLS); rowOn(rr); colOn(cc); delay(beat); allOff();
t0=millis();
while(millis()-t0<beat){
for(int k=0;k<ROWS*COLS*0.8;k++) digitalWrite(lights[random(ROWS)][random(COLS)],HIGH);
delay(40); allOff(); delay(40);
}
}

// ===================== SEIZURE BREAKDOWN (starts exactly beat 356) =====================
void seizureBreakdown(){
waitUntilBeat(356); // hard resync to downbeat

// Bars 1–4: build
randomJumpingLights_1Measure(1); // bar 1
randomJumpingLights_1Measure(2); // bar 2
randomJumpingLights_1Measure(4); // bar 3
halfBarChaos(); // bar 4a
twoBeatBurst(); // bar 4b

// Bars 5–10: fast effects
lightningChaosBar(); // bar 5
spiralEffectBar(); // bar 6
megaStrobeRowWipeBar(); // bar 7
waveSweepsBar(); // bar 8
sparkleStormBar(); // bar 9
zigZagChaserBar(); // bar 10

// Bars 11–12: finale
playBar7();
playBar8();

// Extend finale ~1619 ms
unsigned long t0=millis();
while(millis()-t0<1619){
allOff();
for(int k=0;k<ROWS*COLS*0.6;k++){
int rr=random(ROWS), cc=random(COLS);
digitalWrite(lights[rr][cc],HIGH);
}
delay(60);
}
allOff();

// Big Finish: 2 extra beats
waitExact(BEAT_MS); // all off
waitExact(BEAT_MS/2); // half-beat rest
allOn(); waitExact(BEAT_MS/2);
allOff();
}

// ===================== SETUP =====================
void setup(){
for(int i=0;i<35;i++) pinMode(allLights[i],OUTPUT);
allOff();
songStart = millis();
songStartUs = micros();
randomSeed(analogRead(0));
}

// ===================== TIMELINE =====================
void loop(){
unsigned long now = millis() - songStart;
int beat = now / BEAT_MS;

// INTRO (0–16)
if(beat < 16){
introEffectBeat();
}
// INTRO CHORUS (16–48)
else if(beat < 48){
int localBeat = beat - 16;
if (localBeat % 4 == 0) introChorus_checkerboard();
else if (localBeat % 4 == 1) introChorus_sparkle();
else if (localBeat % 4 == 2) introChorus_softStrobe();
else introChorus_meteor();
}
// INTERLUDE 1 (48–64)
else if(beat < 64){
interlude1();
}
// VERSE 1 (64–128)
else if(beat < 128){
int localBeat = beat - 64;
if (localBeat < 4) verse1_waveformBeat();
else if (localBeat < 8) verse1_wormJump();
else if (localBeat < 12) verse1_auraPulse();
else if (localBeat < 16) verse1_wipeUpDown();
else if (localBeat < 20) verse1_waveformBeat2();
else if (localBeat < 24) verse1_twinkleSweep();
else if (localBeat < 28) verse1_brotherMuis();
else if (localBeat < 32) verse1_jesusCross();
else if (localBeat < 36) explodingClusters();
else if (localBeat < 40) epilepticSeizure();
else if (localBeat < 44) bubblegumGrow();
else if (localBeat < 48) bubblegumPop();
else if (localBeat < 52) spiralFill();
else if (localBeat < 56) edgyGlitch();
else if (localBeat < 60) ringsOnOff();
else if (localBeat < 64) checkerboardStrobe();
}
// CHORUS 2 (128–192)
else if(beat < 192){
int localBeat = beat - 128;
int cycle = (localBeat/4) % 4;
if (cycle==0) chorus_diagonalMeteorSmall();
else if (cycle==1) chorus_sparkleGroove();
else if (cycle==2) chorus_strobeRow();
else chorus_meteorGroove();
}
// INTERLUDE 2 (192–208)
else if(beat < 208){
interlude2();
}
// VERSE 2 (208–322)
else if(beat < 322){
verse2_sequence();
}
// CHORUS 3 (322–354)
else if(beat < 354){
if(!didSyncChorus3){ waitUntilBeat(323); didSyncChorus3=true; } // shift by 1 beat
int localBeat = beat - 323;
int cycle=(localBeat/4)%4;
if (cycle==0) chorus_diagonalMeteor();
else if (cycle==1) chorus_sparkleStorm();
else if (cycle==2) chorus_strobeRow();
else chorus_meteor();
}
// SEIZURE INTERLUDE (356–372)
else if(beat < 372){
seizureBreakdown();
}
// FINAL CHORUS (372–476) => 104 beats
else if(beat < 476){
int localBeat = beat - 372;
if(localBeat < 88){ // Crazy 88 beats
int cycle=(localBeat/2)%5;
switch(cycle){
case 0: chorus_lightningChaos(); break;
case 1: chorus_sparkleGroove(); break;
case 2: chorus_rapidRowSweep(); break;
case 3: chorus_diagonalMeteor(); break;
case 4: chorus_meteorGroove(); break;
}
} else { // Last 16 beats calm pulse
if((localBeat-88)%4==0){ allOn(); waitExact(2*BEAT_MS); allOff(); }
else { waitExact(2*BEAT_MS); }
}
}
// END
else {
allOff();
while(true);
}
}
