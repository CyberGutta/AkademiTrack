using System;
using System.Net.Http;
using AkademiTrack.Services.Configuration;

namespace AkademiTrack.Services.Http
{
    /// <summary>
    /// Factory for creating and managing shared HttpClient instances
    /// Prevents socket exhaustion by reusing HttpClient instances
    /// </summary>
    public static class HttpClientFactory
    {
        private static readonly Lazy<HttpClient> _defaultClient = new Lazy<HttpClient>(() => CreateDefaultClient());
        private static readonly Lazy<HttpClient> _longTimeoutClient = new Lazy<HttpClient>(() => CreateLongTimeoutClient());

        /// <summary>
        /// Get the default shared HttpClient instance (30 second timeout)
        /// </summary>
        public static HttpClient DefaultClient => _defaultClient.Value;

        /// <summary>
        /// Get a shared HttpClient with longer timeout (60 seconds) for slow operations
        /// </summary>
        public static HttpClient LongTimeoutClient => _longTimeoutClient.Value;

        private static HttpClient CreateDefaultClient()
        {
            var config = AppConfiguration.Instance;
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(config.RequestTimeoutSeconds)
            };
            
            // Set default headers
            client.DefaultRequestHeaders.Add("User-Agent", "AkademiTrack/1.0");
            
            return client;
        }

        private static HttpClient CreateLongTimeoutClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
            
            client.DefaultRequestHeaders.Add("User-Agent", "AkademiTrack/1.0");
            
            return client;
        }

        /// <summary>
        /// Use sparingly - prefer DefaultClient or LongTimeoutClient
        /// </summary>
        public static HttpClient CreateClient(TimeSpan? timeout = null)
        {
            var config = AppConfiguration.Instance;
            var client = new HttpClient
            {
                Timeout = timeout ?? TimeSpan.FromSeconds(config.RequestTimeoutSeconds)
            };
            
            client.DefaultRequestHeaders.Add("User-Agent", "AkademiTrack/1.0");
            
            return client;
        }
    }
}
