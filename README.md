# GearFlow — OS Service

Microsserviço da **Ordem de Serviço**. Roda inteiro na máquina: API, PostgreSQL e RabbitMQ. Não chama nuvem nem Mercado Pago.

Este serviço é dono do cliente, do veículo, da abertura da OS, do status e do histórico. Orçamento, operação e estoque entram depois, por evento. A saga é coreografada: este serviço publica `order.created` e reage aos eventos dos outros.

## Status

```
Created → WaitingOrcamento → Paid → InProgress → Completed
                              └─ rejeição → Cancelled
```

`Paid` fica no histórico. Depois do pagamento o estado gravado é `InProgress`. A entrega só conclui a OS se a operação já tiver terminado.

## Subir local

Na pasta deste repositório:

```powershell
docker compose up --build
```

- API: http://localhost:5080
- Scalar (documentação): http://localhost:5080/scalar
- OpenAPI: http://localhost:5080/openapi/v1.json
- RabbitMQ: http://localhost:15672 (`guest` / `guest`)
- Postgres no host: `localhost:5433`, banco `gearflow_os`, usuário e senha `gearflow`

Para desenvolver a API fora do container, suba só a infraestrutura e rode o projeto:

```powershell
docker compose up -d postgres rabbitmq
dotnet run --project src/GearFlow.Os.Api
```

O schema é criado no startup.

## Fluxo local sem os outros serviços

Enquanto Orçamento, Operação e Estoque não existem, o endpoint de desenvolvimento publica o mesmo efeito que a fila. Ele só responde com `ASPNETCORE_ENVIRONMENT=Development`.

```http
POST /api/clientes
{ "name": "Ana Souza", "document": "529.982.247-25", "email": "ana@oficina.com" }

POST /api/veiculos
{ "clientId": "<id>", "plate": "ABC1D23", "model": "Gol" }

POST /api/ordens-servico
{ "vehicleId": "<id>", "services": ["Troca de óleo"] }

POST /api/dev/eventos
{ "tipo": "OrcamentoGerado", "ordemServicoId": "<id>" }

POST /api/dev/eventos
{ "tipo": "OrcamentoApproved", "ordemServicoId": "<id>" }

POST /api/dev/eventos
{ "tipo": "OperationCompleted", "ordemServicoId": "<id>" }

POST /api/ordens-servico/<id>/entrega

GET /api/ordens-servico/<id>
```

`tipo` também aceita `OrcamentoRejected`, que cancela a OS.

A abertura grava `order.created` na outbox e o dispatcher publica no exchange `gearflow.events`. Dá para ver a mensagem no painel do RabbitMQ.

## Eventos

| Rota | Direção | Efeito neste serviço |
|---|---|---|
| `order.created` | publica | OS aberta |
| `orcamento.gerado` | consome | `WaitingOrcamento` |
| `orcamento.approved` | consome | histórico `Paid`, estado `InProgress` |
| `orcamento.rejected` | consome | `Cancelled` |
| `operation.completed` | consome | libera a entrega |

O corpo dos eventos consumidos é JSON: `{ "messageId": "<guid>", "serviceOrderId": "<guid>" }`. O mesmo `messageId` não muda a OS duas vezes. A publicação e a gravação da OS saem na mesma transação (outbox).

## Testes

```powershell
dotnet test
```

Há testes de unidade da máquina de estados e um cenário BDD (Reqnroll) do ciclo abrir → status → histórico → entrega.

## Stack

.NET 10, PostgreSQL 16, RabbitMQ 3, EF Core, outbox/inbox. Tudo pelo `docker-compose.yml`.
