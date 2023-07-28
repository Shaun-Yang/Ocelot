using System.Collections.Concurrent;
using Ocelot.Configuration;
using Ocelot.Configuration.Builder;
using Ocelot.Configuration.Creator;
using Ocelot.Configuration.File;
using Ocelot.Configuration.Repository;
using Ocelot.DownstreamRouteFinder;
using Ocelot.DownstreamRouteFinder.Finder;
using Ocelot.DownstreamRouteFinder.UrlMatcher;
using Ocelot.LoadBalancer.LoadBalancers;
using Ocelot.Responses;
using Route = Ocelot.Configuration.Route;
using RouteBuilder = Ocelot.Configuration.Builder.RouteBuilder;

namespace Ocelot.Samples.OcelotBasic.ApiGateway;

public class DynamicDownstreamRouteWithAuthCreator : IDownstreamRouteProvider
{
    private readonly IQoSOptionsCreator _qoSOptionsCreator;
    private readonly ConcurrentDictionary<string, OkResponse<DownstreamRouteHolder>> _cache;
    private readonly AuthenticationOptions _globalAuthenticationOptions = null;


    public DynamicDownstreamRouteWithAuthCreator(IQoSOptionsCreator qoSOptionsCreator, IConfiguration fileConfiguration, IInternalConfigurationRepository internalConfigurationRepository)
    {
        _qoSOptionsCreator = qoSOptionsCreator;
        _cache = new ConcurrentDictionary<string, OkResponse<DownstreamRouteHolder>>();

        var configuration = internalConfigurationRepository.Get().Data;

        // 创建无需授权的路由缓存
        foreach (Route route in configuration.Routes)
        {
            foreach (var httpMethod in route.UpstreamHttpMethod)
            {
                var key = CreateLoadBalancerKey(route.UpstreamTemplatePattern.OriginalValue, httpMethod.Method.ToUpper(),
                    configuration.LoadBalancerOptions);

                var okResponse =
                    new OkResponse<DownstreamRouteHolder>(new DownstreamRouteHolder(new List<PlaceholderNameAndValue>(),
                        route));

                _cache.TryAdd(key, okResponse);
            }
        }

        var globalConfiguration = fileConfiguration.GetSection("GlobalConfiguration:AuthenticationOptions")
            .Get<FileAuthenticationOptions>();

        if (globalConfiguration != null)
        {
            _globalAuthenticationOptions = new AuthenticationOptions(globalConfiguration.AllowedScopes,
                globalConfiguration.AuthenticationProviderKey);
        }
    }

    public Response<DownstreamRouteHolder> Get(string upstreamUrlPath, string upstreamQueryString, string upstreamHttpMethod, IInternalConfiguration configuration, string upstreamHost)
    {
        var serviceName = GetServiceName(upstreamUrlPath);

        var downstreamPath = GetDownstreamPath(upstreamUrlPath);

        if (HasQueryString(downstreamPath))
        {
            downstreamPath = RemoveQueryString(downstreamPath);
        }

        var downstreamPathForKeys = $"/{serviceName}{downstreamPath}";

        var loadBalancerKey = CreateLoadBalancerKey(downstreamPathForKeys, upstreamHttpMethod, configuration.LoadBalancerOptions);

        // 一开始所有的无需授权路由就已经加入了
        if (_cache.TryGetValue(loadBalancerKey, out var downstreamRouteHolder))
        {
            return downstreamRouteHolder;
        }

        var qosOptions = _qoSOptionsCreator.Create(configuration.QoSOptions, downstreamPathForKeys, new List<string> { upstreamHttpMethod });

        var upstreamPathTemplate = new UpstreamPathTemplateBuilder().WithOriginalValue(upstreamUrlPath).Build();

        var downstreamRouteBuilder = new DownstreamRouteBuilder()
            .WithServiceName(serviceName)
            .WithLoadBalancerKey(loadBalancerKey)
            .WithDownstreamPathTemplate(downstreamPath)
            .WithUseServiceDiscovery(true)
            .WithHttpHandlerOptions(configuration.HttpHandlerOptions)
            .WithQosOptions(qosOptions)
            .WithDownstreamScheme(configuration.DownstreamScheme)
            .WithLoadBalancerOptions(configuration.LoadBalancerOptions)
            .WithDownstreamHttpVersion(configuration.DownstreamHttpVersion)
            .WithUpstreamPathTemplate(upstreamPathTemplate);

        // 原来使用 route 来设置rate limiting，现在加一个判断，只有启用了才视作ratelimit 路由
        var rateLimitOptions = configuration.Routes?.SelectMany(x => x.DownstreamRoute)
            .FirstOrDefault(x => x.ServiceName == serviceName && x.RateLimitOptions.EnableRateLimiting);

        if (rateLimitOptions != null)
        {
            downstreamRouteBuilder
                .WithRateLimitOptions(rateLimitOptions.RateLimitOptions)
                .WithEnableRateLimiting(true);
        }

        if (_globalAuthenticationOptions != null)
        {
            downstreamRouteBuilder.WithAuthenticationOptions(_globalAuthenticationOptions)
                .WithIsAuthenticated(true);
        }

        var downstreamRoute = downstreamRouteBuilder.Build();

        var route = new RouteBuilder()
            .WithDownstreamRoute(downstreamRoute)
            .WithUpstreamHttpMethod(new List<string> { upstreamHttpMethod })
            .WithUpstreamPathTemplate(upstreamPathTemplate)
            .Build();

        downstreamRouteHolder = new OkResponse<DownstreamRouteHolder>(new DownstreamRouteHolder(new List<PlaceholderNameAndValue>(), route));

        _cache.AddOrUpdate(loadBalancerKey, downstreamRouteHolder, (x, y) => downstreamRouteHolder);

        return downstreamRouteHolder;
    }

    private static string RemoveQueryString(string downstreamPath)
    {
        return downstreamPath
            .Substring(0, downstreamPath.IndexOf('?'));
    }

    private static bool HasQueryString(string downstreamPath)
    {
        return downstreamPath.Contains('?');
    }

    private static string GetDownstreamPath(string upstreamUrlPath)
    {
        if (upstreamUrlPath.IndexOf('/', 1) == -1)
        {
            return "/";
        }

        return upstreamUrlPath
            .Substring(upstreamUrlPath.IndexOf('/', 1));
    }

    private static string GetServiceName(string upstreamUrlPath)
    {
        if (upstreamUrlPath.IndexOf('/', 1) == -1)
        {
            return upstreamUrlPath
                .Substring(1);
        }

        return upstreamUrlPath
            .Substring(1, upstreamUrlPath.IndexOf('/', 1))
            .TrimEnd('/');
    }

    private static string CreateLoadBalancerKey(string downstreamTemplatePath, string httpMethod, LoadBalancerOptions loadBalancerOptions)
    {
        if (!string.IsNullOrEmpty(loadBalancerOptions.Type) && !string.IsNullOrEmpty(loadBalancerOptions.Key) && loadBalancerOptions.Type == nameof(CookieStickySessions))
        {
            return $"{nameof(CookieStickySessions)}:{loadBalancerOptions.Key}";
        }

        return CreateQoSKey(downstreamTemplatePath, httpMethod);
    }

    private static string CreateQoSKey(string downstreamTemplatePath, string httpMethod)
    {
        var loadBalancerKey = $"{downstreamTemplatePath}|{httpMethod}";
        return loadBalancerKey;
    }
}