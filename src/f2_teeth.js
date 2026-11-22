
/* ---------- teeth: 16 separate fine-voxel teeth (0.13 cm) set into the gums ----------
   upper teeth hang from the head, lower teeth ride the jaw hinge; each can bleed, chip, loosen and fly out root and all */
var TEETH=[],TS_=0.13,UPPERG=new T.Group(),JAWPIV=new T.Group(),JAWIN=new T.Group();
JAWPIV.add(JAWIN);JAWIN.position.set(0,0.008,-0.006);
/* shape by slot from the centre: 0 central incisor, 1 lateral incisor, 2 canine, 3 premolar */
function toothKind(slot){var c=slot<4?3-slot:slot-4;return c}
function rbox(x,y,z,bx,by,bz,r){var qx=Math.abs(x)-bx+r,qy=Math.abs(y)-by+r,qz=Math.abs(z)-bz+r;
  return Math.sqrt(Math.max(qx,0)*Math.max(qx,0)+Math.max(qy,0)*Math.max(qy,0)+Math.max(qz,0)*Math.max(qz,0))+Math.min(Math.max(qx,Math.max(qy,qz)),0)-r}
/* local frame: y=0 at the biting edge, +y toward the root, +z out of the mouth */
function toothSDF(kind,low,chip){
  var hw=low?[.23,.26,.29,.3][kind]:[.31,.27,.29,.3][kind],ch=low?[.92,.95,1.02,.86][kind]:[1.06,.96,1.08,.88][kind];
  return function(x,y,z){
    var d,t=Math.max(0,Math.min(1,y/ch)),w,dep;
    if(kind<2){w=hw*(0.9+0.12*t);dep=0.1+0.17*t;d=rbox(x,y-ch*.5,z+.04*(x*x/(w*w))-0.02,w,ch*.5,dep,.07)}
    else if(kind===2){w=hw*Math.min(1,0.32+y/0.42);dep=0.22*Math.min(1,0.45+y/0.5);d=rbox(x,y-ch*.5,z,w,ch*.5,dep,.08)}
    else{d=sdEll(x,y-ch*.52,z,hw,ch*.55,.3)}
    d=smin(d,sdEll(x,y-ch-0.42,z+.02,hw*.52,0.62,.15),0.18);   // root
    if(chip)d=Math.max(d,-(chip[0]*x+chip[1]*(y-chip[2])));      // broken-off corner
    return d;
  };
}
function toothPart(kind,low){var ch=low?[.92,.95,1.02,.86][kind]:[1.06,.96,1.08,.88][kind];return function(x,y,z){return y<0.07&&kind<2?3:y>ch+0.1?2:y>ch-0.08?1:0}}
function toothSlot(id){var low=id>8,slot=(id-1)%8,xc=-2.75+(slot+0.5)*0.6875,u=Math.abs(xc)/2.75,zf=8.25-3.0*u*u-(low?0.22:0);
  return{low:low,slot:slot,kind:toothKind(slot),x:xc,y:low?-5.26:-5.24,z:zf-0.3,yaw:Math.atan(6*xc/7.5625)}}
function toothColors(L,t){
  var e=L.teeth||[250,246,232],gd=L.gold===t.id,n=0.94+0.1*hash1(t.id*7+L.skin[0]);
  var en=gd?[236,188,72]:[Math.min(255,e[0]*n*1.04),Math.min(255,e[1]*n*1.04),Math.min(255,e[2]*n),],wt=gd?.95:.55;
  return[[en[0],en[1],en[2],wt,.9],[en[0]*.9,en[1]*.84,en[2]*.68,.5,.9],[222,204,168,.4,.9],[en[0]*.97,en[1]*.985,Math.min(255,en[2]*1.03+4),.7,.94]];
}
function paintTooth(t){
  var v=t.v,pal=toothColors(TLOOK,t),i,p,n,b,ro=t.root;paintStatic(v,pal);
  if(t.bl>0.02||ro)for(i=0;i<v.n;i++){if(!v.type[i])continue;var Y=Math.abs(v.Y(((i/v.W)|0)%v.H)),h=hash1(i*3+t.id);
    b=ro&&v.fl[i]===2?Math.min(1,0.4+(Y-1.1)*0.9):(h<t.bl*0.8?0.55+0.4*h:t.bl*0.25);
    if(b>0){v.rgb[i*3]=v.rgb[i*3]*(1-b)+130*b;v.rgb[i*3+1]=v.rgb[i*3+1]*(1-b)+12*b;v.rgb[i*3+2]=v.rgb[i*3+2]*(1-b)+14*b;v.wet[i]=Math.max(v.wet[i],200*b)}}
  v.process(0,1e9,1e9);
}
function makeToothBody(t,chip){var sl=t.sl,sdf=toothSDF(sl.kind,sl.low,chip),pt=toothPart(sl.kind,sl.low);
  var f=sl.low?function(x,y,z){return sdf(x,-y,z)}:sdf,pf=sl.low?function(x,y,z){return pt(x,-y,z)}:pt;
  var v=buildStatic(sl.low?[-.42,-2.1,-.42,.42,.08,.42]:[-.42,-.08,-.42,.42,2.1,.42],TS_,f,pf);
  return v;
}
var TLOOK=null;
function teethBuild(L){TLOOK=L;
  var k,t;for(k=1;k<TEETH.length;k++){t=TEETH[k];if(t){if(t.v){t.g.remove(t.v.group);t.v.dispose()}if(t.g.parent)t.g.parent.remove(t.g)}}
  if(UPPERG.parent!==HEADV.group)HEADV.group.add(UPPERG);if(JAWPIV.parent!==HEADV.group)HEADV.group.add(JAWPIV);
  if(JAWIN.parent!==JAWPIV){if(JAWIN.parent)JAWIN.parent.remove(JAWIN);JAWPIV.add(JAWIN)}JAWIN.position.set(0,0.008,-0.006);JAWIN.rotation.set(0,0,0);
  TEETH=[null];
  for(k=1;k<=16;k++){var sl=toothSlot(k);
    t={id:k,sl:sl,hp:40,out:false,loose:false,chip:0,bl:0,root:false,cx:sl.x,cy:sl.low?-5.75:-4.7,cz:sl.z,g:new T.Group()};
    t.g.position.set(sl.x*.01,sl.y*.01,sl.z*.01);t.g.rotation.set(0,sl.yaw,0);
    (sl.low?JAWIN:UPPERG).add(t.g);
    t.v=makeToothBody(t,null);t.g.add(t.v.group);TEETH.push(t);paintTooth(t);
  }
}
var _tw=new T.Vector3();
/* how much of the tooth the lips hide: cells right in front of it */
function toothCover(v,t){var n=0,k;for(k=1;k<=4;k++){var i=v.cell(t.cx,t.cy,t.cz+0.3+k*0.35);if(i>=0&&v.type[i])n++}return n/4}
function teethHit(v,hx,hy,hz,E,kind,FD,opened){return teethHitCm(v.X(hx),v.Y(hy),v.Z(hz),E,kind,FD,opened)}
function teethHitCm(X,Y,Z,E,kind,FD,opened){
  var out=0,k,t,dx,dy,dz,prox,f,v=HEADV,bled=false;
  for(k=1;k<=16;k++){t=TEETH[k];if(!t||t.out)continue;if(t.sl.low&&F.jawOff)continue;
    dx=t.cx-X;dy=t.cy-Y;dz=t.cz-Z;prox=Math.exp(-(dx*dx+dy*dy+dz*dz)/4.8);
    if(kind==='upper'&&t.sl.low)prox=Math.max(prox,0.24);if(prox<0.05)continue;
    f=(1-0.7*toothCover(v,t)*(1-F.sm*0.8))*(opened?1.35:1);t.hp-=E*prox*f*2.4;
    if(E>0.5&&prox>0.25){t.bl=Math.min(1,t.bl+0.1*prox*E);bled=true}
    if(t.hp<=0){knockTooth(v,k,FD);out++;continue}
    if(t.hp<27&&!t.chip){chipTooth(t)}
    if(!t.loose&&t.hp<16){t.loose=true;t.g.rotation.x+=rnd(-.35,.35)*(t.sl.low?-1:1);t.g.rotation.z+=rnd(-.25,.25);t.g.position.z-=0.0012;t.g.position.y+=t.sl.low?0.0012:-0.0012}
    else if(bled)paintTooth(t);
  }
  return out;
}
/* a corner of the crown snaps off */
function chipTooth(t){
  t.chip=1;var a=rnd(-1,1),sx=a<0?-1:1,c=[sx*0.75,0.65,rnd(.28,.48)];
  t.g.remove(t.v.group);t.v.dispose();t.v=makeToothBody(t,c);t.g.add(t.v.group);paintTooth(t);
  t.g.updateMatrixWorld(true);_tw.set(0,t.sl.low?.002:-.002,.002).applyMatrix4(t.g.matrixWorld);
  for(var k=0;k<5;k++)spawn(_tw.x,_tw.y,_tw.z,rnd(-.8,.8)+FD.x,rnd(.2,1.4),rnd(.4,1.6),236,230,212,.6,rnd(.0012,.0022),1,2);
  sfx.tooth(true);
}
/* the whole tooth comes out with its root and flies, the socket bleeds */
function knockTooth(v,k,FDv){
  var t=TEETH[k];if(!t||t.out)return;t.out=true;t.g.visible=false;
  t.g.updateMatrixWorld(true);
  var fly={id:t.id,sl:t.sl,bl:Math.max(.25,t.bl),root:true,v:null};fly.v=makeToothBody(fly,t.chip?[.6,.7,.4]:null);paintTooth(fly);
  var ch=t.sl.low?-.005:.005;
  addPiece(fly.v,t.g.matrixWorld,new T.Vector3(0,ch,0),new T.Vector3(FDv.x*rnd(.8,2.2)+rnd(-.8,.8),rnd(1.2,3),rnd(1,2.8)+FDv.z*.4),new T.Vector3(rnd(-30,30),rnd(-30,30),rnd(-30,30))).snd='tooth';
  /* socket: gum torn and bleeding */
  var gy=t.sl.low?-6.4:-4.0,i,gx,gz,gw=0;
  for(gx=-0.5;gx<=0.5;gx+=0.25)for(gz=-0.6;gz<=0.6;gz+=0.3){i=v.cell(t.cx+gx,gy,t.cz+gz);if(i>=0&&v.type[i]){var x=i%v.W,y=((i/v.W)|0)%v.H,z=(i/(v.W*v.H))|0;bloody(v,x,y,z,255);if(!gw){addWound(v,x,y,z,2.6);gw=1}}}
  _tw.setFromMatrixPosition(t.g.matrixWorld);for(var q=0;q<14;q++)blood(_tw.x,_tw.y,_tw.z,FDv.x*1.4+rnd(-.6,.6),rnd(.2,1.6),rnd(.4,1.8),rnd(.0015,.003),0);
  for(q=1;q<=16;q++){var o=TEETH[q];if(o&&!o.out&&Math.abs(o.sl.slot-t.sl.slot)<=1&&o.sl.low===t.sl.low){o.bl=Math.min(1,o.bl+.35);paintTooth(o)}}
}
function teethCount(){var n=0;for(var k=1;k<=16;k++)if(TEETH[k]&&TEETH[k].out)n++;return n}
/* lower teeth follow the jaw hinge exactly like the shader does */
function teethPose(a,lat){JAWPIV.position.set(lat,-0.008,0.006);JAWPIV.rotation.set(a,0,0)}
/* the jaw tears off: lower teeth go with it */
function teethWithJaw(p){JAWIN.parent.remove(JAWIN);JAWIN.position.set(0,0,0);p.v.group.add(JAWIN)}
