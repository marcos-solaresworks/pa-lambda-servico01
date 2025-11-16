using Amazon.Lambda.Core;
using Amazon.S3;
using Microsoft.Extensions.DependencyInjection;
using ProcessamentoClienteMalaDireta.Services;
using ProcessamentoClienteMalaDireta.Models;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace ProcessamentoClienteMalaDireta;

public class Function
{
    private readonly ServiceProvider _serviceProvider;
    
    public Function()
    {
        // Configurar DI Container
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    /// <summary>
    /// Processa lotes de impressão para clientes do tipo Mala Direta
    /// </summary>
    /// <param name="request">Payload com dados do lote e configurações de processamento</param>
    /// <param name="context">Contexto da Lambda para logging</param>
    /// <returns>Resposta com status do processamento</returns>
    public async Task<LambdaProcessamentoResponse> FunctionHandler(LambdaProcessamentoPayload request, ILambdaContext context)
    {
        var logger = context.Logger;
        logger.LogInformation($"[ClienteMalaDireta] Iniciando processamento do Lote: {request.LoteId}");
        
        try
        {
            // Validar payload recebido
            ValidarPayload(request, logger);
            
            // Log detalhado do que foi recebido
            logger.LogInformation($"Cliente: {request.Cliente?.Nome} (ID: {request.Cliente?.Id})");
            logger.LogInformation($"Perfil: {request.PerfilProcessamento?.Nome} (Tipo: {request.TipoProcessamento})");
            logger.LogInformation($"Arquivos PCL: {request.ArquivosPcl?.Count ?? 0}");
            
            // Criar services com logger do contexto
            var s3Client = new AmazonS3Client();
            var s3Service = new S3Service(s3Client, logger);
            var pclProcessor = new PclProcessorService(logger);
            var processamentoService = new MalaDiretaProcessamentoService(s3Service, pclProcessor, logger);
            
            // Processar lote usando o serviço especializado
            var resultado = await processamentoService.ProcessarLoteAsync(request);
            
            logger.LogInformation($"[ClienteMalaDireta] Processamento concluído com sucesso para Lote: {request.LoteId}");
            
            return new LambdaProcessamentoResponse
            {
                LoteId = request.LoteId,
                Status = "Sucesso",
                Sucesso = true,
                MensagemRetorno = "Lote processado com sucesso para Cliente Mala Direta",
                DataProcessamento = DateTime.UtcNow,
                DetalhesProcessamento = resultado,
                TipoProcessamento = request.TipoProcessamento,
                TempoProcessamento = resultado.TempoProcessamento,
                ArquivosProcessados = resultado.ArquivosProcessados,
                TotalPaginas = resultado.TotalPaginas
            };
        }
        catch (ArgumentException ex)
        {
            logger.LogError($"[ClienteMalaDireta] Erro de validação: {ex.Message}");
            return CriarRespostaErro(request.LoteId, "Erro de Validação", ex.Message, request.TipoProcessamento);
        }
        catch (Exception ex)
        {
            logger.LogError($"[ClienteMalaDireta] Erro inesperado: {ex.Message}");
            logger.LogError($"StackTrace: {ex.StackTrace}");
            return CriarRespostaErro(request.LoteId, "Erro Interno", ex.Message, request.TipoProcessamento);
        }
    }
    
    private void ConfigureServices(IServiceCollection services)
    {
        // Configurar AWS S3
        services.AddSingleton<IAmazonS3>(provider => new AmazonS3Client());
        
        // Registrar services
        services.AddSingleton<IS3Service>(provider => 
            new S3Service(provider.GetRequiredService<IAmazonS3>(), provider.GetRequiredService<ILambdaLogger>()));
        services.AddSingleton<IPclProcessorService>(provider => 
            new PclProcessorService(provider.GetRequiredService<ILambdaLogger>()));
        services.AddSingleton<IMalaDiretaProcessamentoService>(provider => 
            new MalaDiretaProcessamentoService(
                provider.GetRequiredService<IS3Service>(),
                provider.GetRequiredService<IPclProcessorService>(),
                provider.GetRequiredService<ILambdaLogger>()));
        
        // Logger será injetado pelo contexto da Lambda
        services.AddSingleton<ILambdaLogger>(provider => throw new InvalidOperationException("Logger deve ser fornecido pelo contexto"));
    }

    private void ValidarPayload(LambdaProcessamentoPayload request, ILambdaLogger logger)
    {
        if (request == null)
            throw new ArgumentException("Payload não pode ser nulo");
            
        if (request.LoteId <= 0)
            throw new ArgumentException("LoteId deve ser maior que zero");
            
        if (request.Cliente == null)
            throw new ArgumentException("Dados do Cliente são obrigatórios");
            
        if (request.PerfilProcessamento == null)
            throw new ArgumentException("Perfil de Processamento é obrigatório");
            
        if (request.ArquivosPcl == null || !request.ArquivosPcl.Any())
            throw new ArgumentException("Lista de arquivos PCL não pode estar vazia");
            
        if (string.IsNullOrEmpty(request.TipoProcessamento))
            throw new ArgumentException("Tipo de Processamento é obrigatório");
            
        // Validação específica para ClienteMalaDireta
        if (request.TipoProcessamento != "ClienteMalaDireta")
        {
            logger.LogWarning($"Tipo de processamento inesperado: {request.TipoProcessamento}. Esperado: ClienteMalaDireta");
        }
        
        logger.LogInformation("Payload validado com sucesso");
    }

    
    private LambdaProcessamentoResponse CriarRespostaErro(int loteId, string status, string mensagem, string? tipoProcessamento)
    {
        return new LambdaProcessamentoResponse
        {
            LoteId = loteId,
            Status = status,
            Sucesso = false,
            MensagemRetorno = mensagem,
            DataProcessamento = DateTime.UtcNow,
            TipoProcessamento = tipoProcessamento,
            DetalhesProcessamento = null,
            TempoProcessamento = TimeSpan.Zero,
            ArquivosProcessados = new List<string>(),
            TotalPaginas = 0
        };
    }
}
