using UnityEngine;

/// <summary>
/// Look-alike models under one set piece (the FallenRunner's six DeadBody LITE corpses): all are children, one
/// is active. MazeGenerator.BuildSetPieces calls Apply with a hash of the cell - the same no-rng-draw trick as
/// DecalVariants and InteractableKit.bottleVariants, so the +8 set-piece stream is untouched.
/// Written by KEEP IT LIT &gt; Build &gt; Floor Themes (CorpseUpgrade); never edited by hand.
/// F104 B9: it also changes at runtime now - WorldShiftDirector calls ApplyNext on an unobserved placement to swap
/// the active model for the next look-alike.
/// </summary>
public class ModelVariants : MonoBehaviour
{
    [SerializeField] private GameObject[] options;

    public int Count => options != null ? options.Length : 0;

    /// <summary>F104 B9: index of the currently active option, or -1 if none is.</summary>
    public int ActiveIndex
    {
        get
        {
            for (int i = 0; i < Count; i++)
            {
                if (options[i] != null && options[i].activeSelf) return i;
            }

            return -1;
        }
    }

    /// <summary>Activates option (hash mod Count) and deactivates the rest.</summary>
    public void Apply(int hash)
    {
        if (Count == 0) return;

        Activate((hash & int.MaxValue) % options.Length);
    }

    /// <summary>F104 B9: activates the option after the active one, wrapping around. Does nothing with fewer than two options.</summary>
    public void ApplyNext()
    {
        if (Count < 2) return;

        Activate((ActiveIndex + 1) % options.Length);
    }

    private void Activate(int index)
    {
        for (int i = 0; i < options.Length; i++)
        {
            if (options[i] != null) options[i].SetActive(i == index);
        }
    }
}
