using System.Collections.ObjectModel;
using AkademiTrack.Services;

namespace AkademiTrack.ViewModels
{
    public class ChangelogWindowViewModel
    {
        public string Title { get; set; }
        public string VersionInfo { get; set; }
        public string? HeaderImage { get; set; }
        public string? Description { get; set; }
        public bool HasHeaderImage => !string.IsNullOrEmpty(HeaderImage);
        public bool HasDescription => !string.IsNullOrEmpty(Description);
        public ObservableCollection<ChangeCategory> Changes { get; set; }

        public ChangelogWindowViewModel(ChangelogData data)
        {
            Title = data.Title;
            VersionInfo = $"Version {data.Version} - Released {data.ReleaseDate}";
            HeaderImage = data.HeaderImage;
            Description = data.Description;
            Changes = new ObservableCollection<ChangeCategory>(data.Changes);
        }
    }
}
