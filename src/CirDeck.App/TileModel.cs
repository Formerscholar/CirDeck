using System.ComponentModel;
using System.Windows.Media;
using CirDeck.Core;

namespace CirDeck.App;

public sealed class TileModel : INotifyPropertyChanged
{
    private ImageSource? _icon;
    private bool _selected;

    public TileModel(LaunchTile tile)
    {
        Tile = tile;
    }

    public LaunchTile Tile { get; }

    public AppEntry? Entry => Tile.App;

    public string Name => Tile.Name;

    public bool IsFolder => Tile.IsFolder;

    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
        }
    }

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
            {
                return;
            }

            _selected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
