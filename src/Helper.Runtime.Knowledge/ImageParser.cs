using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Knowledge
{
    public class ImageParser : IDocumentParser
    {
        private readonly AILink _ai;
        private const string VisionModel = "ingu627/Qwen2.5-VL-7B-Instruct-Q5_K_M:latest";

        public ImageParser(AILink ai)
        {
            _ai = ai;
        }

        public bool CanParse(string ext) 
            => ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp" || ext == ".webp";

        public async Task<string> ParseAsync(string path, CancellationToken ct)
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(path, ct);
                var base64 = Convert.ToBase64String(bytes);

                var prompt = "Describe this image in detail and extract any visible text. If it is a technical diagram or code screenshot, explain the logic.";
                
                var analysis = await _ai.AskAsync(prompt, ct, overrideModel: VisionModel, base64Image: base64);
                
                return $"[Image Analysis: {Path.GetFileName(path)}]\n{analysis}";
            }
            catch (Exception ex)
            {
                return $"[Error parsing image {Path.GetFileName(path)}]: {ex.Message}";
            }
        }

        public async Task ParseStreamingAsync(string path, Func<string, Task> onChunk, CancellationToken ct = default)
            => await onChunk(await ParseAsync(path, ct));
    }
}

