namespace AppServiceSample.Models
{
    public class ContainerAllocationResponse
    {
        public required string ContainerId { get; set; }
        public required string WebSocketUrl { get; set; }
    }
}
