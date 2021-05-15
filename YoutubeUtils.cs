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
        /// <param name="filePathOverride">Override the default file output path</param>
        /// <returns></returns>
        public async Task<ConvertResponse> SaveVideosAsFiles(YoutubeToFileRequest request)
        {
            var videosConverted = 0;
            var requestId = ExtractRequestId(request.IsPlaylist, request.Uri);

            if (request.IsPlaylist)
            {
                try
                {
                    var response = await DownloadVideosFromPlaylist(requestId);
                    videosConverted = response.VideoConverted;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Playlist processing error: {ex}");
                }
            }
            else
            {
                _videoIds.Add(requestId);

                foreach (var id in _videoIds)
                {
                    var videoUrl = FormatVideoUri(id);

                    try
                    {
                        var resultSuccess = await RetrieveVideoFile(videoUrl, request.TargetMediaType);
                        if (resultSuccess)
                            videosConverted++;
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn($"Issue downloading item {videoUrl}: {ex.Message}");
                    }
                }
            }

            try
            {
                if (videosConverted > 0)
                {
                    ConvertVideosToFile(request.TargetMediaType);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error converting video files: {ex.Message}");
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

        private async Task<DownloadPlaylistResponse> DownloadVideosFromPlaylist(string playlistId)
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
                var newVidName = FormatFileName(vid.Title);
                var newVidPath = $"{FILE_BASE_PATH}\\{newVidName}";
                await DownloadVideo(vid, newVidName);
                videosConverted++;
            }
            successed = true;

            response.Successed = successed;
            response.VideoConverted = videosConverted;

            return response;
        }

        private void ConvertVideosToFile(string targetMediaType)
        {
            var status = $"Starting batch conversion to output type: {targetMediaType}";
            _logger.Info(status);

            try
            {
                switch (targetMediaType)
                {
                    case "mp3":
                        FileConverter.ConvertBatchToMp3(FILE_BASE_PATH);
                        break;

                    case "wav":
                        FileConverter.ConvertBatchToWav(FILE_BASE_PATH);
                        break;

                    case "mp4":
                        Console.WriteLine("Video file has been downloaded");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex.Message);
                throw;
            }
            finally
            {
                _logger.Info($"Batch conversion completed successful ly");
                _logger.Info($"Cleaning up temp video files");
                if (targetMediaType != "mp4")
                {
                    FileConverter.RemoveVideoFiles(FILE_BASE_PATH);
                    _logger.Info($"Video files removed");
                }
            }
        }

        private async Task<bool> RetrieveVideoFile(string videoUrl, string mediaType)
        {
            var metaData = await _youtube.Videos.GetAsync(videoUrl);
            var newVidName = FormatFileName(metaData.Title);
            var newVidPath = $"{FILE_BASE_PATH}\\{newVidName}";

            if (!Directory.Exists(FILE_BASE_PATH))
            {
                Directory.CreateDirectory(FILE_BASE_PATH);
            }

            if (!File.Exists(newVidPath) && !File.Exists(newVidPath.Replace(".mp4", $"{mediaType}")))
            {
                await DownloadVideo(metaData, newVidName);
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

                await GetVideoFromStreamManifest(streamManifest, newVidName);
            }
        }

        private async Task DownloadVideo(Video metaData, string newVidName)
        {
            var streamManifest = await _youtube.Videos.Streams.GetManifestAsync(metaData.Id);

            if (streamManifest != null)
            {
                var status = $"Downloading {metaData.Title}...";
                Console.WriteLine(status);
                _logger.Info(status);

                await GetVideoFromStreamManifest(streamManifest, newVidName);
            }
        }

        private async Task GetVideoFromStreamManifest(StreamManifest streamManifest, string newVidName)
        {
            // Select streams (highest video quality / highest bitrate audio)
            var audioStreamInfo = streamManifest
                .GetAudio()
                .WithHighestBitrate();
            var videoStreamInfo = streamManifest
                .GetVideo()
                .Where(s => s.Container == Container.Mp4)
                .WithHighestVideoQuality();

            var streamInfos = new IStreamInfo[] { audioStreamInfo, videoStreamInfo };

            if (streamInfos != null)
            {
                // Download and process them into one file
                await _youtube.Videos.DownloadAsync(streamInfos, new ConversionRequestBuilder($"{FILE_BASE_PATH}\\{newVidName}.mp4").Build());
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