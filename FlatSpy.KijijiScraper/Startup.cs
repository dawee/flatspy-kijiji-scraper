using FlatSpy.KijijiScraper.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;
using StackExchange.Redis;
using System;
using System.Net.Http.Headers;

namespace FlatSpy.KijijiScraper
{
    public class Startup
    {
        private ConnectionMultiplexer _redis;
        private MongoClient _mongo;

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            _redis = ConnectionMultiplexer.Connect(
                new ConfigurationOptions
                {
                    EndPoints = { "redis:6379" },
                    AbortOnConnectFail = false
                });

            _mongo = new MongoClient("mongodb://kijiji-scrapper-db:27017");

            services.AddRazorPages();
            services.AddSingleton<IConnectionMultiplexer>(_redis);
            services.AddSingleton<IMongoClient>(_mongo);
            services.AddHttpClient<KijijiScraperService>("kijiji", httpClient => {
                httpClient.BaseAddress = new Uri("https://www.kijiji.ca");
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/74.0.3729.169 Safari/537.36");
                httpClient.DefaultRequestHeaders.Add("Accept-Encoding", "identity");
                httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
            });
            services.AddLogging();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapRazorPages();
            });
        }
    }
}
