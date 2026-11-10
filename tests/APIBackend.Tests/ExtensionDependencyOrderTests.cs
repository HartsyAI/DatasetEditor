using System.Collections.Generic;
using System.Linq;
using DatasetStudio.APIBackend.Services.Extensions;
using DatasetStudio.Extensions.SDK;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// Load order of extensions that depend on each other (audit X-4).
    /// </summary>
    public sealed class ExtensionDependencyOrderTests
    {
        private static ExtensionManifest Manifest(string id, params string[] dependencies) => new()
        {
            Metadata = new ExtensionMetadata { Id = id, Name = id, Version = "1.0.0" },
            DeploymentTarget = ExtensionDeploymentTarget.Api,
            Dependencies = dependencies.ToDictionary(d => d, _ => ">=1.0.0"),
        };

        private static List<string> Order(params ExtensionManifest[] manifests) =>
            ApiExtensionLoader.OrderByDependencies(manifests, NullLogger.Instance)
                .Select(m => m.Metadata.Id)
                .ToList();

        [Fact]
        public void DependencyListedAfterItsDependent_IsLoadedFirst()
        {
            // The old Kahn's algorithm counted dependents instead of dependencies, so it emitted
            // dependents before the extensions they need.
            Order(Manifest("App", "Base"), Manifest("Base")).Should().Equal("Base", "App");
        }

        [Fact]
        public void Chain_IsLoadedFromTheBottomUp()
        {
            Order(Manifest("Top", "Middle"), Manifest("Middle", "Bottom"), Manifest("Bottom"))
                .Should().Equal("Bottom", "Middle", "Top");
        }

        [Fact]
        public void IndependentExtensions_KeepDiscoveryOrder()
        {
            Order(Manifest("B"), Manifest("A"), Manifest("C")).Should().Equal("B", "A", "C");
        }

        [Fact]
        public void MissingDependency_SkipsTheDependentAndItsDependents()
        {
            // Before, a missing dependency was only a warning and the dependent loaded anyway.
            Order(Manifest("App", "Missing"), Manifest("Plugin", "App"), Manifest("Other"))
                .Should().Equal("Other");
        }

        [Fact]
        public void Cycle_SkipsTheCycleWithoutStoppingTheOthers()
        {
            // Before, a cycle threw InvalidOperationException out of startup, so one bad
            // community manifest stopped the whole API.
            Order(Manifest("A", "B"), Manifest("B", "A"), Manifest("Fine"))
                .Should().Equal("Fine");
        }

        [Fact]
        public void DuplicateId_KeepsTheFirstDiscovered()
        {
            ExtensionManifest builtIn = Manifest("Same");
            ExtensionManifest community = Manifest("Same");

            ApiExtensionLoader.OrderByDependencies(new[] { builtIn, community }, NullLogger.Instance)
                .Should().ContainSingle().Which.Should().BeSameAs(builtIn);
        }
    }
}
