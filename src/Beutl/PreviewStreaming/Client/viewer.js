import { PreviewMediaPlayer } from './media-player.js';
const media = new PreviewMediaPlayer(updateAudioButton);
const ja = navigator.language.startsWith('ja');
const words = ja ? {
  title: '映像を、手元に。', instructions: 'Beutlに表示された6桁のコードを入力してください。',
  code: '接続コード', connect: '接続', fullscreen: '全画面', note: 'ライブプレビュー · 閲覧専用',
  waiting: '映像を待っています…', reconnecting: '接続を確認しています… Beutlで配信が開始されているか確認してください。',
  incorrect: 'コードが違うか、有効期限が切れています。Beutlのコードを確認してください。',
  limited: '試行回数の上限です。1分待ってから再試行してください。', full: '接続台数の上限です。Beutlで端末を切断してください。',
  ended: '共有が終了しました。Beutlで共有を開始し、新しいQRコードを読み取ってください。',
  audioOn: '音声をオン', audioOff: 'ミュート', audioFailed: '音声ボタンをもう一度押してください。',
  playing: '再生中', paused: '一時停止', connecting: '接続中…'
} : {
  title: 'Your preview, here.', instructions: 'Enter the 6-digit code shown in Beutl.',
  code: 'Connection code', connect: 'Connect', fullscreen: 'Full screen', note: 'Live preview · View only',
  waiting: 'Waiting for a frame…', reconnecting: 'Reconnecting… Check that sharing is running in Beutl.',
  incorrect: 'The code is incorrect or expired. Check the code in Beutl.',
  limited: 'Too many attempts. Try again in one minute.', full: 'Device limit reached. Disconnect a device in Beutl.',
  ended: 'Sharing ended. Start sharing in Beutl and scan the new QR code.',
  audioOn: 'Enable audio', audioOff: 'Mute', audioFailed: 'Tap the audio button again to enable sound.',
  playing: 'Playing', paused: 'Paused', connecting: 'Connecting…'
};
document.documentElement.lang = ja ? 'ja' : 'en';
document.querySelectorAll('[data-text]').forEach(element => { element.textContent = words[element.dataset.text]; });
const pairing = document.querySelector('#pairing');
const viewer = document.querySelector('#viewer');
const connection = document.querySelector('#connection');
const frame = document.querySelector('#frame');
const status = document.querySelector('#pair-status');
const button = document.querySelector('#pair-button');
let socket, timer, stalled, imageUrl, generation = 0, retry = 1000;
function message(text) { connection.textContent = text; connection.hidden = !text; }
function retryConnection() {
  message(words.reconnecting);
  if (document.hidden) return;
  clearTimeout(timer);
  timer = setTimeout(connect, retry);
  retry = Math.min(retry * 2, 10000);
}
async function connect() {
  clearTimeout(timer);
  const attempt = ++generation;
  if (socket) { const old = socket; socket = null; old.close(); }
  try {
    const response = await fetch('api/session', { cache: 'no-store', signal: AbortSignal.timeout(5000) });
    if (attempt !== generation) return;
    if (response.status === 401) {
      pairing.hidden = false; viewer.hidden = true;
      return;
    }
    if (response.status === 410) {
      media.disconnect(); pairing.hidden = true; viewer.hidden = false; frame.hidden = true;
      message(words.ended); return;
    }
    if (!response.ok) throw new Error('Session unavailable');
    pairing.hidden = true; viewer.hidden = false;
    message(words.waiting);
    const url = new URL('stream', location.href);
    url.protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
    const ws = new WebSocket(url);
    ws.binaryType = 'arraybuffer';
    ws.onopen = () => { if (media.enabled) ws.send('audio-on'); };
    socket = ws;
    ws.onmessage = async event => {
      if (socket !== ws) return;
      if (typeof event.data === 'string') {
        const state = JSON.parse(event.data);
        if (state.ended) {
          socket = null; ws.close(); clearTimeout(stalled); media.disconnect();
          frame.hidden = true; message(words.ended); return;
        }
        if (state.playing === false) { clearTimeout(stalled); media.stop(state.playbackId); }
        document.querySelector('#state').textContent = state.playing ? words.playing : words.paused;
        return;
      }
      if (event.data.byteLength < 24) { ws.close(); return; }
      const packet = new DataView(event.data);
      if (packet.getUint8(0) === 2) {
        media.receiveAudio(event.data);
        ws.send('audio-ready');
        return;
      }
      if (packet.getUint8(0) !== 1) { ws.close(); return; }
      const playing = packet.getUint8(1) === 1;
      const time = packet.getFloat64(8, true), id = packet.getFloat64(16, true);
      if (playing && id > 0 && id <= media.stoppedId) { ws.send('ready'); return; }
      const url = URL.createObjectURL(new Blob([event.data.slice(24)], { type: 'image/jpeg' }));
      try {
        const decoded = new Image();
        decoded.src = url;
        await decoded.decode();
        if (socket !== ws || ws.readyState !== WebSocket.OPEN) { URL.revokeObjectURL(url); return; }
        const delay = media.video(time, id, playing);
        const revision = media.revision;
        if (delay) await new Promise(resolve => setTimeout(resolve, delay));
        if (socket !== ws || ws.readyState !== WebSocket.OPEN || revision !== media.revision) {
          URL.revokeObjectURL(url);
          if (socket === ws && ws.readyState === WebSocket.OPEN) ws.send('ready');
          return;
        }
        document.querySelector('#state').textContent = playing ? words.playing : words.paused;
        frame.src = url;
        frame.hidden = false;
        if (imageUrl) URL.revokeObjectURL(imageUrl);
        imageUrl = url;
        message('');
        clearTimeout(stalled);
        if (playing) stalled = setTimeout(() => media.resetClock(), 500);
        retry = 1000;
        // Only ask for another frame after decoding. The server retains at most one newer image.
        ws.send('ready');
      } catch {
        URL.revokeObjectURL(url);
        ws.close();
      }
    };
    ws.onclose = () => { if (socket === ws) { socket = null; clearTimeout(stalled); media.disconnect(); retryConnection(); } };
  } catch {
    if (attempt === generation) {
      status.textContent = words.reconnecting;
      retryConnection();
    }
  }
}
document.querySelector('#pair-form').addEventListener('submit', async event => {
  event.preventDefault();
  button.disabled = true;
  status.textContent = words.connecting;
  try {
    const response = await fetch('api/pair', {
      method: 'POST', headers: { 'Content-Type': 'text/plain' },
      body: document.querySelector('#code').value, signal: AbortSignal.timeout(5000)
    });
    if (response.ok) { status.textContent = ''; await connect(); }
    else status.textContent = response.status === 429 ? words.limited : response.status === 409 ? words.full : words.incorrect;
  } catch { status.textContent = words.reconnecting; }
  finally { button.disabled = false; }
});
function updateAudioButton() {
  const active = media.enabled && media.context?.state === 'running';
  const button = document.querySelector('#audio');
  button.textContent = active ? words.audioOff : words.audioOn;
  button.setAttribute('aria-pressed', String(active));
}
document.querySelector('#audio').addEventListener('click', async event => {
  event.stopPropagation();
  if (media.enabled && media.context?.state === 'running') media.mute();
  else {
    try { if (!await media.enable()) message(words.audioFailed); }
    catch { message(words.audioFailed); }
  }
  updateAudioButton();
  if (socket?.readyState === WebSocket.OPEN) socket.send(media.enabled ? 'audio-on' : 'audio-off');
});
document.querySelector('#fullscreen').addEventListener('click', async event => {
  event.stopPropagation();
  try {
    if (viewer.requestFullscreen) await viewer.requestFullscreen();
    else viewer.classList.add('immersive');
  } catch { viewer.classList.add('immersive'); }
});
viewer.addEventListener('click', () => viewer.classList.remove('immersive'));
document.addEventListener('visibilitychange', () => {
  if (document.hidden) {
    clearTimeout(timer); clearTimeout(stalled); ++generation; media.disconnect();
    if (socket) { const old = socket; socket = null; old.close(); }
  } else connect();
});
connect();
