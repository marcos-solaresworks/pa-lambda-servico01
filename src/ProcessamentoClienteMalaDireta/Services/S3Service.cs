using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Lambda.Core;

namespace ProcessamentoClienteMalaDireta.Services;

public interface IS3Service
{
    Task<Stream> DownloadArquivoAsync(string bucketName, string key);
    Task<string> UploadArquivoAsync(string bucketName, string key, Stream conteudo, string contentType = "application/octet-stream");
    Task<bool> ArquivoExisteAsync(string bucketName, string key);
}

public class S3Service : IS3Service
{
    private readonly IAmazonS3 _s3Client;
    private readonly ILambdaLogger _logger;

    public S3Service(IAmazonS3 s3Client, ILambdaLogger logger)
    {
        _s3Client = s3Client;
        _logger = logger;
    }

    public async Task<Stream> DownloadArquivoAsync(string bucketName, string key)
    {
        try
        {
            _logger.LogInformation($"Baixando arquivo do S3: {bucketName}/{key}");
            
            var request = new GetObjectRequest
            {
                BucketName = bucketName,
                Key = key
            };

            var response = await _s3Client.GetObjectAsync(request);
            _logger.LogInformation($"Arquivo baixado com sucesso: {key} ({response.ContentLength} bytes)");
            
            return response.ResponseStream;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Erro ao baixar arquivo {bucketName}/{key}: {ex.Message}");
            throw;
        }
    }

    public async Task<string> UploadArquivoAsync(string bucketName, string key, Stream conteudo, string contentType = "application/octet-stream")
    {
        try
        {
            _logger.LogInformation($"Enviando arquivo para S3: {bucketName}/{key}");
            
            var request = new PutObjectRequest
            {
                BucketName = bucketName,
                Key = key,
                InputStream = conteudo,
                ContentType = contentType,
                ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
            };

            var response = await _s3Client.PutObjectAsync(request);
            _logger.LogInformation($"Arquivo enviado com sucesso: {key} (ETag: {response.ETag})");
            
            return $"s3://{bucketName}/{key}";
        }
        catch (Exception ex)
        {
            _logger.LogError($"Erro ao enviar arquivo {bucketName}/{key}: {ex.Message}");
            throw;
        }
    }

    public async Task<bool> ArquivoExisteAsync(string bucketName, string key)
    {
        try
        {
            var request = new GetObjectMetadataRequest
            {
                BucketName = bucketName,
                Key = key
            };

            await _s3Client.GetObjectMetadataAsync(request);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Erro ao verificar existência do arquivo {bucketName}/{key}: {ex.Message}");
            throw;
        }
    }
}