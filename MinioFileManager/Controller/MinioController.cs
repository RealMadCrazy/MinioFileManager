using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Minio;
using Minio.ApiEndpoints;
using Minio.DataModel.Args;
using Minio.DataModel.Tags;
using Minio.Exceptions;
using MinioFileManager.Model;

namespace MinioFileManager.Controller
{
    /// <summary>
    /// A REST API controller for interacting with a MinIO server.
    /// Provides endpoints for common S3-style operations like
    /// uploading, downloading, copying, deleting, tagging,
    /// and generating presigned URLs.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class MinioController(IMinioClient minioClient, IOptions<MinioSettings> options) : ControllerBase
    {
        private readonly IMinioClient _minioClient = minioClient;
        private readonly MinioSettings _settings = options.Value;

        // ================================================================
        // UPLOAD FILE TO MINIO
        // ================================================================
        /// <summary>
        /// Uploads a file to MinIO.
        /// Automatically creates the target bucket if it does not exist.
        /// </summary>
        [HttpPost("Upload")]
        public async Task<IActionResult> Upload(IFormFile file, [FromQuery] string? bucketName = null)
        {
            if (file is null || file.Length == 0)
                return BadRequest("File is empty");

            bucketName ??= _settings.BucketName;
            var safeFileName = Path.GetFileName(file.FileName);

            try
            {
                // Ensure the target bucket exists before uploading
                bool bucketExists = await _minioClient.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucketName));
                if (!bucketExists)
                    await _minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucketName));

                await using var stream = file.OpenReadStream();

                // Upload the file to MinIO
                await _minioClient.PutObjectAsync(new PutObjectArgs()
                    .WithBucket(bucketName)
                    .WithObject(safeFileName)
                    .WithStreamData(stream)
                    .WithObjectSize(file.Length)
                    .WithContentType(file.ContentType));

                return Ok(new { file = safeFileName, bucket = bucketName, status = "uploaded" });
            }
            catch (MinioException ex)
            {
                return StatusCode(500, $"MinIO error: {ex.Message}");
            }
        }

        // ================================================================
        // DOWNLOAD FILE FROM MINIO
        // ================================================================
        /// <summary>
        /// Downloads a file from MinIO as a stream.
        /// Validates bucket and object existence before returning.
        /// </summary>
        [HttpGet("Download/{fileName}")]
        public async Task<IActionResult> Download(string fileName, [FromQuery] string? bucketName = null)
        {
            bucketName ??= _settings.BucketName;
            var safeFileName = Path.GetFileName(fileName);

            try
            {
                // Check if the bucket exists
                bool bucketExists = await _minioClient.BucketExistsAsync(
                    new BucketExistsArgs().WithBucket(bucketName));
                if (!bucketExists)
                    return NotFound($"Bucket '{bucketName}' does not exist");

                // Check if the file exists
                try
                {
                    await _minioClient.StatObjectAsync(
                        new StatObjectArgs().WithBucket(bucketName).WithObject(safeFileName));
                }
                catch (ObjectNotFoundException)
                {
                    return NotFound($"File '{safeFileName}' not found in bucket '{bucketName}'");
                }

                // Download the file
                var memoryStream = new MemoryStream();
                await _minioClient.GetObjectAsync(new GetObjectArgs()
                    .WithBucket(bucketName)
                    .WithObject(safeFileName)
                    .WithCallbackStream(stream => stream.CopyTo(memoryStream)));

                memoryStream.Position = 0;
                return File(memoryStream, "application/octet-stream", safeFileName);
            }
            catch (MinioException ex)
            {
                return StatusCode(500, $"MinIO error: {ex.Message}");
            }
        }

        // ================================================================
        // LIST ALL BUCKETS AND OBJECTS
        // ================================================================
        /// <summary>
        /// Lists all buckets and the objects they contain.
        /// Supports optional prefix filtering.
        /// </summary>
        [HttpGet("List")]
        public async Task<IActionResult> List([FromQuery] string? prefix = null)
        {
            try
            {
                var buckets = await _minioClient.ListBucketsAsync();
                var result = new List<object>();

                foreach (var bucket in buckets.Buckets)
                {
                    var objects = new List<string>();

                    // MinIO v6+ uses IObservable<Item> for listing
                    var observable = _minioClient.ListObjectsAsync(
                        new ListObjectsArgs()
                            .WithBucket(bucket.Name)
                            .WithRecursive(true)
                            .WithPrefix(prefix ?? string.Empty));

                    var tcs = new TaskCompletionSource();
                    var subscription = observable.Subscribe(
                        item => objects.Add(item.Key),
                        ex => tcs.SetException(ex),
                        () => tcs.SetResult());

                    await tcs.Task;
                    subscription.Dispose();

                    result.Add(new
                    {
                        bucket = bucket.Name,
                        created = bucket.CreationDate,
                        count = objects.Count,
                        objects
                    });
                }

                return Ok(result);
            }
            catch (MinioException ex)
            {
                return StatusCode(500, $"MinIO error: {ex.Message}");
            }
        }

        // ================================================================
        // DELETE SINGLE OBJECT
        // ================================================================
        /// <summary>
        /// Deletes a single object from a bucket.
        /// </summary>
        [HttpDelete("Delete/{fileName}")]
        public async Task<IActionResult> Delete(string fileName, [FromQuery] string? bucketName = null)
        {
            bucketName ??= _settings.BucketName;
            var safeFileName = Path.GetFileName(fileName);

            try
            {
                // Remove file
                await _minioClient.RemoveObjectAsync(new RemoveObjectArgs()
                    .WithBucket(bucketName)
                    .WithObject(safeFileName));

                return Ok(new { file = safeFileName, bucket = bucketName, status = "deleted" });
            }
            catch (MinioException ex)
            {
                return StatusCode(500, $"MinIO error: {ex.Message}");
            }
        }

        // ================================================================
        // BULK DELETE MULTIPLE OBJECTS
        // ================================================================
        /// <summary>
        /// Deletes multiple files in a single request.
        /// </summary>
        [HttpDelete("BulkDelete")]
        public async Task<IActionResult> BulkDelete([FromQuery] string? bucketName, [FromBody] List<string> files)
        {
            bucketName ??= _settings.BucketName;

            if (files.Count == 0)
                return BadRequest("No files provided");

            try
            {
                // Remove files
                await _minioClient.RemoveObjectsAsync(
                    new RemoveObjectsArgs()
                        .WithBucket(bucketName)
                        .WithObjects(files));

                return Ok(new { bucket = bucketName, count = files.Count, status = "deleted" });
            }
            catch (MinioException ex)
            {
                return StatusCode(500, $"MinIO error: {ex.Message}");
            }
        }

        // ================================================================
        // COPY OR MOVE OBJECT
        // ================================================================
        /// <summary>
        /// Copies or moves an object between buckets or within the same bucket.
        /// Set 'cut=true' to delete the source file after copying.
        /// </summary>
        [HttpPost("Copy")]
        public async Task<IActionResult> CopyObject(
            [FromQuery] string source,
            [FromQuery] string destination,
            [FromQuery] string? sourceBucket = null,
            [FromQuery] string? destinationBucket = null,
            [FromQuery] bool cut = false)
        {
            sourceBucket ??= _settings.BucketName;
            destinationBucket ??= _settings.BucketName;

            try
            {
                // Validate source exists
                try
                {
                    await _minioClient.StatObjectAsync(
                        new StatObjectArgs()
                            .WithBucket(sourceBucket)
                            .WithObject(source));
                }
                catch (ObjectNotFoundException)
                {
                    return NotFound($"Source file '{source}' not found in bucket '{sourceBucket}'");
                }

                // Perform copy operation
                var copyArgs = new CopyObjectArgs()
                    .WithBucket(destinationBucket)
                    .WithObject(destination)
                    .WithCopyObjectSource(new CopySourceObjectArgs()
                        .WithBucket(sourceBucket)
                        .WithObject(source));

                await _minioClient.CopyObjectAsync(copyArgs);

                // If 'cut=true', delete original file
                if (cut)
                {
                    await _minioClient.RemoveObjectAsync(
                        new RemoveObjectArgs()
                            .WithBucket(sourceBucket)
                            .WithObject(source));
                }

                return Ok(new
                {
                    from = $"{sourceBucket}/{source}",
                    to = $"{destinationBucket}/{destination}",
                    renamed = cut,
                    status = cut ? "moved" : "copied"
                });
            }
            catch (MinioException ex)
            {
                return StatusCode(500, $"MinIO error: {ex.Message}");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Unexpected error: {ex.Message}");
            }
        }

        // ================================================================
        // GENERATE PRESIGNED GET URL
        // ================================================================
        /// <summary>
        /// Generates a temporary presigned URL for downloading an object.
        /// Valid for 10 minutes by default.
        /// </summary>
        [HttpGet("PresignedUrl/{fileName}")]
        public async Task<IActionResult> GetPresignedUrl(string fileName, [FromQuery] string? bucketName = null)
        {
            bucketName ??= _settings.BucketName;
            var safeFileName = Path.GetFileName(fileName);

            try
            {
                var url = await _minioClient.PresignedGetObjectAsync(
                    new PresignedGetObjectArgs()
                        .WithBucket(bucketName)
                        .WithObject(safeFileName)
                        .WithExpiry(600));

                return Ok(new { file = safeFileName, bucket = bucketName, url });
            }
            catch (MinioException ex)
            {
                return StatusCode(500, $"MinIO error: {ex.Message}");
            }
        }

        // ================================================================
        // GENERATE PRESIGNED PUT URL
        // ================================================================
        /// <summary>
        /// Generates a temporary presigned URL for uploading directly to MinIO.
        /// Allows clients to upload without sending the file through the API server.
        /// </summary>
        [HttpGet("PresignedUpload/{fileName}")]
        public async Task<IActionResult> GetPresignedUploadUrl(string fileName, [FromQuery] string? bucketName = null)
        {
            bucketName ??= _settings.BucketName;
            var safeFileName = Path.GetFileName(fileName);

            try
            {
                // Ensure bucket exists
                bool bucketExists = await _minioClient.BucketExistsAsync(
                    new BucketExistsArgs().WithBucket(bucketName));

                if (!bucketExists)
                    await _minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucketName));

                // Generate presigned URL (valid 10 minutes)
                var url = await _minioClient.PresignedPutObjectAsync(
                    new PresignedPutObjectArgs()
                        .WithBucket(bucketName)
                        .WithObject(safeFileName)
                        .WithExpiry(600));

                return Ok(new
                {
                    bucket = bucketName,
                    file = safeFileName,
                    expiresIn = "10 minutes",
                    url
                });
            }
            catch (MinioException ex)
            {
                return StatusCode(500, $"MinIO error: {ex.Message}");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Unexpected error: {ex.Message}");
            }
        }

        // ================================================================
        // GET OBJECT METADATA AND TAGS
        // ================================================================
        /// <summary>
        /// Returns metadata and tags for an object.
        /// Includes file size, content type, and custom user tags.
        /// </summary>
        [HttpGet("Info/{fileName}")]
        public async Task<IActionResult> GetObjectInfo(string fileName, [FromQuery] string? bucketName = null)
        {
            bucketName ??= _settings.BucketName;
            var safeFileName = Path.GetFileName(fileName);

            try
            {
                var stat = await _minioClient.StatObjectAsync(
                    new StatObjectArgs().WithBucket(bucketName).WithObject(safeFileName));

                var tags = await _minioClient.GetObjectTagsAsync(
                    new GetObjectTagsArgs().WithBucket(bucketName).WithObject(safeFileName));

                return Ok(new
                {
                    file = safeFileName,
                    bucket = bucketName,
                    size = stat.Size,
                    contentType = stat.ContentType,
                    lastModified = stat.LastModified,
                    metadata = stat.MetaData,
                    tags = tags.Tags
                });
            }
            catch (ObjectNotFoundException)
            {
                return NotFound($"File '{safeFileName}' not found in bucket '{bucketName}'");
            }
            catch (MinioException ex)
            {
                return StatusCode(500, $"MinIO error: {ex.Message}");
            }
        }

        // ================================================================
        // SET OBJECT TAGS
        // ================================================================
        /// <summary>
        /// Sets or replaces tags (metadata) on an object.
        /// Tags are stored as key-value pairs and support up to 10 entries per object.
        /// </summary>
        [HttpPost("Tags/{fileName}")]
        public async Task<IActionResult> SetObjectTags(string fileName, [FromQuery] string? bucketName = null, [FromBody] Dictionary<string, string> tags = null!)
        {
            bucketName ??= _settings.BucketName;
            var safeFileName = Path.GetFileName(fileName);

            if (tags is null)
                return BadRequest("Tags cannot be null");

            try
            {
                // Set tag
                await _minioClient.SetObjectTagsAsync(
                    new SetObjectTagsArgs()
                        .WithBucket(bucketName)
                        .WithObject(safeFileName)
                        .WithTagging(new Tagging(tags, false)));

                return Ok(new { file = safeFileName, bucket = bucketName, tags, status = "tags-updated" });
            }
            catch (MinioException ex)
            {
                return StatusCode(500, $"MinIO error: {ex.Message}");
            }
        }
    }
}
