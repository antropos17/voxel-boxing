
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
/* styles test: step frames without drawing (fixed 16.7 ms, no wall-clock meshing budget, so a seeded run repeats exactly), swing on demand */
window.__g.CHEAT=CHEAT;window.__g.STY=STY;window.__g.dodge=dodge;window.__g.startFight=function(n){startFight(n,true)};window.__g.foeSide=function(){return F.side};
window.__g.run=function(frames,fn){var rr=renderer.render,raf=window.requestAnimationFrame,t=last,i=0,mb=MESHB;MESHB=1e9;
  renderer.render=function(){scene.updateMatrixWorld();camera.updateMatrixWorld()};window.requestAnimationFrame=function(){};
  try{for(;i<frames;i++){if(fn&&fn(i)===false)break;t+=16.7;frame(t)}}finally{renderer.render=rr;window.requestAnimationFrame=raf;MESHB=mb;last=performance.now()}return i};
window.__g.swing=function(hand,kind,dur){F.hand=hand;F.kind=kind;F.feint=false;F.chain=0;F.side=hand;setF('windup',dur||.6)};
window.__g.glovePx=function(k){var v=F.gw[k].clone().project(camera);return [(v.x*.5+.5)*canvas.clientWidth,(-v.y*.5+.5)*canvas.clientHeight]};
