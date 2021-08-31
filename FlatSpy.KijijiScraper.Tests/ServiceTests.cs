using FlatSpy.KijijiScraper.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Moq;
using Moq.Contrib.HttpClient;
using Moq.Protected;
using Newtonsoft.Json;
using NUnit.Framework;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FlatSpy.KijijiScraper.Tests
{
    public class ServiceTests
    {
        public static readonly string BaseUrl = "http://test";

        public static readonly string FlatHtmlContent =
            File.ReadAllText(TestContext.CurrentContext.TestDirectory + "\\Data\\flat.html");

        public readonly string ListHtmlContent =
            File.ReadAllText(TestContext.CurrentContext.TestDirectory + "\\Data\\list.html");

        private Mock<IConnectionMultiplexer> _redisMock;
        private Mock<HttpMessageHandler> _httpMessageHandlerMock;
        private Mock<IHttpClientFactory> _httpClientFactoryMock;
        private Mock<IDatabase> _redisDatabaseMock;
        private KijijiScraperService _service;

        [SetUp]
        public void Setup()
        {
            _redisMock = new Mock<IConnectionMultiplexer>();
            _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
            _httpClientFactoryMock = Mock.Get(_httpMessageHandlerMock.CreateClientFactory());
            _redisDatabaseMock = new Mock<IDatabase>();

            _redisMock
                .Setup(_ => _.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(_redisDatabaseMock.Object);

            _httpClientFactoryMock
                .Setup(x => x.CreateClient("kijiji"))
                .Returns(() =>
                {
                    var client = _httpMessageHandlerMock.CreateClient();
                    client.BaseAddress = new Uri(BaseUrl);
                    return client;
                });

            _service = new KijijiScraperService(
                _httpClientFactoryMock.Object,
                _redisMock.Object,
                new NullLoggerFactory(),
                null);
        }

        [Test]
        public async Task TestProcessEntry()
        {
            RedisKey key = default;
            RedisValue[] values = default;

            var uri = "/flat/666";
            var id = "666";

            _httpMessageHandlerMock
                .SetupRequest(HttpMethod.Get, $"{BaseUrl}{uri}")
                .ReturnsResponse(FlatHtmlContent);

            _redisDatabaseMock
                .Setup(
                    m => m.ListRightPushAsync(
                        It.IsAny<RedisKey>(),
                        It.IsAny<RedisValue[]>(),
                        It.IsAny<When>(),
                        It.IsAny<CommandFlags>()))
                .Callback<RedisKey, RedisValue[], When, CommandFlags>(
                    (k, v, w, f) => { key = k; values = v; });

            await _service.ProcessEntry(id, uri);

            Assert.AreEqual(new RedisKey("entries"), key);
            Assert.AreEqual(1, values.Length);

            var entry = JsonConvert.DeserializeObject<Entry>(values.First());

            Assert.AreEqual(id, entry.Id);
            Assert.AreEqual("390  Boul.Cote Vertu, Saint-Laurent Montreal, QC, H4N 1E3", entry.Address);
            Assert.AreEqual(uri, entry.Uri);
            Assert.AreEqual("2 Bedroom Apartment for Rent - 390  Boul.Cote Vertu", entry.Title);
        }

        [Test]
        public async Task TestListEntries()
        {
            _httpMessageHandlerMock
                .SetupRequest(HttpMethod.Get, $"{BaseUrl}{KijijiScraperService.ApartmentsListUri}")
                .ReturnsResponse(ListHtmlContent);

            var entries = new List<(string, string)>();
            await foreach (var entry in _service.ListEntries().WithCancellation(CancellationToken.None))
            {
                entries.Add(entry);
            }

            Assert.Equals(entries.Count, 7);
        }
    }
}