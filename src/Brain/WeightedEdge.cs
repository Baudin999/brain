using System.Globalization;

namespace Brain;

/// <summary>An edge of a node with its weight today.</summary>
internal sealed record WeightedEdge(PlacedNode From, Edge Edge, double Weight)
{
    private const string WeightFormat = "0.00";

    public string WeightText => Weight.ToString(WeightFormat, CultureInfo.InvariantCulture);
}
