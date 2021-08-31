using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using System.Linq;
using StackExchange.Redis;
using MongoDB.Driver;
using MongoDB.Bson;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace FlatSpy.KijijiScraper.Services
{
    public class Entry
    {
        public string Id { get; set; }

        public string Title { get; set; }

        public string Uri { get; set; }

        public string Address { get; set; }

        public void TryGetTitle(HtmlDocument doc)
        {
            var node = doc.DocumentNode.SelectSingleNode("//meta[@property='og:title']");

            if (node != null)
            {
                Title = node.Attributes["content"].Value.Split("|").First().Trim();
            }
        }

        public void TryGetAddress(HtmlDocument doc)
        {
            var node = doc.DocumentNode.SelectSingleNode("//span[@itemprop='address']");

            if (node != null)
            {
                Address = node.InnerText;
            }
        }
    }

    public class KijijiScraperService : BackgroundService
    {
        private static readonly int _delay = 300000;
        public static readonly string ApartmentsListUri = "/b-grand-montreal/apartment-for-rent/k0l80002";

        private static readonly string exampleUri = "/v-appartement-condo/ville-de-montreal/2-bedroom-apartment-for-rent-390-boul-cote-vertu/1578304397";
        private readonly ILogger<KijijiScraperService> _logger;
        private readonly HttpClient _httpClient;
        private readonly IConnectionMultiplexer _redis;
        private readonly IMongoClient _mongo;

        public KijijiScraperService(
            IHttpClientFactory httpClientFactory,
            IConnectionMultiplexer redis,
            ILoggerFactory loggerFactory,
            IMongoClient mongo)
        {
            _logger = loggerFactory.CreateLogger<KijijiScraperService>();
            _httpClient = httpClientFactory.CreateClient("kijiji");
            _redis = redis;
            _mongo = mongo;
        }

        public async IAsyncEnumerable<(string, string)> ListEntries()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, ApartmentsListUri);
            var response = await _httpClient.SendAsync(request);
            var source = await response.Content.ReadAsStringAsync();
            var doc = new HtmlDocument();

            doc.LoadHtml(source);

            var nodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'search-item') and contains(@class, 'regular-ad')]");

            foreach (var node in nodes)
            {
                var uri = node.Attributes["data-vip-url"]?.Value;
                var id = node.Attributes["data-listing-id"]?.Value;

                if (uri != null && id != null)
                {
                    yield return (id, uri);
                }
            }
        }

        public async Task ProcessEntry(string id, string uri)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            var response = await _httpClient.SendAsync(request);
            var source = await response.Content.ReadAsStringAsync();
            var doc = new HtmlDocument();
            var entry = new Entry { Id = id, Uri = uri };

            doc.LoadHtml(source);

            entry.TryGetTitle(doc);
            entry.TryGetAddress(doc);

            if (entry.Title != null && entry.Address != null)
            {
                await _redis.GetDatabase().ListRightPushAsync("entries", new RedisValue[] {
                    JsonConvert.SerializeObject(entry)
                });
            }
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            _logger.LogInformation("Start KijijiScraperService");

            var database = _mongo.GetDatabase("kijiji-scrapper");
            var collection = database.GetCollection<BsonDocument>("seen");

            while (!ct.IsCancellationRequested)
            {
                await foreach (var (id, uri) in ListEntries())
                {
                    var seen = (await collection.Find($"{{ _id: ObjectId('{id}') }}").SingleAsync(ct)) != null;

                    if (!seen)
                    {
                        await ProcessEntry(id, uri);
                    }
                }

                await Task.Delay(_delay, ct);
            }
        }
    }
}
