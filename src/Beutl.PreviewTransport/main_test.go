package main

import (
	"bytes"
	"encoding/json"
	"strings"
	"testing"

	"github.com/pion/webrtc/v4"
)

func TestMalformedCandidateDoesNotStopTransport(t *testing.T) {
	var buffer bytes.Buffer
	outputLock.Lock()
	previous := output
	output = json.NewEncoder(&buffer)
	outputLock.Unlock()
	defer func() {
		outputLock.Lock()
		output = previous
		outputLock.Unlock()
	}()
	run(strings.NewReader(`{"id":"malformed","type":"candidate","candidate":{"candidate":17}}
{"id":"following","type":"unknown"}
`))
	outputLock.Lock()
	result := buffer.String()
	outputLock.Unlock()
	if !strings.Contains(result, `"id":"malformed"`) || !strings.Contains(result, `"id":"following"`) {
		t.Fatalf("transport must process commands after a malformed candidate: %s", result)
	}
}

func TestNegotiatedVideoProfile(t *testing.T) {
	for _, tc := range []struct {
		name, mime, fmtp string
		hdr, color, ok   bool
	}{
		{"SDR", webrtc.MimeTypeVP8, "", false, false, true},
		{"HDR", webrtc.MimeTypeVP9, "profile-id=2", true, true, true},
		{"8-bit VP9 is not HDR", webrtc.MimeTypeVP9, "profile-id=0", true, true, false},
		{"missing HDR metadata", webrtc.MimeTypeVP9, "profile-id=2", true, false, false},
	} {
		t.Run(tc.name, func(t *testing.T) {
			m := &webrtc.MediaEngine{}
			err := m.RegisterCodec(webrtc.RTPCodecParameters{
				RTPCodecCapability: webrtc.RTPCodecCapability{MimeType: tc.mime, ClockRate: 90000, SDPFmtpLine: tc.fmtp}, PayloadType: 100,
			}, webrtc.RTPCodecTypeVideo)
			if err != nil {
				t.Fatal(err)
			}
			if tc.color {
				if err = m.RegisterHeaderExtension(webrtc.RTPHeaderExtensionCapability{URI: colorSpaceURI}, webrtc.RTPCodecTypeVideo); err != nil {
					t.Fatal(err)
				}
			}
			receiver, err := webrtc.NewAPI(webrtc.WithMediaEngine(m)).NewPeerConnection(webrtc.Configuration{})
			if err != nil {
				t.Fatal(err)
			}
			defer receiver.Close()
			if _, err = receiver.AddTransceiverFromKind(webrtc.RTPCodecTypeVideo, webrtc.RTPTransceiverInit{Direction: webrtc.RTPTransceiverDirectionRecvonly}); err != nil {
				t.Fatal(err)
			}
			offer, err := receiver.CreateOffer(nil)
			if err != nil {
				t.Fatal(err)
			}
			sender, err := newPeer(command{ID: tc.name, SDP: offer.SDP, HDR: tc.hdr})
			if sender != nil {
				defer sender.pc.Close()
			}
			if (err == nil) != tc.ok {
				t.Fatalf("negotiation success=%v, expected %v: %v", err == nil, tc.ok, err)
			}
			if tc.ok {
				answer := sender.pc.LocalDescription().SDP
				if !strings.Contains(answer, "a=sendonly") || !strings.Contains(answer, strings.TrimPrefix(tc.mime, "video/")+"/90000") {
					t.Fatalf("expected a send-only %s track", tc.mime)
				}
				if tc.hdr && (!strings.Contains(answer, "profile-id=2") || !strings.Contains(answer, colorSpaceURI)) {
					t.Fatal("HDR answer must include Profile 2 and color metadata")
				}
			}
		})
	}
}
