using System.Diagnostics;

namespace Verx.Contracts;

/// <summary>
/// Correlação ponta a ponta: o id é o trace id W3C da requisição. Ele viaja no evento e é retomado
/// no Consolidado, de modo que os logs dos dois serviços podem ser ligados pelo mesmo id.
/// </summary>
public static class Correlacao
{
    public static string? Atual => Activity.Current is { TraceId: var id } && id != default ? id.ToString() : null;

    /// <summary>Retoma o trace de origem. Sem id válido, não cria atividade. Descarte o resultado ao terminar.</summary>
    public static Activity? Retomar(string? correlationId, string nome)
    {
        if (correlationId is not { Length: 32 } || !correlationId.All(Uri.IsHexDigit)) return null;
        var traceId = ActivityTraceId.CreateFromString(correlationId);
        if (traceId == default) return null;

        var atividade = new Activity(nome);
        atividade.SetParentId(traceId, ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded);
        return atividade.Start();
    }
}
