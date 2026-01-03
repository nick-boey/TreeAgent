namespace TreeAgent.Web.Features.Roadmap;

/// <summary>
/// Exception thrown when ROADMAP.json validation fails.
/// </summary>
public class RoadmapValidationException : Exception
{
    public RoadmapValidationException(string message) : base(message) { }
    public RoadmapValidationException(string message, Exception inner) : base(message, inner) { }
}