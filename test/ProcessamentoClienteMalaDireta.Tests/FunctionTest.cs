using Xunit;
using Amazon.Lambda.Core;
using Amazon.Lambda.TestUtilities;

namespace ProcessamentoClienteMalaDireta.Tests;

public class FunctionTest
{
    [Fact]
    public async Task TestProcessamentoMalaDiretaSucesso()
    {
        // Arrange
        var function = new Function();
        var context = new TestLambdaContext();
        
        var payload = new LambdaProcessamentoPayload
        {
            LoteId = 123,
            TipoProcessamento = "ClienteMalaDireta",
            Cliente = new Cliente
            {
                Id = 1,
                Nome = "Empresa XYZ Ltda",
                Email = "contato@empresaxyz.com",
                Telefone = "(11) 99999-9999",
                DataCadastro = DateTime.UtcNow.AddDays(-30)
            },
            PerfilProcessamento = new PerfilProcessamento
            {
                Id = 1,
                Nome = "Perfil Mala Direta Padrão",
                Descricao = "Perfil para processamento de mala direta",
                TipoProcessamento = "ClienteMalaDireta",
                LambdaFunction = "ProcessamentoClienteMalaDireta",
                DataCriacao = DateTime.UtcNow.AddDays(-10)
            },
            ArquivosPcl = new List<ArquivoPcl>
            {
                new ArquivoPcl
                {
                    Id = 1,
                    NomeArquivo = "mala_direta_001.pcl",
                    CaminhoArquivo = "/uploads/mala_direta_001.pcl",
                    TamanhoBytes = 1024000,
                    NumeroPaginas = 50,
                    DataUpload = DateTime.UtcNow.AddHours(-1)
                },
                new ArquivoPcl
                {
                    Id = 2,
                    NomeArquivo = "mala_direta_002.pcl",
                    CaminhoArquivo = "/uploads/mala_direta_002.pcl",
                    TamanhoBytes = 2048000,
                    NumeroPaginas = 100,
                    DataUpload = DateTime.UtcNow.AddHours(-1)
                }
            },
            DataCriacao = DateTime.UtcNow.AddMinutes(-30),
            ProcessamentoConfig = new Dictionary<string, object>
            {
                ["FormatoImpressao"] = "A4",
                ["TipoEnvelope"] = "Janela"
            }
        };

        // Act
        var response = await function.FunctionHandler(payload, context);

        // Assert
        Assert.NotNull(response);
        Assert.Equal(123, response.LoteId);
        Assert.True(response.Sucesso);
        Assert.Equal("Sucesso", response.Status);
        Assert.Equal("ClienteMalaDireta", response.TipoProcessamento);
        Assert.Equal(2, response.ArquivosProcessados.Count);
        Assert.Equal(150, response.TotalPaginas); // 50 + 100
        Assert.Contains("mala_direta_001.pcl", response.ArquivosProcessados);
        Assert.Contains("mala_direta_002.pcl", response.ArquivosProcessados);
        Assert.True(response.TempoProcessamento > TimeSpan.Zero);
    }
    
    [Fact]
    public async Task TestProcessamentoMalaDiretaPayloadInvalido()
    {
        // Arrange
        var function = new Function();
        var context = new TestLambdaContext();
        
        var payloadInvalido = new LambdaProcessamentoPayload
        {
            LoteId = 0, // Inválido
            TipoProcessamento = "ClienteMalaDireta"
            // Faltam dados obrigatórios
        };

        // Act
        var response = await function.FunctionHandler(payloadInvalido, context);

        // Assert
        Assert.NotNull(response);
        Assert.False(response.Sucesso);
        Assert.Equal("Erro de Validação", response.Status);
        Assert.Contains("LoteId deve ser maior que zero", response.MensagemRetorno);
    }
    
    [Fact]
    public async Task TestProcessamentoMalaDiretaSemArquivos()
    {
        // Arrange
        var function = new Function();
        var context = new TestLambdaContext();
        
        var payload = new LambdaProcessamentoPayload
        {
            LoteId = 123,
            TipoProcessamento = "ClienteMalaDireta",
            Cliente = new Cliente { Id = 1, Nome = "Teste" },
            PerfilProcessamento = new PerfilProcessamento { Id = 1, Nome = "Teste" },
            ArquivosPcl = new List<ArquivoPcl>(), // Lista vazia
            DataCriacao = DateTime.UtcNow
        };

        // Act
        var response = await function.FunctionHandler(payload, context);

        // Assert
        Assert.NotNull(response);
        Assert.False(response.Sucesso);
        Assert.Equal("Erro de Validação", response.Status);
        Assert.Contains("Lista de arquivos PCL não pode estar vazia", response.MensagemRetorno);
    }
}
