const colorSpaceURI = 'http://www.webrtc.org/experiments/rtp-hdrext/color-space';

export function selectVideoCodec(codecs, hdrDisplay) {
  const hdr = hdrDisplay && codecs.find(codec => codec.mimeType.toLowerCase() === 'video/vp9' &&
    /(?:^|;)\s*profile-id=2(?:;|$)/u.test(codec.sdpFmtpLine || ''));
  return { hdr: !!hdr, codec: hdr || codecs.find(codec => codec.mimeType.toLowerCase() === 'video/vp8') };
}

// Connection/reconnect/negotiation only. The viewer owns DOM, localization, audio gestures and full screen.
export class PreviewConnection {
  constructor(video, state) {
    this.video = video;
    this.state = state;
    this.generation = 0;
    this.retry = 1000;
    this.allowHdr = true;
    this.ended = false;
    this.controlSequence = 0;
  }

  close() {
    ++this.generation;
    clearTimeout(this.timer);
    if (this.socket) { this.socket.onclose = null; this.socket.close(); this.socket = null; }
    if (this.peer) { this.peer.close(); this.peer = null; }
    this.video.srcObject = null;
    this.pendingControl?.finish(false);
    this.state('disconnected');
  }

  async connect() {
    this.close();
    if (document.hidden || this.ended) return;
    const generation = this.generation;
    try {
      const response = await fetch('api/session', { cache: 'no-store', signal: AbortSignal.timeout(5000) });
      if (generation !== this.generation) return;
      if (response.status === 401) { this.state('pair'); return; }
      if (response.status === 410) { this.end(); return; }
      if (!response.ok) throw new Error('Session unavailable');
      this.state('connecting');
      const url = new URL('stream', location.href);
      url.protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
      const socket = this.socket = new WebSocket(url);
      let messages = Promise.resolve();
      socket.onmessage = event => {
        messages = messages.then(async () => {
          if (generation !== this.generation) return;
          const signal = JSON.parse(event.data);
          if (signal.ended) { this.end(); return; }
          if (signal.type === 'config') await this.negotiate(signal.iceServers, socket, generation);
          else if (signal.type === 'answer') {
            const peer = this.peer;
            await peer.setRemoteDescription({ type: 'answer', sdp: signal.sdp });
            if (generation !== this.generation) return;
            for (const candidate of this.remoteCandidates) await peer.addIceCandidate(candidate);
            if (generation !== this.generation) return;
            this.remoteCandidates = [];
            this.state('range', signal.hdr ? 'HDR' : 'SDR');
          } else if (signal.type === 'candidate') {
            if (this.peer?.remoteDescription) await this.peer.addIceCandidate(signal.candidate);
            else this.remoteCandidates.push(signal.candidate);
          } else if (signal.type === 'playback') this.state('playback', signal);
          else if (signal.type === 'control-result') {
            if (signal.requestId === this.pendingControl?.id) this.pendingControl.finish(signal.ok === true);
          }
          else if (signal.type === 'error') {
            if (this.hdr && !signal.fatal) { this.allowHdr = false; this.reconnect(); }
            else { this.close(); this.state('error'); }
          }
        }).catch(() => { if (generation === this.generation) this.reconnect(); });
      };
      socket.onclose = () => { if (generation === this.generation) this.reconnect(); };
    } catch {
      if (generation === this.generation) this.reconnect();
    }
  }

  async negotiate(iceServers, socket, generation) {
    if (!globalThis.RTCPeerConnection || !globalThis.RTCRtpReceiver) { this.close(); this.state('unsupported'); return; }
    const peer = this.peer = new RTCPeerConnection({ iceServers });
    this.remoteCandidates = [];
    const stream = new MediaStream();
    this.video.srcObject = stream;
    peer.ontrack = ({ track }) => {
      if (generation !== this.generation) return;
      stream.addTrack(track);
      track.onunmute = () => {
        if (generation === this.generation) this.video.play().catch(() => {
          if (generation === this.generation) this.state('audioFailed');
        });
      };
    };
    peer.onconnectionstatechange = () => {
      if (generation !== this.generation) return;
      if (peer.connectionState === 'connected') { this.retry = 1000; this.state('connected'); }
      if (peer.connectionState === 'failed') this.reconnect();
      if (peer.connectionState === 'disconnected') {
        clearTimeout(this.timer);
        this.timer = setTimeout(() => { if (peer.connectionState === 'disconnected') this.reconnect(); }, 3000);
      }
    };
    let offered = false;
    const candidates = [];
    peer.onicecandidate = ({ candidate }) => {
      if (!candidate || generation !== this.generation) return;
      const signal = { type: 'candidate', candidate: candidate.toJSON() };
      if (offered) socket.send(JSON.stringify(signal)); else candidates.push(signal);
    };
    const video = peer.addTransceiver('video', { direction: 'recvonly' });
    peer.addTransceiver('audio', { direction: 'recvonly' });
    const codecs = RTCRtpReceiver.getCapabilities('video').codecs;
    const selected = selectVideoCodec(codecs, this.allowHdr && matchMedia('(video-dynamic-range: high)').matches);
    if (!selected.codec) { this.close(); this.state('unsupported'); return; }
    this.hdr = selected.hdr;
    video.setCodecPreferences([selected.codec]);
    let offer = await peer.createOffer();
    // Color metadata is needed even when a browser can decode 10-bit VP9. Never label it HDR without it.
    if (this.hdr && !offer.sdp.includes(colorSpaceURI)) {
      this.hdr = false;
      video.setCodecPreferences([selectVideoCodec(codecs, false).codec]);
      offer = await peer.createOffer();
    }
    await peer.setLocalDescription(offer);
    if (generation !== this.generation) return;
    socket.send(JSON.stringify({ type: 'offer', sdp: offer.sdp, hdr: this.hdr }));
    offered = true;
    for (const candidate of candidates) socket.send(JSON.stringify(candidate));
  }

  control(action, position) {
    if (this.pendingControl || this.socket?.readyState !== WebSocket.OPEN || this.peer?.connectionState !== 'connected')
      return Promise.resolve(false);
    return new Promise(resolve => {
      const id = ++this.controlSequence;
      const finish = ok => { clearTimeout(timer); this.pendingControl = null; resolve(ok); };
      const timer = setTimeout(() => finish(false), 10000);
      this.pendingControl = { id, finish };
      try { this.socket.send(JSON.stringify({ type: 'control', requestId: id, action, position })); }
      catch { finish(false); }
    });
  }

  reconnect() {
    this.close();
    if (document.hidden || this.ended) return;
    this.state('reconnecting');
    this.timer = setTimeout(() => this.connect(), this.retry);
    this.retry = Math.min(this.retry * 2, 10000);
  }

  end() { this.ended = true; this.close(); this.state('ended'); }
}
