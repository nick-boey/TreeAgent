namespace Homespun.Features.OpenCode.Data.Models;

public class ModelCost
{
    public decimal Input { get; set; }
    public decimal Output { get; set; }
    public ModelCacheCost? Cache { get; set; }
}

public class ModelCacheCost
{
    public decimal Read { get; set; }
    public decimal Write { get; set; }
}
