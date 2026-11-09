using System;
using System.Net;
using System.Net.Http;
using DatasetStudio.ClientApp.Features.Datasets.Services;
using Xunit;

namespace DatasetStudio.Tests.ClientApp;

/// <summary>
/// PR 2.2 (audit U-10): the viewer reports a failed load once, inline, with a message that says
/// what went wrong. It used to show "Failed to load dataset from API" for every cause, plus a
/// second snackbar for the same failure.
/// </summary>
public sealed class DatasetLoadFailureTests
{
    /// <summary>A dataset id the API doesn't return reads as missing or forbidden, not as an outage.</summary>
    [Fact]
    public void MissingDataset_SaysItDoesNotExistOrIsNotAccessible()
    {
        string message = DatasetCacheService.DescribeLoadFailure(new InvalidOperationException("Dataset not found on server."));

        Assert.Equal("This dataset doesn't exist, or you don't have access to it.", message);
    }

    /// <summary>
    /// The API answers 404 for an unknown or hidden id; GetFromJsonAsync surfaces that as an
    /// HttpRequestException, which must not be mistaken for an outage (found in the browser check).
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void NotFoundOrForbiddenResponse_SaysItDoesNotExistOrIsNotAccessible(HttpStatusCode status)
    {
        string message = DatasetCacheService.DescribeLoadFailure(new HttpRequestException("status", null, status));

        Assert.Equal("This dataset doesn't exist, or you don't have access to it.", message);
    }

    /// <summary>Other HTTP errors name the status code so the user can report it.</summary>
    [Fact]
    public void ServerErrorResponse_NamesTheStatusCode()
    {
        string message = DatasetCacheService.DescribeLoadFailure(new HttpRequestException("status", null, HttpStatusCode.InternalServerError));

        Assert.Equal("The server couldn't load this dataset (HTTP 500). Retry, or check the API logs.", message);
    }

    /// <summary>An unreachable API tells the user to check the server, then retry.</summary>
    [Fact]
    public void UnreachableServer_SaysTheServerDidNotRespond()
    {
        string message = DatasetCacheService.DescribeLoadFailure(new HttpRequestException("Failed to fetch"));

        Assert.Equal("The server didn't respond. Check that the API is running, then retry.", message);
    }

    /// <summary>Anything else gets a neutral sentence rather than a raw exception message.</summary>
    [Fact]
    public void OtherFailure_GetsANeutralMessage()
    {
        Assert.Equal("The dataset couldn't be loaded.", DatasetCacheService.DescribeLoadFailure(new FormatException("bad json")));
    }
}
