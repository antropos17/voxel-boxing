
window.__g={G:G,P:P,get F(){return F},get HEADV(){return HEADV},get TORSOV(){return TORSOV},aim:aim,punch:punch,hitFoe:hitFoe,fracture:fracture,detachJaw:detachJaw,
  camera:camera,renderer:renderer,RIG:RIG,pieces:pieces,WOUNDS:WOUNDS,DRIPS:DRIPS,get TEETH(){return TEETH},get EYES(){return EYES},bodies:bodies,cgeo:cgeo,
  setInspect:function(o){INSPECT=o?{pos:new T.Vector3().fromArray(o.pos),look:new T.Vector3().fromArray(o.look),fov:o.fov}:null},
  impactAt:function(sx,sy,type,side,pw){var r=aim(sx,sy);if(r.kind==='none')return 'none';var g={side:side||-1,type:type,pw:pw||1,weak:false,bloody:0};hitFoe(r,g);return r.kind},
  timing:{}};
window.__g.arms=function(on){FARM.forEach(function(a){a.up.group.visible=on;a.fo.group.visible=on;a.gl.group.visible=on})};
window.__g.pgl=function(on){PARM.forEach(function(a){a.fo.group.visible=on;a.gl.group.visible=on})};
window.__g.headPos=function(){var v=new T.Vector3();RIG.headG.getWorldPosition(v);return v.toArray()};
window.__g.cellAt=function(X,Y,Z){var v=new T.Vector3(X*0.01,Y*0.01,Z*0.01).applyMatrix4(HEADV.group.matrixWorld).project(camera);return [(v.x*.5+.5)*canvas.clientWidth,(-v.y*.5+.5)*canvas.clientHeight]};
window.__g.cellAtT=function(X,Y,Z){var v=new T.Vector3(X*0.01,Y*0.01,Z*0.01).applyMatrix4(TORSOV.group.matrixWorld).project(camera);return [(v.x*.5+.5)*canvas.clientWidth,(-v.y*.5+.5)*canvas.clientHeight]};
window.__g.newFoeT=function(l){newFoe(l)};
window.__g.megaAdd=megaAdd;window.__g.setF=function(st,d){setF(st,d)};window.__g.headScreen=headScreen;
window.__g.parmUp=function(on){PARM.forEach(function(a){a.up.group.visible=on})};
window.__g.dbg=function(){var a=new T.Vector3();return{pieces:pieces.map(function(p){return [p.g.position.toArray().map(function(v){return +v.toFixed(3)}),p.teth?p.teth.a(a).toArray().map(function(v){return +v.toFixed(3)}):null,p.rest]}),
  ropes:ROPES.map(function(r){return [r.n,r.rigid,[r.p[0],r.p[1],r.p[2]].map(function(v){return +v.toFixed(3)}),[r.p[r.n*3-3],r.p[r.n*3-2],r.p[r.n*3-1]].map(function(v){return +v.toFixed(3)})]})}};
window.__g.T=T;
window.__g.neckDbg=function(){var v=NECK,c=0,red=0;for(var i=0;i<v.n;i++){if(!v.type[i])continue;c++;if(v.rgb[i*3+1]<40)red++}return [c,red,v.chunks.length,v.chunks.map(function(k){return [!!k.geo,k.cd,k.gd]})]};
window.__g.RD=RD;window.__g.rdP=function(){var o=[];for(var i=0;i<RN;i++)o.push([+RD.p[i*3].toFixed(2),+RD.p[i*3+1].toFixed(2),+RD.p[i*3+2].toFixed(2)]);return o};
window.__g.farm=function(on){FARM.forEach(function(a){a.up.group.visible=a.fo.group.visible=a.gl.group.visible=on})};
/* profiler: wraps the per-frame and per-hit functions */
window.__prof={t:{},n:{},mx:{}};
function __wrap(name,fn){var P_=window.__prof;P_.t[name]=0;P_.n[name]=0;P_.mx[name]=0;return function(){var t0=performance.now(),r=fn.apply(this,arguments),d=performance.now()-t0;P_.t[name]+=d;P_.n[name]++;if(d>P_.mx[name])P_.mx[name]=d;return r}}
poseFoe=__wrap('poseFoe',poseFoe);updCamera=__wrap('updCamera',updCamera);updPlayer=__wrap('updPlayer',updPlayer);updRopes=__wrap('updRopes',updRopes);updPieces=__wrap('updPieces',updPieces);
updCubes=__wrap('updCubes',updCubes);piecesMesh=__wrap('piecesMesh',piecesMesh);updBleed=__wrap('updBleed',updBleed);matTick=__wrap('matTick',matTick);updHud=__wrap('updHud',updHud);rdStep=__wrap('rdStep',rdStep);rdApply=__wrap('rdApply',rdApply);
hitFoe=__wrap('hitFoe',hitFoe);impact=__wrap('impact',impact);checkIslands=__wrap('checkIslands',checkIslands);goreHit=__wrap('goreHit',goreHit);aim=__wrap('aim',aim);foeAI=__wrap('foeAI',foeAI);updBend=__wrap('updBend',updBend);legsPose=__wrap('legsPose',legsPose);gloveCollide=__wrap('gloveCollide',gloveCollide);bodyPhys=__wrap('bodyPhys',bodyPhys);poseArms=__wrap('poseArms',poseArms);rdApplyBodies=__wrap('rdApplyBodies',rdApplyBodies);rdGather=__wrap('rdGather',rdGather);
(function(){var pr=VBody.prototype;pr.process=__wrap('process',pr.process);pr.meshCh=__wrap('meshCh',pr.meshCh);pr.recol=__wrap('recol',pr.recol);var rr=renderer.render.bind(renderer);renderer.render=__wrap('render',rr)})();
window.__prof.reset=function(){var P_=window.__prof,k;for(k in P_.t){P_.t[k]=0;P_.n[k]=0;P_.mx[k]=0}P_.f0=performance.now();P_.fr=0};
window.__prof.report=function(){var P_=window.__prof,k,o={},fr=P_.n.render||1;for(k in P_.t)if(P_.n[k])o[k]=[+(P_.t[k]/fr).toFixed(3),P_.n[k],+P_.mx[k].toFixed(1)];o._frames=fr;o._info={calls:renderer.info.render.calls,tris:renderer.info.render.triangles,geo:renderer.info.memory.geometries,prog:renderer.info.programs.length,pieces:pieces.length,ropes:ROPES.length,inst:cgeo.instanceCount,dpr:renderer.getPixelRatio()};return o};
window.__g.tris=function(){var o={},tot=0;function cnt(g){var n=0;g.traverse(function(m){if(m.isMesh&&m.geometry){var gi=m.geometry;n+=(gi.index?gi.index.count:gi.attributes.position.count)/3*(gi.isInstancedBufferGeometry?gi.instanceCount||0:1)}});return Math.round(n)}
  o.head=cnt(HEADV.group);o.torso=cnt(TORSOV.group);o.neck=cnt(NECK.group);o.legs=LEGP.map(function(v){return cnt(v.group)});o.farm=FARM.map(function(a){return [cnt(a.up.group),cnt(a.fo.group),cnt(a.gl.group)]});o.parm=PARM.map(function(a){return [cnt(a.up.group),cnt(a.fo.group),cnt(a.gl.group)]});
  o.teeth=cnt(UPPERG)+cnt(JAWIN);o.scene=cnt(scene);o.headChunks=HEADV.chunks.length;o.torsoChunks=TORSOV.chunks.length;return o};
window.__g.bodyLog=function(){var e=new T.Euler().setFromQuaternion(RIG.headG.getWorldQuaternion(new T.Quaternion()),'YXZ'),h=RIG.headG.getWorldPosition(new T.Vector3()),p=RD.p,s=RD.s1;
  return [+(e.y*57.3).toFixed(0),+(e.x*57.3).toFixed(0),+(e.z*57.3).toFixed(0),+((p[27]-s[27])*100).toFixed(1),+((p[29]-s[29])*100).toFixed(1),+((p[9]-s[9])*100).toFixed(1),+((p[11]-s[11])*100).toFixed(1),+((p[18]-s[18])*100).toFixed(1),+((p[20]-s[20])*100).toFixed(1),+((p[43]-s[43])*100).toFixed(1)]};
window.__g.rdHit=rdHit;
/* headless soak: thousands of frames without drawing, random play and admin actions; reports exceptions, NaNs and slow frames */
window.__g.errs=function(){return [ERRN,ERRMSG,G.mode,!!G.building]};
window.__g.soak=function(frames,seed,opt){window.__SYNC=true;
  opt=opt||{};var s=seed||1,R=function(){return (s=(s*16807)%2147483647)/2147483647},rr=renderer.render,raf=window.requestAnimationFrame,t=performance.now(),i,log=[],slow=[],err=null,maxMs=0,sum=0,P_=window.__prof,k,a;
  renderer.render=function(){scene.updateMatrixWorld();camera.updateMatrixWorld()};window.requestAnimationFrame=function(){};
  var acts=['nose','jaw','cheek','orb','teeth','jawoff','guard','eye','skull','flay','burst','gut','arm','ribs','smile','kd','heal','fresh','next'],fights=0,kos=0,lost=0,stats={};
  try{for(i=0;i<frames;i++){
    t+=16.7;var t0=performance.now(),before={};if(opt.prof)for(k in P_.t)before[k]=P_.t[k];
    if(G.mode==='fight'){var r=R(),hs=headScreen(),x=hs[0]+(R()-.5)*130,y=hs[1]+(R()-.3)*280;
      if(r<.05){a='jab';punch('jab',x,y,x<195?-1:1,1,0,0)}
      else if(r<.11){var d=[[90,0],[-90,0],[0,-90],[0,90]][(R()*4)|0],ty=Math.abs(d[0])>0?'hook':d[1]<0?'upper':'over';a=ty;punch(ty,x,y,ty==='hook'?(d[0]>0?-1:1):(x<195?-1:1),1+R()*.3,d[0],d[1])}
      else if(r<.118){a='block';setBlock(!P.block)}
      else if(r<.126){a='dodge';dodge(R()<.5?-1:1)}
      else if(r<.13){a='mega';megaAdd(100)}
      else if(r<.13+(opt.adm||.004)){a=acts[(R()*acts.length)|0];admAct(a)}
      else a=null;if(a){log.push(i+':'+a);if(log.length>12)log.shift();stats[a]=(stats[a]||0)+1}}
    else if(G.mode==='won'||G.mode==='dead'){if(G.mode==='won')kos++;else lost++;fights++;startFight(G.mode==='won');log.push(i+':next')}
    frame(t);if(ERRN)throw new Error('frame error: '+ERRMSG);
    var ms=performance.now()-t0;sum+=ms;if(ms>maxMs)maxMs=ms;
    if(ms>(opt.slow||25)){var top=[];if(opt.prof){for(k in P_.t){var dd=P_.t[k]-before[k];if(dd>3)top.push(k+' '+dd.toFixed(0))}}slow.push([i,+ms.toFixed(0),G.mode,F.st,log.slice(-2).join(','),top.join(' ')])}
    for(k=0;k<RD.p.length;k++)if(RD.p[k]!==RD.p[k]){throw new Error('NaN in body particles at frame '+i+' p['+k+']')}
  }}catch(e){err=e.message+' | '+(e.stack||'').split('\n').slice(1,5).join(' <- ')+' | frame '+i+' mode '+G.mode+' st '+(F&&F.st)+' log '+log.join(',')}
  renderer.render=rr;window.requestAnimationFrame=raf;window.__SYNC=false;
  return {err:err,frames:i,avgMs:+(sum/Math.max(1,i)).toFixed(2),maxMs:+maxMs.toFixed(0),slow:slow.slice(0,opt.nslow||14),nslow:slow.length,fights:fights,kos:kos,lost:lost,level:G.level,pieces:pieces.length,ropes:ROPES.length,wounds:WOUNDS.length,stats:stats,mx:(function(){var o={},k;for(k in P_.mx)if(P_.mx[k]>4)o[k]=[+P_.mx[k].toFixed(0),P_.n[k],+(P_.t[k]/Math.max(1,P_.n[k])).toFixed(2)];return o})()};
};
(function(){var names=['newFoe','resetWorld','headLook','torsoLook','paintFighter','teethBuild','eyesInit','fracture','crush','spill','bloodBurst','teethHit','detachJaw','knockdown','knockout','megaHitFx','impactFx','rdHit','brainHit','spillCore','pieceFrom','pulpAt','stripAt','shatterAt','brainOut','headBurst','buildAll','paintStatic','removeCell','rawStump','neckGore','openSkull','popEye','ejectGuard','openGut','breakArm','screenSplat','popWorld','banner','chip','startFight','clearPieces','clearRopes','rdInit','trimPieces','mince','woundNear','holeAt','openFracture','breakTo','tear'],i;
for(i=0;i<names.length;i++){try{eval('if(typeof '+names[i]+'==="function")'+names[i]+'=__wrap("'+names[i]+'",'+names[i]+')')}catch(e){}}
var pr=VBody.prototype;pr.islands=__wrap('islands',pr.islands);pr.upload=__wrap('upload',pr.upload);pr.islandsFast=__wrap('islandsFast',pr.islandsFast)})();
window.__g.progs=function(){return {n:renderer.info.programs.length,ids:renderer.info.programs.map(function(p){return p.id+':'+p.usedTimes}).join(' '),mode:G.mode,pieces:pieces.length}};
window.__g.adm=function(a){admAct(a)};
window.__g.chunkTimes=function(){function tm(v){var o=[],k,c,t0;for(var r=0;r<3;r++){o=[];for(k=0;k<v.chunks.length;k++){c=v.chunks[k];if(!c.geo)continue;t0=performance.now();v.meshCh(c);o.push(+(performance.now()-t0).toFixed(1))}}return o}
  return {head:tm(HEADV),torso:tm(TORSOV),W:[HEADV.W,HEADV.H,HEADV.D,TORSOV.W,TORSOV.H,TORSOV.D]}};
/* perf: what the GPU is asked to draw, split by object (counts mirror three's own culling: visible chain, frustum, drawRange, instances) */
window.__g.perfCats=function(){
  var tag=new Map(),k,o={},fr=new T.Frustum(),pm=new T.Matrix4(),sp=new T.Sphere();
  function put(g,n){if(g)tag.set(g,n)}
  put(UPPERG,'teeth');put(JAWIN,'teeth');put(HEADV.group,'head');put(TORSOV.group,'torso');put(NECK.group,'neck');LEGP.forEach(function(v){put(v.group,'legs')});
  FARM.forEach(function(a){put(a.up.group,'foeArms');put(a.fo.group,'foeArms');put(a.gl.group,'foeGloves')});PARM.forEach(function(a){put(a.up.group,'myArms');put(a.fo.group,'myArms');put(a.gl.group,'myGloves')});
  put(cubes,'cubes');pieces.forEach(function(p){put(p.g,'pieces')});if(F&&F.guardV)put(F.guardV.group,'guard');
  scene.updateMatrixWorld();camera.updateMatrixWorld();pm.multiplyMatrices(camera.projectionMatrix,camera.matrixWorldInverse);fr.setFromProjectionMatrix(pm);
  scene.traverseVisible(function(m){if(!m.isMesh||!m.geometry)return;var g=m.geometry,n=g.index?g.index.count:g.attributes.position.count,dr=g.drawRange,c=Math.max(0,Math.min(n,dr.start+dr.count)-dr.start),inst=1;
    if(m.isInstancedMesh)inst=m.count;else if(g.isInstancedBufferGeometry)inst=g.instanceCount||0;
    if(!c||!inst)return;
    if(m.frustumCulled){if(!g.boundingSphere)g.computeBoundingSphere();sp.copy(g.boundingSphere).applyMatrix4(m.matrixWorld);if(!fr.intersectsSphere(sp))return}
    var p=m,name='arena';while(p){if(tag.has(p)){name=tag.get(p);break}p=p.parent}
    var e=o[name]||(o[name]={tris:0,calls:0,verts:0});e.tris+=Math.round(c/3*inst);e.calls++;e.verts+=g.index?Math.round(c/1.5*inst):c*inst});
  var tot={tris:0,calls:0};for(k in o){tot.tris+=o[k].tris;tot.calls+=o[k].calls}o._total=tot;return o};
/* perf: GPU-side resources: programs, textures, buffer bytes, drawing buffer */
window.__g.perfRes=function(){var gl=renderer.getContext(),seen=new Set(),bytes=0,nb=0,geo=0,tex=[];
  function ab(a){if(!a||!a.array||seen.has(a.array))return;seen.add(a.array);bytes+=a.array.byteLength;nb++}
  scene.traverse(function(m){if(m.geometry){geo++;var g=m.geometry,k;for(k in g.attributes)ab(g.attributes[k]);ab(g.index)}
    var mt=m.material;if(mt&&mt.map&&tex.indexOf(mt.map)<0)tex.push(mt.map)});
  return {programs:renderer.info.programs.length,geometries:renderer.info.memory.geometries,textures:renderer.info.memory.textures,sceneGeo:geo,buffers:nb,bufferMB:+(bytes/1048576).toFixed(2),
    tex:tex.map(function(t){var i=t.image;return i?(i.width+'x'+i.height):'?'}).join(','),pr:+PR.toFixed(2),prmax:PRMAX,dbuf:gl.drawingBufferWidth+'x'+gl.drawingBufferHeight,mpix:+(gl.drawingBufferWidth*gl.drawingBufferHeight/1e6).toFixed(2),
    gpu:(function(){var e=gl.getExtension('WEBGL_debug_renderer_info');return e?gl.getParameter(e.UNMASKED_RENDERER_WEBGL):gl.getParameter(gl.RENDERER)})(),webgl2:renderer.capabilities.isWebGL2}};
/* perf: CPU memory held by voxel bodies (typed arrays) and loose pieces */
window.__g.perfMem=function(){var seen=new Set(),tot=0,n=0;function walk(v){if(!v)return;for(var k in v){var a=v[k];if(a&&ArrayBuffer.isView(a)&&!seen.has(a.buffer)){seen.add(a.buffer);tot+=a.buffer.byteLength;n++}}}
  [HEADV,TORSOV,NECK].concat(LEGP).forEach(walk);FARM.concat(PARM).forEach(function(a){walk(a.up);walk(a.fo);walk(a.gl)});pieces.forEach(function(p){if(p)walk(p.v)});TEETH.forEach(function(t){if(t)walk(t.v)});
  return {voxArrays:n,voxMB:+(tot/1048576).toFixed(1),heapMB:performance.memory?+(performance.memory.usedJSHeapSize/1048576).toFixed(1):null,pieces:pieces.length,ropes:ROPES.length,wounds:WOUNDS.length,drips:DRIPS.length,inst:cgeo.instanceCount,geometries:renderer.info.memory.geometries,textures:renderer.info.memory.textures,programs:renderer.info.programs.length}};
/* perf: GPU bench inside one task: n renders, each forced to finish by a 1-pixel read; and the same renders without the wait (CPU submit only) */
window.__g.gpuBench=function(n,opt){opt=opt||{};var gl=renderer.getContext(),px=new Uint8Array(4),i,t0,sub=0,tot=0,hid=[],k,old=PR;
  if(opt.pr)renderer.setPixelRatio(opt.pr);
  if(opt.hide)opt.hide.forEach(function(nm){var gs={head:[HEADV.group],torso:[TORSOV.group],legs:LEGP.map(function(v){return v.group}),arms:[].concat.apply([],FARM.concat(PARM).map(function(a){return [a.up.group,a.fo.group,a.gl.group]})),cubes:[cubes]}[nm]||[];gs.forEach(function(g){hid.push([g,g.visible]);g.visible=false})});
  cullAll();renderer.render(scene,camera);gl.readPixels(0,0,1,1,gl.RGBA,gl.UNSIGNED_BYTE,px);
  var ts=[];for(i=0;i<n;i++){t0=performance.now();renderer.render(scene,camera);var t1=performance.now();gl.readPixels(0,0,1,1,gl.RGBA,gl.UNSIGNED_BYTE,px);var t2=performance.now();sub+=t1-t0;ts.push(t2-t0)}
  var info={tris:renderer.info.render.triangles,calls:renderer.info.render.calls,dbuf:gl.drawingBufferWidth+'x'+gl.drawingBufferHeight};
  hid.forEach(function(h){h[0].visible=h[1]});if(opt.pr)renderer.setPixelRatio(old);
  ts.sort(function(a,b){return a-b});
  return {med:+ts[n>>1].toFixed(2),p90:+ts[(n*.9)|0].toFixed(2),submit:+(sub/n).toFixed(2),tris:info.tris,calls:info.calls,dbuf:info.dbuf}};
window.__g.noAdapt=function(pr){prOff=true;if(pr)setPR(pr)};
/* perf: per-frame log of step() with the functions that cost most inside long frames */
(function(){var P_=window.__prof,before={};P_.rec=null;
  var st=step;step=function(now){if(window.__man&&!window.__inStep)return;if(!P_.rec)return st(now);var k,t0=performance.now();for(k in P_.t)before[k]=P_.t[k];st(now);var d=performance.now()-t0;P_.rec.push(+d.toFixed(2));
    if(d>(P_.recSlow||16)){var top=[];for(k in P_.t){var dd=P_.t[k]-before[k];if(dd>1&&k!=='render'||k==='render'&&dd>2)top.push([k,+dd.toFixed(1)])}top.sort(function(a,b){return b[1]-a[1]});P_.slow.push([+d.toFixed(1),G.mode,F&&F.st,top.slice(0,6).map(function(x){return x[0]+' '+x[1]}).join(', ')])}};
  P_.startRec=function(slow){P_.rec=[];P_.slow=[];P_.recSlow=slow||16};P_.stopRec=function(){var r={ms:P_.rec,slow:P_.slow};P_.rec=null;return r}})();
(function(){var c=renderer.compile.bind(renderer);renderer.compile=__wrap('compile',c)})();
/* perf: reference copy of the v19 mesher; __g.meshCheck() rebuilds every chunk with both and compares the buffers byte for byte */
VBody.prototype.cornerRef=function(X,Y,Z){
  var W=this.W,H=this.H,D=this.D,type=this.type,sw=this.sw,dx=this.dx,dy=this.dy,dz=this.dz,nx=this.nx,ny=this.ny,nz=this.nz;
  var cnt=0,ox=0,oy=0,oz=0,mx=0,ax=0,ay=0,az=0,xx,yy,zz,j,w;
  for(zz=Z-1;zz<=Z;zz++){if(zz<0||zz>=D)continue;
    for(yy=Y-1;yy<=Y;yy++){if(yy<0||yy>=H)continue;
      for(xx=X-1;xx<=X;xx++){if(xx<0||xx>=W)continue;
        j=xx+W*(yy+H*zz);if(!type[j])continue;
        cnt++;ox+=dx[j];oy+=dy[j];oz+=dz[j];
        w=sw[j];if(w){if(w>mx)mx=w;ax+=nx[j]*w;ay+=ny[j]*w;az+=nz[j]*w}
  }}}
  var f=cnt?this.s/(16*cnt):0;CD[0]=ox*f;CD[1]=oy*f;CD[2]=oz*f;CD[3]=mx*0.01;
  var l=Math.sqrt(ax*ax+ay*ay+az*az)||1;CD[4]=ax/l;CD[5]=ay/l;CD[6]=az/l;
};
VBody.prototype.snormRef=function(X,Y,Z,c){
  var cs=this.cs+1,key=(X-c.x0)+cs*((Y-c.y0)+cs*(Z-c.z0)),o=key*3;
  if(NCS[key]===NCT){SN[0]=NCN[o];SN[1]=NCN[o+1];SN[2]=NCN[o+2];return}
  var W=this.W,H=this.H,D=this.D,type=this.type,gx=0,gy=0,gz=0,a,b,cc,xx,yy,zz,ox,oy,oz,w;
  for(cc=-2;cc<2;cc++){zz=Z+cc;oz=cc+0.5;for(b=-2;b<2;b++){yy=Y+b;oy=b+0.5;for(a=-2;a<2;a++){xx=X+a;ox=a+0.5;
    if(xx<0||yy<0||zz<0||xx>=W||yy>=H||zz>=D||!type[xx+W*(yy+H*zz)]){w=1/(ox*ox+oy*oy+oz*oz);gx+=ox*w;gy+=oy*w;gz+=oz*w}}}}
  var l=Math.sqrt(gx*gx+gy*gy+gz*gz);if(l>1e-6){gx/=l;gy/=l;gz/=l}else{gx=gy=gz=0}
  NCN[o]=SN[0]=gx;NCN[o+1]=SN[1]=gy;NCN[o+2]=SN[2]=gz;NCS[key]=NCT;
};
VBody.prototype.meshChRef=function(c){
  var W=this.W,H=this.H,D=this.D,type=this.type,fl=this.fl,s=this.s,OX=this.ox,OY=this.oy,OZ=this.oz,dyn=this.dyn,def=this.anyDef&&dyn,jf=this.jawFn;
  var aol=this.aol,q=0,x,y,z,i,t,d,dd,nX,nY,nZ,uX,uY,uZ,vX,vY,vZ,fx,fy,fz,fi,k,a,b,du,dv,gx,gy,gz,bX,bY,bZ,X,Y,Z,o,o4,fli,hc,s1,s2,s3,ao,a0=0,a1=0,a2=0,a3=0,px,py,pz,mx,my,mz,ml,jw,g=GN,eyef=0,skf=0,jhard=0,sm=this.sm,k2;
  NCT++;
  for(z=c.z0;z<c.z1;z++)for(y=c.y0;y<c.y1;y++){
    i=c.x0+W*(y+H*z);
    for(x=c.x0;x<c.x1;x++,i++){
      t=type[i];if(!t)continue;
      fli=fl[i];hc=false;
      for(d=0;d<6;d++){
        dd=DIRS[d];nX=dd[0];nY=dd[1];nZ=dd[2];fx=x+nX;fy=y+nY;fz=z+nZ;
        if(fx>=0&&fy>=0&&fz>=0&&fx<W&&fy<H&&fz<D){fi=fx+W*(fy+H*fz);if(type[fi]&&!(dyn&&(fli&F_JAWH)&&(fl[fi]&F_JAWH)&&((fli^fl[fi])&F_JAWL)))continue}
        if(q>=SQN)scratch(q+1);
        if(!hc){this.colorOf(i,CC);hc=true;
          if(dyn){eyef=(fli&F_EYE)?255:t===GUM?128:0;skf=(fli&F_SKIN)?255:0;jhard=(fli&F_JAWH)?((fli&F_JAWL)?255:0):-1}}
        uX=dd[3];uY=dd[4];uZ=dd[5];vX=dd[6];vY=dd[7];vZ=dd[8];
        for(dv=-1;dv<=1;dv++)for(du=-1;du<=1;du++){
          if(!du&&!dv)continue;
          gx=fx+du*uX+dv*vX;gy=fy+du*uY+dv*vY;gz=fz+du*uZ+dv*vZ;
          g[(du+1)+(dv+1)*3]=(gx>=0&&gy>=0&&gz>=0&&gx<W&&gy<H&&gz<D&&type[gx+W*(gy+H*gz)])?1:0;
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
          if(sm>0){this.snormRef(X,Y,Z,c);if(SN[0]||SN[1]||SN[2]){mx=nX*(1-sm)+SN[0]*sm;my=nY*(1-sm)+SN[1]*sm;mz=nZ*(1-sm)+SN[2]*sm;ml=Math.sqrt(mx*mx+my*my+mz*mz)||1;mx/=ml;my/=ml;mz/=ml}}
          if(def){k2=(X-c.x0)+33*((Y-c.y0)+33*(Z-c.z0));if(CDS[k2]===NCT){k2*=7;CD[0]=CDC[k2];CD[1]=CDC[k2+1];CD[2]=CDC[k2+2];CD[3]=CDC[k2+3];CD[4]=CDC[k2+4];CD[5]=CDC[k2+5];CD[6]=CDC[k2+6]}
            else{this.cornerRef(X,Y,Z);CDS[k2]=NCT;k2*=7;CDC[k2]=CD[0];CDC[k2+1]=CD[1];CDC[k2+2]=CD[2];CDC[k2+3]=CD[3];CDC[k2+4]=CD[4];CDC[k2+5]=CD[5];CDC[k2+6]=CD[6]}
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
window.__g.meshCheck=function(){var bodies_=[HEADV,TORSOV,NECK].concat(LEGP),bad=[],n=0,k,b,c,A,B,j,nm,snap;
  FARM.concat(PARM).forEach(function(a){bodies_.push(a.up,a.fo,a.gl)});pieces.forEach(function(p){if(p)bodies_.push(p.v)});TEETH.forEach(function(t){if(t)bodies_.push(t.v)});
  function grab(c){var at=c.geo.attributes,o={nq:c.nq};['position','normal','aCol','aAux'].forEach(function(nm){o[nm]=at[nm].array.slice(0,c.nq*(nm==='position'||nm==='normal'?12:16))});o.idx=c.geo.index.array.slice(0,c.nq*6);o.qc=c.qc.slice(0,c.nq);o.gs=c.gs?c.gs.slice():null;return o}
  for(b=0;b<bodies_.length;b++){var v=bodies_[b];for(k=0;k<v.chunks.length;k++){c=v.chunks[k];if(!c.geo)continue;
    v.meshChRef(c);A=grab(c);v.meshCh(c);B=grab(c);n++;
    if(A.nq!==B.nq){bad.push([b,k,'nq',A.nq,B.nq]);continue}
    for(nm in A){if(!A[nm])continue;var x=A[nm],y=B[nm];if(x.length!==y.length){bad.push([b,k,nm,'len']);break}for(j=0;j<x.length;j++)if(x[j]!==y[j]&&!(x[j]!==x[j]&&y[j]!==y[j])){bad.push([b,k,nm,j,x[j],y[j]]);break}}}}
  return {chunks:n,bad:bad.slice(0,10),nbad:bad.length}};
/* perf: min-of-N timings that survive a busy machine: render submit, cullAll, meshing per body, recolour, island search */
window.__g.microBench=function(reps){reps=reps||5;var gl=renderer.getContext(),px=new Uint8Array(4),o={},i,t,k,c,mn,ts;
  function minOf(fn,n){var m=1e9,a=[];for(var r=0;r<n;r++){var t0=performance.now();fn();var d=performance.now()-t0;a.push(d);if(d<m)m=d}a.sort(function(x,y){return x-y});return [+m.toFixed(3),+a[a.length>>1].toFixed(3)]}
  ts=[];for(i=0;i<40;i++){gl.readPixels(0,0,1,1,gl.RGBA,gl.UNSIGNED_BYTE,px);t=performance.now();renderer.render(scene,camera);ts.push(performance.now()-t)}ts.sort(function(a,b){return a-b});o.renderJS=[+ts[0].toFixed(3),+ts[20].toFixed(3)];
  o.cullAll=minOf(cullAll,40);
  function body(v){var tot=1e9,mx=0,r,s,d;for(r=0;r<reps;r++){s=0;for(k=0;k<v.chunks.length;k++){c=v.chunks[k];if(!c.geo)continue;t=performance.now();v.meshCh(c);d=performance.now()-t;s+=d;if(r===reps-1&&d>mx)mx=d}if(s<tot)tot=s}
    var rc=minOf(function(){for(k=0;k<v.chunks.length;k++)v.recol(v.chunks[k])},reps);return {mesh:+tot.toFixed(2),worstChunk:+mx.toFixed(2),recol:rc[0],quads:v.chunks.reduce(function(a,c){return a+(c.nq||0)},0)}}
  o.head=body(HEADV);o.torso=body(TORSOV);o.gloveP=body(PARM[0].gl);o.legs=body(LEGP[1]);
  o.islandsHead=minOf(function(){HEADV.islands()},reps)[0];o.islandsTorso=minOf(function(){TORSOV.islands()},reps)[0];
  return o};
/* perf: manual frame stepping for repeatable screenshots (with a seeded Math.random from the test): the real rAF loop stops, frames advance by exactly 16.7 ms */
window.__g.manual=function(seed){window.__man=true;var r=seed||12345;Math.random=function(){return (r=(r*16807)%2147483647)/2147483647};
  window.requestAnimationFrame=function(){return 0};MESHB=1e9;G.anim=0;clockT=0;newFoe(G.level);G.pristine=0;beginFight(false);G.tipT=0;F.moveT=99;window.__mt=performance.now();last=window.__mt};
if(window.__MANUAL){window.__man=true;window.requestAnimationFrame=function(){return 0}}
window.__g.stepN=function(n){window.__inStep=true;for(var i=0;i<n;i++){window.__mt+=16.7;frame(window.__mt)}window.__inStep=false;return [G.mode,F.st,ERRN]};
/* perf: v19 mesher against the current one on the same chunks, interleaved, min of reps (ms for the whole body) */
window.__g.meshAB=function(reps){reps=reps||7;var o={};[['head',HEADV],['torso',TORSOV],['glove',PARM[0].gl],['legs',LEGP[0]]].forEach(function(e){var v=e[1],a=1e9,b=1e9,r,k,t,s;
  for(r=0;r<reps;r++){t=performance.now();for(k=0;k<v.chunks.length;k++)if(v.chunks[k].geo)v.meshChRef(v.chunks[k]);s=performance.now()-t;if(s<a)a=s;
    t=performance.now();for(k=0;k<v.chunks.length;k++)if(v.chunks[k].geo)v.meshCh(v.chunks[k]);s=performance.now()-t;if(s<b)b=s}
  o[e[0]]=[+a.toFixed(1),+b.toFixed(1),+(a/b).toFixed(2)]});return o};
window.__g.scene=scene;
/* styles test: step frames without drawing (fixed 16.7 ms, no wall-clock meshing budget, so a seeded run repeats exactly), swing on demand */
window.__g.CHEAT=CHEAT;window.__g.STY=STY;window.__g.dodge=dodge;window.__g.startFight=function(n){startFight(n,true)};window.__g.foeSide=function(){return F.side};
window.__g.run=function(frames,fn,ms){ms=ms||16.7;var rr=renderer.render,raf=window.requestAnimationFrame,t=last,i=0,mb=MESHB;MESHB=1e9;
  renderer.render=function(){scene.updateMatrixWorld();camera.updateMatrixWorld()};window.requestAnimationFrame=function(){};
  try{for(;i<frames;i++){if(fn&&fn(i)===false)break;t+=ms;frame(t)}}finally{renderer.render=rr;window.requestAnimationFrame=raf;MESHB=mb;last=performance.now()}return i};
window.__g.swing=function(hand,kind,dur){F.hand=hand;F.kind=kind;F.feint=false;F.early=false;F.chain=0;F.side=hand;setF('windup',dur||.6)};
window.__g.glovePx=function(k){var v=F.gw[k].clone().project(camera);return [(v.x*.5+.5)*canvas.clientWidth,(-v.y*.5+.5)*canvas.clientHeight]};
