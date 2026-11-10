using System.Collections.Concurrent;
using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace DatasetStudio.APIBackend.Services.Storage;

public sealed class S3StorageService : IS3StorageService, IDisposable
{
    private readonly IS3StorageProfileResolver _profileResolver;
    private readonly ConcurrentDictionary<string, AmazonS3Client> _clientsByProfile = new(StringComparer.OrdinalIgnoreCase);

    public S3StorageService(IS3StorageProfileResolver profileResolver)
    {
        _profileResolver = profileResolver;
    }

    public async Task<S3ObjectContent?> GetObjectAsync(
        string profileName, string bucket, string key, CancellationToken cancellationToken)
    {
        AmazonS3Client client = GetOrCreateClient(profileName);
        try
        {
            GetObjectResponse response = await client.GetObjectAsync(
                new GetObjectRequest { BucketName = bucket, Key = key }, cancellationToken);
            // The caller disposes the response through S3ObjectContent (audit S-4).
            return new S3ObjectContent(response.ResponseStream, response.ContentLength, response);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<S3ListPage> ListObjectsAsync(
        string profileName, string bucket, string? prefix, string? continuationToken, int maxKeys, CancellationToken cancellationToken)
    {
        AmazonS3Client client = GetOrCreateClient(profileName);
        ListObjectsV2Response response = await client.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = bucket,
            Prefix = prefix,
            ContinuationToken = continuationToken,
            MaxKeys = maxKeys,
        }, cancellationToken);

        // S3Objects is null (not an empty list) for a zero-match listing against MinIO --
        // confirmed live, real AWS S3's own SDK docs don't guarantee non-null either. A plain
        // .Select() on that threw ArgumentNullException, surfacing as a very confusing
        // "Could not reach bucket" 400 on ImportFromS3 for any prefix with zero objects yet.
        List<string> keys = response.S3Objects?.Select(o => o.Key).ToList() ?? new List<string>();
        return new S3ListPage(keys, response.IsTruncated == true ? response.NextContinuationToken : null);
    }

    private AmazonS3Client GetOrCreateClient(string profileName)
    {
        return _clientsByProfile.GetOrAdd(profileName, name =>
        {
            S3StorageProfileConfig profile = _profileResolver.GetProfile(name)
                ?? throw new InvalidOperationException($"S3 storage profile '{name}' is not configured.");

            AmazonS3Config config = new()
            {
                ForcePathStyle = profile.UsePathStyle,
                AuthenticationRegion = profile.Region,
                // AWS SDK v4 defaults to always computing/sending a flexible-checksum header
                // (x-amz-content-sha256 etc.). Real AWS S3 handles this fine, but MinIO's SigV4
                // implementation rejects it outright ("the provided header does not match what
                // was computed") -- confirmed against a real MinIO container before writing
                // this. WHEN_REQUIRED restores the v3-era behavior (only send/verify when the
                // operation actually requires it), which both real S3 and MinIO handle.
                RequestChecksumCalculation = Amazon.Runtime.RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = Amazon.Runtime.ResponseChecksumValidation.WHEN_REQUIRED,
            };
            if (!string.IsNullOrWhiteSpace(profile.ServiceUrl))
            {
                config.ServiceURL = profile.ServiceUrl;
            }

            BasicAWSCredentials credentials = new(profile.AccessKey, profile.SecretKey);
            return new AmazonS3Client(credentials, config);
        });
    }

    public void Dispose()
    {
        foreach (AmazonS3Client client in _clientsByProfile.Values)
        {
            client.Dispose();
        }
    }
}
