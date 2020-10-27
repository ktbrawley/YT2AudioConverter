namespace YoutubeConverter.Models
{
    public class YoutubeToFileRequest
    {
        public string Uri { get; set; }
        public bool IsPlaylist { get; set; }

        public string TargetMediaType { get; set; }
    }
}