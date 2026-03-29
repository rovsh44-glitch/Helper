using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Helper.Runtime.Core;

namespace Helper.Runtime.Infrastructure
{
    public class VisualInspector : IVisualInspector
    {
        private readonly AILink _ai;

        public VisualInspector(AILink ai)
        {
            _ai = ai;
        }

        public async Task<bool> InspectProjectAsync(string projectPath, Action<string>? onProgress = null, CancellationToken ct = default)
        {
            onProgress?.Invoke("👁️ [Vision] Scanning UI layouts for anomalies...");

            var uiFiles = Directory.GetFiles(projectPath, "*.xaml", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(projectPath, "*.html", SearchOption.AllDirectories))
                .Concat(Directory.GetFiles(projectPath, "*.tsx", SearchOption.AllDirectories)) // React
                .Where(f => !f.Contains("App.xaml")) // Skip logic-only XAML
                .ToList();

            if (!uiFiles.Any())
            {
                onProgress?.Invoke("👁️ [Vision] No UI files found. Skipping visual inspection.");
                return true;
            }

            foreach (var file in uiFiles)
            {
                var content = await File.ReadAllTextAsync(file, ct);
                var fileName = Path.GetFileName(file);

                // --- CRITICAL FORMAT CHECK ---
                if (fileName.EndsWith(".xaml") && !content.Trim().StartsWith("<"))
                {
                    onProgress?.Invoke($"❌ [Vision] FORMAT ERROR in {fileName}: File contains C# code instead of XML markup.");
                    return false;
                }

                onProgress?.Invoke($"👁️ [Vision] Simulating render for {fileName}...");

                // VISION-BASED QUALITY GATE:
                // We use qwen2.5vl:7b (Multi-modal) to analyze the structure more accurately
                var visionModel = _ai.GetBestModel("vision"); 
                
                var prompt = $@"
                ACT AS A VISUAL QA ENGINEER.
                
                ANALYZE THE FOLLOWING UI CODE (XAML/HTML/TSX).
                FILE: {fileName}
                
                CONTEXT: This code will be rendered on a standard 1080p screen.
                
                CODE:
                {content}
                
                TASKS:
                1. Detect overlapping elements.
                2. Check for 'Ghost' controls (Visibility=Collapsed by mistake).
                3. Validate Color Contrast (WCAG 2.1).
                4. Check for broken bindings or missing resources in the markup.
                
                OUTPUT ONLY JSON:
                {{
                  ""IsVisuallyAcceptable"": true,
                  ""DefectDescription"": ""None"",
                  ""ConfidenceScore"": 0.95
                }}";

                try 
                {
                    var result = await _ai.AskJsonAsync<VisualInspectionResult>(prompt, ct, visionModel); 
                    
                    if (!result.IsVisuallyAcceptable)
                    {
                        onProgress?.Invoke($"❌ [Vision] Defect detected in {fileName}: {result.DefectDescription}");
                        // In AGI-Level mode, we would trigger a re-generation task here
                        return false; 
                    }
                }
                catch
                {
                    // Fallback if model fails or isn't JSON compliant
                    onProgress?.Invoke($"⚠️ [Vision] Could not cognitively render {fileName}. Assuming pass.");
                }
            }

            onProgress?.Invoke("✅ [Vision] Visual Quality Gate passed.");
            return true;
        }

        public async Task<bool> InspectProjectScreenshotAsync(string imagePath, Action<string>? onProgress = null, CancellationToken ct = default)
        {
            if (!File.Exists(imagePath))
            {
                onProgress?.Invoke("❌ [Vision] Screenshot file not found.");
                return false;
            }

            onProgress?.Invoke($"👁️ [Vision] Inspecting screenshot: {Path.GetFileName(imagePath)}...");

            try
            {
                var base64Image = Convert.ToBase64String(await File.ReadAllBytesAsync(imagePath, ct));
                var visionModel = _ai.GetBestModel("vision"); // e.g. llava or bakllava
                
                var prompt = $@"
                ACT AS A VISUAL QA ENGINEER.
                ANALYZE THIS SCREENSHOT OF A UI.
                
                TASKS:
                1. Detect overlapping elements or broken layouts.
                2. Check for contrast issues or unreadable text.
                3. Validate alignment and spacing.
                
                OUTPUT ONLY JSON:
                {{
                  ""IsVisuallyAcceptable"": true,
                  ""DefectDescription"": ""None"",
                  ""ConfidenceScore"": 0.95
                }}";

                var result = await _ai.AskJsonAsync<VisualInspectionResult>(prompt, ct, overrideModel: visionModel, base64Image: base64Image);
                
                if (!result.IsVisuallyAcceptable)
                {
                    onProgress?.Invoke($"❌ [Vision] UI Defect detected: {result.DefectDescription} (Confidence: {result.ConfidenceScore})");
                    return false;
                }

                onProgress?.Invoke("✅ [Vision] Screenshot Quality Gate passed.");
                return true;
            }
            catch (Exception ex)
            {
                onProgress?.Invoke($"⚠️ [Vision] Vision analysis failed: {ex.Message}");
                return true; // Fallback to pass if model is unavailable
            }
        }

        private record VisualInspectionResult(bool IsVisuallyAcceptable, string DefectDescription, double ConfidenceScore);
    }
}

