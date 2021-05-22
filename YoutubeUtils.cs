using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using YT2AudioConverter.Services;
using YT2AudioConverter.Models;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using YoutubeExplode.Converter;
using System.Reflection;
using YoutubeExplode.Videos;
using YoutubeExplode.Playlists;
using YoutubeExplode.Common;

namespace YT2AudioConverter
{
    public class YoutubeUtils : IUtils, IDisposable
    {
        private string _youtubeApiKey = String.Empty;
        private readonly List<string> _videoIds = new List<string> { };

        private readonly string FILE_BASE_PATH = $"{new DirectoryInfo(Assembly.GetExecutingAssembly().Location).Parent.FullName}\\Files";

        private NLog.ILogger _logger;

        private readonly YoutubeClient _youtube;

        public YoutubeUtils(IConfiguration configuration)
        {
            ServiceProvider.BuildDi(configuration);
            _logger = NLog.LogManager.GetCurrentClassLogger();
            _youtube = new YoutubeClient();
        }

        protected virtual void Dispose(bool disposing)
        {
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Save youtube video to specified file format
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public async Task<ConvertResponse> ConvertYoutubeUriToFile(YoutubeToFileRequest request)
        {
            var videosConverted = 0;
            var requestId = ExtractRequestId(request.IsPlaylist, request.Uri);
            try
            {
                if (request.IsPlaylist)
                {
                    var response = await DownloadFilesFromPlaylist(request.Uri, request.TargetMediaType);
                    videosConverted = response.VideoConverted;
                }
                else
                {
                    var resultSuccess = await RetrieveFile(request.Uri, request.TargetMediaType);
                    if (resultSuccess)
                        videosConverted++;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Playlist processing error: {ex}");
            }
            return GenerateResponse(videosConverted);
        }

        private string ExtractRequestId(bool isPlaylist, string url)
        {
            var requestId = string.Empty;
            switch (isPlaylist)
            {
                case true:
                    requestId = ExtractPlaylistIdFromRequestUri(url);
                    break;

                case false:
                    requestId = ExtractVideoIdFromRequestUri(url);
                    break;
            }
            return requestId;
        }

        private string ExtractPlaylistIdFromRequestUri(string uri)
        {
            var id = uri.Split(new string[] { "list=" }, StringSplitOptions.None)[1];

            if (id.Contains("index="))
            {
                id = uri.Split('=')[1];
            }

            return id;
        }

        private string ExtractVideoIdFromRequestUri(string uri)
        {
            var id = uri.Split(new string[] { "v=" }, StringSplitOptions.None)[1];
            return id;
        }

        private async Task<DownloadPlaylistResponse> DownloadFilesFromPlaylist(string playlistId, string mediaType)
        {
            var playlist = await _youtube.Playlists.GetAsync(playlistId);
            var successed = false;
            var videosConverted = 0;
            DownloadPlaylistResponse response = new DownloadPlaylistResponse()
            {
                VideoConverted = videosConverted,
                Successed = successed
            };

            // Get all playlist videos
            var playlistVideos = await _youtube.Playlists.GetVideosAsync(playlist.Id);

            if (playlistVideos == null || !playlistVideos.Any())
            {
                return response;
            }

            foreach (var vid in playlistVideos)
            {
                await RetrieveFile(vid.Url, mediaType);
                videosConverted++;
            }
            successed = true;

            response.Successed = successed;
            response.VideoConverted = videosConverted;

            return response;
        }

        private async Task<bool> RetrieveFile(string videoUrl, string mediaType)
        {
            var metaData = await _youtube.Videos.GetAsync(videoUrl);
            var newFileName = FormatFileName(metaData.Title);
            var newFilePath = $"{FILE_BASE_PATH}\\{newFileName}.mp4";

            if (!Directory.Exists(FILE_BASE_PATH))
            {
                Directory.CreateDirectory(FILE_BASE_PATH);
            }

            if (!File.Exists(newFilePath) && !File.Exists(newFilePath.Replace(".mp4", $"{mediaType}")))
            {
                await DownloadFile(metaData, newFileName, mediaType);
                return true;
            }
            return false;
        }

        private async Task DownloadVideo(PlaylistVideo metaData, string newVidName)
        {
            var streamManifest = await _youtube.Videos.Streams.GetManifestAsync(metaData.Id);

            if (streamManifest != null)
            {
                var status = $"Downloading {metaData.Title}...";
                Console.WriteLine(status);
                _logger.Info(status);

                await GetFileFromStreamManifest(streamManifest, newVidName, "mp4");
            }
        }

        private async Task DownloadFile(Video metaData, string newFileName, string mediaType)
        {
            var streamManifest = await _youtube.Videos.Streams.GetManifestAsync(metaData.Id);

            if (streamManifest != null)
            {
                var status = $"Downloading {metaData.Title} as {mediaType}";
                Console.WriteLine(status);
                _logger.Info(status);

                await GetFileFromStreamManifest(streamManifest, newFileName, mediaType);
                Console.WriteLine($"{newFileName} has been downloaded");
            }
        }

        private async Task GetFileFromStreamManifest(StreamManifest streamManifest, string newVidName, string mediaType)
        {
            // Select streams (highest video quality / highest bitrate audio)
            IVideoStreamInfo videoStreamInfo = null;

            var audioStreamInfo = streamManifest
                .GetAudioOnlyStreams()
                .GetWithHighestBitrate();

            if (mediaType.Contains("mp4"))
            {
                videoStreamInfo = streamManifest
                   .GetVideoOnlyStreams()
                   .Where(s => s.Container == Container.Mp4)
                   .GetWithHighestVideoQuality();
            }

            var streamInfos = new IStreamInfo[] { audioStreamInfo };
            if (videoStreamInfo != null)
            {
                streamInfos = streamInfos.Concat(new IStreamInfo[] { videoStreamInfo }).ToArray();
            }

            if (streamInfos != null)
            {
                // Download and process them into one file
                await _youtube.Videos.DownloadAsync(streamInfos, new ConversionRequestBuilder($"{FILE_BASE_PATH}\\{newVidName}.{mediaType}").Build());
            }
        }

        private string FormatFileName(string fileName)
        {
            return fileName
                .Replace(" ", "_")
                .Replace(":", "")
                .Replace("\"", "")
                .Replace("(", "")
                .Replace("`", "")
                .Replace("'", "")
                .Replace(")", "")
                .Replace("|", "_")
                .Replace("/", "_");
        }

        private string FormatVideoUri(string id)
        {
            var videoUrl = $"https://www.youtube.com/watch?v=id";
            return videoUrl.Replace("id", id);
        }

        private ConvertResponse GenerateResponse(int videosConverted)
        {
            var newResponse = new ConvertResponse { Message = "", Succeeded = false };

            if (videosConverted > 0)
            {
                newResponse.Message = $"Converted {videosConverted} files successfully.";
                newResponse.Succeeded = true;
            }
            else
            {
                newResponse.Error = $"Unable to download file(s) for specified link";
            }

            return newResponse;
        }
    }
}