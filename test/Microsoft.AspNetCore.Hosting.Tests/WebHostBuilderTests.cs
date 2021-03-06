// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Fakes;
using Microsoft.AspNetCore.Hosting.Internal;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.PlatformAbstractions;
using Xunit;

[assembly: HostingStartup(typeof(WebHostBuilderTests.TestHostingStartup))]

namespace Microsoft.AspNetCore.Hosting
{
    public class WebHostBuilderTests
    {
        [Fact]
        public void Build_honors_UseStartup_with_string()
        {
            var builder = CreateWebHostBuilder().UseServer(new TestServer());

            var host = (WebHost)builder.UseStartup("MyStartupAssembly").Build();

            Assert.Equal("MyStartupAssembly", host.Options.ApplicationName);
            Assert.Equal("MyStartupAssembly", host.Options.StartupAssembly);
        }

        [Fact]
        public async Task StartupMissing_Fallback()
        {
            var builder = CreateWebHostBuilder();
            var server = new TestServer();
            var host = builder.UseServer(server).UseStartup("MissingStartupAssembly").Build();
            using (host)
            {
                await host.StartAsync();
                await AssertResponseContains(server.RequestDelegate, "MissingStartupAssembly");
            }
        }

        [Fact]
        public async Task StartupStaticCtorThrows_Fallback()
        {
            var builder = CreateWebHostBuilder();
            var server = new TestServer();
            var host = builder.UseServer(server).UseStartup<StartupStaticCtorThrows>().Build();
            using (host)
            {
                await host.StartAsync();
                await AssertResponseContains(server.RequestDelegate, "Exception from static constructor");
            }
        }

        [Fact]
        public async Task StartupCtorThrows_Fallback()
        {
            var builder = CreateWebHostBuilder();
            var server = new TestServer();
            var host = builder.UseServer(server).UseStartup<StartupCtorThrows>().Build();
            using (host)
            {
                await host.StartAsync();
                await AssertResponseContains(server.RequestDelegate, "Exception from constructor");
            }
        }

        [Fact]
        public async Task StartupCtorThrows_TypeLoadException()
        {
            var builder = CreateWebHostBuilder();
            var server = new TestServer();
            var host = builder.UseServer(server).UseStartup<StartupThrowTypeLoadException>().Build();
            using (host)
            {
                await host.StartAsync();
                await AssertResponseContains(server.RequestDelegate, "Message from the LoaderException</div>");
            }
        }

        [Fact]
        public async Task IApplicationLifetimeRegisteredEvenWhenStartupCtorThrows_Fallback()
        {
            var builder = CreateWebHostBuilder();
            var server = new TestServer();
            var host = builder.UseServer(server).UseStartup<StartupCtorThrows>().Build();
            using (host)
            {
                await host.StartAsync();
                var services = host.Services.GetServices<IApplicationLifetime>();
                Assert.NotNull(services);
                Assert.NotEmpty(services);

                await AssertResponseContains(server.RequestDelegate, "Exception from constructor");
            }
        }

        [Fact]
        public async Task DefaultObjectPoolProvider_IsRegistered()
        {
            var server = new TestServer();
            var host = CreateWebHostBuilder()
                .UseServer(server)
                .Configure(app => { })
                .Build();
            using (host)
            {
                await host.StartAsync();
                Assert.IsType<DefaultObjectPoolProvider>(host.Services.GetService<ObjectPoolProvider>());
            }
        }

        [Fact]
        public async Task StartupConfigureServicesThrows_Fallback()
        {
            var builder = CreateWebHostBuilder();
            var server = new TestServer();
            var host = builder.UseServer(server).UseStartup<StartupConfigureServicesThrows>().Build();
            using (host)
            {
                await host.StartAsync();
                await AssertResponseContains(server.RequestDelegate, "Exception from ConfigureServices");
            }
        }

        [Fact]
        public async Task StartupConfigureThrows_Fallback()
        {
            var builder = CreateWebHostBuilder();
            var server = new TestServer();
            var host = builder.UseServer(server).UseStartup<StartupConfigureServicesThrows>().Build();
            using (host)
            {
                await host.StartAsync();
                await AssertResponseContains(server.RequestDelegate, "Exception from Configure");
            }
        }

        [Fact]
        public void DefaultCreatesLoggerFactory()
        {
            var hostBuilder = new WebHostBuilder()
                .UseServer(new TestServer())
                .UseStartup<StartupNoServices>();

            var host = (WebHost)hostBuilder.Build();

            Assert.NotNull(host.Services.GetService<ILoggerFactory>());
        }

        [Fact]
        public void ConfigureDefaultServiceProvider()
        {
            var hostBuilder = new WebHostBuilder()
                .UseServer(new TestServer())
                .ConfigureServices(s =>
                {
                    s.AddTransient<ServiceD>();
                    s.AddScoped<ServiceC>();
                })
                .Configure(app =>
                {
                    app.ApplicationServices.GetRequiredService<ServiceC>();
                })
                .UseDefaultServiceProvider(options =>
                {
                    options.ValidateScopes = true;
                });

            Assert.Throws<InvalidOperationException>(() => hostBuilder.Build());
        }

        [Fact]
        public void UseLoggerFactoryHonored()
        {
            var loggerFactory = new LoggerFactory();

            var hostBuilder = new WebHostBuilder()
                .UseLoggerFactory(loggerFactory)
                .UseServer(new TestServer())
                .UseStartup<StartupNoServices>();

            var host = (WebHost)hostBuilder.Build();

            Assert.Same(loggerFactory, host.Services.GetService<ILoggerFactory>());
        }

        [Fact]
        public void MultipleConfigureLoggingInvokedInOrder()
        {
            var callCount = 0; //Verify ordering
            var hostBuilder = new WebHostBuilder()
                .ConfigureLogging(loggerFactory =>
                {
                    Assert.Equal(0, callCount++);
                })
                .ConfigureLogging(loggerFactory =>
                {
                    Assert.Equal(1, callCount++);
                })
                .UseServer(new TestServer())
                .UseStartup<StartupNoServices>();

            var host = (WebHost)hostBuilder.Build();
            Assert.Equal(2, callCount);
        }

        [Fact]
        public void UseLoggerFactoryDelegateIsHonored()
        {
            var loggerFactory = new LoggerFactory();

            var hostBuilder = new WebHostBuilder()
                .UseLoggerFactory(_ => loggerFactory)
                .UseServer(new TestServer())
                .UseStartup<StartupNoServices>();

            var host = (WebHost)hostBuilder.Build();

            Assert.Same(loggerFactory, host.Services.GetService<ILoggerFactory>());
        }

        [Fact]
        public void UseLoggerFactoryFuncAndConfigureLoggingCompose()
        {
            var callCount = 0; //Verify that multiple configureLogging calls still compose correctly.
            var loggerFactory = new LoggerFactory();
            var hostBuilder = new WebHostBuilder()
                .UseLoggerFactory(_ => loggerFactory)
                .ConfigureLogging(factory =>
                {
                    Assert.Equal(0, callCount++);
                })
                .ConfigureLogging(factory =>
                {
                    Assert.Equal(1, callCount++);
                })
                .UseServer(new TestServer())
                .UseStartup<StartupNoServices>();
            var host = (WebHost)hostBuilder.Build();
            Assert.Equal(2, callCount);
            Assert.Same(loggerFactory, host.Services.GetService<ILoggerFactory>());
        }

        [Fact]
        public void ConfigureLoggingCalledIfLoggerFactoryTypeMatches()
        {
            var callCount = 0;
            var hostBuilder = new WebHostBuilder()
                .UseLoggerFactory(_ => new SubLoggerFactory())
                .ConfigureLogging<CustomLoggerFactory>(factory =>
                {
                    Assert.Equal(0, callCount++);
                })
                .UseServer(new TestServer())
                .UseStartup<StartupNoServices>();

            var host = (WebHost)hostBuilder.Build();
            Assert.Equal(1, callCount);
        }

        [Fact]
        public void ConfigureLoggingNotCalledIfLoggerFactoryTypeDoesNotMatches()
        {
            var callCount = 0;
            var hostBuilder = new WebHostBuilder()
                .UseLoggerFactory(_ => new NonSubLoggerFactory())
                .ConfigureLogging<CustomLoggerFactory>(factory =>
                {
                    Assert.Equal(0, callCount++);
                })
                .UseServer(new TestServer())
                .UseStartup<StartupNoServices>();

            var host = (WebHost)hostBuilder.Build();
            Assert.Equal(0, callCount);
        }

        [Fact]
        public void CanUseCustomLoggerFactory()
        {
            var hostBuilder = new WebHostBuilder()
                .UseLoggerFactory(_ => new CustomLoggerFactory())
                .ConfigureLogging<CustomLoggerFactory>(factory =>
                {
                    factory.CustomConfigureMethod();
                })
                .UseServer(new TestServer())
                .UseStartup<StartupNoServices>();
            var host = (WebHost)hostBuilder.Build();
            Assert.IsType(typeof(CustomLoggerFactory), host.Services.GetService<ILoggerFactory>());
        }

        [Fact]
        public void ThereIsAlwaysConfiguration()
        {
            var hostBuilder = new WebHostBuilder()
                .UseServer(new TestServer())
                .UseStartup<StartupNoServices>();
            var host = (WebHost)hostBuilder.Build();

            Assert.NotNull(host.Services.GetService<IConfiguration>());
        }

        [Fact]
        public void ConfigureConfigurationSettingsPropagated()
        {
            var hostBuilder = new WebHostBuilder()
                .UseSetting("key1", "value1")
                .ConfigureConfiguration((context, configBuilder) =>
                {
                    var config = configBuilder.Build();
                    Assert.Equal("value1", config["key1"]);
                })
                .UseServer(new TestServer())
                .UseStartup<StartupNoServices>();
            var host = (WebHost)hostBuilder.Build();
        }

        [Fact]
        public void CanConfigureConfigurationAndRetrieveFromDI()
        {
            var hostBuilder = new WebHostBuilder()
                .ConfigureConfiguration((_, configBuilder) =>
                {
                    configBuilder
                        .AddInMemoryCollection(
                            new KeyValuePair<string, string>[]
                            {
                                new KeyValuePair<string, string>("key1", "value1")
                            })
                        .AddEnvironmentVariables();
                })
                .UseServer(new TestServer())
                .UseStartup<StartupNoServices>();
            var host = (WebHost)hostBuilder.Build();

            var config = host.Services.GetService<IConfiguration>();
            Assert.NotNull(config);
            Assert.Equal("value1", config["key1"]);
        }

        [Fact]
        public void DoNotCaptureStartupErrorsByDefault()
        {
            var hostBuilder = new WebHostBuilder()
                .UseServer(new TestServer())
                .UseStartup<StartupBoom>();

            var exception = Assert.Throws<InvalidOperationException>(() => hostBuilder.Build());
            Assert.Equal("A public method named 'ConfigureProduction' or 'Configure' could not be found in the 'Microsoft.AspNetCore.Hosting.Fakes.StartupBoom' type.", exception.Message);
        }

        [Fact]
        public void CaptureStartupErrorsHonored()
        {
            var hostBuilder = new WebHostBuilder()
                .CaptureStartupErrors(false)
                .UseServer(new TestServer())
                .UseStartup<StartupBoom>();

            var exception = Assert.Throws<InvalidOperationException>(() => hostBuilder.Build());
            Assert.Equal("A public method named 'ConfigureProduction' or 'Configure' could not be found in the 'Microsoft.AspNetCore.Hosting.Fakes.StartupBoom' type.", exception.Message);
        }

        [Fact]
        public void ConfigureServices_CanBeCalledMultipleTimes()
        {
            var callCount = 0; // Verify ordering
            var hostBuilder = new WebHostBuilder()
                .UseServer(new TestServer())
                .ConfigureServices(services =>
                {
                    Assert.Equal(0, callCount++);
                    services.AddTransient<ServiceA>();
                })
                .ConfigureServices(services =>
                {
                    Assert.Equal(1, callCount++);
                    services.AddTransient<ServiceB>();
                })
                .Configure(app => { });

            var host = hostBuilder.Build();
            Assert.Equal(2, callCount);

            Assert.NotNull(host.Services.GetRequiredService<ServiceA>());
            Assert.NotNull(host.Services.GetRequiredService<ServiceB>());
        }

        [Fact]
        public void CodeBasedSettingsCodeBasedOverride()
        {
            var hostBuilder = new WebHostBuilder()
                .UseSetting(WebHostDefaults.EnvironmentKey, "EnvA")
                .UseSetting(WebHostDefaults.EnvironmentKey, "EnvB")
                .UseServer(new TestServer())
                .UseStartup<StartupNoServices>();

            var host = (WebHost)hostBuilder.Build();

            Assert.Equal("EnvB", host.Options.Environment);
        }

        [Fact]
        public void CodeBasedSettingsConfigBasedOverride()
        {
            var settings = new Dictionary<string, string>
            {
                { WebHostDefaults.EnvironmentKey, "EnvB" }
            };

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();

            var hostBuilder = new WebHostBuilder()
                .UseSetting(WebHostDefaults.EnvironmentKey, "EnvA")
                .UseConfiguration(config)
                .UseServer(new TestServer())
                .UseStartup<StartupNoServices>();

            var host = (WebHost)hostBuilder.Build();

            Assert.Equal("EnvB", host.Options.Environment);
        }

        [Fact]
        public void ConfigBasedSettingsCodeBasedOverride()
        {
            var settings = new Dictionary<string, string>
            {
                { WebHostDefaults.EnvironmentKey, "EnvA" }
            };

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();

            var hostBuilder = new WebHostBuilder()
                .UseConfiguration(config)
                .UseSetting(WebHostDefaults.EnvironmentKey, "EnvB")
                .UseServer(new TestServer())
                .UseStartup<StartupNoServices>();

            var host = (WebHost)hostBuilder.Build();

            Assert.Equal("EnvB", host.Options.Environment);
        }

        [Fact]
        public void ConfigBasedSettingsConfigBasedOverride()
        {
            var settings = new Dictionary<string, string>
            {
                { WebHostDefaults.EnvironmentKey, "EnvA" }
            };

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();

            var overrideSettings = new Dictionary<string, string>
            {
                { WebHostDefaults.EnvironmentKey, "EnvB" }
            };

            var overrideConfig = new ConfigurationBuilder()
                .AddInMemoryCollection(overrideSettings)
                .Build();

            var hostBuilder = new WebHostBuilder()
                .UseConfiguration(config)
                .UseConfiguration(overrideConfig)
                .UseServer(new TestServer())
                .UseStartup<StartupNoServices>();

            var host = (WebHost)hostBuilder.Build();

            Assert.Equal("EnvB", host.Options.Environment);
        }

        [Fact]
        public void UseEnvironmentIsNotOverriden()
        {
            var vals = new Dictionary<string, string>
            {
                { "ENV", "Dev" },
            };
            var builder = new ConfigurationBuilder()
                .AddInMemoryCollection(vals);
            var config = builder.Build();

            var expected = "MY_TEST_ENVIRONMENT";
            var host = new WebHostBuilder()
                .UseConfiguration(config)
                .UseEnvironment(expected)
                .UseServer(new TestServer())
                .UseStartup("Microsoft.AspNetCore.Hosting.Tests")
                .Build();

            Assert.Equal(expected, host.Services.GetService<IHostingEnvironment>().EnvironmentName);
        }

        [Fact]
        public void BuildAndDispose()
        {
            var vals = new Dictionary<string, string>
            {
                { "ENV", "Dev" },
            };
            var builder = new ConfigurationBuilder()
                .AddInMemoryCollection(vals);
            var config = builder.Build();

            var expected = "MY_TEST_ENVIRONMENT";
            var host = new WebHostBuilder()
                .UseConfiguration(config)
                .UseEnvironment(expected)
                .UseServer(new TestServer())
                .UseStartup("Microsoft.AspNetCore.Hosting.Tests")
                .Build();

            host.Dispose();
        }

        [Fact]
        public void UseBasePathConfiguresBasePath()
        {
            var vals = new Dictionary<string, string>
            {
                { "ENV", "Dev" },
            };
            var builder = new ConfigurationBuilder()
                .AddInMemoryCollection(vals);
            var config = builder.Build();

            var host = new WebHostBuilder()
                .UseConfiguration(config)
                .UseContentRoot("/")
                .UseServer(new TestServer())
                .UseStartup("Microsoft.AspNetCore.Hosting.Tests")
                .Build();

            Assert.Equal("/", host.Services.GetService<IHostingEnvironment>().ContentRootPath);
        }

        [Fact]
        public void RelativeContentRootIsResolved()
        {
            var host = new WebHostBuilder()
                .UseContentRoot("testroot")
                .UseServer(new TestServer())
                .UseStartup("Microsoft.AspNetCore.Hosting.Tests")
                .Build();

            var basePath = host.Services.GetRequiredService<IHostingEnvironment>().ContentRootPath;
            Assert.True(Path.IsPathRooted(basePath));
            Assert.EndsWith(Path.DirectorySeparatorChar + "testroot", basePath);
        }

        [Fact]
        public void DefaultContentRootIsApplicationBasePath()
        {
            var host = new WebHostBuilder()
                .UseServer(new TestServer())
                .UseStartup("Microsoft.AspNetCore.Hosting.Tests")
                .Build();

            var appBase = PlatformServices.Default.Application.ApplicationBasePath;
            Assert.Equal(appBase, host.Services.GetService<IHostingEnvironment>().ContentRootPath);
        }

        [Fact]
        public void DefaultApplicationNameToStartupAssemblyName()
        {
            var builder = new ConfigurationBuilder();
            var host = new WebHostBuilder()
                .UseServer(new TestServer())
                .UseStartup("Microsoft.AspNetCore.Hosting.Tests")
                .Build();

            var hostingEnv = host.Services.GetService<IHostingEnvironment>();
            Assert.Equal("Microsoft.AspNetCore.Hosting.Tests", hostingEnv.ApplicationName);
        }

        [Fact]
        public void DefaultApplicationNameToStartupType()
        {
            var builder = new ConfigurationBuilder();
            var host = new WebHostBuilder()
                .UseServer(new TestServer())
                .UseStartup<StartupNoServices>()
                .UseStartup("Microsoft.AspNetCore.Hosting.Tests.NonExistent")
                .Build();

            var hostingEnv = host.Services.GetService<IHostingEnvironment>();
            Assert.Equal("Microsoft.AspNetCore.Hosting.Tests.NonExistent", hostingEnv.ApplicationName);
        }

        [Fact]
        public void DefaultApplicationNameAndBasePathToStartupMethods()
        {
            var builder = new ConfigurationBuilder();
            var host = new WebHostBuilder()
                .UseServer(new TestServer())
                .Configure(app => { })
                .UseStartup("Microsoft.AspNetCore.Hosting.Tests.NonExistent")
                .Build();

            var hostingEnv = host.Services.GetService<IHostingEnvironment>();
            Assert.Equal("Microsoft.AspNetCore.Hosting.Tests.NonExistent", hostingEnv.ApplicationName);
        }

        [Fact]
        public void Configure_SupportsNonStaticMethodDelegate()
        {
            var host = new WebHostBuilder()
                .UseServer(new TestServer())
                .Configure(app => { })
                .Build();

            var hostingEnv = host.Services.GetService<IHostingEnvironment>();
            Assert.Equal("Microsoft.AspNetCore.Hosting.Tests", hostingEnv.ApplicationName);
        }

        [Fact]
        public void Configure_SupportsStaticMethodDelegate()
        {
            var host = new WebHostBuilder()
                .UseServer(new TestServer())
                .Configure(StaticConfigureMethod)
                .Build();

            var hostingEnv = host.Services.GetService<IHostingEnvironment>();
            Assert.Equal("Microsoft.AspNetCore.Hosting.Tests", hostingEnv.ApplicationName);
        }

        [Fact]
        public void Build_DoesNotAllowBuildingMuiltipleTimes()
        {
            var builder = CreateWebHostBuilder();
            var server = new TestServer();
            builder.UseServer(server)
                .UseStartup<StartupNoServices>()
                .Build();

            var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());

            Assert.Equal("WebHostBuilder allows creation only of a single instance of WebHost", ex.Message);
        }

        [Fact]
        public void Build_PassesSameAutoCreatedILoggerFactoryEverywhere()
        {
            var builder = CreateWebHostBuilder();
            var server = new TestServer();
            var host = builder.UseServer(server)
                .UseStartup<StartupWithILoggerFactory>()
                .Build();

            var startup = host.Services.GetService<StartupWithILoggerFactory>();

            Assert.Equal(startup.ConfigureLoggerFactory, startup.ConstructorLoggerFactory);
        }

        [Fact]
        public void Build_PassesSamePassedILoggerFactoryEverywhere()
        {
            var factory = new LoggerFactory();
            var builder = CreateWebHostBuilder();
            var server = new TestServer();
            var host = builder.UseServer(server)
                .UseLoggerFactory(factory)
                .UseStartup<StartupWithILoggerFactory>()
                .Build();

            var startup = host.Services.GetService<StartupWithILoggerFactory>();

            Assert.Equal(factory, startup.ConfigureLoggerFactory);
            Assert.Equal(factory, startup.ConstructorLoggerFactory);
        }

        [Fact]
        public void Build_PassedILoggerFactoryNotDisposed()
        {
            var factory = new DisposableLoggerFactory();
            var builder = CreateWebHostBuilder();
            var server = new TestServer();

            var host = builder.UseServer(server)
                .UseLoggerFactory(factory)
                .UseStartup<StartupWithILoggerFactory>()
                .Build();

            host.Dispose();

            Assert.Equal(false, factory.Disposed);
        }

        [Fact]
        public void Build_DoesNotOverrideILoggerFactorySetByConfigureServices()
        {
            var factory = new DisposableLoggerFactory();
            var builder = CreateWebHostBuilder();
            var server = new TestServer();

            var host = builder.UseServer(server)
                .ConfigureServices(collection => collection.AddSingleton<ILoggerFactory>(factory))
                .UseStartup<StartupWithILoggerFactory>()
                .Build();

            var factoryFromHost = host.Services.GetService<ILoggerFactory>();
            Assert.Equal(factory, factoryFromHost);
        }

        [Fact]
        public void Build_RunsHostingStartupAssembliesIfSpecified()
        {
            var builder = CreateWebHostBuilder()
                .CaptureStartupErrors(false)
                .UseSetting(WebHostDefaults.HostingStartupAssembliesKey, typeof(WebHostBuilderTests).GetTypeInfo().Assembly.FullName)
                .Configure(app => { })
                .UseServer(new TestServer());

            var host = (WebHost)builder.Build();

            Assert.Equal("1", builder.GetSetting("testhostingstartup"));
        }

        [Fact]
        public void Build_RunsHostingStartupAssembliesBeforeApplication()
        {
            var startup = new StartupVerifyServiceA();
            var startupAssemblyName = typeof(WebHostBuilderTests).GetTypeInfo().Assembly.GetName().Name;

            var builder = CreateWebHostBuilder()
                .CaptureStartupErrors(false)
                .UseSetting(WebHostDefaults.HostingStartupAssembliesKey, typeof(WebHostBuilderTests).GetTypeInfo().Assembly.FullName)
                .UseSetting(WebHostDefaults.ApplicationKey, startupAssemblyName)
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IStartup>(startup);
                })
                .UseServer(new TestServer());

            var host = (WebHost)builder.Build();

            Assert.NotNull(startup.ServiceADescriptor);
            Assert.NotNull(startup.ServiceA);
        }

        [Fact]
        public void Build_ConfigureLoggingInHostingStartupWorks()
        {
            var builder = CreateWebHostBuilder()
                .CaptureStartupErrors(false)
                .UseSetting(WebHostDefaults.HostingStartupAssembliesKey, typeof(WebHostBuilderTests).GetTypeInfo().Assembly.FullName)
                .Configure(app =>
                {
                    var loggerFactory = app.ApplicationServices.GetService<ILoggerFactory>();
                    var logger = loggerFactory.CreateLogger(nameof(WebHostBuilderTests));
                    logger.LogInformation("From startup");
                })
                .UseServer(new TestServer());

            var host = (WebHost)builder.Build();
            var sink = host.Services.GetRequiredService<ITestSink>();
            Assert.True(sink.Writes.Any(w => w.State.ToString() == "From startup"));
        }

        [Fact]
        public void Build_DoesNotRunHostingStartupAssembliesDoNotRunIfNotSpecified()
        {
            var builder = CreateWebHostBuilder()
                .Configure(app => { })
                .UseServer(new TestServer());

            var host = (WebHost)builder.Build();

            Assert.Null(builder.GetSetting("testhostingstartup"));
        }

        [Fact]
        public void Build_ThrowsIfUnloadableAssemblyNameInHostingStartupAssemblies()
        {
            var builder = CreateWebHostBuilder()
                .CaptureStartupErrors(false)
                .UseSetting(WebHostDefaults.HostingStartupAssembliesKey, "SomeBogusName")
                .Configure(app => { })
                .UseServer(new TestServer());

            var ex = Assert.Throws<AggregateException>(() => (WebHost)builder.Build());
            Assert.IsType<InvalidOperationException>(ex.InnerExceptions[0]);
            Assert.IsType<FileNotFoundException>(ex.InnerExceptions[0].InnerException);
        }

        [Fact]
        public async Task Build_DoesNotThrowIfUnloadableAssemblyNameInHostingStartupAssembliesAndCaptureStartupErrorsTrue()
        {
            var provider = new TestLoggerProvider();
            var builder = CreateWebHostBuilder()
                .ConfigureLogging(factory =>
                {
                    factory.AddProvider(provider);
                })
                .CaptureStartupErrors(true)
                .UseSetting(WebHostDefaults.HostingStartupAssembliesKey, "SomeBogusName")
                .Configure(app => { })
                .UseServer(new TestServer());

            using (var host = builder.Build())
            {
                await host.StartAsync();
                var context = provider.Sink.Writes.FirstOrDefault(s => s.EventId.Id == LoggerEventIds.HostingStartupAssemblyException);
                Assert.NotNull(context);
            }
        }

        [Fact]
        public void HostingStartupTypeCtorThrowsIfNull()
        {
            Assert.Throws<ArgumentNullException>(() => new HostingStartupAttribute(null));
        }

        [Fact]
        public void HostingStartupTypeCtorThrowsIfNotIHosting()
        {
            Assert.Throws<ArgumentException>(() => new HostingStartupAttribute(typeof(WebHostTests)));
        }

        private static void StaticConfigureMethod(IApplicationBuilder app)
        { }

        private IWebHostBuilder CreateWebHostBuilder()
        {
            var vals = new Dictionary<string, string>
            {
                { "DetailedErrors", "true" },
                { "captureStartupErrors", "true" }
            };
            var builder = new ConfigurationBuilder()
                .AddInMemoryCollection(vals);
            var config = builder.Build();
            return new WebHostBuilder().UseConfiguration(config);
        }

        private async Task AssertResponseContains(RequestDelegate app, string expectedText)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Response.Body = new MemoryStream();
            await app(httpContext);
            httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
            var bodyText = new StreamReader(httpContext.Response.Body).ReadToEnd();
            Assert.Contains(expectedText, bodyText);
        }

        private class TestServer : IServer
        {
            IFeatureCollection IServer.Features { get; }
            public RequestDelegate RequestDelegate { get; private set; }

            public void Dispose()
            {

            }

            public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken)
            {
                RequestDelegate = async ctx =>
                {
                    var httpContext = application.CreateContext(ctx.Features);
                    try
                    {
                        await application.ProcessRequestAsync(httpContext);
                    }
                    catch (Exception ex)
                    {
                        application.DisposeContext(httpContext, ex);
                        throw;
                    }
                    application.DisposeContext(httpContext, null);
                };

                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }
        }

        internal class StartupVerifyServiceA : IStartup
        {
            internal ServiceA ServiceA { get; set; }

            internal ServiceDescriptor ServiceADescriptor { get; set; }

            public IServiceProvider ConfigureServices(IServiceCollection services)
            {
                ServiceADescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(ServiceA));

                return services.BuildServiceProvider();
            }

            public void Configure(IApplicationBuilder app)
            {
                ServiceA = app.ApplicationServices.GetService<ServiceA>();
            }
        }

        public class TestHostingStartup : IHostingStartup
        {
            public void Configure(IWebHostBuilder builder)
            {
                var loggerProvider = new TestLoggerProvider();
                builder.UseSetting("testhostingstartup", "1")
                       .ConfigureServices(services => services.AddSingleton<ServiceA>())
                       .ConfigureServices(services => services.AddSingleton<ITestSink>(loggerProvider.Sink))
                       .ConfigureLogging(lf => lf.AddProvider(loggerProvider));
            }
        }

        public class TestLoggerProvider : ILoggerProvider
        {
            public TestSink Sink { get; set; } = new TestSink();

            public ILogger CreateLogger(string categoryName)
            {
                return new TestLogger(categoryName, Sink, enabled: true);
            }

            public void Dispose()
            {

            }
        }

        private class ServiceC
        {
            public ServiceC(ServiceD serviceD)
            {

            }
        }

        internal class ServiceD
        {

        }

        internal class ServiceA
        {

        }

        internal class ServiceB
        {

        }

        private class DisposableLoggerFactory : ILoggerFactory
        {
            public void Dispose()
            {
                Disposed = true;
            }

            public bool Disposed { get; set; }

            public ILogger CreateLogger(string categoryName)
            {
                return NullLogger.Instance;
            }

            public void AddProvider(ILoggerProvider provider)
            {
            }
        }
    }
}
