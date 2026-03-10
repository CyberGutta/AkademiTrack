using System;
using System.Collections.ObjectModel;
using AkademiTrack.Services;
using System.Diagnostics;
using Avalonia.Media.Imaging;

namespace AkademiTrack.ViewModels
{
    public class ChangelogWindowViewModel
    {
        public string Title { get; set; }
        public string VersionInfo { get; set; }
        public string? HeaderImage { get; set; }
        public Bitmap? HeaderImageBitmap { get; set; }
        public string? Description { get; set; }
        public bool HasHeaderImage => HeaderImageBitmap != null;
        public bool HasDescription => !string.IsNullOrEmpty(Description);
        public ObservableCollection<ChangeCategory> Changes { get; set; }

        public ChangelogWindowViewModel(ChangelogData data)
        {
            Title = data.Title;
            VersionInfo = $"Version {data.Version} - Released {data.ReleaseDate}";
            HeaderImage = data.HeaderImage;
            HeaderImageBitmap = data.HeaderImageBitmap;
            Description = data.Description;
            Changes = new ObservableCollection<ChangeCategory>(data.Changes);
            
            Console.WriteLine($"[ChangelogViewModel] HeaderImage: {HeaderImage}");
            Console.WriteLine($"[ChangelogViewModel] HeaderImageBitmap: {(HeaderImageBitmap != null ? "Loaded" : "Null")}");
            Console.WriteLine($"[ChangelogViewModel] HasHeaderImage: {HasHeaderImage}");
        }
    }
}
