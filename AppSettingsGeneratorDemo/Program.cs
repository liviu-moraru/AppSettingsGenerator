using System;
using System.IO;
using additiv.Caching.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AppSettingsGeneratorDemo
{
    class Program
    {

        private static IConfiguration _configuration;
        static void Main(string[] args)
        {
            IHost host;
            var rc = new RedisConfiguration();
            var ev = new EvoPdfConfiguration();
            host = AppStartup();
            var hc = host.Services.GetRequiredService<HostConfiguration>();
        }

        private static IHost AppStartup()
        {
            var builder = new ConfigurationBuilder();
            BuildConfig(builder);

            var host = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    ConfigureServices(services);
                })
                .Build();

            return host;
        }

        private static void BuildConfig(IConfigurationBuilder builder)
        {
            _configuration = builder.SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .Build();
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            var hostConfiguration = services.AddHostConfiguration(_configuration);
            services.AddSingleton<HostConfiguration>(hostConfiguration);
        }
    }
}