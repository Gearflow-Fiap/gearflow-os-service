# Kubernetes — gearflow-os-service

Manifests base em `k8s/base`. O deploy automático ainda não está ligado no CI; este é o artefato exigido pelo PDF da Fase 4.

Secret esperado (`gearflow-os-secret`):

- `ConnectionStrings__OsDb`
- `RabbitMq__Username`
- `RabbitMq__Password`
- `Jwt__Secret` (HMAC, mínimo 32 caracteres, o mesmo emissor dos outros serviços)

```bash
kubectl apply -k k8s/base
```
