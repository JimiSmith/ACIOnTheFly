using Microsoft.Azure.ServiceBus;
using Newtonsoft.Json;
using System;
using System.Text;
using System.Threading.Tasks;

namespace ACIOnTheFly
{
    class Program
    {
        private static QueueClient _outQueueClient;

        static async Task Main(string[] args)
        {
            var connectionString = Environment.GetEnvironmentVariable("ServiceBusConnectionString");
            var outQueueName = Environment.GetEnvironmentVariable("OutQueueName");
            var containerId = Environment.GetEnvironmentVariable("ContainerId");
            Console.WriteLine($"connectionString: {connectionString}");
            Console.WriteLine($"outQueueName: {outQueueName}");
            Console.WriteLine($"containerId: {containerId}");
            _outQueueClient = new QueueClient(
                connectionString,
                outQueueName);
            var outObject = new
            {
                ContainerId = containerId,
                Result = Guid.NewGuid()
            };
            await _outQueueClient.SendAsync(new Message(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(outObject)))).ConfigureAwait(false);
            Console.WriteLine("Sent close message");
            await _outQueueClient.CloseAsync();
        }
    }
}
