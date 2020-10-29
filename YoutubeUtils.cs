using System;
using System.IO;
using System.Threading.Tasks;
using VideoLibrary;
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

namespace YT2AudioConverter
{
    public class YoutubeUtils : IUtils, IDisposable
    {
        private List<YouTubeVideo> _youtubeVideos;
        private string _youtubeApiKey = String.Empty;
        private readonly List<string> _videoIds = new List<string> { };

        private NLog.ILogger _logger;

        private string outputDir = String.Empty;

        public YoutubeUtils(IConfiguration configuration, NLog.ILogger logger)
        {
            ServiceProvider.BuildDi(configuration);
            _youtubeVideos = new List<YouTubeVideo>();
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

        public async Task<ConvertResponse> SaveVideosAsAudio(YoutubeToFileRequest request, string outputDir)
        {

            var requestId = request.Uri.Split('=')[1];
            this.outputDir = outputDir;

            if (request.IsPlaylist)
            {
                await ExtractYoutubeVideoInfoFromPlaylist(requestId);
            }
            else
            {
                _videoIds.Add(requestId);
            }


            var videosConverted = 0;
            using (var service = Client.For(YouTube.Default))
            {

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
                    ConvertVideosToAudio(request.TargetMediaType);

                }
                return GenerateResponse(videosConverted);
            }
        }

        private async Task ExtractYoutubeVideoInfoFromPlaylist(string playlistId)
        {
            var youtubeService = new YouTubeService(new BaseClientService.Initializer()
            {
                ApiKey = _youtubeApiKey,
                ApplicationName = "SaveRoomCP"
            });

            var playlistRequest = youtubeService.PlaylistItems.List("snippet");
            playlistRequest.PlaylistId = playlistId;
            playlistRequest.MaxResults = 20;

            // Retrieve the list of videos uploaded to the authenticated user's channel.
            var playlistItemsListResponse = await playlistRequest.ExecuteAsync();

            foreach (var playlistItem in playlistItemsListResponse.Items)
            {
                // Print information about each video.
                // Console.WriteLine("{0} ({1})", playlistItem.Snippet.Title, playlistItem.Snippet.ResourceId.VideoId);
                _videoIds.Add(playlistItem.Snippet.ResourceId.VideoId);
            }
        }

        private void ConvertVideosToAudio(string targetMediaType)
        {
            var status = $"Starting batch conversion to output type: {targetMediaType}";
            _logger.Info(status);

            try
            {
                switch (targetMediaType)
                {
                    case "mp3":
                        AudioConvertor.ConvertBatchToMp3(this.outputDir);
                        break;
                    case "wav":
                        AudioConvertor.ConvertBatchToWav(this.outputDir);
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
                AudioConvertor.RemoveVideoFiles(this.outputDir);
            }
        }

        private async Task<bool> RetrieveVideoFile(string videoUrl, Client<YouTubeVideo> youtube, string mediaType)
        {
            var vid = await youtube.GetVideoAsync(videoUrl);
            var videoFileName = $"{outputDir}/{vid.FullName}";
            var newVidName = $"{outputDir}/{FormatFileName(vid.FullName)}";

            if (!File.Exists(newVidName) && !File.Exists(newVidName.Replace(".mp4", $"{mediaType}")))
            {
                var status = $"Downloading {vid.FullName}...";
                Console.WriteLine(status);
                _logger.Info(status);
                File.WriteAllBytes($"{newVidName}", await vid.GetBytesAsync());
                return true;
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
