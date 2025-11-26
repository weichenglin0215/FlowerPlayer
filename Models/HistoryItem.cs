using Windows.Storage;
using System;

namespace FlowerPlayer.Models
{
    public class HistoryItem
    {
        public StorageFile File { get; set; }
        public string FileName { get; set; }
        public string FileSize { get; set; }
        public string ModifiedDate { get; set; }
        public string FilePath { get; set; }

        public HistoryItem(StorageFile file)
        {
            File = file;
            FileName = file.Name;
            FilePath = file.Path;
            FileSize = "Loading...";
            ModifiedDate = "";
        }

        // Constructor for test data without StorageFile
        public HistoryItem(string fileName, string fileSize, string modifiedDate, string filePath)
        {
            File = null;
            FileName = fileName;
            FileSize = fileSize;
            ModifiedDate = modifiedDate;
            FilePath = filePath;
        }

        public async System.Threading.Tasks.Task LoadPropertiesAsync()
        {
            try
            {
                if (File != null)
                {
                    var props = await File.GetBasicPropertiesAsync();
                    FileSize = FormatFileSize(props.Size);
                    ModifiedDate = props.DateModified.LocalDateTime.ToString("yyyy/MM/dd HH:mm:ss");
                }
            }
            catch
            {
                FileSize = "Unknown";
                ModifiedDate = "Unknown";
            }
        }

        private string FormatFileSize(ulong bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}

