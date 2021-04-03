using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Google.Apis.YouTube.v3;
using Google.Apis.Services;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using YT2AudioConverter.Services;
using YT2AudioConverter.Models;
using Microsoft.Extensions.Logging;
using NLog;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using YoutubeExplode.Converter;
using System.Reflection;

namespace YT2AudioConverter
{
    public class YoutubeUtils : IUtils, IDisposable
    {
        private string _youtubeApiKey = String.Empty;
        private readonly List<string> _videoIds = new List<string> { };

        private readonly string FILE_BASE_PATH = $"{new DirectoryInfo(Assembly.GetExecutingAssembly().Location).Parent.FullName}\\Files";

        private NLog.ILogger _logger;

        public YoutubeUtils(IConfiguration configuration)
        {
            ServiceProvider.BuildDi(configuration);
            _youtubeApiKey = configuration.GetSection("YoutubeApiKey").Value;
            _logger = NLog.LogManager.GetCurrentClassLogger();
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
            var requestId = request.Uri.Split('=')[1];

            if (request.IsPlaylist)
            {
                await ExtractYoutubeVideoInfoFromPlaylist(requestId);
            }
            else
            {
                _videoIds.Add(requestId);
            }

            var videosConverted = 0;

            var service = new YoutubeClient();
            foreach (var id in _videoIds)
            {
                var videoUrl = FormatVideoUri(id);

                try
                {
                    var resultSuccess = await RetrieveVideoFile(videoUrl, service, request.TargetMediaType);
                    if (resultSuccess)
                        videosConverted++;
                }
                catch (Exception ex)
                {
                    _logger.Warn($"Issue downloading item {videoUrl}: {ex.Message}");
                }
            }
            if (videosConverted > 0)
            {
                ConvertVideosToFile(request.TargetMediaType);
            }
            return GenerateResponse(videosConverted);
        }

        private async Task ExtractYoutubeVideoInfoFromPlaylist(string playlistId)
        {
            try
            {
                var youtubeService = new YouTubeService(new BaseClientService.Initializer()
                {
                    ApiKey = _youtubeApiKey,
                    ApplicationName = "SaveRoomCP"
                });

                var playlistRequest = youtubeService.PlaylistItems.List("snippet");
                playlistRequest.PlaylistId = playlistId;
                playlistRequest.MaxResults = 20;

                _logger.Info($"Attempting to acquire video information from playlist url");

                // Retrieve the list of videos uploaded to the authenticated user's channel.
                var playlistItemsListResponse = await playlistRequest.ExecuteAsync();

                foreach (var playlistItem in playlistItemsListResponse.Items)
                {
                    // Print information about each video.
                    // Console.WriteLine("{0} ({1})", playlistItem.Snippet.Title, playlistItem.Snippet.ResourceId.VideoId);
                    _videoIds.Add(playlistItem.Snippet.ResourceId.VideoId);
                }

                _logger.Info($"Playlist consists of {_videoIds.Count()} songs");
            }
            catch (Exception ex)
            {
                _logger.Error($"Playlist processing error: {ex}");
            }
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

        private async Task<bool> RetrieveVideoFile(string videoUrl, YoutubeClient youtube, string mediaType)
        {
            var metaData = await youtube.Videos.GetAsync(videoUrl);
            var newVidName = $"{FILE_BASE_PATH}\\{FormatFileName(metaData.Title)}";

            var streamManifest = await youtube.Videos.Streams.GetManifestAsync(metaData.Id);

            if (!Directory.Exists(FILE_BASE_PATH))
            {
                Directory.CreateDirectory(FILE_BASE_PATH);
            }

            if (!File.Exists(newVidName) && !File.Exists(newVidName.Replace(".mp4", $"{mediaType}")))
            {
                var status = $"Downloading {metaData.Title}...";
                Console.WriteLine(status);
                _logger.Info(status);

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
                    // Download the stream to file
                    // Download and process them into one file
                    await youtube.Videos.DownloadAsync(streamInfos, new ConversionRequestBuilder($"{metaData.Title}.mp4").Build());
                    return true;
                }
            }
            return false;
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
                .Replace(")", "");
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