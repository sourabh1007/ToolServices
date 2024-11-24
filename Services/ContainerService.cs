using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager.ContainerInstance;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager;
using Azure;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Azure.ResourceManager.ContainerInstance.Models;
using AppServiceSample.Models;

namespace AppServiceSample
{
    public class ContainerService
    {
        private readonly ILogger<ContainerService> _logger;
        private readonly SubscriptionResource _subscription;

        private readonly string _subscriptionId;
        private readonly string _resourceGroupName;
        private readonly AzureLocation _location;

        // Thread-safe dictionary to track container allocations
        private readonly ConcurrentDictionary<string, string> _userContainerMap = new();

        public ContainerService(ILogger<ContainerService> logger)
        {
            _logger = logger;

            _subscriptionId = Environment.GetEnvironmentVariable("ACI_SUBSCRIPTION_ID") ?? "dummy-sub-id";
            _resourceGroupName = Environment.GetEnvironmentVariable("ACI_RESOURCE_GROUP") ?? "dummy-resource-group";
            _location = Environment.GetEnvironmentVariable("ACI_REGION") ?? AzureLocation.EastUS;

            TokenCredential cred = new DefaultAzureCredential();
            ArmClient client = new ArmClient(cred);

            _subscription = client
                .GetSubscriptionResource(SubscriptionResource.CreateResourceIdentifier(_subscriptionId));
        }

        public async Task<ContainerAllocationResponse> AllocateContainerAsync(string userId)
        {
            // Check if a container is already assigned to the user
            if (_userContainerMap.TryGetValue(userId, out var containerId))
            {
                _logger.LogInformation($"User {userId} already has a container: {containerId}");
                return await GetContainerDetailsAsync(containerId);
            }

            // No existing container; create a new one
            string containerName = $"user-container-{Guid.NewGuid()}";
            _logger.LogInformation($"Creating a new container for user {userId}: {containerName}");

            var containerDetails = await CreateNewContainerAsync(containerName);
            _userContainerMap[userId] = containerName;

            return containerDetails;
        }

        public async Task ReleaseContainerAsync(string containerId)
        {
            ResourceGroupResource resourceGroup = await _subscription.GetResourceGroupAsync(_resourceGroupName);
            ContainerGroupCollection containerGroupCollection = resourceGroup.GetContainerGroups();

            // Delete the container from ACI
            _logger.LogInformation($"Releasing container: {containerId}");
            await containerGroupCollection.Get(containerId).Value.DeleteAsync(WaitUntil.Completed);

            // Remove from the tracking dictionary
            _userContainerMap.Values
                .Where(v => v == containerId)
                .ToList()
                .ForEach(k => _userContainerMap.TryRemove(k, out _));
        }

        private async Task<ContainerAllocationResponse> CreateNewContainerAsync(string containerName)
        {
            ResourceGroupResource resourceGroup = await _subscription.GetResourceGroupAsync(_resourceGroupName);

            ContainerResourceRequestsContent requests = new ContainerResourceRequestsContent(memoryInGB: 1.5, cpu: 1);
            ContainerResourceRequirements resources = new ContainerResourceRequirements(requests);
            ContainerInstanceContainer container = new ContainerInstanceContainer(
                containerName, 
                "mcr.microsoft.com/azuredocs/aci-helloworld",
                resources)
            {
                Ports = { new ContainerPort(80) }
            };

            List<ContainerInstanceContainer> containers = new List<ContainerInstanceContainer> { container };
            ContainerGroupData groupData = new ContainerGroupData(
                _location, 
                containers, 
                ContainerInstanceOperatingSystemType.Linux);
            ContainerGroupPort gPort = new ContainerGroupPort(80);
            List<ContainerGroupPort> listOfContainerGroupPort = new List<ContainerGroupPort>();
            listOfContainerGroupPort.Add(gPort);
            groupData.IPAddress = new ContainerGroupIPAddress(listOfContainerGroupPort, ContainerGroupIPAddressType.Public);

            ContainerGroupCollection containerGroupCollection = resourceGroup.GetContainerGroups();
            ArmOperation<ContainerGroupResource> armOperation = await containerGroupCollection
                .CreateOrUpdateAsync(WaitUntil.Completed, containerName, groupData);
            ContainerGroupResource containerGroup = armOperation.Value;
            string? ipAddress = containerGroup.Data?.IPAddress?.IP?.ToString();

            return new ContainerAllocationResponse
            {
                ContainerId = containerName,
                WebSocketUrl = string.IsNullOrEmpty(ipAddress) ? "Container IP Address not available" : $"ws://{ipAddress}:8080"
            };
        }

        private async Task<ContainerAllocationResponse> GetContainerDetailsAsync(string containerId)
        {
            ResourceGroupResource resourceGroup = await _subscription.GetResourceGroupAsync(_resourceGroupName);
            ContainerGroupCollection containerGroupCollection = resourceGroup.GetContainerGroups();

            Response<ContainerGroupResource> containerGroup = await containerGroupCollection.GetAsync(containerId);

            string ipAddress = containerGroup.Value.Data.IPAddress.IP.ToString();
            return new ContainerAllocationResponse
            {
                ContainerId = containerId,
                WebSocketUrl = $"ws://{ipAddress}:8080"
            };
        }
    }
}
