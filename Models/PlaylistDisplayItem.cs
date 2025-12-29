using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FlowerPlayer.Models
{
    public partial class PlaylistDisplayItem : ObservableObject
    {
        [ObservableProperty]
        private string _fileName = string.Empty;

        [ObservableProperty]
        private string _fileSize = string.Empty;

        [ObservableProperty]
        private string _duration = string.Empty;

        [ObservableProperty]
        private string _modifiedDate = string.Empty;

        [ObservableProperty]
        private string _directory = string.Empty;

        [ObservableProperty]
        private string _fullPath = string.Empty;// 用於雙擊開啟

        [ObservableProperty]
        private bool _isMissing = false; // 是否找不到檔案

        // 用於精確排序的原始資料
        [ObservableProperty]
        private long _rawSize;

        [ObservableProperty]
        private DateTime _rawModifiedDate;

        [ObservableProperty]
        private TimeSpan _rawDuration;
    }
}
