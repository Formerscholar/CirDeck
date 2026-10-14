using System.ComponentModel;
using System.Windows.Media;
using CirDeck.Core;

namespace CirDeck.App;

public sealed class TileModel : INotifyPropertyChanged
{
    private ImageSource? _icon;
    private bool _selected;

    public TileModel(AppEntry entry)
    {
        Entry = entry;
    }

    public AppEntry Entry { get; }

    public string Name => Entry.Name;

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
