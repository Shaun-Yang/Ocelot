using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Ocelot.DependencyInjection;
using Ocelot.DownstreamRouteFinder.Finder;
using Ocelot.Middleware;
using Ocelot.Provider.Consul;

namespace Ocelot.Samples.OcelotBasic.ApiGateway;

public class Startup
{
    // This method gets called by the runtime. Use this method to add services to the container.
    // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddAuthentication("Bearer")
            .AddJwtBearer("Bearer", options =>
            {
                options.Authority = "https://localhost:7346";

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateAudience = false
                };
            });

        services.AddOcelot()
            .AddConsul();

        services.AddSingleton<IDownstreamRouteProvider, DynamicDownstreamRouteWithAuthCreator>();
        services.Replace(new ServiceDescriptor(typeof(IDownstreamRouteProviderFactory),
            typeof(DynamicDownstreamRouteProviderFactory), ServiceLifetime.Singleton));

        services.AddLogging();
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseOcelot().Wait();
    }
}
