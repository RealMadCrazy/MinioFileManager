using System.Diagnostics;
using FluentFTP;
using Microsoft.AspNetCore.Mvc;
using Minio;
using Minio.DataModel.Args;
using Minio.DataModel.Encryption;

namespace MinioFileManager.Controller
{
    [Route("api/[controller]")]
    [ApiController]
    public class BenchmarkController(IMinioClient minioClient, ILogger<BenchmarkController> logger) : ControllerBase
    {
        private readonly string _minioBucket = "benchmark-bucket"; // default bucket name

        [HttpPost("Run")]
        public async Task<IActionResult> RunBenchmark(IFormFile file)
        {
            if (file.Length == 0)
                return BadRequest("No file provided.");

            string fileName = Path.GetFileName(file.FileName);
            logger.LogInformation("Starting benchmark for file: {FileName} ({FileSize} bytes)", fileName, file.Length);

            // Save to temp file for FTP operations
            string tempFilePath = Path.GetTempFileName();
            try
            {
                using (var fs = new FileStream(tempFilePath, FileMode.Create))
                {
                    await file.CopyToAsync(fs);
                }
                logger.LogInformation("Temporary file created: {TempFilePath}", tempFilePath);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create temporary file: {TempFilePath}", tempFilePath);
                return StatusCode(500, $"Failed to create temporary file: {ex.Message}");
            }

            // Read file bytes for MinIO
            byte[] fileBytes;
            try
            {
                await using (var stream = file.OpenReadStream())
                {
                    fileBytes = new byte[stream.Length];
                    await stream.ReadAsync(fileBytes);
                }
                logger.LogInformation("File bytes read successfully: {FileSize} bytes", fileBytes.Length);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to read file bytes");
                return StatusCode(500, $"Failed to read file bytes: {ex.Message}");
            }

            // ===========================
            // FTP Benchmark
            // ===========================
            var ftpUploadTimer = new Stopwatch();
            var ftpDownloadTimer = new Stopwatch();
            string ftpTempDownloadPath = Path.Combine(Path.GetTempPath(), "ftp_download.tmp");

            int ftpUploadSuccessCount = 0;
            int ftpUploadFailCount = 0;
            int ftpDownloadSuccessCount = 0;
            int ftpDownloadFailCount = 0;

            try
            {
                using (var ftp = new FtpClient("127.0.0.1"))
                {
                    ftp.Connect();
                    logger.LogInformation("FTP client connected successfully");

                    // FTP Upload 1000 times
                    ftpUploadTimer.Start();
                    for (int i = 0; i < 1000; i++)
                    {
                        string uniqueName = $"{Path.GetFileNameWithoutExtension(fileName)}_{i + 1}{Path.GetExtension(fileName)}";
                        string remotePath = "/Files/" + uniqueName;
                        
                        try
                        {
                            ftp.UploadFile(tempFilePath, remotePath, FtpRemoteExists.Overwrite, true);
                            ftpUploadSuccessCount++;
                            logger.LogDebug("FTP upload successful: {RemotePath}", remotePath);
                        }
                        catch (Exception ex)
                        {
                            ftpUploadFailCount++;
                            logger.LogError(ex, "FTP upload failed: {RemotePath}", remotePath);
                        }
                    }
                    ftpUploadTimer.Stop();
                    logger.LogInformation("FTP upload completed: {SuccessCount} successful, {FailCount} failed", ftpUploadSuccessCount, ftpUploadFailCount);

                    // FTP Download 1000 times
                    ftpDownloadTimer.Start();
                    for (int i = 0; i < 1000; i++)
                    {
                        string uniqueName = $"{Path.GetFileNameWithoutExtension(fileName)}_{i + 1}{Path.GetExtension(fileName)}";
                        string remotePath = "/Files/" + uniqueName;
                        
                        try
                        {
                            ftp.DownloadFile(ftpTempDownloadPath, remotePath);
                            ftpDownloadSuccessCount++;
                            logger.LogDebug("FTP download successful: {RemotePath}", remotePath);
                        }
                        catch (Exception ex)
                        {
                            ftpDownloadFailCount++;
                            logger.LogError(ex, "FTP download failed: {RemotePath}", remotePath);
                        }
                    }
                    ftpDownloadTimer.Stop();
                    logger.LogInformation("FTP download completed: {SuccessCount} successful, {FailCount} failed", ftpDownloadSuccessCount, ftpDownloadFailCount);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "FTP client operation failed");
                return StatusCode(500, $"FTP operation failed: {ex.Message}");
            }
            finally
            {
                // Clean up temporary files
                try
                {
                    if (System.IO.File.Exists(tempFilePath))
                    {
                        System.IO.File.Delete(tempFilePath);
                        logger.LogInformation("Temporary file deleted: {TempFilePath}", tempFilePath);
                    }
                    if (System.IO.File.Exists(ftpTempDownloadPath))
                    {
                        System.IO.File.Delete(ftpTempDownloadPath);
                        logger.LogInformation("FTP download temporary file deleted: {TempFilePath}", ftpTempDownloadPath);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to delete temporary files");
                }
            }

            // ===========================
            // MinIO Benchmark
            // ===========================
            var minioUploadTimer = new Stopwatch();
            var minioDownloadTimer = new Stopwatch();

            int minioUploadSuccessCount = 0;
            int minioUploadFailCount = 0;
            int minioDownloadSuccessCount = 0;
            int minioDownloadFailCount = 0;

            try
            {
                // Ensure bucket exists
                bool bucketExists = await minioClient.BucketExistsAsync(new BucketExistsArgs().WithBucket(_minioBucket));
                if (!bucketExists)
                {
                    await minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(_minioBucket));
                    logger.LogInformation("MinIO bucket created: {BucketName}", _minioBucket);
                }
                else
                {
                    logger.LogInformation("MinIO bucket already exists: {BucketName}", _minioBucket);
                }

                // MinIO Upload 1000 times - REUSING THE SAME MEMORY STREAM
                minioUploadTimer.Start();
                for (int i = 0; i < 1000; i++)
                {
                    string objectName = $"{i + 1}_{fileName}";
                    
                    try
                    {
                        // Create the stream once and reuse it by resetting position
                        using var stream = new MemoryStream(fileBytes);
                        await minioClient.PutObjectAsync(new PutObjectArgs()
                            .WithBucket(_minioBucket)
                            .WithObject(objectName)
                            .WithStreamData(stream)
                            .WithObjectSize(fileBytes.Length) // Use the known file bytes length
                            .WithContentType(file.ContentType));
                        
                        minioUploadSuccessCount++;
                        logger.LogDebug("MinIO upload successful: {ObjectName}", objectName);
                    }
                    catch (Exception ex)
                    {
                        minioUploadFailCount++;
                        logger.LogError(ex, "MinIO upload failed: {ObjectName}", objectName);
                    }
                }
                minioUploadTimer.Stop();
                logger.LogInformation("MinIO upload completed: {SuccessCount} successful, {FailCount} failed", minioUploadSuccessCount, minioUploadFailCount);

                // MinIO Download 1000 times
                minioDownloadTimer.Start();
                for (int i = 0; i < 1000; i++)
                {
                    string objectName = $"{i + 1}_{fileName}";
                    
                    try
                    {
                        using var tempStream = new MemoryStream();
                        await minioClient.GetObjectAsync(new GetObjectArgs()
                            .WithBucket(_minioBucket)
                            .WithObject(objectName)
                            .WithCallbackStream(s => s.CopyTo(tempStream)));
                        
                        minioDownloadSuccessCount++;
                        logger.LogDebug("MinIO download successful: {ObjectName}", objectName);
                    }
                    catch (Exception ex)
                    {
                        minioDownloadFailCount++;
                        logger.LogError(ex, "MinIO download failed: {ObjectName}", objectName);
                    }
                }
                minioDownloadTimer.Stop();
                logger.LogInformation("MinIO download completed: {SuccessCount} successful, {FailCount} failed", minioDownloadSuccessCount, minioDownloadFailCount);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "MinIO client operation failed");
                return StatusCode(500, $"MinIO operation failed: {ex.Message}");
            }

            // ===========================
            // Return results
            // ===========================
            var result = new
            {
                FTP = new
                {
                    UploadTime = ftpUploadTimer.Elapsed.ToString(),
                    DownloadTime = ftpDownloadTimer.Elapsed.ToString(),
                    UploadStats = new { Successful = ftpUploadSuccessCount, Failed = ftpUploadFailCount },
                    DownloadStats = new { Successful = ftpDownloadSuccessCount, Failed = ftpDownloadFailCount }
                },
                MinIO = new
                {
                    UploadTime = minioUploadTimer.Elapsed.ToString(),
                    DownloadTime = minioDownloadTimer.Elapsed.ToString(),
                    UploadStats = new { Successful = minioUploadSuccessCount, Failed = minioUploadFailCount },
                    DownloadStats = new { Successful = minioDownloadSuccessCount, Failed = minioDownloadFailCount }
                }
            };

            logger.LogInformation("Benchmark completed successfully. Results: {@Results}", result);
            return Ok(result);
        }
    }
}