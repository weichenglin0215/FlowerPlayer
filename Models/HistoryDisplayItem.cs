namespace FlowerPlayer.Models
{
    public class HistoryDisplayItem
    {
        public string FileName { get; set; }
        public string FileSize { get; set; }
        public string ModifiedDate { get; set; }
        public string FilePath { get; set; }
        public int Index { get; set; } // 用於找回原始的 HistoryItem
    }
}
