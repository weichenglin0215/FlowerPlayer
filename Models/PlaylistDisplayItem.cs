namespace FlowerPlayer.Models
{
    public class PlaylistDisplayItem
    {
        public string FileName { get; set; } = string.Empty;
        public string FileSize { get; set; } = string.Empty;
        public string Duration { get; set; } = string.Empty;
        public string ModifiedDate { get; set; } = string.Empty;
        public string Directory { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;// 用於雙擊開啟
    }
}
