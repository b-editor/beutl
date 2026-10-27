// The editor sends audio ahead of video. Anchor both to the first displayed frame,
// rather than playing a received audio block immediately (which would lead by seconds).
export class PreviewMediaPlayer {
  constructor(onAudioStateChanged = () => {}) {
    this.onAudioStateChanged = onAudioStateChanged;
    this.context = null;
    this.enabled = false;
    this.id = 0;
    this.stoppedId = 0;
    this.anchor = null;
    this.audio = [];
    this.sources = new Set();
    this.scheduledUntil = -Infinity;
    this.revision = 0;
  }
  async enable() {
    if (!this.context) {
      this.context = new (window.AudioContext || window.webkitAudioContext)();
      this.context.onstatechange = () => {
        if (this.context.state !== 'running') this.resetClock();
        this.onAudioStateChanged();
      };
    }
    await this.context.resume();
    this.enabled = this.context.state === 'running';
    this.resetClock();
    return this.enabled;
  }
  mute() { this.enabled = false; this.resetClock(); }
  resetClock() {
    for (const source of this.sources) { try { source.stop(); } catch {} }
    this.sources.clear();
    this.anchor = null;
    this.scheduledUntil = -Infinity;
    ++this.revision;
  }
  stop(id = this.id) {
    this.stoppedId = Math.max(this.stoppedId, this.id, id);
    this.audio = this.audio.filter(a => a.id > this.stoppedId);
    this.resetClock();
  }
  disconnect() {
    this.stop();
    // A new connection may be a new session, with its own playback sequence.
    this.id = this.stoppedId = 0;
  }
  receiveAudio(packet) {
    if (!this.enabled) return;
    const view = new DataView(packet);
    const id = view.getFloat64(16, true), time = view.getFloat64(8, true);
    const channels = view.getUint16(2, true), rate = view.getUint32(4, true);
    if (id <= this.stoppedId || id < this.id || channels !== 2 || rate < 8000 || rate > 192000) return;
    const count = (packet.byteLength - 24) / (channels * 2);
    if (!Number.isInteger(count) || count <= 0 || count > rate * 2) return;
    this.audio.push({ id, time, rate, channels, count, view });
    this.audio = this.audio.filter(a => a.id >= id).slice(-4);
    this.scheduleAudio();
  }
  // Returns a delay for this frame. ACK is sent only after display, bounding video buffering.
  video(time, id, playing) {
    if (!playing || id === 0) { this.stop(id); return 0; }
    if (id <= this.stoppedId) return 0;
    if (id !== this.id) {
      this.resetClock();
      this.id = id;
      this.audio = this.audio.filter(a => a.id === id);
    }
    if (!this.enabled || this.context?.state !== 'running') return 0;
    const now = this.context.currentTime;
    let target = this.anchor === null ? now : this.anchor + time;
    // Re-anchor on a stalled network; never let latency accumulate indefinitely.
    if (this.anchor === null || target < now - .25 || target > now + .5) {
      this.resetClock();
      this.anchor = now + .12 - time;
      this.scheduledUntil = time;
      target = now + .12;
    }
    this.scheduleAudio();
    return Math.max(0, (target - now) * 1000);
  }
  scheduleAudio() {
    if (this.anchor === null || !this.enabled || this.context.state !== 'running') return;
    const now = this.context.currentTime;
    for (const audio of this.audio) {
      if (audio.id !== this.id) continue;
      const end = audio.time + audio.count / audio.rate;
      if (end <= this.scheduledUntil || this.anchor + end <= now) continue;
      const start = Math.max(audio.time, this.scheduledUntil, now + .01 - this.anchor);
      if (start >= end) continue;
      const buffer = this.context.createBuffer(audio.channels, audio.count, audio.rate);
      for (let channel = 0; channel < audio.channels; channel++) {
        const samples = buffer.getChannelData(channel);
        for (let i = 0; i < audio.count; i++)
          samples[i] = audio.view.getInt16(24 + (i * audio.channels + channel) * 2, true) / 32768;
      }
      const source = this.context.createBufferSource();
      source.buffer = buffer;
      source.connect(this.context.destination);
      source.onended = () => this.sources.delete(source);
      this.sources.add(source);
      source.start(this.anchor + start, start - audio.time);
      this.scheduledUntil = end;
    }
  }
}
