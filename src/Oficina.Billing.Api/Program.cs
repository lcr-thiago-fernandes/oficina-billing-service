using System.Text.Json.Serialization;
using Oficina.Billing.Api.Configuracao;
using Oficina.Billing.Api.Simulador;
using Oficina.Billing.Aplicacao;
using Oficina.Billing.Infraestrutura;
using Oficina.Billing.Infraestrutura.MercadoPago;
using Oficina.Billing.Infraestrutura.Simulador;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, _, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration).Enrich.FromLogContext());

var modoPagamento = ValidarConfiguracaoPagamento(builder.Configuration);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AdicionarSwagger();
builder.Services.AdicionarAplicacao();
builder.Services.AdicionarInfraestrutura(builder.Configuration);
builder.Services.AdicionarJwtBearer(builder.Configuration);
builder.Services.AdicionarPoliticas();

var app = builder.Build();

// Atras do API Gateway/NLB o servico responde sob /billing (e continua respondendo sem o prefixo).
app.UsePathBase("/billing");
app.UseRouting();
app.UseMiddleware<MiddlewareDeCorrelacao>();
app.UseSerilogRequestLogging();
app.UseMiddleware<MiddlewareDeExcecoes>();

// Swagger em todos os ambientes; endpoint relativo para funcionar sob o PathBase.
app.UseSwagger();
app.UseSwaggerUI(options => options.SwaggerEndpoint("v1/swagger.json", "Oficina Billing API v1"));

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
if (modoPagamento == ModoPagamento.Simulado)
    app.MapearSimulador();

app.Run();

// Falha alta na subida: no modo MercadoPago, sem token nao ha cobranca e sem segredo todo webhook seria recusado.
static ModoPagamento ValidarConfiguracaoPagamento(IConfiguration configuracao)
{
    var modo = configuracao.GetSection(OpcoesPagamento.Secao).Get<OpcoesPagamento>()?.Modo ?? ModoPagamento.Simulado;
    if (modo != ModoPagamento.MercadoPago)
        return modo;

    var mercadoPago = configuracao.GetSection(OpcoesMercadoPago.Secao).Get<OpcoesMercadoPago>() ?? new OpcoesMercadoPago();
    if (string.IsNullOrWhiteSpace(mercadoPago.AccessToken))
        throw new InvalidOperationException(
            $"{OpcoesPagamento.Secao}:Modo = MercadoPago exige {OpcoesMercadoPago.Secao}:AccessToken.");
    if (string.IsNullOrWhiteSpace(mercadoPago.WebhookSecret))
        throw new InvalidOperationException(
            $"{OpcoesPagamento.Secao}:Modo = MercadoPago exige {OpcoesMercadoPago.Secao}:WebhookSecret.");
    return modo;
}

public partial class Program { }
