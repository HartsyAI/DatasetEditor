using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DatasetStudio.APIBackend.Models;
using DatasetStudio.APIBackend.Services.DatasetManagement;
using DatasetStudio.APIBackend.Services.Integration;
using DatasetStudio.DTO.Datasets;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
using SkiaSharp;
using Xunit;
using IDatasetItemRepository = DatasetStudio.Core.Abstractions.Repositories.IDatasetItemRepository;
using IDatasetRepository = DatasetStudio.Core.Abstractions.Repositories.IDatasetRepository;

namespace DatasetStudio.Tests.APIBackend
{
    /// <summary>
    /// PR 3.2 (audit I-6, I-8, I-10, H-4, F-3): Parquet files ingest (including HuggingFace's
    /// embedded images), ZIPs of data files ingest, a failed HuggingFace download is cleaned up,
    /// oversized downloads are refused before fetching, and folder imports record their source,
    /// real dimensions and no absolute paths.
    /// </summary>
    public sealed class FormatIngestionTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "DatasetStudioTests", "formats", Guid.NewGuid().ToString("N"));
        private readonly Guid _datasetId = Guid.NewGuid();
        private readonly Mock<IDatasetRepository> _datasets = new();
        private readonly Mock<IDatasetItemRepository> _items = new();
        private readonly Mock<IHuggingFaceClient> _huggingFace = new();
        private readonly List<DatasetItemDto> _written = new();
        private DatasetDto _dataset;

        public FormatIngestionTests()
        {
            Directory.CreateDirectory(_root);
            _dataset = new DatasetDto { Id = _datasetId, Name = "formats" };
            _datasets.Setup(r => r.GetByIdAsync(_datasetId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _dataset);
            _datasets.Setup(r => r.UpdateAsync(It.IsAny<DatasetDto>(), It.IsAny<CancellationToken>()))
                .Callback((DatasetDto d, CancellationToken _) => _dataset = d)
                .Returns(Task.CompletedTask);
            _items.Setup(r => r.InsertItemsAsync(_datasetId, It.IsAny<IEnumerable<DatasetItemDto>>(), It.IsAny<CancellationToken>()))
                .Callback((Guid _, IEnumerable<DatasetItemDto> batch, CancellationToken _) => _written.AddRange(batch))
                .Returns(Task.CompletedTask);
            _items.Setup(r => r.GetCountAsync(_datasetId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _written.Count);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        private string UploadPath => Path.Combine(_root, "uploads");

        private string DatasetRootPath => Path.Combine(_root, "datasets");

        private DatasetIngestionService CreateService(long? maxDownloadBytes = null)
        {
            Dictionary<string, string?> settings = new()
            {
                ["Storage:UploadPath"] = UploadPath,
                ["Storage:DatasetRootPath"] = DatasetRootPath,
            };
            if (maxDownloadBytes is long limit)
            {
                settings["Ingestion:MaxDownloadBytes"] = limit.ToString();
            }
            return new DatasetIngestionService(
                _datasets.Object,
                _items.Object,
                _huggingFace.Object,
                new Mock<IHuggingFaceDatasetServerClient>().Object,
                new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
                NullLogger<DatasetIngestionService>.Instance);
        }

        private static byte[] Png(int width, int height)
        {
            using SKBitmap bitmap = new(width, height);
            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }

        private static async Task WriteParquetAsync(string path, ParquetSchema schema, params Array[] columns)
        {
            await using FileStream file = File.Create(path);
            using ParquetWriter writer = await ParquetWriter.CreateAsync(schema, file);
            using ParquetRowGroupWriter group = writer.CreateRowGroup();
            DataField[] fields = schema.GetDataFields();
            for (int i = 0; i < fields.Length; i++)
            {
                await group.WriteColumnAsync(new DataColumn(fields[i], columns[i]));
            }
        }

        /// <summary>A flat Parquet file maps like a CSV with the same columns. It used to be NotSupportedException.</summary>
        [Fact]
        public async Task Upload_Parquet_IngestsRows()
        {
            string path = Path.Combine(_root, "rows.parquet");
            ParquetSchema schema = new(
                new DataField<string>("title"),
                new DataField<string>("image_url"),
                new DataField<int>("width"),
                new DataField<long>("downloads"));
            await WriteParquetAsync(path, schema,
                new[] { "first", "second" },
                new[] { "https://example.com/1.jpg", "https://example.com/2.jpg" },
                new[] { 640, 800 },
                new[] { 10L, 20L });

            await CreateService().StartIngestionAsync(_datasetId, path, CancellationToken.None);

            _written.Select(i => i.Title).Should().Equal("first", "second");
            _written[0].ImageUrl.Should().Be("https://example.com/1.jpg");
            _written[1].Width.Should().Be(800);
            _written[1].Metadata.Should().ContainKey("downloads").WhoseValue.Should().Be("20");
            _datasets.Verify(r => r.UpdateStatusAsync(_datasetId, IngestionStatusDto.Completed, null, It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// HuggingFace image datasets store each image inline as <c>image: {bytes, path}</c>. The bytes
        /// are written to the dataset folder and served from there, with real dimensions.
        /// </summary>
        [Fact]
        public async Task Upload_ParquetWithEmbeddedImages_WritesAndServesThem()
        {
            string path = Path.Combine(_root, "train-00000-of-00001.parquet");
            ParquetSchema schema = new(
                new DataField<string>("text"),
                new StructField("image", new DataField<byte[]>("bytes"), new DataField<string>("path")));
            await WriteParquetAsync(path, schema,
                new[] { "a red square", "a blue square" },
                new[] { Png(4, 3), Png(2, 5) },
                new[] { "red.png", "blue.png" });

            await CreateService().StartIngestionAsync(_datasetId, path, CancellationToken.None);

            _written.Should().HaveCount(2);
            DatasetItemDto first = _written[0];
            first.Title.Should().Be("a red square");
            first.ImageUrl.Should().StartWith($"/api/datasets/{_datasetId}/files/images/").And.EndWith(".png");
            first.ThumbnailUrl.Should().Be(first.ImageUrl);
            (first.Width, first.Height).Should().Be((4, 3));
            first.Metadata["image_file_name"].Should().Be("red.png");
            Directory.EnumerateFiles(DatasetRootPath, "*.png", SearchOption.AllDirectories).Should().HaveCount(2);
        }

        /// <summary>Audit I-6: a ZIP of CSV files used to ingest 0 items and fail.</summary>
        [Fact]
        public async Task Upload_ZipOfCsvFiles_IngestsEveryRow()
        {
            string path = Path.Combine(_root, "tables.zip");
            using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                await WriteEntryAsync(zip, "part-1.csv", "title,image_url\na,https://example.com/a.jpg\nb,https://example.com/b.jpg\n");
                await WriteEntryAsync(zip, "nested/part-2.csv", "title,image_url\nc,https://example.com/c.jpg\n");
            }

            await CreateService().StartIngestionAsync(_datasetId, path, CancellationToken.None);

            _written.Select(i => i.Title).Should().BeEquivalentTo(new[] { "a", "b", "c" });
        }

        /// <summary>
        /// HuggingFace imagefolder layout: images plus metadata.jsonl naming them by file_name. Each
        /// row is linked to its image, and a described image isn't listed a second time.
        /// </summary>
        [Fact]
        public async Task Upload_ZipImageFolderWithMetadata_LinksRowsToImages()
        {
            string path = Path.Combine(_root, "imagefolder.zip");
            using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                await WriteEntryAsync(zip, "train/0001.png", Png(6, 4));
                await WriteEntryAsync(zip, "train/0002.png", Png(2, 2));
                await WriteEntryAsync(zip, "train/extra.png", Png(1, 1));
                await WriteEntryAsync(zip, "train/metadata.jsonl",
                    "{\"file_name\": \"0001.png\", \"text\": \"first caption\"}\n{\"file_name\": \"0002.png\", \"text\": \"second caption\"}\n");
            }

            await CreateService().StartIngestionAsync(_datasetId, path, CancellationToken.None);

            _written.Should().HaveCount(3, "two described images plus one image with no row");
            DatasetItemDto first = _written.Single(i => i.Title == "first caption");
            first.ImageUrl.Should().Be($"/api/datasets/{_datasetId}/files/train/0001.png");
            (first.Width, first.Height).Should().Be((6, 4));
            _written.Single(i => i.Title == "extra").ImageUrl.Should().EndWith("train/extra.png");
        }

        /// <summary>
        /// Codex review on #20: A.png and a.png are different images on a case-sensitive
        /// filesystem. Each row gets its own image and neither is dropped.
        /// </summary>
        [Fact]
        public async Task Upload_ZipWithImagesDifferingOnlyInCase_KeepsThemApart()
        {
            string probe = Path.Combine(_root, "CaseProbe");
            await File.WriteAllTextAsync(probe, "x");
            if (File.Exists(Path.Combine(_root, "caseprobe")))
            {
                return; // case-insensitive filesystem (Windows, macOS): the two names are one file there
            }

            string path = Path.Combine(_root, "case.zip");
            using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                await WriteEntryAsync(zip, "A.png", Png(3, 3));
                await WriteEntryAsync(zip, "a.png", Png(5, 5));
                await WriteEntryAsync(zip, "metadata.jsonl",
                    "{\"file_name\": \"a.png\", \"text\": \"lower\"}\n{\"file_name\": \"A.png\", \"text\": \"upper\"}\n");
            }

            await CreateService().StartIngestionAsync(_datasetId, path, CancellationToken.None);

            _written.Should().HaveCount(2, "two rows, each describing its own image");
            _written.Single(i => i.Title == "lower").ImageUrl.Should().EndWith("/a.png");
            _written.Single(i => i.Title == "upper").ImageUrl.Should().EndWith("/A.png");
            (_written.Single(i => i.Title == "lower").Width).Should().Be(5);
        }

        /// <summary>Codex review on #20: "../images/a.png" from a data file in a sibling folder links to images/a.png.</summary>
        [Fact]
        public async Task Upload_ZipRowWithDotDotPath_LinksToTheImage()
        {
            string path = Path.Combine(_root, "dotdot.zip");
            using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                await WriteEntryAsync(zip, "images/a.png", Png(4, 4));
                await WriteEntryAsync(zip, "metadata/train.csv", "title,image\ncaptioned,../images/a.png\n");
            }

            await CreateService().StartIngestionAsync(_datasetId, path, CancellationToken.None);

            DatasetItemDto item = _written.Should().ContainSingle("the row describes the only image").Subject;
            item.Title.Should().Be("captioned");
            item.ImageUrl.Should().Be($"/api/datasets/{_datasetId}/files/images/a.png");
        }

        /// <summary>
        /// Codex review on #20: a Parquet image struct with no bytes and only a path refers to an
        /// image shipped beside it in the ZIP; the row links to that image.
        /// </summary>
        [Fact]
        public async Task Upload_ZipWithPathOnlyParquetImages_LinksRowsToTheImages()
        {
            string parquet = Path.Combine(_root, "refs.parquet");
            ParquetSchema schema = new(
                new DataField<string>("text"),
                new StructField("image", new DataField<byte[]>("bytes"), new DataField<string>("path")));
            await WriteParquetAsync(parquet, schema,
                new[] { "first" },
                new byte[]?[] { null },
                new[] { "images/a.png" });

            string path = Path.Combine(_root, "parquet-refs.zip");
            using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                await WriteEntryAsync(zip, "images/a.png", Png(6, 2));
                await WriteEntryAsync(zip, "data.parquet", await File.ReadAllBytesAsync(parquet));
            }

            await CreateService().StartIngestionAsync(_datasetId, path, CancellationToken.None);

            DatasetItemDto item = _written.Should().ContainSingle().Subject;
            item.Title.Should().Be("first");
            item.ImageUrl.Should().Be($"/api/datasets/{_datasetId}/files/images/a.png");
            (item.Width, item.Height).Should().Be((6, 2));
        }

        [Theory]
        [InlineData("a/./b/../c.png", "a/c.png")]
        [InlineData("metadata/../images/a.png", "images/a.png")]
        [InlineData("../outside.png", null)]
        [InlineData("a/../../outside.png", null)]
        public void NormalizeArchivePath_ResolvesDotSegmentsAndRefusesToLeaveTheArchive(string input, string? expected)
        {
            DatasetIngestionService.NormalizeArchivePath(input).Should().Be(expected);
        }

        /// <summary>Audit I-8: a download that fails to parse is deleted, not left in the upload folder.</summary>
        [Fact]
        public async Task HuggingFaceDownload_ParseFailure_DeletesTheDownloadedFile()
        {
            SetUpRepository(new HuggingFaceDatasetFile { Path = "data/train.csv", Size = 10 });
            _huggingFace
                .Setup(c => c.DownloadFileAsync("org/ds", "data/train.csv", It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
                .Returns((string _, string _, string destination, string? _, string? _, long? _, CancellationToken _) =>
                    File.WriteAllTextAsync(destination, "title\n")); // header only: "No items were parsed"

            Func<Task> act = () => CreateService().ImportFromHuggingFaceAsync(_datasetId, new ImportHuggingFaceDatasetRequest { Repository = "org/ds" });

            await act.Should().ThrowAsync<InvalidOperationException>();
            Directory.EnumerateFiles(UploadPath).Should().BeEmpty("the failed download must not be left on disk");
        }

        /// <summary>Audit H-4: a file the tree lists as over the limit is refused before any of it is fetched.</summary>
        [Fact]
        public async Task HuggingFaceDownload_FileOverLimit_IsRefusedBeforeDownloading()
        {
            SetUpRepository(new HuggingFaceDatasetFile { Path = "data/train.parquet", Size = 5_000 });

            Func<Task> act = () => CreateService(maxDownloadBytes: 1_000)
                .ImportFromHuggingFaceAsync(_datasetId, new ImportHuggingFaceDatasetRequest { Repository = "org/ds" });

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*download limit*");
            _huggingFace.Verify(c => c.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>Download mode picks a parseable file in a nested folder, Parquet and train split first, and passes the limit on.</summary>
        [Fact]
        public void PickDownloadFile_PrefersParquetTrainSplit()
        {
            HuggingFaceDatasetFile? picked = DatasetIngestionService.PickDownloadFile(new[]
            {
                new HuggingFaceDatasetFile { Path = "README.md" },
                new HuggingFaceDatasetFile { Path = "data/test-00000.parquet" },
                new HuggingFaceDatasetFile { Path = "data/train.csv" },
                new HuggingFaceDatasetFile { Path = "data/train-00000.parquet" },
            });

            picked!.Path.Should().Be("data/train-00000.parquet");
        }

        /// <summary>
        /// Audit I-10, F-3: a folder import is recorded as a server folder, items carry real
        /// dimensions and only relative paths, and TIFF files (which nothing serves as images) are skipped.
        /// </summary>
        [Fact]
        public async Task FolderImport_SetsSourceType_ReadsDimensions_AndKeepsPathsRelative()
        {
            string folder = Path.Combine(_root, "photos");
            Directory.CreateDirectory(Path.Combine(folder, "sub"));
            await File.WriteAllBytesAsync(Path.Combine(folder, "sub", "wide.png"), Png(8, 2));
            await File.WriteAllBytesAsync(Path.Combine(folder, "scan.tif"), new byte[] { 0x49, 0x49, 0x2A, 0x00 });

            await CreateService().IngestFromFolderAsync(_datasetId, folder, CancellationToken.None);

            _dataset.SourceType.Should().Be(DatasetSourceType.LocalFolder);
            DatasetItemDto item = _written.Should().ContainSingle("the .tif file is not an image the app can serve").Subject;
            (item.Width, item.Height).Should().Be((8, 2));
            item.Metadata.Values.Should().NotContain(v => v.Contains(_root), "absolute server paths must not leak into item metadata");
            item.Metadata["relative_path"].Should().Be(Path.Combine("sub", "wide.png"));
        }

        private void SetUpRepository(params HuggingFaceDatasetFile[] files)
        {
            _huggingFace
                .Setup(c => c.GetDatasetInfoAsync("org/ds", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HuggingFaceDatasetInfo { Id = "org/ds", Files = files.ToList() });
        }

        private static async Task WriteEntryAsync(ZipArchive zip, string name, string content)
        {
            await WriteEntryAsync(zip, name, Encoding.UTF8.GetBytes(content));
        }

        private static async Task WriteEntryAsync(ZipArchive zip, string name, byte[] content)
        {
            ZipArchiveEntry entry = zip.CreateEntry(name);
            await using Stream stream = entry.Open();
            await stream.WriteAsync(content);
        }
    }
}
