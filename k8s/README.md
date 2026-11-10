# Kubernetes — gearflow-os-service

Manifests base em `k8s/base`. O deploy automático ainda não está ligado no CI; este é o artefato exigido pelo PDF da Fase 4.

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

```bash
kubectl apply -k k8s/base
```
