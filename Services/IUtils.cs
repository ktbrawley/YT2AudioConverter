using System.Collections.Generic;
using System.Threading.Tasks;
using YoutubeConverter.Models;

namespace YT2AudioConverter.Services
{
    public interface IUtils
    {
        Task<ConvertResponse> SaveVideosAsAudio(YoutubeToFileRequest request, string outputDir);
    }
}