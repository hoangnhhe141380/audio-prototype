using Microsoft.AspNetCore.Mvc;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace AudioPrototype.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class SignalController : ControllerBase
    {
        public static Dictionary<uint, RTCPeerConnection> _connections = new Dictionary<uint, RTCPeerConnection>();
        private static uint _id = 0;


        [HttpGet]
        public object Get()
        {
            var pc = new RTCPeerConnection(null);

            var format = new VideoFormat(VideoCodecsEnum.H264, 102);

            var track = new MediaStreamTrack(format);
            pc.addTrack(track);
            var offer = pc.createOffer(null);
            pc.setLocalDescription(offer);
            _id++;

            _connections.Add(_id, pc);
            return new
            {
                id = _id,
                offer
            };
        }

        [HttpPost]
        public object Post(dynamic answer)
        {
            uint clientId = answer.id;
            string tmp = answer.answer.sdp.ToString();
            var pc = _connections[clientId];

            pc.setRemoteDescription(new RTCSessionDescriptionInit { sdp = tmp, type = RTCSdpType.answer });
            Console.WriteLine("POST >>>" + tmp);

            return "offer";
        }
    }
}