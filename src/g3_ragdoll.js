
/* ---------- one elastic body ----------
   Standing: springy hips ride on bent knees (leg IK), the belly blends chest->pelvis and the neck blends chest->head in the shader.
   Knocked down: a 19-particle verlet ragdoll with a waist joint, a neck joint, limits everywhere; getting up is the same ragdoll
   pulled toward the standing pose with its bones kept at length (no stretching). */
var V3=T.Vector3;
var _r1=new V3(),_r2=new V3(),_r3=new V3(),_r4=new V3(),_rs=new V3(),_rm=new T.Matrix4(),_rm2=new T.Matrix4();
/* orthonormal frame with y along A->B and z as close to ref as it gets */
function frameM(A,B,ref,o){_r2.subVectors(B,A).normalize();perpTo(_r4,ref,_r2);_r3.crossVectors(_r2,_r4);return o.makeBasis(_r3,_r2,_r4)}

/* ----- soft joints (shader bend) ----- */
var NK_G0i=new T.Matrix4().makeTranslation(-NECKB.x,-NECKB.y,-NECKB.z),TS_R=new T.Matrix4().makeTranslation(0,.98,-.02),
    NK_R=new T.Matrix4().compose(new V3(0,.545,.03),new T.Quaternion().setFromEuler(new T.Euler(-.12,0,0)),new V3(1,1,1)).invert().multiply(new T.Matrix4().makeTranslation(NECKB.x,NECKB.y,NECKB.z+.03));
function updBend(fresh){
  RIG.headG.updateMatrix();NECK.mat.uniforms.uB2.value.copy(NK_G0i).multiply(RIG.headG.matrix).multiply(NK_R);
  if(!fresh)RIG.root.updateMatrixWorld(true);
  TORSOV.mat.uniforms.uB2.value.copy(TORSOV.group.matrixWorld).invert().multiply(LEGP[0].group.matrixWorld).multiply(TS_R);
}

/* ----- hips and legs while standing ----- */
var LEGW={hip:[new V3(),new V3()],knee:[new V3(),new V3()],ank:[new V3(),new V3()]},LREST=null;
var _lq=new T.Quaternion(),_le=new T.Euler(),_lh=new V3(),_la=new V3(),_lk2=new V3(),_lpole=new V3(),_lm=new T.Matrix4(),_lx=new V3(),_lx0=new V3(1,0,0),_lpiv=new V3(0,.9,0),_lt=new V3();
function legRest(){LREST=[];for(var k=0;k<2;k++){var h=new V3().fromArray(LJ.hip[k]).multiplyScalar(.01),n=new V3().fromArray(LJ.knee[k]).multiplyScalar(.01),a=new V3().fromArray(LJ.ank[k]).multiplyScalar(.01);
  LREST.push({hip:h,knee:n,ank:a,l1:h.distanceTo(n),l2:n.distanceTo(a),tR:frameM(h,n,_lx0,new T.Matrix4()).transpose(),sR:frameM(n,a,_lx0,new T.Matrix4()).transpose()})}}
function limbPlace(g,A0,R0t,A,B,ref){frameM(A,B,ref,_lm).multiply(R0t);g.quaternion.setFromRotationMatrix(_lm);g.position.copy(A0).applyQuaternion(g.quaternion).negate().add(A)}
/* pelvis offset pp and tilt pr (root space); feet stay planted (plus a step lift), knees take up the difference */
function legsSolve(pp,pr,l0,l1){
  if(!LREST)legRest();var pg=LEGP[0].group,k,it,L,over;
  _lq.setFromEuler(_le.set(pr.x,pr.y,pr.z));pg.quaternion.copy(_lq);pg.position.copy(_lpiv).applyQuaternion(_lq).negate().add(_lpiv).add(pp);
  for(it=0;it<2;it++){over=0;for(k=0;k<2;k++){L=LREST[k];_lh.copy(L.hip).applyQuaternion(_lq).add(pg.position);_la.copy(L.ank);_la.y+=k?l1:l0;over=Math.max(over,_lh.distanceTo(_la)-(L.l1+L.l2)*.996)}
    if(over>0)pg.position.y-=over*1.1;else break}
  RIG.torsoG.position.set(0,.98,-.02).applyQuaternion(_lq).add(pg.position);
  RIG.root.updateMatrix();RIG.root.matrixWorld.copy(RIG.root.matrix);
  _lx.copy(_lx0).applyQuaternion(_lq);
  for(k=0;k<2;k++){L=LREST[k];_lh.copy(L.hip).applyQuaternion(_lq).add(pg.position);_la.copy(L.ank);_la.y+=k?l1:l0;
    _lpole.set(k?.22:-.22,0,1);ik2(_lh,_la,L.l1,L.l2,_lpole,_lk2);
    limbPlace(LEGP[1+k].group,L.hip,L.tR,_lh,_lk2,_lx);limbPlace(LEGP[3+k].group,L.knee,L.sR,_lk2,_la,_lx0);
    LEGW.hip[k].copy(_lh).applyMatrix4(RIG.root.matrixWorld);LEGW.knee[k].copy(_lk2).applyMatrix4(RIG.root.matrixWorld);LEGW.ank[k].copy(_la).applyMatrix4(RIG.root.matrixWorld)}
}
function legsPose(dt,sway,twist,lungeT,hurt,bob){
  var kn=(F.st==='stagger'?.035:0)+(F.groggy>0?.02+.012*Math.sin(G.anim*7):0)+hurt*.015+(F.st==='windup'?.012:0)+(F.st==='guard'?.015:0);
  _lt.set(sway*.65,-.028-kn-Math.min(.06,Math.abs(F.recoil)*.3)+bob*.6,F.recoil*.2+lungeT*.15);
  F.ppv.x+=((_lt.x-F.pp.x)*110-F.ppv.x*13)*dt;F.ppv.y+=((_lt.y-F.pp.y)*110-F.ppv.y*13)*dt;F.ppv.z+=((_lt.z-F.pp.z)*110-F.ppv.z*13)*dt;
  F.pp.addScaledVector(F.ppv,dt);F.pp.x=clamp(F.pp.x,-.09,.09);F.pp.y=clamp(F.pp.y,-.13,0);F.pp.z=clamp(F.pp.z,-.09,.09);
  F.pr.set(F.tr.x*.2,(F.tr.y+twist)*.4,F.tr.z*.35+F.pp.x*.6);
  var lf=Math.min(.05,Math.abs(F.dzV)*.35),sp=Math.sin(F.stepPh);
  legsSolve(F.pp,F.pr,Math.max(0,sp)*lf,Math.max(0,-sp)*lf);
}

/* ----- ragdoll -----
   0,1 shoulders  2 neck base  3 sternum  18 waist   (chest, rigid)
   4,5 hips  6 tailbone  18 waist                    (pelvis, rigid; shares the waist = spine joint)
   7 skull base  8 face  9 skull                     (head, rigid; hangs on the neck 2-7)
   10,11 elbows  12,13 fists  14,15 knees  16,17 ankles */
var RN=19,RD={live:false,mode:0,bacc:0,off:new V3(),on:false,rise:false,p:new Float32Array(RN*3),q:new Float32Array(RN*3),s1:new Float32Array(RN*3),c:[],tet:[],bodies:[],t:0,ft:0,dur:1.9,thud:0,acc:0,pf:new V3()};
var RDR=[.085,.085,.07,.07,.09,.09,.06,.06,.045,.095,.05,.05,.07,.07,.06,.06,.05,.05,.085];
var RDW=[.5,.5,.5,.4,.4,.4,.4,.7,.8,.6,1,1,1.1,1.1,.9,.9,.9,.9,.35];
/* muscle stiffness while standing (per 120 Hz step): feet are planted, the rest is held softly so a blow travels through him */
var RDKS=[.004,.004,.004,.004,.008,.008,.008,.0045,.003,.003,0,0,0,0,.02,.02,1,1,.006];
var RDDEL=[.3,.3,.32,.3,.18,.18,.18,.42,.42,.42,.38,.38,.44,.44,.07,.07,0,0,.22];
function rdV(i,o){return o.set(RD.p[i*3],RD.p[i*3+1],RD.p[i*3+2])}
function rdSet(arr,i,v){arr[i*3]=v.x;arr[i*3+1]=v.y;arr[i*3+2]=v.z}
function rdGather(arr,elbow,hand){
  RIG.root.updateMatrixWorld(true);var tm=RIG.torsoG.matrixWorld,hm=RIG.headG.matrixWorld,pm=LEGP[0].group.matrixWorld,k;
  for(k=0;k<2;k++){rdSet(arr,k,_r1.copy(SHO[k]).applyMatrix4(tm));rdSet(arr,4+k,LEGW.hip[k]);rdSet(arr,10+k,elbow[k]);rdSet(arr,12+k,hand[k]);rdSet(arr,14+k,LEGW.knee[k]);rdSet(arr,16+k,LEGW.ank[k])}
  rdSet(arr,2,_r1.copy(NECKB).applyMatrix4(tm));rdSet(arr,3,_r1.set(0,.2,.05).applyMatrix4(tm));rdSet(arr,18,_r1.set(0,0,0).applyMatrix4(tm));
  rdSet(arr,6,_r1.set(0,.88,-.07).applyMatrix4(pm));
  rdSet(arr,7,_r1.set(0,0,0).applyMatrix4(hm));rdSet(arr,8,_r1.set(0,.03,.115).applyMatrix4(hm));rdSet(arr,9,_r1.set(0,.07,.015).applyMatrix4(hm));
}
/* the relaxed standing pose, as particles: constraint lengths come from it, and it is what he gets up into */
var _np=new V3(),_nr=new V3(),_nh=new V3(),_ns=new V3(),_npl=new V3();
function rdNeutral(arr){
  var hurt=1-F.hp/F.max,el=[],hd=[],k;
  RIG.root.position.set(0,0,ROOTZ+F.dz);RIG.root.rotation.set(0,0,0);
  RIG.torsoG.rotation.set(.2+hurt*.08,0,0);RIG.headG.rotation.set(-.12+hurt*.1,0,0);RIG.headG.position.set(0,.545,.03);
  legsSolve(_np.set(0,-.028,0),_nr.set(0,0,0),0,0);RIG.root.updateMatrixWorld(true);
  for(k=0;k<2;k++){var sd=k?1:-1,E=new V3();_nh.set(sd*.125,1.47,.25).applyMatrix4(RIG.root.matrixWorld);_ns.copy(SHO[k]).applyMatrix4(RIG.torsoG.matrixWorld);
    ik2(_ns,_nh,UPL,FOL+GLOFF,_npl.set(sd*.4,-1,.1),E);el.push(E);hd.push(_nh.clone())}
  rdGather(arr,el,hd);
}
function rdBuild(n){
  var c=[],i,j,d,CH=[0,1,2,3,18],PV=[4,5,6,18],HD=[7,8,9];
  function D(i,j){var dx=n[j*3]-n[i*3],dy=n[j*3+1]-n[i*3+1],dz=n[j*3+2]-n[i*3+2];return Math.sqrt(dx*dx+dy*dy+dz*dz)}
  function rig(i,j){d=D(i,j);c.push([i,j,d,d])}function rng(i,j,a,b){d=D(i,j);c.push([i,j,d*a,d*b])}function mn(i,j,m){c.push([i,j,m,1e9])}function fx(i,j,L){c.push([i,j,L,L])}
  function arm(i){return i>=10&&i<=13}
  function all(S){for(i=0;i<S.length;i++)for(j=i+1;j<S.length;j++)rig(S[i],S[j])}
  all(CH);all(PV);all(HD);
  /* neck: skull base on a short link, limited swing; head limited in twist, nod and tilt */
  rig(2,7);rng(7,0,.9,1.1);rng(7,1,.9,1.1);rng(7,3,.9,1.08);
  rng(8,0,.78,1.2);rng(8,1,.78,1.2);rng(8,3,.72,1.12);rng(9,0,.86,1.12);rng(9,1,.86,1.12);rng(9,3,.9,1.07);
  /* waist: side bend, twist, fold forward and arch back all limited */
  rng(0,4,.84,1.1);rng(1,5,.84,1.1);rng(0,5,.86,1.08);rng(1,4,.86,1.08);rng(3,4,.74,1.1);rng(3,5,.74,1.1);rng(2,6,.88,1.03);
  /* arms */
  fx(0,10,UPL);fx(1,11,UPL);fx(10,12,FOL+GLOFF);fx(11,13,FOL+GLOFF);c.push([0,12,.2,(UPL+FOL+GLOFF)*.995]);c.push([1,13,.2,(UPL+FOL+GLOFF)*.995]);
  mn(12,13,.11);mn(12,9,.15);mn(13,9,.15);mn(12,3,.15);mn(13,3,.15);mn(10,3,.13);mn(11,3,.13);mn(10,18,.12);mn(11,18,.12);
  /* legs */
  rig(4,14);rig(14,16);rig(5,15);rig(15,17);
  d=D(4,14)+D(14,16);c.push([4,16,d*.5,d*.998]);d=D(5,15)+D(15,17);c.push([5,17,d*.5,d*.998]);
  mn(14,15,.14);mn(16,17,.12);mn(14,17,.1);mn(15,16,.1);mn(14,3,.2);mn(15,3,.2);mn(9,14,.16);mn(9,15,.16);
  d=D(14,6);c.push([14,6,d*.84,1e9]);d=D(15,6);c.push([15,6,d*.84,1e9]);
  for(i=0;i<c.length;i++)c[i][4]=arm(c[i][0])||arm(c[i][1])?1:0;
  return c;
}
function tetVol(p,a,b,c,d){var ax=p[a*3],ay=p[a*3+1],az=p[a*3+2],ux=p[b*3]-ax,uy=p[b*3+1]-ay,uz=p[b*3+2]-az,vx=p[c*3]-ax,vy=p[c*3+1]-ay,vz=p[c*3+2]-az;
  _r1.set(uy*vz-uz*vy,uz*vx-ux*vz,ux*vy-uy*vx);return _r1.x*(p[d*3]-ax)+_r1.y*(p[d*3+1]-ay)+_r1.z*(p[d*3+2]-az)}
/* bodies follow a frame built from two particles and a reference direction */
function rdShoulders(o){rdV(1,o);rdV(0,_rs);return o.sub(_rs)}
function rdHips(o){rdV(5,o);rdV(4,_rs);return o.sub(_rs)}
function rdFace(o){rdV(8,o);rdV(7,_rs);return o.sub(_rs)}
var _f1=new V3(),_f2=new V3(),_fa=new V3(),_fb=new V3(),_fr=new V3(),_kf=new V3();
function rdChestFwd(o){rdV(1,_f1);rdV(0,_f2);_f1.sub(_f2);rdV(2,_f2);rdV(18,o);_f2.sub(o);return o.crossVectors(_f1,_f2)}
function rdFrame(a,b,ref,o,A){rdV(a,_fa);rdV(b,_fb);ref(_fr);frameM(_fa,_fb,_fr,o);if(A)A.copy(_fa);return o}
function rdBody(obj,a,b,ref){obj.updateMatrixWorld(true);var R=new T.Matrix4(),A=new V3();rdFrame(a,b,ref,R,A);
  RD.bodies.push({o:obj,a:a,b:b,ref:ref,M0:obj.matrixWorld.clone(),A0:A,R0t:R.transpose()})}
var _ba2=new V3();
function rdApplyBodies(){
  for(var k=0;k<RD.bodies.length;k++){var B=RD.bodies[k];rdFrame(B.a,B.b,B.ref,_rm,_ba2);
    _rm.multiply(B.R0t);_rm2.makeTranslation(-B.A0.x,-B.A0.y,-B.A0.z).premultiply(_rm);_rm2.setPosition(_rs.setFromMatrixPosition(_rm2).add(_ba2));_rm2.multiply(B.M0);
    /* parents come earlier in the list, so their world matrix is already current: no subtree walks here */
    if(B.o.parent&&B.o.parent!==scene){_rm.copy(B.o.parent.matrixWorld).invert();_rm2.premultiply(_rm)}
    _rm2.decompose(B.o.position,B.o.quaternion,B.o.scale);B.o.updateMatrix();if(B.o.parent)B.o.matrixWorld.multiplyMatrices(B.o.parent.matrixWorld,B.o.matrix);else B.o.matrixWorld.copy(B.o.matrix)}
}
/* the body is always a particle body. Set up once per fighter from the relaxed stance */
function rdInit(){
  var k,t;
  rdNeutral(RD.s1);RD.c=rdBuild(RD.s1);RD.tet=[[0,1,18,3,0],[4,5,6,18,0]];for(k=0;k<2;k++){t=RD.tet[k];t[4]=tetVol(RD.s1,t[0],t[1],t[2],t[3])>0?1:-1}
  RD.p.set(RD.s1);RD.q.set(RD.s1);
  RD.bodies=[];
  rdBody(RIG.torsoG,18,2,rdShoulders);rdBody(LEGP[0].group,6,18,rdHips);rdBody(RIG.headG,7,9,rdFace);
  rdBody(LEGP[1].group,4,14,rdHips);rdBody(LEGP[2].group,5,15,rdHips);rdBody(LEGP[3].group,14,16,rdHips);rdBody(LEGP[4].group,15,17,rdHips);
  /* which way the knees fold, remembered in the pelvis frame */
  rdFrame(6,18,rdHips,_rm);_rm2.copy(_rm).transpose();LEGP[0].group.getWorldQuaternion(_lq);RD.pf.set(0,0,1).applyQuaternion(_lq).applyMatrix4(_rm2);
  RD.live=true;RD.on=false;RD.rise=false;RD.mode=0;RD.bacc=0;RD.off.set(0,0,0);
}
/* a blow: velocity goes into the particles nearest the glove, the joints carry it through the rest of him */
var RDH=[7,8,9],RDT=[0,1,2,3,18,4,5,6],_hw=new Float32Array(8);
function rdHit(pt,dir,J,head){
  if(!RD.live||RD.mode!==0)return;
  var ids=head?RDH:RDT,n=ids.length,s=0,k,a,dx,dy,dz,f,g=head?.5:.3,e=head?.004:.02,p=RD.p,q=RD.q;
  for(k=0;k<n;k++){a=ids[k]*3;dx=p[a]-pt.x;dy=p[a+1]-pt.y;dz=p[a+2]-pt.z;_hw[k]=1/(dx*dx+dy*dy+dz*dz+e);s+=_hw[k]}
  for(k=0;k<n;k++){a=ids[k]*3;f=Math.min(2.2,_hw[k]/s*n)*J*g*PH;q[a]-=dir.x*f;q[a+1]-=dir.y*f;q[a+2]-=dir.z*f}
  /* a head shot rocks the shoulders too */
  if(head)for(k=0;k<4;k++){a=k*3;f=J*.13*PH;q[a]-=dir.x*f;q[a+1]-=dir.y*f*.5;q[a+2]-=dir.z*f}
}
var _el=[new V3(),new V3()],_hd=[new V3(),new V3()];
/* once a frame, right after the animation has posed the rig: physics moves the real body toward that pose */
function bodyPhys(dt){
  if(!RD.live)rdInit();
  var p=RD.p,q=RD.q,s=RD.s1,k,a,n=0,f;
  if(RD.mode===0){
    for(k=0;k<2;k++){_el[k].copy(FARM[k].E||F.gw[k]);_hd[k].copy(F.gw[k])}
    rdGather(s,_el,_hd);
    /* arms are IK while he stands: their particles just ride along, keeping their speed for the moment he drops */
    f=dt>0?Math.min(1,PH/dt):0;for(k=30;k<42;k++){q[k]=s[k]-(s[k]-p[k])*f;p[k]=s[k]}
    /* the pose jumped (new fighter, reset): do not drag him there */
    a=18;if(Math.abs(p[a]-s[a])+Math.abs(p[a+1]-s[a+1])+Math.abs(p[a+2]-s[a+2])>.5){p.set(s);q.set(s)}
  }
  RD.bacc+=dt;while(RD.bacc>=PH&&n<8){rdStep(PH);RD.bacc-=PH;n++}if(n>=8)RD.bacc=0;
  rdApply();
  if(RD.mode===0){RD.off.set((p[9]-s[9])*.8,(p[10]-s[10])*.8,(p[11]-s[11])*.8);poseArms(dt)}
}
/* he goes down: the muscles let go, the arms join the body, head and chest get the shove of the last punch */
function rdStart(){
  if(!RD.live)rdInit();
  if(RD.on){RD.rise=false;RD.mode=1;RD.ft=0;return}
  var k,h=1/120,a;
  for(k=0;k<2;k++){if(FARM[k].E){a=(10+k)*3;RD.q[a]+=FARM[k].E.x-RD.p[a];RD.q[a+1]+=FARM[k].E.y-RD.p[a+1];RD.q[a+2]+=FARM[k].E.z-RD.p[a+2];rdSet(RD.p,10+k,FARM[k].E)}
    a=(12+k)*3;RD.q[a]+=F.gw[k].x-RD.p[a];RD.q[a+1]+=F.gw[k].y-RD.p[a+1];RD.q[a+2]+=F.gw[k].z-RD.p[a+2];rdSet(RD.p,12+k,F.gw[k]);
    rdBody(FARM[k].up.group,k,10+k,rdChestFwd);rdBody(FARM[k].fo.group,10+k,12+k,rdChestFwd);rdBody(FARM[k].gl.group,10+k,12+k,rdChestFwd)}
  for(k=30;k<42;k++){a=RD.p[k]-RD.q[k];if(a>.03)RD.q[k]=RD.p[k]-.03;else if(a<-.03)RD.q[k]=RD.p[k]+.03}
  var J=Math.min(6,F.lastJ||3),fx=FD.x,fz=FD.z;
  function kick(i,vx,vy,vz){RD.q[i*3]-=vx*h;RD.q[i*3+1]-=vy*h;RD.q[i*3+2]-=vz*h}
  for(k=7;k<=9;k++)kick(k,fx*J*.3,.2+J*.05,fz*J*.3);
  for(k=0;k<=3;k++)kick(k,fx*J*.2,0,fz*J*.2);kick(18,fx*J*.1,-.2,fz*J*.1);
  kick(14,0,-.3,.5);kick(15,0,-.3,.5);for(k=4;k<=6;k++)kick(k,0,-.4,0);
  RD.on=true;RD.rise=false;RD.mode=1;RD.t=0;RD.ft=0;RD.thud=0;
}
var RDK=new Float32Array(RN);
function rdStep(h){
  var p=RD.p,q=RD.q,s=RD.s1,n=RN,i,a,b,it,k,c,dx,dy,dz,d,t,wa,wb,f,g=-9.8*h*h,vx,vy,vz,rise=RD.rise,st=RD.mode===0,u=0,damp=rise?.9:st?.957:.995,e,km=1,its=st?6:10;
  /* dazed or badly hurt: the muscles hold less, he sags and wobbles */
  if(st){km=(F.groggy>0||F.st==='stagger'?.5:1)*(1-.35*(1-F.hp/F.max));g*=(1-km)*.5;
    /* braced when he punches or covers up */
    if(F.st==='windup'||F.st==='strike'||F.st==='recover')km*=3.5;else if(F.st==='guard')km*=1.8}
  if(rise){RD.t+=h;u=RD.t/RD.dur;for(i=0;i<n;i++){e=clamp((u-RDDEL[i])/.34,0,1);RDK[i]=e*e*(3-2*e)}}else RD.ft+=h;
  for(i=0;i<n;i++){a=i*3;if(st){if(i>=10&&i<=13)continue;k=RDKS[i];if(k>=1){p[a]=q[a]=s[a];p[a+1]=q[a+1]=s[a+1];p[a+2]=q[a+2]=s[a+2];continue}k*=km;vx=(p[a]-q[a])*damp;vy=(p[a+1]-q[a+1])*damp;vz=(p[a+2]-q[a+2])*damp;q[a]=p[a];q[a+1]=p[a+1];q[a+2]=p[a+2];
      p[a]+=vx+(s[a]-p[a])*k;p[a+1]+=vy+g+(s[a+1]-p[a+1])*k;p[a+2]+=vz+(s[a+2]-p[a+2])*k;continue}
    k=rise?RDK[i]:0;vx=(p[a]-q[a])*damp;vy=(p[a+1]-q[a+1])*damp;vz=(p[a+2]-q[a+2])*damp;q[a]=p[a];q[a+1]=p[a+1];q[a+2]=p[a+2];p[a]+=vx;p[a+1]+=vy+g*(1-k);p[a+2]+=vz;
    /* getting up: muscles pull every joint toward where it stands, feet first, head last */
    if(k>0){dx=(s[a]-p[a])*k*.07;dy=(s[a+1]-p[a+1])*k*.07;dz=(s[a+2]-p[a+2])*k*.07;d=Math.sqrt(dx*dx+dy*dy+dz*dz);if(d>.012){f=.012/d;dx*=f;dy*=f;dz*=f}p[a]+=dx;p[a+1]+=dy;p[a+2]+=dz}}
  rdFrame(6,18,rdHips,_rm);_kf.copy(RD.pf).applyMatrix4(_rm);
  for(it=0;it<its;it++){
    for(k=0;k<RD.c.length;k++){c=RD.c[k];if(st&&c[4])continue;a=c[0]*3;b=c[1]*3;dx=p[b]-p[a];dy=p[b+1]-p[a+1];dz=p[b+2]-p[a+2];d=Math.sqrt(dx*dx+dy*dy+dz*dz)||1e-6;
      t=d<c[2]?c[2]:d>c[3]?c[3]:d;if(t===d)continue;wa=RDW[c[0]];wb=RDW[c[1]];f=(d-t)/d/(wa+wb);
      p[a]+=dx*f*wa;p[a+1]+=dy*f*wa;p[a+2]+=dz*f*wa;p[b]-=dx*f*wb;p[b+1]-=dy*f*wb;p[b+2]-=dz*f*wb}
    /* knees fold one way only */
    for(k=0;k<2;k++){var hi=(4+k)*3,kn=(14+k)*3,an=(16+k)*3,dd=(p[kn]-(p[hi]+p[an])*.5)*_kf.x+(p[kn+1]-(p[hi+1]+p[an+1])*.5)*_kf.y+(p[kn+2]-(p[hi+2]+p[an+2])*.5)*_kf.z;
      if(dd<.02){f=(.02-dd)*.5;p[kn]+=_kf.x*f;p[kn+1]+=_kf.y*f;p[kn+2]+=_kf.z*f}}
    for(i=0;i<n;i++){a=i*3;if(p[a+1]<RDR[i])p[a+1]=RDR[i]}
  }
  /* chest and pelvis must never turn inside out */
  for(k=0;k<RD.tet.length;k++){c=RD.tet[k];if(tetVol(p,c[0],c[1],c[2],c[3])*c[4]<0){d=_r1.length()||1;_r1.multiplyScalar(1/d);a=c[0]*3;b=c[3]*3;f=2*((p[b]-p[a])*_r1.x+(p[b+1]-p[a+1])*_r1.y+(p[b+2]-p[a+2])*_r1.z);
    p[b]-=_r1.x*f;p[b+1]-=_r1.y*f;p[b+2]-=_r1.z*f;q[b]=p[b];q[b+1]=p[b+1];q[b+2]=p[b+2]}}
  /* floor: friction, and the skull landing */
  for(i=0;i<n;i++){a=i*3;if(p[a+1]<=RDR[i]+.002){vy=(p[a+1]-q[a+1])/h;q[a]=p[a]-(p[a]-q[a])*.82;q[a+2]=p[a+2]-(p[a+2]-q[a+2])*.82;q[a+1]=p[a+1]+Math.max(0,-vy*h*.18);
    if(i===9&&!rise&&vy<-1.1&&RD.thud<=0){RD.thud=.35;rdHeadHit(-vy)}}}
  if(RD.thud>0)RD.thud-=h;
  /* the last moment of getting up settles exactly into the stance */
  if(rise&&u>.86){e=clamp((u-.86)/.14,0,1);e=e*e*(3-2*e);for(i=0;i<n*3;i++){d=p[i]-q[i];p[i]+=(s[i]-p[i])*e;q[i]=p[i]-d*(1-e)}}
}
function rdHeadHit(v){
  sfx.thud();vib(Math.min(120,40+v*25));if(!REDUCED)G.shake=Math.max(G.shake,Math.min(.1,.03+v*.02));
  rdV(9,_hp);for(var k=0;k<10+WOUNDS.length*3;k++)stain(_hp.x+rnd(-.12,.12),_hp.z+rnd(-.12,.12),rnd(.004,.012));
  _hp.y=.08;bloodBurst(_hp,Math.min(60,6+WOUNDS.length*4),0,.3,0,.7);if(!F.downHit){F.downHit=true;if(G.mode==='ko')sfx.bell(3)}
}
function rdSettled(){return RD.on&&!RD.rise&&RD.ft>1.1&&(RD.p[9*3+1]<.5||RD.ft>2.4)}
function rdRiseStart(){rdNeutral(RD.s1);F.sx=0;F.sv=0;RD.rise=true;RD.mode=2;RD.t=0;RD.dur=F.dur}
function rdEnd(){RD.on=false;RD.rise=false;RD.mode=0;RD.bodies.length=7;
  F.pp.set(0,-.028,0);F.ppv.set(0,0,0);F.pr.set(0,0,0);F.hr.set(0,0,0);F.hv.set(0,0,0);F.tr.set(0,0,0);F.tv.set(0,0,0);F.hpp.set(0,0,0);F.hpv.set(0,0,0);F.recoil=0;F.recoilV=0;
  F.gl[0].set(-.125,1.47,.25);F.gl[1].set(.125,1.47,.25);F.glv[0].set(0,0,0);F.glv[1].set(0,0,0)}
function rdApply(){
  rdApplyBodies();updBend();
  if(RD.mode===0){F.fa=0;return}
  for(var k=0;k<2;k++)rdV(12+k,F.gw[k]);
  if(F.downHit&&WOUNDS.length&&Math.random()<.25){rdV(9,_hp);pool(_hp.x+rnd(-.06,.06),_hp.z+rnd(-.06,.06),Math.min(.12,.02+F.t*.008))}
  rdV(2,_r1);rdV(6,_r2);_r1.sub(_r2).normalize();F.fa=Math.acos(clamp(_r1.y,-1,1));
  shadow.position.x=_r2.x;shadow.position.z=_r2.z-Math.sin(F.fa)*.35;shadow.scale.set(.75+Math.sin(F.fa)*.2,.5+Math.sin(F.fa)*1.6,1);
}
