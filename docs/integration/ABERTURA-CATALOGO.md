# Abertura com serviços do catálogo

O POST `/api/ordens-servico` recebe serviços estruturados:

```json
{
  "vehicleId": "11111111-1111-1111-1111-111111111111",
  "services": [
    {
      "catalogJobId": "22222222-2222-2222-2222-222222222222",
      "description": "Troca de óleo"
    }
  ]
}
```

Os GUIDs acima são ilustrativos: usar veículo cadastrado e serviço existente no
catálogo do ambiente. Esta versão valida formato/identidade, mas ainda não consulta
o catálogo remoto para verificar existência. A lista anterior de strings deixa de
ser aceita. Atualizar os clientes da API antes do rollout.

Cada ocorrência recebe LineId próprio, persistido como RequestedService.Id;
CatalogJobId identifica o serviço e pode se repetir em linhas distintas.
São aceitos de 1 a 200 serviços, com descrição de até 200 caracteres.
StartDiagnostic usa essas identidades sem fabricar IDs de catálogo. Peças iniciais
continuam vazias; não há entrada de peças nesta API. ServiceOrderCode é null no
comando porque o banco gera Code ao salvar. Estado e outbox são salvos juntos.

Para banco existente, aplicar `scripts/sql/20261111_requested_service_catalog.sql`
antes da aplicação; EnsureCreated só cria o schema inicial. A coluna nullable
preserva registros legados, que exigem associação explícita antes de novo envio
de diagnóstico. Não executar rollback removendo a coluna enquanto houver dados novos.

Esta entrega corrige a entrada do diagnóstico. Autorização comercial, compensação
confirmada e homologação com RabbitMQ real permanecem no plano de integração.
