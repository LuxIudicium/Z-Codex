using System.Collections.ObjectModel;
using System.IO;

namespace ZCodex.App.ViewModels;

public class FolderTreeItemViewModel : ViewModelBase
{
    private bool _isExpanded;
    private bool _isSelected;
    private bool _childrenLoaded;

    public string Path { get; }
    public string DisplayName { get; }
    public ObservableCollection<FolderTreeItemViewModel> Children { get; } = new();

    // Placeholder child to show the expand arrow before lazy-loading real children
    private FolderTreeItemViewModel(bool isPlaceholder)
    {
        Path = "";
        DisplayName = "";
        _childrenLoaded = true;
    }

    /// <param name="displayName">Libellé à afficher à la place du nom de dossier. Sert au nœud
    /// GWRank, qui doit pouvoir annoncer que la vue date d'avant la panne en cours. Le
    /// <see cref="Path"/>, lui, reste le vrai chemin.</param>
    public FolderTreeItemViewModel(string path, string? displayName = null)
    {
        Path = path;
        DisplayName = displayName is { Length: > 0 } d ? d
                    : System.IO.Path.GetFileName(path) is { Length: > 0 } n ? n : path;
        if (HasSubDirectories())
            Children.Add(new FolderTreeItemViewModel(isPlaceholder: true));
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetField(ref _isExpanded, value) && value && !_childrenLoaded)
            {
                _childrenLoaded = true;
                Children.Clear();
                try
                {
                    foreach (var dir in Directory.EnumerateDirectories(Path).OrderBy(d => d))
                        Children.Add(new FolderTreeItemViewModel(dir));
                }
                catch { }
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    private bool HasSubDirectories()
    {
        try { return Directory.EnumerateDirectories(Path).Any(); }
        catch { return false; }
    }
}
