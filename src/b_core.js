(function(){
'use strict';
var $=function(id){return document.getElementById(id)};
if(!window.THREE){$('fail').style.display='flex';$('start').hidden=true;return}
var T=THREE;
var REDUCED=!!(window.matchMedia&&matchMedia('(prefers-reduced-motion:reduce)').matches);

/* ---------- helpers ---------- */
function rnd(a,b){return b===undefined?Math.random()*a:a+Math.random()*(b-a)}
function clamp(v,a,b){return v<a?a:v>b?b:v}
function sstep(e0,e1,x){var t=(x-e0)/(e1-e0);t=t<0?0:t>1?1:t;return t*t*(3-2*t)}
function hash3(x,y,z){var h=(x*374761393+y*668265263+z*2147483647)^0x5bd1e995;h=Math.imul(h^(h>>>13),1274126177);return((h^(h>>>16))>>>0)/4294967295}
function hash1(i){var h=Math.imul(i^0x9e3779b9,0x85ebca6b);h=Math.imul(h^(h>>>13),0xc2b2ae35);return((h^(h>>>16))>>>0)/4294967295}
function col(h){return new T.Color(h)}
function vib(ms){try{if(navigator.vibrate)navigator.vibrate(ms)}catch(e){}}
function retrig(el,cls){el.classList.remove(cls);void el.offsetWidth;el.classList.add(cls)}
function inEll(x,y,z,a,b,c){x/=a;y/=b;z/=c;return x*x+y*y+z*z<1}
function gs(dx,dy,dz,r){return Math.exp(-(dx*dx+dy*dy+dz*dz)/(r*r))}

/* ---------- signed distances, centimetres ---------- */
function sdEll(px,py,pz,rx,ry,rz){var a=px/rx,b=py/ry,c=pz/rz,k0=Math.sqrt(a*a+b*b+c*c);a/=rx;b/=ry;c/=rz;var k1=Math.sqrt(a*a+b*b+c*c);return k1<1e-9?-Math.min(rx,ry,rz):k0*(k0-1)/k1}
function sdCap(px,py,pz,ax,ay,az,bx,by,bz,r,r2){var pax=px-ax,pay=py-ay,paz=pz-az,bax=bx-ax,bay=by-ay,baz=bz-az,h=(pax*bax+pay*bay+paz*baz)/(bax*bax+bay*bay+baz*baz);h=h<0?0:h>1?1:h;pax-=bax*h;pay-=bay*h;paz-=baz*h;return Math.sqrt(pax*pax+pay*pay+paz*paz)-(r2===undefined?r:r+(r2-r)*h)}
function smin(a,b,k){var h=k-Math.abs(a-b);return h>0?Math.min(a,b)-h*h*0.25/k:Math.min(a,b)}
function smax(a,b,k){var h=k-Math.abs(a-b);return h>0?Math.max(a,b)+h*h*0.25/k:Math.max(a,b)}

/* ---------- renderer, scene, light ---------- */
var canvas=$('c'),renderer;
try{renderer=new T.WebGLRenderer({canvas:canvas,antialias:false,powerPreference:'high-performance'})}
catch(e){$('fail').style.display='flex';$('start').hidden=true;return}
var PRMAX=Math.min(window.devicePixelRatio||1,2),PR=PRMAX;
renderer.setPixelRatio(PR);
var BGC=0x0d0a11,FOGN=3.0,FOGF=15;
var scene=new T.Scene();scene.background=col(BGC);scene.fog=new T.Fog(BGC,FOGN,FOGF);
var camera=new T.PerspectiveCamera(50,1,0.02,40);
var LT={sky:new T.Color(0.64,0.6,0.58),gnd:new T.Color(0.15,0.12,0.17),
  d1:new T.Vector3(0.32,1,0.7).normalize(),c1:new T.Color(1,0.95,0.88).multiplyScalar(0.72),
  d2:new T.Vector3(-0.55,0.42,-1).normalize(),c2:new T.Color(0.45,0.55,1).multiplyScalar(0.5),
  d3:new T.Vector3(-0.2,-0.3,1).normalize(),c3:new T.Color(1,0.7,0.5).multiplyScalar(0.13)};
scene.add(new T.HemisphereLight(LT.sky,LT.gnd,1));
[[LT.d1,LT.c1],[LT.d2,LT.c2],[LT.d3,LT.c3]].forEach(function(l){var d=new T.DirectionalLight(l[1],1);d.position.copy(l[0]);scene.add(d)});
var SU={uSky:{value:LT.sky},uGnd:{value:LT.gnd},uL1d:{value:LT.d1},uL1c:{value:LT.c1},uL2d:{value:LT.d2},uL2c:{value:LT.c2},uL3d:{value:LT.d3},uL3c:{value:LT.c3},
  uFogC:{value:scene.fog.color},uFog:{value:new T.Vector2(FOGN,FOGF)},uSweat:{value:0}};

/* shared lighting: wrapped diffuse, a warm terminator on skin, and gloss that grows with wetness (blood, sweat, eyes) */
var GL_LIGHT=[
'uniform vec3 uSky;uniform vec3 uGnd;uniform vec3 uL1d;uniform vec3 uL1c;uniform vec3 uL2d;uniform vec3 uL2c;uniform vec3 uL3d;uniform vec3 uL3c;uniform vec3 uFogC;uniform float uSweat;',
'vec3 shade(vec3 col,vec3 N,vec3 V,float wet,float skin,float fog){',
' float d1=dot(N,uL1d);',
' vec3 c=col*(mix(uGnd,uSky,N.y*0.5+0.5)+uL1c*max((d1+0.25)/1.25,0.0)+uL2c*max(dot(N,uL2d),0.0)+uL3c*max(dot(N,uL3d),0.0));',
' c+=skin*col*vec3(0.42,0.09,0.03)*max(0.0,0.6-abs(d1));',
' float w=clamp(wet+skin*uSweat,0.0,1.0);',
' c+=uL1c*pow(max(dot(N,normalize(uL1d+V)),0.0),16.0+56.0*w)*w*1.1;',
' c+=uL2c*pow(max(dot(N,normalize(uL2d+V)),0.0),30.0)*w*0.7;',
' return mix(c,uFogC,fog);',
'}'].join('\n');

/* voxel meshes: the glove dent and ripple and the jaw hinge are done here, so a hit costs no CPU per frame */
var VOX_VS=[GL_LIGHT,
'uniform vec3 uLid;uniform float uBlink;uniform vec4 uGlow;',
'uniform vec4 uHitP[4];uniform vec4 uHitD[4];uniform vec4 uHitR[4];uniform vec4 uJaw;uniform vec4 uSmile;uniform mat4 uB2;uniform vec3 uBend;uniform vec3 uCen;uniform vec2 uFog;',
'attribute vec4 aCol;attribute vec4 aAux;',
'varying vec3 vCol;',
'void main(){',
' vec3 p=position;vec3 n=normal;',
' float jw=aAux.y;',
/* smile: lips part and corners rise, cheeks bunch; gums (aux 0.5) stay put so the teeth behind show */
'#ifdef HEAD',
' if(uSmile.x>0.001){',
'  float st=step(0.3,aAux.z)*step(aAux.z,0.7);',
'  vec3 m=(p-vec3(0.0,-0.052,0.088))/vec3(0.034,0.024,0.026);float w=exp(-dot(m,m)*1.4)*(1.0-st);',
'  float cx=min(abs(p.x)/0.024,1.3);float sx=sign(p.x);float lo=step(0.5,jw);',
'  float c2=min(cx*cx,1.0);vec3 d=vec3(sx*0.003*cx,mix(0.0072*(1.0-c2)+0.0036*c2,-0.0038*(1.0-c2)+0.0036*c2,lo),0.0);',
'  p+=d*w*uSmile.x;',
'  vec3 c=vec3(abs(p.x)-0.038,p.y+0.024,p.z-0.07)/0.02;float wc=exp(-dot(c,c))*(1.0-st);p.y+=uSmile.x*0.003*wc;p.z+=uSmile.x*0.0018*wc;',
' }',
' if(jw>0.002){float a=uJaw.x*jw;float a2=a*a;float c=1.0-a2*0.5+a2*a2*0.0416667;float s=a*(1.0-a2*0.1666667);vec2 q=p.yz-uJaw.zw;',
'  p.yz=uJaw.zw+vec2(c*q.x-s*q.y,s*q.x+c*q.y);n.yz=vec2(c*n.y-s*n.z,s*n.y+c*n.z);p.x+=uJaw.y*jw;}',
'#endif',
'#ifdef HITS',
' for(int k=0;k<4;k++){',
'  float t=uHitP[k].w;',
'  if(t>=0.0&&t<1.6){',
'   vec3 d=p-uHitP[k].xyz;float r2=dot(d,d);float sg=uHitR[k].x;',
'   p+=uHitD[k].xyz*(uHitD[k].w*exp(-r2/(2.0*sg*sg))*exp(-t*6.5)*cos(t*21.0));',
'   float r=sqrt(r2);',
/* jelly: a ring wave runs out from the impact across the skin and fades */
'   if(r<uHitR[k].z){vec3 o=p-uCen;float fr=clamp((t*0.36-r)/0.025+1.0,0.0,1.0);float fall=1.0-r/uHitR[k].z;',
'    p+=o*(uHitR[k].y*fr*exp(-t*3.2)*sin(r*uHitR[k].w-t*30.0)*fall*sqrt(fall)/max(length(o),0.0001));}',
'  }',
' }',
'#endif',
/* soft joint: vertices blend along y between this mesh's own bone and a second one (neck: chest->head, belly: chest->pelvis) */
'#ifdef BEND',
' if(uBend.z!=0.0){float bw=smoothstep(uBend.x,uBend.y,p.y);if(uBend.z<0.0)bw=1.0-bw;',
'  if(bw>0.0){p=mix(p,(uB2*vec4(p,1.0)).xyz,bw);n=normalize(mix(n,mat3(uB2)*n,bw));}}',
'#endif',
' vec4 wp=modelMatrix*vec4(p,1.0);',
' vec3 V=normalize(cameraPosition-wp.xyz);vec3 N=normalize(mat3(modelMatrix)*n);',
' vec4 mv=viewMatrix*wp;',
' float bl=step(0.75,aAux.z)*uBlink;',
' vec3 c=shade(mix(aCol.rgb*aAux.x,uLid,bl),N,V,aCol.a*(1.0-bl),aAux.w+bl,smoothstep(uFog.x,uFog.y,-mv.z));',
' if(uGlow.a>0.001){float r=1.0-max(dot(N,V),0.0);c+=uGlow.rgb*uGlow.a*(0.28+0.9*r*r);}',
' vCol=c;',
' gl_Position=projectionMatrix*mv;',
'}'].join('\n');
var VOX_FS='varying vec3 vCol;void main(){gl_FragColor=vec4(vCol,1.0);}';
/* particles and loose chunks: one instanced draw, each cube has position, rotation, non-uniform scale and colour */
var CUBE_VS=[GL_LIGHT,
'attribute vec3 iPos;attribute vec4 iQ;attribute vec3 iS;attribute vec4 iC;uniform vec2 uFog;',
'varying vec3 vCol;',
'vec3 qrot(vec4 q,vec3 v){return v+2.0*cross(q.xyz,cross(q.xyz,v)+q.w*v);}',
'void main(){',
' vec3 p=qrot(iQ,position*iS)+iPos;',
' vec4 mv=viewMatrix*vec4(p,1.0);',
' vCol=iC.a>0.999?iC.rgb:shade(iC.rgb,normalize(qrot(iQ,normal)),normalize(cameraPosition-p),iC.a,0.0,smoothstep(uFog.x,uFog.y,-mv.z));',
' gl_Position=projectionMatrix*mv;',
'}'].join('\n');
var CUBE_FS='varying vec3 vCol;void main(){gl_FragColor=vec4(vCol,1.0);}';

/* shader variants: only the head carries the jaw and smile code, only bodies with a live dent carry the hit loop, only the belly and neck bend */
function voxMat(defs,share){
  var P=[],D=[],R=[],k;for(k=0;k<4;k++){P.push(new T.Vector4(0,0,0,-1));D.push(new T.Vector4());R.push(new T.Vector4(0.02,0,0.08,160))}
  if(share)return new T.ShaderMaterial({uniforms:share.uniforms,vertexShader:VOX_VS,fragmentShader:VOX_FS,defines:defs||{}});
  var u={uHitP:{value:P},uHitD:{value:D},uHitR:{value:R},uJaw:{value:new T.Vector4()},uCen:{value:new T.Vector3()},uLid:{value:new T.Color(0.75,0.55,0.45)},uBlink:{value:0},uGlow:{value:new T.Vector4()},uSmile:{value:new T.Vector4()},uB2:{value:new T.Matrix4()},uBend:{value:new T.Vector3(0,1,0)}};
  for(k in SU)u[k]=SU[k];
  return new T.ShaderMaterial({uniforms:u,vertexShader:VOX_VS,fragmentShader:VOX_FS,defines:defs||{}});
}
var STATMAT=voxMat();
var CUBEMAT=(function(){var u={},k;for(k in SU)u[k]=SU[k];return new T.ShaderMaterial({uniforms:u,vertexShader:CUBE_VS,fragmentShader:CUBE_FS})})();
