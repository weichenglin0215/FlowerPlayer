using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage;
using System.IO;
using System.Linq;
using Windows.Media.Core;
using System.Diagnostics;
using System.Threading;

namespace FlowerPlayer.Helpers
{
    public static class MediaHelper
    {
        private static string _ffmpegPath = null;
        
        private static string GetFFmpegPath()
        {
            if (_ffmpegPath != null) return _ffmpegPath;
            
            // 設置 FFmpeg 路徑為應用程序目錄下的 FFmpeg 文件夾
            var appFolder = Windows.ApplicationModel.Package.Current.InstalledLocation.Path;
            var ffmpegPath = Path.Combine(appFolder, "FFmpeg", "ffmpeg.exe");
            
            if (File.Exists(ffmpegPath))
            {
                _ffmpegPath = ffmpegPath;
                System.Diagnostics.Debug.WriteLine($"MediaHelper: FFmpeg found at {ffmpegPath}");
                return _ffmpegPath;
            }
            else
            {
                // 如果應用內沒有 FFmpeg，嘗試使用系統 PATH 中的
                _ffmpegPath = "ffmpeg"; // 使用系統 PATH
                System.Diagnostics.Debug.WriteLine("MediaHelper: FFmpeg not found in app folder, using system PATH");
                return _ffmpegPath;
            }
        }
        
        public static async Task<float[]> GenerateWaveformAsync(StorageFile file, int sampleCount, bool firstFiveMinutesOnly = false)
        {
            var startTime = DateTime.Now;
            try
            {
                var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                System.Diagnostics.Debug.WriteLine($"[{elapsed:F0}ms] GenerateWaveformAsync: Starting waveform generation for {file.Name}, sampleCount={sampleCount}");
                
                // 使用 FFmpeg 讀取真實音頻數據
                string filePath = file.Path;
                elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                System.Diagnostics.Debug.WriteLine($"[{elapsed:F0}ms] GenerateWaveformAsync: Media file path: {filePath}");
                System.Diagnostics.Debug.WriteLine($"[{elapsed:F0}ms] GenerateWaveformAsync: Media file name: {file.Name}");
                
                // 獲取媒體信息
                TimeSpan duration = TimeSpan.Zero;
                try
                {
                    // 嘗試從文件屬性獲取持續時間
                    elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                    System.Diagnostics.Debug.WriteLine($"[{elapsed:F0}ms] GenerateWaveformAsync: Getting file properties...");
                    var audioProperties = await file.Properties.GetMusicPropertiesAsync();
                    duration = audioProperties.Duration;
                    elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                    System.Diagnostics.Debug.WriteLine($"[{elapsed:F0}ms] GenerateWaveformAsync: Got duration from file properties: {duration.TotalSeconds}s");
                }
                catch (Exception ex2)
                {
                    elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                    System.Diagnostics.Debug.WriteLine($"[{elapsed:F0}ms] GenerateWaveformAsync: Error getting duration: {ex2.Message}");
                    // 使用默認值
                    duration = TimeSpan.FromSeconds(60);
                }
                
                // 創建臨時文件來存儲 PCM 數據
                elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                System.Diagnostics.Debug.WriteLine($"[{elapsed:F0}ms] GenerateWaveformAsync: Creating temporary PCM file...");
                var tempFolder = Windows.Storage.ApplicationData.Current.TemporaryFolder;
                var tempPcmFile = await tempFolder.CreateFileAsync($"waveform_{Guid.NewGuid()}.pcm", Windows.Storage.CreationCollisionOption.ReplaceExisting);
                
                try
                {
                    // 直接調用 FFmpeg 命令行（避免使用 FFMpegCore，因為 AOT 編譯禁用反射）
                    var ffmpegPath = GetFFmpegPath();
                    
                    // 構建完整的 FFmpeg 命令
                    string durationLimit = firstFiveMinutesOnly ? "-t 300" : ""; // -t 300 表示只處理前300秒（5分鐘）
                    string ffmpegCommand = $"-i \"{filePath}\" {durationLimit} -f s16le -ac 1 -ar 8000 -y \"{tempPcmFile.Path}\"";
                    string fullCommand = $"{ffmpegPath} {ffmpegCommand}";
                    
                    elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                    System.Diagnostics.Debug.WriteLine($"[{elapsed:F0}ms] GenerateWaveformAsync: ========== FFmpeg Command ==========");
                    System.Diagnostics.Debug.WriteLine($"[{elapsed:F0}ms] GenerateWaveformAsync: FFmpeg executable: {ffmpegPath}");
                    System.Diagnostics.Debug.WriteLine($"[{elapsed:F0}ms] GenerateWaveformAsync: Input file path: {filePath}");
                    System.Diagnostics.Debug.WriteLine($"[{elapsed:F0}ms] GenerateWaveformAsync: Output PCM file: {tempPcmFile.Path}");
                    System.Diagnostics.Debug.WriteLine($"[{elapsed:F0}ms] GenerateWaveformAsync: Full command: {fullCommand}");
                    System.Diagnostics.Debug.WriteLine($"[{elapsed:F0}ms] GenerateWaveformAsync: ======================================");
                    
                    // 使用 Task.Run 在線程池中執行，避免 WinRT 異步問題
                    var taskRunStart = DateTime.Now;
                    elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                    System.Diagnostics.Debug.WriteLine($"[{elapsed:F0}ms] GenerateWaveformAsync: About to start Task.Run");
                    await Task.Run(() =>
                    {
                        var taskRunElapsed = (DateTime.Now - taskRunStart).TotalMilliseconds;
                        System.Diagnostics.Debug.WriteLine($"[{taskRunElapsed:F0}ms] GenerateWaveformAsync: Inside Task.Run");
                        Process process = null;
                        try
                        {
                            taskRunElapsed = (DateTime.Now - taskRunStart).TotalMilliseconds;
                            System.Diagnostics.Debug.WriteLine($"[{taskRunElapsed:F0}ms] GenerateWaveformAsync: Creating ProcessStartInfo");
                            var processStartInfo = new ProcessStartInfo
                            {
                                FileName = ffmpegPath,
                                Arguments = ffmpegCommand, // 使用預先構建的命令
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                CreateNoWindow = true,
                                WorkingDirectory = Path.GetDirectoryName(ffmpegPath) ?? Environment.CurrentDirectory
                            };
                            
                            taskRunElapsed = (DateTime.Now - taskRunStart).TotalMilliseconds;
                            System.Diagnostics.Debug.WriteLine($"[{taskRunElapsed:F0}ms] GenerateWaveformAsync: ProcessStartInfo created");
                            System.Diagnostics.Debug.WriteLine($"[{taskRunElapsed:F0}ms] GenerateWaveformAsync:   FileName: {processStartInfo.FileName}");
                            System.Diagnostics.Debug.WriteLine($"[{taskRunElapsed:F0}ms] GenerateWaveformAsync:   Arguments: {processStartInfo.Arguments}");
                            System.Diagnostics.Debug.WriteLine($"[{taskRunElapsed:F0}ms] GenerateWaveformAsync:   WorkingDirectory: {processStartInfo.WorkingDirectory}");
                            
                            taskRunElapsed = (DateTime.Now - taskRunStart).TotalMilliseconds;
                            System.Diagnostics.Debug.WriteLine($"[{taskRunElapsed:F0}ms] GenerateWaveformAsync: Creating Process object");
                            process = new Process();
                            process.StartInfo = processStartInfo;
                            
                            taskRunElapsed = (DateTime.Now - taskRunStart).TotalMilliseconds;
                            System.Diagnostics.Debug.WriteLine($"[{taskRunElapsed:F0}ms] GenerateWaveformAsync: Starting FFmpeg process...");
                            bool started = process.Start();
                            taskRunElapsed = (DateTime.Now - taskRunStart).TotalMilliseconds;
                            System.Diagnostics.Debug.WriteLine($"[{taskRunElapsed:F0}ms] GenerateWaveformAsync: Process.Start() returned: {started}, PID={process.Id}");
                            
                            if (!started)
                            {
                                throw new Exception("Failed to start FFmpeg process");
                            }
                            
                            // 使用超時機制，避免無限等待（根據文件時長動態調整）
                            int timeoutMs = (int)(duration.TotalSeconds * 3000) + 20000; // 文件時長 * 3 + 20秒緩衝
                            if (timeoutMs < 20000) timeoutMs = 20000; // 最少20秒
                            if (timeoutMs > 180000) timeoutMs = 180000; // 最多180秒（3分鐘）
                            
                            taskRunElapsed = (DateTime.Now - taskRunStart).TotalMilliseconds;
                            System.Diagnostics.Debug.WriteLine($"[{taskRunElapsed:F0}ms] GenerateWaveformAsync: Waiting for FFmpeg to exit (timeout: {timeoutMs}ms)...");
                            bool exited = process.WaitForExit(timeoutMs);
                            taskRunElapsed = (DateTime.Now - taskRunStart).TotalMilliseconds;
                            
                            if (!exited)
                            {
                                System.Diagnostics.Debug.WriteLine($"[{taskRunElapsed:F0}ms] GenerateWaveformAsync: WARNING - FFmpeg did not exit within timeout, killing process");
                                try
                                {
                                    process.Kill();
                                    process.WaitForExit(5000); // 等待進程被殺死
                                }
                                catch (Exception exKill)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[{taskRunElapsed:F0}ms] GenerateWaveformAsync: Error killing process: {exKill.Message}");
                                }
                                throw new Exception($"FFmpeg process did not complete within {timeoutMs}ms");
                            }
                            
                            taskRunElapsed = (DateTime.Now - taskRunStart).TotalMilliseconds;
                            System.Diagnostics.Debug.WriteLine($"[{taskRunElapsed:F0}ms] GenerateWaveformAsync: FFmpeg exited, ExitCode={process.ExitCode}");
                            
                            // 進程結束後再讀取錯誤輸出
                            string errorOutput = string.Empty;
                            try
                            {
                                taskRunElapsed = (DateTime.Now - taskRunStart).TotalMilliseconds;
                                System.Diagnostics.Debug.WriteLine($"[{taskRunElapsed:F0}ms] GenerateWaveformAsync: Reading stderr...");
                                errorOutput = process.StandardError.ReadToEnd();
                                taskRunElapsed = (DateTime.Now - taskRunStart).TotalMilliseconds;
                                System.Diagnostics.Debug.WriteLine($"[{taskRunElapsed:F0}ms] GenerateWaveformAsync: Read {errorOutput.Length} characters from stderr");
                            }
                            catch (Exception ex2)
                            {
                                taskRunElapsed = (DateTime.Now - taskRunStart).TotalMilliseconds;
                                System.Diagnostics.Debug.WriteLine($"[{taskRunElapsed:F0}ms] GenerateWaveformAsync: Error reading stderr: {ex2.Message}, Type: {ex2.GetType().FullName}");
                            }
                            
                            if (process.ExitCode != 0)
                            {
                                taskRunElapsed = (DateTime.Now - taskRunStart).TotalMilliseconds;
                                System.Diagnostics.Debug.WriteLine($"[{taskRunElapsed:F0}ms] GenerateWaveformAsync: FFmpeg exited with code {process.ExitCode}");
                                System.Diagnostics.Debug.WriteLine($"[{taskRunElapsed:F0}ms] GenerateWaveformAsync: FFmpeg error output (first 500 chars): {errorOutput.Substring(0, Math.Min(500, errorOutput.Length))}");
                                throw new Exception($"FFmpeg process failed with exit code {process.ExitCode}: {errorOutput.Substring(0, Math.Min(500, errorOutput.Length))}");
                            }
                            else
                            {
                                taskRunElapsed = (DateTime.Now - taskRunStart).TotalMilliseconds;
                                System.Diagnostics.Debug.WriteLine($"[{taskRunElapsed:F0}ms] GenerateWaveformAsync: FFmpeg completed successfully");
                                System.Diagnostics.Debug.WriteLine($"[{taskRunElapsed:F0}ms] GenerateWaveformAsync: Error output length: {errorOutput.Length}");
                            }
                        }
                        catch (Exception ex)
                        {
                            taskRunElapsed = (DateTime.Now - taskRunStart).TotalMilliseconds;
                            System.Diagnostics.Debug.WriteLine($"[{taskRunElapsed:F0}ms] GenerateWaveformAsync: Exception in Task.Run: {ex.Message}");
                            System.Diagnostics.Debug.WriteLine($"[{taskRunElapsed:F0}ms] GenerateWaveformAsync: Exception type: {ex.GetType().FullName}");
                            System.Diagnostics.Debug.WriteLine($"[{taskRunElapsed:F0}ms] GenerateWaveformAsync: StackTrace: {ex.StackTrace}");
                            if (ex.InnerException != null)
                            {
                                System.Diagnostics.Debug.WriteLine($"[{taskRunElapsed:F0}ms] GenerateWaveformAsync: InnerException: {ex.InnerException.Message}, Type: {ex.InnerException.GetType().FullName}");
                            }
                            throw;
                        }
                        finally
                        {
                            taskRunElapsed = (DateTime.Now - taskRunStart).TotalMilliseconds;
                            System.Diagnostics.Debug.WriteLine($"[{taskRunElapsed:F0}ms] GenerateWaveformAsync: Disposing process");
                            try
                            {
                                process?.Dispose();
                            }
                            catch (Exception exDispose)
                            {
                                System.Diagnostics.Debug.WriteLine($"[{taskRunElapsed:F0}ms] GenerateWaveformAsync: Error disposing process: {exDispose.Message}");
                            }
                            taskRunElapsed = (DateTime.Now - taskRunStart).TotalMilliseconds;
                            System.Diagnostics.Debug.WriteLine($"[{taskRunElapsed:F0}ms] GenerateWaveformAsync: Task.Run completed");
                        }
                    });
                    elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                    System.Diagnostics.Debug.WriteLine($"[{elapsed:F0}ms] GenerateWaveformAsync: Task.Run finished, continuing...");
                    
                    elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                    System.Diagnostics.Debug.WriteLine($"[{elapsed:F0}ms] GenerateWaveformAsync: FFmpeg command completed, checking PCM file...");
                    
                    // 檢查 PCM 文件是否存在且有內容
                    elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                    System.Diagnostics.Debug.WriteLine($"[{elapsed:F0}ms] GenerateWaveformAsync: Getting PCM file properties...");
                    var pcmFileProps = await tempPcmFile.GetBasicPropertiesAsync();
                    elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                    System.Diagnostics.Debug.WriteLine($"[{elapsed:F0}ms] GenerateWaveformAsync: PCM file size: {pcmFileProps.Size} bytes");
                    
                    if (pcmFileProps.Size == 0)
                    {
                        elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                        System.Diagnostics.Debug.WriteLine($"[{elapsed:F0}ms] GenerateWaveformAsync: WARNING - PCM file is empty!");
                        return new float[sampleCount];
                    }
                    
                    // 讀取 PCM 數據
                    elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                    System.Diagnostics.Debug.WriteLine($"[{elapsed:F0}ms] GenerateWaveformAsync: Opening PCM file stream...");
                    byte[] pcmBytes;
                    using (var stream = await tempPcmFile.OpenStreamForReadAsync())
                    {
                        elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                        System.Diagnostics.Debug.WriteLine($"[{elapsed:F0}ms] GenerateWaveformAsync: Stream opened, length={stream.Length}");
                        using (var memoryStream = new MemoryStream())
                        {
                            elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                            System.Diagnostics.Debug.WriteLine($"[{elapsed:F0}ms] GenerateWaveformAsync: Copying stream to memory...");
                            await stream.CopyToAsync(memoryStream);
                            pcmBytes = memoryStream.ToArray();
                            elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                            System.Diagnostics.Debug.WriteLine($"[{elapsed:F0}ms] GenerateWaveformAsync: Copied {pcmBytes.Length} bytes to memory");
                        }
                    }
                    var sampleCountActual = pcmBytes.Length / 2; // 每個樣本 2 字節（16位）
                    
                    elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                    System.Diagnostics.Debug.WriteLine($"[{elapsed:F0}ms] GenerateWaveformAsync: Read {sampleCountActual} PCM samples");
                    
                    // 將字節轉換為樣本值
                    System.Diagnostics.Debug.WriteLine($"GenerateWaveformAsync: Converting bytes to samples...");
                    var samples = new List<short>();
                    for (int i = 0; i < pcmBytes.Length - 1; i += 2)
                    {
                        short sample = BitConverter.ToInt16(pcmBytes, i);
                        samples.Add(sample);
                    }
                    System.Diagnostics.Debug.WriteLine($"GenerateWaveformAsync: Converted to {samples.Count} samples");
                    
                    // 計算波形數據（降採樣到 sampleCount 個點）
                    System.Diagnostics.Debug.WriteLine($"GenerateWaveformAsync: Calculating waveform data...");
                    var waveform = new float[sampleCount];
                    int samplesPerPoint = samples.Count / sampleCount;
                    if (samplesPerPoint < 1) samplesPerPoint = 1;
                    System.Diagnostics.Debug.WriteLine($"GenerateWaveformAsync: samplesPerPoint={samplesPerPoint}");
                    
                    for (int i = 0; i < sampleCount; i++)
                    {
                        int startIdx = i * samplesPerPoint;
                        int endIdx = Math.Min(startIdx + samplesPerPoint, samples.Count);
                        
                        if (startIdx >= samples.Count) break;
                        
                        // 計算這個區間的最大絕對值（峰值）
                        float maxPeak = 0;
                        for (int j = startIdx; j < endIdx; j++)
                        {
                            // 將 16位整數轉換為 -1.0 到 1.0 的浮點數
                            float normalizedValue = samples[j] / 32768.0f;
                            float absValue = Math.Abs(normalizedValue);
                            if (absValue > maxPeak) maxPeak = absValue;
                        }
                        
                        waveform[i] = maxPeak;
                    }
                    
                    elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                    System.Diagnostics.Debug.WriteLine($"[{elapsed:F0}ms] GenerateWaveformAsync: Calculated {waveform.Length} waveform points");
                    
                    // 正規化到 0-1 範圍
                    elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                    System.Diagnostics.Debug.WriteLine($"[{elapsed:F0}ms] GenerateWaveformAsync: Normalizing waveform...");
                    float maxValue = 0;
                    float minValue = float.MaxValue;
                    for (int i = 0; i < waveform.Length; i++)
                    {
                        if (waveform[i] > maxValue) maxValue = waveform[i];
                        if (waveform[i] < minValue) minValue = waveform[i];
                    }
                    elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                    System.Diagnostics.Debug.WriteLine($"[{elapsed:F0}ms] GenerateWaveformAsync: Before normalization - Max: {maxValue}, Min: {minValue}");
                    
                    if (maxValue > 0)
                    {
                        for (int i = 0; i < waveform.Length; i++)
                        {
                            waveform[i] /= maxValue;
                        }
                        elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                        System.Diagnostics.Debug.WriteLine($"[{elapsed:F0}ms] GenerateWaveformAsync: Normalized waveform");
                    }
                    else
                    {
                        elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                        System.Diagnostics.Debug.WriteLine($"[{elapsed:F0}ms] GenerateWaveformAsync: WARNING - maxValue is 0, cannot normalize");
                    }
                    
                    // 重新計算最大值和最小值用於調試
                    maxValue = 0;
                    minValue = float.MaxValue;
                    for (int i = 0; i < waveform.Length; i++)
                    {
                        if (waveform[i] > maxValue) maxValue = waveform[i];
                        if (waveform[i] < minValue) minValue = waveform[i];
                    }
                    
                    elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                    System.Diagnostics.Debug.WriteLine($"[{elapsed:F0}ms] GenerateWaveformAsync: Generated waveform with {waveform.Length} points");
                    System.Diagnostics.Debug.WriteLine($"[{elapsed:F0}ms] GenerateWaveformAsync: Final Max value: {maxValue}, Min value: {minValue}");
                    System.Diagnostics.Debug.WriteLine($"[{elapsed:F0}ms] GenerateWaveformAsync: First 10 values: [{string.Join(", ", waveform.Take(10).Select(v => v.ToString("F3")))}]");
                    
                    elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                    System.Diagnostics.Debug.WriteLine($"[{elapsed:F0}ms] GenerateWaveformAsync: Returning waveform array, length={waveform.Length}");
                    return waveform;
                }
                finally
                {
                    // 清理臨時文件
                    try
                    {
                        await tempPcmFile.DeleteAsync();
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GenerateWaveformAsync: Error - {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"GenerateWaveformAsync: Error type - {ex.GetType().FullName}");
                System.Diagnostics.Debug.WriteLine($"GenerateWaveformAsync: StackTrace - {ex.StackTrace}");
                
                if (ex.InnerException != null)
                {
                    System.Diagnostics.Debug.WriteLine($"GenerateWaveformAsync: InnerException - {ex.InnerException.Message}");
                }
                
                // 如果讀取失敗，返回空數組
                return new float[sampleCount];
            }
        }

        // Frame rate estimation (defaulting to 30 if unknown)
        public static double EstimateFrameRate(TimeSpan duration, ulong frameCount)
        {
            if (duration.TotalSeconds == 0) return 30.0;
            return frameCount / duration.TotalSeconds;
        }
    }
}
