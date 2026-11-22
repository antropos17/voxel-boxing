
/* ---------- voxel bodies ----------
   Dense grid per body part. Geometry is rebuilt per 32-cell chunk and holds only the faces that touch air,
   with corner ambient occlusion. Colour-only changes (bruises maturing, blood running) rewrite the colour buffer in place. */
var SKIN=1,FAT=2,MUSC=3,BONE=4,CORE=5,TOOTH=6,EYE=7,IRIS=8,PUPIL=9,HAIR=10,BROW=11,LIP=12,MOUTH=13,TONGUE=14,GUM=15,CART=16,LASH=17,EYEIN=18;
var HPT=new Float32Array([0,8.5,8,12,38,18,30,14,14,14,5,8.5,7,9,10,8,14,9,8,1e9]);
var WETT=new Float32Array([0,.1,.5,.62,.3,.65,.5,.95,.95,.95,.04,.05,.32,.8,.8,.7,.42,.1,.7,0]);
var SKL=new Uint8Array(24);SKL[SKIN]=SKL[LIP]=SKL[BROW]=SKL[HAIR]=1;
var F_SKIN=1,F_EYE=2,F_JAWH=4,F_JAWL=8,F_CRACK=16,F_ACT=32,F_NRM=64,F_LOOSE=128;
var AOL=[132,178,220,255],AOS=[176,208,234,255],DIRS=[[1,0,0,0,1,0,0,0,1],[-1,0,0,0,0,1,0,1,0],[0,1,0,0,0,1,1,0,0],[0,-1,0,1,0,0,0,0,1],[0,0,1,1,0,0,0,1,0],[0,0,-1,0,1,0,1,0,0]];
var SQN=0,sP,sN,sC,sA,sQ,sF,GN=new Uint8Array(9),CC=new Float32Array(4),CD=new Float32Array(7);
var NCN=new Float32Array(65*65*65*3),NCS=new Int32Array(65*65*65),NCT=0,SN=new Float32Array(3);
var CDC=new Float32Array(33*33*33*7),CDS=new Int32Array(33*33*33),MESHT=0;
function scratch(n){if(n<=SQN)return;SQN=Math.max(n,SQN*2,16384);sP=new Float32Array(SQN*12);sN=new Int8Array(SQN*12);sC=new Uint8Array(SQN*16);sA=new Uint8Array(SQN*16);sQ=new Int32Array(SQN);sF=new Uint8Array(SQN);sD=new Uint8Array(SQN)}
var sD,OFF6=new Int32Array(6),CNT6=new Int32Array(6);
scratch(16384);
function upd(a,n){a.updateRange.offset=0;a.updateRange.count=n;a.needsUpdate=true}

function VBody(W,H,D,s,ox,oy,oz,dyn){
  this.W=W;this.H=H;this.D=D;this.s=s;this.ox=ox;this.oy=oy;this.oz=oz;this.dyn=dyn;this.cs=32;
  var n=this.n=W*H*D;
  this.type=new Uint8Array(n);this.rgb=new Uint8Array(n*3);this.fl=new Uint8Array(n);
  if(dyn){
    this.hp=new Float32Array(n);this.br=new Uint8Array(n);this.mt=new Uint8Array(n);this.ab=new Uint8Array(n);this.bl=new Uint8Array(n);this.sw=new Uint8Array(n);
    this.rg=new Uint8Array(n);this.vu=new Uint8Array(n);this.sf=new Uint8Array(n);
    this.dx=new Int8Array(n);this.dy=new Int8Array(n);this.dz=new Int8Array(n);this.nx=new Int8Array(n);this.ny=new Int8Array(n);this.nz=new Int8Array(n);
    this.mat=voxMat();this.hitT=[-1,-1,-1,-1];this.hitN=0;
  }else{this.wet=new Uint8Array(n);this.mat=STATMAT}
  this.group=new T.Group();this.chunks=[];this.anyDef=false;this.jawFn=null;this.dirty=false;this.seed=0;this.sm=0.6;this.aol=AOL;
}
VBody.prototype.X=function(x){return this.ox+(x+0.5)*this.s};
VBody.prototype.Y=function(y){return this.oy+(y+0.5)*this.s};
VBody.prototype.Z=function(z){return this.oz+(z+0.5)*this.s};
VBody.prototype.cell=function(Xc,Yc,Zc){var x=Math.floor((Xc-this.ox)/this.s),y=Math.floor((Yc-this.oy)/this.s),z=Math.floor((Zc-this.oz)/this.s);if(x<0||y<0||z<0||x>=this.W||y>=this.H||z>=this.D)return -1;return x+this.W*(y+this.H*z)};
VBody.prototype.exp=function(x,y,z,i){var W=this.W,t=this.type,WH=W*this.H;return x===0||y===0||z===0||x===W-1||y===this.H-1||z===this.D-1||!t[i-1]||!t[i+1]||!t[i-W]||!t[i+W]||!t[i-WH]||!t[i+WH]};
VBody.prototype.initChunks=function(){
  var cs=this.cs,nx=this.ncx=Math.ceil(this.W/cs),ny=this.ncy=Math.ceil(this.H/cs),nz=this.ncz=Math.ceil(this.D/cs),i,j,k;
  for(k=0;k<nz;k++)for(j=0;j<ny;j++)for(i=0;i<nx;i++)
    this.chunks.push({x0:i*cs,y0:j*cs,z0:k*cs,x1:Math.min(this.W,i*cs+cs),y1:Math.min(this.H,j*cs+cs),z1:Math.min(this.D,k*cs+cs),mesh:null,geo:null,cap:0,nq:0,qc:null,gd:true,cd:false,tc:0});
  this.dirty=true;
};
/* structural change at a cell: every chunk whose faces, occlusion or corner sampling can see it */
VBody.prototype.mg=function(x,y,z){
  var cs=this.cs,a=(x>0?x-1:0)/cs|0,b=(x<this.W-1?x+1:x)/cs|0,c=(y>0?y-1:0)/cs|0,d=(y<this.H-1?y+1:y)/cs|0,e=(z>0?z-1:0)/cs|0,f=(z<this.D-1?z+1:z)/cs|0,i,j,k;
  for(k=e;k<=f;k++)for(j=c;j<=d;j++)for(i=a;i<=b;i++)this.chunks[i+this.ncx*(j+this.ncy*k)].gd=true;
  this.dirty=true;
};
VBody.prototype.mc=function(x,y,z){var cs=this.cs;this.chunks[(x/cs|0)+this.ncx*((y/cs|0)+this.ncy*(z/cs|0))].cd=true;this.dirty=true};
VBody.prototype.allDirty=function(){for(var k=0;k<this.chunks.length;k++)this.chunks[k].gd=true;this.dirty=true};
VBody.prototype.allCol=function(){for(var k=0;k<this.chunks.length;k++)this.chunks[k].cd=true;this.dirty=true};

VBody.prototype.colorOf=function(i,o){
  var j=i*3,r=this.rgb[j],g=this.rgb[j+1],b=this.rgb[j+2];
  if(!this.dyn){o[0]=r;o[1]=g;o[2]=b;o[3]=this.wet[i];return}
  var t=this.type[i],w=WETT[t],k,m,B;
  if(t===BONE&&(this.fl[i]&F_CRACK)&&hash1(i)<0.4){r=92;g=76;b=62}
  if(SKL[t]){
    B=this.br[i];
    if(B){B/=255;m=this.mt[i]/255;
      k=B*(1-m)*0.78;r+=(192-r)*k;g+=(62-g)*k;b+=(60-b)*k;
      k=B*m*0.78;r+=(96-r)*k;g+=(44-g)*k;b+=(96-b)*k;
      if(B>0.68&&m>0.3){k=Math.min(0.7,(B-0.68)*(m-0.3)*3.4);r+=(52-r)*k;g+=(28-g)*k;b+=(58-b)*k}
    }
    B=this.ab[i];
    if(B){k=B/255*0.86;r+=(208-r)*k;g+=(86-g)*k;b+=(76-b)*k;w+=0.3*k;
      if(hash1(i*7+3)<k*0.3){r*=0.74;g*=0.36;b*=0.38}}
  }
  B=this.bl[i];
  if(B){B/=255;k=Math.min((t===BONE||t===TOOTH||t===EYE||t===IRIS||t===PUPIL)?0.3:0.93,B*1.25);
    m=B>0.72?110:150;r+=(m-r)*k;g+=(10-g)*k;b+=(13-b)*k;w+=(0.9-w)*Math.min(1,B*1.5)}
  o[0]=r>255?255:r;o[1]=g>255?255:g;o[2]=b>255?255:b;o[3]=w>=1?255:w*255;
};
/* deformation at a grid corner from the 8 cells that share it: fracture offsets (averaged) and swelling (max, along the surface normal) */
VBody.prototype.corner=function(X,Y,Z){
  var W=this.W,H=this.H,sw=this.sw,dx=this.dx,dy=this.dy,dz=this.dz,nx=this.nx,ny=this.ny,nz=this.nz;
  var cnt=0,ox=0,oy=0,oz=0,mx=0,ax=0,ay=0,az=0,xx,yy,zz,j,w,k=0,base=(X-1-this.cx0)+VPW*(Y-1-this.cy0)+VPWH*(Z-1-this.cz0);
  /* the 8 cells sharing the corner, in the same order as before; VPE is 0 outside the grid */
  for(zz=Z-1;zz<=Z;zz++)for(yy=Y-1;yy<=Y;yy++)for(xx=X-1;xx<=X;xx++,k++){
    if(!VPE[base+CRO[k]])continue;
    j=xx+W*(yy+H*zz);
    cnt++;ox+=dx[j];oy+=dy[j];oz+=dz[j];
    w=sw[j];if(w){if(w>mx)mx=w;ax+=nx[j]*w;ay+=ny[j]*w;az+=nz[j]*w}
  }
  var f=cnt?this.s/(16*cnt):0;CD[0]=ox*f;CD[1]=oy*f;CD[2]=oz*f;CD[3]=mx*0.01;
  var l=Math.sqrt(ax*ax+ay*ay+az*az)||1;CD[4]=ax/l;CD[5]=ay/l;CD[6]=az/l;
};
/* smooth outward normal at a grid corner from the occupancy of the surrounding 4x4x4 cells; blended into the face normal to soften terracing.
   Reads VPE, the padded copy of the chunk's cell types; the 64 weights (offset / distance^2) are a fixed table, summed in the same order as before */
var VPE=new Uint8Array(68*68*68),VPW=0,VPWH=0,SNX=new Float64Array(64),SNY=new Float64Array(64),SNZ=new Float64Array(64),SNO=new Int32Array(64),CRO=new Int32Array(8);
(function(){var a,b,cc,i=0,ox,oy,oz,w;for(cc=-2;cc<2;cc++){oz=cc+0.5;for(b=-2;b<2;b++){oy=b+0.5;for(a=-2;a<2;a++){ox=a+0.5;w=1/(ox*ox+oy*oy+oz*oz);SNX[i]=ox*w;SNY[i]=oy*w;SNZ[i]=oz*w;i++}}}})();
VBody.prototype.snorm=function(X,Y,Z,c){
  var cs=this.cs+1,key=(X-c.x0)+cs*((Y-c.y0)+cs*(Z-c.z0)),o=key*3;
  if(NCS[key]===NCT){SN[0]=NCN[o];SN[1]=NCN[o+1];SN[2]=NCN[o+2];return}
  var base=(X-c.x0)+VPW*(Y-c.y0)+VPWH*(Z-c.z0),gx=0,gy=0,gz=0,i;
  for(i=0;i<64;i++)if(!VPE[base+SNO[i]]){gx+=SNX[i];gy+=SNY[i];gz+=SNZ[i]}
  var l=Math.sqrt(gx*gx+gy*gy+gz*gz);if(l>1e-6){gx/=l;gy/=l;gz/=l}else{gx=gy=gz=0}
  NCN[o]=SN[0]=gx;NCN[o+1]=SN[1]=gy;NCN[o+2]=SN[2]=gz;NCS[key]=NCT;
};
/* VPE: the cell types of the chunk plus a 2-cell margin (everything the mesher looks at), 0 outside the grid */
VBody.prototype.padChunk=function(c){
  var W=this.W,H=this.H,D=this.D,type=this.type,x0=c.x0-2,y0=c.y0-2,z0=c.z0-2,PW=c.x1-c.x0+4,PH=c.y1-c.y0+4,PD=c.z1-c.z0+4,p=0,x,y,z,i,xa,xb,xe=x0+PW;
  xa=Math.max(0,x0);xb=Math.min(W,xe);
  for(z=z0;z<z0+PD;z++)for(y=y0;y<y0+PH;y++){
    if(z<0||y<0||z>=D||y>=H){VPE.fill(0,p,p+PW);p+=PW;continue}
    i=W*(y+H*z);for(x=x0;x<xa;x++)VPE[p++]=0;for(;x<xb;x++)VPE[p++]=type[i+x];for(;x<xe;x++)VPE[p++]=0}
  VPW=PW;VPWH=PW*PH;
  var a,b,cc,k=0;for(cc=0;cc<4;cc++)for(b=0;b<4;b++)for(a=0;a<4;a++)SNO[k++]=a+PW*b+VPWH*cc;
  k=0;for(cc=0;cc<2;cc++)for(b=0;b<2;b++)for(a=0;a<2;a++)CRO[k++]=a+PW*b+VPWH*cc;
};
VBody.prototype.meshCh=function(c){
  var W=this.W,H=this.H,D=this.D,type=this.type,fl=this.fl,s=this.s,OX=this.ox,OY=this.oy,OZ=this.oz,dyn=this.dyn,def=this.anyDef&&dyn,jf=this.jawFn;
  var aol=this.aol,q=0,x,y,z,i,t,d,dd,nX,nY,nZ,uX,uY,uZ,vX,vY,vZ,fx,fy,fz,fi,k,a,b,du,dv,gx,gy,gz,bX,bY,bZ,X,Y,Z,o,o4,fli,hc,s1,s2,s3,ao,a0=0,a1=0,a2=0,a3=0,px,py,pz,mx,my,mz,ml,jw,g=GN,eyef=0,skf=0,jhard=0,sm=this.sm,k2;
  NCT++;this.padChunk(c);this.cx0=c.x0-2;this.cy0=c.y0-2;this.cz0=c.z0-2;
  var PW=VPW,PWH=VPWH,p,fp,ou,ov,hinge;
  for(z=c.z0;z<c.z1;z++)for(y=c.y0;y<c.y1;y++){
    i=c.x0+W*(y+H*z);p=2+PW*(y-c.y0+2)+PWH*(z-c.z0+2);
    for(x=c.x0;x<c.x1;x++,i++,p++){
      t=type[i];if(!t)continue;
      fli=fl[i];hinge=dyn&&(fli&F_JAWH);
      /* buried cell: all six neighbours solid, no faces (a jaw hinge cell can still show a face between upper and lower jaw) */
      if(!hinge&&VPE[p-1]&&VPE[p+1]&&VPE[p-PW]&&VPE[p+PW]&&VPE[p-PWH]&&VPE[p+PWH])continue;
      hc=false;
      for(d=0;d<6;d++){
        dd=DIRS[d];nX=dd[0];nY=dd[1];nZ=dd[2];fx=x+nX;fy=y+nY;fz=z+nZ;fp=p+nX+PW*nY+PWH*nZ;
        if(VPE[fp]){if(!hinge)continue;fi=fx+W*(fy+H*fz);if(!((fl[fi]&F_JAWH)&&((fli^fl[fi])&F_JAWL)))continue}
        if(q>=SQN)scratch(q+1);
        if(!hc){this.colorOf(i,CC);hc=true;
          if(dyn){eyef=(fli&F_EYE)?255:t===GUM?128:0;skf=(fli&F_SKIN)?255:0;jhard=(fli&F_JAWH)?((fli&F_JAWL)?255:0):-1}}
        uX=dd[3];uY=dd[4];uZ=dd[5];vX=dd[6];vY=dd[7];vZ=dd[8];
        ou=uX+PW*uY+PWH*uZ;ov=vX+PW*vY+PWH*vZ;
        for(dv=-1;dv<=1;dv++)for(du=-1;du<=1;du++){
          if(!du&&!dv)continue;
          g[(du+1)+(dv+1)*3]=VPE[fp+du*ou+dv*ov]?1:0;
        }
        bX=x+(nX>0?1:0);bY=y+(nY>0?1:0);bZ=z+(nZ>0?1:0);
        o=q*12;o4=q*16;
        for(k=0;k<4;k++){
          a=(k===1||k===2)?1:0;b=k>=2?1:0;
          s1=g[(a?2:0)+3];s2=g[1+(b?6:0)];s3=g[(a?2:0)+(b?6:0)];
          ao=(s1&&s2)?0:3-s1-s2-s3;
          if(k===0)a0=ao;else if(k===1)a1=ao;else if(k===2)a2=ao;else a3=ao;
          X=bX+a*uX+b*vX;Y=bY+a*uY+b*vY;Z=bZ+a*uZ+b*vZ;
          px=OX+X*s;py=OY+Y*s;pz=OZ+Z*s;mx=nX;my=nY;mz=nZ;
          if(sm>0){this.snorm(X,Y,Z,c);if(SN[0]||SN[1]||SN[2]){mx=nX*(1-sm)+SN[0]*sm;my=nY*(1-sm)+SN[1]*sm;mz=nZ*(1-sm)+SN[2]*sm;ml=Math.sqrt(mx*mx+my*my+mz*mz)||1;mx/=ml;my/=ml;mz/=ml}}
          if(def){k2=(X-c.x0)+33*((Y-c.y0)+33*(Z-c.z0));if(CDS[k2]===NCT){k2*=7;CD[0]=CDC[k2];CD[1]=CDC[k2+1];CD[2]=CDC[k2+2];CD[3]=CDC[k2+3];CD[4]=CDC[k2+4];CD[5]=CDC[k2+5];CD[6]=CDC[k2+6]}
            else{this.corner(X,Y,Z);CDS[k2]=NCT;k2*=7;CDC[k2]=CD[0];CDC[k2+1]=CD[1];CDC[k2+2]=CD[2];CDC[k2+3]=CD[3];CDC[k2+4]=CD[4];CDC[k2+5]=CD[5];CDC[k2+6]=CD[6]}
            px+=CD[0];py+=CD[1];pz+=CD[2];
            if(CD[3]>0){px+=CD[4]*CD[3];py+=CD[5]*CD[3];pz+=CD[6]*CD[3];ml=Math.min(1.4,CD[3]*3);mx+=CD[4]*ml;my+=CD[5]*ml;mz+=CD[6]*ml;ml=Math.sqrt(mx*mx+my*my+mz*mz)||1;mx/=ml;my/=ml;mz/=ml}}
          sP[o+k*3]=px*0.01;sP[o+k*3+1]=py*0.01;sP[o+k*3+2]=pz*0.01;
          sN[o+k*3]=mx*127;sN[o+k*3+1]=my*127;sN[o+k*3+2]=mz*127;
          jw=jhard>=0?jhard:(jf?jf(OX+X*s,OY+Y*s,OZ+Z*s)*255:0);
          sC[o4+k*4]=CC[0];sC[o4+k*4+1]=CC[1];sC[o4+k*4+2]=CC[2];sC[o4+k*4+3]=CC[3];
          sA[o4+k*4]=aol[ao];sA[o4+k*4+1]=jw;sA[o4+k*4+2]=eyef;sA[o4+k*4+3]=skf;
        }
        sQ[q]=i;sF[q]=(a0+a2>a1+a3)?1:0;sD[q]=d;q++;
      }
    }
  }
  this.upload(c,q);
};
VBody.prototype.upload=function(c,nq){
  if(!c.geo||c.cap<nq){
    var cap=Math.max(64,Math.ceil(nq*1.3)),geo=new T.BufferGeometry();
    var aP=new T.BufferAttribute(new Float32Array(cap*12),3),aN=new T.BufferAttribute(new Int8Array(cap*12),3,true),aC=new T.BufferAttribute(new Uint8Array(cap*16),4,true),aA=new T.BufferAttribute(new Uint8Array(cap*16),4,true);
    var ia=new T.BufferAttribute(cap*4>65535?new Uint32Array(cap*6):new Uint16Array(cap*6),1);
    [aP,aN,aC,aA,ia].forEach(function(a){a.setUsage(T.DynamicDrawUsage)});
    geo.setAttribute('position',aP);geo.setAttribute('normal',aN);geo.setAttribute('aCol',aC);geo.setAttribute('aAux',aA);geo.setIndex(ia);
    geo.boundingSphere=new T.Sphere(new T.Vector3(),100);
    if(c.mesh){c.geo.dispose();c.mesh.geometry=geo}else{c.mesh=new T.Mesh(geo,this.mat);c.mesh.frustumCulled=false;c.mesh.matrixAutoUpdate=false;this.group.add(c.mesh)}
    c.geo=geo;c.cap=cap;c.qc=new Int32Array(cap);
  }
  var at=c.geo.attributes,I=c.geo.index.array,q,b,o;
  at.position.array.set(sP.subarray(0,nq*12));at.normal.array.set(sN.subarray(0,nq*12));at.aCol.array.set(sC.subarray(0,nq*16));at.aAux.array.set(sA.subarray(0,nq*16));
  /* indices are grouped by face direction (least likely to face the camera first), so whole directions can be left out of a draw */
  var ord=c.ord||(c.ord=this.dirOrder(c)),gs=c.gs||(c.gs=new Uint32Array(7)),acc=0,k;
  CNT6.fill(0);for(q=0;q<nq;q++)CNT6[sD[q]]++;
  for(k=0;k<6;k++){gs[k]=acc*6;OFF6[ord[k]]=acc;acc+=CNT6[ord[k]]}gs[6]=acc*6;
  for(q=0;q<nq;q++){b=q*4;o=(OFF6[sD[q]]++)*6;
    if(sF[q]){I[o]=b+1;I[o+1]=b+2;I[o+2]=b+3;I[o+3]=b+1;I[o+4]=b+3;I[o+5]=b}
    else{I[o]=b;I[o+1]=b+1;I[o+2]=b+2;I[o+3]=b;I[o+4]=b+2;I[o+5]=b+3}}
  upd(at.position,nq*12);upd(at.normal,nq*12);upd(at.aCol,nq*16);upd(at.aAux,nq*16);upd(c.geo.index,nq*6);
  /* static parts get a real bounding sphere so whatever is off screen (his legs, most of the time) is skipped */
  if(!this.dyn&&!this.noCull&&nq){var P=at.position.array,n=nq*12,j,x0=1e9,y0=1e9,z0=1e9,x1=-1e9,y1=-1e9,z1=-1e9;
    for(j=0;j<n;j+=3){if(P[j]<x0)x0=P[j];if(P[j]>x1)x1=P[j];if(P[j+1]<y0)y0=P[j+1];if(P[j+1]>y1)y1=P[j+1];if(P[j+2]<z0)z0=P[j+2];if(P[j+2]>z1)z1=P[j+2]}
    c.geo.boundingSphere.center.set((x0+x1)/2,(y0+y1)/2,(z0+z1)/2);c.geo.boundingSphere.radius=Math.sqrt((x1-x0)*(x1-x0)+(y1-y0)*(y1-y0)+(z1-z0)*(z1-z0))/2+.02;c.mesh.frustumCulled=true;
    c.bb=[x0,y0,z0,x1,y1,z1]}
  if(!c.bb){var pd=this.pad||.03,s_=this.s*.01;c.bb=[this.ox*.01+c.x0*s_-pd,this.oy*.01+c.y0*s_-pd,this.oz*.01+c.z0*s_-pd,this.ox*.01+c.x1*s_+pd,this.oy*.01+c.y1*s_+pd,this.oz*.01+c.z1*s_+pd]}
  if(c.soft===undefined)c.soft=this.softFn?this.softFn(c.bb):false;
  c.geo.setDrawRange(0,nq*6);c.qc.set(sQ.subarray(0,nq));c.nq=nq;c.mesh.visible=nq>0;
};
/* order of the six face directions inside a chunk's index buffer: by how likely they are to be seen from where the camera usually is */
VBody.prototype.dirOrder=function(c){
  var p=this.prior||[0,.3,1],px=p[0],k,o=[0,1,2,3,4,5],w=[];
  if(this.sidePrior){var cx=this.ox+(c.x0+c.x1)*.5*this.s;px=cx>0?-this.sidePrior:this.sidePrior}
  for(k=0;k<6;k++)w[k]=DIRS[k][0]*px+DIRS[k][1]*p[1]+DIRS[k][2]*p[2];
  o.sort(function(a,b){return w[a]-w[b]});return o;
};
/* leave out the face directions that cannot face the camera right now (tol = sine of how far deformation may turn a face) */
var _ci=new T.Matrix4(),_cl=new T.Vector3(),_ca=[0,0,0];
/* swap in the shader variant with the hit loop only while a dent is alive */
VBody.prototype.setHits=function(on){var m=on?this.matH:this.mat0;if(!m||this.mat===m)return;this.mat=m;for(var k=0;k<this.chunks.length;k++)if(this.chunks[k].mesh)this.chunks[k].mesh.material=m};
VBody.prototype.hitsLive=function(now){for(var k=0;k<4;k++)if(this.hitT[k]>=0&&now-this.hitT[k]<1.65)return true;return false};
VBody.prototype.cull=function(cam,tol,tolSoft){
  var k,c,bb,dx,dy,dz,lim,k0,k1,o,g,d,a,inv;
  _ci.copy(this.group.matrixWorld).invert();_cl.copy(cam).applyMatrix4(_ci);_ca[0]=_cl.x;_ca[1]=_cl.y;_ca[2]=_cl.z;
  for(k=0;k<this.chunks.length;k++){c=this.chunks[k];if(!c.nq||!c.bb)continue;bb=c.bb;o=c.ord;g=c.gs;
    dx=_cl.x-(bb[0]+bb[3])*.5;dy=_cl.y-(bb[1]+bb[4])*.5;dz=_cl.z-(bb[2]+bb[5])*.5;
    lim=-(c.soft?tolSoft:tol)*(Math.sqrt(dx*dx+dy*dy+dz*dz)+.5*Math.sqrt((bb[3]-bb[0])*(bb[3]-bb[0])+(bb[4]-bb[1])*(bb[4]-bb[1])+(bb[5]-bb[2])*(bb[5]-bb[2])));
    for(k0=0;k0<6;k0++){d=o[k0];a=d>>1;if(!((d&1)?(bb[a+3]-_ca[a]<lim):(_ca[a]-bb[a]<lim)))break}
    if(k0>5){c.geo.setDrawRange(0,0);continue}
    for(k1=5;k1>k0;k1--){d=o[k1];a=d>>1;if(!((d&1)?(bb[a+3]-_ca[a]<lim):(_ca[a]-bb[a]<lim)))break}
    c.geo.setDrawRange(g[k0],g[k1+1]-g[k0])}
};
/* loose parts near the cells removed since the last check: a flood from each seed that stops as soon as it is clearly the main body */
VBody.prototype.islandsFast=function(cap){
  var sd=this.seeds;this.islN=(this.islN||0)+1;
  if(this.islFull||!sd||this.islN%24===0){this.islFull=false;if(sd)sd.length=0;return this.islands()}
  if(!sd.length)return [];
  var n=this.n,W=this.W,H=this.H,D=this.D,WH=W*H,type=this.type,st=this._st||(this._st=new Int32Array(n)),q=this._q||(this._q=new Int32Array(n)),out=[],k,s,h,t,i,j,x,y,z,id,m,main,nb,run=(this._sid||0)+1;
  for(k=0;k<sd.length;k++){s=sd[k];if(!type[s]||st[s]>=run)continue;
    id=this._sid=(this._sid||0)+1;h=0;t=0;q[t++]=s;st[s]=id;main=false;
    while(h<t&&!main){i=q[h++];if(t>cap){main=true;break}x=i%W;y=(i/W|0)%H;z=i/WH|0;
      for(nb=0;nb<6;nb++){
        if(nb===0){if(x===0)continue;j=i-1}else if(nb===1){if(x===W-1)continue;j=i+1}else if(nb===2){if(y===0)continue;j=i-W}else if(nb===3){if(y===H-1)continue;j=i+W}else if(nb===4){if(z===0)continue;j=i-WH}else{if(z===D-1)continue;j=i+WH}
        if(!type[j])continue;m=st[j];if(m===id)continue;if(m>=run){main=true;break}st[j]=id;q[t++]=j}}
    if(!main)out.push(Array.prototype.slice.call(q,0,t))}
  sd.length=0;return out;
};
VBody.prototype.recol=function(c){
  if(!c.geo||!c.nq)return;
  var A=c.geo.attributes.aCol,C=A.array,qc=c.qc,q,o,k;
  for(q=0;q<c.nq;q++){this.colorOf(qc[q],CC);o=q*16;for(k=0;k<16;k+=4){C[o+k]=CC[0];C[o+k+1]=CC[1];C[o+k+2]=CC[2];C[o+k+3]=CC[3]}}
  upd(A,c.nq*16);
};
/* geometry rebuilds share a per-frame time budget (ms): a hit that dirties many chunks spreads over a few frames instead of one long one */
var MESHB=5;
VBody.prototype.process=function(now,gb,cb){
  if(!this.dirty)return;
  var left=false,k,c;
  for(k=0;k<this.chunks.length;k++){c=this.chunks[k];
    if(c.gd){if(gb>0&&(now===0||MESHT<MESHB)){var t0=performance.now();this.meshCh(c);MESHT+=performance.now()-t0;c.gd=false;c.cd=false;c.tc=now;gb--}else left=true}
    else if(c.cd){if(cb>0&&(now===0||now-c.tc>0.08)){this.recol(c);c.cd=false;c.tc=now;cb--}else left=true}
  }
  this.dirty=left;
};
VBody.prototype.fill=function(fn){
  var W=this.W,H=this.H,D=this.D,s=this.s,B=4,rb=B*s*0.87*1.6+0.2,x,y,z,bx,by,bz,i,ex,ey,ez,dc,full;
  for(bz=0;bz<D;bz+=B)for(by=0;by<H;by+=B)for(bx=0;bx<W;bx+=B){
    ex=Math.min(W,bx+B);ey=Math.min(H,by+B);ez=Math.min(D,bz+B);
    dc=fn(this.ox+(bx+ex)*0.5*s,this.oy+(by+ey)*0.5*s,this.oz+(bz+ez)*0.5*s);
    if(dc>rb)continue;full=dc<-rb;
    for(z=bz;z<ez;z++)for(y=by;y<ey;y++){i=bx+W*(y+H*z);
      for(x=bx;x<ex;x++,i++)if(full||fn(this.ox+(x+.5)*s,this.oy+(y+.5)*s,this.oz+(z+.5)*s)<=0)this.type[i]=1}
  }
};
/* ray in body-local metres; returns the first solid cell */
VBody.prototype.raycast=function(o,d){
  var W=this.W,H=this.H,D=this.D,s=this.s,px=(o.x*100-this.ox)/s,py=(o.y*100-this.oy)/s,pz=(o.z*100-this.oz)/s,dx=d.x,dy=d.y,dz=d.z,t0=0,t1=1e9,a,b,tt,k;
  var P=[px,py,pz],DD=[dx,dy,dz],S=[W,H,D];
  for(k=0;k<3;k++){if(Math.abs(DD[k])<1e-9){if(P[k]<0||P[k]>S[k])return null}else{a=-P[k]/DD[k];b=(S[k]-P[k])/DD[k];if(a>b){tt=a;a=b;b=tt}if(a>t0)t0=a;if(b<t1)t1=b;if(t0>t1)return null}}
  var t=t0+1e-4,x=Math.floor(px+dx*t),y=Math.floor(py+dy*t),z=Math.floor(pz+dz*t),sx=dx>0?1:-1,sy=dy>0?1:-1,sz=dz>0?1:-1;
  var tdx=dx?Math.abs(1/dx):1e9,tdy=dy?Math.abs(1/dy):1e9,tdz=dz?Math.abs(1/dz):1e9;
  var tmx=dx?((dx>0?x+1:x)-px)/dx:1e9,tmy=dy?((dy>0?y+1:y)-py)/dy:1e9,tmz=dz?((dz>0?z+1:z)-pz)/dz:1e9;
  for(var n=0;n<W+H+D+8;n++){
    if(x>=0&&y>=0&&z>=0&&x<W&&y<H&&z<D){var i=x+W*(y+H*z);if(this.type[i])return{x:x,y:y,z:z,i:i,t:t*s*0.01}}
    if(tmx<tmy&&tmx<tmz){x+=sx;t=tmx;tmx+=tdx}else if(tmy<tmz){y+=sy;t=tmy;tmy+=tdy}else{z+=sz;t=tmz;tmz+=tdz}
    if(t>t1+1e-3)break;
  }
  return null;
};
/* pieces no longer attached to the core */
VBody.prototype.islands=function(){
  var n=this.n,W=this.W,H=this.H,D=this.D,WH=W*H,type=this.type,vis=this._v||(this._v=new Uint8Array(n)),q=this._q||(this._q=new Int32Array(n)),out=[];
  vis.fill(0);
  var h=0,t=0,i,j,x,y,z,s0,big=-1,bn=0;
  function grow(){
    while(h<t){i=q[h++];x=i%W;y=(i/W|0)%H;z=i/WH|0;
      if(x>0&&type[i-1]&&!vis[i-1]){vis[i-1]=1;q[t++]=i-1}
      if(x<W-1&&type[i+1]&&!vis[i+1]){vis[i+1]=1;q[t++]=i+1}
      if(y>0&&type[i-W]&&!vis[i-W]){vis[i-W]=1;q[t++]=i-W}
      if(y<H-1&&type[i+W]&&!vis[i+W]){vis[i+W]=1;q[t++]=i+W}
      if(z>0&&type[i-WH]&&!vis[i-WH]){vis[i-WH]=1;q[t++]=i-WH}
      if(z<D-1&&type[i+WH]&&!vis[i+WH]){vis[i+WH]=1;q[t++]=i+WH}}
  }
  /* the biggest connected part is the body; everything else has come loose */
  for(j=0;j<n;j++){if(!type[j]||vis[j])continue;s0=t;q[t++]=j;vis[j]=1;h=s0;grow();if(t-s0>bn){bn=t-s0;big=out.length}out.push([s0,t])}
  var res=[];for(j=0;j<out.length;j++)if(j!==big)res.push(Array.prototype.slice.call(q,out[j][0],out[j][1]));
  return res;
};
VBody.prototype.addHit=function(pm,dm,amp,sig,rip,rr,now){
  var k=this.hitN++%4,u=this.mat.uniforms;
  u.uHitP.value[k].set(pm.x,pm.y,pm.z,0);u.uHitD.value[k].set(dm.x,dm.y,dm.z,amp);u.uHitR.value[k].set(sig,rip,rr,160);this.hitT[k]=now;
};
VBody.prototype.tickHits=function(now){var u=this.mat.uniforms.uHitP.value,k;for(k=0;k<4;k++)u[k].w=this.hitT[k]<0?-1:now-this.hitT[k]};
VBody.prototype.clearHits=function(){for(var k=0;k<4;k++){this.hitT[k]=-1;this.mat.uniforms.uHitP.value[k].w=-1}};
VBody.prototype.dispose=function(){for(var k=0;k<this.chunks.length;k++)if(this.chunks[k].geo)this.chunks[k].geo.dispose();if(this.dyn)this.mat.dispose()};

/* chamfer 3-4-5 distance to the nearest empty cell (out of bounds counts as empty) */
function chamfer(type,W,H,D){
  var n=W*H*D,WH=W*H,dist=new Uint16Array(n),Fw=[],Bw=[],dx,dy,dz,x,y,z,i,k,m,v,xx,yy,zz;
  for(dz=-1;dz<=1;dz++)for(dy=-1;dy<=1;dy++)for(dx=-1;dx<=1;dx++){if(!dx&&!dy&&!dz)continue;var w=[3,4,5][Math.abs(dx)+Math.abs(dy)+Math.abs(dz)-1];
    if(dz<0||(dz===0&&dy<0)||(dz===0&&dy===0&&dx<0))Fw.push(dx,dy,dz,w);else Bw.push(dx,dy,dz,w)}
  for(i=0;i<n;i++)dist[i]=type[i]?60000:0;
  for(z=0;z<D;z++)for(y=0;y<H;y++){i=W*(y+H*z);for(x=0;x<W;x++,i++){if(!dist[i])continue;m=dist[i];
    for(k=0;k<Fw.length;k+=4){xx=x+Fw[k];yy=y+Fw[k+1];zz=z+Fw[k+2];v=(xx<0||yy<0||zz<0||xx>=W||yy>=H||zz>=D)?0:dist[xx+W*(yy+H*zz)];v+=Fw[k+3];if(v<m)m=v}dist[i]=m}}
  for(z=D-1;z>=0;z--)for(y=H-1;y>=0;y--){i=W*(y+H*z)+W-1;for(x=W-1;x>=0;x--,i--){if(!dist[i])continue;m=dist[i];
    for(k=0;k<Bw.length;k+=4){xx=x+Bw[k];yy=y+Bw[k+1];zz=z+Bw[k+2];v=(xx<0||yy<0||zz<0||xx>=W||yy>=H||zz>=D)?0:dist[xx+W*(yy+H*zz)];v+=Bw[k+3];if(v<m)m=v}dist[i]=m}}
  return dist;
}
