-- Aplicar no banco exclusivo da OS antes de implantar a nova versão.
-- EnsureCreated não atualiza tabelas existentes. Linhas legadas continuam sem
-- associação: não inventar CatalogJobId nem reenviar diagnóstico sem saneamento.
BEGIN;
ALTER TABLE IF EXISTS service_order_services ADD COLUMN IF NOT EXISTS "CatalogJobId" uuid NULL;
COMMIT;
