import { PreviewConnection } from './preview-connection.js';

const ja = navigator.language.startsWith('ja');
const words = ja ? {
  instructions: 'Beutlに表示された6桁のコードを入力してください。', code: '接続コード', connect: '接続する',
  play: '再生', pause: '一時停止', previous: '1コマ戻る', next: '1コマ進む', seek: '再生位置',
  fullscreen: '全画面', exitFullscreen: '全画面を終了',
  connecting: '接続しています…', reconnecting: '再接続しています…',
  invalid: 'コードが違うか、有効期限が切れています。', limited: '1分後にもう一度お試しください。',
  full: '接続台数の上限です。Beutlで端末を切断してください。',
  ended: '共有は終了しました。Beutlで共有を開始し直してください。',
  audioOn: '音声を有効にする', audioOff: 'ミュート', audioFailed: '音声ボタンを押すと再生できます。',
  controlFailed: '操作できませんでした。Beutlの状態を確認してください。',
  error: '配信が停止しました。Beutlで共有を開始し直してください。',
  unsupported: 'このブラウザはプレビューに対応していません。'
} : {
  instructions: 'Enter the 6-digit code shown in Beutl.', code: 'Connection code', connect: 'Connect',
  play: 'Play', pause: 'Pause', previous: 'Previous frame', next: 'Next frame', seek: 'Playback position',
  fullscreen: 'Full screen', exitFullscreen: 'Exit full screen',
  connecting: 'Connecting…', reconnecting: 'Reconnecting…',
  invalid: 'The code is incorrect or expired.', limited: 'Try again in one minute.',
  full: 'Device limit reached. Disconnect a device in Beutl.',
  ended: 'Sharing ended. Start sharing again in Beutl.',
  audioOn: 'Enable audio', audioOff: 'Mute', audioFailed: 'Tap the audio button to start playback.',
  controlFailed: 'Could not apply the control. Check Beutl on your computer.',
  error: 'Preview stopped. Start sharing again in Beutl.', unsupported: 'This browser cannot play the preview.'
};
const $ = selector => document.querySelector(selector);
const pairing = $('#pairing'), viewer = $('#viewer'), video = $('#frame'), notice = $('#connection');
const controls = $('#controls'), seek = $('#seek'), play = $('#play'), audio = $('#audio'), full = $('#fullscreen');
const timeDisplay = $('#time'), rangeDisplay = $('#range'), remoteButtons = document.querySelectorAll('[data-remote]');
let connected = false, busy = false, seeking = false, wantsAudio = false, hideTimer;
let playback = { playing: false, position: 0, minimum: 0, duration: 0, frameRate: 30, enabled: false };
function label(element, key) { element.setAttribute('aria-label', words[key]); element.title = words[key]; }
document.documentElement.lang = ja ? 'ja' : 'en';
document.querySelectorAll('[data-text]').forEach(el => { el.textContent = words[el.dataset.text]; });
document.querySelectorAll('[data-label]').forEach(el => label(el, el.dataset.label));
function message(text = '') { notice.textContent = text; notice.hidden = !text; }
function canControl() { return connected && playback.enabled && !busy; }
function time(seconds, fraction = false) {
  const ticks = Math.round(Math.abs(seconds) * 100), minutes = Math.floor(ticks / 6000);
  const second = fraction ? ((ticks % 6000) / 100).toFixed(2).padStart(5, '0') : String(Math.floor(ticks % 6000 / 100)).padStart(2, '0');
  return `${seconds < 0 ? '−' : ''}${minutes}:${second}`;
}
function renderPlayback() {
  remoteButtons.forEach(el => { el.disabled = !canControl(); });
  seek.disabled = !canControl() || playback.duration <= playback.minimum;
  audio.disabled = !connected;
  play.dataset.playing = String(playback.playing);
  label(play, playback.playing ? 'pause' : 'play');
  if (!seeking) {
    seek.min = playback.minimum; seek.max = playback.duration;
    seek.step = 1 / playback.frameRate; seek.value = playback.position;
  }
  const position = seeking ? Number(seek.value) : playback.position;
  timeDisplay.textContent = `${time(position, true)} / ${time(playback.duration)}`;
  seek.setAttribute('aria-valuetext', time(position, true));
}
function setControlsVisible(visible) {
  controls.classList.toggle('is-hidden', !visible); controls.inert = !visible;
}
function hideControls() {
  if (!connected || !playback.playing || busy || seeking || controls.querySelector(':focus-visible') || !notice.hidden) return;
  setControlsVisible(false);
}
function showControls() {
  clearTimeout(hideTimer); setControlsVisible(true);
  if (connected && playback.playing) hideTimer = setTimeout(hideControls, 3000);
}
function refreshControls() { renderPlayback(); showControls(); }
function setAudio(enabled) {
  wantsAudio = enabled; video.muted = !enabled;
  audio.setAttribute('aria-pressed', String(enabled)); label(audio, enabled ? 'audioOff' : 'audioOn');
}
function onConnectionState(state, value) {
  if (state === 'disconnected') { connected = false; refreshControls(); return; }
  if (state === 'playback') {
    const changed = playback.playing !== value.playing;
    playback = { ...playback, ...value }; renderPlayback();
    if (changed) showControls();
    return;
  }
  if (state === 'range') { rangeDisplay.hidden = value !== 'HDR'; return; }
  if (state === 'audioFailed') { message(words.audioFailed); showControls(); return; }
  pairing.hidden = state !== 'pair'; viewer.hidden = state === 'pair';
  connected = state === 'connected'; refreshControls();
  if (connected) { message(); setAudio(wantsAudio); }
  else if (state !== 'pair') message(words[state]);
}
const connection = new PreviewConnection(video, onConnectionState);
$('#pair-form').onsubmit = async event => {
  event.preventDefault(); const button = $('#pair-button'), status = $('#pair-status');
  button.disabled = true; status.textContent = words.connecting; status.dataset.error = 'false';
  try {
    const response = await fetch('api/pair', { method: 'POST', body: $('#code').value, signal: AbortSignal.timeout(5000) });
    if (response.ok) { status.textContent = ''; connection.ended = false; await connection.connect(); }
    else { status.dataset.error = 'true'; status.textContent = words[response.status === 429 ? 'limited' : response.status === 409 ? 'full' : response.status === 410 ? 'ended' : 'invalid']; }
  } catch { status.dataset.error = 'true'; status.textContent = words.reconnecting; }
  finally { button.disabled = false; }
};
async function control(action, position) {
  if (!canControl()) return;
  busy = true; refreshControls();
  try {
    const ok = await connection.control(action, position);
    if (connected) message(ok ? '' : words.controlFailed);
  } finally { busy = false; seeking = false; refreshControls(); }
}
play.onclick = () => control(playback.playing ? 'pause' : 'play');
$('#previous').onclick = () => control('previous');
$('#next').onclick = () => control('next');
seek.oninput = () => { seeking = true; refreshControls(); };
seek.onchange = () => control('seek', Number(seek.value));
seek.onpointercancel = () => { seeking = false; refreshControls(); };
audio.onclick = async () => {
  setAudio(!wantsAudio);
  try { await video.play(); message(); }
  catch { setAudio(false); message(words.audioFailed); }
};
function fullscreenLabel() { label(full, document.fullscreenElement || viewer.classList.contains('immersive') ? 'exitFullscreen' : 'fullscreen'); }
full.onclick = async () => {
  try {
    if (viewer.classList.contains('immersive')) viewer.classList.remove('immersive');
    else if (document.fullscreenElement) await document.exitFullscreen();
    else await viewer.requestFullscreen();
  } catch { viewer.classList.toggle('immersive'); }
  fullscreenLabel(); showControls();
};
document.addEventListener('fullscreenchange', fullscreenLabel);
video.onclick = () => { if (controls.classList.contains('is-hidden')) showControls(); else hideControls(); };
viewer.addEventListener('pointermove', event => { if (event.pointerType === 'mouse') showControls(); });
controls.addEventListener('pointerdown', showControls);
controls.addEventListener('focusin', showControls);
document.addEventListener('keydown', event => {
  if (viewer.hidden) return;
  showControls();
  if (event.key === 'Escape') { viewer.classList.remove('immersive'); fullscreenLabel(); }
  if (event.code === 'Space' && !event.target.closest('input, button')) { event.preventDefault(); play.click(); }
});
function disconnectViewer() { seeking = false; connection.close(); }
document.addEventListener('visibilitychange', () => { if (document.hidden) disconnectViewer(); else connection.connect(); });
window.addEventListener('pagehide', disconnectViewer);
connection.connect();
