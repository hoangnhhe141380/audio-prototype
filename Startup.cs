using System.Diagnostics;
using FFmpeg.AutoGen;
using AudioPrototype.Controllers;
using NAudio.Wave;
using Newtonsoft.Json.Converters;

namespace AudioPrototype
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers().AddNewtonsoftJson(options =>
                options.SerializerSettings.Converters.Add(new StringEnumConverter()));
            services.AddControllers();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            Task.Run(Loop);
            Task.Run(() =>
            {
                var proc = Process.GetCurrentProcess();
                while (true)
                {
                    Console.WriteLine("PROCESS MEMORY >>> " + proc.PrivateMemorySize64 / 1024 / 1024);
                    Thread.Sleep(5000);
                }
            });
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();
            app.UseFileServer();
            app.UseEndpoints(endpoints => { endpoints.MapControllers(); });
        }

        private unsafe void Loop()
        {
            // string audioFilePath = "sample.wav";

            // var audioFile = new AudioFileReader(audioFilePath);
            // var outputDevice = new WaveOutEvent();

            var url = @"rtsp://wowzaec2demo.streamlock.net/vod/mp4:BigBuckBunny_115k.mov";
            Console.WriteLine("Connected >>> " + url);

            AVDictionary* dict = null;
            ffmpeg.av_dict_set(&dict, "stimeout", $"{TimeSpan.FromSeconds(5).TotalMilliseconds * 1000}", 0);
            var ctx = ffmpeg.avformat_alloc_context();
            if (ffmpeg.avformat_open_input(&ctx, url, null, &dict) != 0)
            {
                ffmpeg.avformat_close_input(&ctx);
                throw new Exception("Cannot connect to: " + url);
            }

            ffmpeg.avformat_find_stream_info(ctx, null);

            var videoStreamIndex = -1;
            for (var i = 0; i < ctx->nb_streams; i++)
            {
                var stream = ctx->streams[i];
                if (stream->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO)
                {
                    videoStreamIndex = i;
                }
            }

            if (videoStreamIndex < 0)
            {
                ffmpeg.avformat_close_input(&ctx);
                throw new Exception("No VIDEO STREAM found");
            }

            ffmpeg.av_read_play(ctx);

            var pkt = ffmpeg.av_packet_alloc();

            long lastTs = 0;
            while (true)
            {
                ffmpeg.av_init_packet(pkt);
                if (ffmpeg.av_read_frame(ctx, pkt) != 0)
                {
                    ffmpeg.avformat_close_input(&ctx);
                    ffmpeg.av_packet_free(&pkt);
                    throw new Exception("Error reading STREAM: " + url);
                }

                if (pkt->stream_index != videoStreamIndex || pkt->pts < 0)
                {
                    ffmpeg.av_packet_unref(pkt);
                    continue;
                }

                if (pkt->pts <= lastTs)
                {
                    ffmpeg.av_packet_unref(pkt);
                    continue;
                }

                lastTs = pkt->pts;

                foreach (var pc in SignalController._connections.Values)
                {
                    var mem = new Span<byte>(pkt->data, pkt->size);
                    pc.SendVideo((uint)pkt->dts, mem.ToArray());
                }

                ffmpeg.av_packet_unref(pkt);
            }
        }
    }
}