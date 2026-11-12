# os no Academy

Perfil EKS/ECR inspirado no Billing. Nenhum recurso remoto foi aplicado na preparação.

## GitHub

Criar environment `academy`. Reutilizar secrets existentes `AWS_ACCESS_KEY_ID`,
`AWS_SECRET_ACCESS_KEY`, `AWS_SESSION_TOKEN`, `JWT_SIGNING_KEY` e `DB_PASSWORD`.
`AWS_REGION` e `EKS_CLUSTER_NAME` aceitam Secret ou Variable (Secret prevalece).
Academy é o delivery padrão; não exige variável de seleção de perfil.
Os Organization secrets devem permitir acesso a este repositório. Secrets de outro environment não são herdados por academy.
Executar `OS Academy EKS` na main; pushes posteriores entregam após CI aprovado.
O workflow delivery.yml usa Academy; o delivery antigo foi substituído. Não são necessários novos nomes de secrets.

## Infraestrutura

Exige EKS acessível pelo Actions, acesso Kubernetes da role temporária, pull ECR pelos nós,
permissão ECR e provisionamento EBS. Detecta Auto Mode; não instala drivers nem cria cluster.
Usa PostgreSQL 16 próprio, PVC 2Gi Retain, banco gearflow_os, uma réplica da API.
Não usa SQL Server nem banco de outro contexto. Namespace gearflow recebe label de acesso ao broker.
Broker deve estar pronto em messaging. Secret esperado `rabbitmq-os-credentials`,
username `gearflow_os`. Confirmar esses nomes no infra-broker; podem ser
sobrescritos pelas Variables `OS_BROKER_SECRET` e `OS_BROKER_USER`.
A cópia lê apenas a credencial do serviço e não registra valores.

PostgreSQL do laboratório usa DB_PASSWORD para o usuário do serviço e para migration.
Esse usuário possui privilégios administrativos na instância isolada; produção exige separação.
Alterar DB_PASSWORD no GitHub NÃO altera senha em volume PostgreSQL já inicializado:
coordenar rotação no banco antes de atualizar os Secrets. Nunca apagar PVC para corrigir senha.
Não migra PVCs entre EKS convencional/Auto Mode; mudança de perfil exige migração planejada.
JWT usa issuer GearFlow.Api e audience GearFlow.Client; confirmar compatibilidade com o emissor.
Não habilita exportação New Relic adicional nem provisiona Inventory.

## Validação e operação

Antes de ativar, renderizar `kubectl kustomize k8s/academy` e `k8s/academy-auto`.
Workflow faz dry-run no servidor, espera banco, aplica schema quando há Job e verifica readiness.
OS preserva EnsureCreatedAsync na inicialização; evolução de banco existente exige roteiro SQL
compatível e revisado, não é realizada automaticamente por esse perfil.
Operation executa migration antes do rollout com a mesma imagem por digest.
No disparo manual, os testes são executados antes de autenticar na AWS.
Homologar mensagens, JWT e persistência após reinício separadamente do smoke de readiness.

`kubectl port-forward -n gearflow service/gearflow-os 18080:8080`

Acessar http://localhost:18080/health/ready. Sem Ingress ou Load Balancer novo.
Rollback: selecionar digest validado, usar kubectl set image no Deployment e conferir rollout/readiness.
Reversão de imagem não reverte schema ou mensagens. Volumes não possuem backup automático.
