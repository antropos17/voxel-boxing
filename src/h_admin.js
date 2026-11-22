
/* ---------- admin panel ---------- */
var ADM=$('adm');
function saveCheat(){try{localStorage.setItem('vb_cheat',JSON.stringify(CHEAT))}catch(e){}}
function syncAdm(){
  var b=ADM.querySelectorAll('.tg'),k,n=0;for(k=0;k<b.length;k++){var on=!!CHEAT[b[k].dataset.c];b[k].classList.toggle('on',on);b[k].setAttribute('aria-pressed',on);if(on)n++}
  b=$('admDmg').children;for(k=0;k<b.length;k++)b[k].classList.toggle('on',+b[k].dataset.d===CHEAT.dmg);
  b=$('admLvl').children;for(k=0;k<b.length;k++)b[k].classList.toggle('on',+b[k].dataset.l===G.level);
  if(CHEAT.dmg!==1)n++;
  $('admDiag').textContent='FPS '+(DG.fps?Math.round(DG.fps):Math.round(1000/ftAvg))+' · худший кадр '+Math.round(Math.max(DG.w,DG.w0))+' мс · подвисаний '+DG.st+' · чёткость '+PR.toFixed(2)+(ERRN?' · ошибок '+ERRN+': '+ERRMSG:'');
  $('admBtn').classList.toggle('on',n>0);$('cheatTag').textContent=n?'читы: '+n:'';
}
function openAdm(on){if(on&&G.mode!=='fight'&&G.mode!=='ko')return;ADM.hidden=!on;G.paused=on;if(on){setBlock(false);touches={};syncAdm()}}
$('admBtn').addEventListener('click',function(){openAdm(ADM.hidden)});
$('admClose').addEventListener('click',function(){openAdm(false)});
ADM.addEventListener('click',function(e){
  var b=e.target.closest('button');if(!b)return;
  if(b.dataset.c){var c=b.dataset.c;CHEAT[c]=!CHEAT[c];
    if(c==='mega'&&!CHEAT.mega){P.megaReady=false;P.mega=0}
    if(c==='passive'&&CHEAT.passive&&(F.st==='windup'||F.st==='strike')){setF('idle');F.next=1}
    saveCheat();syncAdm();return}
  if(b.dataset.d){CHEAT.dmg=+b.dataset.d;saveCheat();syncAdm();return}
  if(b.dataset.l){G.level=+b.dataset.l;startFight(false);openAdm(false);return}
  if(b.dataset.a)admAct(b.dataset.a);
});
function admAct(a){
  if(!F)return;var k,n;
  G.paused=false;
  if(a==='nose'){if((F.broken.nose||0)<FMAX)fracture('nose',1,{x:0,y:0,z:9})}
  else if(a==='jaw'){if((F.broken.jaw||0)<FMAX)fracture('jaw',Math.random()<.5?-1:1,{x:0,y:-7,z:6})}
  else if(a==='cheek'){if((F.broken.cheekL||0)<FMAX)fracture('cheekL',1,{x:-4.6,y:0,z:6.6});if((F.broken.cheekR||0)<FMAX)fracture('cheekR',-1,{x:4.6,y:0,z:6.6})}
  else if(a==='orb'){if((F.broken.orbL||0)<FMAX)fracture('orbL',1,{x:-3.2,y:3,z:7.4});if((F.broken.orbR||0)<FMAX)fracture('orbR',-1,{x:3.2,y:3,z:7.4})}
  else if(a==='teeth'){n=0;if(!F.jawOff)for(k=1;k<=16&&n<4;k++){var id=1+((Math.random()*16)|0);if(TEETH[id]&&!TEETH[id].out){knockTooth(HEADV,id,_ld.set(rnd(-.4,.4),.3,1));n++}}
    if(n){G.stats.teeth+=n;sfx.tooth();var tc=0;for(k=1;k<=16;k++)if(TEETH[k].out)tc++;chip('teeth','зубы −'+tc,tc>3);banner('Зубы −'+n)}}
  else if(a==='jawoff'&&F.jawOff&&F.jawP&&F.jawP.pins&&F.jawP.pins.length){FD.set(rnd(-.4,.4),-.2,1).normalize();tearJawPin(0)}
  else if(a==='jawoff'&&!F.jawOff){F.broken.jaw=2;FD.set(rnd(-.4,.4),-.2,1).normalize();var jm=['hang2','hang1'][(Math.random()*2)|0];detachJaw(FD,jm);G.stats.jaw=true;banner(jm==='hang2'?'Челюсть повисла':'Челюсть висит на лоскуте');G.freeze=.16;
    if(!CHEAT.immortal)knockout(true,'Челюсть оторвана')}
  else if(a==='guard'){if(!F.jawOff)ejectGuard(FD.set(rnd(-.3,.3),0,1))}
  else if(a==='eye'){FD.set(0,0,-1);if(!EYES[0].popped)breakTo('orbL',1,{x:-3.2,y:3,z:7.4});else if(!EYES[1].popped)breakTo('orbR',-1,{x:3.2,y:3,z:7.4})}
  else if(a==='skull'){FD.set(0,-.5,-1).normalize();var sp=surfAlong(HEADV,0,3.4,-1.2,rnd(-.3,.3),1,.95);breakTo('skull',1,sp)}
  else if(a==='flay'){FD.set(0,0,-1);var fs=[[-3.4,-1.5,6.6],[3.4,-1.5,6.6],[0,-1,8.6],[-2.8,3,7],[2.8,3,7],[0,-5.4,7.6],[0,5.8,7.4]];for(k=0;k<fs.length;k++)stripAt(HEADV,fs[k][0],fs[k][1],fs[k][2],3.5,true);chip('bone','кость наружу',true);checkIslands(HEADV,FD);banner('Лицо содрано')}
  else if(a==='burst'){FD.set(rnd(-.5,.5),-.3,-1).normalize();F.burst=false;var bp=surfAlong(HEADV,0,3.4,-1.2,rnd(-.6,.6),1,.8);headBurst(HEADV,bp.x,bp.y,bp.z);checkIslands(HEADV,FD)}
  else if(a==='gut'){FD.set(0,0,-1);breakTo('abs',1,{x:rnd(-4,4),y:12,z:10})}
  else if(a==='arm'){breakArm(F.armBroken[0]?1:0)}
  else if(a==='ribs'){FD.set(0,0,-1);if((F.broken.ribL||0)<2)breakTo('ribL',-1,{x:-11,y:24,z:6});else breakTo('ribR',1,{x:11,y:24,z:6})}
  else if(a==='kd'){FD.set(rnd(-.25,.25),.05,-1).normalize();F.lastJ=4;if(F.st!=='down'&&F.st!=='rise'&&G.mode==='fight'){var nk=CHEAT.noKD;CHEAT.noKD=false;knockdown('Нокдаун');CHEAT.noKD=nk}}
  else if(a==='smile'){F.smTime=0;foeSmile(1,3)}
  else if(a==='heal'){P.hp=100;P.st=100}
  else if(a==='fresh'){startFight(false)}
  else if(a==='next'){startFight(true)}
  openAdm(false);
}
syncAdm();
