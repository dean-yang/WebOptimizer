﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using NUglify.Css;
using NUglify.JavaScript;

namespace WebOptimizer
{
    internal class AssetPipeline : IAssetPipeline
    {
        private List<IAsset> _assets = new List<IAsset>();

        public bool? EnableTagHelperBundling { get; set; }

        /// <summary>
        /// Gets a list of transforms added.
        /// </summary>
        public IReadOnlyList<IAsset> Assets => _assets;

        public IFileProvider FileProvider { get; set; }

        /// <summary>
        /// Gets the <see cref="IAsset" /> from the specified route.
        /// </summary>
        /// <param name="route">The route to find the asset by.</param>
        /// <param name="asset">The asset matching the route.</param>
        public bool TryFromRoute(string route, out IAsset asset)
        {
            asset = null;
            string cleanRoute = route.TrimStart('~');

            foreach (IAsset existing in Assets)
            {
                if (existing.Route.Equals(cleanRoute, StringComparison.OrdinalIgnoreCase))
                {
                    asset = existing;
                    return true;
                }
            }

            foreach (IAsset existing in Assets.Where(a => a.Route[0] == '.'))
            {
                if (route.EndsWith(existing.Route, StringComparison.OrdinalIgnoreCase))
                {
                    asset = Asset.Create(cleanRoute, existing.ContentType, new[] { cleanRoute });

                    foreach (IProcessor processor in existing.Processors)
                    {
                        asset.Processors.Add(processor);
                    }

                    _assets.Add(asset);
                    return true;
                }
            }

            return false;
        }

        public IAsset Add(IAsset asset)
        {
            return Add(asset.Route, asset.ContentType, asset.SourceFiles.ToArray());
        }

        public IEnumerable<IAsset> Add(IEnumerable<IAsset> assets)
        {
            var list = new List<IAsset>();

            foreach (IAsset asset in assets)
            {
                IAsset ass = Add(asset.Route, asset.ContentType, asset.SourceFiles.ToArray());
                list.Add(ass);
            }

            return list;
        }

        public IAsset Add(string route, string contentType, params string[] sourceFiles)
        {
            if (!route.StartsWith("/") && !route.StartsWith("."))
            {
                throw new ArgumentException($"The route \"{route}\" must start with a / or a .", nameof(route));
            }

            if (Assets.Any(a => a.Route.Equals(route, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException($"The route \"{route}\" was already specified", nameof(route));
            }

            string[] sources = sourceFiles;

            if (!route.StartsWith(".") && sourceFiles.Length == 0)
            {
                sources = new[] { route };
            }

            IAsset asset = Asset.Create(route, contentType, sources);
            _assets.Add(asset);

            return asset;
        }
    }

    /// <summary>
    /// Extension methods for <see cref="IAssetPipeline"/>.
    /// </summary>
    public static partial class PipelineExtensions
    {
        /// <summary>
        /// Adds WebOptimizer to the specified <see cref="IServiceCollection"/>.
        /// </summary>
        public static void AddWebOptimizer(this IServiceCollection services, Action<IAssetPipeline> assetPipeline)
        {
            var pipeline = new AssetPipeline();
            assetPipeline(pipeline);

            services.AddSingleton<IAssetPipeline, AssetPipeline>(factory => pipeline);
        }

        /// <summary>
        /// Ensures that defaults are set
        /// </summary>
        public static void EnsureDefaults(this IAssetPipeline pipeline, IHostingEnvironment env)
        {
            pipeline.FileProvider = pipeline.FileProvider ?? env.WebRootFileProvider;
            pipeline.EnableTagHelperBundling = pipeline.EnableTagHelperBundling ?? !env.IsDevelopment();
        }

        /// <summary>
        /// Dynamically adds all requested .css files to the pipeline.
        /// </summary>
        public static IAsset AddJs(this IAssetPipeline pipeline)
        {
            return pipeline.AddJs(new CodeSettings());
        }

        /// <summary>
        /// Dynamically adds all requested .css files to the pipeline.
        /// </summary>
        public static IAsset AddJs(this IAssetPipeline pipeline, CodeSettings settings)
        {
            return pipeline.AddFileExtension(".js", "application/javascript")
                           .MinifyJavaScript(settings);
        }

        /// <summary>
        /// Creates a JavaScript bundle on the specified route and minifies the output.
        /// </summary>
        public static IAsset AddJs(this IAssetPipeline pipeline, string route, params string[] sourceFiles)
        {
            return pipeline.AddJs(route, new CodeSettings(), sourceFiles);
        }

        /// <summary>
        /// Creates a JavaScript bundle on the specified route and minifies the output.
        /// </summary>
        public static IAsset AddJs(this IAssetPipeline pipeline, string route, CodeSettings settings, params string[] sourceFiles)
        {
            return pipeline.Add(route, "application/javascript", sourceFiles)
                           .Concatinate()
                           .MinifyJavaScript(settings);
        }

        /// <summary>
        /// Dynamically adds all requested .css files to the pipeline.
        /// </summary>
        public static IAsset AddCss(this IAssetPipeline pipeline)
        {
            return pipeline.AddCss(new CssSettings());
        }

        /// <summary>
        /// Dynamically adds all requested .css files to the pipeline.
        /// </summary>
        public static IAsset AddCss(this IAssetPipeline pipeline, CssSettings settings)
        {
            return pipeline.AddFileExtension(".css", "text/css")
                           .CssFingerprint()
                           .MinifyCss(settings);
        }

        /// <summary>
        /// Creates a CSS bundle on the specified route and minifies the output.
        /// </summary>
        public static IAsset AddCss(this IAssetPipeline pipeline, string route, params string[] sourceFiles)
        {
            return pipeline.AddCss(route, new CssSettings(), sourceFiles);
        }

        /// <summary>
        /// Creates a CSS bundle on the specified route and minifies the output.
        /// </summary>
        public static IAsset AddCss(this IAssetPipeline pipeline, string route, CssSettings settings, params string[] sourceFiles)
        {
            return pipeline.Add(route, "text/css", sourceFiles)
                           .AdjustRelativePaths()
                           .Concatinate()
                           .CssFingerprint()
                           .MinifyCss(settings);
        }

        /// <summary>
        /// Adds loose files to the optimization pipeline.
        /// </summary>
        /// <param name="pipeline">The pipeline object.</param>
        /// <param name="contentType">The content type of the response. Example: "text/css".</param>
        /// <param name="sourceFiles">A list of relative file names of the sources to optimize.</param>
        public static IEnumerable<IAsset> AddFiles(this IAssetPipeline pipeline, string contentType, params string[] sourceFiles)
        {
            var list = new List<IAsset>();

            foreach (string file in sourceFiles)
            {
                IAsset asset = pipeline.Add(file, contentType, file);
                asset.Processors.Add(new Concatenator());
                list.Add(asset);
            }

            return list;
        }

        /// <summary>
        /// Adds the file extension.
        /// </summary>
        /// <param name="pipeline">The pipeline.</param>
        /// <param name="extension">The extension to use. Example: .css or .js</param>
        /// <param name="contentType">The content type of the response. Example: text/css or application/javascript.</param>
        public static IAsset AddFileExtension(this IAssetPipeline pipeline, string extension, string contentType)
        {
            IAsset asset = Asset.Create(extension, contentType, Enumerable.Empty<string>());
            return pipeline.Add(asset);
        }
    }
}
