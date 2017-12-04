using Microsoft.Azure.Management.ContainerInstance.Fluent.ContainerGroup.Definition;
using Microsoft.Azure.Management.ContainerRegistry.Fluent;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

namespace ACIOnTheFlyController
{
    public static class ACIExtensions
    {
        public static IWithPrivateImageRegistry WithRestartPolicy(this IWithPublicOrPrivateImageRegistry builder, string policy)
        {
            var impl = builder.GetType().GetProperty("Inner").GetValue(builder, null);
            var prop = impl.GetType().GetProperty("RestartPolicy");
            prop.SetValue(impl, policy);
            return builder;
        }
    }

    public static class StartContainers
    {
        [FunctionName("StartContainers")]
        public static async Task<HttpResponseMessage> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "StartContainers/{count}")]HttpRequestMessage req,
            int count)
        {
            var credentials = SdkContext.AzureCredentialsFactory.FromServicePrincipal(
                Environment.GetEnvironmentVariable("SP.ClientId"),
                Environment.GetEnvironmentVariable("SP.ClientSecret"),
                Environment.GetEnvironmentVariable("SP.TenantId"),
                AzureEnvironment.AzureGlobalCloud);
            var azure = Azure
                .Configure()
                .WithLogLevel(HttpLoggingDelegatingHandler.Level.Basic)
                .Authenticate(credentials)
                .WithSubscription(Environment.GetEnvironmentVariable("SubscriptionId"));
            const string containerImageName = "jimismith-on.azurecr.io/aci-on-the-fly:v1.7";
            var azureRegistry = azure.ContainerRegistries
                .GetByResourceGroup("common", "jimismith");
            const string resourceGroup = "aci-experiment";
            await EnsureResourceGroup(azure, resourceGroup);
            var containerIdBase = SdkContext.RandomResourceName("aci-test-v3", 20);
            await Task.WhenAll(
                Enumerable.Range(1, count)
                .Select(async c =>
                {
                    var containerId = $"{containerIdBase}-{c}";
                    var acrCredentials = await azureRegistry.GetCredentialsAsync();
                    var prop = azure.ContainerGroups.Manager.Inner.GetType().GetProperty("ApiVersion");
                    prop.SetValue(azure.ContainerGroups.Manager.Inner, "2017-10-01-preview");
                    await azure.ContainerGroups
                            .Define(containerId)
                            .WithRegion(Region.EuropeWest)
                            .WithExistingResourceGroup(resourceGroup)
                            .WithLinux()
                            .WithRestartPolicy("OnFailure")
                            .WithPrivateImageRegistry(azureRegistry.LoginServerUrl, acrCredentials.Username, acrCredentials.AccessKeys[AccessKeyType.Primary])
                            .WithoutVolume()
                            .DefineContainerInstance(containerId)
                                .WithImage(containerImageName)
                                .WithoutPorts()
                                .WithCpuCoreCount(1)
                                .WithMemorySizeInGB(1)
                                .WithEnvironmentVariables(new Dictionary<string, string>
                                {
                                    ["ServiceBusConnectionString"] = Environment.GetEnvironmentVariable("ServiceBusConnectionString"),
                                    ["OutQueueName"] = "output",
                                    ["ContainerId"] = containerId
                                })
                                .Attach()
                            .CreateAsync();
                })
            );
            
            return req.CreateResponse(HttpStatusCode.OK, $"Started {count} containers");
        }

        [FunctionName("ReceiveResult")]
        public static void ReceiveResult([ServiceBusTrigger("output", Connection = "ServiceBusConnectionString")] string message,
            TraceWriter log)
        {
            log.Info(message);

            var resp = JsonConvert.DeserializeObject<dynamic>(message);

            var credentials = SdkContext.AzureCredentialsFactory.FromServicePrincipal(
                Environment.GetEnvironmentVariable("SP.ClientId"),
                Environment.GetEnvironmentVariable("SP.ClientSecret"),
                Environment.GetEnvironmentVariable("SP.TenantId"),
                AzureEnvironment.AzureGlobalCloud);
            var azure = Azure
                .Configure()
                .WithLogLevel(HttpLoggingDelegatingHandler.Level.Basic)
                .Authenticate(credentials)
                .WithSubscription(Environment.GetEnvironmentVariable("SubscriptionId"));
            var container = azure.ContainerGroups.GetByResourceGroup("aci-experiment", (string)resp.ContainerId);
            if (container != null)
            {
                azure.ContainerGroups.DeleteById(container.Id);
            }
        }

        private static async Task EnsureResourceGroup(
            IAzure azure,
            string resourceGroup)
        {
            IResourceGroup group;
            if (await azure.ResourceGroups.ContainAsync(resourceGroup))
            {
                group = await azure.ResourceGroups.GetByNameAsync(resourceGroup);
            }
            else
            {
                group = await azure.ResourceGroups.Define(resourceGroup)
                    .WithRegion(Region.EuropeWest)
                    .CreateAsync();
            }
        }
    }
}
