using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ocelot.Configuration;
using Ocelot.DownstreamRouteFinder.Finder;
using Ocelot.Logging;

namespace Ocelot.Samples.OcelotBasic.ApiGateway;

public class DynamicDownstreamRouteProviderFactory : IDownstreamRouteProviderFactory
{
    private readonly IOcelotLogger _logger;
    private readonly bool _enableDynamicRouting;
    private readonly IDownstreamRouteProvider _dynamicDownstreamRouteWithAuthCreator;
    private readonly IDownstreamRouteProvider _downstreamRouteCreator;
    private readonly IDownstreamRouteProvider _downstreamRouteFinder;

    public DynamicDownstreamRouteProviderFactory(IServiceProvider provider, IOcelotLoggerFactory factory, IConfiguration configuration)
    {
        var value = configuration["GlobalConfiguration:EnableDynamicRouting"];

        _enableDynamicRouting = !string.IsNullOrEmpty(value) && value == "True";
        _logger = factory.CreateLogger<DownstreamRouteProviderFactory>();
        var providers = provider.GetServices<IDownstreamRouteProvider>().ToDictionary(x => x.GetType().Name);

        _dynamicDownstreamRouteWithAuthCreator = providers[nameof(DynamicDownstreamRouteWithAuthCreator)];
        _downstreamRouteCreator = providers[nameof(DownstreamRouteCreator)];
        _downstreamRouteFinder = providers[nameof(DownstreamRouteFinder.Finder.DownstreamRouteFinder)];
    }

    public IDownstreamRouteProvider Get(IInternalConfiguration config)
    {
        // 原来的实现，route 为0 使用 动态路由，不为0则去路由中匹配路由
        ////todo - this is a bit hacky we are saying there are no routes or there are routes but none of them have
        ////an upstream path template which means they are dyanmic and service discovery is on...
        //if ((!config.Routes.Any() || config.Routes.All(x => string.IsNullOrEmpty(x.UpstreamTemplatePattern?.OriginalValue))) && IsServiceDiscovery(config.ServiceProviderConfiguration))
        //{
        //    _logger.LogInformation($"Selected {nameof(DownstreamRouteCreator)} as DownstreamRouteProvider for this request");
        //    return _providers[nameof(DownstreamRouteCreator)];
        //}

        //return _providers[nameof(DownstreamRouteFinder)];

        // 修改后的实现。在全局配置上增加了启用动态路由的属性。如果启用了，普通路由视作无需鉴权的路由，其他全部视作动态路由，需要授权操作。授权配置在全局配置上配置；未启用按照原逻辑进行。
        if (_enableDynamicRouting)
        {
            return _dynamicDownstreamRouteWithAuthCreator;
        }

        //todo - this is a bit hacky we are saying there are no routes or there are routes but none of them have
        //an upstream path template which means they are dyanmic and service discovery is on...
        if ((!config.Routes.Any() || config.Routes.All(x => string.IsNullOrEmpty(x.UpstreamTemplatePattern?.OriginalValue))) && IsServiceDiscovery(config.ServiceProviderConfiguration))
        {
            _logger.LogInformation($"Selected {nameof(DownstreamRouteCreator)} as DownstreamRouteProvider for this request");
            return _downstreamRouteCreator;
        }

        return _downstreamRouteFinder;
    }

    private static bool IsServiceDiscovery(ServiceProviderConfiguration config)
    {
        return !string.IsNullOrEmpty(config?.Host) && config?.Port > 0 && !string.IsNullOrEmpty(config.Type);
    }
}
