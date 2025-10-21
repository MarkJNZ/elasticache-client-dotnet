using System;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.Runtime;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using StackExchange.Redis;
using StackExchange.Redis.Configuration;

public class ElasticacheIamTokenProvider
{
    private readonly string _roleArn;
    private readonly string _userId;
    private readonly string _cacheEndpoint;
    private readonly int _port;
    private readonly RegionEndpoint _region;
    private readonly bool _isServerless;

    private readonly AWSCredentials _defaultCredentials;
    private AssumeRoleResponse? _assumedRoleResponse;
    private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);

    public ElasticacheIamTokenProvider(
        string roleArn,
        string userId,
        string cacheEndpoint,
        int port,
        RegionEndpoint region,
        bool isServerless = false)
    {
        _roleArn = roleArn;
        _userId = userId;
        _cacheEndpoint = cacheEndpoint;
        _port = port;
        _region = region;
        _isServerless = isServerless;
        #pragma warning disable CS0618 // FallbackCredentialsFactory is obsolete; keep for compatibility
        _defaultCredentials = FallbackCredentialsFactory.GetCredentials();
        #pragma warning restore CS0618
    }

    public async Task<ConfigurationOptions> GetConfigurationOptionsAsync()
    {
        // Check if a new token is needed
        if (_assumedRoleResponse == null || IsTokenExpired())
        {
            await RefreshTokenAsync();
        }

        var config = new ConfigurationOptions
        {
            EndPoints = { { _cacheEndpoint, _port } },
            Ssl = true,
            User = _userId,
            Password = GetSignedTokenUrl(),
            AbortOnConnectFail = false,
        };

        // Note: attaching a defaults provider for StackExchange.Redis is optional here.
        // If you need automatic refresh hooks, implement the appropriate provider from
        // the StackExchange.Redis API and set it here. For now we just return the
        // ConfigurationOptions with the signed password.
        return config;
    }

    private bool IsTokenExpired()
    {
        // Token is valid for 15 minutes. Refresh before it expires.
        var expiryTime = _assumedRoleResponse?.Credentials?.Expiration;
        if (!expiryTime.HasValue) return true;
        return expiryTime.Value.ToUniversalTime() < DateTime.UtcNow.AddMinutes(5);
    }

    private async Task RefreshTokenAsync()
    {
        await _refreshLock.WaitAsync();
        try
        {
            if (_assumedRoleResponse != null && !IsTokenExpired()) return;

            Console.WriteLine("Refreshing ElastiCache IAM token...");

            var stsClient = new AmazonSecurityTokenServiceClient(_defaultCredentials, _region);
            var assumeRoleRequest = new AssumeRoleRequest
            {
                RoleArn = _roleArn,
                RoleSessionName = "ElastiCacheConnectSession",
                DurationSeconds = 900 // Token valid for 15 minutes
            };
            _assumedRoleResponse = await stsClient.AssumeRoleAsync(assumeRoleRequest);
        }
        finally
        {
            _refreshLock.Release();
        }
    }
    
    private string GetSignedTokenUrl()
    {
        // NOTE: Proper SigV4 query-string signing is required here to generate a
        // valid ElastiCache IAM auth token. The AWS SDK's internal signer types are
        // not public in all packages, so implement a SigV4 signer or use a helper
        // library. For now this method returns a simple placeholder token so the
        // project compiles. Replace this with a real signing implementation.
        var credentials = _assumedRoleResponse?.Credentials;
        if (credentials == null)
            throw new InvalidOperationException("Credentials are not available. Call RefreshTokenAsync first.");

        // Build the token as a minimal placeholder. DO NOT use this in production.
        var token = string.Format(CultureInfo.InvariantCulture, "AKID={0};ST={1}",
            credentials.AccessKeyId, credentials.SessionToken ?? string.Empty);
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(token));
    }
    
    private string GetTokenUri()
    {
        var uri = $"https://{_cacheEndpoint}:{_port}/?Action=connect&User={_userId}";
        if (_isServerless)
        {
            uri += "&ResourceType=ServerlessCache";
        }
        return uri;
    }
}