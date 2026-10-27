import { PreviewConnection } from './preview-connection.js';

const ja = navigator.language.startsWith('ja');
const words = ja ? {
  title: 'プレビューに接続', preview: 'プレビュー', instructions: 'Beutlに表示されている6桁のコードを入力してください。',
  code: '接続コード', connect: '接続する', note: 'ライブプレビュー · 閲覧専用', fullscreen: '全画面',
  connecting: '接続しています…', reconnecting: '再接続しています…', playing: '再生中', paused: '停止中',
  invalid: 'コードが違うか、有効期限が切れています。Beutlで確認してください。',
  limited: '試行回数が多すぎます。1分後にお試しください。', full: '接続台数の上限です。Beutlで端末を切断してください。',
  ended: '共有は終了しました。Beutlで共有を開始し、新しいQRコードを読み取ってください。',
  audioOn: '音声を有効にする', audioOff: 'ミュート', audioFailed: '音声ボタンを押すと再生できます。',
  error: '配信が停止しました。Beutlで共有を開始し直してください。繰り返す場合はFFmpegの設定を確認してください。',
  unsupported: 'このブラウザはプレビュー再生に対応していません。最新のブラウザで開いてください。'
} : {
  title: 'Connect to preview', preview: 'Preview', instructions: 'Enter the 6-digit code shown in Beutl.',
  code: 'Connection code', connect: 'Connect', note: 'Live preview · View only', fullscreen: 'Full screen',
  connecting: 'Connecting…', reconnecting: 'Reconnecting…', playing: 'Playing', paused: 'Paused',
  invalid: 'The code is incorrect or expired. Check the code in Beutl.',
  limited: 'Too many attempts. Try again in one minute.', full: 'Device limit reached. Disconnect a device in Beutl.',
  ended: 'Sharing ended. Start sharing in Beutl and scan the new QR code.',
  audioOn: 'Enable audio', audioOff: 'Mute', audioFailed: 'Tap the audio button to start playback.',
  error: 'Preview stopped. Start sharing again in Beutl. If this repeats, check the FFmpeg settings.',
  unsupported: 'This browser cannot play the preview. Open it in a current browser.'
};
document.documentElement.lang = ja ? 'ja' : 'en';
document.querySelectorAll('[data-text]').forEach(element => { element.textContent = words[element.dataset.text]; });
const pairing = document.querySelector('#pairing'), viewer = document.querySelector('#viewer');
const video = document.querySelector('#frame'), notice = document.querySelector('#connection');
const status = document.querySelector('#pair-status'), button = document.querySelector('#pair-button');
const audio = document.querySelector('#audio');
let wantsAudio = false;
function message(text = '') { notice.textContent = text; notice.hidden = !text; }
const connection = new PreviewConnection(video, (state, value) => {
  pairing.hidden = state !== 'pair'; viewer.hidden = state === 'pair';
  if (state === 'pair') return;
  if (state === 'range') { const badge = document.querySelector('#range'); badge.textContent = value; badge.hidden = false; return; }
  if (state === 'playing' || state === 'paused') { document.querySelector('#state').textContent = words[state]; return; }
  if (state === 'connected') { message(); video.muted = !wantsAudio; return; }
  if (state === 'ended' || state === 'error') { document.querySelector('#state').textContent = ''; document.querySelector('#range').hidden = true; }
  message(words[state]);
});
document.querySelector('#pair-form').onsubmit = async event => {
  event.preventDefault(); button.disabled = true; status.textContent = words.connecting; status.dataset.error = 'false';
  try {
    const response = await fetch('api/pair', { method: 'POST', body: document.querySelector('#code').value, signal: AbortSignal.timeout(5000) });
    if (response.ok) { status.textContent = ''; connection.ended = false; await connection.connect(); }
    else { status.dataset.error = 'true'; status.textContent = words[response.status === 429 ? 'limited' : response.status === 409 ? 'full' : response.status === 410 ? 'ended' : 'invalid']; }
  } catch { status.dataset.error = 'true'; status.textContent = words.reconnecting; }
  finally { button.disabled = false; }
};
audio.onclick = async () => {
  wantsAudio = !wantsAudio;
  video.muted = !wantsAudio;
  try { await video.play(); message(); }
  catch { wantsAudio = false; video.muted = true; message(words.audioFailed); }
  audio.setAttribute('aria-pressed', String(wantsAudio));
  audio.textContent = wantsAudio ? words.audioOff : words.audioOn;
};
document.querySelector('#fullscreen').onclick = async () => {
  try { if (document.fullscreenElement) await document.exitFullscreen(); else await viewer.requestFullscreen(); }
  catch { viewer.classList.toggle('immersive'); }
};
document.addEventListener('keydown', event => { if (event.key === 'Escape') viewer.classList.remove('immersive'); });
// On browsers without element fullscreen, a tap restores the controls.
document.querySelector('#stage').onclick = () => viewer.classList.remove('immersive');
document.addEventListener('visibilitychange', () => { if (document.hidden) connection.close(); else connection.connect(); });
window.addEventListener('pagehide', () => connection.close());
connection.connect();
