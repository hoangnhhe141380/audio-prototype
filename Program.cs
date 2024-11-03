using System.Net;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using WebSocketSharp.Server;

namespace AudioPrototype
{
    class Program
    {
        private static string STUN_URL = "stun:localhost:3479";
        private static int SIP_LISTEN_PORT = 5060;
        private const int WEBSOCKET_PORT = 8081;
        private const string MUSIC_FILENAME = "music.wav";

        private static RTCPeerConnection _peerConnection;
        private static RTPSession _rtpSession;

        static void Main()
        {
            Console.WriteLine("Example STUN Server");

            // STUN servers need two separate end points to listen on.
            IPEndPoint primaryEndPoint = new IPEndPoint(IPAddress.Any, 3478);
            IPEndPoint secondaryEndPoint = new IPEndPoint(IPAddress.Any, 3479);

            // Create the two STUN listeners and wire up the STUN server.
            STUNListener primarySTUNListener = new STUNListener(primaryEndPoint);
            STUNListener secondarySTUNListener = new STUNListener(secondaryEndPoint);
            STUNServer stunServer = new STUNServer(primaryEndPoint, primarySTUNListener.Send, secondaryEndPoint,
                secondarySTUNListener.Send);
            primarySTUNListener.MessageReceived += stunServer.STUNPrimaryReceived;
            secondarySTUNListener.MessageReceived += stunServer.STUNSecondaryReceived;

            // Optional. Provides verbose logs of STUN server activity.
            // EnableVerboseLogs(stunServer);

            Console.WriteLine("STUN server successfully initialised.");
            Console.WriteLine("====================");
            Console.WriteLine("SIPSorcery SIP to WebRTC example.");
            Console.WriteLine("Press ctrl-c to exit.");

            // Plumbing code to facilitate a graceful exit.
            CancellationTokenSource
                exitCts = new CancellationTokenSource(); // Cancellation token to stop the SIP transport and RTP stream.

            // Start web socket.
            Console.WriteLine("Starting web socket server...");
            var webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT);
            webSocketServer.AddWebSocketService<WebRTCWebSocketPeer>("/",
                (peer) => peer.CreatePeerConnection = CreatePeerConnection);
            webSocketServer.Start();

            Console.WriteLine(
                $"Waiting for web socket connections on {webSocketServer.Address}:{webSocketServer.Port}...");
            Console.WriteLine("Press ctrl-c to exit.");

            // Ctrl-c will gracefully exit the call at any point.
            ManualResetEvent exitMre = new ManualResetEvent(false);
            Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                exitMre.Set();
            };

            // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
            exitMre.WaitOne();
        }

        private static Task<RTCPeerConnection> CreatePeerConnection()
        {
            RTCConfiguration config = new RTCConfiguration
            {
                iceServers = new List<RTCIceServer> { new RTCIceServer { urls = STUN_URL } }
            };
            var pc = new RTCPeerConnection(config);
            
            AudioExtrasSource audioSource = new AudioExtrasSource(new AudioEncoder(), new AudioSourceOptions { AudioSource = AudioSourcesEnum.SineWave });

            MediaStreamTrack audioTrack = new MediaStreamTrack(audioSource.GetAudioSourceFormats(), MediaStreamStatusEnum.SendRecv);
            pc.addTrack(audioTrack);

            audioSource.OnAudioSourceEncodedSample += pc.SendAudio;

            pc.OnAudioFormatsNegotiated += (formats) => audioSource.SetAudioSourceFormat(formats.First());
            
            pc.onconnectionstatechange += async (state) =>
            {
                Console.WriteLine($"Peer connection state change to {state}.");

                if (state == RTCPeerConnectionState.connected)
                {
                    await audioSource.StartAudio();
                }
                else if (state == RTCPeerConnectionState.failed)
                {
                    pc.Close("ice disconnection");
                }
                else if (state == RTCPeerConnectionState.closed)
                {
                    await audioSource.CloseAudio();
                }
            };

            // Diagnostics.
            pc.OnReceiveReport += (re, media, rr) =>
                Console.WriteLine($"RTCP Receive for {media} from {re}\n{rr.GetDebugSummary()}");
            pc.OnSendReport += (media, sr) => Console.WriteLine($"RTCP Send for {media}\n{sr.GetDebugSummary()}");
            pc.GetRtpChannel().OnStunMessageReceived += (msg, ep, isRelay) =>
                Console.WriteLine($"STUN {msg.Header.MessageType} received from {ep}.");
            pc.oniceconnectionstatechange += (state) => Console.WriteLine($"ICE connection state change to {state}.");

            pc.OnRtpPacketReceived += ForwardMediaToSIP;
            _peerConnection = pc;

            return Task.FromResult(pc);
        }

        /// <summary>
        /// Forwards media from the WebRTC Peer Connection to the remote SIP user agent.
        /// </summary>
        /// <param name="remote">The remote endpoint the RTP packet was received from.</param>
        /// <param name="mediaType">The type of media.</param>
        /// <param name="rtpPacket">The RTP packet received on the SIP session.</param>
        private static void ForwardMediaToSIP(IPEndPoint remote, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket)
        {
            if (_rtpSession != null && mediaType == SDPMediaTypesEnum.audio)
            {
                _rtpSession.SendAudio((uint)rtpPacket.Payload.Length, rtpPacket.Payload);
            }
        }
    }
}