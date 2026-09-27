namespace PhoneVr
{
    static class PhonePage
    {
        public const string Html = @"<!doctype html>
<meta name=viewport content='width=device-width,initial-scale=1,maximum-scale=1,user-scalable=no'>
<title>Phone VR</title>
<style>
  html, body { margin:0; height:100%; background:#14120f; color:#f6f1e8; font-family:Segoe UI, sans-serif; touch-action:none; overflow:hidden; }
  main { box-sizing:border-box; height:100%; padding:16px; display:flex; flex-direction:column; gap:12px; }
  h1 { font-size:1.35rem; margin:0; letter-spacing:-.02em; }
  #s { margin:0; min-height:2.4rem; color:#d9d0c3; }
  .row { display:flex; gap:10px; }
  button, .pad { font:inherit; color:#f6f1e8; background:#2a261f; border:0; border-radius:18px; }
  button { cursor:pointer; }
  #start, #center { padding:.85rem 1rem; background:#0f7a45; color:white; }
  #start:disabled { background:#3d5c49; }
  .grow { flex:1; }
  #stick { position:relative; width:42vw; max-width:220px; aspect-ratio:1; background:#221f1a; border-radius:24px; }
  #knob { position:absolute; width:64px; height:64px; margin:-32px 0 0 -32px; border-radius:50%; background:#8ddec0; left:50%; top:50%; }
  #trigger { flex:1; font-size:1.4rem; background:#245c78; }
  #trigger.on, #grip.on, .face.on { filter:brightness(1.35); }
  #grip, .face, #aim { flex:1; padding:.9rem .4rem; }
  .tiny { display:flex; gap:8px; }
</style>
<main>
  <div>
    <h1>Phone VR</h1>
    <p id=s>Tap Start and allow motion.</p>
  </div>
  <button id=start>Start</button>
  <div class=row style='flex:1; min-height:0'>
    <div id=stick><div id=knob></div></div>
    <button id=trigger class=grow>Trigger</button>
  </div>
  <div class='row tiny'>
    <button id=grip>Grip</button>
    <button id=a class=face>A</button>
    <button id=b class=face>B</button>
    <button id=menu class=face>Menu</button>
  </div>
  <div class=row>
    <button id=center style='flex:1'>Center</button>
    <button id=aim style='flex:1.4'>Aim: top of phone</button>
  </div>
</main>
<script>
const s = document.getElementById('s');
const start = document.getElementById('start');
const stick = document.getElementById('stick');
const knob = document.getElementById('knob');
let ws, alpha = 0, beta = 0, gamma = 0, screenDeg = 0, haveOri = false, abs = false;
let trigger = 0, grip = 0, a = 0, b = 0, menu = 0, click = 0, sx = 0, sy = 0, recenter = 0;
let pointTop = 1;
let stickId = null;

function screenAngle() {
  const orient = window.screen && window.screen.orientation;
  if (orient && typeof orient.angle === 'number') return orient.angle;
  return window.orientation || 0;
}
function onOri(e) {
  if (e.alpha == null && e.beta == null && e.gamma == null) return;
  haveOri = true;
  let next = e.alpha || 0;
  if (typeof e.webkitCompassHeading === 'number') next = (360 - e.webkitCompassHeading) % 360;
  alpha = next; beta = e.beta || 0; gamma = e.gamma || 0; screenDeg = screenAngle();
}
window.addEventListener('deviceorientationabsolute', (e) => { abs = true; onOri(e); }, true);
window.addEventListener('deviceorientation', (e) => { if (!abs) onOri(e); }, true);

function bindHold(el, set) {
  const down = (e) => { e.preventDefault(); set(1); el.classList.add('on'); };
  const up = () => { set(0); el.classList.remove('on'); };
  el.addEventListener('pointerdown', down);
  el.addEventListener('pointerup', up);
  el.addEventListener('pointercancel', up);
}
bindHold(document.getElementById('trigger'), (v) => trigger = v);
bindHold(document.getElementById('grip'), (v) => grip = v);
bindHold(document.getElementById('a'), (v) => a = v);
bindHold(document.getElementById('b'), (v) => b = v);
bindHold(document.getElementById('menu'), (v) => menu = v);

stick.addEventListener('pointerdown', (e) => {
  stickId = e.pointerId;
  stick.setPointerCapture(e.pointerId);
  moveStick(e);
  click = 1;
});
stick.addEventListener('pointermove', (e) => { if (e.pointerId === stickId) moveStick(e); });
function endStick(e) {
  if (e.pointerId !== stickId) return;
  stickId = null; sx = 0; sy = 0; click = 0;
  knob.style.left = '50%'; knob.style.top = '50%';
}
stick.addEventListener('pointerup', endStick);
stick.addEventListener('pointercancel', endStick);
function moveStick(e) {
  const r = stick.getBoundingClientRect();
  const cx = r.left + r.width / 2, cy = r.top + r.height / 2;
  const dx = (e.clientX - cx) / (r.width * 0.38);
  const dy = (e.clientY - cy) / (r.height * 0.38);
  sx = Math.max(-1, Math.min(1, dx));
  sy = Math.max(-1, Math.min(1, -dy));
  knob.style.left = (50 + sx * 38) + '%';
  knob.style.top = (50 - sy * 38) + '%';
}

document.getElementById('aim').onclick = () => {
  pointTop = pointTop ? 0 : 1;
  document.getElementById('aim').textContent = pointTop ? 'Aim: top of phone' : 'Aim: out the back';
};
document.getElementById('center').onclick = () => { recenter = 1; s.textContent = 'Centered. Point the phone.'; };

function send() {
  if (!ws || ws.readyState !== 1) return;
  const line = [alpha, beta, gamma, screenDeg, trigger, grip, a, b, menu, sx, sy, click, recenter, pointTop]
    .map((n) => Number(n).toFixed(4)).join(' ');
  ws.send(line);
  recenter = 0;
}
setInterval(send, 33);

start.onclick = async () => {
  start.disabled = true;
  s.textContent = 'Starting...';
  try {
    if (navigator.wakeLock) navigator.wakeLock.request('screen').catch(() => {});
    const asks = [];
    if (typeof DeviceOrientationEvent !== 'undefined' && typeof DeviceOrientationEvent.requestPermission === 'function')
      asks.push(DeviceOrientationEvent.requestPermission());
    if (typeof DeviceMotionEvent !== 'undefined' && typeof DeviceMotionEvent.requestPermission === 'function')
      asks.push(DeviceMotionEvent.requestPermission());
    if (asks.length) {
      const answers = await Promise.all(asks);
      if (answers.some((x) => x && x !== 'granted')) {
        s.textContent = 'Motion permission was denied.';
        start.disabled = false;
        return;
      }
    }
    const proto = location.protocol === 'https:' ? 'wss://' : 'ws://';
    ws = new WebSocket(proto + location.host + '/ws');
    ws.onopen = () => { s.textContent = haveOri ? 'Live. Tap Center while the phone points forward.' : 'Connected, but this browser is not sending motion.'; };
    ws.onclose = () => { s.textContent = 'Disconnected. Tap Start again.'; start.disabled = false; };
    ws.onerror = () => { s.textContent = 'Could not connect.'; start.disabled = false; };
  } catch (err) {
    s.textContent = 'Could not start motion.';
    start.disabled = false;
  }
};
</script>
";
    }
}
