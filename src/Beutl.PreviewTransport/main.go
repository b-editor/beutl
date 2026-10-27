// WebRTC transport for Beutl. Only encoded media and signaling enter this process;
// it has no editor or rendering API. Pion and its dependencies are distributed
// with their licenses in THIRD-PARTY-NOTICES.txt.
package main

import (
	"bufio"
	"encoding/json"
	"fmt"
	"io"
	"os"
	"strings"
	"sync"

	"github.com/pion/interceptor"
	"github.com/pion/rtcp"
	"github.com/pion/rtp"
	"github.com/pion/rtp/codecs"
	"github.com/pion/webrtc/v4"
)

const colorSpaceURI = "http://www.webrtc.org/experiments/rtp-hdrext/color-space"

type command struct {
	Type       string                   `json:"type"`
	ID         string                   `json:"id"`
	SDP        string                   `json:"sdp"`
	Candidate  *webrtc.ICECandidateInit `json:"candidate"`
	ICEServers []webrtc.ICEServer       `json:"iceServers"`
	HDR        bool                     `json:"hdr"`
	Data       []byte                   `json:"data"`
	Timestamp  uint32                   `json:"timestamp"`
}

type peer struct {
	pc              *webrtc.PeerConnection
	video, audio    *webrtc.TrackLocalStaticRTP
	sender          *webrtc.RTPSender
	videoPacketizer rtp.Packetizer
	audioPacketizer rtp.Packetizer
	hdr             bool
}

var output = json.NewEncoder(os.Stdout)
var outputLock sync.Mutex

func emit(value any) {
	outputLock.Lock()
	defer outputLock.Unlock()
	_ = output.Encode(value)
}

func main() {
	run(os.Stdin)
}

func run(input io.Reader) {
	peers := map[string]*peer{}
	defer func() {
		for _, p := range peers {
			_ = p.pc.Close()
		}
	}()
	scanner := bufio.NewScanner(input)
	scanner.Buffer(make([]byte, 64*1024), 8*1024*1024)
	for scanner.Scan() {
		var cmd command
		if err := json.Unmarshal(scanner.Bytes(), &cmd); err != nil {
			// A malformed browser candidate must not stop the other viewers' transport.
			emit(map[string]any{"id": cmd.ID, "type": "error", "message": "invalid transport command"})
			continue
		}
		var err error
		switch cmd.Type {
		case "offer":
			if old := peers[cmd.ID]; old != nil {
				_ = old.pc.Close()
				delete(peers, cmd.ID)
			}
			if len(peers) >= 4 {
				err = fmt.Errorf("preview device limit reached")
				break
			}
			var p *peer
			p, err = newPeer(cmd)
			if err == nil {
				peers[cmd.ID] = p
			}
		case "candidate":
			if p := peers[cmd.ID]; p != nil && cmd.Candidate != nil {
				err = p.pc.AddICECandidate(*cmd.Candidate)
			}
		case "remove":
			if p := peers[cmd.ID]; p != nil {
				_ = p.pc.Close()
				delete(peers, cmd.ID)
			}
		case "video", "audio":
			for id, p := range peers {
				if p.pc.ConnectionState() != webrtc.PeerConnectionStateConnected || (cmd.Type == "video" && p.hdr != cmd.HDR) {
					continue
				}
				if sendErr := p.send(cmd); sendErr != nil {
					emit(map[string]any{"id": id, "type": "error", "message": sendErr.Error()})
				}
			}
		default:
			err = fmt.Errorf("unknown transport command")
		}
		if err != nil {
			emit(map[string]any{"id": cmd.ID, "type": "error", "message": err.Error()})
		}
	}
}

func newPeer(cmd command) (_ *peer, err error) {
	// A browser only requests HDR after offering VP9 Profile 2 and the color-space extension.
	// The negotiated codec and extension are also checked below, before an answer is sent.
	m := &webrtc.MediaEngine{}
	videoCodec := webrtc.RTPCodecCapability{MimeType: webrtc.MimeTypeVP8, ClockRate: 90000,
		RTCPFeedback: []webrtc.RTCPFeedback{{Type: "nack"}, {Type: "nack", Parameter: "pli"}, {Type: "ccm", Parameter: "fir"}}}
	if cmd.HDR {
		videoCodec.MimeType = webrtc.MimeTypeVP9
		videoCodec.SDPFmtpLine = "profile-id=2"
	}
	if err = m.RegisterCodec(webrtc.RTPCodecParameters{RTPCodecCapability: videoCodec, PayloadType: 96}, webrtc.RTPCodecTypeVideo); err != nil {
		return nil, err
	}
	audioCodec := webrtc.RTPCodecCapability{MimeType: webrtc.MimeTypeOpus, ClockRate: 48000, Channels: 2, SDPFmtpLine: "minptime=10;useinbandfec=1;stereo=1"}
	if err = m.RegisterCodec(webrtc.RTPCodecParameters{RTPCodecCapability: audioCodec, PayloadType: 111}, webrtc.RTPCodecTypeAudio); err != nil {
		return nil, err
	}
	if err = m.RegisterHeaderExtension(webrtc.RTPHeaderExtensionCapability{URI: colorSpaceURI}, webrtc.RTPCodecTypeVideo); err != nil {
		return nil, err
	}
	i := &interceptor.Registry{}
	if err = webrtc.RegisterDefaultInterceptors(m, i); err != nil {
		return nil, err
	}
	pc, err := webrtc.NewAPI(webrtc.WithMediaEngine(m), webrtc.WithInterceptorRegistry(i)).NewPeerConnection(webrtc.Configuration{ICEServers: cmd.ICEServers})
	if err != nil {
		return nil, err
	}
	defer func() {
		if err != nil {
			_ = pc.Close()
		}
	}()
	p := &peer{pc: pc, hdr: cmd.HDR}
	if p.video, err = webrtc.NewTrackLocalStaticRTP(videoCodec, "video", "beutl-preview"); err != nil {
		return nil, err
	}
	if p.audio, err = webrtc.NewTrackLocalStaticRTP(audioCodec, "audio", "beutl-preview"); err != nil {
		return nil, err
	}
	if p.sender, err = pc.AddTrack(p.video); err != nil {
		return nil, err
	}
	audioSender, err := pc.AddTrack(p.audio)
	if err != nil {
		return nil, err
	}
	go readFeedback(p.sender, cmd.ID)
	go readFeedback(audioSender, cmd.ID)
	pc.OnICECandidate(func(candidate *webrtc.ICECandidate) {
		if candidate != nil {
			emit(map[string]any{"id": cmd.ID, "type": "candidate", "candidate": candidate.ToJSON()})
		}
	})
	pc.OnConnectionStateChange(func(state webrtc.PeerConnectionState) {
		emit(map[string]any{"id": cmd.ID, "type": "state", "state": state.String(), "hdr": cmd.HDR})
	})
	if err = pc.SetRemoteDescription(webrtc.SessionDescription{Type: webrtc.SDPTypeOffer, SDP: cmd.SDP}); err != nil {
		return nil, err
	}
	answer, err := pc.CreateAnswer(nil)
	if err != nil {
		return nil, err
	}
	if err = pc.SetLocalDescription(answer); err != nil {
		return nil, err
	}
	if cmd.HDR {
		// Pion permits a MIME-only codec match. Use the negotiated parameters so
		// a Profile 0 answer can never receive our Profile 2 bitstream.
		params := p.sender.GetParameters()
		profile, color := false, false
		for _, codec := range params.Codecs {
			for _, field := range strings.Split(codec.SDPFmtpLine, ";") {
				profile = profile || (strings.EqualFold(codec.MimeType, webrtc.MimeTypeVP9) && strings.TrimSpace(field) == "profile-id=2")
			}
		}
		for _, extension := range params.HeaderExtensions {
			color = color || extension.URI == colorSpaceURI
		}
		if !profile || !color {
			return nil, fmt.Errorf("browser did not negotiate HDR codec and color metadata")
		}
	}
	var payloader rtp.Payloader = &codecs.VP8Payloader{EnablePictureID: true}
	if cmd.HDR {
		payloader = &codecs.VP9Payloader{}
	}
	p.videoPacketizer = rtp.NewPacketizer(1200, 96, 0, payloader, rtp.NewRandomSequencer(), 90000)
	p.audioPacketizer = rtp.NewPacketizer(1200, 111, 0, &codecs.OpusPayloader{}, rtp.NewRandomSequencer(), 48000)
	emit(map[string]any{"id": cmd.ID, "type": "answer", "sdp": pc.LocalDescription().SDP, "hdr": cmd.HDR})
	return p, nil
}

func readFeedback(sender *webrtc.RTPSender, id string) {
	for {
		packets, _, err := sender.ReadRTCP()
		if err != nil {
			return
		}
		for _, packet := range packets {
			switch packet.(type) {
			case *rtcp.PictureLossIndication, *rtcp.FullIntraRequest:
				emit(map[string]any{"id": id, "type": "keyframe"})
			}
		}
	}
}

func (p *peer) send(cmd command) error {
	track, packetizer := p.video, p.videoPacketizer
	if cmd.Type == "audio" {
		track, packetizer = p.audio, p.audioPacketizer
	}
	for _, packet := range packetizer.Packetize(cmd.Data, 0) {
		packet.Timestamp = cmd.Timestamp
		if cmd.Type == "video" && packet.Marker {
			for _, ext := range p.sender.GetParameters().HeaderExtensions {
				if ext.URI == colorSpaceURI {
					// H.273: BT.2020, PQ, BT.2020 non-constant luminance, limited range.
					color := []byte{1, 13, 1, 0x10}
					if p.hdr {
						color = []byte{9, 16, 9, 0x10}
					}
					if err := packet.SetExtension(uint8(ext.ID), color); err != nil {
						return err
					}
				}
			}
		}
		if err := track.WriteRTP(packet); err != nil && err != io.ErrClosedPipe {
			return err
		}
	}
	return nil
}
