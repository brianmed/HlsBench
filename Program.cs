using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

using M3U8Parser;
using M3U8Parser.ExtXType;
using Mono.Options;

namespace HlsBench;

class Program
{
    public static CancellationTokenSource AppCancellationTokenSource;

    public static int SmallDelayToYieldCpu = 10;

    public static SemaphoreSlim ConcurrentDownloads;

    public static int TotalSegmentDownloads = 0;

    public static long BytesDownloaded = 0;

    public static SemaphoreSlim BytesDownloadedMutex = new(1, 1);

    public static Stopwatch ThirtySeconds = Stopwatch.StartNew();

    public static Stopwatch TotalRuntime = Stopwatch.StartNew();

    public static Stopwatch HlsClientCycleStopwatch = Stopwatch.StartNew();

    public static HttpClient HttpClient;

    private static async Task Main(string[] args)
    {
        int concurrentDownloads = 3;

        int countCycleHlsClients = 30;

        TimeSpan cycleHlsClientsTimeSpan = TimeSpan.FromSeconds(120);

        int rampUpHlsClients = 30;

        int rampUpSeconds = 1;

        string masterManifestUrl = null;

        int totalHlsClients = 10;

        bool showHelp = false;

        bool didShow3Seconds = false;

        bool didShow15Seconds = false;

        DateTime nextRampup = DateTime.Now;

        int hlsClientIndex = 0;

		OptionSet p = new()
        {
			"Usage: hlsbench [OPTIONS]",
			"HLS Benchmark",
			"",
			"Options:",
			{ "concurrent-downloads=", "Count of Concurrent Downloads", (string v) => concurrentDownloads = Int32.Parse(v) },
			{ "count-cycle-hls-clients=", "Number of HLS Clients to Stop and Start Every TimeSpan seconds", (string v) => countCycleHlsClients = Int32.Parse(v) },
			{ "seconds-cycle-hls-clients=", "Every Number of Seconds Initiate Stop and Rampup of HLS Clients", (string v) => cycleHlsClientsTimeSpan = TimeSpan.FromSeconds(Double.Parse(v)) },
			{ "rampup-hls-clients=", "Number of HLS Clients Provision each RampUp", (string v) => rampUpHlsClients = Int32.Parse(v) },
			{ "rampup-seconds=", "Number of Seconds to Wait Between a RampUp", (string v) => rampUpSeconds = Int32.Parse(v) },
			{ "master-manifest-url=", "HLS Master Manifest URL", (string v) => masterManifestUrl = v },
			{ "total-hls-clients=", "Total HLS Clients", (string v) => totalHlsClients = Int32.Parse(v) },
			{ "h|help",  "Show this Message and Exit", v => showHelp = v != null }
		};

		List<string> extra;

		try
        {
			extra = p.Parse(args);
		}
		catch (OptionException e)
        {
			Console.Write("hlsbench: ");
			Console.WriteLine(e.Message);
			Console.WriteLine("Try 'hlsbench --help' for more information.");

			return;
		}

		if (showHelp || String.IsNullOrWhiteSpace(masterManifestUrl))
        {
			p.WriteOptionDescriptions(Console.Out);

            Environment.Exit(0);
		}

        Program.ConcurrentDownloads = new(concurrentDownloads, concurrentDownloads);

        Program.AppCancellationTokenSource = new();

        PosixSignalRegistration sigTerm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, async (ctx) =>
        {
            await Program.AppCancellationTokenSource.CancelAsync();
        });

        PosixSignalRegistration sigQuit = PosixSignalRegistration.Create(PosixSignal.SIGQUIT, async (ctx) =>
        {
            await Program.AppCancellationTokenSource.CancelAsync();
        });

        PosixSignalRegistration sigInt = PosixSignalRegistration.Create(PosixSignal.SIGINT, async (ctx) =>
        {
            await Program.AppCancellationTokenSource.CancelAsync();
        });

        HttpClientHandler handler = new()
        {
            AllowAutoRedirect = true
        };

        handler.ClientCertificateOptions = ClientCertificateOption.Manual;
        handler.ServerCertificateCustomValidationCallback = (httpRequestMessage, cert, cetChain, policyErrors) =>
        {
            return true;
        };

        Program.HttpClient = new HttpClient(handler);

        List<HlsClient> hlsClients = new List<HlsClient>();

        Console.WriteLine($"Running: Ramping up {rampUpHlsClients} at a Time");
        while (Program.AppCancellationTokenSource.Token.IsCancellationRequested is false)
        {
            bool showSummary = false;

            if (Program.ThirtySeconds.Elapsed >= TimeSpan.FromSeconds(30.0))
            {
                showSummary = true;

                Program.ThirtySeconds.Restart();
            }
            else if (didShow3Seconds is false && Program.TotalRuntime.Elapsed >= TimeSpan.FromSeconds(3.0))
            {
                showSummary = true;

                didShow3Seconds = true;
            }
            else if (didShow15Seconds is false && Program.TotalRuntime.Elapsed >= TimeSpan.FromSeconds(15.0))
            {
                showSummary = true;

                didShow15Seconds = true;
            }

            if (showSummary)
            {
                await Program.BytesDownloadedMutex.WaitAsync(Program.AppCancellationTokenSource.Token);

                try
                {
                    await Console.Out.WriteLineAsync($"Runtime: {Program.TotalRuntime}: SegmentDownloads: {Program.TotalSegmentDownloads}: BytesDownloaded: {Program.BytesDownloaded}: CountHlsClients: {hlsClients.Count}");
                }
                finally
                {
                    Program.BytesDownloadedMutex.Release();
                }
            }

            if (Program.HlsClientCycleStopwatch.Elapsed >= cycleHlsClientsTimeSpan)
            {
                HlsClient[] toRemove = hlsClients.Take(countCycleHlsClients).ToArray();

                foreach (HlsClient hlsClient in toRemove)
                {
                    await hlsClient.DisposeAsync();

                    hlsClients.Remove(hlsClient);
                }

                Program.HlsClientCycleStopwatch.Restart();
            }

            if (hlsClients.Count < totalHlsClients && nextRampup <= DateTime.Now)
            {
                foreach (int term in Enumerable.Range(0, rampUpHlsClients))
                {
                    hlsClients.Add(new HlsClient(masterManifestUrl, hlsClientIndex));

                    hlsClientIndex += 1;

                    if (hlsClients.Count >= totalHlsClients)
                    {
                        break;
                    }
                }

                nextRampup = DateTime.Now.AddSeconds(rampUpSeconds);
            }

            await Task.Delay(Program.SmallDelayToYieldCpu);
        }
    }
}

public sealed class HlsClient : IAsyncDisposable
{
    public CancellationTokenSource DoWorkCancellationTokenSource { get; } = new();

    // Don't download over and over.  
    // It's how we support our sliding window.
    public HashSet<string> DownloadedSegments { get; } = new();

    public int Index { get; init; }

    public string MasterManifestUri { get; init; }

    public Task WorkerTask { get; init; }

    private Uri CurrentManifestUri { get; set; }

    public HlsClient(string masterManifestUrl, int index)
    {
        this.MasterManifestUri = masterManifestUrl;

        this.Index = index;

        this.DoWorkCancellationTokenSource.Token.ThrowIfCancellationRequested();

        this.WorkerTask = Task.Factory.StartNew(() => this.DoWork(), TaskCreationOptions.LongRunning);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await this.DoWorkCancellationTokenSource.CancelAsync();
        }
        catch (Exception ex)
        {
            Console.Out.WriteLineAsync(ex.ToString());
        }

        GC.SuppressFinalize(this);
    }

    // TODO: Record 2xx, 4xx, 5xx, and other
    private async Task<HttpResponseMessage> DownloadAsync(HttpRequestMessage httpRequestMessage, bool isSegment)
    {
        await Program.ConcurrentDownloads.WaitAsync();

        HttpResponseMessage httpResponseMessage = null;

        string writeLine = String.Empty;

        try
        {
            writeLine = $"{httpRequestMessage?.RequestUri?.OriginalString}";

            httpResponseMessage = await Program.HttpClient.SendAsync(httpRequestMessage, this.DoWorkCancellationTokenSource.Token);

            if (isSegment)
            {
                ++Program.TotalSegmentDownloads;

                await Program.BytesDownloadedMutex.WaitAsync(this.DoWorkCancellationTokenSource.Token);

                try
                {
                    Program.BytesDownloaded += (await httpResponseMessage.Content.ReadAsStreamAsync(this.DoWorkCancellationTokenSource.Token)).Length;
                }
                finally
                {
                    Program.BytesDownloadedMutex.Release();
                }
            }
        }
        catch (System.Threading.Tasks.TaskCanceledException ex)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{writeLine}: {ex}");

            throw;
        }
        finally
        {
            Program.ConcurrentDownloads.Release();
        }

        return httpResponseMessage;
    }

    public async Task<MediaPlaylist> GetMediaPlaylistAsync()
    {
        using HttpRequestMessage httpRequestMasterMessage = new()
        {
            Method = HttpMethod.Get,
            RequestUri = this.CurrentManifestUri ?? new Uri(this.MasterManifestUri)
        };
        httpRequestMasterMessage.Headers.TryAddWithoutValidation("User-Agent", $"HlsBenchmark/1.0 (HlsBenchmark); UserIndex/{this.Index}");

        using HttpResponseMessage httpResponseMasterMessage = await this.DownloadAsync(httpRequestMasterMessage, isSegment: false);
        httpResponseMasterMessage.EnsureSuccessStatusCode();

        if (this.CurrentManifestUri is null)
        {
            // If there is a redirect url, we get that here
            this.CurrentManifestUri = httpResponseMasterMessage.RequestMessage.RequestUri;
        }

        using StreamReader sr = new(await httpResponseMasterMessage.Content.ReadAsStreamAsync(this.DoWorkCancellationTokenSource.Token));
        string masterManifestText = await sr.ReadToEndAsync();

        MasterPlaylist masterPlaylist = MasterPlaylist.LoadFromText(masterManifestText);

        if (masterPlaylist.Streams.Count > 0)
        {
            using HttpRequestMessage httpRequestMediaMessage = new()
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri(masterPlaylist.Streams[0].Uri)
            };
            httpRequestMediaMessage.Headers.TryAddWithoutValidation("User-Agent", $"HlsBenchmark/1.0 (HlsBenchmark); UserIndex/{this.Index}");

            using HttpResponseMessage httpResponseMediaMessage = await this.DownloadAsync(httpRequestMediaMessage, isSegment: false);
            httpResponseMediaMessage.EnsureSuccessStatusCode();

            using StreamReader srMedia = new(await httpResponseMediaMessage.Content.ReadAsStreamAsync(this.DoWorkCancellationTokenSource.Token));
            string mediaManifestText = await srMedia.ReadToEndAsync();

            return MediaPlaylist.LoadFromText(mediaManifestText);
        }
        else
        {
            return MediaPlaylist.LoadFromText(masterManifestText);
        }
    }

    public async Task DoWork()
    {
        while (this.DoWorkCancellationTokenSource.Token.IsCancellationRequested is false)
        {
            MediaPlaylist mediaPlaylist = null;

            try
            {
                if (mediaPlaylist is null || mediaPlaylist.PlaylistType.ToString() != "VOD")
                {
                    mediaPlaylist = await GetMediaPlaylistAsync();
                }

                HashSet<string> currentSegments = new();

                string baseUrlWithoutM3u8 = this.CurrentManifestUri.AbsoluteUri
                    .Remove(this.CurrentManifestUri.AbsoluteUri.Length - this.CurrentManifestUri.Segments[^1].Length)
                    .TrimEnd('/');

                int segmentDownloadsRemaining = 2;

                foreach(Segment segment in mediaPlaylist.MediaSegments.SelectMany(ms => ms.Segments))
                {
                    Uri uri = null;

                    try
                    {
                        if (segment.Uri.StartsWith("http"))
                        {
                            uri = new Uri(segment.Uri);
                        }
                        else
                        {
                            uri = new Uri($"{baseUrlWithoutM3u8}/{segment.Uri}");
                        }
                    }
                    catch
                    { }

                    if (this.DownloadedSegments.Contains(uri.ToString()) is false && segmentDownloadsRemaining > 0)
                    {
                        currentSegments.Add(uri.ToString());

                        using HttpRequestMessage httpRequestSegmentMessage = new()
                        {
                            Method = HttpMethod.Get,
                            RequestUri = uri
                        };
                        httpRequestSegmentMessage.Headers.TryAddWithoutValidation("User-Agent", $"HlsBenchmark/1.0 (HlsBenchmark); UserIndex/{this.Index}");

                        Stopwatch downloadTime = Stopwatch.StartNew();

                        using HttpResponseMessage httpResponseSegmentMessage = await this.DownloadAsync(httpRequestSegmentMessage, isSegment: true);

                        if (httpResponseSegmentMessage.StatusCode == System.Net.HttpStatusCode.NotFound)
                        {
                            segmentDownloadsRemaining -= 1;

                            continue;
                        }

                        httpResponseSegmentMessage.EnsureSuccessStatusCode();

                        downloadTime.Stop();

                        this.DownloadedSegments.Add(uri.ToString());

                        TimeSpan remaingDelay = TimeSpan.FromSeconds(TimeSpan.FromSeconds(segment.Duration).TotalSeconds - downloadTime.Elapsed.TotalSeconds);

                        if (Math.Sign(remaingDelay.TotalSeconds) > 0)
                        {
                            await Task.Delay(remaingDelay);
                        }

                        segmentDownloadsRemaining -= 1;
                    }
                }

                // Prune Cache

                string[] urls = DownloadedSegments.ToArray();

                foreach (string url in urls)
                {
                    if (currentSegments.Contains(url))
                    {
                        continue;
                    }
                    else
                    {
                        DownloadedSegments.Remove(url);
                    }
                }

                if (mediaPlaylist?.PlaylistType?.ToString() == "VOD")
                {
                    if (currentSegments.Count == 0)
                    {
                        mediaPlaylist = null;
                        this.CurrentManifestUri = null;
                    }
                }
            }
            catch (System.Threading.Tasks.TaskCanceledException ex)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                break;
            }
            catch (System.Net.Http.HttpRequestException ex)
            {
                Console.WriteLine($"{ex}");

                mediaPlaylist = null;
                this.CurrentManifestUri = null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex}");
            }

            await Task.Delay(Program.SmallDelayToYieldCpu);
        }
    }
}
