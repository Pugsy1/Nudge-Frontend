using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Nudge.App.ViewModels;

/// <summary>
/// A keyboard the controller can type on, for the search box - the one place in Nudge that needs
/// characters rather than choices, and therefore the one place a pad alone cannot reach.
///
/// The selection is held here rather than expressed as WPF focus. Every key is an identical cell in
/// a fixed grid, so "which key am I on" is a row and column, and moving is arithmetic that always
/// behaves the same. Focus traversal across a wrapped grid of 30-odd identical buttons picks its own
/// path, which is fine for Tab and wrong for a D-pad.
/// </summary>
public sealed partial class OnScreenKeyboardViewModel : ObservableObject
{
    /// <summary>
    /// Rows are equal length so movement is plain arithmetic and every column lines up vertically.
    /// The last row is padded rather than ragged for the same reason - a short final row would make
    /// "down" from some columns land nowhere.
    /// </summary>
    private static readonly string[] Rows =
    [
        "1234567890",
        "QWERTYUIOP",
        "ASDFGHJKL-",
        "ZXCVBNM ,."
    ];

    public OnScreenKeyboardViewModel()
    {
        for (int row = 0; row < Rows.Length; row++)
        {
            for (int column = 0; column < Rows[row].Length; column++)
            {
                Keys.Add(new OnScreenKey(Rows[row][column].ToString(), row, column));
            }
        }

        UpdateSelection();
    }

    /// <summary>Flat list, since the view lays them out in a uniform grid - each key knows its own row and column.</summary>
    public ObservableCollection<OnScreenKey> Keys { get; } = [];

    public int Columns => Rows[0].Length;

    private int _row;
    private int _column;

    /// <summary>The text being typed. Bound straight to the library's search box, so results filter as keys are pressed.</summary>
    [ObservableProperty]
    private string _text = string.Empty;

    public void Move(int columns, int rows)
    {
        // Wraps rather than clamps: a keyboard is a small fixed grid the user can see all of at
        // once, so running off one edge and appearing at the other is quicker than reversing, and
        // never leaves a press doing nothing.
        _row = (_row + rows + Rows.Length) % Rows.Length;
        _column = (_column + columns + Columns) % Columns;
        UpdateSelection();
    }

    /// <summary>Types the selected key.</summary>
    public void TypeSelected()
    {
        OnScreenKey? key = Keys.FirstOrDefault(k => k.IsSelected);
        if (key is not null)
        {
            Text += key.Character;
        }
    }

    public void Backspace()
    {
        if (Text.Length > 0)
        {
            Text = Text[..^1];
        }
    }

    public void Clear() => Text = string.Empty;

    private void UpdateSelection()
    {
        foreach (OnScreenKey key in Keys)
        {
            key.IsSelected = key.Row == _row && key.Column == _column;
        }
    }
}

/// <summary>One key. Observable only for its selected state - the character never changes.</summary>
public sealed partial class OnScreenKey : ObservableObject
{
    public OnScreenKey(string character, int row, int column)
    {
        Character = character;
        Row = row;
        Column = column;
    }

    public string Character { get; }

    public int Row { get; }

    public int Column { get; }

    /// <summary>Shown as a space rather than blank, so the space bar is visibly a key.</summary>
    public string Label => Character == " " ? "space" : Character;

    [ObservableProperty]
    private bool _isSelected;
}
