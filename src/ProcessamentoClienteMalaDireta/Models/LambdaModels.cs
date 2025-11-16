namespace ProcessamentoClienteMalaDireta.Models;

// Models para receber o payload do OrquestradorCentral
public class LambdaProcessamentoPayload
{
    public int LoteId { get; set; }
    public Cliente? Cliente { get; set; }
    public PerfilProcessamento? PerfilProcessamento { get; set; }
    public List<ArquivoPcl>? ArquivosPcl { get; set; }
    public DateTime DataCriacao { get; set; }
    public string? TipoProcessamento { get; set; }
    public string? LambdaArn { get; set; }
    public Dictionary<string, object>? ProcessamentoConfig { get; set; }
}

public class Cliente
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Telefone { get; set; } = string.Empty;
    public DateTime DataCadastro { get; set; }
}

public class PerfilProcessamento
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;
    public string? ConfiguracaoJson { get; set; }
    public string? LogotipoPath { get; set; }
    public DateTime DataCriacao { get; set; }
    public string? TipoProcessamento { get; set; }
    public string? LambdaFunction { get; set; }
}

public class ArquivoPcl
{
    public int Id { get; set; }
    public int LoteId { get; set; }
    public string NomeArquivo { get; set; } = string.Empty;
    public string CaminhoArquivo { get; set; } = string.Empty;
    public long TamanhoBytes { get; set; }
    public int NumeroPaginas { get; set; }
    public DateTime DataUpload { get; set; }
}

// Model para resposta da Lambda
public class LambdaProcessamentoResponse
{
    public int LoteId { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool Sucesso { get; set; }
    public string MensagemRetorno { get; set; } = string.Empty;
    public DateTime DataProcessamento { get; set; }
    public DetalhesProcessamento? DetalhesProcessamento { get; set; }
    public string? TipoProcessamento { get; set; }
    public TimeSpan TempoProcessamento { get; set; }
    public List<string> ArquivosProcessados { get; set; } = new();
    public int TotalPaginas { get; set; }
}

public class DetalhesProcessamento
{
    public TimeSpan TempoProcessamento { get; set; }
    public List<string> ArquivosProcessados { get; set; } = new();
    public List<string> ArquivosProcessadosS3 { get; set; } = new();
    public int TotalPaginas { get; set; }
    public ConfiguracaoMalaDireta? ConfiguracaoUtilizada { get; set; }
}

public class ConfiguracaoMalaDireta
{
    public string FormatoImpressao { get; set; } = "A4";
    public string TipoEnvelope { get; set; } = "Janela";
    public bool EnderecoCompleto { get; set; } = true;
    public bool CodigoPostal { get; set; } = true;
    public string LogotipoEmpresa { get; set; } = string.Empty;
    public int MargemSuperior { get; set; } = 15;
    public int MargemInferior { get; set; } = 15;
    public int MargemEsquerda { get; set; } = 20;
    public int MargemDireita { get; set; } = 20;
}