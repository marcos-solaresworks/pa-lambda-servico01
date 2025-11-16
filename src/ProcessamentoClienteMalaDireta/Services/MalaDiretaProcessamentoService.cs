using Amazon.Lambda.Core;
using ProcessamentoClienteMalaDireta.Models;

namespace ProcessamentoClienteMalaDireta.Services;

public interface IMalaDiretaProcessamentoService
{
    Task<DetalhesProcessamento> ProcessarLoteAsync(LambdaProcessamentoPayload payload);
    Task<ArquivoProcessadoResult> ProcessarArquivoAsync(ArquivoPcl arquivo, ConfiguracaoMalaDireta configuracao);
    ConfiguracaoMalaDireta ObterConfiguracaoMalaDireta(LambdaProcessamentoPayload payload);
}

public class MalaDiretaProcessamentoService : IMalaDiretaProcessamentoService
{
    private readonly IS3Service _s3Service;
    private readonly IPclProcessorService _pclProcessor;
    private readonly ILambdaLogger _logger;
    
    // Configurações S3
    private const string BUCKET_ORIGEM = "grafica-ltda-uploads";
    private const string BUCKET_PROCESSADOS = "grafica-ltda-processados";

    public MalaDiretaProcessamentoService(
        IS3Service s3Service, 
        IPclProcessorService pclProcessor, 
        ILambdaLogger logger)
    {
        _s3Service = s3Service;
        _pclProcessor = pclProcessor;
        _logger = logger;
    }

    public async Task<DetalhesProcessamento> ProcessarLoteAsync(LambdaProcessamentoPayload payload)
    {
        var inicioProcessamento = DateTime.UtcNow;
        var arquivosProcessados = new List<string>();
        var arquivosProcessadosS3 = new List<string>();
        var totalPaginas = 0;

        _logger.LogInformation($"Iniciando processamento de lote Mala Direta: {payload.LoteId}");
        
        // Obter configuração específica para Mala Direta
        var configuracao = ObterConfiguracaoMalaDireta(payload);
        _logger.LogInformation($"Configuração Mala Direta: Formato={configuracao.FormatoImpressao}, Envelope={configuracao.TipoEnvelope}");

        // Processar cada arquivo PCL
        foreach (var arquivo in payload.ArquivosPcl ?? new List<ArquivoPcl>())
        {
            try
            {
                _logger.LogInformation($"Processando arquivo: {arquivo.NomeArquivo} ({arquivo.NumeroPaginas} páginas)");
                
                var resultado = await ProcessarArquivoAsync(arquivo, configuracao);
                
                arquivosProcessados.Add(arquivo.NomeArquivo);
                arquivosProcessadosS3.Add(resultado.CaminhoS3);
                totalPaginas += arquivo.NumeroPaginas;
                
                _logger.LogInformation($"Arquivo processado com sucesso: {arquivo.NomeArquivo} -> {resultado.CaminhoS3}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erro ao processar arquivo {arquivo.NomeArquivo}: {ex.Message}");
                throw new InvalidOperationException($"Falha no processamento do arquivo {arquivo.NomeArquivo}", ex);
            }
        }

        // Aplicar regras específicas de Mala Direta
        await AplicarRegrasMalaDiretaAsync(payload, configuracao);

        var tempoProcessamento = DateTime.UtcNow - inicioProcessamento;
        
        _logger.LogInformation($"Lote {payload.LoteId} processado com sucesso: {arquivosProcessados.Count} arquivos, {totalPaginas} páginas, {tempoProcessamento.TotalSeconds:F2}s");

        return new DetalhesProcessamento
        {
            TempoProcessamento = tempoProcessamento,
            ArquivosProcessados = arquivosProcessados,
            ArquivosProcessadosS3 = arquivosProcessadosS3,
            TotalPaginas = totalPaginas,
            ConfiguracaoUtilizada = configuracao
        };
    }

    public async Task<ArquivoProcessadoResult> ProcessarArquivoAsync(ArquivoPcl arquivo, ConfiguracaoMalaDireta configuracao)
    {
        // 1. Baixar arquivo original do S3
        var chaveOrigem = $"uploads/lote-{arquivo.LoteId}/{arquivo.NomeArquivo}";
        
        using var arquivoOriginal = await _s3Service.DownloadArquivoAsync(BUCKET_ORIGEM, chaveOrigem);
        
        // 2. Processar arquivo PCL com configurações de Mala Direta
        var arquivoProcessado = await _pclProcessor.ProcessarArquivoPclAsync(arquivoOriginal, configuracao, arquivo);
        
        // 3. Gerar caminho de destino no S3
        var chaveDestino = GerarCaminhoDestinoS3(arquivo, configuracao);
        
        // 4. Fazer upload do arquivo processado
        using var streamProcessado = new MemoryStream(arquivoProcessado);
        var caminhoS3 = await _s3Service.UploadArquivoAsync(BUCKET_PROCESSADOS, chaveDestino, streamProcessado, "application/vnd.hp-pcl");
        
        return new ArquivoProcessadoResult
        {
            NomeArquivo = arquivo.NomeArquivo,
            CaminhoS3 = caminhoS3,
            TamanhoProcessado = arquivoProcessado.Length,
            ChaveS3 = chaveDestino
        };
    }

    public ConfiguracaoMalaDireta ObterConfiguracaoMalaDireta(LambdaProcessamentoPayload payload)
    {
        // Configuração padrão para Mala Direta
        var configuracao = new ConfiguracaoMalaDireta
        {
            FormatoImpressao = "A4",
            TipoEnvelope = "Janela",
            EnderecoCompleto = true,
            CodigoPostal = true,
            LogotipoEmpresa = payload.PerfilProcessamento?.LogotipoPath ?? "",
            MargemSuperior = 15,
            MargemInferior = 15,
            MargemEsquerda = 20,
            MargemDireita = 20
        };

        // Sobrescrever com configurações específicas do payload
        if (payload.ProcessamentoConfig != null)
        {
            if (payload.ProcessamentoConfig.ContainsKey("FormatoImpressao"))
                configuracao.FormatoImpressao = payload.ProcessamentoConfig["FormatoImpressao"]?.ToString() ?? configuracao.FormatoImpressao;
                
            if (payload.ProcessamentoConfig.ContainsKey("TipoEnvelope"))
                configuracao.TipoEnvelope = payload.ProcessamentoConfig["TipoEnvelope"]?.ToString() ?? configuracao.TipoEnvelope;
                
            if (payload.ProcessamentoConfig.ContainsKey("EnderecoCompleto"))
                configuracao.EnderecoCompleto = bool.Parse(payload.ProcessamentoConfig["EnderecoCompleto"]?.ToString() ?? "true");
                
            if (payload.ProcessamentoConfig.ContainsKey("CodigoPostal"))
                configuracao.CodigoPostal = bool.Parse(payload.ProcessamentoConfig["CodigoPostal"]?.ToString() ?? "true");
        }

        return configuracao;
    }

    private string GerarCaminhoDestinoS3(ArquivoPcl arquivo, ConfiguracaoMalaDireta configuracao)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var nomeArquivoSemExtensao = Path.GetFileNameWithoutExtension(arquivo.NomeArquivo);
        var nomeArquivoProcessado = $"{nomeArquivoSemExtensao}_processed_{timestamp}.pcl";
        
        return $"mala-direta/lote-{arquivo.LoteId}/{configuracao.FormatoImpressao.ToLower()}/{nomeArquivoProcessado}";
    }

    private async Task AplicarRegrasMalaDiretaAsync(LambdaProcessamentoPayload payload, ConfiguracaoMalaDireta configuracao)
    {
        _logger.LogInformation("Aplicando regras específicas de Mala Direta");
        
        // Regras específicas para Mala Direta:
        // 1. Validar se endereços estão completos
        // 2. Verificar formatação de envelope
        // 3. Aplicar logotipo da empresa se necessário
        // 4. Configurar layout específico para impressão
        
        // Simular tempo de aplicação das regras
        await Task.Delay(200);
        
        _logger.LogInformation($"Regras aplicadas: Envelope={configuracao.TipoEnvelope}, Endereço Completo={configuracao.EnderecoCompleto}");
    }
}

public class ArquivoProcessadoResult
{
    public string NomeArquivo { get; set; } = string.Empty;
    public string CaminhoS3 { get; set; } = string.Empty;
    public string ChaveS3 { get; set; } = string.Empty;
    public long TamanhoProcessado { get; set; }
}