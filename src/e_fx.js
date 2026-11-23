
/* ---------- ring ---------- */
var RING=5,RCZ=-0.5,ZMIN=RCZ-RING/2,FPX=512;
var fc=document.createElement('canvas');fc.width=fc.height=FPX;var fx=fc.getContext('2d');
function drawFloor(){
  fx.fillStyle='#2b6680';fx.fillRect(0,0,FPX,FPX);
  for(var y=0;y<FPX;y+=4)for(var x=0;x<FPX;x+=4){var n=hash3(x,y,7);fx.fillStyle=n>.5?'rgba(255,255,255,'+((n-.5)*.07)+')':'rgba(0,0,0,'+((.5-n)*.12)+')';fx.fillRect(x,y,4,4)}
  fx.strokeStyle='rgba(245,239,226,.3)';fx.lineWidth=6;fx.strokeRect(26,26,FPX-52,FPX-52);
  fx.beginPath();fx.arc(FPX/2,FPX/2,88,0,Math.PI*2);fx.stroke();
  fx.fillStyle='rgba(245,239,226,.24)';fx.font='bold 40px monospace';fx.textAlign='center';fx.fillText('VOXEL BOX',FPX/2,FPX/2+14);
}
drawFloor();
var floorTex=new T.CanvasTexture(fc);floorTex.magFilter=T.NearestFilter;floorTex.minFilter=T.LinearFilter;floorTex.generateMipmaps=false;
var stainDirty=false,stainT=0;
function stain(x,z,size){
  var u=(x+RING/2)/RING*FPX,v=(z-ZMIN)/RING*FPX;if(u<2||v<2||u>FPX-3||v>FPX-3)return;
  var k=Math.max(1,Math.round(size*FPX/RING*1.6));
  fx.fillStyle=Math.random()<.5?'#7a0e0e':(Math.random()<.5?'#931212':'#600a0a');
  fx.fillRect((u-k/2)|0,(v-k/2)|0,k,k);
  if(k>1&&Math.random()<.5)fx.fillRect(((u-k/2)|0)+(Math.random()<.5?k:-1),(v|0),1,1);
  stainDirty=true;
}
function pool(x,z,r){var u=(x+RING/2)/RING*FPX,v=(z-ZMIN)/RING*FPX,R=r*FPX/RING;fx.fillStyle='rgba(96,8,9,0.55)';
  for(var k=0;k<6;k++){var a=rnd(Math.PI*2),d=rnd(R*0.7);fx.beginPath();fx.arc(u+Math.cos(a)*d,v+Math.sin(a)*d,R*rnd(0.35,0.7),0,Math.PI*2);fx.fill()}stainDirty=true}
(function(){
  var fl=new T.Mesh(new T.PlaneGeometry(RING,RING),new T.MeshLambertMaterial({map:floorTex}));fl.rotation.x=-Math.PI/2;fl.position.set(0,0,RCZ);scene.add(fl);
  var B=[],h=RING/2,pc=[0xc8231c,0x3b82f0,0xece2c8,0xece2c8],i=0,k;
  B.push([0,-0.31,RCZ,RING+0.5,0.6,RING+0.5,0x1c1622]);
  [[-h,-h],[h,-h],[-h,h],[h,h]].forEach(function(p){B.push([p[0],0.77,RCZ+p[1],0.13,1.55,0.13,0x2a2430]);B.push([p[0]-Math.sign(p[0])*0.09,0.92,RCZ+p[1]-Math.sign(p[1])*0.09,0.24,1.0,0.24,pc[i++]])});
  var rc=[0x3b82f0,0xece2c8,0xc8231c,0xece2c8],hs=[0.5,0.78,1.06,1.34];
  for(k=0;k<4;k++){B.push([0,hs[k],RCZ-h,RING,0.038,0.038,rc[k]]);B.push([0,hs[k],RCZ+h,RING,0.038,0.038,rc[k]]);B.push([-h,hs[k],RCZ,0.038,0.038,RING,rc[k]]);B.push([h,hs[k],RCZ,0.038,0.038,RING,rc[k]])}
  B.push([0,4.5,RCZ-1.4,3.8,0.1,0.1,0x2a2430]);
  scene.add(new T.Mesh(boxesGeo(B),new T.MeshLambertMaterial({vertexColors:true})));
  var L=[];for(k=0;k<5;k++)L.push([-1.6+k*0.8,4.4,RCZ-1.4,0.34,0.08,0.34,0xfff0c0]);
  scene.add(new T.Mesh(boxesGeo(L),new T.MeshBasicMaterial({vertexColors:true,fog:false})));
  var N=720,cm=new T.InstancedMesh(new T.BoxGeometry(1,1,1),new T.MeshLambertMaterial(),N*2),M=new T.Matrix4(),q=new T.Quaternion(),p=new T.Vector3(),s=new T.Vector3(),c=col(0);
  for(var j=0;j<N;j++){var a=rnd(Math.PI*2),r=rnd(4.3,12.5),bh=rnd(.42,.56);
    p.set(Math.cos(a)*r,.1+(r-4)*.62+rnd(.2),RCZ+Math.sin(a)*r);if(p.z>1.6)p.z=-p.z+2*RCZ;
    s.set(.36,bh,.3);M.compose(p,q,s);cm.setMatrixAt(j*2,M);c.setHSL(rnd(1),.4,rnd(.13,.3));cm.setColorAt(j*2,c);
    p.y+=bh/2+.11;s.set(.2,.22,.2);M.compose(p,q,s);cm.setMatrixAt(j*2+1,M);c.setHSL(rnd(.04,.09),.4,rnd(.16,.3));cm.setColorAt(j*2+1,c)}
  cm.frustumCulled=false;scene.add(cm);
})();
function boxesGeo(list){
  var bg=new T.BoxGeometry(1,1,1),bp=bg.attributes.position.array,bn=bg.attributes.normal.array,bi=bg.index.array,n=list.length;
  var P=new Float32Array(n*72),N=new Float32Array(n*72),C=new Float32Array(n*72),I=new Uint16Array(n*36),k,j,l,c=col(0);
  for(k=0;k<n;k++){l=list[k];c.setHex(l[6]);
    for(j=0;j<24;j++){P[k*72+j*3]=bp[j*3]*l[3]+l[0];P[k*72+j*3+1]=bp[j*3+1]*l[4]+l[1];P[k*72+j*3+2]=bp[j*3+2]*l[5]+l[2];
      N[k*72+j*3]=bn[j*3];N[k*72+j*3+1]=bn[j*3+1];N[k*72+j*3+2]=bn[j*3+2];C[k*72+j*3]=c.r;C[k*72+j*3+1]=c.g;C[k*72+j*3+2]=c.b}
    for(j=0;j<36;j++)I[k*36+j]=bi[j]+k*24}
  var g=new T.BufferGeometry();g.setAttribute('position',new T.BufferAttribute(P,3));g.setAttribute('normal',new T.BufferAttribute(N,3));g.setAttribute('color',new T.BufferAttribute(C,3));g.setIndex(new T.BufferAttribute(I,1));return g;
}
/* soft contact shadow under the fighter */
var shadow=(function(){
  var c=document.createElement('canvas');c.width=c.height=64;var x=c.getContext('2d'),gr=x.createRadialGradient(32,32,2,32,32,31);
  gr.addColorStop(0,'rgba(0,0,0,0.62)');gr.addColorStop(1,'rgba(0,0,0,0)');x.fillStyle=gr;x.fillRect(0,0,64,64);
  var m=new T.Mesh(new T.PlaneGeometry(1,1),new T.MeshBasicMaterial({map:new T.CanvasTexture(c),transparent:true,depthWrite:false,fog:false}));
  m.rotation.x=-Math.PI/2;m.position.y=0.003;m.renderOrder=1;scene.add(m);return m;
})();

/* ---------- cubes: blood, crumbs, sweat, teeth and small chunks in one instanced draw ---------- */
var CN=4300,PN=3300,BN=40,BV=28,NEAR0=.2,NEAR1=.6,STRMAX=.026;
var cgeo=new T.InstancedBufferGeometry();
(function(){var b=new T.BoxGeometry(1,1,1);cgeo.setIndex(b.index);cgeo.setAttribute('position',b.attributes.position);cgeo.setAttribute('normal',b.attributes.normal)})();
function iattr(n,sz,u8){var a=new T.InstancedBufferAttribute(u8?new Uint8Array(n*sz):new Float32Array(n*sz),sz,!!u8);a.setUsage(T.DynamicDrawUsage);return a}
var aIP=iattr(CN,3),aIQ=iattr(CN,4),aIS=iattr(CN,3),aIC=iattr(CN,4,true);
cgeo.setAttribute('iPos',aIP);cgeo.setAttribute('iQ',aIQ);cgeo.setAttribute('iS',aIS);cgeo.setAttribute('iC',aIC);cgeo.instanceCount=0;
var cubes=new T.Mesh(cgeo,CUBEMAT);cubes.frustumCulled=false;scene.add(cubes);
var pP=new Float32Array(PN*3),pV=new Float32Array(PN*3),pQ=new Float32Array(PN*4),pW=new Float32Array(PN*3),pC=new Uint8Array(PN*4),pS=new Float32Array(PN),pL=new Float32Array(PN),pK=new Uint8Array(PN),pR=new Uint8Array(PN),pcur=0;
/* kind: 0 blood drop, 1 crumb, 2 sweat, 4 spark, 5 mist, 6 glove trail (unlit), 7 mega fire (unlit, rises) */
function spawn(x,y,z,vx,vy,vz,r,g,b,w,size,kind,life){
  var i=pcur,a=i*3,q=i*4;pcur=(pcur+1)%PN;
  pP[a]=x;pP[a+1]=y;pP[a+2]=z;pV[a]=vx;pV[a+1]=vy;pV[a+2]=vz;pC[q]=r;pC[q+1]=g;pC[q+2]=b;pC[q+3]=w*255;pS[i]=size;pL[i]=life;pK[i]=kind;pR[i]=0;
  pQ[q]=0;pQ[q+1]=0;pQ[q+2]=0;pQ[q+3]=1;
  if(kind===1){pW[a]=rnd(-20,20);pW[a+1]=rnd(-20,20);pW[a+2]=rnd(-20,20)}
}
var BLC=[[178,12,12],[150,10,10],[205,26,20],[120,8,9]];
function blood(x,y,z,vx,vy,vz,size,life){var c=BLC[(Math.random()*4)|0];spawn(x,y,z,vx,vy,vz,c[0],c[1],c[2],0.9,size,0,life||3)}
function bloodBurst(p,n,dx,dy,dz,spd){
  spd=spd||1;
  for(var i=0;i<n;i++){
    if(i%3===2){var c=BLC[(Math.random()*4)|0];spawn(p.x,p.y,p.z,(dx*rnd(.5,2.6)+rnd(-1.4,1.4))*spd,(dy*rnd(.5,2)+rnd(-.2,2.2))*spd,(dz*rnd(.3,1.5)+rnd(.2,2.2))*spd,c[0],c[1],c[2],0.8,rnd(.0012,.0022),5,rnd(.25,.6))}
    else blood(p.x+rnd(-.01,.01),p.y+rnd(-.01,.01),p.z+rnd(.004,.012),(dx*rnd(.4,2.4)+rnd(-.9,.9))*spd,(dy*rnd(.3,1.8)+rnd(.2,2.4))*spd,(dz*rnd(.2,1.4)+rnd(.1,1.6))*spd,rnd(.0022,.0055),3);
  }
}
var bodies=[],bcur=0;
for(var bi_=0;bi_<BN;bi_++)bodies.push({on:false,p:new T.Vector3(),v:new T.Vector3(),q:new T.Quaternion(),w:new T.Vector3(),n:0,off:new Float32Array(BV*3),c:new Uint8Array(BV*4),s:.003,r:.01,life:0,rest:false});
/* a rigid clump of cubes: pts = world xyz list, cols = rgbw list (0..255) */
function clump(pts,cols,size,vx,vy,vz){
  var b=bodies[bcur],n=Math.min(BV,pts.length/3),k,cx=0,cy=0,cz=0;bcur=(bcur+1)%BN;if(!n){b.on=false;return null}
  for(k=0;k<n;k++){cx+=pts[k*3];cy+=pts[k*3+1];cz+=pts[k*3+2]}cx/=n;cy/=n;cz/=n;
  for(k=0;k<n;k++){b.off[k*3]=pts[k*3]-cx;b.off[k*3+1]=pts[k*3+1]-cy;b.off[k*3+2]=pts[k*3+2]-cz;b.c[k*4]=cols[k*4];b.c[k*4+1]=cols[k*4+1];b.c[k*4+2]=cols[k*4+2];b.c[k*4+3]=cols[k*4+3]}
  b.p.set(cx,cy,cz);b.v.set(vx,vy,vz);b.q.set(0,0,0,1);b.w.set(rnd(-14,14),rnd(-14,14),rnd(-14,14));
  b.n=n;b.s=size;b.r=Math.max(.008,Math.cbrt(n)*size*.6);b.life=rnd(6,9);b.rest=false;b.on=true;return b;
}
var _bq=new T.Quaternion(),_cq=new T.Quaternion(),_ba=new T.Vector3(),_bp=new T.Vector3();
/* ---------- ropes: verlet chains drawn as voxel cubes (intestines, optic nerve); a rope pinned at both ends with no slack is a bone shard ---------- */
var ROPEMAX=34,ROPES=[],_ra=new T.Vector3(),_rb=new T.Vector3(),_rpp=new T.Vector3(),_rpn=new T.Vector3();
/* o: {n, seg, a:fn(out) start pin, b:fn(out)|null end pin, size, col:[r,g,b,w], base:[r,g,b] colour near the pin, dir:Vector3 initial direction, damp} */
function addRope(o){
  var n=o.n,r={n:n,seg:o.seg,a:o.a,b:o.b||null,size:o.size,col:o.col,base:o.base||null,p:new Float32Array(n*3),q:new Float32Array(n*3),damp:o.damp||0.985,rigid:!!o.rigid,plane:o.plane||null,seed:(Math.random()*1e4)|0},k,d=o.dir||_rb.set(0,-1,0);
  o.a(_ra);for(k=0;k<n;k++){r.p[k*3]=r.q[k*3]=_ra.x+d.x*o.seg*k+rnd(-.002,.002);r.p[k*3+1]=r.q[k*3+1]=_ra.y+d.y*o.seg*k;r.p[k*3+2]=r.q[k*3+2]=_ra.z+d.z*o.seg*k+rnd(-.002,.002)}
  if(o.kick)for(k=1;k<n;k++){r.q[k*3]-=o.kick.x*k/n;r.q[k*3+1]-=o.kick.y*k/n;r.q[k*3+2]-=o.kick.z*k/n}
  r.tmp=!!o.tmp;ROPES.push(r);
  /* decorative strands are capped: the oldest one goes when a new one arrives */
  if(ROPES.length>ROPEMAX)for(k=0;k<ROPES.length;k++)if(ROPES[k].tmp){ROPES.splice(k,1);break}
  return r;
}
function updRopes(dt){
  if(!ROPES.length)return;var g=-7.6*dt*dt,k,j,it,r,p,q,n,a,b,x,y,z,dx,dy,dz,l,f,sl;
  for(k=0;k<ROPES.length;k++){r=ROPES[k];p=r.p;q=r.q;n=r.n;
    r.a(_ra);if(r.b)r.b(_rb);
    if(r.rigid){for(j=0;j<n;j++){f=j/(n-1);p[j*3]=_ra.x+(_rb.x-_ra.x)*f;p[j*3+1]=_ra.y+(_rb.y-_ra.y)*f;p[j*3+2]=_ra.z+(_rb.z-_ra.z)*f}continue}
    if(dt>0)for(j=1;j<n;j++){a=j*3;x=p[a];y=p[a+1];z=p[a+2];
      p[a]+=(x-q[a])*r.damp;p[a+1]+=(y-q[a+1])*r.damp+g;p[a+2]+=(z-q[a+2])*r.damp;q[a]=x;q[a+1]=y;q[a+2]=z}
    p[0]=_ra.x;p[1]=_ra.y;p[2]=_ra.z;
    /* no node may move faster than ~1.5 m/s per step: the anchor jerks with every punch */
    for(j=1;j<n;j++){a=j*3;dx=p[a]-q[a];dy=p[a+1]-q[a+1];dz=p[a+2]-q[a+2];l=dx*dx+dy*dy+dz*dz;if(l>0.0009){l=0.03/Math.sqrt(l);q[a]=p[a]-dx*l;q[a+1]=p[a+1]-dy*l;q[a+2]=p[a+2]-dz*l}}
    for(it=0;it<8;it++){
      for(j=0;j<n-1;j++){a=j*3;b=a+3;dx=p[b]-p[a];dy=p[b+1]-p[a+1];dz=p[b+2]-p[a+2];l=Math.sqrt(dx*dx+dy*dy+dz*dz)||1e-6;f=(l-r.seg)/l;
        if(j===0){p[b]-=dx*f;p[b+1]-=dy*f;p[b+2]-=dz*f}else{f*=0.5;p[a]+=dx*f;p[a+1]+=dy*f;p[a+2]+=dz*f;p[b]-=dx*f;p[b+1]-=dy*f;p[b+2]-=dz*f}}
      if(r.b){a=(n-1)*3;p[a]=_rb.x;p[a+1]=_rb.y;p[a+2]=_rb.z}
      /* stay outside the body: a plane in front of the wound */
      if(r.plane){if(it===0)r.plane(_rpp,_rpn);for(j=1;j<n;j++){a=j*3;dx=p[a]-p[0];dy=p[a+1]-p[1];dz=p[a+2]-p[2];if(dx*dx+dy*dy+dz*dz>0.016)continue;f=(p[a]-_rpp.x)*_rpn.x+(p[a+1]-_rpp.y)*_rpn.y+(p[a+2]-_rpp.z)*_rpn.z-r.size*0.6;if(f<0){p[a]-=_rpn.x*f;p[a+1]-=_rpn.y*f;p[a+2]-=_rpn.z*f;q[a]-=_rpn.x*f;q[a+1]-=_rpn.y*f;q[a+2]-=_rpn.z*f}}}
      for(j=1;j<n;j++){a=j*3;if(p[a+1]<r.size*0.5){p[a+1]=r.size*0.5;q[a]+=(p[a]-q[a])*0.3;q[a+2]+=(p[a+2]-q[a+2])*0.3}}
    }
  }
}
function drawRopes(P,Q,S,C,j){
  var k,i,m,t,r,p,a,b,dx,dy,dz,l,cnt,f,h,c,bs;
  for(k=0;k<ROPES.length;k++){r=ROPES[k];p=r.p;c=r.col;bs=r.base;
    for(i=0;i<r.n-1;i++){a=i*3;b=a+3;dx=p[b]-p[a];dy=p[b+1]-p[a+1];dz=p[b+2]-p[a+2];l=Math.sqrt(dx*dx+dy*dy+dz*dz);cnt=Math.max(1,Math.ceil(l/(r.size*0.7)));
      for(m=0;m<cnt&&j<CN;m++){t=m/cnt;P[j*3]=p[a]+dx*t;P[j*3+1]=p[a+1]+dy*t;P[j*3+2]=p[a+2]+dz*t;Q[j*4]=Q[j*4+1]=Q[j*4+2]=0;Q[j*4+3]=1;
        h=0.84+0.22*hash1(r.seed+i*7+m);S[j*3]=S[j*3+1]=S[j*3+2]=r.size*(r.rigid?1-0.45*(i+t)/(r.n-1):1);
        f=bs?Math.max(0,1-(i+t)/(r.n-1)*2.2):0;
        C[j*4]=clamp((c[0]+(bs?(bs[0]-c[0])*f:0))*h,0,255);C[j*4+1]=clamp((c[1]+(bs?(bs[1]-c[1])*f:0))*h,0,255);C[j*4+2]=clamp((c[2]+(bs?(bs[2]-c[2])*f:0))*h,0,255);C[j*4+3]=c[3]*255;j++}}}
  return j;
}
function clearRopes(){ROPES.length=0}
function updCubes(dt){
  var P=aIP.array,Q=aIQ.array,S=aIS.array,C=aIC.array,j=0,i,a,q,s,sp,k,b,wl,sc,ux,uy,uz,qw,l,cd,cpx=camera.position.x,cpy=camera.position.y,cpz=camera.position.z;
  for(i=0;i<PN;i++){
    if(pL[i]<=0)continue;
    pL[i]-=dt;if(pL[i]<=0)continue;a=i*3;q=i*4;
    if(!pR[i]){
      if(pK[i]===6){l=1-7*dt;pV[a]*=l;pV[a+1]*=l;pV[a+2]*=l}else if(pK[i]===7){pV[a+1]+=2.4*dt;l=1-3*dt;pV[a]*=l;pV[a+2]*=l}else pV[a+1]-=7.6*dt;
      if(pK[i]===5){l=1-2.5*dt;pV[a]*=l;pV[a+1]*=l;pV[a+2]*=l}else if(pK[i]===0){l=1-0.6*dt;pV[a]*=l;pV[a+2]*=l}
      pP[a]+=pV[a]*dt;pP[a+1]+=pV[a+1]*dt;pP[a+2]+=pV[a+2]*dt;
      if(pK[i]===1){wl=Math.sqrt(pW[a]*pW[a]+pW[a+1]*pW[a+1]+pW[a+2]*pW[a+2]);if(wl>1e-3){_ba.set(pW[a]/wl,pW[a+1]/wl,pW[a+2]/wl);_bq.setFromAxisAngle(_ba,wl*dt);_cq.set(pQ[q],pQ[q+1],pQ[q+2],pQ[q+3]).premultiply(_bq);pQ[q]=_cq.x;pQ[q+1]=_cq.y;pQ[q+2]=_cq.z;pQ[q+3]=_cq.w}}
      if(pP[a+1]<pS[i]*0.5){
        if(pK[i]===0){stain(pP[a],pP[a+2],pS[i]);if(pS[i]>.0045)sfx.drip(Math.min(1,pS[i]*160));if(pS[i]>.004&&pV[a+1]<-2.2&&Math.random()<.5)blood(pP[a],.004,pP[a+2],rnd(-.5,.5),rnd(.4,1),rnd(-.5,.5),.0018,.5);pL[i]=0;continue}
        if(pK[i]!==1){pL[i]=0;continue}
        pP[a+1]=pS[i]*0.5;stain(pP[a],pP[a+2],pS[i]*0.6);
        if(Math.abs(pV[a+1])<.6){pR[i]=1;pW[a]=pW[a+1]=pW[a+2]=0}else{pV[a+1]*=-.3;pV[a]*=.5;pV[a+2]*=.5}
      }
    }
    s=pS[i]*(pL[i]<.3?pL[i]/.3:1);
    /* near the lens a drop would cover the fight: shrink it away between NEAR1 and NEAR0 (the foe's face is never closer than ~0.7 m) */
    cd=Math.sqrt((pP[a]-cpx)*(pP[a]-cpx)+(pP[a+1]-cpy)*(pP[a+1]-cpy)+(pP[a+2]-cpz)*(pP[a+2]-cpz));
    if(cd<NEAR1){if(cd<=NEAR0)continue;s*=(cd-NEAR0)/(NEAR1-NEAR0)}
    P[j*3]=pP[a];P[j*3+1]=pP[a+1];P[j*3+2]=pP[a+2];
    if(pK[i]===0&&!pR[i]){
      sp=Math.sqrt(pV[a]*pV[a]+pV[a+1]*pV[a+1]+pV[a+2]*pV[a+2]);
      if(sp>.3){ux=pV[a]/sp;uy=pV[a+1]/sp;uz=pV[a+2]/sp;qw=1+uz;
        if(qw<1e-4){Q[j*4]=1;Q[j*4+1]=0;Q[j*4+2]=0;Q[j*4+3]=0}else{l=Math.sqrt(uy*uy+ux*ux+qw*qw);Q[j*4]=-uy/l;Q[j*4+1]=ux/l;Q[j*4+2]=0;Q[j*4+3]=qw/l}
        /* a streak: length capped on screen, and the longer it is the thinner it gets */
        l=Math.max(s,Math.min(s*(1+Math.min(4.5,sp*0.011/s)),cd*STRMAX));S[j*3+2]=l;S[j*3]=S[j*3+1]=s*Math.sqrt(s/l)}
      else{Q[j*4]=0;Q[j*4+1]=0;Q[j*4+2]=0;Q[j*4+3]=1;S[j*3]=S[j*3+1]=S[j*3+2]=s}
    }else{Q[j*4]=pQ[q];Q[j*4+1]=pQ[q+1];Q[j*4+2]=pQ[q+2];Q[j*4+3]=pQ[q+3];S[j*3]=S[j*3+1]=S[j*3+2]=s}
    C[j*4]=pC[q];C[j*4+1]=pC[q+1];C[j*4+2]=pC[q+2];C[j*4+3]=pC[q+3];j++;
  }
  for(i=0;i<BN;i++){
    b=bodies[i];if(!b.on)continue;b.life-=dt;if(b.life<=0){b.on=false;continue}
    if(!b.rest){
      b.v.y-=7.6*dt;b.p.addScaledVector(b.v,dt);
      wl=b.w.length();if(wl>1e-3){_ba.copy(b.w).multiplyScalar(1/wl);_bq.setFromAxisAngle(_ba,wl*dt);b.q.premultiply(_bq)}
      if(b.p.y<b.r){b.p.y=b.r;stain(b.p.x,b.p.z,b.r*.7);
        if(Math.abs(b.v.y)<.8){b.rest=true;b.v.set(0,0,0);b.w.set(0,0,0)}else{b.v.y*=-.32;b.v.x*=.6;b.v.z*=.6;b.w.multiplyScalar(.5)}}
    }
    sc=b.life<.4?b.life/.4:1;cd=b.p.distanceTo(camera.position);if(cd<NEAR1){if(cd<=NEAR0)continue;sc*=(cd-NEAR0)/(NEAR1-NEAR0)}
    for(k=0;k<b.n&&j<CN;k++){
      _bp.set(b.off[k*3],b.off[k*3+1],b.off[k*3+2]).multiplyScalar(sc).applyQuaternion(b.q).add(b.p);
      P[j*3]=_bp.x;P[j*3+1]=_bp.y;P[j*3+2]=_bp.z;Q[j*4]=b.q.x;Q[j*4+1]=b.q.y;Q[j*4+2]=b.q.z;Q[j*4+3]=b.q.w;S[j*3]=S[j*3+1]=S[j*3+2]=b.s*sc;
      C[j*4]=b.c[k*4];C[j*4+1]=b.c[k*4+1];C[j*4+2]=b.c[k*4+2];C[j*4+3]=b.c[k*4+3];j++;
    }
  }
  j=drawRopes(P,Q,S,C,j);
  cgeo.instanceCount=j;upd(aIP,j*3);upd(aIQ,j*4);upd(aIS,j*3);upd(aIC,j*4);
}
function clearCubes(){pL.fill(0);for(var i=0;i<BN;i++)bodies[i].on=false}

/* ---------- pieces: whole torn-off parts (jaw, ear, flaps) as their own voxel meshes with rigid-body motion ---------- */
var pieces=[];
var _pm=new T.Matrix4(),_pq=new T.Quaternion(),_ps=new T.Vector3(),_pv=new T.Vector3(),_pc=new T.Vector3();
function addPiece(v,worldM,comM,vel,ang){
  var g=new T.Group();_pc.copy(comM).applyMatrix4(worldM);worldM.decompose(_pv,_pq,_ps);
  g.position.copy(_pc);g.quaternion.copy(_pq);v.group.position.copy(comM).multiplyScalar(-1);g.add(v.group);scene.add(g);
  var r=0.5*Math.max(v.W,v.H,v.D)*v.s*0.01*0.55;
  var p={g:g,v:v,vel:vel.clone(),w:ang.clone(),r:Math.max(0.012,r),rest:false,bleed:2.5,t:0};pieces.push(p);return p;
}
function piecesMesh(){for(var k=0;k<pieces.length;k++)pieces[k].v.process(0,8,4)}
function updPieces(dt){
  for(var k=0;k<pieces.length;k++){var p=pieces[k];
    if(p.rest)continue;p.t+=dt;
    p.vel.y-=7.6*dt;p.g.position.addScaledVector(p.vel,dt);
    if(p.teth){p.teth.a(_pc);_pv.subVectors(p.g.position,_pc);var tl=_pv.length();
      if(tl>p.teth.len){_pv.multiplyScalar(1/tl);p.g.position.copy(_pc).addScaledVector(_pv,p.teth.len);var vr=p.vel.dot(_pv);if(vr>0)p.vel.addScaledVector(_pv,-vr*1.6)}
      if(p.teth.plane){p.teth.plane(_rpp,_rpn);var pd=_pv.subVectors(p.g.position,_rpp).dot(_rpn)-p.r;if(pd<0){p.g.position.addScaledVector(_rpn,-pd);var vn=p.vel.dot(_rpn);if(vn<0)p.vel.addScaledVector(_rpn,-vn)}}
      p.vel.multiplyScalar(1-1.2*dt);p.w.multiplyScalar(1-2*dt);p.w.addScaledVector(_pv,0);
      if(p.bleed<=0&&Math.random()<0.05)blood(p.g.position.x,p.g.position.y,p.g.position.z,0,-.2,0,rnd(.002,.004),2)}
    var wl=p.w.length();if(wl>1e-3){_ba.copy(p.w).multiplyScalar(1/wl);_bq.setFromAxisAngle(_ba,wl*dt);p.g.quaternion.premultiply(_bq)}
    if(p.pins&&p.pins.length&&dt>0)pinStep(p,dt);
    if(p.bleed>0){p.bleed-=dt;if(Math.random()<0.6)blood(p.g.position.x,p.g.position.y,p.g.position.z,p.vel.x*0.3+rnd(-.3,.3),p.vel.y*0.3,p.vel.z*0.3+rnd(-.3,.3),rnd(.003,.006),3)}
    var held=p.teth||(p.pins&&p.pins.length);
    if(p.g.position.y<p.r){p.g.position.y=p.r;if(!held||p.vel.y<-0.5)pool(p.g.position.x,p.g.position.z,p.r*1.2);
      if(held){if(p.vel.y<-0.8)sfx.squelch();p.vel.y=Math.max(0,p.vel.y);p.vel.x*=.9;p.vel.z*=.9;p.w.multiplyScalar(.8)}
      else if(Math.abs(p.vel.y)<0.7){p.vel.set(0,0,0);p.w.set(0,0,0);p.rest=true;if(p.snd==='tooth')sfx.toothFloor(.7);else sfx.drop()}
      else{p.vel.y*=-0.28;p.vel.x*=0.55;p.vel.z*=0.55;p.w.multiplyScalar(0.45);if(p.snd==='tooth')sfx.toothFloor(Math.min(1,.4+Math.abs(p.vel.y)*.25));else sfx.squelch()}}
  }
}
/* a piece hanging from anchor points on the body (jaw on its hinges): position-based correction of the attach points */
var _pa=new T.Vector3(),_pb=new T.Vector3(),_pd=new T.Vector3(),_pr=new T.Vector3(),_pt=new T.Vector3();
function pinStep(p,dt){
  var it,q,pn,dl,rl,an;
  for(it=0;it<5;it++){p.g.updateMatrixWorld(true);
    for(q=0;q<p.pins.length;q++){pn=p.pins[q];pn.a(_pa);_pb.copy(pn.l).applyMatrix4(p.v.group.matrixWorld);
      _pd.subVectors(_pa,_pb);dl=_pd.length();if(it===0)pn.d=dl;if(dl<=pn.slack)continue;_pd.multiplyScalar((dl-pn.slack)/dl);
      _pr.subVectors(_pb,p.g.position);p.g.position.addScaledVector(_pd,.5);p.vel.addScaledVector(_pd,.5/dt);
      rl=_pr.lengthSq();if(rl>1e-6){_pt.crossVectors(_pr,_pd).multiplyScalar(.5/rl);an=_pt.length();if(an>1e-7){_ba.copy(_pt).multiplyScalar(1/an);_bq.setFromAxisAngle(_ba,an);p.g.quaternion.premultiply(_bq);p.w.addScaledVector(_pt,.9/dt)}}
      p.g.updateMatrixWorld(true)}}
  var vl=p.vel.length();if(vl>3.5)p.vel.multiplyScalar(3.5/vl);var wl=p.w.length();if(wl>14)p.w.multiplyScalar(14/wl);
  p.vel.multiplyScalar(1-1.8*dt);p.w.multiplyScalar(1-2.6*dt);
  for(q=0;q<p.pins.length;q++){pn=p.pins[q];
    if(pn.d>pn.slack+0.06){pn.strain+=dt;if(pn.strain>0.8&&p.onTear){p.onTear(p,q);break}}else pn.strain=Math.max(0,pn.strain-dt*.5);
    if(Math.random()<.22){pn.a(_pa);blood(_pa.x,_pa.y-.005,_pa.z,rnd(-.1,.1),-.2,rnd(-.1,.1),rnd(.002,.004),2)}}
}
function clearPieces(){for(var k=0;k<pieces.length;k++){scene.remove(pieces[k].g);pieces[k].v.dispose()}pieces.length=0}

/* ---------- impact rings ---------- */
var rings=[];
(function(){var RG=new T.RingGeometry(.88,1,32),CG=new T.CircleGeometry(1,20);
  for(var i=0;i<4;i++){var m=new T.Mesh(i<2?RG:CG,new T.MeshBasicMaterial({color:0xfff6dc,transparent:true,opacity:0,blending:T.AdditiveBlending,depthWrite:false,depthTest:false,fog:false,side:T.DoubleSide}));
    m.visible=false;m.renderOrder=5;scene.add(m);rings.push({m:m,t:0,dur:.2,s0:.05,s1:.5,o:1})}})();
var ringN=0;
function impactFx(p,str){
  var r=rings[ringN++%2];r.t=0;r.dur=.15+.1*str;r.s0=.02;r.s1=.06+.11*str;r.o=.45+.3*Math.min(1,str);
  r.m.position.copy(p);r.m.scale.setScalar(r.s0);r.m.material.opacity=r.o;r.m.visible=true;
  var c=rings[2+(ringN%2)];c.t=0;c.dur=.08;c.s0=.015+.02*str;c.s1=.035+.04*str;c.o=.7;c.m.position.copy(p);c.m.scale.setScalar(c.s0);c.m.material.opacity=c.o;c.m.visible=true;
  if(str>=1&&!REDUCED){var V=new T.Vector3().copy(p).project(camera),L=$('lines');
    L.style.setProperty('--ix',((V.x*.5+.5)*100).toFixed(1)+'%');L.style.setProperty('--iy',((-V.y*.5+.5)*100).toFixed(1)+'%');retrig(L,'hit');retrig($('white'),'hit')}
}
function updRings(dt){for(var i=0;i<4;i++){var r=rings[i];if(!r.m.visible)continue;r.t+=dt;var k=r.t/r.dur;if(k>=1){r.m.visible=false;continue}
  r.m.scale.setScalar(r.s0+(r.s1-r.s0)*(1-(1-k)*(1-k)));r.m.material.opacity=(1-k)*r.o;r.m.quaternion.copy(camera.quaternion)}}

/* ---------- audio ---------- */
/* Звуки — файлы из dist/sounds (набор earshot, см. docs/SOUND.md): manifest.json -> событие -> варианты. Пока файлы не
   скачаны и не декодированы (и всегда из file://), играет прежний синтез syn.*. Декодирование идёт при первом касании
   (audioInit), до гонга, не на первом ударе. Одновременных голосов не больше SNDMAX, у события свой предел. */
var AC=null,master=null,noiseBuf=null;
function noop(){}
function acResume(){if(AC&&AC.state!=='running'&&AC.state!=='closed')try{var p=AC.resume();if(p&&p.catch)p.catch(noop)}catch(e){}}
function audioInit(){if(AC){acResume();return}try{AC=new(window.AudioContext||window.webkitAudioContext)();master=AC.createGain();master.gain.value=.55;master.connect(AC.destination);
  noiseBuf=AC.createBuffer(1,AC.sampleRate,AC.sampleRate);var d=noiseBuf.getChannelData(0);for(var i=0;i<d.length;i++)d[i]=Math.random()*2-1;if(AC.state==='suspended')AC.resume()}catch(e){AC=null;return}
  /* iOS: после звонка или сворачивания контекст 'interrupted'/'suspended': будим на возврате и на следующем касании */
  document.addEventListener('visibilitychange',function(){if(!document.hidden)acResume()});
  ['pointerdown','touchend','keydown'].forEach(function(n){window.addEventListener(n,acResume,true)});
  for(var f in SND.raw)sndDecode(f)}
function noise(dur,freq,type,gain,delay,sweep){if(!AC)return;try{var t=AC.currentTime+(delay||0),s=AC.createBufferSource(),f=AC.createBiquadFilter(),g=AC.createGain();
  s.buffer=noiseBuf;f.type=type;f.frequency.setValueAtTime(freq,t);if(sweep)f.frequency.exponentialRampToValueAtTime(sweep,t+dur);
  g.gain.setValueAtTime(gain,t);g.gain.exponentialRampToValueAtTime(.001,t+dur);s.connect(f);f.connect(g);g.connect(master);s.start(t,Math.random()*.5);s.stop(t+dur+.03)}catch(e){}}
function tone(f0,f1,dur,gain,type,delay){if(!AC)return;try{var t=AC.currentTime+(delay||0),o=AC.createOscillator(),g=AC.createGain();
  o.type=type||'sine';o.frequency.setValueAtTime(f0,t);if(f1!==f0)o.frequency.exponentialRampToValueAtTime(f1,t+dur);g.gain.setValueAtTime(gain,t);g.gain.exponentialRampToValueAtTime(.001,t+dur);
  o.connect(g);g.connect(master);o.start(t);o.stop(t+dur+.03)}catch(e){}}
/* прежний синтез: запасной путь */
var syn={
  jab:function(){noise(.07,900,'lowpass',.7);tone(170,60,.09,.6)},
  heavy:function(w){noise(.18,700,'lowpass',1.1);noise(.05,2600,'bandpass',.5);tone(130,34,.3,1.1);tone(62,30,.36,.9,'triangle');if(w)noise(.12,1500,'bandpass',.35,.02)},
  block:function(){noise(.05,1400,'bandpass',.5);tone(220,120,.05,.3)},
  crack:function(){for(var i=0;i<4;i++)noise(.03,3200,'highpass',.75,i*.035);noise(.09,1100,'bandpass',.6,.02);tone(90,36,.32,.9)},
  whoosh:function(){noise(.16,500,'bandpass',.22,0,1800)},
  swing:function(){noise(.2,300,'bandpass',.3,0,2200)},
  bell:function(n){for(var i=0;i<(n||1);i++){tone(830,830,1.3,.3,'sine',i*.38);tone(1245,1245,.9,.14,'sine',i*.38);tone(2075,2075,.5,.06,'sine',i*.38)}},
  hurt:function(){noise(.2,320,'lowpass',1);tone(90,30,.3,1)},
  step:function(){noise(.06,260,'lowpass',.35);noise(.05,240,'lowpass',.3,.12)},
  tick:function(){tone(1200,900,.05,.14,'square')},
  tooth:function(){tone(2400,1800,.05,.12,'triangle',.03)},
  thud:function(){noise(.3,200,'lowpass',1);tone(70,28,.4,1)},
  clack:function(){tone(1800,900,.03,.22,'square');noise(.03,2500,'highpass',.4)},
  squelch:function(){noise(.13,900,'bandpass',.4,.01,280)},
  ready:function(){tone(420,1260,.22,.22,'square');tone(840,1680,.3,.16,'triangle',.12);tone(1260,1260,.4,.08,'sine',.26)},
  charge:function(){noise(.38,180,'bandpass',.55,0,3200);tone(70,240,.38,.4,'sawtooth')},
  mega:function(){noise(.5,500,'lowpass',1.3);noise(.08,2600,'bandpass',.6);tone(120,30,.5,1.3);tone(52,22,.9,1.2,'triangle');for(var i=0;i<3;i++)noise(.03,3400,'highpass',.6,.03+i*.04);noise(.2,1300,'bandpass',.4,.04)},
  tear:function(){noise(.25,600,'bandpass',.6,0,180);noise(.08,2600,'highpass',.4,.05);tone(80,30,.4,1)}
};
/* банк файлов */
var SNDURL='sounds/',SNDMAX=20,SNDQ=4;
var SND={man:null,ext:'mp3',raw:{},buf:{},bad:{},last:{},v:[],hall:null,dripT:0,n:{play:0,steal:0,drop:0}};
function sndLoad(){
  if(SND.man||SND.loading||!window.fetch||location.protocol==='file:')return;SND.loading=true;
  try{var a=document.createElement('audio');if(a.canPlayType&&a.canPlayType('audio/webm; codecs="opus"'))SND.ext='webm'}catch(e){}
  fetch(SNDURL+'manifest.json').then(function(r){return r.ok?r.json():null}).then(function(m){
    if(!m||!m.events)return;SND.man=m;var q=[],e,i=0;for(e in m.events)q=q.concat(m.events[e].files);
    function next(){if(i>=q.length)return;var f=q[i++];fetch(SNDURL+f+'.'+SND.ext).then(function(r){return r.ok?r.arrayBuffer():null}).then(function(b){if(b){SND.raw[f]=b;if(AC)sndDecode(f)}}).catch(noop).then(next)}
    for(var k=0;k<SNDQ;k++)next()}).catch(noop)}
/* не декодировалось как webm (старый Safari говорит «да», а потом не может): берём mp3 */
function sndDecode(f,alt){var b=SND.raw[f];if(!AC||!b)return;delete SND.raw[f];
  function ok(x){if(x)SND.buf[f]=x}
  function bad(){if(alt||SND.ext!=='webm'){SND.bad[f]=1;return}fetch(SNDURL+f+'.mp3').then(function(r){return r.ok?r.arrayBuffer():null}).then(function(b2){if(b2){SND.raw[f]=b2;sndDecode(f,true)}}).catch(noop)}
  try{var p=AC.decodeAudioData(b,ok,bad);if(p&&p.catch)p.catch(noop)}catch(e){bad()}}
function sndKill(x,t){try{x.g.gain.setTargetAtTime(0,t,.012);x.s.stop(t+.06)}catch(e){}}
/* одно событие: случайный вариант (не тот же, что в прошлый раз), разброс высоты и громкости, пределы голосов */
function snd(ev,vol,rate,delay){
  if(!AC||!SND.man)return false;var e=SND.man.events[ev];if(!e)return false;
  var fs=e.files,n=fs.length,k=(Math.random()*n)|0,b=null,i,t=AC.currentTime,v=SND.v,same=0,os=-1,low=-1,x;
  if(n>1&&k===SND.last[ev])k=(k+1+((Math.random()*(n-1))|0))%n;
  for(i=0;i<n&&!b;i++)if(SND.buf[fs[(k+i)%n]]){k=(k+i)%n;b=SND.buf[fs[k]]}
  if(!b)return false;SND.last[ev]=k;
  for(i=v.length-1;i>=0;i--)if(v[i].done)v.splice(i,1);
  for(i=0;i<v.length;i++){x=v[i];if(x.ev===ev){same++;if(os<0)os=i}if(low<0||x.pri<v[low].pri||(x.pri===v[low].pri&&x.st<v[low].st))low=i}
  if(same>=(e.voices||2)){sndKill(v[os],t);v.splice(os,1);SND.n.steal++}
  else if(v.length>=SNDMAX){if(v[low].pri>(e.pri||0)){SND.n.drop++;return true}sndKill(v[low],t);v.splice(low,1);SND.n.steal++}
  var r=(rate||1)*Math.pow(2,(Math.random()*2-1)*(e.pv||0)/12),s=AC.createBufferSource(),g=AC.createGain(),at=t+(delay||0);
  s.buffer=b;s.playbackRate.value=r;g.gain.value=Math.pow(10,(e.gain||0)/20)*(vol==null?1:vol)*(1-Math.random()*(e.vv||0));
  s.connect(g);g.connect(master);s.start(at);
  /* голос держит место, пока узел не скажет ended: время конца, посчитанное из currentTime, ненадёжно — часы движутся
     и внутри задачи, а под нагрузкой прыгают на сотню мс, и узел с прошедшим временем старта звучит дольше расчётного */
  x={s:s,g:g,ev:ev,pri:e.pri||0,st:at};s.onended=function(){x.done=1};v.push(x);SND.n.play++;return true}
/* фон зала: одна петля, идёт пока на экране бой, нокаут или проигрыш */
function sndHall(on){
  if(!AC)return;var H=SND.hall,t=AC.currentTime;
  if(on&&!H){var e=SND.man&&SND.man.events.hall,b=e&&SND.buf[e.files[0]];if(!b)return;
    var s=AC.createBufferSource(),g=AC.createGain();s.buffer=b;s.loop=true;g.gain.setValueAtTime(0,t);g.gain.linearRampToValueAtTime(Math.pow(10,(e.gain||0)/20),t+1.5);
    s.connect(g);g.connect(master);s.start(t,Math.random()*b.duration*.9);SND.hall={s:s,g:g}}
  else if(!on&&H){H.g.gain.cancelScheduledValues(t);H.g.gain.setTargetAtTime(0,t,.35);try{H.s.stop(t+2)}catch(e){}SND.hall=null}}
setInterval(function(){if(AC&&typeof G!=='undefined'&&G)sndHall(G.mode==='fight'||G.mode==='ko'||G.mode==='lost')},500);
window.addEventListener('load',function(){setTimeout(sndLoad,300)});
var CRACKZ={nose:'crack_nose',jaw:'crack_jaw',mouth:'crack_jaw',cheekL:'crack_cheek',cheekR:'crack_cheek',orbL:'crack_cheek',orbR:'crack_cheek',skull:'crack_skull',ribL:'crack_rib',ribR:'crack_rib',abs:'crack_rib',arm:'crack_arm'};
/* имена и прежние вызовы те же; новые параметры необязательны */
var sfx={
  /* head===false: в корпус; fl: удар мега-серии */
  jab:function(head,fl){snd(fl?'flurry':head===false?'jab_body':'jab_head')||syn.jab()},
  /* w: по окровавленному; s: сила (g.pw*mult, 1..2.2); type: hook/upper/over/flurry */
  heavy:function(w,s,head,type){s=s||1;var ev=type==='flurry'?'flurry':(type==='upper'||type==='over'?type:'hook')+(head===false?'_body':'_head');
    if(snd(ev,clamp(.72+.32*(s-1),.6,1.15),1-.035*(s-1))){if(w)snd('wet',.85,1,.012)}else syn.heavy(w)},
  block:function(){snd('block')||syn.block()},
  guardBreak:function(){snd('guard_break')||syn.block()},
  crack:function(zone){snd(CRACKZ[zone]||'crack_jaw')||syn.crack()},
  whoosh:function(){snd('whoosh')||syn.whoosh()},
  miss:function(){snd('miss')||syn.whoosh()},
  dodge:function(){snd('dodge')||syn.whoosh()},
  swing:function(){snd('swing')||syn.swing()},
  bell:function(n){n=n||1;if(!snd('bell'))return syn.bell(n);for(var i=1;i<n;i++)snd('bell',1,1,i*.38)},
  /* k: доля урона от полного удара соперника; вскользь это половина */
  hurt:function(hook,k){snd(k!=null&&k<.6?'graze':hook?'hurt_hook':'hurt')||syn.hurt()},
  step:function(){snd('step')||syn.step()},
  tick:function(){snd('tick')||syn.tick()},
  count:function(){snd('count')||syn.tick()},
  tooth:function(chip){snd(chip?'tooth_chip':'tooth')||syn.tooth()},
  toothFloor:function(v){snd('tooth_floor',v)},
  thud:function(){snd('thud')||syn.thud()},
  drop:function(){snd('drop')||syn.thud()},
  clack:function(){snd('clack')||syn.clack()},
  squelch:function(){snd('squelch')||syn.squelch()},
  gut:function(){snd('gut')||syn.squelch()},
  tear:function(){snd('tear')||syn.tear()},
  flay:function(){snd('flay')||syn.crack()},
  jawOff:function(){snd('jaw_off')||syn.tear()},
  eye:function(){snd('eye')||syn.squelch()},
  burst:function(){if(!snd('burst')){syn.crack();syn.tear();syn.squelch()}},
  /* капли: не чаще раза в 70 мс, громкость от размера */
  drip:function(v){if(!AC||AC.currentTime-SND.dripT<.07)return;SND.dripT=AC.currentTime;snd('drip',v)},
  ready:function(){snd('ready')||syn.ready()},
  charge:function(){snd('charge')||syn.charge()},
  mega:function(){snd('mega')||syn.mega()},
  breath:function(){snd('breath')},
  cough:function(){snd('cough')},
  crowd:function(k){snd('crowd_'+k)},
  ui:function(){snd('ui')},
  combo:function(n){snd('combo',1,Math.pow(2,Math.min(9,(n||3)-3)/12))},
  win:function(){snd('win')},
  lose:function(){snd('lose')},
  star:function(){snd('star')},
  coin:function(){snd('coin')}
};

/* ---------- screen fx and HUD ---------- */
var popsEl=$('pops');
function popAt(text,sx,sy,cls){var mw=Math.min(canvas.clientWidth*.5,text.length*7+20);sx=clamp(sx,mw,canvas.clientWidth-mw);var e=document.createElement('div');e.className='pop'+(cls?' '+cls:'');e.textContent=text;e.style.left=sx+'px';e.style.top=sy+'px';popsEl.appendChild(e);setTimeout(function(){if(e.parentNode)e.parentNode.removeChild(e)},900)}
var _pw=new T.Vector3();
function popWorld(text,wp,cls){_pw.copy(wp).project(camera);popAt(text,(_pw.x*.5+.5)*canvas.clientWidth,(-_pw.y*.5+.5)*canvas.clientHeight,cls)}
function banner(text){var b=$('banner');b.textContent=text;retrig(b,'show')}
function screenSplat(n){
  var host=$('vfx'),w=canvas.clientWidth,h=canvas.clientHeight;
  for(var i=0;i<n;i++){var e=document.createElement('div'),u=(4+Math.random()*6)|0,sh=[];
    for(var k=0;k<16;k++)sh.push(((rnd(-4,4)|0)*u)+'px '+((rnd(-4,4)|0)*u)+'px 0 0 '+(Math.random()<.5?'#c8231c':'#9a0e0c'));
    e.className='splat';e.style.width=e.style.height=u+'px';e.style.left=rnd(.08,.92)*w+'px';e.style.top=rnd(.15,.8)*h+'px';e.style.boxShadow=sh.join(',');host.appendChild(e);
    (function(el){setTimeout(function(){if(el.parentNode)el.parentNode.removeChild(el)},3500)})(e)}
}
