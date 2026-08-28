using System.Collections;

namespace Nudge.Core.Models;

/// <summary>
/// One observation that fed a classification, e.g. ("PE header", "Machine type is AMD64").
/// </summary>
/// <param name="Source">Where the observation came from, in words a user can understand.</param>
/// <param name="Observation">What was observed.</param>
/// <param name="Weight">How much this observation moved the decision.</param>
public sealed record EvidenceItem(string Source, string Observation, EvidenceWeight Weight = EvidenceWeight.Supporting)
{
    public override string ToString() => $"{Source}: {Observation}";
}

public enum EvidenceWeight
{
    /// <summary>Interesting context, did not drive the decision.</summary>
    Informational = 0,

    /// <summary>Backs up the decision but would not be enough alone.</summary>
    Supporting,

    /// <summary>On its own, strong enough to decide.</summary>
    Decisive,

    /// <summary>Points away from the decision. Recorded so the user can see the doubt.</summary>
    Contradicting
}

/// <summary>
/// The ordered trail of reasons behind a detection result. Confidence is a first-class concept in
/// Nudge, so every classification carries the evidence that produced it and the UI can show it.
/// </summary>
public sealed class DetectionEvidence : IReadOnlyList<EvidenceItem>
{
    private readonly List<EvidenceItem> _items = [];

    public static DetectionEvidence Empty() => new();

    public DetectionEvidence Add(string source, string observation, EvidenceWeight weight = EvidenceWeight.Supporting)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(observation);
        _items.Add(new EvidenceItem(source, observation, weight));
        return this;
    }

    public DetectionEvidence AddRange(IEnumerable<EvidenceItem> items)
    {
        _items.AddRange(items);
        return this;
    }

    public bool HasDecisiveEvidence => _items.Any(i => i.Weight == EvidenceWeight.Decisive);

    public bool HasContradictions => _items.Any(i => i.Weight == EvidenceWeight.Contradicting);

    public string Summary => _items.Count == 0
        ? "No evidence recorded."
        : string.Join(Environment.NewLine, _items.Select(i => "- " + i));

    public int Count => _items.Count;

    public EvidenceItem this[int index] => _items[index];

    public IEnumerator<EvidenceItem> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
