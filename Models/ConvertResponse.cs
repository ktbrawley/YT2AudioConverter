namespace YoutubeConverter.Models
{
    public class ConvertResponse
    {
        public bool Succeeded { get; set; }
        public string Error { get; set; }
        public string Message { get; set; }
    }
}