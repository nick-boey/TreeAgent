namespace TreeAgent.Web.Features.Git;

public class WorktreeInfo
{
    public string Path { get; set; } = "";
    public string? Branch { get; set; }
    public string? HeadCommit { get; set; }
    public bool IsBare { get; set; }
    public bool IsDetached { get; set; }
}