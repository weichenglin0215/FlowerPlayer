using CommunityToolkit.Mvvm.ComponentModel;
using FlowerPlayer.Services;

namespace FlowerPlayer.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        [ObservableProperty]
        private bool _autoPlayOnOpen;

        [ObservableProperty]
        private bool _resumeLastFile;

        [ObservableProperty]
        private double _smartSkipPlayDuration;

        [ObservableProperty]
        private double _smartSkipSkipDuration;

        [ObservableProperty]
        private bool _autoPlayNext;

        public SettingsViewModel()
        {
            // Load initial values
            _autoPlayOnOpen = LocalSettingsService.AutoPlayOnOpen;
            _resumeLastFile = LocalSettingsService.ResumeLastFile;
            _smartSkipPlayDuration = LocalSettingsService.SmartSkipPlayDuration;
            _smartSkipSkipDuration = LocalSettingsService.SmartSkipSkipDuration;
            _autoPlayNext = LocalSettingsService.AutoPlayNext;
        }

        partial void OnAutoPlayOnOpenChanged(bool value) => LocalSettingsService.AutoPlayOnOpen = value;
        partial void OnResumeLastFileChanged(bool value) => LocalSettingsService.ResumeLastFile = value;
        partial void OnSmartSkipPlayDurationChanged(double value) => LocalSettingsService.SmartSkipPlayDuration = value;
        partial void OnSmartSkipSkipDurationChanged(double value) => LocalSettingsService.SmartSkipSkipDuration = value;
        partial void OnAutoPlayNextChanged(bool value) => LocalSettingsService.AutoPlayNext = value;
    }
}
