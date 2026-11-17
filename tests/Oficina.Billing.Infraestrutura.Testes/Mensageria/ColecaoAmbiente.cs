namespace Oficina.Billing.Infraestrutura.Testes.Mensageria;

/// <summary>
/// Testes que leem ou alteram a variavel de processo HOSTNAME (nome da fila do bus) nao rodam em paralelo entre si.
/// </summary>
[CollectionDefinition(Nome, DisableParallelization = true)]
public sealed class ColecaoAmbiente
{
    public const string Nome = "Ambiente (HOSTNAME)";
}
