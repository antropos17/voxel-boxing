
/* ---------- anatomy ----------
   Head in centimetres: origin at ear-canal height on the mid-line, +z toward the camera.
   Real adult proportions: 15.5 cm wide, 23 cm chin to crown, 19.5 cm deep. Voxel 3.3 mm. */
var HS=0.33,HB=[-9.4,-11.95,-11.2,9.4,14.7,12.1],PIV=[0,-4.6,-2.6];
var EX=3.2,EY=2.3,EZ=6.2,ER=1.2,LR=1.5;
var R_SCALP=1,R_FORE=2,R_BROW=3,R_EYE=4,R_NOSE=5,R_CHB=6,R_CHEEK=7,R_MOUTH=8,R_JAW=9,R_EAR=10,R_NECK=11,
    R_CHEST=20,R_STERN=21,R_RIBS=22,R_LIVER=23,R_ABS=24,R_SHLD=25,R_BACK=26;
var RVU=new Uint8Array(32),RSF=new Uint8Array(32),RSC=new Float32Array(32),RBL=new Float32Array(32);
/* region: vulnerability of skin to splitting x100, swelling factor x100, swelling cap cm, bleeding rate */
[[R_SCALP,60,70,.5,3],[R_FORE,90,110,.9,3],[R_BROW,200,160,.75,3.2],[R_EYE,130,220,.85,2.2],[R_NOSE,180,120,.55,2.6],[R_CHB,180,150,.75,2],
 [R_CHEEK,70,110,.65,1.6],[R_MOUTH,190,160,.6,2.6],[R_JAW,120,80,.5,1.4],[R_EAR,100,150,.7,1.6],[R_NECK,80,60,.35,1],
 [R_CHEST,50,45,.5,.9],[R_STERN,60,40,.4,.9],[R_RIBS,55,50,.5,.9],[R_LIVER,55,50,.5,.9],[R_ABS,45,40,.4,.8],[R_SHLD,50,40,.4,.8],[R_BACK,40,30,.4,.6]
].forEach(function(r){RVU[r[0]]=r[1];RSF[r[0]]=r[2];RSC[r[0]]=r[3];RBL[r[0]]=r[4]});

function almond(ax,y){var u=(ax-EX)/1.45;if(u<=-1||u>=1)return false;var w=Math.pow(1-u*u,0.7),dy=y-EY;return dy<0.5*w+0.07*u&&dy>-0.33*w+0.05*u}
function headSDF(x,y,z){
  var ax=x<0?-x:x,d,e;
  d=sdEll(x,y-3.4,z+1.2,7.5,9.4,9.4);                       // cranium
  d=smin(d,sdEll(x,y-6.0,z-3.0,6.3,5.0,5.6),2.0);           // forehead
  d=smin(d,sdEll(x,y-3.95,z-6.45,5.5,1.25,2.25),1.0);       // brow ridge
  d=smin(d,sdEll(ax-4.4,y-0.9,z-5.6,2.3,1.5,2.2),1.2);      // cheekbones
  d=smin(d,sdEll(ax-6.3,y-0.7,z-2.0,1.2,0.95,3.0),1.0);     // zygomatic arch
  d=smin(d,sdEll(x,y+1.6,z-5.6,4.1,3.6,3.4),1.6);           // midface
  d=smin(d,sdEll(x,y+4.3,z-6.9,3.2,2.0,2.4),1.2);           // upper jaw under the lip
  d=smin(d,sdEll(x,y+6.9,z-3.6,5.2,2.6,5.0),1.8);           // mandible
  d=smin(d,sdEll(x,y+8.6,z-7.1,2.5,2.0,1.7),1.4);           // chin
  d=smin(d,sdEll(ax-4.9,y+6.4,z-0.2,1.3,1.8,1.9),1.4);      // jaw angle
  d=smin(d,sdCap(ax,y,z,4.9,-6.0,0.2,5.0,-0.8,0.6,1.2),1.2);// ramus
  d=smin(d,sdEll(x,y+7.4,z+2.4,5.0,4.2,4.8),2.2);           // skull base and top of the neck
  e=sdCap(x,y,z,0,3.0,8.3,0,-1.4,10.3,0.55,0.72);           // nose bridge
  e=smin(e,sdEll(x,y+1.75,z-10.15,1.1,0.95,0.95),0.8);      // tip
  e=smin(e,sdEll(ax-1.3,y+2.25,z-8.95,0.82,0.72,0.95),0.6); // nostril wings
  d=smin(d,e,0.9);
  d=smin(d,sdEll(x,y+4.45,z-8.55,2.45,0.72,1.0),0.7);       // upper lip
  d=smin(d,sdEll(x,y+5.95,z-8.35,2.25,0.75,0.95),0.7);      // lower lip
  e=sdEll(ax-7.65,y-0.6,z+1.0,0.95,3.1,2.0);                // ear
  e=smax(e,-sdEll(ax-8.35,y-0.2,z+0.7,0.55,1.5,1.05),0.3);  // ear bowl
  d=smin(d,e,0.5);
  d=smax(d,-sdEll(ax-EX,y-EY,z-7.15,1.8,1.4,1.55),0.6);     // eye sockets
  d=smax(d,-sdEll(x,y+5.25,z-5.3,2.55,0.85,3.2),0.4);       // mouth cavity
  d=smax(d,-sdEll(ax-0.56,y+2.72,z-9.35,0.3,0.24,0.55),0.15);// nostrils
  var ex=ax-EX,ey=y-EY,ez=z-EZ,er=Math.sqrt(ex*ex+ey*ey+ez*ez),lid=er-LR;
  if(ez>0.2&&almond(ax,y))lid=Math.max(lid,0.25);
  d=smin(d,lid,0.3);
  return Math.min(d,er-ER);
}
function toothAt(x,y,z){
  var ax=x<0?-x:x;if(ax>2.75)return 0;
  var u=ax/2.75,zf=8.25-3.0*u*u,k=Math.min(7,Math.floor((x+2.75)/0.6875));
  if(y>-5.24&&y<-4.18&&z<=zf&&z>zf-0.72)return 1+k;
  zf-=0.22;
  if(y<=-5.26&&y>-6.28&&z<=zf&&z>zf-0.72)return 9+k;
  return 0;
}
function inTongue(x,y,z){return inEll(x,y+5.95,z-5.5,1.95,0.5,2.4)}
function lipZone(ax,y,z){if(z<7.9||y>-4.55||y<-6.3)return false;var u=(y+5.25)/1.05;return ax<2.3-0.9*u*u}
function browZone(ax,y,z){if(ax<0.75||ax>5.5||z<5.4)return false;var t=(ax-0.75)/4.75,c=3.45+0.42*Math.sin(Math.PI*Math.min(1,t*1.2))-0.15*t,h=0.42-0.17*t;return y>c-h&&y<c+h}
function lashZone(ax,y){var u=(ax-EX)/1.45;if(u<=-1.02||u>=1.02)return false;var w=Math.pow(Math.max(0,1-u*u),0.7),top=0.5*w+0.07*u,dy=y-EY;return dy>=top-0.05&&dy<top+0.36}
function gumZone(x,y,z){var ax=x<0?-x:x;if(ax>3.0)return false;var u=Math.min(ax,2.75)/2.75,zf=8.25-3.0*u*u;return z<=zf+0.25&&z>zf-0.95&&((y>-4.25&&y<-3.55)||(y<-6.22&&y>-6.9))}
function jawSplitY(x,z){var u=x/2.6;return -5.25+(8.7-z)*0.05-0.12*u*u}
function jawW(x,y,z){if(z<-1.6)return 0;var ys=-4.55+(8.6-z)*0.12,w=sstep(ys+0.9,ys-0.9,y);if(z<2.4)w*=sstep(-1.6,1.2,z);return w}
function softT(x,y,z){var ax=x<0?-x:x;return 0.62+1.0*gs(ax-4.0,y+2.6,z-5.0,2.0)+0.8*gs(ax-5.0,y+5.0,z-1.5,1.8)+0.35*gs(x,y+9.0,z-7.5,1.5)+0.35*gs(ax-6.6,y-3.0,z-2.0,1.5)-0.15*gs(x,y-2.4,z-8.6,1.0)-0.12*gs(ax-4.6,y-0.9,z-7.2,1.2)}
function fatT(x,y,z){var ax=x<0?-x:x;return 0.33+0.5*gs(ax-4.0,y+2.4,z-5.4,1.8)}
function noBone(ax,y,z){
  if(ax>6.85&&y>-3.2&&y<4.1&&z<1.6)return true;
  if(z>8.55&&y<1.8&&y>-3.3&&ax<2.3)return true;
  if(z>7.9&&y<-3.4&&y>-6.9&&ax<2.7)return true;
  return false;
}
function headRegion(x,y,z){var ax=x<0?-x:x;
  if(ax>6.75&&y>-3.6&&y<4.3&&z<1.9)return R_EAR;
  if(y<-9.0&&z<4.5)return R_NECK;
  if(z<0.6)return y<-3?R_NECK:R_SCALP;
  if(y>4.7)return z>3.4?R_FORE:R_SCALP;
  if(y>3.05&&ax<5.7)return R_BROW;
  if(ax<1.8&&y>-3.35&&y<=3.05&&z>7.3)return R_NOSE;
  if(y>0.4&&ax<5.6)return R_EYE;
  if(ax<2.9&&y<=-3.2&&y>-7.0&&z>6.0)return R_MOUTH;
  if(y>-0.4&&ax>=2.6)return R_CHB;
  if(y<=-6.2||(y<=-3.6&&ax>=4.2))return R_JAW;
  return R_CHEEK;
}
function hairZone(x,y,z){var ax=x<0?-x:x;
  if(ax>6.6&&y<4.1&&y>-3.9&&z>-3.4&&z<2.1)return false;
  var q=(ax/5.5)*(ax/5.5),h;
  if(z>=4.4)h=9.2+1.5*q;
  else if(z>=1.4)h=9.2+1.5*q-(4.4-z)*(8.3+1.5*q)/3.0;
  else if(z>=-2.6)h=0.9;
  else h=-3.2-0.8*(1-Math.min(1,ax/6));
  return y>h;
}
function beardZone(x,y,z){var ax=x<0?-x:x;
  if(z<0.8||ax>7.2||lipZone(ax,y,z))return false;
  if(y<-2.4)return y<-6.9||!(ax<2.9&&z>7.2&&y<-3.6);
  return ax<2.6&&y>-3.98&&y<-3.0&&z>8.2;
}

var HEADBASE=null;
function buildHead(){
  var s=HS,W=Math.ceil((HB[3]-HB[0])/s),H=Math.ceil((HB[4]-HB[1])/s),D=Math.ceil((HB[5]-HB[2])/s);
  var v=new VBody(W,H,D,s,HB[0],HB[1],HB[2],true),x,y,z,i,X,Y,Z,id;
  v.tid=new Uint8Array(v.n);
  v.fill(headSDF);
  var x0=Math.floor((-3.2-v.ox)/s),x1=Math.ceil((3.2-v.ox)/s),y0=Math.floor((-6.7-v.oy)/s),y1=Math.ceil((-3.9-v.oy)/s),z0=Math.floor((2.8-v.oz)/s),z1=Math.ceil((8.7-v.oz)/s);
  for(z=z0;z<=z1;z++)for(y=y0;y<=y1;y++)for(x=x0;x<=x1;x++){i=x+W*(y+H*z);X=v.X(x);Y=v.Y(y);Z=v.Z(z);
    id=toothAt(X,Y,Z);if(id){v.type[i]=0}else if(inTongue(X,Y,Z))v.type[i]=1}
  var dist=chamfer(v.type,W,H,D),ax,t,f,dep,surf,ex,ey,ez,er,R;
  for(z=0;z<D;z++){Z=v.Z(z);for(y=0;y<H;y++){Y=v.Y(y);i=W*(y+H*z);for(x=0;x<W;x++,i++){
    if(!v.type[i])continue;
    X=v.X(x);ax=X<0?-X:X;dep=dist[i]/3*s;surf=dist[i]<=4;f=0;
    ex=ax-EX;ey=Y-EY;ez=Z-EZ;er=Math.sqrt(ex*ex+ey*ey+ez*ez);
    if(v.tid[i])t=TOOTH;
    else if(er<ER){if(surf&&ez>0){var r2=ex*ex+ey*ey;t=r2<0.055?PUPIL:r2<0.34?IRIS:EYE;f|=F_EYE}else t=surf?EYE:EYEIN}
    else if(inTongue(X,Y,Z))t=TONGUE;
    else if(surf){
      if(gumZone(X,Y,Z)&&!lipZone(ax,Y,Z))t=GUM;
      else if(inEll(X,Y+5.25,Z-5.3,3.0,1.25,3.65)||inEll(ax-0.56,Y+2.72,Z-9.35,0.62,0.55,0.85))t=MOUTH;
      else if(lipZone(ax,Y,Z))t=LIP;
      else if(er<LR+0.45&&ez>0.15&&lashZone(ax,Y))t=LASH;
      else if(browZone(ax,Y,Z))t=BROW;
      else t=SKIN;
    }else if(gumZone(X,Y,Z))t=GUM;
    else if(noBone(ax,Y,Z))t=(ax>6.7||(Z>8.4&&Y>-3.3))?CART:MUSC;
    else{var soft=softT(X,Y,Z);t=dep<=s+fatT(X,Y,Z)?FAT:dep<=soft?MUSC:dep<=soft+0.72?BONE:CORE}
    if(t===SKIN||t===LIP)f|=F_SKIN;
    if(ax<3.0&&Z>3.6&&Y>-7.4&&Y<-3.3){f|=F_JAWH;if(Y<jawSplitY(X,Z))f|=F_JAWL}
    if(t===TONGUE)f|=F_JAWH|F_JAWL;
    v.type[i]=t;v.fl[i]=f;R=headRegion(X,Y,Z);v.rg[i]=R;v.vu[i]=RVU[R];v.sf[i]=RSF[R];
  }}}
  v.jawFn=jawW;v.seed=v.cell(0,3.4,-1.2);
  v.mat.uniforms.uCen.value.set(0,0.01,0);
  v.cs=24;v.sidePrior=.2;v.initChunks();
  HEADBASE={type:v.type.slice(),fl:v.fl.slice()};
  return v;
}
var TC=new Float32Array(3);
function headSkin(X,Y,Z,ax,L){
  var r=L.skin[0],g=L.skin[1],b=L.skin[2],k,t2,qx,qy;
  k=gs(ax-3.9,Y+1.6,Z-6.2,2.4);r+=15*k;g-=7*k;b-=5*k;
  k=gs(X,Y+1.8,Z-9.9,1.5)+0.6*gs(ax-1.3,Y+2.2,Z-8.9,0.9);r+=12*k;g-=8*k;b-=6*k;
  if(ax>6.75&&Y>-3.4&&Y<4.2&&Z<1.8){r+=8;g-=5;b-=4;if(inEll(ax-8.3,Y-0.2,Z+0.7,0.9,1.9,1.4)){r*=0.72;g*=0.66;b*=0.66}}
  k=gs(ax-3.2,Y-0.95,Z-7.0,0.85);r*=1-0.1*k;g*=1-0.14*k;b*=1-0.06*k;
  if(Z>6.4&&Y>EY+0.42&&Y<EY+0.95&&Math.abs(ax-EX)<1.45){r*=0.86;g*=0.84;b*=0.84}
  if(ax<0.42&&Y>-4.05&&Y<-2.95&&Z>8.3){r*=0.9;g*=0.88;b*=0.88}
  t2=clamp(((ax-1.85)*1.05+(Y+2.5)*(-2.9))/(1.05*1.05+2.9*2.9),0,1);qx=1.85+1.05*t2;qy=-2.5-2.9*t2;
  if(Z>7&&(ax-qx)*(ax-qx)+(Y-qy)*(Y-qy)<0.07){r*=0.86;g*=0.84;b*=0.84}
  if(ax<1.8&&Y>-7.15&&Y<-6.65&&Z>7.8){r*=0.88;g*=0.86;b*=0.86}
  if(Y<-9.4&&Z<4.5){r*=0.85;g*=0.85;b*=0.86}
  if(Y>4&&ax<4&&Z>5){r*=1.03;g*=1.03;b*=1.03}
  if(L.stub>0&&beardZone(X,Y,Z)){k=L.stub;r+=(L.beardC[0]-r)*k;g+=(L.beardC[1]-g)*k;b+=(L.beardC[2]-b)*k}
  TC[0]=r;TC[1]=g;TC[2]=b;
}
function headLook(v,L){
  var W=v.W,H=v.H,D=v.D,WH=W*H,type=v.type,x,y,z,i,X,Y,Z,ax,t,r,g,b,n,k,pass,st=L.hair;
  type.set(HEADBASE.type);v.fl.set(HEADBASE.fl);
  v.br.fill(0);v.mt.fill(0);v.ab.fill(0);v.bl.fill(0);v.sw.fill(0);v.dx.fill(0);v.dy.fill(0);v.dz.fill(0);v.anyDef=false;
  for(i=0;i<v.n;i++)if(type[i]){var R0=v.rg[i];v.vu[i]=RVU[R0];v.sf[i]=RSF[R0]}
  // hair: shaved scalp becomes hair colour; longer styles grow voxel layers outward
  if(st!=='bald'||L.beard==='full'){
    for(z=0;z<D;z++){Z=v.Z(z);for(y=0;y<H;y++){Y=v.Y(y);i=W*(y+H*z);for(x=0;x<W;x++,i++){
      if(type[i]!==SKIN)continue;X=v.X(x);
      var inH=st!=='bald'&&hairZone(X,Y,Z)&&(st!=='mohawk'||Math.abs(X)<1.4);
      if((inH||(L.beard==='full'&&beardZone(X,Y,Z)))&&v.exp(x,y,z,i)){type[i]=HAIR;v.fl[i]&=~F_SKIN}
    }}}
    var layers=st==='short'?1:st==='mohawk'?5:0,np=Math.max(layers,L.beard==='full'?1:0);
    for(pass=0;pass<np;pass++){
      var add=[];
      for(z=1;z<D-1;z++){Z=v.Z(z);for(y=1;y<H-1;y++){Y=v.Y(y);i=1+W*(y+H*z);for(x=1;x<W-1;x++,i++){
        if(type[i])continue;
        if(type[i-1]!==HAIR&&type[i+1]!==HAIR&&type[i-W]!==HAIR&&type[i+W]!==HAIR&&type[i-WH]!==HAIR&&type[i+WH]!==HAIR)continue;
        X=v.X(x);
        if((pass<layers&&hairZone(X,Y,Z)&&(st!=='mohawk'||(Math.abs(X)<1.1&&Y>3)))||(L.beard==='full'&&pass===0&&beardZone(X,Y,Z)))add.push(i);
      }}}
      for(k=0;k<add.length;k++){i=add[k];type[i]=HAIR;v.fl[i]=0;var hz=v.Z((i/WH)|0)>0.8&&v.Y(((i/W)|0)%H)<-2;v.rg[i]=hz?R_JAW:R_SCALP;v.vu[i]=60;v.sf[i]=50}
    }
  }
  // colour every solid cell
  var sk=L.skin,hc=L.hairC,ir=L.iris;
  for(z=0;z<D;z++){Z=v.Z(z);for(y=0;y<H;y++){Y=v.Y(y);i=W*(y+H*z);for(x=0;x<W;x++,i++){
    t=type[i];if(!t)continue;X=v.X(x);ax=X<0?-X:X;n=0.95+0.1*hash1(i);
    switch(t){
      case SKIN:headSkin(X,Y,Z,ax,L);r=TC[0];g=TC[1];b=TC[2];if(st==='bald'&&Y>6)n*=1.03;break;
      case HAIR:n=0.8+0.35*hash1(i*3+1);r=hc[0];g=hc[1];b=hc[2];
        if(st==='buzz'){headSkin(X,Y,Z,ax,L);r=r*0.62+TC[0]*0.38;g=g*0.62+TC[1]*0.38;b=b*0.62+TC[2]*0.38}break;
      case BROW:n=0.8+0.3*hash1(i*3+1);r=hc[0]*0.85;g=hc[1]*0.85;b=hc[2]*0.85;break;
      case LASH:r=32;g=24;b=22;break;
      case LIP:headSkin(X,Y,Z,ax,L);r=TC[0]*0.74+40;g=TC[1]*0.7+10;b=TC[2]*0.7+16;if(Y>-5.45&&Y<-4.95)n*=0.84;else if(Y<-5.9)n*=0.95;break;
      case EYE:r=236;g=230;b=222;if(Math.abs(ax-EX)>0.85)n*=0.9;break;
      case IRIS:r=ir[0];g=ir[1];b=ir[2];break;
      case PUPIL:r=14;g=10;b=12;break;
      case EYEIN:r=150;g=46;b=52;break;
      case TOOTH:r=236;g=230;b=206;var tl=v.tid[i-1];if(tl&&tl!==v.tid[i])n*=0.72;else n=0.96+0.06*((v.tid[i]%2));break;
      case GUM:r=208;g=112;b=114;break;
      case MOUTH:if(Y>-3.6){r=62;g=34;b=34}else{r=148;g=56;b=62}break;
      case TONGUE:r=198;g=98;b=102;break;
      case CART:r=224;g=192;b=184;break;
      case FAT:r=232;g=200;b=124;break;
      case MUSC:r=168;g=38;b=42;if(((x+y*2)%5)===0)n*=0.82;break;
      case BONE:r=236;g=226;b=200;break;
      case CORE:if(brainZone(X,Y,Z)){brainCol(X,Y,Z,i);r=TC[0];g=TC[1];b=TC[2];n=1}else{var hm=hash1(i*11+5);if(hm<.5){r=96;g=18;b=24}else if(hm<.8){r=142;g=30;b=36}else if(hm<.93){r=188;g=84;b=88}else{r=222;g=206;b=178}n=1}break;
      default:r=88;g=22;b=28;
    }
    if(t===EYE||t===IRIS||t===PUPIL){var hx=(X<0?-EX:EX)+0.3,hy=EY+0.33;if((X-hx)*(X-hx)+(Y-hy)*(Y-hy)<0.04){r=255;g=255;b=255;n=1}}
    v.rgb[i*3]=clamp(r*n,0,255);v.rgb[i*3+1]=clamp(g*n,0,255);v.rgb[i*3+2]=clamp(b*n,0,255);v.hp[i]=HPT[t];
  }}}
  v.mat.uniforms.uLid.value.setRGB(sk[0]/255*0.86,sk[1]/255*0.84,sk[2]/255*0.84);
  surfNormals(v);v.clearHits();v.allDirty();
}
/* brain: everything inside the skull above the eyes and behind the face; gyri from a folded sine field */
function brainZone(X,Y,Z){return Y>EY-0.6||(Z<0.5&&Y>-3.2)}
function brainCol(X,Y,Z,i){
  var f=Math.sin(X*1.9+1.4*Math.sin(Y*1.2+Z*0.4))+Math.sin(Y*1.8+1.3*Math.sin(Z*1.1))+Math.sin(Z*1.7+1.2*Math.sin(X*0.9+Y*0.5)),h=0.92+0.12*hash1(i*7+3);
  if(Math.abs(X)<0.35){TC[0]=120;TC[1]=52;TC[2]=60}
  else if(Math.abs(f)<0.42){TC[0]=150*h;TC[1]=78*h;TC[2]=88*h}
  else{TC[0]=222*h;TC[1]=164*h;TC[2]=164*h}
}
/* intestines behind the abdominal wall */
function gutCol(X,Y,Z,i){
  var f=Math.sin(X*0.95+1.8*Math.sin(Y*0.7))*Math.sin(Y*1.05+1.7*Math.sin(Z*0.6+X*0.35)),h=0.9+0.15*hash1(i*5+11);
  if(Math.abs(f)<0.16){TC[0]=126*h;TC[1]=42*h;TC[2]=50*h}
  else if(hash1(i*13+5)<0.05){TC[0]=228*h;TC[1]=196*h;TC[2]=126*h}
  else{TC[0]=212*h;TC[1]=128*h;TC[2]=122*h}
}
function gutZone(X,Y,Z){return Y<23&&Z>-4}
/* mouthguard: a horseshoe over the upper teeth, in head centimetres */
function guardSDF(x,y,z){var ax=x<0?-x:x,u=Math.min(ax,3.1)/2.75,zf=8.25-3.0*u*u;
  return Math.max(Math.max(z-(zf+0.42),(zf-1.05)-z),Math.max(Math.max(y+3.92,-5.42-y),ax-3.15))}
function surfNormals(v){var W=v.W,H=v.H,D=v.D,x,y,z,i;for(z=1;z<D-1;z++)for(y=1;y<H-1;y++){i=1+W*(y+H*z);for(x=1;x<W-1;x++,i++)if(SKL[v.type[i]]&&v.exp(x,y,z,i))setNrm(v,x,y,z,i)}}

/* ---------- torso: waist at the origin, 48 cm across the shoulders, ribs under two layers of muscle. Voxel 6.2 mm ---------- */
var TS=0.55,TB=[-27.2,-2.5,-14.4,27.2,55.4,12.6];
function torsoSDF(x,y,z){var ax=x<0?-x:x,d;
  d=sdEll(x,y-29,z+1,15.0,16.5,10.8);
  d=smin(d,sdEll(x,y-12,z-1.5,13.2,13.5,9.6),5);
  d=smin(d,sdEll(x,y-2,z,14.4,7.5,10.4),4);
  d=smin(d,sdEll(ax-7.4,y-33.8,z-6.4,8.8,5.8,3.5),2.2);
  d=smin(d,sdEll(ax-10.5,y-27,z+3.5,6.5,13,7),4);
  d=smin(d,sdEll(x,y-34,z+4.5,15.5,13,8.6),4);
  d=smin(d,sdEll(x,y-45.5,z+3.0,11.5,4.8,6.0),4);
  d=smin(d,sdEll(ax-18.6,y-41.2,z+0.3,5.8,6.8,5.9),3);
  d=smin(d,sdCap(ax,y,z,2,45,3.5,16,46.5,0,1.8),2);
  d=smin(d,sdCap(x,y,z,0,44,-1.5,0,51,0,6.0),3);
  return d;
}
function ribBand(x,y,z){var ax=x<0?-x:x;
  if(sdCap(ax,y,z,2,45,3.5,16,46.5,0,1.1)<0)return true;
  if(ax<1.6&&y>21&&y<44&&z>0)return true;
  if(y>43||y<9)return false;
  if(z>0&&y<21-0.8*ax)return false;
  var ph=(y+0.3*ax)/2.7;return ph-Math.floor(ph)<0.46;
}
function torsoRegion(x,y,z){var ax=x<0?-x:x;
  if(ax>15.5&&y>34)return R_SHLD;
  if(y>44)return R_SHLD;
  if(z<-3)return R_BACK;
  if(ax<4.5&&y>16&&y<28)return R_STERN;
  if(y>=27.5&&ax<16)return R_CHEST;
  if(x<-4.5&&y>9&&y<24)return R_LIVER;
  if(ax>=8&&y>9)return R_RIBS;
  return R_ABS;
}
var TORSOBASE=null;
function buildTorso(){
  var s=TS,W=Math.ceil((TB[3]-TB[0])/s),H=Math.ceil((TB[4]-TB[1])/s),D=Math.ceil((TB[5]-TB[2])/s);
  var v=new VBody(W,H,D,s,TB[0],TB[1],TB[2],true),x,y,z,i,X,Y,Z,ax,t,dep,R;
  v.fill(torsoSDF);
  var dist=chamfer(v.type,W,H,D);
  for(z=0;z<D;z++){Z=v.Z(z);for(y=0;y<H;y++){Y=v.Y(y);i=W*(y+H*z);for(x=0;x<W;x++,i++){
    if(!v.type[i])continue;X=v.X(x);ax=X<0?-X:X;dep=dist[i]/3*s;
    if(dist[i]<=4)t=SKIN;
    else if(dep<=s*1.95)t=FAT;
    else{var pec=inEll(ax-7.4,Y-33.8,Z-6.4,9.4,6.4,4.3),soft=pec?3.4:(Z>0&&Y<21-0.8*ax)?2.4:2.0;
      t=dep<=soft?MUSC:dep<=soft+1.3?(ribBand(X,Y,Z)?BONE:MUSC):CORE}
    v.type[i]=t;v.fl[i]=t===SKIN?F_SKIN:0;R=torsoRegion(X,Y,Z);v.rg[i]=R;v.vu[i]=RVU[R];v.sf[i]=RSF[R];
  }}}
  v.seed=v.cell(0,28,-1);v.aol=AOS;v.sm=0.68;v.mat.uniforms.uCen.value.set(0,0.28,-0.01);
  v.initChunks();
  TORSOBASE={type:v.type.slice(),fl:v.fl.slice()};
  return v;
}
function torsoLook(v,L){
  var W=v.W,H=v.H,D=v.D,type=v.type,x,y,z,i,X,Y,Z,ax,t,r,g,b,n,sk=L.skin;
  type.set(TORSOBASE.type);v.fl.set(TORSOBASE.fl);
  v.br.fill(0);v.mt.fill(0);v.ab.fill(0);v.bl.fill(0);v.sw.fill(0);v.dx.fill(0);v.dy.fill(0);v.dz.fill(0);v.anyDef=false;
  for(z=0;z<D;z++){Z=v.Z(z);for(y=0;y<H;y++){Y=v.Y(y);i=W*(y+H*z);for(x=0;x<W;x++,i++){
    t=type[i];if(!t)continue;X=v.X(x);ax=X<0?-X:X;n=0.95+0.1*hash1(i+77);
    if(t===SKIN){
      r=sk[0];g=sk[1];b=sk[2];
      var pe=((ax-7.4)/8.8)*((ax-7.4)/8.8)+((Y-33.8)/5.8)*((Y-33.8)/5.8);
      if(Y<33.8&&Z>3.5&&pe>0.82&&pe<1.22)n*=0.8;                          // under the pecs
      if(Z>5.5&&Y>8.5&&Y<24&&(ax<0.4||(ax<6.0&&ax>0.8&&(Math.abs(Y-17.0)<0.4||Math.abs(Y-12.2)<0.4))))n*=0.88; // abs
      if(Z>6&&ax<0.45&&Math.abs(Y-3.4)<0.45)n*=0.45;                       // navel
      if(Z>4&&(ax-9.6)*(ax-9.6)+(Y-30.6)*(Y-30.6)<0.42){r=r*0.72+30;g*=0.64;b*=0.62} // nipples
      if(sdCap(ax,Y,Z,2,45,3.5,16,46.5,0,2.6)<0.4)n*=Y>45.6?1.06:0.9;     // collarbones
      if(ax>10.5&&ax<14&&Z>-2&&Y>18&&Y<30&&((Y+ax*0.8)%3.2)<0.7)n*=0.9;    // serratus
      if(ax>15&&Y>35&&Math.abs(ax-14.8-(Y-35)*0.12)<0.5)n*=0.86;          // deltoid line
      if(L.chestHair&&ax<6.5-Math.abs(Y-31)*0.25&&Y>22&&Y<40&&Z>4&&hash1(i*5+2)<0.22){r=r*0.55+L.hairC[0]*0.45;g=g*0.55+L.hairC[1]*0.45;b=b*0.55+L.hairC[2]*0.45}
    }else if(t===FAT){r=232;g=200;b=124}
    else if(t===MUSC){r=166;g=38;b=42;if(((x+y)%4)===0)n*=0.84}
    else if(t===BONE){r=234;g=224;b=198}
    else if(gutZone(X,Y,Z)){gutCol(X,Y,Z,i);r=TC[0];g=TC[1];b=TC[2];n=1}
    else{r=86;g=22;b=28}
    v.rgb[i*3]=clamp(r*n,0,255);v.rgb[i*3+1]=clamp(g*n,0,255);v.rgb[i*3+2]=clamp(b*n,0,255);v.hp[i]=t===SKIN?14:t===FAT?10:t===MUSC?16:t===BONE?45:26;
  }}}
  surfNormals(v);v.clearHits();v.allDirty();
}

/* ---------- static parts: built once, repainted per fighter ---------- */
function buildStatic(bb,s,sdf,part){
  var W=Math.ceil((bb[3]-bb[0])/s)+2,H=Math.ceil((bb[4]-bb[1])/s)+2,D=Math.ceil((bb[5]-bb[2])/s)+2;
  var v=new VBody(W,H,D,s,bb[0]-s,bb[1]-s,bb[2]-s,false),i,x,y,z;v.cs=64;v.aol=AOS;v.sm=0.7;
  v.fill(sdf);
  for(z=0;z<D;z++)for(y=0;y<H;y++){i=W*(y+H*z);for(x=0;x<W;x++,i++)if(v.type[i])v.fl[i]=part(v.X(x),v.Y(y),v.Z(z))}
  v.initChunks();return v;
}
function paintStatic(v,pal){
  for(var i=0;i<v.n;i++){if(!v.type[i])continue;var p=pal[v.fl[i]]||pal[0],n=p[4]===undefined?0.94+0.12*hash1(i):p[4]+(1-p[4])*hash1(i);
    v.rgb[i*3]=clamp(p[0]*n,0,255);v.rgb[i*3+1]=clamp(p[1]*n,0,255);v.rgb[i*3+2]=clamp(p[2]*n,0,255);v.wet[i]=p[3]*255}
  for(var k=0;k<v.chunks.length;k++){if(v.chunks[k].geo)v.chunks[k].cd=true;else v.chunks[k].gd=true}v.dirty=true;
}
/* glove: centred on the fist, +z toward the knuckles, +y the back of the hand */
function gloveSDF(side){return function(x,y,z){
  var d=sdEll(x,y,z-1.0,6.4,5.6,7.6);
  d=smin(d,sdEll(x,y-0.6,z-5.4,6.2,4.9,4.0),2.5);
  d=smin(d,sdCap(x,y,z,side*5.2,-1.5,-2.8,side*4.6,-2.0,4.4,2.4),1.6);
  d=smin(d,sdCap(x,y,z,0,-0.4,-11.5,0,-0.4,-4.5,4.8),2.2);
  return d;
}}
function glovePart(side){return function(x,y,z){
  var ax=x<0?-x:x;
  if(z<-7.0&&z>-10.4)return 1;
  if(z<=-7.0)return y>3.4&&ax<1.8?2:5;
  if(z<-4&&y>3.6&&ax<1.5)return 2;
  if(y>4.1&&ax<1.0&&z>-3.2&&z<5)return 3;
  if(y<-3.7)return 4;
  if(Math.abs(x-side*3.6)<0.5&&y<-0.5&&z<4.5)return 5;
  return 0;
}}
function armUpSDF(x,y,z){var d=sdCap(x,y,z,0,0,0,0,29,0,4.5,3.8);d=smin(d,sdEll(x,y-15,z-1.3,4.1,7.5,4.3),2);return smin(d,sdEll(x,y-14,z+1.5,4.2,8.5,4.0),2)}
function armFoSDF(x,y,z){var d=sdCap(x,y,z,0,0,0,0,27,0,4.2,2.9);return smin(d,sdEll(x-0.6,y-7,z-0.6,3.9,7,3.5),2)}
function neckSDF(x,y,z){var q=Math.sqrt((x/5.6)*(x/5.6)+(z/5.0)*(z/5.0))-1,d=Math.max(q*5.2,Math.max(-y,y-14));
  d=smin(d,sdCap(Math.abs(x),y,z,2.0,0,3.0,4.0,13,-0.8,1.3),1.2);return smin(d,sdEll(x,y-8.6,z-4.6,0.9,1.3,0.9),0.8)}
function legD(x,y,z){
  var d=sdCap(x,y,z,0,86,0,0.5,48,3,9.0,6.3);
  d=smin(d,sdCap(x,y,z,0,84,0,0.8,62,1.5,10.6,9.8),2);
  d=smin(d,sdCap(x,y,z,0.5,48,3,0.6,10,0,5.6,4.0),2.5);
  d=smin(d,sdEll(x,y-33,z+1.6,5.6,9,5.8),3);
  d=smin(d,sdCap(x,y,z,0.6,4,0,0.6,19,0,5.4),1.5);
  return smin(d,sdEll(x,y-4,z-4.5,5.0,4.2,11.5),2);
}
function legsSDF(x,y,z){var d=sdEll(x,y-91,z+1,17.5,13.5,12.5);d=smin(d,sdEll(x,y-100.5,z+1,16.8,4.0,12.0),2);return smin(d,Math.min(legD(x+10.5,y,z-9),legD(x-10.5,y,z+9)),4)}
/* legs split for the ragdoll: pelvis with the trunks, a thigh and a shin per leg (same coordinates as the whole) */
var LJ={hip:[[-10.5,86,9],[10.5,86,-9]],knee:[[-10,48,12],[11,48,-6]],ank:[[-9.9,10,9],[11.1,10,-9]]};
function legOne(side){return side<0?function(x,y,z){return legD(x+10.5,y,z-9)}:function(x,y,z){return legD(x-10.5,y,z+9)}}
function pelvSDF(x,y,z){return Math.max(legsSDF(x,y,z),79-y)}
function sphD(x,y,z,c,r){return Math.sqrt((x-c[0])*(x-c[0])+(y-c[1])*(y-c[1])+(z-c[2])*(z-c[2]))-r}
function thighSDF(side){var f=legOne(side),k=side<0?0:1,h=LJ.hip[k],n=LJ.knee[k];return function(x,y,z){return Math.min(Math.max(f(x,y,z),Math.max(46-y,y-84)),Math.min(sphD(x,y,z,h,8.6),sphD(x,y,z,n,5.9)))}}
function shinSDF(side){var f=legOne(side),n=LJ.knee[side<0?0:1];return function(x,y,z){return Math.min(Math.max(f(x,y,z),y-50),sphD(x,y,z,n,5.8))}}
function legsPart(x,y,z){var ax=x<0?-x:x;
  if(y>97.2)return y>99.6&&y<100.6?3:2;
  if(y>61.5){if(y<64&&ax<15&&ax>6)return 1;return Math.abs(ax-20.5+(97-y)*0.12)<0.9?3:1}
  if(y<19.5)return y<1.6?5:(Math.abs(x+(x<0?10.5:-10.5))<0.9&&z>(x<0?11:-7))?5:4;
  return 0;
}
