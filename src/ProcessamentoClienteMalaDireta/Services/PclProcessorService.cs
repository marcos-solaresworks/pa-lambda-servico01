using Amazon.Lambda.Core;
using System.Text;
using ProcessamentoClienteMalaDireta.Models;

namespace ProcessamentoClienteMalaDireta.Services;

public interface IPclProcessorService
{
    Task<byte[]> ProcessarArquivoPclAsync(Stream arquivoOriginal, ConfiguracaoMalaDireta configuracao, ArquivoPcl metadados);
    byte[] GerarCabecalhoPcl(ConfiguracaoMalaDireta configuracao);
    byte[] GerarRodapePcl(ConfiguracaoMalaDireta configuracao);
    byte[] AplicarConfiguracaoA4(byte[] conteudoPcl, ConfiguracaoMalaDireta configuracao);
}

public class PclProcessorService : IPclProcessorService
{
    private readonly ILambdaLogger _logger;

    public PclProcessorService(ILambdaLogger logger)
    {
        _logger = logger;
    }

    public async Task<byte[]> ProcessarArquivoPclAsync(Stream arquivoOriginal, ConfiguracaoMalaDireta configuracao, ArquivoPcl metadados)
    {
        _logger.LogInformation($"Iniciando processamento PCL para arquivo: {metadados.NomeArquivo}");
        
        try
        {
            // 1. Ler conteúdo original do arquivo PCL
            using var memoryStream = new MemoryStream();
            await arquivoOriginal.CopyToAsync(memoryStream);
            var conteudoOriginal = memoryStream.ToArray();
            
            _logger.LogInformation($"Arquivo original lido: {conteudoOriginal.Length} bytes");

            // 2. Gerar cabeçalho PCL com configurações de Mala Direta
            var cabecalho = GerarCabecalhoPcl(configuracao);
            
            // 3. Aplicar configurações específicas A4
            var conteudoProcessado = AplicarConfiguracaoA4(conteudoOriginal, configuracao);
            
            // 4. Gerar rodapé PCL
            var rodape = GerarRodapePcl(configuracao);
            
            // 5. Combinar todos os elementos
            var arquivoFinal = CombinarElementosPcl(cabecalho, conteudoProcessado, rodape);
            
            _logger.LogInformation($"Processamento PCL concluído: {arquivoFinal.Length} bytes finais");
            
            return arquivoFinal;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Erro no processamento PCL: {ex.Message}");
            throw;
        }
    }

    public byte[] GerarCabecalhoPcl(ConfiguracaoMalaDireta configuracao)
    {
        var sb = new StringBuilder();
        
        // PCL Reset
        sb.Append("\x1B" + "E");
        
        // Configurar orientação e tamanho de papel (A4)
        sb.Append("\x1B" + "&l0O"); // Orientação retrato
        sb.Append("\x1B" + "&l26A"); // Tamanho A4
        
        // Configurar margens baseadas na configuração
        sb.Append($"\x1B" + $"&l{configuracao.MargemSuperior}E"); // Margem superior
        sb.Append($"\x1B" + $"&a{configuracao.MargemEsquerda}L"); // Margem esquerda
        
        // Configurar fonte padrão
        sb.Append("\x1B" + "(s0P"); // Fonte primária
        sb.Append("\x1B" + "(s10H"); // Tamanho 10 pontos
        sb.Append("\x1B" + "(s0S"); // Estilo normal
        sb.Append("\x1B" + "(s0B"); // Peso normal
        
        // Configurações específicas para Mala Direta
        if (configuracao.TipoEnvelope == "Janela")
        {
            // Posicionamento para envelope com janela
            sb.Append("\x1B" + "&a2000V"); // Posição vertical para janela
            sb.Append("\x1B" + "&a1200H"); // Posição horizontal para janela
        }
        
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    public byte[] GerarRodapePcl(ConfiguracaoMalaDireta configuracao)
    {
        var sb = new StringBuilder();
        
        // Adicionar logotipo se configurado
        if (!string.IsNullOrEmpty(configuracao.LogotipoEmpresa))
        {
            // PCL para inserir logotipo (simplificado)
            sb.Append("\x1B" + "&a8000V"); // Posição para rodapé
            sb.Append($"<!-- LOGOTIPO: {configuracao.LogotipoEmpresa} -->");
        }
        
        // Adicionar informações de processamento
        sb.Append($"\n<!-- Processado em: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC -->");
        sb.Append($"\n<!-- Configuração: {configuracao.FormatoImpressao} / {configuracao.TipoEnvelope} -->");
        
        // PCL Form Feed (nova página)
        sb.Append("\x0C");
        
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    public byte[] AplicarConfiguracaoA4(byte[] conteudoPcl, ConfiguracaoMalaDireta configuracao)
    {
        _logger.LogInformation("Aplicando configurações A4 ao conteúdo PCL");
        
        var conteudoString = Encoding.ASCII.GetString(conteudoPcl);
        
        // Aplicar ajustes específicos para formato A4
        var sb = new StringBuilder(conteudoString);
        
        // Substituir configurações de margem existentes
        sb.Replace("\x1B&l0E", $"\x1B&l{configuracao.MargemSuperior}E"); // Margem superior
        sb.Replace("\x1B&l0L", $"\x1B&a{configuracao.MargemEsquerda}L"); // Margem esquerda
        
        // Garantir formato A4
        if (!conteudoString.Contains("\x1B&l26A"))
        {
            sb.Insert(0, "\x1B&l26A"); // Forçar A4 se não estiver presente
        }
        
        // Configurações específicas para Mala Direta
        if (configuracao.EnderecoCompleto)
        {
            // Inserir campos para endereço completo
            sb.Append("\n<!-- ENDERECO_COMPLETO_HABILITADO -->");
        }
        
        if (configuracao.CodigoPostal)
        {
            // Inserir campos para código postal
            sb.Append("\n<!-- CODIGO_POSTAL_HABILITADO -->");
        }
        
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private byte[] CombinarElementosPcl(byte[] cabecalho, byte[] conteudo, byte[] rodape)
    {
        var tamanhoTotal = cabecalho.Length + conteudo.Length + rodape.Length;
        var resultado = new byte[tamanhoTotal];
        
        var offset = 0;
        
        // Copiar cabeçalho
        Array.Copy(cabecalho, 0, resultado, offset, cabecalho.Length);
        offset += cabecalho.Length;
        
        // Copiar conteúdo
        Array.Copy(conteudo, 0, resultado, offset, conteudo.Length);
        offset += conteudo.Length;
        
        // Copiar rodapé
        Array.Copy(rodape, 0, resultado, offset, rodape.Length);
        
        return resultado;
    }
}