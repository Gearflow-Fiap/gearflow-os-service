using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GearFlow.Os.Infrastructure.Persistence;

/// <summary>
/// O OS cria o banco com <c>EnsureCreatedAsync</c>, que NÃO altera tabelas que já existem. As colunas de trace do outbox foram acrescentadas
/// depois, então um banco criado antes delas ficaria sem as colunas e todo INSERT/SELECT do outbox falharia. Este DDL idempotente
/// (<c>ADD COLUMN IF NOT EXISTS</c>) roda logo depois do <c>EnsureCreatedAsync</c>: em banco novo não faz nada, em banco antigo completa a tabela.
/// </summary>
public static class OutboxSchema
{
    public const int TraceParentLength = 64;
    public const int TraceStateLength = 512;

    public static Task EnsureTraceColumnsAsync(this DatabaseFacade database, CancellationToken cancellationToken = default)
    {
        if (!database.IsNpgsql())
            return Task.CompletedTask;

        // Valores fixos (sem entrada do usuário); os nomes seguem o modelo: tabela outbox_messages, colunas com o nome da propriedade.
        return database.ExecuteSqlRawAsync(
            $"""
             ALTER TABLE outbox_messages ADD COLUMN IF NOT EXISTS "TraceParent" character varying({TraceParentLength}) NULL;
             ALTER TABLE outbox_messages ADD COLUMN IF NOT EXISTS "TraceState" character varying({TraceStateLength}) NULL;
             """,
            cancellationToken);
    }
}
