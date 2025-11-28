using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Storage;

namespace FlowerPlayer.Helpers
{
    public static class MediaFileHelper
    {
        // 支持的视频文件格式
        public static readonly string[] VideoExtensions = new[]
        {
            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v",
            ".3gp", ".3g2", ".asf", ".dv", ".m2ts", ".mts", ".ts", ".vob",
            ".mpg", ".mpeg"
        };

        // 支持的音频文件格式
        public static readonly string[] AudioExtensions = new[]
        {
            ".mp3", ".wav", ".wma", ".aac", ".m4a", ".flac", ".ogg", ".opus",
            ".ac3", ".amr", ".au", ".ra", ".rm", ".mp2", ".mpa", ".ape"
        };

        // 所有支持的媒体文件格式
        public static readonly string[] AllMediaExtensions = VideoExtensions.Concat(AudioExtensions).ToArray();

        /// <summary>
        /// 检查文件是否为支持的媒体文件
        /// </summary>
        public static bool IsMediaFile(StorageFile file)
        {
            if (file == null) return false;
            var extension = file.FileType.ToLower();
            return AllMediaExtensions.Contains(extension);
        }

        /// <summary>
        /// 检查文件是否为视频文件
        /// </summary>
        public static bool IsVideoFile(StorageFile file)
        {
            if (file == null) return false;
            var extension = file.FileType.ToLower();
            return VideoExtensions.Contains(extension);
        }

        /// <summary>
        /// 检查文件是否为音频文件
        /// </summary>
        public static bool IsAudioFile(StorageFile file)
        {
            if (file == null) return false;
            var extension = file.FileType.ToLower();
            return AudioExtensions.Contains(extension);
        }

        /// <summary>
        /// 获取支持的媒体文件格式描述
        /// </summary>
        public static string GetSupportedFormatsDescription()
        {
            return $"支持的媒体文件格式：\n视频：{string.Join(", ", VideoExtensions)}\n音频：{string.Join(", ", AudioExtensions)}";
        }
    }
}

