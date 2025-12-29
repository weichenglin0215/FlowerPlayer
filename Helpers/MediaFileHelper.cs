using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Storage;

namespace FlowerPlayer.Helpers
{
    public static class MediaFileHelper
    {
        // 支援的影片檔案格式
        public static readonly string[] VideoExtensions = new[]
        {
            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v",
            ".3gp", ".3g2", ".asf", ".dv", ".m2ts", ".mts", ".ts", ".vob",
            ".mpg", ".mpeg"
        };

        // 支援的音訊檔案格式
        public static readonly string[] AudioExtensions = new[]
        {
            ".mp3", ".wav", ".wma", ".aac", ".m4a", ".flac", ".ogg", ".opus",
            ".ac3", ".amr", ".au", ".ra", ".rm", ".mp2", ".mpa", ".ape"
        };

        // 所有支援的媒體檔案格式
        public static readonly string[] AllMediaExtensions = VideoExtensions.Concat(AudioExtensions).ToArray();

        /// <summary>
        /// 檢查檔案是否為支援的媒體檔案
        /// </summary>
        public static bool IsMediaFile(StorageFile file)
        {
            if (file == null) return false;
            var extension = file.FileType.ToLower();
            return AllMediaExtensions.Contains(extension);
        }

        /// <summary>
        /// 檢查檔案是否為影片檔案
        /// </summary>
        public static bool IsVideoFile(StorageFile file)
        {
            if (file == null) return false;
            var extension = file.FileType.ToLower();
            return VideoExtensions.Contains(extension);
        }

        /// <summary>
        /// 檢查檔案是否為音訊檔案
        /// </summary>
        public static bool IsAudioFile(StorageFile file)
        {
            if (file == null) return false;
            var extension = file.FileType.ToLower();
            return AudioExtensions.Contains(extension);
        }

        /// <summary>
        /// 獲取支援的媒體檔案格式描述
        /// </summary>
        public static string GetSupportedFormatsDescription()
        {
            return $"支援的媒體檔案格式：\n影片：{string.Join(", ", VideoExtensions)}\n音訊：{string.Join(", ", AudioExtensions)}";
        }
    }
}

