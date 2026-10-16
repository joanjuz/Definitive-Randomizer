using System;
using System.ComponentModel;

namespace ThreeDS.Progressive.Models;

/// <summary>Opción de compatibilidad vinculada a un bit concreto del archivo Personal.</summary>
public sealed class FlagChoice : INotifyPropertyChanged
{
    private bool _checked;

    public FlagChoice(string label, int index, int group, bool enabled)
    {
        Label = label;
        Index = index;
        Group = group;
        _checked = enabled;
    }

    public string Label { get; }
    public int Index { get; }
    public int Group { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? Changed;

    public bool IsChecked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
            Changed?.Invoke();
        }
    }
}
