
/* ---------- rig ---------- */
var HEADV=null,TORSOV=null,LEGS=null,LEGP=[],NECK=null,FARM=[],PARM=[];
var RIG={root:new T.Group(),torsoG:new T.Group(),headG:new T.Group()};
RIG.root.add(RIG.torsoG);RIG.torsoG.add(RIG.headG);scene.add(RIG.root);
RIG.torsoG.position.set(0,0.98,-0.02);RIG.headG.position.set(0,0.545,0.03);
var ROOTZ=-0.98,UPL=0.3,FOL=0.27,GLOFF=0.085;
var SHO=[new T.Vector3(-0.195,0.415,0),new T.Vector3(0.195,0.415,0)],NECKB=new T.Vector3(0,0.44,-0.005);
var NAMES=['Кирпич','Шкаф','Молот','Бетон','Утюг','Гранит','Бульдозер','Тихий'];
/* fighting style per name: w/p/d multiply windup, pauses and damage; c chance of a series, n extra blows at most, cw windup of a follow-up;
   fe feint chance, co counter chance, hk hook chance, lf chance to lead with the screen-right hand, g guard bonus,
   bs/bc stamina and hp a blocked blow costs, hn strength of the dodge hint on the buttons */
var STY=[
  {s:'одиночные, медленно',w:1.25,p:1.25,d:1,c:0,n:0,cw:.75,fe:0,co:0,hk:.15,lf:.5,g:0,bs:18,bc:.2,hn:1},
  {s:'тяжёлые одиночные',w:1.45,p:1.35,d:1.55,c:0,n:0,cw:.75,fe:0,co:0,hk:.25,lf:.5,g:0,bs:34,bc:.3,hn:.85},
  {s:'серии',w:1,p:1,d:.8,c:.75,n:2,cw:.5,fe:0,co:0,hk:.3,lf:.5,g:-.05,bs:14,bc:.2,hn:.7},
  {s:'контрудар',w:1.1,p:1.1,d:1.05,c:.15,n:1,cw:.75,fe:0,co:.75,hk:.3,lf:.5,g:.1,bs:18,bc:.2,hn:.55},
  {s:'финт, левша',w:1,p:1,d:1,c:.2,n:1,cw:.75,fe:.45,co:0,hk:.25,lf:.8,g:0,bs:18,bc:.2,hn:.4},
  {s:'тяжёлые хуки',w:1.15,p:1,d:1.2,c:.25,n:1,cw:.75,fe:.1,co:.25,hk:.65,lf:.5,g:0,bs:26,bc:.25,hn:.25},
  {s:'напор',w:.9,p:.5,d:.95,c:.55,n:2,cw:.6,fe:.1,co:.35,hk:.35,lf:.5,g:-.1,bs:18,bc:.2,hn:.1},
  {s:'всё сразу',w:1,p:.85,d:1.1,c:.45,n:2,cw:.55,fe:.3,co:.5,hk:.4,lf:.5,g:0,bs:22,bc:.22,hn:0}
];
var LOOKS=[
  {skin:[222,168,128],hair:'short',hairC:[46,31,23],beard:'stub',stub:0.2,iris:[86,58,36],trunks:[47,111,216],accent:[244,241,234]},
  {teeth:[246,236,206],gold:3,skin:[176,116,78],hair:'buzz',hairC:[22,16,14],beard:'full',stub:0.3,iris:[44,28,18],trunks:[42,157,98],accent:[244,182,46],chestHair:true},
  {skin:[238,194,160],hair:'mohawk',hairC:[186,90,38],beard:'stub',stub:0.1,iris:[70,116,150],trunks:[142,68,214],accent:[244,241,234]},
  {teeth:[242,230,198],gold:13,skin:[128,82,56],hair:'bald',hairC:[18,14,12],beard:'full',stub:0.3,iris:[32,20,14],trunks:[224,144,42],accent:[26,22,24]},
  {teeth:[244,234,206],skin:[208,150,110],hair:'short',hairC:[112,110,116],beard:'stub',stub:0.18,iris:[84,100,62],trunks:[43,179,192],accent:[244,241,234],chestHair:true}
];
LOOKS.forEach(function(L){L.beardC=[L.hairC[0]*0.85,L.hairC[1]*0.85,L.hairC[2]*0.9]});
var PSKIN=[226,172,132];
function buildAll(step){
  if(step===0){HEADV=buildHead();HEADV.mat.defines={HEAD:1};HEADV.mat0=HEADV.mat;HEADV.matH=voxMat({HEAD:1,HITS:1},HEADV.mat);HEADV.softFn=function(b){return b[1]<-.03&&b[5]>-.02};HEADV.group.position.set(-PIV[0]*0.01,-PIV[1]*0.01,-PIV[2]*0.01);RIG.headG.add(HEADV.group);return}
  if(step===1){TORSOV=buildTorso();TORSOV.sidePrior=.2;TORSOV.pad=.07;TORSOV.mat.defines={BEND:1};TORSOV.mat0=TORSOV.mat;TORSOV.matH=voxMat({BEND:1,HITS:1},TORSOV.mat);TORSOV.softFn=function(b){return b[1]<.26};RIG.torsoG.add(TORSOV.group);TORSOV.mat.uniforms.uBend.value.set(.03,.2,-1);TORSOV.bend={y0:.03,y1:.2,s:-1,M:TORSOV.mat.uniforms.uB2.value};return}
  if(step===2){LEGP=[buildStatic([-21,69,-26,21,106,28],1.1,pelvSDF,legsPart),buildStatic([-22,41,-5,1,96,25],1.1,thighSDF(-1),legsPart),buildStatic([-1,41,-23,22,96,7],1.1,thighSDF(1),legsPart),
      buildStatic([-21,0,-7,1,53,28],1.1,shinSDF(-1),legsPart),buildStatic([-1,0,-24,21,53,11],1.1,shinSDF(1),legsPart)];
    LEGS=LEGP[0];LEGP.forEach(function(v){RIG.root.add(v.group)});
    NECK=buildStatic([-6.4,-1,-6,6.4,15,6],0.65,neckSDF,function(){return 0});NECK.mat=voxMat({BEND:1});NECK.noCull=true;NECK.mat.uniforms.uBend.value.set(.015,.115,1);RIG.torsoG.add(NECK.group);NECK.group.position.copy(NECKB);return}
  if(step===3){
    var zero=function(){return 0},wrap=function(x,y){return y>22.5?1:0},k;
    for(k=0;k<2;k++){var sd=k?1:-1;
      FARM.push({up:buildStatic([-6.2,-6.5,-6.2,6.2,33,6.2],0.85,armUpSDF,zero),fo:buildStatic([-5.2,-5,-5.2,5.2,30.5,5.2],0.8,armFoSDF,wrap),gl:buildStatic([-7.8,-6.3,-17,7.8,6.3,10.3],0.52,gloveSDF(-sd),glovePart(-sd))});
      PARM.push({up:buildStatic([-6.2,-6.5,-6.2,6.2,33,6.2],0.75,armUpSDF,zero),fo:buildStatic([-5.2,-5,-5.2,5.2,30.5,5.2],0.7,armFoSDF,wrap),gl:buildStatic([-7.8,-6.3,-17,7.8,6.3,10.3],0.45,gloveSDF(sd),glovePart(sd))})}
    FARM.concat(PARM).forEach(function(a){scene.add(a.up.group);scene.add(a.fo.group);scene.add(a.gl.group)});
    PARM.forEach(function(a){a.gl.prior=[0,.5,-1];a.fo.prior=[0,-1,.3];a.up.group.visible=false;a.gl.mat=PGLMAT;
      paintStatic(a.up,[[PSKIN[0],PSKIN[1],PSKIN[2],.12]]);paintStatic(a.fo,[[PSKIN[0],PSKIN[1],PSKIN[2],.12],[236,232,222,.05]]);
      paintStatic(a.gl,[[238,230,214,.22],[244,182,46,.2],[250,248,240,.1],[200,40,34,.25],[212,204,188,.2],[170,160,146,.2]])});
  }
}
function paintFighter(L){
  var sk=L.skin,tr=L.trunks,ac=L.accent,s=[sk[0],sk[1],sk[2],.12];
  var lp=[s,[tr[0],tr[1],tr[2],.25],[ac[0],ac[1],ac[2],.15],[tr[0]*0.6+ac[0]*0.4,tr[1]*0.6+ac[1]*0.4,tr[2]*0.6+ac[2]*0.4,.25],[26,22,28,.3],[236,232,224,.2]];LEGP.forEach(function(v){paintStatic(v,lp)});
  paintStatic(NECK,[s]);
  FARM.forEach(function(a){paintStatic(a.up,[s]);paintStatic(a.fo,[s,[236,232,222,.05]]);
    paintStatic(a.gl,[[tr[0],tr[1],tr[2],.28],[244,241,234,.18],[236,232,224,.1],[ac[0],ac[1],ac[2],.25],[tr[0]*0.8,tr[1]*0.8,tr[2]*0.8,.22],[tr[0]*0.66,tr[1]*0.66,tr[2]*0.66,.22]])});
}

/* ---------- cheats (admin panel) ---------- */
var CHEAT={god:false,stam:false,mega:false,immortal:false,noKD:false,passive:false,thru:false,slow:false,nostop:false,dmg:1};
try{var _cs=JSON.parse(localStorage.getItem('vb_cheat')||'{}');for(var _ck in CHEAT)if(_ck in _cs)CHEAT[_ck]=_cs[_ck]}catch(e){}
/* ---------- state ---------- */
var DODGE=.42,STRIKE=.13;
var G={paused:false,cine:0,cineZ:0,tipT:0,mode:'load',level:1,time:0,anim:0,freeze:0,slow:0,shake:0,fall:0,koLook:0,fovK:0,endT:0,combo:0,comboT:0,stats:null};
var P={hp:100,st:100,block:false,dodge:0,dodgeDir:1,dodgeCd:0,kick:0,gl:[],buf:null,cr:new T.Vector3(),cv:new T.Vector3()};
var F=null,INSPECT=null;
var ZONES={
  nose:{n:'нос',b:['Перелом носа','Нос раздроблен','Нос размозжён'],th:9},
  jaw:{n:'челюсть',b:['Перелом челюсти','Челюсть сломана в двух местах','Челюсть раскрошена'],th:14},
  cheekL:{n:'◀ скула',b:['Перелом скулы','Скула вдавлена','Скула раздавлена'],th:12},cheekR:{n:'скула ▶',b:['Перелом скулы','Скула вдавлена','Скула раздавлена'],th:12},
  orbL:{n:'◀ глазница',b:['Перелом глазницы','Глазница раздроблена','Глазница смята'],th:12},orbR:{n:'глазница ▶',b:['Перелом глазницы','Глазница раздроблена','Глазница смята'],th:12},
  skull:{n:'череп',b:['Трещина в черепе','Череп проломлен','Череп раскалывается'],th:14},
  abs:{n:'живот',b:['Разрыв пресса','Живот вспорот','Кишки наружу'],th:15},
  ribL:{n:'◀ рёбра',b:['Перелом рёбер','Рёбра сломаны со смещением','Грудь проломлена'],th:12},ribR:{n:'рёбра ▶',b:['Перелом рёбер','Рёбра сломаны со смещением','Грудь проломлена'],th:12}
};
var FMAX=6;
var STRESS={nose:['nose',1],jaw:['jaw',1],mouth:['jaw',0.55],cheekL:['cheekL',0.85],cheekR:['cheekR',0.85],orbL:['orbL',0.8],orbR:['orbR',0.8],skull:['skull',0.95],ribL:['ribL',1],ribR:['ribR',1],abs:['abs',1],liver:['ribL',0.6]};
var STUN={jaw:1.6,mouth:1.5,skull:1.2,earL:1.25,earR:1.25,cheekL:1.15,cheekR:1.15,nose:1,orbL:0.9,orbR:0.9,liver:0.8};
function newStats(){return{thrown:0,landed:0,teeth:0,fract:[],blocked:0,kd:0,cuts:0}}
function headZone(X,Y,Z){var r=headRegion(X,Y,Z),L=X<0;
  if(r===R_NOSE)return'nose';if(r===R_MOUTH)return'mouth';if(r===R_JAW||r===R_NECK)return'jaw';if(r===R_CHB||r===R_CHEEK)return L?'cheekL':'cheekR';
  if(r===R_EYE||r===R_BROW)return L?'orbL':'orbR';if(r===R_EAR)return L?'earL':'earR';return'skull'}
function torsoZone(X,Y,Z){var r=torsoRegion(X,Y,Z);if(r===R_LIVER)return'liver';if(r===R_RIBS)return X<0?'ribL':'ribR';if(r===R_STERN)return'stern';if(r===R_CHEST)return'chest';if(r===R_ABS)return'abs';return'shoulder'}

function resetWorld(){
  RD.live=false;RD.on=false;RD.rise=false;RD.mode=0;if(F&&F.guardV){F.guardV.dispose();F.guardV=null}clearPieces();clearCubes();clearRopes();WOUNDS.length=0;DRIPS.length=0;
  for(var k=0;k<ACT.length;k+=2)ACT[k].fl[ACT[k+1]]&=~F_ACT;ACT.length=0;
  drawFloor();floorTex.needsUpdate=true;
}
/* a new opponent is built in slices: run back to back at load, one per tick between fights so the page never stalls for the whole rebuild */
function foeSteps(level){
  var L=LOOKS[(level-1)%LOOKS.length];
  function trim(v){var isl=v.islands(),k,q;for(k=0;k<isl.length;k++)for(q=0;q<isl[k].length;q++)v.type[isl[k][q]]=0}
  return [
  function(){resetWorld();headLook(HEADV,L)},
  function(){trim(HEADV);torsoLook(TORSOV,L);paintFighter(L);trim(TORSOV)},
  function(){
  teethBuild(L);eyesInit(HEADV);
  var f={look:L,level:level,sm:0,smA:0,smTime:2,tauntT:rnd(5,9)};f.smA=1;
  f.name=NAMES[(level-1)%NAMES.length];
  var S=STY[(level-1)%STY.length],cyc=Math.floor((level-1)/STY.length);f.sty=S;
  f.max=220+50*(level-1);f.hp=f.max;f.dmg=(9+1.6*(level-1))*S.d;
  f.wind=Math.max(.3,.6-.045*(level-1))*S.w;f.pace=Math.max(.45,1-.09*(level-1))*S.p;
  f.guardP=clamp(.22+.05*(level-1)+S.g,0,.6);f.chainP=S.c?Math.min(.85,S.c+.08*cyc):0;
  f.feintP=S.fe?Math.min(.6,S.fe+.08*cyc):0;f.counterP=S.co?Math.min(.9,S.co+.1*cyc):0;f.hn=cyc?0:S.hn;
  f.st='idle';f.t=0;f.dur=0;f.next=1.4;f.hand=1;f.side=1;f.kind='straight';f.chain=0;f.guardHits=0;f.feint=false;f.baited=false;f.ctrCd=0;
  f.log={wind:0,one:0,ser:0,feint:0,bait:0,ctr:0,hook:0,lead:0,right:0,wt:0};
  f.gl=[new T.Vector3(-0.125,1.47,0.25),new T.Vector3(0.125,1.47,0.25)];f.glv=[new T.Vector3(),new T.Vector3()];f.gw=[new T.Vector3(),new T.Vector3()];
  f.hr=new T.Vector3();f.hv=new T.Vector3();f.tr=new T.Vector3();f.tv=new T.Vector3();f.hpp=new T.Vector3();f.hpv=new T.Vector3();
  f.pp=new T.Vector3(0,-.028,0);f.ppv=new T.Vector3();f.pr=new T.Vector3();
  f.dz=0;f.dzT=0;f.dzV=0;f.moveT=1.5;f.stepPh=0;
  f.recoil=0;f.recoilV=0;f.lunge=0;f.sx=0;f.sv=0;f.slowMul=1;
  f.jo=0;f.jov=0;f.jx=0;f.jxv=0;f.jawShift=0;f.jawOff=false;f.blinkT=0;f.blinkN=2;
  f.stun=0;f.stunMax=28+level*2.5;f.kd=0;f.fa=0;f.fw=0;f.fz=0;f.count=0;f.countT=0;f.riseAt=5;f.groggy=0;f.lastFx=0;f.lastJ=1;f.liver=0;f.liverT=0;
  f.trauma={};f.burst=false;f.shat=0;f.jawP=null;f.armBroken=[false,false];f.armHit=[0,0];f.skull=null;f.gut=null;f.guardOut=false;
  f.stress={};f.broken={};f.chips={};f.noseBleed=0;f.downHit=false;f.koText='';
  f.guardV=guardBody(L);
  F=f;
  $('foeName').textContent=f.name;$('chips').innerHTML='';$('lvl').textContent='Бой '+level;
  },
  function(){HEADV.process(0,1e9,0)},
  function(){TORSOV.process(0,1e9,0);LEGP.forEach(function(v){v.process(0,1e9,0)});NECK.process(0,1e9,0)},
  function(){FARM.concat(PARM).forEach(function(a){a.up.process(0,1e9,0);a.fo.process(0,1e9,0);a.gl.process(0,1e9,0)})}
  ];
}
function newFoe(level){var st=foeSteps(level),k;for(k=0;k<st.length;k++)st[k]()}
function chip(key,text,bad){var c=F.chips[key];if(!c){c=document.createElement('span');$('chips').appendChild(c);F.chips[key]=c}c.textContent=text;c.className=bad?'bad':''}

/* ---------- HUD ---------- */
var hud={me:-1,st:-1,foe:-1,clock:-1,combo:-1,mega:-1,rd:null,wl:-1,wr:-1,tip:null,hs:0,hn:-1};
function updHud(){
  var a=Math.round(clamp(P.hp,0,100)),b=Math.round(clamp(P.st,0,100)),c=Math.round(clamp(F.hp/F.max,0,1)*100),t=Math.floor(G.time);
  if(a!==hud.me){hud.me=a;$('hpMe').style.transform='scaleX('+a/100+')';$('vig').style.opacity=a<55?(.55-a/100)*1.5:0}
  if(b!==hud.st){hud.st=b;$('stMe').style.transform='scaleX('+b/100+')'}
  if(c!==hud.foe){hud.foe=c;$('hpFoe').style.transform='scaleX('+c/100+')'}
  if(t!==hud.clock){hud.clock=t;$('clock').textContent=Math.floor(t/60)+':'+('0'+t%60).slice(-2)}
  var cs=G.combo>=2&&G.comboT>0?G.combo:0;
  if(cs!==hud.combo){hud.combo=cs;var ce=$('cnum');if(cs){ce.textContent='Комбо ×'+cs;ce.style.opacity=1;ce.className=cs>=5?'big':''}else ce.style.opacity=0}
  var m=Math.round(P.mega),rd=P.megaReady;
  if(m!==hud.mega||rd!==hud.rd){hud.mega=m;$('mfill').style.width=(rd?100:m)+'%';
    if(rd!==hud.rd){hud.rd=rd;$('megaBox').classList.toggle('ready',rd);$('mtxt').textContent=rd?'Мега готов: свайп или тап':'Мега'}}
  /* incoming punch: the screen edge on the attacking side glows red through the windup */
  var wl=0,wr=0;if(F&&G.mode==='fight'){var wk=F.st==='windup'?.25+.6*Math.min(1,F.t/F.dur):F.st==='strike'?1:0;if(F.hand<0)wl=wk;else wr=wk}
  if(wl!==hud.wl){hud.wl=wl;$('warnL').style.opacity=wl}if(wr!==hud.wr){hud.wr=wr;$('warnR').style.opacity=wr}
  /* dodge hint: the button that takes the head away from the striking glove lights up through a real windup (not a feint); fades out over the ladder */
  var hs=F&&G.mode==='fight'&&F.hn>0&&!F.feint&&(F.st==='windup'||F.st==='strike')?-F.side:0;
  if(F&&F.hn!==hud.hn){hud.hn=F.hn;$('pad').style.setProperty('--hn',F.hn)}
  if(hs!==hud.hs){hud.hs=hs;$('bL').classList.toggle('hint',hs<0);$('bR').classList.toggle('hint',hs>0)}
  var tip=G.mode==='fight'&&G.tipT>0;if(tip!==hud.tip){hud.tip=tip;$('tips').classList.toggle('off',!tip)}
}

/* ---------- fractures, jaw ---------- */
function woundNear(v,X,Y,Z,rate){var i=v.cell(X,Y,Z);if(i<0)return;var W=v.W,H=v.H,x=i%W,y=((i/W)|0)%H,z=(i/(W*H))|0,a,b,c,xx,yy,zz,j;
  for(c=2;c>=-3;c--)for(b=-2;b<=2;b++)for(a=-2;a<=2;a++){xx=x+a;yy=y+b;zz=z+c;if(xx<0||yy<0||zz<0||xx>=W||yy>=H||zz>=v.D)continue;j=xx+W*(yy+H*zz);if(v.type[j]&&v.exp(xx,yy,zz,j)){addWound(v,xx,yy,zz,rate);return}}}
function fracture(zone,dir,C){
  var h=HEADV,t=TORSOV,Zn=ZONES[zone],lvl=(F.broken[zone]||0)+1,out={rem:[],split:0},s,k;
  F.broken[zone]=lvl;
  if(lvl>=3){crush(zone,dir,C,lvl);return}
  if(zone==='nose'){
    if(lvl===1){shove(h,0,0.3,9.6,2.6,dir*0.5,-0.06,-0.16,function(X,Y,Z){return Z>8.1&&Y<3.1&&Math.abs(X)<2.3});
      bruiseArea(h,0,0.4,9.0,2.8,0.45,0.22);bruiseArea(h,-3.2,0.9,7.3,1.4,0.3,0.12);bruiseArea(h,3.2,0.9,7.3,1.4,0.3,0.12)}
    else{shove(h,0,-0.6,10.0,2.3,dir*0.3,-0.12,-0.55,function(X,Y,Z){return Z>8.3&&Math.abs(X)<2.2});cutAt(h,0,1.3,0.75,0,out,3)}
    F.noseBleed=lvl;woundNear(h,-0.56,-3.0,9.45,2.4+lvl);woundNear(h,0.56,-3.0,9.45,2.4+lvl);
  }else if(zone==='jaw'){
    F.jawShift+=dir*(lvl===1?0.0035:0.0025);
    bruiseArea(h,dir*3.0,-7.2,6.0,3.2,0.4,0.25);
    woundNear(h,dir*2.3,-5.3,8.4,2.2);woundNear(h,0,-5.7,9.1,1.8);
    for(k=0;k<(lvl===1?2:3);k++){var id=9+((Math.random()*8)|0),tt=TEETH[id];if(tt&&!tt.out){tt.hp-=lvl===1?16:40;if(tt.hp<=0)knockTooth(h,id,_ld.set(dir,0.3,0.6));else if(!tt.loose)teethHitCm(tt.cx,tt.cy,tt.cz,0.01,'jab',_ld,false)}}
    if(lvl===2)cutAt(h,dir*1.0,-8.6,0.8,0.35*dir,out,2.5);
  }else if(zone==='cheekL'||zone==='cheekR'){s=zone==='cheekL'?-1:1;
    shove(h,s*4.6,0.3,6.6,2.8,-s*0.12,-0.1,lvl===1?-0.42:-0.5,function(X,Y,Z){return Z>3.5&&Math.abs(X)>1.6});
    bruiseArea(h,s*4.4,0.1,6.8,2.6,0.5,0.35);if(lvl===2)cutAt(h,s*4.6,0.9,1.0,0.25*s,out,2.2);
  }else if(zone==='orbL'||zone==='orbR'){s=zone==='orbL'?-1:1;
    shove(h,s*3.2,3.1,7.4,2.5,0,-0.1,lvl===1?-0.28:-0.42,function(X,Y,Z){return Z>5});
    bruiseArea(h,s*3.2,2.2,7.1,2.2,0.55,0.55);if(lvl===2){cutAt(h,s*3.1,3.75,1.0,0,out,3.2);popEye(s<0?0:1)}
  }else if(zone==='skull'){
    var nx=C.x,ny=C.y-3.4,nz=C.z+1.2,l=Math.sqrt(nx*nx+ny*ny+nz*nz)||1;
    shove(h,C.x,C.y,C.z,2.4,-nx/l*0.35*lvl,-ny/l*0.35*lvl,-nz/l*0.35*lvl);
    bruiseArea(h,C.x,C.y,C.z,2.8,0.5,0.7);if(lvl===2)openSkull(C);
  }else if(zone==='abs'){
    shove(t,clamp(C.x,-9,9),clamp(C.y,6,20),8,6,0,-0.2,lvl===1?-0.9:-1.3,function(X,Y,Z){return Z>2});bruiseArea(t,clamp(C.x,-9,9),clamp(C.y,6,20),9,7,0.5,0.3);
    if(lvl===2)openGut(C);
  }else{s=zone==='ribL'?-1:1;var cy=clamp(C.y,14,34);
    shove(t,s*11,cy,6,6.5,-s*0.25,-0.35,lvl===1?-1.0:-1.4,function(X,Y,Z){return Z>-3&&X*s>2});
    bruiseArea(t,s*11,cy,7,6.5,0.45,0.3);
  }
  if(lvl===2)openFracture(zone,C);
  if(out.rem.length)spill(h,out.rem,false,FD,true);
  F.hp-=6;F.slowMul*=(zone==='ribL'||zone==='ribR')?1.15:1.06;
  hitStop(.09,.5);if(!REDUCED){G.shake=Math.max(G.shake,.06);G.fovK=Math.max(G.fovK,1.3)}
  sfx.crack();vib([40,30,110]);banner(Zn.b[lvl-1]);
  if(lvl===1)G.stats.fract.push(Zn.n.replace(/[◀▶ ]+/g,' ').trim());
  chip(zone,Zn.n+(lvl>1?' ×2':''),lvl>1);
}
var _jm=new T.Matrix4(),_jm2=new T.Matrix4();
function detachJaw(FDv,mode,keep){mode=mode||'free';
  var v=HEADV,W=v.W,H=v.H,cells=[],x,y,z,i,f,X,Y,Z,w;
  for(z=0;z<v.D;z++){Z=v.Z(z);if(Z<-1.0)continue;for(y=0;y<H;y++){Y=v.Y(y);if(Y>-1)continue;i=W*(y+H*z);for(x=0;x<W;x++,i++){
    if(!v.type[i])continue;f=v.fl[i];X=v.X(x);w=(f&F_JAWH)?((f&F_JAWL)?1:0):jawW(X,Y,Z);if(w>=0.5)cells.push(i)}}}
  var a=F.jo*0.16,lat=F.jx*0.0035+F.jawShift;
  _jm.makeTranslation(lat,-0.008,0.006).multiply(_jm2.makeRotationX(a)).multiply(new T.Matrix4().makeTranslation(0,0.008,-0.006));
  var hang=mode!=='free',vs=hang?0.3:1;
  var jp=pieceFrom(v,cells,_jm,new T.Vector3((FDv.x*2.4+rnd(-.3,.3))*vs,(FDv.y*2.0+1.0+rnd(.4))*vs,(FDv.z*2.0+rnd(-.2,.2))*vs),new T.Vector3(rnd(-11,11)*vs,rnd(-6,6)*vs,rnd(-8,8)*vs));
  teethWithJaw(jp);F.jawP=jp;
  if(hang)jawPins(jp,mode==='hang2'?[-1,1]:[keep||(Math.random()<.5?-1:1)]);
  rawStump(v);neckGore();stumpTatters(v);
  for(var k=0;k<7;k++)woundNear(v,rnd(-3.6,3.6),-5.2+rnd(-0.6,0.6),rnd(3,8.5),8);
  F.jawOff=true;F.jo=0;F.jov=0;
  RIG.headG.getWorldPosition(_hp);_hp.y-=0.05;_hp.z+=0.06;bloodBurst(_hp,200,FDv.x,0.4,1,1.4);
  for(k=0;k<3;k++)screenSplat(2);
  sfx.tear();vib([80,40,160]);
}

/* ---------- hits ---------- */
var FD=new T.Vector3(),PE={hook:3.4,upper:3.8,over:3.2},PJ={hook:3.8,upper:4.6,over:3.6};
var _ld=new T.Vector3(),_lp=new T.Vector3(),_r=new T.Vector3(),_tq=new T.Vector3(),_hp=new T.Vector3();
function setF(st,dur){F.st=st;F.t=0;if(dur!==undefined)F.dur=dur}
function hitFoe(res,g){
  var power=g.type!=='jab',mult=1,k;
  if(F.st==='open')mult=1.6;else if(F.st==='windup')mult=1.3;
  if(g.type==='jab')FD.set(0,0,-1);else if(g.type==='hook')FD.set(-g.side*0.94,0,-0.34);else if(g.type==='upper')FD.set(0,0.86,-0.5);else FD.set(0,-0.55,-0.83);
  FD.normalize();
  var reach=clamp(1+F.dz*2.2,0.72,1.2),mg=!!g.mega,E=reach*(power?PE[g.type]*g.pw:1)*mult*(g.weak?0.6:1)*(mg?2.4:g.fl?1.2:1)*CHEAT.dmg,J=(power?PJ[g.type]*g.pw:1.1)*mult*(g.weak?0.6:1)*(mg?2:1)*Math.sqrt(CHEAT.dmg);
  F.lastFx=FD.x;F.lastJ=J;
  if(res.kind==='glove'){
    G.stats.blocked++;sfx.block();hitStop(.03,0);G.combo=0;F.glv[res.k].addScaledVector(FD,J*1.5);F.glv[res.k].y+=J*.3;
    for(k=0;k<(power?12:5);k++)spawn(res.point.x,res.point.y,res.point.z,rnd(-1.4,1.4),rnd(.2,1.8),rnd(.3,1.6),255,243,196,0.5,.006,4,.25);
    impactFx(res.point,power?.6:.25);popWorld('Блок',res.point,'pj');if(!g.fl)megaAdd(2);
    if(Math.random()<.5)foeSmile(.9,.8);
    if(power){F.armHit[res.k]+=E*(mg?3:1);if(F.armHit[res.k]>=13)breakArm(res.k);F.hp-=E*0.4;F.hv.x-=J*.4;rdHit(res.point,FD,J*.45,true);F.recoilV+=FD.z*J*0.35;if(!REDUCED)G.shake=Math.max(G.shake,.025);if(F.hp<=0)knockout(false)}
    if(!g.fl&&!mg)foeCounter();return;
  }
  G.stats.landed++;
  var head=res.kind==='head',v=res.v;
  _im.copy(v.group.matrixWorld).invert();_ld.copy(FD).transformDirection(_im);_lp.copy(res.point).applyMatrix4(_im);
  var gb=g.bloody,out=impact(v,res.x,res.y,res.z,_ld,E,g.type,head,gb);
  var X=v.X(res.x),Y=v.Y(res.y),Z=v.Z(res.z),zone=head?headZone(X,Y,Z):torsoZone(X,Y,Z);
  var teeth=0;
  if(head&&!F.jawOff&&(zone==='jaw'||zone==='mouth'||g.type==='upper'))teeth=teethHit(v,res.x,res.y,res.z,E*(F.guardOut?1.3:0.6),g.type,FD,F.jo>0.3);
  if(head&&!F.guardOut&&!F.jawOff&&power&&(zone==='jaw'||zone==='mouth'||g.type==='upper')&&J>=3.2&&Math.random()<(mg?1:(F.broken.jaw?0.5:0.22)))ejectGuard(FD);
  if(head&&F.skull&&(power||mg)){var sdx=X-F.skull.x,sdy=Y-F.skull.y,sdz=Z-F.skull.z;if(sdx*sdx+sdy*sdy+sdz*sdz<36)brainHit(E)}
  if(!head&&F.gut&&power){var gdx=X-F.gut.x,gdy=Y-F.gut.y;if(gdx*gdx+gdy*gdy<64){spillCore(v,F.gut.x,F.gut.y,F.gut.z,3.4,Math.round(6+E*3),FD,false);sfx.squelch()}}
  if(head)for(k=0;k<pieces.length;k++)if(pieces[k].teth)pieces[k].vel.addScaledVector(FD,J*0.3).y+=J*0.15;
  if(head){var ce=eyeCheck(v);if(ce>=0){chip('eye'+ce,ce?'глаз ▶ заплыл':'◀ глаз заплыл',true);banner('Глаз заплыл')}}
  var gored=goreHit(v,head,zone,X,Y,Z,E,power,mg);
  /* the loose-part search is the heaviest step of a hit: it runs on the next frame */
  if((out.rem.length&&(out.deep||power))||gored){ISLQ.v=v;ISLQ.d.copy(FD);ISLQ.t=1}
  spill(v,out.rem,power,FD,head);
  if(out.split)G.stats.cuts++;
  var dmg=E*(head?2.1:1.4);F.hp-=dmg;
  if(zone==='liver'&&power)F.liver+=E;
  if(head&&zone==='nose'&&out.maxP>3&&Math.random()<0.55){woundNear(v,-0.56,-3.0,9.45,1.1);woundNear(v,0.56,-3.0,9.45,1.1)}
  if(head&&zone==='mouth'&&out.maxP>3.2&&Math.random()<0.45)woundNear(v,rnd(-1.6,1.6),-5.5,9.0,1.0);
  /* momentum: torque about the neck (or hips) from where the glove landed and which way it travelled */
  (head?RIG.headG:RIG.torsoG).getWorldPosition(_hp);_r.copy(res.point).sub(_hp);_tq.crossVectors(_r,FD).multiplyScalar(J);
  rdHit(res.point,FD,J,head);
  if(head){F.hv.x+=_tq.x*6;F.hv.y+=_tq.y*9;F.hv.z+=_tq.z*4;F.hpv.addScaledVector(FD,J*0.04);F.tv.x+=_tq.x*1.2;F.tv.y+=_tq.y*1.5;
    if(g.type==='upper'){if(F.jo>0.35)sfx.clack();F.jov-=16}else F.jov+=J*2.2;F.blinkT=0.22}
  else{F.tv.x+=power?1.2*J/3.8:0.3;F.tv.y+=_tq.y*2.5;F.tv.z+=_tq.z*1;F.hv.x+=power?1:0.3;F.jov+=J*1.4}
  F.recoilV+=FD.z*J*0.6;F.sv+=FD.x*J*0.5;F.ppv.x+=FD.x*J*(head?.04:.1);F.ppv.z+=FD.z*J*(head?.04:.1);if(power)F.ppv.y-=J*(head?.04:.07);if(power){F.dzT-=0.025*J/3.8*(mg?2.5:1);F.dzV-=0.25*J/3.8;F.moveT=Math.max(F.moveT,.8)}
  for(k=0;k<2;k++){F.glv[k].addScaledVector(FD,J*0.45);F.glv[k].y-=J*0.2}
  v.addHit(_lp,_ld,power?0.011*Math.min(1.4,J/3.8):0.0045,head?(power?0.022:0.015):(power?0.04:0.028),(mg?0.0065:power?0.0042*Math.min(1.5,J/3.8):0.0018)*(head?1:1.3),head?0.16:0.26,head?115:80,G.anim);
  var wet=out.n?out.bl/out.n/255:0,p=res.point,ddx=FD.x*1.6,ddy=g.type==='upper'?1.6:g.type==='over'?-0.4:0.2;
  var nb=Math.round(out.split*6+out.deep*1.6+out.rem.length/7*0.6+wet*E*16+(F.noseBleed&&zone==='nose'?10:0));
  if(nb>0){bloodBurst(p,Math.min(150,nb),ddx,ddy,0.9,power?1.2:0.9);g.bloody=Math.min(1,g.bloody+(power?0.12:0.05)*Math.min(1,wet*2+out.split*0.2));
    if(power&&nb>22&&Math.random()<0.5)screenSplat(1+((Math.random()*2)|0));if(power&&nb>14)sfx.squelch()}
  else for(k=0;k<(power?20:5);k++)spawn(p.x,p.y,p.z+.01,rnd(-1.4,1.4)+ddx,rnd(.3,2.2),rnd(.2,1.8),226,236,244,0.9,.0018,2,.6);
  if(teeth){G.stats.teeth+=teeth;sfx.tooth();popWorld(teeth>1?'Зубы −'+teeth:'Зуб',p,'gold');var tc=0;for(k=1;k<=16;k++)if(TEETH[k].out)tc++;chip('teeth','зубы −'+tc,tc>3)}
  var sm=STRESS[zone];
  if(sm){var zk=sm[0];F.stress[zk]=(F.stress[zk]||0)+E*sm[1];var lv=F.broken[zk]||0,need=ZONES[zk].th*(1+0.85*lv);if(mg&&lv<FMAX)F.stress[zk]=Math.max(F.stress[zk],need);
    if(lv<FMAX&&F.stress[zk]>=need){fracture(zk,g.type==='hook'?-g.side:(X<0?1:-1),{x:X,y:Y,z:Z});bloodBurst(p,30,ddx,1,1,1.1)}}
  if(head&&power&&!F.jawOff&&(zone==='jaw'||zone==='mouth')&&(F.broken.jaw||0)>=2&&J>=3.6){
    var jm=mg||J>=6.5?'free':g.type==='hook'?'hang1':'hang2';detachJaw(FD,jm,X<0?1:-1);G.stats.jaw=true;impactFx(p,1.6);hitStop(.16,0,true);if(mg)megaHitFx(g,p,dmg);
    knockout(true,jm==='free'?'Челюсть оторвана':'Челюсть повисла');return}
  if(head&&F.jawP&&F.jawP.pins&&F.jawP.pins.length&&(power||mg)){F.jawP.vel.addScaledVector(FD,J*.35);F.jawP.w.x+=rnd(-6,6);F.jawP.w.z+=rnd(-6,6);
    if(mg||Math.random()<((zone==='jaw'||zone==='mouth')?.6:.3))tearJawPin(g.type==='hook'?(X<0?-1:1):0)}
  if(power){foeSmile(.5,.32);if(F.st!=='open'&&F.st!=='down'&&F.st!=='rise')setF('stagger',.4+.14*(mult-1))}
  else{F.next+=.1;if(F.st==='guard'&&!head){F.guardHits++;if(F.guardHits>=2){setF('idle');F.next=rnd(.3,.7)}}}
  _pw2.copy(p);_pw2.y+=.05;
  if(mg)megaHitFx(g,p,dmg);
  else popWorld((reach<0.82?'Далеко · ':'')+(mult>1.5?'Контра ':mult>1?'Встречный ':'')+PNAME[g.type]+' −'+Math.round(dmg),_pw2,power||mult>1?'gold':'pj');
  impactFx(p,mg?2.6:power?Math.min(1.6,g.pw*mult):.3);
  if(power){hitStop(g.fl?.03:.08,g.fl?0:.2+.12*(g.pw*mult-1));if(!REDUCED){G.shake=Math.max(G.shake,.045*g.pw*mult);G.fovK=Math.max(G.fovK,.9*g.pw*mult)}sfx.heavy(nb>0);vib(45)}
  else{hitStop(.03,0);if(!REDUCED)G.shake=Math.max(G.shake,.012);sfx.jab();vib(12)}
  G.combo=G.comboT>0?G.combo+1:1;G.comboT=1.6;G.stats.maxCombo=Math.max(G.stats.maxCombo||0,G.combo);
  if(!mg&&!g.fl)megaAdd((power?11:5)*(mult>1?1.5:1)+Math.min(6,G.combo-1));
  F.stun+=E*(STUN[zone]||(head?1:0.5));
  if(g.fl&&F.st!=='down'&&F.st!=='rise')setF('stagger',.35);
  if(mg&&F.hp>0&&F.st!=='down'&&F.st!=='rise'&&(F.hp<F.max*0.45||F.stun>=F.stunMax*0.6)){if(F.kd>=2)knockout(true);else knockdown('Нокдаун')}
  else if(mg&&F.hp>0&&F.st!=='down'&&F.st!=='rise'){F.stun=Math.max(F.stun,F.stunMax*0.7);setF('stagger',1.1)}
  if(F.hp<=0)knockout(false);
  else if(F.st!=='down'&&F.st!=='rise'&&F.stun>=F.stunMax){if(F.kd>=2)knockout(true);else knockdown('Нокдаун')}
  else if(F.liver>9&&F.st!=='down'){F.liver=3;F.liverT=0.35}
}
var _pw2=new T.Vector3();
function megaHitFx(g,p,dmg){
  banner(MNAME[g.type]+'!');popWorld('−'+Math.round(dmg),_pw2.copy(p).setY(p.y+.06),'mega');
  hitStop(g.fl?.06:.16,g.fl?.15:.6);if(!REDUCED){G.shake=Math.max(G.shake,.11);G.fovK=Math.max(G.fovK,2.4);retrig($('megafx'),'on')}
  bloodBurst(p,140,FD.x*2,FD.y*1.5+.8,1,1.5);screenSplat(3);sfx.mega();vib([60,30,140]);
  for(var k=0;k<40;k++)spawn(p.x,p.y,p.z+.01,rnd(-2.6,2.6)+FD.x*2,rnd(-1,2.8),rnd(-.5,2.4),255,110+rnd(120),30,1,rnd(.005,.012),7,rnd(.2,.5));
}
/* past the second break a zone keeps collapsing: the bone caves in further, a crater opens, more bone shows */
function crush(zone,dir,C,lvl){
  var h=HEADV,t=TORSOV,Zn=ZONES[zone],head=!(zone==='ribL'||zone==='ribR'||zone==='abs'),v=head?h:t,k=Math.min(1.6,0.6+0.25*(lvl-2)),s;
  var n=head?outN(C.x,C.y,C.z,0,1.5,-1):outN(C.x,C.y,C.z,0,26,-2),nx=n.x,ny=n.y,nz=n.z;
  shove(v,C.x,C.y,C.z,head?2.8:6,-nx*.35*k+dir*.06,-ny*.35*k,-nz*.35*k);
  holeAt(v,C.x+nx*.3,C.y+ny*.3,C.z+nz*.3,(head?1.1:2.2)+0.25*(lvl-2)*(head?1:2),FD,head,lvl>=4);
  for(var q=0;q<(head?1:2);q++)boneShard(v,C.x+rnd(-1,1),C.y+rnd(-1,1),C.z,nx+rnd(-.5,.5),ny+rnd(-.5,.5),nz+rnd(-.3,.3),head?rnd(.8,1.3):rnd(2,3),head?.005:.008);
  if(zone==='skull'){if(F.skull)brainHit(12+lvl*4);else openSkull(C)}
  else if(zone==='abs'){if(!F.gut)openGut(C);else{spillCore(t,F.gut.x,F.gut.y,F.gut.z,4,30,FD,false);
    addRope({n:22,seg:.02,size:.022,col:[214,128,122,.82],base:[150,30,36],a:bodyPt(t,F.gut.x+rnd(-1.5,1.5),F.gut.y+rnd(-1,1),F.gut.z+3),dir:_gv.set(0,-.5,1).normalize(),damp:.975,plane:bodyPlane(t,F.gut.x,F.gut.y,F.gut.z+3.8,0,0,1)})}}
  else if(zone==='orbL'||zone==='orbR'){s=zone==='orbL'?0:1;if(!EYES[s].popped)popEye(s);else tearEye(s)}
  else if(zone==='jaw'&&!F.jawOff&&lvl>=3){F.broken.jaw=Math.max(2,F.broken.jaw);detachJaw(FD,lvl>=4?'free':'hang1',dir<0?1:-1);G.stats.jaw=true}
  else if(zone==='jaw'&&F.jawP&&F.jawP.pins&&F.jawP.pins.length)tearJawPin(0);
  else if(zone==='nose'){shove(h,0,-.4,9.6,2.4,0,-.1,-.6,function(X,Y,Z){return Z>8&&Math.abs(X)<2.4});F.noseBleed=Math.max(F.noseBleed,lvl)}
  if(F.jawOff&&zone==='jaw')spillCore(h,0,-5,3,3,20,FD,true);
  RIG.headG.getWorldPosition(_gv);if(!head){t.group.updateMatrixWorld();_gv.set(C.x*.01,C.y*.01,C.z*.01).applyMatrix4(t.group.matrixWorld)}
  bloodBurst(_gv,40+lvl*10,FD.x,.8,1,1.2);
  F.hp-=4+lvl;hitStop(.08,.4);if(!REDUCED)G.shake=Math.max(G.shake,.06);
  sfx.crack();sfx.squelch();vib([40,30,90]);banner(Zn.b[2]+(lvl>3?' ×'+(lvl-2):''));chip(zone,Zn.n+' ×'+lvl,true);
}
/* hit-stop and slow motion: blows that follow each other closely get less of both, so a flurry on a wrecked face never turns the fight into a slideshow */
var CINET=-9,STOPT=-9,STOPF=-1,STOPQ=1;
function hitStop(f,s,full){
  if(CHEAT.nostop)return;
  if(clockT!==STOPF){STOPQ=clamp((clockT-STOPT)/.8,.25,1);STOPF=clockT;if(f>.04)STOPT=clockT}
  var q=full?1:STOPQ;if(f>0)G.freeze=Math.max(G.freeze,f*q);if(s>0)G.slow=Math.max(G.slow,s*q);
}
function fall(){setF('down');rdStart();F.fa=.08;F.fw=1.5+Math.min(2.5,F.lastJ*.3);F.fz=Math.abs(F.lastFx)>.6?(F.lastFx>0?-1:1):0;F.downHit=false;F.count=0;F.countT=0;F.riseAt=4+((Math.random()*3)|0)}
function knockdown(text){if(CHEAT.noKD){F.stun=0;F.liver=0;if(F.st!=='down'&&F.st!=='rise')setF('stagger',.7);banner(text==='Нокдаун'?'Устоял':text);return}
  F.kd++;G.stats.kd++;F.stun=0;fall();banner(text);hitStop(0,.9,true);vib([50,40,90])}
function knockout(tko,text){if(CHEAT.immortal){F.hp=Math.max(1,F.hp);if(!CHEAT.noKD&&F.st!=='down'&&F.st!=='rise'){F.stun=0;F.kd++;G.stats.kd++;fall();banner(text||'Нокдаун');hitStop(0,.9,true)}else if(text)banner(text);return}
  F.hp=0;G.mode='ko';G.endT=0;if(F.st!=='down')fall();hitStop(0,1.2,true);vib([60,40,120]);F.koText=text||(tko?'Технический нокаут':'Нокаут');banner(F.koText)}
function foeSmile(a,d){if(!F||F.st==='down'||F.st==='rise')return;if(F.smTime>0&&F.smA>a)return;F.smA=a;F.smTime=d}
function playerHurt(d,hook,hand){foeSmile(1,1.3);
  if(CHEAT.god)d=0;P.hp-=d;P.kick=1;sfx.hurt();if(!P.megaReady)P.mega=Math.max(0,P.mega-18);P.flurry=null;vib(70);P.cv.x+=5.5;P.cv.y+=hook?(hand||1)*7:rnd(-2,2);P.cv.z+=rnd(-3,3);
  if(!REDUCED){G.shake=Math.max(G.shake,.06);retrig($('flash'),'hit')}
  screenSplat(hook?4:2+(P.hp<50?2:0));G.combo=0;
  if(P.hp<=0){P.hp=0;G.mode='lost';G.endT=0;P.block=false;sfx.thud()}
}

/* ---------- foe brain ---------- */
/* chain: next blow of a series (other hand); ctr: a counter right after your miss or a blow into his gloves.
   A feint is a short windup with no "!" over the glove and no tick: it breaks off and the other hand throws for real */
function startWindup(chain,ctr){
  if(CHEAT.passive){setF('idle');F.chain=0;F.next=rnd(.6,1.2);return}
  var S=F.sty,L_=F.log,fk;
  F.hand=chain?-F.hand:(Math.random()<S.lf?1:-1);
  if(F.armBroken[F.hand<0?0:1])F.hand=-F.hand;if(F.armBroken[F.hand<0?0:1]){setF('idle');F.chain=0;F.next=rnd(1,1.8);return}F.kind=Math.random()<S.hk?'hook':'straight';
  fk=!chain&&!ctr&&!F.armBroken[0]&&!F.armBroken[1]&&Math.random()<F.feintP;
  if(!chain)F.chain=!ctr&&!fk&&Math.random()<F.chainP?1+((Math.random()*S.n)|0):0;
  F.feint=fk;F.baited=false;F.side=F.hand;if(!chain){F.log.lead++;if(F.hand>0)F.log.right++}
  setF('windup',F.wind*(chain?S.cw:1)*(ctr?.6:1)*(fk?.5:1)*F.slowMul*(F.groggy>0?1.3:1));
  if(fk){L_.feint++;return}
  if(!chain){if(ctr)L_.ctr++;else if(F.chain)L_.ser++;else L_.one++}
  windupCue();
}
function windupCue(){var L_=F.log;L_.wind++;L_.wt+=F.dur;if(F.kind==='hook')L_.hook++;
  sfx.tick();_hp.copy(F.gw[F.hand<0?0:1]);_hp.y+=.12;popWorld('!',_hp,'warn')}
function foeCounter(){
  if(!F||G.mode!=='fight'||F.ctrCd>0||!(F.st==='idle'||F.st==='guard'||F.st==='recover'&&!F.chain)||Math.random()>=F.counterP)return;
  F.ctrCd=1.2;startWindup(false,true)}
/* which side of the screen the punch comes from: the striking glove projected through the camera at rest (frozen while you dodge, the camera moves then) */
function foeSide(){if(P.dodge>0)return;_hp.copy(F.gw[F.hand<0?0:1]).project(camera);F.side=_hp.x<0?-1:1}
/* footwork: he drifts in and out of range, steps in to throw, gets driven back by heavy shots */
var DZMIN=-0.17,DZMAX=0.09;
function footwork(dt){
  var lying=F.st==='down'||F.st==='rise';
  if(!lying){F.moveT-=dt;
    if(F.moveT<=0){F.moveT=rnd(1.4,3.2)*(F.groggy>0?1.6:1);var r=Math.random();F.dzT=r<.35?rnd(-.16,-.08):r<.7?rnd(-.05,.03):rnd(.03,.09);if(Math.abs(F.dzT-F.dz)>.05)sfx.step()}
    if(F.st==='windup'&&F.dzT<.0)F.dzT=rnd(0,.05);
    if(F.st==='guard')F.dzT=Math.min(F.dzT,-.02);}
  F.dzT=clamp(F.dzT,DZMIN,DZMAX);
  /* critically damped step, plus a little bounce while moving */
  F.dzV+=((F.dzT-F.dz)*38-F.dzV*11)*dt;F.dz=clamp(F.dz+F.dzV*dt,DZMIN-.03,DZMAX+.02);F.stepPh+=Math.min(1,Math.abs(F.dzV)*14)*dt*11;
}
function foeAI(dt){
  F.t+=dt;footwork(dt);
  if(F.st==='idle'||F.st==='guard'){F.tauntT-=dt;if(F.tauntT<=0){F.tauntT=rnd(6,12);foeSmile(rnd(.7,1),rnd(1,1.6))}}F.stun=Math.max(0,F.stun-1.4*dt);if(F.groggy>0)F.groggy-=dt;if(F.ctrCd>0)F.ctrCd-=dt;
  if(F.liverT>0){F.liverT-=dt;if(F.liverT<=0&&F.st!=='down'&&F.st!=='rise'){if(F.kd>=2)knockout(true,'Удар по печени');else knockdown('Удар по печени')}}
  var gr=F.groggy>0?1.4:1;
  switch(F.st){
    case'idle':F.next-=dt;if(F.next<=0){if(Math.random()<F.guardP){F.guardHits=0;setF('guard',rnd(.9,1.6))}else startWindup(false)}break;
    case'guard':if(F.t>F.dur){if(Math.random()<.6)startWindup(false);else{setF('idle');F.next=rnd(.2,.6)*F.pace*gr}}break;
    case'windup':foeSide();if(F.t>F.dur){
        if(F.feint){F.feint=false;F.hand=-F.hand;if(F.armBroken[F.hand<0?0:1]){setF('idle');F.next=rnd(.6,1.2);break}setF('windup',F.wind*(F.baited?.38:.55)*F.slowMul*(F.groggy>0?1.3:1));foeSide();windupCue();
          if(F.baited){F.log.bait++;_hp.copy(F.gw[0]).add(F.gw[1]).multiplyScalar(.5);_hp.y+=.24;popWorld('Финт!',_hp,'warn')}}
        else{setF('strike');sfx.whoosh()}}break;
    case'strike':
      if(F.t>STRIKE){var rc=F.chain>0?.24:.36;
        if(P.dodge>.04&&P.dodgeDir===-F.side){setF('open',.95);G.shake=0;_hp.copy(F.gw[0]).add(F.gw[1]).multiplyScalar(.5);_hp.y+=.2;popWorld('Мимо! Бей',_hp,'gold')}
        else if(P.dodge>.04){var hk=F.kind==='hook';playerHurt(F.dmg*(hk?1.25:.5),hk,F.hand);_hp.copy(F.gw[F.hand<0?0:1]);_hp.y+=.1;popWorld(hk?'Под хук':'Вскользь',_hp,'warn');setF('recover',rc)}
        else if(P.block){P.st-=F.sty.bs;
          if(CHEAT.stam)P.st=Math.max(P.st,0);if(P.st<0){P.st=0;playerHurt(F.dmg*.8,false,F.hand);popAt('Блок пробит',canvas.clientWidth/2,canvas.clientHeight*.6)}
          else{if(!CHEAT.god)P.hp-=F.dmg*F.sty.bc;sfx.block();P.kick=.4;P.bkv+=2.6;P.gl[0].sqv+=7;P.gl[1].sqv+=7;F.glv[F.hand<0?0:1].z-=3;P.cv.x+=1.6;if(!REDUCED)G.shake=Math.max(G.shake,.02);if(P.hp<=0){P.hp=0;G.mode='lost';G.endT=0;P.block=false;sfx.thud()}}
          setF('recover',rc*.83)}
        else{playerHurt(F.dmg*(F.kind==='hook'?1.25:1),F.kind==='hook',F.hand);setF('recover',rc)}
      }break;
    case'recover':if(F.t>F.dur){if(F.chain>0){F.chain--;startWindup(true)}else{setF('idle');F.next=rnd(.7,1.5)*F.pace*F.slowMul*gr}}break;
    case'open':if(F.t>F.dur){setF('idle');F.next=rnd(.3,.7)*F.pace}break;
    case'stagger':if(F.t>F.dur){setF('idle');F.next=rnd(.35,.9)*F.pace*gr}break;
    case'down':
      if(rdSettled()){F.countT+=dt;if(F.countT>=.8){F.countT=0;F.count++;popAt(String(F.count),canvas.clientWidth/2,canvas.clientHeight*.3,'warn');sfx.tick();if(F.count>=F.riseAt){setF('rise',1.9);rdRiseStart()}}}break;
    case'rise':if(F.t>F.dur){if(RD.on)rdEnd();F.fa=0;F.fw=0;setF('idle');F.next=1.1;F.groggy=3.5;foeSmile(1,1.9)}break;
  }
}
/* his gloves are solid: kept out of his head (and the cuff out of his chin), off his chest and apart from each other.
   A glove driven into his face by your punch snaps his head back */
var _hc=new T.Vector3(),_gd=new T.Vector3(),_ga2=new T.Vector3(),_gb=new T.Vector3();
function pushOut(gp,gv,c,R,head){_gd.subVectors(gp,c);var l=_gd.length();if(l>=R||l<1e-5)return 0;_gd.multiplyScalar(1/l);gp.copy(c).addScaledVector(_gd,R);
  var vn=gv.dot(_gd);if(vn<0){gv.addScaledVector(_gd,-vn*1.25);if(head&&vn<-1.5){F.hv.x+=vn*.8;F.hv.y+=_gd.x*vn*.5;sfx.block()}}return 1}
function gloveCollide(){
  if(RD.on||!F.gl)return;
  _hc.set(0,.01,.005).applyMatrix4(HEADV.group.matrixWorld);RIG.root.worldToLocal(_hc);
  for(var i=0;i<2;i++){var gp=F.gl[i],gv=F.glv[i];
    pushOut(gp,gv,_hc,.175,true);
    /* the cuff hangs about 8 cm below and behind the fist */
    _gb.copy(gp);_gb.y-=.075;_gb.z-=.035;_gd.subVectors(_gb,_hc);var l=_gd.length();if(l<.125&&l>1e-5){_gd.multiplyScalar((.125-l)/l);gp.add(_gd)}
    /* chest: a capsule down the middle of the torso */
    var ty=clamp(gp.y,1.0,1.38);_ga2.set(0,ty,.03+(ty-1)*.08);pushOut(gp,gv,_ga2,.21,false);
  }
  _gd.subVectors(F.gl[1],F.gl[0]);var d=_gd.length();if(d<.13&&d>1e-5){_gd.multiplyScalar((.13-d)/d*.5);F.gl[1].add(_gd);F.gl[0].sub(_gd)}
}
function spring(r,v,k,c,dt,lim){v.x+=(-k*r.x-c*v.x)*dt;v.y+=(-k*r.y-c*v.y)*dt;v.z+=(-k*r.z-c*v.z)*dt;r.x=clamp(r.x+v.x*dt,-lim,lim);r.y=clamp(r.y+v.y*dt,-lim,lim);r.z=clamp(r.z+v.z*dt,-lim,lim)}
var _m=new T.Matrix4(),_bx=new T.Vector3(),_by=new T.Vector3(),_bz=new T.Vector3();
/* out = h made perpendicular to unit ax; never degenerate */
function perpTo(out,h,ax){out.copy(h).addScaledVector(ax,-h.dot(ax));if(out.lengthSq()<1e-4){if(Math.abs(ax.y)<.9)out.set(0,1,0);else out.set(0,0,1);out.addScaledVector(ax,-out.dot(ax))}return out.normalize()}
function segTo(obj,A,B,hint,L){
  _by.subVectors(B,A);var len=_by.length();if(len<1e-5)return;_by.multiplyScalar(1/len);
  perpTo(_bz,hint,_by);_bx.crossVectors(_by,_bz);_m.makeBasis(_bx,_by,_bz);obj.quaternion.setFromRotationMatrix(_m);obj.position.copy(A);obj.scale.set(1,len/L,1);
}
function aimZ(obj,P0,dir,up,sc){
  _bz.copy(dir).normalize();perpTo(_by,up,_bz);
  _bx.crossVectors(_by,_bz);_m.makeBasis(_bx,_by,_bz);obj.quaternion.setFromRotationMatrix(_m);obj.position.copy(P0);obj.scale.setScalar(sc);
}
var _u=new T.Vector3(),_pp=new T.Vector3();
function ik2(S,Wr,L1,L2,pole,E){
  _u.subVectors(Wr,S);var d=_u.length();if(d<1e-5){E.copy(S);E.y-=L1;return}_u.multiplyScalar(1/d);
  var dd=Math.min(d,(L1+L2)*0.999),a=(L1*L1-L2*L2+dd*dd)/(2*dd),h=Math.sqrt(Math.max(0,L1*L1-a*a)),sc=d/dd;
  _pp.copy(pole).addScaledVector(_u,-pole.dot(_u));var pl=_pp.length();if(pl<1e-5)_pp.set(0,-1,0);else _pp.multiplyScalar(1/pl);
  E.copy(S).addScaledVector(_u,a*sc).addScaledVector(_pp,h);
}
/* one arm: shoulder S, wanted glove centre Gt, wanted knuckle direction K.
   The wrist sits GLOFF behind the glove, reach is clamped so bones never stretch,
   and the glove follows the forearm axis (only a slight wrist bend toward K). Returns the real glove centre in out. */
var _W2=new T.Vector3(),_u2=new T.Vector3(),_ax=new T.Vector3(),_ga=new T.Vector3(),_bk=new T.Vector3();
function armSolve(a,S,Gt,K,pole,back,sc,out,upHint){
  _W2.copy(Gt).addScaledVector(K,-GLOFF*sc);
  _u2.subVectors(_W2,S);var d=_u2.length(),R=(UPL+FOL)*0.985;
  if(d>R)_W2.copy(S).addScaledVector(_u2,R/d);else if(d<0.12&&d>1e-5)_W2.copy(S).addScaledVector(_u2,0.12/d);
  ik2(S,_W2,UPL,FOL,pole,_E);(a.E||(a.E=new T.Vector3())).copy(_E);
  _ax.subVectors(_W2,_E).normalize();
  _ga.copy(_ax).multiplyScalar(0.84).addScaledVector(K,0.16).normalize();
  out.copy(_W2).addScaledVector(_ga,GLOFF*sc);
  if(a.up.group.visible)segTo(a.up.group,S,_E,upHint,UPL);
  perpTo(_bk,back,_ax);segTo(a.fo.group,_E,_W2,_bk,FOL);
  aimZ(a.gl.group,out,_ga,back,sc);
}
function lerpDir(cur,want,k){cur.lerp(want,k);if(cur.lengthSq()<1e-6)cur.copy(want);return cur.normalize()}
var GT=[new T.Vector3(),new T.Vector3()],_fwr=new T.Vector3(),_rt=new T.Vector3(),_Kw=new T.Vector3(),_Pw=new T.Vector3(),_Bw=new T.Vector3(),_S=new T.Vector3(),_W=new T.Vector3(),_E=new T.Vector3(),_Gc=new T.Vector3(),_f=new T.Vector3(),_pole=new T.Vector3(),_up=new T.Vector3(),_fw=new T.Vector3(),_rq=new T.Quaternion(),_A=new T.Vector3(),_B=new T.Vector3();
function poseFoe(dt){
  var t=G.anim,L=GT[0],R=GT[1],twist=0,lean=0,lungeT=0,hurt=1-F.hp/F.max,g,k,i,lying=F.st==='down'||F.st==='rise',bob=Math.sin(t*5)*.008;
  L.set(-0.125,1.47+bob,0.25);R.set(0.125,1.47-bob,0.25);
  switch(F.st){
    case'guard':L.set(-0.07,1.6,0.33);R.set(0.07,1.6,0.33);lean=.06;break;
    case'windup':g=F.hand<0?L:R;k=Math.min(1,F.t/F.dur);if(F.kind==='hook')g.set(F.hand*0.36,1.47,0.14);else g.set(F.hand*0.2,1.53,0.17);twist=F.hand*.3*k;lean=-.05;break;
    case'strike':g=F.hand<0?L:R;if(P.block){_hp.copy(P.gl[0].c).add(P.gl[1].c).multiplyScalar(.5);_hp.x+=F.hand*.02;_hp.z-=.09}else{_hp.copy(camera.position);_hp.x+=F.hand*0.04;_hp.y-=0.06;_hp.z-=0.16}RIG.root.worldToLocal(_hp);g.copy(_hp);
      if(F.kind==='hook'&&F.t<STRIKE*.6)g.x+=F.hand*.3;twist=-F.hand*.34;lean=.17;lungeT=.2;break;
    case'recover':lungeT=.06;lean=.06;break;
    case'open':L.set(-0.26,1.2,0.25);R.set(0.27,1.24,0.26);lean=.22;lungeT=.1;break;
    case'stagger':L.set(-0.22,1.32,0.2);R.set(0.22,1.32,0.2);break;
    case'down':L.set(-0.42,1.22,0.08);R.set(0.42,1.26,0.08);break;
    case'rise':L.set(-0.2,1.34,0.26);R.set(0.2,1.34,0.26);lean=.22*(1-Math.min(1,F.t/F.dur));break;
  }
  if(F.armBroken[0])L.set(-0.27,1.06+Math.sin(t*2.1)*.01,0.1);if(F.armBroken[1])R.set(0.27,1.06+Math.sin(t*2.3)*.01,0.1);
  for(i=0;i<2;i++){var tg=i?R:L,gp=F.gl[i],gv=F.glv[i];
    if(F.st==='strike'&&((F.hand<0)===(i===0))){gp.lerp(tg,1-Math.exp(-46*dt));gv.set(0,0,0)}
    else{k=lying||F.armBroken[i]?55:170;gv.x+=((tg.x-gp.x)*k-gv.x*20)*dt;gv.y+=((tg.y-gp.y)*k-gv.y*20)*dt;gv.z+=((tg.z-gp.z)*k-gv.z*20)*dt;gp.addScaledVector(gv,dt)}}
  gloveCollide();
  spring(F.hr,F.hv,150,12,dt,.85);spring(F.tr,F.tv,120,13,dt,.55);spring(F.hpp,F.hpv,260,22,dt,.05);
  F.recoilV+=(-80*F.recoil-11*F.recoilV)*dt;F.recoil+=F.recoilV*dt;
  F.sv+=(-60*F.sx-9*F.sv)*dt;F.sx=clamp(F.sx+F.sv*dt,-.16,.16);
  F.lunge+=(lungeT-F.lunge)*(1-Math.exp(-14*dt));
  if(RD.on){}
  else if(F.st==='down'){
    F.fw+=9*Math.sin(F.fa)*dt;F.fa+=F.fw*dt;
    if(F.fa>=1.48){F.fa=1.48;
      if(F.fw>1.3){if(!F.downHit){F.downHit=true;sfx.thud();vib(80);if(!REDUCED)G.shake=Math.max(G.shake,.08);if(G.mode==='ko')sfx.bell(3);
          RIG.headG.getWorldPosition(_hp);for(k=0;k<30+WOUNDS.length*4;k++)stain(_hp.x+rnd(-.12,.12),_hp.z+rnd(-.12,.12),rnd(.004,.012));_hp.y=.08;bloodBurst(_hp,Math.min(70,8+WOUNDS.length*5),0,.3,0,.8)}
        F.fw*=-.24;F.hv.x+=6;F.tv.x-=2;F.glv[0].y+=2;F.glv[1].y+=2}else F.fw=0}
    if(F.fa<0){F.fa=0;F.fw=Math.abs(F.fw)}
    if(F.downHit&&WOUNDS.length&&Math.random()<0.25){RIG.headG.getWorldPosition(_hp);pool(_hp.x+rnd(-.06,.06),_hp.z+rnd(-.06,.06),Math.min(0.12,0.02+F.t*0.008))}
  }else if(F.st==='rise'){k=Math.min(1,F.t/F.dur);F.fa=1.48*(1-k*k*(3-2*k))}
  var sway=lying?0:Math.sin(t*1.15)*(.04+hurt*.04+(F.groggy>0?.04:0));
  if(!RD.on){
  RIG.root.position.set(sway*.35+F.sx,Math.sin(F.fa)*.1+Math.abs(Math.sin(F.stepPh))*Math.min(.012,Math.abs(F.dzV)*.08),ROOTZ+F.dz+F.lunge+F.recoil*0.6-Math.sin(F.fa)*.2);
  RIG.root.rotation.set(-F.fa*(F.fz?.92:1),0,F.fz*F.fa*.35);
  legsPose(dt,sway,twist,lungeT,hurt,bob);
  RIG.torsoG.rotation.set(0.2+F.tr.x+lean+hurt*.08,F.tr.y+twist,F.tr.z+Math.sin(t*1.15+.6)*.03);
  RIG.headG.rotation.set(-0.12+F.hr.x+Math.sin(t*2.3)*.015+hurt*.1,F.hr.y,F.hr.z);
  RIG.headG.position.set(F.hpp.x,0.545+F.hpp.y,0.03+F.hpp.z);
  }
  /* jaw: drops open when he is hit or gasping, snaps shut on an uppercut, hangs once broken */
  var jb=F.jawOff?0:(F.broken.jaw||0),joT=F.jawOff?0:(jb?0.6*jb:0)+(hurt>.45?.3+.25*Math.sin(t*6):0)+(F.st==='stagger'?.5:0)+(lying?.8:0);
  F.jov+=((joT-F.jo)*(jb?70:170)-F.jov*(jb?7:17))*dt;F.jo=clamp(F.jo+F.jov*dt,-.1,2.6);
  F.jxv+=(-F.jx*(jb?40:300)-F.jxv*(jb?5:25)-F.hv.y*(jb?1.5:.15))*dt;F.jx=clamp(F.jx+F.jxv*dt,-2.2,2.2);
  HEADV.mat.uniforms.uJaw.value.set(F.jawOff?0:F.jo*0.16,F.jawOff?0:F.jx*0.0035+F.jawShift,-0.008,0.006);
  if(!F.jawOff)teethPose(F.jo*0.16,F.jx*0.0035+F.jawShift);
  /* expression: grin that shows the teeth */
  var smt=0;if(F.smTime>0){F.smTime-=dt;smt=F.smA}if(lying||G.mode!=='fight'&&G.mode!=='lost')smt=0;
  F.sm+=(smt-F.sm)*(1-Math.exp(-(smt>F.sm?11:6)*dt));HEADV.mat.uniforms.uSmile.value.x=F.sm;
  F.blinkN-=dt;if(F.blinkN<=0){F.blinkT=.11;F.blinkN=rnd(2.5,5)}if(F.blinkT>0)F.blinkT-=dt;
  HEADV.mat.uniforms.uBlink.value=(F.blinkT>0||lying)?1:0;
  HEADV.tickHits(G.anim);TORSOV.tickHits(G.anim);
  if(RD.on)return;
  RIG.root.updateMatrixWorld(true);
}
/* arms: IK from where the (physical) shoulders really are to the gloves; the guard sways with the chest */
function poseArms(dt){
  var i,lying=false;
  RIG.root.getWorldQuaternion(_rq);_up.set(0,1,0).applyQuaternion(_rq);_fwr.set(0,0,1).applyQuaternion(_rq);_rt.set(1,0,0).applyQuaternion(_rq);
  var sk=1-Math.exp(-30*dt);
  for(i=0;i<2;i++){var side=i?1:-1,a=FARM[i],act=(F.hand<0)===(i===0),hk=F.kind==='hook';
    _S.copy(SHO[i]);RIG.torsoG.localToWorld(_S);_Gc.copy(F.gl[i]);RIG.root.localToWorld(_Gc);if(!(act&&F.st==='strike'))_Gc.add(RD.off);
    /* knuckle direction and elbow pole per state: guard = fists up, elbows tucked; straight = elbow down behind the fist; hook = elbow up and out */
    if(F.armBroken[i]){_Kw.copy(_up).multiplyScalar(-.75).addScaledVector(_rt,side*.6).addScaledVector(_fwr,.2);_Pw.copy(_fwr).multiplyScalar(.8).addScaledVector(_rt,-side*.6).addScaledVector(_up,.3)}
    else if(act&&F.st==='strike'){_Kw.subVectors(_Gc,_S).normalize();if(hk){_Kw.multiplyScalar(.6).addScaledVector(_rt,-side*.6).normalize();_Pw.copy(_rt).multiplyScalar(side).addScaledVector(_up,.7)}
      else _Pw.copy(_up).multiplyScalar(-1).addScaledVector(_rt,side*.45)}
    else if(act&&F.st==='windup'){if(hk){_Kw.copy(_fwr).multiplyScalar(.5).addScaledVector(_rt,-side*.55).addScaledVector(_up,.4);_Pw.copy(_rt).multiplyScalar(side).addScaledVector(_up,.35)}
      else{_Kw.copy(_fwr).multiplyScalar(.75).addScaledVector(_up,.55);_Pw.copy(_up).multiplyScalar(-1).addScaledVector(_rt,side*.4)}}
    else if(lying||F.st==='open'||F.st==='stagger'){_Kw.copy(_fwr).multiplyScalar(.8).addScaledVector(_up,.2).addScaledVector(_rt,-side*.1);_Pw.copy(_up).multiplyScalar(-1).addScaledVector(_rt,side*.5).addScaledVector(_fwr,-.15)}
    else{_Kw.copy(_fwr).multiplyScalar(.5).addScaledVector(_up,.82).addScaledVector(_rt,-side*.18);_Pw.copy(_up).multiplyScalar(-1).addScaledVector(_rt,side*.38).addScaledVector(_fwr,.12)}
    _Kw.normalize();
    if(!F.K){F.K=[_Kw.clone(),_Kw.clone()];F.PL=[_Pw.clone(),_Pw.clone()]}
    lerpDir(F.K[i],_Kw,sk);F.PL[i].lerp(_Pw,sk);
    _Bw.copy(_up).addScaledVector(_fwr,F.K[i].dot(_up)*1.5);
    armSolve(a,_S,_Gc,F.K[i],F.PL[i],_Bw,1,F.gw[i],_fwr);
  }
  shadow.position.x=RIG.root.position.x;shadow.position.z=ROOTZ+F.dz-Math.sin(F.fa)*0.75;shadow.scale.set(0.75+Math.sin(F.fa)*0.2,0.5+Math.sin(F.fa)*1.6,1);
}

/* ---------- player ---------- */
var PNAME={jab:'Джеб',hook:'Хук',upper:'Апперкот',over:'Оверхенд'},MNAME={jab:'Мега-джеб',hook:'Мега-хук',upper:'Мега-апперкот',over:'Мега-оверхенд'};
/* elbow poles in camera space (x is mirrored per hand) */
var POLE={guard:[.5,-1,.35],jab:[.55,-1,.2],hook:[1,.6,.1],upper:[.3,-1,.4],over:[.9,.75,.25]};
for(var gi=0;gi<2;gi++){var gs_=gi?1:-1;P.gl.push({side:gs_,phase:0,t:0,dur:.1,back:.14,type:'jab',sx:0,sy:0,dx:0,dy:0,pw:1,weak:false,bloody:0,mega:false,fl:false,
  target:new T.Vector3(),rest:new T.Vector3(gs_*.12,-.15,-.38),fw:0,e:0,dwell:0,hitO:null,hitL:null,sq:0,sqv:0,K:new T.Vector3(0,.5,-1).normalize(),PL:new T.Vector3(),c:new T.Vector3(),lp:new T.Vector3(),trail:false})}
P.mega=0;P.megaReady=false;P.megaIdle=0;P.flurry=null;P.lunge=0;P.glow=0;P.bk=0;P.bkv=0;
var RAY=new T.Raycaster(),NDC=new T.Vector2(),INV=new T.Matrix4(),LO=new T.Vector3(),LD=new T.Vector3();
function raySphere(o,d,c,r){var ox=o.x-c.x,oy=o.y-c.y,oz=o.z-c.z,b=ox*d.x+oy*d.y+oz*d.z,cc=ox*ox+oy*oy+oz*oz-r*r,disc=b*b-cc;if(disc<0)return -1;var t=-b-Math.sqrt(disc);return t>0?t:-1}
function aim(sx,sy,noGlove){noGlove=noGlove||CHEAT.thru;
  var rc=canvas.getBoundingClientRect();NDC.set((sx-rc.left)/rc.width*2-1,-((sy-rc.top)/rc.height*2-1));RAY.setFromCamera(NDC,camera);
  var o=RAY.ray.origin,d=RAY.ray.direction,best={kind:'none',t:1e9},k,t,h,vs=[HEADV,TORSOV];
  if(!noGlove)for(k=0;k<2;k++){if(F.armBroken[k])continue;t=raySphere(o,d,F.gw[k],.068);if(t>0&&t<best.t)best={kind:'glove',t:t,k:k}}
  for(k=0;k<2;k++){INV.copy(vs[k].group.matrixWorld).invert();LO.copy(o).applyMatrix4(INV);LD.copy(d).transformDirection(INV);
    h=vs[k].raycast(LO,LD);if(h&&h.t<best.t){best=h;best.kind=k?'torso':'head';best.v=vs[k]}}
  if(best.kind==='none')best.t=Math.max(0.3,(RIG.root.position.z+.25-o.z)/Math.min(-0.2,d.z));
  best.point=new T.Vector3().copy(o).addScaledVector(d,best.t);return best;
}
function swipeAim(x,y,dx,dy,ng){var r=aim(x,y,ng);if(r.kind!=='none')return{r:r,x:x,y:y};var l=Math.sqrt(dx*dx+dy*dy),ux,uy,k;
  if(l<1){/* no direction (flurry): pull toward the head */var hp=headScreen();ux=hp[0]-x;uy=hp[1]-y;l=Math.sqrt(ux*ux+uy*uy)||1;ux/=l;uy/=l}else{ux=dx/l;uy=dy/l}
  for(k=12;k<=300;k+=12){r=aim(x+ux*k,y+uy*k,ng);if(r.kind!=='none')return{r:r,x:x+ux*k,y:y+uy*k}}return{r:aim(x,y,ng),x:x,y:y}}
function headScreen(){RIG.headG.getWorldPosition(_hp);_hp.project(camera);return[(_hp.x*.5+.5)*canvas.clientWidth,(-_hp.y*.5+.5)*canvas.clientHeight]}
function megaAdd(n){if(P.megaReady||n<=0)return;P.mega=Math.min(100,P.mega+n);P.megaIdle=0;
  if(P.mega>=100){P.megaReady=true;banner('Мега-удар готов');sfx.ready();vib([20,30,20])}}
function megaUse(){P.megaReady=false;P.mega=0;P.megaIdle=0}
function punch(type,sx,sy,side,pw,dx,dy,buffered,opt){
  if(G.mode!=='fight'||P.block||P.dodge>.14||F.st==='down'||F.st==='rise')return;
  if(P.flurry&&!opt)return;
  BBA=type;
  if(P.megaReady&&!opt&&!buffered){megaUse();if(type==='jab'){startFlurry(sx,sy);return}opt={mega:true}}
  var g=P.gl[side<0?0:1];
  if(opt){if(g.phase===1&&!opt.fl){g=P.gl[side<0?1:0]}g.phase=0}
  else if(g.phase!==0){g=P.gl[side<0?1:0];if(g.phase!==0){if(!buffered)P.buf={a:[type,sx,sy,side,pw,dx,dy],ttl:.28};return}}
  var power=type!=='jab',cost=opt?0:power?16:5,a,mg=!!(opt&&opt.mega),fl=!!(opt&&opt.fl);
  g.weak=!opt&&P.st<cost;P.st=Math.max(0,P.st-cost);g.type=type;g.pw=pw||1;g.dx=dx||0;g.dy=dy||0;g.phase=1;g.t=0;g.mega=mg;g.fl=fl&&!mg;g.trail=false;
  if(mg){g.dur=fl?.26:.4;g.back=.3;G.cine=CHEAT.nostop?0:(clockT-CINET<2.5?(fl?.1:.16):(fl?.26:.4));CINET=clockT;sfx.charge();if(!REDUCED)retrig($('megafx'),'on');vib(30)}
  else if(fl){g.dur=power?.12:.07;g.back=power?.1:.06}
  else{g.dur=power?.2:.085;g.back=power?.24:.13}
  if(power||mg){a=swipeAim(sx,sy,g.dx,g.dy,mg);g.sx=a.x;g.sy=a.y;g.target.copy(a.r.point);if(!mg)sfx.swing()}else{a=tapAim(sx,sy);g.sx=a.x;g.sy=a.y;g.target.copy(a.r.point)}
}
var FLSEQ=['jab','jab','hook','hook','upper','hook'];
function startFlurry(sx,sy){P.flurry={sx:sx,sy:sy,i:0,t:0};banner('Мега-серия');sfx.charge();if(!REDUCED)retrig($('megafx'),'on');vib([20,20,20,20,20])}
function updFlurry(dt){var f=P.flurry;if(!f)return;
  if(G.mode!=='fight'||F.st==='down'||F.st==='rise'){P.flurry=null;return}
  f.t-=dt;if(f.t>0)return;
  var type=FLSEQ[f.i],last=f.i===FLSEQ.length-1,side=type==='hook'?(f.i===2?-1:1):(f.i%2?1:-1);
  if(last)side=-1;
  var hp=headScreen(),x=f.i<2?f.sx:hp[0]+rnd(-14,14),y=f.i<2?f.sy:hp[1]+rnd(-10,18);
  punch(type,x,y,side,1.15,0,0,false,{fl:true,mega:last});f.i++;f.t=last?0:(FLSEQ[f.i]==='jab'?.1:.15);
  if(f.i>=FLSEQ.length)P.flurry=null}
function resolvePunch(g){
  var res=aim(g.sx,g.sy,g.mega);G.stats.thrown++;
  if(res.kind==='none'&&(g.type!=='jab'||g.mega||g.fl))res=swipeAim(g.sx,g.sy,g.dx,g.dy,g.mega).r;
  var k=g.mega?2:1,s=g.side;
  if(g.type==='hook'){P.cv.y+=s*1.6*k;P.cv.z+=s*1.0*k}else if(g.type==='upper')P.cv.x+=2.2*k;else if(g.type==='over')P.cv.x-=2.6*k;else P.cv.x+=.7;
  if(res.kind==='none'){sfx.whoosh();G.combo=0;if(Math.random()<.6)foeSmile(.85,.9);popAt('Мимо',g.sx,g.sy-30,'miss');if(!g.fl&&!g.mega)foeCounter();return}
  g.target.copy(res.point).addScaledVector(RAY.ray.direction,-.07);
  /* contact: the glove stays on what it hit for a moment and rides along with it, then springs back */
  var hb=res.kind==='glove'?FARM[res.k].gl.group:res.v?res.v.group:null,pw=g.type!=='jab'||g.mega;
  if(hb){hb.updateMatrixWorld(true);g.hitO=hb;g.hitL=(g.hitL||new T.Vector3()).copy(g.target);hb.worldToLocal(g.hitL);g.dwell=g.mega?.12:res.kind==='glove'?.05:pw?.075:.045}
  g.sqv+=g.mega?13:pw?9:5.5;
  hitFoe(res,g);
}
function retAt(x,y,text,mega){var e=document.createElement('div');e.className='ret'+(mega?' mega':'');e.style.left=x+'px';e.style.top=y+'px';popsEl.appendChild(e);
  setTimeout(function(){if(e.parentNode)e.parentNode.removeChild(e)},400);
  if(text){var l=document.createElement('div');l.className='rlab'+(mega?' mega':'');l.textContent=text;l.style.left=clamp(x,70,canvas.clientWidth-70)+'px';l.style.top=y+'px';popsEl.appendChild(l);
    setTimeout(function(){if(l.parentNode)l.parentNode.removeChild(l)},650)}}
var REST=new T.Vector3(),GW=new T.Vector3(),GP=new T.Vector3(),CR=new T.Vector3(),CU=new T.Vector3(),CF=new T.Vector3(),PFW=new T.Vector3(),PGLC=[new T.Color(),new T.Color()];
var _tm=new T.Vector3(),_Ks=new T.Vector3(),_Kg=new T.Vector3(),_Pg=new T.Vector3(),_Ps=new T.Vector3(),_tr=new T.Vector3();
var PGLMAT=voxMat();
function updPlayer(dt,rdt){
  CR.setFromMatrixColumn(camera.matrixWorld,0);CU.setFromMatrixColumn(camera.matrixWorld,1);CF.setFromMatrixColumn(camera.matrixWorld,2);PFW.copy(CF).negate();
  updFlurry(rdt);
  var lungeT=0,anyMega=false;
  for(var i=0;i<2;i++){
    var g=P.gl[i],s=g.side,k,e=0,arc,power=g.type!=='jab',a=PARM[i],gdt=g.mega?rdt:dt,pl;
    if(G.mode==='lost')REST.set(s*.3,-.62,-.35);else if(P.block)REST.set(s*.07,-.08,-.33+P.bk);else REST.set(s*.1,-.145+Math.sin(G.anim*2.2+i)*.005,-.37);
    g.rest.lerp(REST,1-Math.exp(-18*dt));GW.copy(g.rest).applyMatrix4(camera.matrixWorld);
    if(g.phase===1){g.t+=gdt;k=Math.min(1,g.t/g.dur);
      if(power){var w0=g.mega?.42:.28;
        if(k<w0){var q=Math.sin(k/w0*Math.PI*.5);GP.copy(GW).addScaledVector(CF,.07*q);
          if(g.mega)GP.addScaledVector(CU,.06*q).addScaledVector(CR,s*.04*q);else if(g.type==='hook')GP.addScaledVector(CR,s*.08*q);else if(g.type==='upper')GP.addScaledVector(CU,-.07*q);else if(g.type==='over')GP.addScaledVector(CU,.05*q).addScaledVector(CR,s*.03*q);else GP.addScaledVector(CR,s*.03*q)}
        else{e=Math.pow((k-w0)/(1-w0),1.8);arc=Math.sin(Math.PI*(k-w0)/(1-w0));GP.copy(GW).addScaledVector(CF,.07*(1-e)).lerp(g.target,e);
          if(g.type==='hook')GP.addScaledVector(CR,s*arc*.22);else if(g.type==='upper')GP.addScaledVector(CU,-arc*.18);else if(g.type==='over')GP.addScaledVector(CU,arc*.15)}}
      else{e=Math.pow(k,1.4);GP.copy(GW).lerp(g.target,e)}
      if(k>=1){resolvePunch(g);g.phase=2;g.t=0;GP.copy(g.target);e=1}}
    else if(g.phase===2){
      if(g.dwell>0){g.dwell-=gdt;if(g.hitO){g.target.copy(g.hitL);g.hitO.localToWorld(g.target)}GP.copy(g.target);e=1;if(g.dwell<=0)g.hitO=null}
      else{g.t+=gdt;k=Math.min(1,g.t/g.back);var ee=1-Math.pow(1-k,2.6);GP.copy(g.target).lerp(GW,ee);e=1-ee;if(k>=1){g.phase=0;g.mega=false}}}
    else GP.copy(GW);
    g.fw=e;if(g.mega)anyMega=true;
    lungeT=Math.max(lungeT,e*(g.mega?.09:power?.045:.025));
    /* shoulder swings forward and in with the punch */
    _S.set(s*(.2-.06*e),-.3+.04*e,.03-.22*e).applyMatrix4(camera.matrixWorld);
    _Kg.copy(PFW).multiplyScalar(.72).addScaledVector(CU,P.block?1:.62).addScaledVector(CR,-s*.12).normalize();
    _Ks.subVectors(GP,_S).normalize();
    if(g.type==='hook')_Ks.multiplyScalar(.55).addScaledVector(CR,-s*.7);else if(g.type==='upper')_Ks.multiplyScalar(.6).addScaledVector(CU,.65);else if(g.type==='over')_Ks.addScaledVector(CU,-.25);
    _Ks.normalize();_Kg.lerp(_Ks,g.phase?Math.min(1,e*1.3):0).normalize();lerpDir(g.K,_Kg,g.phase?1:1-Math.exp(-30*dt));
    pl=POLE.guard;_Pg.set(pl[0]*s,pl[1],pl[2]);pl=POLE[g.type];_Ps.set(pl[0]*s,pl[1],pl[2]);_Pg.lerp(_Ps,g.phase?e:0).transformDirection(camera.matrixWorld);
    g.PL.lerp(_Pg,1-Math.exp(-40*dt));
    _Bw.copy(CU).addScaledVector(PFW,g.K.dot(CU)*1.5);
    armSolve(a,_S,GP,g.K,g.PL,_Bw,0.8,g.c,PFW);
    /* glove squash and wobble on contact */
    g.sqv+=(-g.sq*620-g.sqv*24)*rdt;g.sq=clamp(g.sq+g.sqv*rdt,-.18,.34);
    if(Math.abs(g.sq)>.002){var sq=g.sq;a.gl.group.scale.set(.8*(1+sq*.45),.8*(1+sq*.45),.8*(1-sq))}
    /* motion streak behind the glove while it is travelling forward */
    if(g.phase===1&&e>0.04&&!REDUCED){
      if(g.trail){_tr.subVectors(g.c,g.lp);var dl=_tr.length();
        /* speed lines from the glove rim: two streaks either side of the travel path */
        if(dl>0.004){_tm.crossVectors(_tr,CF).normalize();var n=g.mega?4:power?3:2,rr=g.mega?.06:.05,j,sd2;
          for(sd2=-1;sd2<=1;sd2+=2)for(j=0;j<n;j++){var f=(j+.5)/n,px=g.lp.x+_tr.x*f+_tm.x*rr*sd2,py=g.lp.y+_tr.y*f+_tm.y*rr*sd2,pz=g.lp.z+_tr.z*f+_tm.z*rr*sd2;
            if(g.mega)spawn(px,py,pz,0,0,0,255,110+rnd(90),25,1,rnd(.007,.011),6,.22);
            else spawn(px,py,pz,0,0,0,255,power?232:246,power?170:236,1,power?.006:.004,6,power?.16:.1)}}}
      g.trail=true;g.lp.copy(g.c)}else g.trail=false;
    if(g.bloody>0.02){PGLC[i].setRGB(238,230,214).lerp(_bcol,g.bloody*0.85);if(Math.abs(PGLC[i].r-(a.lastR||0))>3){a.lastR=PGLC[i].r;tintGlove(a.gl,PGLC[i],g.bloody)}}
    if(P.megaReady&&!REDUCED&&Math.random()<.55)spawn(g.c.x+rnd(-.05,.05),g.c.y+rnd(-.03,.04),g.c.z+rnd(-.05,.05),rnd(-.1,.1),rnd(.2,.6),rnd(-.1,.1),255,90+rnd(110),20,1,rnd(.006,.013),7,rnd(.25,.45));
  }
  P.lunge+=(lungeT-P.lunge)*(1-Math.exp(-22*rdt));
  P.bkv+=(-P.bk*260-P.bkv*20)*rdt;P.bk+=P.bkv*rdt;
  var gw=P.megaReady?.55+.25*Math.sin(G.anim*9):anyMega?1:0;P.glow+=(gw-P.glow)*(1-Math.exp(-10*rdt));
  PGLMAT.uniforms.uGlow.value.set(1,.45,.1,P.glow<.01?0:P.glow*1.4);
  if(P.buf){P.buf.ttl-=dt;if(P.buf.ttl<=0)P.buf=null;else if(P.gl[0].phase===0||P.gl[1].phase===0){var b=P.buf.a;P.buf=null;punch(b[0],b[1],b[2],b[3],b[4],b[5],b[6],true)}}
}
var _bcol=new T.Color(150,16,14);
function tintGlove(v,c,amt){for(var i=0;i<v.n;i++){if(!v.type[i]||v.fl[i]===1)continue;var n=0.92+0.12*hash1(i),bl=hash1(i*3+1)<amt*1.2;
  v.rgb[i*3]=clamp((bl?140:c.r)*n,0,255);v.rgb[i*3+1]=clamp((bl?14:c.g)*n,0,255);v.rgb[i*3+2]=clamp((bl?14:c.b)*n,0,255);v.wet[i]=bl?220:56}
  v.allCol()}
function dodge(dir){if(G.mode!=='fight'||P.dodge>0||P.dodgeCd>0)return;if(F&&F.st==='windup'&&F.feint)F.baited=true;P.dodge=DODGE;P.dodgeDir=dir;P.dodgeCd=DODGE+.28;P.st=Math.max(0,P.st-8);sfx.whoosh()}

/* ---------- camera ---------- */
var CAM0=new T.Vector3(0,1.6,0),LOOK0=new T.Vector3(0,1.5,-0.8),_lk=new T.Vector3(),BASEFOV=50,fovNow=0;
function updCamera(dt){
  var x=0,y=0,roll=0,k;
  if(P.dodge>0){k=Math.sin(Math.PI*(1-P.dodge/DODGE));x=P.dodgeDir*.3*k;y=-.08*k;roll=-P.dodgeDir*.2*k}
  P.kick*=Math.exp(-7*dt);G.shake*=Math.exp(-9*dt);G.fovK*=Math.exp(-6.5*dt);spring(P.cr,P.cv,95,9,dt,.5);
  var sh=G.shake,f=G.fall;
  camera.position.set(CAM0.x+x+rnd(-sh,sh),CAM0.y+y+rnd(-sh,sh)-f*1.2-P.lunge*.25,CAM0.z+f*.25+P.kick*.08-G.fovK*.04-P.lunge);
  G.cineZ+=((G.cine>0?1:0)-G.cineZ)*(1-Math.exp(-(G.cine>0?14:6)*dt));
  G.koLook+=((F&&F.st==='down'?1:0)-G.koLook)*(1-Math.exp(-2.5*dt));
  _lk.copy(LOOK0);_lk.x+=x*.5;_lk.y+=f*2.6+P.kick*.4;
  if(G.koLook>.01){RIG.headG.getWorldPosition(_hp);_lk.lerp(_hp,G.koLook*0.85)}
  if(INSPECT){camera.position.copy(INSPECT.pos);_lk.copy(INSPECT.look)}
  k=(INSPECT&&INSPECT.fov)||BASEFOV-G.fovK*6-G.cineZ*7;if(Math.abs(k-fovNow)>.05){fovNow=k;camera.fov=k;camera.updateProjectionMatrix()}
  camera.lookAt(_lk);if(!INSPECT){camera.rotateZ(roll+f*.4+(sh>0?rnd(-sh,sh)*.6:0));camera.rotateY(P.cr.y*.25);camera.rotateX(P.cr.x*.25);camera.rotateZ(P.cr.z*.2)}
  camera.updateMatrixWorld();
}

/* ---------- flow ---------- */
function showEnd(win){
  var acc=G.stats.thrown?Math.round(G.stats.landed/G.stats.thrown*100):0,t=Math.floor(G.time);
  $('endT').textContent=win?F.koText||'Нокаут':'Ты на полу';
  $('endP').textContent=win?F.name+' лежит. Дальше соперник быстрее и крепче.':F.name+' оказался сильнее. Блокируй и лови его на замахе.';
  $('endS').innerHTML='<dt>Удары</dt><dd>'+G.stats.landed+' из '+G.stats.thrown+', точность '+acc+'%</dd>'+
    '<dt>Рассечения</dt><dd>'+G.stats.cuts+'</dd>'+
    '<dt>Переломы</dt><dd>'+(G.stats.fract.length?G.stats.fract.join(', '):'нет')+'</dd>'+
    '<dt>Лучшее комбо</dt><dd>×'+(G.stats.maxCombo||0)+'</dd>'+
    '<dt>Нокдауны</dt><dd>'+G.stats.kd+'</dd>'+
    '<dt>Зубы</dt><dd>выбито '+G.stats.teeth+'</dd>'+
    '<dt>Время</dt><dd>'+Math.floor(t/60)+':'+('0'+t%60).slice(-2)+'</dd>';
  $('goNext').textContent=win?'Следующий бой':'Реванш';$('end').hidden=false;$('game').classList.add('sheeted');G.mode=win?'won':'dead';
}
function startFight(next,sync){
  if(G.building)return;
  if(next)G.level++;
  /* the opponent built at load is still untouched: the first bell needs no rebuild */
  if(!next&&G.pristine===G.level){G.pristine=0;beginFight(next);return}
  G.pristine=0;
  if(sync||window.__SYNC){newFoe(G.level);beginFight(next);return}
  var st=foeSteps(G.level),i=0,btn=$('end').hidden?$('goStart'):$('goNext'),txt=btn.textContent,go=false;
  G.building=true;G.mode='load';btn.textContent='Загрузка…';btn.disabled=true;
  function done(){G.building=false;btn.disabled=false;btn.textContent=txt}
  function run(){
    try{st[i++]()}catch(e){done();G.mode='menu';frameErr(e);return}
    if(i<st.length)setTimeout(run,0);else{done();beginFight(next)}
  }
  /* one painted frame first, so the label is on screen before the work starts */
  function kick(){if(go)return;go=true;setTimeout(run,0)}
  requestAnimationFrame(kick);setTimeout(kick,120);
}
function beginFight(next){
  P.hp=next?Math.min(100,P.hp+45):100;P.st=100;P.block=false;P.dodge=0;P.dodgeCd=0;P.kick=0;P.buf=null;P.cr.set(0,0,0);P.cv.set(0,0,0);
  P.flurry=null;P.lunge=0;P.megaIdle=0;if(!next){P.mega=0;P.megaReady=false}G.cine=0;G.cineZ=0;G.tipT=(G.level===1&&!G.tipsSeen)?14:0;G.tipsSeen=true;
  P.gl.forEach(function(g,i){g.phase=0;g.fw=0;g.mega=false;g.fl=false;if(!next){g.bloody=0;PARM[i].lastR=0;paintStatic(PARM[i].gl,[[238,230,214,.22],[244,182,46,.2],[250,248,240,.1],[200,40,34,.25],[212,204,188,.2],[170,160,146,.2]])}});
  G.mode='fight';G.paused=!ADM.hidden;G.time=0;G.fall=0;G.koLook=0;G.freeze=0;G.slow=0;STOPT=-9;CINET=-9;last=performance.now();G.fovK=0;G.combo=0;G.comboT=0;G.stats=newStats();SU.uSweat.value=0;
  $('start').hidden=true;$('end').hidden=true;$('game').classList.remove('sheeted');$('bB').classList.remove('on');
  sfx.bell(1);
}
$('goStart').addEventListener('click',function(){audioInit();startFight(false)});
$('goNext').addEventListener('click',function(){audioInit();startFight(G.mode==='won')});

/* ---------- input ---------- */
var touches={},SWIPE=26;
/* a tap that just misses him still goes where it was clearly meant: pulled a little toward the head */
function tapAim(x,y){var r=aim(x,y);if(r.kind!=='none')return{r:r,x:x,y:y};var hp=headScreen(),ux=hp[0]-x,uy=hp[1]-y,l=Math.sqrt(ux*ux+uy*uy)||1,m=Math.min(l,SWIPE*2.4),k;ux/=l;uy/=l;
  for(k=10;k<=m;k+=10){r=aim(x+ux*k,y+uy*k);if(r.kind!=='none')return{r:r,x:x+ux*k,y:y+uy*k}}return{r:aim(x,y),x:x,y:y}}
function firePower(p){
  var dx=p.x-p.x0,dy=p.y-p.y0,mid=canvas.clientWidth/2,type,side,speed=Math.sqrt(dx*dx+dy*dy)/Math.max(16,performance.now()-p.t0),pw=clamp(.95+speed*.22,1,1.35);
  if(Math.abs(dx)>Math.abs(dy)){type='hook';side=dx>0?-1:1}else{type=dy<0?'upper':'over';side=p.x0<mid?-1:1}
  var mg=P.megaReady&&!P.flurry,ar=type==='hook'?(dx>0?' ▶':'◀ '):type==='upper'?'▲ ':'▼ ',nm=(mg?MNAME:PNAME)[type];
  if(G.mode==='fight'&&!P.block&&F.st!=='down'&&F.st!=='rise'&&!(P.flurry))retAt(p.x0,p.y0,type==='hook'&&dx<0?ar+nm:type==='hook'?nm+ar:ar+nm,mg);
  punch(type,p.x0,p.y0,side,pw,dx,dy);
}
canvas.addEventListener('pointerdown',function(e){if(G.mode!=='fight')return;e.preventDefault();touches[e.pointerId]={x0:e.clientX,y0:e.clientY,x:e.clientX,y:e.clientY,t0:performance.now(),fired:false}});
canvas.addEventListener('pointermove',function(e){var p=touches[e.pointerId];if(!p||p.fired)return;p.x=e.clientX;p.y=e.clientY;var dx=p.x-p.x0,dy=p.y-p.y0;if(dx*dx+dy*dy>=SWIPE*SWIPE){p.fired=true;firePower(p)}});
function endTouch(e){var p=touches[e.pointerId];if(!p)return;delete touches[e.pointerId];if(e.type==='pointercancel'||p.fired)return;
  p.x=e.clientX;p.y=e.clientY;var dx=p.x-p.x0,dy=p.y-p.y0;if(dx*dx+dy*dy>=SWIPE*SWIPE){firePower(p);return}if(G.mode==='fight'&&!P.block&&!P.flurry&&F.st!=='down'&&F.st!=='rise')retAt(p.x0,p.y0,P.megaReady?'Мега-серия':'',P.megaReady);punch('jab',p.x0,p.y0,p.x0<canvas.clientWidth/2?-1:1,1,0,0)}
canvas.addEventListener('pointerup',endTouch);canvas.addEventListener('pointercancel',endTouch);canvas.addEventListener('contextmenu',function(e){e.preventDefault()});
function setBlock(on){if(G.mode!=='fight')on=false;P.block=on;$('bB').classList.toggle('on',on)}
var bB=$('bB');
bB.addEventListener('pointerdown',function(e){e.preventDefault();try{bB.setPointerCapture(e.pointerId)}catch(x){}setBlock(true)});
['pointerup','pointercancel','lostpointercapture'].forEach(function(n){bB.addEventListener(n,function(){setBlock(false)})});
[['bL',-1],['bR',1]].forEach(function(b){var el=$(b[0]);el.addEventListener('pointerdown',function(e){e.preventDefault();dodge(b[1]);el.classList.add('on');setTimeout(function(){el.classList.remove('on')},180)})});
window.addEventListener('keydown',function(e){if(e.repeat)return;if(e.code==='KeyA'||e.code==='ArrowLeft')dodge(-1);else if(e.code==='KeyD'||e.code==='ArrowRight')dodge(1);else if(e.code==='Space'||e.code==='KeyS'||e.code==='ArrowDown'){e.preventDefault();setBlock(true)}});
window.addEventListener('keyup',function(e){if(e.code==='Space'||e.code==='KeyS'||e.code==='ArrowDown')setBlock(false)});

/* ---------- loop ---------- */
function resize(){var w=canvas.clientWidth||innerWidth,h=canvas.clientHeight||innerHeight,a=w/h;SWIPE=clamp(Math.min(w,h)*.065,22,40);renderer.setSize(w,h,false);camera.aspect=a;
  BASEFOV=a<1?clamp(2*Math.atan(Math.tan(12*Math.PI/180)/a)*180/Math.PI,36,60):32;fovNow=0}
window.addEventListener('resize',resize);window.addEventListener('orientationchange',function(){setTimeout(resize,250)});
/* phone locked, call, app switch: let go of everything and do not jump ahead on return */
function calm(){touches={};setBlock(false);P.buf=null}
document.addEventListener('visibilitychange',function(){calm();last=performance.now()});window.addEventListener('blur',calm);window.addEventListener('pagehide',calm);
var ftAvg=16,ftT=0,ftGood=0,prBad=9,prBadT=0,prTry=0,prFrom=0,prOff=false;
function setPR(v){PR=v;renderer.setPixelRatio(PR);resize()}
function adapt(ms){if(ms>200)return;ftAvg+=(ms-ftAvg)*.06;if(prOff||G.mode==='load')return;ftT+=ms;prBadT-=ms;if(ftT<1500)return;ftT=0;
  /* a lower resolution that bought nothing (frame rate capped by the system, or the load is not in the pixels): take it back and leave the resolution alone */
  if(prTry){if(ftAvg>prTry*.93){setPR(prFrom);prOff=true;return}prTry=0}
  if(ftAvg>23&&PR>1){prBad=PR;prBadT=30000;ftGood=0;prTry=ftAvg;prFrom=PR;setPR(Math.max(1,PR-.25))}
  else if(ftAvg<18&&PR<PRMAX){if(++ftGood>=4&&(PR+.25<prBad||prBadT<=0)){ftGood=0;setPR(Math.min(PRMAX,PR+.25))}}else ftGood=0}
var last=performance.now(),clockT=0;
/* per frame: drop the face directions that point away from the camera, hide the teeth while the mouth is shut */
function cullAll(){
  var cp=camera.position,e=TORSOV.mat.uniforms.uB2.value.elements,ba=Math.acos(clamp((e[0]+e[5]+e[10]-1)*.5,-1,1)),k,a;
  var hh=HEADV.hitsLive(G.anim)||(WARM&2)>0,th=TORSOV.hitsLive(G.anim)||(WARM&2)>0;HEADV.setHits(hh);TORSOV.setHits(th);
  HEADV.cull(cp,hh?.4:.09,Math.sin(Math.min(1.2,(hh?.4:.2)+(F.jawOff?0:F.jo*.16))));TORSOV.cull(cp,th?.4:.09,Math.sin(Math.min(1.3,(th?.4:.2)+ba)));
  for(k=0;k<LEGP.length;k++)LEGP[k].cull(cp,.07);
  for(k=0;k<2;k++){a=FARM[k];a.up.cull(cp,.07);a.fo.cull(cp,.07);a.gl.cull(cp,.07);a=PARM[k];a.fo.cull(cp,.07);a.gl.cull(cp,.1)}
  var T_=F.trauma,show=F.jawOff||F.sm>.02||F.jo>.1||F.chips.bone||(T_.mouth||0)+(T_.jaw||0)>5||(T_._head||0)>18;
  UPPERG.visible=!!show;JAWIN.visible=!!show;
}
var PH=1/120,ISLQ={v:null,d:new T.Vector3(),t:0};
/* one bad frame must never stop the picture: the loop always asks for the next frame, and a failure that keeps repeating clears the loose parts, then restarts the fight */
var ERRN=0,ERRC=0,ERRMSG='';
function frameErr(e){
  ERRN++;ERRC++;ERRMSG=String(e&&e.message||e)+' '+String(e&&e.stack||'').split('\n').slice(1,3).join(' ').replace(/\s+/g,' ').slice(0,160);
  try{console.error(e)}catch(x){}
  if(ERRMSG.slice(0,60)!==BBE){BBE=ERRMSG.slice(0,60);bbLog('ошибка: '+BBE,0)}
  if(ERRC===5){try{clearPieces();clearRopes();ISLQ.v=null;WOUNDS.length=0;DRIPS.length=0}catch(x){}}
  else if(ERRC>=40){ERRC=0;try{if(G.mode!=='load'&&G.mode!=='menu'){G.building=false;startFight(false,true)}}catch(x){}}
}
function frame(now){
  try{step(now);ERRC=0}catch(e){frameErr(e)}
  requestAnimationFrame(frame);
}
/* black box: slow frames, errors, a lost context and a tab that died on screen survive a reload; the admin panel shows them. At most BBMAX events */
var BBMAX=12,BB={runs:0,lost:0,ev:[],at:null,open:false},BBA='',BBT=0,BBE='';
try{var _bb=JSON.parse(localStorage.getItem('vb_box')||'null');if(_bb&&_bb.ev)BB=_bb}catch(e){}
function bbSave(){try{localStorage.setItem('vb_box',JSON.stringify(BB))}catch(e){}}
function bbWhere(){return [G.mode,F?F.st:'','бой '+G.level,BBA]}
function bbLog(kind,ms,w){var d=new Date();BB.ev.push([d.getHours()+':'+('0'+d.getMinutes()).slice(-2),kind,ms].concat(w||bbWhere()));if(BB.ev.length>BBMAX)BB.ev.shift();bbSave()}
BB.runs++;if(BB.open){BB.lost++;bbLog('обрыв',0,BB.at||[])}BB.open=true;BB.at=null;bbSave();
document.addEventListener('visibilitychange',function(){BB.open=!document.hidden;bbSave()});window.addEventListener('pagehide',function(){BB.open=false;bbSave()});
var DG={n:0,t:0,w:0,w0:0,st:0,fps:0};
function diag(raw){
  if(document.hidden||G.paused||(G.mode!=='fight'&&G.mode!=='ko'))return;
  if(raw>250)bbLog('кадр',Math.round(raw));
  BBT+=raw;if(BBT>2000){BBT=0;BB.at=bbWhere();bbSave()}
  DG.n++;DG.t+=raw;if(raw>DG.w)DG.w=raw;if(raw>60)DG.st++;
  if(DG.t>=5000){DG.fps=DG.n/DG.t*1000;DG.w0=DG.w;DG.n=0;DG.t=0;DG.w=0}
}
function step(now){
  var raw=now-last,dt=Math.max(0,Math.min(.05,raw/1000))*(window.__TS||1);last=now;adapt(raw);diag(raw);clockT+=dt;
  if(G.mode==='load'||CTXLOST)return;
  if(G.paused){renderer.render(scene,camera);return}
  if(CHEAT.slow)dt*=.45;
  if(G.mode==='fight'){if(CHEAT.god)P.hp=100;if(CHEAT.stam)P.st=100;if(CHEAT.immortal&&F.hp<1)F.hp=1;
    if(CHEAT.mega&&!P.megaReady&&!P.flurry){P.mega=100;P.megaReady=true}}
  var sdt=dt;
  if(G.freeze>0){G.freeze-=dt;sdt=0}else if(G.cine>0){G.cine-=dt;sdt=dt*.22}else if(G.slow>0){G.slow-=dt;sdt=dt*.28}
  if(G.tipT>0&&G.mode==='fight'){G.tipT-=dt;if(G.stats.thrown>=7)G.tipT=Math.min(G.tipT,.01)}
  G.anim+=sdt;
  if(G.mode==='fight'){
    G.time+=sdt;SU.uSweat.value=Math.min(0.28,0.04+G.time/150);
    P.st=Math.min(100,P.st+(P.block?9:19)*sdt);if(P.dodge>0)P.dodge=Math.max(0,P.dodge-sdt);if(P.dodgeCd>0)P.dodgeCd-=sdt;if(G.comboT>0)G.comboT-=sdt;
    P.megaIdle+=sdt;if(!P.megaReady&&P.megaIdle>2.5)P.mega=Math.max(0,P.mega-4*sdt);
    foeAI(sdt);
  }else if(G.mode==='ko'){F.t+=sdt;G.endT+=dt;if(G.endT>3.2)showEnd(true)}
  else if(G.mode==='lost'){G.endT+=dt;G.fall=Math.min(1,G.fall+dt*1.6);if(G.endT>1.5)showEnd(false)}
  else if(G.mode==='won'||G.mode==='dead'){F.t+=sdt}
  MESHT=0;if(ISLQ.v&&--ISLQ.t<=0){var iv=ISLQ.v;ISLQ.v=null;checkIslands(iv,ISLQ.d)}
  updBleed(sdt);matTick(sdt);
  poseFoe(sdt);bodyPhys(sdt);updCamera(dt);updPlayer(sdt,G.freeze>0?0:dt);
  /* physics at a fixed 120 Hz whatever the frame rate: ragdoll, ropes, loose pieces */
  RD.acc+=sdt;var ns=0;while(RD.acc>=PH&&ns<10){updRopes(PH);updPieces(PH);RD.acc-=PH;ns++}if(ns>=10)RD.acc=0;
  piecesMesh();updCubes(sdt||dt*.04);updRings(sdt||dt*.3);
  HEADV.process(clockT,6,4);TORSOV.process(clockT,4,3);
  PARM[0].gl.process(clockT,2,2);PARM[1].gl.process(clockT,2,2);
  stainT-=dt;if(stainDirty&&stainT<=0){floorTex.needsUpdate=true;stainDirty=false;stainT=.12}
  updHud();
  cullAll();if(WARM>0)WARM--;
  renderer.render(scene,camera);
}
/* the system took the graphics context away (memory pressure, a long stay in the background): say so instead of showing a dead picture */
var CTXLOST=false,WARM=0;
canvas.addEventListener('webglcontextlost',function(e){e.preventDefault();CTXLOST=true;bbLog('графика отключена',0);var f=$('fail');f.textContent='Система остановила графику. Нажми, чтобы перезапустить.';f.style.display='flex';f.style.pointerEvents='auto'},false);
canvas.addEventListener('webglcontextrestored',function(){CTXLOST=false;$('fail').style.display='none';resize()},false);
$('fail').addEventListener('click',function(){if(CTXLOST)location.reload()});
G.stats=newStats();resize();requestAnimationFrame(frame);
var bstep=0,lsteps=null;
function buildNext(){
  try{
    if(bstep<4){buildAll(bstep);bstep++;setTimeout(buildNext,16);return}
    /* the first opponent in the same slices as between fights, one task each, so loading never freezes the page for seconds */
    if(!lsteps)lsteps=foeSteps(1);
    if(bstep<4+lsteps.length){lsteps[bstep-4]();bstep++;setTimeout(buildNext,0);return}
    /* compile both shader variants now, so the first punch does not stall on a shader build; one variant per task */
    if(bstep===4+lsteps.length){HEADV.setHits(true);TORSOV.setHits(true);renderer.compile(scene,camera);HEADV.setHits(false);TORSOV.setHits(false);bstep++;setTimeout(buildNext,0);return}
    renderer.compile(scene,camera);
    WARM=8;spawn(0,3,-9,0,0,0,0,0,0,0,.0005,6,.4);G.pristine=1;
    G.mode='menu';var b=$('goStart');b.disabled=false;b.textContent='В бой';
  }catch(e){$('fail').textContent='Ошибка загрузки: '+e.message;$('fail').style.display='flex';throw e}
}
setTimeout(buildNext,30);
