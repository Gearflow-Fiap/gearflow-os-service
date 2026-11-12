# Kubernetes â€” gearflow-os-service

Manifests base em `k8s/base`. O deploy automÃ¡tico Ã© o workflow `.github/workflows/delivery.yml`: roda depois do CI verde na `main`, usa a imagem `ghcr.io/gearflow-fiap/gearflow-os-service:sha-<commit>` publicada pelo `docker-publish.yml` e sÃ³ Ã© ativado com a variÃ¡vel do repositÃ³rio `DEPLOY_ENABLED=true` (mais `AWS_ROLE_ARN`, `AWS_REGION` e `EKS_CLUSTER_NAME`).

Secret esperado (`gearflow-os-secret`):

- `ConnectionStrings__OsDb`
- `Jwt__Secret` (HMAC, mÃ­nimo 32 caracteres, o mesmo emissor dos outros serviÃ§os)

## RabbitMQ compartilhado

O OS conecta no broker do `gearflow-infra-broker` (veja `docs/GUIA-INTEGRACAO-RABBITMQ.md` naquele repositÃ³rio):

- Host `rabbitmq.messaging.svc.cluster.local`, porta `5672`, vhost `gearflow`, AMQP sem TLS (no ConfigMap).
- UsuÃ¡rio `gearflow_os`, lido do Secret `rabbitmq-os-credentials` (chaves `username` e `password`) no namespace da aplicaÃ§Ã£o. O Secret original fica em `messaging`; o operador do broker copia somente a credencial do OS para cÃ¡.
- O Pod jÃ¡ leva a label `gearflow.io/rabbitmq-client: "true"`. O namespace precisa da label `gearflow.io/messaging-access=true`:

```bash
kubectl label namespace gearflow gearflow.io/messaging-access=true --overwrite
```

## Observabilidade

Traces e mÃ©tricas saem por OTLP para o New Relic (`https://otlp.nr-data.net:4317`), como no Inventory. A licenÃ§a vem do Secret `newrelic-apm-secret` (chave `NEW_RELIC_LICENSE_KEY`) no namespace; sem ele o Pod sobe normalmente e sÃ³ nÃ£o exporta.

```bash
kubectl apply -k k8s/base
```

## Academy

Perfil com os secrets GitHub existentes: [instruções](academy/README.md).
