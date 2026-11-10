# Kubernetes — gearflow-os-service

Manifests base em `k8s/base`. O deploy automático é o workflow `.github/workflows/delivery.yml`: roda depois do CI verde na `main`, usa a imagem `ghcr.io/gearflow-fiap/gearflow-os-service:sha-<commit>` publicada pelo `docker-publish.yml` e só é ativado com a variável do repositório `DEPLOY_ENABLED=true` (mais `AWS_ROLE_ARN`, `AWS_REGION` e `EKS_CLUSTER_NAME`).

Secret esperado (`gearflow-os-secret`):

- `ConnectionStrings__OsDb`
- `Jwt__Secret` (HMAC, mínimo 32 caracteres, o mesmo emissor dos outros serviços)

## RabbitMQ compartilhado

O OS conecta no broker do `gearflow-infra-broker` (veja `docs/GUIA-INTEGRACAO-RABBITMQ.md` naquele repositório):

- Host `rabbitmq.messaging.svc.cluster.local`, porta `5672`, vhost `gearflow`, AMQP sem TLS (no ConfigMap).
- Usuário `gearflow_os`, lido do Secret `rabbitmq-os-credentials` (chaves `username` e `password`) no namespace da aplicação. O Secret original fica em `messaging`; o operador do broker copia somente a credencial do OS para cá.
- O Pod já leva a label `gearflow.io/rabbitmq-client: "true"`. O namespace precisa da label `gearflow.io/messaging-access=true`:

```bash
kubectl label namespace gearflow gearflow.io/messaging-access=true --overwrite
```

## Observabilidade

Traces e métricas saem por OTLP para o New Relic (`https://otlp.nr-data.net:4317`), como no Inventory. A licença vem do Secret `newrelic-apm-secret` (chave `NEW_RELIC_LICENSE_KEY`) no namespace; sem ele o Pod sobe normalmente e só não exporta.

```bash
kubectl apply -k k8s/base
```
