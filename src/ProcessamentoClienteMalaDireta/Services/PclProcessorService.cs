using Amazon.Lambda.Core;
using System.Text;
using ProcessamentoClienteMalaDireta.Models;

namespace ProcessamentoClienteMalaDireta.Services;

public interface IPclProcessorService
{
    Task<byte[]> ProcessarDadosParaPclAsync(Stream arquivoDados, ConfiguracaoMalaDireta configuracao, ArquivoPcl metadados);
    byte[] GerarCabecalhoPcl(ConfiguracaoMalaDireta configuracao);
    byte[] GerarRodapePcl(ConfiguracaoMalaDireta configuracao);
    byte[] GerarPclDeRegistros(List<Dictionary<string, string>> registros, ConfiguracaoMalaDireta configuracao);
}

public class PclProcessorService : IPclProcessorService
{
    private readonly ILambdaLogger _logger;

    public PclProcessorService(ILambdaLogger logger)
    {
        _logger = logger;
    }

    public async Task<byte[]> ProcessarDadosParaPclAsync(Stream arquivoDados, ConfiguracaoMalaDireta configuracao, ArquivoPcl metadados)
    {
        _logger.LogInformation($"Iniciando processamento de dados para PCL: {metadados.NomeArquivo}");
        
        try
        {
            // 1. Ler arquivo CSV/TXT
            var registros = await LerArquivoDadosAsync(arquivoDados, metadados.NomeArquivo);
            _logger.LogInformation($"Arquivo lido: {registros.Count} registros encontrados");

            // 2. Gerar cabeçalho PCL com configurações de Mala Direta
            var cabecalho = GerarCabecalhoPcl(configuracao);
            
            // 3. Gerar conteúdo PCL a partir dos registros
            var conteudoPcl = GerarPclDeRegistros(registros, configuracao);
            _logger.LogInformation($"Conteúdo PCL gerado: {conteudoPcl.Length} bytes");
            
            // 4. Gerar rodapé PCL
            var rodape = GerarRodapePcl(configuracao);
            
            // 5. Combinar todos os elementos
            var arquivoFinal = CombinarElementosPcl(cabecalho, conteudoPcl, rodape);
            
            _logger.LogInformation($"Processamento PCL concluído: {arquivoFinal.Length} bytes finais, {registros.Count} registros processados");
            
            return arquivoFinal;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Erro no processamento de dados para PCL: {ex.Message}");
            throw;
        }
    }

    private async Task<List<Dictionary<string, string>>> LerArquivoDadosAsync(Stream arquivoDados, string nomeArquivo)
    {
        var registros = new List<Dictionary<string, string>>();
        var extensao = Path.GetExtension(nomeArquivo).ToLowerInvariant();

        using var reader = new StreamReader(arquivoDados, Encoding.UTF8);
        
        if (extensao == ".csv")
        {
            // Processar CSV
            var header = (await reader.ReadLineAsync())?.Split(',');
            if (header == null) return registros;

            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                var valores = line.Split(',');
                var registro = new Dictionary<string, string>();
                
                for (int i = 0; i < header.Length && i < valores.Length; i++)
                {
                    registro[header[i].Trim()] = valores[i].Trim();
                }
                
                registros.Add(registro);
            }
        }
        else // TXT (delimitado por tab ou pipe)
        {
            var header = (await reader.ReadLineAsync())?.Split(new[] { '\t', '|' });
            if (header == null) return registros;

            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                var valores = line.Split(new[] { '\t', '|' });
                var registro = new Dictionary<string, string>();
                
                for (int i = 0; i < header.Length && i < valores.Length; i++)
                {
                    registro[header[i].Trim()] = valores[i].Trim();
                }
                
                registros.Add(registro);
            }
        }

        _logger.LogInformation($"Lidos {registros.Count} registros do arquivo {nomeArquivo}");
        return registros;
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

    public byte[] GerarPclDeRegistros(List<Dictionary<string, string>> registros, ConfiguracaoMalaDireta configuracao)
    {
        _logger.LogInformation($"Gerando PCL para {registros.Count} registros");
        
        var sb = new StringBuilder();
        var linhaAtual = configuracao.MargemSuperior + 10; // Posição vertical inicial

        foreach (var registro in registros)
        {
            // Nova página para cada registro (envelope)
            if (registros.IndexOf(registro) > 0)
            {
                sb.Append("\x0C"); // Form Feed
                linhaAtual = configuracao.MargemSuperior + 10;
            }

            // Posicionar cursor para início do endereço
            sb.Append($"\x1B&a{linhaAtual}V"); // Posição vertical
            sb.Append($"\x1B&a{configuracao.MargemEsquerda}H"); // Posição horizontal

            // Imprimir Nome/Destinatário
            if (registro.ContainsKey("Nome") || registro.ContainsKey("Destinatario"))
            {
                var nome = registro.GetValueOrDefault("Nome") ?? registro.GetValueOrDefault("Destinatario") ?? "";
                sb.Append($"{nome}\n");
                linhaAtual += 20;
            }

            // Imprimir Endereço
            if (configuracao.EnderecoCompleto)
            {
                sb.Append($"\x1B&a{linhaAtual}V");
                if (registro.ContainsKey("Endereco"))
                {
                    sb.Append($"{registro["Endereco"]}\n");
                    linhaAtual += 20;
                }

                // Complemento
                if (registro.ContainsKey("Complemento") && !string.IsNullOrEmpty(registro["Complemento"]))
                {
                    sb.Append($"\x1B&a{linhaAtual}V");
                    sb.Append($"{registro["Complemento"]}\n");
                    linhaAtual += 20;
                }

                // Bairro
                if (registro.ContainsKey("Bairro"))
                {
                    sb.Append($"\x1B&a{linhaAtual}V");
                    sb.Append($"{registro["Bairro"]}\n");
                    linhaAtual += 20;
                }

                // Cidade/Estado
                sb.Append($"\x1B&a{linhaAtual}V");
                var cidade = registro.GetValueOrDefault("Cidade") ?? "";
                var estado = registro.GetValueOrDefault("Estado") ?? registro.GetValueOrDefault("UF") ?? "";
                sb.Append($"{cidade} - {estado}\n");
                linhaAtual += 20;
            }

            // Código Postal/CEP
            if (configuracao.CodigoPostal && registro.ContainsKey("CEP"))
            {
                sb.Append($"\x1B&a{linhaAtual}V");
                sb.Append($"CEP: {registro["CEP"]}\n");
            }
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