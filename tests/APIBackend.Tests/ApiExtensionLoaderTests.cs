using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using DatasetStudio.APIBackend.Services.Extensions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// Loads real extension assemblies: the SampleExtension test fixture (tests/ExtensionFixtures,
    /// built by the test project's build-order reference) and the built-in CoreViewer (built by the
    /// API's build-order reference). No database needed.
    /// </summary>
    public sealed class ApiExtensionLoaderTests : IDisposable
    {
        private const string SampleExtensionId = "SampleExtension";

        private readonly string _scratchDirectory =
            Path.Combine(Path.GetTempPath(), "DatasetStudioTests", "extensions", Guid.NewGuid().ToString("N"));

        /// <summary>The repository root, found by walking up from the test output folder.</summary>
        internal static string RepositoryRoot
        {
            get
            {
                DirectoryInfo? directory = new(AppContext.BaseDirectory);
                while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DatasetStudio.sln")))
                {
                    directory = directory.Parent;
                }

                return directory?.FullName ?? throw new InvalidOperationException("DatasetStudio.sln not found above the test output folder.");
            }
        }

        /// <summary>Folder holding the SampleExtension fixture, used as Extensions:BuiltInPath.</summary>
        internal static string FixturesPath => Path.Combine(RepositoryRoot, "tests", "ExtensionFixtures");

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_scratchDirectory))
                {
                    Directory.Delete(_scratchDirectory, recursive: true);
                }
            }
            catch (IOException)
            {
                // Loaded extension assemblies stay mapped until the process exits; a leftover temp
                // folder isn't worth failing the test over.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private ApiExtensionLoader CreateLoader(string? builtInPath, string contentRootPath, IDictionary<string, string?>? extraSettings = null)
        {
            Dictionary<string, string?> settings = new()
            {
                // A community folder that doesn't exist, so only the folder under test is scanned.
                ["Extensions:CommunityPath"] = Path.Combine(_scratchDirectory, "no-community"),
            };
            if (builtInPath is not null)
            {
                settings["Extensions:BuiltInPath"] = builtInPath;
            }
            if (extraSettings is not null)
            {
                foreach (KeyValuePair<string, string?> setting in extraSettings)
                {
                    settings[setting.Key] = setting.Value;
                }
            }

            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            return new ApiExtensionLoader(NullLogger<ApiExtensionLoader>.Instance, configuration, contentRootPath);
        }

        [Fact]
        public void LoadAll_ExtensionBuiltIntoItsProjectBinFolder_IsLoaded()
        {
            // Audit X-1: the probe list looked under {extension}/src/{Id}.Api/bin/... and
            // {extension}/bin/..., neither of which is where `dotnet build` puts the assembly
            // ({extension}/{Id}.Api/bin/{Configuration}/{tfm}/), so nothing loaded outside Docker.
            IReadOnlyList<LoadedApiExtension> loaded = CreateLoader(FixturesPath, AppContext.BaseDirectory).LoadAll();

            loaded.Should().ContainSingle(e => e.Manifest.Metadata.Id == SampleExtensionId)
                .Which.Instance.GetType().FullName.Should().Be("DatasetStudio.Tests.ExtensionFixtures.SampleExtension.SampleExtensionApi");
        }

        [Fact]
        public void LoadAll_DefaultFolders_LoadTheBuiltInCoreViewer()
        {
            // What `dotnet run --project src/APIBackend` does: content root src/APIBackend, default
            // built-in folder src/Extensions/BuiltIn, CoreViewer built into its own bin folder.
            string apiContentRoot = Path.Combine(RepositoryRoot, "src", "APIBackend");

            IReadOnlyList<LoadedApiExtension> loaded = CreateLoader(builtInPath: null, apiContentRoot).LoadAll();

            loaded.Select(e => e.Manifest.Metadata.Id).Should().Contain("CoreViewer");
        }

        [Fact]
        public void LoadAll_PublishedExtensionCopiedFlatIntoItsFolder_IsLoaded()
        {
            // The Dockerfile's layout: the extension's publish output sits flat next to its manifest.
            string builtIn = Path.Combine(_scratchDirectory, "BuiltIn");
            CopyFixtureFlat(Path.Combine(builtIn, SampleExtensionId));

            IReadOnlyList<LoadedApiExtension> loaded = CreateLoader(builtIn, AppContext.BaseDirectory).LoadAll();

            loaded.Should().ContainSingle(e => e.Manifest.Metadata.Id == SampleExtensionId);
        }

        [Fact]
        public void LoadedExtension_ResolvesItsPrivateDependencyFromItsOwnFolder()
        {
            // Audit X-2: the bare load context had no dependency resolver, so the extension's
            // first call into SampleExtension.Greeting (which only ships in the extension's folder)
            // threw FileNotFoundException.
            LoadedApiExtension sample = CreateLoader(FixturesPath, AppContext.BaseDirectory).LoadAll()
                .Single(e => e.Manifest.Metadata.Id == SampleExtensionId);
            ServiceCollection services = new();

            sample.Instance.ConfigureServices(services, new ConfigurationBuilder().Build());

            using ServiceProvider provider = services.BuildServiceProvider();
            provider.GetRequiredKeyedService<string>("SampleExtension.Greeting")
                .Should().Be("Hello, Dataset Studio, from SampleExtension.Greeting");
            // Loaded into the extension's own context, not into the API's.
            AssemblyLoadContext.Default.Assemblies.Select(a => a.GetName().Name).Should().NotContain("SampleExtension.Greeting");
            AssemblyLoadContext.GetLoadContext(sample.Instance.GetType().Assembly).Should().NotBe(AssemblyLoadContext.Default);
        }

        [Fact]
        public void LoadAll_ExtensionShippingItsOwnCopyOfTheSdk_UsesTheApisCopy()
        {
            // A plain `dotnet publish` of an extension copies Extensions.SDK.dll next to it (audit
            // R-10). If the extension's context loaded that copy, its IApiExtension would be a
            // different type from the API's and the loader would find no implementation.
            // Without a deps.json the dependency resolver offers every assembly in the folder,
            // so the SDK copy is resolvable and only the API-first rule keeps it out.
            string builtIn = Path.Combine(_scratchDirectory, "BuiltIn");
            string extensionDirectory = Path.Combine(builtIn, SampleExtensionId);
            CopyFixtureFlat(extensionDirectory);
            File.Delete(Path.Combine(extensionDirectory, $"{SampleExtensionId}.Api.deps.json"));
            string sdkPath = typeof(DatasetStudio.Extensions.SDK.IApiExtension).Assembly.Location;
            File.Copy(sdkPath, Path.Combine(extensionDirectory, Path.GetFileName(sdkPath)));

            IReadOnlyList<LoadedApiExtension> loaded = CreateLoader(builtIn, AppContext.BaseDirectory).LoadAll();

            loaded.Should().ContainSingle(e => e.Manifest.Metadata.Id == SampleExtensionId);
        }

        /// <summary>
        /// Copies the fixture's manifest and build output into <paramref name="extensionDirectory"/>
        /// the way <c>dotnet publish -o</c> lays an extension out.
        /// </summary>
        private static void CopyFixtureFlat(string extensionDirectory)
        {
            string fixtureDirectory = Path.Combine(FixturesPath, SampleExtensionId);
            Directory.CreateDirectory(extensionDirectory);
            File.Copy(Path.Combine(fixtureDirectory, "extension.manifest.json"), Path.Combine(extensionDirectory, "extension.manifest.json"));
            foreach (string file in Directory.GetFiles(FixtureBuildOutput()))
            {
                File.Copy(file, Path.Combine(extensionDirectory, Path.GetFileName(file)));
            }
        }

        /// <summary>The fixture's bin/{Configuration}/{tfm} folder, newest build first.</summary>
        private static string FixtureBuildOutput()
        {
            string bin = Path.Combine(FixturesPath, SampleExtensionId, $"{SampleExtensionId}.Api", "bin");
            return Directory.GetFiles(bin, $"{SampleExtensionId}.Api.dll", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Select(path => Path.GetDirectoryName(path)!)
                .First();
        }
    }
}
