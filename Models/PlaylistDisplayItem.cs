namespace FlowerPlayer.Models
{
    public class PlaylistDisplayItem
    {
        public string FileName { get; set; }
        public string Duration { get; set; }
        public string ModifiedDate { get; set; }
        public string Directory { get; set; }
        public string FullPath { get; set; } // 用於雙擊開啟
    }
}
