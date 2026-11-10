# GearFlow — OS Service

Microsserviço da **Ordem de Serviço** e **orquestrador da Saga** da Fase 4. Roda na máquina: API, PostgreSQL e RabbitMQ.

Este serviço é dono do cliente, do veículo, da abertura da OS, do status global e da coordenação persistente do fluxo. Billing, Operation e Inventory **executam comandos e devolvem eventos**; o OS decide o próximo passo e as compensações.

## Escolha da Saga: orquestrada

A Fase 4 aceita orquestração ou coreografia. Este serviço adota **orquestração**, alinhado ao que Inventory e Budget já documentam:

- O OS publica comandos com destinatário único.
- Os participantes respondem com fatos.
- Compensações (`CancelOperation`, `ReleaseStock`, `RefundPayment` ou `CancelBudget`) saem daqui, não de um segundo orquestrador.

Justificativa: Inventory já consome `ReserveStock`/`ReleaseStock`/`ConsumeStock`, Billing já consome `GenerateBudget`/`RequestPayment`/`CancelBudget`/`RefundPayment`, e Operation já consome `StartDiagnostic`/`StartExecution`/`CancelOperation`. Sem um orquestrador, esses contratos ficam órfãos.

## Status

```
Created → WaitingOrcamento → AwaitingStock → AwaitingPayment → InProgress → Completed
                 │                  └ AwaitingParts (estoque insuficiente)
                 └ rejeição → Cancelled
```

Sem peças, a aprovação pula `AwaitingStock` e vai para `AwaitingPayment`. `Paid` fica no histórico na confirmação do pagamento. A entrega exige execução concluída e, se houve reserva, baixa de estoque (`StockConsumed`).

## Falhas e compensações

Qualquer falha antes do fim da execução cancela a OS (`Cancelled`) e desfaz, uma única vez, o que a Saga já fez:

| Falha recebida | Compensação enviada pelo OS |
|---|---|
| `DiagnosticFailed`, `BudgetGenerationFailed`, `BudgetRejected` | `CancelOperation`; `CancelBudget` se já existe orçamento |
| `PaymentFailed` | `CancelOperation`, `ReleaseStock` (se reservou) e `CancelBudget` |
| `OperationCommandRejected` (ex.: `StartExecution` recusado) | `CancelOperation`, `ReleaseStock` e `RefundPayment` (se já pagou) ou `CancelBudget` |
| `OperationCanceled` iniciado pelo Operation | `ReleaseStock` e `RefundPayment` ou `CancelBudget` (sem reenviar `CancelOperation`) |
| Cancelamento pela recepção (`POST /api/ordens-servico/{id}/cancelamento`) | as mesmas da linha anterior, mais `CancelOperation` |

Depois que o Operation conclui a execução, a OS não é mais cancelada: a falha fica registrada no histórico. Recusa de cancelamento (`OperationCancellationRejected` ou `OperationCommandRejected` de `CancelOperation`) e confirmações (`StockReleased`, `PaymentRefunded`, `BudgetCanceled`, `PaymentRequested`, `OperationCanceled` após o cancelamento) também vão para o histórico.

O `RequestPayment` usa o total do `BudgetApproved` (`totalAmount`), que é o valor que o Billing confere. O total do diagnóstico fica só como reserva.

A OS que espera peças (`AwaitingParts`) não tem prazo automático: a recepção cancela pela rota acima.

## Contratos de mensageria

O OS fala os três idiomas que já existem — não inventa um quarto:

| Destino | Como publica | Nome |
|---|---|---|
| Broker (informativo) | topic `gearflow.events` | `os.order-created.v1` |
| Billing | topic `gearflow.commands.v1` + envelope v1 (`Producer=gearflow-os-service`) | `billing.GenerateBudget`, `billing.RequestPayment`, `billing.CancelBudget`, `billing.RefundPayment` |
| Operation | fila MassTransit | `operation-start-diagnostic`, `operation-start-execution`, `operation-cancel-operation` |
| Inventory | fila MassTransit | `inventory-reserve-stock`, `inventory-release-stock`, `inventory-consume-stock` |

Consome:

- `gearflow.events.v1` / `gearflow-budget-service.*`
- fanouts `operation.diagnostic-finished`, `operation.diagnostic-failed`, `operation.execution-finished`, `operation.canceled`, `operation.command-rejected`, `operation.cancellation-rejected`
- fanouts `inventory.stock-reserved`, `inventory.stock-insufficient`, `inventory.stock-released`, `inventory.stock-consumed`, `inventory.stock-replenished`

Com o broker do `gearflow-infra-broker`, use `RabbitMq__VirtualHost=gearflow`.

## Subir local

```powershell
docker compose up --build
```

Se o Postgres local já existia **antes desta versão**, recrie o volume (`docker compose down -v`) — o schema ganhou colunas da Saga.

- API: http://localhost:5080
- Scalar: http://localhost:5080/scalar
- OpenAPI: http://localhost:5080/openapi/v1.json
- Health: `/health/live` e `/health/ready`
- RabbitMQ: http://localhost:15672 (`guest` / `guest`)
- Postgres no host: `localhost:5433`

```powershell
docker compose up -d postgres rabbitmq
dotnet run --project src/GearFlow.Os.Api
```

JWT de desenvolvimento (o mesmo segredo HMAC do Inventory):

```http
POST /api/dev/token
```

Use `Authorization: Bearer <accessToken>` nas rotas `/api/*`.

## Fluxo local sem os outros serviços

Em Development, `POST /api/dev/eventos` injeta o mesmo efeito que a fila.

```http
POST /api/dev/token

POST /api/clientes
{ "name": "Ana Souza", "document": "529.982.247-25", "email": "ana@oficina.com" }

POST /api/veiculos
{ "clientId": "<id>", "plate": "ABC1D23", "model": "Gol" }

POST /api/ordens-servico
{ "vehicleId": "<id>", "services": ["Troca de óleo"] }

POST /api/dev/eventos
{ "tipo": "BudgetGenerated", "ordemServicoId": "<id>" }

POST /api/dev/eventos
{ "tipo": "BudgetApproved", "ordemServicoId": "<id>" }

POST /api/dev/eventos
{ "tipo": "PaymentConfirmed", "ordemServicoId": "<id>" }

POST /api/dev/eventos
{ "tipo": "ExecutionFinished", "ordemServicoId": "<id>" }

POST /api/ordens-servico/<id>/entrega
```

`tipo` também aceita os nomes antigos (`OrcamentoGerado`, `OrcamentoApproved`, `OperationCompleted`), os de estoque (`StockReserved`, `StockInsufficient`, `StockConsumed`) e as falhas (`PaymentFailed`, `DiagnosticFailed`, `BudgetGenerationFailed`, `OperationCanceled`), para demonstrar as compensações.

## Testes

```powershell
dotnet test
```

Há testes de unidade da máquina de estados, da orquestração e dos caminhos de falha (compensações), além de dois cenários BDD (Reqnroll): o ciclo completo abrir → orçamento → pagamento → execução → entrega e o pagamento recusado com compensação.

O CI no GitHub Actions aplica gate de cobertura de 80% de linhas em Domain + Application. Primeira execução no GitHub (10/11/2026, job "Build + testes + cobertura"): gate aprovado; o relatório Cobertura fica no artefato `coverage` de cada execução em [Actions](https://github.com/Gearflow-Fiap/gearflow-os-service/actions/workflows/ci.yml). Medição local de 05/11/2026: 82,2% de linhas.

## Observabilidade e entrega

- Traces/métricas OpenTelemetry por OTLP; no Kubernetes vão para o New Relic (veja [`k8s/README.md`](k8s/README.md))
- Probes `/health/live` e `/health/ready` (Postgres + RabbitMQ)
- Manifests em [`k8s/base`](k8s/base)
- [`ci.yml`](.github/workflows/ci.yml): build, testes, gate de cobertura e SonarCloud (liga com `SONAR_PROJECT_KEY`, `SONAR_ORG` e o secret `SONAR_TOKEN`)
- [`docker-publish.yml`](.github/workflows/docker-publish.yml): imagem no GHCR (`ghcr.io/gearflow-fiap/gearflow-os-service`)
- [`delivery.yml`](.github/workflows/delivery.yml): deploy no EKS depois do CI verde na `main` (liga com `DEPLOY_ENABLED=true`)
- A `main` é protegida: merge só por PR, com aprovação e o CI verde

## Stack

.NET 10, PostgreSQL 16, RabbitMQ 3, EF Core, outbox/inbox, JWT HS256. Tudo pelo `docker-compose.yml`.
