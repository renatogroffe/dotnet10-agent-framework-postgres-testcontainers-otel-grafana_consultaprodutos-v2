using Azure.AI.OpenAI;
using Bogus;
using ConsoleAppChatAIProdutos.Data;
using ConsoleAppChatAIProdutos.Inputs;
using ConsoleAppChatAIProdutos.Plugins;
using ConsoleAppChatAIProdutos.Tracing;
using ConsoleAppChatAIProdutos.Utils;
using Grafana.OpenTelemetry;
using LinqToDB;
using LinqToDB.Data;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.ClientModel;
using Testcontainers.PostgreSql;

var numberOfRecords = InputHelper.GetNumberOfNewProducts();

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json")
    .AddEnvironmentVariables()
    .Build();

CommandLineHelper.Execute("docker images",
    "Imagens antes da execucao do Testcontainers...");
CommandLineHelper.Execute("docker container ls",
    "Containers antes da execucao do Testcontainers...");

Console.WriteLine("Criando container para uso do PostgreSQL...");
var postgresContainer = new PostgreSqlBuilder("postgres:18.1")
    .WithDatabase("basecatalogo")
    .WithResourceMapping(
        DBFileAsByteArray.GetContent("basecatalogo.sql"),
        "/docker-entrypoint-initdb.d/01-basecatalogo.sql")
    .Build();
await postgresContainer.StartAsync();

Console.WriteLine("Container iniciado. Coletando estado apos Testcontainers...");
CommandLineHelper.Execute("docker images",
    "Imagens apos execucao do Testcontainers...");
CommandLineHelper.Execute("docker container ls",
    "Containers apos execucao do Testcontainers...");

var connectionString = postgresContainer.GetConnectionString();
Console.WriteLine($"Connection String da base de dados PostgreSQL: {connectionString}");
CatalogoContext.ConnectionString = connectionString;

var db = new DataConnection(new DataOptions().UsePostgreSQL(connectionString));

var random = new Random();
var fakeProdutos = new Faker<ConsoleAppChatAIProdutos.Data.Fake.Produto>("pt_BR").StrictMode(false)
            .RuleFor(p => p.Nome, f => f.Commerce.Product())
            .RuleFor(p => p.CodigoBarras, f => f.Commerce.Ean13())
            .RuleFor(p => p.Preco, f => random.Next(10, 30))
            .Generate(numberOfRecords);


Console.WriteLine($"Gerando {numberOfRecords} produtos...");
await db.BulkCopyAsync<ConsoleAppChatAIProdutos.Data.Fake.Produto>(fakeProdutos);
Console.WriteLine($"Produtos gerados com sucesso!");
Console.WriteLine();
var resultSelectProdutos = await postgresContainer.ExecScriptAsync(
    "SELECT * FROM \"Produtos\"");
Console.WriteLine(resultSelectProdutos.Stdout);

var resourceBuilder = ResourceBuilder
    .CreateDefault()
    .AddService(OpenTelemetryExtensions.ServiceName);
var traceProvider = Sdk.CreateTracerProviderBuilder()
    .SetResourceBuilder(resourceBuilder)
    .AddSource(OpenTelemetryExtensions.ServiceName)
    .AddEntityFrameworkCoreInstrumentation()
    .AddHttpClientInstrumentation()
    .UseGrafana()
    .Build();
var metricsProvider = Sdk.CreateMeterProviderBuilder()
    .SetResourceBuilder(resourceBuilder)
    .AddHttpClientInstrumentation()
    .UseGrafana()
    .Build();
var loggerFactory = LoggerFactory.Create(builder =>
{
    builder
        .SetMinimumLevel(LogLevel.Information)
        .AddOpenTelemetry(options =>
        {
            options.SetResourceBuilder(resourceBuilder);
            options.IncludeFormattedMessage = true;
            options.IncludeScopes = true;
            options.ParseStateValues = true;
            options.AttachLogsToActivityEvent();
            options.UseGrafana();
        });
});
var logger = loggerFactory.CreateLogger("ConsoleAppChatAIProdutos");


var agent = new AzureOpenAIClient(endpoint: new Uri(configuration["AzureOpenAI:Endpoint"]!),
        credential: new ApiKeyCredential(configuration["AzureOpenAI:ApiKey"]!))
    .GetChatClient(configuration["AzureOpenAI:DeploymentName"]!)
    .AsAIAgent(
        instructions: "Você é um assistente de IA que ajuda o usuario a consultar informações" +
            "sobre produtos em uma base de dados do PostgreSQL.",
        tools: [.. ProdutosPlugin.GetFunctions()])
    .AsBuilder()
    .UseOpenTelemetry(sourceName: OpenTelemetryExtensions.ServiceName,
        configure: (cfg) => cfg.EnableSensitiveData = true)
    .Build();
var oldForegroundColor = Console.ForegroundColor;
while (true)
{
    Console.WriteLine("Sua pergunta:");
    var userPrompt = Console.ReadLine();

    using var activity1 = OpenTelemetryExtensions.ActivitySource
        .StartActivity("PerguntaChatIAProdutos")!;
    logger.LogInformation($"Pergunta do usuario ---> {userPrompt}");

    var result = await agent.RunAsync(userPrompt!);

    Console.WriteLine();
    Console.WriteLine("Resposta da IA:");
    Console.WriteLine();

    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Cyan;
    var textReponse = result.AsChatResponse().Messages.Last().Text;
    Console.WriteLine(textReponse);
    logger.LogInformation($"Resposta da IA ---> {textReponse}");
    Console.ForegroundColor = oldForegroundColor;

    Console.WriteLine();
    Console.WriteLine();

    activity1.Stop();
}