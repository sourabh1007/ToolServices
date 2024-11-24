using AppServiceSample.Models;
using Microsoft.AspNetCore.Mvc;

namespace AppServiceSample
{
    [ApiController]
    [Route("api/containers")]
    public class ContainerController : ControllerBase
    {
        private readonly ContainerService _containerService;

        public ContainerController(ContainerService containerService)
        {
            _containerService = containerService;
        }

        [HttpPost("allocate")]
        public async Task<IActionResult> AllocateContainer([FromBody] ContainerAllocationRequest request)
        {
            var result = await _containerService.AllocateContainerAsync(request.UserId);
            return Ok(result);
        }

        [HttpPost("release")]
        public async Task<IActionResult> ReleaseContainer([FromBody] ContainerReleaseRequest request)
        {
            await _containerService.ReleaseContainerAsync(request.ContainerId);
            return Ok("Container released successfully.");
        }
    }
}
