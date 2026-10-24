using UnityEngine;

/// <summary>
/// F105 C6 layer 1: where on this floor the player has been heard or seen, one value per maze cell. AIFollower raises it on
/// those events and a share of its wander legs head for the warmest cell, so a noisy corner keeps being revisited. Each cell
/// stores its heat (0..1) and the time of the reading; it cools linearly, so only a cell's own value is touched on a read.
/// A plain class with arrays sized once in <see cref="Configure"/> - nothing allocates after that. A floor builds a fresh one.
/// </summary>
public class HunterHeatMap
{
    private float[] _heat = new float[0];
    private float[] _at = new float[0];
    private int _width;
    private int _height;
    private float _decaySeconds = 1f;

    /// <summary>Sizes the grid (width x height cells) and sets how long a full heat takes to cool to zero. Clears everything.</summary>
    public void Configure(int width, int height, float decaySeconds)
    {
        _width = Mathf.Max(0, width);
        _height = Mathf.Max(0, height);
        _decaySeconds = Mathf.Max(0.01f, decaySeconds);
        _heat = new float[_width * _height];
        _at = new float[_width * _height];
    }

    /// <summary>The cell's heat now: its stored value cooled linearly by the time since it was last raised. 0 outside the grid.</summary>
    public float HeatAt(int x, int z, float now)
    {
        if (!InGrid(x, z)) return 0f;

        int i = x + z * _width;
        return Mathf.Max(0f, _heat[i] - (now - _at[i]) / _decaySeconds);
    }

    /// <summary>Raises one cell by <paramref name="amount"/> (capped at 1). Ignored outside the grid.</summary>
    public void Add(int x, int z, float amount, float now)
    {
        if (!InGrid(x, z)) return;

        int i = x + z * _width;
        _heat[i] = Mathf.Min(1f, HeatAt(x, z, now) + amount);
        _at[i] = now;
    }

    /// <summary>Raises a cell by <paramref name="center"/> and its four neighbours by <paramref name="neighbour"/>.</summary>
    public void AddWithNeighbours(int x, int z, float center, float neighbour, float now)
    {
        Add(x, z, center, now);
        Add(x - 1, z, neighbour, now);
        Add(x + 1, z, neighbour, now);
        Add(x, z - 1, neighbour, now);
        Add(x, z + 1, neighbour, now);
    }

    private bool InGrid(int x, int z) => x >= 0 && z >= 0 && x < _width && z < _height;
}
