using System;
using System.Threading.Tasks;
using Amazon;
using StackExchange.Redis;

namespace HelloWorld
{
    class Program
    {
        // --- Configuration ---
        private const string AssumeRoleArn = "arn:aws:iam::123456789012:role/elastiCache-connect-role"; // Replace with your IAM role ARN
        private const string ElastiCacheUserId = "iam-test-user-01"; // Replace with your ElastiCache IAM-enabled user ID
        private const string ElastiCacheHost = "your-redis-cluster.xyz.cache.amazonaws.com"; // Replace with your cluster endpoint
        private const int ElastiCachePort = 6379; // Replace with your port if different
        private static readonly RegionEndpoint Region = RegionEndpoint.USWest2; // Replace with your AWS region

        static async Task Main(string[] args)
        {
            // Use the custom provider to get the configuration options
            var optionsProvider = new ElasticacheIamTokenProvider(
                AssumeRoleArn,
                ElastiCacheUserId,
                ElastiCacheHost,
                ElastiCachePort,
                Region);

            var config = await optionsProvider.GetConfigurationOptionsAsync();
            using var connection = await ConnectionMultiplexer.ConnectAsync(config);

            Console.WriteLine("Connected to ElastiCache via IAM.");

            var db = connection.GetDatabase();

            try
            {
                await db.StringSetAsync("iam-test-key", "iam-test-value");
                Console.WriteLine($"Successfully set key 'iam-test-key'");

                var value = await db.StringGetAsync("iam-test-key");
                Console.WriteLine($"Retrieved key 'iam-test-key' with value '{value}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error interacting with Redis: {ex.Message}");
            }
        }
    }
}
