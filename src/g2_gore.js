
/* ---------- extreme damage: open fractures, skull, brain, eyes, mouthguard, guts, broken arms ---------- */
var _gv=new T.Vector3(),_gw=new T.Vector3(),_gn=new T.Vector3(),_gt=new T.Vector3();
/* a point fixed in a body's local frame (centimetres), read back in world space every frame */
var _bt=new T.Vector3();
/* same soft-joint blend as the shader, for points that must stay glued to a bent mesh */
function bendPt(v,o){var b=v.bend;if(!b)return o;var w=sstep(b.y0,b.y1,o.y);if(b.s<0)w=1-w;if(w>0){_bt.copy(o).applyMatrix4(b.M);o.lerp(_bt,w)}return o}
function bodyPlane(v,X,Y,Z,nx,ny,nz){var x=X*.01,y=Y*.01,z=Z*.01,st=!v.dyn;return function(o,n){if(st)v.group.updateMatrixWorld();bendPt(v,o.set(x,y,z)).applyMatrix4(v.group.matrixWorld);n.set(nx,ny,nz).transformDirection(v.group.matrixWorld);return o}}
function bodyPt(v,X,Y,Z){var x=X*.01,y=Y*.01,z=Z*.01,st=!v.dyn;return function(o){if(st)v.group.updateMatrixWorld();return bendPt(v,o.set(x,y,z)).applyMatrix4(v.group.matrixWorld)}}
var BONEC=[236,226,200,.45],BONEB=[150,20,22];
/* a jagged bone end sticking out of the wound: base inside the flesh, tip outside along n */
function boneShard(v,X,Y,Z,nx,ny,nz,len,size){
  var l=Math.sqrt(nx*nx+ny*ny+nz*nz)||1;nx/=l;ny/=l;nz/=l;
  addRope({tmp:true,n:3,seg:.01,rigid:true,size:size||.006,col:BONEC,base:BONEB,a:bodyPt(v,X-nx*.4,Y-ny*.4,Z-nz*.4),b:bodyPt(v,X+nx*len+rnd(-.3,.3),Y+ny*len+rnd(-.3,.3),Z+nz*len+rnd(-.3,.3))});
}
/* punch through every layer inside radius R (cm) down to whatever cannot break (brain, guts) */
function holeAt(v,X,Y,Z,R,FDv,isHead,wc){
  var s=v.s,x0=Math.max(0,Math.floor((X-R-v.ox)/s)),x1=Math.min(v.W-1,Math.ceil((X+R-v.ox)/s)),y0=Math.max(0,Math.floor((Y-R-v.oy)/s)),y1=Math.min(v.H-1,Math.ceil((Y+R-v.oy)/s)),z0=Math.max(0,Math.floor((Z-R-v.oz)/s)),z1=Math.min(v.D-1,Math.ceil((Z+R-v.oz)/s));
  var out={rem:[]},x,y,z,i,t,dx,dy,dz,r2=R*R,k;
  for(z=z0;z<=z1;z++){dz=v.Z(z)-Z;for(y=y0;y<=y1;y++){dy=v.Y(y)-Y;for(x=x0;x<=x1;x++){i=x+v.W*(y+v.H*z);t=v.type[i];if(!t||(t===CORE&&!wc)||t===TOOTH||(v.fl[i]&F_EYE))continue;
    dx=v.X(x)-X;if(dx*dx+dy*dy*1.1+dz*dz>r2*(0.85+0.3*hash1(i)))continue;removeCell(v,i,x,y,z,out)}}}
  spill(v,out.rem,true,FDv,isHead);
  for(k=0;k<5;k++)woundNear(v,X+rnd(-R,R)*.8,Y+rnd(-R,R)*.8,Z,isHead?3:2.4);
  return out.rem.length;
}
/* remove some exposed core cells near a point: brain matter or guts falling out */
function spillCore(v,X,Y,Z,R,max,FDv,isHead){
  var s=v.s,x0=Math.max(0,Math.floor((X-R-v.ox)/s)),x1=Math.min(v.W-1,Math.ceil((X+R-v.ox)/s)),y0=Math.max(0,Math.floor((Y-R-v.oy)/s)),y1=Math.min(v.H-1,Math.ceil((Y+R-v.oy)/s)),z0=Math.max(0,Math.floor((Z-R-v.oz)/s)),z1=Math.min(v.D-1,Math.ceil((Z+R-v.oz)/s));
  var c=[],x,y,z,i,dx,dy,dz,out={rem:[]},k;
  for(z=z1;z>=z0;z--){dz=v.Z(z)-Z;for(y=y0;y<=y1;y++){dy=v.Y(y)-Y;for(x=x0;x<=x1;x++){i=x+v.W*(y+v.H*z);if(v.type[i]!==CORE)continue;dx=v.X(x)-X;if(dx*dx+dy*dy+dz*dz>R*R||!v.exp(x,y,z,i))continue;c.push(i,x,y,z)}}}
  for(k=0;k<c.length&&out.rem.length<max*7;k+=4)if(Math.random()<0.6)removeCell(v,c[k],c[k+1],c[k+2],c[k+3],out);
  spill(v,out.rem,true,FDv,isHead);return out.rem.length/7;
}
function outN(X,Y,Z,cx,cy,cz){_gn.set(X-cx,Y-cy,Z-cz).normalize();return _gn}

/* skull caved in: a hole through scalp and bone, the brain shows, bone edges stick out */
function openSkull(C){
  if(F.skull)return;var h=HEADV,n=outN(C.x,C.y,C.z,0,1.5,-1),k,a,tx,ty,tz;
  var X=C.x+n.x*0.2,Y=C.y+n.y*0.2,Z=C.z+n.z*0.2;
  holeAt(h,X,Y,Z,2.5,FD,true);F.skull={x:X-n.x*1.6,y:Y-n.y*1.6,z:Z-n.z*1.6};
  _gt.set(0,1,0);if(Math.abs(n.y)>.9)_gt.set(1,0,0);_gw.crossVectors(n,_gt).normalize();_gt.crossVectors(n,_gw);
  for(k=0;k<4;k++){a=k*1.57+rnd(-.4,.4);tx=_gw.x*Math.cos(a)+_gt.x*Math.sin(a);ty=_gw.y*Math.cos(a)+_gt.y*Math.sin(a);tz=_gw.z*Math.cos(a)+_gt.z*Math.sin(a);
    boneShard(h,X+tx*2.1-n.x*.6,Y+ty*2.1-n.y*.6,Z+tz*2.1-n.z*.6,n.x+tx*.5,n.y+ty*.5,n.z+tz*.5,rnd(.7,1.3),.005)}
  spillCore(h,F.skull.x,F.skull.y,F.skull.z,2.2,24,FD,true);brainOut(h,X,Y,Z,6);brainOut(h,X,Y,Z,6);brainStrings(X,Y,Z,2);
  RIG.headG.getWorldPosition(_gv);bloodBurst(_gv.set(_gv.x,_gv.y+.08,_gv.z),90,FD.x,1.4,.6,1.2);
  chip('brain','мозг',true);
}
function brainHit(E){if(!F.skull)return;brainOut(HEADV,F.skull.x,F.skull.y,F.skull.z,E);var n=spillCore(HEADV,F.skull.x,F.skull.y,F.skull.z,2.6,Math.round(8+E*6),FD,true);if(n>3){sfx.squelch();if(n>12)popWorld('Мозги',_gv.set(0,0,0).add(RIG.headG.getWorldPosition(_gw)),'gold')}}

/* the eyeball comes out of a shattered orbit and hangs on its nerve */
function popEye(k){
  var E=EYES[k];if(!E||E.popped)return;var v=HEADV,sx=k?1:-1,W=v.W,H=v.H,cells=[],x,y,z,i,X,Y,Z,ex,ey,ez,q,sk;
  for(q=0;q<E.e.length;q++){i=E.e[q];if(v.type[i]===SKIN){v.type[i]=EYE;v.rgb[i*3]=228;v.rgb[i*3+1]=200;v.rgb[i*3+2]=196;v.bl[i]=120;v.br[i]=0;v.sw[i]=0}}
  var c=v.cell(sx*EX,EY,EZ),cx=c%W,cy=((c/W)|0)%H,cz=(c/(W*H))|0,R=Math.ceil((ER+0.4)/v.s);
  for(z=cz-R;z<=cz+R;z++)for(y=cy-R;y<=cy+R;y++)for(x=cx-R;x<=cx+R;x++){if(x<0||y<0||z<0||x>=W||y>=H||z>=v.D)continue;i=x+W*(y+H*z);var t=v.type[i];
    if(t!==EYE&&t!==IRIS&&t!==PUPIL&&t!==EYEIN)continue;X=v.X(x);Y=v.Y(y);Z=v.Z(z);ex=X-sx*EX;ey=Y-EY;ez=Z-EZ;if(ex*ex+ey*ey+ez*ez<(ER+0.15)*(ER+0.15))cells.push(i)}
  if(cells.length<10)return;
  E.popped=true;E.closed=true;
  var p=pieceFrom(v,cells,null,new T.Vector3(FD.x*1.2+sx*.6,.6,1.6),new T.Vector3(rnd(-2,2),rnd(-2,2),rnd(-2,2))),pv=p.v,j,Zl;
  for(j=0;j<pv.n;j++){if(!pv.type[j])continue;Zl=pv.Z((j/(pv.W*pv.H))|0);if(Zl>EZ-0.3){pv.bl[j]=pv.type[j]===EYEIN?200:30;if(pv.type[j]===EYE&&hash1(j)<0.25)pv.bl[j]=150}}pv.allCol();
  var anc=bodyPt(v,sx*EX,EY-0.3,EZ+0.4);p.eyeK=k;p.teth={a:anc,len:.06,plane:bodyPlane(v,sx*EX,EY-1.5,EZ+1.3,0,0,1)};p.bleed=4;
  addRope({n:9,seg:.0068,size:.005,col:[206,78,90,.8],base:[150,20,26],a:anc,b:function(o){return o.copy(p.g.position)},plane:bodyPlane(v,sx*EX,EY-1.5,EZ+0.9,0,0,1)}).eyeK=k;
  woundNear(v,sx*EX,EY-0.4,EZ-0.6,5);
  chip('eye'+k,k?'глаз ▶ выбит':'◀ глаз выбит',true);G.stats.eyes=(G.stats.eyes||0)+1;
}

function guardBody(L){
  var c=L.trunks,ac=L.accent,g=buildStatic([-3.4,-5.7,3.6,3.4,-3.7,8.9],0.22,guardSDF,function(x,y){return y>-4.3?1:0});
  paintStatic(g,[[c[0],c[1],c[2],.72,.9],[c[0]*.6+ac[0]*.4,c[1]*.6+ac[1]*.4,c[2]*.6+ac[2]*.4,.72,.9]]);g.process(0,1e9,0);return g;
}
/* mouthguard flies out with spit and blood */
function ejectGuard(FDv){
  if(F.guardOut)return;F.guardOut=true;var g=F.guardV||guardBody(F.look);F.guardV=null;
  addPiece(g,HEADV.group.matrixWorld,new T.Vector3(0,-.046,.068),new T.Vector3(FDv.x*2.2+rnd(-.3,.3),1.8+rnd(.6),1.3+FDv.z*.6),new T.Vector3(rnd(-14,14),rnd(-14,14),rnd(-14,14)));
  RIG.headG.getWorldPosition(_gv);_gv.y-=.05;_gv.z+=.08;
  for(var k=0;k<26;k++)spawn(_gv.x,_gv.y,_gv.z,FDv.x*1.5+rnd(-.8,.8),rnd(.3,1.8),rnd(.6,2),k%3?232:170,k%3?232:20,k%3?236:20,.9,rnd(.0015,.003),k%3?2:0,1.2);
  sfx.clack();popWorld('Капа',_gv,'gold');chip('guard','капа',true);
}

/* belly torn open: the wall goes, loops of gut slide out and hang */
function openGut(C){
  if(F.gut)return;var t=TORSOV,X=clamp(C.x,-8,8),Y=clamp(C.y,6,19),k;
  var i=t.cell(X,Y,0),Z=10;if(i>=0){var x=i%t.W,y=((i/t.W)|0)%t.H;for(var z=t.D-1;z>=0;z--)if(t.type[x+t.W*(y+t.H*z)]){Z=t.Z(z);break}}
  holeAt(t,X,Y,Z,4.6,FD,false);F.gut={x:X,y:Y,z:Z-3.6};
  spillCore(t,X,Y,Z-3.4,3.2,14,FD,false);
  for(k=0;k<3;k++){var ax=X+rnd(-1.6,1.6),ay=Y+rnd(-1.4,1.4);
    addRope({n:20+k*5,seg:.02,size:.022,col:[214,128,122,.82],base:[150,30,36],a:bodyPt(t,ax,ay,Z-0.6),dir:_gv.set(rnd(-.3,.3),-.5,1).normalize(),kick:_gw.set(rnd(-.02,.02),.01,.03),damp:.975,plane:bodyPlane(t,X,Y,Z+0.2,0,0,1)})}
  t.group.updateMatrixWorld();_gv.set(X*.01,Y*.01,Z*.01).applyMatrix4(t.group.matrixWorld);bloodBurst(_gv,120,FD.x,.2,1.2,1.1);
  chip('gut','живот вспорот',true);
}

/* forearm snapped: the arm hangs, the glove is useless, bone shows */
function breakArm(k){
  if(F.armBroken[k])return;F.armBroken[k]=true;var a=FARM[k],sx=k?1:-1;
  boneShard(a.fo,sx*2.4,13.5,1.4,sx*1,0.5,0.7,4.2,.01);boneShard(a.fo,sx*2,12.4,-.8,sx*.8,-.4,-.4,2.6,.008);
  a.fo.group.updateMatrixWorld();_gv.set(sx*.016,.13,.01).applyMatrix4(a.fo.group.matrixWorld);bloodBurst(_gv,60,0,.6,.6,1);
  paintArmWound(a.fo,sx);
  sfx.crack('arm');vib([40,30,110]);banner('Перелом руки');chip('arm'+k,k?'рука ▶ сломана':'◀ рука сломана',true);G.stats.fract.push('рука');
  if(F.st==='guard'){setF('idle');F.next=.6}
}
function paintArmWound(v,sx){for(var i=0;i<v.n;i++){if(!v.type[i])continue;var W=v.W,H=v.H,X=v.X(i%W),Y=v.Y(((i/W)|0)%H),Z=v.Z((i/(W*H))|0);
  var d=(X-sx*1.8)*(X-sx*1.8)+(Y-13.2)*(Y-13.2)*.5+(Z-.6)*(Z-.6);if(d<9){var w=1-d/9;v.rgb[i*3]=v.rgb[i*3]*(1-w)+120*w;v.rgb[i*3+1]=v.rgb[i*3+1]*(1-w)+14*w;v.rgb[i*3+2]=v.rgb[i*3+2]*(1-w)+16*w;v.wet[i]=200}}
  for(var k=0;k<v.chunks.length;k++){if(v.chunks[k].geo)v.chunks[k].cd=true;else v.chunks[k].gd=true}v.dirty=true}

/* open fractures for the zones that already have closed ones */
function openFracture(zone,C){
  var h=HEADV,s;
  if(zone==='nose'){boneShard(h,.5,-.6,9.8,.6,.3,1,1.2,.0055);boneShard(h,-.4,.1,9.6,-.7,.4,.9,1.0,.005)}
  else if(zone==='cheekL'||zone==='cheekR'){s=zone==='cheekL'?-1:1;boneShard(h,s*4.9,.6,6.4,s*.8,.2,1,1.4,.006)}
  else if(zone==='jaw'){boneShard(h,F.jawShift>0?1.6:-1.6,-8.2,7.6,0,-.6,1,1.3,.006)}
  else if(zone==='ribL'||zone==='ribR'){s=zone==='ribL'?-1:1;var cy=clamp(C.y,16,32);
    holeAt(TORSOV,s*11.5,cy,6.5,1.8,FD,false);boneShard(TORSOV,s*11.5,cy,5.2,s*.9,.3,1,3.0,.008);boneShard(TORSOV,s*11,cy-2.6,5.6,s*.7,-.4,1,2.2,.007);
    chip('ribopen','открытый перелом',true)}
}
/* first solid cell met coming in from outside along (dx,dy,dz) toward (cx,cy,cz), in body centimetres */
function surfAlong(v,cx,cy,cz,dx,dy,dz){var l=Math.sqrt(dx*dx+dy*dy+dz*dz)||1,k,i;dx/=l;dy/=l;dz/=l;
  for(k=30;k>0;k-=v.s*0.5){i=v.cell(cx+dx*k,cy+dy*k,cz+dz*k);if(i>=0&&v.type[i])return{x:cx+dx*k,y:cy+dy*k,z:cz+dz*k}}return{x:cx,y:cy,z:cz}}
function breakTo(zone,dir,C){var n=0;while((F.broken[zone]||0)<2&&n++<2)fracture(zone,dir,C)}

/* a hanging eye torn off its nerve */
function tearEye(k){for(var q=0;q<pieces.length;q++){var p=pieces[q];if(p.teth&&p.eyeK===k){p.teth=null;p.vel.set(FD.x*2,1.5,1.8);
  for(var r=ROPES.length-1;r>=0;r--)if(ROPES[r].eyeK===k)ROPES.splice(r,1);chip('eye'+k,k?'глаз ▶ оторван':'◀ глаз оторван',true);banner('Глаз оторван');return}}}

/* ---------- jaw torn loose ---------- */
var _jw=new T.Vector3();
function pieceLocal(p,X,Y,Z){var l=new T.Vector3(X*.01,Y*.01,Z*.01);return function(o){p.g.updateMatrixWorld(true);return o.copy(l).applyMatrix4(p.v.group.matrixWorld)}}
var RAWC=[172,40,44,.86],RAWB=[110,10,12];
/* the jaw stays on its hinge(s): one pin per side, plus strings of torn muscle and skin between jaw and face */
function jawPins(p,sides){
  p.pins=[];p.bleed=0;p.onTear=function(pp,q){tearJawPin(pp.pins[q].side)};
  for(var k=0;k<sides.length;k++){var sx=sides[k],pn={side:sx,l:new T.Vector3(sx*.047,-.034,.004),a:bodyPt(HEADV,sx*4.7,-3.4,.4),slack:.01,strain:0,d:0,ropes:[]};
    for(var q=0;q<3;q++){var hx=sx*rnd(3.6,5.4),hy=rnd(-4.6,-3.4),hz=rnd(-.6,3.2);
      pn.ropes.push(addRope({n:6,seg:.007,size:rnd(.005,.008),col:q?RAWC:[200,150,120,.6],base:RAWB,a:bodyPt(HEADV,hx,hy,hz),b:pieceLocal(p,hx+rnd(-.4,.4),hy-1.2,hz+rnd(-.5,.5)),damp:.96}))}
    p.pins.push(pn)}
  /* lip and cheek strings at the front, the first to snap */
  if(sides.length===2)for(var f=-1;f<=1;f+=2)p.pins[f<0?0:1].ropes.push(addRope({n:7,seg:.007,size:.005,col:[188,90,84,.8],base:RAWB,a:bodyPt(HEADV,f*2.4,-5.2,7.3),b:pieceLocal(p,f*2.4,-5.6,7.6),damp:.96}));
}
function tearJawPin(side){
  var p=F.jawP;if(!p||!p.pins||!p.pins.length)return;var q=0,k;
  if(side)for(k=0;k<p.pins.length;k++)if(p.pins[k].side===side){q=k;break}
  var pn=p.pins.splice(q,1)[0];
  for(k=0;k<pn.ropes.length;k++){var i=ROPES.indexOf(pn.ropes[k]);if(i>=0)ROPES.splice(i,1)}
  pn.a(_jw);bloodBurst(_jw,60,pn.side*.6,.4,.6,1);sfx.tear();vib([30,20,60]);
  p.vel.y-=.4;p.vel.x+=pn.side*-.4;p.w.z+=pn.side*4;
  if(!p.pins.length){p.pins=null;p.bleed=2.5;banner('Челюсть оторвалась');popWorld('Оторвалась',_jw,'gold')}
  else banner('Висит на лоскуте');
}
/* the face above the missing jaw: ragged, raw muscle, fat and bone, all of it bleeding */
function rawStump(v){
  var W=v.W,H=v.H,x,y,z,i,X,Y,Z,t,ax,h,rem=[];
  function inside(X,Y,Z){return Z>-3&&Y<-2.4&&Y>-10.5&&Math.abs(X)<6.6}
  for(z=0;z<v.D;z++){Z=v.Z(z);if(Z<-3)continue;for(y=0;y<H;y++){Y=v.Y(y);if(Y>-2.4||Y<-10.5)continue;i=W*(y+H*z);for(x=0;x<W;x++,i++){t=v.type[i];if(!t)continue;X=v.X(x);if(Math.abs(X)>6.6)continue;
    if(!v.exp(x,y,z,i))continue;if(!((v.fl[i]&F_JAWH)||Y<-5.2||jawW(X,Y,Z)>0.05))continue;
    if(t!==BONE&&t!==GUM&&t!==TOOTH&&hash1(i*13+7)<0.3)rem.push(i,x,y,z)}}}
  for(var k=0;k<rem.length;k+=4){v.type[rem[k]]=0;v.mg(rem[k+1],rem[k+2],rem[k+3])}v.islFull=true;
  for(z=0;z<v.D;z++){Z=v.Z(z);if(Z<-3)continue;for(y=0;y<H;y++){Y=v.Y(y);if(Y>-2.4||Y<-10.5)continue;i=W*(y+H*z);for(x=0;x<W;x++,i++){t=v.type[i];if(!t)continue;X=v.X(x);if(Math.abs(X)>6.6)continue;
    if(!v.exp(x,y,z,i))continue;if(!((v.fl[i]&F_JAWH)||Y<-5.2||jawW(X,Y,Z)>0.05))continue;
    h=hash1(i*7+3);
    if(t===BONE){v.rgb[i*3]=228;v.rgb[i*3+1]=212;v.rgb[i*3+2]=186;v.bl[i]=Math.max(v.bl[i],70+90*h)}
    else if(t===FAT){v.rgb[i*3]=214;v.rgb[i*3+1]=176;v.rgb[i*3+2]=104;v.bl[i]=Math.max(v.bl[i],110+100*h)}
    else if(t===GUM||t===TOOTH){v.bl[i]=Math.max(v.bl[i],150)}
    else{var st=Math.sin(X*3.1+Z*1.3)*0.5+0.5;v.rgb[i*3]=120+50*st+20*h;v.rgb[i*3+1]=22+12*st;v.rgb[i*3+2]=26+10*st;v.bl[i]=Math.max(v.bl[i],h<.7?255:170);if(SKL[t])v.ab[i]=255}
    v.fl[i]|=F_LOOSE;v.sw[i]=0;v.mc(x,y,z)}}}
  for(k=0;k<6;k++)woundNear(v,rnd(-4.5,4.5),-5+rnd(-1.4,0.6),rnd(-1,7),9);
}
/* scraps of muscle and skin hanging from the edge of the stump */
function stumpTatters(v){
  for(var k=0;k<7;k++){var sx=k%2?1:-1,ax=rnd(.6,4.8),Y=rnd(-5.6,-4.4),Z=ax>3.5?rnd(1.5,4.5):rnd(4.5,7.4),sp=surfAlong(v,sx*ax,Y+3,Z-2,0,-1,0.1);
    addRope({tmp:true,n:3+((Math.random()*4)|0),seg:.0075,size:rnd(.005,.009),col:k%3?RAWC:[196,140,110,.6],base:RAWB,a:bodyPt(v,sp.x,sp.y,sp.z),dir:_gv.set(0,-1,.2),damp:.95})}
}
/* the neck under the missing jaw: raw flesh and the windpipe at the top, blood coating the front and running down in streaks */
function neckGore(){
  var v=NECK,i,X,Y,Z,h,L,b,W=v.W,H=v.H,ax,r,g,bb,k;
  for(i=0;i<v.n;i++){if(!v.type[i])continue;X=v.X(i%W);Y=v.Y(((i/W)|0)%H);Z=v.Z((i/(W*H))|0);ax=Math.abs(X);
    var fr=Z>-1.5?Math.min(1,(Z+1.5)/4):0;h=hash1(Math.floor(X/.5)*31+Math.floor(Z/.5)*7);
    var rawTop=14-1.2-4.2*fr*(1-Math.min(1,ax/5));
    if(Y>rawTop&&Z>-3.5){
      if(ax<1.5&&Z>3&&Y>8.5){var ring=Math.sin(Y*7.5)>0.25;r=ring?212:156;g=ring?162:44;bb=ring?150:48}   // windpipe rings
      else{var st=Math.sin(X*2.6+Y*.8)*.5+.5;r=116+46*st+24*hash1(i);g=18+14*st;bb=22+10*st}
      v.rgb[i*3]=r;v.rgb[i*3+1]=g;v.rgb[i*3+2]=bb;v.wet[i]=240;continue}
    if(fr>0){L=5+9*h*h;b=Y>9?0.85+0.15*h:(Y>14-L-2?0.7+0.3*h:0);if(Y>3&&hash1(i*5)<0.05)b=Math.max(b,.8);
      if(b>0){v.rgb[i*3]=v.rgb[i*3]*(1-b)+122*b;v.rgb[i*3+1]=v.rgb[i*3+1]*(1-b)+12*b;v.rgb[i*3+2]=v.rgb[i*3+2]*(1-b)+14*b;v.wet[i]=Math.max(v.wet[i],215*b)}}}
  for(k=0;k<v.chunks.length;k++){if(v.chunks[k].geo)v.chunks[k].cd=true;else v.chunks[k].gd=true}v.dirty=true;v.process(0,1e9,1e9);
}

/* ---------- trauma: the more a spot takes, the further it goes ----------
   pulp (ragged, pitted meat) -> flesh stripped to the bone -> bone shattered into flying plates -> brain coming out in lumps -> the skull bursts */
function isSoft(t){return t===SKIN||t===FAT||t===MUSC||t===LIP||t===BROW||t===HAIR||t===CART||t===LASH||t===MOUTH}
function eachNear(v,X,Y,Z,R,fn){
  var s=v.s,W=v.W,H=v.H,x0=Math.max(0,Math.floor((X-R-v.ox)/s)),x1=Math.min(W-1,Math.ceil((X+R-v.ox)/s)),y0=Math.max(0,Math.floor((Y-R-v.oy)/s)),y1=Math.min(H-1,Math.ceil((Y+R-v.oy)/s)),z0=Math.max(0,Math.floor((Z-R-v.oz)/s)),z1=Math.min(v.D-1,Math.ceil((Z+R-v.oz)/s));
  var x,y,z,i,dx,dy,dz,d2,R2=R*R;
  for(z=z0;z<=z1;z++){dz=v.Z(z)-Z;for(y=y0;y<=y1;y++){dy=v.Y(y)-Y;i=x0+W*(y+H*z);for(x=x0;x<=x1;x++,i++){if(!v.type[i])continue;dx=v.X(x)-X;d2=dx*dx+dy*dy+dz*dz;if(d2<=R2)fn(i,x,y,z,d2/R2)}}}
}
var PULPC=[[122,18,22],[150,30,34],[190,80,84],[214,176,104],[62,8,10]];
function mince(v,i){var t=v.type[i];if(SKL[t]||t===BONE||t===CORE)return;var h=Math.random(),c=PULPC[h<.4?0:h<.68?1:h<.86?2:h<.94?3:4];v.rgb[i*3]=c[0];v.rgb[i*3+1]=c[1];v.rgb[i*3+2]=c[2]}
function trimPieces(max){for(var k=0;k<pieces.length&&pieces.length>max;k++){var p=pieces[k];if(p.rest&&!p.teth&&!p.pins&&p!==F.jawP){scene.remove(p.g);p.v.dispose();pieces.splice(k,1);k--}}}
/* mashed surface: some of it torn away, the rest ragged, raw and soaked */
function pulpAt(v,X,Y,Z,R,amt,isHead){
  var L=[],out={rem:[]},k,i;
  eachNear(v,X,Y,Z,R,function(i,x,y,z,f){var t=v.type[i];if(!isSoft(t)&&t!==GUM)return;if(!v.exp(x,y,z,i))return;L.push(i,x,y,z,f)});
  for(k=0;k<L.length;k+=5){i=L[k];
    if(v.type[i]!==GUM&&Math.random()<amt*(1-L[k+4]*.6)){removeCell(v,i,L[k+1],L[k+2],L[k+3],out);continue}
    v.bl[i]=255;if(SKL[v.type[i]]){v.ab[i]=255;v.br[i]=255}v.sw[i]=0;v.fl[i]|=F_LOOSE;v.dx[i]=rnd(-6,6);v.dy[i]=rnd(-6,6);v.dz[i]=rnd(-6,6);mince(v,i);v.mg(L[k+1],L[k+2],L[k+3])}
  if(L.length)v.anyDef=true;
  spill(v,out.rem,true,FD,isHead);return out.rem.length;
}
/* everything soft comes off inside R: bare bone, with a ragged bleeding rim */
function stripAt(v,X,Y,Z,R,isHead){
  var L=[],out={rem:[]},k,n=0;
  eachNear(v,X,Y,Z,R,function(i,x,y,z,f){if(!isSoft(v.type[i]))return;if(f>.6&&hash1(i*5+1)<(f-.6)*2.2)return;L.push(i,x,y,z)});
  for(k=0;k<L.length;k+=4)removeCell(v,L[k],L[k+1],L[k+2],L[k+3],out);
  eachNear(v,X,Y,Z,R+.7,function(i,x,y,z){var t=v.type[i],h;if(!v.exp(x,y,z,i))return;h=hash1(i*9+4);
    if(t===BONE){n++;v.bl[i]=h<.3?235:60+h*70;if(h>.82)v.fl[i]|=F_CRACK;v.mc(x,y,z)}
    else if(isSoft(t)){v.bl[i]=255;if(SKL[t])v.ab[i]=255;v.sw[i]=0;v.fl[i]|=F_LOOSE;mince(v,i);v.mc(x,y,z)}});
  spill(v,out.rem,true,FD,isHead);
  for(k=0;k<3;k++)woundNear(v,X+rnd(-R,R)*.8,Y+rnd(-R,R)*.8,Z+rnd(-.5,.5),isHead?4:3);
  v.group.updateMatrixWorld();_gv.set(X*.01,Y*.01,Z*.01).applyMatrix4(v.group.matrixWorld);bloodBurst(_gv,Math.min(70,10+out.rem.length/8),FD.x,.6,.8,1);
  return out.rem.length+(n>20?1:0);
}
/* the bone under the blow breaks into plates that fly off; what was behind it shows */
function shatterAt(v,X,Y,Z,R,isHead){
  var L=[],G3=[[],[],[]],out={rem:[]},k,q,n=outN(X,Y,Z,0,isHead?1.5:26,isHead?-1:-2),nx=n.x,ny=n.y,nz=n.z,made=0;
  eachNear(v,X,Y,Z,R,function(i,x,y,z,f){if(v.type[i]!==BONE)return;if(f>.65&&hash1(i*3+2)<(f-.65)*2.6)return;L.push(i,x,y,z)});
  if(L.length<14)return 0;
  _gt.set(0,1,0);if(Math.abs(ny)>.9)_gt.set(1,0,0);_gw.crossVectors(n,_gt).normalize();_gt.set(ny*_gw.z-nz*_gw.y,nz*_gw.x-nx*_gw.z,nx*_gw.y-ny*_gw.x);
  var ax=_gw.x,ay=_gw.y,az=_gw.z,bx=_gt.x,by=_gt.y,bz=_gt.z,ph=rnd(6.28);
  for(k=0;k<L.length;k+=4){var dx=v.X(L[k+1])-X,dy=v.Y(L[k+2])-Y,dz=v.Z(L[k+3])-Z,a=Math.atan2(dx*bx+dy*by+dz*bz,dx*ax+dy*ay+dz*az)+ph;G3[((a/6.2832*3)%3+3|0)%3].push(L[k])}
  trimPieces(30);
  for(q=0;q<3;q++){var g=G3[q];if(!g.length)continue;
    if(g.length>=26&&pieces.length<36){var a2=q*2.09-ph,p=pieceFrom(v,g,null,new T.Vector3(nx*rnd(1,2.2)+FD.x*.9+(ax*Math.cos(a2)+bx*Math.sin(a2))*.9,ny*rnd(.6,1.6)+rnd(.6,1.6),nz*rnd(1,2.2)+FD.z*.5+(az*Math.cos(a2)+bz*Math.sin(a2))*.9),new T.Vector3(rnd(-14,14),rnd(-14,14),rnd(-14,14)));p.bleed=.8;made++}
    else for(k=0;k<g.length;k++){var i=g[k];if(v.type[i])removeCell(v,i,i%v.W,((i/v.W)|0)%v.H,(i/(v.W*v.H))|0,out)}}
  spill(v,out.rem,true,FD,isHead);
  /* what is behind stays readable: wet, not drowned in red */
  eachNear(v,X,Y,Z,R+.5,function(i,x,y,z){if(v.type[i]===CORE&&v.exp(x,y,z,i)){v.bl[i]=40+hash1(i*7)*90;v.mc(x,y,z)}});
  v.group.updateMatrixWorld();_gv.set(X*.01,Y*.01,Z*.01).applyMatrix4(v.group.matrixWorld);bloodBurst(_gv,70,FD.x,.8,.8,1.2);
  for(k=0;k<14;k++)spawn(_gv.x,_gv.y,_gv.z,rnd(-2,2)+nx*1.5,rnd(.2,2.4),rnd(-1,2)+nz*1.5,236,226,200,.4,rnd(.002,.0045),1,rnd(2,4));
  sfx.flay();return L.length/4;
}
function brainStrings(X,Y,Z,n){for(var k=0;k<n;k++)addRope({tmp:true,n:4+((Math.random()*3)|0),seg:.011,size:rnd(.011,.015),col:[224,160,164,.8],base:[160,60,70],a:bodyPt(HEADV,X+rnd(-1.2,1.2),Y+rnd(-1.2,1.2),Z+rnd(-1,1)),dir:_gv.set(rnd(-.3,.3),-1,.3).normalize(),damp:.95})}
/* a lump of brain comes away where it is exposed near the blow */
function brainOut(v,X,Y,Z,E){
  var L=[],C=[],k;
  eachNear(v,X,Y,Z,3,function(i,x,y,z){if(v.type[i]===CORE&&v.exp(x,y,z,i)&&brainZone(v.X(x),v.Y(y),v.Z(z)))L.push(x,y,z)});
  if(L.length<12)return false;
  var q=((Math.random()*L.length/3)|0)*3,cx=v.X(L[q]),cy=v.Y(L[q+1]),cz=v.Z(L[q+2]),n=outN(cx,cy,cz,0,1.5,-1),nx=n.x,ny=n.y,nz=n.z,r=rnd(1.1,1.5)+Math.min(.9,E*.07);
  eachNear(v,cx-nx*.5,cy-ny*.5,cz-nz*.5,r,function(i){if(v.type[i]===CORE)C.push(i)});
  v.group.updateMatrixWorld();_gw.set(cx*.01,cy*.01,cz*.01).applyMatrix4(v.group.matrixWorld);
  trimPieces(30);
  if(C.length>=18&&pieces.length<38){var p=pieceFrom(v,C,null,new T.Vector3(nx*rnd(.6,1.5)+FD.x*.8,rnd(.5,1.7)+ny*.6,nz*rnd(.6,1.5)+.5),new T.Vector3(rnd(-9,9),rnd(-9,9),rnd(-9,9))),pv=p.v;
    for(k=0;k<pv.n;k++)if(pv.type[k])pv.bl[k]=Math.min(pv.bl[k],50+hash1(k)*70);pv.allCol();p.bleed=1}
  else{var out={rem:[]};for(k=0;k<C.length;k++){var i=C[k];removeCell(v,i,i%v.W,((i/v.W)|0)%v.H,(i/(v.W*v.H))|0,out)}spill(v,out.rem,true,FD,true)}
  eachNear(v,cx,cy,cz,r+.6,function(i,x,y,z){if(v.type[i]===CORE&&v.exp(x,y,z,i)){v.bl[i]=40+hash1(i*7)*90;v.mc(x,y,z)}});
  for(k=0;k<18;k++)spawn(_gw.x,_gw.y,_gw.z,rnd(-1.6,1.6)+nx*1.2+FD.x,rnd(.2,2.2),rnd(-.6,1.8)+nz*1.2,214+rnd(20),140+rnd(40),150+rnd(30),.85,rnd(.003,.0065),1,rnd(2.5,5));
  if(Math.random()<.6)brainStrings(cx,cy,cz,1);
  sfx.squelch();G.stats.brain=(G.stats.brain||0)+1;if(!F.chips.brain)chip('brain','мозг',true);
  if(!F.skull)F.skull={x:cx-nx*1.6,y:cy-ny*1.6,z:cz-nz*1.6};
  return true;
}
/* the end of it: that side of the skull goes, brain and bone everywhere */
function headBurst(v,X,Y,Z){
  if(F.burst)return;F.burst=true;var n=outN(X,Y,Z,0,1.5,-1),nx=n.x,ny=n.y,nz=n.z,cx=X-nx*1.2,cy=Y-ny*1.2,cz=Z-nz*1.2,k;
  stripAt(v,cx,cy,cz,4.4,true);shatterAt(v,cx,cy,cz,3.9,true);
  for(k=0;k<5;k++)brainOut(v,cx+rnd(-2,2),cy+rnd(-2,2),cz+rnd(-2,2),10);
  spillCore(v,cx-nx*1.5,cy-ny*1.5,cz-nz*1.5,3.4,40,FD,true);
  for(k=0;k<5;k++){var a=k*1.26;boneShard(v,cx+Math.cos(a)*3.4*(Math.abs(nx)>.7?0:1),cy+Math.sin(a)*3.4,cz+(Math.abs(nx)>.7?Math.cos(a)*3.4:0),nx+rnd(-.5,.5),ny+rnd(-.2,.6),nz+rnd(-.5,.5),rnd(1,1.8),.006)}
  brainStrings(cx,cy,cz,4);
  v.group.updateMatrixWorld();_gv.set(X*.01,Y*.01,Z*.01).applyMatrix4(v.group.matrixWorld);bloodBurst(_gv,220,FD.x,1.2,.8,1.5);
  for(k=0;k<40;k++)spawn(_gv.x,_gv.y,_gv.z,rnd(-2.6,2.6)+nx*2,rnd(.4,3.4),rnd(-1.2,2.6)+nz*2,214+rnd(20),140+rnd(40),150+rnd(30),.85,rnd(.004,.008),1,rnd(3,6));
  screenSplat(5);sfx.burst();vib([80,40,200]);hitStop(.2,1.1,true);if(!REDUCED)G.shake=Math.max(G.shake,.12);
  chip('burst','череп раскрошен',true);G.stats.fract.push('череп вдребезги');
  knockout(true,'Голова разбита');
}
function goreHit(v,head,zone,X,Y,Z,E,power,mg){
  var T_=F.trauma,key=head?zone:'t_'+zone,add=E*(power?1:.5),tr=(T_[key]=(T_[key]||0)+add),did=0,tk=head?'_head':'_body';
  /* damage elsewhere on the head softens the whole of it */
  T_[tk]=(T_[tk]||0)+add;tr+=(T_[tk]-tr)*.3;
  if(!head){if(!power)return 0;
    if(tr>22){did=stripAt(v,X,Y,Z,2.3+Math.min(1.8,E*.22),false);if(!F.chips.meat)chip('meat','мясо до кости',true)}
    else if(tr>10)did=pulpAt(v,X,Y,Z,2.6,.3,false);
    return did}
  if(tr>7&&(power||Math.random()<.4))did+=pulpAt(v,X,Y,Z,power?1.5+(mg?.8:0):.9,power?.32:.18,true);
  if(power&&tr>14){did+=stripAt(v,X,Y,Z,1.4+Math.min(1.4,E*.17),true);if(!F.chips.bone)chip('bone','кость наружу',true)}
  if(power&&tr>24){F.shat++;if(mg||F.shat%2===1)did+=shatterAt(v,X,Y,Z,1.7+Math.min(1.3,E*.14),true)}
  if(power&&brainOut(v,X,Y,Z,E)){did++;if(Math.random()<.5)popWorld('Мозги',_gw,'gold')}
  if(power&&(tr>42||(mg&&tr>24))&&(zone==='skull'||zone==='orbL'||zone==='orbR'||zone==='earL'||zone==='earR')){headBurst(v,X,Y,Z);did++}
  return did;
}
