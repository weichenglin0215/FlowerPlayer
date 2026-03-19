using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace FlowerPlayer.Helpers
{
    public static class FFmpegHelper
    {
        private static string _ffmpegPath = null;

        private static string GetFFmpegPath()
        {
            if (_ffmpegPath != null) return _ffmpegPath;

            try
            {
                var appFolder = Windows.ApplicationModel.Package.Current.InstalledLocation.Path;
                var ffmpegPath = Path.Combine(appFolder, "FFmpeg", "ffmpeg.exe");

                if (File.Exists(ffmpegPath))
                {
                    _ffmpegPath = ffmpegPath;
                    System.Diagnostics.Debug.WriteLine($"FFmpegHelper: FFmpeg found at {ffmpegPath}");
                }
                else
                {
                    _ffmpegPath = "ffmpeg"; // Try system PATH
                    System.Diagnostics.Debug.WriteLine("FFmpegHelper: FFmpeg not found in app folder, using system PATH");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FFmpegHelper: Error finding FFmpeg: {ex.Message}");
                _ffmpegPath = "ffmpeg";
            }
            return _ffmpegPath;
        }

        /// <summary>
        /// Clips a video segment without re-encoding (Direct Stream Copy).
        /// </summary>
        /// <param name="inputPath">Full path to the source video.</param>
        /// <param name="outputPath">Full path for the output video.</param>
        /// <param name="startTime">Start time of the clip.</param>
        /// <param name="duration">Duration of the clip.</param>
        /// <returns>Task representing the operation.</returns>
        public static async Task ClipVideoAsync(string inputPath, string outputPath, TimeSpan startTime, TimeSpan duration)
        {
            var ffmpegPath = GetFFmpegPath();
            
            // Format time strings for FFmpeg
            // -ss: Start time
            // -t: Duration
            // -c copy: Copy streams (no re-encoding)
            // -map 0: include all streams from input 0
            // -y: Overwrite output file if it exists (though FileSavePicker usually handles confirmation)
            
            string startStr = startTime.ToString(@"hh\:mm\:ss\.fff");
            string durationStr = duration.ToString(@"hh\:mm\:ss\.fff");
            
            // NOTE: Put -ss BEFORE -i for faster seeking (input seeking)
            string arguments = $"-ss {startStr} -i \"{inputPath}\" -t {durationStr} -c copy -map 0 -y \"{outputPath}\"";

            var processStartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(ffmpegPath) ?? Environment.CurrentDirectory
            };

            await Task.Run(() =>
            {
                using (var process = new Process())
                {
                    process.StartInfo = processStartInfo;
                    
                    System.Diagnostics.Debug.WriteLine($"FFmpegHelper: Starting clip...");
                    System.Diagnostics.Debug.WriteLine($"FFmpegHelper: Command: {ffmpegPath} {arguments}");

                    process.Start();

                    // Read stderr asynchronously to avoid deadlocks (FFmpeg writes logs to stderr)
                    // We can collect it if we want to show errors, or just log it.
                    string errorOutput = process.StandardError.ReadToEnd();
                    
                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"FFmpegHelper: Error clipping video. ExitCode: {process.ExitCode}");
                        System.Diagnostics.Debug.WriteLine($"FFmpegHelper: Stderr: {errorOutput}");
                        throw new Exception($"FFmpeg failed with exit code {process.ExitCode}. Details: {errorOutput}");
                    }
                    
                    System.Diagnostics.Debug.WriteLine("FFmpegHelper: Clip completed successfully.");
                }
            });
        }
        public static async Task TranscodeClipAsync(string inputPath, string outputPath, TimeSpan startTime, TimeSpan duration, TranscodeOptions options)
        {
            var ffmpegPath = GetFFmpegPath();
            
            string startStr = startTime.ToString(@"hh\:mm\:ss\.fff");
            string durationStr = duration.ToString(@"hh\:mm\:ss\.fff");
            
            string arguments = $"-ss {startStr} -i \"{inputPath}\" -t {durationStr}";

            // Video settings
            arguments += $" -c:v {options.VideoCodec}";
            if (options.VideoCodec != "copy")
            {
                if (options.Width > 0 && options.Height > 0)
                {
                    arguments += $" -s {options.Width}x{options.Height}";
                }
                if (options.FrameRate > 0)
                {
                    arguments += $" -r {options.FrameRate}";
                }
                if (options.VideoBitrateKbps > 0)
                {
                    arguments += $" -b:v {options.VideoBitrateKbps}k";
                }
            }

            // Audio settings
            arguments += $" -c:a {options.AudioCodec}";
            if (options.AudioCodec != "copy")
            {
                if (options.AudioChannels > 0)
                {
                    arguments += $" -ac {options.AudioChannels}";
                }
                if (options.AudioSampleRate > 0)
                {
                    arguments += $" -ar {options.AudioSampleRate}";
                }
                if (options.AudioBitrateKbps > 0)
                {
                    arguments += $" -b:a {options.AudioBitrateKbps}k";
                }
            }

            arguments += $" -map 0 -y \"{outputPath}\"";

            var processStartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(ffmpegPath) ?? Environment.CurrentDirectory
            };

            await Task.Run(() =>
            {
                using (var process = new Process())
                {
                    process.StartInfo = processStartInfo;
                    
                    System.Diagnostics.Debug.WriteLine($"FFmpegHelper: Starting clip transcode...");
                    System.Diagnostics.Debug.WriteLine($"FFmpegHelper: Command: {ffmpegPath} {arguments}");

                    process.Start();

                    string errorOutput = process.StandardError.ReadToEnd();
                    
                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"FFmpegHelper: Error clipping video. ExitCode: {process.ExitCode}");
                        System.Diagnostics.Debug.WriteLine($"FFmpegHelper: Stderr: {errorOutput}");
                        throw new Exception($"FFmpeg failed with exit code {process.ExitCode}. Details: {errorOutput}");
                    }
                    
                    System.Diagnostics.Debug.WriteLine("FFmpegHelper: Transcode Clip completed successfully.");
                }
            });
        }
    }

    public class TranscodeOptions
    {
        public string VideoCodec { get; set; } = "libx264";
        public uint Width { get; set; } = 0;
        public uint Height { get; set; } = 0;
        public uint VideoBitrateKbps { get; set; } = 0;
        public double FrameRate { get; set; } = 0;
        
        public string AudioCodec { get; set; } = "aac";
        public uint AudioChannels { get; set; } = 0;
        public uint AudioSampleRate { get; set; } = 0;
        public uint AudioBitrateKbps { get; set; } = 0;
    }
}
