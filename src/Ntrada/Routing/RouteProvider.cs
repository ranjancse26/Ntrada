using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Ntrada.Routing
{
    public class RouteProvider : IRouteProvider
    {
        private readonly IDictionary<string, Action<IRouteBuilder, string, RouteConfig>> _methods;
        private readonly IRouteConfigurator _routeConfigurator;
        private readonly IRequestExecutionValidator _requestExecutionValidator;
        private readonly IUpstreamBuilder _upstreamBuilder;
        private readonly NtradaConfiguration _configuration;
        private readonly IRequestHandlerManager _requestHandlerManager;
        private readonly ILogger<RouteProvider> _logger;

        public RouteProvider(NtradaConfiguration configuration, IRequestHandlerManager requestHandlerManager,
            IRouteConfigurator routeConfigurator, IRequestExecutionValidator requestExecutionValidator,
            IUpstreamBuilder upstreamBuilder, ILogger<RouteProvider> logger)
        {
            _routeConfigurator = routeConfigurator;
            _requestExecutionValidator = requestExecutionValidator;
            _upstreamBuilder = upstreamBuilder;
            _configuration = configuration;
            _requestHandlerManager = requestHandlerManager;
            _logger = logger;
            _methods = new Dictionary<string, Action<IRouteBuilder, string, RouteConfig>>
            {
                ["get"] = (builder, path, routeConfig) =>
                    builder.MapGet(path,
                        (request, response, routeData) => Handle(request, response, routeData, routeConfig)),
                ["post"] = (builder, path, routeConfig) =>
                    builder.MapPost(path,
                        (request, response, routeData) => Handle(request, response, routeData, routeConfig)),
                ["put"] = (builder, path, routeConfig) =>
                    builder.MapPut(path,
                        (request, response, routeData) => Handle(request, response, routeData, routeConfig)),
                ["delete"] = (builder, path, routeConfig) =>
                    builder.MapDelete(path,
                        (request, response, routeData) => Handle(request, response, routeData, routeConfig)),
                ["patch"] = (builder, path, routeConfig) =>
                    builder.MapVerb("patch", path,
                        (request, response, routeData) => Handle(request, response, routeData, routeConfig)),
            };
        }

        private async Task Handle(HttpRequest request, HttpResponse response, RouteData routeData,
            RouteConfig routeConfig)
        {
            if (!await _requestExecutionValidator.TryExecuteAsync(request, response, routeData, routeConfig))
            {
                return;
            }

            var handler = routeConfig.Route.Use;
            await _requestHandlerManager.HandleAsync(handler, request, response, routeData, routeConfig);
        }

        public Action<IRouteBuilder> Build()
            => routeBuilder =>
            {
                foreach (var module in _configuration.Modules.Where(m => m.Enabled != false))
                {
                    _logger.LogInformation($"Building routes for the module: '{module.Name}'");
                    foreach (var route in module.Routes)
                    {
                        route.Upstream = _upstreamBuilder.Build(module, route);
                        var routeConfig = _routeConfigurator.Configure(module, route);
                        _methods[route.Method](routeBuilder, route.Upstream, routeConfig);
                    }
                }
            };
    }
}