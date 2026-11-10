using System;
using DatasetStudio.Extensions.SDK;
using FluentAssertions;
using Xunit;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// The manifest is one of the three pieces of the extension system plan item 6.3 kept. The id
    /// becomes the API assembly's file name and a URL segment, so it is validated as both.
    /// </summary>
    public sealed class ExtensionManifestTests
    {
        private static string ManifestJson(string id) =>
            "{ \"schemaVersion\": 1, \"metadata\": { \"id\": \"" + id + "\", \"name\": \"N\", \"version\": \"1.0.0\" }, \"deploymentTarget\": \"Api\" }";

        [Theory]
        [InlineData("../Escape")]
        [InlineData("a/b")]
        [InlineData("a\\\\b")]
        [InlineData(".hidden")]
        [InlineData("has space")]
        public void LoadFromJson_IdThatIsNotSafeAsAFileNameAndUrlSegment_IsRejected(string id)
        {
            // Before, any non-blank id was accepted, and the loader built its probe path as
            // Path.Combine(extensionDirectory, $"{id}.Api.dll"), so "../Escape" probed outside the
            // extension's folder.
            Action load = () => ExtensionManifest.LoadFromJson(ManifestJson(id));

            load.Should().Throw<InvalidOperationException>().WithMessage("*Metadata.Id*");
        }

        [Theory]
        [InlineData("CoreViewer")]
        [InlineData("my-extension.v2_beta")]
        public void LoadFromJson_OrdinaryId_IsAccepted(string id)
        {
            ExtensionManifest manifest = ExtensionManifest.LoadFromJson(ManifestJson(id));

            manifest.Metadata.Id.Should().Be(id);
        }

        [Fact]
        public void LoadFromJson_ManifestWithRemovedFields_StillLoads()
        {
            // requiredPermissions and apiEndpoints were removed from the model (audit X-3, X-5);
            // a manifest written for the old schema must keep loading, with the fields ignored.
            const string json = """
                {
                  "schemaVersion": 1,
                  "metadata": { "id": "Old", "name": "Old", "version": "1.0.0", "tags": ["x"] },
                  "deploymentTarget": "Both",
                  "requiredPermissions": ["datasets.read"],
                  "apiEndpoints": [{ "method": "GET", "route": "/x", "handlerType": "X" }]
                }
                """;

            ExtensionManifest manifest = ExtensionManifest.LoadFromJson(json);

            manifest.Metadata.Id.Should().Be("Old");
            manifest.DeploymentTarget.Should().Be(ExtensionDeploymentTarget.Both);
        }
    }
}
