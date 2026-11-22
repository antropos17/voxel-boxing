
/* ---------- tissue damage ----------
   A blow spreads over the glove's contact patch, weighted toward whatever sticks out (nose, brow, cheekbone, chin).
   Skin takes bruising, swelling and scrapes first; it splits only where it is already weak, and the split runs as a line.
   Fat, muscle and bone are reached only once the layer above is gone. */
var ACT=[],actT=0;
function matTick(dt){
  actT+=dt;if(actT<0.45)return;var inc=actT/14*255,k,j=0,v,i,m,W,H;actT=0;
  for(k=0;k<ACT.length;k+=2){v=ACT[k];i=ACT[k+1];
    if(!SKL[v.type[i]]){v.fl[i]&=~F_ACT;continue}
    m=v.mt[i]+inc;if(m>=255)m=255;v.mt[i]=m;W=v.W;H=v.H;v.mc(i%W,((i/W)|0)%H,(i/(W*H))|0);
    if(m<255){ACT[j++]=v;ACT[j++]=i}else v.fl[i]&=~F_ACT;
  }
  ACT.length=j;
}
function setNrm(v,x,y,z,i){
  if(v.fl[i]&F_NRM)return;
  var W=v.W,H=v.H,D=v.D,t=v.type,sx=0,sy=0,sz=0,a,b,c,xx,yy,zz;
  for(c=-2;c<=2;c++){zz=z+c;for(b=-2;b<=2;b++){yy=y+b;for(a=-2;a<=2;a++){xx=x+a;
    if(xx<0||yy<0||zz<0||xx>=W||yy>=H||zz>=D||!t[xx+W*(yy+H*zz)]){sx+=a;sy+=b;sz+=c}}}}
  var l=Math.sqrt(sx*sx+sy*sy+sz*sz);if(l<1e-6){sz=1;l=1}
  v.nx[i]=sx/l*127;v.ny[i]=sy/l*127;v.nz[i]=sz/l*127;v.fl[i]|=F_NRM;
}
function track(v,i){if(!(v.fl[i]&F_ACT)){v.fl[i]|=F_ACT;ACT.push(v,i)}}
var PL=[],FAIL=[];
function impact(v,hx,hy,hz,LD,E,kind,isHead,gb){
  var s=v.s,W=v.W,H=v.H,D=v.D,type=v.type,power=kind!=='jab';
  var Rc=(isHead?(power?2.4:1.7):(power?3.8:2.6))/s,R=Math.ceil(Rc*1.5+2),R2=Rc*Rc*2.1,hs=1.4/s;
  var out={rem:[],wet:0,split:0,maxP:0,bl:0,n:0,lip:0,deep:0};
  PL.length=0;FAIL.length=0;
  var i0=hx+W*(hy+H*hz),cx_=0,cy_=0,cz_=0;
  if(v.fl[i0]&F_NRM){cx_=v.nx[i0]/127;cy_=v.ny[i0]/127;cz_=v.nz[i0]/127}else{setNrm(v,hx,hy,hz,i0);cx_=v.nx[i0]/127;cy_=v.ny[i0]/127;cz_=v.nz[i0]/127;if(!SKL[type[i0]])v.fl[i0]&=~F_NRM}
  var gx_=-cx_*0.7+LD.x*0.3,gy_=-cy_*0.7+LD.y*0.3,gz_=-cz_*0.7+LD.z*0.3,gl_=Math.sqrt(gx_*gx_+gy_*gy_+gz_*gz_)||1;
  var dx,dy,dz,x,y,z,i,t,al,px,py,pz,pr2,p,k,lx=gx_/gl_,ly=gy_/gl_,lz=gz_/gl_,tx=LD.x,ty=LD.y,tz=LD.z;
  for(dz=-R;dz<=R;dz++){z=hz+dz;if(z<0||z>=D)continue;
    for(dy=-R;dy<=R;dy++){y=hy+dy;if(y<0||y>=H)continue;
      for(dx=-R;dx<=R;dx++){x=hx+dx;if(x<0||x>=W)continue;
        i=x+W*(y+H*z);t=type[i];if(!t)continue;
        al=-(dx*lx+dy*ly+dz*lz);if(al<-hs)continue;
        px=dx+al*lx;py=dy+al*ly;pz=dz+al*lz;pr2=px*px+py*py+pz*pz;if(pr2>R2)continue;
        if(!v.exp(x,y,z,i))continue;
        p=Math.exp(-pr2/(Rc*Rc)*1.4)*Math.min(1.5,1+al/hs);if(p<0.04)continue;
        PL.push(i,x,y,z,p);
  }}}
  var Ep,c,sl,nx,ny,nz,cap,cur;
  for(k=0;k<PL.length;k+=5){
    i=PL[k];x=PL[k+1];y=PL[k+2];z=PL[k+3];p=PL[k+4];t=type[i];Ep=E*p;
    if(Ep>out.maxP)out.maxP=Ep;out.bl+=v.bl[i];out.n++;
    if(SKL[t]){
      if(t===LIP)out.lip++;
      v.br[i]=Math.min(255,v.br[i]+Ep*21);v.mt[i]=v.mt[i]*Math.max(0.3,1-Ep*0.16);track(v,i);
      if(!(v.fl[i]&F_NRM))setNrm(v,x,y,z,i);
      cap=RSC[v.rg[i]]*100;cur=v.sw[i];if(cur<cap&&!(v.fl[i]&F_LOOSE)){v.sw[i]=Math.min(cap,cur+Ep*2.6*v.sf[i]/100);v.anyDef=true}
      nx=v.nx[i]/127;ny=v.ny[i]/127;nz=v.nz[i]/127;c=-(nx*tx+ny*ty+nz*tz);sl=Math.sqrt(Math.max(0,1-c*c));
      if(Ep>1.0)v.ab[i]=Math.min(255,v.ab[i]+(Ep-1.0)*22*(0.25+sl)*(0.35+v.br[i]/255)*(power?1:0.55));
      v.hp[i]-=Ep*0.3*v.vu[i]/100*(0.55+v.br[i]/255+v.ab[i]/400);
      if(gb>0.05)v.bl[i]=Math.min(255,v.bl[i]+gb*p*70);
      v.mg(x,y,z);
    }else if(t===EYE||t===IRIS||t===PUPIL||t===LASH){v.hp[i]-=Ep*0.12;v.mc(x,y,z)}
    else if(t!==TOOTH){
      v.hp[i]-=Ep*(t===BONE?0.26:t===CART?0.32:t===CORE?0.36:0.42);v.bl[i]=Math.min(255,v.bl[i]+Ep*45);out.wet++;out.deep++;
      if(t===BONE&&v.hp[i]<HPT[BONE]*0.72)v.fl[i]|=F_CRACK;
      v.mc(x,y,z);
    }
    if(v.hp[i]<=0)FAIL.push(i,x,y,z,t);
  }
  for(k=0;k<FAIL.length;k+=5){
    i=FAIL[k];if(!type[i])continue;x=FAIL[k+1];y=FAIL[k+2];z=FAIL[k+3];t=FAIL[k+4];
    var rg=v.rg[i];
    removeCell(v,i,x,y,z,out);
    if(SKL[t]){out.split++;tear(v,x,y,z,E,rg,out);addWound(v,x,y,z,RBL[rg]*(isHead?1:0.6))}
    else if(t!==BONE&&Math.random()<0.3)addWound(v,x,y,z,RBL[rg]*0.5);
  }
  return out;
}
function removeCell(v,i,x,y,z,out){
  v.colorOf(i,CC);out.rem.push(x,y,z,CC[0],CC[1],CC[2],v.type[i]);
  v.type[i]=0;v.mg(x,y,z);
  var sd=v.seeds||(v.seeds=[]),W_=v.W,WH_=W_*v.H;if(sd.length>30000)v.islFull=true;else{if(x>0)sd.push(i-1);if(x<W_-1)sd.push(i+1);if(y>0)sd.push(i-W_);if(y<v.H-1)sd.push(i+W_);if(z>0)sd.push(i-WH_);if(z<v.D-1)sd.push(i+WH_)}
  edge(v,x+1,y,z);edge(v,x-1,y,z);edge(v,x,y+1,z);edge(v,x,y-1,z);edge(v,x,y,z+1);edge(v,x,y,z-1);
  bloody(v,x+1,y,z,70);bloody(v,x-1,y,z,70);bloody(v,x,y+1,z,50);bloody(v,x,y-1,z,90);bloody(v,x,y-2,z,60);bloody(v,x,y,z-1,90);
}
function edge(v,x,y,z){if(x<0||y<0||z<0||x>=v.W||y>=v.H||z>=v.D)return;var i=x+v.W*(y+v.H*z);if(v.dyn&&v.type[i]){v.fl[i]|=F_LOOSE;v.sw[i]=v.sw[i]>>1}}
function bloody(v,x,y,z,a){if(x<0||y<0||z<0||x>=v.W||y>=v.H||z>=v.D)return;var i=x+v.W*(y+v.H*z);if(!v.type[i])return;v.bl[i]=Math.min(255,v.bl[i]+a);v.mc(x,y,z)}
/* skin tears along tension lines: across the brow and cheekbones, up and down through the lips */
function tear(v,x,y,z,E,rg,out){
  var tx=1,ty=0,tz=0,ang;
  if(rg===R_MOUTH){tx=0.25;ty=1}else if(rg===R_CHEEK||rg===R_JAW){ang=rnd(-0.9,0.9);tx=Math.cos(ang);ty=Math.sin(ang)}
  else if(rg===R_SCALP||rg===R_EAR||rg>=20){ang=rnd(Math.PI);tx=Math.cos(ang);ty=Math.sin(ang)}
  else{ang=rnd(-0.25,0.25);tx=Math.cos(ang);ty=Math.sin(ang)}
  var i0=x+v.W*(y+v.H*z),nx=v.nx[i0]/127,ny=v.ny[i0]/127,nz=v.nz[i0]/127,c=tx*nx+ty*ny+tz*nz;tx-=c*nx;ty-=c*ny;tz-=c*nz;
  var l=Math.sqrt(tx*tx+ty*ty+tz*tz)||1;tx/=l;ty/=l;tz/=l;
  var steps=1+Math.round(E*0.35+Math.random()),sg,st,cx,cy,cz,best,bh,bx,by,bz,a,b,cc,xx,yy,zz,j,t;
  for(sg=-1;sg<=1;sg+=2){cx=x+0.5;cy=y+0.5;cz=z+0.5;
    for(st=0;st<steps;st++){cx+=tx*sg;cy+=ty*sg;cz+=tz*sg;best=-1;bh=1e9;
      for(cc=-1;cc<=1;cc++)for(b=-1;b<=1;b++)for(a=-1;a<=1;a++){xx=(cx|0)+a;yy=(cy|0)+b;zz=(cz|0)+cc;
        if(xx<0||yy<0||zz<0||xx>=v.W||yy>=v.H||zz>=v.D)continue;j=xx+v.W*(yy+v.H*zz);t=v.type[j];
        if(!SKL[t]||v.hp[j]>=HPT[t]*0.72||!v.exp(xx,yy,zz,j))continue;if(v.hp[j]<bh){bh=v.hp[j];best=j;bx=xx;by=yy;bz=zz}}
      if(best<0)break;
      removeCell(v,best,bx,by,bz,out);out.split++;cx=bx+0.5;cy=by+0.5;cz=bz+0.5;
    }}
}

/* ---------- bleeding: wounds spawn drips that run over the surface under gravity ---------- */
var WOUNDS=[],DRIPS=[];
function addWound(v,x,y,z,rate){
  for(var k=0;k<WOUNDS.length;k++){var w=WOUNDS[k];if(w.v===v&&Math.abs(w.x-x)+Math.abs(w.y-y)+Math.abs(w.z-z)<6){w.rate=Math.min(9,w.rate*Math.exp(-w.age/22)+rate*0.7);w.age=0;return}}
  if(WOUNDS.length>=44)WOUNDS.shift();
  WOUNDS.push({v:v,x:x,y:y,z:z,rate:rate,acc:Math.random(),age:0});
}
function startDrip(v,x,y,z,vol){if(DRIPS.length>=70)return;DRIPS.push({v:v,x:x,y:y,z:z,px:-1,py:-1,pz:-1,vol:vol,t:Math.random(),sp:v===HEADV?17:9})}
var _gL=new T.Vector3(),_gT=new T.Vector3(),_dq=new T.Quaternion(),_dw=new T.Vector3(),_dl=new T.Vector3(),_im=new T.Matrix4();
function gravLocal(v,out){v.group.getWorldQuaternion(_dq);out.set(0,-1,0).applyQuaternion(_dq.invert())}
function updBleed(dt){
  var k,w,r,d,i;
  for(k=WOUNDS.length-1;k>=0;k--){w=WOUNDS[k];w.age+=dt;r=w.rate*Math.exp(-w.age/22);
    if(r<0.05||!w.v.type[w.x+w.v.W*(w.y+w.v.H*w.z)]&&r<0.4){WOUNDS.splice(k,1);continue}
    w.acc+=r*dt;while(w.acc>=1){w.acc-=1;startDrip(w.v,w.x,w.y,w.z,10+((Math.random()*18)|0))}}
  if(!DRIPS.length)return;
  gravLocal(HEADV,_gL);gravLocal(TORSOV,_gT);
  for(k=DRIPS.length-1;k>=0;k--){d=DRIPS[k];d.t+=dt*d.sp;var alive=true;
    while(d.t>=1&&alive){d.t-=1;alive=stepDrip(d,d.v===HEADV?_gL:_gT)}
    if(!alive)DRIPS.splice(k,1)}
}
function stepDrip(d,g){
  var v=d.v,W=v.W,H=v.H,D=v.D,i=d.x+W*(d.y+H*d.z),a,b,c,xx,yy,zz,j,sc,best=0.12,bx=-1,by=0,bz=0;
  if(v.dead||!v.type[i])return false;
  v.bl[i]=Math.min(255,v.bl[i]+34+d.vol*5);v.mc(d.x,d.y,d.z);
  if(--d.vol<=0)return false;
  for(c=-1;c<=1;c++){zz=d.z+c;if(zz<0||zz>=D)continue;
    for(b=-1;b<=1;b++){yy=d.y+b;if(yy<0||yy>=H)continue;
      for(a=-1;a<=1;a++){if(!a&&!b&&!c)continue;xx=d.x+a;if(xx<0||xx>=W)continue;
        if(xx===d.px&&yy===d.py&&zz===d.pz)continue;
        j=xx+W*(yy+H*zz);if(!v.type[j]||!v.exp(xx,yy,zz,j))continue;
        sc=(a*g.x+b*g.y+c*g.z)/Math.sqrt(a*a+b*b+c*c)+Math.random()*0.32;
        if(sc>best){best=sc;bx=xx;by=yy;bz=zz}}}}
  if(bx<0){dropOff(d);return false}
  d.px=d.x;d.py=d.y;d.pz=d.z;d.x=bx;d.y=by;d.z=bz;return true;
}
function dropOff(d){
  var v=d.v,t=TORSOV,x,y,z,gx,gy,gz,j,k;
  _dw.set(v.X(d.x)*0.01,v.Y(d.y)*0.01,v.Z(d.z)*0.01).applyMatrix4(v.group.matrixWorld);
  if(v===HEADV&&F&&F.st!=='down'&&F.st!=='rise'){
    _dl.copy(_dw).applyMatrix4(_im.copy(t.group.matrixWorld).invert());
    gx=Math.floor((_dl.x*100-t.ox)/t.s);gy=Math.floor((_dl.y*100-t.oy)/t.s);gz=Math.floor((_dl.z*100-t.oz)/t.s);
    if(gx>=0&&gx<t.W&&gy>0){for(y=Math.min(t.H-1,gy);y>=0;y--){for(k=-3;k<=3;k++){z=gz+k;if(z<0||z>=t.D)continue;j=gx+t.W*(y+t.H*z);
      if(t.type[j]){for(z=t.D-1;z>=0;z--){j=gx+t.W*(y+t.H*z);if(t.type[j]){startDrip(t,gx,y,z,d.vol+10);return}}}}}}
  }
  blood(_dw.x,_dw.y,_dw.z,rnd(-.05,.05),-0.2,rnd(-.05,.05),rnd(.003,.005),3);
}

/* ---------- debris ---------- */
var _sv=new T.Vector3();
function spill(v,rem,power,FD,isHead){
  if(!rem.length)return;
  var bin=isHead?3:2,groups={},k,key,a,made=0,crumbs=0,m=v.group.matrixWorld;
  for(k=0;k<rem.length;k+=7){key=((rem[k]/bin)|0)+'_'+((rem[k+1]/bin)|0)+'_'+((rem[k+2]/bin)|0);(groups[key]||(groups[key]=[])).push(k)}
  for(key in groups){a=groups[key];
    if(power&&a.length>=6&&made<8){var pts=[],cols=[];
      for(var q=0;q<a.length&&pts.length<BV*3;q++){k=a[q];_sv.set(v.X(rem[k])*0.01,v.Y(rem[k+1])*0.01,v.Z(rem[k+2])*0.01).applyMatrix4(m);pts.push(_sv.x,_sv.y,_sv.z);cols.push(rem[k+3],rem[k+4],rem[k+5],WETT[rem[k+6]]*255)}
      clump(pts,cols,v.s*0.01,FD.x*rnd(.8,2.4)+rnd(-.8,.8),rnd(.6,2.6),FD.z*rnd(.2,1)+rnd(.3,1.6));made++;continue}
    for(var q2=0;q2<a.length;q2++){if(crumbs>60)break;k=a[q2];if(q2%2&&a.length>3)continue;crumbs++;
      _sv.set(v.X(rem[k])*0.01,v.Y(rem[k+1])*0.01,v.Z(rem[k+2])*0.01).applyMatrix4(m);
      spawn(_sv.x,_sv.y,_sv.z,rnd(-1.4,1.4)+FD.x*1.8,rnd(.4,2.6),rnd(.2,2)+FD.z*0.5,rem[k+3],rem[k+4],rem[k+5],WETT[rem[k+6]],v.s*0.0095,1,rnd(2.5,5))}
  }
}
/* whatever the blow cut loose: large parts become their own meshes, small ones crumble */
function checkIslands(v,FD){
  var isl=v.islandsFast(40000),k,c,q,i,W=v.W,H=v.H,WH=W*H,rem;
  for(k=0;k<isl.length;k++){c=isl[k];
    if(c.length>=60)pieceFrom(v,c,null,_sv.set(FD.x*1.6+rnd(-.4,.4),rnd(.6,1.6),FD.z*0.6+rnd(.2,.8)).clone(),new T.Vector3(rnd(-9,9),rnd(-9,9),rnd(-9,9)));
    else{rem=[];var o={rem:rem};for(q=0;q<c.length;q++){i=c[q];removeCell(v,i,i%W,((i/W)|0)%H,(i/WH)|0,o)}spill(v,rem,true,FD,v===HEADV)}
  }
}
function pieceFrom(v,cells,extraM,vel,ang){
  var W=v.W,H=v.H,WH=W*H,k,i,x,y,z,x0=1e9,y0=1e9,z0=1e9,x1=-1,y1=-1,z1=-1,cx=0,cy=0,cz=0;
  for(k=0;k<cells.length;k++){i=cells[k];x=i%W;y=((i/W)|0)%H;z=(i/WH)|0;if(x<x0)x0=x;if(x>x1)x1=x;if(y<y0)y0=y;if(y>y1)y1=y;if(z<z0)z0=z;if(z>z1)z1=z}
  var pw=x1-x0+1,ph=y1-y0+1,pd=z1-z0+1,p=new VBody(pw,ph,pd,v.s,v.ox+x0*v.s,v.oy+y0*v.s,v.oz+z0*v.s,true),j,q,nb=[1,-1,W,-W,WH,-WH];
  /* the tear: piece cells that were glued to cells staying on the body */
  var mk=v._m||(v._m=new Uint8Array(v.n)),tear=[];
  for(k=0;k<cells.length;k++)mk[cells[k]]=1;
  for(k=0;k<cells.length;k++){i=cells[k];for(q=0;q<6;q++){j=i+nb[q];if(j>=0&&j<v.n&&v.type[j]&&!mk[j]){tear.push(i,j);break}}}
  for(k=0;k<cells.length;k++){i=cells[k];x=i%W;y=((i/W)|0)%H;z=(i/WH)|0;j=(x-x0)+pw*((y-y0)+ph*(z-z0));
    p.type[j]=v.type[i];p.rgb[j*3]=v.rgb[i*3];p.rgb[j*3+1]=v.rgb[i*3+1];p.rgb[j*3+2]=v.rgb[i*3+2];p.fl[j]=v.fl[i]&~(F_JAWH|F_JAWL|F_ACT);
    p.hp[j]=v.hp[i];p.br[j]=v.br[i];p.mt[j]=v.mt[i];p.ab[j]=v.ab[i];p.bl[j]=v.bl[i];p.sw[j]=v.sw[i];p.dx[j]=v.dx[i];p.dy[j]=v.dy[i];p.dz[j]=v.dz[i];
    p.nx[j]=v.nx[i];p.ny[j]=v.ny[i];p.nz[j]=v.nz[i];p.rg[j]=v.rg[i];
    cx+=v.X(x);cy+=v.Y(y);cz+=v.Z(z);v.type[i]=0;v.mg(x,y,z)}
  p.anyDef=v.anyDef;p.seed=0;
  /* torn edge on the body and on the piece: raw and bloody */
  for(k=0;k<tear.length;k+=2){i=tear[k];x=i%W;y=((i/W)|0)%H;z=(i/WH)|0;j=(x-x0)+pw*((y-y0)+ph*(z-z0));p.bl[j]=255;if(SKL[p.type[j]])p.ab[j]=255;
    j=tear[k+1];(v.seeds||(v.seeds=[])).push(j);v.bl[j]=255;if(SKL[v.type[j]])v.ab[j]=255;v.fl[j]|=F_LOOSE;v.sw[j]=0;v.mc(j%W,((j/W)|0)%H,(j/WH)|0)}
  for(k=0;k<cells.length;k++)mk[cells[k]]=0;
  var n=cells.length,com=new T.Vector3(cx/n*0.01,cy/n*0.01,cz/n*0.01);
  p.initChunks();p.process(0,1e9,0);
  var M=new T.Matrix4().copy(v.group.matrixWorld);if(extraM)M.multiply(extraM);
  return addPiece(p,M,com,vel,ang);
}

/* ---------- eyes swell shut ---------- */
var EYES=[];
function eyesInit(v){
  EYES=[{e:[],lid:[],closed:false},{e:[],lid:[],closed:false}];var W=v.W,H=v.H,x,y,z,i,X,Y,Z,ax,r;
  for(z=0;z<v.D;z++){Z=v.Z(z);if(Z<EZ-0.4)continue;for(y=0;y<H;y++){Y=v.Y(y);if(Math.abs(Y-EY)>2.4)continue;i=W*(y+H*z);for(x=0;x<W;x++,i++){
    if(!v.type[i])continue;X=v.X(x);ax=X<0?-X:X;r=Math.sqrt((ax-EX)*(ax-EX)+(Y-EY)*(Y-EY)+(Z-EZ)*(Z-EZ));
    if(v.fl[i]&F_EYE)EYES[X<0?0:1].e.push(i);else if(SKL[v.type[i]]&&r>1.2&&r<2.4)EYES[X<0?0:1].lid.push(i)}}}
}
function eyeCheck(v){
  var k,q,s,n,E,closed=-1;
  for(k=0;k<2;k++){E=EYES[k];if(E.closed||!E.lid.length)continue;s=0;n=0;for(q=0;q<E.lid.length;q++){s+=v.sw[E.lid[q]];n++}
    if(s/n*0.01>0.36){closeEye(v,k);closed=k}}
  return closed;
}
function closeEye(v,k){
  var E=EYES[k],q,i,W=v.W,H=v.H,sk=F.look.skin;E.closed=true;
  for(q=0;q<E.e.length;q++){i=E.e[q];if(!v.type[i])continue;var x=i%W,y=((i/W)|0)%H,z=(i/(W*H))|0;
    v.type[i]=SKIN;v.fl[i]=(v.fl[i]&~F_EYE)|F_SKIN;v.rgb[i*3]=sk[0]*0.92;v.rgb[i*3+1]=sk[1]*0.86;v.rgb[i*3+2]=sk[2]*0.86;
    v.br[i]=235;v.mt[i]=60;v.hp[i]=HPT[SKIN];setNrm(v,x,y,z,i);v.sw[i]=Math.max(v.sw[i],70);v.rg[i]=R_EYE;v.vu[i]=RVU[R_EYE];v.sf[i]=RSF[R_EYE];track(v,i);v.mg(x,y,z)}
  v.anyDef=true;
}

/* ---------- fractures ---------- */
function ci(a){return a<-127?-127:a>127?127:Math.round(a)}
function shove(v,cx,cy,cz,r,fx,fy,fz,flt){
  var s=v.s,x0=Math.max(0,Math.floor((cx-r-v.ox)/s)),x1=Math.min(v.W-1,Math.ceil((cx+r-v.ox)/s)),y0=Math.max(0,Math.floor((cy-r-v.oy)/s)),y1=Math.min(v.H-1,Math.ceil((cy+r-v.oy)/s)),z0=Math.max(0,Math.floor((cz-r-v.oz)/s)),z1=Math.min(v.D-1,Math.ceil((cz+r-v.oz)/s));
  var x,y,z,i,X,Y,Z,d2,w,k=16/s;
  for(z=z0;z<=z1;z++){Z=v.Z(z);for(y=y0;y<=y1;y++){Y=v.Y(y);for(x=x0;x<=x1;x++){i=x+v.W*(y+v.H*z);if(!v.type[i])continue;X=v.X(x);
    d2=((X-cx)*(X-cx)+(Y-cy)*(Y-cy)+(Z-cz)*(Z-cz))/(r*r);if(d2>=1)continue;if(flt&&!flt(X,Y,Z))continue;
    w=(1-d2)*(1-d2);v.dx[i]=ci(v.dx[i]+fx*w*k);v.dy[i]=ci(v.dy[i]+fy*w*k);v.dz[i]=ci(v.dz[i]+fz*w*k);v.mg(x,y,z)}}}
  v.anyDef=true;
}
function bruiseArea(v,cx,cy,cz,r,amt,swc){
  var s=v.s,x0=Math.max(0,Math.floor((cx-r-v.ox)/s)),x1=Math.min(v.W-1,Math.ceil((cx+r-v.ox)/s)),y0=Math.max(0,Math.floor((cy-r-v.oy)/s)),y1=Math.min(v.H-1,Math.ceil((cy+r-v.oy)/s)),z0=Math.max(0,Math.floor((cz-r-v.oz)/s)),z1=Math.min(v.D-1,Math.ceil((cz+r-v.oz)/s));
  var x,y,z,i,X,Y,Z,d2,w,cap;
  for(z=z0;z<=z1;z++){Z=v.Z(z);for(y=y0;y<=y1;y++){Y=v.Y(y);for(x=x0;x<=x1;x++){i=x+v.W*(y+v.H*z);if(!SKL[v.type[i]])continue;X=v.X(x);
    d2=((X-cx)*(X-cx)+(Y-cy)*(Y-cy)+(Z-cz)*(Z-cz))/(r*r);if(d2>=1||!v.exp(x,y,z,i))continue;w=1-d2;
    v.br[i]=Math.min(255,v.br[i]+amt*w*255);v.mt[i]*=0.7;track(v,i);
    if(swc>0){setNrm(v,x,y,z,i);cap=RSC[v.rg[i]]*100;v.sw[i]=Math.min(cap,v.sw[i]+swc*w*v.sf[i]);v.anyDef=true}
    v.mg(x,y,z)}}}
}
/* a laceration on the front of the face: a short line of skin opens */
function cutAt(v,X,Y,len,ang,out,rate){
  var s=v.s,k,px,py,x,y,z,i,first=-1;
  for(k=-len;k<=len;k+=s*0.7){px=X+Math.cos(ang)*k;py=Y+Math.sin(ang)*k;x=Math.floor((px-v.ox)/s);y=Math.floor((py-v.oy)/s);if(x<0||y<0||x>=v.W||y>=v.H)continue;
    for(z=v.D-1;z>=0;z--){i=x+v.W*(y+v.H*z);if(v.type[i]){if(SKL[v.type[i]]){setNrm(v,x,y,z,i);removeCell(v,i,x,y,z,out);if(first<0)first=i;var j=x+v.W*(y+v.H*(z-1));if(z>0&&v.type[j]===FAT&&Math.random()<0.5)removeCell(v,j,x,y,z-1,out)}break}}}
  if(first>=0){x=first%v.W;y=((first/v.W)|0)%v.H;z=(first/(v.W*v.H))|0;while(z>0&&!v.type[x+v.W*(y+v.H*z)])z--;addWound(v,x,y,z,rate||3)}
}
